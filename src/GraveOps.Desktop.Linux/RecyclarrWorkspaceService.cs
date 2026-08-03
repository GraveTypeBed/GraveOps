using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace GraveOps.Desktop.Linux;

public sealed record RecyclarrTargetRow(
    string Service,
    string Instance,
    string ConfigFile,
    string Endpoint);

public sealed record RecyclarrConfigFileRow(
    string File,
    string RelativePath,
    string Size,
    string Modified,
    string Targets);

public sealed record RecyclarrWorkspaceSnapshot(
    DateTimeOffset CapturedAt,
    string RuntimeState,
    string Version,
    string ContainerName,
    string Image,
    string ComposeProject,
    string ComposeService,
    string ConfigContainerPath,
    string ConfigHostPath,
    string Schedule,
    string LastRunSummary,
    string Evidence,
    bool IsRunning,
    bool ConfigReadable,
    IReadOnlyList<RecyclarrConfigFileRow> ConfigFiles,
    IReadOnlyList<RecyclarrTargetRow> Targets)
{
    public static RecyclarrWorkspaceSnapshot NotDetected(
        string evidence) =>
        new(
            DateTimeOffset.Now,
            "NOT DETECTED",
            "--",
            "--",
            "--",
            "--",
            "--",
            "/config",
            "--",
            "--",
            "No Recyclarr run evidence available.",
            evidence,
            false,
            false,
            Array.Empty<RecyclarrConfigFileRow>(),
            Array.Empty<RecyclarrTargetRow>());
}

public sealed record RecyclarrCommandResult(
    bool Success,
    int ExitCode,
    string Output,
    string Summary);

public sealed class RecyclarrWorkspaceService
{
    private static readonly Regex AnsiRegex =
        new("\\x1B(?:[@-Z\\-_]|\\[[0-?]*[ -/]*[@-~])", RegexOptions.Compiled);

    private static readonly Regex SecretRegex =
        new(
            "(?i)(api[_-]?key|token|password|passphrase|secret)(\\s*[:=]\\s*)([^\\s,;]+)",
            RegexOptions.Compiled);

