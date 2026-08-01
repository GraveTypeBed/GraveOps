using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;

namespace GraveOps.Desktop.Linux;

public partial class MainWindow
{
    private readonly LinuxDownloadClientTelemetryService
        _downloadClientTelemetry =
            new();

    private readonly DispatcherTimer
        _downloadClientTimer =
            new()
            {
                Interval = TimeSpan.FromSeconds(10)
            };

    private readonly Dictionary<
        string,
        LinuxDownloadClientSnapshot>
        _downloadClientCache =
            new(
                StringComparer.OrdinalIgnoreCase);

    private string _activeDownloadClient =
        "SABnzbd";

    private bool _downloadClientBusy;

    private void InitializeDownloadClientWorkspace()
    {
        _downloadClientTimer.Tick +=
            async (_, _) =>
            {
                if (Get<Control>(
                        "DownloadClientWorkspacePage")
                    .IsVisible)
                {
                    await RefreshDownloadClientTelemetryAsync(
                        showStatus: false);
                }
            };

        Opened +=
            (_, _) =>
                _downloadClientTimer.Start();

        Closed +=
            (_, _) =>
                _downloadClientTimer.Stop();
    }

    private void ActivateDownloadClient(
        string clientKey)
    {
        var normalized =
            LinuxDownloadClientTelemetryService
                .NormalizeClientKey(clientKey);

        if (!LinuxDownloadClientTelemetryService
                .IsSupported(normalized))
        {
            return;
        }

        _activeDownloadClient =
            normalized;

        SelectIntegrationByName(
            normalized);

        PopulateDownloadClientWorkspace();

        _ =
            RefreshDownloadClientTelemetryAsync(
                showStatus: false);
    }

    private void PopulateDownloadClientWorkspace()
    {
        ConfigureDownloadClientSurface();

        if (_downloadClientCache.TryGetValue(
                _activeDownloadClient,
                out var snapshot))
        {
            ApplyDownloadClientSnapshot(
                snapshot);
        }
        else
        {
            SetDownloadClientLoadingState();
        }

        UpdateDownloadClientOpenState();
    }

    private void ConfigureDownloadClientSurface()
    {
        var isQbit =
            _activeDownloadClient.Equals(
                "qBittorrent",
                StringComparison.OrdinalIgnoreCase);

        Get<TextBlock>(
                "DownloadClientHeadingText")
            .Text =
            _activeDownloadClient;

        Get<TextBlock>(
                "DownloadClientTargetText")
            .Text =
            _controlPlane.ActiveProfile.DisplayName;

        Get<TextBlock>(
                "DownloadClientDescriptionText")
            .Text =
            isQbit
                ? "Torrent transfer analytics, progress, ETA, seeding and protected container-local API telemetry."
                : "Usenet queue analytics, progress, remaining data, ETA and recent SABnzbd history.";

        Get<TextBlock>(
                "DownloadClientOperationsHintText")
            .Text =
            isQbit
                ? "The Web UI remains authenticated on the host or LAN. GraveOps reads telemetry only from inside the qBittorrent container."
                : "GraveOps uses the SABnzbd API key only on the Linux host. The key is never returned to the interface.";

        Get<Button>(
                "DownloadClientOpenButton")
            .Content =
            isQbit
                ? "Open qBittorrent"
                : "Open SABnzbd";

        Get<Button>(
                "DownloadClientDockerButton")
            .IsVisible =
            isQbit ||
            ActiveDownloadClientIntegration()?
                .Kind.Contains(
                    "docker",
                    StringComparison.OrdinalIgnoreCase) ==
                true;

        Get<TextBlock>(
                "DownloadClientItemsLabelText")
            .Text =
            isQbit
                ? "TORRENTS"
                : "QUEUE";

        Get<TextBlock>(
                "DownloadClientMetric1LabelText")
            .Text =
            "DOWNLOAD";

        Get<TextBlock>(
                "DownloadClientMetric2LabelText")
            .Text =
            isQbit
                ? "UPLOAD"
                : "REMAINING";

        Get<TextBlock>(
                "DownloadClientMetric3LabelText")
            .Text =
            isQbit
                ? "REMAINING"
                : "ETA";

        Get<TextBlock>(
                "DownloadClientMetric4LabelText")
            .Text =
            isQbit
                ? "NEXT ETA"
                : "FAILED RECENT";

        Get<TextBlock>(
                "DownloadClientCurrentWorkHeadingText")
            .Text =
            isQbit
                ? "Current torrents"
                : "Current downloads";

        Get<TextBlock>(
                "DownloadClientCurrentWorkHintText")
            .Text =
            isQbit
                ? "Progress, transfer rate, remaining work, ETA, ratio and peers from qBittorrent."
                : "Queue status, progress, remaining size and time from SABnzbd.";

        Get<TextBlock>(
                "DownloadClientHistoryHeadingText")
            .Text =
            isQbit
                ? "Recently completed"
                : "Recent history";

        Get<TextBlock>(
                "DownloadClientHistoryHintText")
            .Text =
            isQbit
                ? "Most recently completed torrents still present in qBittorrent."
                : "Recent completed and failed SABnzbd jobs.";

        Get<StackPanel>(
                "DownloadClientQbitQueueTable")
            .IsVisible =
            isQbit;

        Get<StackPanel>(
                "DownloadClientSabQueueTable")
            .IsVisible =
            !isQbit;

        Get<TextBlock>(
                "DownloadClientHistoryDetailHeaderText")
            .Text =
            isQbit
                ? "DETAIL"
                : "SOURCE / DETAIL";
    }

