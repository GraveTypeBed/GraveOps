var root = FindRoot();

var sharedProjectPath =
    Path.Combine(
        root,
        "src",
        "GraveOps.Presentation.Avalonia",
        "GraveOps.Presentation.Avalonia.csproj");

var windowsProjectPath =
    Path.Combine(
        root,
        "src",
        "GraveOps.Desktop.Windows",
        "GraveOps.Desktop.Windows.csproj");

var linuxProjectPath =
    Path.Combine(
        root,
        "src",
        "GraveOps.Desktop.Linux",
        "GraveOps.Desktop.Linux.csproj");

var migrationPath =
    Path.Combine(
        root,
        "docs",
        "shared-avalonia-presentation-migration.md");

var sharedProject =
    File.ReadAllText(sharedProjectPath);

var windowsProject =
    File.ReadAllText(windowsProjectPath);

var linuxProject =
    File.ReadAllText(linuxProjectPath);

var migration =
    File.ReadAllText(migrationPath);

var tests = new (string Name, Action Run)[]
{
    ("Shared Avalonia project exists", SharedProjectExists),
    ("Shared project remains platform neutral", SharedProjectIsNeutral),
    ("Windows consumes shared presentation", WindowsConsumesSharedPresentation),
    ("Linux consumes shared presentation", LinuxConsumesSharedPresentation),
    ("Exact Linux source is pinned", ExactLinuxSourceIsPinned),
    ("Parity status is explicit", ParityStatusIsExplicit)
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
    $"All {tests.Length} shared-presentation foundation tests passed.");

void SharedProjectExists()
{
    True(
        File.Exists(sharedProjectPath),
        "shared project file exists");

    Present(
        sharedProject,
        "<TargetFramework>net10.0</TargetFramework>");

    Present(
        sharedProject,
        "<PackageReference Include=\"Avalonia\" Version=\"12.1.0\" />");
}

void SharedProjectIsNeutral()
{
    Present(
        sharedProject,
        "..\\GraveOps.Core\\GraveOps.Core.csproj");

    foreach (var forbidden in new[]
    {
        "GraveOps.Platform.Linux",
        "GraveOps.Platform.Windows",
        "System.Windows",
        "PresentationFramework",
        "Microsoft.Win32"
    })
    {
        Missing(
            sharedProject,
            forbidden);
    }
}

void WindowsConsumesSharedPresentation()
{
    Present(
        windowsProject,
        "..\\GraveOps.Presentation.Avalonia\\GraveOps.Presentation.Avalonia.csproj");
}

void LinuxConsumesSharedPresentation()
{
    Present(
        linuxProject,
        "..\\GraveOps.Presentation.Avalonia\\GraveOps.Presentation.Avalonia.csproj");
}

void ExactLinuxSourceIsPinned()
{
    Present(
        migration,
        "8699e7628196d80f6fee111e77bc4f12fae6e229");

    Present(
        migration,
        "68f5c2888c0025a4a7bb28e6880ea187b940c65e");
}

void ParityStatusIsExplicit()
{
    Present(
        migration,
        "Unified dashboard extraction | pending port");

    Present(
        migration,
        "same shared view/control");

    Present(
        migration,
        "runtime screenshots");
}

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