    private static readonly Regex ServiceRegex =
        new(
            "^(sonarr|radarr):\\s*(?:#.*)?$",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex InstanceRegex =
        new(
            "^ {2}([^\\s:#][^:]*):\\s*(?:#.*)?$",
            RegexOptions.Compiled);

    private static readonly Regex BaseUrlRegex =
        new(
            "^ {4}base_url:\\s*(.+?)\\s*(?:#.*)?$",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public async Task<RecyclarrWorkspaceSnapshot> CaptureAsync(
        CancellationToken cancellationToken = default)
    {
        var inventory = await RunDockerAsync(
            new[]
            {
                "ps",
                "-a",
                "--format",
                "{{.Names}}\\t{{.Image}}\\t{{.Status}}"
            },
            TimeSpan.FromSeconds(15),
            cancellationToken);

        if (!inventory.Success)
        {
            return RecyclarrWorkspaceSnapshot.NotDetected(
                $"Docker inventory unavailable: {inventory.CombinedOutput}");
        }

        var containers = inventory.StandardOutput
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(ParseContainerLine)
            .Where(item => item is not null)
            .Select(item => item!)
            .Where(item =>
                item.Name.Contains("recyclarr", StringComparison.OrdinalIgnoreCase) ||
                item.Image.Contains("recyclarr", StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(item =>
                item.Name.Equals("recyclarr", StringComparison.OrdinalIgnoreCase))
            .ThenBy(item => item.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (containers.Length == 0)
        {
            return RecyclarrWorkspaceSnapshot.NotDetected(
                "No Docker container with a Recyclarr name or image was detected.");
        }

        var selected = containers[0];
        var inspect = await RunDockerAsync(
            new[] { "inspect", selected.Name },
            TimeSpan.FromSeconds(15),
            cancellationToken);

        if (!inspect.Success)
        {
            return RecyclarrWorkspaceSnapshot.NotDetected(
                $"Detected {selected.Name}, but Docker inspect failed: {inspect.CombinedOutput}");
        }

        using var document = JsonDocument.Parse(inspect.StandardOutput);
        var root = document.RootElement[0];
        var config = root.GetProperty("Config");
        var state = root.GetProperty("State");

        var environment = ReadEnvironment(config);
        var configContainerPath = environment.TryGetValue(
            "RECYCLARR_CONFIG_DIR",
            out var configuredPath)
                ? configuredPath
                : "/config";
        var schedule = environment.TryGetValue(
            "CRON_SCHEDULE",
            out var configuredSchedule)
                ? configuredSchedule
                : "@daily";

        var running =
            state.TryGetProperty("Running", out var runningElement) &&
            runningElement.ValueKind == JsonValueKind.True;
        var runtimeState =
            state.TryGetProperty("Status", out var statusElement)
                ? statusElement.GetString() ?? selected.Status
                : selected.Status;

        var image =
            config.TryGetProperty("Image", out var imageElement)
                ? imageElement.GetString() ?? selected.Image
                : selected.Image;

        var labels = ReadLabels(config);
        var composeProject = labels.TryGetValue(
            "com.docker.compose.project",
            out var project)
                ? project
                : "--";
        var composeService = labels.TryGetValue(
            "com.docker.compose.service",
            out var service)
                ? service
                : "--";

        var configHostPath = ResolveConfigHostPath(
            root,
            configContainerPath);

        var evidence = new List<string>();
        var configRows = new List<RecyclarrConfigFileRow>();
        var targetRows = new List<RecyclarrTargetRow>();
        var configReadable = false;

        if (!string.IsNullOrWhiteSpace(configHostPath) &&
            Directory.Exists(configHostPath))
        {
            ReadConfigInventory(
                configHostPath,
                configRows,
                targetRows,
                evidence);
            configReadable = true;
        }
        else
        {
            evidence.Add(
                "The container config mount is not directly readable from the current user context.");
        }

        string version = ImageVersionFallback(image);
        string localConfigEvidence = string.Empty;

        if (running)
        {
            var versionResult = await RunDockerAsync(
                new[] { "exec", selected.Name, "recyclarr", "--version" },
                TimeSpan.FromSeconds(20),
                cancellationToken);

            if (versionResult.Success)
            {
                version = FirstMeaningfulLine(versionResult.StandardOutput) ?? version;
            }
            else
            {
                evidence.Add("The Recyclarr version command did not complete successfully.");
            }

            var configResult = await RunDockerAsync(
                new[]
                {
                    "exec",
                    selected.Name,
                    "recyclarr",
                    "config",
                    "list",
                    "local",
                    "--log",
                    "info"
                },
                TimeSpan.FromSeconds(30),
                cancellationToken);

            localConfigEvidence = CleanOutput(configResult.CombinedOutput);
            if (!configResult.Success)
            {
                evidence.Add("Recyclarr could not enumerate its local configuration files.");
            }
        }

        var logs = await RunDockerAsync(
            new[] { "logs", "--tail", "120", selected.Name },
            TimeSpan.FromSeconds(20),
            cancellationToken);

        var lastRun = ExtractLastRunSummary(logs.CombinedOutput);

        if (configRows.Count == 0 && !string.IsNullOrWhiteSpace(localConfigEvidence))
        {
            evidence.Add(
                "Recyclarr reported local configuration evidence, but the host mount could not be parsed directly.");
        }

        if (evidence.Count == 0)
        {
            evidence.Add(
                $"Container {selected.Name} and its configuration ownership were captured successfully.");
        }

        return new RecyclarrWorkspaceSnapshot(
            DateTimeOffset.Now,
            running
                ? "RUNNING"
                : runtimeState.ToUpperInvariant(),
            version,
            selected.Name,
            image,
            composeProject,
            composeService,
            configContainerPath,
            string.IsNullOrWhiteSpace(configHostPath)
                ? "--"
                : configHostPath,
            schedule,
            lastRun,
            string.Join(" ", evidence),
            running,
            configReadable,
            configRows,
            targetRows);
    }

    public async Task<RecyclarrCommandResult> PreviewAsync(
        string containerName,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(containerName) ||
            containerName == "--")
        {
            return new RecyclarrCommandResult(
                false,
                -1,
                "No Recyclarr container is selected.",
                "Preview could not start.");
        }

        var result = await RunDockerAsync(
            new[]
            {
                "exec",
                containerName,
                "recyclarr",
                "sync",
                "--preview",
                "--log",
                "info"
            },
            TimeSpan.FromMinutes(4),
            cancellationToken);

        var output = CleanOutput(result.CombinedOutput);
        if (string.IsNullOrWhiteSpace(output))
        {
            output = result.Success
                ? "Recyclarr preview completed without console output."
                : "Recyclarr preview failed without console output.";
        }

        return new RecyclarrCommandResult(
            result.Success,
            result.ExitCode,
            output,
            result.Success
                ? "Read-only preview completed. No Sonarr or Radarr settings were changed."
                : $"Preview failed with exit code {result.ExitCode}.");
    }

    private static ContainerCandidate? ParseContainerLine(string line)
    {
        var parts = line.Split('\t');
        if (parts.Length < 2 || string.IsNullOrWhiteSpace(parts[0]))
            return null;

        return new ContainerCandidate(
            parts[0].Trim(),
            parts[1].Trim(),
            parts.Length > 2
                ? parts[2].Trim()
                : "unknown");
    }

    private static Dictionary<string, string> ReadEnvironment(
        JsonElement config)
    {
        var values = new Dictionary<string, string>(
            StringComparer.OrdinalIgnoreCase);

        if (!config.TryGetProperty("Env", out var env) ||
            env.ValueKind != JsonValueKind.Array)
        {
            return values;
        }

        foreach (var item in env.EnumerateArray())
        {
            var value = item.GetString();
            if (string.IsNullOrWhiteSpace(value))
                continue;

            var separator = value.IndexOf('=');
            if (separator <= 0)
                continue;

            values[value[..separator]] = value[(separator + 1)..];
        }

        return values;
    }

    private static Dictionary<string, string> ReadLabels(
        JsonElement config)
    {
        var values = new Dictionary<string, string>(
            StringComparer.OrdinalIgnoreCase);

        if (!config.TryGetProperty("Labels", out var labels) ||
            labels.ValueKind != JsonValueKind.Object)
        {
            return values;
        }

        foreach (var property in labels.EnumerateObject())
        {
            values[property.Name] = property.Value.GetString() ?? string.Empty;
        }

        return values;
    }

    private static string ResolveConfigHostPath(
        JsonElement root,
        string configContainerPath)
    {
        if (!root.TryGetProperty("Mounts", out var mounts) ||
            mounts.ValueKind != JsonValueKind.Array)
        {
            return string.Empty;
        }

        JsonElement? fallback = null;

        foreach (var mount in mounts.EnumerateArray())
        {
            if (!mount.TryGetProperty("Destination", out var destinationElement))
                continue;

            var destination = destinationElement.GetString() ?? string.Empty;
            if (destination.Equals(
                    configContainerPath,
                    StringComparison.Ordinal))
            {
                return mount.TryGetProperty("Source", out var sourceElement)
                    ? sourceElement.GetString() ?? string.Empty
                    : string.Empty;
            }

            if (destination.Equals("/config", StringComparison.Ordinal))
                fallback = mount;
        }

        if (fallback is { } selected &&
            selected.TryGetProperty("Source", out var fallbackSource))
        {
            return fallbackSource.GetString() ?? string.Empty;
        }

        return string.Empty;
    }

    private static void ReadConfigInventory(
        string configHostPath,
        ICollection<RecyclarrConfigFileRow> configRows,
        ICollection<RecyclarrTargetRow> targetRows,
        ICollection<string> evidence)
    {
        try
        {
            var candidates = Directory
                .EnumerateFiles(
                    configHostPath,
                    "*",
                    SearchOption.AllDirectories)
                .Where(path =>
                {
                    var extension = Path.GetExtension(path);
                    return extension.Equals(".yml", StringComparison.OrdinalIgnoreCase) ||
                           extension.Equals(".yaml", StringComparison.OrdinalIgnoreCase);
                })
                .Select(path => new
                {
                    Path = path,
                    Relative = Path.GetRelativePath(configHostPath, path)
                        .Replace(Path.DirectorySeparatorChar, '/')
                })
                .Where(item =>
                    item.Relative.Equals("recyclarr.yml", StringComparison.OrdinalIgnoreCase) ||
                    item.Relative.StartsWith("configs/", StringComparison.OrdinalIgnoreCase))
                .OrderBy(item => item.Relative, StringComparer.OrdinalIgnoreCase)
                .Take(128)
                .ToArray();

            foreach (var candidate in candidates)
            {
                var fileTargets = ParseTargets(
                    candidate.Path,
                    candidate.Relative);
                foreach (var target in fileTargets)
                    targetRows.Add(target);

                var info = new FileInfo(candidate.Path);
                configRows.Add(
                    new RecyclarrConfigFileRow(
                        info.Name,
                        candidate.Relative,
                        FormatBytes(info.Length),
                        info.LastWriteTime.ToString(
                            "g",
                            CultureInfo.CurrentCulture),
                        fileTargets.Count == 0
                            ? "No Sonarr/Radarr targets"
                            : string.Join(
                                ", ",
                                fileTargets.Select(item =>
                                    $"{item.Service}:{item.Instance}"))));
            }

            if (candidates.Length == 0)
            {
                evidence.Add(
                    "No recyclarr.yml file or YAML files under configs/ were found in the config mount.");
            }
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException)
        {
            evidence.Add(
                $"The Recyclarr config mount could not be enumerated: {exception.Message}");
        }
    }

    private static IReadOnlyList<RecyclarrTargetRow> ParseTargets(
        string path,
        string relativePath)
    {
        var rows = new List<RecyclarrTargetRow>();
        string? service = null;
        string? instance = null;
        string endpoint = "--";

        void Commit()
        {
            if (service is null || instance is null)
                return;

            rows.Add(
                new RecyclarrTargetRow(
                    char.ToUpperInvariant(service[0]) + service[1..],
                    instance,
                    relativePath,
                    SanitizeEndpoint(endpoint)));
        }

        foreach (var rawLine in File.ReadLines(path))
        {
            if (string.IsNullOrWhiteSpace(rawLine) ||
                rawLine.TrimStart().StartsWith('#'))
            {
                continue;
            }

            var serviceMatch = ServiceRegex.Match(rawLine);
            if (serviceMatch.Success)
            {
                Commit();
                service = serviceMatch.Groups[1].Value.ToLowerInvariant();
                instance = null;
                endpoint = "--";
                continue;
            }

            if (service is null)
                continue;

            if (rawLine.Length > 0 && !char.IsWhiteSpace(rawLine[0]))
            {
                Commit();
                service = null;
                instance = null;
                endpoint = "--";
                continue;
            }

            var instanceMatch = InstanceRegex.Match(rawLine);
            if (instanceMatch.Success)
            {
                Commit();
                instance = TrimYamlScalar(instanceMatch.Groups[1].Value);
                endpoint = "--";
                continue;
            }

            if (instance is null)
                continue;

            var urlMatch = BaseUrlRegex.Match(rawLine);
            if (urlMatch.Success)
            {
                endpoint = TrimYamlScalar(urlMatch.Groups[1].Value);
            }
        }

        Commit();
        return rows;
    }

    private static string TrimYamlScalar(string value)
    {
        var trimmed = value.Trim();
        if (trimmed.Length >= 2 &&
            ((trimmed[0] == '\'' && trimmed[^1] == '\'') ||
             (trimmed[0] == '"' && trimmed[^1] == '"')))
        {
            return trimmed[1..^1];
        }

        return trimmed;
    }

    private static string SanitizeEndpoint(string endpoint)
    {
        var trimmed = TrimYamlScalar(endpoint);

        if (string.IsNullOrWhiteSpace(trimmed) ||
            trimmed == "--")
        {
            return "--";
        }

        if (trimmed.StartsWith(
                "!secret",
                StringComparison.OrdinalIgnoreCase))
        {
            return "Configured through secret";
        }

        if (trimmed.StartsWith(
                "!env_var",
                StringComparison.OrdinalIgnoreCase))
        {
            return "Configured through environment";
        }

        if (trimmed[0] == '!')
            return "Configured through tagged value";

        if (trimmed[0] == '*')
            return "Configured through YAML reference";

        if (!Uri.TryCreate(trimmed, UriKind.Absolute, out var uri))
            return "Configured";

        var builder = new UriBuilder(uri)
        {
            UserName = string.Empty,
            Password = string.Empty,
            Query = string.Empty,
            Fragment = string.Empty
        };

        return builder.Uri.ToString().TrimEnd('/');
    }

    private static string ImageVersionFallback(string image)
    {
        var separator = image.LastIndexOf(':');
        return separator >= 0 && separator < image.Length - 1
            ? image[(separator + 1)..]
            : image;
    }

    private static string ExtractLastRunSummary(string rawOutput)
    {
        var lines = CleanOutput(rawOutput)
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        var selected = lines.LastOrDefault(line =>
            line.Contains("sync", StringComparison.OrdinalIgnoreCase) ||
            line.Contains("completed", StringComparison.OrdinalIgnoreCase) ||
            line.Contains("warning", StringComparison.OrdinalIgnoreCase) ||
            line.Contains("error", StringComparison.OrdinalIgnoreCase));

        return selected ??
               lines.LastOrDefault() ??
               "No recent Recyclarr run evidence was returned by Docker logs.";
    }

    private static string? FirstMeaningfulLine(string output) =>
        CleanOutput(output)
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .FirstOrDefault();

    private static string CleanOutput(string output)
    {
        var cleaned = AnsiRegex.Replace(output ?? string.Empty, string.Empty);
        cleaned = SecretRegex.Replace(cleaned, "$1$2[REDACTED]");
        cleaned = cleaned.Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n')
            .Trim();

        const int maximum = 24000;
        if (cleaned.Length <= maximum)
            return cleaned;

        const int half = maximum / 2;
        return cleaned[..half] +
               "\n\n... output truncated by GraveOps ...\n\n" +
               cleaned[^half..];
    }

    private static string FormatBytes(long bytes)
    {
        string[] units = { "B", "KiB", "MiB", "GiB" };
        double value = bytes;
        var unit = 0;

        while (value >= 1024 && unit < units.Length - 1)
        {
            value /= 1024;
            unit++;
        }

        return $"{value:0.#} {units[unit]}";
    }

    private static async Task<ProcessResult> RunDockerAsync(
        IEnumerable<string> arguments,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = "docker",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            }
        };

        foreach (var argument in arguments)
            process.StartInfo.ArgumentList.Add(argument);

        try
        {
            if (!process.Start())
            {
                return new ProcessResult(
                    false,
                    -1,
                    string.Empty,
                    "Docker did not start.");
            }
        }
        catch (Exception exception)
        {
            return new ProcessResult(
                false,
                -1,
                string.Empty,
                exception.Message);
        }

        var stdoutTask = process.StandardOutput.ReadToEndAsync();
        var stderrTask = process.StandardError.ReadToEndAsync();

        using var timeoutSource =
            CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutSource.CancelAfter(timeout);

        try
        {
            await process.WaitForExitAsync(timeoutSource.Token);
        }
        catch (OperationCanceledException)
        {
            try
            {
                if (!process.HasExited)
                    process.Kill(entireProcessTree: true);
            }
            catch (InvalidOperationException)
            {
            }

            var timedOutOutput = await stdoutTask;
            var timedOutError = await stderrTask;
            return new ProcessResult(
                false,
                -1,
                timedOutOutput,
                string.IsNullOrWhiteSpace(timedOutError)
                    ? $"Docker command timed out after {timeout.TotalSeconds:0} seconds."
                    : timedOutError);
        }

        var standardOutput = await stdoutTask;
        var standardError = await stderrTask;
        return new ProcessResult(
            process.ExitCode == 0,
            process.ExitCode,
            standardOutput,
            standardError);
    }

    private sealed record ContainerCandidate(
        string Name,
        string Image,
        string Status);

    private sealed record ProcessResult(
        bool Success,
        int ExitCode,
        string StandardOutput,
        string StandardError)
    {
        public string CombinedOutput =>
            string.Join(
                "\n",
                new[] { StandardOutput, StandardError }
                    .Where(value => !string.IsNullOrWhiteSpace(value)))
                .Trim();
    }
}
