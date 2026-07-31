namespace GraveOps.App.Models;

public enum FleetEventSeverity
{
    Info = 0,
    Healthy = 1,
    Attention = 2,
    Offline = 3
}

public sealed class FleetHistoryRecord
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public DateTimeOffset Timestamp { get; set; } = DateTimeOffset.Now;
    public Guid? ServerId { get; set; }
    public string Host { get; set; } = "";
    public string Component { get; set; } = "Environment";
    public string Category { get; set; } = "Health";
    public string FromState { get; set; } = "";
    public string ToState { get; set; } = "";
    public string Detail { get; set; } = "";
    public string DeepLink { get; set; } = "page:Dashboard";
    public FleetEventSeverity Severity { get; set; } = FleetEventSeverity.Info;

    public string TimeText => Timestamp.ToLocalTime().ToString("MM/dd HH:mm:ss");
    public string SeverityText => Severity.ToString().ToUpperInvariant();
    public string TransitionText =>
        string.IsNullOrWhiteSpace(FromState)
            ? ToState
            : $"{FromState} → {ToState}";
}

public sealed class IncidentReplaySnapshot
{
    public DateTimeOffset CenterTime { get; set; }
    public List<FleetHistoryRecord> HealthEvents { get; set; } = new();
    public List<ActivityRecord> ActivityEvents { get; set; } = new();
}
