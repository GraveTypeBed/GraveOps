using System.Diagnostics;
using GraveOps.Core.Hosts;

namespace GraveOps.Platform.Linux;

public sealed class LocalLinuxHostProbe : ILocalHostProbe
{
    private static readonly string[] KnownServiceUnits =
    {
        "plexmediaserver.service",
        "docker.service",
        "containerd.service",
        "ssh.service",
        "sshd.service",
        "smbd.service",
        "nmbd.service",
        "NetworkManager.service",
        "mullvad-daemon.service"
    };

    private static readonly (string Name, string[] Tokens)[] IntegrationCatalog =
    {
        ("Plex", new[] { "plex" }),
        ("Jellyfin", new[] { "jellyfin" }),
        ("Emby", new[] { "emby" }),
        ("Tautulli", new[] { "tautulli" }),
        ("Kometa", new[] { "kometa", "plex-meta-manager" }),
        ("Sonarr", new[] { "sonarr" }),
        ("Radarr", new[] { "radarr" }),
        ("Lidarr", new[] { "lidarr" }),
        ("Prowlarr", new[] { "prowlarr" }),
        ("Bazarr", new[] { "bazarr" }),
        ("Seerr", new[] { "seerr", "overseerr", "jellyseerr" }),
        ("SABnzbd", new[] { "sabnzbd" }),
        ("qBittorrent", new[] { "qbittorrent" }),
        ("Recyclarr", new[] { "recyclarr" }),
        ("Profilarr", new[] { "profilarr" }),
        ("autobrr", new[] { "autobrr" }),
        ("Unpackerr", new[] { "unpackerr" }),
        ("Cleanuparr", new[] { "cleanuparr" }),
        ("Tdarr", new[] { "tdarr" }),
        ("Maintainerr", new[] { "maintainerr" }),
        ("Pi-hole", new[] { "pihole", "pi-hole" }),
        ("DUMB", new[] { "dumb" }),
        ("Decypharr", new[] { "decypharr" }),
        ("Zurg", new[] { "zurg" }),
        ("Zilean", new[] { "zilean" })
    };

