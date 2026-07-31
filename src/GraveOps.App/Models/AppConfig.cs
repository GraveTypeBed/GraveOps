namespace GraveOps.App.Models;

public sealed class AppConfig
{
    public int ConfigVersion { get; set; } = 7;
    public Guid? SelectedServerId { get; set; }
    public List<ServerProfile> Servers { get; set; } = new();
    public List<ManagedApp> Applications { get; set; } = new();
    public List<QuickAction> Actions { get; set; } = new();
    public List<string> FavoriteKeys { get; set; } = new();
    public AppSettings Settings { get; set; } = new();
}

public sealed class AppSettings
{
    public int DashboardRefreshSeconds { get; set; } = 30;
    public bool OpenAppsEmbedded { get; set; } = true;
    public bool ConfirmNormalActions { get; set; }
    public bool StartMinimizedToTray { get; set; }
    public bool CloseToTray { get; set; } = true;
    public bool EnableDesktopNotifications { get; set; } = true;
    public int MonitorIntervalSeconds { get; set; } = 60;
    public bool FirstRunCompleted { get; set; }
    public bool MaintenanceMode { get; set; }
    public bool ShowOverviewDrawer { get; set; } = true;
    public bool CompactLayout { get; set; }
    public bool SafeMode { get; set; }
    public string MaintenanceBeforeHealth { get; set; } = "";
    public DateTimeOffset? MaintenanceUntilUtc { get; set; }
    public string LastPageKey { get; set; } = "Dashboard";
    public int LastApplicationsTabIndex { get; set; }
    public List<string> HiddenApplicationCards { get; set; } = new();
    public List<string> ApplicationCardOrder { get; set; } = new();
    public List<string> RecentKeys { get; set; } = new();
    public List<string> CollapsedNavigationGroups { get; set; } = new();
    public bool ShowQuickModules { get; set; } = true;
    public List<string> QuickModuleOrder { get; set; } = new();
    public bool EnableFleetHistory { get; set; } = true;
}
