using System.Collections.Concurrent;
using GraveOps.App.Models;

namespace GraveOps.App.Services;

public sealed class JobService
{
    private readonly string _filePath;
    private readonly JsonSerializerOptions _json = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    private readonly ConcurrentDictionary<Guid, CancellationTokenSource> _cancellation = new();

    public ObservableCollection<GraveJob> Items { get; } = new();
    public event Action? Changed;

    public JobService()
    {
        var root = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "GraveOps");

        Directory.CreateDirectory(root);
        _filePath = Path.Combine(root, "jobs.json");
        Load();
    }

    public GraveJob Begin(string title, Guid? serverId = null, string deepLink = "")
    {
        var job = new GraveJob
        {
            Title = title,
            ServerId = serverId,
            DeepLink = deepLink,
            State = GraveJobState.Running,
            Detail = "Running"
        };

        Dispatch(() =>
        {
            Items.Insert(0, job);
            Trim();
            Save();
            Changed?.Invoke();
        });

        return job;
    }

    public void Update(
        GraveJob job,
        GraveJobState state,
        string detail,
        double? progress = null)
    {
        Dispatch(() =>
        {
            job.State = state;
            job.Detail = detail;
            job.Progress = progress;

            if (state is GraveJobState.Success or GraveJobState.Failed or GraveJobState.Cancelled)
                job.Completed ??= DateTimeOffset.Now;

            Save();
            Changed?.Invoke();
        });
    }

    public void RegisterCancellation(GraveJob job, CancellationTokenSource source)
    {
        _cancellation[job.Id] = source;
        Changed?.Invoke();
    }

    public void ReleaseCancellation(GraveJob job)
    {
        _cancellation.TryRemove(job.Id, out _);
        Changed?.Invoke();
    }

    public bool CanCancel(GraveJob? job)
        => job is not null &&
           job.State is GraveJobState.Running or GraveJobState.Queued &&
           _cancellation.ContainsKey(job.Id);

    public bool RequestCancel(GraveJob? job)
    {
        if (job is null || !_cancellation.TryGetValue(job.Id, out var source))
            return false;

        try
        {
            source.Cancel();
            Dispatch(() =>
            {
                job.Detail = "Cancellation requested...";
                Save();
                Changed?.Invoke();
            });
            return true;
        }
        catch
        {
            return false;
        }
    }

    public int ClearCompleted()
    {
        var removed = 0;
        Dispatch(() =>
        {
            var completed = Items
                .Where(x => x.State is GraveJobState.Success or GraveJobState.Failed or GraveJobState.Cancelled)
                .ToList();

            foreach (var item in completed)
            {
                Items.Remove(item);
                removed++;
            }

            Save();
            Changed?.Invoke();
        });
        return removed;
    }

    public int RunningCount
        => Items.Count(x => x.State is GraveJobState.Running or GraveJobState.Queued);

    private void Load()
    {
        try
        {
            if (!File.Exists(_filePath))
                return;

            var saved =
                JsonSerializer.Deserialize<List<GraveJob>>(
                    File.ReadAllText(_filePath),
                    _json) ?? new();

            var repaired = false;

            foreach (var job in saved
                         .OrderByDescending(x => x.Started)
                         .Take(250))
            {
                if (job.State is GraveJobState.Running or GraveJobState.Queued)
                {
                    job.State = GraveJobState.Cancelled;
                    job.Completed = DateTimeOffset.Now;
                    job.Detail = "Interrupted when the previous GraveOps session ended.";
                    repaired = true;
                }

                Items.Add(job);
            }

            if (repaired)
                Save();
        }
        catch
        {
            // Job history must never prevent GraveOps from starting.
        }
    }

    private void Trim()
    {
        while (Items.Count > 250)
            Items.RemoveAt(Items.Count - 1);
    }

    private void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_filePath)!);
            var temp = _filePath + ".tmp";
            File.WriteAllText(
                temp,
                JsonSerializer.Serialize(Items.ToList(), _json),
                new UTF8Encoding(false));
            File.Move(temp, _filePath, true);
        }
        catch
        {
            // Job persistence is best effort and never blocks an operation.
        }
    }

    private static void Dispatch(Action action)
    {
        var dispatcher = System.Windows.Application.Current?.Dispatcher;
        if (dispatcher is null || dispatcher.CheckAccess())
            action();
        else
            dispatcher.Invoke(action);
    }
}