using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text.Json;
using GraveOps.Core.Hosts;

namespace GraveOps.Platform.Linux;

public sealed class LocalLinuxHostProbe : ILocalHostProbe
{
    private static readonly SemaphoreSlim
        ProcessGate =
            new(4, 4);
    private static readonly TimeSpan
        ProcessTimeout =
            TimeSpan.FromSeconds(15);

    private static readonly string
        JournalCachePath =
            ResolveJournalCachePath();

    private bool _journalCacheLoaded;
    private string _journalBootId =
        string.Empty;
    private string _journalCursor =
        string.Empty;
    private List<string> _journalLines =
        new();

    private sealed class JournalCacheDocument
    {
        public string BootId { get; set; } =
            string.Empty;
        public string Cursor { get; set; } =
            string.Empty;
        public List<string> Lines { get; set; } =
            new();
    }

    private static string ResolveJournalCachePath()
    {
        var root =
            Environment.GetEnvironmentVariable(
                "XDG_CACHE_HOME");
        if (string.IsNullOrWhiteSpace(root))
        {
            root = Path.Combine(
                Environment.GetFolderPath(
                    Environment.SpecialFolder.UserProfile),
                ".cache");
        }

        return Path.Combine(
            root,
            "GraveOps",
            "journal-cursor-cache.json");
    }
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
        "mullvad-daemon.service",
        "sonarr.service",
        "radarr.service",
        "lidarr.service",
        "prowlarr.service",
        "readarr.service",
        "whisparr.service",
        "bazarr.service",
        "mylar.service",
        "mylar3.service",
        "recyclarr.service",
        "cleanuparr.service",
        "maintainerr.service",
        "profilarr.service",
        "unpackerr.service",
        "autobrr.service"
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
        ("Configarr", new[] { "configarr" }),
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
        var warnings =
            new ConcurrentBag<string>();

        if (!OperatingSystem.IsLinux())
        {
            warnings.Add(
                "The native Linux provider requires a Linux runtime.");
            return EmptySnapshot(
                warnings.ToArray());
        }

        var hostnameTask = RunTextAsync(
            "hostname",
            Array.Empty<string>(),
            cancellationToken,
            warnings,
            "hostname");
        var kernelTask = RunTextAsync(
            "uname",
            new[] { "-r" },
            cancellationToken,
            warnings,
            "kernel");
        var uptimeTask = RunTextAsync(
            "uptime",
            new[] { "-p" },
            cancellationToken,
            warnings,
            "uptime");
        var systemStateTask = RunTextAsync(
            "systemctl",
            new[] { "is-system-running" },
            cancellationToken,
            warnings,
            "systemd state",
            allowNonZeroExit: true,
            warnOnNonZeroExit: false);
        var dockerVersionTask = RunTextAsync(
            "docker",
            new[] { "version", "--format", "{{.Server.Version}}" },
            cancellationToken,
            warnings,
            "Docker",
            allowNonZeroExit: true,
            warnOnNonZeroExit: false);
        var ipAddressesTask = RunTextAsync(
            "hostname",
            new[] { "-I" },
            cancellationToken,
            warnings,
            "IP addresses",
            allowNonZeroExit: true,
            warnOnNonZeroExit: false);
        var storageTask = CaptureStorageAsync(
            cancellationToken,
            warnings);
        var servicesTask = CaptureServicesAsync(
            cancellationToken,
            warnings);
        var containersTask = CaptureContainersAsync(
            cancellationToken,
            warnings);
        var failedUnitsTask = CaptureFailedUnitsAsync(
            cancellationToken,
            warnings);
        var logsTask = CaptureLogsAsync(
            cancellationToken,
            warnings);

        await Task.WhenAll(
            hostnameTask,
            kernelTask,
            uptimeTask,
            systemStateTask,
            dockerVersionTask,
            ipAddressesTask,
            storageTask,
            servicesTask,
            containersTask,
            failedUnitsTask,
            logsTask);

