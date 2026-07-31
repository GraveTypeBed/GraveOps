using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using GraveOps.App.Models;
using GraveOps.App.Services;

namespace GraveOps.App.Views;

public partial class DownloadClientView : UserControl
{
    private readonly string _clientKey;
    private readonly ObservableCollection<DownloadQueueItem> _queue = new();
    private readonly ObservableCollection<DownloadHistoryItem> _history = new();

    private AppServices S => App.Services;
    private LiveAnalyticsService Live => LiveAnalyticsHub.Current;
    private ServerProfile? Server => S.Context.Current;
    private bool IsQbit => _clientKey.Equals("qBittorrent", StringComparison.OrdinalIgnoreCase);

    public DownloadClientView(string clientKey)
    {
        InitializeComponent();

        _clientKey = DownloadClientService.NormalizeClientKey(clientKey);
        if (!DownloadClientService.IsSupported(_clientKey))
            throw new ArgumentOutOfRangeException(nameof(clientKey), clientKey, "Unsupported download client page.");

        QueueGrid.ItemsSource = _queue;
        HistoryGrid.ItemsSource = _history;

        ConfigureSurface();
        Loaded += DownloadClientView_Loaded;
        Unloaded += DownloadClientView_Unloaded;
    }

    private void ConfigureSurface()
    {
        TargetText.Text = Server?.Name ?? "No global target";
        HeadingText.Text = _clientKey;

        if (IsQbit)
        {
            DescriptionText.Text = "Torrent transfer analytics, progress, ETA, seeding and protected container-local API telemetry.";
            OperationsHintText.Text = "The Web UI remains authenticated on the host/LAN. GraveOps reads telemetry only from inside the qBittorrent container.";
            ItemsLabelText.Text = "TORRENTS";
            Metric1LabelText.Text = "DOWNLOAD";
            Metric2LabelText.Text = "UPLOAD";
            Metric3LabelText.Text = "REMAINING";
            Metric4LabelText.Text = "NEXT ETA";
            CurrentWorkHeadingText.Text = "Current torrents";
            CurrentWorkHintText.Text = "Progress, transfer rate, remaining work, ETA, ratio and peers from qBittorrent.";
            HistoryHeadingText.Text = "Recently completed";
            HistoryHintText.Text = "Most recently completed torrents still present in qBittorrent.";
            DockerButton.Visibility = Visibility.Visible;
        }
        else
        {
            DescriptionText.Text = "Usenet queue analytics, progress, remaining data, ETA and recent SABnzbd history.";
            OperationsHintText.Text = "GraveOps uses the SABnzbd API key only on the Linux host; the key is never returned to Windows.";
            ItemsLabelText.Text = "QUEUE";
            Metric1LabelText.Text = "DOWNLOAD";
            Metric2LabelText.Text = "REMAINING";
            Metric3LabelText.Text = "ETA";
            Metric4LabelText.Text = "FAILED RECENT";
            CurrentWorkHeadingText.Text = "Current downloads";
            CurrentWorkHintText.Text = "Queue status, progress, remaining size and time from SABnzbd.";
            HistoryHeadingText.Text = "Recent history";
            HistoryHintText.Text = "Recent completed and failed SABnzbd jobs.";
            DownloadedColumn.Visibility = Visibility.Collapsed;
            UploadColumn.Visibility = Visibility.Collapsed;
            RatioColumn.Visibility = Visibility.Collapsed;
            PeersColumn.Visibility = Visibility.Collapsed;
        }
    }

    private async void DownloadClientView_Loaded(object sender, RoutedEventArgs e)
    {
        Live.Updated -= Live_Updated;
        Live.Updated += Live_Updated;
        Live.SetActivePage(_clientKey);
        ApplyLiveCache();

        if (Live.GetDownloadSnapshot(_clientKey) is null)
        {
            FreshnessText.Text = "CHECKING...";
            StatusText.Text = $"Loading {_clientKey} analytics...";
            await RefreshLiveAsync(false);
        }
    }

    private void DownloadClientView_Unloaded(object sender, RoutedEventArgs e)
    {
        Live.Updated -= Live_Updated;
        Live.DeactivatePage(_clientKey);
    }

    private void Live_Updated(object? sender, LiveAnalyticsUpdateEventArgs e)
    {
        if (!IsLoaded ||
            e.Domain != LiveAnalyticsDomain.DownloadClient ||
            !e.PageKey.Equals(_clientKey, StringComparison.OrdinalIgnoreCase))
            return;

        ApplyLiveCache();
        FreshnessText.Text = e.BadgeText;
        StatusText.Text = $"{e.BadgeText} - {e.Message}";
    }

