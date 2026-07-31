using GraveOps.App.Models;

namespace GraveOps.App.Services.Hosts;

public sealed class HostProviderRegistry
{
    private readonly IReadOnlyList<IHostProvider> _providers;

    public HostProviderRegistry(IEnumerable<IHostProvider> providers) =>
        _providers = providers.ToArray();

    public IReadOnlyList<IHostProvider> Providers => _providers;

    public bool TryResolve(ServerProfile profile, out IHostProvider provider)
    {
        provider = _providers.FirstOrDefault(x => x.CanHandle(profile))!;
        return provider is not null;
    }

    public IHostProvider Resolve(ServerProfile profile) =>
        TryResolve(profile, out var provider)
            ? provider
            : throw new NotSupportedException(
                $"No GraveOps host provider is registered for {profile.ConnectionKind}.");
}