    private void SetDownloadClientLoadingState()
    {
        var isQbit =
            _activeDownloadClient.Equals(
                "qBittorrent",
                StringComparison.OrdinalIgnoreCase);

        var state =
            Get<TextBlock>(
                "DownloadClientStateText");

        state.Text =
            "CHECKING";

        state.Foreground =
            OpsPalette.Foreground(
                OpsSeverity.Info);

        Get<TextBlock>(
                "DownloadClientSecurityText")
            .Text =
            isQbit
                ? "Container-local protected API"
                : "Linux-host API key";

        Get<TextBlock>(
                "DownloadClientVersionText")
            .Text =
            "--";

        Get<TextBlock>(
                "DownloadClientConnectionText")
            .Text =
            "--";

        Get<TextBlock>(
                "DownloadClientActiveText")
            .Text =
            "--";

        Get<TextBlock>(
                "DownloadClientActiveDetailText")
            .Text =
            "Telemetry pending";

        Get<TextBlock>(
                "DownloadClientItemsText")
            .Text =
            "--";

        Get<TextBlock>(
                "DownloadClientItemsDetailText")
            .Text =
            "Telemetry pending";

        Get<TextBlock>(
                "DownloadClientMetric1ValueText")
            .Text =
            "--";

        Get<TextBlock>(
                "DownloadClientMetric2ValueText")
            .Text =
            "--";

        Get<TextBlock>(
                "DownloadClientMetric3ValueText")
            .Text =
            "--";

        Get<TextBlock>(
                "DownloadClientMetric4ValueText")
            .Text =
            "--";

        Get<TextBlock>(
                "DownloadClientTransferAnalyticsText")
            .Text =
            "Waiting for the first live client sample.";

        Get<TextBlock>(
                "DownloadClientWorkloadAnalyticsText")
            .Text =
            "Waiting for the first live client sample.";

        Get<TextBlock>(
                "DownloadClientFreshnessText")
            .Text =
            "CHECKING...";

        Get<TextBlock>(
                "DownloadClientStatusText")
            .Text =
            $"Loading {_activeDownloadClient} analytics...";

        SetDownloadClientCollections(
            snapshot: null);
    }

