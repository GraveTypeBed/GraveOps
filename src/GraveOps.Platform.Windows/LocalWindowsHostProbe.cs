using System.Diagnostics;
using System.Net;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using GraveOps.Core.Hosts;

namespace GraveOps.Platform.Windows;

public sealed class LocalWindowsHostProbe : ILocalHostProbe
{
    private static readonly IntegrationDefinition[] IntegrationCatalog =
    {
        new("Plex", new[] { "plex" }, new[] { 32400 }),
        new("Jellyfin", new[] { "jellyfin" }, new[] { 8096, 8920 }),
        new("Emby", new[] { "emby" }, new[] { 8096, 8920 }),
        new("Tautulli", new[] { "tautulli" }, new[] { 8181 }),
        new("Kometa", new[] { "kometa", "plex-meta-manager" }, Array.Empty<int>()),
        new("Sonarr", new[] { "sonarr" }, new[] { 8989, 8990 }),
        new("Radarr", new[] { "radarr" }, new[] { 7878, 7879 }),
        new("Lidarr", new[] { "lidarr" }, new[] { 8686 }),
        new("Prowlarr", new[] { "prowlarr" }, new[] { 9696 }),
        new("Readarr", new[] { "readarr" }, new[] { 8787 }),
        new("Whisparr", new[] { "whisparr" }, new[] { 6969 }),
        new("Mylar3", new[] { "mylar3", "mylar" }, new[] { 8090 }),
        new("Bazarr", new[] { "bazarr" }, new[] { 6767 }),
        new("Seerr", new[] { "seerr", "overseerr", "jellyseerr" }, new[] { 5055 }),
        new("SABnzbd", new[] { "sabnzbd" }, new[] { 8080 }),
        new("qBittorrent", new[] { "qbittorrent" }, new[] { 8080, 6881 }),
        new("Recyclarr", new[] { "recyclarr" }, Array.Empty<int>()),
        new("Profilarr", new[] { "profilarr" }, new[] { 6868 }),
        new("autobrr", new[] { "autobrr" }, new[] { 7474 }),
        new("Unpackerr", new[] { "unpackerr" }, Array.Empty<int>()),
        new("Cleanuparr", new[] { "cleanuparr" }, Array.Empty<int>()),
        new("Tdarr", new[] { "tdarr" }, new[] { 8265, 8266 }),
        new("Maintainerr", new[] { "maintainerr" }, new[] { 6246 }),
        new("Pi-hole", new[] { "pihole", "pi-hole" }, Array.Empty<int>()),
        new("DUMB", new[] { "dumb" }, new[] { 3005 }),
        new("Decypharr", new[] { "decypharr" }, new[] { 8282 }),
        new("Zurg", new[] { "zurg" }, new[] { 18080 }),
        new("Zilean", new[] { "zilean" }, new[] { 8182 })
    };

