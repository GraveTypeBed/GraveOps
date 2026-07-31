namespace GraveOps.App.Models;

public sealed class ControlPlaneNode
{
    public string Key { get; set; } = "";
    public string Component { get; set; } = "";
    public string State { get; set; } = "UNKNOWN";
    public string Severity { get; set; } = "INFO";
    public string Summary { get; set; } = "";
    public string DependsOn { get; set; } = "";
    public string Feeds { get; set; } = "";
    public string DeepLink { get; set; } = "";
    public string DrillTarget { get; set; } = "";

    public string DependencyText =>
        string.IsNullOrWhiteSpace(DependsOn)
            ? "Root dependency"
            : $"Depends on {DependsOn}";

    public string ImpactText =>
        string.IsNullOrWhiteSpace(Feeds)
            ? ""
            : $"Feeds {Feeds}";
}

public sealed class ControlPlaneFinding
{
    public string Severity { get; set; } = "INFO";
    public string Component { get; set; } = "";
    public string Problem { get; set; } = "";
    public string Evidence { get; set; } = "";
    public string Impact { get; set; } = "";
    public string NextStep { get; set; } = "";
    public string DeepLink { get; set; } = "";
    public string DrillTarget { get; set; } = "";
    public string ActionName { get; set; } = "";

    public int Rank => SeverityRank(Severity);

    public string DetailText =>
        $"Component: {Component}\n" +
        $"Severity: {Severity}\n" +
        $"Problem: {Problem}\n\n" +
        $"Evidence\n{Evidence}\n\n" +
        $"Why it matters\n{Impact}\n\n" +
        $"Recommended next step\n{NextStep}" +
        (string.IsNullOrWhiteSpace(ActionName)
            ? ""
            : $"\n\nProtected GraveOps action\n{ActionName}");

    public static int SeverityRank(string severity)
        => (severity ?? "").Trim().ToUpperInvariant() switch
        {
            "CRITICAL" => 5,
            "ERROR" => 4,
            "WARNING" => 3,
            "INFO" => 2,
            "HEALTHY" => 1,
            _ => 0
        };
}

public sealed class ControlPlaneIntelligenceSnapshot
{
    public DateTimeOffset Timestamp { get; set; } = DateTimeOffset.Now;
    public string ServerName { get; set; } = "";
    public string OverallSeverity { get; set; } = "HEALTHY";
    public string RootCause { get; set; } = "No critical fault detected";
    public string Headline { get; set; } = "Control plane is healthy.";
    public List<ControlPlaneNode> Nodes { get; set; } = new();
    public List<ControlPlaneFinding> Findings { get; set; } = new();
    public List<string> ProbeNotes { get; set; } = new();

    public int BlockerCount =>
        Findings.Count(
            x => x.Severity is "CRITICAL" or "ERROR");

    public int WarningCount =>
        Findings.Count(
            x => x.Severity == "WARNING");

    public int InfoCount =>
        Findings.Count(
            x => x.Severity == "INFO");

    public string StatusLine =>
        $"{OverallSeverity} | {BlockerCount} blocker(s) | {WarningCount} warning(s) | {InfoCount} observation(s)";

    public string BuildReport()
    {
        var sb = new System.Text.StringBuilder();

        sb.AppendLine("GRAVEOPS CONTROL PLANE INTELLIGENCE");
        sb.AppendLine($"Generated: {Timestamp:O}");
        sb.AppendLine($"Target: {ServerName}");
        sb.AppendLine($"Overall: {OverallSeverity}");
        sb.AppendLine($"Root cause: {RootCause}");
        sb.AppendLine(Headline);
        sb.AppendLine();

        sb.AppendLine("DEPENDENCY STATE");

        foreach (var node in Nodes)
        {
            sb.AppendLine(
                $"[{node.Severity}] {node.Component}: {node.State} - {node.Summary}");

            if (!string.IsNullOrWhiteSpace(node.DependsOn))
                sb.AppendLine($"  Depends on: {node.DependsOn}");

            if (!string.IsNullOrWhiteSpace(node.Feeds))
                sb.AppendLine($"  Feeds: {node.Feeds}");
        }

        sb.AppendLine();
        sb.AppendLine("PRIORITY FINDINGS");

        if (Findings.Count == 0)
        {
            sb.AppendLine("- No active findings.");
        }
        else
        {
            foreach (var finding in Findings)
            {
                sb.AppendLine(
                    $"[{finding.Severity}] {finding.Component}: {finding.Problem}");
                sb.AppendLine($"  Evidence: {finding.Evidence}");
                sb.AppendLine($"  Impact: {finding.Impact}");
                sb.AppendLine($"  Next: {finding.NextStep}");
            }
        }

        if (ProbeNotes.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("PROBE NOTES");

            foreach (var note in ProbeNotes)
                sb.AppendLine("- " + note);
        }

        return sb.ToString().TrimEnd();
    }
}