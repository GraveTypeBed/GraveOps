using GraveOps.Core.Hosts;

namespace GraveOps.Platform.Linux;

public sealed class LocalLinuxHostProbe : ILocalHostProbe
{
    private readonly LinuxSnapshotCollector _collector;

    public LocalLinuxHostProbe()
        : this(
            new LinuxSnapshotCollector(
                new LocalLinuxCommandRunner()))
    {
    }

    public LocalLinuxHostProbe(
        ILinuxCommandRunner runner)
        : this(
            new LinuxSnapshotCollector(
                runner))
    {
    }

    public LocalLinuxHostProbe(
        LinuxSnapshotCollector collector)
    {
        _collector = collector ??
            throw new ArgumentNullException(
                nameof(collector));
    }

    public Task<HostSnapshot> CaptureAsync(
        CancellationToken cancellationToken = default) =>
        _collector.CaptureAsync(
            cancellationToken);
}
