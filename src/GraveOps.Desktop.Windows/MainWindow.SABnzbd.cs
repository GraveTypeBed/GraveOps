using System.Diagnostics;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using GraveOps.Core.Hosts;
using GraveOps.Core.Telemetry;

namespace GraveOps.Desktop.Windows;

public partial class MainWindow
{
    private static readonly TimeSpan
        SABnzbdForegroundRefreshInterval =
            TimeSpan.FromSeconds(
                10);

    private static readonly TimeSpan
        SABnzbdBackgroundRefreshInterval =
            TimeSpan.FromSeconds(
                30);

    private static readonly TimeSpan
        SABnzbdMinimizedRefreshInterval =
            TimeSpan.FromSeconds(
                60);

    private readonly DispatcherTimer
        _sabnzbdTimer =
            new()
            {
                Interval =
                    SABnzbdForegroundRefreshInterval
            };

    private readonly Dictionary<
        string,
        DownloadClientTelemetrySnapshot>
        _sabnzbdCache =
            new(
                StringComparer.Ordinal);

    private WindowsSABnzbdTelemetryService?
        _sabnzbdTelemetry;

    private IntegrationSnapshot?
        _sabnzbdDiscovery;

    private bool _sabnzbdBusy;

    private void InitializeSABnzbdWorkspace()
    {
        _sabnzbdTelemetry =
            new WindowsSABnzbdTelemetryService(
                _targetSession);

        _sabnzbdTimer.Tick +=
            async (_, _) =>
            {
                UpdateSABnzbdTimerCadence();

                await RefreshSABnzbdTelemetryAsync(
                    showStatus:
                        false);
            };

        Opened +=
            (_, _) =>
            {
                UpdateSABnzbdTimerCadence();
                _sabnzbdTimer.Start();
            };

        Closed +=
            (_, _) =>
                _sabnzbdTimer.Stop();
    }

    private void ActivateWindowsSABnzbdWorkspace()
    {
        UpdateSABnzbdTimerCadence();

        _ =
            LoadAndRefreshSABnzbdWorkspaceAsync();
    }

    private async Task LoadAndRefreshSABnzbdWorkspaceAsync()
    {
        var target =
            _targetSession.SelectedTarget;

        if (target is null ||
            _sabnzbdTelemetry is null)
        {
            return;
        }

        var targetId =
            target.Id;

        SetText(
            "SABnzbdTargetText",
            target.DisplayName);

        RefreshSABnzbdDiscoveryEvidence();

        try
        {
            var configuration =
                await _sabnzbdTelemetry.ResolveConfigurationAsync(
                    target);

            if (!IsCurrentSABnzbdTarget(
                    targetId))
            {
                return;
            }

            Get<TextBox>(
                    "SABnzbdEndpointTextBox")
                .Text =
                    configuration.Endpoint;
        }
        catch (Exception exception)
        {
            if (!IsCurrentSABnzbdTarget(
                    targetId))
            {
                return;
            }

            SetText(
                "SABnzbdStatusText",
                exception.Message);
        }

        if (!IsCurrentSABnzbdTarget(
                targetId))
        {
            return;
        }

        Get<TextBox>(
                "SABnzbdApiKeyTextBox")
            .Text =
                string.Empty;

        if (_sabnzbdCache.TryGetValue(
                targetId,
                out var cached))
        {
            ApplySABnzbdSnapshot(
                cached);
        }
        else
        {
            SetSABnzbdLoadingState();
        }

        await RefreshSABnzbdTelemetryAsync(
            showStatus:
                false);
    }

    private void SetSABnzbdLoadingState()
    {
        SetText(
            "SABnzbdStateText",
            "CHECKING");

        SetText(
            "SABnzbdVersionText",
            "--");

        SetText(
            "SABnzbdConnectionText",
            "--");

        SetText(
            "SABnzbdQueueText",
            "--");

        SetText(
            "SABnzbdActiveText",
            "--");

        SetText(
            "SABnzbdDownloadSpeedText",
            "--");

        SetText(
            "SABnzbdRemainingText",
            "--");

        SetText(
            "SABnzbdEtaText",
            "--");

        SetText(
            "SABnzbdFailedRecentText",
            "--");

        SetText(
            "SABnzbdTransferAnalyticsText",
            "Waiting for the first authenticated SABnzbd sample.");

        SetText(
            "SABnzbdHistoryAnalyticsText",
            "Waiting for the first authenticated SABnzbd sample.");

        SetText(
            "SABnzbdSecurityText",
            "The API key stays in Windows Credential Manager.");

        SetText(
            "SABnzbdStatusText",
            "Waiting for SABnzbd API telemetry.");

        SetText(
            "SABnzbdFreshnessText",
            "CHECKING...");

        SetSABnzbdCollections(
            snapshot:
                null);
    }

