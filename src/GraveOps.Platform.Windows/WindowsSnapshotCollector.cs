using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using GraveOps.Core.Applications;
using GraveOps.Core.Hosts;

namespace GraveOps.Platform.Windows;

public sealed class WindowsSnapshotCollector
{
    private static readonly Regex SensitiveAssignment =
        new(
            @"(?i)\b(password|passphrase|token|api[_ -]?key|authorization)\b\s*[:=]\s*\S+",
            RegexOptions.Compiled |
            RegexOptions.CultureInvariant);

    private static readonly Regex SensitiveArgument =
        new(
            @"(?i)(--?(?:password|passphrase|token|api[-_]?key|authorization))\s+\S+",
            RegexOptions.Compiled |
            RegexOptions.CultureInvariant);

    private static readonly Regex BearerCredential =
        new(
            @"(?i)\bbearer\s+[A-Za-z0-9._~+/=-]+",
            RegexOptions.Compiled |
            RegexOptions.CultureInvariant);

    private static readonly JsonSerializerOptions JsonOptions =
        new()
        {
            PropertyNameCaseInsensitive =
                true,
            NumberHandling =
                JsonNumberHandling.AllowReadingFromString
        };

    private readonly IWindowsPowerShellRunner _runner;

    public WindowsSnapshotCollector(
        IWindowsPowerShellRunner runner)
    {
        _runner =
            runner ??
            throw new ArgumentNullException(
                nameof(runner));
    }

