using System.Text.RegularExpressions;

namespace GraveOps.Desktop.Linux;

public enum ProductOperationalHealthKind
{
    RuntimeOnly,
    HttpStatus,
    ArrPing,
    PlexIdentity,
    JellyfinHealth,
    EmbyPing,
    DnsService
}

public sealed record ProductOperationalContract(
    string Name,
    string Family,
    IReadOnlyList<string> Aliases,
    IReadOnlyList<string> Dependencies,
    IReadOnlyList<string> DefaultUnits,
    IReadOnlyList<string> DefaultContainers,
    IReadOnlyList<int> DefaultPorts,
    string HealthPath,
    string NavigationName,
    bool AllowsExitedPrimary,
    ProductOperationalHealthKind HealthKind,
    IReadOnlyList<int> ReachableHttpStatuses,
    IReadOnlyList<string> HealthyTokens,
    string VerificationDescription);

public static class ProductOperationalCatalog
{
    private static readonly Regex HttpStatusPattern = new(
        @"(?<!\d)(?<status>[1-5]\d{2})(?!\d)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex UrlPattern = new(
        @"https?://[^\s·;,]+",
        RegexOptions.IgnoreCase | RegexOptions.Compiled |
        RegexOptions.CultureInvariant);
    private static readonly Regex GenericExitedContextPattern = new(
        @"(^|\W)(oneshot|supporting service|timer|mount)(\W|$)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled |
        RegexOptions.CultureInvariant);

    private static readonly IReadOnlyList<ProductOperationalContract>
        Contracts = BuildContracts();

    public static IReadOnlyList<ProductOperationalContract> All => Contracts;

    public static ProductOperationalContract? Find(string value)
    {
        var token = Normalize(value);
        if (token.Length == 0)
            return null;

        return Contracts.FirstOrDefault(contract =>
            Normalize(contract.Name).Equals(
                token,
                StringComparison.OrdinalIgnoreCase) ||
            contract.Aliases.Any(alias =>
                Normalize(alias).Equals(
                    token,
                    StringComparison.OrdinalIgnoreCase)));
    }

    public static bool Supports(string value) => Find(value) is not null;

    public static IReadOnlyList<VerifiedRemediationProduct>
        ToVerifiedRemediationProducts() =>
        Contracts.Select(contract =>
            new VerifiedRemediationProduct(
                contract.Name,
                contract.Family,
                contract.Aliases,
                contract.Dependencies,
                contract.DefaultUnits,
                contract.DefaultContainers,
                contract.HealthPath,
                contract.AllowsExitedPrimary))
            .ToArray();

    public static string NavigationFor(string product) =>
        Find(product)?.NavigationName ?? "MediaHubNav";

    public static string ResolveNavigation(
        string product,
        string fallback)
    {
        if (!string.IsNullOrWhiteSpace(fallback) &&
            !fallback.Equals(
                "MediaHubNav",
                StringComparison.OrdinalIgnoreCase) &&
            !fallback.Equals(
                "DashboardNav",
                StringComparison.OrdinalIgnoreCase))
        {
            return fallback;
        }

        return NavigationFor(product);
    }

    public static bool AllowsExitedPrimary(
        string identity,
        string evidence)
    {
        var combined = $"{identity} {evidence}";
        var matched = Contracts.FirstOrDefault(contract =>
            ContractTokens(contract).Any(token =>
                ContainsToken(combined, token)));
        if (matched?.AllowsExitedPrimary == true)
            return true;

        return GenericExitedContextPattern.IsMatch(combined);
    }

    public static string ResolveEndpoint(
        string product,
        string suppliedEndpoint,
        string evidence)
    {
        var contract = Find(product);
        var candidate = suppliedEndpoint?.Trim() ?? string.Empty;
        if (candidate.Length == 0)
        {
            var match = UrlPattern.Match(evidence ?? string.Empty);
            if (match.Success)
                candidate = match.Value.TrimEnd('.', ')', ']');
        }

        if (candidate.Length > 0)
            return AddHealthPath(candidate, contract?.HealthPath ?? "/");

        var port = contract?.DefaultPorts.FirstOrDefault() ?? 0;
        if (port <= 0)
            return string.Empty;

        return AddHealthPath(
            $"http://127.0.0.1:{port}",
            contract!.HealthPath);
    }

