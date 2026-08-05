using System.Globalization;
using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using GraveOps.Core.Security;

namespace GraveOps.Core.Telemetry;

public static class SABnzbdTelemetryEndpoint
{
    public static Uri Normalize(string endpoint)
    {
        if (string.IsNullOrWhiteSpace(endpoint))
        {
            throw new InvalidOperationException(
                "The SABnzbd endpoint is required.");
        }

        if (!Uri.TryCreate(
                endpoint.Trim(),
                UriKind.Absolute,
                out var parsed))
        {
            throw new InvalidOperationException(
                "The SABnzbd endpoint must be an absolute HTTP or HTTPS URL.");
        }

        return Normalize(parsed);
    }

    public static Uri Normalize(Uri endpoint)
    {
        ArgumentNullException.ThrowIfNull(endpoint);

        if (!endpoint.IsAbsoluteUri ||
            (!endpoint.Scheme.Equals(
                 Uri.UriSchemeHttp,
                 StringComparison.OrdinalIgnoreCase) &&
             !endpoint.Scheme.Equals(
                 Uri.UriSchemeHttps,
                 StringComparison.OrdinalIgnoreCase)))
        {
            throw new InvalidOperationException(
                "The SABnzbd endpoint must use HTTP or HTTPS.");
        }

        if (!string.IsNullOrWhiteSpace(endpoint.UserInfo))
        {
            throw new InvalidOperationException(
                "Credentials cannot be embedded in the SABnzbd endpoint.");
        }

        var builder =
            new UriBuilder(endpoint)
            {
                Query = string.Empty,
                Fragment = string.Empty
            };

        var path =
            (builder.Path ?? string.Empty)
                .TrimEnd('/');

        if (path.EndsWith(
                "/api",
                StringComparison.OrdinalIgnoreCase))
        {
            path = path[..^4];
        }

        builder.Path =
            string.IsNullOrWhiteSpace(path)
                ? "/"
                : path.TrimEnd('/') + "/";

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
            IPAddress.TryParse(
                normalized.DnsSafeHost,
                out var address) &&
            IPAddress.IsLoopback(address);
    }

    public static Uri ApiUri(
        Uri endpoint,
        string mode,
        string? apiKey = null,
        int? start = null,
        int? limit = null)
    {
        var normalized = Normalize(endpoint);
        var api = new Uri(normalized, "api");

        var query =
            new List<string>
            {
                "mode=" +
                Uri.EscapeDataString(mode),

                "output=json"
            };

        if (start.HasValue)
        {
            query.Add(
                "start=" +
                start.Value.ToString(
                    CultureInfo.InvariantCulture));
        }

        if (limit.HasValue)
        {
            query.Add(
                "limit=" +
                limit.Value.ToString(
                    CultureInfo.InvariantCulture));
        }

        if (!string.IsNullOrWhiteSpace(apiKey))
        {
            query.Add(
                "apikey=" +
                Uri.EscapeDataString(apiKey));
        }

        var builder =
            new UriBuilder(api)
            {
                Query = string.Join("&", query)
            };

        return builder.Uri;
    }
}

public sealed record SABnzbdTelemetryRequest(
    Uri BaseUri,
    SecretValue ApiKey,
    bool RequireCompleteTelemetry = false);

public sealed class SABnzbdTelemetryClient
{
    private static readonly HashSet<string>
        ActiveStates =
            new(
                new[]
                {
                    "Downloading",
                    "Fetching",
                    "Propagating",
                    "Verifying",
                    "Repairing",
                    "Extracting",
                    "Moving",
                    "Running"
                },
                StringComparer.OrdinalIgnoreCase);

    private static readonly HashSet<string>
        DownloadingStates =
            new(
                new[]
                {
                    "Downloading",
                    "Fetching",
                    "Propagating"
                },
                StringComparer.OrdinalIgnoreCase);

    private static readonly Regex HtmlPattern =
        new(
            "<[^>]+>",
            RegexOptions.Compiled);