    private void ApplyDownloadClientSnapshot(
        LinuxDownloadClientSnapshot snapshot)
    {
        var isQbit =
            snapshot.ClientKey.Equals(
                "qBittorrent",
                StringComparison.OrdinalIgnoreCase);

        var state =
            Get<TextBlock>(
                "DownloadClientStateText");

        state.Text =
            snapshot.State.ToUpperInvariant();

        state.Foreground =
            OpsPalette.Foreground(
                DownloadClientSeverity(
                    snapshot.State));

        Get<TextBlock>(
                "DownloadClientSecurityText")
            .Text =
            snapshot.Security;

        Get<TextBlock>(
                "DownloadClientVersionText")
            .Text =
            string.IsNullOrWhiteSpace(
                snapshot.Version)
                ? "--"
                : $"v{snapshot.Version.TrimStart('v', 'V')}";

        Get<TextBlock>(
                "DownloadClientConnectionText")
            .Text =
            snapshot.Connection;

        Get<TextBlock>(
                "DownloadClientActiveText")
            .Text =
            snapshot.ActiveCount.ToString();

        Get<TextBlock>(
                "DownloadClientItemsText")
            .Text =
            snapshot.TotalCount.ToString();

        if (isQbit)
        {
            Get<TextBlock>(
                    "DownloadClientActiveDetailText")
                .Text =
                $"{snapshot.DownloadingCount} downloading · " +
                $"{snapshot.SeedingCount} seeding";

            Get<TextBlock>(
                    "DownloadClientItemsDetailText")
                .Text =
                $"{snapshot.PausedCount} paused · " +
                $"{snapshot.StalledCount} stalled";

            Get<TextBlock>(
                    "DownloadClientMetric1ValueText")
                .Text =
                snapshot.DownloadSpeed;

            Get<TextBlock>(
                    "DownloadClientMetric2ValueText")
                .Text =
                snapshot.UploadSpeed;

            Get<TextBlock>(
                    "DownloadClientMetric3ValueText")
                .Text =
                snapshot.Remaining;

            Get<TextBlock>(
                    "DownloadClientMetric4ValueText")
                .Text =
                snapshot.Eta;

            Get<TextBlock>(
                    "DownloadClientTransferAnalyticsText")
                .Text =
                $"Connection · {snapshot.Connection}\n" +
                $"Download · {snapshot.DownloadSpeed}   " +
                $"Upload · {snapshot.UploadSpeed}\n" +
                $"Session · {snapshot.SessionDownloaded} down   " +
                $"{snapshot.SessionUploaded} up\n" +
                $"Rate limits · {snapshot.RateLimit}   " +
                $"DHT · {snapshot.DhtNodes} nodes";

            Get<TextBlock>(
                    "DownloadClientWorkloadAnalyticsText")
                .Text =
                $"Total torrents · {snapshot.TotalCount}   " +
                $"Active · {snapshot.ActiveCount}\n" +
                $"Downloading · {snapshot.DownloadingCount}   " +
                $"Seeding · {snapshot.SeedingCount}\n" +
                $"Paused · {snapshot.PausedCount}   " +
                $"Stalled · {snapshot.StalledCount}\n" +
                $"Remaining · {snapshot.Remaining}   " +
                $"Completed today · {snapshot.CompletedRecentCount}";
        }
        else
        {
            Get<TextBlock>(
                    "DownloadClientActiveDetailText")
                .Text =
                $"{snapshot.DownloadingCount} downloading · " +
                $"{snapshot.PausedCount} paused";

            Get<TextBlock>(
                    "DownloadClientItemsDetailText")
                .Text =
                $"{snapshot.CompletedRecentCount} completed · " +
                $"{snapshot.FailedRecentCount} failed recent";

            Get<TextBlock>(
                    "DownloadClientMetric1ValueText")
                .Text =
                snapshot.DownloadSpeed;

            Get<TextBlock>(
                    "DownloadClientMetric2ValueText")
                .Text =
                snapshot.Remaining;

            Get<TextBlock>(
                    "DownloadClientMetric3ValueText")
                .Text =
                snapshot.Eta;

            Get<TextBlock>(
                    "DownloadClientMetric4ValueText")
                .Text =
                snapshot.FailedRecentCount.ToString();

            Get<TextBlock>(
                    "DownloadClientTransferAnalyticsText")
                .Text =
                $"Download · {snapshot.DownloadSpeed}   " +
                $"Remaining · {snapshot.Remaining}   " +
                $"ETA · {snapshot.Eta}\n" +
                $"Today · {snapshot.DayDownloaded}   " +
                $"Week · {snapshot.WeekDownloaded}\n" +
                $"Month · {snapshot.MonthDownloaded}   " +
                $"Total · {snapshot.TotalDownloaded}\n" +
                $"Rate limit · {snapshot.RateLimit}   " +
                $"Disk free · {snapshot.DiskFree}";

            Get<TextBlock>(
                    "DownloadClientWorkloadAnalyticsText")
                .Text =
                $"Queued jobs · {snapshot.TotalCount}   " +
                $"Active · {snapshot.ActiveCount}\n" +
                $"Downloading · {snapshot.DownloadingCount}   " +
                $"Paused · {snapshot.PausedCount}\n" +
                $"Recent completed · {snapshot.CompletedRecentCount}   " +
                $"Failed · {snapshot.FailedRecentCount}\n" +
                (string.IsNullOrWhiteSpace(
                     snapshot.Detail)
                    ? "Read-only SABnzbd analytics."
                    : snapshot.Detail);
        }

        Get<TextBlock>(
                "DownloadClientFreshnessText")
            .Text =
            $"LIVE · updated " +
            $"{snapshot.SampledAt.ToLocalTime():h:mm:ss tt}";

        Get<TextBlock>(
                "DownloadClientStatusText")
            .Text =
            string.IsNullOrWhiteSpace(
                snapshot.Detail)
                ? $"{snapshot.ClientKey} telemetry refreshed."
                : snapshot.Detail;

        SetDownloadClientCollections(
            snapshot);

        UpdateDownloadClientOpenState();
    }

