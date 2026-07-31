using GraveOps.App.Models;

namespace GraveOps.App.Services;

public sealed class GlobalContextService
{
    private readonly AppServices _services;
    public event Action<ServerProfile?>? TargetChanged;

    public GlobalContextService(AppServices services) => _services = services;

    public ServerProfile? Current => _services.Config.GetSelectedServer();

    public void Select(ServerProfile? server)
    {
        var old = _services.Config.Current.SelectedServerId;
        var next = server?.Id;
        if (old == next) return;

        _services.Config.Current.SelectedServerId = next;
        _services.Config.Save();
        TargetChanged?.Invoke(server);
    }
}