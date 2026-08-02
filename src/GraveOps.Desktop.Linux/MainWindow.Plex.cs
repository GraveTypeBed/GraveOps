using System.Diagnostics;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;

namespace GraveOps.Desktop.Linux;

public partial class MainWindow
{
    private readonly LinuxPlexTelemetryService
        _plexTelemetry =
            new();

    private readonly DispatcherTimer
        _plexTimer =
            new()
            {
                Interval =
                    TimeSpan.FromSeconds(10)
            };

    private readonly Dictionary<
        string,
        LinuxPlexSnapshot>
        _plexCache =
            new(
                StringComparer.OrdinalIgnoreCase);

    private bool _plexBusy;

    private void InitializePlexWorkspace()
    {
        _plexTimer.Tick +=
            async (_, _) =>
            {
                if (Get<Control>(
                        "PlexWorkspacePage")
                    .IsVisible)
                {
                    await RefreshPlexTelemetryAsync(
                        showStatus: false);
                }
            };

        Opened +=
            (_, _) =>
                _plexTimer.Start();

        Closed +=
            (_, _) =>
                _plexTimer.Stop();
    }

    private void ActivatePlexWorkspace()
    {
        SelectMediaIntegrationByName(
            "Plex");

        PopulatePlexWorkspace();

        _ =
            RefreshPlexTelemetryAsync(
                showStatus: false);
    }

    private void PopulatePlexWorkspace()
    {
        Get<TextBlock>("PlexTargetText")
            .Text =
            _controlPlane.ActiveProfile.DisplayName;

        var cacheKey =
            _controlPlane.ActiveProfile.Id;

        if (_plexCache.TryGetValue(
                cacheKey,
                out var snapshot))
        {
            ApplyPlexSnapshot(
                snapshot);
        }
        else
        {
            SetPlexLoadingState();
        }

        UpdatePlexOperationState();
    }

    private void SetPlexLoadingState()
    {
        var state =
            Get<TextBlock>("PlexServiceText");

        state.Text =
            "CHECKING";

        state.Foreground =
            OpsPalette.Foreground(
                OpsSeverity.Info);

        Get<TextBlock>("PlexServiceDetailText")
            .Text =
            "Inspecting systemd and Docker ownership";

        Get<TextBlock>("PlexVersionText")
            .Text =
            "--";

        Get<TextBlock>("PlexEndpointText")
            .Text =
            ResolvePlexUrl() ??
            "--";

        Get<TextBlock>("PlexConnectionText")
            .Text =
            "Waiting for identity probe";

        Get<TextBlock>("PlexDependencyText")
            .Text =
            PlexDependencySummary();

        Get<TextBlock>("PlexActiveSessionsText")
            .Text =
            "--";

        Get<TextBlock>("PlexDirectPlayText")
            .Text =
            "--";

        Get<TextBlock>("PlexTranscodeText")
            .Text =
            "--";

        Get<TextBlock>("PlexLibrariesText")
            .Text =
            "--";

        Get<TextBlock>("PlexPlaybackAnalyticsText")
            .Text =
            "Waiting for live session telemetry...";

        Get<TextBlock>("PlexServerContextText")
            .Text =
            "Waiting for Plex identity and library context...";

        Get<TextBlock>("PlexSessionCountText")
            .Text =
            "--";

        Get<ListBox>("PlexSessionsList")
            .ItemsSource =
            Array.Empty<LinuxPlexSessionRow>();

        Get<ListBox>("PlexSessionsList")
            .IsVisible =
            false;

        Get<Border>("PlexSessionsEmptyState")
            .IsVisible =
            true;

        Get<TextBlock>("PlexSessionsEmptyText")
            .Text =
            "Plex session telemetry is loading.";

        Get<TextBlock>("PlexSecurityText")
            .Text =
            "The Plex token is used only inside the target Linux host and is never returned to GraveOps.";

        Get<TextBlock>("PlexStatusText")
            .Text =
            "Waiting for Plex telemetry.";

        Get<TextBlock>("PlexFreshnessText")
            .Text =
            "CHECKING...";
    }

