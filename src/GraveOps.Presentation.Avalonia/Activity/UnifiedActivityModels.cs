namespace GraveOps.Presentation.Avalonia.Activity;

public enum UnifiedActivitySeverity
{
    Healthy = 0,
    Info = 1,
    Warning = 2,
    Error = 3,
    Critical = 4
}

public sealed record UnifiedActivityRow(
    DateTimeOffset Timestamp,
    string Stream,
    string Target,
    string Component,
    string Title,
    string Detail,
    UnifiedActivitySeverity Severity,
    string NavigationKey,
    string Replay)
{
    public string DisplayTime =>
        Timestamp.ToLocalTime().ToString(
            "g");

    public string SeverityLabel =>
        UnifiedActivityLabels.Severity(
            Severity);

    public string Key =>
        string.Join(
            "|",
            Timestamp.ToUnixTimeMilliseconds(),
            Stream,
            Target,
            Component,
            Title);

    public bool IsHealthTransition =>
        Stream.Equals(
            "Health transition",
            StringComparison.OrdinalIgnoreCase);

    public bool IsIncident =>
        Severity >=
            UnifiedActivitySeverity.Warning ||
        Stream.Equals(
            "Incident",
            StringComparison.OrdinalIgnoreCase);
}

public sealed record UnifiedActivityState(
    IReadOnlyList<UnifiedActivityRow> Events,
    int RetainedCount,
    string Status,
    string RetentionDetail)
{
    public static UnifiedActivityState Empty { get; } =
        new(
            Array.Empty<UnifiedActivityRow>(),
            0,
            "No retained activity is available.",
            "No activity source has reported data.");
}

public static class UnifiedActivityLabels
{
    public static string Severity(
        UnifiedActivitySeverity severity) =>
        severity switch
        {
            UnifiedActivitySeverity.Healthy =>
                "HEALTHY",
            UnifiedActivitySeverity.Info =>
                "INFO",
            UnifiedActivitySeverity.Warning =>
                "WARNING",
            UnifiedActivitySeverity.Error =>
                "ERROR",
            UnifiedActivitySeverity.Critical =>
                "CRITICAL",
            _ =>
                "UNKNOWN"
        };
}

public sealed class UnifiedActivityNavigationRequestedEventArgs(
    string navigationKey)
    : EventArgs
{
    public string NavigationKey { get; } =
        navigationKey;
}

public sealed class UnifiedActivityCopyRequestedEventArgs(
    string text)
    : EventArgs
{
    public string Text { get; } =
        text;
}