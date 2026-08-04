using System.Diagnostics;
using System.Net;
using GraveOps.Core.Hosts;

namespace GraveOps.Platform.Windows;

public sealed class LocalWindowsHostProbe : ILocalHostProbe
{
    private static readonly string[] KnownServiceNames =
    {
        "PlexUpdateService",
        "Docker",
        "com.docker.service",
        "ssh-agent",
        "sshd"
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
        ("Readarr", new[] { "readarr" }),
        ("Whisparr", new[] { "whisparr" }),
        ("Mylar3", new[] { "mylar3", "mylar" }),
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

        if (!OperatingSystem.IsWindows())
        {
            warnings.Add(
                "The native Windows provider requires a Windows runtime.");

            return EmptySnapshot(warnings);
        }

        var storage = CaptureStorage(warnings);
        var services = await CaptureServicesAsync(
            cancellationToken,
            warnings);
        var containers = await CaptureContainersAsync(
            cancellationToken,
            warnings);
        var integrations = DetectIntegrations(
            services,
            containers);

        var dockerVersion = await RunTextAsync(
            "docker.exe",
            new[] { "version", "--format", "{{.Server.Version}}" },
            cancellationToken,
            warnings,
            "Docker",
            warnOnFailure: false);

        var hostName = Environment.MachineName;
        var addresses = await CaptureAddressesAsync(
            hostName,
            cancellationToken,
            warnings);

        var runningContainers = containers.Count(container =>
            container.State.Equals(
                "running",
                StringComparison.OrdinalIgnoreCase));

        var dockerState = string.IsNullOrWhiteSpace(dockerVersion)
            ? "Unavailable or not running"
            : $"Docker {dockerVersion.Trim()} | {runningContainers} running";

        return new HostSnapshot(
            DateTimeOffset.UtcNow,
            hostName,
            Environment.OSVersion.VersionString,
            Environment.OSVersion.Version.ToString(),
            FormatUptime(
                TimeSpan.FromMilliseconds(Environment.TickCount64)),
            "Available",
            dockerState,
            Environment.GetEnvironmentVariable(
                "PROCESSOR_IDENTIFIER") ??
            $"{Environment.ProcessorCount} logical processors",
            "Windows load average is not exposed by this preview provider",
            ReadMemorySummary(),
            addresses,
            storage,
            services,
            containers,
            integrations,
            Array.Empty<string>(),
            Array.Empty<string>(),
            warnings.Distinct().ToArray());
    }

