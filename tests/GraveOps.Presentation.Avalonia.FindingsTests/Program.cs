var root = FindRoot();

var models =
    Read(
        Path.Combine(
            root,
            "src",
            "GraveOps.Presentation.Avalonia",
            "Findings",
            "UnifiedFindingsModels.cs"));

var view =
    Read(
        Path.Combine(
            root,
            "src",
            "GraveOps.Presentation.Avalonia",
            "Findings",
            "UnifiedFindingsView.cs"));

var windowsAdapter =
    Read(
        Path.Combine(
            root,
            "src",
            "GraveOps.Desktop.Windows",
            "MainWindow.SharedUnifiedFindings.cs"));

var linuxAdapter =
    Read(
        Path.Combine(
            root,
            "src",
            "GraveOps.Desktop.Linux",
            "MainWindow.SharedUnifiedFindings.cs"));

var windowsMain =
    Read(
        Path.Combine(
            root,
            "src",
            "GraveOps.Desktop.Windows",
            "MainWindow.axaml.cs"));

var linuxMain =
    Read(
        Path.Combine(
            root,
            "src",
            "GraveOps.Desktop.Linux",
            "MainWindow.axaml.cs"));

var linuxInsight =
    Read(
        Path.Combine(
            root,
            "src",
            "GraveOps.Desktop.Linux",
            "MainWindow.Insight.cs"));

var tests =
    new (string Name, Action Run)[]
    {
        (
            "shared findings owns exact intelligence headings",
            OwnsHeadings),

        (
            "shared findings owns environment and remediation",
            OwnsContext),

        (
            "shared findings owns metrics and evidence grids",
            OwnsMetrics),

        (
            "shared findings owns selection and report actions",
            OwnsActions),

        (
            "shared findings contracts remain platform neutral",
            ContractsAreNeutral),

        (
            "Linux projects existing insight analysis",
            LinuxProjectsInsight),

        (
            "Windows projects provider recommendations conservatively",
            WindowsProjectsEvidence),

        (
            "both hosts initialize the shared findings workspace",
            BothHostsInitialize),

        (
            "both hosts retain legacy pages only as adapters",
            LegacyPagesAreAdapters),

        (
            "both refresh paths update the shared findings workspace",
            RefreshPathsUpdate)
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
    $"All {tests.Length} shared findings tests passed.");

void OwnsHeadings()
{
    Present(
        view,
        "\"Control plane intelligence\"");

    Present(
        view,
        "\"Root cause, dependency impact and the safest next control from live GraveOps telemetry.\"");

    Present(
        view,
        "\"State history\"");

    Present(
        view,
        "\"Analyze now\"");
}

void OwnsContext()
{
    Present(
        view,
        "\"Environment context\"");

    Present(
        view,
        "\"Fleet-wide impact before selected-host root-cause analysis.\"");

    Present(
        view,
        "\"Guided remediation\"");

    Present(
        view,
        "\"Upstream-first troubleshooting from environment ownership and live telemetry.\"");
}

void OwnsMetrics()
{
    foreach (var label in new[]
    {
        "\"OVERALL\"",
        "\"BLOCKERS\"",
        "\"WARNINGS\"",
        "\"ROOT CAUSE\"",
        "\"Dependency state\"",
        "\"Priority findings\""
    })
    {
        Present(
            view,
            label);
    }
}

void OwnsActions()
{
    Present(
        view,
        "\"Open selected\"");

    Present(
        view,
        "\"Open related\"");

    Present(
        view,
        "\"Copy report\"");

    Present(
        view,
        "UnifiedFindingsReport.Build(");

    Present(
        view,
        "NavigationRequested?.Invoke(");
}

void ContractsAreNeutral()
{
    foreach (var source in new[]
    {
        models,
        view
    })
    {
        Missing(
            source,
            "GraveOps.Desktop.Windows");

        Missing(
            source,
            "GraveOps.Desktop.Linux");

        Missing(
            source,
            "GraveOps.Platform.Windows");

        Missing(
            source,
            "GraveOps.Platform.Linux");

        Missing(
            source,
            "System.Windows");

        Missing(
            source,
            "PresentationFramework");
    }
}

void LinuxProjectsInsight()
{
    Present(
        linuxAdapter,
        "_insightStore.BuildAttention(");

    Present(
        linuxAdapter,
        "_intelligenceDependencies");

    Present(
        linuxAdapter,
        "_analysis.Findings");

    Present(
        linuxAdapter,
        "_intelligenceRemediation");

    Present(
        linuxAdapter,
        "_insightStore.BuildIntelligenceReport(");

    Present(
        linuxAdapter,
        "LinuxInsightStore\n                                .NavigationForComponent(");
}

void WindowsProjectsEvidence()
{
    Present(
        windowsAdapter,
        "IReadOnlyList<RecommendationRow> recommendations");

    Present(
        windowsAdapter,
        "HealthSummary health");

    Present(
        windowsAdapter,
        "BuildWindowsDependencies(");

    Present(
        windowsAdapter,
        "RecommendationImpact(");

    Present(
        windowsAdapter,
        "RecommendationNextStep(");

    Missing(
        windowsAdapter,
        "LinuxOpsAnalyzer");

    Missing(
        windowsAdapter,
        "LinuxInsightStore");
}

void BothHostsInitialize()
{
    Present(
        windowsMain,
        "InitializeSharedUnifiedFindings();");

    Present(
        linuxMain,
        "InitializeSharedUnifiedFindings();");

    Present(
        windowsAdapter,
        "new UnifiedFindingsView()");

    Present(
        linuxAdapter,
        "new UnifiedFindingsView()");
}

void LegacyPagesAreAdapters()
{
    Present(
        windowsAdapter,
        "Get<Grid>(\n                \"WarningsPage\")");

    Present(
        linuxAdapter,
        "Get<Grid>(\n                \"IntelligencePage\")");

    Present(
        windowsAdapter,
        "child.IsVisible =\n                false;");

    Present(
        linuxAdapter,
        "child.IsVisible =\n                false;");
}

void RefreshPathsUpdate()
{
    Present(
        windowsMain,
        "UpdateSharedUnifiedFindings(\n            snapshot,\n            recommendations,\n            health);");

    Present(
        linuxInsight,
        "UpdateSharedUnifiedFindings();");

    Present(
        windowsAdapter,
        "await RefreshAsync();");

    Present(
        linuxAdapter,
        "await RefreshAsync();");
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