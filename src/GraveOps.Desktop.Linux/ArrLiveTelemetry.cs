using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace GraveOps.Desktop.Linux;

public sealed record ArrServiceTelemetryRow(
    string InstanceKey,
    string Service,
    string Endpoint,
    string Version,
    string Work,
    string Health,
    string Access,
    string State)
{
    public string DisplayName =>
        Service;

    public string Detail =>
        string.Join(
            " · ",
            new[]
            {
                Work,
                $"Health {Health}",
                Access
            }
            .Where(value =>
                !string.IsNullOrWhiteSpace(value)));
}

public sealed record ArrWorkItemRow(
    string Service,
    string Type,
    string ItemIssue,
    string State,
    string Progress,
    string Remaining,
    string Detail);

public sealed record ArrLiveTelemetrySnapshot(
    DateTimeOffset CapturedAt,
    IReadOnlyList<ArrServiceTelemetryRow> Services,
    IReadOnlyList<ArrWorkItemRow> WorkItems,
    string OverallState,
    string VersionSummary,
    string WorkSummary,
    string HealthSummary);

public sealed class ArrLiveTelemetryService : IDisposable
{
    private readonly HttpClient _http =
        new(
            new SocketsHttpHandler
            {
                AllowAutoRedirect = false,
                ConnectTimeout =
                    TimeSpan.FromSeconds(3),
                PooledConnectionLifetime =
                    TimeSpan.FromMinutes(2),
                PooledConnectionIdleTimeout =
                    TimeSpan.FromMinutes(1),
                MaxConnectionsPerServer = 4
            },
            disposeHandler: true)
        {
            Timeout =
                TimeSpan.FromSeconds(6)
        };

    public async Task<ArrLiveTelemetrySnapshot> CaptureAsync(
        IReadOnlyList<ArrWorkspaceView> instances,
        CancellationToken cancellationToken = default)
    {
        var tasks = instances
            .Select(instance =>
                CaptureInstanceAsync(
                    instance,
                    cancellationToken))
            .ToArray();

        var results = await Task.WhenAll(tasks);
        var services = results
            .Select(result => result.Service)
            .ToArray();
        var workItems = results
            .SelectMany(result => result.WorkItems)
            .ToArray();

        var available = results.Count(result =>
            result.Available);
        var attention = results.Count(result =>
            result.Available &&
            result.HealthCount > 0);
        var totalWork = results
            .Where(result => result.WorkCount.HasValue)
            .Sum(result => result.WorkCount ?? 0);
        var totalHealth = results.Sum(result =>
            result.HealthCount);

        var overallState = instances.Count == 0
            ? "NOT DETECTED"
            : available == 0
                ? "UNAVAILABLE"
                : available < instances.Count ||
                  attention > 0
                    ? "ATTENTION"
                    : "ONLINE";

        var versions = services
            .Select(service => service.Version)
            .Where(version =>
                !string.IsNullOrWhiteSpace(version) &&
                version != "--")
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var versionSummary = versions.Length switch
        {
            0 => "--",
            1 => versions[0],
            _ => $"{versions.Length} versions"
        };

        var hasKnownWork = results.Any(result =>
            result.WorkCount.HasValue);

        return new ArrLiveTelemetrySnapshot(
            DateTimeOffset.Now,
            services,
            workItems,
            overallState,
            versionSummary,
            hasKnownWork
                ? totalWork.ToString(
                    CultureInfo.InvariantCulture)
                : "--",
            totalHealth.ToString(
                CultureInfo.InvariantCulture));
    }

