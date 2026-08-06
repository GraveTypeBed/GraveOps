using System.Diagnostics;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using GraveOps.Core.Hosts;
using GraveOps.Core.Telemetry;

namespace GraveOps.Desktop.Windows;

public partial class MainWindow
{
    private static readonly TimeSpan QBittorrentForegroundRefreshInterval =
        TimeSpan.FromSeconds(10);

    private static readonly TimeSpan QBittorrentBackgroundRefreshInterval =
        TimeSpan.FromSeconds(30);

    private static readonly TimeSpan QBittorrentMinimizedRefreshInterval =
        TimeSpan.FromSeconds(60);

    private readonly DispatcherTimer _qbittorrentTimer =
        new()
        {
            Interval = QBittorrentForegroundRefreshInterval
        };

    private readonly Dictionary<
        string,
        DownloadClientTelemetrySnapshot>
        _qbittorrentCache =
            new(StringComparer.Ordinal);

    private WindowsQBittorrentTelemetryService? _qbittorrentTelemetry;
    private IntegrationSnapshot? _qbittorrentDiscovery;
    private bool _qbittorrentBusy;

    private void InitializeQBittorrentWorkspace()
    {
        _qbittorrentTelemetry =
            new WindowsQBittorrentTelemetryService(_targetSession);

        _qbittorrentTimer.Tick +=
            async (_, _) =>
            {
                UpdateQBittorrentTimerCadence();

                await RefreshQBittorrentTelemetryAsync(
                    showStatus: false);
            };

        Opened +=
            (_, _) =>
            {
                UpdateQBittorrentTimerCadence();
                _qbittorrentTimer.Start();
            };

        Closed +=
            (_, _) =>
                _qbittorrentTimer.Stop();
    }

    private void ActivateWindowsQBittorrentWorkspace()
    {
        UpdateQBittorrentTimerCadence();
        _ = LoadAndRefreshQBittorrentWorkspaceAsync();
    }

    private async Task LoadAndRefreshQBittorrentWorkspaceAsync()
    {
        var target = _targetSession.SelectedTarget;

        if (target is null || _qbittorrentTelemetry is null)
            return;

        var targetId = target.Id;

        SetText("QBittorrentTargetText", target.DisplayName);
        RefreshQBittorrentDiscoveryEvidence();

        try
        {
            var configuration =
                await _qbittorrentTelemetry.ResolveConfigurationAsync(target);

            if (!IsCurrentQBittorrentTarget(targetId))
                return;

            Get<TextBox>("QBittorrentEndpointTextBox").Text =
                configuration.Endpoint;

            Get<TextBox>("QBittorrentUsernameTextBox").Text =
                configuration.Username;
        }
        catch (Exception exception)
        {
            if (!IsCurrentQBittorrentTarget(targetId))
                return;

            SetText("QBittorrentStatusText", exception.Message);
        }

        if (!IsCurrentQBittorrentTarget(targetId))
            return;

        Get<TextBox>("QBittorrentPasswordTextBox").Text =
            string.Empty;

        if (_qbittorrentCache.TryGetValue(targetId, out var cached))
            ApplyQBittorrentSnapshot(cached);
        else
            SetQBittorrentLoadingState();

        await RefreshQBittorrentTelemetryAsync(showStatus: false);
    }

    private void SetQBittorrentLoadingState()
    {
        SetText("QBittorrentStateText", "CHECKING");
        SetText("QBittorrentVersionText", "--");
        SetText("QBittorrentConnectionText", "--");
        SetText("QBittorrentTotalText", "--");
        SetText("QBittorrentActiveText", "--");
        SetText("QBittorrentDownloadSpeedText", "--");
        SetText("QBittorrentUploadSpeedText", "--");
        SetText("QBittorrentRemainingText", "--");
        SetText("QBittorrentEtaText", "--");

        SetText(
            "QBittorrentTransferAnalyticsText",
            "Waiting for the first authenticated qBittorrent sample.");

        SetText(
            "QBittorrentWorkloadAnalyticsText",
            "Waiting for the first authenticated qBittorrent sample.");

        SetText(
            "QBittorrentSecurityText",
            "WebUI password stays in Windows Credential Manager; SID cookies are memory-only.");

        SetText(
            "QBittorrentStatusText",
            "Waiting for qBittorrent Web API telemetry.");

        SetText("QBittorrentFreshnessText", "CHECKING...");

        SetQBittorrentCollections(snapshot: null);
    }

