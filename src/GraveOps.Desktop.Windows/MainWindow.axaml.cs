using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using GraveOps.Core.Hosts;
using GraveOps.Platform.Windows;

namespace GraveOps.Desktop.Windows;

public partial class MainWindow : Window
{
    private static readonly IReadOnlyDictionary<string, NavigationTarget> Navigation =
        new Dictionary<string, NavigationTarget>(StringComparer.Ordinal)
        {
            ["DashboardNav"] = new("DashboardPage", "Dashboard",
                "Interactive environment health, ownership and active-host operations"),
            ["IntelligenceNav"] = new("WarningsPage", "Intelligence",
                "Fleet impact, root cause, evidence and contextual next actions"),
            ["LifecycleNav"] = new("ParityPage", "Media Lifecycle",
                "Track active media across acquisition, download, import and library stages"),
            ["HistoryNav"] = new("ParityPage", "History & Incidents",
                "Health transitions, GraveOps activity and incident replay"),
            ["ServersNav"] = new("ParityPage", "Servers",
                "Local and remote host profiles, capabilities and secure connections"),
            ["MediaHubNav"] = new("IntegrationsPage", "Media Hub",
                "Fleet health, discovery and all media applications"),
            ["LogsNav"] = new("WarningsPage", "Logs",
                "Grouped warnings, provider output and crash evidence"),
            ["BackupsNav"] = new("ParityPage", "Backups",
                "Schedule, artifact and restore-readiness evidence"),
            ["SettingsNav"] = new("ParityPage", "Settings",
                "Windows paths, provider state and parity configuration"),
            ["ToolsNav"] = new("ParityPage", "Operator Tools",
                "Redacted diagnostics, validation and safe local access"),
            ["ServicesNav"] = new("ServicesPage", "Services",
                "Cataloged Windows service inventory and state"),
            ["DockerNav"] = new("DockerPage", "Docker",
                "Container runtime and discovered workload state"),
            ["StorageNav"] = new("StoragePage", "Storage",
                "Ready Windows volumes and capacity evidence"),
            ["IntegrationsNav"] = new("IntegrationsPage", "Integrations",
                "Detected applications, endpoints and discovery evidence"),
            ["WarningsNav"] = new("WarningsPage", "Warnings",
                "Capture limitations and read-only operational findings")
        };

    private readonly ILocalHostProbe _hostProbe = new LocalWindowsHostProbe();
    private readonly List<ActivityRow> _activity = new();
    private CancellationTokenSource? _refreshCancellation;
    private HostSnapshot? _snapshot;

    public MainWindow()
    {
        InitializeComponent();
        InitializeLinuxShellParity();

        Opened += async (_, _) =>
        {
            Navigate("DashboardNav");
            RecordActivity("Preview opened", "Windows Avalonia Phase 2 shell initialized.");
            await RefreshAsync();
        };
    }

    protected override void OnClosed(EventArgs e)
    {
        _refreshCancellation?.Cancel();
        _refreshCancellation?.Dispose();
        base.OnClosed(e);
    }

    private T Get<T>(string name) where T : Control =>
        this.FindControl<T>(name) ??
        throw new InvalidOperationException($"Required control '{name}' was not found.");

    private void TitleDragRegion_OnPointerPressed(
        object? sender,
        PointerPressedEventArgs e)
    {
        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
            BeginMoveDrag(e);
    }

    private void TitleDragRegion_OnDoubleTapped(
        object? sender,
        TappedEventArgs e) =>
        ToggleMaximized();

    private void Minimize_Click(
        object? sender,
        RoutedEventArgs e) =>
        WindowState = WindowState.Minimized;

    private void Maximize_Click(
        object? sender,
        RoutedEventArgs e) =>
        ToggleMaximized();

    private void Close_Click(
        object? sender,
        RoutedEventArgs e) =>
        Close();

    private void ToggleMaximized() =>
        WindowState =
            WindowState == WindowState.Maximized
                ? WindowState.Normal
                : WindowState.Maximized;

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

