using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Data;
using System.Windows.Threading;
using GraveOps.App.Models;
using GraveOps.App.Views;
using MessageBox = GraveOps.App.Windows.GraveOpsMessageBox;

namespace GraveOps.App;

public partial class MainWindow : Window
{
    private readonly Dictionary<string, UserControl> _pages = new();
    private string _currentKey = "Dashboard";
    private bool _suppressStartupPageMemory;
    private bool _syncingTarget;
    private bool _allowRealClose;
    private string _lastMonitorState = "UNKNOWN";
    private EnvironmentOverviewSnapshot? _lastEnvironmentSnapshot;
    private readonly DispatcherTimer _shellTimer = new() { Interval = TimeSpan.FromSeconds(1) };
    private int _activityUnread;
    private static readonly HashSet<string> ApplicationPageKeys = new(StringComparer.OrdinalIgnoreCase)
    {
        "Plex", "Tautulli", "Sonarr", "Radarr", "Lidarr", "Prowlarr", "Bazarr", "Seerr",
        "SABnzbd", "qBittorrent", "Recyclarr", "Profilarr", "Tdarr", "Maintainerr"
    };

    public MainWindow()
    {
        InitializeComponent();
        PreviewKeyDown += MainWindow_PreviewKeyDown;
        Closing += MainWindow_Closing;
        _shellTimer.Tick += ShellTimer_Tick;
        _suppressStartupPageMemory = true;
        try
        {
            NavigateByKey("Dashboard");
        }
        finally
        {
            _suppressStartupPageMemory = false;
        }

        Loaded += (_, _) =>
        {
            GraveOps.App.Services.LiveAnalyticsHub.Current.Start(
                System.TimeSpan.FromSeconds(15));

            GraveOps.App.Services.LiveAnalyticsHub.Current.SetMinimized(
                WindowState ==
                    System.Windows.WindowState.Minimized);

            StateChanged +=
                (_, _) =>
                    GraveOps.App.Services.LiveAnalyticsHub.Current.SetMinimized(
                        WindowState ==
                            System.Windows.WindowState.Minimized);

            Closed +=
                (_, _) =>
                    GraveOps.App.Services.LiveAnalyticsHub.Current.Stop();
            LoadGlobalTargets();
            RefreshNavigationCapabilities();
            LoadQuickBar();
            ActivityList.ItemsSource = App.Services.Activity.Recent;
            JobsList.ItemsSource = App.Services.Jobs.Items;
            NotificationList.ItemsSource = App.Services.Notifications.History;
            App.Services.Jobs.Changed += Jobs_Changed;
            App.Services.Activity.ActivityAdded += Activity_ActivityAdded;
            App.Services.Notifications.Changed += Notifications_Changed;
            App.Services.Notifications.OpenRequested += Notifications_OpenRequested;
            App.Services.Notifications.ExitRequested += Notifications_ExitRequested;
            ApplyDesktopSettings();
            _shellTimer.Start();
            RefreshShellStatus();
            if (App.Services.Config.Current.Servers.Count == 0 && !App.Services.Config.Current.Settings.FirstRunCompleted)
            {
                var wizard = new SetupWizardWindow { Owner = this };
                wizard.ShowDialog();
                if (wizard.SavedProfile)
                {
                    LoadGlobalTargets();
                    RefreshNavigationCapabilities();
                }
            }
            App.Services.Context.TargetChanged += Context_TargetChanged;
            App.Services.Navigation.NavigationRequested += Navigation_NavigationRequested;
            App.Services.Monitor.StateChanged += Monitor_StateChanged;
            App.Services.Environment.Updated += Environment_Updated;
            App.Services.Monitor.Start();
            _ = VerifyUnverifiedHostsAsync();
        };

        Closed += (_, _) =>
        {
            App.Services.Context.TargetChanged -= Context_TargetChanged;
            App.Services.Navigation.NavigationRequested -= Navigation_NavigationRequested;
            App.Services.Monitor.StateChanged -= Monitor_StateChanged;
            App.Services.Environment.Updated -= Environment_Updated;
            App.Services.Jobs.Changed -= Jobs_Changed;
            App.Services.Activity.ActivityAdded -= Activity_ActivityAdded;
            App.Services.Notifications.Changed -= Notifications_Changed;
            App.Services.Notifications.OpenRequested -= Notifications_OpenRequested;
            App.Services.Notifications.ExitRequested -= Notifications_ExitRequested;
            _shellTimer.Stop();
            App.Services.Monitor.Dispose();
            App.Services.Notifications.Dispose();
        };
    }

    public void RefreshEnvironmentChrome()
    {
        App.Services.Environment.Invalidate();
        LoadGlobalTargets();
        RefreshNavigationCapabilities();
        LoadQuickBar();
    }

    private void LoadGlobalTargets()
    {
        _syncingTarget = true;
        GlobalTargetCombo.ItemsSource = null;
        GlobalTargetCombo.ItemsSource = App.Services.Config.Current.Servers;
        GlobalTargetCombo.SelectedItem = App.Services.Context.Current;
        GlobalTargetCombo.IsEnabled = App.Services.Config.Current.Servers.Count > 0;
        _syncingTarget = false;
    }

