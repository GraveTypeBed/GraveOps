using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using GraveOps.Core.Targets;
using GraveOps.Core.Telemetry;

namespace GraveOps.Desktop.Windows;

public static class WindowsMediaLifecycleTargetLease
{
    public static bool IsCurrent(
        string requestedTargetId,
        TargetProfile? currentTarget)
    {
        if (string.IsNullOrWhiteSpace(
                requestedTargetId) ||
            currentTarget is null)
        {
            return false;
        }

        return currentTarget.Id.Equals(
            requestedTargetId,
            StringComparison.Ordinal);
    }

    public static bool ShouldRefreshCurrent(
        string completedTargetId,
        TargetProfile? currentTarget,
        bool pageVisible) =>
        pageVisible &&
        currentTarget is not null &&
        !IsCurrent(
            completedTargetId,
            currentTarget);
}

public partial class MainWindow
{
    private static readonly TimeSpan
        MediaLifecycleForegroundRefreshInterval =
            TimeSpan.FromSeconds(
                10);

    private static readonly TimeSpan
        MediaLifecycleBackgroundRefreshInterval =
            TimeSpan.FromSeconds(
                30);

    private static readonly TimeSpan
        MediaLifecycleMinimizedRefreshInterval =
            TimeSpan.FromSeconds(
                60);

    private static readonly TimeSpan
        MediaLifecycleFreshSourceWindow =
            TimeSpan.FromSeconds(
                25);

    private readonly DispatcherTimer
        _mediaLifecycleTimer =
            new()
            {
                Interval =
                    MediaLifecycleForegroundRefreshInterval
            };

    private readonly Dictionary<
        string,
        MediaLifecycleSnapshot>
        _mediaLifecycleCache =
            new(
                StringComparer.Ordinal);

    private bool _mediaLifecycleBusy;

    private void InitializeMediaLifecycleWorkspace()
    {
        _mediaLifecycleTimer.Tick +=
            async (_, _) =>
            {
                UpdateMediaLifecycleTimerCadence();

                await RefreshMediaLifecycleAsync(
                    showStatus:
                        false);
            };

        Opened +=
            (_, _) =>
            {
                UpdateMediaLifecycleTimerCadence();
                _mediaLifecycleTimer.Start();
            };

        Closed +=
            (_, _) =>
                _mediaLifecycleTimer.Stop();
    }

    private void ActivateWindowsMediaLifecycleWorkspace()
    {
        UpdateMediaLifecycleTimerCadence();

        _ =
            LoadAndRefreshMediaLifecycleWorkspaceAsync();
    }

    private async Task
        LoadAndRefreshMediaLifecycleWorkspaceAsync()
    {
        var target =
            _targetSession.SelectedTarget;

        if (target is null)
            return;

        SetText(
            "MediaLifecycleTargetText",
            target.DisplayName);

        if (_mediaLifecycleCache.TryGetValue(
                target.Id,
                out var cached))
        {
            ApplyMediaLifecycleSnapshot(
                cached);
        }
        else
        {
            SetMediaLifecycleLoadingState();
        }

        await RefreshMediaLifecycleAsync(
            showStatus:
                false);
    }

    private void SetMediaLifecycleLoadingState()
    {
        SetText(
            "MediaLifecycleStateText",
            "CHECKING");

        SetText(
            "MediaLifecycleTotalText",
            "--");

        SetText(
            "MediaLifecycleTransferText",
            "--");

        SetText(
            "MediaLifecycleProcessingText",
            "--");

        SetText(
            "MediaLifecyclePlayingText",
            "--");

        SetText(
            "MediaLifecycleAttentionText",
            "--");

        SetText(
            "MediaLifecycleSourceSummaryText",
            "Waiting for configured application telemetry.");

        SetText(
            "MediaLifecycleStatusText",
            "Collecting read-only Arr, download-client and Plex evidence.");

        SetText(
            "MediaLifecycleFreshnessText",
            "CHECKING...");

        SetMediaLifecycleCollections(
            snapshot:
                null);
    }

    private void ApplyMediaLifecycleSnapshot(
        MediaLifecycleSnapshot snapshot)
    {
        SetText(
            "MediaLifecycleStateText",
            snapshot.OverallState.ToUpperInvariant());

        SetText(
            "MediaLifecycleTotalText",
            snapshot.TotalCount.ToString());

        SetText(
            "MediaLifecycleTransferText",
            snapshot.TransferCount.ToString());

        SetText(
            "MediaLifecycleProcessingText",
            snapshot.ProcessingCount.ToString());

        SetText(
            "MediaLifecyclePlayingText",
            snapshot.PlayingCount.ToString());

        SetText(
            "MediaLifecycleAttentionText",
            snapshot.AttentionCount.ToString());

        SetText(
            "MediaLifecycleSourceSummaryText",
            snapshot.SourceSummary);

        SetText(
            "MediaLifecycleStatusText",
            "Read-only correlation uses conservative title matching. " +
            "Plex item availability is verified only by an active matching session; " +
            "otherwise GraveOps reports server readiness without claiming the item is in a library.");

        SetText(
            "MediaLifecycleFreshnessText",
            $"LIVE · {MediaLifecycleCadenceLabel()} · updated " +
            $"{snapshot.CapturedAt.ToLocalTime():h:mm:ss tt}");

        SetMediaLifecycleCollections(
            snapshot);
    }

