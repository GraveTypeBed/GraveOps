using GraveOps.Core.Hosts;
using GraveOps.Core.Providers;
using GraveOps.Core.Security;
using GraveOps.Core.Snapshots;
using GraveOps.Core.Targets;

namespace GraveOps.Platform.Windows;

public sealed class RemoteWindowsHostProvider :
    IHostProvider
{
    private static readonly HostProviderDescriptor ProviderDescriptor =
        new(
            HostProviderIds.RemoteWindows,
            "Remote Windows over WinRM HTTPS",
            new HashSet<TargetPlatform>
            {
                TargetPlatform.Windows
            },
            new HashSet<TargetLocation>
            {
                TargetLocation.Remote
            });

    private readonly IRemoteWindowsPowerShellExecutor _executor;

    public RemoteWindowsHostProvider(
        IRemoteWindowsPowerShellExecutor executor)
    {
        _executor =
            executor ??
            throw new ArgumentNullException(
                nameof(executor));
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
                TargetLocation.Remote &&
            target.ProviderId.Equals(
                HostProviderIds.RemoteWindows,
                StringComparison.OrdinalIgnoreCase) &&
            target.Connection.TransportId.Equals(
                TransportIds.WinRmHttps,
                StringComparison.OrdinalIgnoreCase);
    }

    public Task<HostProviderProbeResult> ProbeAsync(
        TargetProfile target,
        CancellationToken cancellationToken = default)
    {
        ValidateTarget(
            target);
        cancellationToken.ThrowIfCancellationRequested();

        var options =
            RemoteWindowsConnectionParser.Parse(
                target);

        var evidence =
            new List<string>
            {
                $"WinRM HTTPS endpoint configured on port {options.Port}",
                "System certificate trust and hostname validation required",
                "Credentials resolved from ICredentialVault at execution time",
                "Credential payload sent through process standard input",
                "Shared Windows CIM, registry, listener, Docker and event-log collector"
            };

        if (options.PinnedServerCertificateSha256 is not null)
        {
            evidence.Add(
                "Additional SHA-256 server-certificate pin configured");
        }

        return Task.FromResult(
            new HostProviderProbeResult(
                WindowsTargetCapabilityCatalog.ForRemoteTarget(),
                "Read-only remote Windows provider over WinRM HTTPS",
                evidence,
                Array.Empty<string>()));
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
                "The remote Windows refresh lease belongs to a different target.");
        }

        var runner =
            new RemoteWindowsPowerShellRunner(
                target,
                _executor);

        var snapshot =
            await new WindowsSnapshotCollector(
                    runner)
                .CaptureAsync(
                    cancellationToken);

        return new TargetSnapshotEnvelope<HostSnapshot>(
            refreshLease,
            Descriptor.Id,
            snapshot.CapturedAt,
            WindowsTargetCapabilityCatalog.ForRemoteTarget(),
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
                $"The remote Windows provider cannot handle target '{target.Id}'.");
        }

        _ =
            RemoteWindowsConnectionParser.Parse(
                target);
    }
}

public static class RemoteWindowsHostProviderFactory
{
    public static IHostProvider Create(
        ICredentialVault credentialVault,
        IWinRmPowerShellProcessInvoker? processInvoker = null,
        IRemoteWindowsCertificateValidator? certificateValidator = null) =>
        new RemoteWindowsHostProvider(
            new WinRmHttpsPowerShellExecutor(
                credentialVault,
                processInvoker,
                certificateValidator));
}
