namespace GraveOps.Desktop.Linux;

public sealed record ArrApiAdapterDefinition(
    string Product,
    IReadOnlyList<string> ApiRoots,
    bool SupportsQueue);

public static class ArrApiCatalog
{
    private static readonly IReadOnlyDictionary<
        string,
        ArrApiAdapterDefinition> Adapters =
        new Dictionary<
            string,
            ArrApiAdapterDefinition>(
            StringComparer.OrdinalIgnoreCase)
        {
            ["Sonarr"] =
                new ArrApiAdapterDefinition(
                    "Sonarr",
                    new[]
                    {
                        "api/v5",
                        "api/v3"
                    },
                    SupportsQueue: true),
            ["Radarr"] =
                new ArrApiAdapterDefinition(
                    "Radarr",
                    new[]
                    {
                        "api/v3"
                    },
                    SupportsQueue: true),
            ["Lidarr"] =
                new ArrApiAdapterDefinition(
                    "Lidarr",
                    new[]
                    {
                        "api/v1"
                    },
                    SupportsQueue: true),
            ["Prowlarr"] =
                new ArrApiAdapterDefinition(
                    "Prowlarr",
                    new[]
                    {
                        "api/v1"
                    },
                    SupportsQueue: false),
            ["Readarr"] =
                new ArrApiAdapterDefinition(
                    "Readarr",
                    new[]
                    {
                        "api/v1"
                    },
                    SupportsQueue: true),
            ["Whisparr"] =
                new ArrApiAdapterDefinition(
                    "Whisparr",
                    new[]
                    {
                        "api/v1"
                    },
                    SupportsQueue: true)
        };

    public static IReadOnlyList<string>
        SupportedProducts { get; } =
        Adapters.Keys
            .OrderBy(product => product)
            .ToArray();

    public static bool IsSupportedProduct(
        string? product) =>
        !string.IsNullOrWhiteSpace(product) &&
        Adapters.ContainsKey(product);

    public static IReadOnlyList<string>
        ApiRootsFor(
            string? product) =>
        !string.IsNullOrWhiteSpace(product) &&
        Adapters.TryGetValue(
            product,
            out var adapter)
            ? adapter.ApiRoots
            : Array.Empty<string>();

    public static IReadOnlyList<string>
        OrderedStatusPaths(
            string? expectedProduct)
    {
        var roots =
            new List<string>();

        roots.AddRange(
            ApiRootsFor(
                expectedProduct));

        foreach (var root in
                 Adapters.Values
                     .SelectMany(adapter =>
                         adapter.ApiRoots))
        {
            if (!roots.Contains(
                    root,
                    StringComparer.OrdinalIgnoreCase))
            {
                roots.Add(root);
            }
        }

        return roots
            .Select(root =>
                $"{root.TrimEnd('/')}/system/status")
            .ToArray();
    }

    public static bool SupportsQueue(
        string? product) =>
        !string.IsNullOrWhiteSpace(product) &&
        Adapters.TryGetValue(
            product,
            out var adapter) &&
        adapter.SupportsQueue;

    public static string ApiVersionFromPath(
        string path)
    {
        var match =
            System.Text.RegularExpressions.Regex.Match(
                path ?? string.Empty,
                @"/?api/(v\d+)(?:/|$)",
                System.Text.RegularExpressions
                    .RegexOptions.IgnoreCase);

        return match.Success
            ? match.Groups[1].Value.ToLowerInvariant()
            : "unknown API";
    }
}
