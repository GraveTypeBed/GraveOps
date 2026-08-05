using GraveOps.Core.Hosts;
using GraveOps.Core.Snapshots;
using GraveOps.Core.Targets;

namespace GraveOps.Core.Providers;

public sealed record HostProviderDescriptor(
    string Id,
    string DisplayName,
    IReadOnlySet<TargetPlatform> Platforms,
    IReadOnlySet<TargetLocation> Locations);

public sealed record HostProviderProbeResult(
    TargetCapabilities Capabilities,
    string Summary,
    IReadOnlyList<string> Evidence,
    IReadOnlyList<string> Warnings);

public interface IHostProvider
{
    HostProviderDescriptor Descriptor { get; }

    bool CanHandle(TargetProfile target);

    Task<HostProviderProbeResult> ProbeAsync(
        TargetProfile target,
        CancellationToken cancellationToken = default);

    Task<TargetSnapshotEnvelope<HostSnapshot>> CaptureAsync(
        TargetProfile target,
        TargetRefreshLease refreshLease,
        CancellationToken cancellationToken = default);
}

public interface IHostProviderRegistry
{
    IReadOnlyList<IHostProvider> Providers { get; }

    bool TryResolve(
        TargetProfile target,
        out IHostProvider? provider);

    IHostProvider Resolve(TargetProfile target);
}

public sealed class HostProviderRegistry : IHostProviderRegistry
{
    private readonly IReadOnlyList<IHostProvider> _providers;

    public HostProviderRegistry(IEnumerable<IHostProvider> providers)
    {
        ArgumentNullException.ThrowIfNull(providers);

        _providers = providers.ToArray();

        var duplicate = _providers
            .GroupBy(
                provider => provider.Descriptor.Id,
                StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault(group => group.Count() > 1);

        if (duplicate is not null)
        {
            throw new InvalidOperationException(
                $"Host provider ID '{duplicate.Key}' is registered more than once.");
        }
    }

    public IReadOnlyList<IHostProvider> Providers => _providers;

    public bool TryResolve(
        TargetProfile target,
        out IHostProvider? provider)
    {
        ArgumentNullException.ThrowIfNull(target);

        provider = _providers.FirstOrDefault(candidate =>
            candidate.CanHandle(target));

        return provider is not null;
    }

    public IHostProvider Resolve(TargetProfile target) =>
        TryResolve(target, out var provider)
            ? provider!
            : throw new NotSupportedException(
                $"No GraveOps host provider is registered for target " +
                $"'{target.Id}' using provider '{target.ProviderId}'.");
}
