namespace GraveOps.Presentation.Avalonia.Findings;

public enum UnifiedFindingSeverity
{
    Healthy = 0,
    Info = 1,
    Warning = 2,
    Error = 3,
    Critical = 4
}

public sealed record UnifiedImpactRow(
    string Target,
    string Component,
    string Issue,
    string Evidence,
    string Impact,
    string NextStep,
    UnifiedFindingSeverity Severity,
    string NavigationKey)
{
    public string SeverityLabel =>
        UnifiedFindingLabels.Severity(
            Severity);
}

public sealed record UnifiedDependencyRow(
    int Order,
    string Component,
    string State,
    string Evidence,
    string Impact,
    string NextStep,
    UnifiedFindingSeverity Severity,
    string NavigationKey)
{
    public string SeverityLabel =>
        UnifiedFindingLabels.Severity(
            Severity);
}

public sealed record UnifiedFindingRow(
    UnifiedFindingSeverity Severity,
    string Component,
    string Problem,
    string Evidence,
    string Impact,
    string NextStep,
    string NavigationKey)
{
    public string SeverityLabel =>
        UnifiedFindingLabels.Severity(
            Severity);
}

public sealed record UnifiedRemediationRow(
    int Step,
    UnifiedFindingSeverity Severity,
    string Component,
    string Why,
    string NextStep,
    string NavigationKey)
{
    public string SeverityLabel =>
        UnifiedFindingLabels.Severity(
            Severity);
}

public sealed record UnifiedFindingsState(
    UnifiedFindingSeverity Severity,
    string OverallLabel,
    string Headline,
    string RootCause,
    int Blockers,
    int Warnings,
    IReadOnlyList<UnifiedImpactRow> Impact,
    IReadOnlyList<UnifiedDependencyRow> Dependencies,
    IReadOnlyList<UnifiedFindingRow> Findings,
    IReadOnlyList<UnifiedRemediationRow> Remediation,
    string ReportText,
    string StatusText)
{
    public static UnifiedFindingsState Waiting { get; } =
        new(
            UnifiedFindingSeverity.Info,
            "WAITING",
            "Refresh the environment to run analysis.",
            "No analysis captured",
            0,
            0,
            Array.Empty<UnifiedImpactRow>(),
            Array.Empty<UnifiedDependencyRow>(),
            Array.Empty<UnifiedFindingRow>(),
            Array.Empty<UnifiedRemediationRow>(),
            string.Empty,
            "Waiting for provider evidence.");
}

public static class UnifiedFindingLabels
{
    public static string Severity(
        UnifiedFindingSeverity severity) =>
        severity switch
        {
            UnifiedFindingSeverity.Healthy =>
                "HEALTHY",
            UnifiedFindingSeverity.Info =>
                "INFO",
            UnifiedFindingSeverity.Warning =>
                "WARNING",
            UnifiedFindingSeverity.Error =>
                "ERROR",
            UnifiedFindingSeverity.Critical =>
                "CRITICAL",
            _ =>
                "UNKNOWN"
        };
}

public static class UnifiedFindingsReport
{
    public static string Build(
        UnifiedFindingsState state)
    {
        if (!string.IsNullOrWhiteSpace(
                state.ReportText))
        {
            return state.ReportText;
        }

        var lines =
            new List<string>
            {
                "GRAVEOPS HEALTH & FINDINGS",
                string.Empty,
                $"Overall: {state.OverallLabel}",
                $"Headline: {state.Headline}",
                $"Root cause: {state.RootCause}",
                $"Blockers: {state.Blockers}",
                $"Warnings: {state.Warnings}",
                string.Empty,
                "PRIORITY FINDINGS"
            };

        if (state.Findings.Count == 0)
        {
            lines.Add(
                "- No active findings.");
        }
        else
        {
            foreach (var finding in
                     state.Findings)
            {
                lines.Add(
                    $"- [{finding.SeverityLabel}] " +
                    $"{finding.Component}: " +
                    finding.Problem);

                if (!string.IsNullOrWhiteSpace(
                        finding.Evidence))
                {
                    lines.Add(
                        $"  Evidence: {finding.Evidence}");
                }

                if (!string.IsNullOrWhiteSpace(
                        finding.Impact))
                {
                    lines.Add(
                        $"  Impact: {finding.Impact}");
                }

                if (!string.IsNullOrWhiteSpace(
                        finding.NextStep))
                {
                    lines.Add(
                        $"  Next: {finding.NextStep}");
                }
            }
        }

        lines.Add(
            string.Empty);

        lines.Add(
            "DEPENDENCY STATE");

        if (state.Dependencies.Count == 0)
        {
            lines.Add(
                "- No dependency evidence reported.");
        }
        else
        {
            foreach (var dependency in
                     state.Dependencies)
            {
                lines.Add(
                    $"- {dependency.Order}. " +
                    $"{dependency.Component}: " +
                    dependency.State);
            }
        }

        return string.Join(
            Environment.NewLine,
            lines);
    }
}

public sealed class UnifiedFindingsNavigationRequestedEventArgs(
    string navigationKey)
    : EventArgs
{
    public string NavigationKey { get; } =
        navigationKey;
}

public sealed class UnifiedFindingsCopyRequestedEventArgs(
    string reportText)
    : EventArgs
{
    public string ReportText { get; } =
        reportText;
}