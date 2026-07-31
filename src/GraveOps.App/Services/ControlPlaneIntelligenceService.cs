using GraveOps.App.Models;

namespace GraveOps.App.Services;

/// <summary>
/// Environment-aware, provider-neutral intelligence. It ranks upstream dependencies
/// before downstream symptoms and never assumes a specific compose stack, mount path
/// or helper script.
/// </summary>
public sealed class ControlPlaneIntelligenceService
{
    private readonly AppServices _services;

    public ControlPlaneIntelligenceService(AppServices services) => _services = services;

    public async Task<ControlPlaneIntelligenceSnapshot> AnalyzeAsync(
        ServerProfile server,
        CancellationToken cancellationToken = default)
    {
        var snapshot = new ControlPlaneIntelligenceSnapshot { ServerName = server.Name };

        HostProbeResult? hostProbe = null;
        EnvironmentOverviewSnapshot? environment = null;
        MediaOperationsSnapshot? media = null;

        try
        {
            hostProbe = await _services.Hosts.Resolve(server).ProbeAsync(server, cancellationToken);
        }
        catch (Exception ex)
        {
            snapshot.OverallSeverity = "CRITICAL";
            snapshot.RootCause = "Host reachability";
            snapshot.Headline = $"{server.Name} is not reachable through its configured provider.";
            snapshot.Findings.Add(new ControlPlaneFinding
            {
                Severity = "CRITICAL",
                Component = server.Name,
                Problem = "Host provider probe failed.",
                Evidence = ex.Message,
                Impact = "Every application owned by this host can be blocked by host reachability.",
                NextStep = "Open Servers and verify network, credentials and transport before touching child services.",
                DeepLink = "page:Servers"
            });
            snapshot.Nodes.Add(new ControlPlaneNode
            {
                Key = "host",
                Component = "Host",
                State = "OFFLINE",
                Severity = "CRITICAL",
                Summary = ex.Message,
                Feeds = "storage, applications and all downstream workflows",
                DeepLink = "page:Servers"
            });
            return snapshot;
        }

        try { environment = await _services.Environment.GetSnapshotAsync(false, cancellationToken); }
        catch (Exception ex) { snapshot.ProbeNotes.Add("Environment snapshot unavailable: " + ex.Message); }

        try { media = await _services.MediaOps.GetSnapshotAsync(server, false, cancellationToken); }
        catch (Exception ex) { snapshot.ProbeNotes.Add("Detailed media telemetry unavailable: " + ex.Message); }

        var host = environment?.Hosts.FirstOrDefault(x => x.ServerId == server.Id);
        var impacts = environment?.Impacts.Where(x => x.ServerId == server.Id).ToList() ?? new();
        var apps = host?.Apps ?? new List<EnvironmentAppSnapshot>();

        AddNode(snapshot, "host", "Host", "READY", "HEALTHY",
            $"{hostProbe.HostName} | {hostProbe.OperatingSystem}",
            "", "storage and host-owned services", "page:Servers", "");

        AddNode(snapshot, "storage", "Storage",
            hostProbe.StorageRoots.Count > 0 ? "READY" : "UNKNOWN",
            hostProbe.StorageRoots.Count > 0 ? "HEALTHY" : "INFO",
            hostProbe.StorageRoots.Count > 0
                ? $"{hostProbe.StorageRoots.Count} storage root(s) visible."
                : "No storage roots were reported by this provider.",
            "Host", "Docker and application data paths", "page:Storage", "storage");

        if (hostProbe.Capabilities.HasFlag(HostCapability.Docker))
        {
            AddNode(snapshot, "docker", "Docker", "AVAILABLE", "HEALTHY",
                "Docker capability detected on the selected host.",
                "Host / Storage", "containerized applications", "page:Docker", "docker");
        }

        var prowlarr = apps.FirstOrDefault(x => x.Name.Equals("Prowlarr", StringComparison.OrdinalIgnoreCase));
        if (prowlarr is not null)
            AddAppNode(snapshot, prowlarr, "Discovery", "Host / network", "Sonarr, Radarr and Lidarr", "queues");

        var downloadApps = apps.Where(x =>
            x.Name.Equals("SABnzbd", StringComparison.OrdinalIgnoreCase) ||
            x.Name.Equals("qBittorrent", StringComparison.OrdinalIgnoreCase)).ToList();
        var downloadSeverity = Worst(downloadApps.Select(x => x.State));
        AddNode(snapshot, "downloads", "Downloads",
            downloadApps.Count == 0 ? "NOT CONFIGURED" : StateText(downloadSeverity),
            Severity(downloadSeverity),
            downloadApps.Count == 0
                ? "No verified download client is assigned to this host."
                : $"{downloadApps.Count} verified download client(s).",
            "Discovery / Arr", "import workflows", "page:Applications", "queues");

        var arrApps = apps.Where(x =>
            x.Name.Contains("Sonarr", StringComparison.OrdinalIgnoreCase) ||
            x.Name.Contains("Radarr", StringComparison.OrdinalIgnoreCase) ||
            x.Name.Equals("Lidarr", StringComparison.OrdinalIgnoreCase)).ToList();
        var arrSeverity = Worst(arrApps.Select(x => x.State));
        AddNode(snapshot, "arr", "Arr fleet",
            arrApps.Count == 0 ? "NOT CONFIGURED" : StateText(arrSeverity),
            Severity(arrSeverity),
            arrApps.Count == 0 ? "No verified Arr applications." : $"{arrApps.Count} Arr application(s) verified.",
            prowlarr is null ? "Host / Download clients" : "Prowlarr / Download clients",
            "import and media library availability", "page:Applications", "queues");

        var processing = apps.Where(x =>
            x.Name.Equals("Bazarr", StringComparison.OrdinalIgnoreCase) ||
            x.Name.Equals("Tdarr", StringComparison.OrdinalIgnoreCase) ||
            x.Name.Equals("Unpackerr", StringComparison.OrdinalIgnoreCase)).ToList();
        if (processing.Count > 0)
        {
            var state = Worst(processing.Select(x => x.State));
            AddNode(snapshot, "processing", "Processing", StateText(state), Severity(state),
                $"{processing.Count} downstream processing integration(s) verified.",
                "Arr / Downloads", "library readiness", "page:Applications", "");
        }

        var library = apps.FirstOrDefault(x =>
            x.Name.Equals("Plex", StringComparison.OrdinalIgnoreCase) ||
            x.Name.Equals("Jellyfin", StringComparison.OrdinalIgnoreCase) ||
            x.Name.Equals("Emby", StringComparison.OrdinalIgnoreCase));
        if (library is not null)
            AddAppNode(snapshot, library, "Library", "Storage / Import", "playback and users", "plex");

        foreach (var impact in impacts)
        {
            snapshot.Findings.Add(new ControlPlaneFinding
            {
                Severity = impact.State == EnvironmentHealthState.Offline ? "ERROR" : "WARNING",
                Component = impact.Component,
                Problem = impact.Detail,
                Evidence = impact.Impact,
                Impact = DependencyImpact(impact.Component),
                NextStep = RecommendedNext(impact.Component),
                DeepLink = $"page:{impact.PageKey}",
                DrillTarget = DrillTarget(impact.Component)
            });
        }

        if (media is not null)
        {
            foreach (var card in media.Apps
                         .Where(x => x.Health is AppHealthState.Degraded or AppHealthState.Busy or AppHealthState.Stale)
                         .Where(card => snapshot.Findings.All(f => !f.Component.Equals(card.Name, StringComparison.OrdinalIgnoreCase))))
            {
                snapshot.Findings.Add(new ControlPlaneFinding
                {
                    Severity = card.Health == AppHealthState.Degraded ? "WARNING" : "INFO",
                    Component = card.Name,
                    Problem = card.AttentionText,
                    Evidence = $"{card.HttpText}; {card.QueueText}; {card.IssuesText}",
                    Impact = DependencyImpact(card.Name),
                    NextStep = RecommendedNext(card.Name),
                    DeepLink = $"page:{EnvironmentImpactSnapshot.ResolvePageKey(card.Name)}",
                    DrillTarget = DrillTarget(card.Name)
                });
            }
        }

        snapshot.Findings = snapshot.Findings
            .OrderByDescending(x => x.Rank)
            .ThenBy(x => DependencyRank(x.Component))
            .ThenBy(x => x.Component)
            .ToList();

        var top = snapshot.Findings.FirstOrDefault();
        if (top is null)
        {
            snapshot.OverallSeverity = "HEALTHY";
            snapshot.RootCause = "No critical fault detected";
            snapshot.Headline = "No active provider, application or workflow findings are present.";
        }
        else
        {
            snapshot.OverallSeverity = top.Severity is "CRITICAL" or "ERROR" ? "ERROR" : "WARNING";
            snapshot.RootCause = top.Component;
            snapshot.Headline = $"Highest-priority finding: {top.Component} — {top.Problem}";
        }

        return snapshot;
    }

