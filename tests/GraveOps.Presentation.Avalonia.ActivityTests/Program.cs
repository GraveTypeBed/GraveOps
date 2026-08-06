var root = FindRoot();

var models =
    Read(
        Path.Combine(
            root,
            "src",
            "GraveOps.Presentation.Avalonia",
            "Activity",
            "UnifiedActivityModels.cs"));

var view =
    Read(
        Path.Combine(
            root,
            "src",
            "GraveOps.Presentation.Avalonia",
            "Activity",
            "UnifiedActivityView.cs"));

var linuxAdapter =
    Read(
        Path.Combine(
            root,
            "src",
            "GraveOps.Desktop.Linux",
            "MainWindow.SharedUnifiedActivity.cs"));

var windowsAdapter =
    Read(
        Path.Combine(
            root,
            "src",
            "GraveOps.Desktop.Windows",
            "MainWindow.SharedUnifiedActivity.cs"));

var linuxMain =
    Read(
        Path.Combine(
            root,
            "src",
            "GraveOps.Desktop.Linux",
            "MainWindow.axaml.cs"));

var linuxHistory =
    Read(
        Path.Combine(
            root,
            "src",
            "GraveOps.Desktop.Linux",
            "MainWindow.HistoryLogs.cs"));

var windowsMain =
    Read(
        Path.Combine(
            root,
            "src",
            "GraveOps.Desktop.Windows",
            "MainWindow.axaml.cs"));

var windowsXaml =
    Read(
        Path.Combine(
            root,
            "src",
            "GraveOps.Desktop.Windows",
            "MainWindow.axaml"));

var tests =
    new (string Name, Action Run)[]
    {
        (
            "shared activity owns Linux history headings",
            OwnsHeadings),

        (
            "shared activity owns reliability filters",
            OwnsFilters),

        (
            "shared activity owns history metrics",
            OwnsMetrics),

        (
            "shared activity owns lists detail and replay actions",
            OwnsWorkspace),

        (
            "shared activity remains platform neutral",
            IsNeutral),

        (
            "Linux projects existing reliability and replay",
            LinuxProjectsReliability),

        (
            "Windows projects real session activity",
            WindowsProjectsSession),

        (
            "both hosts initialize the shared activity workspace",
            BothHostsInitialize),

        (
            "Windows routes HistoryNav to a dedicated page",
            WindowsRoutesDedicatedPage),

        (
            "both event paths update the shared activity workspace",
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
    $"All {tests.Length} shared activity tests passed.");

void OwnsHeadings()
{
    Present(
        view,
        "\"History & incidents\"");

    Present(
        view,
        "\"Classified transitions, meaningful control-plane activity and replayable evidence.\"");

    Present(
        view,
        "\"Selected event detail\"");

    Present(
        view,
        "\"Incident replay\"");
}

void OwnsFilters()
{
    foreach (var marker in new[]
    {
        "\"All meaningful\"",
        "\"All events\"",
        "\"Incidents\"",
        "\"Health transitions\"",
        "\"Actions & changes\"",
        "\"Notifications\"",
        "\"Navigation\"",
        "\"Warnings & errors\"",
        "\"Errors only\"",
        "\"Last 24 hours\"",
        "\"Last 7 days\"",
        "\"All retained\"",
        "\"Reset filters\""
    })
    {
        Present(
            view,
            marker);
    }
}

void OwnsMetrics()
{
    foreach (var marker in new[]
    {
        "\"VISIBLE TRANSITIONS\"",
        "\"VISIBLE ACTIVITY\"",
        "\"VISIBLE INCIDENTS\"",
        "\"VISIBLE / RETAINED\""
    })
    {
        Present(
            view,
            marker);
    }
}

void OwnsWorkspace()
{
    foreach (var marker in new[]
    {
        "\"Health transitions\"",
        "\"Meaningful activity\"",
        "\"Incidents\"",
        "\"Open related\"",
        "\"Copy replay\"",
        "\"Clear history\"",
        "NavigationRequested?.Invoke(",
        "CopyRequested?.Invoke(",
        "ClearRequested?.Invoke("
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

void LinuxProjectsReliability()
{
    Present(
        linuxAdapter,
        "HistoryLogReliabilityPresenter.BuildHistory(");

    Present(
        linuxAdapter,
        "_insightStore.BuildIncidentReplay(");

    Present(
        linuxAdapter,
        "_historyRows.Count");

    Present(
        linuxAdapter,
        "_history.Clear();");

    Present(
        linuxAdapter,
        "_controlPlane.State.ClearActivities();");
}

void WindowsProjectsSession()
{
    Present(
        windowsAdapter,
        "_activity");

    Present(
        windowsAdapter,
        "\"Session-only history - Windows persistence has not been implemented.\"");

    Present(
        windowsAdapter,
        "ActivitySeverity(");

    Present(
        windowsAdapter,
        "BuildWindowsActivityReplay(");

    Present(
        windowsMain,
        "DateTimeOffset Timestamp");

    Missing(
        windowsAdapter,
        "LinuxHistoryStore");

    Missing(
        windowsAdapter,
        "HistoryLogReliabilityPresenter");
}

void BothHostsInitialize()
{
    Present(
        linuxMain,
        "InitializeSharedUnifiedActivity();");

    Present(
        windowsMain,
        "InitializeSharedUnifiedActivity();");

    Present(
        linuxAdapter,
        "new UnifiedActivityView()");

    Present(
        windowsAdapter,
        "new UnifiedActivityView()");
}

void WindowsRoutesDedicatedPage()
{
    Present(
        windowsMain,
        "[\"HistoryNav\"] = new(\"HistoryPage\"");

    Present(
        windowsXaml,
        "x:Name=\"HistoryPage\"");

    Missing(
        windowsMain,
        "[\"HistoryNav\"] = new(\"ParityPage\"");
}

void BothHostsUpdate()
{
    Present(
        linuxHistory,
        "UpdateSharedUnifiedActivity();");

    Present(
        windowsMain,
        "UpdateSharedUnifiedActivity();");

    Present(
        windowsAdapter,
        "PopulateActivity();");

    Present(
        linuxAdapter,
        "PopulateHistoryV43();");
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