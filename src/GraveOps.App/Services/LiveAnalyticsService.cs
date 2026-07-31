using System.Windows.Threading;
using GraveOps.App.Models;

namespace GraveOps.App.Services;

public enum LiveAnalyticsDomain
{
    MediaSummary,
    QueueDetail,
    PlexSessions,
    DownloadClient,
    PiHole
}

public sealed class LiveAnalyticsUpdateEventArgs : EventArgs
{
    public LiveAnalyticsDomain Domain { get; init; }
    public string PageKey { get; init; } = "";
    public bool Success { get; init; }
    public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.Now;
    public DateTimeOffset? LastSuccess { get; init; }
    public string Message { get; init; } = "";

    public string BadgeText =>
        Success
            ? $"LIVE - updated {Timestamp.ToLocalTime():HH:mm:ss}"
            : LastSuccess is { } last
                ? $"STALE - last good {last.ToLocalTime():HH:mm:ss}"
                : "STALE - no successful sample yet";
}

public sealed class PiHoleLiveSnapshot
{
    public DateTimeOffset SampledAt { get; set; } = DateTimeOffset.Now;

    public bool DnsOnline { get; set; }
    public bool BlockingEnabled { get; set; }

    public string Host { get; set; } = "--";
    public string Uptime { get; set; } = "--";
    public string Load { get; set; } = "--";
    public double? TemperatureC { get; set; }

    public long Queries { get; set; }
    public long Blocked { get; set; }
    public double PercentBlocked { get; set; }
    public bool StatsAvailable { get; set; }
}

public sealed class LiveAnalyticsService : IDisposable
{
    private readonly AppServices _services;
    private readonly OperationsDrillDownService _drill;
    private readonly PlexSessionService _plexSessions;
    private readonly DownloadClientService _downloadClients;

    private readonly DispatcherTimer _timer =
        new()
        {
            Interval = TimeSpan.FromSeconds(1)
        };

    private readonly SemaphoreSlim _mediaGate = new(1, 1);
    private readonly SemaphoreSlim _queueGate = new(1, 1);
    private readonly SemaphoreSlim _plexGate = new(1, 1);
    private readonly SemaphoreSlim _downloadGate = new(1, 1);
    private readonly SemaphoreSlim _piGate = new(1, 1);

    private readonly Dictionary<string, IReadOnlyList<QueueDrillRow>>
        _queueByPage =
            new(StringComparer.OrdinalIgnoreCase);

    private readonly Dictionary<string, DateTimeOffset>
        _queueUpdated =
            new(StringComparer.OrdinalIgnoreCase);

    private readonly Dictionary<string, DownloadClientSnapshot>
        _downloadByClient =
            new(StringComparer.OrdinalIgnoreCase);

    private readonly Dictionary<string, DateTimeOffset>
        _downloadUpdated =
            new(StringComparer.OrdinalIgnoreCase);

    private readonly Dictionary<LiveAnalyticsDomain, int>
        _failures = new();

    private DateTimeOffset _startupReady;
    private DateTimeOffset _nextMedia;
    private DateTimeOffset _nextQueue;
    private DateTimeOffset _nextPlex;
    private DateTimeOffset _nextDownload;
    private DateTimeOffset _nextPiHole;

    private Guid? _lastServerId;
    private bool _started;
    private bool _minimized;
    private string _activePage = "Background";

    public MediaOperationsSnapshot? MediaSnapshot { get; private set; }
    public PlexSessionSnapshot? PlexSnapshot { get; private set; }
    public Guid? PlexServerId { get; private set; }
    public PiHoleLiveSnapshot? PiHoleSnapshot { get; private set; }

    public DateTimeOffset? MediaUpdatedAt { get; private set; }
    public DateTimeOffset? PlexUpdatedAt { get; private set; }
    public DateTimeOffset? PiHoleUpdatedAt { get; private set; }

    public string ActivePage => _activePage;
    public bool IsMinimized => _minimized;
    public bool IsStarted => _started;

    public event EventHandler<LiveAnalyticsUpdateEventArgs>? Updated;

    public LiveAnalyticsService(
        AppServices services)
    {
        _services = services;
        _drill = new OperationsDrillDownService(services);
        _plexSessions = new PlexSessionService(services);
        _downloadClients = services.DownloadClients;
        _timer.Tick += Timer_Tick;
    }