    private static void AddAppNode(
        ControlPlaneIntelligenceSnapshot snapshot,
        EnvironmentAppSnapshot app,
        string component,
        string dependsOn,
        string feeds,
        string drill)
        => AddNode(snapshot, component.ToLowerInvariant(), component, StateText(app.State), Severity(app.State),
            $"{app.Name}: {app.Detail}", dependsOn, feeds,
            $"page:{EnvironmentImpactSnapshot.ResolvePageKey(app.Name)}", drill);

    private static void AddNode(
        ControlPlaneIntelligenceSnapshot snapshot,
        string key,
        string component,
        string state,
        string severity,
        string summary,
        string dependsOn,
        string feeds,
        string deepLink,
        string drillTarget)
        => snapshot.Nodes.Add(new ControlPlaneNode
        {
            Key = key,
            Component = component,
            State = state,
            Severity = severity,
            Summary = summary,
            DependsOn = dependsOn,
            Feeds = feeds,
            DeepLink = deepLink,
            DrillTarget = drillTarget
        });

    private static EnvironmentHealthState Worst(IEnumerable<EnvironmentHealthState> states)
    {
        var list = states.ToList();
        if (list.Any(x => x == EnvironmentHealthState.Offline)) return EnvironmentHealthState.Offline;
        if (list.Any(x => x == EnvironmentHealthState.Attention)) return EnvironmentHealthState.Attention;
        if (list.Any(x => x == EnvironmentHealthState.Healthy)) return EnvironmentHealthState.Healthy;
        return EnvironmentHealthState.Unknown;
    }