    private void SetMediaLifecycleCollections(
        MediaLifecycleSnapshot? snapshot)
    {
        var sources =
            snapshot?.Sources ??
            Array.Empty<MediaLifecycleSourceRow>();

        var items =
            snapshot?.Items ??
            Array.Empty<MediaLifecycleItemRow>();

        Get<ListBox>(
                "MediaLifecycleSourcesList")
            .ItemsSource =
                sources;

        SetText(
            "MediaLifecycleSourceCountText",
            snapshot is null
                ? "--"
                : $"{sources.Count} sources");

        var list =
            Get<ListBox>(
                "MediaLifecycleItemsList");

        list.ItemsSource =
            items;

        list.IsVisible =
            items.Count >
            0;

        Get<Border>(
                "MediaLifecycleEmptyState")
            .IsVisible =
                items.Count ==
                0;

        SetText(
            "MediaLifecycleEmptyText",
            snapshot is null
                ? "Lifecycle evidence is loading."
                : "No active Arr work, transfers, recent completions or Plex sessions were returned.");

        SetText(
            "MediaLifecycleItemCountText",
            snapshot is null
                ? "--"
                : $"{items.Count} shown");
    }

    private async Task RefreshMediaLifecycleAsync(
        bool showStatus)
    {
        if (_mediaLifecycleBusy)
            return;

        var target =
            _targetSession.SelectedTarget;

        if (target is null)
            return;

        var targetId =
            target.Id;

        var hasCached =
            _mediaLifecycleCache.TryGetValue(
                targetId,
                out var cached);

        _mediaLifecycleBusy =
            true;

        SetText(
            "MediaLifecycleFreshnessText",
            hasCached
                ? $"LIVE · {MediaLifecycleCadenceLabel()} · updating"
                : $"UPDATING · {MediaLifecycleCadenceLabel()}");

        if (showStatus)
        {
            SetText(
                "MediaLifecycleStatusText",
                "Refreshing configured lifecycle sources...");
        }

        try
        {
            var sonarrTask =
                CaptureLifecycleArrAsync(
                    target,
                    "Sonarr");

            var radarrTask =
                CaptureLifecycleArrAsync(
                    target,
                    "Radarr");

            var lidarrTask =
                CaptureLifecycleArrAsync(
                    target,
                    "Lidarr");

            var qbittorrentTask =
                CaptureLifecycleQBittorrentAsync(
                    target);

            var sabnzbdTask =
                CaptureLifecycleSABnzbdAsync(
                    target);

            var plexTask =
                CaptureLifecyclePlexAsync(
                    target);

            await Task.WhenAll(
                new Task[]
                {
                    sonarrTask,
                    radarrTask,
                    lidarrTask,
                    qbittorrentTask,
                    sabnzbdTask,
                    plexTask
                });

            var captures =
                new[]
                {
                    sonarrTask.Result.ToSourceRow(),
                    radarrTask.Result.ToSourceRow(),
                    lidarrTask.Result.ToSourceRow(),
                    qbittorrentTask.Result.ToSourceRow(),
                    sabnzbdTask.Result.ToSourceRow(),
                    plexTask.Result.ToSourceRow()
                };

            var failures =
                captures
                    .Where(row =>
                        row is not null)
                    .Cast<MediaLifecycleSourceRow>()
                    .ToArray();

            var arrSnapshots =
                new[]
                {
                    sonarrTask.Result.Snapshot,
                    radarrTask.Result.Snapshot,
                    lidarrTask.Result.Snapshot
                }
                .Where(snapshot =>
                    snapshot is not null)
                .Cast<ArrLiveTelemetrySnapshot>()
                .ToArray();

            var snapshot =
                MediaLifecycleCorrelator.Build(
                    arrSnapshots,
                    qbittorrentTask.Result.Snapshot,
                    sabnzbdTask.Result.Snapshot,
                    plexTask.Result.Snapshot,
                    failures,
                    DateTimeOffset.UtcNow);

            _mediaLifecycleCache[targetId] =
                snapshot;

            if (!IsCurrentMediaLifecycleTarget(
                    targetId))
            {
                return;
            }

            ApplyMediaLifecycleSnapshot(
                snapshot);

            if (showStatus)
            {
                SetText(
                    "MediaLifecycleStatusText",
                    "Lifecycle sources refreshed. " +
                    "Correlation remains read-only and conservative.");
            }
        }
        catch (Exception exception)
        {
            if (!IsCurrentMediaLifecycleTarget(
                    targetId))
            {
                return;
            }

            if (hasCached &&
                cached is not null)
            {
                ApplyMediaLifecycleSnapshot(
                    cached);

                SetText(
                    "MediaLifecycleFreshnessText",
                    $"STALE · {MediaLifecycleCadenceLabel()} · retrying");

                SetText(
                    "MediaLifecycleStatusText",
                    showStatus
                        ? "Last lifecycle snapshot retained · " +
                          exception.Message
                        : "Last lifecycle snapshot retained while source telemetry retries.");
            }
            else
            {
                SetText(
                    "MediaLifecycleStateText",
                    "UNAVAILABLE");

                SetText(
                    "MediaLifecycleFreshnessText",
                    "PROBE FAILED");

                SetText(
                    "MediaLifecycleStatusText",
                    exception.Message);

                SetMediaLifecycleCollections(
                    snapshot:
                        null);
            }
        }
        finally
        {
            var refreshCurrent =
                WindowsMediaLifecycleTargetLease
                    .ShouldRefreshCurrent(
                        targetId,
                        _targetSession.SelectedTarget,
                        Get<Control>(
                                "LifecyclePage")
                            .IsVisible);

            _mediaLifecycleBusy =
                false;

            if (refreshCurrent)
            {
                ActivateWindowsMediaLifecycleWorkspace();
            }
        }
    }

