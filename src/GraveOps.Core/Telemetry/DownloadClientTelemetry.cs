namespace GraveOps.Core.Telemetry;

public sealed class DownloadQueueTelemetry
{
    public string Name { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    public string State { get; set; } = string.Empty;
    public string Progress { get; set; } = "--";
    public double ProgressPercent { get; set; }
    public string Size { get; set; } = "--";
    public string Downloaded { get; set; } = "--";
    public string Remaining { get; set; } = "--";
    public string DownloadSpeed { get; set; } = "--";
    public string UploadSpeed { get; set; } = "--";
    public string Eta { get; set; } = "--";
    public string Ratio { get; set; } = "--";
    public string Peers { get; set; } = "--";
    public string Added { get; set; } = "--";
    public string Detail { get; set; } = string.Empty;
}

public sealed class DownloadHistoryTelemetry
{
    public string Name { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    public string State { get; set; } = string.Empty;
    public string Size { get; set; } = "--";
    public string Completed { get; set; } = "--";
    public string Duration { get; set; } = "--";
    public string Detail { get; set; } = string.Empty;
}

public sealed class DownloadClientTelemetrySnapshot :
    IApplicationTelemetrySnapshot
{
    public string ClientKey { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string State { get; set; } = "Unknown";
    public string Version { get; set; } = "--";
    public string Security { get; set; } = "--";
    public string Connection { get; set; } = "--";
    public string Detail { get; set; } = string.Empty;

    public string DownloadSpeed { get; set; } = "--";
    public string UploadSpeed { get; set; } = "--";
    public string Remaining { get; set; } = "--";
    public string Eta { get; set; } = "--";
    public string SessionDownloaded { get; set; } = "--";
    public string SessionUploaded { get; set; } = "--";
    public string RateLimit { get; set; } = "--";
    public string DiskFree { get; set; } = "--";
    public string DayDownloaded { get; set; } = "--";
    public string WeekDownloaded { get; set; } = "--";
    public string MonthDownloaded { get; set; } = "--";
    public string TotalDownloaded { get; set; } = "--";

    public int TotalCount { get; set; }
    public int ActiveCount { get; set; }
    public int DownloadingCount { get; set; }
    public int SeedingCount { get; set; }
    public int PausedCount { get; set; }
    public int StalledCount { get; set; }
    public int CompletedRecentCount { get; set; }
    public int FailedRecentCount { get; set; }
    public int DhtNodes { get; set; }

    public DateTimeOffset SampledAt { get; set; } =
        DateTimeOffset.Now;

    public List<DownloadQueueTelemetry> Queue { get; set; } =
        new();

    public List<DownloadHistoryTelemetry> History { get; set; } =
        new();

    DateTimeOffset IApplicationTelemetrySnapshot.CapturedAt =>
        SampledAt;
}
