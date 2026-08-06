var root =
    FindRoot();

var models =
    Read(
        Path.Combine(
            root,
            "src",
            "GraveOps.Presentation.Avalonia",
            "Fleet",
            "UnifiedFleetModels.cs"));

var view =
    Read(
        Path.Combine(
            root,
            "src",
            "GraveOps.Presentation.Avalonia",
            "Fleet",
            "UnifiedFleetView.cs"));

var linuxAdapter =
    Read(
        Path.Combine(
            root,
            "src",
            "GraveOps.Desktop.Linux",
            "MainWindow.SharedUnifiedFleetApplications.cs"));

var windowsAdapter =
    Read(
        Path.Combine(
            root,
            "src",
            "GraveOps.Desktop.Windows",
            "MainWindow.SharedUnifiedFleetApplications.cs"));

var linuxMain =
    Read(
        Path.Combine(
            root,
            "src",
            "GraveOps.Desktop.Linux",
            "MainWindow.axaml.cs"));

var linuxMedia =
    Read(
        Path.Combine(
            root,
            "src",
            "GraveOps.Desktop.Linux",
            "MainWindow.Media.cs"));

var windowsMain =
    Read(
        Path.Combine(
            root,
            "src",
            "GraveOps.Desktop.Windows",
            "MainWindow.axaml.cs"));

var windowsTargets =
    Read(
        Path.Combine(
            root,
            "src",
            "GraveOps.Desktop.Windows",
            "MainWindow.TargetSession.cs"));

var tests =
    new (string Name, Action Run)[]
    {
        (
            "shared Fleet models own hosts and applications",
            OwnsModels),

        (
            "shared Fleet view owns headings metrics filters and actions",
            OwnsView),

        (
            "shared Fleet presentation remains platform neutral",
            IsNeutral),

        (
            "Linux projects persistent owned fleet inventory",
            LinuxProjectsFleet),

        (
            "Linux delegates target and application actions",
            LinuxDelegatesActions),

        (
            "Windows projects target session and snapshot inventory",
            WindowsProjectsFleet),

        (
            "Windows adapter does not import Linux inventory types",
            WindowsOmitsLinuxTypes),

        (
            "both hosts initialize shared Fleet and Applications",
            BothHostsInitialize),

        (
            "legacy connection editors remain available behind adapters",
            LegacyManagersRemain),

        (
            "both refresh paths update shared Fleet state",
            BothHostsUpdate)
    };

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
        Console.Error.WriteLine(
            $"FAIL: {test.Name}");

        Console.Error.WriteLine(
            exception);

        Environment.ExitCode =
            1;

        return;
    }
}

Console.WriteLine(
    $"All {tests.Length} shared Fleet and Applications tests passed.");

void OwnsModels()
{
    foreach (var marker in new[]
    {
        "UnifiedFleetHostRow(",
        "UnifiedFleetApplicationRow(",
        "UnifiedFleetState(",
        "UnifiedFleetHostRequestedEventArgs",
        "UnifiedFleetApplicationRequestedEventArgs",
        "IsActive",
        "IsStale",
        "CanActivate",
        "CanOpen",
        "CanEditIdentity"
    })
    {
        Present(
            models,
            marker);
    }
}

void OwnsView()
{
    foreach (var marker in new[]
    {
        "\"Fleet & connections\"",
        "\"Applications\"",
        "\"SAVED HOSTS\"",
        "\"STALE INVENTORIES\"",
        "\"VERIFIED\"",
        "\"OWNING TARGETS\"",
        "\"Filter hosts\"",
        "\"Filter applications, owners or state\"",
        "\"Activate host\"",
        "\"Manage connections\"",
        "\"Open application\"",
        "\"Edit identity\"",
        "HostRequested?.Invoke(",
        "ApplicationRequested?.Invoke(",
        "ManageConnectionsRequested?.Invoke(",
        "RefreshRequested?.Invoke("
    })
    {
        Present(
            view,
            marker);
    }
}

void IsNeutral()
{
    foreach (var source in new[]
    {
        models,
        view
    })
    {
        foreach (var forbidden in new[]
        {
            "GraveOps.Desktop.Windows",
            "GraveOps.Desktop.Linux",
            "GraveOps.Platform.Windows",
            "GraveOps.Platform.Linux",
            "PresentationFramework",
            "System.Windows"
        })
        {
            Missing(
                source,
                forbidden);
        }
    }
}