    private static readonly Regex UrlPattern =
        new(
            @"https?://\S+",
            RegexOptions.Compiled |
            RegexOptions.IgnoreCase);

    private static readonly Regex WindowsPathPattern =
        new(
            @"\b[A-Za-z]:\\.*$",
            RegexOptions.Compiled |
            RegexOptions.Singleline);

    private static readonly Regex UnixPathPattern =
        new(
            @"/(?:mnt|media|home|downloads|data|config|var/lib)/.*$",
            RegexOptions.Compiled |
            RegexOptions.IgnoreCase |
            RegexOptions.Singleline);

    private static readonly Regex WhitespacePattern =
        new(
            @"\s+",
            RegexOptions.Compiled);

    private readonly HttpClient _client;

    public SABnzbdTelemetryClient(HttpClient client)
    {
        _client =
            client ??
            throw new ArgumentNullException(
                nameof(client));
    }

    public async Task<DownloadClientTelemetrySnapshot>
        CaptureAsync(
            SABnzbdTelemetryRequest request,
            CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.ApiKey);

        var endpoint =
            SABnzbdTelemetryEndpoint.Normalize(
                request.BaseUri);

        var apiKey =
            new string(
                request.ApiKey.Reveal().Span);

        JsonElement version =
            default;

        try
        {
            version =
                await GetJsonAsync(
                    SABnzbdTelemetryEndpoint.ApiUri(
                        endpoint,
                        "version"),
                    protectedRequest: false,
                    cancellationToken);
        }
        catch (InvalidOperationException)
        {
            // Queue and history are the protected source of truth.
        }

        var queue =
            await GetJsonAsync(
                SABnzbdTelemetryEndpoint.ApiUri(
                    endpoint,
                    "queue",
                    apiKey,
                    start: 0,
                    limit: 100),
                protectedRequest: true,
                cancellationToken);

        JsonElement history =
            default;

        string? historyFailure =
            null;

        try
        {
            history =
                await GetJsonAsync(
                    SABnzbdTelemetryEndpoint.ApiUri(
                        endpoint,
                        "history",
                        apiKey,
                        start: 0,
                        limit: 40),
                    protectedRequest: true,
                    cancellationToken);
        }
        catch (InvalidOperationException exception)
        {
            if (request.RequireCompleteTelemetry)
            {
                throw new InvalidOperationException(
                    "Protected SABnzbd telemetry verification failed: " +
                    exception.Message);
            }

            historyFailure =
                exception.Message;
        }

