using System;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;

namespace GraveOps.Desktop.Linux;

public partial class MainWindow
{
    private readonly DockerWorkspaceService _dockerWorkspaceService = new();
    private DockerFleetSnapshot? _dockerFleetSnapshot;
    private DockerContainerDetailSnapshot? _dockerDetailSnapshot;
    private bool _dockerFleetBusy;
    private bool _dockerActionBusy;
    private bool _dockerShowRawLogs;
    private int _dockerDetailRequest;

    private void InitializeDockerWorkspace()
    {
        Get<TextBlock>("DockerDaemonText").Text = "Capture pending";
        Get<TextBlock>("DockerTotalMetricText").Text = "0";
        Get<TextBlock>("DockerRunningMetricText").Text = "0";
        Get<TextBlock>("DockerAttentionMetricText").Text = "0";
        Get<TextBlock>("DockerProjectMetricText").Text = "0";
        Get<TextBox>("DockerLogsText").Text =
            "Select a container to capture the last 200 log lines on demand.";
        _dockerShowRawLogs = false;
        UpdateDockerLogModeButtons();
        ClearDockerWorkspaceDetail();
        UpdateDockerWorkspaceActionButtons();
    }

    private void ActivateDockerWorkspace()
    {
        PopulateDockerWorkspaceFallback();

        if (!_controlPlane.ActiveProfile.IsLocal)
        {
            ApplyRemoteDockerBoundary();
            return;
        }

        _ = RefreshDockerWorkspaceAsync();
    }

    private void ApplyRemoteDockerBoundary()
    {
        _dockerFleetSnapshot = null;
        Get<TextBlock>("DockerDaemonText").Text = "Local provider required";
        Get<TextBlock>("DockerSummaryText").Text =
            "Remote Docker drilldown is not enabled in this provider batch.";
        Get<ListBox>("DockerList").ItemsSource =
            Array.Empty<DockerFleetRow>();
        Get<Border>("DockerEmptyState").IsVisible = true;
        ClearDockerWorkspaceDetail();
        UpdateDockerWorkspaceActionButtons();
    }

    private void PopulateDockerWorkspaceFallback()
    {
        if (_dockerFleetSnapshot?.Available == true)
        {
            ApplyDockerWorkspaceFilter();
            return;
        }

        if (_snapshot is not null)
        {
            _dockerFleetSnapshot =
                DockerWorkspaceService.FromHostSnapshot(
                    _snapshot.Containers);
        }

        ApplyDockerWorkspaceFilter();
    }

    private async Task RefreshDockerWorkspaceAsync(
        string? preferredContainer = null)
    {
        if (_dockerFleetBusy)
            return;

        if (!_controlPlane.ActiveProfile.IsLocal)
        {
            ApplyRemoteDockerBoundary();
            return;
        }

        _dockerFleetBusy = true;
        var button = Get<Button>("DockerRefreshButton");
        button.IsEnabled = false;
        button.Content = "Refreshing...";

        var currentName = preferredContainer ??
            SelectedDockerRow()?.Name;

        try
        {
            _dockerFleetSnapshot =
                await _dockerWorkspaceService.CaptureFleetAsync();
            ApplyDockerWorkspaceFilter(currentName);

            Get<TextBlock>("DockerWorkspaceStatusText").Text =
                _dockerFleetSnapshot.Evidence;
        }
        catch (Exception exception)
        {
            Get<TextBlock>("DockerWorkspaceStatusText").Text =
                $"Docker workspace refresh failed: {exception.Message}";
        }
        finally
        {
            button.IsEnabled = true;
            button.Content = "Refresh";
            _dockerFleetBusy = false;
            UpdateDockerWorkspaceActionButtons();
        }
    }

