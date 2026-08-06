namespace GraveOps.Presentation.Avalonia.MediaWorkspaces;

public enum UnifiedMediaHubMode
{
    Fleet,
    Identity
}

public sealed record UnifiedMediaInstanceRow(
    string Key,
    string DisplayName,
    string State,
    string Meta,
    string Endpoint,
    string Detail);

public sealed record UnifiedMediaProductRow(
    string Key,
    string Product,
    string Category,
    string State,
    string Summary,
    IReadOnlyList<UnifiedMediaInstanceRow> Instances,
    bool CanOpen,
    bool CanEditIdentity);

public sealed record UnifiedIdentityRow(
    string Key,
    string DisplayName,
    string Product,
    string Role,
    string Protocol,
    string Parent,
    string Url,
    string Category,
    string Verification,
    string Detected,
    bool OwnsHealth,
    bool ShowNavigation,
    bool IsVisible);

public sealed record UnifiedIdentityEditRequest(
    string Key,
    string DisplayName,
    string Product,
    string Role,
    string Protocol,
    string Parent,
    string Url,
    string Category,
    bool OwnsHealth,
    bool ShowNavigation,
    bool IsVisible);

public sealed record UnifiedMediaHubState(
    string SampleAge,
    string Target,
    string Healthy,
    string Attention,
    string Offline,
    string GroupingSummary,
    IReadOnlyList<UnifiedMediaProductRow> Products,
    UnifiedMediaHubMode Mode,
    bool ShowHidden,
    bool CanRefresh,
    bool CanShowHidden,
    bool IdentityAvailable,
    string IdentityStorePath,
    string IdentitySummary,
    IReadOnlyList<UnifiedIdentityRow> IdentityRows,
    string IdentityStatus)
{
    public static UnifiedMediaHubState Empty { get; } =
        new(
            "Waiting for environment capture",
            "--",
            "0",
            "0",
            "0",
            "Waiting for grouped fleet projection.",
            Array.Empty<UnifiedMediaProductRow>(),
            UnifiedMediaHubMode.Fleet,
            false,
            true,
            false,
            false,
            "--",
            "Identity registry unavailable.",
            Array.Empty<UnifiedIdentityRow>(),
            "No application identity selected.");
}

public sealed class UnifiedMediaHubModeEventArgs : EventArgs
{
    public UnifiedMediaHubModeEventArgs(
        UnifiedMediaHubMode mode)
    {
        Mode = mode;
    }

    public UnifiedMediaHubMode Mode { get; }
}

public sealed class UnifiedMediaProductEventArgs : EventArgs
{
    public UnifiedMediaProductEventArgs(
        UnifiedMediaProductRow row)
    {
        Row = row;
    }

    public UnifiedMediaProductRow Row { get; }
}

public sealed class UnifiedIdentityEventArgs : EventArgs
{
    public UnifiedIdentityEventArgs(
        UnifiedIdentityRow row)
    {
        Row = row;
    }

    public UnifiedIdentityRow Row { get; }
}

public sealed class UnifiedIdentitySaveEventArgs : EventArgs
{
    public UnifiedIdentitySaveEventArgs(
        UnifiedIdentityEditRequest request)
    {
        Request = request;
    }

    public UnifiedIdentityEditRequest Request { get; }
}

public sealed record UnifiedPlexSessionRow(
    string Key,
    string Title,
    string User,
    string Player,
    string State,
    string Progress,
    string Video,
    string Audio,
    string Bandwidth,
    string Detail);

public sealed record UnifiedPlexState(
    string Target,
    string Freshness,
    string Service,
    string ServiceDetail,
    string Version,
    string Endpoint,
    string Connection,
    string Dependency,
    string ActiveSessions,
    string DirectPlay,
    string DirectStream,
    string Transcoding,
    string Libraries,
    string PlaybackAnalytics,
    string ServerContext,
    string SessionCount,
    IReadOnlyList<UnifiedPlexSessionRow> Sessions,
    string EmptyText,
    string Security,
    string Status,
    bool CanRefresh,
    bool CanOpen,
    bool CanRestart,
    bool CanOpenLogs,
    bool CanOpenTerminal,
    bool CanOpenIntelligence,
    bool ConfigEditable,
    string ConfigEndpoint,
    string ConfigEvidence,
    string ConfigStatus)
{
    public static UnifiedPlexState Empty { get; } =
        new(
            "--",
            "CHECKING...",
            "CHECKING",
            "Waiting for runtime ownership",
            "--",
            "--",
            "Waiting for identity probe",
            "--",
            "--",
            "--",
            "--",
            "--",
            "--",
            "Waiting for live session telemetry...",
            "Waiting for Plex identity and library context...",
            "--",
            Array.Empty<UnifiedPlexSessionRow>(),
            "No active Plex sessions.",
            "Protected telemetry.",
            "Waiting for Plex telemetry.",
            true,
            false,
            false,
            false,
            false,
            false,
            false,
            string.Empty,
            "Configuration is managed by the active platform adapter.",
            "No configuration action run.");
}