    private void GlobalTargetCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_syncingTarget) return;
        App.Services.Context.Select(GlobalTargetCombo.SelectedItem as ServerProfile);
    }

    private void Context_TargetChanged(ServerProfile? server)
    {
        Dispatcher.Invoke(() =>
        {
            _syncingTarget = true;
            GlobalTargetCombo.SelectedItem = server;
            _syncingTarget = false;

            // Target changes are a hard ownership boundary. Clear shared host-bound
            // telemetry before any replacement page can paint from the previous host.
            GraveOps.App.Services.LiveAnalyticsHub.Current.OnTargetChanged(server);

            RefreshNavigationCapabilities();

            // Cached views can retain hidden selectors and other host-specific state.
            // Recreate the destination page (and every other cached view) on a target
            // change so a fallback to Dashboard can never resurrect the prior host.
            var nextKey = IsPageVisibleForCurrentTarget(_currentKey) ? _currentKey : "Dashboard";
            _pages.Clear();
            NavigateByKey(nextKey);
            App.Services.Activity.Record(
                "Active target changed",
                server is null ? "No global server selected." : $"Now operating against {server.Name}.",
                ActivityLevel.Info,
                serverId: server?.Id,
                deepLink: server is null ? "page:Servers" : $"server:{server.Id}");
        });
    }

    private async Task VerifyUnverifiedHostsAsync()
    {
        foreach (var profile in App.Services.Config.Current.Servers
                     .Where(x => x.LastIntegrationDiscoveryUtc is null)
                     .ToArray())
        {
            try
            {
                if (profile.ConnectionKind == HostConnectionKind.LocalWindows)
                {
                    var result = await App.Services.WindowsDiscovery.DiscoverAsync();
                    profile.DetectedOperatingSystem = result.Host.OperatingSystem;
                    AddHostModule(profile, "LocalWindows");
                    AddHostModule(profile, "Storage");
                    AddHostModule(profile, "LocalHttp");
                    if (result.Host.Capabilities.HasFlag(HostCapability.Docker))
                        AddHostModule(profile, "Docker");
                    App.Services.IntegrationAssignments.ApplyVerified(
                        profile,
                        result.Integrations,
                        "Native Windows verified discovery");
                }
                else if (profile.ConnectionKind == HostConnectionKind.RemoteLinux)
                {
                    var host = await App.Services.Hosts.Resolve(profile).ProbeAsync(profile);
                    profile.DetectedOperatingSystem = host.OperatingSystem;
                    AddHostModule(profile, "RemoteLinux");
                    if (host.Capabilities.HasFlag(HostCapability.Docker)) AddHostModule(profile, "Docker");
                    if (host.Capabilities.HasFlag(HostCapability.Systemd)) AddHostModule(profile, "Systemd");
                    if (host.Capabilities.HasFlag(HostCapability.Smart)) AddHostModule(profile, "SMART");

                    var result = await App.Services.LinuxDiscovery.DiscoverAsync(profile, host);
                    App.Services.IntegrationAssignments.ApplyVerified(
                        profile,
                        result.Integrations,
                        "Remote Linux verified discovery");
                }

                App.Services.Activity.Record(
                    "Environment capability verification",
                    profile.IntegrationDiscoverySummary,
                    ActivityLevel.Success,
                    serverId: profile.Id,
                    deepLink: "page:Servers");
            }
            catch (Exception ex)
            {
                App.Services.Activity.Record(
                    "Environment capability verification deferred",
                    $"{profile.Name}: {ex.Message}",
                    ActivityLevel.Warning,
                    serverId: profile.Id,
                    deepLink: "page:Servers");
            }
        }

        RefreshNavigationCapabilities();
        LoadQuickBar();
    }

    private static void AddHostModule(ServerProfile profile, string module)
    {
        if (!profile.EnabledModules.Any(x => x.Equals(module, StringComparison.OrdinalIgnoreCase)))
            profile.EnabledModules.Add(module);
    }

    private void Monitor_StateChanged(string state, bool healthy)
    {
        Dispatcher.Invoke(() =>
        {
            _lastMonitorState = state;
            RefreshControlPlaneState();
        });
    }

    private void RefreshControlPlaneState()
    {
        var snapshot = _lastEnvironmentSnapshot ?? App.Services.Environment.Current;
        string state;
        System.Windows.Media.Brush brush;

        if (snapshot is { HostCount: > 0 })
        {
            // OFFLINE is reserved for a genuinely unreachable environment. A single
            // stale helper/monitor signal must not contradict working SSH/API telemetry.
            if (snapshot.OnlineHostCount == 0)
            {
                state = "OFFLINE";
                brush = (System.Windows.Media.Brush)FindResource("Danger");
            }
            else if (snapshot.State is EnvironmentHealthState.Attention or EnvironmentHealthState.Offline)
            {
                state = "ATTENTION";
                brush = (System.Windows.Media.Brush)FindResource("Warn");
            }
            else
            {
                state = "HEALTHY";
                brush = (System.Windows.Media.Brush)FindResource("Success");
            }
        }
        else
        {
            state = _lastMonitorState switch
            {
                "OFFLINE" => "DEGRADED",
                "ATTENTION" => "ATTENTION",
                "HEALTHY" => "HEALTHY",
                _ => "READY"
            };
            brush = state switch
            {
                "HEALTHY" => (System.Windows.Media.Brush)FindResource("Success"),
                "ATTENTION" or "DEGRADED" => (System.Windows.Media.Brush)FindResource("Warn"),
                _ => (System.Windows.Media.Brush)FindResource("Muted")
            };
        }

        ConnectionText.Text = state;
        ConnectionText.Foreground = brush;
        ConnectionDot.Fill = brush;
    }

    private void Navigate(string key, string title, string subtitle, Func<UserControl> factory)
    {
        _currentKey = key;
        if (!_pages.TryGetValue(key, out var page))
        {
            try
            {
                page = factory();
                _pages[key] = page;
            }
            catch (System.Exception ex)
            {
                MessageBox.Show(
                    "Unable to open this GraveOps page.\n\n" + ex.Message,
                    "GraveOps page error",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
                return;
            }
        }

        PageTitle.Text = title;
        PageSubtitle.Text = subtitle;
        PageHost.Content = page;
        MarkNavigation(key);
        if (!_suppressStartupPageMemory &&
            !string.Equals(
                App.Services.Config.Current.Settings.LastPageKey,
                key,
                StringComparison.Ordinal))
        {
            App.Services.Config.Current.Settings.LastPageKey = key;
            App.Services.Config.Save();
        }
        RefreshShellStatus();
    }

    private void NavigateByKey(string key)
    {
        if (ApplicationPageKeys.Contains(key))
            EnsureApplicationOwner(key);

        switch (key)
        {
            case "Dashboard": Navigate(key, "Dashboard", "Interactive environment health, ownership and active-host operations", () => new DashboardView()); break;
            case "Intelligence": Navigate(key, "Intelligence", "Fleet impact, root cause, dependencies and contextual next actions", () => new ControlPlaneIntelligenceView()); break;
            case "Lifecycle": Navigate(key, "Media Lifecycle", "Track active media across acquisition, download, import, processing and library stages", () => new MediaLifecycleView()); break;
            case "History": Navigate(key, "History & Incidents", "Fleet health transitions, GraveOps activity and incident replay", () => new FleetHistoryView()); break;
            case "Servers": Navigate(key, "Servers", "Local and remote host profiles, capabilities and secure credentials", () => new ServersView()); break;
            case "Applications": Navigate(key, "Media Hub", "Fleet health, launcher configuration and all media applications", () => new ApplicationsView()); break;
            case "Plex": Navigate(key, "Plex", "Playback health, sessions, logs and verified Plex operations", () => new MediaServiceView("Plex")); break;
            case "Tautulli": Navigate(key, "Tautulli", "Plex analytics, activity and historical visibility", () => new IntegrationView("Tautulli")); break;
            case "Kometa": Navigate(key, "Kometa", "Plex collections, overlays, playlists and metadata automation", () => new IntegrationView("Kometa")); break;
            case "Sonarr": Navigate(key, "Sonarr", "Sonarr + Sonarr Debrid health, queues and tools", () => new MediaServiceView("Sonarr")); break;
            case "Radarr": Navigate(key, "Radarr", "Radarr + Radarr Debrid health, queues and tools", () => new MediaServiceView("Radarr")); break;
            case "Lidarr": Navigate(key, "Lidarr", "Music acquisition health, queues and tools", () => new MediaServiceView("Lidarr")); break;
            case "Prowlarr": Navigate(key, "Prowlarr", "Indexer health, application access and diagnostics", () => new MediaServiceView("Prowlarr")); break;
            case "Bazarr": Navigate(key, "Bazarr", "Subtitle automation health and owner routing", () => new IntegrationView("Bazarr")); break;
            case "Seerr": Navigate(key, "Seerr", "Request service health and media workflow ownership", () => new IntegrationView("Seerr")); break;
            case "SABnzbd": Navigate(key, "SABnzbd", "Usenet analytics, queue progress, remaining time and recent history", () => new DownloadClientView("SABnzbd")); break;
            case "qBittorrent": Navigate(key, "qBittorrent", "Torrent analytics, progress, ETA, seeding and protected local API telemetry", () => new DownloadClientView("qBittorrent")); break;
            case "Recyclarr": Navigate(key, "Recyclarr", "TRaSH-backed quality policy, discovery and safe preview operations", () => new IntegrationView("Recyclarr")); break;
            case "Profilarr": Navigate(key, "Profilarr", "Sonarr/Radarr configuration management and deployment", () => new IntegrationView("Profilarr")); break;
            case "autobrr": Navigate(key, "autobrr", "Release automation, IRC/RSS filtering and acquisition handoff", () => new IntegrationView("autobrr")); break;
            case "Unpackerr": Navigate(key, "Unpackerr", "Archive extraction runtime and import-pipeline health", () => new IntegrationView("Unpackerr")); break;
            case "Cleanuparr": Navigate(key, "Cleanuparr", "Queue cleanup, replacement search and download hygiene", () => new IntegrationView("Cleanuparr")); break;
            case "Tdarr": Navigate(key, "Tdarr", "Distributed media processing and transcoding health", () => new IntegrationView("Tdarr")); break;
            case "Maintainerr": Navigate(key, "Maintainerr", "Media lifecycle and retention automation health", () => new IntegrationView("Maintainerr")); break;
            case "Downloads": NavigateByKey("SABnzbd"); break;
            case "Terminal": Navigate(key, "Terminal", "PowerShell, CMD and SSH sessions in one workspace", () => new TerminalView()); break;
            case "Services": Navigate(key, "Services & Actions", "Safe one-click operational commands with confirmation tiers", () => new ActionsView()); break;
            case "Docker": Navigate(key, "Docker", "Container status, restart counts, health and logs", () => new DockerView()); break;
            case "Storage": Navigate(key, "Storage", "Mounts, free space, SMART and media drive status", () => new StorageView()); break;
            case "PiHole": Navigate(key, "Pi-hole", "DNS status and blocking controls over saved SSH", () => new PiHoleView()); break;
            case "Backups": Navigate(key, "Backups", "Provider-neutral backup readiness, schedules and protected actions", () => new BackupsView()); break;
            case "Logs": Navigate(key, "Logs", "Central log viewer with presets and terminal handoff", () => new LogsView()); break;
            case "Files": Navigate(key, "Files / SFTP", "Browse, download and upload remote files over SSH", () => new FilesView()); break;
            case "Scripts": Navigate(key, "Script Library", "Reusable commands and custom automation", () => new ScriptsView()); break;
            case "Settings": Navigate(key, "Settings", "Appearance, refresh behavior and local configuration", () => new SettingsView()); break;
            case "Updates": Navigate(key, "Update Center", "Read-only package and application update inventory", () => new UpdateCenterView()); break;
            default: NavigateByKey("Dashboard"); break;
        }
    }

    private void MarkNavigation(string key)
    {
        DashboardNav.IsChecked = key == "Dashboard";
        ServersNav.IsChecked = key == "Servers";
        AppsNav.IsChecked = key == "Applications";
        PlexNav.IsChecked = key == "Plex";
        TautulliNav.IsChecked = key == "Tautulli";
        KometaNav.IsChecked = key == "Kometa";
        SonarrNav.IsChecked = key == "Sonarr";
        RadarrNav.IsChecked = key == "Radarr";
        LidarrNav.IsChecked = key == "Lidarr";
        ProwlarrNav.IsChecked = key == "Prowlarr";
        BazarrNav.IsChecked = key == "Bazarr";
        SeerrNav.IsChecked = key == "Seerr";
        SabnzbdNav.IsChecked = key == "SABnzbd";
        QbittorrentNav.IsChecked = key == "qBittorrent";
        RecyclarrNav.IsChecked = key == "Recyclarr";
        ProfilarrNav.IsChecked = key == "Profilarr";
        AutobrrNav.IsChecked = key == "autobrr";
        UnpackerrNav.IsChecked = key == "Unpackerr";
        CleanuparrNav.IsChecked = key == "Cleanuparr";
        TdarrNav.IsChecked = key == "Tdarr";
        MaintainerrNav.IsChecked = key == "Maintainerr";
        IntelligenceNav.IsChecked = key == "Intelligence";
        LifecycleOverviewNav.IsChecked = key == "Lifecycle";
        HistoryNav.IsChecked = key == "History";
        TerminalNav.IsChecked = key == "Terminal";
        ServicesNav.IsChecked = key == "Services";
        DockerNav.IsChecked = key == "Docker";
        StorageNav.IsChecked = key == "Storage";
        PiHoleNav.IsChecked = key == "PiHole";
        BackupsNav.IsChecked = key == "Backups";
        LogsNav.IsChecked = key == "Logs";
        FilesNav.IsChecked = key == "Files";
        ScriptsNav.IsChecked = key == "Scripts";
        SettingsNav.IsChecked = key == "Settings";
        UpdatesNav.IsChecked = key == "Updates";
        EnsureNavigationGroupVisibleForPage(key);
    }


    private void RefreshNavigationCapabilities()
    {
        if (!IsLoaded && MediaSectionHeader is null)
            return;

        var server = App.Services.Context.Current ?? App.Services.Config.GetSelectedServer();
        var verifiedFleetApps = App.Services.Config.Current.Applications
            .Where(x => x.DiscoveryVerified)
            .ToArray();
        var currentApps = server is null
            ? Array.Empty<ManagedApp>()
            : verifiedFleetApps.Where(x => x.ServerId == server.Id).ToArray();

        bool FleetHas(string name) => verifiedFleetApps.Any(x => x.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
        bool CurrentHas(string name) => currentApps.Any(x => x.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
        static Visibility V(bool value) => value ? Visibility.Visible : Visibility.Collapsed;

        // Application navigation is environment-wide. Selecting an app automatically
        // activates a host that owns that verified capability.
        PlexNav.Visibility = V(FleetHas("Plex"));
        TautulliNav.Visibility = V(FleetHas("Tautulli"));
        KometaNav.Visibility = V(FleetHas("Kometa"));
        SonarrNav.Visibility = V(FleetHas("Sonarr"));
        RadarrNav.Visibility = V(FleetHas("Radarr"));
        LidarrNav.Visibility = V(FleetHas("Lidarr"));
        ProwlarrNav.Visibility = V(FleetHas("Prowlarr"));
        BazarrNav.Visibility = V(FleetHas("Bazarr"));
        SeerrNav.Visibility = V(FleetHas("Seerr"));
        SabnzbdNav.Visibility = V(FleetHas("SABnzbd"));
        QbittorrentNav.Visibility = V(FleetHas("qBittorrent"));
        RecyclarrNav.Visibility = V(FleetHas("Recyclarr"));
        ProfilarrNav.Visibility = V(FleetHas("Profilarr"));
        AutobrrNav.Visibility = V(FleetHas("autobrr"));
        UnpackerrNav.Visibility = V(FleetHas("Unpackerr"));
        CleanuparrNav.Visibility = V(FleetHas("Cleanuparr"));
        TdarrNav.Visibility = V(FleetHas("Tdarr"));
        MaintainerrNav.Visibility = V(FleetHas("Maintainerr"));

        var hasLibrary = PlexNav.Visibility == Visibility.Visible || TautulliNav.Visibility == Visibility.Visible || KometaNav.Visibility == Visibility.Visible;
        var hasAcquisition = new[] { SonarrNav, RadarrNav, LidarrNav, ProwlarrNav, BazarrNav, SeerrNav }
            .Any(x => x.Visibility == Visibility.Visible);
        var hasDownloads = SabnzbdNav.Visibility == Visibility.Visible || QbittorrentNav.Visibility == Visibility.Visible;
        var hasAutomation = new[] { RecyclarrNav, ProfilarrNav, AutobrrNav, UnpackerrNav, CleanuparrNav }
            .Any(x => x.Visibility == Visibility.Visible);
        var hasProcessing = TdarrNav.Visibility == Visibility.Visible;
        var hasLifecycle = MaintainerrNav.Visibility == Visibility.Visible;
        var hasMedia = hasLibrary || hasAcquisition || hasDownloads || hasAutomation || hasProcessing || hasLifecycle || verifiedFleetApps.Length > 0;

        MediaSectionHeader.Visibility = V(hasMedia);
        AppsNav.Visibility = V(hasMedia);
        LibraryGroupButton.Visibility = V(hasLibrary);
        AcquisitionGroupButton.Visibility = V(hasAcquisition);
        DownloadsGroupButton.Visibility = V(hasDownloads);
        AutomationGroupButton.Visibility = V(hasAutomation);
        ProcessingGroupButton.Visibility = V(hasProcessing);
        LifecycleGroupButton.Visibility = V(hasLifecycle);

        ApplyNavigationGroupState("Library", LibraryGroupButton, LibraryNavGroup, LibraryGroupGlyph);
        ApplyNavigationGroupState("Acquisition", AcquisitionGroupButton, AcquisitionNavGroup, AcquisitionGroupGlyph);
        ApplyNavigationGroupState("Downloads", DownloadsGroupButton, DownloadsNavGroup, DownloadsGroupGlyph);
        ApplyNavigationGroupState("Automation", AutomationGroupButton, AutomationNavGroup, AutomationGroupGlyph);
        ApplyNavigationGroupState("Processing", ProcessingGroupButton, ProcessingNavGroup, ProcessingGroupGlyph);
        ApplyNavigationGroupState("Lifecycle", LifecycleGroupButton, LifecycleNavGroup, LifecycleGroupGlyph);

        RefreshNavigationAttention(App.Services.Environment.Current);

        var hasTarget = server is not null;
        var isRemoteLinux = server?.ConnectionKind == HostConnectionKind.RemoteLinux;
        bool HasModule(string name) =>
            server?.EnabledModules.Any(x => x.Equals(name, StringComparison.OrdinalIgnoreCase)) == true;

        // Infrastructure navigation is capability-driven. Pages that already
        // support Windows stay visible on Windows instead of being hidden by
        // an old Remote-Linux-only gate.
        ServicesNav.Visibility = V(hasTarget);
        DockerNav.Visibility = V(hasTarget && HasModule("Docker"));
        StorageNav.Visibility = V(hasTarget);
        PiHoleNav.Visibility = V(isRemoteLinux &&
            (CurrentHas("Pi-hole") ||
             server?.Role.Contains("Pi-hole", StringComparison.OrdinalIgnoreCase) == true));
        BackupsNav.Visibility = V(hasTarget);
        TerminalNav.Visibility = V(hasTarget);

        LogsNav.Visibility = V(hasTarget);
        FilesNav.Visibility = V(isRemoteLinux);
        ScriptsNav.Visibility = V(isRemoteLinux);
        UpdatesNav.Visibility = V(hasTarget);
    }

    private void Environment_Updated(EnvironmentOverviewSnapshot snapshot)
    {
        Dispatcher.Invoke(() =>
        {
            _lastEnvironmentSnapshot = snapshot;
            RefreshNavigationAttention(snapshot);
            RefreshControlPlaneState();
        });
    }

    private void RefreshNavigationAttention(EnvironmentOverviewSnapshot? snapshot)
    {
        static Visibility V(bool value) => value ? Visibility.Visible : Visibility.Collapsed;

        bool NeedsAttention(params string[] names) => snapshot?.Hosts
            .SelectMany(x => x.Apps)
            .Any(x => names.Any(name => x.Name.Equals(name, StringComparison.OrdinalIgnoreCase)) &&
                      x.State is EnvironmentHealthState.Attention or EnvironmentHealthState.Offline) == true;

        bool HasOffline(params string[] names) => snapshot?.Hosts
            .SelectMany(x => x.Apps)
            .Any(x => names.Any(name => x.Name.Equals(name, StringComparison.OrdinalIgnoreCase)) &&
                      x.State == EnvironmentHealthState.Offline) == true;

        LibraryGroupAttention.Visibility = V(NeedsAttention("Plex", "Tautulli", "Kometa"));
        AcquisitionGroupAttention.Visibility = V(NeedsAttention("Sonarr", "Sonarr Debrid", "Radarr", "Radarr Debrid", "Lidarr", "Prowlarr", "Bazarr", "Seerr"));
        DownloadsGroupAttention.Visibility = V(NeedsAttention("SABnzbd", "qBittorrent"));
        AutomationGroupAttention.Visibility = V(NeedsAttention("Recyclarr", "Profilarr", "autobrr", "Unpackerr", "Cleanuparr"));
        ProcessingGroupAttention.Visibility = V(NeedsAttention("Tdarr"));
        LifecycleGroupAttention.Visibility = V(NeedsAttention("Maintainerr"));

        LibraryGroupAttention.Foreground = HasOffline("Plex", "Tautulli", "Kometa")
            ? (System.Windows.Media.Brush)FindResource("Danger")
            : (System.Windows.Media.Brush)FindResource("Warn");
        AcquisitionGroupAttention.Foreground = HasOffline("Sonarr", "Sonarr Debrid", "Radarr", "Radarr Debrid", "Lidarr", "Prowlarr", "Bazarr", "Seerr")
            ? (System.Windows.Media.Brush)FindResource("Danger")
            : (System.Windows.Media.Brush)FindResource("Warn");
        DownloadsGroupAttention.Foreground = HasOffline("SABnzbd", "qBittorrent")
            ? (System.Windows.Media.Brush)FindResource("Danger")
            : (System.Windows.Media.Brush)FindResource("Warn");
        AutomationGroupAttention.Foreground = HasOffline("Recyclarr", "Profilarr", "autobrr", "Unpackerr", "Cleanuparr")
            ? (System.Windows.Media.Brush)FindResource("Danger")
            : (System.Windows.Media.Brush)FindResource("Warn");
        ProcessingGroupAttention.Foreground = HasOffline("Tdarr")
            ? (System.Windows.Media.Brush)FindResource("Danger")
            : (System.Windows.Media.Brush)FindResource("Warn");
        LifecycleGroupAttention.Foreground = HasOffline("Maintainerr")
            ? (System.Windows.Media.Brush)FindResource("Danger")
            : (System.Windows.Media.Brush)FindResource("Warn");
    }

    private bool IsPageVisibleForCurrentTarget(string key)
    {
        if (ApplicationPageKeys.Contains(key))
            return CurrentTargetOwnsApplication(key);

        return key switch
        {
            "Applications" => App.Services.Config.Current.Applications.Any(x => x.DiscoveryVerified),
            "Services" => ServicesNav.Visibility == Visibility.Visible,
            "Docker" => DockerNav.Visibility == Visibility.Visible,
            "Storage" => StorageNav.Visibility == Visibility.Visible,
            "PiHole" => PiHoleNav.Visibility == Visibility.Visible,
            "Backups" => BackupsNav.Visibility == Visibility.Visible,
            "Logs" => LogsNav.Visibility == Visibility.Visible,
            "Files" => FilesNav.Visibility == Visibility.Visible,
            "Scripts" => ScriptsNav.Visibility == Visibility.Visible,
            "Updates" => UpdatesNav.Visibility == Visibility.Visible,
            _ => true
        };
    }

    private bool CurrentTargetOwnsApplication(string key)
    {
        var current = App.Services.Context.Current ?? App.Services.Config.GetSelectedServer();
        return current is not null && App.Services.Config.Current.Applications.Any(x =>
            x.DiscoveryVerified &&
            x.ServerId == current.Id &&
            x.Name.Equals(key, StringComparison.OrdinalIgnoreCase));
    }

    private void EnsureApplicationOwner(string key)
    {
        if (CurrentTargetOwnsApplication(key))
            return;

        var ownerId = App.Services.Config.Current.Applications
            .Where(x => x.DiscoveryVerified && x.Name.Equals(key, StringComparison.OrdinalIgnoreCase))
            .Select(x => x.ServerId)
            .FirstOrDefault(x => x is not null);

        if (ownerId is not { } id)
            return;

        var owner = App.Services.Config.Current.Servers.FirstOrDefault(x => x.Id == id);
        if (owner is not null)
            App.Services.Context.Select(owner);
    }

    private void LibraryGroup_Click(object sender, RoutedEventArgs e) => ToggleNavigationGroup("Library", LibraryGroupButton, LibraryNavGroup, LibraryGroupGlyph);
    private void AcquisitionGroup_Click(object sender, RoutedEventArgs e) => ToggleNavigationGroup("Acquisition", AcquisitionGroupButton, AcquisitionNavGroup, AcquisitionGroupGlyph);
    private void DownloadsGroup_Click(object sender, RoutedEventArgs e) => ToggleNavigationGroup("Downloads", DownloadsGroupButton, DownloadsNavGroup, DownloadsGroupGlyph);
    private void AutomationGroup_Click(object sender, RoutedEventArgs e) => ToggleNavigationGroup("Automation", AutomationGroupButton, AutomationNavGroup, AutomationGroupGlyph);
    private void ProcessingGroup_Click(object sender, RoutedEventArgs e) => ToggleNavigationGroup("Processing", ProcessingGroupButton, ProcessingNavGroup, ProcessingGroupGlyph);
    private void LifecycleGroup_Click(object sender, RoutedEventArgs e) => ToggleNavigationGroup("Lifecycle", LifecycleGroupButton, LifecycleNavGroup, LifecycleGroupGlyph);

    private void ToggleNavigationGroup(string key, FrameworkElement button, FrameworkElement panel, TextBlock glyph)
    {
        if (button.Visibility != Visibility.Visible)
            return;

        var collapsed = App.Services.Config.Current.Settings.CollapsedNavigationGroups;
        var isCollapsed = collapsed.Any(x => x.Equals(key, StringComparison.OrdinalIgnoreCase));
        if (isCollapsed)
            collapsed.RemoveAll(x => x.Equals(key, StringComparison.OrdinalIgnoreCase));
        else
            collapsed.Add(key);

        App.Services.Config.Save();
        ApplyNavigationGroupState(key, button, panel, glyph);
    }

    private void ApplyNavigationGroupState(string key, FrameworkElement button, FrameworkElement panel, TextBlock glyph)
    {
        if (button.Visibility != Visibility.Visible)
        {
            panel.Visibility = Visibility.Collapsed;
            return;
        }

        var collapsed = App.Services.Config.Current.Settings.CollapsedNavigationGroups
            .Any(x => x.Equals(key, StringComparison.OrdinalIgnoreCase));
        panel.Visibility = collapsed ? Visibility.Collapsed : Visibility.Visible;
        glyph.Text = collapsed ? "▶" : "▼";
    }

    private void EnsureNavigationGroupVisibleForPage(string key)
    {
        if ((key is "Plex" or "Tautulli" or "Kometa") && LibraryGroupButton.Visibility == Visibility.Visible)
        {
            LibraryNavGroup.Visibility = Visibility.Visible;
            LibraryGroupGlyph.Text = "▼";
        }
        else if ((key is "Sonarr" or "Radarr" or "Lidarr" or "Prowlarr" or "Bazarr" or "Seerr") && AcquisitionGroupButton.Visibility == Visibility.Visible)
        {
            AcquisitionNavGroup.Visibility = Visibility.Visible;
            AcquisitionGroupGlyph.Text = "▼";
        }
        else if ((key is "SABnzbd" or "qBittorrent") && DownloadsGroupButton.Visibility == Visibility.Visible)
        {
            DownloadsNavGroup.Visibility = Visibility.Visible;
            DownloadsGroupGlyph.Text = "▼";
        }
        else if ((key is "Recyclarr" or "Profilarr" or "autobrr" or "Unpackerr" or "Cleanuparr") && AutomationGroupButton.Visibility == Visibility.Visible)
        {
            AutomationNavGroup.Visibility = Visibility.Visible;
            AutomationGroupGlyph.Text = "▼";
        }
        else if (key == "Tdarr" && ProcessingGroupButton.Visibility == Visibility.Visible)
        {
            ProcessingNavGroup.Visibility = Visibility.Visible;
            ProcessingGroupGlyph.Text = "▼";
        }
        else if (key == "Maintainerr" && LifecycleGroupButton.Visibility == Visibility.Visible)
        {
            LifecycleNavGroup.Visibility = Visibility.Visible;
            LifecycleGroupGlyph.Text = "▼";
        }
    }

    private void RefreshCurrentPage()
    {
        _pages.Remove(_currentKey);
        NavigateByKey(_currentKey);
    }

    private void LoadQuickBar()
    {
        QuickBarItems.ItemsSource = App.Services.Config.Current.FavoriteKeys
            .Select(App.Services.Search.ResolveSemanticKey)
            .Where(x => x is not null)
            .Cast<SearchEntry>()
            .ToList();
    }

    private void QuickBarCustomize_Click(object sender, RoutedEventArgs e)
    {
        var window = new QuickBarSettingsWindow { Owner = this };
        window.ShowDialog();
        if (window.Changed)
        {
            LoadQuickBar();
            App.Services.Activity.Record(
                "Quick Bar customized",
                "Pinned commands were updated.",
                ActivityLevel.Info,
                deepLink: "page:Dashboard");
        }
    }
    private async void QuickBarButton_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is not SearchEntry item) return;
        await ExecuteSearchEntryAsync(item);
    }

    private List<SearchEntry> BuildPaletteResults(string query)
    {
        var normal = App.Services.Search.Search(query);
        if (!string.IsNullOrWhiteSpace(query)) return normal;

        var recent = App.Services.Config.Current.Settings.RecentKeys
            .Select(App.Services.Search.ResolveSemanticKey)
            .Where(x => x is not null)
            .Cast<SearchEntry>()
            .ToList();

        return recent
            .Concat(normal.Where(x => recent.All(r => !string.Equals(r.Key, x.Key, StringComparison.OrdinalIgnoreCase))))
            .Take(40)
            .ToList();
    }

    private void RememberRecent(SearchEntry entry)
    {
        var recent = App.Services.Config.Current.Settings.RecentKeys;
        recent.RemoveAll(x => string.Equals(x, entry.Key, StringComparison.OrdinalIgnoreCase));
        recent.Insert(0, entry.Key);
        while (recent.Count > 8) recent.RemoveAt(recent.Count - 1);
        App.Services.Config.Save();
    }
    private void OpenPalette()
    {
        PaletteOverlay.Visibility = Visibility.Visible;
        PaletteBox.Text = "";
        PaletteList.ItemsSource = BuildPaletteResults("");
        PaletteBox.Focus();
    }

    private void ClosePalette()
    {
        PaletteOverlay.Visibility = Visibility.Collapsed;
        Focus();
    }

    private void PaletteBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (PaletteList is null) return;
        PaletteList.ItemsSource = BuildPaletteResults(PaletteBox.Text);
        if (PaletteList.Items.Count > 0) PaletteList.SelectedIndex = 0;
    }

    private async void PaletteBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            ClosePalette();
            e.Handled = true;
            return;
        }

        if (e.Key == Key.Down)
        {
            if (PaletteList.Items.Count > 0)
                PaletteList.SelectedIndex = Math.Min(PaletteList.Items.Count - 1, PaletteList.SelectedIndex + 1);
            e.Handled = true;
            return;
        }

        if (e.Key == Key.Up)
        {
            if (PaletteList.Items.Count > 0)
                PaletteList.SelectedIndex = Math.Max(0, PaletteList.SelectedIndex - 1);
            e.Handled = true;
            return;
        }

        if (e.Key == Key.Enter && PaletteList.SelectedItem is SearchEntry selected)
        {
            ClosePalette();
            await ExecuteSearchEntryAsync(selected);
            e.Handled = true;
        }
    }

    private async void PaletteList_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (PaletteList.SelectedItem is not SearchEntry selected) return;
        ClosePalette();
        await ExecuteSearchEntryAsync(selected);
    }

    private async Task ExecuteSearchEntryAsync(SearchEntry entry)
    {
        RememberRecent(entry);
        switch (entry.Kind)
        {
            case SearchItemKind.Page:
                NavigateByKey(entry.Key["page:".Length..]);
                break;

            case SearchItemKind.Server:
                if (Guid.TryParse(entry.Key["server:".Length..], out var serverId))
                {
                    var server = App.Services.Config.Current.Servers.FirstOrDefault(x => x.Id == serverId);
                    if (server is not null) App.Services.Context.Select(server);
                }
                break;

            case SearchItemKind.Application:
                OpenApplication(entry.Key["app:".Length..]);
                break;

            case SearchItemKind.Action:
                await RunActionByNameAsync(entry.Key["action:".Length..]);
                break;

            case SearchItemKind.Setting:
                if (entry.Key == "setting:maintenance")
                {
                    var settings = App.Services.Config.Current.Settings;
                    settings.MaintenanceMode = !settings.MaintenanceMode;
                    App.Services.Config.Save();
                    App.Services.Activity.Record("Maintenance Mode", settings.MaintenanceMode ? "Enabled" : "Disabled", ActivityLevel.Info, deepLink: "page:Settings");
                    QuickStatusText.Text = settings.MaintenanceMode ? "Maintenance Mode enabled" : "Maintenance Mode disabled";
                }
                else if (entry.Key == "setting:setup")
                {
                    var wizard = new SetupWizardWindow { Owner = this };
                    wizard.ShowDialog();
                    if (wizard.SavedProfile)
                {
                    LoadGlobalTargets();
                    RefreshNavigationCapabilities();
                }
                }
                else
                {
                    NavigateByKey("Settings");
                }
                break;
        }
    }

    private void OpenApplication(string name)
    {
        var current = App.Services.Context.Current ?? App.Services.Config.GetSelectedServer();
        var apps = App.Services.Config.Current.Applications
            .Where(x => x.Name.Equals(name, StringComparison.OrdinalIgnoreCase))
            .ToArray();

        var app = apps.FirstOrDefault(x => x.ServerId == current?.Id)
            ?? apps.FirstOrDefault(x => x.DiscoveryVerified)
            ?? apps.FirstOrDefault();
        if (app is null) return;

        if (string.IsNullOrWhiteSpace(app.Url) && ApplicationPageKeys.Contains(app.Name))
        {
            NavigateByKey(app.Name);
            return;
        }

        var server = app.ServerId is { } id
            ? App.Services.Config.Current.Servers.FirstOrDefault(x => x.Id == id)
            : current;
        if (server is null) return;

        if (current?.Id != server.Id)
            App.Services.Context.Select(server);

        var resolved = app.Url.Replace("{host}", server.Host, StringComparison.OrdinalIgnoreCase);
        if (app.OpenEmbedded)
            new EmbeddedBrowserWindow(app.Name, resolved).Show();
        else
            Process.Start(new ProcessStartInfo(resolved) { UseShellExecute = true });

        App.Services.Activity.Record($"Opened {app.Name}", resolved, ActivityLevel.Info, serverId: server.Id, deepLink: $"app:{app.Name}");
    }

    private async Task RunActionByNameAsync(string name)
    {
        var action = App.Services.Config.Current.Actions.FirstOrDefault(x => x.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
        var server = action?.ServerId is { } id
            ? App.Services.Config.Current.Servers.FirstOrDefault(x => x.Id == id)
            : App.Services.Context.Current;
        if (action is null || server is null) return;

        if (action.Risk != ActionRisk.ReadOnly)
        {
            var result = MessageBox.Show(
                $"Run '{action.Name}' on {server.Name}?\n\n{action.Command}",
                "Confirm GraveOps action",
                MessageBoxButton.YesNo,
                action.Risk == ActionRisk.Dangerous ? MessageBoxImage.Warning : MessageBoxImage.Question);
            if (result != MessageBoxResult.Yes) return;
        }

        QuickStatusText.Text = $"Running {action.Name}...";
        var run = await App.Services.ActionRunner.RunAsync(action, server);
        QuickStatusText.Text = run.Success
            ? $"{action.Name} succeeded in {run.Duration.TotalSeconds:0.0}s"
            : $"{action.Name} failed";

        if (!run.Success)
            MessageBox.Show(string.IsNullOrWhiteSpace(run.Error) ? run.Verification : run.Error, $"{action.Name} failed", MessageBoxButton.OK, MessageBoxImage.Error);
    }

    private void Jobs_Click(object sender, RoutedEventArgs e)
    {
        OverviewDrawer.Visibility = Visibility.Collapsed;
        ActivityDrawer.Visibility = Visibility.Collapsed;
        JobsDrawer.Visibility = Visibility.Collapsed;

        var window = new OperationsHistoryWindow(0)
        {
            Owner = this
        };
        window.ShowDialog();
    }
    private void JobsClose_Click(object sender, RoutedEventArgs e) => JobsDrawer.Visibility = Visibility.Collapsed;
    private void Overview_Click(object sender, RoutedEventArgs e)
    {
        if (!App.Services.Config.Current.Settings.ShowOverviewDrawer) { NavigateByKey("Settings"); return; }
        JobsDrawer.Visibility = Visibility.Collapsed; ActivityDrawer.Visibility = Visibility.Collapsed;
        RefreshOverview();
        OverviewDrawer.Visibility = OverviewDrawer.Visibility == Visibility.Visible ? Visibility.Collapsed : Visibility.Visible;
    }
    private void OverviewSafeMode_Click(object sender, RoutedEventArgs e)
    {
        var settings = App.Services.Config.Current.Settings;
        settings.SafeMode = !settings.SafeMode;
        App.Services.Config.Save();

        App.Services.Activity.Record(
            settings.SafeMode ? "Safe Mode enabled" : "Safe Mode disabled",
            settings.SafeMode
                ? "Mutating server operations are blocked. Read-only monitoring remains available."
                : "Normal server controls restored.",
            ActivityLevel.Info,
            deepLink: "page:Dashboard");

        RefreshOverview();
        RefreshShellStatus();
    }
    private async void OverviewMaintenance_Click(object sender, RoutedEventArgs e)
    {
        var settings = App.Services.Config.Current.Settings;

        if (settings.MaintenanceMode)
        {
            settings.MaintenanceMode = false;
            settings.MaintenanceUntilUtc = null;
            App.Services.Config.Save();

            App.Services.Activity.Record(
                "Maintenance Mode disabled",
                "Normal desktop monitor alerting restored.",
                ActivityLevel.Info,
                deepLink: "page:Dashboard");

            await RecordMaintenanceComparisonAsync("Maintenance ended manually.");
        }
        else
        {
            var minutes = 0;
            if (MaintenanceDurationCombo.SelectedItem is ComboBoxItem item)
                int.TryParse(item.Tag?.ToString(), out minutes);

            settings.MaintenanceBeforeHealth = await CaptureMaintenanceHealthAsync();
            settings.MaintenanceMode = true;
            settings.MaintenanceUntilUtc = minutes > 0
                ? DateTimeOffset.UtcNow.AddMinutes(minutes)
                : null;

            var detail = minutes > 0
                ? $"Expected desktop alerts suppressed for {minutes} minutes. Monitoring and Activity remain active."
                : "Expected desktop alerts suppressed until Maintenance Mode is disabled manually. Monitoring and Activity remain active.";

            App.Services.Config.Save();

            App.Services.Activity.Record(
                "Maintenance Mode enabled",
                $"{detail}\nBefore: {settings.MaintenanceBeforeHealth}",
                ActivityLevel.Info,
                deepLink: "page:Dashboard");
        }

        RefreshOverview();
        RefreshShellStatus();
    }
    private void OverviewClose_Click(object sender, RoutedEventArgs e) => OverviewDrawer.Visibility = Visibility.Collapsed;
    private void OverviewHistory_Click(object sender, RoutedEventArgs e)
    {
        OverviewDrawer.Visibility = Visibility.Collapsed;

        var window = new OperationsHistoryWindow(1)
        {
            Owner = this
        };
        window.ShowDialog();
    }
    private void OverviewDashboard_Click(object sender, RoutedEventArgs e) { OverviewDrawer.Visibility = Visibility.Collapsed; NavigateByKey("Dashboard"); }
    private void OverviewStorage_Click(object sender, RoutedEventArgs e) { OverviewDrawer.Visibility = Visibility.Collapsed; NavigateByKey("Storage"); }
    private void OverviewApps_Click(object sender, RoutedEventArgs e) { OverviewDrawer.Visibility = Visibility.Collapsed; NavigateByKey("Applications"); }
    private void OverviewBackups_Click(object sender, RoutedEventArgs e) { OverviewDrawer.Visibility = Visibility.Collapsed; NavigateByKey("Backups"); }
    private void OverviewIncident_Click(object sender, RoutedEventArgs e) { OverviewDrawer.Visibility = Visibility.Collapsed; NavigateByKey("Services"); }
    private void Activity_ActivityAdded(ActivityRecord item)
    {
        Dispatcher.Invoke(() =>
        {
            if (ActivityDrawer.Visibility != Visibility.Visible)
                _activityUnread++;
            RefreshShellBadges();
        });
    }

    private void Notifications_Changed()
        => Dispatcher.Invoke(RefreshShellBadges);

    private void RefreshShellBadges()
    {
        if (ActivityButtonText is not null)
            ActivityButtonText.Text = _activityUnread > 0 ? $"Activity ({_activityUnread})" : "Activity";

        if (OverviewButtonText is not null)
        {
            var unread = App.Services.Notifications.UnreadCount;
            OverviewButtonText.Text = unread > 0 ? $"Overview ({unread})" : "Overview";
        }
    }
    private void Jobs_Changed()
    {
        Dispatcher.Invoke(() =>
        {
            JobsButtonText.Text = App.Services.Jobs.RunningCount > 0
                ? $"Jobs ({App.Services.Jobs.RunningCount})"
                : "Jobs";
            RefreshShellStatus();
        });
    }
    private void RefreshOverview()
    {
        OverviewTargetText.Text = App.Services.Context.Current?.Name ?? "No target";

        var settings = App.Services.Config.Current.Settings;
        var liveState = string.IsNullOrWhiteSpace(_lastMonitorState) || _lastMonitorState == "UNKNOWN"
            ? "NORMAL"
            : _lastMonitorState.ToUpperInvariant();

        OverviewModeText.Text = settings.MaintenanceMode ? "MAINTENANCE" : liveState;
        OverviewMaintenanceButton.Content = settings.MaintenanceMode ? "Disable Maintenance" : "Enable Maintenance";
        MaintenanceDurationCombo.IsEnabled = !settings.MaintenanceMode;
        OverviewSafeModeText.Text = settings.SafeMode ? "READ ONLY - mutations blocked" : "Normal controls";
        OverviewSafeModeButton.Content = settings.SafeMode ? "Disable Safe Mode" : "Enable Safe Mode";

        if (!settings.MaintenanceMode)
        {
            OverviewMaintenanceCountdownText.Text = "Normal alerting active";
        }
        else if (settings.MaintenanceUntilUtc is { } until)
        {
            var remaining = until - DateTimeOffset.UtcNow;
            OverviewMaintenanceCountdownText.Text = remaining > TimeSpan.Zero
                ? $"Ends in {FormatRemaining(remaining)}"
                : "Ending...";
        }
        else
        {
            OverviewMaintenanceCountdownText.Text = "Manual - no automatic expiry";
        }
    }

    private async Task<string> CaptureMaintenanceHealthAsync()
    {
        var server = App.Services.Context.Current;
        if (server is null) return "No active server";

        try
        {
            var state = await App.Services.Incident.CaptureStateAsync(server);
            return string.Join(" | ", state.Lines().Take(4));
        }
        catch (Exception ex)
        {
            return $"Health snapshot unavailable: {ex.Message}";
        }
    }

    private async Task RecordMaintenanceComparisonAsync(string reason)
    {
        var settings = App.Services.Config.Current.Settings;
        var before = string.IsNullOrWhiteSpace(settings.MaintenanceBeforeHealth)
            ? "Before state unavailable"
            : settings.MaintenanceBeforeHealth;

        var after = await CaptureMaintenanceHealthAsync();
        settings.MaintenanceBeforeHealth = "";
        App.Services.Config.Save();

        App.Services.Activity.Record(
            "Maintenance completed",
            $"{reason}\nBefore: {before}\nAfter: {after}",
            before == after ? ActivityLevel.Success : ActivityLevel.Info,
            serverId: App.Services.Context.Current?.Id,
            deepLink: "page:Dashboard");
    }
    private static string FormatRemaining(TimeSpan remaining)
    {
        if (remaining.TotalHours >= 1)
            return $"{(int)remaining.TotalHours}h {remaining.Minutes:00}m";
        return $"{Math.Max(0, remaining.Minutes)}m {Math.Max(0, remaining.Seconds):00}s";
    }

    private void RefreshShellStatus()
    {
        var settings = App.Services.Config.Current.Settings;
        FooterTargetText.Text = App.Services.Context.Current?.Name ?? "No target";
        RefreshControlPlaneState();
        FooterConnectionText.Text = ConnectionText.Text;
        FooterModeText.Text = settings.SafeMode ? "SAFE MODE" : settings.MaintenanceMode ? "MAINTENANCE" : "NORMAL";
        FooterJobsText.Text = App.Services.Jobs.RunningCount == 1
            ? "1 job"
            : $"{App.Services.Jobs.RunningCount} jobs";

        var lastCheck = App.Services.Monitor.LastCheckUtc;
        if (lastCheck is null)
        {
            FooterFreshnessText.Text = "telemetry waiting";
        }
        else
        {
            var age = DateTimeOffset.UtcNow - lastCheck.Value;
            FooterFreshnessText.Text = age.TotalSeconds < 5
                ? "telemetry live"
                : $"telemetry {Math.Max(0, (int)age.TotalSeconds)}s ago";
        }
    }

    private async void ShellTimer_Tick(object? sender, EventArgs e)
    {
        var settings = App.Services.Config.Current.Settings;
        if (settings.MaintenanceMode &&
            settings.MaintenanceUntilUtc is { } until &&
            DateTimeOffset.UtcNow >= until)
        {
            settings.MaintenanceMode = false;
            settings.MaintenanceUntilUtc = null;
            App.Services.Config.Save();

            App.Services.Activity.Record(
                "Maintenance Mode expired",
                "Normal desktop monitor alerting restored automatically.",
                ActivityLevel.Info,
                deepLink: "page:Dashboard");

            await RecordMaintenanceComparisonAsync("Timed Maintenance Mode expired.");
        }

        RefreshOverview();
        RefreshShellStatus();
        RefreshShellBadges();
    }
    private void Notifications_OpenRequested() => Dispatcher.Invoke(() => { Show(); WindowState = WindowState.Normal; Activate(); });
    private void Notifications_ExitRequested() => Dispatcher.Invoke(() => { _allowRealClose = true; Close(); });
    private void MainWindow_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        if (_allowRealClose || !App.Services.Config.Current.Settings.CloseToTray) return;
        e.Cancel = true; Hide();
    }
    private void MainWindow_SizeChanged(object sender, SizeChangedEventArgs e)
        => ApplyResponsiveLayout();

    private void ApplyResponsiveLayout()
    {
        if (NavigationColumn is null || PageHost is null) return;

        var width = ActualWidth > 0 ? ActualWidth : Width;
        var compact = App.Services.Config.Current.Settings.CompactLayout;

        NavigationColumn.Width = new GridLength(width < 1300 ? 220 : 260);

        PageHost.Margin = width < 1450
            ? new Thickness(16, 14, 16, 18)
            : compact
                ? new Thickness(18, 14, 18, 18)
                : new Thickness(24, 18, 24, 20);

        if (QuickStatusText is not null)
            QuickStatusText.Visibility = width < 1380 ? Visibility.Collapsed : Visibility.Visible;

        var availableDrawer = Math.Max(320, width - NavigationColumn.Width.Value - 80);
        var standardDrawerWidth = Math.Min(430, availableDrawer);
        var activityDrawerWidth = Math.Min(480, availableDrawer);

        if (ActivityDrawer is not null) ActivityDrawer.Width = activityDrawerWidth;
        if (JobsDrawer is not null) JobsDrawer.Width = standardDrawerWidth;
        if (OverviewDrawer is not null) OverviewDrawer.Width = standardDrawerWidth;
    }
    private void ApplyDesktopSettings()
    {
        if (App.Services.Config.Current.Settings.CompactLayout) PageHost.Margin = new Thickness(18, 14, 18, 18);
        if (App.Services.Config.Current.Settings.StartMinimizedToTray) Dispatcher.BeginInvoke(new Action(Hide));
        ApplyResponsiveLayout();
        RefreshOverview(); Jobs_Changed(); RefreshShellStatus(); RefreshShellBadges();
    }
    private void Activity_Click(object sender, RoutedEventArgs e)
    {
        ActivityDrawer.Visibility = ActivityDrawer.Visibility == Visibility.Visible
            ? Visibility.Collapsed
            : Visibility.Visible;

        if (ActivityDrawer.Visibility == Visibility.Visible)
        {
            _activityUnread = 0;
            RefreshShellBadges();
        }
    }

    private void ActivityClose_Click(object sender, RoutedEventArgs e) => ActivityDrawer.Visibility = Visibility.Collapsed;
    private void ActivityFilter_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!IsLoaded || ActivityList is null) return;
        RefreshActivityFilter();
    }

    private void RefreshActivityFilter()
    {
        var mode = (ActivityFilterCombo.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "ALL";
        var view = CollectionViewSource.GetDefaultView(App.Services.Activity.Recent);

        view.Filter = value =>
        {
            if (value is not ActivityRecord item) return false;

            return mode switch
            {
                "WARNINGS" => item.Level is ActivityLevel.Warning or ActivityLevel.Error,
                "ERRORS" => item.Level == ActivityLevel.Error,
                _ => true
            };
        };

        view.Refresh();
        UpdateActivitySelectionPanel();
    }

    private void ActivityList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        => UpdateActivitySelectionPanel();

    private void UpdateActivitySelectionPanel()
    {
        if (ActivityList.SelectedItem is not ActivityRecord item)
        {
            SelectedActivityPanel.Visibility = Visibility.Collapsed;
            ActivityOpenRelatedButton.IsEnabled = false;
            ActivityCopyDetailsButton.IsEnabled = false;
            ActivityViewDiffButton.IsEnabled = false;
            ActivityRestorePreviousButton.IsEnabled = false;
            return;
        }

        SelectedActivityPanel.Visibility = Visibility.Visible;
        SelectedActivityTitleText.Text = item.Title;
        SelectedActivityDetailText.Text = item.Detail;

        ActivityOpenRelatedButton.IsEnabled = !string.IsNullOrWhiteSpace(item.DeepLink);
        ActivityCopyDetailsButton.IsEnabled = true;

        var rollbackAvailable = false;
        try { rollbackAvailable = ResolveRollbackTarget(item) is not null; }
        catch { rollbackAvailable = false; }

        ActivityViewDiffButton.IsEnabled = rollbackAvailable;
        ActivityRestorePreviousButton.IsEnabled = rollbackAvailable;
    }
    private async void ActivityOpenSelected_Click(object sender, RoutedEventArgs e)
        => await OpenSelectedActivityAsync();

    private async Task OpenSelectedActivityAsync()
    {
        if (ActivityList.SelectedItem is not ActivityRecord item ||
            string.IsNullOrWhiteSpace(item.DeepLink))
            return;

        var entry = App.Services.Search.ResolveSemanticKey(item.DeepLink);
        if (entry is null) return;

        ActivityDrawer.Visibility = Visibility.Collapsed;
        await ExecuteSearchEntryAsync(entry);
    }

    private void ActivityCopySelected_Click(object sender, RoutedEventArgs e)
    {
        if (ActivityList.SelectedItem is not ActivityRecord item) return;

        var text = string.IsNullOrWhiteSpace(item.Detail)
            ? item.Title
            : $"{item.Title}{Environment.NewLine}{item.Detail}";

        System.Windows.Clipboard.SetText(text);
    }

    private void ActivityRollbackFolder_Click(object sender, RoutedEventArgs e)
    {
        var root = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "GraveOps",
            "file-backups");

        Directory.CreateDirectory(root);
        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("explorer.exe", $"\"{root}\"")
        {
            UseShellExecute = true
        });
    }

    private async void ActivityViewDiff_Click(object sender, RoutedEventArgs e)
    {
        if (ActivityList.SelectedItem is not ActivityRecord item)
        {
            GraveOps.App.Windows.GraveOpsDialog.Show(this, "Select a remote file edit in Activity first.", "View diff", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var target = ResolveRollbackTarget(item);
        if (target is null)
        {
            GraveOps.App.Windows.GraveOpsDialog.Show(this, 
                "GraveOps could not resolve a remote path and rollback copy from this Activity entry.\n\nUse Open copies to inspect the rollback folder.",
                "No rollback copy resolved",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        var server = ResolveActivityServer(item);
        if (server is null)
        {
            GraveOps.App.Windows.GraveOpsDialog.Show(this, "The server profile for this Activity entry is unavailable.", "View diff", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var current = await ReadRemoteRollbackTextAsync(server, target.Value.RemotePath);
        if (!current.Success)
        {
            GraveOps.App.Windows.GraveOpsDialog.Show(this, current.Error, "Could not read remote file", MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }

        string previous;
        try { previous = File.ReadAllText(target.Value.BackupPath); }
        catch (Exception ex)
        {
            GraveOps.App.Windows.GraveOpsDialog.Show(this, ex.Message, "Could not read rollback copy", MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }

        new GraveOps.App.Windows.RollbackDiffWindow(
            target.Value.RemotePath,
            target.Value.BackupPath,
            previous,
            current.Text)
        { Owner = this }.ShowDialog();
    }

    private async void ActivityRestorePrevious_Click(object sender, RoutedEventArgs e)
    {
        if (ActivityList.SelectedItem is not ActivityRecord item)
        {
            GraveOps.App.Windows.GraveOpsDialog.Show(this, "Select a remote file edit in Activity first.", "Restore previous", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        if (App.Services.Config.Current.Settings.SafeMode)
        {
            App.Services.Activity.Record(
                "Safe Mode blocked file restore",
                "Restore Previous was blocked because Safe Mode is enabled.",
                ActivityLevel.Warning,
                serverId: item.ServerId,
                deepLink: "page:Files");

            GraveOps.App.Windows.GraveOpsDialog.Show(this, "Safe Mode is enabled. Remote file restore is blocked.", "Restore blocked", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var target = ResolveRollbackTarget(item);
        if (target is null)
        {
            GraveOps.App.Windows.GraveOpsDialog.Show(this, "GraveOps could not resolve a remote path and rollback copy from this Activity entry.", "No rollback copy resolved", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var server = ResolveActivityServer(item);
        if (server is null)
        {
            GraveOps.App.Windows.GraveOpsDialog.Show(this, "The server profile for this Activity entry is unavailable.", "Restore previous", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        string previous;
        try { previous = File.ReadAllText(target.Value.BackupPath); }
        catch (Exception ex)
        {
            GraveOps.App.Windows.GraveOpsDialog.Show(this, ex.Message, "Could not read rollback copy", MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }

        var current = await ReadRemoteRollbackTextAsync(server, target.Value.RemotePath);
        if (!current.Success)
        {
            GraveOps.App.Windows.GraveOpsDialog.Show(this, current.Error, "Could not snapshot current remote file", MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }

        if (previous == current.Text)
        {
            GraveOps.App.Windows.GraveOpsDialog.Show(this, "The remote file already matches this rollback copy. No restore is necessary.", "Already identical", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var confirm = GraveOps.App.Windows.GraveOpsDialog.Show(this, 
            $"Restore the selected previous copy?\n\nServer: {server.Name}\nRemote: {target.Value.RemotePath}\nRollback: {target.Value.BackupPath}\n\nGraveOps will first create a new local snapshot of the current remote content, then write the rollback copy and verify the result.",
            "Confirm remote file restore",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);

        if (confirm != MessageBoxResult.Yes) return;

        string preRestorePath;
        try { preRestorePath = SavePreRestoreSnapshot(target.Value.RemotePath, current.Text); }
        catch (Exception ex)
        {
            GraveOps.App.Windows.GraveOpsDialog.Show(this, 
                "GraveOps refused to continue because the pre-restore snapshot could not be created.\n\n" + ex.Message,
                "Restore cancelled",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            return;
        }

        var write = await WriteRemoteRollbackTextAsync(server, target.Value.RemotePath, previous);
        if (!write.Success)
        {
            App.Services.Activity.Record(
                "Remote file restore failed",
                $"{target.Value.RemotePath}\nSource: {target.Value.BackupPath}\nPre-restore: {preRestorePath}\n{write.Error}",
                ActivityLevel.Error,
                serverId: server.Id,
                deepLink: "page:Files");

            GraveOps.App.Windows.GraveOpsDialog.Show(this, write.Error, "Restore failed", MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }

        var verify = await ReadRemoteRollbackTextAsync(server, target.Value.RemotePath);
        if (!verify.Success || verify.Text != previous)
        {
            var error = verify.Success
                ? "Post-restore verification did not match the selected rollback copy."
                : verify.Error;

            App.Services.Activity.Record(
                "Remote file restore verification failed",
                $"{target.Value.RemotePath}\nSource: {target.Value.BackupPath}\nPre-restore: {preRestorePath}\n{error}",
                ActivityLevel.Error,
                serverId: server.Id,
                deepLink: "page:Files");

            GraveOps.App.Windows.GraveOpsDialog.Show(this, 
                error + "\n\nThe pre-restore snapshot was preserved at:\n" + preRestorePath,
                "Restore verification failed",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            return;
        }

        App.Services.Activity.Record(
            "Remote file restored",
            $"{target.Value.RemotePath}\nSource: {target.Value.BackupPath}\nPre-restore: {preRestorePath}\nVerified: exact content match",
            ActivityLevel.Success,
            serverId: server.Id,
            deepLink: "page:Files");

        GraveOps.App.Windows.GraveOpsDialog.Show(this, 
            "Restore completed and the remote file matches the selected rollback copy.\n\nPre-restore snapshot:\n" + preRestorePath,
            "Restore verified",
            MessageBoxButton.OK,
            MessageBoxImage.Information);

        RefreshShellBadges();
    }

    private ServerProfile? ResolveActivityServer(ActivityRecord item)
    {
        if (item.ServerId is { } serverId)
        {
            var matched = App.Services.Config.Current.Servers.FirstOrDefault(x => x.Id == serverId);
            if (matched is not null) return matched;
        }

        return App.Services.Context.Current;
    }

    private (string RemotePath, string BackupPath)? ResolveRollbackTarget(ActivityRecord item)
    {
        var remotePath = ExtractRemotePath(item.Detail);
        if (string.IsNullOrWhiteSpace(remotePath)) return null;

        var root = Path.Combine(
            App.Services.Config.DirectoryPath,
            "file-backups");

        if (!Directory.Exists(root)) return null;

        // New 5D-C hotfix records carry the exact rollback file.
        var labeledRollback = System.Text.RegularExpressions.Regex.Match(
            item.Detail ?? "",
            @"(?im)^\s*(?:rollback|rollback copy|source)\s*:\s*(?<path>[A-Z]:\\[^\r\n]+)");

        if (labeledRollback.Success)
        {
            var candidate = labeledRollback.Groups["path"].Value.Trim().Trim('"');
            if (File.Exists(candidate) &&
                candidate.StartsWith(root, StringComparison.OrdinalIgnoreCase))
                return (remotePath, candidate);
        }

        // Compatibility with any older Activity text that happened to contain
        // a complete local backup path.
        foreach (System.Text.RegularExpressions.Match match in
                 System.Text.RegularExpressions.Regex.Matches(item.Detail ?? "", @"(?im)([A-Z]:\\[^\r\n]+)"))
        {
            var candidate = match.Groups[1].Value.Trim().Trim('"');
            if (File.Exists(candidate) &&
                candidate.StartsWith(root, StringComparison.OrdinalIgnoreCase))
                return (remotePath, candidate);
        }

        // Batch 4 rollback names are:
        // <first 16 chars of SHA256(remote path)>-yyyyMMdd-HHmmss.txt
        // Reconstruct that hash so pre-hotfix Activity entries remain usable.
        var safe = Convert.ToHexString(
            System.Security.Cryptography.SHA256.HashData(
                System.Text.Encoding.UTF8.GetBytes(remotePath)))
            [..16]
            .ToLowerInvariant();

        var activityLocal = item.Timestamp.LocalDateTime;
        var candidates = Directory
            .EnumerateFiles(root, safe + "-*.txt", SearchOption.AllDirectories)
            .Select(path => new { Path = path, Time = File.GetLastWriteTime(path) })
            .Where(x => x.Time <= activityLocal.AddMinutes(5))
            .OrderBy(x => Math.Abs((x.Time - activityLocal).TotalSeconds))
            .ThenByDescending(x => x.Time)
            .ToList();

        return candidates.Count == 0 ? null : (remotePath, candidates[0].Path);
    }
    private static string? ExtractRemotePath(string? detail)
    {
        if (string.IsNullOrWhiteSpace(detail)) return null;

        var labeled = System.Text.RegularExpressions.Regex.Match(
            detail,
            @"(?im)^\s*(?:remote|path|file)\s*:\s*(?<path>/[^\r\n]+)");

        if (labeled.Success)
            return labeled.Groups["path"].Value.Trim().Trim('"', '\'');

        foreach (var line in detail.Replace("\r", "").Split('\n'))
        {
            var value = line.Trim();
            if (value.StartsWith("/", StringComparison.Ordinal) &&
                !value.Contains(" -> ", StringComparison.Ordinal))
                return value.Trim('"', '\'');
        }

        return null;
    }

    private async Task<(bool Success, string Text, string Error)> ReadRemoteRollbackTextAsync(ServerProfile server, string remotePath)
    {
        const string begin = "__GRAVEOPS_ROLLBACK_BEGIN__";
        const string end = "__GRAVEOPS_ROLLBACK_END__";
        const string missing = "__GRAVEOPS_ROLLBACK_MISSING__";

        var quoted = ShellQuoteRollback(remotePath);
        var command =
            $"printf '{begin}'; if [ -f {quoted} ]; then base64 -w0 -- {quoted}; else printf '{missing}'; fi; printf '{end}'";

        try
        {
            var result = await App.Services.Ssh.ExecuteAsync(server, command, 60);
            var combined = result.Combined ?? "";
            var start = combined.IndexOf(begin, StringComparison.Ordinal);
            var finish = combined.IndexOf(end, StringComparison.Ordinal);

            if (start < 0 || finish <= start)
                return (false, "", "Remote read did not return GraveOps verification markers.");

            var payload = combined[(start + begin.Length)..finish];
            if (payload.Contains(missing, StringComparison.Ordinal))
                return (false, "", $"Remote file does not exist: {remotePath}");

            try
            {
                return (true, System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(payload.Trim())), "");
            }
            catch (Exception ex)
            {
                return (false, "", "Remote file payload could not be decoded: " + ex.Message);
            }
        }
        catch (Exception ex) { return (false, "", ex.Message); }
    }

    private async Task<(bool Success, string Error)> WriteRemoteRollbackTextAsync(ServerProfile server, string remotePath, string content)
    {
        var quotedPath = ShellQuoteRollback(remotePath);
        var quotedPayload = ShellQuoteRollback(Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(content)));

        var command =
            "tmp=$(mktemp) || exit 71; " +
            $"printf '%s' {quotedPayload} | base64 -d > \"$tmp\" || {{ rc=$?; rm -f \"$tmp\"; exit $rc; }}; " +
            $"cat \"$tmp\" > {quotedPath}; rc=$?; rm -f \"$tmp\"; exit $rc";

        try
        {
            var result = await App.Services.Ssh.ExecuteAsync(server, command, 90);
            if (!string.IsNullOrWhiteSpace(result.Combined) &&
                result.Combined.Contains("Permission denied", StringComparison.OrdinalIgnoreCase))
                return (false, result.Combined.Trim());

            return (true, "");
        }
        catch (Exception ex) { return (false, ex.Message); }
    }

    private static string SavePreRestoreSnapshot(string remotePath, string content)
    {
        var root = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "GraveOps",
            "file-backups",
            "pre-restore");

        Directory.CreateDirectory(root);

        var fileName = Path.GetFileName(remotePath);
        if (string.IsNullOrWhiteSpace(fileName)) fileName = "remote-file";
        foreach (var invalid in Path.GetInvalidFileNameChars())
            fileName = fileName.Replace(invalid, '_');

        var path = Path.Combine(root, $"{DateTime.Now:yyyyMMdd-HHmmss-fff}-{fileName}");
        File.WriteAllText(path, content, new System.Text.UTF8Encoding(false));
        return path;
    }

    private static string ShellQuoteRollback(string value)
        => "'" + value.Replace("'", "'\"'\"'") + "'";
    private void ActivityClear_Click(object sender, RoutedEventArgs e)
    {
        if (App.Services.Activity.Recent.Count == 0) return;

        if (MessageBox.Show(
            "Clear the local GraveOps activity timeline?",
            "Clear activity",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question) != MessageBoxResult.Yes)
            return;

        App.Services.Activity.Clear();
        _activityUnread = 0;
        RefreshShellBadges();
    }

    private async void ActivityList_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (ActivityList.SelectedItem is not ActivityRecord item || string.IsNullOrWhiteSpace(item.DeepLink)) return;
        var entry = App.Services.Search.ResolveSemanticKey(item.DeepLink);
        if (entry is not null)
        {
            ActivityDrawer.Visibility = Visibility.Collapsed;
            await ExecuteSearchEntryAsync(entry);
        }
    }

    private void Navigation_NavigationRequested(string deepLink)
    {
        Dispatcher.BeginInvoke(new Action(async () =>
        {
            var entry = App.Services.Search.ResolveSemanticKey(deepLink);
            if (entry is not null) await ExecuteSearchEntryAsync(entry);
        }));
    }

    private void MainWindow_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if ((Keyboard.Modifiers & ModifierKeys.Control) != 0 && e.Key == Key.K)
        {
            OpenPalette();
            e.Handled = true;
        }
        else if ((Keyboard.Modifiers & ModifierKeys.Control) != 0 && e.Key == Key.T)
        {
            NavigateByKey("Terminal");
            e.Handled = true;
        }
        else if ((Keyboard.Modifiers & (ModifierKeys.Control | ModifierKeys.Shift)) == (ModifierKeys.Control | ModifierKeys.Shift) && e.Key == Key.P)
        {
            OpenApplication("Plex");
            e.Handled = true;
        }
        else if (e.Key == Key.F5)
        {
            RefreshCurrentPage();
            e.Handled = true;
        }
        else if (e.Key == Key.Escape && PaletteOverlay.Visibility == Visibility.Visible)
        {
            ClosePalette();
            e.Handled = true;
        }
    }

    private void Dashboard_Click(object sender, RoutedEventArgs e) => NavigateByKey("Dashboard");
    private void Servers_Click(object sender, RoutedEventArgs e) => NavigateByKey("Servers");
    private void LifecycleOverview_Click(object sender, RoutedEventArgs e) => NavigateByKey("Lifecycle");
    private void HistoryNav_Click(object sender, RoutedEventArgs e) => NavigateByKey("History");
    private void Apps_Click(object sender, RoutedEventArgs e) => NavigateByKey("Applications");
    private void Terminal_Click(object sender, RoutedEventArgs e) => NavigateByKey("Terminal");
    private void Services_Click(object sender, RoutedEventArgs e) => NavigateByKey("Services");
    private void Docker_Click(object sender, RoutedEventArgs e) => NavigateByKey("Docker");
    private void Storage_Click(object sender, RoutedEventArgs e) => NavigateByKey("Storage");
    private void PiHole_Click(object sender, RoutedEventArgs e) => NavigateByKey("PiHole");
    private void Backups_Click(object sender, RoutedEventArgs e) => NavigateByKey("Backups");
    private void Logs_Click(object sender, RoutedEventArgs e) => NavigateByKey("Logs");
    private void Files_Click(object sender, RoutedEventArgs e) => NavigateByKey("Files");
    private void Scripts_Click(object sender, RoutedEventArgs e) => NavigateByKey("Scripts");
    private void Settings_Click(object sender, RoutedEventArgs e) => NavigateByKey("Settings");
    private void Updates_Click(object sender, RoutedEventArgs e) => NavigateByKey("Updates");
    private void QuickTerminal_Click(object sender, RoutedEventArgs e) => NavigateByKey("Terminal");

    private void Minimize_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;
    private void Maximize_Click(object sender, RoutedEventArgs e) => WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    private void Intelligence_Click(
        object sender,
        RoutedEventArgs e)
    {
        OverviewDrawer.Visibility = Visibility.Collapsed;
        ActivityDrawer.Visibility = Visibility.Collapsed;
        JobsDrawer.Visibility = Visibility.Collapsed;
        NavigateByKey("Intelligence");
    }

    private void PlexNav_Click(object sender, RoutedEventArgs e)
        => NavigateByKey("Plex");

    private void SonarrNav_Click(object sender, RoutedEventArgs e)
        => NavigateByKey("Sonarr");

    private void RadarrNav_Click(object sender, RoutedEventArgs e)
        => NavigateByKey("Radarr");

    private void LidarrNav_Click(object sender, RoutedEventArgs e)
        => NavigateByKey("Lidarr");

    private void ProwlarrNav_Click(object sender, RoutedEventArgs e)
        => NavigateByKey("Prowlarr");

    private void SabnzbdNav_Click(object sender, RoutedEventArgs e)
        => NavigateByKey("SABnzbd");

    private void QbittorrentNav_Click(object sender, RoutedEventArgs e)
        => NavigateByKey("qBittorrent");

    private void TautulliNav_Click(object sender, RoutedEventArgs e) => NavigateByKey("Tautulli");
    private void KometaNav_Click(object sender, RoutedEventArgs e) => NavigateByKey("Kometa");
    private void BazarrNav_Click(object sender, RoutedEventArgs e) => NavigateByKey("Bazarr");
    private void SeerrNav_Click(object sender, RoutedEventArgs e) => NavigateByKey("Seerr");
    private void RecyclarrNav_Click(object sender, RoutedEventArgs e) => NavigateByKey("Recyclarr");
    private void ProfilarrNav_Click(object sender, RoutedEventArgs e) => NavigateByKey("Profilarr");
    private void AutobrrNav_Click(object sender, RoutedEventArgs e) => NavigateByKey("autobrr");
    private void UnpackerrNav_Click(object sender, RoutedEventArgs e) => NavigateByKey("Unpackerr");
    private void CleanuparrNav_Click(object sender, RoutedEventArgs e) => NavigateByKey("Cleanuparr");
    private void TdarrNav_Click(object sender, RoutedEventArgs e) => NavigateByKey("Tdarr");
    private void MaintainerrNav_Click(object sender, RoutedEventArgs e) => NavigateByKey("Maintainerr");

    private void IntelligenceNav_Click(object sender, RoutedEventArgs e)
        => NavigateByKey("Intelligence");



}