    private void ApplyLiveCache()
    {
        TargetText.Text = Server?.Name ?? "No global target";
        var snapshot = Live.GetDownloadSnapshot(_clientKey);
        if (snapshot is null)
        {
            SetLoadingState();
            return;
        }

        ApplySnapshot(snapshot);
        var updated = Live.GetDownloadUpdatedAt(_clientKey);
        if (updated is { } timestamp)
            FreshnessText.Text = $"LIVE - updated {timestamp.ToLocalTime():HH:mm:ss}";
    }

    private void SetLoadingState()
    {
        StateText.Text = "CHECKING";
        StateText.Foreground = StatePresentation.BrushForText("checking");
        VersionText.Text = "--";
        SecurityText.Text = IsQbit ? "Container-local protected API" : "Linux-host API key";
        ConnectionText.Text = "--";
        ActiveText.Text = "--";
        ActiveDetailText.Text = "Telemetry pending";
        ItemsText.Text = "--";
        ItemsDetailText.Text = "Telemetry pending";
        Metric1ValueText.Text = "--";
        Metric2ValueText.Text = "--";
        Metric3ValueText.Text = "--";
        Metric4ValueText.Text = "--";
        TransferAnalyticsText.Text = "Waiting for the first live client sample.";
        WorkloadAnalyticsText.Text = "Waiting for the first live client sample.";
        UpdateCollections(null);
    }

    private void ApplySnapshot(DownloadClientSnapshot snapshot)
    {
        StateText.Text = snapshot.State;
        StateText.Foreground = StatePresentation.BrushForText(snapshot.State);
        VersionText.Text = string.IsNullOrWhiteSpace(snapshot.Version) ? "--" : $"v{snapshot.Version.TrimStart('v', 'V')}";
        SecurityText.Text = snapshot.Security;
        SecurityText.Foreground = StatePresentation.BrushForText("protected");
        ConnectionText.Text = snapshot.Connection;
        ActiveText.Text = snapshot.ActiveCount.ToString();
        ItemsText.Text = snapshot.TotalCount.ToString();

        if (IsQbit)
        {
            ActiveDetailText.Text = $"{snapshot.DownloadingCount} downloading | {snapshot.SeedingCount} seeding";
            ItemsDetailText.Text = $"{snapshot.PausedCount} paused | {snapshot.StalledCount} stalled";
            Metric1ValueText.Text = snapshot.DownloadSpeed;
            Metric2ValueText.Text = snapshot.UploadSpeed;
            Metric3ValueText.Text = snapshot.Remaining;
            Metric4ValueText.Text = snapshot.Eta;

            TransferAnalyticsText.Text =
                $"Connection: {snapshot.Connection}\n" +
                $"Download: {snapshot.DownloadSpeed} | Upload: {snapshot.UploadSpeed}\n" +
                $"Session: {snapshot.SessionDownloaded} down | {snapshot.SessionUploaded} up\n" +
                $"Rate limits: {snapshot.RateLimit} | DHT: {snapshot.DhtNodes} nodes";

            WorkloadAnalyticsText.Text =
                $"Total torrents: {snapshot.TotalCount} | Active: {snapshot.ActiveCount}\n" +
                $"Downloading: {snapshot.DownloadingCount} | Seeding: {snapshot.SeedingCount}\n" +
                $"Paused: {snapshot.PausedCount} | Stalled: {snapshot.StalledCount}\n" +
                $"Remaining: {snapshot.Remaining} | Recently completed: {snapshot.CompletedRecentCount}";
        }
        else
        {
            ActiveDetailText.Text = $"{snapshot.DownloadingCount} downloading | {snapshot.PausedCount} paused";
            ItemsDetailText.Text = $"{snapshot.CompletedRecentCount} completed | {snapshot.FailedRecentCount} failed recent";
            Metric1ValueText.Text = snapshot.DownloadSpeed;
            Metric2ValueText.Text = snapshot.Remaining;
            Metric3ValueText.Text = snapshot.Eta;
            Metric4ValueText.Text = snapshot.FailedRecentCount.ToString();

            TransferAnalyticsText.Text =
                $"Download: {snapshot.DownloadSpeed} | Remaining: {snapshot.Remaining} | ETA: {snapshot.Eta}\n" +
                $"Today: {snapshot.DayDownloaded} | Week: {snapshot.WeekDownloaded}\n" +
                $"Month: {snapshot.MonthDownloaded} | Total: {snapshot.TotalDownloaded}\n" +
                $"Rate limit: {snapshot.RateLimit} | Disk free: {snapshot.DiskFree}";

            WorkloadAnalyticsText.Text =
                $"Queued jobs: {snapshot.TotalCount} | Active: {snapshot.ActiveCount}\n" +
                $"Downloading: {snapshot.DownloadingCount} | Paused: {snapshot.PausedCount}\n" +
                $"Recent completed: {snapshot.CompletedRecentCount} | Failed: {snapshot.FailedRecentCount}\n" +
                (string.IsNullOrWhiteSpace(snapshot.Detail) ? "Read-only SABnzbd analytics." : snapshot.Detail);
        }

        UpdateCollections(snapshot);
    }

