using GraveOps.Core.Hosts;
using GraveOps.Core.Providers;
using GraveOps.Core.Security;
using GraveOps.Core.Snapshots;
using GraveOps.Core.Targets;
using GraveOps.Platform.Linux;
using GraveOps.Platform.Windows;

namespace GraveOps.Desktop.Linux;

public static class DesktopHostProviderComposition
{
    public static IHostProviderRegistry Create(
        LocalLinuxHostProbe localLinuxProbe,
        LinuxCredentialStore credentials,
        string knownHostsDirectory)
    {
        ArgumentNullException.ThrowIfNull(
            localLinuxProbe);
        ArgumentNullException.ThrowIfNull(
            credentials);

        if (string.IsNullOrWhiteSpace(
                knownHostsDirectory))
        {
            throw new ArgumentException(
                "The known-hosts directory is required.",
                nameof(knownHostsDirectory));
        }

        return new HostProviderRegistry(
            new IHostProvider[]
            {
                new DesktopLocalLinuxHostProvider(
                    localLinuxProbe),
                new DesktopRemoteLinuxHostProvider(
                    credentials,
                    knownHostsDirectory),
                new LocalWindowsHostProvider(),
                RemoteWindowsHostProviderFactory.Create(
                    credentials)
            });
    }
}

public sealed class DesktopLocalLinuxHostProvider :
    IHostProvider
{
    private static readonly HostProviderDescriptor
        ProviderDescriptor =
            new(
                HostProviderIds.LocalLinux,
                "Local Linux",
                new HashSet<TargetPlatform>
                {
                    TargetPlatform.Linux
                },
                new HashSet<TargetLocation>
                {
                    TargetLocation.Local
                });

    private readonly LocalLinuxHostProbe _probe;

    public DesktopLocalLinuxHostProvider(
        LocalLinuxHostProbe probe)
    {
        _probe =
            probe ??
            throw new ArgumentNullException(
                nameof(probe));
    }

    public HostProviderDescriptor Descriptor =>
        ProviderDescriptor;

    public bool CanHandle(
        TargetProfile target)
    {
        ArgumentNullException.ThrowIfNull(
            target);

        return target.Platform ==
                TargetPlatform.Linux &&
            target.Location ==
                TargetLocation.Local &&
            target.ProviderId.Equals(
                HostProviderIds.LocalLinux,
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

        return Task.FromResult(
            new HostProviderProbeResult(
                LinuxTargetCapabilityCatalog.ForTarget(
                    isLocal: true),
                "Native local Linux provider",
                new[]
                {
                    "LocalLinuxCommandRunner",
                    "Shared LinuxSnapshotCollector",
                    "Local backup inventory"
                },
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
        ValidateLease(
            target,
            refreshLease);

        var snapshot =
            await _probe.CaptureAsync(
                cancellationToken);

        return new TargetSnapshotEnvelope<HostSnapshot>(
            refreshLease,
            Descriptor.Id,
            snapshot.CapturedAt,
            LinuxTargetCapabilityCatalog.ForTarget(
                isLocal: true),
            snapshot);
    }

    private void ValidateTarget(
        TargetProfile target)
    {
        target.Validate();

        if (!CanHandle(
                target))
        {
            throw new NotSupportedException(
                $"The local Linux provider cannot handle target '{target.Id}'.");
        }
    }

    private static void ValidateLease(
        TargetProfile target,
        TargetRefreshLease refreshLease)
    {
        if (!refreshLease.TargetId.Equals(
                target.Id,
                StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                "The local Linux refresh lease belongs to a different target.");
        }
    }
}

public sealed class DesktopRemoteLinuxHostProvider :
    IHostProvider
{
    private static readonly HostProviderDescriptor
        ProviderDescriptor =
            new(
                HostProviderIds.RemoteLinuxSsh,
                "Remote Linux over SSH",
                new HashSet<TargetPlatform>
                {
                    TargetPlatform.Linux
                },
                new HashSet<TargetLocation>
                {
                    TargetLocation.Remote
                });

    private readonly LinuxCredentialStore _credentials;
    private readonly string _knownHostsDirectory;

    public DesktopRemoteLinuxHostProvider(
        LinuxCredentialStore credentials,
        string knownHostsDirectory)
    {
        _credentials =
            credentials ??
            throw new ArgumentNullException(
                nameof(credentials));
        _knownHostsDirectory =
            string.IsNullOrWhiteSpace(
                knownHostsDirectory)
                ? throw new ArgumentException(
                    "The known-hosts directory is required.",
                    nameof(knownHostsDirectory))
                : knownHostsDirectory;
    }

    public HostProviderDescriptor Descriptor =>
        ProviderDescriptor;

    public bool CanHandle(
        TargetProfile target)
    {
        ArgumentNullException.ThrowIfNull(
            target);

        return target.Platform ==
                TargetPlatform.Linux &&
            target.Location ==
                TargetLocation.Remote &&
            target.ProviderId.Equals(
                HostProviderIds.RemoteLinuxSsh,
                StringComparison.OrdinalIgnoreCase) &&
            target.Connection.TransportId.Equals(
                TransportIds.Ssh,
                StringComparison.OrdinalIgnoreCase);
    }

    public Task<HostProviderProbeResult> ProbeAsync(
        TargetProfile target,
        CancellationToken cancellationToken = default)
    {
        ValidateTarget(
            target);
        cancellationToken.ThrowIfCancellationRequested();

        return Task.FromResult(
            new HostProviderProbeResult(
                LinuxTargetCapabilityCatalog.ForTarget(
                    isLocal: false),
                "Fingerprint-pinned remote Linux SSH provider",
                new[]
                {
                    "Per-target known-host file",
                    "SSH agent, private-key or keyring-backed password authentication",
                    "Shared LinuxSnapshotCollector"
                },
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
                "The remote Linux refresh lease belongs to a different target.");
        }

        var profile =
            LinuxHostProfile.FromTargetProfile(
                target);

        var snapshot =
            await new RemoteLinuxHostProbe(
                    profile,
                    _credentials,
                    _knownHostsDirectory)
                .CaptureAsync(
                    cancellationToken);

        return new TargetSnapshotEnvelope<HostSnapshot>(
            refreshLease,
            Descriptor.Id,
            snapshot.CapturedAt,
            LinuxTargetCapabilityCatalog.ForTarget(
                isLocal: false),
            snapshot);
    }

    private void ValidateTarget(
        TargetProfile target)
    {
        target.Validate();

        if (!CanHandle(
                target))
        {
            throw new NotSupportedException(
                $"The remote Linux provider cannot handle target '{target.Id}'.");
        }
    }
}

internal sealed class TransientCredentialVault :
    ICredentialVault
{
    private readonly ICredentialVault _fallback;
    private readonly CredentialReference _reference;
    private readonly string _secret;

    public TransientCredentialVault(
        ICredentialVault fallback,
        CredentialReference reference,
        string secret)
    {
        _fallback =
            fallback ??
            throw new ArgumentNullException(
                nameof(fallback));
        _reference =
            reference;
        _secret =
            secret ??
            throw new ArgumentNullException(
                nameof(secret));
    }

    public string VaultId =>
        "transient-test-credential";

    public bool IsAvailable =>
        true;

    public Task StoreAsync(
        CredentialReference reference,
        SecretValue secret,
        CancellationToken cancellationToken = default) =>
        _fallback.StoreAsync(
            reference,
            secret,
            cancellationToken);

    public Task<SecretValue?> RetrieveAsync(
        CredentialReference reference,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (reference.Equals(
                _reference))
        {
            return Task.FromResult<
                SecretValue?>(
                new SecretValue(
                    _secret));
        }

        return _fallback.RetrieveAsync(
            reference,
            cancellationToken);
    }

    public Task DeleteAsync(
        CredentialReference reference,
        CancellationToken cancellationToken = default) =>
        _fallback.DeleteAsync(
            reference,
            cancellationToken);
}