    public static IReadOnlyList<string> HttpInspectionCommands(
        string product,
        string endpoint)
    {
        var resolved = ResolveEndpoint(product, endpoint, string.Empty);
        if (string.IsNullOrWhiteSpace(resolved))
        {
            return new[]
            {
                $"echo 'No verified HTTP endpoint is available for {SanitizeDisplay(product)}.'"
            };
        }

        var quoted = VerifiedRemediationPolicy.ShellQuote(resolved);
        return new[]
        {
            $"curl -sS -D - -o /dev/null --max-time 8 {quoted}",
            $"curl -sS -o /dev/null -w '%{{http_code}}' --max-time 8 {quoted}"
        };
    }

    public static string HttpVerificationCommand(
        string product,
        string endpoint)
    {
        var resolved = ResolveEndpoint(product, endpoint, string.Empty);
        return string.IsNullOrWhiteSpace(resolved)
            ? $"echo 'Verification requires the {SanitizeDisplay(product)} workspace.'"
            : $"curl -sS -o /dev/null -w '%{{http_code}}' --max-time 8 " +
              VerifiedRemediationPolicy.ShellQuote(resolved);
    }

    public static bool EndpointVerificationSucceeded(
        string product,
        string output)
    {
        var value = output?.Trim() ?? string.Empty;
        if (value.Length == 0)
            return false;

        var contract = Find(product);
        var statusMatch = HttpStatusPattern.Match(value);
        if (statusMatch.Success &&
            int.TryParse(statusMatch.Groups["status"].Value, out var status))
        {
            var declared = contract?.ReachableHttpStatuses ?? Array.Empty<int>();
            if (declared.Count > 0)
                return declared.Contains(status);
            return status is >= 200 and < 500;
        }

        if (value.Contains("reachable", StringComparison.OrdinalIgnoreCase))
            return true;

        return (contract?.HealthyTokens ?? Array.Empty<string>())
            .Any(token => value.Contains(
                token,
                StringComparison.OrdinalIgnoreCase));
    }

    public static string ExpectedResult(string product)
    {
        var contract = Find(product);
        return contract?.VerificationDescription ??
            $"The {SanitizeDisplay(product)} endpoint or workspace becomes reachable and the finding clears on refresh.";
    }

    public static string CoverageSummary()
    {
        var groups = Contracts
            .GroupBy(item => item.Family)
            .OrderBy(group => group.Key)
            .Select(group => $"{group.Key} {group.Count()}")
            .ToArray();
        return $"{Contracts.Count} product contracts · " +
               string.Join(" · ", groups);
    }

    private static string AddHealthPath(
        string endpoint,
        string healthPath)
    {
        if (!Uri.TryCreate(endpoint, UriKind.Absolute, out var uri))
            return endpoint;
        if (string.IsNullOrWhiteSpace(healthPath) || healthPath == "/")
            return endpoint.TrimEnd('/');
        if (!string.IsNullOrWhiteSpace(uri.AbsolutePath) &&
            uri.AbsolutePath != "/")
        {
            return endpoint.TrimEnd('/');
        }

        return endpoint.TrimEnd('/') + "/" + healthPath.TrimStart('/');
    }

    private static IEnumerable<string> ContractTokens(
        ProductOperationalContract contract) =>
        new[] { contract.Name }.Concat(contract.Aliases)
            .Select(Normalize)
            .Where(token => token.Length > 1)
            .Distinct(StringComparer.OrdinalIgnoreCase);

    private static bool ContainsToken(
        string value,
        string normalizedToken)
    {
        var normalizedValue = Normalize(value);
        return normalizedValue.Equals(
                   normalizedToken,
                   StringComparison.OrdinalIgnoreCase) ||
               normalizedValue.StartsWith(
                   normalizedToken + "-",
                   StringComparison.OrdinalIgnoreCase) ||
               normalizedValue.EndsWith(
                   "-" + normalizedToken,
                   StringComparison.OrdinalIgnoreCase) ||
               normalizedValue.Contains(
                   "-" + normalizedToken + "-",
                   StringComparison.OrdinalIgnoreCase);
    }

