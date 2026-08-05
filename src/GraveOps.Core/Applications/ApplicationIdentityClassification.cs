using System.Text.RegularExpressions;

namespace GraveOps.Core.Applications;

public static class ApplicationIdentityRoles
{
    public const string NativeApplication = "Native application";
    public const string EmbeddedApplication = "Embedded application";
    public const string CompatibilityInterface = "Compatibility interface";
    public const string SupportingService = "Supporting service";
    public const string DiscoveryCandidate = "Discovery candidate";

    public static IReadOnlyList<string> All { get; } =
        new[]
        {
            NativeApplication,
            EmbeddedApplication,
            CompatibilityInterface,
            SupportingService,
            DiscoveryCandidate
        };

    public static bool IsTopLevel(string? role) =>
        !string.Equals(
            role,
            CompatibilityInterface,
            StringComparison.OrdinalIgnoreCase) &&
        !string.Equals(
            role,
            SupportingService,
            StringComparison.OrdinalIgnoreCase);

    public static bool CanOwnHealth(string? role) =>
        string.Equals(
            role,
            NativeApplication,
            StringComparison.OrdinalIgnoreCase) ||
        string.Equals(
            role,
            EmbeddedApplication,
            StringComparison.OrdinalIgnoreCase);
}

public sealed record ApplicationProductDefinition(
    string Product,
    string Category,
    string[] ServiceUnits,
    string[] IdentityTokens,
    int[] DiscoveryPorts,
    string PathSuffix = "");

public static class ApplicationIdentityCatalog
{
    public static IReadOnlyList<ApplicationProductDefinition>
        Definitions { get; } =
        new[]
        {
            new ApplicationProductDefinition(
                "DUMB",
                "Orchestration",
                Array.Empty<string>(),
                new[] { "iampuid0/dumb", "dumb" },
                new[] { 3005, 5000 }),
            new ApplicationProductDefinition(
                "Plex",
                "Library",
                new[] { "plexmediaserver.service" },
                new[] { "plexmediaserver", "plexinc/pms-docker", "plex" },
                new[] { 32400 },
                "/web"),
            new ApplicationProductDefinition(
                "Jellyfin",
                "Library",
                new[] { "jellyfin.service" },
                new[] { "jellyfin" },
                new[] { 8096 }),
            new ApplicationProductDefinition(
                "Emby",
                "Library",
                new[] { "emby-server.service" },
                new[] { "embyserver", "emby-server", "emby" },
                new[] { 8096 }),
            new ApplicationProductDefinition(
                "Tautulli",
                "Library",
                new[] { "tautulli.service" },
                new[] { "tautulli" },
                new[] { 8181 }),
            new ApplicationProductDefinition(
                "Kometa",
                "Library",
                new[] { "kometa.service" },
                new[] { "kometateam/kometa", "plex-meta-manager", "kometa" },
                Array.Empty<int>()),
            new ApplicationProductDefinition(
                "Sonarr",
                "Acquisition",
                new[] { "sonarr.service" },
                new[] { "linuxserver/sonarr", "sonarr" },
                new[] { 8989, 8990 }),
            new ApplicationProductDefinition(
                "Radarr",
                "Acquisition",
                new[] { "radarr.service" },
                new[] { "linuxserver/radarr", "radarr" },
                new[] { 7878, 7879 }),
            new ApplicationProductDefinition(
                "Lidarr",
                "Acquisition",
                new[] { "lidarr.service" },
                new[] { "linuxserver/lidarr", "lidarr" },
                new[] { 8686 }),
            new ApplicationProductDefinition(
                "Prowlarr",
                "Acquisition",
                new[] { "prowlarr.service" },
                new[] { "linuxserver/prowlarr", "prowlarr" },
                new[] { 9696 }),
            new ApplicationProductDefinition(
                "Readarr",
                "Acquisition",
                new[] { "readarr.service" },
                new[] { "linuxserver/readarr", "readarr" },
                new[] { 8787 }),
            new ApplicationProductDefinition(
                "Whisparr",
                "Acquisition",
                new[] { "whisparr.service" },
                new[] { "linuxserver/whisparr", "whisparr" },
                new[] { 6969 }),
            new ApplicationProductDefinition(
                "Mylar3",
                "Acquisition",
                new[] { "mylar3.service", "mylar.service" },
                new[] { "linuxserver/mylar3", "mylar3", "mylar" },
                new[] { 8090 }),
            new ApplicationProductDefinition(
                "Bazarr",
                "Processing",
                new[] { "bazarr.service" },
                new[] { "linuxserver/bazarr", "bazarr" },
                new[] { 6767 }),
            new ApplicationProductDefinition(
                "Seerr",
                "Requests",
                Array.Empty<string>(),
                new[] { "jellyseerr", "overseerr", "seerr" },
                new[] { 5055 }),
            new ApplicationProductDefinition(
                "SABnzbd",
                "Acquisition",
                new[] { "sabnzbdplus.service", "sabnzbd.service" },
                new[] { "linuxserver/sabnzbd", "sabnzbdplus", "sabnzbd" },
                new[] { 8080 }),
            new ApplicationProductDefinition(
                "qBittorrent",
                "Acquisition",
                new[] { "qbittorrent.service", "qbittorrent-nox.service" },
                new[] { "linuxserver/qbittorrent", "qbittorrent-nox", "qbittorrent" },
                new[] { 8080, 8081 }),
            new ApplicationProductDefinition(
                "Recyclarr",
                "Processing",
                new[] { "recyclarr.service" },
                new[] { "ghcr.io/recyclarr/recyclarr", "recyclarr" },
                Array.Empty<int>()),
            new ApplicationProductDefinition(
                "Configarr",
                "Processing",
                new[] { "configarr.service" },
                new[] { "configarr" },
                Array.Empty<int>()),
            new ApplicationProductDefinition(
                "Profilarr",
                "Processing",
                new[] { "profilarr.service" },
                new[] { "profilarr" },
                new[] { 6868 }),
            new ApplicationProductDefinition(
                "autobrr",
                "Processing",
                new[] { "autobrr.service" },
                new[] { "autobrr" },
                new[] { 7474 }),
            new ApplicationProductDefinition(
                "Unpackerr",
                "Processing",
                new[] { "unpackerr.service" },
                new[] { "golift/unpackerr", "unpackerr" },
                new[] { 5656 }),
            new ApplicationProductDefinition(
                "Cleanuparr",
                "Processing",
                new[] { "cleanuparr.service" },
                new[] { "cleanuparr" },
                new[] { 11011 }),
            new ApplicationProductDefinition(
                "Tdarr",
                "Processing",
                new[] { "tdarr.service" },
                new[] { "haveagitgat/tdarr", "tdarr_server", "tdarr" },
                new[] { 8265, 8266 }),
            new ApplicationProductDefinition(
                "Maintainerr",
                "Processing",
                new[] { "maintainerr.service" },
                new[] { "jorenn92/maintainerr", "maintainerr" },
                new[] { 6246 }),
            new ApplicationProductDefinition(
                "Pi-hole",
                "Network",
                new[] { "pihole-ftl.service" },
                new[] { "pihole/pihole", "pihole-ftl", "pi-hole", "pihole" },
                new[] { 80, 443 }),
            new ApplicationProductDefinition(
                "Decypharr",
                "Processing",
                Array.Empty<string>(),
                new[] { "decypharr" },
                new[] { 8282 }),
            new ApplicationProductDefinition(
                "Zurg",
                "Processing",
                new[] { "zurg.service" },
                new[] { "zurg" },
                new[] { 18080 }),
            new ApplicationProductDefinition(
                "Zilean",
                "Processing",
                new[] { "zilean.service" },
                new[] { "zilean" },
                Array.Empty<int>()),
            new ApplicationProductDefinition(
                "FlareSolverr",
                "Supporting service",
                Array.Empty<string>(),
                new[] { "flaresolverr" },
                new[] { 8191 })
        };

