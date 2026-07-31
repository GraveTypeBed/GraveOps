using GraveOps.App.Models;

namespace GraveOps.App.Services.Hosts;

public interface IHostProvider
{
    HostConnectionKind Kind { get; }
    bool CanHandle(ServerProfile profile);
    Task<HostProbeResult> ProbeAsync(
        ServerProfile profile,
        CancellationToken cancellationToken = default);
}
