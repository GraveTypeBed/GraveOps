using System.Diagnostics;
using Avalonia.Controls;
using Avalonia.Interactivity;

namespace GraveOps.Desktop.Linux;

public partial class MainWindow
{
    private readonly LinuxPiHoleTelemetryAdapter
        _piHoleTelemetry =
            new();

    private PiHoleTelemetrySnapshot?
        _piHoleSnapshot;
    private bool _piHoleCaptureBusy;

    private void ActivatePiHoleWorkspace()
    {
        PopulatePiHoleWorkspace();
        _ = RefreshPiHoleWorkspaceAsync();
    }

    private OpsIntegration? PiHoleIntegration() =>
        _integrations.FirstOrDefault(item =>
            item.Name.Equals(
                "Pi-hole",
                StringComparison.OrdinalIgnoreCase));

    private string? PiHoleVerifiedEndpoint() =>
        PiHoleIntegration() is { } integration
            ? ResolveIntegrationUrl(
                integration)
            : null;

    private async void PiHoleRefreshButton_OnClick(
        object? sender,
        RoutedEventArgs e) =>
        await RefreshPiHoleWorkspaceAsync();

    private async Task RefreshPiHoleWorkspaceAsync()
    {
        if (_piHoleCaptureBusy)
            return;

        _piHoleCaptureBusy = true;
        var button =
            Get<Button>("PiHoleRefreshButton");
        button.IsEnabled = false;

        try
        {
            _piHoleSnapshot =
                await _piHoleTelemetry.CaptureAsync(
                    new LinuxPiHoleTelemetryContext(
                        _controlPlane,
                        _controlPlane.ActiveProfile,
                        PiHoleVerifiedEndpoint()));
            PopulatePiHoleWorkspace();

            _controlPlane.State.RecordActivity(
                "System",
                _controlPlane.ActiveProfile.DisplayName,
                "Pi-hole telemetry captured",
                $"{_piHoleSnapshot.State} · " +
                $"{_piHoleSnapshot.QueriesText} queries · " +
                $"{_piHoleSnapshot.PercentBlockedText} blocked",
                "PiHoleNav",
                unread: false);
            PopulateControlPlaneFoundation();
        }
        catch (Exception exception)
        {
            Get<TextBlock>("PiHoleStatusText").Text =
                $"Pi-hole capture failed: {exception.Message}";
            Get<TextBlock>("PiHoleStateText").Text =
                "UNAVAILABLE";
            Get<TextBlock>("PiHoleFreshnessText").Text =
                "Capture failed";
        }
        finally
        {
            button.IsEnabled = true;
            _piHoleCaptureBusy = false;
        }
    }

    private void PopulatePiHoleWorkspace()
    {
        var integration =
            PiHoleIntegration();
        var available =
            integration is not null;

        Get<TextBlock>("PiHoleTargetText").Text =
            _controlPlane.ActiveProfile.DisplayName;
        Get<Button>("PiHoleOpenButton").IsEnabled =
            available ||
            _piHoleSnapshot is not null;

        if (_piHoleSnapshot is not { } snapshot)
        {
            Get<TextBlock>("PiHoleStateText").Text =
                available
                    ? "READY TO CAPTURE"
                    : "NOT DETECTED";
            Get<TextBlock>("PiHoleDnsText").Text = "--";
            Get<TextBlock>("PiHoleBlockingText").Text = "--";
            Get<TextBlock>("PiHoleQueriesText").Text = "--";
            Get<TextBlock>("PiHoleBlockedText").Text = "--";
            Get<TextBlock>("PiHoleVersionsText").Text =
                "Core -- · Web -- · FTL --";
            Get<TextBlock>("PiHoleHostContextText").Text =
                "Capture Pi-hole to inspect host, uptime, load and temperature.";
            Get<TextBlock>("PiHoleClientContextText").Text =
                "Client and query-rate statistics have not been captured.";
            Get<TextBlock>("PiHoleGravityContextText").Text =
                "Gravity inventory has not been captured.";
            Get<TextBox>("PiHoleEvidenceText").Text =
                available
                    ? "Pi-hole was detected. Run Refresh to capture read-only status and statistics."
                    : "No verified Pi-hole source is associated with the active target.";
            Get<TextBlock>("PiHoleFreshnessText").Text =
                "Not captured";
            UpdatePiHoleActionAvailability();
            return;
        }

        Get<TextBlock>("PiHoleStateText").Text =
            snapshot.State;
        Get<TextBlock>("PiHoleDnsText").Text =
            snapshot.DnsOnline
                ? "ONLINE"
                : "OFFLINE";
        Get<TextBlock>("PiHoleBlockingText").Text =
            snapshot.BlockingEnabled
                ? "ENABLED"
                : "DISABLED";
        Get<TextBlock>("PiHoleQueriesText").Text =
            snapshot.QueriesText;
        Get<TextBlock>("PiHoleBlockedText").Text =
            snapshot.PercentBlockedText;
        Get<TextBlock>("PiHoleVersionsText").Text =
            $"Core {snapshot.CoreVersion} · " +
            $"Web {snapshot.WebVersion} · " +
            $"FTL {snapshot.FtlVersion}";
        Get<TextBlock>("PiHoleHostContextText").Text =
            $"{snapshot.Host} · uptime {snapshot.Uptime} · " +
            $"load {snapshot.Load} · {snapshot.Temperature}";
        Get<TextBlock>("PiHoleClientContextText").Text =
            $"{snapshot.ClientText} · {snapshot.QueryRateText}";
        Get<TextBlock>("PiHoleGravityContextText").Text =
            $"{snapshot.GravityText} blocked domains · " +
            snapshot.GravityAgeText;
        Get<TextBox>("PiHoleEvidenceText").Text =
            snapshot.RawEvidence;
        Get<TextBlock>("PiHoleFreshnessText").Text =
            $"Captured {snapshot.CapturedAt.ToLocalTime():g}";
        Get<TextBlock>("PiHoleStatusText").Text =
            snapshot.DnsOnline
                ? snapshot.BlockingEnabled
                    ? "Pi-hole DNS and blocking are operating normally."
                    : "DNS is online, but blocking is disabled."
                : "Pi-hole DNS requires attention.";

        UpdatePiHoleActionAvailability();
    }

