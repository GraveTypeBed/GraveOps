namespace GraveOps.App.Models;

public enum AppHealthState
{
    Healthy,
    Degraded,
    Busy,
    Stale,
    Offline
}

public sealed class AppHealthCard
{
    public Guid AppId { get; set; }
    public string Name { get; set; } = "Application";
    public string Category { get; set; } = "Other";
    public AppHealthState Health { get; set; } = AppHealthState.Offline;
    public string State => Health.ToString();
    public int HttpCode { get; set; }
    public long LatencyMs { get; set; }
    public string Version { get; set; } = "";
    public int? QueueCount { get; set; }
    public int? HealthIssueCount { get; set; }
    public string Detail { get; set; } = "";
    public string ResolvedUrl { get; set; } = "";
    public DateTimeOffset SampledAt { get; set; } = DateTimeOffset.Now;

    public string LatencyText => LatencyMs > 0 ? $"{LatencyMs} ms" : "--";
    public string HttpText => HttpCode > 0 ? $"HTTP {HttpCode}" : "No response";
    public string VersionText => string.IsNullOrWhiteSpace(Version) ? "Version --" : $"v{Version}";
    public string QueueText => QueueCount is { } q ? $"Queue {q}" : "Queue --";
    public string IssuesText => HealthIssueCount is { } h ? $"Health {h}" : "Health --";
    public string AttentionText
    {
        get
        {
            if (Health == AppHealthState.Offline) return "Offline - no endpoint response";
            if (Health == AppHealthState.Busy && QueueCount is > 0) return $"Busy - {QueueCount} queued";
            if (Health == AppHealthState.Degraded && HealthIssueCount is > 0) return $"Degraded - {HealthIssueCount} health issue(s)";
            if (Health == AppHealthState.Stale) return "Stale - telemetry needs refresh";
            if (!string.IsNullOrWhiteSpace(Detail)) return Detail;
            return "Healthy - operating normally";
        }
    }
}

public sealed class PlexTelemetry
{
    public string ServiceState { get; set; } = "Unknown";
    public string Version { get; set; } = "--";
    public string EndpointState { get; set; } = "Unknown";
    public int HttpCode { get; set; }
    public long LatencyMs { get; set; }
    public string DockerDependency { get; set; } = "Host/runtime unknown";
}

public sealed class SabTelemetry
{
    public string State { get; set; } = "Unknown";
    public string Speed { get; set; } = "--";
    public string Remaining { get; set; } = "--";
    public int QueueCount { get; set; }
    public string Detail { get; set; } = "";
}

public sealed class QbitTelemetry
{
    public string State { get; set; } = "Unknown";
    public string DownloadSpeed { get; set; } = "--";
    public string UploadSpeed { get; set; } = "--";
    public string Detail { get; set; } = "";
}

public sealed class MediaOperationsSnapshot
{
    public Guid ServerId { get; set; }
    public DateTimeOffset SampledAt { get; set; } = DateTimeOffset.Now;
    public List<AppHealthCard> Apps { get; set; } = new();
    public PlexTelemetry Plex { get; set; } = new();
    public SabTelemetry Sab { get; set; } = new();
    public QbitTelemetry Qbit { get; set; } = new();

    public int HealthyCount => Apps.Count(x => x.Health == AppHealthState.Healthy);
    public int DegradedCount => Apps.Count(x => x.Health is AppHealthState.Degraded or AppHealthState.Busy or AppHealthState.Stale);
    public int OfflineCount => Apps.Count(x => x.Health == AppHealthState.Offline);
}