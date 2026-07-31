using GraveOps.App.Models;

namespace GraveOps.App.Services;

/// <summary>
/// Persists meaningful fleet-state transitions without creating another polling loop.
/// Existing environment snapshots feed this service; unchanged states are ignored.
/// </summary>
public sealed class FleetHistoryService
{
    private readonly string _filePath;
    private readonly JsonSerializerOptions _json = new() { WriteIndented = true };
    private readonly Dictionary<string, string> _lastStates = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _stateGate = new();

    public ObservableCollection<FleetHistoryRecord> Items { get; } = new();

    public FleetHistoryService(string configDirectory)
    {
        _filePath = Path.Combine(configDirectory, "fleet-history.json");
        Load();
        RebuildStateIndex();
    }

    public void RecordSnapshot(EnvironmentOverviewSnapshot snapshot)
    {
        var changes = new List<FleetHistoryRecord>();

        lock (_stateGate)
        {
            Track(
                "environment",
                null,
                "Environment",
                "Fleet",
                snapshot.State.ToString(),
                snapshot.State switch
                {
                    EnvironmentHealthState.Healthy => "All configured hosts and verified applications are healthy.",
                    EnvironmentHealthState.Attention => $"{snapshot.Impacts.Count} fleet finding(s) need attention.",
                    EnvironmentHealthState.Offline => "At least one configured host is unreachable.",
                    _ => "Fleet state is still being established."
                },
                "page:Dashboard",
                changes);

            foreach (var host in snapshot.Hosts)
            {
                Track(
                    $"host:{host.ServerId:N}",
                    host.ServerId,
                    host.Name,
                    "Host",
                    host.State.ToString(),
                    host.Detail,
                    "page:Servers",
                    changes);

                foreach (var app in host.Apps)
                {
                    Track(
                        $"app:{host.ServerId:N}:{app.Name}",
                        host.ServerId,
                        host.Name,
                        app.Name,
                        app.State.ToString(),
                        app.Detail,
                        $"page:{EnvironmentImpactSnapshot.ResolvePageKey(app.Name)}",
                        changes);
                }
            }
        }

        if (changes.Count == 0)
            return;

        var persisted = UiDispatcher.Invoke(() =>
        {
            foreach (var item in changes.OrderBy(x => x.Severity))
                Items.Insert(0, item);

            while (Items.Count > 750)
                Items.RemoveAt(Items.Count - 1);

            return Items.ToList();
        });

        Save(persisted);
    }

    public IncidentReplaySnapshot ReplayAround(
        DateTimeOffset center,
        ActivityService activity,
        TimeSpan? window = null)
    {
        var span = window ?? TimeSpan.FromMinutes(10);
        var start = center - span;
        var end = center + span;

        var health = UiDispatcher.Invoke(() => Items.ToList());
        var actions = UiDispatcher.Invoke(() => activity.Recent.ToList());

        return new IncidentReplaySnapshot
        {
            CenterTime = center,
            HealthEvents = health
                .Where(x => x.Timestamp >= start && x.Timestamp <= end)
                .OrderBy(x => x.Timestamp)
                .ToList(),
            ActivityEvents = actions
                .Where(x => x.Timestamp >= start && x.Timestamp <= end)
                .OrderBy(x => x.Timestamp)
                .ToList()
        };
    }

    public void Clear()
    {
        lock (_stateGate)
            _lastStates.Clear();

        UiDispatcher.Invoke(Items.Clear);
        Save(Array.Empty<FleetHistoryRecord>());
    }

    private void Track(
        string key,
        Guid? serverId,
        string host,
        string component,
        string state,
        string detail,
        string deepLink,
        List<FleetHistoryRecord> changes)
    {
        if (!_lastStates.TryGetValue(key, out var previous))
        {
            _lastStates[key] = state;
            return;
        }

        if (previous.Equals(state, StringComparison.OrdinalIgnoreCase))
            return;

        _lastStates[key] = state;
        changes.Add(new FleetHistoryRecord
        {
            ServerId = serverId,
            Host = host,
            Component = component,
            FromState = previous,
            ToState = state,
            Detail = detail,
            DeepLink = deepLink,
            Severity = SeverityFor(state)
        });
    }

    private static FleetEventSeverity SeverityFor(string state) =>
        state.ToLowerInvariant() switch
        {
            "offline" => FleetEventSeverity.Offline,
            "attention" or "degraded" or "stale" => FleetEventSeverity.Attention,
            "healthy" or "online" => FleetEventSeverity.Healthy,
            _ => FleetEventSeverity.Info
        };

    private void RebuildStateIndex()
    {
        foreach (var item in Items.OrderByDescending(x => x.Timestamp))
        {
            var key = item.Component.Equals("Fleet", StringComparison.OrdinalIgnoreCase)
                ? "environment"
                : item.Component.Equals("Host", StringComparison.OrdinalIgnoreCase)
                    ? $"host:{item.ServerId:N}"
                    : $"app:{item.ServerId:N}:{item.Component}";

            if (!_lastStates.ContainsKey(key) && !string.IsNullOrWhiteSpace(item.ToState))
                _lastStates[key] = item.ToState;
        }
    }

    private void Load()
    {
        try
        {
            if (!File.Exists(_filePath))
                return;

            var saved = JsonSerializer.Deserialize<List<FleetHistoryRecord>>(
                File.ReadAllText(_filePath),
                _json) ?? new();

            foreach (var item in saved.OrderByDescending(x => x.Timestamp).Take(750))
                Items.Add(item);
        }
        catch
        {
            // History is diagnostic only and must never block startup.
        }
    }

    private void Save(IReadOnlyCollection<FleetHistoryRecord> items)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_filePath)!);
            var temp = _filePath + ".tmp";
            File.WriteAllText(
                temp,
                JsonSerializer.Serialize(items, _json),
                new UTF8Encoding(false));
            File.Move(temp, _filePath, true);
        }
        catch
        {
            // History persistence is best effort.
        }
    }
}
