using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using GraveOps.Core.Hosts;
using GraveOps.Platform.Linux;

namespace GraveOps.Desktop.Linux;

public partial class MainWindow : Window
{
    private readonly ILocalHostProbe _hostProbe =
        new LocalLinuxHostProbe();

    private readonly IReadOnlyDictionary<string, NavigationTarget>
        _navigation =
            new Dictionary<string, NavigationTarget>(
                StringComparer.Ordinal)
            {
                ["DashboardNav"] = new(
                    "DashboardPage",
                    "Dashboard",
                    "Fleet-aware local Linux operations"),
                ["IntelligenceNav"] = new(
                    "IntelligencePage",
                    "Intelligence",
                    "Impact-aware inspection and recommendations"),
                ["LifecycleNav"] = new(
                    "LifecyclePage",
                    "Lifecycle",
                    "Media request-to-library operational flow"),
                ["HistoryNav"] = new(
                    "HistoryPage",
                    "History & Incidents",
                    "Meaningful transitions and incident replay"),
                ["ServersNav"] = new(
                    "ServersPage",
                    "Servers",
                    "Selected-host identity, runtime, and capabilities"),
                ["MediaHubNav"] = new(
                    "MediaHubPage",
                    "Media Hub",
                    "Verified media and acquisition integrations"),
                ["ServicesNav"] = new(
                    "ServicesPage",
                    "Services & Actions",
                    "Native systemd service inventory"),
                ["DockerNav"] = new(
                    "DockerPage",
                    "Docker",
                    "Containers, images, state, and published ports"),
                ["StorageNav"] = new(
                    "StoragePage",
                    "Storage",
                    "Operational filesystems and mount usage"),
                ["LogsNav"] = new(
                    "LogsPage",
                    "Logs",
                    "Recent warning-or-higher journal entries"),
                ["BackupsNav"] = new(
                    "BackupsPage",
                    "Backups",
                    "Backup readiness and restore safety")
            };

    private HostSnapshot? _latestSnapshot;

    public MainWindow()
    {
        InitializeComponent();

        Opened += async (_, _) =>
        {
            Navigate("DashboardNav");
            await RefreshAsync();
        };
    }

    private T Get<T>(
        string name)
        where T : Control =>
        this.FindControl<T>(name) ??
        throw new InvalidOperationException(
            $"Required control '{name}' was not found.");

    private void TitleDragRegion_OnPointerPressed(
        object? sender,
        PointerPressedEventArgs e)
    {
        if (e.GetCurrentPoint(this)
            .Properties
            .IsLeftButtonPressed)
        {
            BeginMoveDrag(e);
        }
    }

    private void TitleDragRegion_OnDoubleTapped(
        object? sender,
        TappedEventArgs e)
    {
        ToggleMaximized();
    }

    private void Minimize_Click(
        object? sender,
        RoutedEventArgs e)
    {
        WindowState = WindowState.Minimized;
    }

    private void Maximize_Click(
        object? sender,
        RoutedEventArgs e)
    {
        ToggleMaximized();
    }

    private void Close_Click(
        object? sender,
        RoutedEventArgs e)
    {
        Close();
    }

    private void ToggleMaximized()
    {
        WindowState =
            WindowState == WindowState.Maximized
                ? WindowState.Normal
                : WindowState.Maximized;
    }

    private void NavigationButton_OnClick(
        object? sender,
        RoutedEventArgs e)
    {
        if (sender is Button button &&
            !string.IsNullOrWhiteSpace(button.Name))
        {
            Navigate(button.Name);
        }
    }

    private void Navigate(
        string navigationName)
    {
        if (!_navigation.TryGetValue(
                navigationName,
                out var target))
        {
            return;
        }

        foreach (var entry in _navigation)
        {
            Get<Control>(entry.Value.PageName)
                .IsVisible =
                    entry.Key.Equals(
                        navigationName,
                        StringComparison.Ordinal);

            Get<Button>(entry.Key)
                .Classes
                .Set(
                    "selected",
                    entry.Key.Equals(
                        navigationName,
                        StringComparison.Ordinal));
        }

        Get<TextBlock>("PageTitleText").Text =
            target.Title;

        Get<TextBlock>("PageSubtitleText").Text =
            target.Subtitle;
    }

