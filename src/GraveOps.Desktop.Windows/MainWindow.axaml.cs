using Avalonia.Controls;
using Avalonia.Interactivity;
using GraveOps.Core.Hosts;
using GraveOps.Platform.Windows;

namespace GraveOps.Desktop.Windows;

public partial class MainWindow : Window
{
    private readonly ILocalHostProbe _hostProbe =
        new LocalWindowsHostProbe();

    public MainWindow()
    {
        InitializeComponent();

        Opened += async (_, _) =>
            await RefreshAsync();
    }

    private async void RefreshButton_Click(
        object? sender,
        RoutedEventArgs e) =>
        await RefreshAsync();

    private async Task RefreshAsync()
    {
        var refreshButton =
            this.FindControl<Button>("RefreshButton")!;
        var statusText =
            this.FindControl<TextBlock>("StatusText")!;

        refreshButton.IsEnabled = false;
        statusText.Text =
            "Capturing the local Windows host snapshot...";

        try
        {
            var snapshot =
                await _hostProbe.CaptureAsync();

            SetText("HostnameText", snapshot.Hostname);
            SetText(
                "OsText",
                $"{snapshot.OperatingSystem} | kernel {snapshot.Kernel}");
            SetText("AddressText", snapshot.IpAddresses);
            SetText("MemoryText", snapshot.MemorySummary);
            SetText("UptimeText", snapshot.Uptime);
            SetText("SystemText", snapshot.SystemState);
            SetText("DockerText", snapshot.DockerState);

            SetOutput(
                "StorageText",
                snapshot.Storage.Count == 0
                    ? "No ready storage volumes were reported."
                    : string.Join(
                        Environment.NewLine,
                        snapshot.Storage.Select(volume =>
                            $"{volume.MountPoint,-8}  " +
                            $"{volume.FileSystem,-8}  " +
                            $"{volume.Used,10} / {volume.Size,-10}  " +
                            $"{volume.PercentUsed,6}  " +
                            $"{volume.Source}")));

            SetOutput(
                "ServicesText",
                snapshot.Services.Count == 0
                    ? "No cataloged GraveOps-related Windows services were detected."
                    : string.Join(
                        Environment.NewLine,
                        snapshot.Services.Select(service =>
                            $"{service.Unit,-24}  " +
                            $"{service.ActiveState,-12}  " +
                            $"{service.UnitFileState,-12}  " +
                            $"{service.Description}")));

            SetOutput(
                "ContainersText",
                snapshot.Containers.Count == 0
                    ? "Docker is unavailable or no containers were reported."
                    : string.Join(
                        Environment.NewLine,
                        snapshot.Containers.Select(container =>
                            $"{container.Name,-24}  " +
                            $"{container.State,-10}  " +
                            $"{container.Image,-42}  " +
                            $"{container.Status}")));

            SetOutput(
                "IntegrationsText",
                snapshot.Integrations.Count == 0
                    ? "No cataloged GraveOps integrations were detected."
                    : string.Join(
                        Environment.NewLine,
                        snapshot.Integrations.Select(integration =>
                            $"{integration.Name,-18}  " +
                            $"{integration.Kind,-18}  " +
                            $"{integration.State,-12}  " +
                            $"{integration.Evidence}")));

            SetOutput(
                "WarningsText",
                snapshot.Warnings.Count == 0
                    ? "No provider warnings."
                    : string.Join(
                        Environment.NewLine,
                        snapshot.Warnings.Select(warning =>
                            $"- {warning}")));

            statusText.Text =
                $"Snapshot captured " +
                $"{snapshot.CapturedAt.ToLocalTime():g}. " +
                $"{snapshot.Storage.Count} storage volume(s), " +
                $"{snapshot.Services.Count} service(s), " +
                $"{snapshot.Containers.Count} container(s), " +
                $"{snapshot.Integrations.Count} integration(s).";
        }
        catch (Exception exception)
        {
            statusText.Text =
                "Snapshot failed: " + exception.Message;
            SetOutput(
                "WarningsText",
                exception.ToString());
        }
        finally
        {
            refreshButton.IsEnabled = true;
        }
    }

    private void SetText(
        string name,
        string value) =>
        this.FindControl<TextBlock>(name)!.Text =
            string.IsNullOrWhiteSpace(value)
                ? "--"
                : value;

    private void SetOutput(
        string name,
        string value) =>
        this.FindControl<TextBox>(name)!.Text = value;
}