    private void ApplyPlexSnapshot(
        LinuxPlexSnapshot snapshot)
    {
        var severity =
            PlexSeverity(
                snapshot.State);

        var state =
            Get<TextBlock>("PlexServiceText");

        state.Text =
            snapshot.State.ToUpperInvariant();

        state.Foreground =
            OpsPalette.Foreground(
                severity);

        Get<TextBlock>("PlexServiceDetailText")
            .Text =
            $"{snapshot.Service} · " +
            $"{snapshot.ServiceDetail}";

        Get<TextBlock>("PlexVersionText")
            .Text =
            string.IsNullOrWhiteSpace(
                snapshot.Version)
                ? "--"
                : $"v{snapshot.Version.TrimStart('v', 'V')}";

        Get<TextBlock>("PlexEndpointText")
            .Text =
            ResolvePlexUrl() ??
            snapshot.Endpoint;

        Get<TextBlock>("PlexConnectionText")
            .Text =
            snapshot.Connection;

        Get<TextBlock>("PlexDependencyText")
            .Text =
            PlexDependencySummary(
                snapshot.Dependency);

        Get<TextBlock>("PlexActiveSessionsText")
            .Text =
            snapshot.ActiveSessions.ToString();

        Get<TextBlock>("PlexDirectPlayText")
            .Text =
            snapshot.DirectPlayCount.ToString();

        Get<TextBlock>("PlexTranscodeText")
            .Text =
            snapshot.TranscodeCount.ToString();

        Get<TextBlock>("PlexLibrariesText")
            .Text =
            snapshot.LibraryCount.ToString();

        Get<TextBlock>("PlexPlaybackAnalyticsText")
            .Text =
            $"Active sessions · " +
            $"{snapshot.ActiveSessions}\n" +
            $"Direct play · " +
            $"{snapshot.DirectPlayCount}   " +
            $"Direct stream · " +
            $"{snapshot.DirectStreamCount}\n" +
            $"Transcoding · " +
            $"{snapshot.TranscodeCount}   " +
            $"Session bandwidth · " +
            $"{snapshot.TotalBandwidth}";

        Get<TextBlock>("PlexServerContextText")
            .Text =
            $"Service owner · " +
            $"{snapshot.Service}\n" +
            $"Connection · " +
            $"{snapshot.Connection}\n" +
            $"Libraries · " +
            $"{snapshot.LibraryCount}\n" +
            $"{PlexDependencySummary(snapshot.Dependency)}";

        var sessions =
            snapshot.Sessions ??
            new List<LinuxPlexSessionRow>();

        var list =
            Get<ListBox>("PlexSessionsList");

        list.ItemsSource =
            sessions;

        list.IsVisible =
            sessions.Count > 0;

        Get<Border>("PlexSessionsEmptyState")
            .IsVisible =
            sessions.Count == 0;

        Get<TextBlock>("PlexSessionsEmptyText")
            .Text =
            snapshot.Security.Contains(
                "identity-only",
                StringComparison.OrdinalIgnoreCase)
                ? "No active sessions are visible because a protected Plex token was not found."
                : "No viewers are currently streaming from this Plex server.";

        Get<TextBlock>("PlexSessionCountText")
            .Text =
            $"{sessions.Count} " +
            $"{(sessions.Count == 1 ? "session" : "sessions")}";

        Get<TextBlock>("PlexSecurityText")
            .Text =
            snapshot.Security;

        Get<TextBlock>("PlexStatusText")
            .Text =
            string.IsNullOrWhiteSpace(
                snapshot.Detail)
                ? "Plex telemetry refreshed."
                : snapshot.Detail;

        Get<TextBlock>("PlexFreshnessText")
            .Text =
            $"LIVE · updated " +
            $"{snapshot.SampledAt.ToLocalTime():h:mm:ss tt}";

        Get<TextBlock>("PlexOperationsStatusText")
            .Text =
            severity >= OpsSeverity.Error
                ? "Plex is not currently reachable. Review service ownership, logs and dependencies."
                : "Plex is reachable. Session analytics and guarded operations are ready.";

        UpdatePlexOperationState();
    }

