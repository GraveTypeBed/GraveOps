using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Threading;
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
    private readonly LinuxFindingPolicyStore _findingPolicies = new();
    private readonly ArrWorkspaceProfileStore _arrWorkspaceProfiles = new();
    private readonly ArrLiveTelemetryService _arrTelemetry = new();
    private readonly LinuxOperatorSettingsStore _operatorSettingsStore = new();
    private readonly string _repositoryPath =
        LinuxOperatorTools.FindRepositoryRoot();
    private LinuxOperatorSettings _operatorSettings =
        LinuxOperatorSettings.Default;

    private static readonly IReadOnlyDictionary<string, int[]> KnownIntegrationPorts =
        new Dictionary<string, int[]>(StringComparer.OrdinalIgnoreCase)
        {
            ["DUMB"] = new[] { 3005 },
            ["Plex"] = new[] { 32400 },
            ["Sonarr"] = new[] { 8989, 8990 },
            ["Radarr"] = new[] { 7878, 7879 },
            ["Lidarr"] = new[] { 8686 },
            ["Prowlarr"] = new[] { 9696 },
            ["Readarr"] = new[] { 8787 },
            ["Whisparr"] = new[] { 6969 },
            ["Mylar3"] = new[] { 8090 },
            ["SABnzbd"] = new[] { 8080 },
            ["qBittorrent"] = new[] { 8081 },
            ["Decypharr"] = new[] { 8282 },
            ["Zurg"] = new[] { 18080 },
            ["Tautulli"] = new[] { 8181 },
            ["Bazarr"] = new[] { 6767 },
            ["Seerr"] = new[] { 5055 },
            ["FlareSolverr"] = new[] { 8191 }
        };

    private static readonly IReadOnlyDictionary<string, string>
        IntegrationNavigationTargets =
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["DumbNav"] = "DUMB",
                ["PlexNav"] = "Plex",
                ["TautulliNav"] = "Tautulli",
                ["KometaNav"] = "Kometa",
                ["SonarrNav"] = "Sonarr",
                ["RadarrNav"] = "Radarr",
                ["LidarrNav"] = "Lidarr",
                ["ProwlarrNav"] = "Prowlarr",
                ["ReadarrNav"] = "Readarr",
                ["WhisparrNav"] = "Whisparr",
                ["Mylar3Nav"] = "Mylar3",
                ["BazarrNav"] = "Bazarr",
                ["SabnzbdNav"] = "SABnzbd",
                ["QBittorrentNav"] = "qBittorrent",
                ["DecypharrNav"] = "Decypharr",
                ["RecyclarrNav"] = "Recyclarr",
                ["ZurgNav"] = "Zurg"
            };

    private readonly IReadOnlyDictionary<string, NavigationTarget> _navigation =
        new Dictionary<string, NavigationTarget>(StringComparer.Ordinal)
        {
            ["DashboardNav"] = new("DashboardPage", "Dashboard", "Interactive environment health, ownership and active-host operations"),
            ["IntelligenceNav"] = new("IntelligencePage", "Intelligence", "Fleet impact, root cause, dependencies and contextual next actions"),
            ["LifecycleNav"] = new("LifecyclePage", "Media Lifecycle", "Track active media across acquisition, download, import, processing and library stages"),
            ["HistoryNav"] = new("HistoryPage", "History & Incidents", "Fleet health transitions, GraveOps activity and incident replay"),
            ["ServersNav"] = new("ServersPage", "Servers", "Local and remote host profiles, capabilities and secure credentials"),
            ["MediaHubNav"] = new("MediaHubPage", "Media Hub", "Fleet health, launcher configuration and all media applications"),
            ["DumbNav"] = new("ApplicationWorkspacePage", "DUMB", "Stack orchestration and verified local interface"),
            ["PlexNav"] = new("PlexWorkspacePage", "Plex", "Library availability, live sessions, playback decisions and guarded operations"),
            ["TautulliNav"] = new("ApplicationWorkspacePage", "Tautulli", "Playback analytics and related findings"),
            ["KometaNav"] = new("ApplicationWorkspacePage", "Kometa", "Library metadata automation and related findings"),
            ["SonarrNav"] = new("ArrWorkspacePage", "Sonarr", "Configurable television acquisition workspace"),
            ["RadarrNav"] = new("ArrWorkspacePage", "Radarr", "Configurable movie acquisition workspace"),
            ["LidarrNav"] = new("ArrWorkspacePage", "Lidarr", "Configurable music acquisition workspace"),
            ["ProwlarrNav"] = new("ArrWorkspacePage", "Prowlarr", "Configurable indexer and application-sync workspace"),
            ["ReadarrNav"] = new("ArrWorkspacePage", "Readarr", "Configurable book and audiobook workspace"),
            ["WhisparrNav"] = new("ArrWorkspacePage", "Whisparr", "Version-aware configurable acquisition workspace"),
            ["Mylar3Nav"] = new("ArrWorkspacePage", "Mylar3", "Configurable comic acquisition workspace"),
            ["BazarrNav"] = new("ArrWorkspacePage", "Bazarr", "Configurable subtitle coverage workspace"),
            ["SabnzbdNav"] = new("DownloadClientWorkspacePage", "SABnzbd", "Usenet queue analytics, progress, history and explicit operations"),
            ["QBittorrentNav"] = new("DownloadClientWorkspacePage", "qBittorrent", "Torrent transfer analytics, progress, seeding and explicit operations"),
            ["DecypharrNav"] = new("ApplicationWorkspacePage", "Decypharr", "Debrid processing and related findings"),
            ["RecyclarrNav"] = new("RecyclarrWorkspacePage", "Recyclarr", "Container runtime, configuration targets, read-only preview and synchronization evidence"),
            ["ZurgNav"] = new("ApplicationWorkspacePage", "Zurg", "Debrid mount availability and related findings"),
            ["ServicesNav"] = new("ServicesPage", "Services & Actions", "Native systemd inventory and guarded actions"),
            ["DockerNav"] = new("DockerPage", "Docker", "Containers, images, state, ports and guarded actions"),
            ["StorageNav"] = new("StoragePage", "Storage", "Operational filesystems and capacity health"),
            ["LogsNav"] = new("LogsPage", "Logs", "Grouped warning journal and crash evidence"),
            ["BackupsNav"] = new("BackupsPage", "Backups", "Schedule, artifact and restore-readiness evidence"),
            ["SettingsNav"] = new("SettingsPage", "Settings", "Linux paths, operator defaults, policies and version state"),
            ["ToolsNav"] = new("ToolsPage", "Operator Tools", "Redacted diagnostics, validation and safe local access")
        };

    private HostSnapshot? _snapshot;
    private OpsBackupSnapshot? _backup;
    private OpsAnalysis? _rawAnalysis;
    private OpsAnalysis? _analysis;
    private IReadOnlyList<OpsLifecycleStage> _rawLifecycle = Array.Empty<OpsLifecycleStage>();
    private IReadOnlyList<OpsLifecycleStage> _lifecycle = Array.Empty<OpsLifecycleStage>();
    private IReadOnlyList<OpsIntegration> _integrations = Array.Empty<OpsIntegration>();
    private IReadOnlyList<OpsLogGroup> _logs = Array.Empty<OpsLogGroup>();
    private IReadOnlyList<ArrWorkspaceView> _arrWorkspaceRows =
        Array.Empty<ArrWorkspaceView>();
    private readonly DispatcherTimer _arrLiveTimer;
    private ArrLiveTelemetrySnapshot? _arrTelemetrySnapshot;
    private string _arrTelemetryProduct = string.Empty;
    private string _activeArrProduct = "Sonarr";
    private string _selectedArrInstanceKey = string.Empty;
    private bool _arrTelemetryBusy;
    private OpsPolicyEvaluation? _policyEvaluation;
    private IReadOnlyList<CommandPaletteItem> _commandPaletteItems =
        Array.Empty<CommandPaletteItem>();

    public MainWindow()
    {
        InitializeComponent();
        _operatorSettings = _operatorSettingsStore.Load();
        ApplyOperatorSettingsToUi();
        InitializeControlPlaneFoundation();
        InitializeDownloadClientWorkspace();
        InitializeMediaWorkspace();
        InitializePlexWorkspace();
        InitializeRecyclarrWorkspace();
        InitializeDockerWorkspace();

        _arrLiveTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(10)
        };
        _arrLiveTimer.Tick += async (_, _) =>
        {
            if (Get<Control>("ArrWorkspacePage").IsVisible)
                await RefreshArrLiveTelemetryAsync();
        };

        Opened += async (_, _) =>
        {
            Navigate("DashboardNav");
            await RefreshAsync();
            await RefreshVersionInfoAsync();
            _arrLiveTimer.Start();

            if (_operatorSettings.OpenOverviewAfterStartup)
            {
                Get<Border>("OverviewDrawer").IsVisible = true;
                PopulateOperatorShell();
            }
        };
    }

    protected override void OnClosed(EventArgs e)
    {
        _arrLiveTimer.Stop();
        _arrTelemetry.Dispose();
        DisposeControlPlaneFoundation();
        base.OnClosed(e);
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

    private void LibraryGroupButton_OnClick(
        object? sender,
        RoutedEventArgs e) =>
        ToggleNavigationGroup(
            "LibraryNavGroup",
            "LibraryGroupGlyph");

    private void AcquisitionGroupButton_OnClick(
        object? sender,
        RoutedEventArgs e) =>
        ToggleNavigationGroup(
            "AcquisitionNavGroup",
            "AcquisitionGroupGlyph");

    private void ProcessingGroupButton_OnClick(
        object? sender,
        RoutedEventArgs e) =>
        ToggleNavigationGroup(
            "ProcessingNavGroup",
            "ProcessingGroupGlyph");

    private void ToggleNavigationGroup(
        string panelName,
        string glyphName)
    {
        var panel =
            Get<StackPanel>(panelName);

        panel.IsVisible =
            !panel.IsVisible;

        Get<Avalonia.Controls.Shapes.Path>(
                glyphName)
            .Data =
            Geometry.Parse(
                panel.IsVisible
                    ? "M2,4 L6,8 L10,4"
                    : "M4,2 L8,6 L4,10");
    }

    private void Navigate(string navigationName)
    {
        if (!_navigation.TryGetValue(
                navigationName,
                out var target))
        {
            return;
        }

        foreach (var pageName in _navigation.Values
                     .Select(item => item.PageName)
                     .Distinct(StringComparer.Ordinal))
        {
            Get<Control>(pageName).IsVisible = false;
        }

        Get<Control>(target.PageName).IsVisible = true;

        foreach (var item in _navigation)
        {
            Get<Button>(item.Key).Classes.Set(
                "selected",
                item.Key == navigationName);
        }

        Get<TextBlock>("PageTitleText").Text = target.Title;
        Get<TextBlock>("PageSubtitleText").Text = target.Subtitle;

        _controlPlane.State.RecordActivity(
            "Navigation",
            _controlPlane.ActiveProfile.DisplayName,
            $"Opened {target.Title}",
            target.Subtitle,
            navigationName,
            unread: false);
        PopulateControlPlaneFoundation();

        if (target.PageName.Equals(
                "DockerPage",
                StringComparison.Ordinal))
        {
            ActivateDockerWorkspace();
        }

        if (IntegrationNavigationTargets.TryGetValue(
                navigationName,
                out var integrationName))
        {
            if (target.PageName.Equals(
                    "ArrWorkspacePage",
                    StringComparison.Ordinal))
            {
                ActivateArrProduct(integrationName);
            }
            else if (target.PageName.Equals(
                         "DownloadClientWorkspacePage",
                         StringComparison.Ordinal))
            {
                ActivateDownloadClient(integrationName);
            }
            else if (target.PageName.Equals(
                         "PlexWorkspacePage",
                         StringComparison.Ordinal))
            {
                ActivatePlexWorkspace();
            }
            else if (target.PageName.Equals(
                         "RecyclarrWorkspacePage",
                         StringComparison.Ordinal))
            {
                ActivateRecyclarrWorkspace();
            }
            else if (target.PageName.Equals(
                         "ApplicationWorkspacePage",
                         StringComparison.Ordinal))
            {
                ActivateDirectIntegration(integrationName);
            }
            else
            {
                SelectIntegrationByName(integrationName);
            }
        }
        CloseCommandPalette();

        if (navigationName is "SettingsNav" or "ToolsNav")
            PopulateSettingsAndTools();
    }

    private void SelectIntegrationByName(string integrationName)
    {
        if (_integrations.Count == 0)
            return;

        var filter = Get<TextBox>("MediaFilterText");

        if (!string.IsNullOrWhiteSpace(filter.Text))
            filter.Text = string.Empty;

        SelectMediaIntegrationByName(
            integrationName);
    }

    private void UpdateIntegrationNavigation()
    {
        bool Detected(string name) =>
            _integrations.Any(item =>
                item.Name.Equals(
                    name,
                    StringComparison.OrdinalIgnoreCase));

        void SetButton(string buttonName, string integrationName) =>
            Get<Button>(buttonName).IsVisible =
                Detected(integrationName);

        SetButton("DumbNav", "DUMB");
        SetButton("PlexNav", "Plex");
        SetButton("TautulliNav", "Tautulli");
        SetButton("KometaNav", "Kometa");
        SetButton("SonarrNav", "Sonarr");
        SetButton("RadarrNav", "Radarr");
        SetButton("LidarrNav", "Lidarr");
        SetButton("ProwlarrNav", "Prowlarr");
        SetButton("ReadarrNav", "Readarr");
        SetButton("WhisparrNav", "Whisparr");
        SetButton("Mylar3Nav", "Mylar3");
        SetButton("BazarrNav", "Bazarr");
        SetButton("SabnzbdNav", "SABnzbd");
        SetButton("QBittorrentNav", "qBittorrent");
        SetButton("DecypharrNav", "Decypharr");
        SetButton("RecyclarrNav", "Recyclarr");
        SetButton("ZurgNav", "Zurg");

        var libraryVisible =
            Detected("Plex") ||
            Detected("Tautulli") ||
            Detected("Kometa");
        var acquisitionVisible =
            Detected("Sonarr") ||
            Detected("Radarr") ||
            Detected("Lidarr") ||
            Detected("Prowlarr") ||
            Detected("Readarr") ||
            Detected("Whisparr") ||
            Detected("Mylar3") ||
            Detected("SABnzbd") ||
            Detected("qBittorrent");
        var processingVisible =
            Detected("Decypharr") ||
            Detected("Recyclarr") ||
            Detected("Bazarr") ||
            Detected("Configarr") ||
            Detected("Profilarr") ||
            Detected("Cleanuparr") ||
            Detected("Maintainerr") ||
            Detected("Unpackerr") ||
            Detected("Zurg");

        Get<Button>("LibraryGroupButton").IsVisible =
            libraryVisible;
        Get<StackPanel>("LibraryNavGroup").IsVisible =
            libraryVisible;
        Get<Button>("AcquisitionGroupButton").IsVisible =
            acquisitionVisible;
        Get<StackPanel>("AcquisitionNavGroup").IsVisible =
            acquisitionVisible;
        Get<Button>("ProcessingGroupButton").IsVisible =
            processingVisible;
        Get<StackPanel>("ProcessingNavGroup").IsVisible =
            processingVisible;
    }

    private void MainWindow_OnKeyDown(
        object? sender,
        KeyEventArgs e)
    {
        if (e.Key == Key.K &&
            e.KeyModifiers.HasFlag(KeyModifiers.Control))
        {
            ToggleCommandPalette();
            e.Handled = true;
            return;
        }

        if (e.Key == Key.Escape)
        {
            CloseCommandPalette();
            CloseControlPlaneDrawers();
            e.Handled = true;
        }
    }

    private void CommandPaletteButton_OnClick(
        object? sender,
        RoutedEventArgs e) =>
        ToggleCommandPalette();

    private void OverviewButton_OnClick(
        object? sender,
        RoutedEventArgs e)
    {
        CloseCommandPalette();
        CloseControlPlaneDrawers(
            except: "OverviewDrawer");
        var drawer = Get<Border>("OverviewDrawer");
        drawer.IsVisible = !drawer.IsVisible;

        if (drawer.IsVisible)
            PopulateOperatorShell();
    }

    private void OverviewCloseButton_OnClick(
        object? sender,
        RoutedEventArgs e) =>
        Get<Border>("OverviewDrawer").IsVisible = false;

    private void OverviewNavigateButton_OnClick(
        object? sender,
        RoutedEventArgs e)
    {
        if (sender is Button button &&
            button.Tag is string navigationName)
        {
            Get<Border>("OverviewDrawer").IsVisible = false;
            Navigate(navigationName);
        }
    }

    private void ToggleCommandPalette()
    {
        var overlay = Get<Grid>("CommandPaletteOverlay");

        if (overlay.IsVisible)
        {
            CloseCommandPalette();
            return;
        }

        CloseControlPlaneDrawers();
        RebuildCommandPalette();

        var box = Get<TextBox>("CommandPaletteTextBox");
        box.Text = string.Empty;
        overlay.IsVisible = true;
        ApplyCommandPaletteFilter();
        box.Focus();
    }

    private void CloseCommandPalette()
    {
        var overlay = Get<Grid>("CommandPaletteOverlay");
        if (!overlay.IsVisible)
            return;

        overlay.IsVisible = false;
        Get<TextBox>("CommandPaletteTextBox").Text =
            string.Empty;
    }

    private void RebuildCommandPalette()
    {
        bool IsDetectedIntegration(string navigationName)
        {
            if (!IntegrationNavigationTargets.TryGetValue(
                    navigationName,
                    out var integrationName))
            {
                return true;
            }

            return _integrations.Any(item =>
                item.Name.Equals(
                    integrationName,
                    StringComparison.OrdinalIgnoreCase));
        }

        _commandPaletteItems = _navigation
            .Where(item => IsDetectedIntegration(item.Key))
            .Select(item => new CommandPaletteItem(
                item.Value.Title,
                item.Value.Subtitle,
                IntegrationNavigationTargets.ContainsKey(item.Key)
                    ? "APPLICATION"
                    : "PAGE",
                item.Key))
            .OrderBy(item =>
                item.Kind.Equals(
                    "PAGE",
                    StringComparison.Ordinal)
                    ? 0
                    : 1)
            .ThenBy(item => item.Title)
            .ToArray();

        ApplyCommandPaletteFilter();
    }

    private void CommandPaletteTextBox_OnTextChanged(
        object? sender,
        TextChangedEventArgs e) =>
        ApplyCommandPaletteFilter();

    private void ApplyCommandPaletteFilter()
    {
        var list = Get<ListBox>("CommandPaletteList");
        var query =
            Get<TextBox>("CommandPaletteTextBox")
                .Text?
                .Trim();

        var rows = _commandPaletteItems
            .Where(item =>
                string.IsNullOrWhiteSpace(query) ||
                item.Title.Contains(
                    query,
                    StringComparison.OrdinalIgnoreCase) ||
                item.Subtitle.Contains(
                    query,
                    StringComparison.OrdinalIgnoreCase) ||
                item.Kind.Contains(
                    query,
                    StringComparison.OrdinalIgnoreCase))
            .ToArray();

        list.ItemsSource = rows;
        list.SelectedIndex = rows.Length > 0 ? 0 : -1;
    }

    private void CommandPaletteTextBox_OnKeyDown(
        object? sender,
        KeyEventArgs e)
    {
        var list = Get<ListBox>("CommandPaletteList");
        var count =
            (list.ItemsSource as IReadOnlyCollection<CommandPaletteItem>)
                ?.Count ??
            0;

        if (e.Key == Key.Down && count > 0)
        {
            list.SelectedIndex =
                Math.Min(list.SelectedIndex + 1, count - 1);
            e.Handled = true;
            return;
        }

        if (e.Key == Key.Up && count > 0)
        {
            list.SelectedIndex =
                Math.Max(list.SelectedIndex - 1, 0);
            e.Handled = true;
            return;
        }

        if (e.Key == Key.Enter)
        {
            ExecuteSelectedCommand();
            e.Handled = true;
            return;
        }

        if (e.Key == Key.Escape)
        {
            CloseCommandPalette();
            e.Handled = true;
        }
    }

    private void CommandPaletteList_OnDoubleTapped(
        object? sender,
        TappedEventArgs e) =>
        ExecuteSelectedCommand();

    private void ExecuteSelectedCommand()
    {
        if (Get<ListBox>("CommandPaletteList").SelectedItem
            is not CommandPaletteItem item)
        {
            return;
        }

        CloseCommandPalette();
        Navigate(item.NavigationName);
    }

    private void PopulateOperatorShell()
    {
        if (_snapshot is null ||
            _backup is null ||
            _policyEvaluation is null)
        {
            return;
        }

        var active = _policyEvaluation.Active
            .Where(item =>
                item.Severity >= OpsSeverity.Warning)
            .ToArray();
        var errors = active.Count(item =>
            item.Severity >= OpsSeverity.Error);
        var warnings = active.Count(item =>
            item.Severity == OpsSeverity.Warning);
        var muted = _policyEvaluation.Muted.Count;
        var background = _logs.Count(item =>
            item.Severity == OpsSeverity.Info);
        var customPolicies =
            LinuxOpsAnalyzer.OperationalStorage(_snapshot)
                .Count(item =>
                    _findingPolicies.HasCustomStorageThreshold(
                        item.MountPoint));
        var safeMode =
            Get<CheckBox>("SafeModeCheckBox").IsChecked == true;

        Get<TextBlock>("FooterTargetText").Text =
            _snapshot.Hostname;
        Get<TextBlock>("FooterConnectionText").Text =
            Get<TextBlock>("ConnectionText").Text;
        Get<TextBlock>("FooterModeText").Text =
            safeMode ? "SAFE MODE" : "NORMAL";
        Get<TextBlock>("FooterFindingsText").Text =
            $"{active.Length} active · {background} background";
        Get<TextBlock>("FooterFreshnessText").Text =
            $"captured {_snapshot.CapturedAt.ToLocalTime():t}";

        Get<TextBlock>("OverviewTargetText").Text =
            _snapshot.Hostname;
        Get<TextBlock>("OverviewOsText").Text =
            _snapshot.OperatingSystem;
        Get<TextBlock>("OverviewControlPlaneText").Text =
            Get<TextBlock>("ConnectionText").Text;
        Get<TextBlock>("OverviewCapturedText").Text =
            $"Captured {_snapshot.CapturedAt.ToLocalTime():g}";
        Get<TextBlock>("OverviewFindingsText").Text =
            $"{errors} error · {warnings} warning · " +
            $"{muted} muted";
        Get<TextBlock>("OverviewPolicyText").Text =
            customPolicies == 0
                ? "Default storage monitoring"
                : $"{customPolicies} custom storage " +
                  $"{(customPolicies == 1 ? "policy" : "policies")} active";
        Get<TextBlock>("OverviewBackupText").Text =
            $"{_backup.State} · {_backup.Summary}";

        var top = active
            .OrderByDescending(item => item.Severity)
            .ThenBy(item => item.Rank)
            .FirstOrDefault();

        Get<TextBlock>("OverviewHighestPriorityText").Text =
            top is null
                ? "No active operational finding."
                : $"Highest priority · {top.Component} — " +
                  $"{top.Problem}";
    }

    private void ApplyOperatorSettingsToUi()
    {
        Get<CheckBox>("SettingsSafeModeCheckBox").IsChecked =
            _operatorSettings.StartInSafeMode;
        Get<CheckBox>("SettingsInformationalLogsCheckBox").IsChecked =
            _operatorSettings.ShowInformationalLogs;
        Get<CheckBox>("SettingsInformationalContainersCheckBox").IsChecked =
            _operatorSettings.ShowInformationalContainers;
        Get<CheckBox>("SettingsOpenOverviewCheckBox").IsChecked =
            _operatorSettings.OpenOverviewAfterStartup;
        Get<TextBox>("SettingsBackgroundRefreshSecondsTextBox").Text =
            NormalizeBackgroundRefreshSeconds(
                _operatorSettings.BackgroundRefreshSeconds)
                .ToString(CultureInfo.InvariantCulture);
        Get<CheckBox>("SettingsDesktopNotificationsCheckBox").IsChecked =
            _operatorSettings.DesktopNotifications;
        ApplyControlPlanePreferences();

        Get<CheckBox>("SafeModeCheckBox").IsChecked =
            _operatorSettings.StartInSafeMode;
        Get<CheckBox>("ShowInformationalLogsCheckBox").IsChecked =
            _operatorSettings.ShowInformationalLogs;
        Get<CheckBox>("ShowInformationalContainersCheckBox").IsChecked =
            _operatorSettings.ShowInformationalContainers;

        if (_snapshot is not null)
        {
            ApplyLogsFilter();
            ApplyDockerWorkspaceFilter();
            UpdateActionButtons();
            PopulateOperatorShell();
        }
    }

    private void PopulateSettingsAndTools()
    {
        Get<TextBlock>("SettingsConfigPathText").Text =
            _operatorSettingsStore.ConfigDirectory;
        Get<TextBlock>("SettingsDataPathText").Text =
            _operatorSettingsStore.DataDirectory;
        Get<TextBlock>("SettingsRepositoryPathText").Text =
            _repositoryPath;
        Get<TextBlock>("SettingsDiagnosticsPathText").Text =
            _operatorSettingsStore.DiagnosticsDirectory;
        Get<TextBlock>("SettingsPolicyFileText").Text =
            _operatorSettingsStore.PolicyPath;
        Get<TextBlock>("SettingsHistoryFileText").Text =
            _operatorSettingsStore.HistoryPath;
        Get<TextBlock>("SettingsPolicyPathText").Text =
            _operatorSettingsStore.PolicyPath;

        if (_policyEvaluation is null || _snapshot is null)
        {
            Get<TextBlock>("SettingsPolicySummaryText").Text =
                "Waiting for environment capture";
            Get<Button>("CreateDiagnosticsButton").IsEnabled =
                false;
            return;
        }

        var customPolicies =
            LinuxOpsAnalyzer.OperationalStorage(_snapshot)
                .Count(item =>
                    _findingPolicies.HasCustomStorageThreshold(
                        item.MountPoint));

        Get<TextBlock>("SettingsPolicySummaryText").Text =
            $"{_policyEvaluation.Active.Count} active · " +
            $"{_policyEvaluation.Muted.Count} muted · " +
            $"{customPolicies} custom storage " +
            $"{(customPolicies == 1 ? "policy" : "policies")}";

        Get<Button>("CreateDiagnosticsButton").IsEnabled =
            true;
        Get<TextBlock>("DiagnosticsStatusText").Text =
            $"Ready to export capture " +
            $"{_snapshot.CapturedAt.ToLocalTime():g}.";
    }

    private void SaveOperatorSettingsButton_OnClick(
        object? sender,
        RoutedEventArgs e)
    {
        try
        {
            if (!TryReadBackgroundRefreshSeconds(
                    out var backgroundRefreshSeconds))
            {
                return;
            }

            _operatorSettings = new LinuxOperatorSettings(
                Get<CheckBox>("SettingsSafeModeCheckBox")
                    .IsChecked == true,
                Get<CheckBox>("SettingsInformationalLogsCheckBox")
                    .IsChecked == true,
                Get<CheckBox>("SettingsInformationalContainersCheckBox")
                    .IsChecked == true,
                Get<CheckBox>("SettingsOpenOverviewCheckBox")
                    .IsChecked == true,
                backgroundRefreshSeconds,
                Get<CheckBox>("SettingsDesktopNotificationsCheckBox")
                    .IsChecked == true);

            _operatorSettingsStore.Save(_operatorSettings);
            ApplyOperatorSettingsToUi();

            Get<TextBlock>("SettingsSaveStatusText").Text =
                $"Saved {_operatorSettingsStore.SettingsPath}";
            _history.RecordPolicy(
                "Operator settings",
                "SAVED",
                "Startup defaults and information-view preferences updated.");
            PopulateHistory();
        }
        catch (Exception exception)
        {
            Get<TextBlock>("SettingsSaveStatusText").Text =
                $"Could not save settings: {exception.Message}";
        }
    }

    private void ResetOperatorSettingsButton_OnClick(
        object? sender,
        RoutedEventArgs e)
    {
        try
        {
            _operatorSettings =
                _operatorSettingsStore.Reset();
            ApplyOperatorSettingsToUi();

            Get<TextBlock>("SettingsSaveStatusText").Text =
                "Default operator settings restored.";
            _history.RecordPolicy(
                "Operator settings",
                "DEFAULTS RESTORED",
                "Safe Mode enabled; informational views disabled.");
            PopulateHistory();
        }
        catch (Exception exception)
        {
            Get<TextBlock>("SettingsSaveStatusText").Text =
                $"Could not restore defaults: {exception.Message}";
        }
    }

    private void SettingsNavigateButton_OnClick(
        object? sender,
        RoutedEventArgs e)
    {
        if (sender is Button button &&
            button.Tag is string navigationName)
        {
            Navigate(navigationName);
        }
    }

    private void OpenOperatorPathButton_OnClick(
        object? sender,
        RoutedEventArgs e)
    {
        if (sender is not Button button ||
            button.Tag is not string key)
        {
            return;
        }

        var path = ResolveOperatorPath(key);

        if (key is "config" or "data" or "diagnostics")
            Directory.CreateDirectory(path);

        var status =
            Get<TextBlock>("SettingsPathStatusText");

        if (LinuxOperatorTools.OpenPath(path, out var error))
            status.Text = $"Opened {path}";
        else
            status.Text = error;
    }

    private void OpenOperatorTerminalButton_OnClick(
        object? sender,
        RoutedEventArgs e)
    {
        if (sender is not Button button ||
            button.Tag is not string key)
        {
            return;
        }

        var path = ResolveOperatorPath(key);
        Directory.CreateDirectory(path);

        var status =
            Get<TextBlock>("TerminalStatusText");

        if (LinuxOperatorTools.OpenTerminal(path, out var error))
            status.Text = $"Opened terminal in {path}";
        else
            status.Text = error;
    }

    private string ResolveOperatorPath(string key) =>
        key switch
        {
            "config" =>
                _operatorSettingsStore.ConfigDirectory,
            "data" =>
                _operatorSettingsStore.DataDirectory,
            "diagnostics" =>
                _operatorSettingsStore.DiagnosticsDirectory,
            "policy" =>
                File.Exists(_operatorSettingsStore.PolicyPath)
                    ? _operatorSettingsStore.PolicyPath
                    : _operatorSettingsStore.ConfigDirectory,
            "history" =>
                File.Exists(_operatorSettingsStore.HistoryPath)
                    ? _operatorSettingsStore.HistoryPath
                    : _operatorSettingsStore.DataDirectory,
            "repo" =>
                _repositoryPath,
            _ =>
                _repositoryPath
        };

    private async void RefreshVersionInfoButton_OnClick(
        object? sender,
        RoutedEventArgs e) =>
        await RefreshVersionInfoAsync();

    private async Task RefreshVersionInfoAsync()
    {
        var version =
            await LinuxOperatorTools.CaptureVersionAsync(
                _repositoryPath);

        Get<TextBlock>("SettingsBranchText").Text =
            version.Branch;
        Get<TextBlock>("SettingsCommitText").Text =
            $"{version.Commit} · {version.Subject}";
        Get<TextBlock>("SettingsWorktreeText").Text =
            version.Worktree;
        Get<TextBlock>("SettingsOriginText").Text =
            version.OriginComparison;
        Get<TextBlock>("SettingsDotnetText").Text =
            version.DotnetVersion;
    }

    private async void RunValidationButton_OnClick(
        object? sender,
        RoutedEventArgs e)
    {
        var button = Get<Button>("RunValidationButton");
        button.IsEnabled = false;
        button.Content = "Validating...";

        try
        {
            Get<TextBox>("ValidationOutputText").Text =
                await LinuxOperatorTools.ValidateAsync(
                    _repositoryPath,
                    _operatorSettingsStore);
        }
        catch (Exception exception)
        {
            Get<TextBox>("ValidationOutputText").Text =
                $"Validation failed: {exception}";
        }
        finally
        {
            button.IsEnabled = true;
            button.Content = "Run validation";
        }
    }

    private async void CreateDiagnosticsButton_OnClick(
        object? sender,
        RoutedEventArgs e)
    {
        if (_snapshot is null ||
            _analysis is null ||
            _backup is null ||
            _policyEvaluation is null)
        {
            Get<TextBlock>("DiagnosticsStatusText").Text =
                "Refresh the environment before exporting.";
            return;
        }

        var button =
            Get<Button>("CreateDiagnosticsButton");
        button.IsEnabled = false;
        button.Content = "Creating bundle...";

        try
        {
            var archivePath =
                await LinuxOperatorTools.CreateDiagnosticsAsync(
                    _repositoryPath,
                    _operatorSettingsStore,
                    _snapshot,
                    _analysis,
                    _lifecycle,
                    _integrations,
                    _logs,
                    _backup,
                    _policyEvaluation,
                    _operatorSettings);

            Get<TextBlock>("DiagnosticsStatusText").Text =
                $"Created {archivePath}";
            _history.RecordPolicy(
                "Diagnostics",
                "EXPORTED",
                archivePath);
            PopulateHistory();
        }
        catch (Exception exception)
        {
            Get<TextBlock>("DiagnosticsStatusText").Text =
                $"Diagnostics export failed: {exception.Message}";
        }
        finally
        {
            button.IsEnabled = true;
            button.Content = "Create diagnostics bundle";
        }
    }

    private async void RefreshButton_OnClick(object? sender, RoutedEventArgs e) => await RefreshAsync();

    private static bool IsPlexTokenProbePrivilegeNoise(
        OpsLogGroup log)
    {
        var source =
            log.Source ?? string.Empty;
        var message =
            log.Message ?? string.Empty;

        return
            source.Equals(
                "sudo",
                StringComparison.OrdinalIgnoreCase) &&
            message.Contains(
                "a password is required",
                StringComparison.OrdinalIgnoreCase) &&
            (
                message.Contains(
                    "Plex Media Server/Preferences.xml",
                    StringComparison.OrdinalIgnoreCase) ||
                message.Contains(
                    "plexmediaserver/Library/Application Support",
                    StringComparison.OrdinalIgnoreCase)
            );
    }

    private async Task RefreshAsync()
    {
        if (_controlPlaneCaptureBusy)
            return;

        var refresh = Get<Button>("RefreshButton");
        var background = _controlPlaneBackgroundRefresh;

        if (!background)
        {
            refresh.IsEnabled = false;
            refresh.Content = "Refreshing environment...";
        }

        SetControlPlaneState(
            OpsSeverity.Info,
            "REFRESHING",
            $"Capturing {_controlPlane.ActiveProfile.ConnectionSummary}");

        try
        {
            _snapshot = await CaptureActiveTargetAsync();
            _backup = await CaptureTargetBackupAsync();
            _integrations = LinuxOpsAnalyzer.EnrichIntegrations(_snapshot);
            _logs =
                LinuxOpsAnalyzer
                    .GroupLogs(_snapshot.RecentLogs)
                    .Where(item =>
                        !IsPlexTokenProbePrivilegeNoise(item))
                    .ToArray();
            _rawAnalysis = LinuxOpsAnalyzer.Analyze(
                _snapshot,
                _backup,
                _logs,
                _integrations);
            _rawLifecycle = LinuxOpsAnalyzer.BuildLifecycle(
                _snapshot,
                _integrations,
                _rawAnalysis);
            ApplyFindingPolicies();
            RecordInsightCapture();
            _history.Record(
                _snapshot,
                _analysis!,
                _lifecycle,
                _backup,
                _findingPolicies.EvaluateStorageSeverity);
            PopulateAll();
            RecordRefreshSuccessAndNotify();

            if (!background)
            {
                _nextBackgroundRefreshAt =
                    DateTimeOffset.Now +
                    TimeSpan.FromSeconds(
                        NormalizeBackgroundRefreshSeconds(
                            _operatorSettings.BackgroundRefreshSeconds));
            }
        }
        catch (Exception exception)
        {
            SetControlPlaneState(
                OpsSeverity.Error,
                "OFFLINE",
                "Provider capture failed");
            Get<TextBlock>("LastUpdatedText").Text =
                $"Capture failed · {exception.Message}";
            RecordRefreshFailure(exception);
        }
        finally
        {
            if (!background)
            {
                refresh.IsEnabled = true;
                refresh.Content = "Refresh environment";
            }
        }
    }

    private void ApplyFindingPolicies()
    {
        if (_snapshot is null || _rawAnalysis is null)
            return;

        _policyEvaluation = _findingPolicies.Evaluate(
            _snapshot,
            _rawAnalysis);
        _analysis = _policyEvaluation.Analysis;
        _lifecycle = _findingPolicies.ApplyLifecycle(
            _snapshot,
            _rawLifecycle,
            _policyEvaluation);
    }

    private void RefreshPolicyProjection()
    {
        ApplyFindingPolicies();
        PopulateAll();
    }

    private void PopulateAll()
    {
        if (_snapshot is null ||
            _backup is null ||
            _analysis is null ||
            _policyEvaluation is null)
        {
            return;
        }

        Get<TextBlock>("SidebarHostname").Text = _snapshot.Hostname;
        Get<TextBlock>("SidebarOperatingSystem").Text = _snapshot.OperatingSystem;
        Get<TextBlock>("LastUpdatedText").Text =
            $"Captured {_snapshot.CapturedAt.ToLocalTime():g}";
        SetControlPlaneState(
            OpsSeverity.Healthy,
            "ONLINE",
            ControlPlaneConnectionDetail());

        PopulateDashboard();
        PopulateIntelligence();
        PopulateLifecycle();
        PopulateHistory();
        PopulateServerPage();
        UpdateIntegrationNavigation();
        ApplyMediaFilter();
        PopulatePlexWorkspace();
        PopulateDirectIntegrationWorkspace();
        PopulateDownloadClientWorkspace();
        PopulateArrApplicationPage();
        PopulateRecyclarrWorkspace();
        ApplyServicesFilter();
        PopulateDockerWorkspaceFallback();
        ApplyStorageFilter();
        ApplyLogsFilter();
        PopulateBackups();
        UpdateActionButtons();
        PopulateOperatorShell();
        PopulateSettingsAndTools();
        RebuildCommandPalette();
        PopulateControlPlaneFoundation();
    }

    private void PopulateDashboard() =>
        PopulateDashboardV43();

    private void PopulateIntelligence() =>
        PopulateIntelligenceV43();

    private void PopulateLifecycle() =>
        PopulateLifecycleV43();

    private void PopulateHistory() =>
        PopulateHistoryV43();

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

    private void ApplyLogsFilter() =>
        ApplyReliableLogsFilter();

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

    private void ActivateArrProduct(string productName)
    {
        _activeArrProduct = productName;
        PopulateArrApplicationPage();
        _ = RefreshArrLiveTelemetryAsync();
    }

    private void PopulateArrApplicationPage()
    {
        _arrWorkspaceRows =
            ArrWorkspaceRegistry.BuildViews(
                _integrations,
                _arrWorkspaceProfiles);

        var instances = ActiveArrInstances();

        Get<TextBlock>("ArrApplicationTitleText").Text =
            _activeArrProduct;
        Get<TextBlock>("ArrApplicationSubtitleText").Text =
            ArrPageSubtitle(_activeArrProduct);
        Get<TextBlock>("ArrOperationsSubtitleText").Text =
            ArrOperationsSubtitle(_activeArrProduct);
        Get<TextBlock>("ArrWorkMetricLabelText").Text =
            ArrWorkMetricLabel(_activeArrProduct);
        Get<TextBlock>("ArrWorkSectionTitleText").Text =
            ArrWorkSectionTitle(_activeArrProduct);
        Get<TextBlock>("ArrWorkSectionSubtitleText").Text =
            ArrWorkSectionSubtitle(_activeArrProduct);
        Get<TextBlock>("ArrInstanceCountText").Text =
            $"{instances.Length} " +
            $"{(instances.Length == 1 ? "instance" : "instances")}";

        Get<TextBlock>("ArrWorkspaceConfigPathText").Text =
            $"Profiles · {_arrWorkspaceProfiles.FilePath}";

        PopulateArrOpenButtons(instances);
        PopulateArrDetectionFallback(instances);
        PopulateArrCustomizationInstances(instances);
    }

    private ArrWorkspaceView[] ActiveArrInstances() =>
        _arrWorkspaceRows
            .Where(item =>
                item.ProductName.Equals(
                    _activeArrProduct,
                    StringComparison.OrdinalIgnoreCase) ||
                item.Integration.Name.Equals(
                    _activeArrProduct,
                    StringComparison.OrdinalIgnoreCase))
            .ToArray();

    private void PopulateArrOpenButtons(
        ArrWorkspaceView[] instances)
    {
        var panel =
            Get<WrapPanel>("ArrOpenButtonsPanel");
        panel.Children.Clear();

        foreach (var instance in instances)
        {
            var url =
                ResolveIntegrationUrl(
                    instance.Integration);

            var button = new Button
            {
                Content = $"Open {instance.DisplayName}",
                Tag = instance,
                IsEnabled = url is not null,
                Margin = new Thickness(0, 0, 8, 8),
                Classes = { "compact", "arrAction" }
            };

            if (panel.Children.Count == 0)
                button.Classes.Add("primary");

            button.Click +=
                ArrDynamicOpenButton_OnClick;

            panel.Children.Add(button);
        }

        if (panel.Children.Count == 0)
        {
            panel.Children.Add(
                new TextBlock
                {
                    Text =
                        $"No {_activeArrProduct} instance was detected.",
                    Classes = { "muted" },
                    VerticalAlignment =
                        VerticalAlignment.Center
                });
        }
    }

    private void PopulateArrDetectionFallback(
        ArrWorkspaceView[] instances)
    {
        if (_arrTelemetrySnapshot is not null &&
            _arrTelemetryProduct.Equals(
                _activeArrProduct,
                StringComparison.OrdinalIgnoreCase))
        {
            ApplyArrTelemetry(
                _arrTelemetrySnapshot);
            return;
        }

        var services = instances
            .Select(instance =>
                new ArrServiceTelemetryRow(
                    instance.InstanceKey,
                    instance.DisplayName,
                    ResolveIntegrationUrl(
                        instance.Integration) ??
                    instance.Endpoint ??
                    "--",
                    "--",
                    "--",
                    "--",
                    "Waiting for live probe",
                    instance.SeverityLabel))
            .ToArray();

        var serviceList =
            Get<ListBox>("ArrInstanceTelemetryList");

        serviceList.ItemsSource = services;
        serviceList.IsVisible = services.Length > 0;

        Get<Border>("ArrServiceEmptyStateText").IsVisible =
            services.Length == 0;

        Get<ListBox>("ArrQueueHealthList")
            .ItemsSource =
                instances.Length == 0
                    ? new[]
                    {
                        new ArrWorkItemRow(
                            _activeArrProduct,
                            "Detection",
                            "No compatible instance detected",
                            "Unavailable",
                            string.Empty,
                            string.Empty,
                            "Verify the service, container identity or published port.")
                    }
                    : new[]
                    {
                        new ArrWorkItemRow(
                            _activeArrProduct,
                            "Telemetry",
                            "Live application probe pending",
                            "Waiting",
                            string.Empty,
                            string.Empty,
                            "GraveOps reads the local config.xml without displaying or storing its API key.")
                    };

        Get<TextBlock>("ArrStateMetricText").Text =
            instances.Length > 0
                ? "DETECTED"
                : "NOT DETECTED";
        Get<TextBlock>("ArrVersionMetricText").Text =
            "--";
        Get<TextBlock>("ArrWorkMetricText").Text =
            "--";
        Get<TextBlock>("ArrHealthMetricText").Text =
            "--";
        Get<TextBlock>("ArrQueueFooterText").Text =
            "Waiting for the first live application probe.";
        Get<TextBlock>("ArrLiveUpdatedText").Text =
            "Waiting for live telemetry";

        Get<ListBox>("ArrQueueHealthList").IsVisible = true;
        Get<Border>("ArrQueueEmptyStateText").IsVisible = false;
    }

    private async Task RefreshArrLiveTelemetryAsync()
    {
        if (_arrTelemetryBusy)
            return;

        var page =
            Get<Control>("ArrWorkspacePage");

        if (!page.IsVisible)
            return;

        if (!_controlPlane.ActiveProfile.IsLocal)
        {
            ApplyRemoteArrTelemetryBoundary();
            return;
        }

        var instances = ActiveArrInstances();

        if (instances.Length == 0)
        {
            PopulateArrDetectionFallback(instances);
            return;
        }

        _arrTelemetryBusy = true;

        var button =
            Get<Button>("ArrRefreshTelemetryButton");
        button.IsEnabled = false;
        button.Content = "Refreshing...";

        try
        {
            var snapshot =
                await _arrTelemetry.CaptureAsync(
                    instances);

            _arrTelemetrySnapshot = snapshot;
            _arrTelemetryProduct =
                _activeArrProduct;

            ApplyArrTelemetry(snapshot);
        }
        catch (Exception exception)
        {
            Get<TextBlock>("ArrQueueFooterText").Text =
                $"Live telemetry failed: {exception.Message}";
        }
        finally
        {
            button.IsEnabled = true;
            button.Content = "Refresh";
            _arrTelemetryBusy = false;
        }
    }

    private void ApplyArrTelemetry(
        ArrLiveTelemetrySnapshot snapshot)
    {
        Get<TextBlock>("ArrStateMetricText").Text =
            snapshot.OverallState;
        Get<TextBlock>("ArrVersionMetricText").Text =
            snapshot.VersionSummary;
        Get<TextBlock>("ArrWorkMetricText").Text =
            snapshot.WorkSummary;
        Get<TextBlock>("ArrHealthMetricText").Text =
            snapshot.HealthSummary;
        Get<TextBlock>("ArrLiveUpdatedText").Text =
            $"LIVE · updated {snapshot.CapturedAt:t}";

        var serviceList =
            Get<ListBox>("ArrInstanceTelemetryList");
        serviceList.ItemsSource = snapshot.Services;
        serviceList.IsVisible = snapshot.Services.Count > 0;

        Get<Border>("ArrServiceEmptyStateText").IsVisible =
            snapshot.Services.Count == 0;

        var queueList =
            Get<ListBox>("ArrQueueHealthList");
        queueList.ItemsSource = snapshot.WorkItems;
        queueList.IsVisible = snapshot.WorkItems.Count > 0;

        Get<Border>("ArrQueueEmptyStateText").IsVisible =
            snapshot.WorkItems.Count == 0;

        Get<TextBlock>("ArrQueueFooterText").Text =
            $"LIVE · updated {snapshot.CapturedAt:T} · " +
            $"{snapshot.WorkItems.Count} " +
            $"{(snapshot.WorkItems.Count == 1 ? "row" : "rows")}.";
    }

    private async void ArrRefreshTelemetryButton_OnClick(
        object? sender,
        RoutedEventArgs e) =>
        await RefreshArrLiveTelemetryAsync();

    private void ArrCustomizeButton_OnClick(
        object? sender,
        RoutedEventArgs e)
    {
        Get<Border>("ArrCustomizationPanel").IsVisible = true;
        PopulateArrCustomization();
    }

    private void ArrCustomizeCloseButton_OnClick(
        object? sender,
        RoutedEventArgs e) =>
        Get<Border>("ArrCustomizationPanel").IsVisible = false;

    private void PopulateArrCustomizationInstances(
        IReadOnlyList<ArrWorkspaceView> instances)
    {
        var combo =
            Get<ComboBox>(
                "ArrCustomizeInstanceComboBox");
        var selectedKey =
            (combo.SelectedItem as ArrWorkspaceView)?
                .InstanceKey ??
            _selectedArrInstanceKey;

        combo.ItemsSource = instances;
        combo.SelectedItem =
            instances.FirstOrDefault(item =>
                item.InstanceKey.Equals(
                    selectedKey,
                    StringComparison.OrdinalIgnoreCase)) ??
            instances.FirstOrDefault();

        PopulateArrCustomization();
    }

    private void ArrCustomizeInstanceComboBox_OnSelectionChanged(
        object? sender,
        SelectionChangedEventArgs e) =>
        PopulateArrCustomization();

    private ArrWorkspaceView? SelectedArrWorkspace() =>
        Get<ComboBox>("ArrCustomizeInstanceComboBox")
            .SelectedItem as ArrWorkspaceView;

    private void PopulateArrCustomization()
    {
        var selected = SelectedArrWorkspace();
        var modulesPanel =
            Get<StackPanel>(
                "ArrWorkspaceModulesPanel");
        modulesPanel.Children.Clear();

        var save =
            Get<Button>("SaveArrWorkspaceButton");
        var reset =
            Get<Button>("ResetArrWorkspaceButton");

        if (selected is null)
        {
            Get<TextBox>("ArrFriendlyNameTextBox").Text =
                string.Empty;
            Get<TextBox>("ArrRoleTextBox").Text =
                string.Empty;
            Get<TextBox>("ArrConfigPathTextBox").Text =
                string.Empty;
            Get<CheckBox>("ArrPrivacyModeCheckBox")
                .IsChecked = false;
            save.IsEnabled = false;
            reset.IsEnabled = false;
            Get<TextBlock>("ArrWorkspaceProfileStatusText")
                .Text = "No instance selected.";
            return;
        }

        _selectedArrInstanceKey =
            selected.InstanceKey;

        Get<TextBox>("ArrFriendlyNameTextBox").Text =
            selected.Profile.FriendlyName;
        Get<TextBox>("ArrRoleTextBox").Text =
            selected.Role;
        Get<TextBox>("ArrConfigPathTextBox").Text =
            selected.Profile.ConfigPath;
        Get<CheckBox>("ArrPrivacyModeCheckBox")
            .IsChecked =
            selected.Profile.PrivacyMode;

        var enabled =
            selected.Profile.EnabledModules.ToHashSet(
                StringComparer.OrdinalIgnoreCase);

        foreach (var module in
                 selected.Definition.Modules)
        {
            var checkBox = new CheckBox
            {
                Tag = module.Id,
                IsChecked =
                    enabled.Contains(module.Id)
            };

            checkBox.Content = new StackPanel
            {
                Margin =
                    new Thickness(6, 0, 0, 0),
                Spacing = 3,
                Children =
                {
                    new TextBlock
                    {
                        Text = module.Title,
                        FontWeight =
                            FontWeight.SemiBold
                    },
                    new TextBlock
                    {
                        Text = module.Description,
                        Classes = { "muted" },
                        FontSize = 10,
                        TextWrapping =
                            TextWrapping.Wrap
                    }
                }
            };

            modulesPanel.Children.Add(checkBox);
        }

        save.IsEnabled = true;
        reset.IsEnabled =
            _arrWorkspaceProfiles.IsCustomized(
                selected.InstanceKey);

        Get<TextBlock>("ArrWorkspaceProfileStatusText")
            .Text =
            reset.IsEnabled
                ? "Custom profile active."
                : "Default integration profile.";
    }

    private IEnumerable<string> SelectedArrModuleIds() =>
        Get<StackPanel>("ArrWorkspaceModulesPanel")
            .Children
            .OfType<CheckBox>()
            .Where(item => item.IsChecked == true)
            .Select(item => item.Tag as string)
            .Where(value =>
                !string.IsNullOrWhiteSpace(value))
            .Cast<string>();

    private void SaveArrWorkspaceButton_OnClick(
        object? sender,
        RoutedEventArgs e)
    {
        var selected = SelectedArrWorkspace();
        if (selected is null)
            return;

        try
        {
            _arrWorkspaceProfiles.Save(
                selected.InstanceKey,
                selected.Definition,
                Get<TextBox>("ArrFriendlyNameTextBox")
                    .Text ?? string.Empty,
                Get<TextBox>("ArrRoleTextBox")
                    .Text ?? string.Empty,
                Get<TextBox>("ArrConfigPathTextBox")
                    .Text ?? string.Empty,
                Get<CheckBox>("ArrPrivacyModeCheckBox")
                    .IsChecked == true,
                SelectedArrModuleIds());

            _history.RecordPolicy(
                selected.ProductName,
                "WORKSPACE PROFILE SAVED",
                selected.InstanceKey);

            _arrTelemetrySnapshot = null;
            PopulateArrApplicationPage();

            Get<TextBlock>("ArrWorkspaceProfileStatusText")
                .Text = "Workspace profile saved.";

            _ = RefreshArrLiveTelemetryAsync();
            PopulateHistory();
        }
        catch (Exception exception)
        {
            Get<TextBlock>("ArrWorkspaceProfileStatusText")
                .Text =
                $"Could not save profile: {exception.Message}";
        }
    }

    private void ResetArrWorkspaceButton_OnClick(
        object? sender,
        RoutedEventArgs e)
    {
        var selected = SelectedArrWorkspace();
        if (selected is null)
            return;

        try
        {
            _arrWorkspaceProfiles.Reset(
                selected.InstanceKey);

            _history.RecordPolicy(
                selected.ProductName,
                "WORKSPACE DEFAULTS RESTORED",
                selected.InstanceKey);

            _arrTelemetrySnapshot = null;
            PopulateArrApplicationPage();

            Get<TextBlock>("ArrWorkspaceProfileStatusText")
                .Text =
                "Default workspace profile restored.";

            _ = RefreshArrLiveTelemetryAsync();
            PopulateHistory();
        }
        catch (Exception exception)
        {
            Get<TextBlock>("ArrWorkspaceProfileStatusText")
                .Text =
                $"Could not restore defaults: {exception.Message}";
        }
    }

    private void EnableAllArrModulesButton_OnClick(
        object? sender,
        RoutedEventArgs e) =>
        SetAllArrModules(true);

    private void DisableAllArrModulesButton_OnClick(
        object? sender,
        RoutedEventArgs e) =>
        SetAllArrModules(false);

    private void SetAllArrModules(bool enabled)
    {
        foreach (var checkBox in
                 Get<StackPanel>(
                         "ArrWorkspaceModulesPanel")
                     .Children
                     .OfType<CheckBox>())
        {
            checkBox.IsChecked = enabled;
        }

        Get<TextBlock>("ArrWorkspaceProfileStatusText")
            .Text =
            enabled
                ? "All modules selected. Save to persist."
                : "All modules cleared. Save to persist.";
    }

    private async void ArrDynamicOpenButton_OnClick(
        object? sender,
        RoutedEventArgs e)
    {
        if (sender is not Button button ||
            button.Tag is not ArrWorkspaceView instance)
        {
            return;
        }

        await OpenArrInstanceAsync(instance);
    }

    private async Task OpenArrInstanceAsync(
        ArrWorkspaceView instance)
    {
        var url =
            ResolveIntegrationUrl(
                instance.Integration);

        if (url is null)
            return;

        try
        {
            using var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = "xdg-open",
                    UseShellExecute = false,
                    CreateNoWindow = true
                }
            };

            process.StartInfo.ArgumentList.Add(url);
            process.Start();

            Get<TextBlock>("ArrQueueFooterText").Text =
                $"Opened {url}";
            await Task.CompletedTask;
        }
        catch (Exception exception)
        {
            Get<TextBlock>("ArrQueueFooterText").Text =
                $"Could not open interface: {exception.Message}";
        }
    }

    private async void ArrOpenNativeDetailButton_OnClick(
        object? sender,
        RoutedEventArgs e)
    {
        var instance =
            ActiveArrInstances().FirstOrDefault();

        if (instance is not null)
            await OpenArrInstanceAsync(instance);
    }

    private void ArrDockerButton_OnClick(
        object? sender,
        RoutedEventArgs e) =>
        Navigate("DockerNav");

    private void ArrLogsButton_OnClick(
        object? sender,
        RoutedEventArgs e) =>
        Navigate("LogsNav");

    private void ArrWorkspaceIntelligenceButton_OnClick(
        object? sender,
        RoutedEventArgs e) =>
        Navigate("IntelligenceNav");

    private static string ArrPageSubtitle(
        string product) =>
        product.ToLowerInvariant() switch
        {
            "sonarr" =>
                "Series and episode health, queues and operational tools.",
            "radarr" =>
                "Movie health, queues, editions and operational tools.",
            "lidarr" =>
                "Artist, album and track health, queues and operational tools.",
            "prowlarr" =>
                "Indexer health, application synchronization and operational tools.",
            "readarr" =>
                "Author, book and edition health, queues and operational tools.",
            "whisparr" =>
                "Version-aware acquisition health, queues and operational tools.",
            "mylar3" =>
                "Comic-series acquisition, pull-list and post-processing tools.",
            "bazarr" =>
                "Subtitle coverage, provider health and synchronization tools.",
            "recyclarr" =>
                "Configuration targets, drift, validation and synchronization evidence.",
            _ =>
                $"{product} health, work and operational tools."
        };

    private static string ArrOperationsSubtitle(
        string product) =>
        $"{product} instances, native interface access, logs and stack context are kept together here.";

    private static string ArrWorkMetricLabel(
        string product) =>
        product.Equals(
            "Prowlarr",
            StringComparison.OrdinalIgnoreCase)
            ? "INDEXERS"
            : product.Equals(
                "Maintainerr",
                StringComparison.OrdinalIgnoreCase)
                ? "RULES"
                : "QUEUE";

    private static string ArrWorkSectionTitle(
        string product) =>
        product.ToLowerInvariant() switch
        {
            "sonarr" => "Episode queue & health",
            "radarr" => "Movie queue & health",
            "lidarr" => "Album queue & health",
            "prowlarr" => "Indexer health & activity",
            "readarr" => "Book queue & health",
            "mylar3" => "Issue queue & health",
            "bazarr" => "Subtitle coverage & health",
            "recyclarr" => "Drift, validation & health",
            _ => "Work & health"
        };

    private static string ArrWorkSectionSubtitle(
        string product) =>
        product.Equals(
            "Prowlarr",
            StringComparison.OrdinalIgnoreCase)
            ? "Indexer inventory and application health from the active Prowlarr instance."
            : $"Item-level work and health messages from every detected {product} instance.";

    private void MediaFilterText_OnTextChanged(object? sender, TextChangedEventArgs e) => ApplyMediaFilter();
    private void ServicesFilterText_OnTextChanged(object? sender, TextChangedEventArgs e) => ApplyServicesFilter();
    private void DockerFilterText_OnTextChanged(object? sender, TextChangedEventArgs e) => ApplyDockerFilter();
    private void ShowInformationalContainersCheckBox_OnClick(object? sender, RoutedEventArgs e) => ApplyDockerFilter();
    private void StorageFilterText_OnTextChanged(object? sender, TextChangedEventArgs e) => ApplyStorageFilter();
    private void ShowInformationalLogsCheckBox_OnClick(object? sender, RoutedEventArgs e) => ApplyLogsFilter();

    private void ApplyMediaFilter()
    {
        PopulateMediaHub();
    }

    private void ApplyServicesFilter()
    {
        if (_snapshot is null)
            return;

        var list = Get<ListBox>("ServicesList");
        var selectedUnit =
            (list.SelectedItem as ServiceSnapshot)?.Unit;
        var filter =
            Get<TextBox>("ServicesFilterText").Text?.Trim();

        var rows = LinuxOpsAnalyzer.UniqueServices(_snapshot)
            .Where(item => Matches(
                filter,
                item.Unit,
                item.Description,
                item.ActiveState,
                item.SubState,
                item.UnitFileState))
            .ToArray();

        list.ItemsSource = rows;
        list.SelectedItem = rows.FirstOrDefault(item =>
            item.Unit.Equals(
                selectedUnit,
                StringComparison.OrdinalIgnoreCase));

        Get<TextBlock>("ServicesSummaryText").Text =
            $"{rows.Length} shown · {_snapshot.FailedUnits.Count} failed";

        UpdateServiceDetail();
    }

    private void ApplyDockerFilter() =>
        ApplyDockerWorkspaceFilter();

    private void ApplyStorageFilter()
    {
        if (_snapshot is null)
            return;

        var list = Get<ListBox>("StorageList");
        var selectedMount =
            (list.SelectedItem as StorageDisplayRow)?
                .Snapshot.MountPoint;
        var filter =
            Get<TextBox>("StorageFilterText").Text?.Trim();

        var volumes = LinuxOpsAnalyzer.OperationalStorage(_snapshot)
            .Where(item => Matches(
                filter,
                item.Source,
                item.FileSystem,
                item.MountPoint,
                item.PercentUsed))
            .ToArray();

        var rows = volumes
            .Select(volume =>
            {
                var custom =
                    _findingPolicies.HasCustomStorageThreshold(
                        volume.MountPoint);
                var severity =
                    _findingPolicies.EvaluateStorageSeverity(
                        volume);

                return new StorageDisplayRow(
                    volume,
                    volume.Source,
                    volume.FileSystem,
                    volume.Size,
                    volume.Used,
                    volume.Available,
                    volume.PercentUsed,
                    custom ? "Custom" : "Default",
                    LinuxOpsAnalyzer.SeverityLabel(severity),
                    volume.MountPoint);
            })
            .ToArray();

        list.ItemsSource = rows;
        list.SelectedItem = rows.FirstOrDefault(item =>
            item.MountPoint.Equals(
                selectedMount,
                StringComparison.OrdinalIgnoreCase));

        var attention = volumes.Count(item =>
            _findingPolicies.EvaluateStorageSeverity(item) >=
            OpsSeverity.Warning);
        var customPolicies = volumes.Count(item =>
            _findingPolicies.HasCustomStorageThreshold(
                item.MountPoint));

        Get<TextBlock>("StorageSummaryText").Text =
            $"{rows.Length} shown · {attention} active capacity finding(s) · " +
            $"{customPolicies} custom " +
            $"{(customPolicies == 1 ? "policy" : "policies")}";

        UpdateStoragePolicyButtons();
    }

    private StorageVolumeSnapshot? SelectedStorageVolume() =>
        (Get<ListBox>("StorageList").SelectedItem
            as StorageDisplayRow)?.Snapshot;

    private void DashboardAttentionList_OnSelectionChanged(
        object? sender,
        SelectionChangedEventArgs e) =>
        UpdateFindingPolicyButtons();

    private void MutedFindingsList_OnSelectionChanged(
        object? sender,
        SelectionChangedEventArgs e) =>
        UpdateFindingPolicyButtons();

    private void StorageList_OnSelectionChanged(
        object? sender,
        SelectionChangedEventArgs e) =>
        UpdateStoragePolicyButtons();

    private void UpdateFindingPolicyButtons()
    {
        var selected = Get<ListBox>("DashboardAttentionList").SelectedItem
            as OpsPolicyFinding;
        var muted = Get<ListBox>("MutedFindingsList").SelectedItem
            as OpsMutedFinding;

        Get<Button>("AcknowledgeFindingButton").IsEnabled =
            selected?.CanAcknowledge == true &&
            selected.Severity >= OpsSeverity.Warning;
        Get<Button>("SnoozeFindingButton").IsEnabled =
            selected?.CanAcknowledge == true &&
            selected.Severity >= OpsSeverity.Warning;
        Get<Button>("IgnoreFindingButton").IsEnabled =
            selected?.CanIgnore == true &&
            selected.Severity >= OpsSeverity.Warning;
        Get<Button>("FindingThresholdButton").IsEnabled =
            selected is not null &&
            LinuxFindingPolicyStore.IsStorageCapacityKey(selected.Key);
        Get<Button>("RestoreMutedFindingButton").IsEnabled =
            muted is not null;
    }

    private void UpdateStoragePolicyButtons()
    {
        var selected =
            SelectedStorageVolume();

        Get<Button>("StorageThresholdButton").IsEnabled =
            selected is not null;
        Get<Button>("RestoreStorageThresholdButton").IsEnabled =
            selected is not null &&
            _findingPolicies.HasCustomStorageThreshold(
                selected.MountPoint);

        var title = Get<TextBlock>("StorageSelectedPolicyTitleText");
        var status = Get<TextBlock>("StoragePolicyStatusText");

        if (selected is null)
        {
            title.Text = "No mount selected";
            status.Text =
                "Select a mount to inspect or customize its policy.";
            return;
        }

        var policy = _findingPolicies.GetStorageThreshold(
            selected.MountPoint);
        var custom = _findingPolicies.HasCustomStorageThreshold(
            selected.MountPoint);
        var severity = _findingPolicies.EvaluateStorageSeverity(
            selected);

        title.Text =
            $"{selected.MountPoint} · " +
            $"{(custom ? "Custom policy active" : "Default policy")}";

        status.Text =
            $"Current: {selected.PercentUsed} used · {selected.Available} free · " +
            $"Status: {LinuxOpsAnalyzer.SeverityLabel(severity)} · " +
            $"Warning {policy.WarningPercent}% or < {FormatPolicyFree(policy.WarningFreeGiB)} · " +
            $"Error {policy.ErrorPercent}% or < {FormatPolicyFree(policy.ErrorFreeGiB)} · " +
            $"Critical {policy.CriticalPercent}% or < {FormatPolicyFree(policy.CriticalFreeGiB)}";
    }

    private void AcknowledgeFindingButton_OnClick(
        object? sender,
        RoutedEventArgs e)
    {
        if (Get<ListBox>("DashboardAttentionList").SelectedItem
            is not OpsPolicyFinding finding)
        {
            return;
        }

        try
        {
            _findingPolicies.Acknowledge(finding);
            _history.RecordPolicy(
                finding.Component,
                "ACKNOWLEDGED",
                $"{finding.Resource} · {finding.Problem}");
            Get<TextBlock>("DashboardPolicyStatusText").Text =
                "Finding acknowledged until its observed state changes.";
            RefreshPolicyProjection();
        }
        catch (Exception exception)
        {
            Get<TextBlock>("DashboardPolicyStatusText").Text =
                exception.Message;
        }
    }

    private async void SnoozeFindingButton_OnClick(
        object? sender,
        RoutedEventArgs e)
    {
        if (Get<ListBox>("DashboardAttentionList").SelectedItem
            is not OpsPolicyFinding finding)
        {
            return;
        }

        var duration = await ShowSnoozeDialogAsync();
        if (duration is null)
            return;

        try
        {
            _findingPolicies.Snooze(finding, duration.Value);
            _history.RecordPolicy(
                finding.Component,
                "SNOOZED",
                $"{finding.Resource} · until " +
                $"{DateTimeOffset.Now.Add(duration.Value).ToLocalTime():g}");
            Get<TextBlock>("DashboardPolicyStatusText").Text =
                "Finding snoozed.";
            RefreshPolicyProjection();
        }
        catch (Exception exception)
        {
            Get<TextBlock>("DashboardPolicyStatusText").Text =
                exception.Message;
        }
    }

    private async void IgnoreFindingButton_OnClick(
        object? sender,
        RoutedEventArgs e)
    {
        if (Get<ListBox>("DashboardAttentionList").SelectedItem
            is not OpsPolicyFinding finding)
        {
            return;
        }

        if (!await ConfirmActionAsync(
                $"Ignore {finding.Resource}?",
                "This creates a permanent rule for this exact finding and resource. Critical conditions still reactivate automatically."))
        {
            return;
        }

        try
        {
            _findingPolicies.Ignore(finding);
            _history.RecordPolicy(
                finding.Component,
                "IGNORED",
                $"{finding.Resource} · {finding.Problem}");
            Get<TextBlock>("DashboardPolicyStatusText").Text =
                "Exact-resource ignore rule created.";
            RefreshPolicyProjection();
        }
        catch (Exception exception)
        {
            Get<TextBlock>("DashboardPolicyStatusText").Text =
                exception.Message;
        }
    }

    private async void FindingThresholdButton_OnClick(
        object? sender,
        RoutedEventArgs e)
    {
        if (Get<ListBox>("DashboardAttentionList").SelectedItem
            is not OpsPolicyFinding finding ||
            !LinuxFindingPolicyStore.IsStorageCapacityKey(finding.Key))
        {
            return;
        }

        var mountPoint =
            LinuxFindingPolicyStore.MountPointFromStorageKey(finding.Key);
        await ConfigureStorageThresholdAsync(mountPoint);
    }

    private void RestoreMutedFindingButton_OnClick(
        object? sender,
        RoutedEventArgs e)
    {
        if (Get<ListBox>("MutedFindingsList").SelectedItem
            is not OpsMutedFinding finding)
        {
            return;
        }

        if (!_findingPolicies.Restore(finding.Key))
            return;

        _history.RecordPolicy(
            finding.Component,
            "MONITORING RESTORED",
            $"{finding.Resource} · removed {finding.Reason}");
        Get<TextBlock>("DashboardPolicyStatusText").Text =
            "Monitoring restored for the selected finding.";
        RefreshPolicyProjection();
    }

    private async void StorageThresholdButton_OnClick(
        object? sender,
        RoutedEventArgs e)
    {
        var volume = SelectedStorageVolume();
        if (volume is null)
            return;

        await ConfigureStorageThresholdAsync(volume.MountPoint);
    }

    private void RestoreStorageThresholdButton_OnClick(
        object? sender,
        RoutedEventArgs e)
    {
        var volume = SelectedStorageVolume();
        if (volume is null)
            return;

        if (!_findingPolicies.ResetStorageThreshold(volume.MountPoint))
            return;

        _history.RecordPolicy(
            "Storage",
            "DEFAULT THRESHOLDS RESTORED",
            $"{volume.MountPoint} · 85/90/95% · free-space thresholds disabled");
        Get<TextBlock>("StoragePolicyStatusText").Text =
            $"Default policy restored for {volume.MountPoint}.";
        Get<TextBlock>("DashboardPolicyStatusText").Text =
            $"Default storage monitoring restored for {volume.MountPoint}.";
        RefreshPolicyProjection();
    }

    private async Task ConfigureStorageThresholdAsync(string mountPoint)
    {
        var result = await ShowStorageThresholdDialogAsync(mountPoint);
        if (result is null)
            return;

        if (result.Reset)
        {
            _findingPolicies.ResetStorageThreshold(mountPoint);
            _history.RecordPolicy(
                "Storage",
                "DEFAULT THRESHOLDS RESTORED",
                $"{mountPoint} · 85/90/95% · free-space thresholds disabled");
            Get<TextBlock>("StoragePolicyStatusText").Text =
                $"Default policy restored for {mountPoint}.";
            Get<TextBlock>("DashboardPolicyStatusText").Text =
                $"Default storage monitoring restored for {mountPoint}.";
        }
        else if (result.Policy is not null)
        {
            _findingPolicies.SetStorageThreshold(
                mountPoint,
                result.Policy);
            _history.RecordPolicy(
                "Storage",
                "CUSTOM THRESHOLDS SAVED",
                $"{mountPoint} · " +
                $"{result.Policy.WarningPercent}/" +
                $"{result.Policy.ErrorPercent}/" +
                $"{result.Policy.CriticalPercent}% · free GiB " +
                $"{result.Policy.WarningFreeGiB:0.##}/" +
                $"{result.Policy.ErrorFreeGiB:0.##}/" +
                $"{result.Policy.CriticalFreeGiB:0.##}");
            Get<TextBlock>("StoragePolicyStatusText").Text =
                $"Custom policy saved for {mountPoint}.";
            Get<TextBlock>("DashboardPolicyStatusText").Text =
                $"Custom storage policy active for {mountPoint}.";
        }

        RefreshPolicyProjection();
    }

    private void IntegrationsList_OnSelectionChanged(
        object? sender,
        SelectionChangedEventArgs e) =>
        PopulateIntegrationWorkspace();

    private void PopulateIntegrationWorkspace()
    {
        var selected =
            SelectedMediaIntegration();

        var name = Get<TextBlock>("IntegrationNameText");
        var runtime = Get<TextBlock>("IntegrationRuntimeText");
        var state = Get<TextBlock>("IntegrationStateText");
        var stateBorder = Get<Border>("IntegrationStateBorder");
        var kind = Get<TextBlock>("IntegrationKindText");
        var endpoint = Get<TextBlock>("IntegrationEndpointText");
        var role = Get<TextBlock>("IntegrationRoleText");
        var evidence = Get<TextBlock>("IntegrationEvidenceText");
        var findingsSummary =
            Get<TextBlock>("IntegrationFindingsSummaryText");
        var findingsText =
            Get<TextBlock>("IntegrationFindingsText");
        var open = Get<Button>("OpenIntegrationButton");
        var intelligence =
            Get<Button>("IntegrationFindingsButton");
        var actionStatus =
            Get<TextBlock>("IntegrationActionStatusText");

        if (selected is null)
        {
            name.Text = "Select an application";
            runtime.Text = "--";
            state.Text = "WAITING";
            state.Foreground =
                OpsPalette.Foreground(OpsSeverity.Info);
            stateBorder.Background =
                OpsPalette.Background(OpsSeverity.Info);
            kind.Text = "--";
            endpoint.Text = "--";
            role.Text = "--";
            evidence.Text =
                "Select a detected application to inspect its evidence.";
            findingsSummary.Text = "--";
            findingsText.Text = "No application selected.";
            open.IsEnabled = false;
            intelligence.IsEnabled = false;
            actionStatus.Text = "Select an application.";
            return;
        }

        var related = _policyEvaluation?.Active
            .Where(item =>
                MatchesIntegration(item, selected.Name))
            .ToArray() ??
            Array.Empty<OpsPolicyFinding>();

        var url = ResolveIntegrationUrl(selected);
        name.Text = selected.Name;
        runtime.Text = selected.State;
        state.Text =
            LinuxOpsAnalyzer.SeverityLabel(
                selected.Severity);
        state.Foreground =
            OpsPalette.Foreground(selected.Severity);
        stateBorder.Background =
            OpsPalette.Background(selected.Severity);
        kind.Text = selected.Kind;
        endpoint.Text = url ??
            (string.IsNullOrWhiteSpace(selected.Endpoint)
                ? "No verified web endpoint"
                : selected.Endpoint);
        role.Text = IntegrationRole(selected.Name);
        evidence.Text =
            IntegrationEvidenceSummary(selected);
        findingsSummary.Text = related.Length == 0
            ? "No active findings"
            : $"{related.Length} active";
        findingsText.Text = related.Length == 0
            ? "No active operational finding is associated with this application."
            : string.Join(
                Environment.NewLine + Environment.NewLine,
                related.Select(item =>
                    $"{item.Severity} · {item.Problem}" +
                    (string.IsNullOrWhiteSpace(item.NextStep)
                        ? string.Empty
                        : Environment.NewLine + item.NextStep)));
        open.IsEnabled = url is not null;
        intelligence.IsEnabled = related.Length > 0;
        actionStatus.Text = url is null
            ? "No verified local web interface is available."
            : "Ready to open the local interface.";
    }

    private async void OpenIntegrationButton_OnClick(
        object? sender,
        RoutedEventArgs e)
    {
        var selected =
            SelectedMediaIntegration();

        if (selected is null)
            return;

        var url = ResolveIntegrationUrl(selected);
        if (url is null)
            return;

        try
        {
            using var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = "xdg-open",
                    UseShellExecute = false,
                    CreateNoWindow = true
                }
            };
            process.StartInfo.ArgumentList.Add(url);
            process.Start();

            Get<TextBlock>("IntegrationActionStatusText").Text =
                $"Opened {url}";
            await Task.CompletedTask;
        }
        catch (Exception exception)
        {
            Get<TextBlock>("IntegrationActionStatusText").Text =
                $"Could not open interface: {exception.Message}";
        }
    }

    private void IntegrationFindingsButton_OnClick(
        object? sender,
        RoutedEventArgs e) =>
        Navigate("IntelligenceNav");

    private static bool MatchesIntegration(
        OpsPolicyFinding finding,
        string integrationName)
    {
        return new[]
        {
            finding.Component,
            finding.Resource,
            finding.Problem,
            finding.Evidence
        }.Any(value =>
            value?.Contains(
                integrationName,
                StringComparison.OrdinalIgnoreCase) == true);
    }

    private static string IntegrationEvidenceSummary(
        OpsIntegration integration)
    {
        if (integration.Kind.Equals(
                "Docker port inference",
                StringComparison.OrdinalIgnoreCase))
        {
            return
                $"{integration.Name} was identified from a published port mapping " +
                $"owned by container '{integration.Evidence}'.";
        }

        if (integration.Kind.Equals(
                "systemd",
                StringComparison.OrdinalIgnoreCase))
        {
            return
                $"{integration.Name} was verified through native systemd unit " +
                $"'{integration.Evidence}'.";
        }

        if (string.IsNullOrWhiteSpace(integration.Evidence))
            return "Detected without additional provider evidence.";

        return
            $"Detection evidence · {integration.Evidence}";
    }

    private static string IntegrationRole(string name)
    {
        if (name.Equals("Plex", StringComparison.OrdinalIgnoreCase) ||
            name.Equals("Jellyfin", StringComparison.OrdinalIgnoreCase) ||
            name.Equals("Emby", StringComparison.OrdinalIgnoreCase) ||
            name.Equals("Tautulli", StringComparison.OrdinalIgnoreCase))
        {
            return "Library and playback";
        }

        if (name.Equals("Sonarr", StringComparison.OrdinalIgnoreCase) ||
            name.Equals("Radarr", StringComparison.OrdinalIgnoreCase) ||
            name.Equals("Lidarr", StringComparison.OrdinalIgnoreCase) ||
            name.Equals("Readarr", StringComparison.OrdinalIgnoreCase) ||
            name.Equals("Whisparr", StringComparison.OrdinalIgnoreCase) ||
            name.Equals("Mylar3", StringComparison.OrdinalIgnoreCase))
        {
            return "Acquisition and import";
        }

        if (name.Equals("Prowlarr", StringComparison.OrdinalIgnoreCase))
            return "Discovery and indexers";

        if (name.Equals("SABnzbd", StringComparison.OrdinalIgnoreCase) ||
            name.Equals("qBittorrent", StringComparison.OrdinalIgnoreCase))
        {
            return "Downloads";
        }

        if (name.Equals("Decypharr", StringComparison.OrdinalIgnoreCase) ||
            name.Equals("Zurg", StringComparison.OrdinalIgnoreCase) ||
            name.Equals("Recyclarr", StringComparison.OrdinalIgnoreCase) ||
            name.Equals("Bazarr", StringComparison.OrdinalIgnoreCase) ||
            name.Equals("Tdarr", StringComparison.OrdinalIgnoreCase))
        {
            return "Processing and supporting services";
        }

        if (name.Equals("DUMB", StringComparison.OrdinalIgnoreCase))
            return "Stack orchestration";

        return "Detected integration";
    }

    private string? ResolveIntegrationUrl(
        OpsIntegration integration)
    {
        var overrideUrl =
            _mediaLauncherStore.ResolveUrl(
                integration.Name);

        if (!string.IsNullOrWhiteSpace(
                overrideUrl) &&
            Uri.TryCreate(
                overrideUrl,
                UriKind.Absolute,
                out var overrideUri) &&
            (overrideUri.Scheme == Uri.UriSchemeHttp ||
             overrideUri.Scheme == Uri.UriSchemeHttps))
        {
            return overrideUri.ToString();
        }

        var detectedPorts = Regex.Matches(
                integration.Endpoint ?? string.Empty,
                @"(?<!\d)\d{2,5}(?!\d)")
            .Select(match => int.TryParse(
                    match.Value,
                    out var port)
                ? port
                : 0)
            .Where(port => port is > 0 and <= 65535)
            .ToArray();

        var candidates = detectedPorts.Length > 0
            ? detectedPorts
            : KnownIntegrationPorts.TryGetValue(
                integration.Name,
                out var known)
                ? known
                : Array.Empty<int>();

        if (candidates.Length == 0)
            return null;

        var port = candidates[0];
        var suffix = integration.Name.Equals(
            "Plex",
            StringComparison.OrdinalIgnoreCase)
            ? "/web"
            : string.Empty;

        return $"http://{ActiveTargetUrlHost()}:{port}{suffix}";
    }

    private static string FormatPolicyFree(double value) =>
        value <= 0
            ? "disabled"
            : value >= 1024
                ? $"{value / 1024:0.##} TiB"
                : $"{value:0.##} GiB";

    private void ServicesList_OnSelectionChanged(
        object? sender,
        SelectionChangedEventArgs e)
    {
        UpdateActionButtons();
        UpdateServiceDetail();
    }

    private void SafeModeCheckBox_OnClick(
        object? sender,
        RoutedEventArgs e)
    {
        UpdateActionButtons();
        UpdatePlexOperationState();
        PopulateOperatorShell();
        PopulateControlPlaneFoundation();
    }

    private void UpdateServiceDetail()
    {
        var selected =
            Get<ListBox>("ServicesList").SelectedItem
            as ServiceSnapshot;

        if (selected is null)
        {
            Get<TextBlock>("ServiceSelectedNameText").Text =
                "No service selected";
            Get<TextBlock>("ServiceSelectedStateText").Text = "--";
            Get<TextBlock>("ServiceSelectedDescriptionText").Text =
                "Select a service to inspect its current state and unit-file policy.";
            Get<TextBlock>("ServiceSelectedPolicyText").Text = "--";
            return;
        }

        var severity =
            LinuxOpsAnalyzer.ServiceSeverity(selected);
        Get<TextBlock>("ServiceSelectedNameText").Text =
            selected.Unit;
        Get<TextBlock>("ServiceSelectedStateText").Text =
            $"{LinuxOpsAnalyzer.SeverityLabel(severity)} · " +
            $"{selected.ActiveState}/{selected.SubState}";
        Get<TextBlock>("ServiceSelectedDescriptionText").Text =
            selected.Description;
        Get<TextBlock>("ServiceSelectedPolicyText").Text =
            $"Unit-file state · {selected.UnitFileState}";
    }

    private void UpdateActionButtons()
    {
        var service =
            Get<ListBox>("ServicesList").SelectedItem
            is ServiceSnapshot;
        var safe =
            Get<CheckBox>("SafeModeCheckBox").IsChecked == true;
        var local = CanRunLocalMutations();

        Get<Button>("ServiceStartButton").IsEnabled =
            service && local;
        Get<Button>("ServiceStopButton").IsEnabled =
            service && local && !safe;
        Get<Button>("ServiceRestartButton").IsEnabled =
            service && local && !safe;

        UpdateDockerWorkspaceActionButtons();
    }

    private async void ServiceStartButton_OnClick(object? sender, RoutedEventArgs e) => await RunServiceActionAsync("start");
    private async void ServiceStopButton_OnClick(object? sender, RoutedEventArgs e) => await RunServiceActionAsync("stop");
    private async void ServiceRestartButton_OnClick(object? sender, RoutedEventArgs e) => await RunServiceActionAsync("restart");

    private async Task RunServiceActionAsync(string action)
    {
        if (Get<ListBox>("ServicesList").SelectedItem is not ServiceSnapshot service)
            return;
        if (!CanRunLocalMutations())
        {
            Get<TextBlock>("ServiceActionStatusText").Text =
                "Remote service mutations are disabled in the V4.2 provider foundation.";
            return;
        }
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
        _controlPlane.State.RecordActivity(
            "Action",
            _controlPlane.ActiveProfile.DisplayName,
            $"{action} {service.Unit}",
            result.Summary,
            "ServicesNav");
        Get<TextBlock>("ServiceActionStatusText").Text = result.Summary;
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
        PopulateHistoryV43();
    }

    private void LogsList_OnSelectionChanged(
        object? sender,
        SelectionChangedEventArgs e) =>
        PopulateReliableLogSelection();

    private async Task<TimeSpan?> ShowSnoozeDialogAsync()
    {
        var dialog = new Window
        {
            Title = "Snooze finding",
            Width = 500,
            Height = 310,
            CanResize = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Background = new SolidColorBrush(Color.Parse("#111113"))
        };

        var customHours = new TextBox
        {
            Width = 110,
            Text = "12",
            PlaceholderText = "Hours"
        };
        var validation = new TextBlock
        {
            Foreground = OpsPalette.Foreground(OpsSeverity.Error),
            TextWrapping = TextWrapping.Wrap
        };

        var oneHour = new Button { Content = "1 hour" };
        var oneDay = new Button { Content = "24 hours" };
        var oneWeek = new Button { Content = "7 days" };
        var custom = new Button { Content = "Custom hours" };
        var cancel = new Button { Content = "Cancel" };

        oneHour.Click += (_, _) => dialog.Close("1");
        oneDay.Click += (_, _) => dialog.Close("24");
        oneWeek.Click += (_, _) => dialog.Close("168");
        cancel.Click += (_, _) => dialog.Close(string.Empty);
        custom.Click += (_, _) =>
        {
            if (!double.TryParse(
                    customHours.Text,
                    NumberStyles.Float,
                    CultureInfo.InvariantCulture,
                    out var hours) ||
                hours <= 0 ||
                hours > 8760)
            {
                validation.Text =
                    "Enter a value greater than 0 and no more than 8760 hours.";
                return;
            }

            dialog.Close(hours.ToString(CultureInfo.InvariantCulture));
        };

        dialog.Content = new Border
        {
            Padding = new Thickness(24),
            Child = new StackPanel
            {
                Spacing = 16,
                Children =
                {
                    new TextBlock
                    {
                        Text = "Snooze selected finding",
                        FontSize = 20,
                        FontWeight = FontWeight.SemiBold
                    },
                    new TextBlock
                    {
                        Text = "The finding returns automatically when the period expires. Critical conditions are never suppressed.",
                        Classes = { "muted" },
                        TextWrapping = TextWrapping.Wrap
                    },
                    new StackPanel
                    {
                        Orientation = Orientation.Horizontal,
                        Spacing = 8,
                        Children = { oneHour, oneDay, oneWeek }
                    },
                    new StackPanel
                    {
                        Orientation = Orientation.Horizontal,
                        Spacing = 8,
                        Children = { customHours, custom }
                    },
                    validation,
                    new StackPanel
                    {
                        Orientation = Orientation.Horizontal,
                        HorizontalAlignment = HorizontalAlignment.Right,
                        Children = { cancel }
                    }
                }
            }
        };

        var result = await dialog.ShowDialog<string>(this);
        if (!double.TryParse(
                result,
                NumberStyles.Float,
                CultureInfo.InvariantCulture,
                out var selectedHours) ||
            selectedHours <= 0)
        {
            return null;
        }

        return TimeSpan.FromHours(selectedHours);
    }

    private async Task<StorageThresholdDialogResult?>
        ShowStorageThresholdDialogAsync(string mountPoint)
    {
        var current = _findingPolicies.GetStorageThreshold(mountPoint);

        TextBox NewBox(double value) => new()
        {
            Width = 100,
            Text = value.ToString("0.##", CultureInfo.InvariantCulture)
        };

        var warningPercent = NewBox(current.WarningPercent);
        var errorPercent = NewBox(current.ErrorPercent);
        var criticalPercent = NewBox(current.CriticalPercent);
        var warningFree = NewBox(current.WarningFreeGiB);
        var errorFree = NewBox(current.ErrorFreeGiB);
        var criticalFree = NewBox(current.CriticalFreeGiB);

        StackPanel ThresholdRow(
            string label,
            TextBox percent,
            TextBox free) =>
            new()
            {
                Orientation = Orientation.Horizontal,
                Spacing = 12,
                Children =
                {
                    new TextBlock
                    {
                        Text = label,
                        Width = 90,
                        VerticalAlignment = VerticalAlignment.Center,
                        FontWeight = FontWeight.SemiBold
                    },
                    percent,
                    free
                }
            };

        bool TryBuildPolicy(
            out StorageThresholdPolicy? policy,
            out string error)
        {
            policy = null;
            error = string.Empty;

            bool Parse(TextBox box, out double value) =>
                double.TryParse(
                    box.Text,
                    NumberStyles.Float,
                    CultureInfo.InvariantCulture,
                    out value);

            if (!Parse(warningPercent, out var warningPercentValue) ||
                !Parse(errorPercent, out var errorPercentValue) ||
                !Parse(criticalPercent, out var criticalPercentValue) ||
                !Parse(warningFree, out var warningFreeValue) ||
                !Parse(errorFree, out var errorFreeValue) ||
                !Parse(criticalFree, out var criticalFreeValue))
            {
                error = "All threshold fields must contain numbers.";
                return false;
            }

            if (warningPercentValue % 1 != 0 ||
                errorPercentValue % 1 != 0 ||
                criticalPercentValue % 1 != 0)
            {
                error = "Percentage thresholds must be whole numbers.";
                return false;
            }

            policy = new StorageThresholdPolicy
            {
                WarningPercent = (int)warningPercentValue,
                ErrorPercent = (int)errorPercentValue,
                CriticalPercent = (int)criticalPercentValue,
                WarningFreeGiB = warningFreeValue,
                ErrorFreeGiB = errorFreeValue,
                CriticalFreeGiB = criticalFreeValue
            };

            if (policy.WarningPercent is < 1 or > 100 ||
                policy.ErrorPercent is < 1 or > 100 ||
                policy.CriticalPercent is < 1 or > 100)
            {
                error = "Percentages must be between 1 and 100.";
                return false;
            }

            if (!(policy.WarningPercent < policy.ErrorPercent &&
                  policy.ErrorPercent < policy.CriticalPercent))
            {
                error =
                    "Percentages must increase from warning to error to critical.";
                return false;
            }

            if (policy.WarningFreeGiB < 0 ||
                policy.ErrorFreeGiB < 0 ||
                policy.CriticalFreeGiB < 0)
            {
                error = "Remaining-space values cannot be negative.";
                return false;
            }

            return true;
        }

        void LoadPolicy(StorageThresholdPolicy policy)
        {
            warningPercent.Text = policy.WarningPercent.ToString(
                CultureInfo.InvariantCulture);
            errorPercent.Text = policy.ErrorPercent.ToString(
                CultureInfo.InvariantCulture);
            criticalPercent.Text = policy.CriticalPercent.ToString(
                CultureInfo.InvariantCulture);
            warningFree.Text = policy.WarningFreeGiB.ToString(
                "0.##",
                CultureInfo.InvariantCulture);
            errorFree.Text = policy.ErrorFreeGiB.ToString(
                "0.##",
                CultureInfo.InvariantCulture);
            criticalFree.Text = policy.CriticalFreeGiB.ToString(
                "0.##",
                CultureInfo.InvariantCulture);
        }

        var dialog = new Window
        {
            Title = $"Storage policy · {mountPoint}",
            Width = 580,
            Height = 500,
            CanResize = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Background = new SolidColorBrush(Color.Parse("#111113"))
        };

        var validation = new TextBlock
        {
            Foreground = OpsPalette.Foreground(OpsSeverity.Error),
            TextWrapping = TextWrapping.Wrap
        };
        var preset = new Button { Content = "Large media preset" };
        var defaults = new Button { Content = "Restore defaults" };
        var cancel = new Button { Content = "Cancel" };
        var save = new Button { Content = "Save" };
        save.Classes.Add("primary");

        preset.Click += (_, _) =>
            LoadPolicy(StorageThresholdPolicy.LargeMediaPreset());
        defaults.Click += (_, _) => dialog.Close("reset");
        cancel.Click += (_, _) => dialog.Close(string.Empty);
        save.Click += (_, _) =>
        {
            if (!TryBuildPolicy(out _, out var error))
            {
                validation.Text = error;
                return;
            }

            dialog.Close("save");
        };

        dialog.Content = new Border
        {
            Padding = new Thickness(24),
            Child = new StackPanel
            {
                Spacing = 14,
                Children =
                {
                    new TextBlock
                    {
                        Text = mountPoint,
                        FontSize = 20,
                        FontWeight = FontWeight.SemiBold,
                        TextWrapping = TextWrapping.Wrap
                    },
                    new TextBlock
                    {
                        Text = "A threshold triggers when either used percentage is reached or remaining space falls at or below the configured GiB value. Set free GiB to 0 to disable that side of the rule.",
                        Classes = { "muted" },
                        TextWrapping = TextWrapping.Wrap
                    },
                    new StackPanel
                    {
                        Orientation = Orientation.Horizontal,
                        Spacing = 12,
                        Children =
                        {
                            new TextBlock
                            {
                                Text = "Severity",
                                Width = 90,
                                Classes = { "eyebrow" }
                            },
                            new TextBlock
                            {
                                Text = "Used %",
                                Width = 100,
                                Classes = { "eyebrow" }
                            },
                            new TextBlock
                            {
                                Text = "Free GiB",
                                Width = 100,
                                Classes = { "eyebrow" }
                            }
                        }
                    },
                    ThresholdRow("Warning", warningPercent, warningFree),
                    ThresholdRow("Error", errorPercent, errorFree),
                    ThresholdRow("Critical", criticalPercent, criticalFree),
                    validation,
                    new StackPanel
                    {
                        Orientation = Orientation.Horizontal,
                        Spacing = 8,
                        Children = { preset, defaults }
                    },
                    new StackPanel
                    {
                        Orientation = Orientation.Horizontal,
                        HorizontalAlignment = HorizontalAlignment.Right,
                        Spacing = 8,
                        Children = { cancel, save }
                    }
                }
            }
        };

        var result = await dialog.ShowDialog<string>(this);
        if (result == "reset")
            return new StorageThresholdDialogResult(true, null);
        if (result != "save")
            return null;
        if (!TryBuildPolicy(out var policy, out _))
            return null;

        return new StorageThresholdDialogResult(false, policy);
    }

    private sealed record StorageThresholdDialogResult(
        bool Reset,
        StorageThresholdPolicy? Policy);

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
        Get<TextBlock>("FooterConnectionText").Text = state;
        Get<TextBlock>("OverviewControlPlaneText").Text = state;
    }

    private static bool Matches(string? filter, params string?[] values)
    {
        if (string.IsNullOrWhiteSpace(filter)) return true;
        return values.Any(value => value?.Contains(filter, StringComparison.OrdinalIgnoreCase) == true);
    }

    private sealed record CommandPaletteItem(
        string Title,
        string Subtitle,
        string Kind,
        string NavigationName);

    private sealed record StorageDisplayRow(
        StorageVolumeSnapshot Snapshot,
        string Source,
        string FileSystem,
        string Size,
        string Used,
        string Available,
        string PercentUsed,
        string PolicyLabel,
        string StatusLabel,
        string MountPoint);

    private sealed record NavigationTarget(
        string PageName,
        string Title,
        string Subtitle);
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