    private void UpdateCollections(DownloadClientSnapshot? snapshot)
    {
        _queue.Clear();
        _history.Clear();

        if (snapshot is not null)
        {
            foreach (var item in snapshot.Queue)
                _queue.Add(item);
            foreach (var item in snapshot.History)
                _history.Add(item);
        }

        QueueCountText.Text = snapshot is null ? "--" : $"{_queue.Count} shown";
        HistoryCountText.Text = snapshot is null ? "--" : $"{_history.Count} shown";

        var hasQueue = _queue.Count > 0;
        QueueGrid.Visibility = hasQueue ? Visibility.Visible : Visibility.Collapsed;
        QueueEmptyText.Visibility = hasQueue ? Visibility.Collapsed : Visibility.Visible;
        QueueEmptyText.Text = snapshot is null
            ? "Live download detail is loading..."
            : IsQbit
                ? "No torrents are currently present in qBittorrent."
                : "No jobs are currently queued in SABnzbd.";

        var hasHistory = _history.Count > 0;
        HistoryGrid.Visibility = hasHistory ? Visibility.Visible : Visibility.Collapsed;
        HistoryEmptyText.Visibility = hasHistory ? Visibility.Collapsed : Visibility.Visible;
        HistoryEmptyText.Text = snapshot is null
            ? "Recent history is loading..."
            : IsQbit
                ? "No completed torrents are currently available for recent-history display."
                : "No recent SABnzbd history is available.";
    }

    private async void Refresh_Click(object sender, RoutedEventArgs e)
        => await RefreshLiveAsync(true);

    private async Task RefreshLiveAsync(bool showStatus)
    {
        if (Server is null)
        {
            StatusText.Text = "Select a global server target first.";
            return;
        }

        RefreshButton.IsEnabled = false;
        FreshnessText.Text = "CHECKING...";
        if (showStatus)
            StatusText.Text = $"Refreshing {_clientKey} live analytics...";

        try
        {
            await Live.ForceAsync(_clientKey);
            ApplyLiveCache();
        }
        catch (Exception ex)
        {
            StatusText.Text = ex.Message;
        }
        finally
        {
            RefreshButton.IsEnabled = true;
        }
    }

    private void Open_Click(object sender, RoutedEventArgs e)
    {
        var app = S.Config.Current.Applications.FirstOrDefault(
            x => x.Name.Equals(_clientKey, StringComparison.OrdinalIgnoreCase));

        if (app is null)
        {
            StatusText.Text = $"{_clientKey} launcher is not configured.";
            return;
        }

        var server = app.ServerId is { } id
            ? S.Config.Current.Servers.FirstOrDefault(x => x.Id == id)
            : Server;

        if (server is null)
        {
            StatusText.Text = "No server target is available.";
            return;
        }

        var resolved = app.Url.Replace("{host}", server.Host, StringComparison.OrdinalIgnoreCase);
        if (app.OpenEmbedded)
        {
            new EmbeddedBrowserWindow(app.Name, resolved)
            {
                Owner = Window.GetWindow(this)
            }.Show();
        }
        else
        {
            Process.Start(new ProcessStartInfo(resolved) { UseShellExecute = true });
        }

        S.Activity.Record(
            $"Opened {app.Name}",
            resolved,
            ActivityLevel.Info,
            serverId: server.Id,
            deepLink: $"page:{_clientKey}");
    }

    private void Docker_Click(object sender, RoutedEventArgs e)
    {
        var owner = Window.GetWindow(this);
        var window = new OperationsDrillDownWindow(0);
        if (owner is not null)
            window.Owner = owner;
        window.ShowDialog();
    }

    private void Logs_Click(object sender, RoutedEventArgs e)
        => S.Navigation.Request("page:Logs");

    private void Terminal_Click(object sender, RoutedEventArgs e)
        => S.Navigation.Request("page:Terminal");
}
