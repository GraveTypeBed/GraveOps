using System.Globalization;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using GraveOps.Core.Security;

namespace GraveOps.Core.Telemetry;

public static class QBittorrentTelemetryEndpoint
{
    public static Uri Normalize(string endpoint)
    {
        if (string.IsNullOrWhiteSpace(endpoint))
            throw new InvalidOperationException("The qBittorrent endpoint is required.");

        if (!Uri.TryCreate(endpoint.Trim(), UriKind.Absolute, out var parsed))
            throw new InvalidOperationException(
                "The qBittorrent endpoint must be an absolute HTTP or HTTPS URL.");

        return Normalize(parsed);
    }

    public static Uri Normalize(Uri endpoint)
    {
        ArgumentNullException.ThrowIfNull(endpoint);

        if (!endpoint.IsAbsoluteUri ||
            (!endpoint.Scheme.Equals(Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase) &&
             !endpoint.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)))
        {
            throw new InvalidOperationException(
                "The qBittorrent endpoint must use HTTP or HTTPS.");
        }

        if (!string.IsNullOrWhiteSpace(endpoint.UserInfo))
        {
            throw new InvalidOperationException(
                "Credentials cannot be embedded in the qBittorrent endpoint.");
        }

        var builder = new UriBuilder(endpoint)
        {
            Query = string.Empty,
            Fragment = string.Empty
        };

        builder.Path =
            string.IsNullOrWhiteSpace(builder.Path) || builder.Path == "/"
                ? "/"
                : builder.Path.TrimEnd('/') + "/";

