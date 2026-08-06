using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace GraveOps.Desktop.Linux;

public enum UiProjectionArea
{
    Refresh,
    Navigation,
    Dashboard,
    CurrentPage,
    Settings,
    LongList
}

public sealed record UiProjectionRecord(
    DateTimeOffset Timestamp,
    long Generation,
    UiProjectionArea Area,
    string Key,
    int ItemCount,
    bool Applied,
    bool SkippedUnchanged,
    long ApplyMilliseconds,
    bool OverBudget,
    string Signature);

public sealed record UiProjectionSummary(
    int Retained,
    int Applied,
    int Skipped,
    long LastApplyMilliseconds,
    long P95ApplyMilliseconds,
    int OverBudgetCount);

public sealed record UiStableReconcileResult(
    int Inserted,
    int Removed,
    int Moved,
    int Updated,
    int Preserved)
{
    public int Changes => Inserted + Removed + Moved + Updated;
}

public sealed class UiDataPipelineSettings
{
    public bool Enabled { get; set; } = true;
    public bool SkipUnchangedProjection { get; set; } = true;
    public int SlowApplyMilliseconds { get; set; } = 16;
    public int LongListLimit { get; set; } = 1000;
    public int RetainedMetrics { get; set; } = 500;

    public UiDataPipelineSettings Clone() => new()
    {
        Enabled = Enabled,
        SkipUnchangedProjection = SkipUnchangedProjection,
        SlowApplyMilliseconds = SlowApplyMilliseconds,
        LongListLimit = LongListLimit,
        RetainedMetrics = RetainedMetrics
    };

    public static UiDataPipelineSettings Normalize(
        UiDataPipelineSettings? settings)
    {
        var value = settings?.Clone() ?? new UiDataPipelineSettings();
        value.SlowApplyMilliseconds = Math.Clamp(
            value.SlowApplyMilliseconds,
            4,
            250);
        value.LongListLimit = Math.Clamp(
            value.LongListLimit,
            100,
            10000);
        value.RetainedMetrics = Math.Clamp(
            value.RetainedMetrics,
            50,
            5000);
        return value;
    }
}

