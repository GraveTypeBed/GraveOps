var root =
    FindRepositoryRoot();

var tests =
    new (string Name, Action Run)[]
    {
        (
            "shared contracts own four operations surfaces",
            () =>
                RequireAll(
                    Read(
                        "src/GraveOps.Presentation.Avalonia/OperationsWorkspaces/UnifiedOperationsModels.cs"),
                    "UnifiedDockerState",
                    "UnifiedBackupsState",
                    "UnifiedSettingsState",
                    "UnifiedToolsState")),
        (
            "shared Docker owns Linux operational language",
            () =>
                RequireAll(
                    Read(
                        "src/GraveOps.Presentation.Avalonia/OperationsWorkspaces/UnifiedDockerView.cs"),
                    "Compose-aware fleet health",
                    "NEEDS ATTENTION",
                    "COMPOSE PROJECTS",
                    "Recent container logs",
                    "Restart DUMB project")),
        (
            "shared Backups owns readiness and evidence",
            () =>
                RequireAll(
                    Read(
                        "src/GraveOps.Presentation.Avalonia/OperationsWorkspaces/UnifiedBackupsView.cs"),
                    "Backup inventory",
                    "Schedules & units",
                    "Recent artifacts",
                    "Shareable by design")),
        (
            "shared Settings owns Linux settings sections",
            () =>
                RequireAll(
                    Read(
                        "src/GraveOps.Presentation.Avalonia/OperationsWorkspaces/UnifiedSettingsView.cs"),
                    "Interface & setup",
                    "Operator defaults",
                    "Policy management",
                    "Application paths",
                    "Version and update state")),
        (
            "shared Tools owns six Linux tool tabs",
            () =>
                RequireAll(
                    Read(
                        "src/GraveOps.Presentation.Avalonia/OperationsWorkspaces/UnifiedToolsView.cs"),
                    "Terminal",
                    "Diagnostics",
                    "Files / SFTP",
                    "Script Library",
                    "Updates",
                    "Parity")),
        (
            "shared operations presentation remains platform neutral",
            () =>
                ForbidAll(
                    ReadDirectory(
                        "src/GraveOps.Presentation.Avalonia/OperationsWorkspaces"),
                    "GraveOps.Desktop.Windows",
                    "GraveOps.Desktop.Linux",
                    "GraveOps.Platform.Windows",
                    "GraveOps.Platform.Linux",
                    "System.Windows")),
        (
            "Linux adapter preserves guarded Docker operations",
            () =>
                RequireAll(
                    Read(
                        "src/GraveOps.Desktop.Linux/MainWindow.SharedUnifiedOperationsWorkspaces.cs"),
                    "DockerStartButton_OnClick",
                    "DockerStopButton_OnClick",
                    "DockerRestartButton_OnClick",
                    "DockerRestartDumbButton_OnClick",
                    "DockerRefreshDetailButton_OnClick")),
        (
            "Linux adapter preserves settings persistence and policy handlers",
            () =>
                RequireAll(
                    Read(
                        "src/GraveOps.Desktop.Linux/MainWindow.SharedUnifiedOperationsWorkspaces.cs"),
                    "SaveInterfaceSettingsButton_OnClick",
                    "SaveOperatorSettingsButton_OnClick",
                    "ResetOperatorSettingsButton_OnClick",
                    "StorageCapacityPolicyButton_OnClick",
                    "SignalQualityPolicyButton_OnClick",
                    "VerifiedRemediationPolicyButton_OnClick",
                    "UiPerformancePolicyButton_OnClick")),
        (
            "Linux adapter preserves operator tool handlers",
            () =>
                RequireAll(
                    Read(
                        "src/GraveOps.Desktop.Linux/MainWindow.SharedUnifiedOperationsWorkspaces.cs"),
                    "UnifiedLocalTerminalButton_OnClick",
                    "UnifiedSshTerminalButton_OnClick",
                    "CreateDiagnosticsButton_OnClick",
                    "RunValidationButton_OnClick",
                    "UnifiedFilesRefreshButton_OnClick",
                    "RunOperatorScriptButton_OnClick",
                    "RefreshUpdateInventoryButton_OnClick")),
        (
            "Windows adapter projects Docker inventory without mutations",
            () =>
                RequireAll(
                    Read(
                        "src/GraveOps.Desktop.Windows/MainWindow.SharedUnifiedOperationsWorkspaces.cs"),
                    "snapshot.Containers",
                    "CanStart:",
                    "false",
                    "CanStop:",
                    "CanRestart:",
                    "inventory only")),
        (
            "Windows adapter exposes explicit unsupported capability states",
            () =>
                RequireAll(
                    Read(
                        "src/GraveOps.Desktop.Windows/MainWindow.SharedUnifiedOperationsWorkspaces.cs"),
                    "No Windows backup provider",
                    "Windows operator-default persistence is unavailable",
                    "Redacted diagnostics export is not implemented for Windows",
                    "No Windows operator-tools capability is advertised")),
        (
            "Windows operations navigation owns dedicated pages",
            () =>
                RequireAll(
                    Read(
                        "src/GraveOps.Desktop.Windows/MainWindow.axaml.cs") +
                    Read(
                        "src/GraveOps.Desktop.Windows/MainWindow.axaml"),
                    "[\"BackupsNav\"] = new(\"BackupsPage\"",
                    "[\"SettingsNav\"] = new(\"SettingsPage\"",
                    "[\"ToolsNav\"] = new(\"ToolsPage\"",
                    "x:Name=\"BackupsPage\"",
                    "x:Name=\"SettingsPage\"",
                    "x:Name=\"ToolsPage\"")),
        (
            "operations routes no longer collide on ParityPage",
            () =>
                ForbidAll(
                    NavigationBlock(
                        Read(
                            "src/GraveOps.Desktop.Windows/MainWindow.axaml.cs")),
                    "[\"BackupsNav\"] = new(\"ParityPage\"",
                    "[\"SettingsNav\"] = new(\"ParityPage\"",
                    "[\"ToolsNav\"] = new(\"ParityPage\"")),
        (
            "Backups navigation opens its capability-state page",
            () =>
                RequireAll(
                    Read(
                        "src/GraveOps.Desktop.Windows/WindowsTargetSession.cs") +
                    Read(
                        "tests/GraveOps.Desktop.Windows.TargetTests/Program.cs"),
                    "\"BackupsNav\" =>",
                    "backups capability-state workspace reachable")),
        (
            "both hosts initialize shared operations workspaces",
            () =>
                RequireAll(
                    Read(
                        "src/GraveOps.Desktop.Linux/MainWindow.axaml.cs") +
                    Read(
                        "src/GraveOps.Desktop.Windows/MainWindow.axaml.cs"),
                    "InitializeSharedUnifiedOperationsWorkspaces();")),
        (
            "Windows snapshot refresh updates shared operations",
            () =>
                RequireAll(
                    Read(
                        "src/GraveOps.Desktop.Windows/MainWindow.axaml.cs"),
                    "UpdateSharedUnifiedOperationsWorkspaces(")),
        (
            "legacy operations pages remain adapter surfaces",
            () =>
                RequireAll(
                    Read(
                        "src/GraveOps.Desktop.Linux/MainWindow.SharedUnifiedOperationsWorkspaces.cs") +
                    Read(
                        "src/GraveOps.Desktop.Windows/MainWindow.SharedUnifiedOperationsWorkspaces.cs"),
                    "ReplaceOperationsWorkspacePage",
                    "child.IsVisible ="))
    };

