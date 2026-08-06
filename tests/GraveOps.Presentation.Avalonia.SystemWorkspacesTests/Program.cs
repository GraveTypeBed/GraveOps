using System.Text;

var root =
    FindRepositoryRoot();

var tests =
    new (string Name, Action Run)[]
    {
        (
            "shared contracts own all three system surfaces",
            () =>
                RequireAll(
                    Read(
                        "src/GraveOps.Presentation.Avalonia/SystemWorkspaces/UnifiedSystemModels.cs"),
                    "UnifiedServiceRow",
                    "UnifiedStorageRow",
                    "UnifiedLogRow",
                    "UnifiedServicesState",
                    "UnifiedStorageState",
                    "UnifiedLogsState")),
        (
            "shared presentation owns Linux page language",
            () =>
                RequireAll(
                    Read(
                        "src/GraveOps.Presentation.Avalonia/SystemWorkspaces/UnifiedSystemWorkspaceViews.cs"),
                    "Operations & troubleshooting",
                    "Storage & capacity",
                    "Log Center",
                    "Action library",
                    "Dependency map",
                    "Selected event detail")),
        (
            "shared source remains platform neutral",
            () =>
                ForbidAll(
                    Read(
                        "src/GraveOps.Presentation.Avalonia/SystemWorkspaces/UnifiedSystemWorkspaceViews.cs") +
                    Read(
                        "src/GraveOps.Presentation.Avalonia/SystemWorkspaces/UnifiedSystemModels.cs"),
                    "GraveOps.Desktop.Windows",
                    "GraveOps.Desktop.Linux",
                    "GraveOps.Platform.Windows",
                    "GraveOps.Platform.Linux",
                    "System.Windows")),
        (
            "Linux adapter preserves guarded actions",
            () =>
                RequireAll(
                    Read(
                        "src/GraveOps.Desktop.Linux/MainWindow.SharedUnifiedSystemWorkspaces.cs"),
                    "RunServiceActionAsync",
                    "StorageCapacityPolicyButton_OnClick",
                    "StorageThresholdButton_OnClick",
                    "RestoreStorageThresholdButton_OnClick",
                    "SafeModeCheckBox_OnClick")),
        (
            "Linux adapter projects reliable logs",
            () =>
                RequireAll(
                    Read(
                        "src/GraveOps.Desktop.Linux/MainWindow.SharedUnifiedSystemWorkspaces.cs"),
                    "_reliableLogRows",
                    "FormatLog(",
                    "UnifiedLogSeverity")),
        (
            "Windows adapter is read-only for service and storage mutations",
            () =>
                RequireAll(
                    Read(
                        "src/GraveOps.Desktop.Windows/MainWindow.SharedUnifiedSystemWorkspaces.cs"),
                    "CanStart:",
                    "false",
                    "service mutations are not exposed",
                    "policy editing is not available")),
        (
            "Windows adapter projects event and provider evidence",
            () =>
                RequireAll(
                    Read(
                        "src/GraveOps.Desktop.Windows/MainWindow.SharedUnifiedSystemWorkspaces.cs"),
                    "snapshot.RecentLogs",
                    "snapshot.Warnings",
                    "Windows Event Log",
                    "Provider")),
        (
            "Windows Logs navigation owns dedicated page",
            () =>
                RequireAll(
                    Read(
                        "src/GraveOps.Desktop.Windows/MainWindow.axaml.cs") +
                    Read(
                        "src/GraveOps.Desktop.Windows/MainWindow.axaml"),
                    "[\"LogsNav\"] = new(\"LogsPage\"",
                    "x:Name=\"LogsPage\"")),
        (
            "both hosts initialize shared system workspaces",
            () =>
                RequireAll(
                    Read(
                        "src/GraveOps.Desktop.Linux/MainWindow.axaml.cs") +
                    Read(
                        "src/GraveOps.Desktop.Windows/MainWindow.axaml.cs"),
                    "InitializeSharedUnifiedSystemWorkspaces();")),
        (
            "all three refresh projections are wired",
            () =>
                RequireAll(
                    Read(
                        "src/GraveOps.Desktop.Linux/MainWindow.axaml.cs") +
                    Read(
                        "src/GraveOps.Desktop.Linux/MainWindow.HistoryLogs.cs") +
                    Read(
                        "src/GraveOps.Desktop.Windows/MainWindow.axaml.cs"),
                    "UpdateSharedUnifiedServices();",
                    "UpdateSharedUnifiedStorage();",
                    "UpdateSharedUnifiedLogs();",
                    "UpdateSharedUnifiedSystemWorkspaces("))
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