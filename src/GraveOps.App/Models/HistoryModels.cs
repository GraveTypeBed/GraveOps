namespace GraveOps.App.Models;

public sealed class SavedStateRecord
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ServerId { get; set; }
    public string ServerName { get; set; } = "";
    public string Label { get; set; } = "Manual snapshot";
    public DateTimeOffset Timestamp { get; set; } = DateTimeOffset.Now;
    public SystemStateSnapshot Snapshot { get; set; } = new();

    public string TimeText => Timestamp.ToLocalTime().ToString("MM/dd/yyyy HH:mm:ss");
    public string Summary =>
        $"Health {Snapshot.Health} | Infrastructure {Snapshot.Infrastructure} | Storage {Snapshot.Mounts}";
}