    public static IReadOnlyList<string> ProductNames { get; } =
        Definitions
            .Select(item => item.Product)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(item => item)
            .ToArray();

    public static string DefaultCategory(string product) =>
        Definitions.FirstOrDefault(item =>
            item.Product.Equals(
                product,
                StringComparison.OrdinalIgnoreCase))
            ?.Category ??
        "Supporting service";

    public static ApplicationProductDefinition? Find(
        string? product) =>
        string.IsNullOrWhiteSpace(product)
            ? null
            : Definitions.FirstOrDefault(item =>
                item.Product.Equals(
                    product.Trim(),
                    StringComparison.OrdinalIgnoreCase));
}

public sealed record ApplicationIdentityEvidence(
    string ProductHint,
    string IdentityRoleHint,
    string RuntimeKind,
    string Protocol,
    string SourceName,
    string Evidence,
    bool HasManagementEndpoint,
    bool IsVerified = true);

public sealed record ApplicationIdentityClassification(
    string ProductId,
    string Category,
    ApplicationRole Role,
    ApplicationRuntimeKind Runtime,
    int Confidence);

public static class ApplicationIdentityClassifier
{
    public static ApplicationIdentityClassification Classify(
        ApplicationIdentityEvidence evidence)
    {
        ArgumentNullException.ThrowIfNull(evidence);

        var product =
            ResolveProduct(evidence);
        var runtime =
            ResolveRuntime(evidence);
        var role =
            ResolveRole(
                product.ProductId,
                evidence,
                runtime);

        var confidence =
            Math.Clamp(
                product.Confidence +
                (evidence.IsVerified ? 5 : 0),
                0,
                100);

        return new ApplicationIdentityClassification(
            product.ProductId,
            product.Category,
            role,
            runtime,
            confidence);
    }

