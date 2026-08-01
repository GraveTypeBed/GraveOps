namespace GraveOps.Core.Hosts;

public sealed record StorageVolumeSnapshot(
    string Source,
    string FileSystem,
    string Size,
    string Used,
    string Available,
    string PercentUsed,
    string MountPoint);

public sealed record ServiceSnapshot(
    string Unit,
    string Description,
    string ActiveState,
    string SubState,
    string UnitFileState);

public sealed record DockerContainerSnapshot(
    string Name,
    string Image,
    string State,
    string Status,
    string Ports);

public sealed record IntegrationSnapshot(
    string Name,
    string Kind,
    string State,
    string Evidence);

public sealed record HostSnapshot(
    DateTimeOffset CapturedAt,
    string Hostname,
    string OperatingSystem,
    string Kernel,
    string Uptime,
    string SystemState,
    string DockerState,
    string CpuModel,
    string LoadAverage,
    string MemorySummary,
    string IpAddresses,
    IReadOnlyList<StorageVolumeSnapshot> Storage,
    IReadOnlyList<ServiceSnapshot> Services,
    IReadOnlyList<DockerContainerSnapshot> Containers,
    IReadOnlyList<IntegrationSnapshot> Integrations,
    IReadOnlyList<string> FailedUnits,
    IReadOnlyList<string> RecentLogs,
    IReadOnlyList<string> Warnings);

public interface ILocalHostProbe
{
    Task<HostSnapshot> CaptureAsync(
        CancellationToken cancellationToken = default);
}
