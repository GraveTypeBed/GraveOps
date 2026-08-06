var repository =
    FindRepositoryRoot();

var sharedRoot =
    Path.Combine(
        repository,
        "src",
        "GraveOps.Presentation.Avalonia",
        "MediaWorkspaces");

var linuxAdapterPath =
    Path.Combine(
        repository,
        "src",
        "GraveOps.Desktop.Linux",
        "MainWindow.SharedUnifiedMediaWorkspaces.cs");

var windowsAdapterPath =
    Path.Combine(
        repository,
        "src",
        "GraveOps.Desktop.Windows",
        "MainWindow.SharedUnifiedMediaWorkspaces.cs");

var linuxMainPath =
    Path.Combine(
        repository,
        "src",
        "GraveOps.Desktop.Linux",
        "MainWindow.axaml.cs");

var windowsMainPath =
    Path.Combine(
        repository,
        "src",
        "GraveOps.Desktop.Windows",
        "MainWindow.axaml.cs");

var linuxXamlPath =
    Path.Combine(
        repository,
        "src",
        "GraveOps.Desktop.Linux",
        "MainWindow.axaml");

var windowsXamlPath =
    Path.Combine(
        repository,
        "src",
        "GraveOps.Desktop.Windows",
        "MainWindow.axaml");

var models =
    Read(
        Path.Combine(
            sharedRoot,
            "UnifiedMediaModels.cs"));

var mediaHub =
    Read(
        Path.Combine(
            sharedRoot,
            "UnifiedMediaHubView.cs"));

var plex =
    Read(
        Path.Combine(
            sharedRoot,
            "UnifiedPlexView.cs"));

var arr =
    Read(
        Path.Combine(
            sharedRoot,
            "UnifiedArrView.cs"));

var download =
    Read(
        Path.Combine(
            sharedRoot,
            "UnifiedDownloadClientView.cs"));

var lifecycle =
    Read(
        Path.Combine(
            sharedRoot,
            "UnifiedLifecycleView.cs"));

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
        linuxAdapterPath);

var windowsAdapter =
    Read(
        windowsAdapterPath);

var linuxMain =
    Read(
        linuxMainPath);

var windowsMain =
    Read(
        windowsMainPath);

var windowsFleetAdapter =
    Read(
        Path.Combine(
            repository,
            "src",
            "GraveOps.Desktop.Windows",
            "MainWindow.SharedUnifiedFleetApplications.cs"));

var linuxXaml =
    Read(
        linuxXamlPath);

var windowsXaml =
    Read(
        windowsXamlPath);

