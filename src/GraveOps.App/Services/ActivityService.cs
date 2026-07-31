using GraveOps.App.Models;

namespace GraveOps.App.Services;

public sealed class ActivityService
{
    private readonly string _filePath;
    private readonly JsonSerializerOptions _json = new() { WriteIndented = true };

    public ObservableCollection<ActivityRecord> Recent { get; } = new();
    public event Action<ActivityRecord>? ActivityAdded;

    public ActivityService(string configDirectory)
    {
        _filePath = Path.Combine(configDirectory, "activity.json");
        Load();
    }

    public ActivityRecord Record(
        string title,
        string detail = "",
        ActivityLevel level = ActivityLevel.Info,
        double? durationSeconds = null,
        Guid? serverId = null,
        string deepLink = "")
    {
        var item = new ActivityRecord
        {
            Timestamp = DateTimeOffset.Now,
            Title = title,
            Detail = detail,
            Level = level,
            DurationSeconds = durationSeconds,
            ServerId = serverId,
            DeepLink = deepLink
        };

        var snapshot = UiDispatcher.Invoke(() =>
        {
            Recent.Insert(0, item);
            while (Recent.Count > 500)
                Recent.RemoveAt(Recent.Count - 1);
            return Recent.ToList();
        });

        Save(snapshot);
        ActivityAdded?.Invoke(item);
        return item;
    }

    public void Clear()
    {
        UiDispatcher.Invoke(Recent.Clear);
        Save(Array.Empty<ActivityRecord>());
    }

    private void Load()
    {
        try
        {
            if (!File.Exists(_filePath))
                return;

            var items = JsonSerializer.Deserialize<List<ActivityRecord>>(
                File.ReadAllText(_filePath),
                _json) ?? new();

            foreach (var item in items
                         .Where(x => !IsLegacyUiThreadNoise(x))
                         .OrderByDescending(x => x.Timestamp)
                         .Take(500))
                Recent.Add(item);
        }
        catch
        {
            // Activity history is best effort and must never block startup.
        }
    }


    private static bool IsLegacyUiThreadNoise(ActivityRecord item) =>
        item.Detail.Contains(
            "CollectionView does not support changes to its SourceCollection from a thread",
            StringComparison.OrdinalIgnoreCase);

    private void Save(IReadOnlyCollection<ActivityRecord> items)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_filePath)!);
            var temp = _filePath + ".tmp";
            File.WriteAllText(temp, JsonSerializer.Serialize(items, _json));
            File.Move(temp, _filePath, true);
        }
        catch
        {
            // Activity persistence is best effort and never blocks operations.
        }
    }
}