void LinuxProjectsFleet()
{
    foreach (var marker in new[]
    {
        "_controlPlane.Profiles.Profiles",
        "_targetApplicationInventories",
        "_applicationRegistry",
        ".ForTarget(",
        "_mediaRows",
        "Persistent redacted fleet inventory",
        "profile.KindLabel",
        "profile.ConnectionSummary"
    })
    {
        Present(
            linuxAdapter,
            marker);
    }
}

void LinuxDelegatesActions()
{
    Present(
        linuxAdapter,
        "SwitchActiveTargetAsync(");

    Present(
        linuxAdapter,
        "ActivateOwnedApplicationAsync(");

    Present(
        linuxAdapter,
        "NavigationForIntegration(");

    Missing(
        view,
        "SwitchActiveTargetAsync");

    Missing(
        view,
        "ActivateOwnedApplicationAsync");
}

void WindowsProjectsFleet()
{
    foreach (var marker in new[]
    {
        "_targetRows",
        "_targetSession.SelectedTarget",
        "HostSnapshot?",
        "snapshot.Integrations",
        "snapshot.InstalledApplications",
        "SelectActiveTargetAsync(",
        "NavigationForWindowsApplication("
    })
    {
        Present(
            windowsAdapter,
            marker);
    }
}

void WindowsOmitsLinuxTypes()
{
    foreach (var forbidden in new[]
    {
        "LinuxHostProfile",
        "LinuxMediaApplicationRow",
        "ApplicationInventoryCacheStore",
        "OwnedApplicationProjections"
    })
    {
        Missing(
            windowsAdapter,
            forbidden);
    }
}

void BothHostsInitialize()
{
    Present(
        linuxMain,
        "InitializeSharedUnifiedFleetApplications();");

    Present(
        windowsMain,
        "InitializeSharedUnifiedFleetApplications();");

    Present(
        linuxAdapter,
        "new UnifiedFleetView(");

    Present(
        windowsAdapter,
        "new UnifiedFleetView(");

    Present(
        linuxAdapter,
        "\"ServersPage\"");

    Present(
        linuxAdapter,
        "\"MediaHubPage\"");

    Present(
        windowsAdapter,
        "\"ServersPage\"");

    Present(
        windowsAdapter,
        "\"IntegrationsPage\"");
}

void LegacyManagersRemain()
{
    foreach (var source in new[]
    {
        linuxAdapter,
        windowsAdapter
    })
    {
        Present(
            source,
            "_legacyFleetHostControls");

        Present(
            source,
            "ShowLegacyFleetHostManager()");

        Present(
            source,
            "\"Back to fleet\"");

        Present(
            source,
            "ManageConnectionsRequested");
    }
}

void BothHostsUpdate()
{
    Present(
        linuxMedia,
        "UpdateSharedUnifiedFleetApplications();");

    Present(
        windowsMain,
        "UpdateSharedUnifiedFleetApplications(");

    Present(
        windowsTargets,
        "UpdateSharedUnifiedFleetHosts();");
}

static string Read(
    string path) =>
    File.ReadAllText(
            path)
        .Replace(
            "\r\n",
            "\n");

static void Present(
    string source,
    string value)
{
    True(
        source.Contains(
            value,
            StringComparison.Ordinal),
        $"contains {value}");
}

static void Missing(
    string source,
    string value)
{
    True(
        !source.Contains(
            value,
            StringComparison.Ordinal),
        $"omits {value}");
}

static void True(
    bool condition,
    string description)
{
    if (!condition)
    {
        throw new InvalidOperationException(
            description);
    }
}

static string FindRoot()
{
    var current =
        new DirectoryInfo(
            Directory.GetCurrentDirectory());

    while (current is not null)
    {
        if (Directory.Exists(
                Path.Combine(
                    current.FullName,
                    "src")) &&
            Directory.Exists(
                Path.Combine(
                    current.FullName,
                    "tests")))
        {
            return current.FullName;
        }

        current =
            current.Parent;
    }

    throw new DirectoryNotFoundException(
        "GraveOps repository root was not found.");
}