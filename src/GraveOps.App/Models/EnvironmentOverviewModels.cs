namespace GraveOps.App.Models;

public enum EnvironmentHealthState
{
    Unknown = 0,
    Healthy = 1,
    Attention = 2,
    Offline = 3
}

public sealed class EnvironmentOverviewSnapshot
{
    public DateTimeOffset SampledAt { get; init; } = DateTimeOffset.Now;
    public EnvironmentHealthState State { get; init; } = EnvironmentHealthState.Unknown;
    public List<EnvironmentHostSnapshot> Hosts { get; init; } = new();

    public int HostCount => Hosts.Count;
    public int OnlineHostCount => Hosts.Count(x => x.State != EnvironmentHealthState.Offline);
    public int AttentionHostCount => Hosts.Count(x => x.State is EnvironmentHealthState.Attention or EnvironmentHealthState.Offline);
    public int VerifiedAppCount => Hosts.Sum(x => x.Apps.Count);
    public int HealthyAppCount => Hosts.Sum(x => x.Apps.Count(a => a.State == EnvironmentHealthState.Healthy));
    public int AttentionAppCount => Hosts.Sum(x => x.Apps.Count(a => a.State is EnvironmentHealthState.Attention or EnvironmentHealthState.Offline));

    public List<EnvironmentImpactSnapshot> Impacts => Hosts
        .SelectMany(host =>
        {
            var items = new List<EnvironmentImpactSnapshot>();

            if (host.State == EnvironmentHealthState.Offline)
            {
                items.Add(new EnvironmentImpactSnapshot
                {
                    ServerId = host.ServerId,
                    HostName = host.Name,
                    Component = host.Name,
                    Category = "Host",
                    State = host.State,
                    Detail = host.Detail,
                    Impact = host.Apps.Count == 0
                        ? "Host operations are unavailable."
                        : $"{host.Apps.Count} verified application(s) are blocked by host reachability.",
                    PageKey = "Servers"
                });
            }

            items.AddRange(host.Apps
                .Where(app => app.State is EnvironmentHealthState.Attention or EnvironmentHealthState.Offline)
                .Select(app => new EnvironmentImpactSnapshot
                {
                    ServerId = host.ServerId,
                    HostName = host.Name,
                    AppId = app.AppId,
                    Component = app.Name,
                    Category = app.Category,
                    State = app.State,
                    Detail = app.Detail,
                    Impact = app.State == EnvironmentHealthState.Offline
                        ? $"{app.Name} is unavailable on {host.Name}; dependent workflows may be blocked."
                        : $"{app.Name} on {host.Name} needs review; its {app.Category.ToLowerInvariant()} workflow may be degraded.",
                    PageKey = EnvironmentImpactSnapshot.ResolvePageKey(app.Name)
                }));

            return items;
        })
        .OrderByDescending(x => x.State == EnvironmentHealthState.Offline)
        .ThenBy(x => x.HostName)
        .ThenBy(x => x.Component)
        .ToList();
}


public sealed class EnvironmentHostSnapshot
{
    public Guid ServerId { get; init; }
    public string Name { get; init; } = "Host";
    public HostConnectionKind ConnectionKind { get; init; }
    public HostPlatform Platform { get; init; } = HostPlatform.Unknown;
    public string PlatformText { get; init; } = "Unknown";
    public string ConnectionText { get; init; } = "Unknown";
    public EnvironmentHealthState State { get; set; } = EnvironmentHealthState.Unknown;
    public string Detail { get; set; } = "";
    public int StorageRootCount { get; init; }
    public List<EnvironmentAppSnapshot> Apps { get; init; } = new();
}

public sealed class EnvironmentAppSnapshot
{
    public Guid AppId { get; init; }
    public string Name { get; init; } = "Application";
    public string Category { get; init; } = "Media";
    public EnvironmentHealthState State { get; init; } = EnvironmentHealthState.Unknown;
    public string Detail { get; init; } = "";
}

public sealed class EnvironmentImpactSnapshot
{
    public Guid ServerId { get; init; }
    public string HostName { get; init; } = "Host";
    public Guid? AppId { get; init; }
    public string Component { get; init; } = "Component";
    public string Category { get; init; } = "Environment";
    public EnvironmentHealthState State { get; init; } = EnvironmentHealthState.Unknown;
    public string Detail { get; init; } = "";
    public string Impact { get; init; } = "";
    public string PageKey { get; init; } = "Dashboard";

    public static string ResolvePageKey(string name) =>
        name.Trim().ToLowerInvariant() switch
        {
            "plex" => "Plex",
            "sonarr" or "sonarr debrid" => "Sonarr",
            "radarr" or "radarr debrid" => "Radarr",
            "lidarr" => "Lidarr",
            "prowlarr" => "Prowlarr",
            "bazarr" => "Bazarr",
            "seerr" or "overseerr" or "jellyseerr" => "Seerr",
            "tautulli" => "Tautulli",
            "kometa" or "plex meta manager" => "Kometa",
            "recyclarr" => "Recyclarr",
            "profilarr" => "Profilarr",
            "autobrr" => "autobrr",
            "unpackerr" => "Unpackerr",
            "cleanuparr" => "Cleanuparr",
            "tdarr" => "Tdarr",
            "maintainerr" => "Maintainerr",
            "sabnzbd" => "SABnzbd",
            "qbittorrent" => "qBittorrent",
            "docker" => "Docker",
            "pi-hole" or "pihole" => "PiHole",
            _ => "Applications"
        };
}