    private async void RefreshButton_OnClick(
        object? sender,
        RoutedEventArgs e)
    {
        await RefreshAsync();
    }

    private async Task RefreshAsync()
    {
        var refreshButton =
            Get<Button>("RefreshButton");

        refreshButton.IsEnabled = false;
        refreshButton.Content =
            "Refreshing environment...";

        Get<TextBlock>("StatusStripText").Text =
            "Capturing native Linux telemetry...";

        try
        {
            _latestSnapshot =
                await _hostProbe.CaptureAsync();

            Populate(_latestSnapshot);

            Get<TextBlock>("StatusStripText").Text =
                _latestSnapshot.FailedUnits.Count == 0 &&
                _latestSnapshot.Warnings.Count == 0
                    ? "Environment healthy · local provider capture complete"
                    : "Environment captured · attention items are available";

            Get<TextBlock>("ConnectionText").Text =
                "Ready";
        }
        catch (Exception exception)
        {
            Get<TextBlock>("StatusStripText").Text =
                "Local provider capture failed";

            Get<TextBlock>("ConnectionText").Text =
                "Attention";

            Get<ListBox>("DashboardAttentionList")
                .ItemsSource =
                    new[]
                    {
                        $"Capture failure · {exception.Message}"
                    };
        }
        finally
        {
            refreshButton.IsEnabled = true;
            refreshButton.Content =
                "Refresh environment";
        }
    }

    private void Populate(
        HostSnapshot snapshot)
    {
        Get<TextBlock>("SidebarHostname").Text =
            snapshot.Hostname;

        Get<TextBlock>("SidebarOperatingSystem").Text =
            snapshot.OperatingSystem;

        Get<TextBlock>("LastUpdatedText").Text =
            $"Captured {snapshot.CapturedAt.ToLocalTime():g}";

        Get<TextBlock>("DashboardHostnameText").Text =
            snapshot.Hostname;

        Get<TextBlock>("DashboardOsText").Text =
            snapshot.OperatingSystem;

        Get<TextBlock>("DashboardSystemText").Text =
            snapshot.SystemState;

        Get<TextBlock>("DashboardKernelText").Text =
            $"Kernel {snapshot.Kernel}";

        Get<TextBlock>("DashboardDockerText").Text =
            snapshot.DockerState;

        Get<TextBlock>("DashboardUptimeText").Text =
            snapshot.Uptime;

        Get<TextBlock>("DashboardCpuText").Text =
            snapshot.CpuModel;

        Get<TextBlock>("DashboardLoadText").Text =
            $"Load {snapshot.LoadAverage}";

        Get<TextBlock>("DashboardMemoryText").Text =
            snapshot.MemorySummary;

        Get<TextBlock>("DashboardIpText").Text =
            snapshot.IpAddresses;

        Get<TextBlock>("DashboardDiscoveryText").Text =
            $"{snapshot.Integrations.Count} integrations · " +
            $"{snapshot.Containers.Count} containers";

        Get<TextBlock>("DashboardServicesModuleText").Text =
            snapshot.Services.Count.ToString();

        Get<TextBlock>("DashboardStorageModuleText").Text =
            snapshot.Storage.Count.ToString();

        Get<TextBlock>("DashboardMediaModuleText").Text =
            snapshot.Integrations.Count.ToString();

        var attention = snapshot.FailedUnits
            .Select(unit => $"FAILED SERVICE · {unit}")
            .Concat(
                snapshot.Warnings.Select(
                    warning => $"PROVIDER · {warning}"))
            .ToArray();

        Get<ListBox>("DashboardAttentionList")
            .ItemsSource =
                attention.Length == 0
                    ? new[]
                    {
                        "No active warnings or failed services."
                    }
                    : attention;

        Get<TextBlock>("DashboardHealthBadge").Text =
            attention.Length == 0
                ? "HEALTHY"
                : "ATTENTION";

        PopulateServerPage(snapshot);
        PopulateMediaPage(snapshot);
        PopulateServicesPage(snapshot);
        PopulateDockerPage(snapshot);
        PopulateStoragePage(snapshot);
        PopulateLogsPage(snapshot);
    }

