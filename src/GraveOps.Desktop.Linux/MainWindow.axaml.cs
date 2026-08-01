using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using GraveOps.Core.Hosts;
using GraveOps.Platform.Linux;

namespace GraveOps.Desktop.Linux;

public partial class MainWindow : Window
{
    private readonly ILocalHostProbe _hostProbe = new LocalLinuxHostProbe();
    private readonly LinuxBackupProbe _backupProbe = new();
    private readonly LinuxHostActionService _actions = new();
    private readonly LinuxHistoryStore _history = new();

    private readonly IReadOnlyDictionary<string, NavigationTarget> _navigation =
        new Dictionary<string, NavigationTarget>(StringComparer.Ordinal)
        {
            ["DashboardNav"] = new("DashboardPage", "Dashboard", "Fleet-aware local Linux operations"),
            ["IntelligenceNav"] = new("IntelligencePage", "Intelligence", "Impact-aware inspection and recommendations"),
            ["LifecycleNav"] = new("LifecyclePage", "Lifecycle", "Provider-neutral media workflow readiness"),
            ["HistoryNav"] = new("HistoryPage", "History", "Persisted transitions, guarded actions and operator decisions"),
            ["ServersNav"] = new("ServersPage", "Servers", "Selected-host identity, runtime and provider capability"),
            ["MediaHubNav"] = new("MediaHubPage", "Media Hub", "Verified media and acquisition integrations"),
            ["ServicesNav"] = new("ServicesPage", "Services & Actions", "Native systemd inventory and guarded actions"),
            ["DockerNav"] = new("DockerPage", "Docker", "Containers, images, state, ports and guarded actions"),
            ["StorageNav"] = new("StoragePage", "Storage", "Operational filesystems and capacity health"),
            ["LogsNav"] = new("LogsPage", "Logs", "Grouped warning journal and crash evidence"),
            ["BackupsNav"] = new("BackupsPage", "Backups", "Schedule, artifact and restore-readiness evidence")
        };

    private HostSnapshot? _snapshot;
    private OpsBackupSnapshot? _backup;
    private OpsAnalysis? _analysis;
    private IReadOnlyList<OpsLifecycleStage> _lifecycle = Array.Empty<OpsLifecycleStage>();
    private IReadOnlyList<OpsIntegration> _integrations = Array.Empty<OpsIntegration>();
    private IReadOnlyList<OpsLogGroup> _logs = Array.Empty<OpsLogGroup>();

    public MainWindow()
    {
        InitializeComponent();
        Opened += async (_, _) =>
        {
            Navigate("DashboardNav");
            await RefreshAsync();
        };
    }

    private T Get<T>(string name) where T : Control =>
        this.FindControl<T>(name) ??
        throw new InvalidOperationException($"Required control '{name}' was not found.");

