using GraveOps.App.Models;

namespace GraveOps.App.Services;

/// <summary>
/// Builds an environment-wide snapshot on demand. This service owns fleet aggregation;
/// it reuses MediaOperationsService's per-host cache and does not add another timer.
/// </summary>
public sealed class EnvironmentOverviewService
{
    private readonly AppServices _services;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private EnvironmentOverviewSnapshot? _cache;
    private static readonly TimeSpan CacheLifetime = TimeSpan.FromSeconds(15);

    public EnvironmentOverviewService(AppServices services) => _services = services;

    public EnvironmentOverviewSnapshot? Current => _cache;
    public event Action<EnvironmentOverviewSnapshot>? Updated;

    public async Task<EnvironmentOverviewSnapshot> GetSnapshotAsync(
        bool force = false,
        CancellationToken cancellationToken = default)
    {
        if (!force &&
            _cache is { } cached &&
            DateTimeOffset.Now - cached.SampledAt < CacheLifetime)
            return cached;

        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (!force &&
                _cache is { } gateCached &&
                DateTimeOffset.Now - gateCached.SampledAt < CacheLifetime)
                return gateCached;

            var hosts = new List<EnvironmentHostSnapshot>();

            foreach (var server in _services.Config.Current.Servers)
            {
                cancellationToken.ThrowIfCancellationRequested();
                hosts.Add(await ProbeHostAsync(server, force, cancellationToken));
            }

            var overall = hosts.Count == 0
                ? EnvironmentHealthState.Unknown
                : hosts.Any(x => x.State == EnvironmentHealthState.Offline)
                    ? EnvironmentHealthState.Offline
                    : hosts.Any(x => x.State == EnvironmentHealthState.Attention)
                        ? EnvironmentHealthState.Attention
                        : hosts.All(x => x.State == EnvironmentHealthState.Healthy)
                            ? EnvironmentHealthState.Healthy
                            : EnvironmentHealthState.Unknown;

            _cache = new EnvironmentOverviewSnapshot
            {
                SampledAt = DateTimeOffset.Now,
                State = overall,
                Hosts = hosts
            };

            if (_services.Config.Current.Settings.EnableFleetHistory)
                _services.FleetHistory.RecordSnapshot(_cache);

            Updated?.Invoke(_cache);
            return _cache;
        }
        finally
        {
            _gate.Release();
        }
    }

    public void Invalidate() => _cache = null;

    private async Task<EnvironmentHostSnapshot> ProbeHostAsync(
        ServerProfile server,
        bool force,
        CancellationToken cancellationToken)
    {
        var verifiedApps = _services.Config.Current.Applications
            .Where(x => x.DiscoveryVerified && x.ServerId == server.Id)
            .OrderBy(x => x.Category)
            .ThenBy(x => x.Name)
            .ToArray();

        try
        {
            var provider = _services.Hosts.Resolve(server);
            var hostProbe = await provider.ProbeAsync(server, cancellationToken);
            MediaOperationsSnapshot? media = null;

            if (verifiedApps.Length > 0)
            {
                try
                {
                    media = await _services.MediaOps.GetSnapshotAsync(
                        server,
                        force,
                        cancellationToken);
                }
                catch
                {
                    // Host reachability and application reachability are separate signals.
                    // Preserve the reachable host and mark applications as attention below.
                }
            }

            var appSnapshots = new List<EnvironmentAppSnapshot>();
            foreach (var app in verifiedApps)
            {
                var card = media?.Apps.FirstOrDefault(x =>
                    x.Name.Equals(app.Name, StringComparison.OrdinalIgnoreCase));

                EnvironmentHealthState state;
                string appDetail;

                if (card is not null)
                {
                    state = MapAppState(card.Health);
                    appDetail = string.IsNullOrWhiteSpace(card.Detail)
                        ? StatePresentation.AppText(card.Health)
                        : card.Detail.Trim();
                }
                else
                {
                    try
                    {
                        var companion = await _services.IntegrationRuntime.ProbeAsync(
                            app.Name,
                            server,
                            app,
                            cancellationToken);
                        state = MapAppState(companion.Health);
                        appDetail = string.IsNullOrWhiteSpace(companion.Detail)
                            ? companion.StateText
                            : companion.Detail.Trim();
                    }
                    catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
                    {
                        state = EnvironmentHealthState.Attention;
                        appDetail = string.IsNullOrWhiteSpace(ex.Message)
                            ? "Verified integration; runtime probe unavailable"
                            : ex.Message;
                    }
                }

                appSnapshots.Add(new EnvironmentAppSnapshot
                {
                    AppId = app.Id,
                    Name = app.Name,
                    Category = app.Category,
                    State = state,
                    Detail = appDetail
                });
            }

            var hostState = appSnapshots.Any(x => x.State == EnvironmentHealthState.Offline)
                ? EnvironmentHealthState.Attention
                : appSnapshots.Any(x => x.State == EnvironmentHealthState.Attention)
                    ? EnvironmentHealthState.Attention
                    : EnvironmentHealthState.Healthy;

            var connectionText = server.ConnectionKind switch
            {
                HostConnectionKind.LocalWindows => "Local Windows",
                HostConnectionKind.RemoteWindows => "Remote Windows",
                HostConnectionKind.LocalLinux => "Local Linux",
                _ => "SSH"
            };

            var detail = appSnapshots.Count == 0
                ? $"Reachable | {hostProbe.StorageRoots.Count} storage root(s) | no verified applications"
                : hostState == EnvironmentHealthState.Healthy
                    ? $"Reachable | {appSnapshots.Count} verified app(s) healthy"
                    : $"Reachable | {appSnapshots.Count(x => x.State != EnvironmentHealthState.Healthy)} app(s) need attention";

            return new EnvironmentHostSnapshot
            {
                ServerId = server.Id,
                Name = server.Name,
                ConnectionKind = server.ConnectionKind,
                Platform = hostProbe.Platform,
                PlatformText = hostProbe.Platform switch
                {
                    HostPlatform.Windows => "Windows",
                    HostPlatform.Linux => "Linux",
                    _ => string.IsNullOrWhiteSpace(server.DetectedOperatingSystem)
                        ? "Unknown"
                        : server.DetectedOperatingSystem
                },
                ConnectionText = connectionText,
                State = hostState,
                Detail = detail,
                StorageRootCount = hostProbe.StorageRoots.Count,
                Apps = appSnapshots
            };
        }
        catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
        {
            var blockedApps = verifiedApps.Select(app => new EnvironmentAppSnapshot
            {
                AppId = app.Id,
                Name = app.Name,
                Category = app.Category,
                State = EnvironmentHealthState.Offline,
                Detail = "Blocked because the owning host is unreachable"
            }).ToList();

            return new EnvironmentHostSnapshot
            {
                ServerId = server.Id,
                Name = server.Name,
                ConnectionKind = server.ConnectionKind,
                Platform = PlatformFromConnection(server.ConnectionKind),
                PlatformText = PlatformFromConnection(server.ConnectionKind) == HostPlatform.Windows ? "Windows" : "Linux",
                ConnectionText = server.ConnectionKind == HostConnectionKind.LocalWindows ? "Local Windows" : "SSH",
                State = EnvironmentHealthState.Offline,
                Detail = string.IsNullOrWhiteSpace(ex.Message) ? "Host unavailable" : ex.Message,
                Apps = blockedApps
            };
        }
    }

    private static EnvironmentHealthState MapAppState(AppHealthState state) =>
        state switch
        {
            AppHealthState.Healthy => EnvironmentHealthState.Healthy,
            AppHealthState.Busy => EnvironmentHealthState.Healthy,
            AppHealthState.Offline => EnvironmentHealthState.Offline,
            AppHealthState.Degraded => EnvironmentHealthState.Attention,
            AppHealthState.Stale => EnvironmentHealthState.Attention,
            _ => EnvironmentHealthState.Attention
        };

    private static HostPlatform PlatformFromConnection(HostConnectionKind kind) =>
        kind is HostConnectionKind.LocalWindows or HostConnectionKind.RemoteWindows
            ? HostPlatform.Windows
            : HostPlatform.Linux;
}
