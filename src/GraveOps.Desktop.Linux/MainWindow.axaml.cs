using System;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Interactivity;
using GraveOps.Core.Hosts;
using GraveOps.Platform.Linux;

namespace GraveOps.Desktop.Linux;

public partial class MainWindow : Window
{
    private readonly ILocalHostProbe _hostProbe =
        new LocalLinuxHostProbe();

    public MainWindow()
    {
        InitializeComponent();
        Opened += async (_, _) => await RefreshAsync();
    }

    private async void RefreshButton_OnClick(
        object? sender,
        RoutedEventArgs e)
    {
        await RefreshAsync();
    }

    private async Task RefreshAsync()
    {
        var refreshButton = this.FindControl<Button>("RefreshButton");

        if (refreshButton is not null)
        {
            refreshButton.IsEnabled = false;
            refreshButton.Content = "Refreshing...";
        }

        try
        {
            var snapshot = await _hostProbe.CaptureAsync();

            SetText("SidebarHostname", snapshot.Hostname);
            SetText("HostnameText", snapshot.Hostname);
            SetText("OperatingSystemText", snapshot.OperatingSystem);
            SetText("SystemStateText", snapshot.SystemState);
            SetText("KernelText", $"Kernel {snapshot.Kernel}");
            SetText("DockerText", snapshot.DockerState);
            SetText("UptimeText", snapshot.Uptime);
            SetText(
                "CapturedAtText",
                $"Captured {snapshot.CapturedAt.ToLocalTime():g}");

            var storageLines = snapshot.Storage.Count == 0
                ? new[] { "No local storage rows were returned." }
                : snapshot.Storage.Select(volume =>
                    $"{volume.Source,-22} " +
                    $"{volume.Size,8} " +
                    $"{volume.Used,8} used " +
                    $"{volume.Available,8} free " +
                    $"{volume.PercentUsed,6} " +
                    $"{volume.MountPoint}");

            SetText("StorageText", string.Join(
                Environment.NewLine,
                storageLines));

            SetText(
                "WarningsText",
                snapshot.Warnings.Count == 0
                    ? "None"
                    : string.Join(
                        Environment.NewLine,
                        snapshot.Warnings.Select(
                            warning => $"• {warning}")));
        }
        catch (Exception exception)
        {
            SetText(
                "WarningsText",
                $"Unable to capture the local Linux host: " +
                exception.Message);
        }
        finally
        {
            if (refreshButton is not null)
            {
                refreshButton.IsEnabled = true;
                refreshButton.Content = "Refresh host";
            }
        }
    }

    private void SetText(
        string controlName,
        string text)
    {
        var textBlock = this.FindControl<TextBlock>(controlName);
        if (textBlock is not null)
        {
            textBlock.Text = text;
            return;
        }

        var textBox = this.FindControl<TextBox>(controlName);
        if (textBox is not null)
            textBox.Text = text;
    }
}