    private async Task RefreshPlexTelemetryAsync(
        bool showStatus)
    {
        if (_plexBusy)
            return;

        _plexBusy =
            true;

        var button =
            Get<Button>("PlexRefreshButton");

        button.IsEnabled =
            false;

        Get<TextBlock>("PlexFreshnessText")
            .Text =
            "CHECKING...";

        if (showStatus)
        {
            Get<TextBlock>("PlexStatusText")
                .Text =
                "Refreshing Plex telemetry...";
        }

        try
        {
            var targetId =
                _controlPlane.ActiveProfile.Id;

            var snapshot =
                await _plexTelemetry.CaptureAsync(
                    _controlPlane);

            _plexCache[targetId] =
                snapshot;

            if (_controlPlane.ActiveProfile.Id.Equals(
                    targetId,
                    StringComparison.OrdinalIgnoreCase))
            {
                ApplyPlexSnapshot(
                    snapshot);
            }
        }
        catch (Exception exception)
        {
            var state =
                Get<TextBlock>("PlexServiceText");

            state.Text =
                "UNAVAILABLE";

            state.Foreground =
                OpsPalette.Foreground(
                    OpsSeverity.Error);

            Get<TextBlock>("PlexFreshnessText")
                .Text =
                "PROBE FAILED";

            Get<TextBlock>("PlexStatusText")
                .Text =
                exception.Message;

            Get<TextBlock>("PlexOperationsStatusText")
                .Text =
                "Plex telemetry failed. Logs and dependency pages remain available.";

            UpdatePlexOperationState();
        }
        finally
        {
            _plexBusy =
                false;

            button.IsEnabled =
                true;
        }
    }

    private void PlexRefreshButton_OnClick(
        object? sender,
        RoutedEventArgs e) =>
        _ =
            RefreshPlexTelemetryAsync(
                showStatus: true);

    private async void PlexOpenButton_OnClick(
        object? sender,
        RoutedEventArgs e)
    {
        var integration =
            PlexIntegration();

        if (integration is not null)
        {
            await OpenMediaIntegrationAsync(
                integration,
                "PlexOperationsStatusText");
        }
    }

    private async void PlexRestartButton_OnClick(
        object? sender,
        RoutedEventArgs e)
    {
        if (!CanRunLocalMutations())
        {
            Get<TextBlock>("PlexOperationsStatusText")
                .Text =
                "Remote Plex restart is disabled. Use the pinned SSH terminal or the Services page.";
            return;
        }

        if (BlockedBySafeMode(
                "restart"))
        {
            Get<TextBlock>("PlexOperationsStatusText")
                .Text =
                "Disable Safe Mode before restarting Plex.";
            return;
        }

        var service =
            _snapshot is null
                ? null
                : LinuxOpsAnalyzer
                    .UniqueServices(_snapshot)
                    .FirstOrDefault(item =>
                        item.Unit.Contains(
                            "plex",
                            StringComparison.OrdinalIgnoreCase));

        var container =
            _snapshot?.Containers
                .FirstOrDefault(item =>
                    item.Name.Contains(
                        "plex",
                        StringComparison.OrdinalIgnoreCase));

        if (service is null &&
            container is null)
        {
            Get<TextBlock>("PlexOperationsStatusText")
                .Text =
                "No restartable Plex systemd service or Docker container was detected.";
            return;
        }

        var owner =
            service is not null
                ? service.Unit
                : container!.Name;

        if (!await ConfirmActionAsync(
                $"Restart {owner}?",
                "This interrupts active Plex sessions. Continue only after reviewing current viewers and dependencies."))
        {
            return;
        }

        Get<TextBlock>("PlexOperationsStatusText")
            .Text =
            $"Restarting {owner}...";

        var result =
            service is not null
                ? await _actions.ServiceAsync(
                    service.Unit,
                    "restart")
                : await _actions.ContainerAsync(
                    container!.Name,
                    "restart");

        _history.RecordAction(
            owner,
            "restart",
            result);

        _controlPlane.State.RecordActivity(
            "Action",
            _controlPlane.ActiveProfile.DisplayName,
            $"restart {owner}",
            result.Summary,
            "PlexNav");

        Get<TextBlock>("PlexOperationsStatusText")
            .Text =
            result.Summary;

        _plexCache.Remove(
            _controlPlane.ActiveProfile.Id);

        await RefreshAsync();

        await RefreshPlexTelemetryAsync(
            showStatus: false);
    }

