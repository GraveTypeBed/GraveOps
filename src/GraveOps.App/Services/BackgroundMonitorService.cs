using System.Windows.Forms;
using GraveOps.App.Models;

namespace GraveOps.App.Services;

public sealed class BackgroundMonitorService : IDisposable
{
    private readonly AppServices _services;
    private readonly NotificationService _notifications;
    private CancellationTokenSource? _cts;
    private string _lastState = "";

    public event Action<string, bool>? StateChanged;
    public DateTimeOffset? LastCheckUtc { get; private set; }
    public string LastState { get; private set; } = "WAITING";

    public BackgroundMonitorService(AppServices services, NotificationService notifications)
    {
        _services = services;
        _notifications = notifications;
    }

    public void Start()
    {
        if (_cts is not null) return;
        _cts = new CancellationTokenSource();
        _ = Task.Run(() => LoopAsync(_cts.Token));
    }

    private async Task LoopAsync(CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            var profile = _services.Context.Current ?? _services.Config.GetSelectedServer();
            if (profile is not null)
                await MonitorHostAsync(profile, token);

            var seconds = Math.Max(30, _services.Config.Current.Settings.MonitorIntervalSeconds);
            try { await Task.Delay(TimeSpan.FromSeconds(seconds), token); }
            catch (OperationCanceledException) { }
        }
    }

    private async Task MonitorHostAsync(ServerProfile profile, CancellationToken token)
    {
        try
        {
            // The background monitor is deliberately provider-native. It does not depend
            // on a server-side GraveOps helper, a particular Docker stack, or fixed paths.
            var provider = _services.Hosts.Resolve(profile);
            var probe = await provider.ProbeAsync(profile, token);
            var detail = $"{probe.HostName} | {probe.OperatingSystem} | {probe.StorageRoots.Count} storage root(s)";

            LastCheckUtc = DateTimeOffset.UtcNow;
            LastState = "HEALTHY";
            StateChanged?.Invoke("HEALTHY", true);
            StateTransition(profile, "HEALTHY", detail, false);
        }
        catch (Exception ex) when (!token.IsCancellationRequested)
        {
            LastCheckUtc = DateTimeOffset.UtcNow;
            LastState = "OFFLINE";
            StateChanged?.Invoke("OFFLINE", false);
            StateTransition(profile, "OFFLINE", ex.Message, true);
        }
    }

    private void StateTransition(ServerProfile profile, string state, string detail, bool bad)
    {
        if (!string.IsNullOrEmpty(_lastState) && state != _lastState)
        {
            _services.Activity.Record(
                bad ? "Host monitor alert" : "Host monitor recovered",
                bad ? detail : $"{profile.Name} returned to a reachable provider state.",
                bad ? ActivityLevel.Warning : ActivityLevel.Success,
                serverId: profile.Id,
                deepLink: bad ? "page:Servers" : "page:Dashboard");

            var settings = _services.Config.Current.Settings;
            if (settings.EnableDesktopNotifications && !settings.MaintenanceMode)
                _notifications.Show(
                    bad ? "GraveOps host alert" : "GraveOps host recovered",
                    bad ? detail : $"{profile.Name} returned to a reachable provider state.",
                    bad ? ToolTipIcon.Warning : ToolTipIcon.Info,
                    bad ? "page:Servers" : "page:Dashboard");
        }
        _lastState = state;
    }

    public void Dispose()
    {
        _cts?.Cancel();
        _cts?.Dispose();
        _cts = null;
    }
}