    private async Task<
        LifecycleCapture<ArrLiveTelemetrySnapshot>>
        CaptureLifecycleArrAsync(
            TargetProfile target,
            string product)
    {
        var normalizedProduct =
            WindowsArrProductPolicy.Normalize(
                product);

        var key =
            ArrCacheKey(
                target.Id,
                normalizedProduct);

        _arrCache.TryGetValue(
            key,
            out var cached);

        if (cached is not null &&
            IsFreshLifecycleSource(
                cached.CapturedAt))
        {
            return LifecycleCapture<
                ArrLiveTelemetrySnapshot>
                .Fresh(
                    normalizedProduct,
                    cached);
        }

        if (_arrTelemetry is null)
        {
            return LifecycleCapture<
                ArrLiveTelemetrySnapshot>
                .Failed(
                    normalizedProduct,
                    cached,
                    "Arr telemetry service is unavailable.");
        }

        try
        {
            var snapshot =
                await _arrTelemetry.CaptureAsync(
                    target,
                    normalizedProduct);

            _arrCache[key] =
                snapshot;

            return LifecycleCapture<
                ArrLiveTelemetrySnapshot>
                .Fresh(
                    normalizedProduct,
                    snapshot);
        }
        catch (Exception exception)
        {
            return LifecycleCapture<
                ArrLiveTelemetrySnapshot>
                .Failed(
                    normalizedProduct,
                    cached,
                    exception.Message);
        }
    }

    private async Task<
        LifecycleCapture<DownloadClientTelemetrySnapshot>>
        CaptureLifecycleQBittorrentAsync(
            TargetProfile target)
    {
        _qbittorrentCache.TryGetValue(
            target.Id,
            out var cached);

        if (cached is not null &&
            IsFreshLifecycleSource(
                cached.SampledAt))
        {
            return LifecycleCapture<
                DownloadClientTelemetrySnapshot>
                .Fresh(
                    "qBittorrent",
                    cached);
        }

        if (_qbittorrentTelemetry is null)
        {
            return LifecycleCapture<
                DownloadClientTelemetrySnapshot>
                .Failed(
                    "qBittorrent",
                    cached,
                    "qBittorrent telemetry service is unavailable.");
        }

        try
        {
            var snapshot =
                await _qbittorrentTelemetry.CaptureAsync(
                    target);

            _qbittorrentCache[target.Id] =
                snapshot;

            return LifecycleCapture<
                DownloadClientTelemetrySnapshot>
                .Fresh(
                    "qBittorrent",
                    snapshot);
        }
        catch (Exception exception)
        {
            return LifecycleCapture<
                DownloadClientTelemetrySnapshot>
                .Failed(
                    "qBittorrent",
                    cached,
                    exception.Message);
        }
    }

