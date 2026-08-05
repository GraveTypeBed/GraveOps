using System.Globalization;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using GraveOps.Core.Security;

namespace GraveOps.Core.Telemetry;

public sealed record ArrTelemetryAdapterDefinition(
    string Product,
    IReadOnlyList<string> ApiRoots,
    bool SupportsQueue);

public static class ArrTelemetryCatalog
{
    private static readonly IReadOnlyDictionary<string, ArrTelemetryAdapterDefinition> Adapters =
        new Dictionary<string, ArrTelemetryAdapterDefinition>(StringComparer.OrdinalIgnoreCase)
        {
            ["Sonarr"] = new("Sonarr", new[] { "api/v5", "api/v3" }, true),
            ["Radarr"] = new("Radarr", new[] { "api/v3" }, true),
            ["Lidarr"] = new("Lidarr", new[] { "api/v1" }, true),
            ["Prowlarr"] = new("Prowlarr", new[] { "api/v1" }, false),
            ["Readarr"] = new("Readarr", new[] { "api/v1" }, true),
            ["Whisparr"] = new("Whisparr", new[] { "api/v1" }, true)
        };

    public static IReadOnlyList<string> SupportedProducts { get; } =
        Adapters.Keys.OrderBy(value => value, StringComparer.OrdinalIgnoreCase).ToArray();

    public static ArrTelemetryAdapterDefinition Resolve(string product)
    {
        if (string.IsNullOrWhiteSpace(product) ||
            !Adapters.TryGetValue(product.Trim(), out var adapter))
        {
            throw new InvalidOperationException(
                $"'{product}' does not have a registered Arr API adapter.");
        }

        return adapter;
    }

    public static bool IsSupported(string? product) =>
        !string.IsNullOrWhiteSpace(product) && Adapters.ContainsKey(product.Trim());

    public static IReadOnlyList<string> ApiRootsFor(string product) =>
        Resolve(product).ApiRoots;

    public static bool SupportsQueue(string product) =>
        Resolve(product).SupportsQueue;
}

public static class ArrTelemetryEndpoint
{
    public static Uri Normalize(string endpoint)
    {
        if (string.IsNullOrWhiteSpace(endpoint))
            throw new InvalidOperationException("The Arr endpoint is required.");

        if (!Uri.TryCreate(endpoint.Trim(), UriKind.Absolute, out var parsed))
        {
            throw new InvalidOperationException(
                "The Arr endpoint must be an absolute HTTP or HTTPS URL.");
        }

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
                "The Arr endpoint must use HTTP or HTTPS.");
        }

        if (!string.IsNullOrWhiteSpace(endpoint.UserInfo))
        {
            throw new InvalidOperationException(
                "Credentials cannot be embedded in the Arr endpoint.");
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
}

public sealed record ArrTelemetryRequest(
    Uri BaseUri,
    string Product,
    string InstanceKey,
    string ServiceName,
    SecretValue ApiKey,
    bool RequireCompleteTelemetry = false);

public sealed class ArrTelemetryClient
{
    private readonly HttpClient _client;

    public ArrTelemetryClient(HttpClient client) =>
        _client = client ?? throw new ArgumentNullException(nameof(client));

    public async Task<ArrLiveTelemetrySnapshot> CaptureAsync(
        ArrTelemetryRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.ApiKey);

        var adapter = ArrTelemetryCatalog.Resolve(request.Product);
        var endpoint = ArrTelemetryEndpoint.Normalize(request.BaseUri);
        var instanceKey = string.IsNullOrWhiteSpace(request.InstanceKey)
            ? request.Product.Trim()
            : request.InstanceKey.Trim();
        var serviceName = string.IsNullOrWhiteSpace(request.ServiceName)
            ? request.Product.Trim()
            : request.ServiceName.Trim();
        var apiKey = new string(request.ApiKey.Reveal().Span);

