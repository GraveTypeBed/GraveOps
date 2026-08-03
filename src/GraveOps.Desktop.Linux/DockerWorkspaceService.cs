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
using GraveOps.Core.Hosts;

namespace GraveOps.Desktop.Linux;

public sealed record DockerFleetRow(
    string Id,
    string ShortId,
    string Group,
    string Name,
    string Image,
    string State,
    string StateLabel,
    string Health,
    string HealthLabel,
    string Status,
    string RestartPolicy,
    int RestartCount,
    int ExitCode,
    string StartedAt,
    string FinishedAt,
    string Ports,
    string Cpu,
    string Memory,
    string MemoryPercent,
    string Resources,
    string ComposeProject,
    string ComposeService,
    string ComposeWorkingDirectory,
    bool IsRunning,
    bool HasAttention)
{
    public string RestartSummary =>
        $"{RestartPolicy} · {RestartCount}";
}

public sealed record DockerFleetSnapshot(
    DateTimeOffset CapturedAt,
    bool Available,
    string DaemonVersion,
    string Evidence,
    IReadOnlyList<DockerFleetRow> Containers)
{
    public int Running =>
        Containers.Count(item => item.IsRunning);

    public int Attention =>
        Containers.Count(item => item.HasAttention);

    public int ComposeProjects =>
        Containers
            .Select(item => item.ComposeProject)
            .Where(value =>
                !string.IsNullOrWhiteSpace(value) &&
                value != "--")
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Count();

    public static DockerFleetSnapshot Unavailable(string evidence) =>
        new(
            DateTimeOffset.Now,
            false,
            "--",
            evidence,
            Array.Empty<DockerFleetRow>());
}

public sealed record DockerContainerDetailSnapshot(
    DateTimeOffset CapturedAt,
    DockerFleetRow Container,
    string CreatedAt,
    string Lifecycle,
    string ComposeOwnership,
    string Ports,
    string Networks,
    string Mounts,
    string EnvironmentNames,
    string RecentLogs,
    string Evidence);

public sealed record DockerWorkspaceCommandResult(
    bool Success,
    int ExitCode,
    string Output,
    string Summary);

public sealed class DockerWorkspaceService
{
    private const int LogTailLines = 200;
    private const int MaximumOutputCharacters = 120_000;

    private static readonly Regex ContainerNameRegex =
        new(
            "^[A-Za-z0-9][A-Za-z0-9_.-]*$",
            RegexOptions.Compiled);

    private static readonly Regex ComposeProjectRegex =
        new(
            "^[A-Za-z0-9][A-Za-z0-9_.-]*$",
            RegexOptions.Compiled);

    private static readonly Regex AnsiRegex =
        new(
            "\\x1B(?:[@-Z\\-_]|\\[[0-?]*[ -/]*[@-~])",
            RegexOptions.Compiled);

    private static readonly Regex SecretRegex =
        new(
            "(?i)(api[_-]?key|token|password|passphrase|secret)(\\s*[:=]\\s*)([^\\s,;]+)",
            RegexOptions.Compiled);

    private static readonly Regex UrlUserInfoRegex =
        new(
            @"(?i)(https?://)[^/\s:@]+:[^@\s/]+@",
            RegexOptions.Compiled);