var failures =
    new List<string>();

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
        failures.Add(
            $"{test.Name}: {exception.Message}");

        Console.WriteLine(
            $"FAIL: {test.Name}");
    }
}

Console.WriteLine();
Console.WriteLine(
    $"{tests.Length - failures.Count}/{tests.Length} tests passed.");

if (failures.Count > 0)
{
    Console.WriteLine();

    foreach (var failure in failures)
        Console.WriteLine(failure);

    Environment.ExitCode =
        1;
}

return;

string Read(
    string relativePath) =>
    File.ReadAllText(
        Path.Combine(
            root,
            relativePath.Replace(
                '/',
                Path.DirectorySeparatorChar)));

string ReadDirectory(
    string relativePath) =>
    string.Join(
        Environment.NewLine,
        Directory
            .EnumerateFiles(
                Path.Combine(
                    root,
                    relativePath.Replace(
                        '/',
                        Path.DirectorySeparatorChar)),
                "*.cs",
                SearchOption.AllDirectories)
            .Select(File.ReadAllText));

string NavigationBlock(
    string source)
{
    var start =
        source.IndexOf(
            "private static readonly IReadOnlyDictionary<string, NavigationTarget> Navigation",
            StringComparison.Ordinal);

    var end =
        source.IndexOf(
            "private readonly List<ActivityRow>",
            StringComparison.Ordinal);

    if (start < 0 ||
        end <= start)
    {
        throw new InvalidOperationException(
            "Windows navigation block was not found.");
    }

    return source[start..end];
}

void RequireAll(
    string text,
    params string[] markers)
{
    foreach (var marker in markers)
    {
        if (!text.Contains(
                marker,
                StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"Missing marker: {marker}");
        }
    }
}

void ForbidAll(
    string text,
    params string[] markers)
{
    foreach (var marker in markers)
    {
        if (text.Contains(
                marker,
                StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"Forbidden marker: {marker}");
        }
    }
}

string FindRepositoryRoot()
{
    var directory =
        new DirectoryInfo(
            AppContext.BaseDirectory);

    while (directory is not null)
    {
        if (Directory.Exists(
                Path.Combine(
                    directory.FullName,
                    "src")) &&
            Directory.Exists(
                Path.Combine(
                    directory.FullName,
                    "tests")))
        {
            return directory.FullName;
        }

        directory =
            directory.Parent;
    }

    throw new DirectoryNotFoundException(
        "Repository root was not found.");
}