    private void PlexLogsButton_OnClick(
        object? sender,
        RoutedEventArgs e) =>
        Navigate("LogsNav");

    private void PlexTerminalButton_OnClick(
        object? sender,
        RoutedEventArgs e) =>
        Navigate("ToolsNav");

    private void PlexIntelligenceButton_OnClick(
        object? sender,
        RoutedEventArgs e) =>
        Navigate("IntelligenceNav");

    private OpsIntegration?
        PlexIntegration() =>
        _integrations.FirstOrDefault(item =>
            item.Name.Equals(
                "Plex",
                StringComparison.OrdinalIgnoreCase));

    private string? ResolvePlexUrl()
    {
        var integration =
            PlexIntegration();

        return integration is null
            ? null
            : ResolveIntegrationUrl(
                integration);
    }

    private string PlexDependencySummary(
        string? captured = null)
    {
        var dumb =
            _integrations.Any(item =>
                item.Name.Equals(
                    "DUMB",
                    StringComparison.OrdinalIgnoreCase));

        var operationalStorage =
            _snapshot is null
                ? 0
                : LinuxOpsAnalyzer
                    .OperationalStorage(_snapshot)
                    .Count;

        if (dumb)
        {
            return
                $"DUMB detected · " +
                $"{operationalStorage} storage " +
                $"{(operationalStorage == 1 ? "root" : "roots")}";
        }

        if (!string.IsNullOrWhiteSpace(
                captured))
        {
            return captured;
        }

        return
            $"{operationalStorage} storage " +
            $"{(operationalStorage == 1 ? "root" : "roots")} · " +
            "native Plex ownership";
    }

    private void UpdatePlexOperationState()
    {
        var integration =
            PlexIntegration();

        Get<Button>("PlexOpenButton")
            .IsEnabled =
            integration is not null &&
            ResolveIntegrationUrl(
                integration) is not null;

        var restartOwner =
            _snapshot is not null &&
            (LinuxOpsAnalyzer
                 .UniqueServices(_snapshot)
                 .Any(item =>
                     item.Unit.Contains(
                         "plex",
                         StringComparison.OrdinalIgnoreCase)) ||
             _snapshot.Containers
                 .Any(item =>
                     item.Name.Contains(
                         "plex",
                         StringComparison.OrdinalIgnoreCase)));

        var safeMode =
            Get<CheckBox>("SafeModeCheckBox")
                .IsChecked ==
            true;

        Get<Button>("PlexRestartButton")
            .IsEnabled =
            CanRunLocalMutations() &&
            restartOwner &&
            !safeMode;
    }

    private static OpsSeverity PlexSeverity(
        string state)
    {
        if (state.Contains(
                "online",
                StringComparison.OrdinalIgnoreCase) ||
            state.Contains(
                "healthy",
                StringComparison.OrdinalIgnoreCase))
        {
            return OpsSeverity.Healthy;
        }

        if (state.Contains(
                "degraded",
                StringComparison.OrdinalIgnoreCase) ||
            state.Contains(
                "busy",
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
                "inactive",
                StringComparison.OrdinalIgnoreCase))
        {
            return OpsSeverity.Error;
        }

        return OpsSeverity.Info;
    }
}