    private static readonly string[] AdditionalDiscoveryTokens =
    {
        "docker",
        "com.docker",
        "dockerd",
        "ssh-agent",
        "sshd"
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

        var processes = await CaptureProcessesAsync(
            cancellationToken,
            warnings);

        var installedApplications =
            await CaptureInstalledApplicationsAsync(
                cancellationToken,
                warnings);

        var listeners = await CaptureListenersAsync(
            cancellationToken,
            warnings);

        var dockerExecutable = ResolveDockerExecutable();

        var containers = await CaptureContainersAsync(
            dockerExecutable,
            cancellationToken,
            warnings);

        var integrations = DetectIntegrations(
            services,
            containers,
            processes,
            installedApplications,
            listeners);

        var dockerVersion = string.IsNullOrWhiteSpace(dockerExecutable)
            ? string.Empty
            : await RunTextAsync(
                dockerExecutable,
                new[]
                {
                    "version",
                    "--format",
                    "{{.Server.Version}}"
                },
                cancellationToken,
                warnings,
                "Docker engine",
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

        var dockerDetected =
            !string.IsNullOrWhiteSpace(dockerExecutable) ||
            services.Any(service =>
                ContainsAnyToken(
                    $"{service.Unit} {service.Description}",
                    AdditionalDiscoveryTokens)) ||
            processes.Any(process =>
                ContainsAnyToken(
                    $"{process.Name} {process.ExecutablePath}",
                    AdditionalDiscoveryTokens)) ||
            installedApplications.Any(application =>
                ContainsAnyToken(
                    $"{application.Name} {application.InstallLocation}",
                    AdditionalDiscoveryTokens));

        var dockerState = !string.IsNullOrWhiteSpace(dockerVersion)
            ? $"Docker {dockerVersion.Trim()} | {runningContainers} running"
            : dockerDetected
                ? "Docker detected | engine unavailable"
                : "Unavailable or not installed";

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
            "Windows load average is not exposed by this provider",
            ReadPhysicalMemorySummary(warnings),
            addresses,
            storage,
            services,
            containers,
            integrations,
            Array.Empty<string>(),
            Array.Empty<string>(),
            warnings
                .Where(warning => !string.IsNullOrWhiteSpace(warning))
                .Distinct(StringComparer.Ordinal)
                .ToArray());
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
        var pattern = BuildDiscoveryPattern();

        var script =
            "$pattern='" +
            EscapePowerShellSingleQuoted(pattern) +
            "'; " +
            "Get-CimInstance Win32_Service -ErrorAction Stop | " +
            "Where-Object { " +
            "(($_.Name + ' ' + $_.DisplayName + ' ' + $_.PathName) " +
            "-match $pattern) } | " +
            "ForEach-Object { " +
            "'{0}`t{1}`t{2}`t{3}' -f " +
            "$_.Name,$_.DisplayName,$_.State,$_.StartMode }";

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
            "Windows service discovery");

        if (string.IsNullOrWhiteSpace(output))
            return Array.Empty<ServiceSnapshot>();