    public void Start(
        TimeSpan? initialDelay = null)
    {
        if (_started)
            return;

        _started = true;

        var now = DateTimeOffset.UtcNow;

        _startupReady =
            now +
            (initialDelay ?? TimeSpan.FromSeconds(15));

        _nextMedia = _startupReady;
        _nextPlex = _startupReady + TimeSpan.FromSeconds(2);
        _nextDownload =
            IsDownloadPage(_activePage)
                ? _startupReady + TimeSpan.FromSeconds(1)
                : DateTimeOffset.MaxValue;
        _nextPiHole = _startupReady + TimeSpan.FromSeconds(4);
        _nextQueue =
            IsQueuePage(_activePage)
                ? _startupReady + TimeSpan.FromSeconds(3)
                : DateTimeOffset.MaxValue;

        _timer.Start();
    }

    public void Stop()
    {
        _timer.Stop();
        _started = false;
    }

    public void SetActivePage(
        string pageKey)
    {
        pageKey =
            string.IsNullOrWhiteSpace(pageKey)
                ? "Background"
                : pageKey.Trim();

        if (_activePage.Equals(
                pageKey,
                StringComparison.OrdinalIgnoreCase))
            return;

        _activePage = pageKey;

        var now = DateTimeOffset.UtcNow;

        if (IsMediaPage(pageKey))
        {
            _nextMedia =
                now + GetMediaInterval();

            if (IsQueuePage(pageKey))
                _nextQueue =
                    now + GetQueueInterval();

            if (pageKey.Equals(
                    "Plex",
                    StringComparison.OrdinalIgnoreCase))
                _nextPlex =
                    now + GetPlexInterval();
        }

        if (IsDownloadPage(pageKey))
        {
            _nextDownload = now;
        }
        else
        {
            _nextDownload = DateTimeOffset.MaxValue;
        }

        if (pageKey.Equals(
                "Pi-hole",
                StringComparison.OrdinalIgnoreCase))
        {
            _nextPiHole =
                now + GetPiHoleInterval();
        }
    }

    public void DeactivatePage(
        string pageKey)
    {
        if (_activePage.Equals(
                pageKey,
                StringComparison.OrdinalIgnoreCase))
        {
            _activePage = "Background";
            _nextQueue = DateTimeOffset.MaxValue;
            _nextDownload = DateTimeOffset.MaxValue;
        }
    }

    public void OnTargetChanged(
        ServerProfile? server)
    {
        ResetTargetScopedState(
            server?.Id,
            DateTimeOffset.UtcNow);
    }

    private bool IsCurrentTarget(
        ServerProfile server) =>
        _services.Context.Current?.Id == server.Id;

    private void ResetTargetScopedState(
        Guid? serverId,
        DateTimeOffset now)
    {
        _lastServerId = serverId;

        MediaSnapshot = null;
        PlexSnapshot = null;
        PlexServerId = null;
        MediaUpdatedAt = null;
        PlexUpdatedAt = null;

        _queueByPage.Clear();
        _queueUpdated.Clear();
        _downloadByClient.Clear();
        _downloadUpdated.Clear();
        _failures.Clear();

        _nextMedia = now;
        _nextPlex = now + TimeSpan.FromSeconds(1);

        _nextQueue =
            IsQueuePage(_activePage)
                ? now + TimeSpan.FromSeconds(3)
                : DateTimeOffset.MaxValue;

        _nextDownload =
            IsDownloadPage(_activePage)
                ? now + TimeSpan.FromSeconds(1)
                : DateTimeOffset.MaxValue;
    }

    public void SetMinimized(
        bool minimized)
    {
        if (_minimized == minimized)
            return;

        _minimized = minimized;

        if (!minimized)
        {
            var now = DateTimeOffset.UtcNow;

            _nextMedia =
                DateTimeOffset.Compare(
                    _nextMedia,
                    now + TimeSpan.FromSeconds(2)) > 0
                    ? now + TimeSpan.FromSeconds(2)
                    : _nextMedia;

            _nextPlex =
                DateTimeOffset.Compare(
                    _nextPlex,
                    now + TimeSpan.FromSeconds(2)) > 0
                    ? now + TimeSpan.FromSeconds(2)
                    : _nextPlex;

            if (IsDownloadPage(_activePage))
            {
                _nextDownload =
                    DateTimeOffset.Compare(
                        _nextDownload,
                        now + TimeSpan.FromSeconds(1)) > 0
                        ? now + TimeSpan.FromSeconds(1)
                        : _nextDownload;
            }

            _nextPiHole =
                DateTimeOffset.Compare(
                    _nextPiHole,
                    now + TimeSpan.FromSeconds(3)) > 0
                    ? now + TimeSpan.FromSeconds(3)
                    : _nextPiHole;
        }
    }

