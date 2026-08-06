using System.Text.RegularExpressions;

var root = FindRoot();
var markup = File.ReadAllText(Path.Combine(root, "src", "GraveOps.Desktop.Windows", "MainWindow.axaml"));
var code = File.ReadAllText(Path.Combine(root, "src", "GraveOps.Desktop.Windows", "MainWindow.axaml.cs"));
var plex = File.ReadAllText(Path.Combine(root, "src", "GraveOps.Desktop.Windows", "MainWindow.Plex.cs"));

var tests = new (string Name, Action Run)[]
{
    ("Plex is reachable without provider discovery", PlexVisible),
    ("Linux-style navigation groups are present", GroupsPresent),
    ("Manual API workspaces start visible", ManualAppsVisible),
    ("Navigation groups can collapse", CollapseBehavior),
    ("Provider refresh preserves collapsed group state", RefreshPreservesCollapse),
    ("Plex discovery cannot hide Plex", DiscoverySafe),
    ("Plex remains routed to its telemetry page", PlexRoute)
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

Console.WriteLine($"All {tests.Length} Windows presentation-parity tests passed.");

void PlexVisible()
{
    Visible("PlexNav");
    Missing(markup, "LibraryGroupLabel");
}

void GroupsPresent()
{
    Present(markup, "x:Name=\"LibraryGroupButton\"");
    Present(markup, "x:Name=\"LibraryNavGroup\"");
    Present(markup, "x:Name=\"AcquisitionGroupButton\"");
    Present(markup, "x:Name=\"AcquisitionNavGroup\"");
    Missing(markup, "AcquisitionGroupLabel");
}

void ManualAppsVisible()
{
    foreach (var name in new[]
    {
        "PlexNav", "SonarrNav", "RadarrNav", "LidarrNav",
        "ProwlarrNav", "SABnzbdNav", "QBittorrentNav"
    })
        Visible(name);
}

void CollapseBehavior()
{
    Present(code, "LibraryGroupButton_OnClick");
    Present(code, "AcquisitionGroupButton_OnClick");
    Present(code, "ToggleNavigationGroup");
    Present(code, "Geometry.Parse");
}

void RefreshPreservesCollapse()
{
    var initializeStart =
        code.IndexOf(
            "private void InitializeWindowsMediaNavigation()",
            StringComparison.Ordinal);

    var policyStart =
        code.IndexOf(
            "private void ApplyWindowsMediaNavigationAvailability()",
            initializeStart,
            StringComparison.Ordinal);

    var policyEnd =
        code.IndexOf(
            "private void LibraryGroupButton_OnClick",
            policyStart,
            StringComparison.Ordinal);

    Check(
        initializeStart >= 0 &&
        policyStart > initializeStart &&
        policyEnd > policyStart,
        "media-navigation method ranges");

    var initialize =
        code[initializeStart..policyStart];

    var availability =
        code[policyStart..policyEnd];

    Present(
        initialize,
        "\"LibraryNavGroup\"");

    Present(
        initialize,
        "\"AcquisitionNavGroup\"");

    Missing(
        availability,
        "\"LibraryNavGroup\"");

    Missing(
        availability,
        "\"AcquisitionNavGroup\"");
}

void DiscoverySafe()
{
    var start = plex.IndexOf("private void UpdatePlexDiscovery", StringComparison.Ordinal);
    var end = plex.IndexOf("private void OnPlexTargetChanged", start, StringComparison.Ordinal);

    Check(start >= 0 && end > start, "Plex discovery method range");

    var method = plex[start..end];
    Missing(method, "\"PlexNav\"");
    Missing(method, "LibraryGroupLabel");
    Present(method, "ApplyWindowsMediaNavigationAvailability");
}

void PlexRoute()
{
    Present(code, "[\"PlexNav\"] = new(\"PlexPage\", \"Plex\"");
    Present(code, "ActivateWindowsPlexWorkspace");
}

void Visible(string name)
{
    var pattern = $@"(?s)<Button\s+[^>]*x:Name=""{Regex.Escape(name)}""[^>]*IsVisible=""True""";
    Check(Regex.IsMatch(markup, pattern), $"{name} visible");
}

static void Present(string source, string value) =>
    Check(source.Contains(value, StringComparison.Ordinal), value);

static void Missing(string source, string value) =>
    Check(!source.Contains(value, StringComparison.Ordinal), $"omits {value}");

static void Check(bool condition, string description)
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