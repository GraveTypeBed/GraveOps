namespace GraveOps.Core.Telemetry;

public sealed class PlexSessionTelemetry
{
    public string Title { get; set; } = string.Empty;
    public string User { get; set; } = string.Empty;
    public string Player { get; set; } = string.Empty;
    public string State { get; set; } = string.Empty;
    public string Progress { get; set; } = "--";
    public string VideoDecision { get; set; } = "--";
    public string AudioDecision { get; set; } = "--";
    public string Bandwidth { get; set; } = "--";
    public string Detail { get; set; } = string.Empty;
}

public sealed class PlexTelemetrySnapshot :
    IApplicationTelemetrySnapshot
{
    public string State { get; set; } = "Unknown";
    public string Service { get; set; } = "--";
    public string ServiceDetail { get; set; } = "--";
    public string Version { get; set; } = "--";
    public string Endpoint { get; set; } =
        "http://127.0.0.1:32400/web";
    public string Connection { get; set; } = "--";
    public string Security { get; set; } = "--";
    public string Dependency { get; set; } = "--";
    public string Detail { get; set; } = string.Empty;
    public int ActiveSessions { get; set; }
    public int DirectPlayCount { get; set; }
    public int DirectStreamCount { get; set; }
    public int TranscodeCount { get; set; }
    public int LibraryCount { get; set; }
    public string TotalBandwidth { get; set; } = "0 Kbps";
    public DateTimeOffset SampledAt { get; set; } =
        DateTimeOffset.Now;
    public List<PlexSessionTelemetry> Sessions { get; set; } =
        new();

    DateTimeOffset IApplicationTelemetrySnapshot.CapturedAt =>
        SampledAt;
}