        var selection = await SelectApiRootAsync(
            endpoint,
            adapter,
            apiKey,
            cancellationToken);

        if (!selection.Result.Success)
            throw new InvalidOperationException(selection.Result.Error);

        var reportedProduct = GetString(selection.Result.Root, "appName");

        if (!string.IsNullOrWhiteSpace(reportedProduct) &&
            !reportedProduct.Contains(
                adapter.Product,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"The configured endpoint identified itself as '{reportedProduct}', " +
                $"not {adapter.Product}.");
        }

        var version = GetString(selection.Result.Root, "version") ?? "--";
        var workItems = new List<ArrWorkItemRow>();
        var healthCount = 0;
        var partialFailure = false;

        var health = await GetJsonAsync(
            new Uri(endpoint, $"{selection.ApiRoot}/health"),
            apiKey,
            cancellationToken);

        if (health.Success && health.Root.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in health.Root.EnumerateArray())
            {
                healthCount++;

                var source =
                    GetString(item, "source") ??
                    GetString(item, "type") ??
                    "Application";
                var message =
                    GetString(item, "message") ??
                    "Health issue reported.";
                var wiki =
                    GetString(item, "wikiUrl") ??
                    string.Empty;

                workItems.Add(
                    new ArrWorkItemRow(
                        serviceName,
                        "Health",
                        source,
                        "Attention",
                        string.Empty,
                        string.Empty,
                        string.IsNullOrWhiteSpace(wiki)
                            ? message
                            : $"{message} · {wiki}"));
            }
        }
        else if (!health.Success)
        {
            if (request.RequireCompleteTelemetry)
            {
                throw new InvalidOperationException(
                    $"Protected {adapter.Product} telemetry verification failed: " +
                    health.Error);
            }

            partialFailure = true;
            workItems.Add(
                new ArrWorkItemRow(
                    serviceName,
                    "Access",
                    "Health endpoint unavailable",
                    health.Status,
                    string.Empty,
                    string.Empty,
                    health.Error));
        }

        int? workCount = null;
        string workLabel;

        if (adapter.SupportsQueue)
        {
            var queue = await GetJsonAsync(
                new Uri(
                    endpoint,
                    $"{selection.ApiRoot}/queue?" +
                    "page=1&pageSize=100&sortDirection=descending&sortKey=timeleft&" +
                    "includeUnknownSeriesItems=true&includeUnknownMovieItems=true"),
                apiKey,
                cancellationToken);

            if (queue.Success)
            {
                var records = QueueRecords(queue.Root);
                workCount = GetInt(queue.Root, "totalRecords") ?? records.Count;
                workLabel = $"Queue {workCount}";

                foreach (var item in records)
                    workItems.Add(ParseQueueRow(serviceName, adapter.Product, item));
            }
            else
            {
                if (request.RequireCompleteTelemetry)
                {
                    throw new InvalidOperationException(
                        $"Protected {adapter.Product} telemetry verification failed: " +
                        queue.Error);
                }

                partialFailure = true;
                workLabel = "Queue unavailable";
                workItems.Add(
                    new ArrWorkItemRow(
                        serviceName,
                        "Access",
                        "Queue endpoint unavailable",
                        queue.Status,
                        string.Empty,
                        string.Empty,
                        queue.Error));
            }
        }
        else
        {
            var indexers = await GetJsonAsync(
                new Uri(endpoint, $"{selection.ApiRoot}/indexer"),
                apiKey,
                cancellationToken);

            if (indexers.Success && indexers.Root.ValueKind == JsonValueKind.Array)
            {
                workCount = indexers.Root.GetArrayLength();
                workLabel = $"Indexers {workCount}";

                foreach (var item in indexers.Root.EnumerateArray())
                {
                    workItems.Add(
                        ParseIndexerRow(
                            serviceName,
                            item));
                }
            }
            else
            {
                if (request.RequireCompleteTelemetry)
                {
                    throw new InvalidOperationException(
                        $"Protected {adapter.Product} telemetry verification failed: " +
                        indexers.Error);
                }

                partialFailure = true;
                workLabel = "Indexers unavailable";
                workItems.Add(
                    new ArrWorkItemRow(
                        serviceName,
                        "Access",
                        "Indexer endpoint unavailable",
                        indexers.Status,
                        string.Empty,
                        string.Empty,
                        indexers.Error));
            }
        }

        if (workItems.Count == 0)
        {
            workItems.Add(
                new ArrWorkItemRow(
                    serviceName,
                    adapter.SupportsQueue ? "Queue" : "Health",
                    adapter.SupportsQueue
                        ? "No queued work or active health issue"
                        : "No active health issue",
                    "Healthy",
                    string.Empty,
                    string.Empty,
                    "The latest API probe returned no actionable rows."));
        }

        var state = healthCount > 0 || partialFailure
            ? "Attention"
            : "Online";

        var service = new ArrServiceTelemetryRow(
            instanceKey,
            serviceName,
            endpoint.AbsoluteUri.TrimEnd('/'),
            version,
            workLabel,
            health.Success
                ? healthCount.ToString(CultureInfo.InvariantCulture)
                : "--",
            $"Connected · {selection.ApiRoot}",
            state);

        return new ArrLiveTelemetrySnapshot(
            DateTimeOffset.UtcNow,
            new[] { service },
            workItems,
            state.ToUpperInvariant(),
            version,
            workCount?.ToString(CultureInfo.InvariantCulture) ?? "--",
            health.Success
                ? healthCount.ToString(CultureInfo.InvariantCulture)
                : "--");
    }

    private async Task<ApiRootSelection> SelectApiRootAsync(
        Uri endpoint,
        ArrTelemetryAdapterDefinition adapter,
        string apiKey,
        CancellationToken cancellationToken)
    {
        ApiResult? strongestFailure = null;

        foreach (var apiRoot in adapter.ApiRoots)
        {
            var result = await GetJsonAsync(
                new Uri(endpoint, $"{apiRoot}/system/status"),
                apiKey,
                cancellationToken);

            if (result.Success)
                return new ApiRootSelection(apiRoot, result);

            if (result.Status is "Unauthorized" or "Redirect blocked")
                return new ApiRootSelection(apiRoot, result);

            strongestFailure = PreferFailure(strongestFailure, result);
        }

        return new ApiRootSelection(
            adapter.ApiRoots[0],
            strongestFailure ??
            new ApiResult(
                false,
                default,
                "Unavailable",
                $"No {adapter.Product} API status endpoint responded."));
    }

    private async Task<ApiResult> GetJsonAsync(
        Uri uri,
        string apiKey,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, uri);
        request.Headers.TryAddWithoutValidation("Accept", "application/json");
        request.Headers.TryAddWithoutValidation("X-Api-Key", apiKey);

        HttpResponseMessage response;

        try
        {
            response = await _client.SendAsync(
                request,
                HttpCompletionOption.ResponseContentRead,
                cancellationToken);
        }
        catch (OperationCanceledException)
            when (!cancellationToken.IsCancellationRequested)
        {
            return new ApiResult(
                false,
                default,
                "Timed out",
                $"GET {SafeUri(uri)} exceeded the Arr API timeout.");
        }
        catch (HttpRequestException)
        {
            return new ApiResult(
                false,
                default,
                "Unavailable",
                $"Could not reach {SafeUri(uri)}.");
        }

        using (response)
        {
            if ((int)response.StatusCode is >= 300 and <= 399)
            {
                return new ApiResult(
                    false,
                    default,
                    "Redirect blocked",
                    $"GET {SafeUri(uri)} returned a redirect. " +
                    "GraveOps did not forward the API key.");
            }

            if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
            {
                return new ApiResult(
                    false,
                    default,
                    "Unauthorized",
                    "The Arr API rejected the configured API key.");
            }

            if (!response.IsSuccessStatusCode)
            {
                return new ApiResult(
                    false,
                    default,
                    $"HTTP {(int)response.StatusCode}",
                    $"GET {SafeUri(uri)} returned " +
                    $"{(int)response.StatusCode} {response.ReasonPhrase}.");
            }

            var content = await response.Content.ReadAsStringAsync(cancellationToken);

            try
            {
                using var document = JsonDocument.Parse(content);
                return new ApiResult(
                    true,
                    document.RootElement.Clone(),
                    "Online",
                    string.Empty);
            }
            catch (JsonException)
            {
                return new ApiResult(
                    false,
                    default,
                    "Invalid response",
                    $"GET {SafeUri(uri)} returned invalid JSON.");
            }
        }
    }

    private static ArrWorkItemRow ParseQueueRow(
        string serviceName,
        string product,
        JsonElement item)
    {
        var title =
            GetString(item, "title") ??
            GetNestedString(item, "series", "title") ??
            GetNestedString(item, "movie", "title") ??
            GetNestedString(item, "album", "title") ??
            GetNestedString(item, "artist", "artistName") ??
            GetNestedString(item, "artist", "name") ??
            GetNestedString(item, "book", "title") ??
            GetNestedString(item, "author", "authorName") ??
            "Queued item";

        var status =
            GetString(item, "trackedDownloadStatus") ??
            GetString(item, "status") ??
            "Queued";

        var remaining =
            GetString(item, "timeleft") ??
            FormatCompletionTime(GetString(item, "estimatedCompletionTime"));

        var detail =
            GetString(item, "errorMessage") ??
            FirstStatusMessage(item) ??
            GetString(item, "downloadClient") ??
            string.Empty;

        return new ArrWorkItemRow(
            serviceName,
            QueueType(product),
            title,
            status,
            FormatProgress(item),
            remaining,
            detail);
    }

    private static ArrWorkItemRow ParseIndexerRow(
        string serviceName,
        JsonElement item)
    {
        var name =
            GetString(
                item,
                "name") ??
            GetString(
                item,
                "implementationName") ??
            "Configured indexer";

        var implementation =
            GetString(
                item,
                "implementationName") ??
            GetString(
                item,
                "implementation") ??
            string.Empty;

        var protocol =
            GetString(
                item,
                "protocol") ??
            "Indexer";

        var priority =
            GetInt(
                item,
                "priority");

        var enabled =
            GetBoolean(
                item,
                "enable") ??
            GetBoolean(
                item,
                "enabled");

        var state =
            enabled switch
            {
                true =>
                    "Enabled",

                false =>
                    "Disabled",

                _ =>
                    "Configured"
            };

        var detail =
            string.Join(
                " Â· ",
                new[]
                {
                    string.IsNullOrWhiteSpace(
                        implementation) ||
                    implementation.Equals(
                        name,
                        StringComparison.OrdinalIgnoreCase)
                        ? null
                        : implementation,

                    priority.HasValue
                        ? $"priority {priority.Value}"
                        : null
                }
                .Where(value =>
                    !string.IsNullOrWhiteSpace(
                        value)));

        return new ArrWorkItemRow(
            serviceName,
            "Indexer",
            name,
            state,
            protocol,
            string.Empty,
            detail);
    }

    private static string QueueType(string product) =>
        product.ToLowerInvariant() switch
        {
            "sonarr" => "Episode",
            "radarr" => "Movie",
            "lidarr" => "Album",
            "readarr" => "Book",
            "whisparr" => "Item",
            _ => "Queue"
        };

    private static string FormatProgress(JsonElement item)
    {
        var size = GetDouble(item, "size");
        var left = GetDouble(item, "sizeleft");

        if (!size.HasValue || size <= 0 || !left.HasValue)
            return string.Empty;

        var percent = Math.Clamp(
            (size.Value - left.Value) / size.Value * 100d,
            0d,
            100d);

        return $"{percent:0}%";
    }

    private static string? FirstStatusMessage(JsonElement item)
    {
        if (item.ValueKind != JsonValueKind.Object ||
            !item.TryGetProperty("statusMessages", out var messages) ||
            messages.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        foreach (var status in messages.EnumerateArray())
        {
            var title = GetString(status, "title");
            if (!string.IsNullOrWhiteSpace(title))
                return title;

            if (!status.TryGetProperty("messages", out var details))
                continue;

            if (details.ValueKind == JsonValueKind.String)
                return details.GetString();

            if (details.ValueKind != JsonValueKind.Array)
                continue;

            foreach (var detail in details.EnumerateArray())
            {
                if (detail.ValueKind == JsonValueKind.String)
                    return detail.GetString();
            }
        }

        return null;
    }

    private static List<JsonElement> QueueRecords(JsonElement root)
    {
        if (root.ValueKind == JsonValueKind.Array)
        {
            return root.EnumerateArray()
                .Select(item => item.Clone())
                .ToList();
        }

        if (root.ValueKind == JsonValueKind.Object &&
            root.TryGetProperty("records", out var records) &&
            records.ValueKind == JsonValueKind.Array)
        {
            return records.EnumerateArray()
                .Select(item => item.Clone())
                .ToList();
        }

        return new List<JsonElement>();
    }

    private static ApiResult PreferFailure(ApiResult? current, ApiResult candidate)
    {
        if (current is null)
            return candidate;

        return FailureRank(candidate) > FailureRank(current)
            ? candidate
            : current;
    }

    private static int FailureRank(ApiResult result) =>
        result.Status switch
        {
            "Unauthorized" => 6,
            "Redirect blocked" => 6,
            "Timed out" => 5,
            "Unavailable" => 4,
            "HTTP 404" => 1,
            _ => 3
        };

    private static string SafeUri(Uri uri) =>
        uri.GetLeftPart(UriPartial.Path);

    private static string? GetString(JsonElement element, string property)
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

    private static string? GetNestedString(
        JsonElement element,
        string parent,
        string property)
    {
        if (element.ValueKind != JsonValueKind.Object ||
            !element.TryGetProperty(parent, out var nested))
        {
            return null;
        }

        return GetString(nested, property);
    }

    private static bool? GetBoolean(
        JsonElement element,
        string property)
    {
        if (element.ValueKind != JsonValueKind.Object ||
            !element.TryGetProperty(
                property,
                out var value))
        {
            return null;
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

            _ =>
                null
        };
    }

    private static int? GetInt(JsonElement element, string property)
    {
        if (element.ValueKind != JsonValueKind.Object ||
            !element.TryGetProperty(property, out var value))
        {
            return null;
        }

        return value.TryGetInt32(out var result)
            ? result
            : null;
    }

    private static double? GetDouble(JsonElement element, string property)
    {
        if (element.ValueKind != JsonValueKind.Object ||
            !element.TryGetProperty(property, out var value))
        {
            return null;
        }

        return value.TryGetDouble(out var result)
            ? result
            : null;
    }

    private static string FormatCompletionTime(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;

        if (!DateTimeOffset.TryParse(
                value,
                CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal,
                out var completion))
        {
            return value;
        }

        var remaining = completion - DateTimeOffset.UtcNow;

        if (remaining <= TimeSpan.Zero)
            return "Due";

        if (remaining.TotalDays >= 1d)
            return $"{remaining.TotalDays:0.0}d";

        if (remaining.TotalHours >= 1d)
            return $"{remaining.TotalHours:0.0}h";

        return $"{Math.Max(1d, remaining.TotalMinutes):0}m";
    }

    private sealed record ApiRootSelection(
        string ApiRoot,
        ApiResult Result);

    private sealed record ApiResult(
        bool Success,
        JsonElement Root,
        string Status,
        string Error);
}