        return BuildSnapshot(
            endpoint,
            version,
            queue,
            history,
            historyFailure);
    }

    private async Task<JsonElement> GetJsonAsync(
        Uri requestUri,
        bool protectedRequest,
        CancellationToken cancellationToken)
    {
        using var request =
            new HttpRequestMessage(
                HttpMethod.Get,
                requestUri);

        request.Headers.TryAddWithoutValidation(
            "Accept",
            "application/json");

        using var response =
            await SendAsync(
                request,
                cancellationToken);

        RejectRedirect(
            response,
            protectedRequest
                ? "SABnzbd protected API request"
                : "SABnzbd API request");

        if (protectedRequest &&
            response.StatusCode is
                HttpStatusCode.Unauthorized or
                HttpStatusCode.Forbidden)
        {
            throw new InvalidOperationException(
                "SABnzbd rejected the configured API key.");
        }

        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException(
                protectedRequest
                    ? "SABnzbd protected API telemetry returned an unsuccessful response."
                    : "SABnzbd API telemetry returned an unsuccessful response.");
        }

        var content =
            await response.Content.ReadAsStringAsync(
                cancellationToken);

        JsonElement root;

        try
        {
            using var document =
                JsonDocument.Parse(content);

            root =
                document.RootElement.Clone();
        }
        catch (JsonException)
        {
            throw new InvalidOperationException(
                protectedRequest
                    ? "SABnzbd protected API telemetry returned invalid JSON."
                    : "SABnzbd API telemetry returned invalid JSON.");
        }

        ThrowIfApplicationError(
            root,
            protectedRequest);

        return root;
    }

    private async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        try
        {
            return
                await _client.SendAsync(
                    request,
                    HttpCompletionOption.ResponseContentRead,
                    cancellationToken);
        }
        catch (OperationCanceledException)
            when (!cancellationToken.IsCancellationRequested)
        {
            throw new InvalidOperationException(
                "The SABnzbd API request timed out.");
        }
        catch (HttpRequestException)
        {
            throw new InvalidOperationException(
                "Could not reach the configured SABnzbd API endpoint.");
        }
    }

    private static void RejectRedirect(
        HttpResponseMessage response,
        string operation)
    {
        if ((int)response.StatusCode is
            >= 300 and <= 399)
        {
            throw new InvalidOperationException(
                $"{operation} returned a redirect. " +
                "GraveOps did not forward the SABnzbd API key.");
        }
    }

    private static void ThrowIfApplicationError(
        JsonElement root,
        bool protectedRequest)
    {
        if (root.ValueKind !=
            JsonValueKind.Object)
        {
            return;
        }

        var failedStatus =
            root.TryGetProperty(
                "status",
                out var status) &&
            status.ValueKind ==
                JsonValueKind.False;

        var error =
            GetText(
                root,
                "error");

        if (!failedStatus &&
            string.IsNullOrWhiteSpace(error))
        {
            return;
        }

        if (protectedRequest &&
            (error?.Contains(
                 "api",
                 StringComparison.OrdinalIgnoreCase) ==
             true ||
             error?.Contains(
                 "key",
                 StringComparison.OrdinalIgnoreCase) ==
             true ||
             error?.Contains(
                 "auth",
                 StringComparison.OrdinalIgnoreCase) ==
             true))
        {
            throw new InvalidOperationException(
                "SABnzbd rejected the configured API key.");
        }

        throw new InvalidOperationException(
            protectedRequest
                ? "SABnzbd protected API telemetry returned an application error."
                : "SABnzbd API telemetry returned an application error.");
    }

    private static DownloadClientTelemetrySnapshot
        BuildSnapshot(
            Uri endpoint,
            JsonElement versionRoot,
            JsonElement queueRoot,
            JsonElement historyRoot,
            string? historyFailure)
    {
        var queue =
            GetObject(
                queueRoot,
                "queue");

        if (queue.ValueKind !=
            JsonValueKind.Object)
        {
            throw new InvalidOperationException(
                "SABnzbd queue telemetry returned an unexpected payload.");
        }

        var queueSlots =
            GetArray(
                queue,
                "slots");

        var speed =
            FormatQueueSpeed(
                queue);

        var paused =
            GetBoolean(
                queue,
                "paused");

        var queueRows =
            new List<DownloadQueueTelemetry>();

        var activeCount =
            0;

        var downloadingCount =
            0;

        var pausedCount =
            0;

        foreach (var item in queueSlots
                     .EnumerateArray()
                     .Take(100))
        {
            if (item.ValueKind !=
                JsonValueKind.Object)
            {
                continue;
            }

            var state =
                GetText(
                    item,
                    "status") ??
                "Queued";

            if (ActiveStates.Contains(state))
                activeCount++;

            if (DownloadingStates.Contains(state))
                downloadingCount++;

            if (state.Equals(
                    "Paused",
                    StringComparison.OrdinalIgnoreCase))
            {
                pausedCount++;
            }

            var totalMb =
                GetFlexibleDouble(
                    item,
                    "mb");

            var remainingMb =
                GetFlexibleDouble(
                    item,
                    "mbleft");

            var total =
                FirstText(
                    GetText(
                        item,
                        "size"),
                    totalMb.HasValue
                        ? FormatMegabytes(
                            totalMb.Value)
                        : null);

            var remaining =
                FirstText(
                    GetText(
                        item,
                        "sizeleft"),
                    remainingMb.HasValue
                        ? FormatMegabytes(
                            remainingMb.Value)
                        : null);

            var downloaded =
                totalMb.HasValue &&
                remainingMb.HasValue
                    ? FormatMegabytes(
                        Math.Max(
                            0d,
                            totalMb.Value -
                            remainingMb.Value))
                    : "--";

            var details =
                new List<string>();

            var priority =
                GetText(
                    item,
                    "priority");

            if (!string.IsNullOrWhiteSpace(priority))
            {
                details.Add(
                    "Priority " +
                    priority);
            }

            var script =
                GetText(
                    item,
                    "script");

            if (!string.IsNullOrWhiteSpace(script) &&
                !script.Equals(
                    "Default",
                    StringComparison.OrdinalIgnoreCase))
            {
                details.Add(
                    "Script " +
                    SanitizeDetail(script));
            }

            var progress =
                Math.Clamp(
                    GetFlexibleDouble(
                        item,
                        "percentage") ??
                    0d,
                    0d,
                    100d);

            queueRows.Add(
                new DownloadQueueTelemetry
                {
                    Name =
                        FirstText(
                            GetText(
                                item,
                                "filename"),
                            GetText(
                                item,
                                "name"),
                            "Download"),

                    Category =
                        FirstText(
                            GetText(
                                item,
                                "cat"),
                            "Default"),

                    State =
                        state,

                    Progress =
                        $"{progress:0.0}%",

                    ProgressPercent =
                        progress,

                    Size =
                        total,

                    Downloaded =
                        downloaded,

                    Remaining =
                        remaining,

                    DownloadSpeed =
                        DownloadingStates.Contains(
                            state)
                            ? speed
                            : "--",

                    UploadSpeed =
                        "--",

                    Eta =
                        FirstText(
                            GetText(
                                item,
                                "timeleft"),
                            "--"),

                    Ratio =
                        "--",

                    Peers =
                        "--",

                    Tracker =
                        "--",

                    Added =
                        FormatEpoch(
                            GetFlexibleLong(
                                item,
                                "time_added")),

                    Detail =
                        string.Join(
                            " · ",
                            details)
                });
        }

        if (paused &&
            pausedCount ==
            0)
        {
            pausedCount =
                1;
        }

        var historyRows =
            new List<DownloadHistoryTelemetry>();

        var completedCount =
            0;

        var failedCount =
            0;

        var dayDownloaded =
            "--";

        var weekDownloaded =
            "--";

        var monthDownloaded =
            "--";

        var totalDownloaded =
            "--";

        if (historyRoot.ValueKind ==
            JsonValueKind.Object)
        {
            var history =
                GetObject(
                    historyRoot,
                    "history");

            if (history.ValueKind ==
                JsonValueKind.Object)
            {
                dayDownloaded =
                    FirstText(
                        GetText(
                            history,
                            "day_size"),
                        "--");

                weekDownloaded =
                    FirstText(
                        GetText(
                            history,
                            "week_size"),
                        "--");

                monthDownloaded =
                    FirstText(
                        GetText(
                            history,
                            "month_size"),
                        "--");

                totalDownloaded =
                    FirstText(
                        GetText(
                            history,
                            "total_size"),
                        "--");

                var historySlots =
                    GetArray(
                        history,
                        "slots");

                foreach (var item in
                         historySlots.EnumerateArray())
                {
                    if (item.ValueKind !=
                        JsonValueKind.Object)
                    {
                        continue;
                    }

                    var state =
                        GetText(
                            item,
                            "status") ??
                        "--";

                    if (state.Equals(
                            "Completed",
                            StringComparison.OrdinalIgnoreCase))
                    {
                        completedCount++;
                    }

                    if (state.Equals(
                            "Failed",
                            StringComparison.OrdinalIgnoreCase))
                    {
                        failedCount++;
                    }
                }

                foreach (var item in
                         historySlots
                             .EnumerateArray()
                             .Take(20))
                {
                    if (item.ValueKind !=
                        JsonValueKind.Object)
                    {
                        continue;
                    }

                    historyRows.Add(
                        ParseHistoryRow(
                            item));
                }
            }
        }

        var overallState =
            !string.IsNullOrWhiteSpace(
                historyFailure)
                ? "Attention"
                : paused
                    ? "Paused"
                    : "Online";

        var version =
            versionRoot.ValueKind ==
                JsonValueKind.Object
                ? FirstText(
                    GetText(
                        versionRoot,
                        "version"),
                    "--")
                : "--";

        var detail =
            string.IsNullOrWhiteSpace(
                historyFailure)
                ? "Read-only queue and history telemetry from the SABnzbd API."
                : "Queue telemetry is online; recent history is unavailable. " +
                  historyFailure;

        var security =
            endpoint.Scheme.Equals(
                Uri.UriSchemeHttps,
                StringComparison.OrdinalIgnoreCase) ||
            SABnzbdTelemetryEndpoint.IsLoopback(
                endpoint)
                ? "API key stored in Windows Credential Manager."
                : "API key stored in Windows Credential Manager; SABnzbd requests use HTTP to the configured LAN endpoint.";

        return new DownloadClientTelemetrySnapshot
        {
            ClientKey =
                "SABnzbd",

            DisplayName =
                "SABnzbd",

            State =
                overallState,

            Version =
                version,

            Security =
                security,

            Connection =
                endpoint.Authority,

            Detail =
                detail,

            DownloadSpeed =
                speed,

            UploadSpeed =
                "--",

            Remaining =
                FirstText(
                    GetText(
                        queue,
                        "sizeleft"),
                    "--"),

            Eta =
                FirstText(
                    GetText(
                        queue,
                        "timeleft"),
                    "--"),

            SessionDownloaded =
                "--",

            SessionUploaded =
                "--",

            RateLimit =
                FormatRateLimit(
                    queue),

            DiskFree =
                FormatDiskFree(
                    queue),

            DayDownloaded =
                dayDownloaded,

            WeekDownloaded =
                weekDownloaded,

            MonthDownloaded =
                monthDownloaded,

            TotalDownloaded =
                totalDownloaded,

            TotalCount =
                GetFlexibleInt(
                    queue,
                    "noofslots") ??
                queueRows.Count,

            ActiveCount =
                activeCount,

            DownloadingCount =
                downloadingCount,

            SeedingCount =
                0,

            PausedCount =
                pausedCount,

            StalledCount =
                0,

            CompletedRecentCount =
                completedCount,

            FailedRecentCount =
                failedCount,

            DhtNodes =
                0,

            CategoryCount =
                queueRows
                    .Select(item =>
                        item.Category)
                    .Where(category =>
                        !string.IsNullOrWhiteSpace(
                            category))
                    .Distinct(
                        StringComparer.OrdinalIgnoreCase)
                    .Count(),

            TrackerCount =
                0,

            SampledAt =
                DateTimeOffset.UtcNow,

            Queue =
                queueRows,

            History =
                historyRows
        };
    }

    private static DownloadHistoryTelemetry
        ParseHistoryRow(
            JsonElement item)
    {
        var duration =
            FirstText(
                FormatDuration(
                    GetFlexibleLong(
                        item,
                        "download_time")),
                FormatDuration(
                    GetFlexibleLong(
                        item,
                        "postproc_time")),
                "--");

        var completed =
            FirstPositive(
                GetFlexibleLong(
                    item,
                    "completed"),
                GetFlexibleLong(
                    item,
                    "completed_time"),
                GetFlexibleLong(
                    item,
                    "time_completed"));

        return new DownloadHistoryTelemetry
        {
            Name =
                FirstText(
                    GetText(
                        item,
                        "name"),
                    GetText(
                        item,
                        "filename"),
                    "History item"),

            Category =
                FirstText(
                    GetText(
                        item,
                        "category"),
                    GetText(
                        item,
                        "cat"),
                    "Default"),

            State =
                FirstText(
                    GetText(
                        item,
                        "status"),
                    "--"),

            Size =
                FirstText(
                    GetText(
                        item,
                        "size"),
                    "--"),

            Completed =
                FormatEpoch(
                    completed),

            Duration =
                duration,

            Detail =
                BuildHistoryDetail(
                    item)
        };
    }

    private static string BuildHistoryDetail(
        JsonElement item)
    {
        var failure =
            GetText(
                item,
                "fail_message");

        if (!string.IsNullOrWhiteSpace(
                failure))
        {
            return SanitizeDetail(
                failure);
        }

        var stageLog =
            GetArray(
                item,
                "stage_log");

        var details =
            new List<string>();

        foreach (var stage in
                 stageLog
                     .EnumerateArray()
                     .Take(3))
        {
            if (stage.ValueKind !=
                JsonValueKind.Object)
            {
                continue;
            }

            var name =
                FirstText(
                    GetText(
                        stage,
                        "name"),
                    "Stage");

            var actions =
                GetArray(
                    stage,
                    "actions");

            var action =
                actions
                    .EnumerateArray()
                    .LastOrDefault();

            if (action.ValueKind ==
                JsonValueKind.String)
            {
                var sanitized =
                    SanitizeDetail(
                        action.GetString());

                if (!string.IsNullOrWhiteSpace(
                        sanitized))
                {
                    details.Add(
                        name +
                        ": " +
                        sanitized);

                    continue;
                }
            }

            details.Add(name);
        }

        return string.Join(
            " · ",
            details);
    }

    private static string SanitizeDetail(
        string? detail)
    {
        if (string.IsNullOrWhiteSpace(
                detail))
        {
            return string.Empty;
        }

        var sanitized =
            HtmlPattern.Replace(
                detail,
                " ");

        sanitized =
            UrlPattern.Replace(
                sanitized,
                "[url]");

        sanitized =
            WindowsPathPattern.Replace(
                sanitized,
                "[path]");

        sanitized =
            UnixPathPattern.Replace(
                sanitized,
                "[path]");

        sanitized =
            WhitespacePattern.Replace(
                sanitized,
                " ")
            .Trim();

        return sanitized.Length >
            260
            ? sanitized[..260]
            : sanitized;
    }

    private static string FormatQueueSpeed(
        JsonElement queue)
    {
        var kilobytes =
            GetFlexibleDouble(
                queue,
                "kbpersec");

        if (kilobytes.HasValue)
        {
            var value =
                Math.Max(
                    0d,
                    kilobytes.Value);

            return value >=
                1024d
                ? $"{value / 1024d:0.0} MB/s"
                : $"{value:0} KB/s";
        }

        return FirstText(
            GetText(
                queue,
                "speed"),
            "--");
    }

    private static string FormatRateLimit(
        JsonElement queue)
    {
        var absolute =
            GetFlexibleDouble(
                queue,
                "speedlimit_abs");

        if (absolute.HasValue)
        {
            if (absolute.Value <=
                0d)
            {
                return "Unlimited";
            }

            return absolute.Value >=
                1024d
                ? $"{absolute.Value / 1024d:0.0} MB/s"
                : $"{absolute.Value:0} KB/s";
        }

        var percentage =
            GetText(
                queue,
                "speedlimit");

        return string.IsNullOrWhiteSpace(
                percentage)
            ? "--"
            : percentage;
    }

    private static string FormatDiskFree(
        JsonElement queue)
    {
        var value =
            GetFlexibleDouble(
                queue,
                "diskspace1") ??
            GetFlexibleDouble(
                queue,
                "diskspace2");

        return value.HasValue
            ? $"{Math.Max(0d, value.Value):0.0} GB"
            : "--";
    }

    private static string FormatMegabytes(
        double value) =>
        $"{Math.Max(0d, value):0.0} MB";

    private static string FormatEpoch(
        long? value)
    {
        if (!value.HasValue ||
            value.Value <=
            0)
        {
            return "--";
        }

        try
        {
            return
                DateTimeOffset
                    .FromUnixTimeSeconds(
                        value.Value)
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

    private static string? FormatDuration(
        long? seconds)
    {
        if (!seconds.HasValue ||
            seconds.Value <=
            0)
        {
            return null;
        }

        var duration =
            TimeSpan.FromSeconds(
                seconds.Value);

        if (duration.TotalHours >=
            1d)
        {
            return
                $"{(int)duration.TotalHours}h {duration.Minutes}m";
        }

        if (duration.TotalMinutes >=
            1d)
        {
            return
                $"{(int)duration.TotalMinutes}m {duration.Seconds}s";
        }

        return
            $"{Math.Max(1, duration.Seconds)}s";
    }

    private static JsonElement GetObject(
        JsonElement element,
        string property)
    {
        if (element.ValueKind ==
                JsonValueKind.Object &&
            element.TryGetProperty(
                property,
                out var value) &&
            value.ValueKind ==
                JsonValueKind.Object)
        {
            return value;
        }

        return default;
    }

    private static JsonElement GetArray(
        JsonElement element,
        string property)
    {
        if (element.ValueKind ==
                JsonValueKind.Object &&
            element.TryGetProperty(
                property,
                out var value) &&
            value.ValueKind ==
                JsonValueKind.Array)
        {
            return value;
        }

        using var document =
            JsonDocument.Parse("[]");

        return document
            .RootElement
            .Clone();
    }

    private static string? GetText(
        JsonElement element,
        string property)
    {
        if (element.ValueKind !=
                JsonValueKind.Object ||
            !element.TryGetProperty(
                property,
                out var value))
        {
            return null;
        }

        return value.ValueKind switch
        {
            JsonValueKind.String =>
                value.GetString(),

            JsonValueKind.Number =>
                value.ToString(),

            JsonValueKind.True =>
                "true",

            JsonValueKind.False =>
                "false",

            _ =>
                null
        };
    }

    private static bool GetBoolean(
        JsonElement element,
        string property)
    {
        if (element.ValueKind !=
                JsonValueKind.Object ||
            !element.TryGetProperty(
                property,
                out var value))
        {
            return false;
        }

        return value.ValueKind switch
        {
            JsonValueKind.True =>
                true,

            JsonValueKind.False =>
                false,

            JsonValueKind.String
                when bool.TryParse(
                    value.GetString(),
                    out var parsed) =>
                parsed,

            JsonValueKind.Number
                when value.TryGetInt32(
                    out var integer) =>
                integer != 0,

            _ =>
                false
        };
    }

    private static double? GetFlexibleDouble(
        JsonElement element,
        string property)
    {
        if (element.ValueKind !=
                JsonValueKind.Object ||
            !element.TryGetProperty(
                property,
                out var value))
        {
            return null;
        }

        if (value.ValueKind ==
                JsonValueKind.Number &&
            value.TryGetDouble(
                out var number))
        {
            return number;
        }

        if (value.ValueKind ==
                JsonValueKind.String &&
            double.TryParse(
                value.GetString(),
                NumberStyles.Float,
                CultureInfo.InvariantCulture,
                out number))
        {
            return number;
        }

        return null;
    }

    private static long? GetFlexibleLong(
        JsonElement element,
        string property)
    {
        var value =
            GetFlexibleDouble(
                element,
                property);

        if (!value.HasValue)
            return null;

        if (value.Value >=
            long.MaxValue)
        {
            return long.MaxValue;
        }

        if (value.Value <=
            long.MinValue)
        {
            return long.MinValue;
        }

        return (long)value.Value;
    }

    private static int? GetFlexibleInt(
        JsonElement element,
        string property)
    {
        var value =
            GetFlexibleLong(
                element,
                property);

        if (!value.HasValue)
            return null;

        if (value.Value >=
            int.MaxValue)
        {
            return int.MaxValue;
        }

        if (value.Value <=
            int.MinValue)
        {
            return int.MinValue;
        }

        return (int)value.Value;
    }

    private static long? FirstPositive(
        params long?[] values) =>
        values.FirstOrDefault(value =>
            value is >
            0);

    private static string FirstText(
        params string?[] values) =>
        values.FirstOrDefault(value =>
            !string.IsNullOrWhiteSpace(
                value)) ??
        "--";
}
