using System.Windows;
using System.Windows.Controls;
using GraveOps.App.Models;
using GraveOps.App.Services;
using GraveOps.App.Windows;

namespace GraveOps.App.Views;

public partial class OperationsDrillDownWindow : Window
{
    private readonly OperationsDrillDownService _service = new(App.Services);
    private readonly int _initialTab;
    private AppServices S => App.Services;
    private ServerProfile? Server => S.Context.Current;
    private DockerDrillRow? SelectedDocker => DockerGrid.SelectedItem as DockerDrillRow;
    private StorageDrillRow? SelectedStorage => StorageGrid.SelectedItem as StorageDrillRow;
    private bool _loaded;

    public OperationsDrillDownWindow(int initialTab = 0)
    {
        InitializeComponent();
        _initialTab = Math.Clamp(initialTab, 0, 2);
        Loaded += OperationsDrillDownWindow_Loaded;
    }

    private async void OperationsDrillDownWindow_Loaded(object sender, RoutedEventArgs e)
    {
        if (_loaded) return;
        _loaded = true;
        ModeTabs.SelectedIndex = _initialTab;
        TargetText.Text = Server?.Name ?? "No global target";
        await RefreshCurrentAsync();
    }

    private async void Refresh_Click(object sender, RoutedEventArgs e)
        => await RefreshCurrentAsync();

    private async void ModeTabs_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!IsLoaded || !_loaded || e.Source != ModeTabs) return;
        await RefreshCurrentAsync();
    }

    private async Task RefreshCurrentAsync()
    {
        if (Server is not { } server)
        {
            StatusText.Text = "Select a global server target first.";
            return;
        }

        RefreshButton.IsEnabled = false;
        TargetText.Text = server.Name;
        try
        {
            switch (ModeTabs.SelectedIndex)
            {
                case 0:
                    StatusText.Text = "Collecting container inspect and resource telemetry...";
                    var docker = await _service.GetDockerAsync(server);
                    DockerGrid.ItemsSource = docker;
                    StatusText.Text = $"Loaded {docker.Count} container(s).";
                    break;

                case 1:
                    StatusText.Text = "Collecting mount identity and capacity telemetry...";
                    var storage = await _service.GetStorageAsync(server);
                    StorageGrid.ItemsSource = storage;
                    StatusText.Text = $"Loaded {storage.Count} mounted target(s).";
                    break;

                default:
                    StatusText.Text = "Collecting Arr, SABnzbd and qBittorrent queue detail...";
                    var queues = await _service.GetQueuesAsync(server);
                    QueueGrid.ItemsSource = queues;
                    StatusText.Text = $"Loaded {queues.Count} queue / health row(s).";
                    break;
            }
        }
        catch (Exception ex)
        {
            StatusText.Text = ex.Message;
            GraveOpsDialog.Show(
                this,
                ex.Message,
                "Operations drill-down failed",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
        finally
        {
            RefreshButton.IsEnabled = true;
        }
    }

    private async void DockerGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        var row = SelectedDocker;
        DockerStartButton.IsEnabled = row is not null;
        DockerRestartButton.IsEnabled = row is not null;
        DockerStopButton.IsEnabled = row is not null;

        if (row is null || Server is not { } server)
        {
            DockerSelectedText.Text = "Select a container for inspect, logs and verified controls.";
            DockerDetailBox.Text = "";
            return;
        }

        DockerSelectedText.Text =
            $"{row.Name} | {row.State} | health {row.Health} | restarts {row.Restarts}";
        DockerDetailBox.Text = "Loading inspect and recent logs...";

        try
        {
            DockerDetailBox.Text = await _service.GetDockerDetailAsync(server, row.Name);
        }
        catch (Exception ex)
        {
            DockerDetailBox.Text = ex.ToString();
        }
    }

    private async void StorageGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        var row = SelectedStorage;
        StorageVerifyButton.IsEnabled = row is not null;
        StorageTerminalButton.IsEnabled = row is not null;

        if (row is null || Server is not { } server)
        {
            StorageSelectedText.Text = "Select a mount for identity, block-device and SMART detail.";
            StorageDetailBox.Text = "";
            return;
        }

        StorageSelectedText.Text =
            $"{row.Target} | {row.Source} | {row.FileSystem} | {row.UsageText} used";
        StorageDetailBox.Text = "Loading mount, block-device and SMART detail...";

        try
        {
            StorageDetailBox.Text = await _service.GetStorageDetailAsync(server, row.Target);
        }
        catch (Exception ex)
        {
            StorageDetailBox.Text = ex.ToString();
        }
    }

    private async Task RunDockerMutationAsync(string operation)
    {
        if (SelectedDocker is not { } row || Server is not { } server) return;

        var verb = char.ToUpperInvariant(operation[0]) + operation[1..];
        var warning = operation == "stop"
            ? $"Stop container '{row.Name}' on {server.Name}?"
            : $"{verb} container '{row.Name}' on {server.Name}?";

        if (GraveOpsDialog.Show(
                this,
                warning + "\n\nGraveOps will verify the resulting container state before reporting success.",
                "Confirm Docker operation",
                MessageBoxButton.YesNo,
                operation == "stop" ? MessageBoxImage.Warning : MessageBoxImage.Question)
            != MessageBoxResult.Yes)
            return;

        SetDockerButtons(false);
        StatusText.Text = $"Running Docker {operation} for {row.Name}...";

        try
        {
            var result = await _service.RunDockerOperationAsync(server, row.Name, operation);
            GraveOpsDialog.Show(
                this,
                result.Success
                    ? result.Verification
                    : (string.IsNullOrWhiteSpace(result.Error) ? result.Verification : result.Error),
                result.Success ? "Docker operation verified" : "Docker operation failed",
                MessageBoxButton.OK,
                result.Success ? MessageBoxImage.Information : MessageBoxImage.Error);

            await RefreshCurrentAsync();
        }
        finally
        {
            SetDockerButtons(SelectedDocker is not null);
        }
    }

    private void SetDockerButtons(bool enabled)
    {
        DockerStartButton.IsEnabled = enabled;
        DockerRestartButton.IsEnabled = enabled;
        DockerStopButton.IsEnabled = enabled;
    }

    private async void DockerStart_Click(object sender, RoutedEventArgs e)
        => await RunDockerMutationAsync("start");

    private async void DockerRestart_Click(object sender, RoutedEventArgs e)
        => await RunDockerMutationAsync("restart");

    private async void DockerStop_Click(object sender, RoutedEventArgs e)
        => await RunDockerMutationAsync("stop");

    private async void StorageVerify_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedStorage is not { } row || Server is not { } server) return;
        StatusText.Text = $"Verifying {row.Target}...";

        try
        {
            var result = await _service.VerifyStorageAsync(server, row.Target);
            StorageDetailBox.Text = result + "\n\n" + StorageDetailBox.Text;
            StatusText.Text = $"Verification completed for {row.Target}.";
        }
        catch (Exception ex)
        {
            StatusText.Text = ex.Message;
            GraveOpsDialog.Show(
                this,
                ex.Message,
                "Storage verification failed",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    private void StorageTerminal_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedStorage is not { } row || Server is not { } server) return;
        TerminalView.QueueSshHandoff(server, row.Target);
        S.Navigation.Request("page:Terminal");
        Close();
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}