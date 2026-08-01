using System.Text.Json;

namespace GraveOps.Desktop.Linux;

public sealed record ModularApplicationDescriptor(
    string Name,
    string Summary,
    string PrimaryModule,
    string SecondaryModule,
    string OperationsLabel)
{
    public static ModularApplicationDescriptor For(
        string application)
    {
        var name = application.Trim();

        return name.ToLowerInvariant() switch
        {
            "plex" => new(
                "Plex",
                "Library availability, playback endpoint and session readiness.",
                "Playback readiness",
                "Library dependencies",
                "Open Plex"),
            "tautulli" => new(
                "Tautulli",
                "Playback analytics availability and owner context.",
                "Analytics readiness",
                "Plex dependency",
                "Open Tautulli"),
            "kometa" => new(
                "Kometa",
                "Library metadata automation and scheduled-run readiness.",
                "Automation readiness",
                "Library dependency",
                "Open Kometa"),
            "sabnzbd" => new(
                "SABnzbd",
                "Usenet download availability and queue ownership.",
                "Download readiness",
                "Queue ownership",
                "Open SABnzbd"),
            "qbittorrent" => new(
                "qBittorrent",
                "Torrent download availability and transfer ownership.",
                "Transfer readiness",
                "Queue ownership",
                "Open qBittorrent"),
            "decypharr" => new(
                "Decypharr",
                "Debrid processing, repair and downstream handoff readiness.",
                "Processing readiness",
                "Download handoff",
                "Open Decypharr"),
            "zurg" => new(
                "Zurg",
                "Debrid mount and library-path availability.",
                "Mount readiness",
                "Library dependency",
                "Open Zurg"),
            "dumb" => new(
                "DUMB",
                "Stack orchestration and application ownership.",
                "Stack readiness",
                "Owned applications",
                "Open DUMB"),
            _ => new(
                string.IsNullOrWhiteSpace(name)
                    ? "Application"
                    : name,
                "Verified application state and operational ownership.",
                "Application readiness",
                "Dependencies",
                $"Open {name}")
        };
    }
}

public sealed class ModularLayoutContract
{
    public string Version { get; set; } = "4.3.6";
    public bool ViewportFirst { get; set; } = true;
    public bool PageLevelScrollDisabled { get; set; } = true;
    public bool DirectAppsUseDedicatedWorkspace { get; set; } = true;
    public bool WindowsVisualParity { get; set; } = true;
    public bool SharedPageComposition { get; set; } = true;
    public int SidebarWidth { get; set; } = 260;
    public int ContentGap { get; set; } = 14;
    public int MetricStripHeight { get; set; } = 82;
    public int MinimumSupportedWidth { get; set; } = 1180;
    public int MinimumSupportedHeight { get; set; } = 760;

    public static ModularLayoutContract Default =>
        new();

    public string ToJson() =>
        JsonSerializer.Serialize(
            this,
            new JsonSerializerOptions
            {
                WriteIndented = true
            });
}
