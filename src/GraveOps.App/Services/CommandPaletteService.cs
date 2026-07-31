using GraveOps.App.Models;

namespace GraveOps.App.Services;

public sealed class CommandPaletteService
{
    private readonly AppServices _services;

    private static readonly (string Key, string Title, string Subtitle)[] Pages =
    {
        ("Dashboard", "Dashboard", "Server health and overview"),
        ("Intelligence", "Intelligence", "Root cause, dependencies and contextual next actions"),
        ("Lifecycle", "Media Lifecycle", "Track active media across acquisition, download, import, processing and library stages"),
        ("History", "History & Incidents", "Fleet health transitions, GraveOps activity and incident replay"),
        ("Servers", "Servers", "Local and remote host profiles"),
        ("Applications", "Media Hub", "Fleet health, launchers and all media applications"),
        ("Plex", "Plex", "Playback health, sessions, logs and Plex operations"),
        ("Tautulli", "Tautulli", "Plex analytics, activity and history"),
        ("Kometa", "Kometa", "Plex metadata, collections, overlays and playlists"),
        ("Sonarr", "Sonarr", "Sonarr + Sonarr Debrid health, queues and tools"),
        ("Radarr", "Radarr", "Radarr + Radarr Debrid health, queues and tools"),
        ("Lidarr", "Lidarr", "Music acquisition health, queues and tools"),
        ("Prowlarr", "Prowlarr", "Indexer health and diagnostics"),
        ("Bazarr", "Bazarr", "Subtitle automation health and owner routing"),
        ("Seerr", "Seerr", "Media request service health and workflow ownership"),
        ("SABnzbd", "SABnzbd", "Usenet queue, progress, remaining time and recent history"),
        ("qBittorrent", "qBittorrent", "Torrent analytics, progress, ETA, seeding and protected local API telemetry"),
        ("Recyclarr", "Recyclarr", "TRaSH-backed quality policy and safe preview operations"),
        ("Profilarr", "Profilarr", "Sonarr/Radarr profile and configuration management"),
        ("autobrr", "autobrr", "IRC/RSS release automation and filtering"),
        ("Unpackerr", "Unpackerr", "Archive extraction pipeline health"),
        ("Cleanuparr", "Cleanuparr", "Queue cleanup and replacement-search automation"),
        ("Tdarr", "Tdarr", "Distributed media processing and transcoding health"),
        ("Maintainerr", "Maintainerr", "Media lifecycle and retention automation health"),
        ("Terminal", "Terminal", "PowerShell, CMD and SSH tabs"),
        ("Services", "Services & Actions", "Operational commands and recovery actions"),
        ("Docker", "Docker", "Containers, status and logs"),
        ("Storage", "Storage", "Drives, capacity, SMART and mounts"),
        ("PiHole", "Pi-hole", "DNS status and controls"),
        ("Backups", "Backups", "Provider-neutral backup readiness, schedules and protected actions"),
        ("Logs", "Logs", "Central log viewer"),
        ("Files", "Files / SFTP", "Remote file browser"),
        ("Scripts", "Script Library", "Saved commands and scripts"),
        ("Settings", "Settings", "GraveOps preferences"),
        ("Updates", "Update Center", "Read-only host and application update inventory")
    };

    public CommandPaletteService(AppServices services) => _services = services;

    public List<SearchEntry> Search(string query, int max = 40)
    {
        query = (query ?? "").Trim();
        var all = BuildIndex();
        if (query.Length == 0) return all.Take(max).ToList();

        return all
            .Select(x => new { Item = x, Score = Score(x, query) })
            .Where(x => x.Score > 0)
            .OrderByDescending(x => x.Score)
            .ThenBy(x => x.Item.Title)
            .Take(max)
            .Select(x => x.Item)
            .ToList();
    }

    public SearchEntry? ResolveSemanticKey(string semanticKey)
    {
        if (string.IsNullOrWhiteSpace(semanticKey)) return null;
        return BuildIndex().FirstOrDefault(x => string.Equals(x.Key, semanticKey, StringComparison.OrdinalIgnoreCase));
    }