    private void PopulateServerPage(
        HostSnapshot snapshot)
    {
        Get<TextBlock>("ServerIdentityText").Text =
            snapshot.Hostname;

        Get<TextBlock>("ServerOperatingSystemText").Text =
            snapshot.OperatingSystem;

        Get<TextBlock>("ServerKernelText").Text =
            $"Kernel {snapshot.Kernel}";

        Get<TextBlock>("ServerUptimeText").Text =
            snapshot.Uptime;

        Get<TextBlock>("ServerCpuText").Text =
            $"CPU · {snapshot.CpuModel}\n" +
            $"Load · {snapshot.LoadAverage}";

        Get<TextBlock>("ServerMemoryText").Text =
            $"Memory · {snapshot.MemorySummary}";

        Get<TextBlock>("ServerNetworkText").Text =
            $"Addresses · {snapshot.IpAddresses}";
    }

    private void PopulateMediaPage(
        HostSnapshot snapshot)
    {
        var rows = snapshot.Integrations
            .Select(integration =>
                $"{integration.Name,-16} " +
                $"{integration.Kind,-8} " +
                $"{integration.State,-24} " +
                $"{integration.Evidence}")
            .ToArray();

        Get<ListBox>("IntegrationsList")
            .ItemsSource =
                rows.Length == 0
                    ? new[]
                    {
                        "No supported integration identity was verified."
                    }
                    : rows;
    }

    private void PopulateServicesPage(
        HostSnapshot snapshot)
    {
        var rows = snapshot.Services
            .Select(service =>
                $"{service.Unit,-30} " +
                $"{service.ActiveState,-10} " +
                $"{service.SubState,-12} " +
                $"{service.UnitFileState,-12} " +
                $"{service.Description}")
            .ToArray();

        Get<ListBox>("ServicesList")
            .ItemsSource =
                rows.Length == 0
                    ? new[]
                    {
                        "No known systemd service units were discovered."
                    }
                    : rows;

        Get<TextBlock>("ServicesSummaryText").Text =
            $"{snapshot.Services.Count} discovered · " +
            $"{snapshot.FailedUnits.Count} failed";
    }

    private void PopulateDockerPage(
        HostSnapshot snapshot)
    {
        var rows = snapshot.Containers
            .Select(container =>
                $"{container.Name,-25} " +
                $"{container.State,-10} " +
                $"{container.Image,-36} " +
                $"{container.Status,-28} " +
                $"{container.Ports}")
            .ToArray();

        Get<ListBox>("DockerList")
            .ItemsSource =
                rows.Length == 0
                    ? new[]
                    {
                        "No Docker containers were returned. " +
                        "Confirm Docker is running and this user can access the daemon."
                    }
                    : rows;

        var running = snapshot.Containers.Count(
            container =>
                container.State.Equals(
                    "running",
                    StringComparison.OrdinalIgnoreCase));

        Get<TextBlock>("DockerSummaryText").Text =
            $"{running} running · " +
            $"{snapshot.Containers.Count} total";
    }

    private void PopulateStoragePage(
        HostSnapshot snapshot)
    {
        var header =
            $"{"SOURCE",-22} {"TYPE",-10} {"SIZE",8} " +
            $"{"USED",8} {"FREE",8} {"USE%",6} MOUNT";

        var rows = new[] { header }
            .Concat(
                snapshot.Storage.Select(volume =>
                    $"{volume.Source,-22} " +
                    $"{volume.FileSystem,-10} " +
                    $"{volume.Size,8} " +
                    $"{volume.Used,8} " +
                    $"{volume.Available,8} " +
                    $"{volume.PercentUsed,6} " +
                    $"{volume.MountPoint}"))
            .ToArray();

        Get<ListBox>("StorageList")
            .ItemsSource = rows;

        Get<TextBlock>("StorageSummaryText").Text =
            $"{snapshot.Storage.Count} operational filesystems";
    }

    private void PopulateLogsPage(
        HostSnapshot snapshot)
    {
        Get<TextBox>("LogsText").Text =
            string.Join(
                Environment.NewLine,
                snapshot.RecentLogs);
    }

    private sealed record NavigationTarget(
        string PageName,
        string Title,
        string Subtitle);
}