    public async Task<HostSnapshot> CaptureAsync(
        CancellationToken cancellationToken = default)
    {
        var warnings = new List<string>();

        if (!OperatingSystem.IsLinux())
        {
            warnings.Add(
                "The native Linux provider requires a Linux runtime.");

            return EmptySnapshot(warnings);
        }

        var hostname = await RunTextAsync(
            "hostname",
            Array.Empty<string>(),
            cancellationToken,
            warnings,
            "hostname");

        var kernel = await RunTextAsync(
            "uname",
            new[] { "-r" },
            cancellationToken,
            warnings,
            "kernel");

        var uptime = await RunTextAsync(
            "uptime",
            new[] { "-p" },
            cancellationToken,
            warnings,
            "uptime");

        var systemState = await RunTextAsync(
            "systemctl",
            new[] { "is-system-running" },
            cancellationToken,
            warnings,
            "systemd state",
            allowNonZeroExit: true,
            warnOnNonZeroExit: false);

        var dockerVersion = await RunTextAsync(
            "docker",
            new[] { "version", "--format", "{{.Server.Version}}" },
            cancellationToken,
            warnings,
            "Docker",
            allowNonZeroExit: true,
            warnOnNonZeroExit: false);

        var ipAddresses = await RunTextAsync(
            "hostname",
            new[] { "-I" },
            cancellationToken,
            warnings,
            "IP addresses",
            allowNonZeroExit: true,
            warnOnNonZeroExit: false);

        var storage = await CaptureStorageAsync(
            cancellationToken,
            warnings);

        var services = await CaptureServicesAsync(
            cancellationToken,
            warnings);

        var containers = await CaptureContainersAsync(
            cancellationToken,
            warnings);

        var failedUnits = await CaptureFailedUnitsAsync(
            cancellationToken,
            warnings);

        var logs = await CaptureLogsAsync(
            cancellationToken,
            warnings);

        var integrations = DetectIntegrations(
            services,
            containers);

        var cpuModel = ReadCpuModel(warnings);
        var loadAverage = ReadLoadAverage(warnings);
        var memorySummary = ReadMemorySummary(warnings);
        var operatingSystem = ReadOsRelease(warnings);

        var dockerState =
            string.IsNullOrWhiteSpace(dockerVersion)
                ? "Unavailable or not running"
                : $"Docker {dockerVersion.Trim()} · " +
                  $"{containers.Count(container =>
                      container.State.Equals(
                          "running",
                          StringComparison.OrdinalIgnoreCase))} running";

        return new HostSnapshot(
            DateTimeOffset.UtcNow,
            ValueOrFallback(hostname, Environment.MachineName),
            operatingSystem,
            ValueOrFallback(kernel, "--"),
            ValueOrFallback(uptime, "--"),
            ValueOrFallback(systemState, "Unknown"),
            dockerState,
            cpuModel,
            loadAverage,
            memorySummary,
            ValueOrFallback(ipAddresses, "No address reported"),
            storage,
            services,
            containers,
            integrations,
            failedUnits,
            logs,
            warnings.Distinct().ToArray());
    }

    private static HostSnapshot EmptySnapshot(
        IReadOnlyList<string> warnings) =>
        new(
            DateTimeOffset.UtcNow,
            Environment.MachineName,
            "Linux runtime required",
            "--",
            "--",
            "Unavailable",
            "Unavailable",
            "--",
            "--",
            "--",
            "--",
            Array.Empty<StorageVolumeSnapshot>(),
            Array.Empty<ServiceSnapshot>(),
            Array.Empty<DockerContainerSnapshot>(),
            Array.Empty<IntegrationSnapshot>(),
            Array.Empty<string>(),
            Array.Empty<string>(),
            warnings);

    private static string ReadOsRelease(
        ICollection<string> warnings)
    {
        const string path = "/etc/os-release";

        try
        {
            if (!File.Exists(path))
                return "Linux";

            var values = File.ReadAllLines(path)
                .Select(line => line.Split('=', 2))
                .Where(parts => parts.Length == 2)
                .ToDictionary(
                    parts => parts[0],
                    parts => parts[1].Trim().Trim('"'),
                    StringComparer.OrdinalIgnoreCase);

            return values.TryGetValue(
                       "PRETTY_NAME",
                       out var prettyName) &&
                   !string.IsNullOrWhiteSpace(prettyName)
                ? prettyName
                : "Linux";
        }
        catch (Exception exception)
        {
            warnings.Add(
                $"Unable to read {path}: {exception.Message}");

            return "Linux";
        }
    }

    private static string ReadCpuModel(
        ICollection<string> warnings)
    {
        try
        {
            const string path = "/proc/cpuinfo";

            if (!File.Exists(path))
                return $"{Environment.ProcessorCount} logical processors";

            var modelLine = File.ReadLines(path)
                .FirstOrDefault(line =>
                    line.StartsWith(
                        "model name",
                        StringComparison.OrdinalIgnoreCase));

            if (string.IsNullOrWhiteSpace(modelLine))
                return $"{Environment.ProcessorCount} logical processors";

            var parts = modelLine.Split(':', 2);

            return parts.Length == 2
                ? parts[1].Trim()
                : modelLine.Trim();
        }
        catch (Exception exception)
        {
            warnings.Add(
                $"Unable to read CPU information: {exception.Message}");

            return $"{Environment.ProcessorCount} logical processors";
        }
    }

    private static string ReadLoadAverage(
        ICollection<string> warnings)
    {
        try
        {
            const string path = "/proc/loadavg";

            if (!File.Exists(path))
                return "--";

            var values = File.ReadAllText(path)
                .Split(
                    (char[]?)null,
                    StringSplitOptions.RemoveEmptyEntries)
                .Take(3)
                .ToArray();

            return values.Length == 3
                ? $"{string.Join("  ", values)} · " +
                  $"{Environment.ProcessorCount} logical CPUs"
                : "--";
        }
        catch (Exception exception)
        {
            warnings.Add(
                $"Unable to read load average: {exception.Message}");

            return "--";
        }
    }

    private static string ReadMemorySummary(
        ICollection<string> warnings)
    {
        try
        {
            const string path = "/proc/meminfo";

            if (!File.Exists(path))
                return "--";

            var values = File.ReadAllLines(path)
                .Select(line => line.Split(':', 2))
                .Where(parts => parts.Length == 2)
                .ToDictionary(
                    parts => parts[0],
                    parts => ParseKilobytes(parts[1]),
                    StringComparer.OrdinalIgnoreCase);

            if (!values.TryGetValue("MemTotal", out var totalKb))
                return "--";

            values.TryGetValue(
                "MemAvailable",
                out var availableKb);

            var usedKb = Math.Max(
                0,
                totalKb - availableKb);

            return $"{FormatKilobytes(usedKb)} used · " +
                   $"{FormatKilobytes(availableKb)} available · " +
                   $"{FormatKilobytes(totalKb)} total";
        }
        catch (Exception exception)
        {
            warnings.Add(
                $"Unable to read memory information: {exception.Message}");

            return "--";
        }
    }

    private static long ParseKilobytes(
        string raw)
    {
        var token = raw
            .Split(
                (char[]?)null,
                StringSplitOptions.RemoveEmptyEntries)
            .FirstOrDefault();

        return long.TryParse(token, out var value)
            ? value
            : 0;
    }

    private static string FormatKilobytes(
        long kilobytes)
    {
        var gibibytes =
            kilobytes / 1024d / 1024d;

        return $"{gibibytes:0.0} GiB";
    }

    private static async Task<IReadOnlyList<StorageVolumeSnapshot>>
        CaptureStorageAsync(
            CancellationToken cancellationToken,
            ICollection<string> warnings)
    {
        var output = await RunTextAsync(
            "df",
            new[]
            {
                "-hPT",
                "-x", "tmpfs",
                "-x", "devtmpfs",
                "-x", "squashfs",
                "-x", "overlay"
            },
            cancellationToken,
            warnings,
            "storage",
            allowNonZeroExit: true);

        if (string.IsNullOrWhiteSpace(output))
            return Array.Empty<StorageVolumeSnapshot>();

        var rows = new List<StorageVolumeSnapshot>();

        foreach (var line in output
                     .Split(
                         '\n',
                         StringSplitOptions.RemoveEmptyEntries)
                     .Skip(1))
        {
            var columns = line.Split(
                (char[]?)null,
                StringSplitOptions.RemoveEmptyEntries);

            if (columns.Length < 7)
                continue;

            rows.Add(
                new StorageVolumeSnapshot(
                    columns[0],
                    columns[1],
                    columns[2],
                    columns[3],
                    columns[4],
                    columns[5],
                    string.Join(' ', columns.Skip(6))));
        }

        return rows;
    }

    private static async Task<IReadOnlyList<ServiceSnapshot>>
        CaptureServicesAsync(
            CancellationToken cancellationToken,
            ICollection<string> warnings)
    {
        var rows = new List<ServiceSnapshot>();

        foreach (var unit in KnownServiceUnits)
        {
            var output = await RunTextAsync(
                "systemctl",
                new[]
                {
                    "show",
                    unit,
                    "--no-pager",
                    "--property=Id",
                    "--property=Description",
                    "--property=LoadState",
                    "--property=ActiveState",
                    "--property=SubState",
                    "--property=UnitFileState"
                },
                cancellationToken,
                warnings,
                unit,
                allowNonZeroExit: true,
                warnOnNonZeroExit: false);

            if (string.IsNullOrWhiteSpace(output))
                continue;

            var values = output
                .Split(
                    '\n',
                    StringSplitOptions.RemoveEmptyEntries)
                .Select(line => line.Split('=', 2))
                .Where(parts => parts.Length == 2)
                .ToDictionary(
                    parts => parts[0],
                    parts => parts[1],
                    StringComparer.OrdinalIgnoreCase);

            if (values.TryGetValue(
                    "LoadState",
                    out var loadState) &&
                loadState.Equals(
                    "not-found",
                    StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            rows.Add(
                new ServiceSnapshot(
                    Value(values, "Id", unit),
                    Value(values, "Description", unit),
                    Value(values, "ActiveState", "unknown"),
                    Value(values, "SubState", "unknown"),
                    Value(values, "UnitFileState", "unknown")));
        }

        return rows
            .OrderBy(service => service.Unit)
            .ToArray();
    }

    private static async Task<IReadOnlyList<DockerContainerSnapshot>>
        CaptureContainersAsync(
            CancellationToken cancellationToken,
            ICollection<string> warnings)
    {
        var output = await RunTextAsync(
            "docker",
            new[]
            {
                "ps",
                "-a",
                "--format",
                "{{.Names}}\t{{.Image}}\t{{.State}}\t{{.Status}}\t{{.Ports}}"
            },
            cancellationToken,
            warnings,
            "Docker containers",
            allowNonZeroExit: true,
            warnOnNonZeroExit: false);

        if (string.IsNullOrWhiteSpace(output))
            return Array.Empty<DockerContainerSnapshot>();

        return output
            .Split(
                '\n',
                StringSplitOptions.RemoveEmptyEntries)
            .Select(line => line.Split('\t'))
            .Where(columns => columns.Length >= 4)
            .Select(columns =>
                new DockerContainerSnapshot(
                    columns[0],
                    columns[1],
                    columns[2],
                    columns[3],
                    columns.Length >= 5
                        ? columns[4]
                        : string.Empty))
            .OrderBy(container => container.Name)
            .ToArray();
    }

    private static async Task<IReadOnlyList<string>>
        CaptureFailedUnitsAsync(
            CancellationToken cancellationToken,
            ICollection<string> warnings)
    {
        var output = await RunTextAsync(
            "systemctl",
            new[]
            {
                "--failed",
                "--type=service",
                "--no-legend",
                "--plain",
                "--no-pager"
            },
            cancellationToken,
            warnings,
            "failed services",
            allowNonZeroExit: true,
            warnOnNonZeroExit: false);

        if (string.IsNullOrWhiteSpace(output))
            return Array.Empty<string>();

        return output
            .Split(
                '\n',
                StringSplitOptions.RemoveEmptyEntries)
            .Select(line => line.Trim())
            .ToArray();
    }

    private static async Task<IReadOnlyList<string>>
        CaptureLogsAsync(
            CancellationToken cancellationToken,
            ICollection<string> warnings)
    {
        var output = await RunTextAsync(
            "journalctl",
            new[]
            {
                "-p", "warning",
                "-n", "40",
                "--no-pager",
                "--output=short-iso"
            },
            cancellationToken,
            warnings,
            "journal warnings",
            allowNonZeroExit: true,
            warnOnNonZeroExit: false);

        if (string.IsNullOrWhiteSpace(output))
            return new[]
            {
                "No warning-or-higher journal entries were returned."
            };

        return output
            .Split(
                '\n',
                StringSplitOptions.RemoveEmptyEntries)
            .ToArray();
    }

    private static IReadOnlyList<IntegrationSnapshot>
        DetectIntegrations(
            IReadOnlyList<ServiceSnapshot> services,
            IReadOnlyList<DockerContainerSnapshot> containers)
    {
        var evidence = containers
            .Select(container =>
                new
                {
                    Text =
                        $"{container.Name} {container.Image}".ToLowerInvariant(),
                    Kind = "Docker",
                    State = container.Status,
                    Evidence = container.Name
                })
            .Concat(
                services.Select(service =>
                    new
                    {
                        Text =
                            $"{service.Unit} {service.Description}".ToLowerInvariant(),
                        Kind = "systemd",
                        State =
                            $"{service.ActiveState}/{service.SubState}",
                        Evidence = service.Unit
                    }))
            .ToArray();

        var rows = new List<IntegrationSnapshot>();

        foreach (var entry in IntegrationCatalog)
        {
            var match = evidence.FirstOrDefault(item =>
                entry.Tokens.Any(token =>
                    item.Text.Contains(
                        token,
                        StringComparison.OrdinalIgnoreCase)));

            if (match is null)
                continue;

            rows.Add(
                new IntegrationSnapshot(
                    entry.Name,
                    match.Kind,
                    match.State,
                    match.Evidence));
        }

        return rows
            .DistinctBy(integration => integration.Name)
            .OrderBy(integration => integration.Name)
            .ToArray();
    }

    private static async Task<string> RunTextAsync(
        string executable,
        IReadOnlyList<string> arguments,
        CancellationToken cancellationToken,
        ICollection<string> warnings,
        string operationName,
        bool allowNonZeroExit = false,
        bool warnOnNonZeroExit = true)
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

            var stdoutTask =
                process.StandardOutput.ReadToEndAsync(
                    cancellationToken);

            var stderrTask =
                process.StandardError.ReadToEndAsync(
                    cancellationToken);

            await process.WaitForExitAsync(
                cancellationToken);

            var stdout = (await stdoutTask).Trim();
            var stderr = (await stderrTask).Trim();

            if (process.ExitCode != 0 &&
                !allowNonZeroExit)
            {
                warnings.Add(
                    $"{operationName} returned exit code " +
                    $"{process.ExitCode}: {stderr}");
            }
            else if (process.ExitCode != 0 &&
                     allowNonZeroExit &&
                     warnOnNonZeroExit &&
                     !string.IsNullOrWhiteSpace(stderr))
            {
                warnings.Add(
                    $"{operationName}: {stderr}");
            }

            return stdout;
        }
        catch (Exception exception)
        {
            warnings.Add(
                $"Unable to query {operationName} using " +
                $"{executable}: {exception.Message}");

            return string.Empty;
        }
    }

    private static string Value(
        IReadOnlyDictionary<string, string> values,
        string key,
        string fallback) =>
        values.TryGetValue(key, out var value) &&
        !string.IsNullOrWhiteSpace(value)
            ? value
            : fallback;

    private static string ValueOrFallback(
        string? value,
        string fallback) =>
        string.IsNullOrWhiteSpace(value)
            ? fallback
            : value.Trim();
}