    private static string StateText(EnvironmentHealthState state) => state switch
    {
        EnvironmentHealthState.Healthy => "READY",
        EnvironmentHealthState.Attention => "DEGRADED",
        EnvironmentHealthState.Offline => "OFFLINE",
        _ => "UNKNOWN"
    };

    private static string Severity(EnvironmentHealthState state) => state switch
    {
        EnvironmentHealthState.Healthy => "HEALTHY",
        EnvironmentHealthState.Attention => "WARNING",
        EnvironmentHealthState.Offline => "ERROR",
        _ => "INFO"
    };

    private static int DependencyRank(string component)
    {
        var name = component.ToLowerInvariant();
        if (name.Contains("host")) return 0;
        if (name.Contains("storage")) return 1;
        if (name.Contains("docker")) return 2;
        if (name.Contains("prowlarr")) return 3;
        if (name.Contains("sab") || name.Contains("qbittorrent")) return 4;
        if (name.Contains("sonarr") || name.Contains("radarr") || name.Contains("lidarr")) return 5;
        if (name.Contains("bazarr") || name.Contains("tdarr") || name.Contains("unpackerr")) return 6;
        if (name.Contains("plex") || name.Contains("jellyfin") || name.Contains("emby")) return 7;
        return 8;
    }

    private static string DependencyImpact(string component)
    {
        var name = component.ToLowerInvariant();
        if (name.Contains("prowlarr")) return "Indexer/discovery problems can cause multiple Arr services to fail to find or grab releases.";
        if (name.Contains("sab") || name.Contains("qbittorrent")) return "Queued work can be blocked between acquisition and import.";
        if (name.Contains("sonarr") || name.Contains("radarr") || name.Contains("lidarr")) return "Acquisition or import for this media type can be delayed or blocked.";
        if (name.Contains("bazarr")) return "Subtitle work may be delayed; acquisition itself should remain independent.";
        if (name.Contains("tdarr")) return "Post-import processing/transcoding may be delayed.";
        if (name.Contains("plex") || name.Contains("jellyfin") || name.Contains("emby")) return "Final library availability or playback can be affected.";
        return "Dependent workflows may be degraded.";
    }

    private static string RecommendedNext(string component)
    {
        var name = component.ToLowerInvariant();
        if (name.Contains("prowlarr")) return "Inspect indexer health first, then retest dependent Arr services.";
        if (name.Contains("sab") || name.Contains("qbittorrent")) return "Inspect queue, connection and free-space state before touching Arr services.";
        if (name.Contains("sonarr") || name.Contains("radarr") || name.Contains("lidarr")) return "Open queue/health detail and resolve the specific item or health message.";
        if (name.Contains("bazarr")) return "Resolve subtitle providers after media import is healthy.";
        if (name.Contains("tdarr")) return "Inspect processing queue and worker/node health.";
        if (name.Contains("plex") || name.Contains("jellyfin") || name.Contains("emby")) return "Validate storage/import dependencies before restarting the media server.";
        return "Open the owning component and inspect its detailed telemetry.";
    }

    private static string DrillTarget(string component)
    {
        var name = component.ToLowerInvariant();
        if (name.Contains("docker")) return "docker";
        if (name.Contains("storage")) return "storage";
        if (name.Contains("sonarr") || name.Contains("radarr") || name.Contains("lidarr") ||
            name.Contains("sab") || name.Contains("qbittorrent")) return "queues";
        if (name.Contains("plex")) return "plex";
        return "";
    }
}
