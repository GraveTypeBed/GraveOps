using System;
using System.IO;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Interactivity;

namespace GraveOps.Desktop.Linux;

public partial class MainWindow
{
    private readonly RecyclarrWorkspaceService _recyclarrWorkspaceService = new();
    private RecyclarrWorkspaceSnapshot? _recyclarrWorkspaceSnapshot;
    private bool _recyclarrWorkspaceBusy;

    private void InitializeRecyclarrWorkspace()
    {
        Get<Button>("RecyclarrOpenConfigButton").IsEnabled = false;
        Get<Button>("RecyclarrPreviewButton").IsEnabled = false;
        Get<TextBox>("RecyclarrOutputText").Text =
            "Preview has not been run. GraveOps will use Recyclarr preview mode, which reads Sonarr and Radarr state without applying changes.";
        Get<TextBlock>("RecyclarrStatusText").Text =
            "Open this page to capture the Recyclarr container and configuration inventory.";
    }

    private void ActivateRecyclarrWorkspace()
    {
        PopulateRecyclarrWorkspace();

        if (!_controlPlane.ActiveProfile.IsLocal)
        {
            ApplyRemoteRecyclarrBoundary();
            return;
        }

        _ = RefreshRecyclarrWorkspaceAsync();
    }

    private void ApplyRemoteRecyclarrBoundary()
    {
        Get<TextBlock>("RecyclarrRuntimeMetricText").Text = "REMOTE";
        Get<TextBlock>("RecyclarrVersionMetricText").Text = "--";
        Get<TextBlock>("RecyclarrConfigMetricText").Text = "--";
        Get<TextBlock>("RecyclarrTargetMetricText").Text = "--";
        Get<TextBlock>("RecyclarrTargetText").Text =
            _controlPlane.ActiveProfile.DisplayName;
        Get<TextBlock>("RecyclarrFreshnessText").Text =
            "Local provider required";
        Get<TextBlock>("RecyclarrStatusText").Text =
            "The native Recyclarr workspace currently reads the local Docker provider. Remote Recyclarr capture remains a later provider extension.";
        Get<Button>("RecyclarrOpenConfigButton").IsEnabled = false;
        Get<Button>("RecyclarrPreviewButton").IsEnabled = false;
    }

    private async Task RefreshRecyclarrWorkspaceAsync()
    {
        if (_recyclarrWorkspaceBusy)
            return;

        if (!_controlPlane.ActiveProfile.IsLocal)
        {
            ApplyRemoteRecyclarrBoundary();
            return;
        }

        _recyclarrWorkspaceBusy = true;
        var button = Get<Button>("RecyclarrRefreshButton");
        button.IsEnabled = false;
        button.Content = "Refreshing...";

        try
        {
            _recyclarrWorkspaceSnapshot =
                await _recyclarrWorkspaceService.CaptureAsync();
            PopulateRecyclarrWorkspace();
        }
        catch (Exception exception)
        {
            Get<TextBlock>("RecyclarrStatusText").Text =
                $"Recyclarr capture failed: {exception.Message}";
            Get<TextBox>("RecyclarrOutputText").Text =
                $"Recyclarr capture failed.{Environment.NewLine}{exception}";
        }
        finally
        {
            button.IsEnabled = true;
            button.Content = "Refresh";
            _recyclarrWorkspaceBusy = false;
        }
    }