    public IReadOnlyList<QueueDrillRow> GetQueueRows(
        string pageKey)
    {
        return _queueByPage.TryGetValue(
                pageKey,
                out var rows)
            ? rows
            : Array.Empty<QueueDrillRow>();
    }

    public DateTimeOffset? GetQueueUpdatedAt(
        string pageKey)
    {
        return _queueUpdated.TryGetValue(
                pageKey,
                out var timestamp)
            ? timestamp
            : null;
    }

    public DownloadClientSnapshot? GetDownloadSnapshot(
        string clientKey)
    {
        var key = DownloadClientService.NormalizeClientKey(clientKey);
        return _downloadByClient.TryGetValue(key, out var snapshot)
            ? snapshot
            : null;
    }

    public DateTimeOffset? GetDownloadUpdatedAt(
        string clientKey)
    {
        var key = DownloadClientService.NormalizeClientKey(clientKey);
        return _downloadUpdated.TryGetValue(key, out var timestamp)
            ? timestamp
            : null;
    }

    public async Task ForceAsync(
        string pageKey)
    {
        if (!_started)
            Start(TimeSpan.Zero);

        var tasks = new List<Task>();

        if (IsMediaPage(pageKey))
            tasks.Add(PollMediaAsync(true));

        if (IsDownloadPage(pageKey))
            tasks.Add(PollDownloadAsync(pageKey, true));

        if (IsQueuePage(pageKey))
            tasks.Add(PollQueueAsync(pageKey, true));

        if (pageKey.Equals(
                "Plex",
                StringComparison.OrdinalIgnoreCase))
            tasks.Add(PollPlexAsync(true));

        if (pageKey.Equals(
                "Pi-hole",
                StringComparison.OrdinalIgnoreCase))
            tasks.Add(PollPiHoleAsync());

        if (tasks.Count > 0)
            await Task.WhenAll(tasks);
    }

    private void Timer_Tick(
        object? sender,
        EventArgs e)
    {
        if (!_started)
            return;

        var now = DateTimeOffset.UtcNow;

        DetectServerChange(now);

        if (now < _startupReady)
            return;

        if (now >= _nextMedia)
        {
            _nextMedia =
                now + GetMediaInterval();
            _ = PollMediaAsync(false);
        }

        if (IsQueuePage(_activePage) &&
            now >= _nextQueue)
        {
            var page = _activePage;

            _nextQueue =
                now + GetQueueInterval();

            _ = PollQueueAsync(page);
        }

        if (IsDownloadPage(_activePage) &&
            now >= _nextDownload)
        {
            var page = _activePage;
            _nextDownload = now + GetDownloadInterval();
            _ = PollDownloadAsync(page);
        }

        if (now >= _nextPlex)
        {
            _nextPlex =
                now + GetPlexInterval();
            _ = PollPlexAsync();
        }

        if (now >= _nextPiHole)
        {
            _nextPiHole =
                now + GetPiHoleInterval();
            _ = PollPiHoleAsync();
        }
    }

    private void DetectServerChange(
        DateTimeOffset now)
    {
        var current =
            _services.Context.Current?.Id;

        if (current == _lastServerId)
            return;

        ResetTargetScopedState(current, now);
    }

    private async Task PollMediaAsync(
        bool force)
    {
        var entered = force
            ? await _mediaGate.WaitAsync(TimeSpan.FromSeconds(50))
            : await _mediaGate.WaitAsync(0);
        if (!entered)
            return;

        var server =
            _services.Context.Current;

        if (server is null)
        {
            _mediaGate.Release();
            return;
        }

        try
        {
            var snapshot =
                await _services.MediaOps.GetSnapshotAsync(
                    server,
                    force);

            if (!IsCurrentTarget(server))
                return;

            MediaSnapshot = snapshot;
            MediaUpdatedAt = DateTimeOffset.Now;

            Success(
                LiveAnalyticsDomain.MediaSummary,
                "",
                MediaUpdatedAt.Value,
                "Application telemetry updated.");
        }
        catch (Exception ex)
        {
            if (!IsCurrentTarget(server))
                return;

            Failure(
                LiveAnalyticsDomain.MediaSummary,
                "",
                MediaUpdatedAt,
                ex.Message);
        }
        finally
        {
            _mediaGate.Release();
        }
    }

