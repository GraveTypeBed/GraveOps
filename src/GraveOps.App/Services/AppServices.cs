using GraveOps.App.Models;
using GraveOps.App.Services.Hosts;

namespace GraveOps.App.Services;

public sealed class AppServices
{
    public ConfigService Config { get; } = new();
    public CredentialManagerService Credentials { get; } = new();
    public DiscoveryService Discovery { get; } = new();
    public IntegrationCatalog Integrations { get; } = new();
    public LocalWindowsDiscoveryService WindowsDiscovery { get; private set; } = null!;
    public RemoteLinuxDiscoveryService LinuxDiscovery { get; private set; } = null!;
    public PowerShellRemotingService PowerShellRemote { get; private set; } = null!;
    public LocalPowerShellService LocalPowerShell { get; private set; } = null!;
    public RemoteWindowsDiscoveryService WindowsRemoteDiscovery { get; private set; } = null!;
    public IntegrationAssignmentService IntegrationAssignments { get; private set; } = null!;
    public SshService Ssh { get; private set; } = null!;
    public SftpService Sftp { get; private set; } = null!;
    public NotificationService Notifications { get; private set; } = null!;
    public BackgroundMonitorService Monitor { get; private set; } = null!;
    public MediaOperationsService MediaOps { get; private set; } = null!;
    public DownloadClientService DownloadClients { get; private set; } = null!;
    public EnvironmentOverviewService Environment { get; private set; } = null!;
    public FleetHistoryService FleetHistory { get; private set; } = null!;
    public MediaLifecycleService Lifecycle { get; private set; } = null!;
    public IntegrationRuntimeService IntegrationRuntime { get; private set; } = null!;
    public HostProviderRegistry Hosts { get; private set; } = null!;
    public GlobalContextService Context { get; private set; } = null!;
    public ActivityService Activity { get; private set; } = null!;
    public CommandPaletteService Search { get; private set; } = null!;
    public NavigationHub Navigation { get; } = new();
    public ActionRunnerService ActionRunner { get; private set; } = null!;
    public JobService Jobs { get; private set; } = null!;
    public WakeOnLanService WakeOnLan { get; private set; } = null!;
    public ProfileTransferService Profiles { get; private set; } = null!;
    public BackupInventoryService Backups { get; private set; } = null!;
    public IncidentService Incident { get; private set; } = null!;

    public void Initialize()
    {
        Config.Load();
        MigratePreview01Defaults(Config.Current);
        MigratePreview02Discovery(Config.Current);
        MigratePreview06DiscoveryExpansion(Config.Current);
        MigratePreview07DiscoveryExpansion(Config.Current);
        Migrate20Rc1(Config.Current);
        Migrate20Rc2(Config.Current);
        SeedDefaults(Config.Current);
        Config.Save();

        Ssh = new SshService(Credentials);
        Sftp = new SftpService(Credentials);
        PowerShellRemote = new PowerShellRemotingService(Credentials);
        LocalPowerShell = new LocalPowerShellService();
        WindowsDiscovery = new LocalWindowsDiscoveryService(Integrations);
        LinuxDiscovery = new RemoteLinuxDiscoveryService(Ssh, Integrations);
        WindowsRemoteDiscovery = new RemoteWindowsDiscoveryService(PowerShellRemote, Integrations);
        IntegrationAssignments = new IntegrationAssignmentService(Config, Integrations);
        Hosts = new HostProviderRegistry(
        [
            new LocalWindowsHostProvider(),
            new RemoteWindowsHostProvider(PowerShellRemote),
            new RemoteLinuxHostProvider(Ssh)
        ]);
        Notifications = new NotificationService();
        Context = new GlobalContextService(this);
        Activity = new ActivityService(Config.DirectoryPath);
        Search = new CommandPaletteService(this);
        Jobs = new JobService();
        WakeOnLan = new WakeOnLanService();
        Profiles = new ProfileTransferService(this);
        Backups = new BackupInventoryService(this);
        ActionRunner = new ActionRunnerService(this);
        Incident = new IncidentService(this);
        Monitor = new BackgroundMonitorService(this, Notifications);
        MediaOps = new MediaOperationsService(this);
        DownloadClients = new DownloadClientService(this);
        IntegrationRuntime = new IntegrationRuntimeService(this);
        FleetHistory = new FleetHistoryService(Config.DirectoryPath);
        Lifecycle = new MediaLifecycleService(this);
        Environment = new EnvironmentOverviewService(this);
    }

