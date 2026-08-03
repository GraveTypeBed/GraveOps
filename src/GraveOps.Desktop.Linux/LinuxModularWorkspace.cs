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
    public string Version { get; set; } = "4.9.0-H";
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
    public bool UniqueApplicationNavigationIcons { get; set; } = true;
    public bool OfficialLogoInspiredArrVectors { get; set; } = true;
    public bool WindowsNavigationHoverParity { get; set; } = true;
    public bool DistinctNavigationFocusState { get; set; } = true;
    public bool SidebarScrollbarGutter { get; set; } = true;
    public bool OfficialLogoInspiredPlexVector { get; set; } = true;
    public bool AdaptiveWorkspaceFoundation { get; set; } = true;
    public bool ContentDrivenWorkspaceRows { get; set; } = true;
    public bool CompactOperationalEmptyStates { get; set; } = true;
    public bool AccessibleActionSurfaces { get; set; } = true;
    public bool ModularWorkspaceThemePreserved { get; set; } = true;
    public bool DedicatedRecyclarrWorkspace { get; set; } = true;
    public bool RecyclarrContainerNativeTelemetry { get; set; } = true;
    public bool RecyclarrConfigTargetInventory { get; set; } = true;
    public bool RecyclarrReadOnlyPreview { get; set; } = true;
    public bool RecyclarrSecretRedaction { get; set; } = true;
    public bool RecyclarrDirectInstanceParsing { get; set; } = true;
    public bool RecyclarrNestedKeyExclusion { get; set; } = true;
    public bool RecyclarrSecretReferenceLabels { get; set; } = true;
    public bool DockerOperationalDrilldown { get; set; } = true;
    public bool DockerComposeOwnershipGrouping { get; set; } = true;
    public bool DockerOneShotResourceCapture { get; set; } = true;
    public bool DockerBoundedRedactedLogs { get; set; } = true;
    public bool DockerEnvironmentNamesOnly { get; set; } = true;
    public bool DockerVerifiedContainerActions { get; set; } = true;
    public bool DockerGuardedDumbProjectRestart { get; set; } = true;
    public bool DockerCleanedRawLogModes { get; set; } = true;
    public bool DockerConsecutiveLogDeduplication { get; set; } = true;
    public bool DockerLocalTimeLogProjection { get; set; } = true;
    public bool DockerLogSourceSeverityProjection { get; set; } = true;
    public bool DockerLogFileNameFirstProjection { get; set; } = true;
    public bool DockerRawEvidenceRetention { get; set; } = true;
    public bool DockerRunningLifecycleLabel { get; set; } = true;
    public bool DockerResponsiveDetailWorkspace { get; set; } = true;
    public bool DockerBoundedDetailTabs { get; set; } = true;
    public bool DockerCompactContainerSummary { get; set; } = true;
    public bool DockerNonWrappingStatusFooter { get; set; } = true;
    public bool DockerViewportWeightedLogs { get; set; } = true;
    public bool DockerDetailTabPersistence { get; set; } = true;
    public bool HistoryEventClassification { get; set; } = true;
    public bool HistoryVisibleCountAccuracy { get; set; } = true;
    public bool HistoryNavigationNoiseSuppression { get; set; } = true;
    public bool HistoryDuplicateEventCollapse { get; set; } = true;
    public bool HistorySourceSeverityTimeFilters { get; set; } = true;
    public bool LogsExplanatoryEmptyStates { get; set; } = true;
    public bool LogsSourceSeverityTimeFilters { get; set; } = true;
    public bool LogsSelectedDetailCopy { get; set; } = true;
    public bool LogsNoAdditionalPolling { get; set; } = true;
    public bool ConditionalServerProfileFields { get; set; } = true;
    public bool CompactServerWorkspace { get; set; } = true;
    public bool NativeLocalServerMode { get; set; } = true;
    public bool RemoteSshCredentialDisclosure { get; set; } = true;
    public bool BenignPortalHealthSuppression { get; set; } = true;
    public bool RawPortalEvidenceRetention { get; set; } = true;
    public bool RoutineActivityDeduplication { get; set; } = true;
    public bool NonContiguousRoutineHistoryCollapse { get; set; } = true;
    public bool DistinctSegmentSelectionState { get; set; } = true;
    public bool ApplicationIdentityRegistry { get; set; } = true;
    public bool StableRuntimeSourceKeys { get; set; } = true;
    public bool OpaqueStableSourceKeys { get; set; } = true;
    public bool LogicalProductInstanceSeparation { get; set; } = true;
    public bool PortHintsNeverOwnHealth { get; set; } = true;
    public bool OperatorIdentityOverrides { get; set; } = true;
    public bool CompatibilityAndSupportRoles { get; set; } = true;
    public bool IdentityAliasAndEndpointOverrides { get; set; } = true;
    public bool ComposeMetadataIdentity { get; set; } = true;
    public bool NoAssumedApplicationUrls { get; set; } = true;
    public bool RenamedComposeProjectSupport { get; set; } = true;
    public bool WindowsIdentityRegistryPending { get; set; } = true;
    public bool VerifiedApplicationDiscovery { get; set; } = true;
    public bool ProductSpecificArrAdapters { get; set; } = true;
    public bool SonarrV3V5Negotiation { get; set; } = true;
    public bool ReadOnlyStatusFingerprinting { get; set; } = true;
    public bool AutomaticVerifiedPromotion { get; set; } = true;
    public bool DockerInternalPublishedPortCorrelation { get; set; } = true;
    public bool TransientApiKeyDiscovery { get; set; } = true;
    public bool ProwlarrAssistedHints { get; set; } = true;
    public bool ProbeLaunchUrlSeparation { get; set; } = true;
    public bool RedirectCredentialProtection { get; set; } = true;
    public bool PrivateDestinationProbePolicy { get; set; } = true;
    public bool NoVerificationBackgroundPolling { get; set; } = true;
    public bool MultipleVerifiedArrInstances { get; set; } = true;
    public bool CompactGroupedMediaHub { get; set; } = true;
    public bool MediaHubCategorySections { get; set; } = true;
    public bool MediaHubProductInstanceGrouping { get; set; } = true;
    public bool MediaHubBoundedInternalScroll { get; set; } = true;
    public bool MediaHubThreeColumnNaturalWrap { get; set; } = true;
    public bool MediaHubEvidenceInRegistryOnly { get; set; } = true;
    public bool StrongestVerificationResultWins { get; set; } = true;
    public bool AuxiliaryProbeFailureRetention { get; set; } = true;
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
