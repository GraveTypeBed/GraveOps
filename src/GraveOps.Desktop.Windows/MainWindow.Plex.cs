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
        PlexForegroundRefreshInterval =
            TimeSpan.FromSeconds(
                5);

    private static readonly TimeSpan
        PlexBackgroundRefreshInterval =
            TimeSpan.FromSeconds(
                15);

    private static readonly TimeSpan
        PlexMinimizedRefreshInterval =
            TimeSpan.FromSeconds(
                45);

    private readonly DispatcherTimer
        _plexTimer =
            new()
            {
                Interval =
                    PlexForegroundRefreshInterval
            };

    private readonly Dictionary<
        string,
        PlexTelemetrySnapshot>
        _plexCache =
            new(
                StringComparer.Ordinal);

    private WindowsPlexTelemetryService?
        _plexTelemetry;

    private IntegrationSnapshot?
        _plexDiscovery;

    private bool _plexBusy;

    private void InitializePlexWorkspace()
    {
        _plexTelemetry =
            new WindowsPlexTelemetryService(
                _targetSession);

        _plexTimer.Tick +=
            async (_, _) =>
            {
                UpdatePlexTimerCadence();

                await RefreshPlexTelemetryAsync(
                    showStatus: false);
            };

        Opened +=
            (_, _) =>
            {
                UpdatePlexTimerCadence();
                _plexTimer.Start();
            };

        Closed +=
            (_, _) =>
                _plexTimer.Stop();
    }

    private void ActivateWindowsPlexWorkspace()
    {
        UpdatePlexTimerCadence();

        _ =
            LoadAndRefreshPlexWorkspaceAsync();
    }

    private async Task LoadAndRefreshPlexWorkspaceAsync()
    {
        var target =
            _targetSession.SelectedTarget;

        if (target is null ||
            _plexTelemetry is null)
        {
            return;
        }

        SetText(
            "PlexTargetText",
            target.DisplayName);

        try
        {
            var endpoint =
                await _plexTelemetry.ResolveEndpointAsync(
                    target);

            Get<TextBox>(
                "PlexEndpointTextBox")
                .Text =
                    endpoint.AbsoluteUri;
        }
        catch (Exception exception)
        {
            SetText(
                "PlexStatusText",
                exception.Message);
        }

        Get<TextBox>(
            "PlexTokenTextBox")
            .Text =
                string.Empty;

        if (_plexCache.TryGetValue(
                target.Id,
                out var cached))
        {
            ApplyPlexSnapshot(
                cached);
        }
        else
        {
            SetPlexLoadingState();
        }

        await RefreshPlexTelemetryAsync(
            showStatus: false);
    }

    private void SetPlexLoadingState()
    {
        SetText(
            "PlexServiceText",
            "CHECKING");

        SetText(
            "PlexServiceDetailText",
            _plexDiscovery is null
                ? "Waiting for API identity"
                : $"{_plexDiscovery.Kind} · {_plexDiscovery.State}");

        SetText(
            "PlexVersionText",
            "--");

        SetText(
            "PlexConnectionText",
            "Waiting for Plex API");

        SetText(
            "PlexActiveSessionsText",
            "--");

        SetText(
            "PlexDirectPlayText",
            "--");

        SetText(
            "PlexDirectStreamText",
            "--");

        SetText(
            "PlexTranscodeText",
            "--");

        SetText(
            "PlexLibrariesText",
            "--");

        SetText(
            "PlexBandwidthText",
            "--");

        SetText(
            "PlexSessionCountText",
            "--");

        Get<ListBox>(
            "PlexSessionsList")
            .ItemsSource =
                Array.Empty<PlexSessionTelemetry>();

        Get<ListBox>(
            "PlexSessionsList")
            .IsVisible =
                false;

        Get<Border>(
            "PlexSessionsEmptyState")
            .IsVisible =
                true;

        SetText(
            "PlexSessionsEmptyText",
            "Plex session telemetry is loading.");

        SetText(
            "PlexSecurityText",
            "Tokens are kept in Windows Credential Manager or read transiently from the local Windows Plex registry.");

        SetText(
            "PlexStatusText",
            "Waiting for Plex telemetry.");

        SetText(
            "PlexFreshnessText",
            "CHECKING...");
    }

    private void ApplyPlexSnapshot(
        PlexTelemetrySnapshot snapshot)
    {
        SetText(
            "PlexServiceText",
            snapshot.State.ToUpperInvariant());

        SetText(
            "PlexServiceDetailText",
            $"{snapshot.Service} · {snapshot.ServiceDetail}");

        SetText(
            "PlexVersionText",
            string.IsNullOrWhiteSpace(
                snapshot.Version)
                ? "--"
                : $"v{snapshot.Version.TrimStart('v', 'V')}");

        SetText(
            "PlexConnectionText",
            snapshot.Connection);

        SetText(
            "PlexActiveSessionsText",
            snapshot.ActiveSessions.ToString());

        SetText(
            "PlexDirectPlayText",
            snapshot.DirectPlayCount.ToString());

        SetText(
            "PlexDirectStreamText",
            snapshot.DirectStreamCount.ToString());

        SetText(
            "PlexTranscodeText",
            snapshot.TranscodeCount.ToString());

        SetText(
            "PlexLibrariesText",
            snapshot.LibraryCount.ToString());

        SetText(
            "PlexBandwidthText",
            snapshot.TotalBandwidth);

        var sessions =
            snapshot.Sessions ??
            new List<PlexSessionTelemetry>();

        Get<ListBox>(
            "PlexSessionsList")
            .ItemsSource =
                sessions;

        Get<ListBox>(
            "PlexSessionsList")
            .IsVisible =
                sessions.Count > 0;

        Get<Border>(
            "PlexSessionsEmptyState")
            .IsVisible =
                sessions.Count == 0;

        SetText(
            "PlexSessionsEmptyText",
            snapshot.Security.Contains(
                "Identity-only",
                StringComparison.OrdinalIgnoreCase)
                ? "Protected session and library telemetry needs a Plex token."
                : "No viewers are currently streaming from this Plex server.");

        SetText(
            "PlexSessionCountText",
            $"{sessions.Count} " +
            (sessions.Count == 1
                ? "session"
                : "sessions"));

        SetText(
            "PlexSecurityText",
            snapshot.Security);

        SetText(
            "PlexStatusText",
            string.IsNullOrWhiteSpace(
                snapshot.Detail)
                ? "Plex telemetry refreshed."
                : snapshot.Detail);

        SetText(
            "PlexFreshnessText",
            $"LIVE · {PlexCadenceLabel()} · updated " +
            $"{snapshot.SampledAt.ToLocalTime():h:mm:ss tt}");
    }

    private async Task RefreshPlexTelemetryAsync(
        bool showStatus)
    {
        if (_plexBusy ||
            _plexTelemetry is null)
        {
            return;
        }

        var target =
            _targetSession.SelectedTarget;

        if (target is null)
            return;

        _plexBusy =
            true;

        var targetId =
            target.Id;

        var hasCachedSnapshot =
            _plexCache.TryGetValue(
                targetId,
                out var cachedSnapshot);

        SetText(
            "PlexFreshnessText",
            hasCachedSnapshot
                ? $"LIVE · {PlexCadenceLabel()} · updating"
                : $"UPDATING · {PlexCadenceLabel()}");

        if (showStatus)
        {
            SetText(
                "PlexStatusText",
                "Refreshing Plex telemetry...");
        }

        try
        {
            var snapshot =
                await _plexTelemetry.CaptureAsync(
                    target);

            _plexCache[targetId] =
                snapshot;

            if (_targetSession.SelectedTarget?
                    .Id.Equals(
                        targetId,
                        StringComparison.Ordinal) ==
                true)
            {
                ApplyPlexSnapshot(
                    snapshot);
            }
        }
        catch (Exception exception)
        {
            if (_targetSession.SelectedTarget?
                    .Id.Equals(
                        targetId,
                        StringComparison.Ordinal) !=
                true)
            {
                return;
            }

            if (hasCachedSnapshot &&
                cachedSnapshot is not null)
            {
                ApplyPlexSnapshot(
                    cachedSnapshot);

                SetText(
                    "PlexFreshnessText",
                    $"STALE · {PlexCadenceLabel()} · retrying");

                SetText(
                    "PlexStatusText",
                    showStatus
                        ? "Last live snapshot retained · " +
                          exception.Message
                        : "Last live snapshot retained while Plex telemetry retries.");
            }
            else
            {
                SetText(
                    "PlexServiceText",
                    "UNAVAILABLE");

                SetText(
                    "PlexFreshnessText",
                    "PROBE FAILED");

                SetText(
                    "PlexStatusText",
                    exception.Message);

                Get<ListBox>(
                    "PlexSessionsList")
                    .IsVisible =
                        false;

                Get<Border>(
                    "PlexSessionsEmptyState")
                    .IsVisible =
                        true;

                SetText(
                    "PlexSessionsEmptyText",
                    "Plex API telemetry is unavailable.");
            }
        }
        finally
        {
            _plexBusy =
                false;
        }
    }

    private void UpdatePlexDiscovery(
        IntegrationSnapshot? integration)
    {
        _plexDiscovery =
            integration;

        ApplyWindowsMediaNavigationAvailability();

        SetText(
            "PlexDiscoveryEvidenceText",
            integration is null
                ? "No Windows provider evidence was reported. Manual LAN or localhost API configuration remains available."
                : $"{integration.Kind} · {integration.State} · {integration.Evidence}");
    }
    private void OnPlexTargetChanged()
    {
        Get<TextBox>(
            "PlexTokenTextBox")
            .Text =
                string.Empty;

        if (Get<Control>(
                "PlexPage")
            .IsVisible)
        {
            ActivateWindowsPlexWorkspace();
        }
    }

    private void UpdatePlexTimerCadence()
    {
        var interval =
            WindowState == WindowState.Minimized
                ? PlexMinimizedRefreshInterval
                : Get<Control>(
                        "PlexPage")
                    .IsVisible
                    ? PlexForegroundRefreshInterval
                    : PlexBackgroundRefreshInterval;

        if (_plexTimer.Interval !=
            interval)
        {
            _plexTimer.Interval =
                interval;
        }
    }

    private string PlexCadenceLabel()
    {
        if (WindowState ==
            WindowState.Minimized)
        {
            return "45s minimized";
        }

        return Get<Control>(
                "PlexPage")
            .IsVisible
            ? "5s live"
            : "15s background";
    }

    private async void PlexRefreshButton_OnClick(
        object? sender,
        RoutedEventArgs e) =>
        await RefreshPlexTelemetryAsync(
            showStatus: true);

    private async void PlexSaveTestButton_OnClick(
        object? sender,
        RoutedEventArgs e)
    {
        if (_plexTelemetry is null)
            return;

        var target =
            ActiveTargetOrThrow();

        var endpoint =
            Get<TextBox>(
                "PlexEndpointTextBox")
            .Text ??
            string.Empty;

        var token =
            Get<TextBox>(
                "PlexTokenTextBox")
            .Text;

        SetText(
            "PlexStatusText",
            "Testing Plex API access...");

        try
        {
            var snapshot =
                await _plexTelemetry.TestAndSaveAsync(
                    target,
                    endpoint,
                    token);

            _plexCache[target.Id] =
                snapshot;

            Get<TextBox>(
                "PlexTokenTextBox")
                .Text =
                    string.Empty;

            ApplyPlexSnapshot(
                snapshot);

            SetText(
                "PlexStatusText",
                "Plex endpoint verified. Configuration saved without secrets.");
        }
        catch (Exception exception)
        {
            SetText(
                "PlexStatusText",
                exception.Message);
        }
    }

    private async void PlexClearTokenButton_OnClick(
        object? sender,
        RoutedEventArgs e)
    {
        if (_plexTelemetry is null)
            return;

        try
        {
            var target =
                ActiveTargetOrThrow();

            await _plexTelemetry.ClearTokenAsync(
                target.Id);

            _plexCache.Remove(
                target.Id);

            Get<TextBox>(
                "PlexTokenTextBox")
                .Text =
                    string.Empty;

            SetText(
                "PlexStatusText",
                "Saved Plex token removed from Windows Credential Manager. Native local Plex may still be discovered through its Windows registry token.");

            await RefreshPlexTelemetryAsync(
                showStatus: false);
        }
        catch (Exception exception)
        {
            SetText(
                "PlexStatusText",
                exception.Message);
        }
    }

    private void PlexOpenButton_OnClick(
        object? sender,
        RoutedEventArgs e)
    {
        try
        {
            var endpoint =
                WindowsPlexEndpointPolicy.Normalize(
                    Get<TextBox>(
                            "PlexEndpointTextBox")
                        .Text ??
                    string.Empty);

            var web =
                new Uri(
                    endpoint,
                    "web");

            Process.Start(
                new ProcessStartInfo(
                    web.AbsoluteUri)
                {
                    UseShellExecute =
                        true
                });

            SetText(
                "PlexStatusText",
                "Opened the configured Plex web interface.");
        }
        catch (Exception exception)
        {
            SetText(
                "PlexStatusText",
                exception.Message);
        }
    }
}