    private void SetDownloadClientCollections(
        LinuxDownloadClientSnapshot? snapshot)
    {
        var queue =
            snapshot?.Queue ??
            new List<LinuxDownloadQueueRow>();

        var history =
            snapshot?.History ??
            new List<LinuxDownloadHistoryRow>();

        var isQbit =
            _activeDownloadClient.Equals(
                "qBittorrent",
                StringComparison.OrdinalIgnoreCase);

        var qbitQueueList =
            Get<ListBox>(
                "DownloadClientQueueList");

        var sabQueueList =
            Get<ListBox>(
                "DownloadClientSabQueueList");

        qbitQueueList.ItemsSource =
            isQbit
                ? queue
                : Array.Empty<LinuxDownloadQueueRow>();

        sabQueueList.ItemsSource =
            isQbit
                ? Array.Empty<LinuxDownloadQueueRow>()
                : queue;

        qbitQueueList.IsVisible =
            isQbit &&
            queue.Count > 0;

        sabQueueList.IsVisible =
            !isQbit &&
            queue.Count > 0;

        var queueEmpty =
            Get<Border>(
                "DownloadClientQueueEmptyState");

        queueEmpty.IsVisible =
            queue.Count == 0;

        Get<TextBlock>(
                "DownloadClientQueueEmptyText")
            .Text =
            snapshot is null
                ? "Live download detail is loading..."
                : _activeDownloadClient.Equals(
                    "qBittorrent",
                    StringComparison.OrdinalIgnoreCase)
                    ? "No torrents are currently present in qBittorrent."
                    : "No jobs are currently queued in SABnzbd.";

        Get<TextBlock>(
                "DownloadClientQueueCountText")
            .Text =
            snapshot is null
                ? "--"
                : $"{queue.Count} shown";

        var historyList =
            Get<ListBox>(
                "DownloadClientHistoryList");

        historyList.ItemsSource =
            history;

        historyList.IsVisible =
            history.Count > 0;

        var historyEmpty =
            Get<Border>(
                "DownloadClientHistoryEmptyState");

        historyEmpty.IsVisible =
            history.Count == 0;

        Get<TextBlock>(
                "DownloadClientHistoryEmptyText")
            .Text =
            snapshot is null
                ? "Recent history is loading..."
                : _activeDownloadClient.Equals(
                    "qBittorrent",
                    StringComparison.OrdinalIgnoreCase)
                    ? "No completed torrents are available for recent-history display."
                    : "No recent SABnzbd history is available.";

        Get<TextBlock>(
                "DownloadClientHistoryCountText")
            .Text =
            snapshot is null
                ? "--"
                : $"{history.Count} shown";
    }

