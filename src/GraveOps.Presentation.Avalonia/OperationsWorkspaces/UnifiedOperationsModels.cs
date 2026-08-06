namespace GraveOps.Presentation.Avalonia.OperationsWorkspaces;

public enum UnifiedDockerAction
{
    Start,
    Stop,
    Restart,
    RestartProject
}

public sealed record UnifiedDockerRow(
    string Key,
    string Group,
    string Name,
    string Image,
    string State,
    string Health,
    string RestartSummary,
    string Resources,
    string Ports,
    string ComposeOwnership,
    string ContainerId,
    bool IsRunning,
    bool HasAttention,
    bool CanStart,
    bool CanStop,
    bool CanRestart,
    bool CanRestartProject);

public sealed record UnifiedDockerDetail(
    string ContainerKey,
    string Title,
    string Subtitle,
    string Image,
    string Compose,
    string Lifecycle,
    string Resources,
    string ContainerId,
    string RestartPolicy,
    string Networks,
    string Ports,
    string Mounts,
    string EnvironmentNames,
    string CleanedLogs,
    string RawLogs,
    string Evidence,
    string ActionStatus,
    int PortCount,
    int MountCount,
    int EnvironmentCount,
    int CleanedIncidentCount,
    int RawLineCount,
    int CollapsedLineCount)
{
    public static UnifiedDockerDetail Empty { get; } =
        new(
            string.Empty,
            "No container selected",
            "--",
            "--",
            "--",
            "--",
            "--",
            "--",
            "--",
            "--",
            "--",
            "Select a container to inspect mounts.",
            "Environment-variable names only. Values are never displayed.",
            "Select a container to capture cleaned logs.",
            "Select a container to capture redacted raw logs.",
            "Select a container to inspect.",
            "No container action run.",
            0,
            0,
            0,
            0,
            0,
            0);
}

public sealed record UnifiedDockerState(
    IReadOnlyList<UnifiedDockerRow> Rows,
    UnifiedDockerDetail Detail,
    string DaemonStatus,
    string Summary,
    string WorkspaceStatus,
    bool ShowExited,
    bool CanRefresh,
    bool CanInspect)
{
    public static UnifiedDockerState Empty { get; } =
        new(
            Array.Empty<UnifiedDockerRow>(),
            UnifiedDockerDetail.Empty,
            "Capture pending",
            "0 shown",
            "Docker workspace capture pending.",
            false,
            true,
            false);
}

public sealed class UnifiedDockerSelectionRequestedEventArgs :
    EventArgs
{
    public UnifiedDockerSelectionRequestedEventArgs(
        UnifiedDockerRow row)
    {
        Row = row;
    }

    public UnifiedDockerRow Row { get; }
}

public sealed class UnifiedDockerActionRequestedEventArgs :
    EventArgs
{
    public UnifiedDockerActionRequestedEventArgs(
        UnifiedDockerRow row,
        UnifiedDockerAction action)
    {
        Row = row;
        Action = action;
    }

    public UnifiedDockerRow Row { get; }
    public UnifiedDockerAction Action { get; }
}

public sealed class UnifiedDockerCopyRequestedEventArgs :
    EventArgs
{
    public UnifiedDockerCopyRequestedEventArgs(
        string text,
        string successMessage)
    {
        Text = text;
        SuccessMessage = successMessage;
    }

    public string Text { get; }
    public string SuccessMessage { get; }
}

public sealed record UnifiedBackupUnitRow(
    string Unit,
    string Active,
    string SubState,
    string Enabled,
    string LastRun,
    string NextRun,
    string Path,
    string Severity);

public sealed record UnifiedBackupArtifactRow(
    string Path,
    string Size,
    string Modified);