public sealed class UiDataPipelineStore
{
    private readonly object _gate = new();
    private readonly string _settingsPath;
    private readonly string _metricsPath;
    private readonly JsonSerializerOptions _json = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };
    private UiDataPipelineSettings _settings;
    private readonly List<UiProjectionRecord> _records = new();
    private readonly object _writeGate = new();
    private Task _pendingWrite = Task.CompletedTask;

    public UiDataPipelineStore(
        string? configRoot = null,
        string? cacheRoot = null)
    {
        var config = ResolveRoot(
            configRoot,
            "XDG_CONFIG_HOME",
            ".config");
        var cache = ResolveRoot(
            cacheRoot,
            "XDG_CACHE_HOME",
            ".cache");
        _settingsPath = Path.Combine(
            config,
            "GraveOps",
            "ui-data-pipeline.json");
        _metricsPath = Path.Combine(
            cache,
            "GraveOps",
            "ui-projection-performance.jsonl");
        _settings = LoadSettings();
    }

    public string SettingsPath => _settingsPath;
    public string MetricsPath => _metricsPath;

    public UiDataPipelineSettings GetSettings()
    {
        lock (_gate)
            return _settings.Clone();
    }

    public void SetSettings(UiDataPipelineSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        lock (_gate)
        {
            _settings = UiDataPipelineSettings.Normalize(settings);
            SaveSettings();
            TrimInMemory();
        }
    }

    public void Record(UiProjectionRecord record)
    {
        lock (_gate)
        {
            _records.Add(record);
            TrimInMemory();
        }

        lock (_writeGate)
        {
            _pendingWrite = _pendingWrite.ContinueWith(
                _ => AppendMetric(record),
                CancellationToken.None,
                TaskContinuationOptions.None,
                TaskScheduler.Default);
        }
    }

    public UiProjectionSummary Summary()
    {
        UiProjectionRecord[] records;
        lock (_gate)
            records = _records.ToArray();

        var applied = records
            .Where(item => item.Applied)
            .OrderBy(item => item.ApplyMilliseconds)
            .ToArray();
        var p95 = applied.Length == 0
            ? 0
            : applied[
                Math.Clamp(
                    (int)Math.Ceiling(applied.Length * 0.95) - 1,
                    0,
                    applied.Length - 1)]
                .ApplyMilliseconds;
        return new UiProjectionSummary(
            records.Length,
            applied.Length,
            records.Count(item => item.SkippedUnchanged),
            applied.LastOrDefault()?.ApplyMilliseconds ?? 0,
            p95,
            records.Count(item => item.OverBudget));
    }

    public bool FlushMetrics(TimeSpan? timeout = null)
    {
        Task pending;
        lock (_writeGate)
            pending = _pendingWrite;
        try
        {
            return pending.Wait(timeout ?? TimeSpan.FromSeconds(5));
        }
        catch
        {
            return false;
        }
    }

    private UiDataPipelineSettings LoadSettings()
    {
        try
        {
            if (!File.Exists(_settingsPath))
                return new UiDataPipelineSettings();
            return UiDataPipelineSettings.Normalize(
                JsonSerializer.Deserialize<UiDataPipelineSettings>(
                    File.ReadAllText(_settingsPath),
                    _json));
        }
        catch
        {
            return new UiDataPipelineSettings();
        }
    }

    private void SaveSettings()
    {
        var directory = Path.GetDirectoryName(_settingsPath)!;
        Directory.CreateDirectory(directory);
        var temporary = _settingsPath + ".tmp";
        File.WriteAllText(
            temporary,
            JsonSerializer.Serialize(_settings, _json));
        File.Move(temporary, _settingsPath, overwrite: true);
    }

    private void AppendMetric(UiProjectionRecord record)
    {
        try
        {
            var directory = Path.GetDirectoryName(_metricsPath)!;
            Directory.CreateDirectory(directory);
            File.AppendAllText(
                _metricsPath,
                JsonSerializer.Serialize(record) + Environment.NewLine);
            var info = new FileInfo(_metricsPath);
            if (info.Length <= 2 * 1024 * 1024)
                return;
            var retained = File.ReadLines(_metricsPath)
                .TakeLast(GetSettings().RetainedMetrics)
                .ToArray();
            var temporary = _metricsPath + ".tmp";
            File.WriteAllLines(temporary, retained);
            File.Move(temporary, _metricsPath, overwrite: true);
        }
        catch
        {
            // UI self-observability must never interrupt application work.
        }
    }

    private void TrimInMemory()
    {
        var retain = _settings.RetainedMetrics;
        if (_records.Count <= retain)
            return;
        _records.RemoveRange(0, _records.Count - retain);
    }

    private static string ResolveRoot(
        string? explicitRoot,
        string environmentName,
        string fallbackDirectory)
    {
        var root = explicitRoot;
        if (string.IsNullOrWhiteSpace(root))
            root = Environment.GetEnvironmentVariable(environmentName);
        if (!string.IsNullOrWhiteSpace(root))
            return root;
        return Path.Combine(
            Environment.GetFolderPath(
                Environment.SpecialFolder.UserProfile),
            fallbackDirectory);
    }
}

public sealed class UiDataPipeline : IDisposable
{
    private readonly object _gate = new();
    private readonly Dictionary<string, string> _signatures =
        new(StringComparer.Ordinal);
    private readonly UiDataPipelineStore _store;
    private long _generation;
    private bool _disposed;

    public UiDataPipeline(UiDataPipelineStore? store = null)
    {
        _store = store ?? new UiDataPipelineStore();
    }

    public UiDataPipelineSettings Settings => _store.GetSettings();
    public string SettingsPath => _store.SettingsPath;
    public string MetricsPath => _store.MetricsPath;
    public long Generation => Interlocked.Read(ref _generation);

    public long BeginRefresh() =>
        Interlocked.Increment(ref _generation);

    public void SetSettings(UiDataPipelineSettings settings)
    {
        ThrowIfDisposed();
        _store.SetSettings(settings);
        Invalidate();
    }

