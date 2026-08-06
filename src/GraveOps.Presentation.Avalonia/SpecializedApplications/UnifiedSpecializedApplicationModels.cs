using System;
using System.Collections.Generic;

namespace GraveOps.Presentation.Avalonia.SpecializedApplications;

public enum UnifiedApplicationWorkspaceAction
{
    Open,
    Docker,
    Logs,
    Intelligence,
    Back
}

public sealed record UnifiedApplicationWorkspaceState(
    string Target,
    string Name,
    string Subtitle,
    string State,
    string Runtime,
    string Role,
    string ActiveFindings,
    string PrimaryTitle,
    string Endpoint,
    string SecondaryTitle,
    string Owner,
    string Evidence,
    string RelatedContext,
    string OperationsStatus,
    bool CanOpen,
    bool CanOpenDocker,
    bool CanOpenLogs,
    bool CanOpenIntelligence,
    bool ShowBack)
{
    public static UnifiedApplicationWorkspaceState Empty { get; } =
        new(
            "--",
            "Application",
            "Verified application state and operational ownership.",
            "WAITING",
            "--",
            "--",
            "0",
            "Application readiness",
            "--",
            "Dependencies",
            "--",
            "Waiting for verified application evidence.",
            "No related operational context is available.",
            "Waiting for application selection.",
            false,
            false,
            false,
            false,
            false);
}

public sealed class UnifiedApplicationWorkspaceActionEventArgs(
    UnifiedApplicationWorkspaceAction action) :
    EventArgs
{
    public UnifiedApplicationWorkspaceAction Action { get; } =
        action;
}

public enum UnifiedRecyclarrAction
{
    Refresh,
    OpenConfig,
    Preview,
    Docker,
    Logs,
    Back
}

public sealed record UnifiedRecyclarrTargetRow(
    string Service,
    string Instance,
    string ConfigFile,
    string Endpoint);

public sealed record UnifiedRecyclarrConfigFileRow(
    string File,
    string RelativePath,
    string Size,
    string Modified,
    string Targets);

public sealed record UnifiedRecyclarrState(
    string Target,
    string Freshness,
    string Runtime,
    string Version,
    string ConfigFiles,
    string Targets,
    string ContainerName,
    string Image,
    string Compose,
    string Schedule,
    string ConfigPath,
    string LastRun,
    string Evidence,
    IReadOnlyList<UnifiedRecyclarrTargetRow> TargetRows,
    IReadOnlyList<UnifiedRecyclarrConfigFileRow> ConfigRows,
    string PreviewStatus,
    string PreviewOutput,
    string Status,
    bool CanRefresh,
    bool CanOpenConfig,
    bool CanPreview,
    bool CanOpenDocker,
    bool CanOpenLogs,
    bool ShowBack)
{
    public static UnifiedRecyclarrState Empty { get; } =
        new(
            "--",
            "Capture pending",
            "WAITING",
            "--",
            "0",
            "0",
            "--",
            "--",
            "--",
            "--",
            "--",
            "--",
            "Recyclarr evidence has not been captured.",
            Array.Empty<UnifiedRecyclarrTargetRow>(),
            Array.Empty<UnifiedRecyclarrConfigFileRow>(),
            "Preview has not been run.",
            "Recyclarr preview output will appear here.",
            "Open this page to capture the Recyclarr container and configuration inventory.",
            true,
            false,
            false,
            true,
            true,
            false);
}

public sealed class UnifiedRecyclarrActionEventArgs(
    UnifiedRecyclarrAction action) :
    EventArgs
{
    public UnifiedRecyclarrAction Action { get; } =
        action;
}

public enum UnifiedPiHoleAction
{
    Refresh,
    Open,
    EnableBlocking,
    DisableBlockingFiveMinutes,
    ReloadDns,
    Logs,
    Back
}

public sealed record UnifiedPiHoleState(
    string Target,
    string Freshness,
    string State,
    string Dns,
    string Blocking,
    string Queries,
    string Blocked,
    string Versions,
    string HostContext,
    string ClientContext,
    string GravityContext,
    string Evidence,
    string Status,
    bool CanRefresh,
    bool CanOpen,
    bool CanEnable,
    bool CanDisable,
    bool CanReload,
    bool CanOpenLogs,
    bool ShowBack)
{
    public static UnifiedPiHoleState Empty { get; } =
        new(
            "--",
            "Not captured",
            "NOT CAPTURED",
            "--",
            "--",
            "--",
            "--",
            "Core -- · Web -- · FTL --",
            "Host context unavailable.",
            "Client statistics unavailable.",
            "Gravity inventory unavailable.",
            "No verified Pi-hole source is associated with the active target.",
            "Capture Pi-hole before running an operation. Safe Mode blocks all mutations.",
            true,
            false,
            false,
            false,
            false,
            true,
            false);
}

public sealed class UnifiedPiHoleActionEventArgs(
    UnifiedPiHoleAction action) :
    EventArgs
{
    public UnifiedPiHoleAction Action { get; } =
        action;
}