public sealed record UnifiedBackupsState(
    bool CapabilityAvailable,
    string State,
    string Severity,
    string Provider,
    string Summary,
    string OperationsState,
    IReadOnlyList<string> Evidence,
    IReadOnlyList<UnifiedBackupUnitRow> Units,
    IReadOnlyList<UnifiedBackupArtifactRow> Artifacts)
{
    public static UnifiedBackupsState Empty { get; } =
        new(
            false,
            "WAITING",
            "Information",
            "--",
            "Waiting for backup inventory.",
            "READ ONLY",
            Array.Empty<string>(),
            Array.Empty<UnifiedBackupUnitRow>(),
            Array.Empty<UnifiedBackupArtifactRow>());
}

public sealed record UnifiedPathRow(
    string Key,
    string Label,
    string Path,
    bool CanOpen,
    bool CanOpenTerminal);

public sealed record UnifiedVersionState(
    string Branch,
    string Commit,
    string Worktree,
    string Origin,
    string Dotnet)
{
    public static UnifiedVersionState Empty { get; } =
        new(
            "--",
            "--",
            "--",
            "--",
            "--");
}

public sealed record UnifiedSettingsState(
    IReadOnlyList<string> ThemeOptions,
    string Theme,
    IReadOnlyList<string> DensityOptions,
    string Density,
    bool RestoreSession,
    bool SilentRefresh,
    bool ShowFreshness,
    bool InterfaceEditable,
    string InterfaceStatus,
    bool StartSafeMode,
    bool ShowInformationalLogs,
    bool ShowInformationalContainers,
    bool OpenOverview,
    bool DesktopNotifications,
    string BackgroundRefreshSeconds,
    bool OperatorDefaultsEditable,
    string OperatorStatus,
    string PolicySummary,
    string CapacityPolicySummary,
    string SignalQualitySummary,
    string RemediationSummary,
    string UiPerformanceSummary,
    IReadOnlyList<UnifiedPathRow> Paths,
    string PathStatus,
    UnifiedVersionState Version,
    bool PolicyActionsAvailable)
{
    public static UnifiedSettingsState Empty { get; } =
        new(
            Array.Empty<string>(),
            "Shared Linux visual system",
            Array.Empty<string>(),
            "Desktop",
            false,
            true,
            true,
            false,
            "Settings are unavailable.",
            true,
            false,
            false,
            false,
            false,
            "60",
            false,
            "Operator defaults are unavailable.",
            "No policy store is available.",
            "Capacity policies are unavailable.",
            "Signal quality policies are unavailable.",
            "Remediation safety policies are unavailable.",
            "UI performance policies are unavailable.",
            Array.Empty<UnifiedPathRow>(),
            "No path action run.",
            UnifiedVersionState.Empty,
            false);
}

public sealed record UnifiedInterfaceSettingsRequest(
    string Theme,
    string Density,
    bool RestoreSession,
    bool SilentRefresh,
    bool ShowFreshness);

public sealed record UnifiedOperatorSettingsRequest(
    bool StartSafeMode,
    bool ShowInformationalLogs,
    bool ShowInformationalContainers,
    bool OpenOverview,
    bool DesktopNotifications,
    string BackgroundRefreshSeconds);

public enum UnifiedSettingsAction
{
    PreviewInterface,
    SaveInterface,
    ExpressSetup,
    ResetDashboard,
    ExportProfile,
    SaveOperatorDefaults,
    RestoreOperatorDefaults,
    CapacityAlerts,
    SignalQuality,
    RemediationSafety,
    UiPerformance,
    StorageThresholds,
    DashboardPolicies,
    RefreshVersion
}

public sealed class UnifiedSettingsActionRequestedEventArgs :
    EventArgs
{
    public UnifiedSettingsActionRequestedEventArgs(
        UnifiedSettingsAction action,
        UnifiedInterfaceSettingsRequest interfaceSettings,
        UnifiedOperatorSettingsRequest operatorSettings)
    {
        Action = action;
        InterfaceSettings = interfaceSettings;
        OperatorSettings = operatorSettings;
    }

    public UnifiedSettingsAction Action { get; }
    public UnifiedInterfaceSettingsRequest InterfaceSettings { get; }
    public UnifiedOperatorSettingsRequest OperatorSettings { get; }
}