    public async Task<HostSnapshot> CaptureAsync(
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var warnings =
            new List<string>();

        if (!_runner.IsWindowsTarget)
        {
            warnings.Add(
                "The local Windows provider requires a Windows runtime.");

            return EmptySnapshot(
                warnings);
        }

        var result =
            await _runner.ExecuteAsync(
                new WindowsPowerShellRequest(
                    WindowsInventoryPowerShell.Script,
                    "Windows host inventory"),
                cancellationToken);

        if (result.TimedOut)
        {
            warnings.Add(
                "Windows host inventory timed out.");

            return EmptySnapshot(
                warnings);
        }

        if (!string.IsNullOrWhiteSpace(
                result.FailureMessage))
        {
            warnings.Add(
                $"Windows host inventory: " +
                $"{SanitizeDiagnostic(result.FailureMessage)}");
        }

        if (!string.IsNullOrWhiteSpace(
                result.StandardError))
        {
            warnings.Add(
                $"Windows host inventory: " +
                $"{SanitizeDiagnostic(result.StandardError)}");
        }

        if (result.ExitCode != 0)
        {
            warnings.Add(
                $"Windows host inventory exited with code " +
                $"{result.ExitCode}.");
        }

        if (string.IsNullOrWhiteSpace(
                result.StandardOutput))
        {
            return EmptySnapshot(
                warnings);
        }

        WindowsInventoryDocument? document;

        try
        {
            document =
                JsonSerializer.Deserialize<
                    WindowsInventoryDocument>(
                    result.StandardOutput,
                    JsonOptions);
        }
        catch (Exception exception)
        {
            warnings.Add(
                $"Windows inventory JSON could not be parsed: " +
                $"{exception.Message}");

            return EmptySnapshot(
                warnings);
        }

        if (document is null)
        {
            warnings.Add(
                "Windows inventory JSON was empty.");

            return EmptySnapshot(
                warnings);
        }

        var storage =
            document.Storage
                .Where(item =>
                    !string.IsNullOrWhiteSpace(
                        item.DeviceId))
                .Select(
                    CreateStorageSnapshot)
                .ToArray();

        var services =
            document.Services
                .Where(item =>
                    !string.IsNullOrWhiteSpace(
                        item.Name))
                .Select(item =>
                    new ServiceSnapshot(
                        item.Name.Trim(),
                        ValueOrFallback(
                            item.DisplayName,
                            item.Name.Trim()),
                        item.State.Equals(
                            "Running",
                            StringComparison.OrdinalIgnoreCase)
                            ? "active"
                            : "inactive",
                        ValueOrFallback(
                            item.State,
                            "unknown")
                            .ToLowerInvariant(),
                        ValueOrFallback(
                            item.StartMode,
                            "unknown")))
                .ToArray();

        var containers =
            document.Containers
                .Where(item =>
                    !string.IsNullOrWhiteSpace(
                        item.Name))
                .Select(item =>
                    new DockerContainerSnapshot(
                        item.Name.Trim(),
                        ValueOrFallback(
                            item.Image,
                            "--"),
                        ValueOrFallback(
                            item.State,
                            "unknown")
                            .ToLowerInvariant(),
                        ValueOrFallback(
                            item.Status,
                            "--"),
                        ValueOrFallback(
                            item.Ports,
                            "--")))
                .ToArray();

        var processes =
            document.Processes
                .Where(item =>
                    item.ProcessId > 0 &&
                    !string.IsNullOrWhiteSpace(
                        item.Name))
                .Select(item =>
                    new ProcessSnapshot(
                        item.ProcessId,
                        item.Name.Trim(),
                        ValueOrFallback(
                            item.ExecutablePath,
                            "--"),
                        FormatBytes(
                            item.WorkingSetSize),
                        FormatCpuTime(
                            item.KernelModeTime +
                            item.UserModeTime)))
                .ToArray();

        var installedApplications =
            document.InstalledApplications
                .Where(item =>
                    !string.IsNullOrWhiteSpace(
                        item.Name))
                .Select(item =>
                    new InstalledApplicationSnapshot(
                        item.Name.Trim(),
                        ValueOrFallback(
                            item.Version,
                            "--"),
                        ValueOrFallback(
                            item.Publisher,
                            "--"),
                        ValueOrFallback(
                            item.InstallLocation,
                            "--"),
                        ValueOrFallback(
                            item.Source,
                            "Windows uninstall registry")))
                .ToArray();

        var listeners =
            document.NetworkListeners
                .Where(item =>
                    item.LocalPort is > 0 and <= 65535)
                .Select(item =>
                    new NetworkListenerSnapshot(
                        ValueOrFallback(
                            item.Protocol,
                            "TCP"),
                        ValueOrFallback(
                            item.LocalAddress,
                            "*"),
                        item.LocalPort,
                        item.OwningProcess,
                        ValueOrFallback(
                            item.ProcessName,
                            "--")))
                .ToArray();

        var integrations =
            DetectIntegrations(
                document);

        var recentLogs =
            document.Events
                .Select(
                    FormatEvent)
                .Where(line =>
                    !string.IsNullOrWhiteSpace(
                        line))
                .ToArray();

        var failedServices =
            document.FailedServices
                .Where(value =>
                    !string.IsNullOrWhiteSpace(
                        value))
                .Select(value =>
                    value.Trim())
                .Distinct(
                    StringComparer.OrdinalIgnoreCase)
                .ToArray();

        var dockerState =
            string.IsNullOrWhiteSpace(
                document.DockerVersion)
                ? "Unavailable or not running"
                : $"Docker {document.DockerVersion.Trim()} · " +
                  $"{containers.Count(container =>
                      container.State.Equals(
                          "running",
                          StringComparison.OrdinalIgnoreCase))} running";

        var snapshot =
            new HostSnapshot(
                DateTimeOffset.UtcNow,
                ValueOrFallback(
                    document.Hostname,
                    _runner.MachineNameFallback),
                ValueOrFallback(
                    document.OperatingSystem,
                    "Windows"),
                ValueOrFallback(
                    document.Kernel,
                    "--"),
                ValueOrFallback(
                    document.Uptime,
                    "--"),
                ValueOrFallback(
                    document.SystemState,
                    "Unknown"),
                dockerState,
                ValueOrFallback(
                    document.CpuModel,
                    "--"),
                FormatCpuLoad(
                    document.CpuLoadPercent,
                    document.LogicalProcessorCount),
                FormatMemory(
                    document.TotalMemoryKilobytes,
                    document.FreeMemoryKilobytes),
                document.IpAddresses.Count == 0
                    ? "No address reported"
                    : string.Join(
                        " ",
                        document.IpAddresses
                            .Where(value =>
                                !string.IsNullOrWhiteSpace(
                                    value))
                            .Distinct(
                                StringComparer.OrdinalIgnoreCase)),
                storage,
                services,
                containers,
                integrations,
                failedServices,
                recentLogs,
                warnings
                    .Where(value =>
                        !string.IsNullOrWhiteSpace(
                            value))
                    .Distinct()
                    .ToArray())
            {
                Processes =
                    processes,
                InstalledApplications =
                    installedApplications,
                NetworkListeners =
                    listeners
            };

        return snapshot;
    }

    private HostSnapshot EmptySnapshot(
        IReadOnlyList<string> warnings) =>
        new(
            DateTimeOffset.UtcNow,
            _runner.MachineNameFallback,
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

    private static StorageVolumeSnapshot CreateStorageSnapshot(
        WindowsStorageRecord item)
    {
        var size =
            Math.Max(
                0,
                item.Size);
        var available =
            Math.Clamp(
                item.FreeSpace,
                0,
                size);
        var used =
            Math.Max(
                0,
                size -
                available);
        var percent =
            size <= 0
                ? 0
                : used /
                  (double)size *
                  100d;

        var label =
            string.IsNullOrWhiteSpace(
                item.VolumeName)
                ? item.DeviceId.Trim()
                : $"{item.DeviceId.Trim()} " +
                  $"({item.VolumeName.Trim()})";

        return new StorageVolumeSnapshot(
            label,
            ValueOrFallback(
                item.FileSystem,
                "--"),
            FormatBytes(
                size),
            FormatBytes(
                used),
            FormatBytes(
                available),
            $"{percent:0.#}%",
            item.DeviceId.Trim());
    }

    private static IReadOnlyList<IntegrationSnapshot>
        DetectIntegrations(
            WindowsInventoryDocument document)
    {
        var evidence =
            new List<WindowsDiscoveryEvidence>();

        evidence.AddRange(
            document.Services
                .Where(item =>
                    !string.IsNullOrWhiteSpace(
                        item.Name))
                .Select(item =>
                    new WindowsDiscoveryEvidence(
                        $"{item.Name} {item.DisplayName}",
                        "Windows service",
                        item.State.Equals(
                            "Running",
                            StringComparison.OrdinalIgnoreCase)
                            ? "Running"
                            : ValueOrFallback(
                                item.State,
                                "Stopped"),
                        $"{item.Name} · " +
                        $"{item.DisplayName} · " +
                        $"{SanitizeDiagnostic(item.PathName)}")));

        evidence.AddRange(
            document.Processes
                .Where(item =>
                    !string.IsNullOrWhiteSpace(
                        item.Name))
                .Select(item =>
                    new WindowsDiscoveryEvidence(
                        item.Name,
                        "Native process",
                        "Running",
                        $"{item.Name} · " +
                        $"{item.ExecutablePath}")));

        evidence.AddRange(
            document.InstalledApplications
                .Where(item =>
                    !string.IsNullOrWhiteSpace(
                        item.Name))
                .Select(item =>
                    new WindowsDiscoveryEvidence(
                        item.Name,
                        "Installed application",
                        "Installed",
                        $"{item.Name} · " +
                        $"{item.Publisher} · " +
                        $"{item.InstallLocation}")));

        evidence.AddRange(
            document.Containers
                .Where(item =>
                    !string.IsNullOrWhiteSpace(
                        item.Name) ||
                    !string.IsNullOrWhiteSpace(
                        item.Image))
                .Select(item =>
                    new WindowsDiscoveryEvidence(
                        $"{item.Name} {item.Image}",
                        "Docker container",
                        item.State.Equals(
                            "running",
                            StringComparison.OrdinalIgnoreCase)
                            ? "Running"
                            : ValueOrFallback(
                                item.State,
                                "Stopped"),
                        $"{item.Name} · " +
                        $"{item.Image} · " +
                        $"{item.Status}")));

        var classified =
            evidence
                .Select(item =>
                {
                    var classification =
                        ApplicationIdentityClassifier.Classify(
                            new ApplicationIdentityEvidence(
                                string.Empty,
                                ApplicationIdentityRoles.NativeApplication,
                                item.Kind,
                                item.Kind,
                                item.SourceName,
                                item.Evidence,
                                HasManagementEndpoint: false,
                                IsVerified: true));

                    return new
                    {
                        Evidence =
                            item,
                        Classification =
                            classification
                    };
                })
                .Where(item =>
                    ApplicationIdentityCatalog.Find(
                        item.Classification.ProductId)
                    is not null)
                .GroupBy(
                    item =>
                        item.Classification.ProductId,
                    StringComparer.OrdinalIgnoreCase)
                .OrderBy(group =>
                    group.Key,
                    StringComparer.OrdinalIgnoreCase)
                .Select(group =>
                {
                    var items =
                        group.ToArray();

                    var primary =
                        items
                            .OrderByDescending(item =>
                                DiscoveryRank(
                                    item.Evidence))
                            .ThenBy(item =>
                                item.Evidence.SourceName,
                                StringComparer.OrdinalIgnoreCase)
                            .First();

                    var state =
                        items.Any(item =>
                            item.Evidence.State.Equals(
                                "Running",
                                StringComparison.OrdinalIgnoreCase))
                            ? "Running"
                            : items.Any(item =>
                                item.Evidence.State.Equals(
                                    "Installed",
                                    StringComparison.OrdinalIgnoreCase))
                                ? "Installed"
                                : primary.Evidence.State;

                    var kinds =
                        items
                            .Select(item =>
                                item.Evidence.Kind)
                            .Distinct(
                                StringComparer.OrdinalIgnoreCase)
                            .OrderBy(value =>
                                value,
                                StringComparer.OrdinalIgnoreCase)
                            .ToArray();

                    var proof =
                        items
                            .OrderByDescending(item =>
                                DiscoveryRank(
                                    item.Evidence))
                            .Select(item =>
                                $"{item.Evidence.Kind}: " +
                                $"{item.Evidence.SourceName}")
                            .Distinct(
                                StringComparer.OrdinalIgnoreCase)
                            .Take(4)
                            .ToArray();

                    return new IntegrationSnapshot(
                        group.Key,
                        string.Join(
                            " + ",
                            kinds),
                        state,
                        string.Join(
                            " · ",
                            proof));
                })
                .ToArray();

        return classified;
    }

    private static int DiscoveryRank(
        WindowsDiscoveryEvidence evidence)
    {
        var stateRank =
            evidence.State.Equals(
                "Running",
                StringComparison.OrdinalIgnoreCase)
                ? 100
                : evidence.State.Equals(
                    "Installed",
                    StringComparison.OrdinalIgnoreCase)
                    ? 20
                    : 0;

        var kindRank =
            evidence.Kind switch
            {
                "Windows service" =>
                    40,
                "Docker container" =>
                    35,
                "Native process" =>
                    30,
                "Installed application" =>
                    10,
                _ =>
                    0
            };

        return stateRank +
            kindRank;
    }

    private static string FormatEvent(
        WindowsEventRecord item)
    {
        if (string.IsNullOrWhiteSpace(
                item.Message) &&
            string.IsNullOrWhiteSpace(
                item.Provider))
        {
            return string.Empty;
        }

        var timestamp =
            DateTimeOffset.TryParse(
                item.TimeCreated,
                out var parsed)
                ? parsed.ToLocalTime()
                    .ToString("yyyy-MM-dd HH:mm:ss")
                : "--";

        return $"{timestamp} · " +
               $"{ValueOrFallback(item.Level, "Event")} · " +
               $"{ValueOrFallback(item.Provider, "Windows")} " +
               $"{item.Id}: " +
               $"{SanitizeDiagnostic(
                   ValueOrFallback(
                       item.Message,
                       "--"))}";
    }

    private static string SanitizeDiagnostic(
        string? value)
    {
        if (string.IsNullOrWhiteSpace(
                value))
        {
            return string.Empty;
        }

        var sanitized =
            SensitiveAssignment.Replace(
                value.Trim(),
                "$1=[REDACTED]");

        sanitized =
            SensitiveArgument.Replace(
                sanitized,
                "$1 [REDACTED]");

        return BearerCredential.Replace(
            sanitized,
            "Bearer [REDACTED]");
    }

    private static string FormatCpuLoad(
        double? percent,
        int logicalProcessorCount)
    {
        var processors =
            Math.Max(
                1,
                logicalProcessorCount);

        return percent is { } value
            ? $"{Math.Clamp(value, 0, 100):0.#}% · " +
              $"{processors} logical CPUs"
            : $"-- · {processors} logical CPUs";
    }

    private static string FormatMemory(
        long totalKilobytes,
        long freeKilobytes)
    {
        if (totalKilobytes <= 0)
            return "--";

        var available =
            Math.Clamp(
                freeKilobytes,
                0,
                totalKilobytes);
        var used =
            Math.Max(
                0,
                totalKilobytes -
                available);

        return $"{FormatKilobytes(used)} used · " +
               $"{FormatKilobytes(available)} available · " +
               $"{FormatKilobytes(totalKilobytes)} total";
    }

    private static string FormatKilobytes(
        long kilobytes) =>
        $"{kilobytes / 1024d / 1024d:0.0} GiB";

    private static string FormatCpuTime(
        long hundredNanoseconds)
    {
        if (hundredNanoseconds <= 0)
            return "0s";

        var seconds =
            hundredNanoseconds /
            10_000_000d;

        return seconds >= 3600
            ? $"{seconds / 3600d:0.0}h"
            : seconds >= 60
                ? $"{seconds / 60d:0.0}m"
                : $"{seconds:0.#}s";
    }

    private static string FormatBytes(
        long bytes)
    {
        var value =
            Math.Max(
                0,
                bytes);

        if (value >= 1024L * 1024L * 1024L * 1024L)
        {
            return $"{value /
                (1024d * 1024d * 1024d * 1024d):0.0} TiB";
        }

        if (value >= 1024L * 1024L * 1024L)
        {
            return $"{value /
                (1024d * 1024d * 1024d):0.0} GiB";
        }

        if (value >= 1024L * 1024L)
        {
            return $"{value /
                (1024d * 1024d):0.0} MiB";
        }

        if (value >= 1024L)
        {
            return $"{value / 1024d:0.0} KiB";
        }

        return $"{value} B";
    }

    private static string ValueOrFallback(
        string? value,
        string fallback) =>
        string.IsNullOrWhiteSpace(
            value)
            ? fallback
            : value.Trim();

    private sealed record WindowsDiscoveryEvidence(
        string SourceName,
        string Kind,
        string State,
        string Evidence);

    private sealed class WindowsInventoryDocument
    {
        public string Hostname { get; set; } =
            string.Empty;
        public string OperatingSystem { get; set; } =
            string.Empty;
        public string Kernel { get; set; } =
            string.Empty;
        public string Uptime { get; set; } =
            string.Empty;
        public string SystemState { get; set; } =
            string.Empty;
        public string CpuModel { get; set; } =
            string.Empty;
        public double? CpuLoadPercent { get; set; }
        public int LogicalProcessorCount { get; set; }
        public long TotalMemoryKilobytes { get; set; }
        public long FreeMemoryKilobytes { get; set; }
        public List<string> IpAddresses { get; set; } =
            new();
        public string DockerVersion { get; set; } =
            string.Empty;
        public List<WindowsStorageRecord> Storage { get; set; } =
            new();
        public List<WindowsServiceRecord> Services { get; set; } =
            new();
        public List<WindowsProcessRecord> Processes { get; set; } =
            new();
        public List<WindowsInstalledApplicationRecord>
            InstalledApplications { get; set; } =
                new();
        public List<WindowsNetworkListenerRecord>
            NetworkListeners { get; set; } =
                new();
        public List<WindowsContainerRecord> Containers { get; set; } =
            new();
        public List<string> FailedServices { get; set; } =
            new();
        public List<WindowsEventRecord> Events { get; set; } =
            new();
    }

    private sealed class WindowsStorageRecord
    {
        public string DeviceId { get; set; } =
            string.Empty;
        public string VolumeName { get; set; } =
            string.Empty;
        public string FileSystem { get; set; } =
            string.Empty;
        public long Size { get; set; }
        public long FreeSpace { get; set; }
    }

    private sealed class WindowsServiceRecord
    {
        public string Name { get; set; } =
            string.Empty;
        public string DisplayName { get; set; } =
            string.Empty;
        public string State { get; set; } =
            string.Empty;
        public string StartMode { get; set; } =
            string.Empty;
        public string PathName { get; set; } =
            string.Empty;
    }

    private sealed class WindowsProcessRecord
    {
        public int ProcessId { get; set; }
        public string Name { get; set; } =
            string.Empty;
        public string ExecutablePath { get; set; } =
            string.Empty;
        public long WorkingSetSize { get; set; }
        public long KernelModeTime { get; set; }
        public long UserModeTime { get; set; }
    }

    private sealed class WindowsInstalledApplicationRecord
    {
        public string Name { get; set; } =
            string.Empty;
        public string Version { get; set; } =
            string.Empty;
        public string Publisher { get; set; } =
            string.Empty;
        public string InstallLocation { get; set; } =
            string.Empty;
        public string Source { get; set; } =
            string.Empty;
    }

    private sealed class WindowsNetworkListenerRecord
    {
        public string Protocol { get; set; } =
            string.Empty;
        public string LocalAddress { get; set; } =
            string.Empty;
        public int LocalPort { get; set; }
        public int OwningProcess { get; set; }
        public string ProcessName { get; set; } =
            string.Empty;
    }

    private sealed class WindowsContainerRecord
    {
        public string Name { get; set; } =
            string.Empty;
        public string Image { get; set; } =
            string.Empty;
        public string State { get; set; } =
            string.Empty;
        public string Status { get; set; } =
            string.Empty;
        public string Ports { get; set; } =
            string.Empty;
    }

    private sealed class WindowsEventRecord
    {
        public string TimeCreated { get; set; } =
            string.Empty;
        public int Id { get; set; }
        public string Provider { get; set; } =
            string.Empty;
        public string Level { get; set; } =
            string.Empty;
        public string Message { get; set; } =
            string.Empty;
    }
}