    private async Task PollQueueAsync(
        string pageKey,
        bool force = false)
    {
        if (!IsQueuePage(pageKey))
            return;

        var entered = force
            ? await _queueGate.WaitAsync(TimeSpan.FromSeconds(50))
            : await _queueGate.WaitAsync(0);
        if (!entered)
            return;

        var server =
            _services.Context.Current;

        if (server is null)
        {
            _queueGate.Release();
            return;
        }

        try
        {
            if (server.ConnectionKind is HostConnectionKind.LocalWindows or HostConnectionKind.RemoteWindows)
            {
                _queueByPage[pageKey] = Array.Empty<QueueDrillRow>();
                _queueUpdated[pageKey] = DateTimeOffset.Now;
                Success(
                    LiveAnalyticsDomain.QueueDetail,
                    pageKey,
                    _queueUpdated[pageKey],
                    "Windows application summary is live; item-level Arr queues require an authenticated application provider.");
                return;
            }

            var names =
                QueueServiceNames(pageKey);

            var rows =
                await _drill.GetQueuesAsync(
                    server,
                    names);

            if (!IsCurrentTarget(server))
                return;

            _queueByPage[pageKey] = rows;
            _queueUpdated[pageKey] = DateTimeOffset.Now;

            Success(
                LiveAnalyticsDomain.QueueDetail,
                pageKey,
                _queueUpdated[pageKey],
                $"{rows.Count} queue / health row(s).");
        }
        catch (Exception ex)
        {
            if (!IsCurrentTarget(server))
                return;

            Failure(
                LiveAnalyticsDomain.QueueDetail,
                pageKey,
                GetQueueUpdatedAt(pageKey),
                ex.Message);
        }
        finally
        {
            _queueGate.Release();
        }
    }

    private async Task PollDownloadAsync(
        string pageKey,
        bool force = false)
    {
        if (!IsDownloadPage(pageKey))
            return;

        var entered = force
            ? await _downloadGate.WaitAsync(TimeSpan.FromSeconds(50))
            : await _downloadGate.WaitAsync(0);
        if (!entered)
            return;

        var server = _services.Context.Current;
        if (server is null)
        {
            _downloadGate.Release();
            return;
        }

        var key = DownloadClientService.NormalizeClientKey(pageKey);

        try
        {
            var snapshot =
                await _downloadClients.GetSnapshotAsync(
                    server,
                    key);

            if (!IsCurrentTarget(server))
                return;

            _downloadByClient[key] = snapshot;
            _downloadUpdated[key] = DateTimeOffset.Now;

            Success(
                LiveAnalyticsDomain.DownloadClient,
                key,
                _downloadUpdated[key],
                $"{snapshot.State}; {snapshot.TotalCount} item(s); {snapshot.ActiveCount} active.");
        }
        catch (Exception ex)
        {
            if (!IsCurrentTarget(server))
                return;

            Failure(
                LiveAnalyticsDomain.DownloadClient,
                key,
                GetDownloadUpdatedAt(key),
                ex.Message);
        }
        finally
        {
            _downloadGate.Release();
        }
    }

