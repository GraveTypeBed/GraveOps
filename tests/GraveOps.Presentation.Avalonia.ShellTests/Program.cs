var root = FindRoot();

var sharedRoot =
    Path.Combine(
        root,
        "src",
        "GraveOps.Presentation.Avalonia",
        "Shell");

var viewSource =
    Read(
        Path.Combine(
            sharedRoot,
            "UnifiedShellView.cs"));

var projectionSource =
    Read(
        Path.Combine(
            sharedRoot,
            "LegacyNavigationProjection.cs"));

var windowsXaml =
    Read(
        Path.Combine(
            root,
            "src",
            "GraveOps.Desktop.Windows",
            "MainWindow.axaml"));

var linuxXaml =
    Read(
        Path.Combine(
            root,
            "src",
            "GraveOps.Desktop.Linux",
            "MainWindow.axaml"));

var windowsAdapter =
    Read(
        Path.Combine(
            root,
            "src",
            "GraveOps.Desktop.Windows",
            "MainWindow.SharedUnifiedShell.cs"));

var linuxAdapter =
    Read(
        Path.Combine(
            root,
            "src",
            "GraveOps.Desktop.Linux",
            "MainWindow.SharedUnifiedShell.cs"));

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

var tests =
    new (string Name, Action Run)[]
    {
        (
            "shared shell owns titlebar and frame geometry",
            SharedShellOwnsFrame),

        (
            "shared shell owns control-plane and target chrome",
            SharedShellOwnsControlPlane),

        (
            "shared shell projects navigation sections and groups",
            SharedShellProjectsNavigation),

        (
            "shared shell owns command header and quick row",
            SharedShellOwnsCommands),

        (
            "shared shell owns page host overlays and footer",
            SharedShellOwnsPageHost),

        (
            "Windows consumes the shared shell",
            WindowsConsumesSharedShell),

        (
            "Linux consumes the shared shell",
            LinuxConsumesSharedShell),

        (
            "both hosts initialize and select through shared shell",
            BothHostsInitializeShell),

        (
            "legacy chrome remains only as a migration adapter",
            LegacyChromeIsAdapter),

        (
            "shared shell remains platform neutral",
            SharedShellIsPlatformNeutral)
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
    $"All {tests.Length} shared shell tests passed.");

void SharedShellOwnsFrame()
{
    Present(
        viewSource,
        "new RowDefinitions(\n                        \"38,*\")");

    Present(
        viewSource,
        "BuildTitleBar()");

    Present(
        viewSource,
        "WindowState.Minimized");

    Present(
        viewSource,
        "ToggleMaximized()");

    Present(
        viewSource,
        "OwnerWindow()?.Close()");

    Present(
        viewSource,
        "this.GetResourceObservable(");

    Present(
        viewSource,
        "this.TryFindResource(");

    Present(
        viewSource,
        "_overlayHost.ZIndex =");

    Missing(
        viewSource,
        "Panel.SetZIndex");
}

void SharedShellOwnsControlPlane()
{
    Present(
        viewSource,
        "\"CONTROL PLANE\"");

    Present(
        viewSource,
        "\"ACTIVE SERVER\"");

    Present(
        viewSource,
        "AttachTargetSelector(");

    Present(
        viewSource,
        "BindConnection(");
}

void SharedShellProjectsNavigation()
{
    Present(
        projectionSource,
        "using Path = Avalonia.Controls.Shapes.Path;");

    Present(
        projectionSource,
        "LegacyNavigationNodeKind.Section");

    Present(
        projectionSource,
        "button.Classes.Contains(\"navGroup\")");

    Present(
        projectionSource,
        "button.Classes.Contains(\"nav\")");

    Present(
        projectionSource,
        "Geometry? IconGeometry");

    Present(
        projectionSource,
        "icon?.Data,");

    Missing(
        projectionSource,
        "icon?.Data?.ToString()");

    Present(
        projectionSource,
        "null,\n                        false,\n                        null,\n                        null));");

    Present(
        projectionSource,
        "null,\n                        false,\n                        button,\n                        null));");

    Missing(
        projectionSource,
        "string.Empty,\n                        false,\n                        null,\n                        null));");

    Missing(
        projectionSource,
        "string.Empty,\n                        false,\n                        button,\n                        null));");

    Present(
        viewSource,
        "BuildNavigationButton(");

    Present(
        viewSource,
        "node.IconGeometry ??");

    Missing(
        viewSource,
        "node.IconData");

    Present(
        viewSource,
        "SelectNavigation(");
}

void SharedShellOwnsCommands()
{
    foreach (var command in new[]
    {
        "\"Overview\"",
        "\"Jobs\"",
        "\"Findings\"",
        "\"Activity\"",
        "\"Terminal\"",
        "\"Maintenance\"",
        "\"Search\"",
        "\"Customize\""
    })
    {
        Present(
            viewSource,
            command);
    }
}

void SharedShellOwnsPageHost()
{
    Present(
        viewSource,
        "AttachPageContent(");

    Present(
        viewSource,
        "AttachOverlays(");

    Present(
        viewSource,
        "new RowDefinitions(\n                        \"72,42,*,26\")");

    Present(
        viewSource,
        "BuildFooter()");
}

void WindowsConsumesSharedShell()
{
    Present(
        windowsXaml,
        "xmlns:shell=\"using:GraveOps.Presentation.Avalonia.Shell\"");

    Present(
        windowsXaml,
        "x:Name=\"SharedShellView\"");

    Present(
        windowsAdapter,
        "shell.AttachPageContent(");

    Present(
        windowsAdapter,
        "\"WPF LEGACY PRESERVED\"");
}

void LinuxConsumesSharedShell()
{
    Present(
        linuxXaml,
        "xmlns:shell=\"using:GraveOps.Presentation.Avalonia.Shell\"");

    Present(
        linuxXaml,
        "x:Name=\"SharedShellView\"");

    Present(
        linuxAdapter,
        "shell.AttachPageContent(");

    Present(
        linuxAdapter,
        "UnifiedShellFooterState");
}

void BothHostsInitializeShell()
{
    Present(
        windowsMain,
        "InitializeSharedUnifiedShell();");

    Present(
        linuxMain,
        "InitializeSharedUnifiedShell();");

    Present(
        windowsMain,
        ".SelectNavigation(\n                navigationName);");

    Present(
        linuxMain,
        ".SelectNavigation(\n                navigationName);");
}

void LegacyChromeIsAdapter()
{
    foreach (var xaml in new[]
    {
        windowsXaml,
        linuxXaml
    })
    {
        Present(
            xaml,
            "x:Name=\"LegacyTitleBar\"");

        Present(
            xaml,
            "x:Name=\"LegacyBody\"");

        Present(
            xaml,
            "x:Name=\"LegacyMainShellGrid\"");

        Present(
            xaml,
            "x:Name=\"LegacyPageHost\"");

        Present(
            xaml,
            "ZIndex=\"200\"");

        Missing(
            xaml,
            "Panel.ZIndex=\"200\"");
    }

    Present(
        windowsAdapter,
        ".Opacity =\n            0;");

    Present(
        linuxAdapter,
        ".Opacity =\n            0;");
}

void SharedShellIsPlatformNeutral()
{
    Missing(
        viewSource,
        "GraveOps.Desktop.Windows");

    Missing(
        viewSource,
        "GraveOps.Desktop.Linux");

    Missing(
        viewSource,
        "GraveOps.Platform.Windows");

    Missing(
        viewSource,
        "GraveOps.Platform.Linux");

    Missing(
        viewSource,
        "System.Windows");

    Missing(
        viewSource,
        "PresentationFramework");
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