    private void ApplyDockerWorkspaceFilter(
        string? preferredContainer = null)
    {
        var list = Get<ListBox>("DockerList");
        var selectedName = preferredContainer ??
            (list.SelectedItem as DockerFleetRow)?.Name;
        var filter =
            Get<TextBox>("DockerFilterText").Text?.Trim();
        var showExited =
            Get<CheckBox>("ShowInformationalContainersCheckBox")
                .IsChecked == true;

        var snapshot = _dockerFleetSnapshot;
        var all = snapshot?.Containers ??
            Array.Empty<DockerFleetRow>();

        var rows = all
            .Where(item =>
                showExited ||
                item.IsRunning ||
                item.HasAttention)
            .Where(item =>
                string.IsNullOrWhiteSpace(filter) ||
                new[]
                {
                    item.Group,
                    item.Name,
                    item.Image,
                    item.StateLabel,
                    item.HealthLabel,
                    item.RestartPolicy,
                    item.Ports,
                    item.ComposeProject,
                    item.ComposeService
                }.Any(value =>
                    value.Contains(
                        filter,
                        StringComparison.OrdinalIgnoreCase)))
            .ToArray();

        list.ItemsSource = rows;
        list.SelectedItem = rows.FirstOrDefault(item =>
            item.Name.Equals(
                selectedName,
                StringComparison.OrdinalIgnoreCase));

        if (list.SelectedItem is null && rows.Length > 0)
            list.SelectedIndex = 0;

        Get<Border>("DockerEmptyState").IsVisible = rows.Length == 0;
        list.IsVisible = rows.Length > 0;

        Get<TextBlock>("DockerTotalMetricText").Text =
            all.Count.ToString();
        Get<TextBlock>("DockerRunningMetricText").Text =
            (snapshot?.Running ?? 0).ToString();
        Get<TextBlock>("DockerAttentionMetricText").Text =
            (snapshot?.Attention ?? 0).ToString();
        Get<TextBlock>("DockerProjectMetricText").Text =
            (snapshot?.ComposeProjects ?? 0).ToString();
        Get<TextBlock>("DockerDaemonText").Text =
            snapshot is null
                ? "Capture pending"
                : snapshot.Available
                    ? $"Docker {snapshot.DaemonVersion} · captured {snapshot.CapturedAt:t}"
                    : "Docker unavailable";

        var hidden = all.Count - rows.Length;
        Get<TextBlock>("DockerSummaryText").Text =
            $"{rows.Length} shown · {snapshot?.Running ?? 0} running · " +
            $"{snapshot?.Attention ?? 0} attention · {hidden} hidden";

        if (rows.Length == 0)
            ClearDockerWorkspaceDetail();

        UpdateDockerWorkspaceActionButtons();
    }

    private DockerFleetRow? SelectedDockerRow() =>
        Get<ListBox>("DockerList").SelectedItem
            as DockerFleetRow;

    private async void DockerList_OnSelectionChanged(
        object? sender,
        SelectionChangedEventArgs e)
    {
        PopulateDockerWorkspaceRowSummary();
        UpdateDockerWorkspaceActionButtons();
        await LoadSelectedDockerDetailAsync();
    }

    private void PopulateDockerWorkspaceRowSummary()
    {
        var row = SelectedDockerRow();
        if (row is null)
        {
            ClearDockerWorkspaceDetail();
            return;
        }

        Get<TextBlock>("DockerSelectedNameText").Text = row.Name;
        Get<TextBlock>("DockerSelectedStateText").Text =
            $"{row.StateLabel} · {row.HealthLabel}";
        Get<TextBlock>("DockerSelectedImageText").Text = row.Image;
        Get<TextBlock>("DockerSelectedIdText").Text = row.ShortId;
        Get<TextBlock>("DockerSelectedComposeText").Text =
            row.ComposeProject == "--"
                ? "Standalone container"
                : $"{row.ComposeProject} / {row.ComposeService}";
        Get<TextBlock>("DockerSelectedRestartText").Text =
            $"{row.RestartPolicy} · {row.RestartCount} restart(s)";
        Get<TextBlock>("DockerSelectedLifecycleText").Text =
            row.IsRunning
                ? $"Started {row.StartedAt} · Still running"
                : $"Started {row.StartedAt} · Finished {row.FinishedAt} · Exit {row.ExitCode}";
        Get<TextBlock>("DockerSelectedResourcesText").Text =
            row.Resources;
        Get<TextBlock>("DockerSelectedPortsText").Text =
            string.IsNullOrWhiteSpace(row.Ports)
                ? "--"
                : row.Ports;
    }

