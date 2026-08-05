using GraveOps.Core.Hosts;
using GraveOps.Core.Providers;
using GraveOps.Core.Snapshots;
using GraveOps.Core.Targets;

namespace GraveOps.Platform.Windows;

public sealed class LocalWindowsHostProbe :
    ILocalHostProbe
{
    private readonly WindowsSnapshotCollector _collector;

    public LocalWindowsHostProbe()
        : this(
            new WindowsSnapshotCollector(
                new LocalWindowsPowerShellRunner()))
    {
    }

    public LocalWindowsHostProbe(
        WindowsSnapshotCollector collector)
    {
        _collector =
            collector ??
            throw new ArgumentNullException(
                nameof(collector));
    }

    public Task<HostSnapshot> CaptureAsync(
        CancellationToken cancellationToken = default) =>
        _collector.CaptureAsync(
            cancellationToken);
}

public sealed class LocalWindowsHostProvider :
    IHostProvider
{
    private static readonly HostProviderDescriptor ProviderDescriptor =
        new(
            HostProviderIds.LocalWindows,
            "Local Windows",
            new HashSet<TargetPlatform>
            {
                TargetPlatform.Windows
            },
            new HashSet<TargetLocation>
            {
                TargetLocation.Local
            });

    private readonly IWindowsPowerShellRunner _runner;
    private readonly WindowsSnapshotCollector _collector;

    public LocalWindowsHostProvider()
        : this(
            new LocalWindowsPowerShellRunner())
    {
    }

    public LocalWindowsHostProvider(
        IWindowsPowerShellRunner runner)
    {
        _runner =
            runner ??
            throw new ArgumentNullException(
                nameof(runner));
        _collector =
            new WindowsSnapshotCollector(
                runner);
    }

    public HostProviderDescriptor Descriptor =>
        ProviderDescriptor;

    public bool CanHandle(
        TargetProfile target)
    {
        ArgumentNullException.ThrowIfNull(
            target);

        return target.Platform ==
                TargetPlatform.Windows &&
            target.Location ==
                TargetLocation.Local &&
            target.ProviderId.Equals(
                HostProviderIds.LocalWindows,
                StringComparison.OrdinalIgnoreCase) &&
            target.Connection.TransportId.Equals(
                TransportIds.Local,
                StringComparison.OrdinalIgnoreCase);
    }

    public Task<HostProviderProbeResult> ProbeAsync(
        TargetProfile target,
        CancellationToken cancellationToken = default)
    {
        ValidateTarget(
            target);
        cancellationToken.ThrowIfCancellationRequested();

        var warnings =
            _runner.IsWindowsTarget
                ? Array.Empty<string>()
                : new[]
                {
                    "The local Windows provider is registered, " +
                    "but native capture requires a Windows runtime."
                };

        return Task.FromResult(
            new HostProviderProbeResult(
                WindowsTargetCapabilityCatalog.ForLocalTarget(),
                "Read-only local Windows PowerShell/CIM provider",
                new[]
                {
                    "Encoded PowerShell command transport",
                    "CIM host, service, process and storage inventory",
                    "Uninstall-registry application inventory",
                    "TCP/UDP listener inventory",
                    "Docker CLI read-only inventory when available",
                    "System event-log warning and error summaries"
                },
                warnings));
    }

    public async Task<TargetSnapshotEnvelope<HostSnapshot>>
        CaptureAsync(
            TargetProfile target,
            TargetRefreshLease refreshLease,
            CancellationToken cancellationToken = default)
    {
        ValidateTarget(
            target);

        if (!refreshLease.TargetId.Equals(
                target.Id,
                StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                "The Windows refresh lease belongs to a different target.");
        }

        var capabilities =
            WindowsTargetCapabilityCatalog.ForLocalTarget();

        var snapshot =
            await _collector.CaptureAsync(
                cancellationToken);

        return new TargetSnapshotEnvelope<HostSnapshot>(
            refreshLease,
            Descriptor.Id,
            snapshot.CapturedAt,
            capabilities,
            snapshot);
    }

    private void ValidateTarget(
        TargetProfile target)
    {
        ArgumentNullException.ThrowIfNull(
            target);
        target.Validate();

        if (!CanHandle(
                target))
        {
            throw new NotSupportedException(
                $"The local Windows provider cannot handle target " +
                $"'{target.Id}'.");
        }
    }
}

public static class WindowsHostProviderFactory
{
    public static IHostProvider CreateLocal(
        IWindowsPowerShellRunner? runner = null) =>
        new LocalWindowsHostProvider(
            runner ??
            new LocalWindowsPowerShellRunner());
}