        var hostname = await hostnameTask;
        var kernel = await kernelTask;
        var uptime = await uptimeTask;
        var systemState = await systemStateTask;
        var dockerVersion = await dockerVersionTask;
        var ipAddresses = await ipAddressesTask;
        var storage = await storageTask;
        var services = await servicesTask;
        var containers = await containersTask;
        var failedUnits = await failedUnitsTask;
        var logs = await logsTask;

        var integrations = DetectIntegrations(
            services,
            containers);
        var cpuModel = ReadCpuModel(
            warnings);
        var loadAverage = ReadLoadAverage(
            warnings);
        var memorySummary = ReadMemorySummary(
            warnings);
        var operatingSystem = ReadOsRelease(
            warnings);

        var dockerState =
            string.IsNullOrWhiteSpace(
                dockerVersion)
                ? "Unavailable or not running"
                : $"Docker {dockerVersion.Trim()} · " +
                  $"{containers.Count(container =>
                      container.State.Equals(
                          "running",
                          StringComparison.OrdinalIgnoreCase))} running";

        return new HostSnapshot(
            DateTimeOffset.UtcNow,
            ValueOrFallback(
                hostname,
                Environment.MachineName),
            operatingSystem,
            ValueOrFallback(
                kernel,
                "--"),
            ValueOrFallback(
                uptime,
                "--"),
            ValueOrFallback(
                systemState,
                "Unknown"),
            dockerState,
            cpuModel,
            loadAverage,
            memorySummary,
            ValueOrFallback(
                ipAddresses,
                "No address reported"),
            storage,
            services,
            containers,
            integrations,
            failedUnits,
            logs,
            warnings
                .Distinct()
                .ToArray());
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
        ConcurrentBag<string> warnings)
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
        ConcurrentBag<string> warnings)
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
        ConcurrentBag<string> warnings)
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
        ConcurrentBag<string> warnings)
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
            ConcurrentBag<string> warnings)
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
            ConcurrentBag<string> warnings)
    {
        var discoveredOutput = await RunTextAsync(
            "systemctl",
            new[]
            {
                "list-unit-files",
                "--type=service",
                "--no-legend",
                "--no-pager"
            },
            cancellationToken,
            warnings,
            "service discovery",
            allowNonZeroExit: true,
            warnOnNonZeroExit: false);

        var discoveredUnits = discoveredOutput
            .Split(
                '\n',
                StringSplitOptions.RemoveEmptyEntries)
            .Select(line => line.Split(
                (char[]?)null,
                StringSplitOptions.RemoveEmptyEntries)
                .FirstOrDefault())
            .Where(unit =>
                !string.IsNullOrWhiteSpace(unit) &&
                IntegrationCatalog.Any(entry =>
                    entry.Tokens.Any(token =>
                        unit!.Contains(
                            token,
                            StringComparison.OrdinalIgnoreCase))))
            .Cast<string>();

        var units =
            KnownServiceUnits
                .Concat(
                    discoveredUnits)
                .Distinct(
                    StringComparer.OrdinalIgnoreCase)
                .OrderBy(unit =>
                    unit,
                    StringComparer.OrdinalIgnoreCase)
                .ToArray();

        if (units.Length == 0)
            return Array.Empty<ServiceSnapshot>();

        var arguments =
            new List<string>
            {
                "show"
            };
        arguments.AddRange(
            units);
        arguments.AddRange(
            new[]
            {
                "--no-pager",
                "--property=Id",
                "--property=Description",
                "--property=LoadState",
                "--property=ActiveState",
                "--property=SubState",
                "--property=UnitFileState"
            });

        var output = await RunTextAsync(
            "systemctl",
            arguments,
            cancellationToken,
            warnings,
            "service states",
            allowNonZeroExit: true,
            warnOnNonZeroExit: false);

        var rows =
            new List<ServiceSnapshot>();
        var blocks =
            output
                .Replace(
                    "\r\n",
                    "\n",
                    StringComparison.Ordinal)
                .Split(
                    "\n\n",
                    StringSplitOptions.RemoveEmptyEntries);

        foreach (var block in blocks)
        {
            var values = block
                .Split(
                    '\n',
                    StringSplitOptions.RemoveEmptyEntries)
                .Select(line =>
                    line.Split('=', 2))
                .Where(parts =>
                    parts.Length == 2)
                .GroupBy(
                    parts => parts[0],
                    StringComparer.OrdinalIgnoreCase)
                .ToDictionary(
                    group => group.Key,
                    group => group.Last()[1],
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

            var id =
                Value(
                    values,
                    "Id",
                    string.Empty);
            if (string.IsNullOrWhiteSpace(
                    id))
            {
                continue;
            }

            rows.Add(
                new ServiceSnapshot(
                    id,
                    Value(
                        values,
                        "Description",
                        id),
                    Value(
                        values,
                        "ActiveState",
                        "unknown"),
                    Value(
                        values,
                        "SubState",
                        "unknown"),
                    Value(
                        values,
                        "UnitFileState",
                        "unknown")));
        }

        return rows
            .OrderBy(service =>
                service.Unit)
            .ToArray();
    }

    private static async Task<IReadOnlyList<DockerContainerSnapshot>>
        CaptureContainersAsync(
            CancellationToken cancellationToken,
            ConcurrentBag<string> warnings)
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
            ConcurrentBag<string> warnings)
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

    private async Task<IReadOnlyList<string>>
        CaptureLogsAsync(
            CancellationToken cancellationToken,
            ConcurrentBag<string> warnings)
    {
        EnsureJournalCacheLoaded();

        var bootId =
            ReadBootId();
        if (!_journalBootId.Equals(
                bootId,
                StringComparison.Ordinal))
        {
            _journalBootId = bootId;
            _journalCursor =
                string.Empty;
            _journalLines.Clear();
        }

        var arguments =
            new List<string>
            {
                "-p",
                "warning",
                "--no-pager",
                "--output=short-iso",
                "--show-cursor"
            };

        if (string.IsNullOrWhiteSpace(
                _journalCursor))
        {
            arguments.Add("-n");
            arguments.Add("80");
        }
        else
        {
            arguments.Add("--after-cursor");
            arguments.Add(_journalCursor);
        }

        var output = await RunTextAsync(
            "journalctl",
            arguments,
            cancellationToken,
            warnings,
            "journal warnings",
            allowNonZeroExit: true,
            warnOnNonZeroExit: false);

        var newLines =
            output
                .Split(
                    '\n',
                    StringSplitOptions.RemoveEmptyEntries)
                .Select(line =>
                    line.TrimEnd())
                .ToArray();

        var cursorLine =
            newLines.LastOrDefault(line =>
                line.StartsWith(
                    "-- cursor:",
                    StringComparison.Ordinal));

        if (!string.IsNullOrWhiteSpace(
                cursorLine))
        {
            _journalCursor =
                cursorLine["-- cursor:".Length..]
                    .Trim();
        }

        foreach (var line in newLines.Where(line =>
                     !line.StartsWith(
                         "-- cursor:",
                         StringComparison.Ordinal) &&
                     !line.Equals(
                         "-- No entries --",
                         StringComparison.OrdinalIgnoreCase)))
        {
            _journalLines.Add(
                line);
        }

        if (_journalLines.Count > 240)
        {
            _journalLines.RemoveRange(
                0,
                _journalLines.Count - 240);
        }

        SaveJournalCache();

        return _journalLines.Count == 0
            ? new[]
            {
                "No warning-or-higher journal entries were returned."
            }
            : _journalLines.ToArray();
    }

    private void EnsureJournalCacheLoaded()
    {
        if (_journalCacheLoaded)
            return;

        _journalCacheLoaded = true;

        try
        {
            if (!File.Exists(
                    JournalCachePath))
            {
                return;
            }

            var document =
                JsonSerializer.Deserialize<JournalCacheDocument>(
                    File.ReadAllText(
                        JournalCachePath));

            if (document is null)
                return;

            _journalBootId =
                document.BootId ??
                string.Empty;
            _journalCursor =
                document.Cursor ??
                string.Empty;
            _journalLines =
                document.Lines?
                    .TakeLast(240)
                    .ToList() ??
                new List<string>();
        }
        catch
        {
            _journalBootId =
                string.Empty;
            _journalCursor =
                string.Empty;
            _journalLines.Clear();
        }
    }

    private void SaveJournalCache()
    {
        try
        {
            var directory =
                Path.GetDirectoryName(
                    JournalCachePath)!;
            Directory.CreateDirectory(
                directory);
            var temporary =
                JournalCachePath +
                ".tmp";
            File.WriteAllText(
                temporary,
                JsonSerializer.Serialize(
                    new JournalCacheDocument
                    {
                        BootId = _journalBootId,
                        Cursor = _journalCursor,
                        Lines = _journalLines
                    }));
            File.Move(
                temporary,
                JournalCachePath,
                overwrite: true);
        }
        catch
        {
            // Journal caching is an optimization, never a capture dependency.
        }
    }

    private static string ReadBootId()
    {
        try
        {
            const string path =
                "/proc/sys/kernel/random/boot_id";
            return File.Exists(path)
                ? File.ReadAllText(path).Trim()
                : string.Empty;
        }
        catch
        {
            return string.Empty;
        }
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
        ConcurrentBag<string> warnings,
        string operationName,
        bool allowNonZeroExit = false,
        bool warnOnNonZeroExit = true)
    {
        var entered = false;
        Process? process = null;
        using var timeout =
            CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken);
        timeout.CancelAfter(
            ProcessTimeout);

        try
        {
            await ProcessGate.WaitAsync(
                timeout.Token);
            entered = true;

            process =
                new Process
                {
                    StartInfo =
                        new ProcessStartInfo
                        {
                            FileName = executable,
                            RedirectStandardOutput = true,
                            RedirectStandardError = true,
                            UseShellExecute = false,
                            CreateNoWindow = true
                        }
                };

            foreach (var argument in arguments)
                process.StartInfo.ArgumentList.Add(
                    argument);

            process.Start();

            var stdoutTask =
                process.StandardOutput.ReadToEndAsync(
                    timeout.Token);
            var stderrTask =
                process.StandardError.ReadToEndAsync(
                    timeout.Token);

            await process.WaitForExitAsync(
                timeout.Token);

            var stdout =
                (await stdoutTask).Trim();
            var stderr =
                (await stderrTask).Trim();

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
                     !string.IsNullOrWhiteSpace(
                         stderr))
            {
                warnings.Add(
                    $"{operationName}: {stderr}");
            }

            return stdout;
        }
        catch (OperationCanceledException)
            when (cancellationToken.IsCancellationRequested)
        {
            TryKill(
                process);
            throw;
        }
        catch (OperationCanceledException)
        {
            TryKill(
                process);
            warnings.Add(
                $"{operationName} timed out after " +
                $"{ProcessTimeout.TotalSeconds:0} seconds.");
            return string.Empty;
        }
        catch (Exception exception)
        {
            TryKill(
                process);
            warnings.Add(
                $"Unable to query {operationName} using " +
                $"{executable}: {exception.Message}");
            return string.Empty;
        }
        finally
        {
            process?.Dispose();

            if (entered)
                ProcessGate.Release();
        }
    }

    private static void TryKill(
        Process? process)
    {
        try
        {
            if (process is not null &&
                !process.HasExited)
            {
                process.Kill(
                    entireProcessTree: true);
            }
        }
        catch
        {
            // Best-effort timeout cleanup.
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