    private async Task LoadSelectedDockerDetailAsync()
    {
        var row = SelectedDockerRow();
        if (row is null || !_controlPlane.ActiveProfile.IsLocal)
            return;

        var request = ++_dockerDetailRequest;
        Get<TextBlock>("DockerDetailStatusText").Text =
            $"Inspecting {row.Name}...";
        Get<Button>("DockerRefreshDetailButton").IsEnabled = false;
        Get<Button>("DockerRefreshLogsButton").IsEnabled = false;

        try
        {
            var detail =
                await _dockerWorkspaceService.CaptureDetailAsync(row);

            if (request != _dockerDetailRequest ||
                SelectedDockerRow()?.Name.Equals(
                    row.Name,
                    StringComparison.OrdinalIgnoreCase) != true)
            {
                return;
            }

            _dockerDetailSnapshot = detail;
            PopulateDockerWorkspaceDetail(detail);
        }
        catch (Exception exception)
        {
            if (request == _dockerDetailRequest)
            {
                Get<TextBlock>("DockerDetailStatusText").Text =
                    $"Detail capture failed: {exception.Message}";
                Get<TextBox>("DockerLogsText").Text =
                    "Container logs were not captured.";
            }
        }
        finally
        {
            if (request == _dockerDetailRequest)
            {
                Get<Button>("DockerRefreshDetailButton").IsEnabled = true;
                Get<Button>("DockerRefreshLogsButton").IsEnabled = true;
            }
        }
    }

    private void PopulateDockerWorkspaceDetail(
        DockerContainerDetailSnapshot detail)
    {
        PopulateDockerWorkspaceRowSummary();
        Get<TextBlock>("DockerSelectedComposeText").Text =
            detail.ComposeOwnership;
        Get<TextBlock>("DockerSelectedLifecycleText").Text =
            detail.Lifecycle;
        Get<TextBlock>("DockerSelectedPortsText").Text = detail.Ports;
        Get<TextBlock>("DockerSelectedNetworksText").Text = detail.Networks;
        Get<TextBox>("DockerSelectedMountsText").Text = detail.Mounts;
        Get<TextBox>("DockerSelectedEnvironmentText").Text =
            detail.EnvironmentNames;
        Get<TextBlock>("DockerDetailStatusText").Text =
            detail.Evidence;
        UpdateDockerLogModeButtons();
        ApplyDockerLogFilter();
    }

    private void ClearDockerWorkspaceDetail()
    {
        _dockerDetailSnapshot = null;
        Get<TextBlock>("DockerSelectedNameText").Text =
            "No container selected";
        Get<TextBlock>("DockerSelectedStateText").Text = "--";
        Get<TextBlock>("DockerSelectedImageText").Text = "--";
        Get<TextBlock>("DockerSelectedIdText").Text = "--";
        Get<TextBlock>("DockerSelectedComposeText").Text = "--";
        Get<TextBlock>("DockerSelectedRestartText").Text = "--";
        Get<TextBlock>("DockerSelectedLifecycleText").Text = "--";
        Get<TextBlock>("DockerSelectedResourcesText").Text = "--";
        Get<TextBlock>("DockerSelectedPortsText").Text = "--";
        Get<TextBlock>("DockerSelectedNetworksText").Text = "--";
        Get<TextBox>("DockerSelectedMountsText").Text =
            "Select a container to inspect mounts.";
        Get<TextBox>("DockerSelectedEnvironmentText").Text =
            "Environment-variable names only. Values are never displayed.";
        Get<TextBlock>("DockerDetailStatusText").Text =
            "Select a fleet row to capture inspect metadata.";
        _dockerShowRawLogs = false;
        UpdateDockerLogModeButtons();
        Get<TextBlock>("DockerLogsStatusText").Text = "Not captured";
        Get<TextBox>("DockerLogsText").Text =
            "Select a container to capture the last 200 log lines on demand.";
    }

    private void UpdateDockerWorkspaceActionButtons()
    {
        var row = SelectedDockerRow();
        var local = CanRunLocalMutations();
        var safe = Get<CheckBox>("SafeModeCheckBox").IsChecked == true;
        var enabled =
            row is not null &&
            local &&
            !safe &&
            !_dockerFleetBusy &&
            !_dockerActionBusy;

        Get<Button>("DockerStartButton").IsEnabled =
            enabled && row?.IsRunning == false;
        Get<Button>("DockerStopButton").IsEnabled =
            enabled && row?.IsRunning == true;
        Get<Button>("DockerRestartButton").IsEnabled =
            enabled && row?.IsRunning == true;
        Get<Button>("DockerRestartDumbButton").IsEnabled =
            enabled &&
            row?.ComposeProject.Equals(
                "dumb",
                StringComparison.OrdinalIgnoreCase) == true &&
            !string.IsNullOrWhiteSpace(row.ComposeWorkingDirectory) &&
            row.ComposeWorkingDirectory != "--";
    }

    private async void DockerRefreshButton_OnClick(
        object? sender,
        RoutedEventArgs e) =>
        await RefreshDockerWorkspaceAsync();

