namespace GraveOps.Presentation.Avalonia.SystemWorkspaces;

public sealed record UnifiedServiceRow(
    string Unit,
    string Description,
    string ActiveState,
    string SubState,
    string Policy,
    string Evidence,
    bool CanStart,
    bool CanStop,
    bool CanRestart);

public sealed record UnifiedStorageRow(
    string Source,
    string MountPoint,
    string FileSystem,
    string Size,
    string Used,
    string Available,
    string PercentUsed,
    double PercentValue,
    string StatusLabel,
    string PolicyLabel,
    bool CanConfigureCapacity,
    bool CanConfigureThreshold,
    bool CanRestoreDefaults);

public enum UnifiedLogSeverity
{
    Information,
    Warning,
    Error
}

public sealed record UnifiedLogRow(
    string Key,
    UnifiedLogSeverity Severity,
    string SeverityLabel,
    string Source,
    string DisplayTime,
    int Count,
    string Message,
    string Detail);

public sealed record UnifiedServicesState(
    IReadOnlyList<UnifiedServiceRow> Rows,
    string Status,
    string ActionStatus,
    bool SafeModeEnabled,
    bool CanToggleSafeMode)
{
    public static UnifiedServicesState Empty { get; } =
        new(
            Array.Empty<UnifiedServiceRow>(),
            "Waiting for service inventory.",
            "No action run.",
            false,
            false);
}

public sealed record UnifiedStorageState(
    IReadOnlyList<UnifiedStorageRow> Rows,
    string Status,
    string PolicyStatus,
    string CapacityStatus)
{
    public static UnifiedStorageState Empty { get; } =
        new(
            Array.Empty<UnifiedStorageRow>(),
            "Waiting for storage inventory.",
            "Select a storage root to inspect policy.",
            "Capacity policy is unavailable.");
}

public sealed record UnifiedLogsState(
    IReadOnlyList<UnifiedLogRow> Rows,
    string Status,
    string EmptyTitle,
    string EmptyDetail)
{
    public static UnifiedLogsState Empty { get; } =
        new(
            Array.Empty<UnifiedLogRow>(),
            "Waiting for log evidence.",
            "No visible log evidence",
            "No platform adapter has projected logs yet.");
}

public sealed class UnifiedServiceActionRequestedEventArgs :
    EventArgs
{
    public UnifiedServiceActionRequestedEventArgs(
        UnifiedServiceRow row,
        string action)
    {
        Row = row;
        Action = action;
    }

    public UnifiedServiceRow Row { get; }
    public string Action { get; }
}

public sealed class UnifiedSafeModeRequestedEventArgs :
    EventArgs
{
    public UnifiedSafeModeRequestedEventArgs(
        bool enabled)
    {
        Enabled = enabled;
    }

    public bool Enabled { get; }
}

public enum UnifiedStorageAction
{
    CapacityPolicy,
    Thresholds,
    RestoreDefaults
}

public sealed class UnifiedStorageActionRequestedEventArgs :
    EventArgs
{
    public UnifiedStorageActionRequestedEventArgs(
        UnifiedStorageRow row,
        UnifiedStorageAction action)
    {
        Row = row;
        Action = action;
    }

    public UnifiedStorageRow Row { get; }
    public UnifiedStorageAction Action { get; }
}

public enum UnifiedLogAction
{
    CopyDetail,
    OpenIntelligence
}

public sealed class UnifiedLogActionRequestedEventArgs :
    EventArgs
{
    public UnifiedLogActionRequestedEventArgs(
        UnifiedLogRow row,
        UnifiedLogAction action)
    {
        Row = row;
        Action = action;
    }

    public UnifiedLogRow Row { get; }
    public UnifiedLogAction Action { get; }
}