    private void ApplySABnzbdSnapshot(
        DownloadClientTelemetrySnapshot snapshot)
    {
        SetText(
            "SABnzbdStateText",
            snapshot.State.ToUpperInvariant());

        SetText(
            "SABnzbdVersionText",
            string.IsNullOrWhiteSpace(
                snapshot.Version)
                ? "--"
                : $"v{snapshot.Version.TrimStart('v', 'V')}");

        SetText(
            "SABnzbdConnectionText",
            snapshot.Connection);

        SetText(
            "SABnzbdQueueText",
            snapshot.TotalCount.ToString());

        SetText(
            "SABnzbdActiveText",
            snapshot.ActiveCount.ToString());

        SetText(
            "SABnzbdDownloadSpeedText",
            snapshot.DownloadSpeed);

        SetText(
            "SABnzbdRemainingText",
            snapshot.Remaining);

        SetText(
            "SABnzbdEtaText",
            snapshot.Eta);

        SetText(
            "SABnzbdFailedRecentText",
            snapshot.FailedRecentCount.ToString());

        SetText(
            "SABnzbdTransferAnalyticsText",
            $"Connection · {snapshot.Connection}\n" +
            $"Download · {snapshot.DownloadSpeed}   " +
            $"Remaining · {snapshot.Remaining}\n" +
            $"ETA · {snapshot.Eta}   " +
            $"Rate limit · {snapshot.RateLimit}\n" +
            $"Disk free · {snapshot.DiskFree}");

        SetText(
            "SABnzbdHistoryAnalyticsText",
            $"Today · {snapshot.DayDownloaded}\n" +
            $"Week · {snapshot.WeekDownloaded}\n" +
            $"Month · {snapshot.MonthDownloaded}\n" +
            $"Total · {snapshot.TotalDownloaded}\n" +
            $"Completed recent · {snapshot.CompletedRecentCount}   " +
            $"Failed recent · {snapshot.FailedRecentCount}");

        SetText(
            "SABnzbdSecurityText",
            snapshot.Security);

        SetText(
            "SABnzbdStatusText",
            snapshot.Detail);

        SetText(
            "SABnzbdFreshnessText",
            $"LIVE · {SABnzbdCadenceLabel()} · updated " +
            $"{snapshot.SampledAt.ToLocalTime():h:mm:ss tt}");

        SetSABnzbdCollections(
            snapshot);
    }

    private void SetSABnzbdCollections(
        DownloadClientTelemetrySnapshot? snapshot)
    {
        var queue =
            snapshot?.Queue ??
            new List<DownloadQueueTelemetry>();

        var history =
            snapshot?.History ??
            new List<DownloadHistoryTelemetry>();

        var queueList =
            Get<ListBox>(
                "SABnzbdQueueList");

        queueList.ItemsSource =
            queue;

        queueList.IsVisible =
            queue.Count >
            0;

        Get<Border>(
                "SABnzbdQueueEmptyState")
            .IsVisible =
                queue.Count ==
                0;

        SetText(
            "SABnzbdQueueEmptyText",
            snapshot is null
                ? "Live SABnzbd queue detail is loading."
                : "No jobs are currently queued in SABnzbd.");

        SetText(
            "SABnzbdQueueCountText",
            snapshot is null
                ? "--"
                : $"{queue.Count} shown");

        var historyList =
            Get<ListBox>(
                "SABnzbdHistoryList");

        historyList.ItemsSource =
            history;

        historyList.IsVisible =
            history.Count >
            0;

        Get<Border>(
                "SABnzbdHistoryEmptyState")
            .IsVisible =
                history.Count ==
                0;

        SetText(
            "SABnzbdHistoryEmptyText",
            snapshot is null
                ? "Recent SABnzbd history is loading."
                : "No recent SABnzbd history was returned.");

        SetText(
            "SABnzbdHistoryCountText",
            snapshot is null
                ? "--"
                : $"{history.Count} shown");
    }