    private async Task PollPlexAsync(bool force = false)
    {
        var entered = force
            ? await _plexGate.WaitAsync(TimeSpan.FromSeconds(50))
            : await _plexGate.WaitAsync(0);
        if (!entered)
            return;

        var server =
            _services.Context.Current;

        if (server is null)
        {
            _plexGate.Release();
            return;
        }

        try
        {
            if (!_plexSessions.HasToken(server))
            {
                PlexSnapshot = null;
                PlexServerId = null;
                PlexUpdatedAt = null;

                Success(
                    LiveAnalyticsDomain.PlexSessions,
                    "Plex",
                    DateTimeOffset.Now,
                    "Plex session token is not configured.");

                return;
            }

            var snapshot =
                await _plexSessions.GetAsync(server);

            if (!IsCurrentTarget(server))
                return;

            PlexSnapshot = snapshot;
            PlexServerId = server.Id;
            PlexUpdatedAt = DateTimeOffset.Now;

            Success(
                LiveAnalyticsDomain.PlexSessions,
                "Plex",
                PlexUpdatedAt.Value,
                $"{snapshot.SessionCount} active session(s).");
        }
        catch (Exception ex)
        {
            if (!IsCurrentTarget(server))
                return;

            Failure(
                LiveAnalyticsDomain.PlexSessions,
                "Plex",
                PlexUpdatedAt,
                ex.Message);
        }
        finally
        {
            _plexGate.Release();
        }
    }

    private async Task PollPiHoleAsync()
    {
        if (!await _piGate.WaitAsync(0))
            return;

        var server =
            _services.Config.Current.Servers
                .FirstOrDefault(
                    x => x.Role.Contains(
                        "Pi-hole",
                        StringComparison.OrdinalIgnoreCase));

        if (server is null)
        {
            _piGate.Release();
            return;
        }

        try
        {
            const string cmd =
                "echo '__STATUS__'; " +
                "pihole status 2>&1 || true; " +
                "echo '__HOST__'; " +
                "hostname; " +
                "uptime -p; " +
                "awk '{print $1}' /proc/loadavg; " +
                "if [ -r /sys/class/thermal/thermal_zone0/temp ]; " +
                "then awk '{printf \"%.1f\\n\", $1/1000}' /sys/class/thermal/thermal_zone0/temp; " +
                "else echo --; fi; " +
                "echo '__STATS__'; " +
                "timeout 5 pihole api stats/summary 2>/dev/null || true";

            var result =
                await _services.Ssh.ExecuteAsync(
                    server,
                    cmd,
                    20);

            if (!result.Success &&
                string.IsNullOrWhiteSpace(result.Combined))
                throw new InvalidOperationException(
                    "Pi-hole live probe returned no data.");

            var snapshot =
                ParsePiHole(result.Combined);

            PiHoleSnapshot = snapshot;
            PiHoleUpdatedAt = snapshot.SampledAt;

            Success(
                LiveAnalyticsDomain.PiHole,
                "Pi-hole",
                snapshot.SampledAt,
                "Pi-hole analytics updated.");
        }
        catch (Exception ex)
        {
            Failure(
                LiveAnalyticsDomain.PiHole,
                "Pi-hole",
                PiHoleUpdatedAt,
                ex.Message);
        }
        finally
        {
            _piGate.Release();
        }
    }

    private static PiHoleLiveSnapshot ParsePiHole(
        string text)
    {
        var status =
            Slice(
                text,
                "__STATUS__",
                "__HOST__");

        var host =
            Slice(
                    text,
                    "__HOST__",
                    "__STATS__")
                .Replace(
                    "\r",
                    "",
                    StringComparison.Ordinal)
                .Split(
                    '\n',
                    StringSplitOptions.RemoveEmptyEntries);

        var stats =
            text.Contains(
                "__STATS__",
                StringComparison.Ordinal)
                ? text[
                    (text.IndexOf(
                        "__STATS__",
                        StringComparison.Ordinal) + 9)..]
                    .Trim()
                : "";

        var snapshot =
            new PiHoleLiveSnapshot
            {
                SampledAt = DateTimeOffset.Now,
                DnsOnline =
                    status.Contains(
                        "FTL is listening on port 53",
                        StringComparison.OrdinalIgnoreCase),
                BlockingEnabled =
                    status.Contains(
                        "blocking is enabled",
                        StringComparison.OrdinalIgnoreCase),
                Host =
                    host.ElementAtOrDefault(0) ?? "--",
                Uptime =
                    (host.ElementAtOrDefault(1) ?? "--")
                        .Replace(
                            "up ",
                            "",
                            StringComparison.OrdinalIgnoreCase),
                Load =
                    host.ElementAtOrDefault(2) ?? "--"
            };

        if (double.TryParse(
                host.ElementAtOrDefault(3),
                out var temp))
            snapshot.TemperatureC = temp;

        try
        {
            var jsonStart =
                stats.IndexOf('{');

            if (jsonStart < 0)
                return snapshot;

            using var doc =
                JsonDocument.Parse(
                    stats[jsonStart..]);

            var root =
                doc.RootElement;

            long total = 0;
            long blocked = 0;
            double percent = 0;

            if (root.TryGetProperty(
                    "queries",
                    out var q))
            {
                if (q.TryGetProperty(
                        "total",
                        out var qt) &&
                    qt.TryGetInt64(
                        out var x))
                    total = x;

                if (q.TryGetProperty(
                        "blocked",
                        out var qb) &&
                    qb.TryGetInt64(
                        out var y))
                    blocked = y;

                if (q.TryGetProperty(
                        "percent_blocked",
                        out var qp) &&
                    qp.TryGetDouble(
                        out var z))
                    percent = z;
            }
            else
            {
                if (root.TryGetProperty(
                        "total_queries",
                        out var tq) &&
                    tq.TryGetInt64(
                        out var x))
                    total = x;

                if (root.TryGetProperty(
                        "blocked_queries",
                        out var bq) &&
                    bq.TryGetInt64(
                        out var y))
                    blocked = y;

                if (root.TryGetProperty(
                        "percent_blocked",
                        out var pb) &&
                    pb.TryGetDouble(
                        out var z))
                    percent = z;
            }

            snapshot.Queries = total;
            snapshot.Blocked = blocked;
            snapshot.PercentBlocked = percent;
            snapshot.StatsAvailable =
                total > 0 ||
                blocked > 0;
        }
        catch
        {
            // Retain service / host state if the optional
            // rolling statistics JSON is unavailable.
        }

        return snapshot;
    }