    private async void DockerRefreshDetailButton_OnClick(
        object? sender,
        RoutedEventArgs e) =>
        await LoadSelectedDockerDetailAsync();

    private async void DockerRefreshLogsButton_OnClick(
        object? sender,
        RoutedEventArgs e) =>
        await LoadSelectedDockerDetailAsync();

    private void DockerLogFilterText_OnTextChanged(
        object? sender,
        TextChangedEventArgs e) =>
        ApplyDockerLogFilter();

    private void DockerCleanedLogsButton_OnClick(
        object? sender,
        RoutedEventArgs e)
    {
        _dockerShowRawLogs = false;
        UpdateDockerLogModeButtons();
        ApplyDockerLogFilter();
    }

    private void DockerRawLogsButton_OnClick(
        object? sender,
        RoutedEventArgs e)
    {
        _dockerShowRawLogs = true;
        UpdateDockerLogModeButtons();
        ApplyDockerLogFilter();
    }

    private void UpdateDockerLogModeButtons()
    {
        var cleaned = Get<Button>("DockerCleanedLogsButton");
        var raw = Get<Button>("DockerRawLogsButton");

        cleaned.IsEnabled = _dockerShowRawLogs;
        raw.IsEnabled = !_dockerShowRawLogs;

        var output = Get<TextBox>("DockerLogsText");
        output.TextWrapping = _dockerShowRawLogs
            ? TextWrapping.NoWrap
            : TextWrapping.Wrap;
    }

    private void ApplyDockerLogFilter()
    {
        var output = Get<TextBox>("DockerLogsText");
        var detail = _dockerDetailSnapshot;
        if (detail is null)
            return;

        var source = _dockerShowRawLogs
            ? detail.RawLogs
            : detail.CleanedLogs;
        var filter =
            Get<TextBox>("DockerLogFilterText").Text?.Trim();

        if (string.IsNullOrWhiteSpace(filter))
        {
            output.Text = source;
        }
        else if (_dockerShowRawLogs)
        {
            var rows = source
                .Split('\n')
                .Where(line => line.Contains(
                    filter,
                    StringComparison.OrdinalIgnoreCase))
                .ToArray();

            output.Text = rows.Length == 0
                ? $"No raw log line contains '{filter}'."
                : string.Join(Environment.NewLine, rows);
        }
        else
        {
            var blocks = source
                .Replace(
                    "\r\n",
                    "\n",
                    StringComparison.Ordinal)
                .Split(
                    "\n\n",
                    StringSplitOptions.RemoveEmptyEntries)
                .Where(block => block.Contains(
                    filter,
                    StringComparison.OrdinalIgnoreCase))
                .ToArray();

            output.Text = blocks.Length == 0
                ? $"No cleaned log incident contains '{filter}'."
                : string.Join(
                    Environment.NewLine +
                    Environment.NewLine,
                    blocks);
        }

        var mode = _dockerShowRawLogs
            ? "Raw"
            : "Cleaned";
        Get<TextBlock>("DockerLogsStatusText").Text =
            $"Captured {detail.CapturedAt:t} · {mode} · " +
            $"{detail.CleanedLogEntryCount} cleaned incident(s) from " +
            $"{detail.RawLogLineCount} raw line(s) · " +
            $"{detail.CollapsedLogLineCount} line(s) collapsed";
    }

    private async void DockerCopyCleanedLogsButton_OnClick(
        object? sender,
        RoutedEventArgs e)
    {
        var detail = _dockerDetailSnapshot;
        if (detail is null)
            return;

        await CopyDockerLogTextAsync(
            detail.CleanedLogs,
            "Cleaned Docker log summary copied.");
    }

    private async void DockerCopyRawLogsButton_OnClick(
        object? sender,
        RoutedEventArgs e)
    {
        var detail = _dockerDetailSnapshot;
        if (detail is null)
            return;

        await CopyDockerLogTextAsync(
            detail.RawLogs,
            "Redacted raw Docker log output copied.");
    }

    private async Task CopyDockerLogTextAsync(
        string text,
        string successMessage)
    {
        var clipboard = TopLevel.GetTopLevel(this)?.Clipboard;
        if (clipboard is null)
        {
            Get<TextBlock>("DockerLogsStatusText").Text =
                "Clipboard access is unavailable.";
            return;
        }

        await Avalonia.Input.Platform.ClipboardExtensions.SetTextAsync(
            clipboard,
            text);
        Get<TextBlock>("DockerLogsStatusText").Text =
            successMessage;
    }