    private async Task RefreshSABnzbdTelemetryAsync(
        bool showStatus)
    {
        if (_sabnzbdBusy ||
            _sabnzbdTelemetry is null)
        {
            return;
        }

        var target =
            _targetSession.SelectedTarget;

        if (target is null)
            return;

        var targetId =
            target.Id;

        var hasCached =
            _sabnzbdCache.TryGetValue(
                targetId,
                out var cached);

        _sabnzbdBusy =
            true;

        SetText(
            "SABnzbdFreshnessText",
            hasCached
                ? $"LIVE · {SABnzbdCadenceLabel()} · updating"
                : $"UPDATING · {SABnzbdCadenceLabel()}");

        if (showStatus)
        {
            SetText(
                "SABnzbdStatusText",
                "Refreshing authenticated SABnzbd telemetry...");
        }

        try
        {
            var snapshot =
                await _sabnzbdTelemetry.CaptureAsync(
                    target);

            _sabnzbdCache[targetId] =
                snapshot;

            if (IsCurrentSABnzbdTarget(
                    targetId))
            {
                ApplySABnzbdSnapshot(
                    snapshot);
            }
        }
        catch (Exception exception)
        {
            if (!IsCurrentSABnzbdTarget(
                    targetId))
            {
                return;
            }

            if (hasCached &&
                cached is not null)
            {
                ApplySABnzbdSnapshot(
                    cached);

                SetText(
                    "SABnzbdFreshnessText",
                    $"STALE · {SABnzbdCadenceLabel()} · retrying");

                SetText(
                    "SABnzbdStatusText",
                    showStatus
                        ? "Last live snapshot retained · " +
                          exception.Message
                        : "Last live snapshot retained while SABnzbd telemetry retries.");
            }
            else
            {
                SetText(
                    "SABnzbdStateText",
                    "UNAVAILABLE");

                SetText(
                    "SABnzbdFreshnessText",
                    "PROBE FAILED");

                SetText(
                    "SABnzbdStatusText",
                    exception.Message);

                SetSABnzbdCollections(
                    snapshot:
                        null);
            }
        }
        finally
        {
            _sabnzbdBusy =
                false;
        }
    }

    private bool IsCurrentSABnzbdTarget(
        string targetId) =>
        WindowsSABnzbdTargetLease.IsCurrent(
            targetId,
            _targetSession.SelectedTarget);

    private void UpdateSABnzbdDiscovery(
        IntegrationSnapshot? integration)
    {
        _sabnzbdDiscovery =
            integration;

        ApplyWindowsMediaNavigationAvailability();

        Get<Button>(
                "SABnzbdNav")
            .IsVisible =
                true;

        RefreshSABnzbdDiscoveryEvidence();
    }

    private void RefreshSABnzbdDiscoveryEvidence()
    {
        SetText(
            "SABnzbdDiscoveryEvidenceText",
            _sabnzbdDiscovery is null
                ? "No Windows provider evidence was reported. Manual localhost or LAN API configuration remains available."
                : $"{_sabnzbdDiscovery.Kind} · " +
                  $"{_sabnzbdDiscovery.State} · " +
                  $"{_sabnzbdDiscovery.Evidence}");
    }

    private void OnSABnzbdTargetChanged()
    {
        Get<TextBox>(
                "SABnzbdApiKeyTextBox")
            .Text =
                string.Empty;

        if (Get<Control>(
                "SABnzbdPage")
            .IsVisible)
        {
            ActivateWindowsSABnzbdWorkspace();
        }
    }

    private void UpdateSABnzbdTimerCadence()
    {
        var interval =
            WindowState ==
                WindowState.Minimized
                ? SABnzbdMinimizedRefreshInterval
                : Get<Control>(
                        "SABnzbdPage")
                    .IsVisible
                    ? SABnzbdForegroundRefreshInterval
                    : SABnzbdBackgroundRefreshInterval;

        if (_sabnzbdTimer.Interval !=
            interval)
        {
            _sabnzbdTimer.Interval =
                interval;
        }
    }

    private string SABnzbdCadenceLabel()
    {
        if (WindowState ==
            WindowState.Minimized)
        {
            return "60s minimized";
        }

        return Get<Control>(
                "SABnzbdPage")
            .IsVisible
            ? "10s live"
            : "30s background";
    }