    private void ApplyQBittorrentSnapshot(
        DownloadClientTelemetrySnapshot snapshot)
    {
        SetText(
            "QBittorrentStateText",
            snapshot.State.ToUpperInvariant());

        SetText(
            "QBittorrentVersionText",
            string.IsNullOrWhiteSpace(snapshot.Version)
                ? "--"
                : $"v{snapshot.Version.TrimStart('v', 'V')}");

        SetText("QBittorrentConnectionText", snapshot.Connection);
        SetText("QBittorrentTotalText", snapshot.TotalCount.ToString());
        SetText("QBittorrentActiveText", snapshot.ActiveCount.ToString());
        SetText("QBittorrentDownloadSpeedText", snapshot.DownloadSpeed);
        SetText("QBittorrentUploadSpeedText", snapshot.UploadSpeed);
        SetText("QBittorrentRemainingText", snapshot.Remaining);
        SetText("QBittorrentEtaText", snapshot.Eta);

        SetText(
            "QBittorrentTransferAnalyticsText",
            $"Connection · {snapshot.Connection}\n" +
            $"Download · {snapshot.DownloadSpeed}   " +
            $"Upload · {snapshot.UploadSpeed}\n" +
            $"Session · {snapshot.SessionDownloaded} down   " +
            $"{snapshot.SessionUploaded} up\n" +
            $"Rate limits · {snapshot.RateLimit}   " +
            $"DHT · {snapshot.DhtNodes} nodes");

        SetText(
            "QBittorrentWorkloadAnalyticsText",
            $"Total · {snapshot.TotalCount}   " +
            $"Active · {snapshot.ActiveCount}\n" +
            $"Downloading · {snapshot.DownloadingCount}   " +
            $"Seeding · {snapshot.SeedingCount}\n" +
            $"Paused · {snapshot.PausedCount}   " +
            $"Stalled · {snapshot.StalledCount}\n" +
            $"Categories · {snapshot.CategoryCount}   " +
            $"Tracker links · {snapshot.TrackerCount}\n" +
            $"Completed today · {snapshot.CompletedRecentCount}   " +
            $"Errors · {snapshot.FailedRecentCount}");

        SetText("QBittorrentSecurityText", snapshot.Security);
        SetText("QBittorrentStatusText", snapshot.Detail);

        SetText(
            "QBittorrentFreshnessText",
            $"LIVE · {QBittorrentCadenceLabel()} · updated " +
            $"{snapshot.SampledAt.ToLocalTime():h:mm:ss tt}");

        SetQBittorrentCollections(snapshot);
    }

    private void SetQBittorrentCollections(
        DownloadClientTelemetrySnapshot? snapshot)
    {
        var queue =
            snapshot?.Queue ??
            new List<DownloadQueueTelemetry>();

        var categories =
            snapshot?.Categories ??
            new List<DownloadCategoryTelemetry>();

        var history =
            snapshot?.History ??
            new List<DownloadHistoryTelemetry>();

        var queueList = Get<ListBox>("QBittorrentQueueList");
        queueList.ItemsSource = queue;
        queueList.IsVisible = queue.Count > 0;

        Get<Border>("QBittorrentQueueEmptyState").IsVisible =
            queue.Count == 0;

        SetText(
            "QBittorrentQueueEmptyText",
            snapshot is null
                ? "Live torrent detail is loading."
                : "No torrents are currently present in qBittorrent.");

        SetText(
            "QBittorrentQueueCountText",
            snapshot is null
                ? "--"
                : $"{queue.Count} shown");

        var categoryList = Get<ListBox>("QBittorrentCategoryList");
        categoryList.ItemsSource = categories;
        categoryList.IsVisible = categories.Count > 0;

        Get<Border>("QBittorrentCategoryEmptyState").IsVisible =
            categories.Count == 0;

        SetText(
            "QBittorrentCategoryEmptyText",
            snapshot is null
                ? "Category inventory is loading."
                : "No configured or active categories were returned.");

        SetText(
            "QBittorrentCategoryCountText",
            snapshot is null
                ? "--"
                : $"{categories.Count} shown");

        var historyList = Get<ListBox>("QBittorrentHistoryList");
        historyList.ItemsSource = history;
        historyList.IsVisible = history.Count > 0;

        Get<Border>("QBittorrentHistoryEmptyState").IsVisible =
            history.Count == 0;

        SetText(
            "QBittorrentHistoryEmptyText",
            snapshot is null
                ? "Recent transfer history is loading."
                : "No completed torrents are available for recent-history display.");

        SetText(
            "QBittorrentHistoryCountText",
            snapshot is null
                ? "--"
                : $"{history.Count} shown");
    }

