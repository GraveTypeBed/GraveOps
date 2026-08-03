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
    public string Version { get; set; } = "4.7.0";
    public bool ViewportFirst { get; set; } = true;
    public bool PageLevelScrollDisabled { get; set; } = false;
    public bool DirectAppsUseDedicatedWorkspace { get; set; } = true;
    public bool WindowsVisualParity { get; set; } = true;
    public bool SharedPageComposition { get; set; } = true;
    public bool UploadedWindowsReferenceSource { get; set; } = true;
    public bool ReferenceShellAndDashboard { get; set; } = true;
    public bool DashboardVisualCalibration { get; set; } = true;
    public bool DesignedEmptyStates { get; set; } = true;
    public bool SubtleDashboardSelection { get; set; } = true;
    public bool WindowsArrWorkspaceReconstruction { get; set; } = true;
    public bool SharedArrPageComposition { get; set; } = true;
    public bool DenseArrQueueTables { get; set; } = true;
    public bool SubtleGlobalListSelection { get; set; } = true;
    public bool WindowsDownloadClientWorkspace { get; set; } = true;
    public bool NativeDownloadClientTelemetry { get; set; } = true;
    public bool ProtectedLocalClientProbes { get; set; } = true;
    public bool DenseDownloadClientTables { get; set; } = true;
    public bool ClientSpecificDownloadSchemas { get; set; } = true;
    public bool FixedDownloadColumnGeometry { get; set; } = true;
    public bool DownloadNameTooltips { get; set; } = true;
    public bool DownloadUnitNormalization { get; set; } = true;
    public bool WindowsMediaHubWorkspace { get; set; } = true;
    public bool DetectedMediaLauncherSettings { get; set; } = true;
    public bool DedicatedPlexWorkspace { get; set; } = true;
    public bool ProtectedPlexSessionTelemetry { get; set; } = true;
    public bool GuardedPlexRestart { get; set; } = true;
    public bool PersistentPlexSecretFile { get; set; } = true;
    public bool PrivilegeFreePlexProbe { get; set; } = true;
    public bool PlexProbeNoiseSuppression { get; set; } = true;
    public bool PlexSessionBandwidthParsing { get; set; } = true;
    public bool PlexForegroundLivePolling { get; set; } = true;
    public bool PlexBackgroundPolling { get; set; } = true;
    public bool PlexMinimizedPolling { get; set; } = true;
    public bool PlexSnapshotFailureRetention { get; set; } = true;
    public bool SharedPlexMediaHubSnapshot { get; set; } = true;
    public bool ExactWindowsNavigationIcons { get; set; } = true;
    public bool WindowsTitleBarIcons { get; set; } = true;
    public bool WindowsCommandIcons { get; set; } = true;
    public bool VectorNavigationGroupChevrons { get; set; } = true;
    public bool StablePlexRefreshButton { get; set; } = true;
    public int PrimaryReferenceWidth { get; set; } = 1920;
    public int PrimaryReferenceHeight { get; set; } = 1040;
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