    private List<SearchEntry> BuildIndex()
    {
        var result = new List<SearchEntry>();

        result.AddRange(Pages
            .Where(p => IsPageAvailable(p.Key))
            .Select(p => new SearchEntry
            {
                Key = $"page:{p.Key}", Kind = SearchItemKind.Page, Title = p.Title, Subtitle = p.Subtitle
            }));

        result.AddRange(_services.Config.Current.Servers.Select(s => new SearchEntry
        {
            Key = $"server:{s.Id}", Kind = SearchItemKind.Server, Title = s.Name, Subtitle = $"{s.Role} - {s.Host}"
        }));

        var apps = _services.Config.Current.Applications
            .Where(a => a.DiscoveryVerified || _services.Integrations.Find(a.Name) is null)
            .ToArray();

        result.AddRange(apps.Select(a =>
        {
            var owner = a.ServerId is { } id
                ? _services.Config.Current.Servers.FirstOrDefault(x => x.Id == id)
                : null;
            return new SearchEntry
            {
                Key = $"app:{a.Name}",
                Kind = SearchItemKind.Application,
                Title = a.Name,
                Subtitle = $"{a.Category} application on {owner?.Name ?? "unassigned host"}"
            };
        }));

        result.AddRange(_services.Config.Current.Actions.Select(a => new SearchEntry
        {
            Key = $"action:{a.Name}", Kind = SearchItemKind.Action, Title = a.Name, Subtitle = $"{a.Category} - {a.Risk}"
        }));

        result.Add(new SearchEntry { Key = "setting:maintenance", Kind = SearchItemKind.Setting, Title = "Maintenance Mode", Subtitle = "Suppress expected alerts while maintenance is in progress" });
        result.Add(new SearchEntry { Key = "setting:export", Kind = SearchItemKind.Setting, Title = "Export GraveOps Profile", Subtitle = "Configuration export without credentials" });
        result.Add(new SearchEntry { Key = "setting:setup", Kind = SearchItemKind.Setting, Title = "Setup Assistant", Subtitle = "Add this Windows PC, another Windows PC, or a Linux server" });

        return result;
    }

    private bool IsPageAvailable(string key)
    {
        var server = _services.Context.Current ?? _services.Config.GetSelectedServer();
        var verifiedFleetApps = _services.Config.Current.Applications
            .Where(a => a.DiscoveryVerified)
            .ToArray();
        var currentApps = server is null
            ? Array.Empty<ManagedApp>()
            : verifiedFleetApps.Where(a => a.ServerId == server.Id).ToArray();

        bool FleetHas(string name) => verifiedFleetApps.Any(x => x.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
        bool CurrentHas(string name) => currentApps.Any(x => x.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
        bool HasModule(string name) => server?.EnabledModules.Any(x => x.Equals(name, StringComparison.OrdinalIgnoreCase)) == true;
        var remoteLinux = server?.ConnectionKind == HostConnectionKind.RemoteLinux;

        return key switch
        {
            "Applications" => verifiedFleetApps.Length > 0,
            "Plex" => FleetHas("Plex"),
            "Tautulli" => FleetHas("Tautulli"),
            "Kometa" => FleetHas("Kometa"),
            "Sonarr" => FleetHas("Sonarr"),
            "Radarr" => FleetHas("Radarr"),
            "Lidarr" => FleetHas("Lidarr"),
            "Prowlarr" => FleetHas("Prowlarr"),
            "Bazarr" => FleetHas("Bazarr"),
            "Seerr" => FleetHas("Seerr"),
            "SABnzbd" => FleetHas("SABnzbd"),
            "qBittorrent" => FleetHas("qBittorrent"),
            "Recyclarr" => FleetHas("Recyclarr"),
            "Profilarr" => FleetHas("Profilarr"),
            "autobrr" => FleetHas("autobrr"),
            "Unpackerr" => FleetHas("Unpackerr"),
            "Cleanuparr" => FleetHas("Cleanuparr"),
            "Tdarr" => FleetHas("Tdarr"),
            "Maintainerr" => FleetHas("Maintainerr"),
            "Services" => server is not null,
            "Docker" => server is not null && HasModule("Docker"),
            "Storage" => server is not null,
            "PiHole" => remoteLinux && (CurrentHas("Pi-hole") || server?.Role.Contains("Pi-hole", StringComparison.OrdinalIgnoreCase) == true),
            "Backups" => server is not null,
            "Logs" or "Updates" => server is not null,
            "Files" or "Scripts" => remoteLinux,
            _ => true
        };
    }

    private static int Score(SearchEntry item, string query)
    {
        var title = item.Title;
        var subtitle = item.Subtitle;
        if (title.Equals(query, StringComparison.OrdinalIgnoreCase)) return 100;
        if (title.StartsWith(query, StringComparison.OrdinalIgnoreCase)) return 80;
        if (title.Contains(query, StringComparison.OrdinalIgnoreCase)) return 60;
        if (subtitle.Contains(query, StringComparison.OrdinalIgnoreCase)) return 35;
        return 0;
    }
}