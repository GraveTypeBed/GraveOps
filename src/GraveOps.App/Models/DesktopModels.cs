using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace GraveOps.App.Models;

public enum GraveJobState
{
    Queued,
    Running,
    Success,
    Failed,
    Cancelled
}

public sealed class GraveJob : INotifyPropertyChanged
{
    private GraveJobState _state = GraveJobState.Queued;
    private string _detail = "Queued";
    private double? _progress;
    private DateTimeOffset? _completed;

    public Guid Id { get; set; } = Guid.NewGuid();
    public string Title { get; set; } = "Job";
    public Guid? ServerId { get; set; }
    public string DeepLink { get; set; } = "";
    public DateTimeOffset Started { get; set; } = DateTimeOffset.Now;

    public GraveJobState State
    {
        get => _state;
        set
        {
            _state = value;
            Changed();
            Changed(nameof(StateText));
            Changed(nameof(DurationText));
        }
    }

    public string Detail
    {
        get => _detail;
        set
        {
            _detail = value;
            Changed();
        }
    }

    public double? Progress
    {
        get => _progress;
        set
        {
            _progress = value;
            Changed();
            Changed(nameof(ProgressText));
        }
    }

    public DateTimeOffset? Completed
    {
        get => _completed;
        set
        {
            _completed = value;
            Changed();
            Changed(nameof(DurationText));
            Changed(nameof(CompletedText));
        }
    }

    public string StateText => State.ToString().ToUpperInvariant();
    public string ProgressText => Progress is { } p ? $"{Math.Clamp(p, 0, 100):0}%" : "";
    public string DurationText => $"{((Completed ?? DateTimeOffset.Now) - Started).TotalSeconds:0.0}s";
    public string StartedText => Started.ToLocalTime().ToString("MM/dd HH:mm:ss");
    public string CompletedText => Completed?.ToLocalTime().ToString("MM/dd HH:mm:ss") ?? "";

    public event PropertyChangedEventHandler? PropertyChanged;
    private void Changed([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

public sealed class NotificationRecord : INotifyPropertyChanged
{
    private bool _isRead;
    private bool _acknowledged;

    public Guid Id { get; set; } = Guid.NewGuid();
    public DateTimeOffset Timestamp { get; set; } = DateTimeOffset.Now;
    public string Title { get; set; } = "Notification";
    public string Message { get; set; } = "";
    public string Severity { get; set; } = "INFO";
    public string DeepLink { get; set; } = "";

    public bool IsRead
    {
        get => _isRead;
        set
        {
            _isRead = value;
            Changed();
            Changed(nameof(ReadText));
        }
    }

    public bool Acknowledged
    {
        get => _acknowledged;
        set
        {
            _acknowledged = value;
            Changed();
            Changed(nameof(AckText));
        }
    }

    public string TimeText => Timestamp.ToLocalTime().ToString("HH:mm");
    public string DateText => Timestamp.ToLocalTime().ToString("MM/dd HH:mm");
    public string ReadText => IsRead ? "READ" : "NEW";
    public string AckText => Acknowledged ? "ACK" : "";

    public event PropertyChangedEventHandler? PropertyChanged;
    private void Changed([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}