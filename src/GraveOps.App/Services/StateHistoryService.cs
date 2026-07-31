using GraveOps.App.Models;

namespace GraveOps.App.Services;

public sealed class StateHistoryService
{
    private readonly AppServices _services;
    private readonly string _filePath;
    private readonly JsonSerializerOptions _json = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    public ObservableCollection<SavedStateRecord> Items { get; } = new();

    public StateHistoryService(AppServices services)
    {
        _services = services;
        _filePath = Path.Combine(
            services.Config.DirectoryPath,
            "state-history.json");
        Load();
    }

    public async Task<SavedStateRecord> CaptureAsync(
        ServerProfile server,
        string label = "Manual snapshot",
        CancellationToken token = default)
    {
        var job = _services.Jobs.Begin(
            "Capture state snapshot",
            server.Id,
            "page:Services");

        using var linked = CancellationTokenSource.CreateLinkedTokenSource(token);
        _services.Jobs.RegisterCancellation(job, linked);

        try
        {
            _services.Jobs.Update(
                job,
                GraveJobState.Running,
                $"Capturing state from {server.Name}...");

            var snapshot =
                await _services.Incident.CaptureStateAsync(
                    server,
                    linked.Token);

            var item = new SavedStateRecord
            {
                ServerId = server.Id,
                ServerName = server.Name,
                Label = string.IsNullOrWhiteSpace(label)
                    ? "Manual snapshot"
                    : label.Trim(),
                Snapshot = snapshot
            };

            Items.Insert(0, item);
            while (Items.Count > 100)
                Items.RemoveAt(Items.Count - 1);

            Save();

            _services.Jobs.Update(
                job,
                GraveJobState.Success,
                $"Saved state snapshot for {server.Name}.",
                100);

            _services.Activity.Record(
                "State snapshot saved",
                $"{server.Name}\n{string.Join(Environment.NewLine, snapshot.Lines())}",
                ActivityLevel.Info,
                serverId: server.Id,
                deepLink: "page:Services");

            return item;
        }
        catch (OperationCanceledException)
        {
            _services.Jobs.Update(
                job,
                GraveJobState.Cancelled,
                "State capture cancelled.");
            throw;
        }
        catch (Exception ex)
        {
            _services.Jobs.Update(
                job,
                GraveJobState.Failed,
                ex.Message);

            _services.Activity.Record(
                "State snapshot failed",
                ex.Message,
                ActivityLevel.Error,
                serverId: server.Id,
                deepLink: "page:Services");

            throw;
        }
        finally
        {
            _services.Jobs.ReleaseCancellation(job);
        }
    }

    public async Task<string> CompareToLiveAsync(
        SavedStateRecord record,
        CancellationToken token = default)
    {
        var server =
            _services.Config.Current.Servers
                .FirstOrDefault(x => x.Id == record.ServerId)
            ?? _services.Context.Current;

        if (server is null)
            throw new InvalidOperationException(
                "The server used for this saved state is no longer available.");

        var live =
            await _services.Incident.CaptureStateAsync(
                server,
                token);

        return IncidentService.Compare(record.Snapshot, live);
    }

    public void Delete(SavedStateRecord? record)
    {
        if (record is null) return;
        Items.Remove(record);
        Save();
    }

    private void Load()
    {
        try
        {
            if (!File.Exists(_filePath))
                return;

            var saved =
                JsonSerializer.Deserialize<List<SavedStateRecord>>(
                    File.ReadAllText(_filePath),
                    _json) ?? new();

            foreach (var item in saved
                         .OrderByDescending(x => x.Timestamp)
                         .Take(100))
            {
                Items.Add(item);
            }
        }
        catch
        {
            // State history must never prevent GraveOps from starting.
        }
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
            // Snapshot persistence is best effort.
        }
    }
}