    private void Navigate(string navigationName)
    {
        if (!Navigation.TryGetValue(navigationName, out var target))
            return;

        foreach (var pageName in Navigation.Values
                     .Select(item => item.PageName)
                     .Distinct(StringComparer.Ordinal))
        {
            Get<Control>(pageName).IsVisible = false;
        }

        Get<Control>(target.PageName).IsVisible = true;

        foreach (var item in Navigation)
        {
            Get<Button>(item.Key).Classes.Set(
                "selected",
                item.Key == navigationName);
        }

        Get<TextBlock>("PageTitleText").Text = target.Title;
        Get<TextBlock>("PageSubtitleText").Text = target.Subtitle;
        UpdateLinuxParityPage(target);

        RecordActivity(
            "Navigation",
            $"Opened {target.Title}.");
    }

    private async void RefreshButton_OnClick(
        object? sender,
        RoutedEventArgs e) =>
        await RefreshAsync();

    private async Task RefreshAsync()
    {
        _refreshCancellation?.Cancel();
        _refreshCancellation?.Dispose();
        _refreshCancellation = new CancellationTokenSource();

        var cancellationToken = _refreshCancellation.Token;
        var refreshButton = Get<Button>("RefreshButton");

        refreshButton.IsEnabled = false;
        SetConnectionState(
            "CAPTURING",
            "Native Windows provider",
            isHealthy: false);

        SetText(
            "CaptureStatusText",
            "Capturing the local Windows host snapshot...");

        try
        {
            var snapshot = await _hostProbe.CaptureAsync(cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();

            _snapshot = snapshot;
            PopulateSnapshot(snapshot);

            RecordActivity(
                "Snapshot captured",
                $"{snapshot.Storage.Count} volume(s), " +
                $"{snapshot.Services.Count} service(s), " +
                $"{snapshot.Containers.Count} container(s), " +
                $"{snapshot.Integrations.Count} integration(s).");
        }
        catch (OperationCanceledException)
        {
            SetText(
                "CaptureStatusText",
                "Snapshot refresh was replaced by a newer request.");
        }
        catch (Exception exception)
        {
            SetConnectionState(
                "FAILED",
                exception.Message,
                isHealthy: false,
                isFailure: true);

            SetText(
                "CaptureStatusText",
                "Snapshot failed: " + exception.Message);

            SetList(
                "WarningsList",
                new[] { exception.ToString() });

            RecordActivity(
                "Snapshot failed",
                exception.Message);
        }
        finally
        {
            refreshButton.IsEnabled = true;
        }
    }

    private void PopulateSnapshot(HostSnapshot snapshot)
    {
        var recommendations = BuildRecommendations(snapshot);
        var health = EvaluateHealth(snapshot, recommendations);

        SetConnectionState(
            "READY",
            "Native Windows provider",
            isHealthy: true);

        SetText("SidebarHostname", snapshot.Hostname);
        SetText(
            "SidebarOperatingSystem",
            $"{snapshot.OperatingSystem} Ã‚| {snapshot.Kernel}");

        SetText(
            "LastUpdatedText",
            snapshot.CapturedAt.ToLocalTime().ToString("g"));
        SetText(
            "DashboardCaptureAgeText",
            $"Captured {snapshot.CapturedAt.ToLocalTime():g}");
        SetText(
            "CaptureStatusText",
            $"Snapshot captured {snapshot.CapturedAt.ToLocalTime():g}. " +
            $"{snapshot.Storage.Count} volume(s), " +
            $"{snapshot.Services.Count} service(s), " +
            $"{snapshot.Containers.Count} container(s), " +
            $"{snapshot.Integrations.Count} integration(s).");

        SetText("DashboardConnectionText", "READY");
        SetText(
            "DashboardConnectionSummaryText",
            string.IsNullOrWhiteSpace(snapshot.SystemState)
                ? "Windows host snapshot available"
                : snapshot.SystemState);

        SetText(
            "DashboardAttentionCountText",
            recommendations.Count.ToString(CultureInfo.InvariantCulture));
        SetText(
            "DashboardHealthSummaryText",
            recommendations.Count == 0
                ? "No actionable read-only findings"
                : $"{health.Warn + health.Fail} item(s) need review");

        SetText(
            "DashboardStorageCountText",
            snapshot.Storage.Count.ToString(CultureInfo.InvariantCulture));
        SetText(
            "DashboardStorageSummaryText",
            BuildStorageSummary(snapshot.Storage));

        SetText(
            "DashboardDockerText",
            NormalizeDisplay(snapshot.DockerState));
        SetText(
            "DashboardDockerSummaryText",
            snapshot.Containers.Count == 0
                ? "No running containers reported"
                : $"{snapshot.Containers.Count} container(s) reported");

        SetText("DashboardHostnameText", snapshot.Hostname);
        SetText(
            "DashboardOsText",
            $"{snapshot.OperatingSystem} | kernel {snapshot.Kernel}");
        SetText("DashboardAddressText", snapshot.IpAddresses);
        SetText("DashboardUptimeText", snapshot.Uptime);
        SetText("DashboardMemoryText", snapshot.MemorySummary);

        SetText(
            "DashboardServiceCountText",
            snapshot.Services.Count.ToString(CultureInfo.InvariantCulture));
        SetText(
            "DashboardContainerCountText",
            snapshot.Containers.Count.ToString(CultureInfo.InvariantCulture));
        SetText(
            "DashboardIntegrationCountText",
            snapshot.Integrations.Count.ToString(CultureInfo.InvariantCulture));
        SetText(
            "DashboardWarningCountText",
            snapshot.Warnings.Count.ToString(CultureInfo.InvariantCulture));
        SetText(
            "DashboardSystemStateText",
            NormalizeDisplay(snapshot.SystemState));

        SetText(
            "DashboardHealthPassText",
            health.Pass.ToString(CultureInfo.InvariantCulture));
        SetText(
            "DashboardHealthWarnText",
            health.Warn.ToString(CultureInfo.InvariantCulture));
        SetText(
            "DashboardHealthFailText",
            health.Fail.ToString(CultureInfo.InvariantCulture));

        SetList("DashboardRecommendationsList", recommendations);
        Get<Border>("DashboardRecommendationsEmptyPanel").IsVisible =
            recommendations.Count == 0;
        Get<ListBox>("DashboardRecommendationsList").IsVisible =
            recommendations.Count > 0;

        SetList("DashboardStorageList", snapshot.Storage);
        SetList("StorageList", snapshot.Storage);
        SetList("ServicesList", snapshot.Services);
        SetList("ContainersList", snapshot.Containers);
        SetList("IntegrationsList", snapshot.Integrations);

        var warnings = snapshot.Warnings.Count == 0
            ? new[] { "No provider warnings." }
            : snapshot.Warnings;
        SetList("WarningsList", warnings);

        SetText(
            "DockerPageSummaryText",
            $"{NormalizeDisplay(snapshot.DockerState)} Ã‚| " +
            $"{snapshot.Containers.Count} container(s)");

        SetText(
            "FooterStatusText",
            $"Windows Avalonia Phase 2 Ã‚| {snapshot.Hostname} Ã‚| read-only");

        PopulateLinuxShellParity(snapshot);
        PopulateActivity();
    }

    private static IReadOnlyList<RecommendationRow> BuildRecommendations(
        HostSnapshot snapshot)
    {
        var rows = new List<RecommendationRow>();

        foreach (var warning in snapshot.Warnings)
        {
            rows.Add(new RecommendationRow(
                "WARN",
                "Provider",
                warning,
                "capture"));
        }

        foreach (var failedUnit in snapshot.FailedUnits)
        {
            rows.Add(new RecommendationRow(
                "FAIL",
                "Service",
                $"{failedUnit} is reported failed.",
                failedUnit));
        }

        foreach (var volume in snapshot.Storage)
        {
            var percent = ParsePercent(volume.PercentUsed);

            if (percent >= 95)
            {
                rows.Add(new RecommendationRow(
                    "FAIL",
                    "Storage",
                    $"{volume.MountPoint} is critically full at {volume.PercentUsed}.",
                    volume.Source));
            }
            else if (percent >= 85)
            {
                rows.Add(new RecommendationRow(
                    "WARN",
                    "Storage",
                    $"{volume.MountPoint} is above the review threshold at {volume.PercentUsed}.",
                    volume.Source));
            }
        }

        foreach (var service in snapshot.Services.Where(service =>
                     !IsHealthyState(service.ActiveState)))
        {
            rows.Add(new RecommendationRow(
                "WARN",
                "Service",
                $"{service.Unit} is {service.ActiveState}.",
                service.UnitFileState));
        }

        foreach (var container in snapshot.Containers.Where(container =>
                     !IsHealthyState(container.State)))
        {
            rows.Add(new RecommendationRow(
                "WARN",
                "Docker",
                $"{container.Name} is {container.State}.",
                container.Image));
        }

        return rows
            .OrderByDescending(row => row.Severity == "FAIL")
            .ThenBy(row => row.Component, StringComparer.OrdinalIgnoreCase)
            .ThenBy(row => row.Message, StringComparer.OrdinalIgnoreCase)
            .Take(30)
            .ToArray();
    }

    private static HealthSummary EvaluateHealth(
        HostSnapshot snapshot,
        IReadOnlyList<RecommendationRow> recommendations)
    {
        var fail = recommendations.Count(row =>
            row.Severity.Equals("FAIL", StringComparison.Ordinal));
        var warn = recommendations.Count - fail;

        var pass = 1;

        pass += snapshot.Storage.Count(volume =>
            ParsePercent(volume.PercentUsed) < 85);
        pass += snapshot.Services.Count(service =>
            IsHealthyState(service.ActiveState));
        pass += snapshot.Containers.Count(container =>
            IsHealthyState(container.State));
        pass += snapshot.Integrations.Count(integration =>
            IsHealthyState(integration.State));

        return new HealthSummary(pass, warn, fail);
    }

    private static string BuildStorageSummary(
        IReadOnlyList<StorageVolumeSnapshot> storage)
    {
        if (storage.Count == 0)
            return "No ready volumes reported";

        var highest = storage
            .Select(volume => new
            {
                Volume = volume,
                Percent = ParsePercent(volume.PercentUsed)
            })
            .OrderByDescending(item => item.Percent)
            .First();

        return $"{highest.Volume.MountPoint} highest at " +
               $"{highest.Volume.PercentUsed}";
    }

    private static double ParsePercent(string value)
    {
        var cleaned = new string(value
            .Where(character =>
                char.IsDigit(character) ||
                character == '.' ||
                character == '-')
            .ToArray());

        return double.TryParse(
            cleaned,
            NumberStyles.Float,
            CultureInfo.InvariantCulture,
            out var parsed)
                ? parsed
                : 0;
    }

    private static bool IsHealthyState(string state)
    {
        if (string.IsNullOrWhiteSpace(state))
            return false;

        return state.Contains("running", StringComparison.OrdinalIgnoreCase) ||
               state.Contains("active", StringComparison.OrdinalIgnoreCase) ||
               state.Contains("available", StringComparison.OrdinalIgnoreCase) ||
               state.Contains("ready", StringComparison.OrdinalIgnoreCase) ||
               state.Contains("online", StringComparison.OrdinalIgnoreCase) ||
               state.Contains("healthy", StringComparison.OrdinalIgnoreCase);
    }

    private void SetConnectionState(
        string state,
        string detail,
        bool isHealthy,
        bool isFailure = false)
    {
        var colorKey = isHealthy
            ? "SuccessBrush"
            : isFailure
                ? "DangerBrush"
                : "WarnBrush";

        var brush = Application.Current?
            .TryFindResource(colorKey, ActualThemeVariant, out var resource) == true
                ? resource as IBrush
                : null;

        SetText("ConnectionText", state);
        SetText("ConnectionDetailText", detail);
        Get<TextBlock>("ConnectionText").Foreground = brush;
        Get<TextBlock>("ConnectionDot").Foreground = brush;
    }

    private void RecordActivity(
        string title,
        string detail)
    {
        _activity.Insert(
            0,
            new ActivityRow(
                DateTimeOffset.Now.ToString("t"),
                title,
                detail));

        if (_activity.Count > 12)
            _activity.RemoveRange(12, _activity.Count - 12);

        PopulateActivity();
    }

    private void PopulateActivity() =>
        SetList(
            "DashboardActivityList",
            _activity.Take(7).ToArray());

    private void SetText(
        string name,
        string value) =>
        Get<TextBlock>(name).Text =
            string.IsNullOrWhiteSpace(value)
                ? "--"
                : value;

    private void SetList(
        string name,
        object source) =>
        Get<ListBox>(name).ItemsSource = source as System.Collections.IEnumerable;

    private static string NormalizeDisplay(string value) =>
        string.IsNullOrWhiteSpace(value)
            ? "Unavailable"
            : value.Trim();

    private sealed record NavigationTarget(
        string PageName,
        string Title,
        string Subtitle);

    private sealed record RecommendationRow(
        string Severity,
        string Component,
        string Message,
        string Evidence);

    private sealed record ActivityRow(
        string Time,
        string Title,
        string Detail);

    private sealed record HealthSummary(
        int Pass,
        int Warn,
        int Fail);
}
