using System.Drawing;
using System.Windows.Forms;
using GraveOps.App.Models;

namespace GraveOps.App.Services;

public sealed class NotificationService : IDisposable
{
    private readonly NotifyIcon _icon;
    private readonly string _filePath;
    private readonly JsonSerializerOptions _json = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    public ObservableCollection<NotificationRecord> History { get; } = new();

    public event Action? OpenRequested;
    public event Action? ExitRequested;
    public event Action? Changed;

    public int UnreadCount => History.Count(x => !x.IsRead);

    public NotificationService()
    {
        var root = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "GraveOps");

        Directory.CreateDirectory(root);
        _filePath = Path.Combine(root, "notifications.json");
        Load();

        var menu = new ContextMenuStrip();
        menu.Items.Add("Open GraveOps", null, (_, _) => OpenRequested?.Invoke());
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Exit", null, (_, _) => ExitRequested?.Invoke());

        _icon = new NotifyIcon
        {
            Icon = LoadTrayIcon(),
            Text = "GraveOps",
            Visible = true,
            ContextMenuStrip = menu
        };

        _icon.DoubleClick += (_, _) => OpenRequested?.Invoke();
    }

    public NotificationRecord Record(
        string title,
        string message,
        string severity = "INFO",
        string deepLink = "")
    {
        var item = new NotificationRecord
        {
            Title = title,
            Message = message,
            Severity = NormalizeSeverity(severity),
            DeepLink = deepLink,
            IsRead = false,
            Acknowledged = false
        };

        Dispatch(() =>
        {
            History.Insert(0, item);
            while (History.Count > 300)
                History.RemoveAt(History.Count - 1);

            Save();
            Changed?.Invoke();
        });

        return item;
    }

    public void Show(
        string title,
        string message,
        ToolTipIcon icon = ToolTipIcon.Info,
        string deepLink = "")
    {
        var severity =
            icon == ToolTipIcon.Warning ? "WARNING" :
            icon == ToolTipIcon.Error ? "ERROR" :
            "INFO";

        Record(title, message, severity, deepLink);

        _icon.BalloonTipTitle = title;
        _icon.BalloonTipText = message.Length > 250 ? message[..250] : message;
        _icon.BalloonTipIcon = icon;
        _icon.ShowBalloonTip(5000);
    }

    public void MarkRead()
    {
        Dispatch(() =>
        {
            foreach (var item in History)
                item.IsRead = true;

            Save();
            Changed?.Invoke();
        });
    }

    public void Acknowledge(NotificationRecord? item)
    {
        if (item is null) return;

        Dispatch(() =>
        {
            item.IsRead = true;
            item.Acknowledged = true;
            Save();
            Changed?.Invoke();
        });
    }

    public void AcknowledgeAll()
    {
        Dispatch(() =>
        {
            foreach (var item in History)
            {
                item.IsRead = true;
                item.Acknowledged = true;
            }

            Save();
            Changed?.Invoke();
        });
    }

    public int ClearAcknowledged()
    {
        var removed = 0;
        Dispatch(() =>
        {
            var items = History.Where(x => x.Acknowledged).ToList();
            foreach (var item in items)
            {
                History.Remove(item);
                removed++;
            }

            Save();
            Changed?.Invoke();
        });

        return removed;
    }

    private void Load()
    {
        try
        {
            if (!File.Exists(_filePath))
                return;

            var saved =
                JsonSerializer.Deserialize<List<NotificationRecord>>(
                    File.ReadAllText(_filePath),
                    _json) ?? new();

            foreach (var item in saved
                         .OrderByDescending(x => x.Timestamp)
                         .Take(300))
            {
                History.Add(item);
            }
        }
        catch
        {
            // Notification history must never prevent GraveOps from starting.
        }
    }

    private void Save()
    {
        try
        {
            var temp = _filePath + ".tmp";
            File.WriteAllText(
                temp,
                JsonSerializer.Serialize(History.ToList(), _json),
                new UTF8Encoding(false));
            File.Move(temp, _filePath, true);
        }
        catch
        {
            // Alert persistence is best effort.
        }
    }

    private static string NormalizeSeverity(string severity)
    {
        var value = (severity ?? "INFO").Trim().ToUpperInvariant();
        return value switch
        {
            "ERROR" => "ERROR",
            "WARNING" => "WARNING",
            "SUCCESS" => "SUCCESS",
            _ => "INFO"
        };
    }

    private static Icon LoadTrayIcon()
    {
        try
        {
            var exe = Environment.ProcessPath;
            if (!string.IsNullOrWhiteSpace(exe))
            {
                var icon = Icon.ExtractAssociatedIcon(exe);
                if (icon is not null)
                    return icon;
            }
        }
        catch
        {
        }

        return SystemIcons.Application;
    }

    private static void Dispatch(Action action)
    {
        var dispatcher = System.Windows.Application.Current?.Dispatcher;
        if (dispatcher is null || dispatcher.CheckAccess())
            action();
        else
            dispatcher.Invoke(action);
    }

    public void Dispose()
    {
        _icon.Visible = false;
        _icon.Dispose();
    }
}