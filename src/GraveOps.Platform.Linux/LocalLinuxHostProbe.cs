using System.Diagnostics;
using GraveOps.Core.Hosts;

namespace GraveOps.Platform.Linux;

public sealed class LocalLinuxHostProbe : ILocalHostProbe
{
    public async Task<HostSnapshot> CaptureAsync(
        CancellationToken cancellationToken = default)
    {
        var warnings = new List<string>();

        if (!OperatingSystem.IsLinux())
        {
            warnings.Add(
                "The local Linux provider is being built from a non-Linux host. " +
                "Run GraveOps.Desktop.Linux on Linux Mint for live local telemetry.");

            return new HostSnapshot(
                DateTimeOffset.UtcNow,
                Environment.MachineName,
                "Linux runtime required",
                "--",
                "--",
                "Unavailable",
                "Unavailable",
                Array.Empty<StorageVolumeSnapshot>(),
                warnings);
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
            allowNonZeroExit: true);

        var dockerVersion = await RunTextAsync(
            "docker",
            new[] { "version", "--format", "{{.Server.Version}}" },
            cancellationToken,
            warnings,
            "Docker",
            allowNonZeroExit: true);

        var operatingSystem = ReadOsRelease(warnings);
        var storage = await CaptureStorageAsync(cancellationToken, warnings);

        return new HostSnapshot(
            DateTimeOffset.UtcNow,
            ValueOrFallback(hostname, Environment.MachineName),
            operatingSystem,
            ValueOrFallback(kernel, "--"),
            ValueOrFallback(uptime, "--"),
            ValueOrFallback(systemState, "Unknown"),
            string.IsNullOrWhiteSpace(dockerVersion)
                ? "Unavailable or not running"
                : $"Docker {dockerVersion.Trim()}",
            storage,
            warnings);
    }

    private static string ReadOsRelease(ICollection<string> warnings)
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

            if (values.TryGetValue("PRETTY_NAME", out var prettyName) &&
                !string.IsNullOrWhiteSpace(prettyName))
            {
                return prettyName;
            }
        }
        catch (Exception exception)
        {
            warnings.Add($"Unable to read {path}: {exception.Message}");
        }

        return "Linux";
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
                "-hP",
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
        var lines = output
            .Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Skip(1);

        foreach (var line in lines)
        {
            var columns = line.Split(
                (char[]?)null,
                StringSplitOptions.RemoveEmptyEntries);

            if (columns.Length < 6)
                continue;

            rows.Add(new StorageVolumeSnapshot(
                columns[0],
                columns[1],
                columns[2],
                columns[3],
                columns[4],
                string.Join(' ', columns.Skip(5))));
        }

        return rows;
    }

    private static async Task<string> RunTextAsync(
        string executable,
        IReadOnlyList<string> arguments,
        CancellationToken cancellationToken,
        ICollection<string> warnings,
        string operationName,
        bool allowNonZeroExit = false)
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

            var stdoutTask = process.StandardOutput.ReadToEndAsync(
                cancellationToken);
            var stderrTask = process.StandardError.ReadToEndAsync(
                cancellationToken);

            await process.WaitForExitAsync(cancellationToken);

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
                     !string.IsNullOrWhiteSpace(stderr))
            {
                warnings.Add($"{operationName}: {stderr}");
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

    private static string ValueOrFallback(
        string? value,
        string fallback) =>
        string.IsNullOrWhiteSpace(value)
            ? fallback
            : value.Trim();
}