    private void PopulateRecyclarrWorkspace()
    {
        var snapshot = _recyclarrWorkspaceSnapshot;

        Get<TextBlock>("RecyclarrTargetText").Text =
            _controlPlane.ActiveProfile.DisplayName;

        if (snapshot is null)
        {
            Get<TextBlock>("RecyclarrRuntimeMetricText").Text = "WAITING";
            Get<TextBlock>("RecyclarrVersionMetricText").Text = "--";
            Get<TextBlock>("RecyclarrConfigMetricText").Text = "0";
            Get<TextBlock>("RecyclarrTargetMetricText").Text = "0";
            Get<TextBlock>("RecyclarrFreshnessText").Text =
                "Capture pending";
            Get<ListBox>("RecyclarrTargetsList").ItemsSource =
                Array.Empty<RecyclarrTargetRow>();
            Get<ListBox>("RecyclarrConfigFilesList").ItemsSource =
                Array.Empty<RecyclarrConfigFileRow>();
            Get<Border>("RecyclarrTargetsEmptyState").IsVisible = true;
            Get<Border>("RecyclarrConfigFilesEmptyState").IsVisible = true;
            Get<Button>("RecyclarrOpenConfigButton").IsEnabled = false;
            Get<Button>("RecyclarrPreviewButton").IsEnabled = false;
            return;
        }

        Get<TextBlock>("RecyclarrRuntimeMetricText").Text =
            snapshot.RuntimeState;
        Get<TextBlock>("RecyclarrVersionMetricText").Text =
            snapshot.Version;
        Get<TextBlock>("RecyclarrConfigMetricText").Text =
            snapshot.ConfigFiles.Count.ToString();
        Get<TextBlock>("RecyclarrTargetMetricText").Text =
            snapshot.Targets.Count.ToString();
        Get<TextBlock>("RecyclarrFreshnessText").Text =
            $"Captured {snapshot.CapturedAt:t}";

        Get<TextBlock>("RecyclarrContainerNameText").Text =
            snapshot.ContainerName;
        Get<TextBlock>("RecyclarrImageText").Text =
            snapshot.Image;
        Get<TextBlock>("RecyclarrComposeText").Text =
            snapshot.ComposeProject == "--"
                ? "No Compose ownership label"
                : $"{snapshot.ComposeProject} / {snapshot.ComposeService}";
        Get<TextBlock>("RecyclarrScheduleText").Text =
            snapshot.Schedule;
        Get<TextBlock>("RecyclarrConfigPathText").Text =
            snapshot.ConfigHostPath == "--"
                ? snapshot.ConfigContainerPath
                : snapshot.ConfigHostPath;
        Get<TextBlock>("RecyclarrLastRunText").Text =
            snapshot.LastRunSummary;
        Get<TextBlock>("RecyclarrEvidenceText").Text =
            snapshot.Evidence;

        var targets = Get<ListBox>("RecyclarrTargetsList");
        targets.ItemsSource = snapshot.Targets;
        targets.IsVisible = snapshot.Targets.Count > 0;
        Get<Border>("RecyclarrTargetsEmptyState").IsVisible =
            snapshot.Targets.Count == 0;
        Get<TextBlock>("RecyclarrTargetCountText").Text =
            $"{snapshot.Targets.Count} " +
            $"{(snapshot.Targets.Count == 1 ? "target" : "targets")}";

        var files = Get<ListBox>("RecyclarrConfigFilesList");
        files.ItemsSource = snapshot.ConfigFiles;
        files.IsVisible = snapshot.ConfigFiles.Count > 0;
        Get<Border>("RecyclarrConfigFilesEmptyState").IsVisible =
            snapshot.ConfigFiles.Count == 0;
        Get<TextBlock>("RecyclarrConfigCountText").Text =
            $"{snapshot.ConfigFiles.Count} " +
            $"{(snapshot.ConfigFiles.Count == 1 ? "file" : "files")}";

        Get<Button>("RecyclarrOpenConfigButton").IsEnabled =
            snapshot.ConfigHostPath != "--" &&
            Directory.Exists(snapshot.ConfigHostPath);
        Get<Button>("RecyclarrPreviewButton").IsEnabled =
            snapshot.IsRunning;

        Get<TextBlock>("RecyclarrStatusText").Text =
            snapshot.IsRunning
                ? snapshot.Targets.Count > 0
                    ? "Container, configuration files and Sonarr/Radarr targets are available. Preview remains read-only."
                    : "The Recyclarr container is running, but no Sonarr or Radarr target could be parsed from the readable config files."
                : "The Recyclarr container is present but not running. Preview is unavailable until the container is started.";
    }

    private async void RecyclarrRefreshButton_OnClick(
        object? sender,
        RoutedEventArgs e) =>
        await RefreshRecyclarrWorkspaceAsync();

    private async void RecyclarrPreviewButton_OnClick(
        object? sender,
        RoutedEventArgs e)
    {
        var snapshot = _recyclarrWorkspaceSnapshot;
        if (snapshot is null || !snapshot.IsRunning)
            return;

        var button = Get<Button>("RecyclarrPreviewButton");
        button.IsEnabled = false;
        button.Content = "Previewing...";

        Get<TextBlock>("RecyclarrPreviewStatusText").Text =
            "Running Recyclarr preview. This can take several minutes while it reads Sonarr and Radarr state.";

        try
        {
            var result =
                await _recyclarrWorkspaceService.PreviewAsync(
                    snapshot.ContainerName);

            Get<TextBox>("RecyclarrOutputText").Text =
                result.Output;
            Get<TextBlock>("RecyclarrPreviewStatusText").Text =
                result.Summary;
        }
        catch (Exception exception)
        {
            Get<TextBox>("RecyclarrOutputText").Text =
                $"Preview failed.{Environment.NewLine}{exception}";
            Get<TextBlock>("RecyclarrPreviewStatusText").Text =
                $"Preview failed: {exception.Message}";
        }
        finally
        {
            button.Content = "Preview all";
            button.IsEnabled =
                _recyclarrWorkspaceSnapshot?.IsRunning == true;
        }
    }

    private void RecyclarrOpenConfigButton_OnClick(
        object? sender,
        RoutedEventArgs e)
    {
        var path =
            _recyclarrWorkspaceSnapshot?.ConfigHostPath;

        if (string.IsNullOrWhiteSpace(path) ||
            path == "--")
        {
            Get<TextBlock>("RecyclarrStatusText").Text =
                "No host configuration path is available.";
            return;
        }

        if (LinuxOperatorTools.OpenPath(path, out var error))
        {
            Get<TextBlock>("RecyclarrStatusText").Text =
                $"Opened {path}";
        }
        else
        {
            Get<TextBlock>("RecyclarrStatusText").Text =
                error;
        }
    }

    private void RecyclarrDockerButton_OnClick(
        object? sender,
        RoutedEventArgs e) =>
        Navigate("DockerNav");

    private void RecyclarrLogsButton_OnClick(
        object? sender,
        RoutedEventArgs e) =>
        Navigate("LogsNav");
}