    private async Task<InstanceResult> CaptureInstanceAsync(
        ArrWorkspaceView view,
        CancellationToken cancellationToken)
    {
        var baseUrl =
            ResolveBaseUrl(view.Integration);

        if (baseUrl is null)
        {
            return AccessFailure(
                view,
                "No verified local endpoint",
                "GraveOps could not resolve a local HTTP endpoint.");
        }

        if (!ArrApiCatalog.IsSupportedProduct(
                view.ProductName))
        {
            return new InstanceResult(
                new ArrServiceTelemetryRow(
                    view.InstanceKey,
                    view.DisplayName,
                    baseUrl,
                    "--",
                    "Runtime only",
                    "--",
                    "Connector pending",
                    view.Integration.State),
                new[]
                {
                    new ArrWorkItemRow(
                        view.DisplayName,
                        "Access",
                        "Live application connector not available yet",
                        "Runtime only",
                        string.Empty,
                        string.Empty,
                        $"{view.ProductName} currently uses verified host and container evidence.")
                },
                Available: false,
                WorkCount: null,
                HealthCount: 0);
        }

        var config = await DiscoverConfigAsync(
            view,
            ParsePort(baseUrl),
            cancellationToken);

        if (config is null ||
            string.IsNullOrWhiteSpace(config.ApiKey))
        {
            return AccessFailure(
                view,
                "API config not discovered",
                $"GraveOps could not discover a local {view.ProductName} config.xml for {baseUrl}. Set its path under Customize.");
        }

        var apiSelection =
            await SelectApiRootAsync(
                baseUrl,
                view.ProductName,
                config.ApiKey,
                cancellationToken);

        var apiRoot =
            apiSelection.ApiRoot;
        var status =
            apiSelection.Result;

        if (!status.Success)
        {
            return AccessFailure(
                view,
                status.Status,
                status.Error);
        }

        var version =
            GetString(status.Root, "version") ??
            "--";

        var health = await GetJsonAsync(
            $"{baseUrl.TrimEnd('/')}/{apiRoot}/health",
            config.ApiKey,
            cancellationToken);

        var workItems = new List<ArrWorkItemRow>();
        var healthCount = 0;

        if (health.Success &&
            health.Root.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in
                     health.Root.EnumerateArray())
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
                        view.DisplayName,
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

        int? workCount = null;
        string workLabel;

        if (ArrApiCatalog.SupportsQueue(view.ProductName))
        {
            var queue = await GetJsonAsync(
                $"{baseUrl.TrimEnd('/')}/{apiRoot}/queue?page=1&pageSize=100&sortDirection=descending&sortKey=timeleft&includeUnknownSeriesItems=true&includeUnknownMovieItems=true",
                config.ApiKey,
                cancellationToken);

            if (queue.Success)
            {
                var records =
                    QueueRecords(queue.Root);

                workCount =
                    GetInt(
                        queue.Root,
                        "totalRecords") ??
                    records.Count;
                workLabel =
                    $"Queue {workCount}";

                foreach (var item in records)
                {
                    workItems.Add(
                        ParseQueueRow(
                            view,
                            item));
                }
            }
            else
            {
                workLabel = "Queue unavailable";
                workItems.Add(
                    new ArrWorkItemRow(
                        view.DisplayName,
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
                $"{baseUrl.TrimEnd('/')}/{apiRoot}/indexer",
                config.ApiKey,
                cancellationToken);

            if (indexers.Success &&
                indexers.Root.ValueKind ==
                JsonValueKind.Array)
            {
                workCount =
                    indexers.Root.GetArrayLength();
                workLabel =
                    $"Indexers {workCount}";
            }
            else
            {
                workLabel =
                    "Indexers unavailable";
            }
        }

        if (workItems.Count == 0)
        {
            workItems.Add(
                new ArrWorkItemRow(
                    view.DisplayName,
                    ArrApiCatalog.SupportsQueue(
                        view.ProductName)
                        ? "Queue"
                        : "Health",
                    ArrApiCatalog.SupportsQueue(
                        view.ProductName)
                        ? "No queued work or active health issue"
                        : "No active health issue",
                    "Healthy",
                    string.Empty,
                    string.Empty,
                    "The latest local API probe returned no actionable rows."));
        }

        return new InstanceResult(
            new ArrServiceTelemetryRow(
                view.InstanceKey,
                view.DisplayName,
                baseUrl,
                version,
                workLabel,
                health.Success
                    ? healthCount.ToString(
                        CultureInfo.InvariantCulture)
                    : "--",
                $"Connected · {Path.GetFileName(config.Path)} · {apiRoot}",
                healthCount > 0
                    ? "Attention"
                    : "Online"),
            workItems,
            Available: true,
            WorkCount: workCount,
            HealthCount: healthCount);
    }

    private static ArrWorkItemRow ParseQueueRow(
        ArrWorkspaceView view,
        JsonElement item)
    {
        var title =
            GetString(item, "title") ??
            GetNestedString(item, "series", "title") ??
            GetNestedString(item, "movie", "title") ??
            GetNestedString(item, "artist", "artistName") ??
            GetNestedString(item, "artist", "name") ??
            GetNestedString(item, "book", "title") ??
            GetNestedString(item, "author", "authorName") ??
            "Queued item";

        var status =
            GetString(
                item,
                "trackedDownloadStatus") ??
            GetString(item, "status") ??
            "Queued";

        var remaining =
            GetString(item, "timeleft") ??
            FormatCompletionTime(
                GetString(
                    item,
                    "estimatedCompletionTime"));

        var detail =
            GetString(item, "errorMessage") ??
            FirstStatusMessage(item) ??
            GetString(item, "downloadClient") ??
            string.Empty;

        return new ArrWorkItemRow(
            view.DisplayName,
            QueueType(view.ProductName),
            title,
            status,
            FormatProgress(item),
            remaining,
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

    private static string FormatProgress(
        JsonElement item)
    {
        var size = GetDouble(item, "size");
        var left = GetDouble(item, "sizeleft");

        if (!size.HasValue ||
            size <= 0 ||
            !left.HasValue)
        {
            return string.Empty;
        }

        var percent = Math.Clamp(
            (size.Value - left.Value) /
            size.Value * 100d,
            0,
            100);

        return $"{percent:0}%";
    }

    private static string? FirstStatusMessage(
        JsonElement item)
    {
        if (!item.TryGetProperty(
                "statusMessages",
                out var messages) ||
            messages.ValueKind !=
            JsonValueKind.Array)
        {
            return null;
        }

        foreach (var status in messages.EnumerateArray())
        {
            var title =
                GetString(status, "title");

            if (!string.IsNullOrWhiteSpace(title))
                return title;

            if (status.TryGetProperty(
                    "messages",
                    out var detail))
            {
                if (detail.ValueKind ==
                    JsonValueKind.Array)
                {
                    var first = detail
                        .EnumerateArray()
                        .FirstOrDefault();

                    if (first.ValueKind ==
                        JsonValueKind.String)
                    {
                        return first.GetString();
                    }
                }

                if (detail.ValueKind ==
                    JsonValueKind.String)
                {
                    return detail.GetString();
                }
            }
        }

        return null;
    }

    private static List<JsonElement> QueueRecords(
        JsonElement root)
    {
        if (root.ValueKind == JsonValueKind.Array)
        {
            return root
                .EnumerateArray()
                .Select(item => item.Clone())
                .ToList();
        }

        if (root.ValueKind == JsonValueKind.Object &&
            root.TryGetProperty(
                "records",
                out var records) &&
            records.ValueKind == JsonValueKind.Array)
        {
            return records
                .EnumerateArray()
                .Select(item => item.Clone())
                .ToList();
        }

        return new List<JsonElement>();
    }

    private async Task<ApiRootSelection>
        SelectApiRootAsync(
            string baseUrl,
            string product,
            string apiKey,
            CancellationToken cancellationToken)
    {
        var roots =
            ArrApiCatalog.ApiRootsFor(
                product);

        if (roots.Count == 0)
        {
            return new ApiRootSelection(
                string.Empty,
                new ApiResult(
                    false,
                    default,
                    "Unsupported API",
                    $"{product} does not have a registered Arr API adapter."));
        }

        ApiResult? strongestFailure =
            null;

        foreach (var root in roots)
        {
            var result =
                await GetJsonAsync(
                    $"{baseUrl.TrimEnd('/')}/" +
                    $"{root.Trim('/')}/system/status",
                    apiKey,
                    cancellationToken);

            if (result.Success)
            {
                return new ApiRootSelection(
                    root,
                    result);
            }

            strongestFailure =
                PreferApiFailure(
                    strongestFailure,
                    result);
        }

        return new ApiRootSelection(
            roots[0],
            strongestFailure ??
            new ApiResult(
                false,
                default,
                "Unavailable",
                $"No {product} API status endpoint responded."));
    }

    private static ApiResult PreferApiFailure(
        ApiResult? current,
        ApiResult candidate)
    {
        if (current is null)
            return candidate;

        return ApiFailureRank(candidate) >
               ApiFailureRank(current)
            ? candidate
            : current;
    }

    private static int ApiFailureRank(
        ApiResult result) =>
        result.Status switch
        {
            "Unauthorized" => 6,
            "Redirect blocked" => 6,
            "Timed out" => 5,
            "Unavailable" => 4,
            "HTTP 404" => 1,
            _ => 3
        };

    private static InstanceResult AccessFailure(
        ArrWorkspaceView view,
        string status,
        string detail)
    {
        var endpoint =
            ResolveBaseUrl(
                view.Integration) ??
            "No endpoint";

        return new InstanceResult(
            new ArrServiceTelemetryRow(
                view.InstanceKey,
                view.DisplayName,
                endpoint,
                "--",
                "--",
                "--",
                status,
                "Unavailable"),
            new[]
            {
                new ArrWorkItemRow(
                    view.DisplayName,
                    "Access",
                    status,
                    "Unavailable",
                    string.Empty,
                    string.Empty,
                    detail)
            },
            Available: false,
            WorkCount: null,
            HealthCount: 0);
    }

    private async Task<ApiResult> GetJsonAsync(
        string url,
        string apiKey,
        CancellationToken cancellationToken)
    {
        try
        {
            using var request =
                new HttpRequestMessage(
                    HttpMethod.Get,
                    url);

            request.Headers
                .TryAddWithoutValidation(
                    "X-Api-Key",
                    apiKey);

            using var response =
                await _http.SendAsync(
                    request,
                    HttpCompletionOption
                        .ResponseHeadersRead,
                    cancellationToken);

            if ((int)response.StatusCode is
                >= 300 and <= 399)
            {
                return new ApiResult(
                    false,
                    default,
                    "Redirect blocked",
                    $"GET {url} returned a redirect. " +
                    "GraveOps did not follow it while an API key was present.");
            }

            var text =
                await response.Content
                    .ReadAsStringAsync(
                        cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                return new ApiResult(
                    false,
                    default,
                    response.StatusCode ==
                    HttpStatusCode.Unauthorized
                        ? "Unauthorized"
                        : $"HTTP {(int)response.StatusCode}",
                    $"GET {url} returned {(int)response.StatusCode} {response.ReasonPhrase}.");
            }

            using var document =
                JsonDocument.Parse(text);

            return new ApiResult(
                true,
                document.RootElement.Clone(),
                "Online",
                string.Empty);
        }
        catch (TaskCanceledException)
        {
            return new ApiResult(
                false,
                default,
                "Timed out",
                $"GET {url} exceeded the local probe timeout.");
        }
        catch (Exception exception)
        {
            return new ApiResult(
                false,
                default,
                "Unavailable",
                exception.Message);
        }
    }

    private static string? ResolveBaseUrl(
        OpsIntegration integration)
    {
        if (!integration.IsVerified)
            return null;

        var endpoint =
            integration.Endpoint?.Trim() ??
            string.Empty;

        if (!Uri.TryCreate(
                endpoint,
                UriKind.Absolute,
                out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttp &&
             uri.Scheme != Uri.UriSchemeHttps))
        {
            return null;
        }

        var builder =
            new UriBuilder(uri)
            {
                Query =
                    string.Empty,
                Fragment =
                    string.Empty
            };

        var path =
            uri.AbsolutePath.TrimEnd('/');

        builder.Path =
            string.IsNullOrWhiteSpace(path)
                ? "/"
                : path + "/";

        return builder.Uri
            .ToString()
            .TrimEnd('/');
    }

    private static int ParsePort(
        string baseUrl) =>
        Uri.TryCreate(
            baseUrl,
            UriKind.Absolute,
            out var uri)
            ? uri.Port
            : 0;

    private async Task<LocalConfig?> DiscoverConfigAsync(
        ArrWorkspaceView view,
        int endpointPort,
        CancellationToken cancellationToken)
    {
        var candidates =
            new List<string>();

        if (!string.IsNullOrWhiteSpace(
                view.Profile.ConfigPath))
        {
            candidates.Add(
                Environment
                    .ExpandEnvironmentVariables(
                        view.Profile.ConfigPath));
        }

        var home =
            Environment.GetFolderPath(
                Environment.SpecialFolder
                    .UserProfile);
        var product = view.ProductName;
        var lower =
            product.ToLowerInvariant();

        candidates.AddRange(
            new[]
            {
                Path.Combine(
                    home,
                    ".config",
                    product,
                    "config.xml"),
                Path.Combine(
                    home,
                    ".config",
                    lower,
                    "config.xml"),
                Path.Combine(
                    "/var/lib",
                    lower,
                    ".config",
                    product,
                    "config.xml"),
                Path.Combine(
                    "/var/lib",
                    lower,
                    "config.xml"),
                Path.Combine(
                    "/opt",
                    lower,
                    "config.xml"),
                Path.Combine(
                    "/opt",
                    lower,
                    "config",
                    "config.xml")
            });

        if (Directory.Exists("/opt/dumb"))
        {
            candidates.AddRange(
                await FindConfigFilesAsync(
                    "/opt/dumb",
                    cancellationToken));
        }

        var ownRoot =
            Path.Combine(
                "/opt",
                lower);

        if (Directory.Exists(ownRoot))
        {
            candidates.AddRange(
                await FindConfigFilesAsync(
                    ownRoot,
                    cancellationToken));
        }

        return candidates
            .Where(path =>
                !string.IsNullOrWhiteSpace(path) &&
                File.Exists(path))
            .Distinct(
                StringComparer.OrdinalIgnoreCase)
            .Select(path =>
                ReadConfig(
                    path,
                    endpointPort,
                    product,
                    string.Equals(
                        path,
                        view.Profile.ConfigPath,
                        StringComparison.OrdinalIgnoreCase)))
            .Where(config =>
                config is not null)
            .Cast<LocalConfig>()
            .OrderByDescending(config =>
                config.Score)
            .FirstOrDefault();
    }

    private static LocalConfig? ReadConfig(
        string path,
        int endpointPort,
        string product,
        bool explicitPath)
    {
        try
        {
            var document =
                XDocument.Load(path);
            var root = document.Root;

            if (root is null)
                return null;

            var apiKey = root
                .Descendants("ApiKey")
                .Select(element =>
                    element.Value.Trim())
                .FirstOrDefault();

            if (string.IsNullOrWhiteSpace(apiKey))
                return null;

            var portText = root
                .Descendants("Port")
                .Select(element =>
                    element.Value.Trim())
                .FirstOrDefault();

            int.TryParse(
                portText,
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out var configPort);

            var score =
                explicitPath ? 1000 : 0;

            if (endpointPort > 0 &&
                configPort == endpointPort)
            {
                score += 500;
            }

            if (path.Contains(
                    product,
                    StringComparison.OrdinalIgnoreCase))
            {
                score += 100;
            }

            return new LocalConfig(
                path,
                apiKey,
                configPort,
                score);
        }
        catch
        {
            return null;
        }
    }

    private static async Task<IReadOnlyList<string>>
        FindConfigFilesAsync(
            string root,
            CancellationToken cancellationToken)
    {
        try
        {
            using var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = "find",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                }
            };

            process.StartInfo.ArgumentList.Add(root);
            process.StartInfo.ArgumentList.Add(
                "-maxdepth");
            process.StartInfo.ArgumentList.Add("6");
            process.StartInfo.ArgumentList.Add(
                "-type");
            process.StartInfo.ArgumentList.Add("f");
            process.StartInfo.ArgumentList.Add(
                "-name");
            process.StartInfo.ArgumentList.Add(
                "config.xml");

            process.Start();

            var output =
                await process.StandardOutput
                    .ReadToEndAsync(
                        cancellationToken);

            await process.WaitForExitAsync(
                cancellationToken);

            return output
                .Split(
                    '\n',
                    StringSplitOptions
                        .RemoveEmptyEntries)
                .Select(path => path.Trim())
                .Take(256)
                .ToArray();
        }
        catch
        {
            return Array.Empty<string>();
        }
    }

    private static string? GetString(
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
            _ => null
        };
    }

    private static string? GetNestedString(
        JsonElement element,
        string parent,
        string property)
    {
        if (element.ValueKind !=
            JsonValueKind.Object ||
            !element.TryGetProperty(
                parent,
                out var nested))
        {
            return null;
        }

        return GetString(
            nested,
            property);
    }

    private static int? GetInt(
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

        return value.TryGetInt32(
            out var number)
            ? number
            : null;
    }

    private static double? GetDouble(
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

        return value.TryGetDouble(
            out var number)
            ? number
            : null;
    }

    private static string FormatCompletionTime(
        string? value)
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

        var remaining =
            completion - DateTimeOffset.Now;

        if (remaining <= TimeSpan.Zero)
            return "Due";

        if (remaining.TotalDays >= 1)
            return $"{remaining.TotalDays:0.0}d";

        if (remaining.TotalHours >= 1)
            return $"{remaining.TotalHours:0.0}h";

        return
            $"{Math.Max(1, remaining.TotalMinutes):0}m";
    }

    public void Dispose() =>
        _http.Dispose();

    private sealed record ApiRootSelection(
        string ApiRoot,
        ApiResult Result);

    private sealed record ApiResult(
        bool Success,
        JsonElement Root,
        string Status,
        string Error);

    private sealed record LocalConfig(
        string Path,
        string ApiKey,
        int Port,
        int Score);

    private sealed record InstanceResult(
        ArrServiceTelemetryRow Service,
        IReadOnlyList<ArrWorkItemRow> WorkItems,
        bool Available,
        int? WorkCount,
        int HealthCount);
}