    private async Task RefreshDownloadClientTelemetryAsync(
        bool showStatus)
    {
        if (_downloadClientBusy ||
            !LinuxDownloadClientTelemetryService
                .IsSupported(
                    _activeDownloadClient))
        {
            return;
        }

        _downloadClientBusy =
            true;

        var button =
            Get<Button>(
                "DownloadClientRefreshButton");

        button.IsEnabled =
            false;

        Get<TextBlock>(
                "DownloadClientFreshnessText")
            .Text =
            "CHECKING...";

        if (showStatus)
        {
            Get<TextBlock>(
                    "DownloadClientStatusText")
                .Text =
                $"Refreshing {_activeDownloadClient} analytics...";
        }

        try
        {
            var requestedClient =
                _activeDownloadClient;

            var snapshot =
                await _downloadClientTelemetry.CaptureAsync(
                    _controlPlane,
                    requestedClient);

            _downloadClientCache[requestedClient] =
                snapshot;

            if (_activeDownloadClient.Equals(
                    requestedClient,
                    StringComparison.OrdinalIgnoreCase))
            {
                ApplyDownloadClientSnapshot(
                    snapshot);
            }
        }
        catch (Exception exception)
        {
            var state =
                Get<TextBlock>(
                    "DownloadClientStateText");

            state.Text =
                "UNAVAILABLE";

            state.Foreground =
                OpsPalette.Foreground(
                    OpsSeverity.Error);

            Get<TextBlock>(
                    "DownloadClientFreshnessText")
                .Text =
                "PROBE FAILED";

            Get<TextBlock>(
                    "DownloadClientStatusText")
                .Text =
                exception.Message;
        }
        finally
        {
            _downloadClientBusy =
                false;

            button.IsEnabled =
                true;
        }
    }

    private void DownloadClientRefreshButton_OnClick(
        object? sender,
        RoutedEventArgs e) =>
        _ =
            RefreshDownloadClientTelemetryAsync(
                showStatus: true);

    private async void DownloadClientOpenButton_OnClick(
        object? sender,
        RoutedEventArgs e)
    {
        var integration =
            ActiveDownloadClientIntegration();

        if (integration is null)
            return;

        var url =
            ResolveIntegrationUrl(
                integration);

        if (url is null)
            return;

        try
        {
            using var process =
                new Process
                {
                    StartInfo =
                        new ProcessStartInfo
                        {
                            FileName = "xdg-open",
                            UseShellExecute = false,
                            CreateNoWindow = true
                        }
                };

            process.StartInfo.ArgumentList.Add(
                url);

            process.Start();

            Get<TextBlock>(
                    "DownloadClientStatusText")
                .Text =
                $"Opened {url}";

            await Task.CompletedTask;
        }
        catch (Exception exception)
        {
            Get<TextBlock>(
                    "DownloadClientStatusText")
                .Text =
                $"Could not open interface: {exception.Message}";
        }
    }

    private void DownloadClientDockerButton_OnClick(
        object? sender,
        RoutedEventArgs e) =>
        Navigate("DockerNav");

    private void DownloadClientLogsButton_OnClick(
        object? sender,
        RoutedEventArgs e) =>
        Navigate("LogsNav");

    private void DownloadClientTerminalButton_OnClick(
        object? sender,
        RoutedEventArgs e) =>
        Navigate("ToolsNav");

    private OpsIntegration?
        ActiveDownloadClientIntegration() =>
        _integrations.FirstOrDefault(item =>
            item.Name.Equals(
                _activeDownloadClient,
                StringComparison.OrdinalIgnoreCase));

    private void UpdateDownloadClientOpenState()
    {
        var integration =
            ActiveDownloadClientIntegration();

        Get<Button>(
                "DownloadClientOpenButton")
            .IsEnabled =
            integration is not null &&
            ResolveIntegrationUrl(
                integration) is not null;
    }

    private static OpsSeverity
        DownloadClientSeverity(
            string state)
    {
        if (state.Contains(
                "online",
                StringComparison.OrdinalIgnoreCase))
        {
            return OpsSeverity.Healthy;
        }

        if (state.Contains(
                "paused",
                StringComparison.OrdinalIgnoreCase) ||
            state.Contains(
                "degraded",
                StringComparison.OrdinalIgnoreCase))
        {
            return OpsSeverity.Warning;
        }

        if (state.Contains(
                "offline",
                StringComparison.OrdinalIgnoreCase) ||
            state.Contains(
                "unavailable",
                StringComparison.OrdinalIgnoreCase) ||
            state.Contains(
                "error",
                StringComparison.OrdinalIgnoreCase))
        {
            return OpsSeverity.Error;
        }

        return OpsSeverity.Info;
    }
}