    private static void MigratePreview02Discovery(AppConfig config)
    {
        if (config.ConfigVersion >= 3)
            return;

        // Preview 0.2 associated common application names with open ports. Preview
        // 0.3 re-verifies those capabilities once using application-specific evidence.
        foreach (var app in config.Applications)
        {
            app.DiscoveryVerified = false;
            app.DiscoveryEvidence = "";
            app.DiscoveredUtc = null;
        }

        foreach (var server in config.Servers)
        {
            server.LastIntegrationDiscoveryUtc = null;
            server.IntegrationDiscoverySummary = "";
        }

        config.ConfigVersion = 3;
    }


    private static void MigratePreview06DiscoveryExpansion(AppConfig config)
    {
        if (config.ConfigVersion >= 4)
            return;

        // Preview 0.6 expands first-class discovery beyond the original Plex/Arr/
        // download set. Existing hosts are verified once so newly supported apps
        // can become fleet capabilities without asking users to recreate profiles.
        foreach (var server in config.Servers)
        {
            server.LastIntegrationDiscoveryUtc = null;
            server.IntegrationDiscoverySummary = "";
        }

        config.ConfigVersion = 4;
    }

    private static void MigratePreview07DiscoveryExpansion(AppConfig config)
    {
        if (config.ConfigVersion >= 5)
            return;

        // Preview 0.7 adds a second automation/library wave. Re-run verified
        // discovery once so existing hosts can acquire the new capabilities
        // without recreating their profiles.
        foreach (var server in config.Servers)
        {
            server.LastIntegrationDiscoveryUtc = null;
            server.IntegrationDiscoverySummary = "";
        }

        config.ConfigVersion = 5;
    }


    private static void Migrate20Rc1(AppConfig config)
    {
        if (config.ConfigVersion >= 6)
            return;

        config.Settings.ShowQuickModules = true;
        if (config.Settings.QuickModuleOrder.Count == 0)
            config.Settings.QuickModuleOrder.AddRange(new[] { "Intelligence", "Servers", "Media Hub", "Lifecycle", "Recyclarr", "Docker", "Storage", "Activity" });
        config.ConfigVersion = 6;
    }

    private static void Migrate20Rc2(AppConfig config)
    {
        if (config.ConfigVersion >= 7)
            return;

        string[] canonicalModules =
        [
            "Intelligence", "Servers", "Media Hub", "Lifecycle", "Recyclarr",
            "Docker", "Storage", "Backups", "Activity"
        ];

        var existing = config.Settings.QuickModuleOrder
            .Where(x => canonicalModules.Contains(x, StringComparer.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        foreach (var module in canonicalModules)
        {
            if (!existing.Contains(module, StringComparer.OrdinalIgnoreCase))
                existing.Add(module);
        }

        config.Settings.QuickModuleOrder = existing;
        config.ConfigVersion = 7;
    }

    private static void SeedDefaults(AppConfig config)
    {
        if (config.FavoriteKeys.Count == 0)
            config.FavoriteKeys.Add("page:Terminal");

        if (config.Settings.QuickModuleOrder.Count == 0)
        {
            config.Settings.QuickModuleOrder.AddRange(
            [
                "Intelligence", "Servers", "Media Hub", "Lifecycle", "Recyclarr",
                "Docker", "Storage", "Backups", "Activity"
            ]);
        }
    }

    private static void MigratePreview01Defaults(AppConfig config)
    {
        if (config.ConfigVersion >= 2)
            return;

        var legacyAppNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "Plex", "Sonarr", "Sonarr Debrid", "Radarr", "Radarr Debrid",
            "Prowlarr", "Lidarr", "Decypharr", "DUMB", "FlareSolverr",
            "SABnzbd", "qBittorrent", "Pi-hole"
        };

        // Preview 0.1 inherited unassigned personal-edition launcher seeds.
        // They are not real discoveries and must not leak into shareable GraveOps profiles.
        config.Applications.RemoveAll(x =>
            x.ServerId is null && legacyAppNames.Contains(x.Name));

        var legacyActionNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "Run Full Health Check", "Failed Systemd Units", "Restart Plex",
            "Plex Status", "Restart DUMB", "DUMB Logs (100)",
            "Recover Media Mounts", "Backup Now", "Restore Test", "Reboot Server",
            "Docker Overview", "Docker Resource Snapshot", "Container Restart Counts",
            "Media Mount Audit", "Drive Identity Snapshot", "Recent System Errors",
            "Backup Timers", "Network & DNS Snapshot", "Media Service Ports"
        };

        config.Actions.RemoveAll(x => legacyActionNames.Contains(x.Name));
        config.FavoriteKeys.RemoveAll(x =>
            x.StartsWith("app:", StringComparison.OrdinalIgnoreCase) ||
            x.StartsWith("action:", StringComparison.OrdinalIgnoreCase));

        config.ConfigVersion = 2;
    }
}