    public static DockerFleetSnapshot FromHostSnapshot(
        IReadOnlyList<DockerContainerSnapshot> containers)
    {
        var rows = containers
            .Select(container =>
            {
                var running = container.State.Equals(
                    "running",
                    StringComparison.OrdinalIgnoreCase);
                var attention =
                    container.State.Equals(
                        "dead",
                        StringComparison.OrdinalIgnoreCase) ||
                    container.Status.Contains(
                        "unhealthy",
                        StringComparison.OrdinalIgnoreCase) ||
                    container.Status.Contains(
                        "restarting",
                        StringComparison.OrdinalIgnoreCase);

                return new DockerFleetRow(
                    container.Name,
                    container.Name,
                    "Unclassified",
                    container.Name,
                    container.Image,
                    container.State,
                    container.State.ToUpperInvariant(),
                    "unknown",
                    "UNKNOWN",
                    container.Status,
                    "unknown",
                    0,
                    0,
                    "--",
                    "--",
                    string.IsNullOrWhiteSpace(container.Ports)
                        ? "--"
                        : container.Ports,
                    "--",
                    "--",
                    "--",
                    "Waiting for on-demand stats",
                    "--",
                    "--",
                    "--",
                    running,
                    attention);
            })
            .OrderBy(item => item.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return new DockerFleetSnapshot(
            DateTimeOffset.Now,
            true,
            "Host snapshot",
            "Lightweight host snapshot shown until the Docker workspace refresh completes.",
            rows);
    }

    public async Task<DockerFleetSnapshot> CaptureFleetAsync(
        CancellationToken cancellationToken = default)
    {
        var daemon = await RunDockerAsync(
            new[]
            {
                "version",
                "--format",
                "{{.Server.Version}}"
            },
            TimeSpan.FromSeconds(15),
            cancellationToken);

        var ps = await RunDockerAsync(
            new[]
            {
                "ps",
                "-a",
                "--no-trunc",
                "--format",
                "{{json .}}"
            },
            TimeSpan.FromSeconds(20),
            cancellationToken);

        if (!ps.Success)
        {
            return DockerFleetSnapshot.Unavailable(
                string.IsNullOrWhiteSpace(ps.CombinedOutput)
                    ? "Docker inventory did not return any evidence."
                    : CleanOutput(ps.CombinedOutput));
        }

        var psRows = ParseJsonLines(ps.StandardOutput, "Names");
        if (psRows.Count == 0)
        {
            return new DockerFleetSnapshot(
                DateTimeOffset.Now,
                true,
                FirstMeaningfulLine(daemon.StandardOutput) ?? "--",
                "Docker is available and no containers were returned.",
                Array.Empty<DockerFleetRow>());
        }

        var names = psRows.Keys
            .Where(IsValidContainerName)
            .OrderBy(value => value, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (names.Length == 0)
        {
            return DockerFleetSnapshot.Unavailable(
                "Docker returned container rows, but none had a safe container name.");
        }

        var inspectArguments = new List<string> { "inspect" };
        inspectArguments.AddRange(names);

        var inspect = await RunDockerAsync(
            inspectArguments,
            TimeSpan.FromSeconds(30),
            cancellationToken);

        if (!inspect.Success)
        {
            return DockerFleetSnapshot.Unavailable(
                $"Docker inspect failed: {CleanOutput(inspect.CombinedOutput)}");
        }

        var stats = await RunDockerAsync(
            new[]
            {
                "stats",
                "--no-stream",
                "--format",
                "{{json .}}"
            },
            TimeSpan.FromSeconds(25),
            cancellationToken);

        var statsRows = stats.Success
            ? ParseJsonLines(stats.StandardOutput, "Name")
            : new Dictionary<string, Dictionary<string, string>>(
                StringComparer.OrdinalIgnoreCase);

        var rows = ParseInspectFleet(
            inspect.StandardOutput,
            psRows,
            statsRows);

        var evidence = stats.Success
            ? "Fleet inventory, inspect metadata and one-shot resource statistics captured."
            : "Fleet inventory and inspect metadata captured. Resource statistics were unavailable.";

        return new DockerFleetSnapshot(
            DateTimeOffset.Now,
            true,
            FirstMeaningfulLine(daemon.StandardOutput) ?? "--",
            evidence,
            rows);
    }

    public async Task<DockerContainerDetailSnapshot> CaptureDetailAsync(
        DockerFleetRow row,
        CancellationToken cancellationToken = default)
    {
        if (!IsValidContainerName(row.Name))
            throw new InvalidOperationException("The selected container name is not safe to inspect.");

        var inspect = await RunDockerAsync(
            new[] { "inspect", row.Name },
            TimeSpan.FromSeconds(20),
            cancellationToken);

        if (!inspect.Success)
        {
            throw new InvalidOperationException(
                $"Docker inspect failed: {CleanOutput(inspect.CombinedOutput)}");
        }

        using var document = JsonDocument.Parse(inspect.StandardOutput);
        var root = document.RootElement[0];
        var config = root.GetProperty("Config");
        var state = root.GetProperty("State");

        var labels = ReadStringObject(config, "Labels");
        var composeProject = ValueOr(labels, "com.docker.compose.project", row.ComposeProject);
        var composeService = ValueOr(labels, "com.docker.compose.service", row.ComposeService);
        var composeWorkingDirectory = ValueOr(
            labels,
            "com.docker.compose.project.working_dir",
            row.ComposeWorkingDirectory);

        var createdAt = FormatTimestamp(StringProperty(root, "Created", "--"));
        var startedAt = FormatTimestamp(StringProperty(state, "StartedAt", row.StartedAt));
        var finishedAt = FormatTimestamp(StringProperty(state, "FinishedAt", row.FinishedAt));
        var lifecycle =
            $"Created {createdAt} · Started {startedAt} · Finished {finishedAt} · Exit {row.ExitCode}";

        var composeOwnership = composeProject == "--"
            ? "Standalone container"
            : $"{composeProject} / {composeService}" +
              (composeWorkingDirectory == "--"
                  ? string.Empty
                  : $" · {composeWorkingDirectory}");

        var logs = await RunDockerAsync(
            new[]
            {
                "logs",
                "--tail",
                LogTailLines.ToString(CultureInfo.InvariantCulture),
                "--timestamps",
                row.Name
            },
            TimeSpan.FromSeconds(25),
            cancellationToken);

        var recentLogs = CleanOutput(logs.CombinedOutput);
        if (string.IsNullOrWhiteSpace(recentLogs))
        {
            recentLogs = logs.Success
                ? "No output was returned from the last 200 container log lines."
                : "Container logs were unavailable.";
        }

        return new DockerContainerDetailSnapshot(
            DateTimeOffset.Now,
            row,
            createdAt,
            lifecycle,
            composeOwnership,
            ReadPorts(root, row.Ports),
            ReadNetworks(root),
            ReadMounts(root),
            ReadEnvironmentNames(config),
            recentLogs,
            $"Docker inspect and the last {LogTailLines} log lines were captured on demand. Environment values were not read into the view model.");
    }

    public async Task<DockerWorkspaceCommandResult> RestartDumbProjectAsync(
        string project,
        string workingDirectory,
        CancellationToken cancellationToken = default)
    {
        if (!project.Equals("dumb", StringComparison.OrdinalIgnoreCase))
        {
            return new DockerWorkspaceCommandResult(
                false,
                -1,
                "Only the detected DUMB Compose project is allowed by this action.",
                "DUMB project restart was blocked.");
        }

        if (!ComposeProjectRegex.IsMatch(project))
        {
            return new DockerWorkspaceCommandResult(
                false,
                -1,
                "The Compose project name is not safe.",
                "DUMB project restart was blocked.");
        }

        if (string.IsNullOrWhiteSpace(workingDirectory) ||
            workingDirectory == "--")
        {
            return new DockerWorkspaceCommandResult(
                false,
                -1,
                "Docker did not provide a Compose working-directory label.",
                "DUMB project restart was blocked.");
        }

        string resolvedDirectory;
        try
        {
            resolvedDirectory = Path.GetFullPath(workingDirectory);
        }
        catch (Exception exception)
        {
            return new DockerWorkspaceCommandResult(
                false,
                -1,
                exception.Message,
                "DUMB project restart was blocked.");
        }

        if (!Path.IsPathRooted(resolvedDirectory) ||
            !Directory.Exists(resolvedDirectory))
        {
            return new DockerWorkspaceCommandResult(
                false,
                -1,
                $"Compose working directory is unavailable: {resolvedDirectory}",
                "DUMB project restart was blocked.");
        }

        var result = await RunDockerAsync(
            new[]
            {
                "compose",
                "--project-directory",
                resolvedDirectory,
                "--project-name",
                project,
                "restart"
            },
            TimeSpan.FromMinutes(3),
            cancellationToken);

        var fleet = await CaptureFleetAsync(cancellationToken);
        var projectRows = fleet.Containers
            .Where(item => item.ComposeProject.Equals(
                project,
                StringComparison.OrdinalIgnoreCase))
            .ToArray();
        var verified =
            result.Success &&
            projectRows.Length > 0 &&
            projectRows.All(item => item.IsRunning);

        return new DockerWorkspaceCommandResult(
            verified,
            result.ExitCode,
            CleanOutput(result.CombinedOutput),
            verified
                ? $"DUMB restart completed and {projectRows.Length} project containers verified running."
                : result.Success
                    ? "Docker Compose returned success, but not every detected DUMB container verified running."
                    : $"DUMB restart failed with exit code {result.ExitCode}.");
    }

    private static IReadOnlyList<DockerFleetRow> ParseInspectFleet(
        string json,
        IReadOnlyDictionary<string, Dictionary<string, string>> psRows,
        IReadOnlyDictionary<string, Dictionary<string, string>> statsRows)
    {
        using var document = JsonDocument.Parse(json);
        var rows = new List<DockerFleetRow>();

        foreach (var root in document.RootElement.EnumerateArray())
        {
            var config = root.GetProperty("Config");
            var state = root.GetProperty("State");
            var hostConfig = root.GetProperty("HostConfig");

            var name = StringProperty(root, "Name", "--").TrimStart('/');
            if (!IsValidContainerName(name))
                continue;

            psRows.TryGetValue(name, out var ps);
            statsRows.TryGetValue(name, out var stats);

            var labels = ReadStringObject(config, "Labels");
            var project = ValueOr(labels, "com.docker.compose.project", "--");
            var service = ValueOr(labels, "com.docker.compose.service", "--");
            var workingDirectory = ValueOr(
                labels,
                "com.docker.compose.project.working_dir",
                "--");

            var status = StringProperty(state, "Status", ValueOr(ps, "State", "unknown"));
            var running = BoolProperty(state, "Running");
            var restarting = BoolProperty(state, "Restarting");
            var exitCode = IntProperty(state, "ExitCode");
            var health = ReadHealth(state);
            var restartCount = IntProperty(root, "RestartCount");
            var restartPolicy = ReadRestartPolicy(hostConfig);
            var image = StringProperty(config, "Image", ValueOr(ps, "Image", "--"));
            var id = StringProperty(root, "Id", ValueOr(ps, "ID", name));
            var cpu = ValueOr(stats, "CPUPerc", "--");
            var memory = ValueOr(stats, "MemUsage", "--");
            var memoryPercent = ValueOr(stats, "MemPerc", "--");
            var resources = stats is null
                ? "On demand"
                : $"CPU {cpu} · MEM {memoryPercent}";
            var stateLabel = BuildStateLabel(status, restarting);
            var healthLabel = BuildHealthLabel(health);
            var hasAttention =
                restarting ||
                health.Equals("unhealthy", StringComparison.OrdinalIgnoreCase) ||
                status.Equals("dead", StringComparison.OrdinalIgnoreCase) ||
                (!running && exitCode != 0);
            var group = project.Equals("dumb", StringComparison.OrdinalIgnoreCase)
                ? "DUMB"
                : project == "--"
                    ? "Standalone"
                    : project;

            rows.Add(
                new DockerFleetRow(
                    id,
                    id.Length > 12 ? id[..12] : id,
                    group,
                    name,
                    image,
                    status,
                    stateLabel,
                    health,
                    healthLabel,
                    ValueOr(ps, "Status", stateLabel),
                    restartPolicy,
                    restartCount,
                    exitCode,
                    FormatTimestamp(StringProperty(state, "StartedAt", "--")),
                    FormatTimestamp(StringProperty(state, "FinishedAt", "--")),
                    ValueOr(ps, "Ports", "--"),
                    cpu,
                    memory,
                    memoryPercent,
                    resources,
                    project,
                    service,
                    workingDirectory,
                    running,
                    hasAttention));
        }

        return rows
            .OrderBy(item => item.Group.Equals("DUMB", StringComparison.OrdinalIgnoreCase) ? 0 : 1)
            .ThenBy(item => item.Group, StringComparer.OrdinalIgnoreCase)
            .ThenBy(item => item.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static Dictionary<string, Dictionary<string, string>> ParseJsonLines(
        string output,
        string keyProperty)
    {
        var rows = new Dictionary<string, Dictionary<string, string>>(
            StringComparer.OrdinalIgnoreCase);

        foreach (var line in output.Split(
                     '\n',
                     StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            try
            {
                using var document = JsonDocument.Parse(line);
                var values = document.RootElement
                    .EnumerateObject()
                    .ToDictionary(
                        property => property.Name,
                        property => property.Value.ValueKind == JsonValueKind.String
                            ? property.Value.GetString() ?? string.Empty
                            : property.Value.ToString(),
                        StringComparer.OrdinalIgnoreCase);

                if (values.TryGetValue(keyProperty, out var key) &&
                    !string.IsNullOrWhiteSpace(key))
                {
                    rows[key] = values;
                }
            }
            catch (JsonException)
            {
                // Ignore malformed formatter lines while preserving other rows.
            }
        }

        return rows;
    }

    private static Dictionary<string, string> ReadStringObject(
        JsonElement parent,
        string propertyName)
    {
        var values = new Dictionary<string, string>(
            StringComparer.OrdinalIgnoreCase);

        if (!parent.TryGetProperty(propertyName, out var element) ||
            element.ValueKind != JsonValueKind.Object)
        {
            return values;
        }

        foreach (var property in element.EnumerateObject())
        {
            values[property.Name] =
                property.Value.ValueKind == JsonValueKind.String
                    ? property.Value.GetString() ?? string.Empty
                    : property.Value.ToString();
        }

        return values;
    }

    private static string ReadRestartPolicy(JsonElement hostConfig)
    {
        if (!hostConfig.TryGetProperty("RestartPolicy", out var policy) ||
            policy.ValueKind != JsonValueKind.Object)
        {
            return "none";
        }

        return StringProperty(policy, "Name", "none");
    }

    private static string ReadHealth(JsonElement state)
    {
        if (!state.TryGetProperty("Health", out var health) ||
            health.ValueKind != JsonValueKind.Object)
        {
            return "none";
        }

        return StringProperty(health, "Status", "unknown");
    }

    private static string BuildStateLabel(string state, bool restarting) =>
        restarting
            ? "RESTARTING"
            : state.ToUpperInvariant();

    private static string BuildHealthLabel(string health) =>
        health.ToLowerInvariant() switch
        {
            "healthy" => "HEALTHY",
            "unhealthy" => "UNHEALTHY",
            "starting" => "STARTING",
            "none" => "NO CHECK",
            _ => health.ToUpperInvariant()
        };

    private static string ReadEnvironmentNames(JsonElement config)
    {
        if (!config.TryGetProperty("Env", out var env) ||
            env.ValueKind != JsonValueKind.Array)
        {
            return "No environment-variable names were returned.";
        }

        var names = env.EnumerateArray()
            .Select(item => item.GetString())
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value!.Split('=', 2)[0].Trim())
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(value => value, StringComparer.OrdinalIgnoreCase)
            .Take(256)
            .ToArray();

        return names.Length == 0
            ? "No environment-variable names were returned."
            : string.Join(Environment.NewLine, names);
    }

    private static string ReadMounts(JsonElement root)
    {
        if (!root.TryGetProperty("Mounts", out var mounts) ||
            mounts.ValueKind != JsonValueKind.Array)
        {
            return "No mounts were returned.";
        }

        var rows = mounts.EnumerateArray()
            .Select(mount =>
            {
                var type = StringProperty(mount, "Type", "mount");
                var source = StringProperty(mount, "Source", "--");
                var destination = StringProperty(mount, "Destination", "--");
                var mode = BoolProperty(mount, "RW") ? "rw" : "ro";
                return $"{type} · {source} → {destination} · {mode}";
            })
            .Take(128)
            .ToArray();

        return rows.Length == 0
            ? "No mounts were returned."
            : string.Join(Environment.NewLine, rows);
    }

    private static string ReadNetworks(JsonElement root)
    {
        if (!root.TryGetProperty("NetworkSettings", out var settings) ||
            !settings.TryGetProperty("Networks", out var networks) ||
            networks.ValueKind != JsonValueKind.Object)
        {
            return "No networks were returned.";
        }

        var rows = networks.EnumerateObject()
            .Select(network =>
            {
                var address = network.Value.ValueKind == JsonValueKind.Object
                    ? StringProperty(network.Value, "IPAddress", "--")
                    : "--";
                return $"{network.Name} · {address}";
            })
            .OrderBy(value => value, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return rows.Length == 0
            ? "No networks were returned."
            : string.Join(Environment.NewLine, rows);
    }

    private static string ReadPorts(JsonElement root, string fallback)
    {
        if (!root.TryGetProperty("NetworkSettings", out var settings) ||
            !settings.TryGetProperty("Ports", out var ports) ||
            ports.ValueKind != JsonValueKind.Object)
        {
            return string.IsNullOrWhiteSpace(fallback) ? "--" : fallback;
        }

        var rows = new List<string>();
        foreach (var port in ports.EnumerateObject())
        {
            if (port.Value.ValueKind != JsonValueKind.Array)
            {
                rows.Add($"{port.Name} · internal only");
                continue;
            }

            foreach (var binding in port.Value.EnumerateArray())
            {
                var hostIp = StringProperty(binding, "HostIp", "0.0.0.0");
                var hostPort = StringProperty(binding, "HostPort", "--");
                rows.Add($"{port.Name} → {hostIp}:{hostPort}");
            }
        }

        return rows.Count == 0
            ? string.IsNullOrWhiteSpace(fallback) ? "--" : fallback
            : string.Join(Environment.NewLine, rows);
    }

    private static string StringProperty(
        JsonElement element,
        string propertyName,
        string fallback)
    {
        if (!element.TryGetProperty(propertyName, out var property))
            return fallback;

        return property.ValueKind == JsonValueKind.String
            ? property.GetString() ?? fallback
            : property.ToString();
    }

    private static bool BoolProperty(
        JsonElement element,
        string propertyName) =>
        element.TryGetProperty(propertyName, out var property) &&
        property.ValueKind == JsonValueKind.True;

    private static int IntProperty(
        JsonElement element,
        string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var property))
            return 0;

        if (property.ValueKind == JsonValueKind.Number &&
            property.TryGetInt32(out var value))
        {
            return value;
        }

        return int.TryParse(
            property.ToString(),
            NumberStyles.Integer,
            CultureInfo.InvariantCulture,
            out value)
                ? value
                : 0;
    }

    private static string ValueOr(
        IReadOnlyDictionary<string, string>? values,
        string key,
        string fallback) =>
        values is not null &&
        values.TryGetValue(key, out var value) &&
        !string.IsNullOrWhiteSpace(value)
            ? value
            : fallback;

    private static string FormatTimestamp(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw) ||
            raw == "--" ||
            raw.StartsWith("0001-", StringComparison.Ordinal))
        {
            return "--";
        }

        return DateTimeOffset.TryParse(
            raw,
            CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal,
            out var value)
                ? value.ToLocalTime().ToString("g", CultureInfo.CurrentCulture)
                : raw;
    }

    private static bool IsValidContainerName(string value) =>
        !string.IsNullOrWhiteSpace(value) &&
        ContainerNameRegex.IsMatch(value);

    private static string? FirstMeaningfulLine(string output) =>
        output.Split(
                '\n',
                StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .FirstOrDefault();

    private static string CleanOutput(string output)
    {
        var clean = AnsiRegex.Replace(output ?? string.Empty, string.Empty);
        clean = SecretRegex.Replace(clean, "$1$2<redacted>");
        clean = UrlUserInfoRegex.Replace(clean, "$1<redacted>@");

        var lines = clean
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n')
            .Split('\n')
            .Take(LogTailLines)
            .ToArray();

        clean = string.Join(Environment.NewLine, lines).Trim();
        return clean.Length <= MaximumOutputCharacters
            ? clean
            : clean[..MaximumOutputCharacters] +
              Environment.NewLine +
              "[output truncated by GraveOps]";
    }

    private static async Task<ProcessResult> RunDockerAsync(
        IReadOnlyList<string> arguments,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        try
        {
            using var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = "docker",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                }
            };

            foreach (var argument in arguments)
                process.StartInfo.ArgumentList.Add(argument);

            process.Start();

            var stdoutTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
            var stderrTask = process.StandardError.ReadToEndAsync(cancellationToken);

            using var timeoutSource =
                CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutSource.CancelAfter(timeout);

            try
            {
                await process.WaitForExitAsync(timeoutSource.Token);
            }
            catch (OperationCanceledException)
                when (!cancellationToken.IsCancellationRequested)
            {
                try
                {
                    process.Kill(entireProcessTree: true);
                }
                catch
                {
                    // Best effort only.
                }

                return new ProcessResult(
                    false,
                    -1,
                    string.Empty,
                    $"docker {string.Join(' ', arguments)} timed out.");
            }

            var stdout = await stdoutTask;
            var stderr = await stderrTask;

            return new ProcessResult(
                process.ExitCode == 0,
                process.ExitCode,
                stdout.Trim(),
                stderr.Trim());
        }
        catch (Exception exception)
        {
            return new ProcessResult(
                false,
                -1,
                string.Empty,
                exception.Message);
        }
    }

    private sealed record ProcessResult(
        bool Success,
        int ExitCode,
        string StandardOutput,
        string StandardError)
    {
        public string CombinedOutput =>
            string.Join(
                Environment.NewLine,
                new[] { StandardOutput, StandardError }
                    .Where(value => !string.IsNullOrWhiteSpace(value)));
    }
}
