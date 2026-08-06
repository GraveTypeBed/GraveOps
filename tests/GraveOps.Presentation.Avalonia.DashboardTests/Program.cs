using GraveOps.Presentation.Avalonia.Dashboard;

var root = FindRoot();

var sharedRoot =
    Path.Combine(
        root,
        "src",
        "GraveOps.Presentation.Avalonia",
        "Dashboard");

var viewSource =
    File.ReadAllText(
        Path.Combine(
            sharedRoot,
            "UnifiedDashboardView.cs"));

var windowsXaml =
    File.ReadAllText(
        Path.Combine(
            root,
            "src",
            "GraveOps.Desktop.Windows",
            "MainWindow.axaml"));

var linuxXaml =
    File.ReadAllText(
        Path.Combine(
            root,
            "src",
            "GraveOps.Desktop.Linux",
            "MainWindow.axaml"));

var windowsBridge =
    File.ReadAllText(
        Path.Combine(
            root,
            "src",
            "GraveOps.Desktop.Windows",
            "MainWindow.SharedUnifiedDashboard.cs"));

var linuxBridge =
    File.ReadAllText(
        Path.Combine(
            root,
            "src",
            "GraveOps.Desktop.Linux",
            "MainWindow.SharedUnifiedDashboard.cs"));

var tests = new (string Name, Action Run)[]
{
    ("layout adds newly detected cards", LayoutAddsNewCards),
    ("layout preserves host ordering", LayoutPreservesOrdering),
    ("visible card filtering follows preferences", VisibleFiltering),
    ("shared renderer owns Linux section geometry", SharedRendererOwnsGeometry),
    ("Windows consumes shared Dashboard control", WindowsConsumesSharedControl),
    ("Linux consumes shared Dashboard control", LinuxConsumesSharedControl),
    ("both hosts project platform cards into shared contracts", HostsProjectSharedContracts),
    ("shared renderer remains platform neutral", SharedRendererIsPlatformNeutral)
};

foreach (var test in tests)
{
    try
    {
        test.Run();
        Console.WriteLine($"PASS: {test.Name}");
    }
    catch (Exception exception)
    {
        Console.Error.WriteLine($"FAIL: {test.Name}");
        Console.Error.WriteLine(exception);
        Environment.ExitCode = 1;
        return;
    }
}

Console.WriteLine(
    $"All {tests.Length} shared unified-Dashboard tests passed.");

void LayoutAddsNewCards()
{
    var cards =
        Cards(
            "core:host",
            "core:storage",
            "app:plex");

    var saved =
        new[]
        {
            new DashboardCardPreference(
                "core:storage",
                false,
                0),
            new DashboardCardPreference(
                "core:host",
                true,
                1)
        };

    var resolved =
        DashboardLayoutResolver.Resolve(
            cards,
            saved);

    Equal(
        3,
        resolved.Count,
        "resolved count");

    True(
        resolved.Any(item =>
            item.Key == "app:plex" &&
            item.IsVisible),
        "new card uses default visibility");
}

void LayoutPreservesOrdering()
{
    var cards =
        Cards(
            "core:host",
            "core:storage",
            "app:plex");

    var saved =
        new[]
        {
            new DashboardCardPreference(
                "app:plex",
                true,
                0),
            new DashboardCardPreference(
                "core:host",
                true,
                1),
            new DashboardCardPreference(
                "core:storage",
                true,
                2)
        };

    var resolved =
        DashboardLayoutResolver.Resolve(
            cards,
            saved);

    Equal(
        "app:plex",
        resolved[0].Key,
        "first key");
}

void VisibleFiltering()
{
    var cards =
        Cards(
            "core:host",
            "core:storage",
            "app:plex");

    var layout =
        new[]
        {
            new DashboardCardPreference(
                "core:host",
                true,
                0),
            new DashboardCardPreference(
                "core:storage",
                false,
                1),
            new DashboardCardPreference(
                "app:plex",
                true,
                2)
        };

    var visible =
        DashboardLayoutResolver.VisibleCards(
            cards,
            layout);

    Equal(
        2,
        visible.Count,
        "visible count");

    Equal(
        "app:plex",
        visible[1].Key,
        "second visible key");
}