    public bool Project(
        UiProjectionArea area,
        string key,
        string signature,
        int itemCount,
        Action apply,
        bool force = false)
    {
        ArgumentNullException.ThrowIfNull(apply);
        ThrowIfDisposed();
        var settings = Settings;
        var scope = $"{area}:{key}";
        var generation = Generation;
        if (settings.Enabled &&
            settings.SkipUnchangedProjection &&
            !force)
        {
            lock (_gate)
            {
                if (_signatures.TryGetValue(scope, out var previous) &&
                    previous.Equals(signature, StringComparison.Ordinal))
                {
                    _store.Record(new UiProjectionRecord(
                        DateTimeOffset.UtcNow,
                        generation,
                        area,
                        key,
                        itemCount,
                        Applied: false,
                        SkippedUnchanged: true,
                        ApplyMilliseconds: 0,
                        OverBudget: false,
                        signature));
                    return false;
                }
            }
        }

        var stopwatch = Stopwatch.StartNew();
        apply();
        stopwatch.Stop();
        lock (_gate)
            _signatures[scope] = signature;
        _store.Record(new UiProjectionRecord(
            DateTimeOffset.UtcNow,
            generation,
            area,
            key,
            itemCount,
            Applied: true,
            SkippedUnchanged: false,
            stopwatch.ElapsedMilliseconds,
            stopwatch.ElapsedMilliseconds > settings.SlowApplyMilliseconds,
            signature));
        return true;
    }

    public void RecordExternal(
        UiProjectionArea area,
        string key,
        int itemCount,
        long milliseconds,
        string signature = "")
    {
        ThrowIfDisposed();
        var settings = Settings;
        _store.Record(new UiProjectionRecord(
            DateTimeOffset.UtcNow,
            Generation,
            area,
            key,
            itemCount,
            Applied: true,
            SkippedUnchanged: false,
            milliseconds,
            milliseconds > settings.SlowApplyMilliseconds,
            signature));
    }

    public UiProjectionSummary Summary() => _store.Summary();

    public bool FlushMetrics(TimeSpan? timeout = null)
    {
        ThrowIfDisposed();
        return _store.FlushMetrics(timeout);
    }

    public void Invalidate(string? scope = null)
    {
        lock (_gate)
        {
            if (string.IsNullOrWhiteSpace(scope))
            {
                _signatures.Clear();
                return;
            }
            foreach (var key in _signatures.Keys
                         .Where(item => item.StartsWith(
                             scope,
                             StringComparison.Ordinal))
                         .ToArray())
            {
                _signatures.Remove(key);
            }
        }
    }

    public static string Signature(IEnumerable<string?> values)
    {
        var normalized = string.Join(
            '\u001f',
            values.Select(value => value ?? string.Empty));
        return Convert.ToHexString(
            SHA256.HashData(Encoding.UTF8.GetBytes(normalized)));
    }

    public static IReadOnlyList<T> Bound<T>(
        IEnumerable<T> source,
        int limit)
    {
        ArgumentNullException.ThrowIfNull(source);
        return source.Take(Math.Max(0, limit)).ToArray();
    }

    public static UiStableReconcileResult ReconcileByKey<T, TKey>(
        IList<T> target,
        IReadOnlyList<T> desired,
        Func<T, TKey> keySelector,
        Func<T, T, T>? merge = null)
        where TKey : notnull
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(desired);
        ArgumentNullException.ThrowIfNull(keySelector);
        var inserted = 0;
        var removed = 0;
        var moved = 0;
        var updated = 0;
        var preserved = 0;
        var desiredKeys = desired
            .Select(keySelector)
            .ToHashSet();

        for (var index = target.Count - 1; index >= 0; index--)
        {
            if (desiredKeys.Contains(keySelector(target[index])))
                continue;
            target.RemoveAt(index);
            removed++;
        }

        for (var index = 0; index < desired.Count; index++)
        {
            var next = desired[index];
            var key = keySelector(next);
            var existingIndex = -1;
            for (var candidate = index; candidate < target.Count; candidate++)
            {
                if (EqualityComparer<TKey>.Default.Equals(
                        keySelector(target[candidate]),
                        key))
                {
                    existingIndex = candidate;
                    break;
                }
            }

            if (existingIndex < 0)
            {
                target.Insert(index, next);
                inserted++;
                continue;
            }

            var existing = target[existingIndex];
            if (existingIndex != index)
            {
                target.RemoveAt(existingIndex);
                target.Insert(index, existing);
                moved++;
            }

            var merged = merge is null
                ? existing
                : merge(existing, next);
            if (!ReferenceEquals(merged, existing) &&
                !EqualityComparer<T>.Default.Equals(merged, existing))
            {
                target[index] = merged;
                updated++;
            }
            else
            {
                preserved++;
            }
        }

        return new UiStableReconcileResult(
            inserted,
            removed,
            moved,
            updated,
            preserved);
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _store.FlushMetrics(TimeSpan.FromSeconds(5));
        _disposed = true;
        lock (_gate)
            _signatures.Clear();
    }

    private void ThrowIfDisposed()
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(UiDataPipeline));
    }
}