    private void UpdatePiHoleActionAvailability()
    {
        var safeMode =
            Get<CheckBox>("SettingsSafeModeCheckBox")
                .IsChecked == true;
        var detected =
            PiHoleIntegration() is not null ||
            _piHoleSnapshot is not null;

        foreach (var name in new[]
                 {
                     "PiHoleEnableButton",
                     "PiHoleDisableButton",
                     "PiHoleReloadButton"
                 })
        {
            var button = Get<Button>(name);
            button.IsEnabled =
                detected &&
                !safeMode;
            ToolTip.SetTip(
                button,
                !detected
                    ? "No Pi-hole source was detected for the active target."
                    : safeMode
                        ? "Unavailable because Safe Mode is enabled."
                        : null);
        }
    }

    private async void PiHoleEnableButton_OnClick(
        object? sender,
        RoutedEventArgs e) =>
        await RunPiHoleControlAsync(
            PiHoleControlAction.EnableBlocking,
            "Enable Pi-hole blocking?",
            "Pi-hole blocking enabled");

    private async void PiHoleDisableButton_OnClick(
        object? sender,
        RoutedEventArgs e) =>
        await RunPiHoleControlAsync(
            PiHoleControlAction.DisableBlockingFiveMinutes,
            "Disable Pi-hole blocking for five minutes?",
            "Pi-hole blocking disabled for five minutes");

    private async void PiHoleReloadButton_OnClick(
        object? sender,
        RoutedEventArgs e) =>
        await RunPiHoleControlAsync(
            PiHoleControlAction.ReloadDns,
            "Reload Pi-hole DNS?",
            "Pi-hole DNS reloaded");

    private async Task RunPiHoleControlAsync(
        PiHoleControlAction action,
        string confirmationTitle,
        string activityTitle)
    {
        if (Get<CheckBox>("SettingsSafeModeCheckBox")
                .IsChecked == true)
        {
            Get<TextBlock>("PiHoleStatusText").Text =
                "Safe Mode blocks Pi-hole control changes.";
            return;
        }

        if (!await ConfirmActionAsync(
                confirmationTitle,
                "This runs a non-interactive Pi-hole command on the active Linux target."))
        {
            return;
        }

        try
        {
            var result =
                await PiHoleWorkspaceService.RunActionAsync(
                    _controlPlane,
                    _controlPlane.ActiveProfile,
                    action);

            Get<TextBlock>("PiHoleStatusText").Text =
                result.Success
                    ? result.Summary
                    : $"{result.Summary} {result.Detail}";
            Get<TextBox>("PiHoleEvidenceText").Text =
                result.Detail;

            _controlPlane.State.RecordActivity(
                result.Success
                    ? "Action"
                    : "Failure",
                _controlPlane.ActiveProfile.DisplayName,
                activityTitle,
                result.Detail,
                "PiHoleNav",
                unread: !result.Success);
            PopulateControlPlaneFoundation();

            if (result.Success)
                await RefreshPiHoleWorkspaceAsync();
        }
        catch (Exception exception)
        {
            Get<TextBlock>("PiHoleStatusText").Text =
                $"Pi-hole action failed: {exception.Message}";
            _controlPlane.State.RecordActivity(
                "Failure",
                _controlPlane.ActiveProfile.DisplayName,
                activityTitle,
                exception.Message,
                "PiHoleNav",
                unread: true);
            PopulateControlPlaneFoundation();
        }
    }

    private void PiHoleOpenButton_OnClick(
        object? sender,
        RoutedEventArgs e)
    {
        var url =
            _piHoleSnapshot?.WebUrl ??
            PiHoleWorkspaceService.NormalizeWebUrl(
                _controlPlane.ActiveProfile,
                PiHoleVerifiedEndpoint());

        try
        {
            var start =
                new ProcessStartInfo
                {
                    FileName = "xdg-open",
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
            start.ArgumentList.Add(url);
            Process.Start(start);
            Get<TextBlock>("PiHoleStatusText").Text =
                $"Opened {url}";
        }
        catch (Exception exception)
        {
            Get<TextBlock>("PiHoleStatusText").Text =
                $"Could not open Pi-hole: {exception.Message}";
        }
    }

    private void PiHoleLogsButton_OnClick(
        object? sender,
        RoutedEventArgs e) =>
        Navigate("LogsNav");
}
