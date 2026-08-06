var repository =
    FindRepositoryRoot();

var sharedRoot =
    Path.Combine(
        repository,
        "src",
        "GraveOps.Presentation.Avalonia",
        "SpecializedApplications");

var models =
    Read(
        Path.Combine(
            sharedRoot,
            "UnifiedSpecializedApplicationModels.cs"));

var applicationView =
    Read(
        Path.Combine(
            sharedRoot,
            "UnifiedApplicationWorkspaceView.cs"));

var recyclarrView =
    Read(
        Path.Combine(
            sharedRoot,
            "UnifiedRecyclarrView.cs"));

var piHoleView =
    Read(
        Path.Combine(
            sharedRoot,
            "UnifiedPiHoleView.cs"));

var shared =
    string.Join(
        Environment.NewLine,
        Directory
            .GetFiles(
                sharedRoot,
                "*.cs",
                SearchOption.AllDirectories)
            .Select(
                File.ReadAllText));

var linuxAdapter =
    Read(
        Path.Combine(
            repository,
            "src",
            "GraveOps.Desktop.Linux",
            "MainWindow.SharedUnifiedSpecializedApplications.cs"));

var windowsAdapter =
    Read(
        Path.Combine(
            repository,
            "src",
            "GraveOps.Desktop.Windows",
            "MainWindow.SharedUnifiedSpecializedApplications.cs"));

var linuxMain =
    Read(
        Path.Combine(
            repository,
            "src",
            "GraveOps.Desktop.Linux",
            "MainWindow.axaml.cs"));

var windowsMain =
    Read(
        Path.Combine(
            repository,
            "src",
            "GraveOps.Desktop.Windows",
            "MainWindow.axaml.cs"));

var windowsFleet =
    Read(
        Path.Combine(
            repository,
            "src",
            "GraveOps.Desktop.Windows",
            "MainWindow.SharedUnifiedFleetApplications.cs"));

var linuxXaml =
    Read(
        Path.Combine(
            repository,
            "src",
            "GraveOps.Desktop.Linux",
            "MainWindow.axaml"));

