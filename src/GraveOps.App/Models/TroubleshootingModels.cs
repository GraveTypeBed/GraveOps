using System.Text.Json.Serialization;

namespace GraveOps.App.Models;

public sealed class IncidentReport
{
    public string Headline { get; set; } = "Analyzing...";
    public string RootCause { get; set; } = "Unknown";
    public string Severity { get; set; } = "INFO";
    public List<string> Findings { get; set; } = new();
    public List<string> Recommendations { get; set; } = new();
    public string Raw { get; set; } = "";
    public DateTimeOffset Timestamp { get; set; } = DateTimeOffset.Now;
}

public sealed class SystemStateSnapshot
{
    public string Health { get; set; } = "--";
    public string Plex { get; set; } = "--";
    // Preserve the old JSON field name while exposing only the current domain term in code.
    [JsonPropertyName("Dumb")]
    public string Infrastructure { get; set; } = "--";
    public string FailedUnits { get; set; } = "--";
    public string Mounts { get; set; } = "--";
    public string Backup { get; set; } = "--";
    public string Uptime { get; set; } = "--";

    public IEnumerable<string> Lines()
    {
        yield return $"Health: {Health}";
        yield return $"Plex: {Plex}";
        yield return $"Infrastructure: {Infrastructure}";
        yield return $"Provider checks: {FailedUnits}";
        yield return $"Storage roots: {Mounts}";
        yield return $"Backup: {Backup}";
        yield return $"Uptime: {Uptime}";
    }
}