    private static string Normalize(string value) =>
        Regex.Replace(
            value?.Trim().ToLowerInvariant() ?? string.Empty,
            @"[^a-z0-9]+",
            "-")
        .Trim('-');

    private static string SanitizeDisplay(string value) =>
        string.IsNullOrWhiteSpace(value)
            ? "product"
            : value.Replace("'", string.Empty).Trim();

    private static IReadOnlyList<ProductOperationalContract>
        BuildContracts()
    {
        static ProductOperationalContract Product(
            string name,
            string family,
            string[] aliases,
            string[] dependencies,
            string[] units,
            string[] containers,
            int[] ports,
            string healthPath,
            string navigation,
            ProductOperationalHealthKind healthKind,
            bool exited = false,
            int[]? statuses = null,
            string[]? healthyTokens = null,
            string? verification = null) =>
            new(
                name,
                family,
                aliases,
                dependencies,
                units,
                containers,
                ports,
                healthPath,
                navigation,
                exited,
                healthKind,
                statuses ?? new[] { 200, 204, 301, 302, 401, 403 },
                healthyTokens ?? new[] { "online", "healthy", "ready", "running" },
                verification ??
                    $"{name} answers its declared health endpoint and its owning runtime remains healthy.");

        return new[]
        {
            Product("Sonarr", "Acquisition", new[] { "sonarr" }, new[] { "Prowlarr", "download client", "storage" }, new[] { "sonarr.service" }, new[] { "sonarr" }, new[] { 8989 }, "/ping", "SonarrNav", ProductOperationalHealthKind.ArrPing),
            Product("Radarr", "Acquisition", new[] { "radarr" }, new[] { "Prowlarr", "download client", "storage" }, new[] { "radarr.service" }, new[] { "radarr" }, new[] { 7878 }, "/ping", "RadarrNav", ProductOperationalHealthKind.ArrPing),
            Product("Lidarr", "Acquisition", new[] { "lidarr" }, new[] { "Prowlarr", "download client", "storage" }, new[] { "lidarr.service" }, new[] { "lidarr" }, new[] { 8686 }, "/ping", "LidarrNav", ProductOperationalHealthKind.ArrPing),
            Product("Prowlarr", "Discovery", new[] { "prowlarr" }, new[] { "indexers", "network" }, new[] { "prowlarr.service" }, new[] { "prowlarr" }, new[] { 9696 }, "/ping", "ProwlarrNav", ProductOperationalHealthKind.ArrPing),
            Product("Readarr", "Acquisition", new[] { "readarr" }, new[] { "Prowlarr", "download client", "storage" }, new[] { "readarr.service" }, new[] { "readarr" }, new[] { 8787 }, "/ping", "ReadarrNav", ProductOperationalHealthKind.ArrPing),
            Product("Whisparr", "Acquisition", new[] { "whisparr" }, new[] { "Prowlarr", "download client", "storage" }, new[] { "whisparr.service" }, new[] { "whisparr" }, new[] { 6969 }, "/ping", "WhisparrNav", ProductOperationalHealthKind.ArrPing),
            Product("Bazarr", "Processing", new[] { "bazarr" }, new[] { "Sonarr", "Radarr", "storage" }, new[] { "bazarr.service" }, new[] { "bazarr" }, new[] { 6767 }, "/api/system/status", "BazarrNav", ProductOperationalHealthKind.HttpStatus),
            Product("Mylar3", "Acquisition", new[] { "mylar", "mylar3" }, new[] { "download client", "storage" }, new[] { "mylar3.service" }, new[] { "mylar3", "mylar" }, new[] { 8090 }, "/", "Mylar3Nav", ProductOperationalHealthKind.HttpStatus),
            Product("Medusa", "Acquisition", new[] { "medusa" }, new[] { "download client", "storage" }, new[] { "medusa.service" }, new[] { "medusa" }, Array.Empty<int>(), "/", "MediaHubNav", ProductOperationalHealthKind.HttpStatus),
            Product("SickChill", "Acquisition", new[] { "sickchill", "sick-chill" }, new[] { "download client", "storage" }, new[] { "sickchill.service" }, new[] { "sickchill" }, Array.Empty<int>(), "/", "MediaHubNav", ProductOperationalHealthKind.HttpStatus),
            Product("LazyLibrarian", "Acquisition", new[] { "lazylibrarian", "lazy-librarian" }, new[] { "download client", "storage" }, new[] { "lazylibrarian.service" }, new[] { "lazylibrarian" }, Array.Empty<int>(), "/", "MediaHubNav", ProductOperationalHealthKind.HttpStatus),

            Product("Recyclarr", "Policy automation", new[] { "recyclarr" }, new[] { "Sonarr", "Radarr", "configuration" }, new[] { "recyclarr.service" }, new[] { "recyclarr" }, Array.Empty<int>(), "/", "RecyclarrNav", ProductOperationalHealthKind.RuntimeOnly, exited: true),
            Product("Configarr", "Policy automation", new[] { "configarr" }, new[] { "Sonarr", "Radarr", "configuration" }, new[] { "configarr.service" }, new[] { "configarr" }, Array.Empty<int>(), "/", "MediaHubNav", ProductOperationalHealthKind.RuntimeOnly, exited: true),
            Product("Profilarr", "Policy automation", new[] { "profilarr" }, new[] { "Arr applications", "configuration" }, new[] { "profilarr.service" }, new[] { "profilarr" }, Array.Empty<int>(), "/", "MediaHubNav", ProductOperationalHealthKind.HttpStatus),
            Product("Cleanuparr", "Policy automation", new[] { "cleanuparr" }, new[] { "Arr applications", "download clients" }, new[] { "cleanuparr.service" }, new[] { "cleanuparr" }, Array.Empty<int>(), "/", "MediaHubNav", ProductOperationalHealthKind.HttpStatus),
            Product("Maintainerr", "Policy automation", new[] { "maintainerr" }, new[] { "media server", "Arr applications" }, new[] { "maintainerr.service" }, new[] { "maintainerr" }, Array.Empty<int>(), "/", "MediaHubNav", ProductOperationalHealthKind.HttpStatus),
            Product("Huntarr", "Policy automation", new[] { "huntarr" }, new[] { "Arr applications", "network" }, new[] { "huntarr.service" }, new[] { "huntarr" }, Array.Empty<int>(), "/", "MediaHubNav", ProductOperationalHealthKind.HttpStatus),
            Product("Notifiarr", "Policy automation", new[] { "notifiarr" }, new[] { "network", "Arr applications" }, new[] { "notifiarr.service" }, new[] { "notifiarr" }, Array.Empty<int>(), "/", "MediaHubNav", ProductOperationalHealthKind.HttpStatus),
            Product("Autobrr", "Discovery", new[] { "autobrr" }, new[] { "trackers", "download clients", "network" }, new[] { "autobrr.service" }, new[] { "autobrr" }, Array.Empty<int>(), "/", "MediaHubNav", ProductOperationalHealthKind.HttpStatus),
            Product("Unpackerr", "Processing", new[] { "unpackerr" }, new[] { "download clients", "Arr applications", "storage" }, new[] { "unpackerr.service" }, new[] { "unpackerr" }, Array.Empty<int>(), "/", "MediaHubNav", ProductOperationalHealthKind.RuntimeOnly),

            Product("Plex", "Media server", new[] { "plex", "plex media server" }, new[] { "storage", "database", "network" }, new[] { "plexmediaserver.service" }, new[] { "plex", "plex-media-server" }, new[] { 32400 }, "/identity", "PlexNav", ProductOperationalHealthKind.PlexIdentity, verification: "Plex answers /identity and plexmediaserver.service remains in a valid continuously-running state."),
            Product("Jellyfin", "Media server", new[] { "jellyfin" }, new[] { "storage", "database", "network" }, new[] { "jellyfin.service" }, new[] { "jellyfin" }, new[] { 8096 }, "/health", "MediaHubNav", ProductOperationalHealthKind.JellyfinHealth, healthyTokens: new[] { "healthy", "online" }, verification: "Jellyfin answers /health and its primary service or container remains healthy."),
            Product("Emby", "Media server", new[] { "emby", "emby server" }, new[] { "storage", "database", "network" }, new[] { "emby-server.service" }, new[] { "emby", "embyserver" }, new[] { 8096 }, "/System/Ping", "MediaHubNav", ProductOperationalHealthKind.EmbyPing, verification: "Emby answers /System/Ping and its primary service or container remains healthy."),
            Product("Navidrome", "Media server", new[] { "navidrome" }, new[] { "music storage", "database", "network" }, new[] { "navidrome.service" }, new[] { "navidrome" }, new[] { 4533 }, "/ping", "MediaHubNav", ProductOperationalHealthKind.HttpStatus),
            Product("Audiobookshelf", "Media server", new[] { "audiobookshelf", "abs" }, new[] { "media storage", "database", "network" }, new[] { "audiobookshelf.service" }, new[] { "audiobookshelf" }, new[] { 13378 }, "/healthcheck", "MediaHubNav", ProductOperationalHealthKind.HttpStatus),
            Product("Kavita", "Media server", new[] { "kavita" }, new[] { "book storage", "database", "network" }, new[] { "kavita.service" }, new[] { "kavita" }, Array.Empty<int>(), "/", "MediaHubNav", ProductOperationalHealthKind.HttpStatus),
            Product("Calibre-Web", "Media server", new[] { "calibre-web", "calibreweb" }, new[] { "book storage", "database", "network" }, new[] { "calibre-web.service" }, new[] { "calibre-web" }, Array.Empty<int>(), "/", "MediaHubNav", ProductOperationalHealthKind.HttpStatus),
            Product("Tautulli", "Media analytics", new[] { "tautulli" }, new[] { "Plex", "database", "network" }, new[] { "tautulli.service" }, new[] { "tautulli" }, new[] { 8181 }, "/status", "TautulliNav", ProductOperationalHealthKind.HttpStatus),
            Product("Kometa", "Metadata automation", new[] { "kometa", "plex-meta-manager" }, new[] { "media server", "configuration", "network" }, new[] { "kometa.service" }, new[] { "kometa" }, Array.Empty<int>(), "/", "KometaNav", ProductOperationalHealthKind.RuntimeOnly, exited: true),

            Product("Jellyseerr", "Requests", new[] { "jellyseerr", "seerr" }, new[] { "media server", "Sonarr", "Radarr" }, new[] { "jellyseerr.service" }, new[] { "jellyseerr" }, new[] { 5055 }, "/api/v1/status", "MediaHubNav", ProductOperationalHealthKind.HttpStatus),
            Product("Overseerr", "Requests", new[] { "overseerr" }, new[] { "Plex", "Sonarr", "Radarr" }, new[] { "overseerr.service" }, new[] { "overseerr" }, new[] { 5055 }, "/api/v1/status", "MediaHubNav", ProductOperationalHealthKind.HttpStatus),
            Product("Ombi", "Requests", new[] { "ombi" }, new[] { "media server", "Sonarr", "Radarr" }, new[] { "ombi.service" }, new[] { "ombi" }, Array.Empty<int>(), "/", "MediaHubNav", ProductOperationalHealthKind.HttpStatus),
            Product("Petio", "Requests", new[] { "petio" }, new[] { "Plex", "Sonarr", "Radarr" }, new[] { "petio.service" }, new[] { "petio" }, Array.Empty<int>(), "/", "MediaHubNav", ProductOperationalHealthKind.HttpStatus),

            Product("SABnzbd", "Downloads", new[] { "sab", "sabnzbd" }, new[] { "network", "storage", "Usenet provider" }, new[] { "sabnzbdplus.service", "sabnzbd.service" }, new[] { "sabnzbd" }, new[] { 8080 }, "/api?mode=version&output=json", "SabnzbdNav", ProductOperationalHealthKind.HttpStatus),
            Product("qBittorrent", "Downloads", new[] { "qbittorrent", "qbit" }, new[] { "network", "storage" }, new[] { "qbittorrent-nox.service" }, new[] { "qbittorrent" }, new[] { 8080 }, "/api/v2/app/version", "QBittorrentNav", ProductOperationalHealthKind.HttpStatus),
            Product("NZBGet", "Downloads", new[] { "nzbget" }, new[] { "network", "storage", "Usenet provider" }, new[] { "nzbget.service" }, new[] { "nzbget" }, Array.Empty<int>(), "/", "MediaHubNav", ProductOperationalHealthKind.HttpStatus),
            Product("Transmission", "Downloads", new[] { "transmission" }, new[] { "network", "storage" }, new[] { "transmission-daemon.service" }, new[] { "transmission" }, Array.Empty<int>(), "/", "MediaHubNav", ProductOperationalHealthKind.HttpStatus),
            Product("Deluge", "Downloads", new[] { "deluge" }, new[] { "network", "storage" }, new[] { "deluged.service" }, new[] { "deluge" }, Array.Empty<int>(), "/", "MediaHubNav", ProductOperationalHealthKind.HttpStatus),
            Product("ruTorrent", "Downloads", new[] { "rutorrent", "rtorrent" }, new[] { "network", "storage" }, new[] { "rtorrent.service" }, new[] { "rutorrent", "rtorrent" }, Array.Empty<int>(), "/", "MediaHubNav", ProductOperationalHealthKind.HttpStatus),

            Product("DUMB", "Orchestration", new[] { "dumb" }, new[] { "Docker", "storage", "network" }, new[] { "dumb.service" }, new[] { "dumb" }, new[] { 3005 }, "/", "DumbNav", ProductOperationalHealthKind.HttpStatus),
            Product("Decypharr", "Processing", new[] { "decypharr" }, new[] { "debrid provider", "storage", "Arr applications" }, new[] { "decypharr.service" }, new[] { "decypharr" }, new[] { 8282 }, "/", "DecypharrNav", ProductOperationalHealthKind.HttpStatus),
            Product("Zurg", "Processing", new[] { "zurg" }, new[] { "debrid provider", "rclone", "storage" }, new[] { "zurg.service" }, new[] { "zurg" }, Array.Empty<int>(), "/", "ZurgNav", ProductOperationalHealthKind.HttpStatus),
            Product("Riven", "Processing", new[] { "riven" }, new[] { "debrid provider", "storage", "database" }, new[] { "riven.service" }, new[] { "riven" }, Array.Empty<int>(), "/", "MediaHubNav", ProductOperationalHealthKind.HttpStatus),
            Product("Tdarr", "Processing", new[] { "tdarr" }, new[] { "storage", "workers", "media server" }, new[] { "tdarr.service" }, new[] { "tdarr", "tdarr_server" }, Array.Empty<int>(), "/", "MediaHubNav", ProductOperationalHealthKind.HttpStatus),
            Product("FileFlows", "Processing", new[] { "fileflows" }, new[] { "storage", "workers" }, new[] { "fileflows.service" }, new[] { "fileflows" }, Array.Empty<int>(), "/", "MediaHubNav", ProductOperationalHealthKind.HttpStatus),
            Product("Unmanic", "Processing", new[] { "unmanic" }, new[] { "storage", "workers" }, new[] { "unmanic.service" }, new[] { "unmanic" }, Array.Empty<int>(), "/", "MediaHubNav", ProductOperationalHealthKind.HttpStatus),
            Product("Zilean", "Discovery", new[] { "zilean" }, new[] { "database", "network", "debrid stack" }, new[] { "zilean.service" }, new[] { "zilean" }, Array.Empty<int>(), "/", "MediaHubNav", ProductOperationalHealthKind.HttpStatus),
            Product("rclone", "Processing", new[] { "rclone" }, new[] { "remote provider", "network", "mount" }, new[] { "rclone.service" }, new[] { "rclone" }, Array.Empty<int>(), "/", "MediaHubNav", ProductOperationalHealthKind.RuntimeOnly),

            Product("Pi-hole", "DNS", new[] { "pihole", "pi-hole" }, new[] { "network", "upstream DNS", "FTL" }, new[] { "pihole-FTL.service" }, new[] { "pihole" }, new[] { 80 }, "/admin/api.php?status", "PiHoleNav", ProductOperationalHealthKind.DnsService, healthyTokens: new[] { "enabled", "listening", "active" }, verification: "Pi-hole FTL is listening and its blocking status is readable."),
            Product("AdGuard Home", "DNS", new[] { "adguard", "adguardhome", "adguard-home" }, new[] { "network", "upstream DNS" }, new[] { "AdGuardHome.service", "adguardhome.service" }, new[] { "adguardhome" }, new[] { 3000 }, "/control/status", "MediaHubNav", ProductOperationalHealthKind.DnsService, healthyTokens: new[] { "running", "enabled", "active" }, verification: "AdGuard Home answers its status endpoint and its DNS service remains active.")
        };
    }
}
