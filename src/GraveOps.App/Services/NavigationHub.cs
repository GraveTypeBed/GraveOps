namespace GraveOps.App.Services;

public sealed class NavigationHub
{
    public event Action<string>? NavigationRequested;
    public void Request(string deepLink) => NavigationRequested?.Invoke(deepLink);
}