    private async void DockerStartButton_OnClick(
        object? sender,
        RoutedEventArgs e) =>
        await RunDockerContainerActionAsync("start");

    private async void DockerStopButton_OnClick(
        object? sender,
        RoutedEventArgs e) =>
        await RunDockerContainerActionAsync("stop");

    private async void DockerRestartButton_OnClick(
        object? sender,
        RoutedEventArgs e) =>
        await RunDockerContainerActionAsync("restart");

    private async Task RunDockerContainerActionAsync(string action)
    {
        var row = SelectedDockerRow();
        if (row is null)
            return;

        if (!CanRunLocalMutations())
        {
            Get<TextBlock>("DockerActionStatusText").Text =
                "Remote Docker mutations are disabled.";
            return;
        }

        if (Get<CheckBox>("SafeModeCheckBox").IsChecked == true)
        {
            Get<TextBlock>("DockerActionStatusText").Text =
                "Disable Safe Mode before running any Docker mutation.";
            return;
        }

        var message = action switch
        {
            "start" =>
                "Start this container and verify its final Docker state?",
            "stop" =>
                "Stop this container? Dependent media applications may become unavailable.",
            "restart" =>
                "Restart this container? Review its Compose ownership, mounts and current health first.",
            _ =>
                "Run the selected Docker action?"
        };

        if (!await ConfirmActionAsync(
                $"{action} {row.Name}?",
                message))
        {
            return;
        }

        _dockerActionBusy = true;
        UpdateDockerWorkspaceActionButtons();
        Get<TextBlock>("DockerActionStatusText").Text =
            $"{action} in progress...";

        try
        {
            var result = await _actions.ContainerAsync(row.Name, action);
            _history.RecordAction(row.Name, action, result);
            _controlPlane.State.RecordActivity(
                "Action",
                _controlPlane.ActiveProfile.DisplayName,
                $"{action} {row.Name}",
                result.Summary,
                "DockerNav");

            await RefreshDockerWorkspaceAsync(row.Name);
            var final = _dockerFleetSnapshot?.Containers
                .FirstOrDefault(item => item.Name.Equals(
                    row.Name,
                    StringComparison.OrdinalIgnoreCase));
            var expected = action == "stop"
                ? final?.IsRunning == false
                : final?.IsRunning == true;

            Get<TextBlock>("DockerActionStatusText").Text =
                final is null
                    ? $"{result.Summary} · Final state could not be located."
                    : $"{result.Summary} · " +
                      $"Verified {final.StateLabel}/{final.HealthLabel}" +
                      (expected ? "." : " (unexpected final state).");
        }
        finally
        {
            _dockerActionBusy = false;
            UpdateDockerWorkspaceActionButtons();
        }
    }

    private async void DockerRestartDumbButton_OnClick(
        object? sender,
        RoutedEventArgs e)
    {
        var row = SelectedDockerRow();
        if (row is null ||
            !row.ComposeProject.Equals(
                "dumb",
                StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        if (!CanRunLocalMutations() ||
            Get<CheckBox>("SafeModeCheckBox").IsChecked == true)
        {
            Get<TextBlock>("DockerActionStatusText").Text =
                "Disable Safe Mode before restarting the DUMB project.";
            return;
        }

        if (!await ConfirmActionAsync(
                "Restart the complete DUMB project?",
                $"Restart every container owned by Compose project '{row.ComposeProject}' from '{row.ComposeWorkingDirectory}' and verify the project returns to running state?"))
        {
            return;
        }

        _dockerActionBusy = true;
        UpdateDockerWorkspaceActionButtons();
        Get<TextBlock>("DockerActionStatusText").Text =
            "DUMB project restart in progress...";

        try
        {
            var result =
                await _dockerWorkspaceService.RestartDumbProjectAsync(
                    row.ComposeProject,
                    row.ComposeWorkingDirectory);

            _history.RecordPolicy(
                "Docker",
                result.Success
                    ? "DUMB PROJECT RESTART VERIFIED"
                    : "DUMB PROJECT RESTART FAILED",
                result.Summary);
            _controlPlane.State.RecordActivity(
                "Action",
                _controlPlane.ActiveProfile.DisplayName,
                "Restart DUMB Compose project",
                result.Summary,
                "DockerNav");

            Get<TextBlock>("DockerActionStatusText").Text =
                result.Summary;
            await RefreshDockerWorkspaceAsync(row.Name);
        }
        finally
        {
            _dockerActionBusy = false;
            UpdateDockerWorkspaceActionButtons();
        }
    }
}