void SharedRendererOwnsGeometry()
{
    Missing(
        viewSource,
        "private readonly TextBlock _emptyText;");

    Missing(
        viewSource,
        "Child =\n                        _emptyText");

    foreach (var value in new[]
    {
        "\"Infrastructure\"",
        "\"Operations\"",
        "\"Media\"",
        "\"Applications\"",
        "\"dashboardProviderCard\"",
        "\"dashboardCardShell\"",
        "\"dashboardInfoButton\"",
        "\"unifiedAttentionStrip\"",
        "ResolveColumns(",
        "BuildCard(",
        "PopulatePicker()",
        "FlyoutShowMode.Standard",
        "No Dashboard cards are visible. Open Customize cards to restore modules."
    })
    {
        Present(
            viewSource,
            value);
    }
}

void WindowsConsumesSharedControl()
{
    Present(
        windowsXaml,
        "xmlns:dashboard=\"using:GraveOps.Presentation.Avalonia.Dashboard\"");

    Present(
        windowsXaml,
        "<dashboard:UnifiedDashboardView");

    Present(
        windowsXaml,
        "x:Name=\"SharedDashboardView\"");

    Present(
        windowsXaml,
        "x:Name=\"LegacyWindowsDashboardScrollViewer\" IsVisible=\"False\"");
}

void LinuxConsumesSharedControl()
{
    Present(
        linuxXaml,
        "xmlns:dashboard=\"using:GraveOps.Presentation.Avalonia.Dashboard\"");

    Present(
        linuxXaml,
        "<dashboard:UnifiedDashboardView");

    Present(
        linuxXaml,
        "x:Name=\"UnifiedDashboardScrollViewer\"\n                  IsVisible=\"False\"");
}

void HostsProjectSharedContracts()
{
    Present(
        windowsBridge,
        "BuildWindowsSharedDashboardCards(");

    Present(
        windowsBridge,
        "new UnifiedDashboardState(");

    Present(
        linuxBridge,
        "MapSharedDashboardCard(");

    Present(
        linuxBridge,
        "new UnifiedDashboardState(");

    Present(
        linuxBridge,
        "UnifiedDashboardActionButton_OnClick(");

    Present(
        linuxBridge,
        "action.IncludeInformationalLogs");
}

void SharedRendererIsPlatformNeutral()
{
    foreach (var forbidden in new[]
    {
        "GraveOps.Desktop.Linux",
        "GraveOps.Desktop.Windows",
        "GraveOps.Platform.Linux",
        "GraveOps.Platform.Windows",
        "System.Windows",
        "PresentationFramework"
    })
    {
        Missing(
            viewSource,
            forbidden);
    }
}

static IReadOnlyList<UnifiedDashboardCard> Cards(
    params string[] keys) =>
    keys
        .Select(
            (key, index) =>
                new UnifiedDashboardCard(
                    key,
                    key,
                    "Test",
                    "READY",
                    DashboardSeverity.Healthy,
                    index.ToString(),
                    string.Empty,
                    string.Empty,
                    "Open",
                    "DashboardNav",
                    string.Empty,
                    key,
                    true))
        .ToArray();

static void Present(
    string text,
    string value)
{
    True(
        text.Contains(
            value,
            StringComparison.Ordinal),
        $"contains {value}");
}

static void Missing(
    string text,
    string value)
{
    True(
        !text.Contains(
            value,
            StringComparison.Ordinal),
        $"omits {value}");
}

static void Equal<T>(
    T expected,
    T actual,
    string description)
{
    if (!EqualityComparer<T>.Default.Equals(
            expected,
            actual))
    {
        throw new InvalidOperationException(
            $"{description}: expected {expected}, actual {actual}");
    }
}

static void True(
    bool condition,
    string description)
{
    if (!condition)
        throw new InvalidOperationException(description);
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

        current = current.Parent;
    }

    throw new DirectoryNotFoundException(
        "GraveOps repository root was not found.");
}