        return builder.Uri;
    }

    public static bool IsLoopback(Uri endpoint)
    {
        var normalized = Normalize(endpoint);

        if (normalized.DnsSafeHost.Equals(
                "localhost",
                StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return
            IPAddress.TryParse(normalized.DnsSafeHost, out var address) &&
            IPAddress.IsLoopback(address);
    }
}

public sealed record QBittorrentTelemetryRequest(
    Uri BaseUri,
    string Username,
    SecretValue Password,
    bool RequireCompleteTelemetry = false);

public sealed class QBittorrentTelemetryClient
{
    private readonly HttpClient _client;

    public QBittorrentTelemetryClient(HttpClient client) =>
        _client = client ?? throw new ArgumentNullException(nameof(client));

    public async Task<DownloadClientTelemetrySnapshot> CaptureAsync(
        QBittorrentTelemetryRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.Password);

        var endpoint = QBittorrentTelemetryEndpoint.Normalize(request.BaseUri);
        var username = NormalizeUsername(request.Username);
        var password = new string(request.Password.Reveal().Span);
        string? sessionCookie = null;

        try
        {
            sessionCookie = await LoginAsync(
                endpoint,
                username,
                password,
                cancellationToken);

            var version =
                (await GetTextAsync(
                    endpoint,
                    "api/v2/app/version",
                    sessionCookie,
                    cancellationToken))
                .Trim()
                .TrimStart('v', 'V');

            var transfer = await GetJsonAsync(
                endpoint,
                "api/v2/transfer/info",
                sessionCookie,
                cancellationToken);

            var torrents = await GetJsonAsync(
                endpoint,
                "api/v2/torrents/info?filter=all",
                sessionCookie,
                cancellationToken);

            JsonElement categories = default;
            string? categoryFailure = null;

            try
            {
                categories = await GetJsonAsync(
                    endpoint,
                    "api/v2/torrents/categories",
                    sessionCookie,
                    cancellationToken);
            }
            catch (InvalidOperationException exception)
            {
                if (request.RequireCompleteTelemetry)
                {
                    throw new InvalidOperationException(
                        "Protected qBittorrent telemetry verification failed: " +
                        exception.Message);
                }

                categoryFailure = exception.Message;
            }

            return BuildSnapshot(
                endpoint,
                version,
                transfer,
                torrents,
                categories,
                categoryFailure);
        }
        finally
        {
            if (!string.IsNullOrWhiteSpace(sessionCookie))
            {
                await TryLogoutAsync(
                    endpoint,
                    sessionCookie,
                    cancellationToken);
            }
        }
    }

    private async Task<string> LoginAsync(
        Uri endpoint,
        string username,
        string password,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            new Uri(endpoint, "api/v2/auth/login"));

        ApplyContextHeaders(request, endpoint);

        request.Content = new FormUrlEncodedContent(
            new Dictionary<string, string>
            {
                ["username"] = username,
                ["password"] = password
            });

        using var response = await SendAsync(request, cancellationToken);
        RejectRedirect(response, "qBittorrent login");

        var body = await response.Content.ReadAsStringAsync(cancellationToken);

        if (response.StatusCode == HttpStatusCode.Forbidden ||
            !response.IsSuccessStatusCode ||
            !body.Trim().Equals("Ok.", StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                "qBittorrent rejected the configured WebUI credentials.");
        }

        var cookie = ExtractSidCookie(response);

        if (string.IsNullOrWhiteSpace(cookie))
        {
            throw new InvalidOperationException(
                "qBittorrent authentication succeeded without returning a usable SID cookie.");
        }

        return cookie;
    }

    private async Task<string> GetTextAsync(
        Uri endpoint,
        string relativePath,
        string sessionCookie,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            new Uri(endpoint, relativePath));

        ApplyContextHeaders(request, endpoint);
        request.Headers.TryAddWithoutValidation("Cookie", sessionCookie);

        using var response = await SendAsync(request, cancellationToken);
        EnsureProtectedSuccess(response, relativePath);

        return await response.Content.ReadAsStringAsync(cancellationToken);
    }

    private async Task<JsonElement> GetJsonAsync(
        Uri endpoint,
        string relativePath,
        string sessionCookie,
        CancellationToken cancellationToken)
    {
        var content = await GetTextAsync(
            endpoint,
            relativePath,
            sessionCookie,
            cancellationToken);

        try
        {
            using var document = JsonDocument.Parse(content);
            return document.RootElement.Clone();
        }
        catch (JsonException)
        {
            throw new InvalidOperationException(
                $"GET {SafePath(relativePath)} returned invalid JSON.");
        }
    }

    private async Task TryLogoutAsync(
        Uri endpoint,
        string sessionCookie,
        CancellationToken cancellationToken)
    {
        try
        {
            using var request = new HttpRequestMessage(
                HttpMethod.Post,
                new Uri(endpoint, "api/v2/auth/logout"));

            ApplyContextHeaders(request, endpoint);
            request.Headers.TryAddWithoutValidation("Cookie", sessionCookie);
            request.Content = new FormUrlEncodedContent(
                Array.Empty<KeyValuePair<string, string>>());

            using var response = await SendAsync(request, cancellationToken);
            _ = response.StatusCode;
        }
        catch
        {
            // The telemetry sample is already complete.
        }
    }

    private async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        try
        {
            return await _client.SendAsync(
                request,
                HttpCompletionOption.ResponseContentRead,
                cancellationToken);
        }
        catch (OperationCanceledException)
            when (!cancellationToken.IsCancellationRequested)
        {
            throw new InvalidOperationException(
                "The qBittorrent Web API request timed out.");
        }
        catch (HttpRequestException)
        {
            throw new InvalidOperationException(
                "Could not reach the configured qBittorrent Web API endpoint.");
        }
    }

    private static void ApplyContextHeaders(
        HttpRequestMessage request,
        Uri endpoint)
    {
        request.Headers.Referrer = endpoint;
        request.Headers.TryAddWithoutValidation(
            "Origin",
            endpoint.GetLeftPart(UriPartial.Authority));
        request.Headers.TryAddWithoutValidation(
            "Accept",
            "application/json, text/plain;q=0.9");
    }

    private static void EnsureProtectedSuccess(
        HttpResponseMessage response,
        string relativePath)
    {
        RejectRedirect(response, $"GET {SafePath(relativePath)}");

        if (response.StatusCode is
            HttpStatusCode.Unauthorized or
            HttpStatusCode.Forbidden)
        {
            throw new InvalidOperationException(
                "qBittorrent rejected the authenticated WebUI session.");
        }

        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException(
                $"GET {SafePath(relativePath)} returned " +
                $"{(int)response.StatusCode} {response.ReasonPhrase}.");
        }
    }

    private static void RejectRedirect(
        HttpResponseMessage response,
        string operation)
    {
        if ((int)response.StatusCode is >= 300 and <= 399)
        {
            throw new InvalidOperationException(
                $"{operation} returned a redirect. " +
                "GraveOps did not forward qBittorrent credentials or the SID cookie.");
        }
    }

    private static string? ExtractSidCookie(HttpResponseMessage response)
    {
        if (!response.Headers.TryGetValues("Set-Cookie", out var values))
            return null;

        foreach (var value in values)
        {
            var first = value.Split(
                ';',
                2,
                StringSplitOptions.TrimEntries)[0];

            if (first.StartsWith(
                    "SID=",
                    StringComparison.OrdinalIgnoreCase) &&
                first.Length > 4)
            {
                return first;
            }
        }

        return null;
    }

    private static DownloadClientTelemetrySnapshot BuildSnapshot(
        Uri endpoint,
        string version,
        JsonElement transfer,
        JsonElement torrents,
        JsonElement categories,
        string? categoryFailure)
    {
        if (transfer.ValueKind != JsonValueKind.Object)
        {
            throw new InvalidOperationException(
                "qBittorrent transfer telemetry returned an unexpected payload.");
        }

        if (torrents.ValueKind != JsonValueKind.Array)
        {
            throw new InvalidOperationException(
                "qBittorrent torrent telemetry returned an unexpected payload.");
        }

        var samples = torrents
            .EnumerateArray()
            .Where(item => item.ValueKind == JsonValueKind.Object)
            .Select(ParseTorrent)
            .ToArray();

        var connection = GetString(transfer, "connection_status") ?? "unknown";

        var state =
            connection.Equals("connected", StringComparison.OrdinalIgnoreCase)
                ? string.IsNullOrWhiteSpace(categoryFailure)
                    ? "Online"
                    : "Attention"
                : "Degraded";

        var remainingBytes = samples.Sum(item => item.AmountLeft);

        var eta = samples
            .Where(item =>
                item.AmountLeft > 0 &&
                item.EtaSeconds is > 0 and < 8_640_000)
            .Select(item => item.EtaSeconds)
            .DefaultIfEmpty(0)
            .Min();

        var queue = samples
            .OrderBy(QueueRank)
            .ThenByDescending(item => item.DownloadSpeedBytes)
            .ThenByDescending(item => item.ProgressFraction)
            .Take(100)
            .Select(item => item.Queue)
            .ToList();

        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

        var history = samples
            .Where(item => item.CompletedOn > 0)
            .OrderByDescending(item => item.CompletedOn)
            .Take(20)
            .Select(item => new DownloadHistoryTelemetry
            {
                Name = item.Queue.Name,
                Category = item.Queue.Category,
                State = item.Queue.State,
                Size = item.Queue.Size,
                Completed = FormatEpoch(item.CompletedOn),
                Duration = FormatEta(item.TimeActiveSeconds),
                Detail =
                    $"Ratio {item.Queue.Ratio} · " +
                    $"Uploaded {FormatBytes(item.UploadedBytes)}"
            })
            .ToList();

        var configuredCategories =
            categories.ValueKind == JsonValueKind.Object
                ? categories.EnumerateObject().Select(property => property.Name)
                : Enumerable.Empty<string>();

        var categoryCounts = samples
            .GroupBy(
                item => item.Queue.Category,
                StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                group => group.Key,
                group => group.Count(),
                StringComparer.OrdinalIgnoreCase);

        var categoryRows = configuredCategories
            .Concat(categoryCounts.Keys)
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
            .Select(name => new DownloadCategoryTelemetry
            {
                Name = name,
                TorrentCount =
                    categoryCounts.TryGetValue(name, out var count)
                        ? count
                        : 0
            })
            .ToList();

        var trackerCount = samples.Sum(item => item.TrackerAssociations);

        var detail =
            string.IsNullOrWhiteSpace(categoryFailure)
                ? "Read-only qBittorrent Web API telemetry."
                : "Core qBittorrent telemetry is online; category inventory is unavailable. " +
                  categoryFailure;

        var security =
            endpoint.Scheme.Equals(
                Uri.UriSchemeHttps,
                StringComparison.OrdinalIgnoreCase) ||
            QBittorrentTelemetryEndpoint.IsLoopback(endpoint)
                ? "Password stored in Windows Credential Manager; SID cookie held in memory only."
                : "Password stored in Windows Credential Manager; WebUI login is sent over HTTP to the configured LAN endpoint.";

        return new DownloadClientTelemetrySnapshot
        {
            ClientKey = "qBittorrent",
            DisplayName = "qBittorrent",
            State = state,
            Version = string.IsNullOrWhiteSpace(version) ? "--" : version,
            Security = security,
            Connection = connection,
            Detail = detail,
            DownloadSpeed = FormatRate(GetInt64(transfer, "dl_info_speed")),
            UploadSpeed = FormatRate(GetInt64(transfer, "up_info_speed")),
            Remaining = FormatBytes(remainingBytes),
            Eta = FormatEta(eta),
            SessionDownloaded = FormatBytes(GetInt64(transfer, "dl_info_data")),
            SessionUploaded = FormatBytes(GetInt64(transfer, "up_info_data")),
            RateLimit = FormatRateLimits(
                GetInt64(transfer, "dl_rate_limit"),
                GetInt64(transfer, "up_rate_limit")),
            TotalCount = samples.Length,
            ActiveCount = samples.Count(item => item.Active),
            DownloadingCount = samples.Count(item => item.Downloading),
            SeedingCount = samples.Count(item => item.Seeding),
            PausedCount = samples.Count(item => item.Paused),
            StalledCount = samples.Count(item => item.Stalled),
            CompletedRecentCount = samples.Count(item =>
                item.CompletedOn > 0 &&
                now >= item.CompletedOn &&
                now - item.CompletedOn <= 86_400),
            FailedRecentCount = samples.Count(item => item.Failed),
            DhtNodes = ToInt32(GetInt64(transfer, "dht_nodes")),
            CategoryCount = categoryRows.Count,
            TrackerCount = trackerCount,
            SampledAt = DateTimeOffset.UtcNow,
            Queue = queue,
            History = history,
            Categories = categoryRows
        };
    }

    private static TorrentSample ParseTorrent(JsonElement item)
    {
        var state = GetString(item, "state") ?? "unknown";
        var normalizedState = state.ToLowerInvariant();
        var downloadSpeed = GetInt64(item, "dlspeed");
        var uploadSpeed = GetInt64(item, "upspeed");
        var amountLeft = Math.Max(0, GetInt64(item, "amount_left"));
        var eta = GetInt64(item, "eta");
        var progress = Math.Clamp(GetDouble(item, "progress"), 0d, 1d);
        var tracker = SafeTrackerHost(GetString(item, "tracker"));

        var trackerAssociations = Math.Max(
            0,
            ToInt32(GetInt64(item, "trackers_count")));

        if (trackerAssociations == 0 &&
            !tracker.Equals("--", StringComparison.Ordinal))
        {
            trackerAssociations = 1;
        }

        var downloading =
            normalizedState.Contains("dl", StringComparison.Ordinal) ||
            normalizedState.Contains("downloading", StringComparison.Ordinal) ||
            normalizedState.Contains("metadl", StringComparison.Ordinal);

        var seeding =
            normalizedState.Contains("up", StringComparison.Ordinal) ||
            normalizedState.Contains("seeding", StringComparison.Ordinal) ||
            normalizedState.Contains("uploading", StringComparison.Ordinal);

        var paused =
            normalizedState.Contains("stopped", StringComparison.Ordinal) ||
            normalizedState.Contains("paused", StringComparison.Ordinal);

        var stalled =
            normalizedState.Contains("stalled", StringComparison.Ordinal);

        var failed =
            normalizedState.Contains("error", StringComparison.Ordinal) ||
            normalizedState.Contains("missingfiles", StringComparison.Ordinal);

        var active =
            !paused &&
            (downloadSpeed > 0 ||
             uploadSpeed > 0 ||
             normalizedState is
                 "downloading" or
                 "forceddl" or
                 "metadl" or
                 "uploading" or
                 "forcedup" or
                 "checkingdl" or
                 "checkingup" or
                 "checkingresumedata" or
                 "moving" or
                 "allocating");

        var category = GetString(item, "category");

        if (string.IsNullOrWhiteSpace(category))
            category = "Uncategorized";

        var seeds = ToInt32(GetInt64(item, "num_seeds"));
        var peers = ToInt32(GetInt64(item, "num_leechs"));

        var queue = new DownloadQueueTelemetry
        {
            Name = GetString(item, "name") ?? "Torrent",
            Category = category,
            State = state,
            Progress = $"{progress * 100d:0.0}%",
            ProgressPercent = progress * 100d,
            Size = FormatBytes(PositiveFirst(
                GetInt64(item, "total_size"),
                GetInt64(item, "size"))),
            Downloaded = FormatBytes(GetInt64(item, "downloaded")),
            Remaining = FormatBytes(amountLeft),
            DownloadSpeed = FormatRate(downloadSpeed),
            UploadSpeed = FormatRate(uploadSpeed),
            Eta = FormatEta(eta),
            Ratio = GetDouble(item, "ratio").ToString(
                "0.00",
                CultureInfo.InvariantCulture),
            Peers = $"{seeds}/{peers}",
            Tracker = tracker,
            Added = FormatEpoch(GetInt64(item, "added_on")),
            Detail =
                $"Seeds {seeds} · Peers {peers} · Trackers {trackerAssociations}"
        };

        return new TorrentSample(
            queue,
            progress,
            downloadSpeed,
            amountLeft,
            eta,
            GetInt64(item, "completion_on"),
            GetInt64(item, "time_active"),
            GetInt64(item, "uploaded"),
            trackerAssociations,
            active,
            downloading,
            seeding,
            paused,
            stalled,
            failed);
    }

    private static int QueueRank(TorrentSample sample)
    {
        if (sample.DownloadSpeedBytes > 0)
            return 0;
        if (sample.Downloading)
            return 1;
        if (sample.Stalled)
            return 2;
        if (sample.Seeding)
            return 3;
        if (sample.Paused)
            return 5;
        return 4;
    }

    private static string NormalizeUsername(string username)
    {
        var normalized = username?.Trim() ?? string.Empty;

        if (normalized.Length is < 1 or > 256 ||
            normalized.Contains('\r') ||
            normalized.Contains('\n'))
        {
            throw new InvalidOperationException(
                "The qBittorrent WebUI username must contain 1 to 256 characters on one line.");
        }

        return normalized;
    }

    private static string SafeTrackerHost(string? tracker)
    {
        if (string.IsNullOrWhiteSpace(tracker))
            return "--";

        if (Uri.TryCreate(tracker, UriKind.Absolute, out var parsed) &&
            !string.IsNullOrWhiteSpace(parsed.Host))
        {
            return parsed.Host;
        }

        return "Configured";
    }

    private static string FormatRateLimits(long download, long upload)
    {
        if (download <= 0 && upload <= 0)
            return "Unlimited";

        return $"DL {FormatRate(download)} · UL {FormatRate(upload)}";
    }

    private static string FormatRate(long value) =>
        FormatBytes(value) + "/s";

    private static string FormatBytes(long value)
    {
        var normalized = Math.Max(0d, value);
        var units = new[] { "B", "KB", "MB", "GB", "TB", "PB" };
        var index = 0;

        while (normalized >= 1024d && index < units.Length - 1)
        {
            normalized /= 1024d;
            index++;
        }

        return index == 0
            ? $"{normalized:0} {units[index]}"
            : $"{normalized:0.0} {units[index]}";
    }

    private static string FormatEta(long seconds)
    {
        if (seconds <= 0 || seconds >= 8_640_000)
            return "--";

        var duration = TimeSpan.FromSeconds(seconds);

        if (duration.TotalDays >= 1d)
            return $"{(int)duration.TotalDays}d {duration.Hours}h";

        if (duration.TotalHours >= 1d)
            return $"{(int)duration.TotalHours}h {duration.Minutes}m";

        return $"{Math.Max(1, duration.Minutes)}m";
    }

    private static string FormatEpoch(long value)
    {
        if (value <= 0)
            return "--";

        try
        {
            return DateTimeOffset
                .FromUnixTimeSeconds(value)
                .ToLocalTime()
                .ToString(
                    "yyyy-MM-dd HH:mm",
                    CultureInfo.InvariantCulture);
        }
        catch (ArgumentOutOfRangeException)
        {
            return "--";
        }
    }

    private static long PositiveFirst(long first, long second) =>
        first > 0 ? first : second;

    private static string SafePath(string relativePath)
    {
        var separator = relativePath.IndexOf('?');
        return separator >= 0
            ? relativePath[..separator]
            : relativePath;
    }

    private static string? GetString(
        JsonElement element,
        string property)
    {
        if (element.ValueKind != JsonValueKind.Object ||
            !element.TryGetProperty(property, out var value))
        {
            return null;
        }

        return value.ValueKind switch
        {
            JsonValueKind.String => value.GetString(),
            JsonValueKind.Number => value.ToString(),
            JsonValueKind.True => "true",
            JsonValueKind.False => "false",
            _ => null
        };
    }

    private static long GetInt64(
        JsonElement element,
        string property)
    {
        if (element.ValueKind != JsonValueKind.Object ||
            !element.TryGetProperty(property, out var value))
        {
            return 0;
        }

        if (value.TryGetInt64(out var integer))
            return integer;

        if (value.TryGetDouble(out var number))
        {
            if (number >= long.MaxValue)
                return long.MaxValue;
            if (number <= long.MinValue)
                return long.MinValue;
            return (long)number;
        }

        return 0;
    }

    private static double GetDouble(
        JsonElement element,
        string property)
    {
        if (element.ValueKind != JsonValueKind.Object ||
            !element.TryGetProperty(property, out var value))
        {
            return 0d;
        }

        return value.TryGetDouble(out var number)
            ? number
            : 0d;
    }

    private static int ToInt32(long value)
    {
        if (value >= int.MaxValue)
            return int.MaxValue;
        if (value <= int.MinValue)
            return int.MinValue;
        return (int)value;
    }

    private sealed record TorrentSample(
        DownloadQueueTelemetry Queue,
        double ProgressFraction,
        long DownloadSpeedBytes,
        long AmountLeft,
        long EtaSeconds,
        long CompletedOn,
        long TimeActiveSeconds,
        long UploadedBytes,
        int TrackerAssociations,
        bool Active,
        bool Downloading,
        bool Seeding,
        bool Paused,
        bool Stalled,
        bool Failed);
}