public sealed record UnifiedSecretConfigurationRequest(
    string Endpoint,
    string UserName,
    string Secret);

public sealed class UnifiedSecretConfigurationEventArgs : EventArgs
{
    public UnifiedSecretConfigurationEventArgs(
        UnifiedSecretConfigurationRequest request)
    {
        Request = request;
    }

    public UnifiedSecretConfigurationRequest Request { get; }
}

public enum UnifiedPlexAction
{
    Refresh,
    Open,
    Restart,
    Logs,
    Terminal,
    Intelligence,
    SaveAndTest,
    ClearCredential
}

public sealed class UnifiedPlexActionEventArgs : EventArgs
{
    public UnifiedPlexActionEventArgs(
        UnifiedPlexAction action,
        UnifiedSecretConfigurationRequest configuration)
    {
        Action = action;
        Configuration = configuration;
    }

    public UnifiedPlexAction Action { get; }
    public UnifiedSecretConfigurationRequest Configuration { get; }
}

public sealed record UnifiedArrInstanceRow(
    string Key,
    string DisplayName,
    string State,
    string Endpoint,
    string Version,
    string Work,
    string Health,
    string Detail);

public sealed record UnifiedMediaWorkRow(
    string Key,
    string Service,
    string Type,
    string Item,
    string State,
    string Progress,
    string Remaining,
    string Detail);

public sealed record UnifiedArrCustomization(
    bool Available,
    string FriendlyName,
    string Role,
    string ConfigPath,
    bool PrivacyMode,
    string Modules,
    string Status);

public sealed record UnifiedArrState(
    string Product,
    string Subtitle,
    string Target,
    string Freshness,
    string InstanceCount,
    string State,
    string Version,
    string WorkLabel,
    string Work,
    string WorkHint,
    string Health,
    string OperationsHint,
    IReadOnlyList<UnifiedArrInstanceRow> Instances,
    string WorkTitle,
    string WorkSubtitle,
    IReadOnlyList<UnifiedMediaWorkRow> WorkRows,
    string Footer,
    bool CanRefresh,
    bool CanOpen,
    bool CanOpenDetail,
    bool CanOpenDocker,
    bool CanOpenLogs,
    bool CanOpenIntelligence,
    bool ConfigEditable,
    string ConfigEndpoint,
    string ConfigEvidence,
    string Security,
    string Status,
    UnifiedArrCustomization Customization)
{
    public static UnifiedArrState Empty { get; } =
        new(
            "Arr application",
            "Application health, queue and operational tools.",
            "--",
            "Waiting for live telemetry",
            "0 instances",
            "WAITING",
            "--",
            "QUEUE",
            "--",
            "Telemetry pending",
            "--",
            "Application and stack tools stay together.",
            Array.Empty<UnifiedArrInstanceRow>(),
            "Queue & health",
            "Item-level work and health messages.",
            Array.Empty<UnifiedMediaWorkRow>(),
            "Waiting for live telemetry.",
            true,
            false,
            false,
            false,
            false,
            false,
            false,
            string.Empty,
            "Configuration is managed by the active platform adapter.",
            "Protected telemetry.",
            "Waiting for application telemetry.",
            new UnifiedArrCustomization(
                false,
                string.Empty,
                string.Empty,
                string.Empty,
                false,
                string.Empty,
                "Workspace customization unavailable."));
}

public enum UnifiedArrAction
{
    Refresh,
    Open,
    OpenDetail,
    Docker,
    Logs,
    Intelligence,
    SaveAndTest,
    ClearCredential,
    SaveCustomization,
    ResetCustomization
}

public sealed record UnifiedArrActionRequest(
    UnifiedArrAction Action,
    UnifiedSecretConfigurationRequest Configuration,
    UnifiedArrCustomization Customization);

public sealed class UnifiedArrActionEventArgs : EventArgs
{
    public UnifiedArrActionEventArgs(
        UnifiedArrActionRequest request)
    {
        Request = request;
    }

    public UnifiedArrActionRequest Request { get; }
}

public sealed record UnifiedTransferRow(
    string Key,
    string Name,
    string Category,
    string State,
    string Progress,
    string Size,
    string Remaining,
    string DownloadSpeed,
    string UploadSpeed,
    string Eta,
    string Peers,
    string Ratio,
    string Added,
    string Completed,
    string Duration,
    string Detail);