var tests =
    new (string Name, Action Run)[]
    {
        (
            "shared contracts own three specialized application surfaces",
            () =>
            {
                Contains(
                    models,
                    "UnifiedApplicationWorkspaceState");
                Contains(
                    models,
                    "UnifiedRecyclarrState");
                Contains(
                    models,
                    "UnifiedPiHoleState");
            }),

        (
            "shared generic application view owns Linux presentation language",
            () =>
            {
                Contains(
                    applicationView,
                    "Verified application state and operational ownership.");
                Contains(
                    applicationView,
                    "Common work stays on this page; Docker, logs and Intelligence remain one click away.");
                Contains(
                    applicationView,
                    "Runtime, endpoint and provider evidence for the selected application.");
            }),

        (
            "shared Recyclarr view owns Linux presentation language",
            () =>
            {
                Contains(
                    recyclarrView,
                    "Container runtime, configuration targets, read-only preview and synchronization evidence.");
                Contains(
                    recyclarrView,
                    "Configuration targets");
                Contains(
                    recyclarrView,
                    "Read-only preview");
                Contains(
                    recyclarrView,
                    "Preview reads Sonarr and Radarr state but does not apply changes.");
            }),

        (
            "shared Pi-hole view owns Linux presentation language",
            () =>
            {
                Contains(
                    piHoleView,
                    "DNS, blocking, query statistics, gravity state and confirmation-gated Linux controls.");
                Contains(
                    piHoleView,
                    "Enable blocking");
                Contains(
                    piHoleView,
                    "Disable 5m");
                Contains(
                    piHoleView,
                    "Gravity inventory remains read-only.");
            }),

        (
            "shared specialized presentation remains platform neutral",
            () =>
            {
                NotContains(
                    shared,
                    "GraveOps.Desktop.Linux");
                NotContains(
                    shared,
                    "GraveOps.Desktop.Windows");
                NotContains(
                    shared,
                    "GraveOps.Platform.Linux");
                NotContains(
                    shared,
                    "GraveOps.Platform.Windows");
                NotContains(
                    shared,
                    "System.Windows");
            }),

        (
            "Linux adapter preserves direct application handlers",
            () =>
            {
                Contains(
                    linuxAdapter,
                    "DirectIntegrationOpenButton_OnClick");
                Contains(
                    linuxAdapter,
                    "DirectIntegrationDockerButton_OnClick");
                Contains(
                    linuxAdapter,
                    "DirectIntegrationLogsButton_OnClick");
                Contains(
                    linuxAdapter,
                    "DirectIntegrationIntelligenceButton_OnClick");
            }),

        (
            "Linux adapter preserves Recyclarr handlers",
            () =>
            {
                Contains(
                    linuxAdapter,
                    "RecyclarrRefreshButton_OnClick");
                Contains(
                    linuxAdapter,
                    "RecyclarrOpenConfigButton_OnClick");
                Contains(
                    linuxAdapter,
                    "RecyclarrPreviewButton_OnClick");
                Contains(
                    linuxAdapter,
                    "RecyclarrDockerButton_OnClick");
                Contains(
                    linuxAdapter,
                    "RecyclarrLogsButton_OnClick");
            }),

        (
            "Linux adapter preserves guarded Pi-hole handlers",
            () =>
            {
                Contains(
                    linuxAdapter,
                    "PiHoleRefreshButton_OnClick");
                Contains(
                    linuxAdapter,
                    "PiHoleEnableButton_OnClick");
                Contains(
                    linuxAdapter,
                    "PiHoleDisableButton_OnClick");
                Contains(
                    linuxAdapter,
                    "PiHoleReloadButton_OnClick");
                Contains(
                    linuxAdapter,
                    "PiHoleOpenButton_OnClick");
            }),

        (
            "Windows adapter exposes honest unsupported boundaries",
            () =>
            {
                Contains(
                    windowsAdapter,
                    "Recyclarr config access and preview require the Linux Docker/config provider");
                Contains(
                    windowsAdapter,
                    "Pi-hole API access and Linux control commands are not provided by the Windows target provider.");
                Contains(
                    windowsAdapter,
                    "Gravity inventory is not exposed by the Windows provider.");
            }),

        (
            "Windows adapter never imports Linux execution engines",
            () =>
            {
                NotContains(
                    windowsAdapter,
                    "RecyclarrWorkspaceService");
                NotContains(
                    windowsAdapter,
                    "LinuxPiHoleTelemetryAdapter");
                NotContains(
                    windowsAdapter,
                    "PiHoleWorkspaceService");
                NotContains(
                    windowsAdapter,
                    "LinuxOperatorTools");
                NotContains(
                    windowsAdapter,
                    "OpsIntegration");
            }),

        (
            "Windows Fleet Applications opens shared detail for provider and installed applications",
            () =>
            {
                Contains(
                    windowsFleet,
                    "ShowSharedWindowsSpecializedApplication");
                Contains(
                    windowsFleet,
                    "CanOpen:");
                Contains(
                    windowsFleet,
                    "true");
                Contains(
                    windowsAdapter,
                    "FindWindowsInstalledApplication");
                Contains(
                    windowsAdapter,
                    "Publisher ·");
            }),

        (
            "known Windows media products keep dedicated routes",
            () =>
            {
                Contains(
                    windowsFleet,
                    "\"PlexNav\"");
                Contains(
                    windowsFleet,
                    "\"SonarrNav\"");
                Contains(
                    windowsFleet,
                    "\"SABnzbdNav\"");
                Contains(
                    windowsFleet,
                    "\"QBittorrentNav\"");
            }),

        (
            "both hosts initialize specialized shared workspaces",
            () =>
            {
                Contains(
                    linuxMain,
                    "InitializeSharedUnifiedSpecializedApplications();");
                Contains(
                    windowsMain,
                    "InitializeSharedUnifiedSpecializedApplications();");
            }),

        (
            "both hosts dispose specialized shared workspaces",
            () =>
            {
                Contains(
                    linuxMain,
                    "DisposeSharedUnifiedSpecializedApplications();");
                Contains(
                    windowsMain,
                    "DisposeSharedUnifiedSpecializedApplications();");
            }),

        (
            "legacy Linux pages remain adapter surfaces",
            () =>
            {
                Contains(
                    linuxXaml,
                    "x:Name=\"ApplicationWorkspacePage\"");
                Contains(
                    linuxXaml,
                    "x:Name=\"RecyclarrWorkspacePage\"");
                Contains(
                    linuxXaml,
                    "x:Name=\"PiHoleWorkspacePage\"");
                Contains(
                    linuxAdapter,
                    "ReplaceSharedSpecializedApplicationPage");
            }),

        (
            "Windows reuses Applications workspace instead of inventing navigation",
            () =>
            {
                Contains(
                    windowsAdapter,
                    "\"IntegrationsPage\"");
                Contains(
                    applicationView,
                    "Back to applications");
                Contains(
                    recyclarrView,
                    "Back to applications");
                Contains(
                    piHoleView,
                    "Back to applications");
                NotContains(
                    windowsMain,
                    "[\"RecyclarrNav\"]");
                NotContains(
                    windowsMain,
                    "[\"PiHoleNav\"]");
            }),

        (
            "specialized adapters preserve target and secret boundaries",
            () =>
            {
                NotContains(
                    shared,
                    "TargetLease");
                NotContains(
                    shared,
                    "SecretValue");
                NotContains(
                    shared,
                    "Credential");
                NotContains(
                    windowsAdapter,
                    "Password");
                NotContains(
                    windowsAdapter,
                    "ApiKey");
            }),

        (
            "WPF remains outside specialized application scope",
            () =>
            {
                NotContains(
                    shared,
                    "GraveOps.Client");
                NotContains(
                    linuxAdapter,
                    "GraveOps.Client");
                NotContains(
                    windowsAdapter,
                    "GraveOps.Client");
            })
    };