public enum UnifiedPathAction
{
    Open,
    Terminal
}

public sealed class UnifiedPathActionRequestedEventArgs :
    EventArgs
{
    public UnifiedPathActionRequestedEventArgs(
        UnifiedPathRow row,
        UnifiedPathAction action)
    {
        Row = row;
        Action = action;
    }

    public UnifiedPathRow Row { get; }
    public UnifiedPathAction Action { get; }
}

public sealed record UnifiedFileRow(
    string Key,
    string Name,
    string FullPath,
    string Kind,
    string Size,
    string Modified,
    bool IsDirectory);

public sealed record UnifiedScriptRow(
    string Key,
    string Name,
    string Description,
    string Command,
    bool IsMutating,
    bool CanRun);

public sealed record UnifiedParityRow(
    string Key,
    string Capability,
    string Classification,
    string LinuxImplementation,
    string WindowsReference,
    string Evidence);


public sealed class UnifiedTextCopyRequestedEventArgs :
    EventArgs
{
    public UnifiedTextCopyRequestedEventArgs(
        string text,
        string successMessage)
    {
        Text = text;
        SuccessMessage = successMessage;
    }

    public string Text { get; }
    public string SuccessMessage { get; }
}

public sealed record UnifiedToolsState(
    string TerminalStatus,
    bool CanOpenLocalTerminal,
    bool CanOpenSsh,
    string DiagnosticsStatus,
    bool CanCreateDiagnostics,
    string ValidationOutput,
    bool CanRunValidation,
    string FilesPath,
    IReadOnlyList<UnifiedFileRow> Files,
    string FilesStatus,
    bool CanBrowseFiles,
    bool CanOpenSftp,
    IReadOnlyList<UnifiedScriptRow> Scripts,
    string ScriptOutput,
    string ScriptStatus,
    string UpdateOutput,
    string UpdateStatus,
    bool CanCaptureUpdates,
    IReadOnlyList<UnifiedParityRow> Parity,
    string ParitySummary)
{
    public static UnifiedToolsState Empty { get; } =
        new(
            "No terminal handoff run.",
            false,
            false,
            "Diagnostics are unavailable.",
            false,
            "Validation has not been run.",
            false,
            string.Empty,
            Array.Empty<UnifiedFileRow>(),
            "Files are unavailable.",
            false,
            false,
            Array.Empty<UnifiedScriptRow>(),
            "Select a script to inspect or run.",
            "Script library is unavailable.",
            "Update inventory has not been captured.",
            "Update inventory is unavailable.",
            false,
            Array.Empty<UnifiedParityRow>(),
            "Parity matrix unavailable.");
}

public enum UnifiedTerminalAction
{
    Home,
    Repository,
    Config,
    Diagnostics,
    Ssh
}

public sealed class UnifiedTerminalActionRequestedEventArgs :
    EventArgs
{
    public UnifiedTerminalActionRequestedEventArgs(
        UnifiedTerminalAction action)
    {
        Action = action;
    }

    public UnifiedTerminalAction Action { get; }
}

public enum UnifiedFilesAction
{
    Refresh,
    Parent,
    OpenSelected,
    Sftp
}

public sealed class UnifiedFilesActionRequestedEventArgs :
    EventArgs
{
    public UnifiedFilesActionRequestedEventArgs(
        UnifiedFilesAction action,
        string path,
        UnifiedFileRow? selected)
    {
        Action = action;
        Path = path;
        Selected = selected;
    }

    public UnifiedFilesAction Action { get; }
    public string Path { get; }
    public UnifiedFileRow? Selected { get; }
}

public enum UnifiedScriptAction
{
    Run,
    Copy
}

public sealed class UnifiedScriptActionRequestedEventArgs :
    EventArgs
{
    public UnifiedScriptActionRequestedEventArgs(
        UnifiedScriptAction action,
        UnifiedScriptRow row)
    {
        Action = action;
        Row = row;
    }

    public UnifiedScriptAction Action { get; }
    public UnifiedScriptRow Row { get; }
}