    private void Success(
        LiveAnalyticsDomain domain,
        string pageKey,
        DateTimeOffset timestamp,
        string message)
    {
        _failures[domain] = 0;

        Raise(
            new LiveAnalyticsUpdateEventArgs
            {
                Domain = domain,
                PageKey = pageKey,
                Success = true,
                Timestamp = timestamp,
                LastSuccess = timestamp,
                Message = message
            });
    }

    private void Failure(
        LiveAnalyticsDomain domain,
        string pageKey,
        DateTimeOffset? lastSuccess,
        string message)
    {
        var count =
            _failures.TryGetValue(
                domain,
                out var current)
                ? current + 1
                : 1;

        _failures[domain] = count;

        ApplyFailureBackoff(
            domain,
            count);

        Raise(
            new LiveAnalyticsUpdateEventArgs
            {
                Domain = domain,
                PageKey = pageKey,
                Success = false,
                Timestamp = DateTimeOffset.Now,
                LastSuccess = lastSuccess,
                Message = message
            });
    }

    private void ApplyFailureBackoff(
        LiveAnalyticsDomain domain,
        int failureCount)
    {
        var multiplier =
            1 << Math.Min(
                2,
                Math.Max(
                    0,
                    failureCount - 1));

        var now =
            DateTimeOffset.UtcNow;

        switch (domain)
        {
            case LiveAnalyticsDomain.MediaSummary:
                _nextMedia =
                    now +
                    Multiply(
                        GetMediaInterval(),
                        multiplier);
                break;

            case LiveAnalyticsDomain.QueueDetail:
                _nextQueue =
                    now +
                    Multiply(
                        GetQueueInterval(),
                        multiplier);
                break;

            case LiveAnalyticsDomain.PlexSessions:
                _nextPlex =
                    now +
                    Multiply(
                        GetPlexInterval(),
                        multiplier);
                break;

            case LiveAnalyticsDomain.DownloadClient:
                _nextDownload =
                    now +
                    Multiply(
                        GetDownloadInterval(),
                        multiplier);
                break;

            case LiveAnalyticsDomain.PiHole:
                _nextPiHole =
                    now +
                    Multiply(
                        GetPiHoleInterval(),
                        multiplier);
                break;
        }
    }

    private void Raise(
        LiveAnalyticsUpdateEventArgs args)
    {
        var dispatcher =
            System.Windows.Application.Current?.Dispatcher;

        if (dispatcher is null ||
            dispatcher.CheckAccess())
        {
            Updated?.Invoke(
                this,
                args);
            return;
        }

        dispatcher.BeginInvoke(
            () => Updated?.Invoke(
                this,
                args));
    }