    private static ProductResolution ResolveProduct(
        ApplicationIdentityEvidence evidence)
    {
        var explicitDefinition =
            ApplicationIdentityCatalog.Find(
                evidence.ProductHint);

        if (explicitDefinition is not null)
        {
            return new ProductResolution(
                explicitDefinition.Product,
                explicitDefinition.Category,
                100);
        }

        var candidates =
            new[]
            {
                (
                    Value:
                        evidence.ProductHint,
                    Confidence:
                        95),
                (
                    Value:
                        evidence.SourceName,
                    Confidence:
                        90),
                (
                    Value:
                        evidence.Evidence,
                    Confidence:
                        75),
                (
                    Value:
                        evidence.Protocol,
                    Confidence:
                        60),
                (
                    Value:
                        evidence.RuntimeKind,
                    Confidence:
                        45)
            };

        foreach (var candidate in candidates)
        {
            if (string.IsNullOrWhiteSpace(
                    candidate.Value))
            {
                continue;
            }

            var definition =
                ApplicationIdentityCatalog.Definitions
                    .FirstOrDefault(item =>
                        item.Product.Equals(
                            candidate.Value.Trim(),
                            StringComparison.OrdinalIgnoreCase) ||
                        item.IdentityTokens.Any(token =>
                            MatchesToken(
                                candidate.Value,
                                token)) ||
                        item.ServiceUnits.Any(unit =>
                            candidate.Value.Contains(
                                unit,
                                StringComparison.OrdinalIgnoreCase)));

            if (definition is not null)
            {
                return new ProductResolution(
                    definition.Product,
                    definition.Category,
                    candidate.Confidence);
            }
        }

        var fallback =
            string.IsNullOrWhiteSpace(
                evidence.ProductHint)
                ? "Unknown"
                : evidence.ProductHint.Trim();

        return new ProductResolution(
            fallback,
            ApplicationIdentityCatalog.DefaultCategory(
                fallback),
            20);
    }

    private static ApplicationRuntimeKind ResolveRuntime(
        ApplicationIdentityEvidence evidence)
    {
        var text =
            CombinedEvidence(evidence);

        if (ContainsAny(
                text,
                "docker",
                "container",
                "compose"))
        {
            return ApplicationRuntimeKind.Container;
        }

        if (ContainsAny(
                text,
                "windows service",
                "service control manager",
                "win32_service"))
        {
            return ApplicationRuntimeKind.WindowsService;
        }

        if (ContainsAny(
                text,
                "systemd",
                ".service",
                "systemctl"))
        {
            return ApplicationRuntimeKind.SystemdService;
        }

        if (ContainsAny(
                text,
                "native process",
                "desktop process",
                "desktop client",
                "executable",
                ".exe") ||
            evidence.IdentityRoleHint.Equals(
                ApplicationIdentityRoles.CompatibilityInterface,
                StringComparison.OrdinalIgnoreCase))
        {
            return ApplicationRuntimeKind.NativeProcess;
        }

        if (evidence.HasManagementEndpoint ||
            ContainsAny(
                text,
                "remote api",
                "web api",
                "web ui",
                "webui",
                "http endpoint",
                "https endpoint"))
        {
            return ApplicationRuntimeKind.RemoteApi;
        }

        if (ContainsAny(
                text,
                "native"))
        {
            return ApplicationRuntimeKind.NativeProcess;
        }

        return ApplicationRuntimeKind.Unknown;
    }