public sealed record UnifiedDownloadClientState(
    string Product,
    string Description,
    string Target,
    string Freshness,
    string State,
    string Security,
    string Version,
    string Connection,
    string Active,
    string ActiveDetail,
    string ItemsLabel,
    string Items,
    string ItemsDetail,
    string Metric1Label,
    string Metric1Value,
    string Metric2Label,
    string Metric2Value,
    string Metric3Label,
    string Metric3Value,
    string Metric4Label,
    string Metric4Value,
    string OperationsHint,
    string TransferAnalytics,
    string WorkloadAnalytics,
    string QueueTitle,
    string QueueHint,
    IReadOnlyList<UnifiedTransferRow> Queue,
    string HistoryTitle,
    string HistoryHint,
    IReadOnlyList<UnifiedTransferRow> History,
    string Status,
    bool CanRefresh,
    bool CanOpen,
    bool CanOpenDocker,
    bool CanOpenLogs,
    bool CanOpenTerminal,
    bool ConfigEditable,
    string ConfigEndpoint,
    string UserNameLabel,
    string ConfigUserName,
    string SecretLabel,
    string ConfigEvidence)
{
    public static UnifiedDownloadClientState Empty { get; } =
        new(
            "Download client",
            "Live download analytics and current work.",
            "--",
            "CHECKING...",
            "CHECKING",
            "Protected telemetry",
            "--",
            "--",
            "--",
            "Telemetry pending",
            "ITEMS",
            "--",
            "Telemetry pending",
            "DOWNLOAD",
            "--",
            "UPLOAD",
            "--",
            "REMAINING",
            "--",
            "TIME",
            "--",
            "Read-only analytics are automatic; operational handoffs stay explicit.",
            "Waiting for transfer analytics.",
            "Waiting for workload analytics.",
            "Current work",
            "Live work appears automatically.",
            Array.Empty<UnifiedTransferRow>(),
            "Recent history",
            "Completed work appears automatically.",
            Array.Empty<UnifiedTransferRow>(),
            "Waiting for download-client telemetry.",
            true,
            false,
            false,
            false,
            false,
            false,
            string.Empty,
            "User name",
            string.Empty,
            "Credential",
            "Configuration is managed by the active platform adapter.");
}

public enum UnifiedDownloadClientAction
{
    Refresh,
    Open,
    Docker,
    Logs,
    Terminal,
    SaveAndTest,
    ClearCredential
}

public sealed class UnifiedDownloadClientActionEventArgs : EventArgs
{
    public UnifiedDownloadClientActionEventArgs(
        UnifiedDownloadClientAction action,
        UnifiedSecretConfigurationRequest configuration)
    {
        Action = action;
        Configuration = configuration;
    }

    public UnifiedDownloadClientAction Action { get; }
    public UnifiedSecretConfigurationRequest Configuration { get; }
}

public sealed record UnifiedLifecycleStageRow(
    string Key,
    string Stage,
    string State,
    string Evidence);

public sealed record UnifiedLifecycleItemRow(
    string Key,
    string Item,
    string Owner,
    string Stage,
    string State,
    string Progress,
    string Remaining,
    string MediaType,
    string Confidence,
    string Evidence);

public sealed record UnifiedRemediationRow(
    string Key,
    string Step,
    string Component,
    string Severity,
    string Why,
    string NextStep);

public sealed record UnifiedLifecycleState(
    string Active,
    string Attention,
    string Downloading,
    string Importing,
    string Playing,
    string Summary,
    IReadOnlyList<UnifiedLifecycleStageRow> Stages,
    IReadOnlyList<UnifiedLifecycleItemRow> Items,
    IReadOnlyList<UnifiedRemediationRow> Remediation,
    string SelectedTitle,
    string SelectedDetail,
    string SourceSummary,
    string Status,
    bool CanRefresh,
    bool CanOpenOwner,
    bool CanOpenIntelligence)
{
    public static UnifiedLifecycleState Empty { get; } =
        new(
            "0",
            "0",
            "0",
            "0",
            "0",
            "Waiting for capture",
            Array.Empty<UnifiedLifecycleStageRow>(),
            Array.Empty<UnifiedLifecycleItemRow>(),
            Array.Empty<UnifiedRemediationRow>(),
            "No lifecycle item selected",
            string.Empty,
            "No source telemetry captured.",
            "Waiting for lifecycle telemetry.",
            true,
            false,
            false);
}

public sealed class UnifiedLifecycleItemEventArgs : EventArgs
{
    public UnifiedLifecycleItemEventArgs(
        UnifiedLifecycleItemRow row)
    {
        Row = row;
    }

    public UnifiedLifecycleItemRow Row { get; }
}

public sealed class UnifiedRemediationEventArgs : EventArgs
{
    public UnifiedRemediationEventArgs(
        UnifiedRemediationRow row)
    {
        Row = row;
    }

    public UnifiedRemediationRow Row { get; }
}

public enum UnifiedLifecycleAction
{
    Refresh,
    OpenOwner,
    Intelligence
}

public sealed class UnifiedLifecycleActionEventArgs : EventArgs
{
    public UnifiedLifecycleActionEventArgs(
        UnifiedLifecycleAction action)
    {
        Action = action;
    }

    public UnifiedLifecycleAction Action { get; }
}