    private TimeSpan GetMediaInterval()
    {
        if (_minimized)
            return TimeSpan.FromSeconds(60);

        if (IsMediaPage(_activePage))
            return TimeSpan.FromSeconds(15);

        return TimeSpan.FromSeconds(30);
    }

    private TimeSpan GetQueueInterval()
    {
        return _minimized
            ? TimeSpan.FromSeconds(60)
            : TimeSpan.FromSeconds(10);
    }

    private TimeSpan GetDownloadInterval()
    {
        return _minimized
            ? TimeSpan.FromSeconds(60)
            : TimeSpan.FromSeconds(5);
    }

    private TimeSpan GetPlexInterval()
    {
        if (_minimized)
            return TimeSpan.FromSeconds(60);

        return _activePage.Equals(
                "Plex",
                StringComparison.OrdinalIgnoreCase)
            ? TimeSpan.FromSeconds(5)
            : TimeSpan.FromSeconds(15);
    }

    private TimeSpan GetPiHoleInterval()
    {
        if (_minimized)
            return TimeSpan.FromSeconds(120);

        return _activePage.Equals(
                "Pi-hole",
                StringComparison.OrdinalIgnoreCase)
            ? TimeSpan.FromSeconds(15)
            : TimeSpan.FromSeconds(45);
    }

    private static TimeSpan Multiply(
        TimeSpan value,
        int multiplier)
    {
        var ticks =
            Math.Min(
                TimeSpan.FromMinutes(5).Ticks,
                value.Ticks * multiplier);

        return TimeSpan.FromTicks(ticks);
    }

    private static bool IsMediaPage(
        string pageKey)
    {
                return pageKey.Equals(
                   "Dashboard",
                   StringComparison.OrdinalIgnoreCase) ||
               pageKey.Equals(
                   "Applications",
                   StringComparison.OrdinalIgnoreCase) ||
               pageKey.Equals(
                   "Plex",
                   StringComparison.OrdinalIgnoreCase) ||
               pageKey.Equals(
                   "Sonarr",
                   StringComparison.OrdinalIgnoreCase) ||
               pageKey.Equals(
                   "Radarr",
                   StringComparison.OrdinalIgnoreCase) ||
               pageKey.Equals(
                   "Lidarr",
                   StringComparison.OrdinalIgnoreCase) ||
               pageKey.Equals(
                   "Prowlarr",
                   StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsQueuePage(
        string pageKey)
    {
        return pageKey.Equals(
                   "Sonarr",
                   StringComparison.OrdinalIgnoreCase) ||
               pageKey.Equals(
                   "Radarr",
                   StringComparison.OrdinalIgnoreCase) ||
               pageKey.Equals(
                   "Lidarr",
                   StringComparison.OrdinalIgnoreCase) ||
               pageKey.Equals(
                   "Prowlarr",
                   StringComparison.OrdinalIgnoreCase);
    }

    private static IReadOnlyList<string> QueueServiceNames(
        string pageKey)
    {
        return pageKey.ToLowerInvariant() switch
        {
            "sonarr" =>
                ["Sonarr", "Sonarr Debrid"],

            "radarr" =>
                ["Radarr", "Radarr Debrid"],

            "lidarr" =>
                ["Lidarr"],

            "prowlarr" =>
                ["Prowlarr"],

            _ =>
                Array.Empty<string>()
        };
    }

    private static bool IsDownloadPage(
        string pageKey)
        => DownloadClientService.IsSupported(pageKey);

    private static string Slice(
        string text,
        string start,
        string end)
    {
        var a =
            text.IndexOf(
                start,
                StringComparison.Ordinal);

        if (a < 0)
            return "";

        a += start.Length;

        var b =
            text.IndexOf(
                end,
                a,
                StringComparison.Ordinal);

        return b < 0
            ? text[a..]
            : text[a..b];
    }

    public void Dispose()
    {
        Stop();

        _mediaGate.Dispose();
        _queueGate.Dispose();
        _plexGate.Dispose();
        _downloadGate.Dispose();
        _piGate.Dispose();
    }
}

public static class LiveAnalyticsHub
{
    private static readonly Lazy<LiveAnalyticsService>
        Instance =
            new(
                () =>
                    new LiveAnalyticsService(
                        App.Services));

    public static LiveAnalyticsService Current =>
        Instance.Value;
}