    private async Task<
        LifecycleCapture<DownloadClientTelemetrySnapshot>>
        CaptureLifecycleSABnzbdAsync(
            TargetProfile target)
    {
        _sabnzbdCache.TryGetValue(
            target.Id,
            out var cached);

        if (cached is not null &&
            IsFreshLifecycleSource(
                cached.SampledAt))
        {
            return LifecycleCapture<
                DownloadClientTelemetrySnapshot>
                .Fresh(
                    "SABnzbd",
                    cached);
        }

        if (_sabnzbdTelemetry is null)
        {
            return LifecycleCapture<
                DownloadClientTelemetrySnapshot>
                .Failed(
                    "SABnzbd",
                    cached,
                    "SABnzbd telemetry service is unavailable.");
        }

        try
        {
            var snapshot =
                await _sabnzbdTelemetry.CaptureAsync(
                    target);

            _sabnzbdCache[target.Id] =
                snapshot;

            return LifecycleCapture<
                DownloadClientTelemetrySnapshot>
                .Fresh(
                    "SABnzbd",
                    snapshot);
        }
        catch (Exception exception)
        {
            return LifecycleCapture<
                DownloadClientTelemetrySnapshot>
                .Failed(
                    "SABnzbd",
                    cached,
                    exception.Message);
        }
    }

    private async Task<
        LifecycleCapture<PlexTelemetrySnapshot>>
        CaptureLifecyclePlexAsync(
            TargetProfile target)
    {
        _plexCache.TryGetValue(
            target.Id,
            out var cached);

        if (cached is not null &&
            IsFreshLifecycleSource(
                cached.SampledAt))
        {
            return LifecycleCapture<
                PlexTelemetrySnapshot>
                .Fresh(
                    "Plex",
                    cached);
        }

        if (_plexTelemetry is null)
        {
            return LifecycleCapture<
                PlexTelemetrySnapshot>
                .Failed(
                    "Plex",
                    cached,
                    "Plex telemetry service is unavailable.");
        }

        try
        {
            var snapshot =
                await _plexTelemetry.CaptureAsync(
                    target);

            _plexCache[target.Id] =
                snapshot;

            return LifecycleCapture<
                PlexTelemetrySnapshot>
                .Fresh(
                    "Plex",
                    snapshot);
        }
        catch (Exception exception)
        {
            return LifecycleCapture<
                PlexTelemetrySnapshot>
                .Failed(
                    "Plex",
                    cached,
                    exception.Message);
        }
    }

    private bool IsCurrentMediaLifecycleTarget(
        string targetId) =>
        WindowsMediaLifecycleTargetLease.IsCurrent(
            targetId,
            _targetSession.SelectedTarget);

    private static bool IsFreshLifecycleSource(
        DateTimeOffset sampledAt)
    {
        var age =
            DateTimeOffset.UtcNow -
            sampledAt.ToUniversalTime();

        return age <=
                   MediaLifecycleFreshSourceWindow &&
               age >=
                   TimeSpan.FromMinutes(
                       -1);
    }

    private void OnMediaLifecycleTargetChanged()
    {
        if (Get<Control>(
                "LifecyclePage")
            .IsVisible)
        {
            ActivateWindowsMediaLifecycleWorkspace();
        }
    }

    private void UpdateMediaLifecycleTimerCadence()
    {
        var interval =
            WindowState ==
                WindowState.Minimized
                ? MediaLifecycleMinimizedRefreshInterval
                : Get<Control>(
                        "LifecyclePage")
                    .IsVisible
                    ? MediaLifecycleForegroundRefreshInterval
                    : MediaLifecycleBackgroundRefreshInterval;

        if (_mediaLifecycleTimer.Interval !=
            interval)
        {
            _mediaLifecycleTimer.Interval =
                interval;
        }
    }

    private string MediaLifecycleCadenceLabel()
    {
        if (WindowState ==
            WindowState.Minimized)
        {
            return "60s minimized";
        }

        return Get<Control>(
                "LifecyclePage")
            .IsVisible
            ? "10s live"
            : "30s background";
    }

    private async void
        MediaLifecycleRefreshButton_OnClick(
            object? sender,
            RoutedEventArgs e) =>
        await RefreshMediaLifecycleAsync(
            showStatus:
                true);

    private sealed record LifecycleCapture<T>(
        string Source,
        T? Snapshot,
        string? Failure,
        bool IsStale)
        where T : class
    {
        public static LifecycleCapture<T> Fresh(
            string source,
            T snapshot) =>
            new(
                source,
                snapshot,
                Failure:
                    null,
                IsStale:
                    false);

        public static LifecycleCapture<T> Failed(
            string source,
            T? snapshot,
            string failure) =>
            new(
                source,
                snapshot,
                failure,
                IsStale:
                    snapshot is not null);

        public MediaLifecycleSourceRow? ToSourceRow()
        {
            if (string.IsNullOrWhiteSpace(
                    Failure))
            {
                return null;
            }

            return new MediaLifecycleSourceRow(
                Source,
                IsStale
                    ? "Stale"
                    : "Unavailable",
                Failure);
        }
    }
}