    private static HostSnapshot EmptySnapshot(
        IReadOnlyList<string> warnings) =>
        new(
            DateTimeOffset.UtcNow,
            Environment.MachineName,
            "Windows runtime required",
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

    private static IReadOnlyList<StorageVolumeSnapshot> CaptureStorage(
        ICollection<string> warnings)
    {
        var rows = new List<StorageVolumeSnapshot>();

        foreach (var drive in DriveInfo.GetDrives())
        {
            try
            {
                if (!drive.IsReady)
                    continue;

                var total = drive.TotalSize;
                var available = drive.AvailableFreeSpace;
                var used = Math.Max(0, total - available);
                var percent = total <= 0
                    ? 0
                    : used * 100d / total;

                rows.Add(
                    new StorageVolumeSnapshot(
                        drive.Name,
                        drive.DriveFormat,
                        FormatBytes(total),
                        FormatBytes(used),
                        FormatBytes(available),
                        $"{percent:0.#}%",
                        drive.RootDirectory.FullName));
            }
            catch (Exception exception)
            {
                warnings.Add(
                    $"Unable to inspect drive {drive.Name}: " +
                    exception.Message);
            }
        }

        return rows
            .OrderBy(row => row.MountPoint)
            .ToArray();
    }

    private static async Task<IReadOnlyList<ServiceSnapshot>>
        CaptureServicesAsync(
            CancellationToken cancellationToken,
            ICollection<string> warnings)
    {
        var names = string.Join(
            ",",
            KnownServiceNames.Select(name => $"'{name}'"));

        var script =
            "$names=@(" + names + "); " +
            "Get-Service -Name $names -ErrorAction SilentlyContinue | " +
            "ForEach-Object { " +
            "'{0}`t{1}`t{2}`t{3}' -f " +
            "$_.Name,$_.DisplayName,$_.Status,$_.StartType }";

        var output = await RunTextAsync(
            "powershell.exe",
            new[]
            {
                "-NoProfile",
                "-NonInteractive",
                "-Command",
                script
            },
            cancellationToken,
            warnings,
            "Windows services",
            warnOnFailure: false);

        if (string.IsNullOrWhiteSpace(output))
            return Array.Empty<ServiceSnapshot>();

        return output
            .Split(
                '\n',
                StringSplitOptions.RemoveEmptyEntries |
                StringSplitOptions.TrimEntries)
            .Select(line => line.Split('\t'))
            .Where(columns => columns.Length >= 4)
            .Select(columns =>
                new ServiceSnapshot(
                    columns[0],
                    columns[1],
                    columns[2],
                    columns[2],
                    columns[3]))
            .OrderBy(row => row.Description)
            .ToArray();
    }

    private static async Task<IReadOnlyList<DockerContainerSnapshot>>
        CaptureContainersAsync(
            CancellationToken cancellationToken,
            ICollection<string> warnings)
    {
        var output = await RunTextAsync(
            "docker.exe",
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
            warnOnFailure: false);

        if (string.IsNullOrWhiteSpace(output))
            return Array.Empty<DockerContainerSnapshot>();

        return output
            .Split(
                '\n',
                StringSplitOptions.RemoveEmptyEntries |
                StringSplitOptions.TrimEntries)
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
            .OrderBy(row => row.Name)
            .ToArray();
    }

    private static IReadOnlyList<IntegrationSnapshot>
        DetectIntegrations(
            IReadOnlyList<ServiceSnapshot> services,
            IReadOnlyList<DockerContainerSnapshot> containers)
    {
        var detected = new List<IntegrationSnapshot>();

        foreach (var item in IntegrationCatalog)
        {
            var service = services.FirstOrDefault(candidate =>
                item.Tokens.Any(token =>
                    ContainsToken(
                        $"{candidate.Unit} {candidate.Description}",
                        token)));

            if (service is not null)
            {
                detected.Add(
                    new IntegrationSnapshot(
                        item.Name,
                        "Windows service",
                        service.ActiveState,
                        $"{service.Unit} | {service.Description}"));
                continue;
            }

            var container = containers.FirstOrDefault(candidate =>
                item.Tokens.Any(token =>
                    ContainsToken(
                        $"{candidate.Name} {candidate.Image}",
                        token)));

            if (container is not null)
            {
                detected.Add(
                    new IntegrationSnapshot(
                        item.Name,
                        "Docker",
                        container.State,
                        $"{container.Name} | {container.Image}"));
            }
        }

        return detected
            .OrderBy(item => item.Name)
            .ToArray();
    }

    private static bool ContainsToken(
        string value,
        string token) =>
        value.Contains(
            token,
            StringComparison.OrdinalIgnoreCase);

    private static async Task<string> CaptureAddressesAsync(
        string hostName,
        CancellationToken cancellationToken,
        ICollection<string> warnings)
    {
        try
        {
            var addresses = await Dns
                .GetHostAddressesAsync(hostName)
                .WaitAsync(cancellationToken);

            var values = addresses
                .Where(address => !IPAddress.IsLoopback(address))
                .Select(address => address.ToString())
                .Distinct()
                .ToArray();

            return values.Length == 0
                ? "No non-loopback address reported"
                : string.Join("  ", values);
        }
        catch (Exception exception)
        {
            warnings.Add(
                "Unable to resolve local IP addresses: " +
                exception.Message);

            return "Unavailable";
        }
    }

    private static string ReadMemorySummary()
    {
        var information = GC.GetGCMemoryInfo();
        var available = information.TotalAvailableMemoryBytes;
        var workingSet = Environment.WorkingSet;

        return available > 0
            ? $"{FormatBytes(workingSet)} process working set | " +
              $"{FormatBytes(available)} runtime memory limit"
            : $"{FormatBytes(workingSet)} process working set";
    }

    private static string FormatUptime(
        TimeSpan uptime)
    {
        var parts = new List<string>();

        if (uptime.Days > 0)
            parts.Add($"{uptime.Days}d");
        if (uptime.Hours > 0 || parts.Count > 0)
            parts.Add($"{uptime.Hours}h");
        parts.Add($"{uptime.Minutes}m");

        return string.Join(" ", parts);
    }

    private static string FormatBytes(
        long bytes)
    {
        string[] units =
        {
            "B",
            "KiB",
            "MiB",
            "GiB",
            "TiB",
            "PiB"
        };

        var value = Math.Max(0, bytes);
        var display = (double)value;
        var unit = 0;

        while (display >= 1024d &&
               unit < units.Length - 1)
        {
            display /= 1024d;
            unit++;
        }

        return $"{display:0.##} {units[unit]}";
    }

    private static async Task<string> RunTextAsync(
        string fileName,
        IEnumerable<string> arguments,
        CancellationToken cancellationToken,
        ICollection<string> warnings,
        string label,
        bool warnOnFailure = true)
    {
        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = fileName,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            foreach (var argument in arguments)
                startInfo.ArgumentList.Add(argument);

            using var process = new Process
            {
                StartInfo = startInfo
            };

            if (!process.Start())
            {
                if (warnOnFailure)
                    warnings.Add($"{label} could not be started.");

                return string.Empty;
            }

            var outputTask =
                process.StandardOutput.ReadToEndAsync();
            var errorTask =
                process.StandardError.ReadToEndAsync();

            await process.WaitForExitAsync(cancellationToken);

            var output = await outputTask;
            var error = await errorTask;

            if (process.ExitCode != 0 && warnOnFailure)
            {
                warnings.Add(
                    $"{label} exited with code {process.ExitCode}: " +
                    (string.IsNullOrWhiteSpace(error)
                        ? "no error text"
                        : error.Trim()));
            }

            return output.Trim();
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            if (warnOnFailure)
            {
                warnings.Add(
                    $"{label} is unavailable: {exception.Message}");
            }

            return string.Empty;
        }
    }
}
