namespace GraveOps.App.Models;

public enum ActivityLevel
{
    Info,
    Success,
    Warning,
    Error
}

public sealed class ActivityRecord
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public DateTimeOffset Timestamp { get; set; } = DateTimeOffset.Now;
    public ActivityLevel Level { get; set; } = ActivityLevel.Info;
    public string Title { get; set; } = "Activity";
    public string Detail { get; set; } = "";
    public double? DurationSeconds { get; set; }
    public Guid? ServerId { get; set; }
    public string DeepLink { get; set; } = "";

    public string TimeText => Timestamp.ToLocalTime().ToString("HH:mm");
    public string DurationText => DurationSeconds is { } d ? $"{d:0.0}s" : "";
    public string StatusText => Level.ToString().ToUpperInvariant();
}

public enum SearchItemKind
{
    Page,
    Server,
    Application,
    Action,
    Setting
}

public sealed class SearchEntry
{
    public string Key { get; init; } = "";
    public SearchItemKind Kind { get; init; }
    public string Title { get; init; } = "";
    public string Subtitle { get; init; } = "";
    public override string ToString() => Title;
}

public sealed record ActionRunResult(
    bool Success,
    int ExitCode,
    string Output,
    string Error,
    string Verification,
    TimeSpan Duration);