    private void TitleDragRegion_OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
            BeginMoveDrag(e);
    }

    private void TitleDragRegion_OnDoubleTapped(object? sender, TappedEventArgs e) => ToggleMaximized();
    private void Minimize_Click(object? sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;
    private void Maximize_Click(object? sender, RoutedEventArgs e) => ToggleMaximized();
    private void Close_Click(object? sender, RoutedEventArgs e) => Close();

    private void ToggleMaximized() =>
        WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;

    private void NavigationButton_OnClick(object? sender, RoutedEventArgs e)
    {
        if (sender is Button button && !string.IsNullOrWhiteSpace(button.Name))
            Navigate(button.Name);
    }

    private void Navigate(string navigationName)
    {
        if (!_navigation.TryGetValue(navigationName, out var target))
            return;

        foreach (var item in _navigation)
        {
            Get<Control>(item.Value.PageName).IsVisible = item.Key == navigationName;
            Get<Button>(item.Key).Classes.Set("selected", item.Key == navigationName);
        }

        Get<TextBlock>("PageTitleText").Text = target.Title;
        Get<TextBlock>("PageSubtitleText").Text = target.Subtitle;
    }

    private async void RefreshButton_OnClick(object? sender, RoutedEventArgs e) => await RefreshAsync();

    private async Task RefreshAsync()
    {
        var refresh = Get<Button>("RefreshButton");
        refresh.IsEnabled = false;
        refresh.Content = "Refreshing environment...";
        SetControlPlaneState(
            OpsSeverity.Info,
            "REFRESHING",
            "Capturing local telemetry");

        try
        {
            _snapshot = await _hostProbe.CaptureAsync();
            _backup = await _backupProbe.CaptureAsync();
            _integrations = LinuxOpsAnalyzer.EnrichIntegrations(_snapshot);
            _logs = LinuxOpsAnalyzer.GroupLogs(_snapshot.RecentLogs);
            _analysis = LinuxOpsAnalyzer.Analyze(_snapshot, _backup, _logs, _integrations);
            _lifecycle = LinuxOpsAnalyzer.BuildLifecycle(_snapshot, _integrations, _analysis);
            _history.Record(_snapshot, _analysis, _lifecycle, _backup);
            PopulateAll();
        }
        catch (Exception exception)
        {
            SetControlPlaneState(
                OpsSeverity.Error,
                "OFFLINE",
                "Provider capture failed");
            Get<TextBlock>("LastUpdatedText").Text =
                $"Capture failed · {exception.Message}";
        }
        finally
        {
            refresh.IsEnabled = true;
            refresh.Content = "Refresh environment";
        }
    }

    private void PopulateAll()
    {
        if (_snapshot is null || _backup is null || _analysis is null)
            return;

        Get<TextBlock>("SidebarHostname").Text = _snapshot.Hostname;
        Get<TextBlock>("SidebarOperatingSystem").Text = _snapshot.OperatingSystem;
        Get<TextBlock>("LastUpdatedText").Text =
            $"Captured {_snapshot.CapturedAt.ToLocalTime():g}";
        SetControlPlaneState(
            OpsSeverity.Healthy,
            "ONLINE",
            "Native Linux provider");

        PopulateDashboard();
        PopulateIntelligence();
        PopulateLifecycle();
        PopulateHistory();
        PopulateServerPage();
        ApplyMediaFilter();
        ApplyServicesFilter();
        ApplyDockerFilter();
        ApplyStorageFilter();
        PopulateLogs();
        PopulateBackups();
        UpdateActionButtons();
    }

    private void PopulateDashboard()
    {
        var snapshot = _snapshot!;
        var analysis = _analysis!;
        var storage = LinuxOpsAnalyzer.OperationalStorage(snapshot);
        var services = LinuxOpsAnalyzer.UniqueServices(snapshot);

        Get<TextBlock>("DashboardHostnameText").Text = snapshot.Hostname;
        Get<TextBlock>("DashboardOsText").Text = snapshot.OperatingSystem;
        Get<TextBlock>("DashboardSystemText").Text = snapshot.SystemState;
        Get<TextBlock>("DashboardKernelText").Text = $"Kernel {snapshot.Kernel}";
        Get<TextBlock>("DashboardDockerText").Text = snapshot.DockerState;
        Get<TextBlock>("DashboardUptimeText").Text = snapshot.Uptime;
        Get<TextBlock>("DashboardCpuText").Text = snapshot.CpuModel;
        Get<TextBlock>("DashboardLoadText").Text = $"Load {snapshot.LoadAverage}";
        Get<TextBlock>("DashboardMemoryText").Text = snapshot.MemorySummary;
        Get<TextBlock>("DashboardIpText").Text = snapshot.IpAddresses;
        Get<TextBlock>("DashboardDiscoveryText").Text = $"{_integrations.Count} integrations · {snapshot.Containers.Count} containers";

        var findings = analysis.Findings
            .Where(item => item.Severity >= OpsSeverity.Warning)
            .Take(12)
            .ToArray();

        var errors = findings.Count(item =>
            item.Severity >= OpsSeverity.Error);
        var warnings = findings.Count(item =>
            item.Severity == OpsSeverity.Warning);

        Get<TextBlock>("DashboardFindingsSummaryText").Text =
            findings.Length == 0
                ? "No active findings"
                : $"{errors} error · {warnings} warning";

        Get<ListBox>("DashboardAttentionList").ItemsSource =
            findings.Length == 0
                ? new[]
                {
                    new OpsFinding(
                        OpsSeverity.Healthy,
                        "Environment",
                        "No active operational findings.",
                        "Latest capture completed successfully.",
                        "No impact detected.",
                        "Continue normal monitoring.",
                        0)
                }
                : findings;

        Get<TextBlock>("DashboardServicesModuleText").Text = services.Count.ToString();
        Get<TextBlock>("DashboardStorageModuleText").Text = storage.Count.ToString();
        Get<TextBlock>("DashboardMediaModuleText").Text = _integrations.Count.ToString();
    }

    private void PopulateIntelligence()
    {
        var analysis = _analysis!;
        var border = Get<Border>("IntelligenceSeverityBorder");
        var severity = Get<TextBlock>("IntelligenceSeverityText");
        border.Background = OpsPalette.Background(analysis.Severity);
        severity.Foreground = OpsPalette.Foreground(analysis.Severity);
        severity.Text = analysis.Label;
        Get<TextBlock>("IntelligenceRootCauseText").Text = analysis.RootCause;
        Get<TextBlock>("IntelligenceHeadlineText").Text = analysis.Headline;
        Get<TextBlock>("IntelligenceCountText").Text = $"{analysis.Findings.Count} finding(s)";
        Get<ListBox>("IntelligenceFindingsList").ItemsSource = analysis.Findings.Count == 0
            ? new[] { new OpsFinding(OpsSeverity.Healthy, "Environment", "No active findings.", "", "No impact detected.", "Continue normal monitoring.", 0) }
            : analysis.Findings;
    }

    private void PopulateLifecycle()
    {
        Get<ListBox>("LifecycleStagesList").ItemsSource = _lifecycle;
        var blocked = _lifecycle.Count(item => item.Severity >= OpsSeverity.Error);
        var warning = _lifecycle.Count(item => item.Severity == OpsSeverity.Warning);
        Get<TextBlock>("LifecycleSummaryText").Text = blocked > 0
            ? $"{blocked} blocked · {warning} attention"
            : warning > 0 ? $"{warning} stage(s) need attention" : "No active lifecycle blocker detected";
    }

    private void PopulateHistory()
    {
        Get<ListBox>("HistoryList").ItemsSource =
            _history.Records;
    }

    private void PopulateServerPage()
    {
        var snapshot = _snapshot!;
        Get<TextBlock>("ServerIdentityText").Text = snapshot.Hostname;
        Get<TextBlock>("ServerOperatingSystemText").Text = snapshot.OperatingSystem;
        Get<TextBlock>("ServerKernelText").Text = $"Kernel {snapshot.Kernel}";
        Get<TextBlock>("ServerUptimeText").Text = snapshot.Uptime;
        Get<TextBlock>("ServerCpuText").Text = $"CPU · {snapshot.CpuModel}\nLoad · {snapshot.LoadAverage}";
        Get<TextBlock>("ServerMemoryText").Text = $"Memory · {snapshot.MemorySummary}";
        Get<TextBlock>("ServerNetworkText").Text = $"Addresses · {snapshot.IpAddresses}";
    }

    private void PopulateLogs()
    {
        Get<ListBox>("LogsList").ItemsSource = _logs;
        Get<TextBlock>("LogsSummaryText").Text = $"{_logs.Count} unique event group(s)";
        Get<TextBox>("LogDetailText").Text = _logs.FirstOrDefault() is { } first
            ? FormatLog(first)
            : "No warning-or-higher journal events were returned.";
    }

    private void PopulateBackups()
    {
        var backup = _backup!;
        var border = Get<Border>("BackupStateBorder");
        var state = Get<TextBlock>("BackupStateText");
        border.Background = OpsPalette.Background(backup.Severity);
        state.Foreground = OpsPalette.Foreground(backup.Severity);
        state.Text = backup.State;
        Get<TextBlock>("BackupProviderText").Text = backup.Provider;
        Get<TextBlock>("BackupSummaryText").Text = backup.Summary;
        Get<ListBox>("BackupEvidenceList").ItemsSource = backup.Evidence.Count == 0
            ? new[] { "No backup evidence was returned." }
            : backup.Evidence;
        Get<ListBox>("BackupUnitsList").ItemsSource = backup.Units;
        Get<ListBox>("BackupArtifactsList").ItemsSource = backup.Artifacts;
    }

    private void MediaFilterText_OnTextChanged(object? sender, TextChangedEventArgs e) => ApplyMediaFilter();
    private void ServicesFilterText_OnTextChanged(object? sender, TextChangedEventArgs e) => ApplyServicesFilter();
    private void DockerFilterText_OnTextChanged(object? sender, TextChangedEventArgs e) => ApplyDockerFilter();
    private void StorageFilterText_OnTextChanged(object? sender, TextChangedEventArgs e) => ApplyStorageFilter();

    private void ApplyMediaFilter()
    {
        var filter = Get<TextBox>("MediaFilterText").Text?.Trim();
        Get<ListBox>("IntegrationsList").ItemsSource = _integrations
            .Where(item => Matches(filter, item.Name, item.Kind, item.State, item.Evidence, item.Endpoint))
            .ToArray();
    }

    private void ApplyServicesFilter()
    {
        if (_snapshot is null) return;
        var filter = Get<TextBox>("ServicesFilterText").Text?.Trim();
        var rows = LinuxOpsAnalyzer.UniqueServices(_snapshot)
            .Where(item => Matches(filter, item.Unit, item.Description, item.ActiveState, item.SubState, item.UnitFileState))
            .ToArray();
        Get<ListBox>("ServicesList").ItemsSource = rows;
        Get<TextBlock>("ServicesSummaryText").Text = $"{rows.Length} shown · {_snapshot.FailedUnits.Count} failed";
    }

    private void ApplyDockerFilter()
    {
        if (_snapshot is null) return;
        var filter = Get<TextBox>("DockerFilterText").Text?.Trim();
        var rows = _snapshot.Containers
            .Where(item => Matches(filter, item.Name, item.Image, item.State, item.Status, item.Ports))
            .ToArray();
        Get<ListBox>("DockerList").ItemsSource = rows;
        var running = rows.Count(item => item.State.Equals("running", StringComparison.OrdinalIgnoreCase));
        Get<TextBlock>("DockerSummaryText").Text = $"{running} running · {rows.Length} shown";
    }

    private void ApplyStorageFilter()
    {
        if (_snapshot is null) return;
        var filter = Get<TextBox>("StorageFilterText").Text?.Trim();
        var rows = LinuxOpsAnalyzer.OperationalStorage(_snapshot)
            .Where(item => Matches(filter, item.Source, item.FileSystem, item.MountPoint, item.PercentUsed))
            .ToArray();
        Get<ListBox>("StorageList").ItemsSource = rows;
        var attention = rows.Count(item => LinuxOpsAnalyzer.StorageSeverity(LinuxOpsAnalyzer.UsePercent(item.PercentUsed)) >= OpsSeverity.Warning);
        Get<TextBlock>("StorageSummaryText").Text = $"{rows.Length} shown · {attention} capacity finding(s)";
    }

    private void ServicesList_OnSelectionChanged(object? sender, SelectionChangedEventArgs e) => UpdateActionButtons();
    private void DockerList_OnSelectionChanged(object? sender, SelectionChangedEventArgs e) => UpdateActionButtons();
    private void SafeModeCheckBox_OnClick(object? sender, RoutedEventArgs e) => UpdateActionButtons();

    private void UpdateActionButtons()
    {
        var service = Get<ListBox>("ServicesList").SelectedItem is ServiceSnapshot;
        var container = Get<ListBox>("DockerList").SelectedItem is DockerContainerSnapshot;
        var safe = Get<CheckBox>("SafeModeCheckBox").IsChecked == true;

        Get<Button>("ServiceStartButton").IsEnabled = service;
        Get<Button>("ServiceStopButton").IsEnabled = service && !safe;
        Get<Button>("ServiceRestartButton").IsEnabled = service && !safe;
        Get<Button>("DockerStartButton").IsEnabled = container;
        Get<Button>("DockerStopButton").IsEnabled = container && !safe;
        Get<Button>("DockerRestartButton").IsEnabled = container && !safe;
    }

    private async void ServiceStartButton_OnClick(object? sender, RoutedEventArgs e) => await RunServiceActionAsync("start");
    private async void ServiceStopButton_OnClick(object? sender, RoutedEventArgs e) => await RunServiceActionAsync("stop");
    private async void ServiceRestartButton_OnClick(object? sender, RoutedEventArgs e) => await RunServiceActionAsync("restart");
    private async void DockerStartButton_OnClick(object? sender, RoutedEventArgs e) => await RunContainerActionAsync("start");
    private async void DockerStopButton_OnClick(object? sender, RoutedEventArgs e) => await RunContainerActionAsync("stop");
    private async void DockerRestartButton_OnClick(object? sender, RoutedEventArgs e) => await RunContainerActionAsync("restart");

    private async Task RunServiceActionAsync(string action)
    {
        if (Get<ListBox>("ServicesList").SelectedItem is not ServiceSnapshot service)
            return;
        if (BlockedBySafeMode(action))
        {
            Get<TextBlock>("ServiceActionStatusText").Text = "Disable Safe Mode to stop or restart services.";
            return;
        }
        if (!await ConfirmActionAsync($"{action} {service.Unit}?",
                action == "start" ? "Start this systemd service?" : "This can interrupt dependent applications. Continue only after reviewing current findings."))
            return;

        Get<TextBlock>("ServiceActionStatusText").Text = $"{action} in progress...";
        var result = await _actions.ServiceAsync(service.Unit, action);
        _history.RecordAction(service.Unit, action, result);
        Get<TextBlock>("ServiceActionStatusText").Text = result.Summary;
        await RefreshAsync();
    }

    private async Task RunContainerActionAsync(string action)
    {
        if (Get<ListBox>("DockerList").SelectedItem is not DockerContainerSnapshot container)
            return;
        if (BlockedBySafeMode(action))
        {
            Get<TextBlock>("DockerActionStatusText").Text = "Disable Safe Mode to stop or restart containers.";
            return;
        }
        if (!await ConfirmActionAsync($"{action} {container.Name}?",
                action == "start" ? "Start this Docker container?" : "This can interrupt dependent applications. Continue only after reviewing storage, network and upstream dependencies."))
            return;

        Get<TextBlock>("DockerActionStatusText").Text = $"{action} in progress...";
        var result = await _actions.ContainerAsync(container.Name, action);
        _history.RecordAction(container.Name, action, result);
        Get<TextBlock>("DockerActionStatusText").Text = result.Summary;
        await RefreshAsync();
    }

    private bool BlockedBySafeMode(string action) =>
        (action is "stop" or "restart") &&
        Get<CheckBox>("SafeModeCheckBox").IsChecked == true;

    private async void ClearHistoryButton_OnClick(object? sender, RoutedEventArgs e)
    {
        if (!await ConfirmActionAsync("Clear Linux history?",
                "This removes local bounded transition history. It does not modify system logs or host state."))
            return;
        _history.Clear();
        Get<ListBox>("HistoryList").ItemsSource = _history.Records;
    }

    private void LogsList_OnSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (Get<ListBox>("LogsList").SelectedItem is OpsLogGroup log)
            Get<TextBox>("LogDetailText").Text = FormatLog(log);
    }

    private async Task<bool> ConfirmActionAsync(string title, string message)
    {
        var dialog = new Window
        {
            Title = title,
            Width = 500,
            Height = 250,
            CanResize = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Background = new SolidColorBrush(Color.Parse("#111113"))
        };

        var confirm = new Button { Content = "Confirm" };
        confirm.Classes.Add("primary");
        var cancel = new Button { Content = "Cancel" };
        confirm.Click += (_, _) => dialog.Close(true);
        cancel.Click += (_, _) => dialog.Close(false);

        dialog.Content = new Border
        {
            Padding = new Thickness(24),
            Child = new StackPanel
            {
                Spacing = 16,
                Children =
                {
                    new TextBlock { Text = title, FontSize = 20, FontWeight = FontWeight.SemiBold },
                    new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap, Foreground = new SolidColorBrush(Color.Parse("#A7A1A7")) },
                    new Border { Height = 1, Background = new SolidColorBrush(Color.Parse("#303036")) },
                    new StackPanel
                    {
                        Orientation = Orientation.Horizontal,
                        HorizontalAlignment = HorizontalAlignment.Right,
                        Spacing = 10,
                        Children = { cancel, confirm }
                    }
                }
            }
        };

        return await dialog.ShowDialog<bool>(this);
    }

    private static string FormatLog(OpsLogGroup log) =>
        $"Severity: {log.Severity}\nSource: {log.Source}\nLast seen: {log.LastSeen.LocalDateTime:g}\nOccurrences: {log.Count}\n\n{log.Message}";

    private void SetControlPlaneState(
        OpsSeverity severity,
        string state,
        string detail)
    {
        var brush = OpsPalette.Foreground(severity);
        Get<TextBlock>("ConnectionText").Text = state;
        Get<TextBlock>("ConnectionText").Foreground = brush;
        Get<TextBlock>("ConnectionDot").Foreground = brush;
        Get<TextBlock>("ConnectionDetailText").Text = detail;
    }

    private static bool Matches(string? filter, params string?[] values)
    {
        if (string.IsNullOrWhiteSpace(filter)) return true;
        return values.Any(value => value?.Contains(filter, StringComparison.OrdinalIgnoreCase) == true);
    }

    private sealed record NavigationTarget(string PageName, string Title, string Subtitle);
}

public static class OpsPalette
{
    public static IBrush Foreground(OpsSeverity severity) =>
        new SolidColorBrush(Color.Parse(severity switch
        {
            OpsSeverity.Healthy => "#63CC8B",
            OpsSeverity.Warning => "#E0B24F",
            OpsSeverity.Error or OpsSeverity.Critical => "#E16B75",
            _ => "#B98BA8"
        }));

    public static IBrush Background(OpsSeverity severity) =>
        new SolidColorBrush(Color.Parse(severity switch
        {
            OpsSeverity.Healthy => "#14291F",
            OpsSeverity.Warning => "#2B2517",
            OpsSeverity.Error or OpsSeverity.Critical => "#2D181C",
            _ => "#2B222A"
        }));
}
