using System.Diagnostics;
using System.IO.Compression;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using GraveOps.Core.Hosts;

namespace GraveOps.Desktop.Linux;

public sealed record LinuxOperatorSettings(
    bool StartInSafeMode,
    bool ShowInformationalLogs,
    bool ShowInformationalContainers,
    bool OpenOverviewAfterStartup,
    int BackgroundRefreshSeconds = 60,
    bool DesktopNotifications = true)
{
    public static LinuxOperatorSettings Default =>
        new(true, false, false, false, 60, true);
}

public sealed class LinuxOperatorSettingsStore
{
    private readonly JsonSerializerOptions _json = new()
    {
        WriteIndented = true
    };

    public LinuxOperatorSettingsStore()
    {
        var home = Environment.GetFolderPath(
            Environment.SpecialFolder.UserProfile);

        var configRoot =
            Environment.GetEnvironmentVariable("XDG_CONFIG_HOME");
        if (string.IsNullOrWhiteSpace(configRoot))
            configRoot = Path.Combine(home, ".config");

        var dataRoot =
            Environment.GetEnvironmentVariable("XDG_DATA_HOME");
        if (string.IsNullOrWhiteSpace(dataRoot))
            dataRoot = Path.Combine(home, ".local", "share");

        ConfigDirectory = Path.Combine(configRoot, "GraveOps");
        DataDirectory = Path.Combine(dataRoot, "GraveOps");
        DiagnosticsDirectory = Path.Combine(
            home,
            "Downloads",
            "GraveOps-Diagnostics");

        SettingsPath = Path.Combine(
            ConfigDirectory,
            "operator-settings.json");
        PolicyPath = Path.Combine(
            ConfigDirectory,
            "finding-policies.json");
        HistoryPath = Path.Combine(
            DataDirectory,
            "fleet-history.json");
        InventoryCachePath = Path.Combine(
            DataDirectory,
            "application-inventory-cache.json");
    }

    public string ConfigDirectory { get; }
    public string DataDirectory { get; }
    public string DiagnosticsDirectory { get; }
    public string SettingsPath { get; }
    public string PolicyPath { get; }
    public string HistoryPath { get; }
    public string InventoryCachePath { get; }

    public LinuxOperatorSettings Load()
    {
        try
        {
            if (!File.Exists(SettingsPath))
                return LinuxOperatorSettings.Default;

            return JsonSerializer.Deserialize<LinuxOperatorSettings>(
                       File.ReadAllText(SettingsPath),
                       _json) ??
                   LinuxOperatorSettings.Default;
        }
        catch
        {
            return LinuxOperatorSettings.Default;
        }
    }

    public void Save(LinuxOperatorSettings settings)
    {
        Directory.CreateDirectory(ConfigDirectory);

        var temporary = SettingsPath + ".tmp";
        File.WriteAllText(
            temporary,
            JsonSerializer.Serialize(settings, _json));
        File.Move(
            temporary,
            SettingsPath,
            overwrite: true);
    }

    public LinuxOperatorSettings Reset()
    {
        var settings = LinuxOperatorSettings.Default;
        Save(settings);
        return settings;
    }

    public string ValidatePersistentFiles()
    {
        return string.Join(
            Environment.NewLine,
            new[]
            {
                $"Config directory: {DescribeDirectory(ConfigDirectory)}",
                $"Data directory: {DescribeDirectory(DataDirectory)}",
                $"Settings file: {DescribeJson(SettingsPath)}",
                $"Finding policy: {DescribeJson(PolicyPath)}",
                $"History file: {DescribeJson(HistoryPath)}",
                $"Fleet inventory cache: {DescribeJson(InventoryCachePath)}",
                $"Diagnostics directory: {DescribeDirectory(DiagnosticsDirectory)}"
            });
    }

    private static string DescribeDirectory(string path) =>
        Directory.Exists(path)
            ? $"OK · {path}"
            : $"NOT CREATED · {path}";

    private static string DescribeJson(string path)
    {
        if (!File.Exists(path))
            return $"NOT PRESENT · {path}";

        try
        {
            using var document =
                JsonDocument.Parse(File.ReadAllText(path));
            return $"VALID JSON · {path}";
        }
        catch (Exception exception)
        {
            return $"INVALID JSON · {path} · {exception.Message}";
        }
    }
}

public sealed record LinuxVersionSnapshot(
    string RepositoryPath,
    string Branch,
    string Commit,
    string Subject,
    string Worktree,
    string OriginComparison,
    string DotnetVersion);