    private async Task RefreshQBittorrentTelemetryAsync(
        bool showStatus)
    {
        if (_qbittorrentBusy || _qbittorrentTelemetry is null)
            return;

        var target = _targetSession.SelectedTarget;
        if (target is null)
            return;

        var targetId = target.Id;

        var hasCached =
            _qbittorrentCache.TryGetValue(targetId, out var cached);

        _qbittorrentBusy = true;

        SetText(
            "QBittorrentFreshnessText",
            hasCached
                ? $"LIVE · {QBittorrentCadenceLabel()} · updating"
                : $"UPDATING · {QBittorrentCadenceLabel()}");

        if (showStatus)
        {
            SetText(
                "QBittorrentStatusText",
                "Refreshing authenticated qBittorrent telemetry...");
        }

        try
        {
            var snapshot =
                await _qbittorrentTelemetry.CaptureAsync(target);

            _qbittorrentCache[targetId] = snapshot;

            if (IsCurrentQBittorrentTarget(targetId))
                ApplyQBittorrentSnapshot(snapshot);
        }
        catch (Exception exception)
        {
            if (!IsCurrentQBittorrentTarget(targetId))
                return;

            if (hasCached && cached is not null)
            {
                ApplyQBittorrentSnapshot(cached);

                SetText(
                    "QBittorrentFreshnessText",
                    $"STALE · {QBittorrentCadenceLabel()} · retrying");

                SetText(
                    "QBittorrentStatusText",
                    showStatus
                        ? "Last live snapshot retained · " +
                          exception.Message
                        : "Last live snapshot retained while qBittorrent telemetry retries.");
            }
            else
            {
                SetText("QBittorrentStateText", "UNAVAILABLE");
                SetText("QBittorrentFreshnessText", "PROBE FAILED");
                SetText("QBittorrentStatusText", exception.Message);
                SetQBittorrentCollections(snapshot: null);
            }
        }
        finally
        {
            _qbittorrentBusy = false;
        }
    }

    private bool IsCurrentQBittorrentTarget(string targetId) =>
        WindowsQBittorrentTargetLease.IsCurrent(
            targetId,
            _targetSession.SelectedTarget);

    private void UpdateQBittorrentDiscovery(
        IntegrationSnapshot? integration)
    {
        _qbittorrentDiscovery = integration;

        ApplyWindowsMediaNavigationAvailability();
        Get<Button>("QBittorrentNav").IsVisible = true;

        RefreshQBittorrentDiscoveryEvidence();
    }

    private void RefreshQBittorrentDiscoveryEvidence()
    {
        SetText(
            "QBittorrentDiscoveryEvidenceText",
            _qbittorrentDiscovery is null
                ? "No Windows provider evidence was reported. Manual localhost or LAN WebUI configuration remains available."
                : $"{_qbittorrentDiscovery.Kind} · " +
                  $"{_qbittorrentDiscovery.State} · " +
                  $"{_qbittorrentDiscovery.Evidence}");
    }

    private void OnQBittorrentTargetChanged()
    {
        Get<TextBox>("QBittorrentPasswordTextBox").Text =
            string.Empty;

        if (Get<Control>("QBittorrentPage").IsVisible)
            ActivateWindowsQBittorrentWorkspace();
    }

    private void UpdateQBittorrentTimerCadence()
    {
        var interval =
            WindowState == WindowState.Minimized
                ? QBittorrentMinimizedRefreshInterval
                : Get<Control>("QBittorrentPage").IsVisible
                    ? QBittorrentForegroundRefreshInterval
                    : QBittorrentBackgroundRefreshInterval;

        if (_qbittorrentTimer.Interval != interval)
            _qbittorrentTimer.Interval = interval;
    }

