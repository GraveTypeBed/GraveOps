using System.Windows;
using GraveOps.App.Services;

namespace GraveOps.App;

public partial class App : Application
{
    private void App_Startup(object sender, StartupEventArgs e)
    {
        EventManager.RegisterClassHandler(
            typeof(Window),
            FrameworkElement.LoadedEvent,
            new RoutedEventHandler((instance, _) =>
            {
                if (instance is Window window)
                    GraveOps.App.Services.WindowThemeService.Apply(window);
            }));
    }

    public static AppServices Services { get; private set; } = null!;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        Services = new AppServices();
        Services.Initialize();
    }
}