public static class LinuxOperatorTools
{
    public static string FindRepositoryRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);

        while (current is not null)
        {
            if (Directory.Exists(
                    Path.Combine(current.FullName, ".git")))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        var fallback = Path.Combine(
            Environment.GetFolderPath(
                Environment.SpecialFolder.UserProfile),
            "GraveOps-Linux");

        return Directory.Exists(Path.Combine(fallback, ".git"))
            ? fallback
            : AppContext.BaseDirectory;
    }

    public static async Task<LinuxVersionSnapshot> CaptureVersionAsync(
        string repositoryPath)
    {
        var branch = await RunAsync(
            "git",
            new[] { "-C", repositoryPath, "branch", "--show-current" });
        var commit = await RunAsync(
            "git",
            new[] { "-C", repositoryPath, "rev-parse", "--short=12", "HEAD" });
        var subject = await RunAsync(
            "git",
            new[] { "-C", repositoryPath, "log", "-1", "--format=%s" });
        var status = await RunAsync(
            "git",
            new[] { "-C", repositoryPath, "status", "--porcelain=v1", "-uall" });
        var comparison = await RunAsync(
            "git",
            new[]
            {
                "-C",
                repositoryPath,
                "rev-list",
                "--left-right",
                "--count",
                "HEAD...origin/linux-client"
            });
        var dotnet = await RunAsync(
            "dotnet",
            new[] { "--version" });

        return new LinuxVersionSnapshot(
            repositoryPath,
            Fallback(branch, "unknown"),
            Fallback(commit, "unknown"),
            Fallback(subject, "No commit subject"),
            string.IsNullOrWhiteSpace(status)
                ? "Clean"
                : "Modified",
            DescribeComparison(comparison),
            Fallback(dotnet, "unknown"));
    }

    public static async Task<string> ValidateAsync(
        string repositoryPath,
        LinuxOperatorSettingsStore store)
    {
        var version = await CaptureVersionAsync(repositoryPath);

        return string.Join(
            Environment.NewLine,
            new[]
            {
                "GraveOps Linux operator validation",
                $"Generated: {DateTimeOffset.Now:g}",
                string.Empty,
                store.ValidatePersistentFiles(),
                string.Empty,
                $"Repository: {(Directory.Exists(Path.Combine(repositoryPath, ".git")) ? "OK" : "NOT FOUND")} · {repositoryPath}",
                $"xdg-open: {DescribeCommand("xdg-open")}",
                $"git: {DescribeCommand("git")}",
                $"dotnet: {DescribeCommand("dotnet")}",
                $"terminal launcher: {DescribeTerminal()}",
                string.Empty,
                $"Branch: {version.Branch}",
                $"Commit: {version.Commit} · {version.Subject}",
                $"Worktree: {version.Worktree}",
                $"Origin comparison: {version.OriginComparison}",
                $".NET SDK: {version.DotnetVersion}"
            });
    }

    public static bool OpenPath(
        string path,
        out string error)
    {
        error = string.Empty;

        try
        {
            if (!Directory.Exists(path) &&
                !File.Exists(path))
            {
                error = $"Path does not exist: {path}";
                return false;
            }

            using var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = "xdg-open",
                    UseShellExecute = false,
                    CreateNoWindow = true
                }
            };
            process.StartInfo.ArgumentList.Add(path);
            process.Start();
            return true;
        }
        catch (Exception exception)
        {
            error = exception.Message;
            return false;
        }
    }

    public static bool OpenTerminal(
        string workingDirectory,
        out string error)
    {
        error = string.Empty;

        if (!Directory.Exists(workingDirectory))
        {
            error =
                $"Working directory does not exist: {workingDirectory}";
            return false;
        }

        string[] candidates =
        {
            "x-terminal-emulator",
            "gnome-terminal",
            "konsole",
            "xfce4-terminal",
            "mate-terminal",
            "kgx"
        };

        foreach (var candidate in candidates)
        {
            try
            {
                var process = new Process
                {
                    StartInfo = new ProcessStartInfo
                    {
                        FileName = candidate,
                        WorkingDirectory = workingDirectory,
                        UseShellExecute = false,
                        CreateNoWindow = false
                    }
                };
                process.Start();
                return true;
            }
            catch
            {
                // Try the next terminal launcher.
            }
        }

        error = "No supported terminal launcher was available.";
        return false;
    }

    public static async Task<string> CreateDiagnosticsAsync(
        string repositoryPath,
        LinuxOperatorSettingsStore store,
        HostSnapshot snapshot,
        OpsAnalysis analysis,
        IReadOnlyList<OpsLifecycleStage> lifecycle,
        IReadOnlyList<OpsIntegration> integrations,
        IReadOnlyList<OpsLogGroup> logs,
        OpsBackupSnapshot backup,
        OpsPolicyEvaluation policyEvaluation,
        LinuxOperatorSettings settings)
    {
        Directory.CreateDirectory(store.DiagnosticsDirectory);

        var archivePath = Path.Combine(
            store.DiagnosticsDirectory,
            $"GraveOps-Diagnostics-{DateTime.Now:yyyyMMdd-HHmmss}.zip");

        var version = await CaptureVersionAsync(repositoryPath);

        using var archive = ZipFile.Open(
            archivePath,
            ZipArchiveMode.Create);

        var readme = string.Join(
            Environment.NewLine,
            new[]
            {
                "GraveOps Linux diagnostics bundle",
                string.Empty,
                "Generated locally for troubleshooting.",
                "Excluded: configuration contents, API keys, passwords,",
                "environment variables, browser data and media files.",
                string.Empty,
                "User-home paths, IP addresses and common secret patterns",
                "are redacted before diagnostic text is written."
            });

        await WriteEntryAsync(archive, "README.txt", readme);

        var environment = new
        {
            snapshot.CapturedAt,
            snapshot.Hostname,
            snapshot.OperatingSystem,
            snapshot.SystemState,
            snapshot.Kernel,
            snapshot.Uptime,
            snapshot.CpuModel,
            snapshot.LoadAverage,
            snapshot.MemorySummary,
            snapshot.DockerState,
            IpAddresses = "<redacted>",
            Storage = LinuxOpsAnalyzer.OperationalStorage(snapshot),
            Services = LinuxOpsAnalyzer.UniqueServices(snapshot),
            snapshot.Containers,
            snapshot.FailedUnits,
            snapshot.Warnings
        };

        await WriteJsonEntryAsync(
            archive,
            "environment.json",
            environment);
        await WriteJsonEntryAsync(
            archive,
            "analysis.json",
            analysis);
        await WriteJsonEntryAsync(
            archive,
            "lifecycle.json",
            lifecycle);
        await WriteJsonEntryAsync(
            archive,
            "integrations.json",
            integrations);
        await WriteJsonEntryAsync(
            archive,
            "journal-groups.json",
            logs);
        await WriteJsonEntryAsync(
            archive,
            "backup-readiness.json",
            backup);
        await WriteJsonEntryAsync(
            archive,
            "operator-policy-summary.json",
            new
            {
                ActiveCount = policyEvaluation.Active.Count,
                MutedCount = policyEvaluation.Muted.Count,
                policyEvaluation.Active,
                policyEvaluation.Muted
            });
        await WriteJsonEntryAsync(
            archive,
            "operator-settings.json",
            settings);
        await WriteJsonEntryAsync(
            archive,
            "version.json",
            version);

        return archivePath;
    }

    private static async Task WriteJsonEntryAsync(
        ZipArchive archive,
        string name,
        object value)
    {
        var text = JsonSerializer.Serialize(
            value,
            new JsonSerializerOptions
            {
                WriteIndented = true
            });

        await WriteEntryAsync(
            archive,
            name,
            Redact(text));
    }

    private static async Task WriteEntryAsync(
        ZipArchive archive,
        string name,
        string text)
    {
        var entry = archive.CreateEntry(
            name,
            CompressionLevel.Optimal);

        await using var stream = entry.Open();
        await using var writer = new StreamWriter(
            stream,
            new UTF8Encoding(false));

        await writer.WriteAsync(Redact(text));
    }

    private static string Redact(string text)
    {
        var home = Environment.GetFolderPath(
            Environment.SpecialFolder.UserProfile);

        var redacted = string.IsNullOrWhiteSpace(home)
            ? text
            : text.Replace(home, "~", StringComparison.Ordinal);

        redacted = Regex.Replace(
            redacted,
            @"\b(?:\d{1,3}\.){3}\d{1,3}\b",
            "<ip>",
            RegexOptions.CultureInvariant);

        redacted = Regex.Replace(
            redacted,
            @"(?i)(api[_-]?key|token|password|secret|authorization)(\s*[:=]\s*)(""[^""]*""|[^\s,;}]+)",
            "$1$2<redacted>",
            RegexOptions.CultureInvariant);

        redacted = Regex.Replace(
            redacted,
            @"(?i)bearer\s+[A-Za-z0-9._~+/=-]+",
            "Bearer <redacted>",
            RegexOptions.CultureInvariant);

        return redacted;
    }

    private static async Task<string> RunAsync(
        string executable,
        IReadOnlyList<string> arguments)
    {
        try
        {
            using var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = executable,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                }
            };

            foreach (var argument in arguments)
                process.StartInfo.ArgumentList.Add(argument);

            process.Start();
            var stdout = process.StandardOutput.ReadToEndAsync();
            await process.WaitForExitAsync();
            return (await stdout).Trim();
        }
        catch
        {
            return string.Empty;
        }
    }

    private static string DescribeComparison(string value)
    {
        var parts = value.Split(
            (char[]?)null,
            StringSplitOptions.RemoveEmptyEntries);

        if (parts.Length != 2 ||
            !int.TryParse(parts[0], out var ahead) ||
            !int.TryParse(parts[1], out var behind))
        {
            return "Unavailable";
        }

        return
            $"{ahead} ahead · {behind} behind local origin/linux-client";
    }

    private static string DescribeCommand(string command)
    {
        string[] roots =
        {
            "/usr/bin",
            "/usr/local/bin",
            "/bin"
        };

        return roots.Any(root =>
            File.Exists(Path.Combine(root, command)))
            ? "available"
            : "not found";
    }

    private static string DescribeTerminal()
    {
        string[] commands =
        {
            "x-terminal-emulator",
            "gnome-terminal",
            "konsole",
            "xfce4-terminal",
            "mate-terminal",
            "kgx"
        };

        return commands.Any(command =>
            new[]
            {
                "/usr/bin",
                "/usr/local/bin",
                "/bin"
            }.Any(root =>
                File.Exists(Path.Combine(root, command))))
            ? "available"
            : "not found";
    }

    private static string Fallback(
        string value,
        string fallback) =>
        string.IsNullOrWhiteSpace(value)
            ? fallback
            : value;
}
