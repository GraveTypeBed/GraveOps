namespace GraveOps.Core.Telemetry;

public sealed record ArrServiceTelemetryRow(
    string InstanceKey,
    string Service,
    string Endpoint,
    string Version,
    string Work,
    string Health,
    string Access,
    string State)
{
    public string DisplayName =>
        Service;

    public string Detail =>
        string.Join(
            " · ",
            new[]
            {
                Work,
                $"Health {Health}",
                Access
            }
            .Where(value =>
                !string.IsNullOrWhiteSpace(value)));
}

public sealed record ArrWorkItemRow(
    string Service,
    string Type,
    string ItemIssue,
    string State,
    string Progress,
    string Remaining,
    string Detail);

public sealed record ArrLiveTelemetrySnapshot(
    DateTimeOffset CapturedAt,
    IReadOnlyList<ArrServiceTelemetryRow> Services,
    IReadOnlyList<ArrWorkItemRow> WorkItems,
    string OverallState,
    string VersionSummary,
    string WorkSummary,
    string HealthSummary) :
    IApplicationTelemetrySnapshot;