        return SplitOutputLines(output)
            .Select(SplitColumns)
            .Where(columns => columns.Length >= 4)
            .Select(columns =>
                new ServiceSnapshot(
                    columns[0],
                    columns[1],
                    NormalizeWindowsState(columns[2]),
                    NormalizeWindowsState(columns[2]),
                    columns[3]))
            .OrderBy(row => row.Description)
            .ThenBy(row => row.Unit)
            .ToArray();
    }

    private static async Task<IReadOnlyList<ProcessEvidence>>
        CaptureProcessesAsync(
            CancellationToken cancellationToken,
            ICollection<string> warnings)
    {
        var pattern = BuildDiscoveryPattern();

        var script =
            "$pattern='" +
            EscapePowerShellSingleQuoted(pattern) +
            "'; " +
            "Get-CimInstance Win32_Process -ErrorAction Stop | " +
            "Where-Object { " +
            "(($_.Name + ' ' + $_.ExecutablePath) -match $pattern) } | " +
            "ForEach-Object { " +
            "$path=if ($_.ExecutablePath) " +
            "{ $_.ExecutablePath -replace \"`t\",' ' } else { '' }; " +
            "'{0}`t{1}`t{2}' -f $_.Name,$_.ProcessId,$path }";

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
            "Windows process discovery");

        if (string.IsNullOrWhiteSpace(output))
            return Array.Empty<ProcessEvidence>();

        return SplitOutputLines(output)
            .Select(SplitColumns)
            .Where(columns =>
                columns.Length >= 2 &&
                int.TryParse(columns[1], out _))
            .Select(columns =>
                new ProcessEvidence(
                    columns[0],
                    int.Parse(columns[1]),
                    columns.Length >= 3
                        ? columns[2]
                        : string.Empty))
            .OrderBy(row => row.Name)
            .ThenBy(row => row.ProcessId)
            .ToArray();
    }

    private static async Task<IReadOnlyList<InstalledApplicationEvidence>>
        CaptureInstalledApplicationsAsync(
            CancellationToken cancellationToken,
            ICollection<string> warnings)
    {
        var pattern = BuildDiscoveryPattern();

        var script =
            "$pattern='" +
            EscapePowerShellSingleQuoted(pattern) +
            "'; " +
            "$roots=@(" +
            "'HKLM:\\Software\\Microsoft\\Windows\\CurrentVersion\\Uninstall\\*'," +
            "'HKLM:\\Software\\WOW6432Node\\Microsoft\\Windows\\CurrentVersion\\Uninstall\\*'," +
            "'HKCU:\\Software\\Microsoft\\Windows\\CurrentVersion\\Uninstall\\*'); " +
            "Get-ItemProperty -Path $roots -ErrorAction SilentlyContinue | " +
            "Where-Object { " +
            "$_.DisplayName -and " +
            "(($_.DisplayName + ' ' + $_.InstallLocation + ' ' + $_.DisplayIcon) " +
            "-match $pattern) } | " +
            "ForEach-Object { " +
            "$location=if ($_.InstallLocation) " +
            "{ $_.InstallLocation -replace \"`t\",' ' } else { '' }; " +
            "$icon=if ($_.DisplayIcon) " +
            "{ $_.DisplayIcon -replace \"`t\",' ' } else { '' }; " +
            "'{0}`t{1}`t{2}' -f $_.DisplayName,$location,$icon }";

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
            "Installed application discovery");

        if (string.IsNullOrWhiteSpace(output))
            return Array.Empty<InstalledApplicationEvidence>();

        return SplitOutputLines(output)
            .Select(SplitColumns)
            .Where(columns => columns.Length >= 1)
            .Select(columns =>
                new InstalledApplicationEvidence(
                    columns[0].Trim(),
                    columns.Length >= 2
                        ? NormalizeRegistryPath(columns[1])
                        : string.Empty,
                    columns.Length >= 3
                        ? NormalizeRegistryPath(columns[2])
                        : string.Empty))
            .GroupBy(
                row => row.Name,
                StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .OrderBy(row => row.Name)
            .ToArray();
    }

    private static async Task<IReadOnlyList<ListenerEvidence>>
        CaptureListenersAsync(
            CancellationToken cancellationToken,
            ICollection<string> warnings)
    {
        var ports = IntegrationCatalog
            .SelectMany(definition => definition.DefaultPorts)
            .Distinct()
            .OrderBy(port => port)
            .ToArray();

        if (ports.Length == 0)
            return Array.Empty<ListenerEvidence>();

        var powerShellPorts = string.Join(",", ports);

        var script =
            "$ports=@(" + powerShellPorts + "); " +
            "Get-NetTCPConnection -State Listen -ErrorAction Stop | " +
            "Where-Object { $ports -contains [int]$_.LocalPort } | " +
            "ForEach-Object { " +
            "'{0}`t{1}`t{2}' -f " +
            "$_.LocalAddress,$_.LocalPort,$_.OwningProcess }";

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
            "TCP listener discovery",
            warnOnFailure: false);

        if (string.IsNullOrWhiteSpace(output))
            return Array.Empty<ListenerEvidence>();

        return SplitOutputLines(output)
            .Select(SplitColumns)
            .Where(columns =>
                columns.Length >= 3 &&
                int.TryParse(columns[1], out _) &&
                int.TryParse(columns[2], out _))
            .Select(columns =>
                new ListenerEvidence(
                    columns[0],
                    int.Parse(columns[1]),
                    int.Parse(columns[2])))
            .OrderBy(row => row.Port)
            .ThenBy(row => row.Address)
            .ToArray();
    }

    private static async Task<IReadOnlyList<DockerContainerSnapshot>>
        CaptureContainersAsync(
            string dockerExecutable,
            CancellationToken cancellationToken,
            ICollection<string> warnings)
    {
        if (string.IsNullOrWhiteSpace(dockerExecutable))
            return Array.Empty<DockerContainerSnapshot>();

        var output = await RunTextAsync(
            dockerExecutable,
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

        return SplitOutputLines(output)
            .Select(SplitColumns)
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
            IReadOnlyList<DockerContainerSnapshot> containers,
            IReadOnlyList<ProcessEvidence> processes,
            IReadOnlyList<InstalledApplicationEvidence> installedApplications,
            IReadOnlyList<ListenerEvidence> listeners)
    {
        var detected = new List<IntegrationSnapshot>();

        foreach (var definition in IntegrationCatalog)
        {
            var service = services.FirstOrDefault(candidate =>
                ContainsAnyToken(
                    $"{candidate.Unit} {candidate.Description}",
                    definition.Tokens));

            var container = containers.FirstOrDefault(candidate =>
                ContainsAnyToken(
                    $"{candidate.Name} {candidate.Image}",
                    definition.Tokens));

            var process = processes.FirstOrDefault(candidate =>
                ContainsAnyToken(
                    $"{candidate.Name} {candidate.ExecutablePath}",
                    definition.Tokens));

            var installedApplication =
                installedApplications.FirstOrDefault(candidate =>
                    ContainsAnyToken(
                        $"{candidate.Name} " +
                        $"{candidate.InstallLocation} " +
                        $"{candidate.DisplayIcon}",
                        definition.Tokens));

            if (service is null &&
                container is null &&
                process is null &&
                installedApplication is null)
            {
                continue;
            }

            var evidence = new List<string>();

            if (service is not null)
            {
                evidence.Add(
                    $"service {service.Unit} ({service.ActiveState})");
            }

            if (container is not null)
            {
                evidence.Add(
                    $"container {container.Name} ({container.State})");
            }

            if (process is not null)
            {
                var executable = string.IsNullOrWhiteSpace(
                    process.ExecutablePath)
                    ? process.Name
                    : process.ExecutablePath;

                evidence.Add(
                    $"process {executable} (PID {process.ProcessId})");

                var processListeners = listeners
                    .Where(listener =>
                        listener.OwningProcessId == process.ProcessId &&
                        (definition.DefaultPorts.Length == 0 ||
                         definition.DefaultPorts.Contains(listener.Port)))
                    .Select(listener =>
                        $"{NormalizeListenerAddress(listener.Address)}:" +
                        listener.Port)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToArray();

                if (processListeners.Length > 0)
                {
                    evidence.Add(
                        "listening " +
                        string.Join(", ", processListeners));
                }
            }

            if (installedApplication is not null)
            {
                var location =
                    FirstNonEmpty(
                        installedApplication.InstallLocation,
                        installedApplication.DisplayIcon,
                        installedApplication.Name);

                evidence.Add(
                    location.Equals(
                        installedApplication.Name,
                        StringComparison.OrdinalIgnoreCase)
                        ? $"installed {installedApplication.Name}"
                        : $"installed {installedApplication.Name} at {location}");
            }

            var state = process is not null
                ? "Running"
                : container is not null
                    ? container.State
                    : service is not null
                        ? service.ActiveState
                        : "Installed";

            var kind = process is not null
                ? "Windows process"
                : container is not null
                    ? "Docker"
                    : service is not null
                        ? "Windows service"
                        : "Installed application";

            detected.Add(
                new IntegrationSnapshot(
                    definition.Name,
                    kind,
                    state,
                    string.Join(
                        " | ",
                        evidence.Take(5))));
        }

        return detected
            .OrderBy(item => item.Name)
            .ToArray();
    }

    private static string BuildDiscoveryPattern()
    {
        var tokens = IntegrationCatalog
            .SelectMany(definition => definition.Tokens)
            .Concat(AdditionalDiscoveryTokens)
            .Where(token => !string.IsNullOrWhiteSpace(token))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(Regex.Escape);

        return string.Join("|", tokens);
    }

    private static bool ContainsAnyToken(
        string value,
        IEnumerable<string> tokens) =>
        tokens.Any(token =>
            value.Contains(
                token,
                StringComparison.OrdinalIgnoreCase));

    private static string EscapePowerShellSingleQuoted(
        string value) =>
        value.Replace("'", "''", StringComparison.Ordinal);

    private static IEnumerable<string> SplitOutputLines(
        string output) =>
        output.Split(
                '\n',
                StringSplitOptions.RemoveEmptyEntries)
            .Select(line => line.TrimEnd('\r'))
            .Where(line => !string.IsNullOrWhiteSpace(line));
    private static string[] SplitColumns(
        string line) =>
        line.Replace(
                "`t",
                "\t",
                StringComparison.Ordinal)
            .Split('\t');

    private static string NormalizeRegistryPath(
        string value)
    {
        var normalized =
            value.Trim().Trim('"');

        var commaIndex =
            normalized.LastIndexOf(',');

        if (commaIndex > 1 &&
            int.TryParse(
                normalized[(commaIndex + 1)..],
                out _))
        {
            normalized =
                normalized[..commaIndex];
        }

        return normalized.Trim().Trim('"');
    }
    private static string NormalizeWindowsState(
        string state) =>
        state.Trim() switch
        {
            "Running" => "Running",
            "Stopped" => "Stopped",
            "Paused" => "Paused",
            "Start Pending" => "Starting",
            "Stop Pending" => "Stopping",
            var value when string.IsNullOrWhiteSpace(value) =>
                "Unknown",
            var value => value
        };

    private static string NormalizeListenerAddress(
        string address) =>
        address switch
        {
            "0.0.0.0" => "all IPv4",
            "::" => "all IPv6",
            _ => address
        };

    private static string FirstNonEmpty(
        params string[] values) =>
        values.FirstOrDefault(value =>
            !string.IsNullOrWhiteSpace(value)) ??
        "--";

    private static string ResolveDockerExecutable()
    {
        var candidates = new List<string>();

        var path = Environment.GetEnvironmentVariable("PATH");

        if (!string.IsNullOrWhiteSpace(path))
        {
            candidates.AddRange(
                path.Split(
                        Path.PathSeparator,
                        StringSplitOptions.RemoveEmptyEntries |
                        StringSplitOptions.TrimEntries)
                    .Select(directory =>
                        Path.Combine(directory, "docker.exe")));
        }

        var programFiles =
            Environment.GetFolderPath(
                Environment.SpecialFolder.ProgramFiles);

        var localAppData =
            Environment.GetFolderPath(
                Environment.SpecialFolder.LocalApplicationData);

        if (!string.IsNullOrWhiteSpace(programFiles))
        {
            candidates.Add(
                Path.Combine(
                    programFiles,
                    "Docker",
                    "Docker",
                    "resources",
                    "bin",
                    "docker.exe"));
        }

        if (!string.IsNullOrWhiteSpace(localAppData))
        {
            candidates.Add(
                Path.Combine(
                    localAppData,
                    "Docker",
                    "resources",
                    "bin",
                    "docker.exe"));
        }

        return candidates
            .Where(candidate =>
                !string.IsNullOrWhiteSpace(candidate))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault(File.Exists) ??
            string.Empty;
    }

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

    private static string ReadPhysicalMemorySummary(
        ICollection<string> warnings)
    {
        try
        {
            var status = new MemoryStatusEx
            {
                Length = (uint)Marshal.SizeOf<MemoryStatusEx>()
            };

            if (!GlobalMemoryStatusEx(ref status))
            {
                var error = Marshal.GetLastWin32Error();

                warnings.Add(
                    "Unable to read physical memory status. " +
                    $"Windows error: {error}");

                return "Unavailable";
            }

            var total = status.TotalPhysical;
            var available = status.AvailablePhysical;
            var used = total >= available
                ? total - available
                : 0;

            return
                $"{FormatBytes(used)} used / " +
                $"{FormatBytes(total)} total | " +
                $"{FormatBytes(available)} available " +
                $"({status.MemoryLoad}% used)";
        }
        catch (Exception exception)
        {
            warnings.Add(
                "Unable to read physical memory status: " +
                exception.Message);

            return "Unavailable";
        }
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
        ulong bytes)
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

        var display = (double)bytes;
        var unit = 0;

        while (display >= 1024d &&
               unit < units.Length - 1)
        {
            display /= 1024d;
            unit++;
        }

        return $"{display:0.##} {units[unit]}";
    }

    private static string FormatBytes(
        long bytes) =>
        FormatBytes((ulong)Math.Max(0, bytes));

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

    [DllImport(
        "kernel32.dll",
        SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GlobalMemoryStatusEx(
        ref MemoryStatusEx buffer);

    [StructLayout(
        LayoutKind.Sequential,
        CharSet = CharSet.Auto)]
    private struct MemoryStatusEx
    {
        public uint Length;
        public uint MemoryLoad;
        public ulong TotalPhysical;
        public ulong AvailablePhysical;
        public ulong TotalPageFile;
        public ulong AvailablePageFile;
        public ulong TotalVirtual;
        public ulong AvailableVirtual;
        public ulong AvailableExtendedVirtual;
    }

    private sealed record IntegrationDefinition(
        string Name,
        string[] Tokens,
        int[] DefaultPorts);

    private sealed record ProcessEvidence(
        string Name,
        int ProcessId,
        string ExecutablePath);

    private sealed record InstalledApplicationEvidence(
        string Name,
        string InstallLocation,
        string DisplayIcon);

    private sealed record ListenerEvidence(
        string Address,
        int Port,
        int OwningProcessId);
}
