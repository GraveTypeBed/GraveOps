using System.Diagnostics;
using System.Windows;
using MessageBox = GraveOps.App.Windows.GraveOpsMessageBox;
using System.Windows.Controls;
using GraveOps.App.Models;

namespace GraveOps.App.Views;

public partial class ApplicationsView : UserControl
{
    private ManagedApp? _editing;
    private readonly ObservableCollection<AppHealthCard> _ops = new();
    private readonly ObservableCollection<AppHealthCard> _arr = new();
    private readonly ObservableCollection<AppHealthCard> _downloads = new();
    private Services.AppServices S => App.Services;

    public ApplicationsView()
    {
        InitializeComponent();
        Loaded += ApplicationsLive_Loaded;
        Unloaded += ApplicationsLive_Unloaded;
        ServerBox.ItemsSource = S.Config.Current.Servers;
        OpsCards.ItemsSource = _ops;
        ArrCards.ItemsSource = _arr;
        DownloadCards.ItemsSource = _downloads;
        ReloadLaunchers();
        var savedTab = S.Config.Current.Settings.LastApplicationsTabIndex;
        ModeTabs.SelectedIndex = savedTab == 4 ? 4 : 0;
    }

    private void ModeTabs_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!IsLoaded || !ReferenceEquals(e.Source, ModeTabs)) return;
        var index = Math.Max(0, ModeTabs.SelectedIndex);
        if (S.Config.Current.Settings.LastApplicationsTabIndex == index) return;
        S.Config.Current.Settings.LastApplicationsTabIndex = index;
        S.Config.Save();
    }
    private void ReloadLaunchers()
    {
        AppsGrid.ItemsSource = null;
        AppsGrid.ItemsSource = S.Config.Current.Applications;
    }

    private async Task RefreshSharedTelemetryAsync(
        bool force)
    {
        var live =
            Services.LiveAnalyticsHub.Current;

        if (force)
            await live.ForceAsync("Applications");

        if (live.MediaSnapshot is { } snapshot)
            BindSnapshot(snapshot);

        SampleAgeText.Text =
            live.MediaUpdatedAt is { } updated
                ? $"LIVE - updated {updated.ToLocalTime():HH:mm:ss}"
                : "Live sample pending";
    }
    private void BindSnapshot(MediaOperationsSnapshot snapshot)
    {
        _ops.Clear(); _arr.Clear(); _downloads.Clear();
        var hidden = S.Config.Current.Settings.HiddenApplicationCards;
        var order = S.Config.Current.Settings.ApplicationCardOrder;

        var orderedCards = snapshot.Apps
            .Where(x => !hidden.Any(h => h.Equals(x.Name, StringComparison.OrdinalIgnoreCase)))
            .OrderBy(x =>
            {
                var index = order.FindIndex(n => n.Equals(x.Name, StringComparison.OrdinalIgnoreCase));
                return index < 0 ? int.MaxValue : index;
            })
            .ThenBy(x => x.Category)
            .ThenBy(x => x.Name);

        foreach (var card in orderedCards)
        {
            _ops.Add(card);
            if (card.Category.Equals("Arr", StringComparison.OrdinalIgnoreCase)) _arr.Add(card);
            if (card.Category.Equals("Downloads", StringComparison.OrdinalIgnoreCase)) _downloads.Add(card);
        }

        HealthyCountText.Text = snapshot.HealthyCount.ToString();
        DegradedCountText.Text = snapshot.DegradedCount.ToString();
        OfflineCountText.Text = snapshot.OfflineCount.ToString();
        PlexServiceText.Text = snapshot.Plex.ServiceState;
        PlexVersionText.Text = snapshot.Plex.Version;
        PlexEndpointText.Text = snapshot.Plex.EndpointState;
        PlexLatencyText.Text = snapshot.Plex.LatencyMs > 0 ? $"{snapshot.Plex.LatencyMs} ms response" : "No response";
        PlexDumbText.Text = snapshot.Plex.DockerDependency.Replace("|", " / ", StringComparison.Ordinal);
        SabStateText.Text = snapshot.Sab.State;
        SabSpeedText.Text = snapshot.Sab.Speed;
        SabQueueText.Text = snapshot.Sab.QueueCount.ToString();
        SabRemainingText.Text = snapshot.Sab.Remaining;
        SabDetailText.Text = snapshot.Sab.Detail;
        QbitStateText.Text = snapshot.Qbit.State;
        QbitDownText.Text = snapshot.Qbit.DownloadSpeed;
        QbitUpText.Text = snapshot.Qbit.UploadSpeed;
        QbitDetailText.Text = snapshot.Qbit.Detail;
    }

    private async void Refresh_Click(object sender, RoutedEventArgs e) => await RefreshSharedTelemetryAsync(true);

    private ManagedApp? ResolveApp(AppHealthCard card)
        => S.Config.Current.Applications.FirstOrDefault(x => x.Id == card.AppId) ?? S.Config.Current.Applications.FirstOrDefault(x => x.Name.Equals(card.Name, StringComparison.OrdinalIgnoreCase));

    private void OpenManagedApp(ManagedApp? app)
    {
        if (app is null) return;
        var server = app.ServerId is { } id ? S.Config.Current.Servers.FirstOrDefault(x => x.Id == id) : S.Context.Current;
        if (server is null) return;
        var resolved = app.Url.Replace("{host}", server.Host, StringComparison.OrdinalIgnoreCase);
        if (app.OpenEmbedded) new EmbeddedBrowserWindow(app.Name, resolved).Show();
        else Process.Start(new ProcessStartInfo(resolved) { UseShellExecute = true });
        S.Activity.Record($"Opened {app.Name}", resolved, ActivityLevel.Info, serverId: server.Id, deepLink: $"app:{app.Name}");
    }

    private void CardOpen_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is AppHealthCard card) OpenManagedApp(ResolveApp(card));
    }
    private void AppOpenMenu_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is AppHealthCard card) OpenManagedApp(ResolveApp(card));
    }
    private void AppTerminalMenu_Click(object sender, RoutedEventArgs e) => S.Navigation.Request("page:Terminal");
    private void AppLogsMenu_Click(object sender, RoutedEventArgs e) => S.Navigation.Request("page:Logs");

    private void AppCopyUrlMenu_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is not AppHealthCard card) return;
        var app = ResolveApp(card);
        if (app is null) return;

        var server = app.ServerId is { } id
            ? S.Config.Current.Servers.FirstOrDefault(x => x.Id == id)
            : S.Context.Current;

        if (server is null) return;

        var resolved = app.Url.Replace("{host}", server.Host, StringComparison.OrdinalIgnoreCase);
        System.Windows.Clipboard.SetText(resolved);
        S.Activity.Record($"Copied {app.Name} URL", resolved, ActivityLevel.Info, serverId: server.Id, deepLink: $"app:{app.Name}");
    }

    private async void AppRefreshMenu_Click(object sender, RoutedEventArgs e)
        => await RefreshSharedTelemetryAsync(true);

    private async void AppMoveFirstMenu_Click(object sender, RoutedEventArgs e)
        => await MoveApplicationCardAsync((sender as FrameworkElement)?.Tag as AppHealthCard, int.MinValue);

    private async void AppMoveUpMenu_Click(object sender, RoutedEventArgs e)
        => await MoveApplicationCardAsync((sender as FrameworkElement)?.Tag as AppHealthCard, -1);

    private async void AppMoveDownMenu_Click(object sender, RoutedEventArgs e)
        => await MoveApplicationCardAsync((sender as FrameworkElement)?.Tag as AppHealthCard, 1);

    private async void AppMoveLastMenu_Click(object sender, RoutedEventArgs e)
        => await MoveApplicationCardAsync((sender as FrameworkElement)?.Tag as AppHealthCard, int.MaxValue);

    private async void AppHideMenu_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is not AppHealthCard card) return;

        var hidden = S.Config.Current.Settings.HiddenApplicationCards;
        if (!hidden.Any(x => x.Equals(card.Name, StringComparison.OrdinalIgnoreCase)))
            hidden.Add(card.Name);

        S.Config.Save();
        S.Activity.Record("Application card hidden", card.Name, ActivityLevel.Info, deepLink: "page:Applications");
        await RefreshSharedTelemetryAsync(false);
    }

    private async void ShowAllCards_Click(object sender, RoutedEventArgs e)
    {
        if (S.Config.Current.Settings.HiddenApplicationCards.Count == 0) return;

        S.Config.Current.Settings.HiddenApplicationCards.Clear();
        S.Config.Save();
        S.Activity.Record("Application cards restored", "All hidden application cards are visible again.", ActivityLevel.Info, deepLink: "page:Applications");
        await RefreshSharedTelemetryAsync(false);
    }

    private async Task MoveApplicationCardAsync(AppHealthCard? card, int delta)
    {
        if (card is null) return;

        var settings = S.Config.Current.Settings;
        var order = settings.ApplicationCardOrder;

        foreach (var name in _ops.Select(x => x.Name))
        {
            if (!order.Any(x => x.Equals(name, StringComparison.OrdinalIgnoreCase)))
                order.Add(name);
        }

        var current = order.FindIndex(x => x.Equals(card.Name, StringComparison.OrdinalIgnoreCase));
        if (current < 0)
        {
            order.Add(card.Name);
            current = order.Count - 1;
        }

        int target;
        if (delta == int.MinValue) target = 0;
        else if (delta == int.MaxValue) target = order.Count - 1;
        else target = Math.Clamp(current + delta, 0, order.Count - 1);

        if (target != current)
        {
            var name = order[current];
            order.RemoveAt(current);
            order.Insert(target, name);
            S.Config.Save();
            S.Activity.Record("Application card moved", $"{card.Name}: position {target + 1}", ActivityLevel.Info, deepLink: "page:Applications");
        }

        await RefreshSharedTelemetryAsync(false);
    }
    private async void AppRestartMenu_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is not AppHealthCard card) return;
        var actionName = card.Name.Equals("Plex", StringComparison.OrdinalIgnoreCase) ? "Restart Plex" : null;
        if (actionName is null)
        {
            MessageBox.Show("This application does not currently expose a protected GraveOps restart action.", "Restart unavailable", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        await RunProtectedActionAsync(actionName);
    }

    private async Task RunProtectedActionAsync(string actionName)
    {
        var action = S.Config.Current.Actions.FirstOrDefault(x => x.Name.Equals(actionName, StringComparison.OrdinalIgnoreCase));
        var server = S.Context.Current;
        if (action is null || server is null) return;
        if (MessageBox.Show($"Run '{actionName}' on {server.Name}?", "Confirm action", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
        SampleAgeText.Text = $"Running {actionName}...";
        var result = await S.ActionRunner.RunAsync(action, server);
        if (!result.Success)
            MessageBox.Show(string.IsNullOrWhiteSpace(result.Error) ? result.Verification : result.Error, $"{actionName} failed", MessageBoxButton.OK, MessageBoxImage.Error);
        S.MediaOps.Invalidate(server.Id);
        await RefreshSharedTelemetryAsync(true);
    }

    private void OpenPlex_Click(object sender, RoutedEventArgs e) => OpenByName("Plex");
    private async void RestartPlex_Click(object sender, RoutedEventArgs e) => await RunProtectedActionAsync("Restart Plex");
    private void PlexLogs_Click(object sender, RoutedEventArgs e) => S.Navigation.Request("page:Logs");
    private void Terminal_Click(object sender, RoutedEventArgs e) => S.Navigation.Request("page:Terminal");
    private void OpenSab_Click(object sender, RoutedEventArgs e) => OpenByName("SABnzbd");
    private void OpenQbit_Click(object sender, RoutedEventArgs e) => OpenByName("qBittorrent");
    private void OpenByName(string name) => OpenManagedApp(S.Config.Current.Applications.FirstOrDefault(x => x.Name.Equals(name, StringComparison.OrdinalIgnoreCase)));

    private void AppsGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (AppsGrid.SelectedItem is not ManagedApp a) return;
        _editing = a;
        NameBox.Text = a.Name; UrlBox.Text = a.Url; CategoryBox.Text = a.Category; EmbeddedCheck.IsChecked = a.OpenEmbedded;
        ServerBox.SelectedItem = a.ServerId is { } id ? S.Config.Current.Servers.FirstOrDefault(x => x.Id == id) : S.Context.Current;
    }

    private void Add_Click(object sender, RoutedEventArgs e)
    {
        _editing = null; AppsGrid.SelectedItem = null; NameBox.Text = ""; UrlBox.Text = "http://"; CategoryBox.Text = "Media"; EmbeddedCheck.IsChecked = true; ServerBox.SelectedItem = S.Context.Current;
    }

    private async void Save_Click(object sender, RoutedEventArgs e)
    {
        var a = _editing ?? new ManagedApp();
        a.Name = NameBox.Text.Trim(); a.Url = UrlBox.Text.Trim(); a.Category = CategoryBox.Text.Trim(); a.OpenEmbedded = EmbeddedCheck.IsChecked == true; a.ServerId = (ServerBox.SelectedItem as ServerProfile)?.Id;
        a.DiscoveryVerified = true;
        a.DiscoveryEvidence = "user-configured application";
        a.DiscoveredUtc = DateTimeOffset.UtcNow;
        var validateUrl = a.Url.Replace("{host}", "127.0.0.1", StringComparison.OrdinalIgnoreCase);
        if (!Uri.TryCreate(validateUrl, UriKind.Absolute, out _)) { StatusText.Text = "Enter a valid absolute URL or use {host} as the server placeholder."; return; }
        if (_editing is null) S.Config.Current.Applications.Add(a);
        _editing = a; S.Config.Save(); ReloadLaunchers(); AppsGrid.SelectedItem = a; StatusText.Text = "Saved.";
        if (a.ServerId is { } serverId) S.MediaOps.Invalidate(serverId);
        await RefreshSharedTelemetryAsync(true);
    }

    private async void Delete_Click(object sender, RoutedEventArgs e)
    {
        if (AppsGrid.SelectedItem is not ManagedApp a) return;
        if (MessageBox.Show($"Delete launcher '{a.Name}'?", "Delete", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
        S.Config.Current.Applications.RemoveAll(x => x.Id == a.Id); S.Config.Save(); _editing = null; ReloadLaunchers();
        if (a.ServerId is { } serverId) S.MediaOps.Invalidate(serverId);
        await RefreshSharedTelemetryAsync(true);
    }

    private void Open_Click(object sender, RoutedEventArgs e) => OpenManagedApp(AppsGrid.SelectedItem as ManagedApp ?? _editing);

    private void QueueDrilldown_Click(object sender, RoutedEventArgs e)
    {
        var window = new OperationsDrillDownWindow(2);
        if (Window.GetWindow(this) is Window owner) window.Owner = owner;
        window.ShowDialog();
    }

    private void PlexSessions_Click(
        object sender,
        RoutedEventArgs e)
    {
        var window =
            new PlexSessionsWindow();

        if (Window.GetWindow(this) is Window owner)
            window.Owner = owner;

        window.ShowDialog();
    }

    private void ApplicationsLive_Loaded(
        object sender,
        RoutedEventArgs e)
    {
        TargetText.Text = S.Context.Current?.Name ?? "No global target";
        var live =
            Services.LiveAnalyticsHub.Current;

        live.Updated -= ApplicationsLive_Updated;
        live.Updated += ApplicationsLive_Updated;
        live.SetActivePage("Applications");

        if (live.MediaSnapshot is { } snapshot)
        {
            BindSnapshot(snapshot);

            if (live.MediaUpdatedAt is { } updated)
                SampleAgeText.Text =
                    $"LIVE - updated {updated.ToLocalTime():HH:mm:ss}";
        }
    }

    private void ApplicationsLive_Unloaded(
        object sender,
        RoutedEventArgs e)
    {
        var live =
            Services.LiveAnalyticsHub.Current;

        live.Updated -= ApplicationsLive_Updated;
        live.DeactivatePage("Applications");
    }

    private void ApplicationsLive_Updated(
        object? sender,
        Services.LiveAnalyticsUpdateEventArgs e)
    {
        if (!IsLoaded ||
            e.Domain !=
                Services.LiveAnalyticsDomain.MediaSummary)
            return;

        var live =
            Services.LiveAnalyticsHub.Current;

        if (live.MediaSnapshot is { } snapshot)
        {
            BindSnapshot(snapshot);
        }

        SampleAgeText.Text =
            e.BadgeText;

        if (!e.Success)
            StatusText.Text =
                e.BadgeText + " - " + e.Message;
    }
}