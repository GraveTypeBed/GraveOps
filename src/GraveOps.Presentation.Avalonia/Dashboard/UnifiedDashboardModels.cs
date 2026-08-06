namespace GraveOps.Presentation.Avalonia.Dashboard;

public enum DashboardSeverity
{
    Info = 0,
    Healthy = 1,
    Warning = 2,
    Error = 3
}

public sealed record UnifiedDashboardAction(
    string Label,
    string NavigationName,
    string Endpoint = "",
    bool IsPrimary = false,
    string LogSource = "",
    string LogText = "",
    bool IncludeInformationalLogs = false,
    string LogContext = "");

public sealed record UnifiedDashboardRow(
    string Label,
    string Value,
    string Detail = "",
    DashboardSeverity Severity = DashboardSeverity.Info,
    string SecondaryValue = "");

public sealed record UnifiedDashboardCard(
    string Key,
    string Title,
    string Category,
    string Status,
    DashboardSeverity Severity,
    string PrimaryValue,
    string Summary,
    string Detail,
    string ActionLabel,
    string NavigationName,
    string Endpoint,
    string SourceKey,
    bool DefaultVisible)
{
    public IReadOnlyList<string> Facts { get; init; } =
        Array.Empty<string>();

    public IReadOnlyList<UnifiedDashboardRow> Rows { get; init; } =
        Array.Empty<UnifiedDashboardRow>();

    public IReadOnlyList<UnifiedDashboardAction> Actions { get; init; } =
        Array.Empty<UnifiedDashboardAction>();
}

public sealed record DashboardCardPreference(
    string Key,
    bool IsVisible,
    int Order);

public sealed record UnifiedDashboardState(
    string HostKey,
    string StatusText,
    string AttentionTitle,
    string AttentionDetail,
    bool IsHealthy,
    string Density,
    IReadOnlyList<UnifiedDashboardCard> Cards,
    IReadOnlyList<DashboardCardPreference> Layout)
{
    public static UnifiedDashboardState Waiting { get; } =
        new(
            "waiting",
            "Waiting",
            "Checking",
            "Waiting for the first provider snapshot",
            false,
            "Compact",
            Array.Empty<UnifiedDashboardCard>(),
            Array.Empty<DashboardCardPreference>());
}

public sealed class DashboardActionRequestedEventArgs :
    EventArgs
{
    public DashboardActionRequestedEventArgs(
        UnifiedDashboardAction action)
    {
        Action = action;
    }

    public UnifiedDashboardAction Action { get; }
}

public sealed class DashboardLayoutChangedEventArgs :
    EventArgs
{
    public DashboardLayoutChangedEventArgs(
        string hostKey,
        IReadOnlyList<DashboardCardPreference> layout)
    {
        HostKey = hostKey;
        Layout = layout;
    }

    public string HostKey { get; }

    public IReadOnlyList<DashboardCardPreference> Layout { get; }
}