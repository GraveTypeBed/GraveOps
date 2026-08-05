namespace GraveOps.Core.Targets;

public static class CapabilityIds
{
    public const string HostSummaryRead = "host.summary.read";
    public const string StorageRead = "host.storage.read";
    public const string ServicesRead = "host.services.read";
    public const string ProcessesRead = "host.processes.read";
    public const string InstalledApplicationsRead =
        "host.applications.installed.read";
    public const string NetworkListenersRead =
        "host.network.listeners.read";
    public const string ContainersRead = "host.containers.read";
    public const string JournalRead = "host.logs.journal.read";
    public const string EventLogRead = "host.logs.eventlog.read";
    public const string ApplicationDiscovery = "applications.discover";
    public const string ApplicationApiTelemetry =
        "applications.api.telemetry.read";
    public const string BackupInventoryRead =
        "host.backups.read";
}

public sealed class TargetCapabilities
{
    private readonly HashSet<string> _values;

    public TargetCapabilities(IEnumerable<string>? values = null)
    {
        _values = new HashSet<string>(
            values ?? Array.Empty<string>(),
            StringComparer.OrdinalIgnoreCase);
    }

    public static TargetCapabilities Empty { get; } = new();

    public IReadOnlySet<string> Values => _values;

    public bool Supports(string capabilityId) =>
        !string.IsNullOrWhiteSpace(capabilityId) &&
        _values.Contains(capabilityId);

    public TargetCapabilities Union(
        IEnumerable<string> capabilityIds) =>
        new(_values.Concat(capabilityIds));
}
