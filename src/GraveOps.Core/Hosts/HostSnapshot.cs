namespace GraveOps.Core.Hosts;

public sealed record StorageVolumeSnapshot(
    string Source,
    string Size,
    string Used,
    string Available,
    string PercentUsed,
    string MountPoint);

public sealed record HostSnapshot(
    DateTimeOffset CapturedAt,
    string Hostname,
    string OperatingSystem,
    string Kernel,
    string Uptime,
    string SystemState,
    string DockerState,
    IReadOnlyList<StorageVolumeSnapshot> Storage,
    IReadOnlyList<string> Warnings);

public interface ILocalHostProbe
{
    Task<HostSnapshot> CaptureAsync(CancellationToken cancellationToken = default);
}