    private static ApplicationRole ResolveRole(
        string productId,
        ApplicationIdentityEvidence evidence,
        ApplicationRuntimeKind runtime)
    {
        var roleHint =
            evidence.IdentityRoleHint?.Trim() ??
            string.Empty;

        if (roleHint.Equals(
                ApplicationIdentityRoles.SupportingService,
                StringComparison.OrdinalIgnoreCase))
        {
            return ApplicationRole.Service;
        }

        if (roleHint.Equals(
                ApplicationIdentityRoles.CompatibilityInterface,
                StringComparison.OrdinalIgnoreCase))
        {
            return ApplicationRole.DesktopClient;
        }

        var text =
            CombinedEvidence(evidence);

        if (IsLibraryServer(productId))
        {
            if (IsPlexDesktop(
                    productId,
                    text,
                    runtime,
                    evidence.HasManagementEndpoint))
            {
                return ApplicationRole.DesktopClient;
            }

            return ApplicationRole.Server;
        }

        if (productId.Equals(
                "qBittorrent",
                StringComparison.OrdinalIgnoreCase))
        {
            if (IsQBittorrentDesktop(
                    text,
                    runtime,
                    evidence.HasManagementEndpoint))
            {
                return ApplicationRole.DesktopClient;
            }

            if (evidence.HasManagementEndpoint ||
                runtime is
                    ApplicationRuntimeKind.Container or
                    ApplicationRuntimeKind.RemoteApi or
                    ApplicationRuntimeKind.SystemdService or
                    ApplicationRuntimeKind.WindowsService ||
                ContainsAny(
                    text,
                    "web ui",
                    "webui",
                    "remote api",
                    "qbittorrent-nox",
                    "qbittorrent nox"))
            {
                return ApplicationRole.WebApplication;
            }

            return runtime ==
                    ApplicationRuntimeKind.NativeProcess
                ? ApplicationRole.DesktopClient
                : ApplicationRole.Unknown;
        }

        if (roleHint.Equals(
                ApplicationIdentityRoles.DiscoveryCandidate,
                StringComparison.OrdinalIgnoreCase))
        {
            return evidence.HasManagementEndpoint
                ? ApplicationRole.WebApplication
                : ApplicationRole.Unknown;
        }

        if (roleHint.Equals(
                ApplicationIdentityRoles.NativeApplication,
                StringComparison.OrdinalIgnoreCase) ||
            roleHint.Equals(
                ApplicationIdentityRoles.EmbeddedApplication,
                StringComparison.OrdinalIgnoreCase))
        {
            return ApplicationRole.WebApplication;
        }

        if (runtime is
            ApplicationRuntimeKind.SystemdService or
            ApplicationRuntimeKind.WindowsService)
        {
            return ApplicationRole.Service;
        }

        if (runtime is
            ApplicationRuntimeKind.Container or
            ApplicationRuntimeKind.RemoteApi)
        {
            return ApplicationRole.WebApplication;
        }

        if (runtime ==
            ApplicationRuntimeKind.NativeProcess)
        {
            return ApplicationRole.DesktopClient;
        }

        return ApplicationRole.Unknown;
    }

    private static bool IsLibraryServer(
        string productId) =>
        productId.Equals(
            "Plex",
            StringComparison.OrdinalIgnoreCase) ||
        productId.Equals(
            "Jellyfin",
            StringComparison.OrdinalIgnoreCase) ||
        productId.Equals(
            "Emby",
            StringComparison.OrdinalIgnoreCase);

    private static bool IsPlexDesktop(
        string productId,
        string text,
        ApplicationRuntimeKind runtime,
        bool hasManagementEndpoint)
    {
        if (!productId.Equals(
                "Plex",
                StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (ContainsAny(
                text,
                "plex media server",
                "plexmediaserver",
                "pms-docker"))
        {
            return false;
        }

        if (ContainsAny(
                text,
                "plex desktop",
                "plex htpc",
                "plex.exe",
                "desktop client"))
        {
            return true;
        }

        return runtime ==
                ApplicationRuntimeKind.NativeProcess &&
            !hasManagementEndpoint;
    }

    private static bool IsQBittorrentDesktop(
        string text,
        ApplicationRuntimeKind runtime,
        bool hasManagementEndpoint)
    {
        if (ContainsAny(
                text,
                "qbittorrent-nox",
                "qbittorrent nox",
                "web ui",
                "webui",
                "remote api"))
        {
            return false;
        }

        if (ContainsAny(
                text,
                "qbittorrent.exe",
                "desktop client",
                "desktop process",
                "graphical client",
                "gui application"))
        {
            return true;
        }

        return runtime ==
                ApplicationRuntimeKind.NativeProcess &&
            !hasManagementEndpoint;
    }

    private static string CombinedEvidence(
        ApplicationIdentityEvidence evidence) =>
        string.Join(
            " ",
            new[]
            {
                evidence.ProductHint,
                evidence.IdentityRoleHint,
                evidence.RuntimeKind,
                evidence.Protocol,
                evidence.SourceName,
                evidence.Evidence
            }.Where(value =>
                !string.IsNullOrWhiteSpace(value)))
            .ToLowerInvariant();

    private static bool ContainsAny(
        string value,
        params string[] candidates) =>
        candidates.Any(candidate =>
            value.Contains(
                candidate,
                StringComparison.OrdinalIgnoreCase));

    private static bool MatchesToken(
        string? value,
        string token)
    {
        if (string.IsNullOrWhiteSpace(value) ||
            string.IsNullOrWhiteSpace(token))
        {
            return false;
        }

        return Regex.IsMatch(
            value,
            $"(^|[^a-z0-9]){Regex.Escape(token)}([^a-z0-9]|$)",
            RegexOptions.IgnoreCase |
            RegexOptions.CultureInvariant);
    }

    private sealed record ProductResolution(
        string ProductId,
        string Category,
        int Confidence);
}