    private string QBittorrentCadenceLabel()
    {
        if (WindowState == WindowState.Minimized)
            return "60s minimized";

        return Get<Control>("QBittorrentPage").IsVisible
            ? "10s live"
            : "30s background";
    }

    private async void QBittorrentRefreshButton_OnClick(
        object? sender,
        RoutedEventArgs e) =>
        await RefreshQBittorrentTelemetryAsync(showStatus: true);

    private async void QBittorrentSaveTestButton_OnClick(
        object? sender,
        RoutedEventArgs e)
    {
        if (_qbittorrentTelemetry is null || _qbittorrentBusy)
            return;

        var target = ActiveTargetOrThrow();
        var targetId = target.Id;

        var endpoint =
            Get<TextBox>("QBittorrentEndpointTextBox").Text ??
            string.Empty;

        var username =
            Get<TextBox>("QBittorrentUsernameTextBox").Text ??
            string.Empty;

        var password =
            Get<TextBox>("QBittorrentPasswordTextBox").Text;

        _qbittorrentBusy = true;

        SetText(
            "QBittorrentStatusText",
            "Testing qBittorrent WebUI authentication and protected telemetry...");

        try
        {
            var snapshot =
                await _qbittorrentTelemetry.TestAndSaveAsync(
                    target,
                    endpoint,
                    username,
                    password);

            _qbittorrentCache[targetId] = snapshot;

            if (!IsCurrentQBittorrentTarget(targetId))
                return;

            Get<TextBox>("QBittorrentPasswordTextBox").Text =
                string.Empty;

            ApplyQBittorrentSnapshot(snapshot);

            SetText(
                "QBittorrentStatusText",
                "qBittorrent endpoint, WebUI credentials and protected telemetry verified.");
        }
        catch (Exception exception)
        {
            if (!IsCurrentQBittorrentTarget(targetId))
                return;

            SetText("QBittorrentStatusText", exception.Message);
        }
        finally
        {
            var targetChanged =
                !IsCurrentQBittorrentTarget(targetId);

            _qbittorrentBusy = false;

            if (targetChanged &&
                Get<Control>("QBittorrentPage").IsVisible)
            {
                ActivateWindowsQBittorrentWorkspace();
            }
        }
    }

    private async void QBittorrentClearSavedPasswordButton_OnClick(
        object? sender,
        RoutedEventArgs e)
    {
        if (_qbittorrentTelemetry is null || _qbittorrentBusy)
            return;

        var target = ActiveTargetOrThrow();
        var targetId = target.Id;

        _qbittorrentBusy = true;

        try
        {
            await _qbittorrentTelemetry.ClearSavedPasswordAsync(
                targetId);

            _qbittorrentCache.Remove(targetId);

            if (!IsCurrentQBittorrentTarget(targetId))
                return;

            Get<TextBox>("QBittorrentPasswordTextBox").Text =
                string.Empty;

            SetQBittorrentLoadingState();

            SetText(
                "QBittorrentStatusText",
                "Saved qBittorrent WebUI password removed from Windows Credential Manager.");
        }
        catch (Exception exception)
        {
            if (!IsCurrentQBittorrentTarget(targetId))
                return;

            SetText("QBittorrentStatusText", exception.Message);
        }
        finally
        {
            var targetChanged =
                !IsCurrentQBittorrentTarget(targetId);

            _qbittorrentBusy = false;

            if (targetChanged &&
                Get<Control>("QBittorrentPage").IsVisible)
            {
                ActivateWindowsQBittorrentWorkspace();
            }
        }
    }

    private void QBittorrentOpenButton_OnClick(
        object? sender,
        RoutedEventArgs e)
    {
        try
        {
            var endpoint =
                QBittorrentTelemetryEndpoint.Normalize(
                    Get<TextBox>("QBittorrentEndpointTextBox").Text ??
                    string.Empty);

            Process.Start(
                new ProcessStartInfo(endpoint.AbsoluteUri)
                {
                    UseShellExecute = true
                });

            SetText(
                "QBittorrentStatusText",
                "Opened qBittorrent WebUI.");
        }
        catch (Exception exception)
        {
            SetText("QBittorrentStatusText", exception.Message);
        }
    }
}