var tests =
    new (string Name, Action Run)[]
    {
        (
            "shared contracts own five media surfaces",
            () =>
            {
                Contains(
                    models,
                    "UnifiedMediaHubState");
                Contains(
                    models,
                    "UnifiedPlexState");
                Contains(
                    models,
                    "UnifiedArrState");
                Contains(
                    models,
                    "UnifiedDownloadClientState");
                Contains(
                    models,
                    "UnifiedLifecycleState");
            }),

        (
            "shared Media Hub owns Linux fleet and identity language",
            () =>
            {
                Contains(
                    mediaHub,
                    "Media operations");
                Contains(
                    mediaHub,
                    "Fleet overview");
                Contains(
                    mediaHub,
                    "Identity registry");
                Contains(
                    mediaHub,
                    "Application identity registry");
            }),

        (
            "shared Plex owns Linux operational and session language",
            () =>
            {
                Contains(
                    plex,
                    "Plex operations");
                Contains(
                    plex,
                    "Session analytics");
                Contains(
                    plex,
                    "Playback analytics");
                Contains(
                    plex,
                    "READ-ONLY SESSION DATA");
            }),

        (
            "shared Arr owns Linux metrics operations and customization",
            () =>
            {
                Contains(
                    arr,
                    "Service telemetry");
                Contains(
                    arr,
                    "Queue & health");
                Contains(
                    arr,
                    "Docker / stack");
                Contains(
                    arr,
                    "Customize workspace");
            }),

        (
            "shared download client owns Linux analytics and operations",
            () =>
            {
                Contains(
                    download,
                    "Transfer analytics");
                Contains(
                    download,
                    "Workload analytics");
                Contains(
                    download,
                    "Docker / container");
                Contains(
                    download,
                    "Protected download-client connection");
            }),

        (
            "shared lifecycle owns workflow and upstream remediation",
            () =>
            {
                Contains(
                    lifecycle,
                    "Request -> Discovery -> Arr -> Download -> Import -> Processing -> Library");
                Contains(
                    lifecycle,
                    "Active lifecycle items");
                Contains(
                    lifecycle,
                    "Guided remediation");
                Contains(
                    lifecycle,
                    "Open selected step");
            }),

        (
            "shared media presentation remains platform neutral",
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
            }),

        (
            "Linux adapter preserves Media Hub identity handlers",
            () =>
            {
                Contains(
                    linuxAdapter,
                    "MediaModeFleetButton_OnClick");
                Contains(
                    linuxAdapter,
                    "MediaModeLauncherButton_OnClick");
                Contains(
                    linuxAdapter,
                    "MediaGroupIdentityButton_OnClick");
                Contains(
                    linuxAdapter,
                    "MediaLauncherSaveButton_OnClick");
                Contains(
                    linuxAdapter,
                    "MediaLauncherResetButton_OnClick");
                Contains(
                    linuxAdapter,
                    "MediaLauncherOpenButton_OnClick");
            }),

        (
            "Linux adapter preserves guarded Plex operations",
            () =>
            {
                Contains(
                    linuxAdapter,
                    "PlexRestartButton_OnClick");
                Contains(
                    linuxAdapter,
                    "PlexLogsButton_OnClick");
                Contains(
                    linuxAdapter,
                    "PlexTerminalButton_OnClick");
                Contains(
                    linuxAdapter,
                    "PlexIntelligenceButton_OnClick");
            }),

        (
            "Linux adapter preserves Arr customization and operations",
            () =>
            {
                Contains(
                    linuxAdapter,
                    "SaveArrWorkspaceButton_OnClick");
                Contains(
                    linuxAdapter,
                    "ResetArrWorkspaceButton_OnClick");
                Contains(
                    linuxAdapter,
                    "ArrDockerButton_OnClick");
                Contains(
                    linuxAdapter,
                    "ArrWorkspaceIntelligenceButton_OnClick");
            }),

        (
            "Linux adapter preserves download client handoffs",
            () =>
            {
                Contains(
                    linuxAdapter,
                    "DownloadClientOpenButton_OnClick");
                Contains(
                    linuxAdapter,
                    "DownloadClientDockerButton_OnClick");
                Contains(
                    linuxAdapter,
                    "DownloadClientLogsButton_OnClick");
                Contains(
                    linuxAdapter,
                    "DownloadClientTerminalButton_OnClick");
            }),

        (
            "Windows adapter preserves protected configuration handlers",
            () =>
            {
                Contains(
                    windowsAdapter,
                    "PlexSaveTestButton_OnClick");
                Contains(
                    windowsAdapter,
                    "ArrSaveTestButton_OnClick");
                Contains(
                    windowsAdapter,
                    "SABnzbdSaveTestButton_OnClick");
                Contains(
                    windowsAdapter,
                    "QBittorrentSaveTestButton_OnClick");
            }),

        (
            "Windows adapter never projects stored secrets",
            () =>
            {
                Contains(
                    windowsAdapter,
                    "if (!string.IsNullOrWhiteSpace");
                NotContains(
                    windowsAdapter,
                    "SharedLegacyText(\n                        \"PlexTokenTextBox\")");
                NotContains(
                    windowsAdapter,
                    "SharedLegacyText(\n                        \"ArrApiKeyTextBox\")");
                NotContains(
                    windowsAdapter,
                    "SharedLegacyText(\n                        \"SABnzbdApiKeyTextBox\")");
                NotContains(
                    windowsAdapter,
                    "SharedLegacyText(\n                        \"QBittorrentPasswordTextBox\")");
            }),

        (
            "Windows media operations remain read only where unsupported",
            () =>
            {
                Contains(
                    windowsAdapter,
                    "CanRestart:\n                    false");
                Contains(
                    windowsAdapter,
                    "CanOpenDocker:\n                    false");
                Contains(
                    windowsAdapter,
                    "CanOpenTerminal:\n                    false");
                NotContains(
                    windowsAdapter,
                    "LinuxOperatorTools");
                NotContains(
                    windowsAdapter,
                    "LinuxOpsAnalyzer");
            }),

        (
            "both hosts initialize shared media workspaces",
            () =>
            {
                Contains(
                    linuxMain,
                    "InitializeSharedUnifiedMediaWorkspaces();");
                Contains(
                    windowsMain,
                    "InitializeSharedUnifiedMediaWorkspaces();");
                Contains(
                    linuxMain,
                    "DisposeSharedUnifiedMediaWorkspaces();");
                Contains(
                    windowsMain,
                    "DisposeSharedUnifiedMediaWorkspaces();");
            }),

        (
            "Windows media projection waits for target initialization",
            () =>
            {
                Contains(
                    windowsMain,
                    "await RefreshAsync();\n            UpdateSharedUnifiedMediaWorkspaces();");
                NotContains(
                    windowsAdapter,
                    "_sharedMediaSyncTimer.Start();\n\n        UpdateSharedUnifiedMediaWorkspaces();");
            }),

        (
            "legacy media pages remain adapter surfaces",
            () =>
            {
                Contains(
                    linuxXaml,
                    "x:Name=\"MediaHubPage\"");
                Contains(
                    linuxXaml,
                    "x:Name=\"PlexWorkspacePage\"");
                Contains(
                    linuxXaml,
                    "x:Name=\"ArrWorkspacePage\"");
                Contains(
                    linuxXaml,
                    "x:Name=\"DownloadClientWorkspacePage\"");
                Contains(
                    windowsXaml,
                    "x:Name=\"MediaHubPage\"");
                Contains(
                    windowsXaml,
                    "x:Name=\"IntegrationsPage\"");
                Contains(
                    windowsXaml,
                    "x:Name=\"PlexPage\"");
                Contains(
                    windowsXaml,
                    "x:Name=\"ArrPage\"");
                Contains(
                    windowsXaml,
                    "x:Name=\"SABnzbdPage\"");
                Contains(
                    windowsXaml,
                    "x:Name=\"QBittorrentPage\"");
            }),

        (
            "Windows Media Hub owns a dedicated page without replacing Fleet Applications",
            () =>
            {
                Contains(
                    windowsMain,
                    "[\"MediaHubNav\"] = new(\"MediaHubPage\"");
                Contains(
                    windowsMain,
                    "[\"IntegrationsNav\"] = new(\"IntegrationsPage\"");
                Contains(
                    windowsAdapter,
                    "\"MediaHubPage\"");
                Contains(
                    windowsFleetAdapter,
                    "\"IntegrationsPage\"");
                NotContains(
                    windowsAdapter,
                    "ReplaceSharedMediaPage(\n            \"IntegrationsPage\"");
            }),

        (
            "Windows keeps separate shared SABnzbd and qBittorrent views",
            () =>
            {
                Contains(
                    windowsAdapter,
                    "_sharedSabnzbdView");
                Contains(
                    windowsAdapter,
                    "_sharedQBittorrentView");
                Contains(
                    windowsAdapter,
                    "\"SABnzbdPage\"");
                Contains(
                    windowsAdapter,
                    "\"QBittorrentPage\"");
            }),

        (
            "shared media synchronization follows visible workspaces",
            () =>
            {
                Contains(
                    linuxAdapter,
                    "SharedMediaPageVisible()");
                Contains(
                    windowsAdapter,
                    "SharedMediaPageVisible()");
                Contains(
                    linuxAdapter,
                    "TimeSpan.FromMilliseconds");
                Contains(
                    windowsAdapter,
                    "TimeSpan.FromMilliseconds");
            }),

        (
            "media adapters do not replace target lease engines",
            () =>
            {
                NotContains(
                    linuxAdapter,
                    "TargetLease");
                NotContains(
                    windowsAdapter,
                    "TargetLease");
                NotContains(
                    shared,
                    "TargetLease");
            }),

        (
            "WPF remains outside shared media scope",
            () =>
            {
                NotContains(
                    shared,
                    "System.Windows");
                NotContains(
                    linuxAdapter,
                    "System.Windows");
                NotContains(
                    windowsAdapter,
                    "System.Windows");
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