    private async void SABnzbdRefreshButton_OnClick(
        object? sender,
        RoutedEventArgs e) =>
        await RefreshSABnzbdTelemetryAsync(
            showStatus:
                true);

    private async void SABnzbdSaveTestButton_OnClick(
        object? sender,
        RoutedEventArgs e)
    {
        if (_sabnzbdTelemetry is null ||
            _sabnzbdBusy)
        {
            return;
        }

        var target =
            ActiveTargetOrThrow();

        var targetId =
            target.Id;

        var endpoint =
            Get<TextBox>(
                    "SABnzbdEndpointTextBox")
                .Text ??
            string.Empty;

        var apiKey =
            Get<TextBox>(
                    "SABnzbdApiKeyTextBox")
                .Text;

        _sabnzbdBusy =
            true;

        SetText(
            "SABnzbdStatusText",
            "Testing the SABnzbd API key and protected queue/history telemetry...");

        try
        {
            var snapshot =
                await _sabnzbdTelemetry.TestAndSaveAsync(
                    target,
                    endpoint,
                    apiKey);

            _sabnzbdCache[targetId] =
                snapshot;

            if (!IsCurrentSABnzbdTarget(
                    targetId))
            {
                return;
            }

            Get<TextBox>(
                    "SABnzbdApiKeyTextBox")
                .Text =
                    string.Empty;

            ApplySABnzbdSnapshot(
                snapshot);

            SetText(
                "SABnzbdStatusText",
                "SABnzbd endpoint, API key, queue and history telemetry verified.");
        }
        catch (Exception exception)
        {
            if (!IsCurrentSABnzbdTarget(
                    targetId))
            {
                return;
            }

            SetText(
                "SABnzbdStatusText",
                exception.Message);
        }
        finally
        {
            var targetChanged =
                !IsCurrentSABnzbdTarget(
                    targetId);

            _sabnzbdBusy =
                false;

            if (targetChanged &&
                Get<Control>(
                    "SABnzbdPage")
                .IsVisible)
            {
                ActivateWindowsSABnzbdWorkspace();
            }
        }
    }

    private async void SABnzbdClearSavedApiKeyButton_OnClick(
        object? sender,
        RoutedEventArgs e)
    {
        if (_sabnzbdTelemetry is null ||
            _sabnzbdBusy)
        {
            return;
        }

        var target =
            ActiveTargetOrThrow();

        var targetId =
            target.Id;

        _sabnzbdBusy =
            true;

        try
        {
            await _sabnzbdTelemetry.ClearSavedApiKeyAsync(
                targetId);

            _sabnzbdCache.Remove(
                targetId);

            if (!IsCurrentSABnzbdTarget(
                    targetId))
            {
                return;
            }

            Get<TextBox>(
                    "SABnzbdApiKeyTextBox")
                .Text =
                    string.Empty;

            SetSABnzbdLoadingState();

            SetText(
                "SABnzbdStatusText",
                "Saved SABnzbd API key removed from Windows Credential Manager.");
        }
        catch (Exception exception)
        {
            if (!IsCurrentSABnzbdTarget(
                    targetId))
            {
                return;
            }

            SetText(
                "SABnzbdStatusText",
                exception.Message);
        }
        finally
        {
            var targetChanged =
                !IsCurrentSABnzbdTarget(
                    targetId);

            _sabnzbdBusy =
                false;

            if (targetChanged &&
                Get<Control>(
                    "SABnzbdPage")
                .IsVisible)
            {
                ActivateWindowsSABnzbdWorkspace();
            }
        }
    }

    private void SABnzbdOpenButton_OnClick(
        object? sender,
        RoutedEventArgs e)
    {
        try
        {
            var endpoint =
                SABnzbdTelemetryEndpoint.Normalize(
                    Get<TextBox>(
                            "SABnzbdEndpointTextBox")
                        .Text ??
                    string.Empty);

            Process.Start(
                new ProcessStartInfo(
                    endpoint.AbsoluteUri)
                {
                    UseShellExecute =
                        true
                });

            SetText(
                "SABnzbdStatusText",
                "Opened SABnzbd.");
        }
        catch (Exception exception)
        {
            SetText(
                "SABnzbdStatusText",
                exception.Message);
        }
    }
}