var failures =
    0;

foreach (var test in tests)
{
    try
    {
        test.Run();

        Console.WriteLine(
            $"PASS: {test.Name}");
    }
    catch (Exception exception)
    {
        failures++;

        Console.WriteLine(
            $"FAIL: {test.Name}");

        Console.WriteLine(
            exception.Message);
    }
}

Console.WriteLine();

if (failures == 0)
{
    Console.WriteLine(
        $"{tests.Length}/{tests.Length} tests passed.");
}
else
{
    Console.WriteLine(
        $"{tests.Length - failures}/{tests.Length} tests passed.");

    Environment.ExitCode =
        1;
}

static string FindRepositoryRoot()
{
    var current =
        new DirectoryInfo(
            AppContext.BaseDirectory);

    while (current is not null)
    {
        if (Directory.Exists(
                Path.Combine(
                    current.FullName,
                    ".git")))
        {
            return current.FullName;
        }

        current =
            current.Parent;
    }

    throw new InvalidOperationException(
        "Repository root was not found.");
}

static string Read(
    string path)
{
    if (!File.Exists(path))
    {
        throw new FileNotFoundException(
            "Required source file was not found.",
            path);
    }

    return File.ReadAllText(
        path);
}

static void Contains(
    string text,
    string expected)
{
    if (!text.Contains(
            expected,
            StringComparison.Ordinal))
    {
        throw new InvalidOperationException(
            $"Expected source marker was not found: {expected}");
    }
}

static void NotContains(
    string text,
    string forbidden)
{
    if (text.Contains(
            forbidden,
            StringComparison.Ordinal))
    {
        throw new InvalidOperationException(
            $"Forbidden source marker was found: {forbidden}");
    }
}
