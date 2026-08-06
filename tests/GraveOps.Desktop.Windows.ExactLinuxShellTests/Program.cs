using System.Security.Cryptography;
using System.Text;

var root = FindRoot();
var windowsRoot = Path.Combine(root, "src", "GraveOps.Desktop.Windows");
var appPath = Path.Combine(windowsRoot, "App.axaml");
var windowPath = Path.Combine(windowsRoot, "MainWindow.axaml");
var projectPath = Path.Combine(windowsRoot, "GraveOps.Desktop.Windows.csproj");
var docPath = Path.Combine(root, "docs", "windows-avalonia-exact-linux-visual-source.md");

var app = File.ReadAllText(appPath).Replace("\r\n", "\n");
var window = File.ReadAllText(windowPath).Replace("\r\n", "\n");
var project = File.ReadAllText(projectPath).Replace("\r\n", "\n");
var documentation = File.ReadAllText(docPath).Replace("\r\n", "\n");

var tests = new (string Name, Action Run)[]
{
    ("Windows theme matches the pinned Linux App XAML", ThemeMatchesPinnedLinux),
    ("Pinned Linux visual source is documented", VisualSourceIsPinned),
    ("Exact Linux brand asset is packaged and used", BrandAssetIsUsed),
    ("Linux navigation language is present", NavigationLanguageMatches),
    ("Lifecycle follows Media Hub in the Media section", LifecycleIsInMedia),
    ("Working Windows telemetry pages remain registered", TelemetryPagesRemain),
    ("Removed flat navigation controls are not requested", RemovedControlsAreAbsent),
    ("Windows desktop source contains no mojibake", SourceEncodingIsClean),
    ("Windows presentation keeps platform boundaries", PlatformBoundariesRemain)
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

Console.WriteLine($"All {tests.Length} exact-Linux shell tests passed.");

void ThemeMatchesPinnedLinux()
{
    const string expected =
        "db11d488514c27f5c74c8a1967856e84486d828e813b19c327c8724295ad22fa";

    var linuxClass =
        app.Replace(
            "x:Class=\"GraveOps.Desktop.Windows.App\"",
            "x:Class=\"GraveOps.Desktop.Linux.App\"",
            StringComparison.Ordinal);

    var hash =
        Convert.ToHexString(
            SHA256.HashData(
                Encoding.UTF8.GetBytes(linuxClass)))
        .ToLowerInvariant();

    Equal(expected, hash, "pinned Linux App.axaml SHA-256");
}

void VisualSourceIsPinned()
{
    Present(documentation, "8699e7628196d80f6fee111e77bc4f12fae6e229");
    Present(documentation, "4852617aaf9590e2d4303bb67898a538d6ffa2e30f79133b27c8ebf1bc43c6e4");
    Present(documentation, "Do not substitute the newer");
}

void BrandAssetIsUsed()
{
    var asset =
        Path.Combine(
            windowsRoot,
            "Assets",
            "graveops-brandmark.png");

    True(File.Exists(asset), "brandmark asset exists");
    True(new FileInfo(asset).Length > 0, "brandmark asset is not empty");
    Present(project, "<AvaloniaResource Include=\"Assets\\**\" />");
    Present(window, "Icon=\"/Assets/graveops-brandmark.png\"");
    Present(window, "Source=\"/Assets/graveops-brandmark.png\"");
}

void NavigationLanguageMatches()
{
    Present(window, "Text=\"Health &amp; Findings\"");
    Present(window, "Text=\"Activity &amp; Incidents\"");
    Present(window, "Text=\"Hosts &amp; Connections\"");
    Present(window, "Text=\"SYSTEM\"");
    Missing(window, "Text=\"INFRASTRUCTURE\"");
}

void LifecycleIsInMedia()
{
    var mediaHub =
        window.IndexOf(
            "x:Name=\"MediaHubNav\"",
            StringComparison.Ordinal);

    var lifecycle =
        window.IndexOf(
            "x:Name=\"LifecycleNav\"",
            StringComparison.Ordinal);

    var library =
        window.IndexOf(
            "x:Name=\"LibraryGroupButton\"",
            StringComparison.Ordinal);

    True(mediaHub >= 0, "Media Hub navigation exists");
    True(lifecycle > mediaHub, "Lifecycle appears after Media Hub");
    True(library > lifecycle, "Lifecycle remains inside Media before Library");
}

void TelemetryPagesRemain()
{
    foreach (var value in new[]
    {
        "x:Name=\"PlexPage\"",
        "x:Name=\"ArrPage\"",
        "x:Name=\"QBittorrentPage\"",
        "x:Name=\"SABnzbdPage\"",
        "x:Name=\"LifecyclePage\""
    })
    {
        Present(window, value);
    }
}

void RemovedControlsAreAbsent()
{
    foreach (var file in Directory.EnumerateFiles(
                 windowsRoot,
                 "*.cs",
                 SearchOption.AllDirectories))
    {
        var source = File.ReadAllText(file);
        Missing(source, "AcquisitionGroupLabel");
        Missing(source, "LibraryGroupLabel");
    }
}

void SourceEncodingIsClean()
{
    foreach (var file in Directory.EnumerateFiles(
                 windowsRoot,
                 "*",
                 SearchOption.AllDirectories)
             .Where(path =>
                 path.EndsWith(".cs", StringComparison.OrdinalIgnoreCase) ||
                 path.EndsWith(".axaml", StringComparison.OrdinalIgnoreCase) ||
                 path.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase)))
    {
        var source = File.ReadAllText(file);

        Missing(source, "Â");
        Missing(source, "Ã");
        Missing(source, "â€");
    }

    Present(window, "Text=\"✓\"");
    Present(window, "Windows Avalonia · read-only");
}

void PlatformBoundariesRemain()
{
    foreach (var file in Directory.EnumerateFiles(
                 windowsRoot,
                 "*.cs",
                 SearchOption.AllDirectories))
    {
        var source = File.ReadAllText(file);
        Missing(source, "GraveOps.Platform.Linux");
        Missing(source, "LocalLinuxHostProbe");
        Missing(source, "LinuxControlPlane");
    }

    Present(project, "..\\GraveOps.Platform.Windows\\GraveOps.Platform.Windows.csproj");
    Missing(project, "GraveOps.Platform.Linux");
    Missing(project, "PresentationFramework");
    Missing(project, "System.Windows");
}

static void Present(string source, string value)
{
    True(
        source.Contains(value, StringComparison.Ordinal),
        $"contains {value}");
}

static void Missing(string source, string value)
{
    True(
        !source.Contains(value, StringComparison.Ordinal),
        $"omits {value}");
}

static void Equal(string expected, string actual, string description)
{
    if (!string.Equals(expected, actual, StringComparison.Ordinal))
        throw new InvalidOperationException(
            $"{description}: expected {expected}, actual {actual}");
}

static void True(bool condition, string description)
{
    if (!condition)
        throw new InvalidOperationException(description);
}

static string FindRoot()
{
    var current = new DirectoryInfo(Directory.GetCurrentDirectory());

    while (current is not null)
    {
        if (Directory.Exists(Path.Combine(current.FullName, "src")) &&
            Directory.Exists(Path.Combine(current.FullName, "tests")))
            return current.FullName;

        current = current.Parent;
    }

    throw new DirectoryNotFoundException("GraveOps repository root was not found.");
}