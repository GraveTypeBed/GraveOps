using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using GraveOps.App.Models;
using GraveOps.App.Services;
using GraveOps.App.Windows;

namespace GraveOps.App.Views;

public partial class MediaServiceView : UserControl
{
    private readonly string _serviceKey;
    private readonly ObservableCollection<AppHealthCard> _cards = new();
    private readonly ObservableCollection<QueueDrillRow> _queueRows = new();
    private readonly ObservableCollection<PlexSessionRow> _plexRows = new();
    private readonly PlexSessionService _plexSessions = new(App.Services);


    private AppServices S => App.Services;
    private Services.LiveAnalyticsService Live => Services.LiveAnalyticsHub.Current;

    public MediaServiceView(string serviceKey)
    {
        InitializeComponent();

        _serviceKey =
            string.IsNullOrWhiteSpace(serviceKey)
                ? "Applications"
                : serviceKey.Trim();

        ServiceCards.ItemsSource = _cards;
        QueueInlineGrid.ItemsSource = _queueRows;
        PlexSessionsGrid.ItemsSource = _plexRows;


        ConfigureSurface();
        Loaded += MediaServiceView_LiveLoaded;
        Unloaded += MediaServiceView_LiveUnloaded;

    }

    private ServerProfile? Server => S.Context.Current;

    private IReadOnlyList<string> ServiceNames =>
        _serviceKey.ToLowerInvariant() switch
        {
            "plex" => ["Plex"],
            "sonarr" => ["Sonarr", "Sonarr Debrid"],
            "radarr" => ["Radarr", "Radarr Debrid"],
            "lidarr" => ["Lidarr"],
            "prowlarr" => ["Prowlarr"],
            _ => []
        };

    private string PrimaryName =>
        ServiceNames.FirstOrDefault() ?? _serviceKey;

    private string? SecondaryName =>
        ServiceNames.Count > 1
            ? ServiceNames[1]
            : null;

    private bool IsPlex =>
        string.Equals(
            _serviceKey,
            "Plex",
            StringComparison.OrdinalIgnoreCase);


    private void ConfigureSurface()
    {
        TargetText.Text =
            Server?.Name ?? "No global target";

        QueueInlinePanel.Visibility =
            IsPlex
                ? Visibility.Collapsed
                : Visibility.Visible;

        PlexSessionsPanel.Visibility =
            IsPlex
                ? Visibility.Visible
                : Visibility.Collapsed;

        FullQueueButton.Visibility =
            IsPlex
                ? Visibility.Collapsed
                : Visibility.Visible;

        switch (_serviceKey.ToLowerInvariant())
        {
            case "plex":
                HeadingText.Text = "Plex";
                DescriptionText.Text =
                    "Playback health, live sessions and verified Plex operations.";
                OperationsHintText.Text =
                    "Common Plex work stays on this page; use Docker only for deeper dependency inspection.";
                RestartPlexButton.Visibility = Visibility.Visible;
                DockerInspectButton.Content = "Docker / dependencies";
                QueueLabelText.Text = "SESSIONS";
                break;

            case "sonarr":
                HeadingText.Text = "Sonarr";
                DescriptionText.Text =
                    "Sonarr and Sonarr Debrid health, queues and operational tools.";
                OperationsHintText.Text =
                    "Both Sonarr instances, their live queues and health issues are kept together here.";
                break;

            case "radarr":
                HeadingText.Text = "Radarr";
                DescriptionText.Text =
                    "Radarr and Radarr Debrid health, queues and operational tools.";
                OperationsHintText.Text =
                    "Both Radarr instances, their live queues and health issues are kept together here.";
                break;

            case "lidarr":
                HeadingText.Text = "Lidarr";
                DescriptionText.Text =
                    "Music acquisition health, queue state and direct operational tools.";
                OperationsHintText.Text =
                    "Lidarr health and item-level queue detail are visible below.";
                break;

            case "prowlarr":
                HeadingText.Text = "Prowlarr";
                DescriptionText.Text =
                    "Indexer health, application access and diagnostics.";
                OperationsHintText.Text =
                    "Indexer health messages are visible inline so routine diagnosis does not require a second window.";
                break;

        }

        OpenPrimaryButton.Content =
            $"Open {PrimaryName}";

        if (!string.IsNullOrWhiteSpace(SecondaryName))
        {
            OpenSecondaryButton.Content =
                $"Open {SecondaryName}";
            OpenSecondaryButton.Visibility =
                Visibility.Visible;
        }
        else
        {
            OpenSecondaryButton.Visibility =
                Visibility.Collapsed;
        }

        UpdatePlexTokenStatus();
    }


    private void BindSnapshot(
        MediaOperationsSnapshot snapshot)
    {
        _cards.Clear();

        foreach (var name in ServiceNames)
        {
            var card =
                snapshot.Apps.FirstOrDefault(
                    x => x.Name.Equals(
                        name,
                        StringComparison.OrdinalIgnoreCase));

            if (card is not null)
                _cards.Add(card);
        }

        PrimaryStateText.Foreground =
            StatePresentation.Resource("GoText");
        VersionText.Foreground =
            StatePresentation.Resource("GoText");
        IssuesText.Foreground =
            StatePresentation.Resource("GoText");

        if (IsPlex)
        {
            var state = StatePresentation.PlexText(snapshot.Plex);
            PrimaryStateText.Text = state;
            PrimaryStateText.Foreground =
                StatePresentation.BrushForText(state);

            VersionText.Text = snapshot.Plex.Version;

            var issueCount =
                state.Equals(
                    "Online",
                    StringComparison.OrdinalIgnoreCase)
                    ? 0
                    : 1;

            IssuesText.Text = issueCount.ToString();
            IssuesText.Foreground =
                issueCount == 0
                    ? StatePresentation.Resource("Success")
                    : state.Equals(
                        "Offline",
                        StringComparison.OrdinalIgnoreCase)
                        ? StatePresentation.Resource("Danger")
                        : StatePresentation.BrushForText("warning");
        }
        else
        {
            var primary =
                _cards.FirstOrDefault(
                    x => x.Name.Equals(
                        PrimaryName,
                        StringComparison.OrdinalIgnoreCase))
                ?? _cards.FirstOrDefault();

            if (primary is null)
            {
                PrimaryStateText.Text = "Not configured";
                PrimaryStateText.Foreground =
                    StatePresentation.Resource("Muted");
            }
            else
            {
                PrimaryStateText.Text =
                    StatePresentation.AppText(primary.Health);
                PrimaryStateText.Foreground =
                    StatePresentation.BrushFor(primary.Health);
            }

            VersionText.Text = primary?.VersionText ?? "--";

            var queues =
                _cards.Where(x => x.QueueCount.HasValue)
                    .Sum(x => x.QueueCount ?? 0);

            var issues =
                _cards.Where(x => x.HealthIssueCount.HasValue)
                    .Sum(x => x.HealthIssueCount ?? 0);

            QueueText.Text =
                _cards.Any(x => x.QueueCount.HasValue)
                    ? queues.ToString()
                    : "--";

            IssuesText.Text =
                _cards.Any(x => x.HealthIssueCount.HasValue)
                    ? issues.ToString()
                    : "--";

            if (IssuesText.Text != "--")
            {
                IssuesText.Foreground =
                    issues == 0
                        ? StatePresentation.Resource("Success")
                        : _cards.Any(x => x.Health == AppHealthState.Offline)
                            ? StatePresentation.Resource("Danger")
                            : StatePresentation.BrushForText("warning");
            }
        }

        SampleText.Text =
            $"Updated {snapshot.SampledAt.ToLocalTime():HH:mm:ss}";

        OpenPrimaryButton.IsEnabled =
            ResolveManagedApp(PrimaryName) is not null;

        OpenSecondaryButton.IsEnabled =
            SecondaryName is not null &&
            ResolveManagedApp(SecondaryName) is not null;
    }

    private void UpdatePlexTokenStatus()
    {
        if (!IsPlex)
            return;

        if (Server is not { } server)
        {
            PlexTokenStatusText.Text =
                "Select a global server target first.";
            ClearPlexTokenButton.IsEnabled = false;
            return;
        }

        var configured =
            _plexSessions.HasToken(server);

        PlexTokenStatusText.Text =
            configured
                ? $"Token saved securely in Windows Credential Manager for {server.Name}. The value is never displayed."
                : $"No Plex session token is saved for {server.Name}. Paste an X-Plex-Token and use Save + test.";

        ClearPlexTokenButton.IsEnabled = configured;
    }


    private void BindPlexSessions(
        PlexSessionSnapshot snapshot)
    {
        var selectedId =
            (PlexSessionsGrid.SelectedItem as PlexSessionRow)?.SessionId;

        _plexRows.Clear();

        foreach (var item in snapshot.Sessions)
            _plexRows.Add(item);

        PlexSessionCountText.Text =
            snapshot.SessionCount.ToString();

        PlexDirectPlayText.Text =
            snapshot.DirectPlayCount.ToString();

        PlexDirectStreamText.Text =
            snapshot.DirectStreamCount.ToString();

        PlexTranscodeText.Text =
            snapshot.TranscodeCount.ToString();

        PlexBandwidthText.Text =
            snapshot.TotalBandwidthText;

        UpdatePlexWorkspace(snapshot.SessionCount);

        QueueText.Text =
            snapshot.SessionCount.ToString();

        if (!string.IsNullOrWhiteSpace(selectedId))
        {
            var match =
                _plexRows.FirstOrDefault(
                    x => x.SessionId == selectedId);

            if (match is not null)
                PlexSessionsGrid.SelectedItem = match;
        }

        UpdateSelectedPlexSession();
    }

    private void ResetPlexSessionSummary()
    {
        _plexRows.Clear();
        UpdatePlexWorkspace(0);
        PlexSessionCountText.Text = "--";
        PlexDirectPlayText.Text = "--";
        PlexDirectStreamText.Text = "--";
        PlexTranscodeText.Text = "--";
        PlexBandwidthText.Text = "--";
        QueueText.Text = "--";
        PlexSessionDetailBox.Text = "";
        PlexSelectedSessionText.Text =
            "Select a session for codec, client and transcode details.";
    }

    private void PlexSessionsGrid_SelectionChanged(
        object sender,
        SelectionChangedEventArgs e)
        => UpdateSelectedPlexSession();

    private void UpdateSelectedPlexSession()
    {
        if (PlexSessionsGrid.SelectedItem is not PlexSessionRow item)
        {
            PlexSelectedSessionText.Text =
                "Select a session for codec, client and transcode details.";
            PlexSessionDetailBox.Text = "";
            return;
        }

        PlexSelectedSessionText.Text =
            $"{item.User} | {item.Title} | {item.Decision}";

        PlexSessionDetailBox.Text =
            item.DetailText;
    }

    private async void SavePlexToken_Click(
        object sender,
        RoutedEventArgs e)
    {
        if (Server is not { } server)
            return;

        var token =
            PlexTokenBox.Password.Trim();

        if (token.Length == 0)
        {
            GraveOpsDialog.Show(
                Window.GetWindow(this),
                "Paste an X-Plex-Token first.",
                "Plex token required",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        try
        {
            PlexSessionStatusText.Text =
                "Testing Plex token before saving...";

            var snapshot =
                await _plexSessions.TestAndSaveAsync(
                    server,
                    token);

            PlexTokenBox.Clear();
            UpdatePlexTokenStatus();
            BindPlexSessions(snapshot);

            PlexSessionStatusText.Text =
                $"Token verified and saved. Loaded {snapshot.SessionCount} active session(s).";
        }
        catch (Exception ex)
        {
            GraveOpsDialog.Show(
                Window.GetWindow(this),
                ex.Message,
                "Plex token test failed",
                MessageBoxButton.OK,
                MessageBoxImage.Error);

            PlexSessionStatusText.Text =
                "Token was not saved because validation failed.";
        }
    }

    private void ClearPlexToken_Click(
        object sender,
        RoutedEventArgs e)
    {
        if (Server is not { } server)
            return;

        if (GraveOpsDialog.Show(
                Window.GetWindow(this),
                $"Remove the saved Plex session token for {server.Name}?",
                "Clear Plex token",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning)
            != MessageBoxResult.Yes)
            return;

        _plexSessions.ClearToken(server);
        PlexTokenBox.Clear();
        ResetPlexSessionSummary();
        UpdatePlexTokenStatus();

        PlexSessionStatusText.Text =
            "Saved Plex session token removed.";
    }

    private ManagedApp? ResolveManagedApp(
        string name)
        => S.Config.Current.Applications
            .FirstOrDefault(
                x => x.Name.Equals(
                    name,
                    StringComparison.OrdinalIgnoreCase));

    private void OpenManagedApp(
        string name)
    {
        var app =
            ResolveManagedApp(name);

        if (app is null)
        {
            StatusText.Text =
                $"{name} launcher is not configured.";
            return;
        }

        var server =
            app.ServerId is { } id
                ? S.Config.Current.Servers
                    .FirstOrDefault(x => x.Id == id)
                : Server;

        if (server is null)
        {
            StatusText.Text =
                "No server target is available.";
            return;
        }

        var resolved =
            app.Url.Replace(
                "{host}",
                server.Host,
                StringComparison.OrdinalIgnoreCase);

        if (app.OpenEmbedded)
        {
            new EmbeddedBrowserWindow(
                app.Name,
                resolved)
            {
                Owner = Window.GetWindow(this)
            }.Show();
        }
        else
        {
            Process.Start(
                new ProcessStartInfo(resolved)
                {
                    UseShellExecute = true
                });
        }

        S.Activity.Record(
            $"Opened {app.Name}",
            resolved,
            ActivityLevel.Info,
            serverId: server.Id,
            deepLink: $"page:{_serviceKey}");
    }

    private async void Refresh_Click(
        object sender,
        RoutedEventArgs e)
    {
        RefreshButton.IsEnabled = false;
        StatusText.Text = $"Refreshing {_serviceKey} live analytics...";

        try
        {
            await Live.ForceAsync(_serviceKey);
            ApplyLiveCache();
            StatusText.Text = $"Updated {DateTime.Now:HH:mm:ss}.";
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

    private void OpenPrimary_Click(
        object sender,
        RoutedEventArgs e)
        => OpenManagedApp(PrimaryName);

    private void OpenSecondary_Click(
        object sender,
        RoutedEventArgs e)
    {
        if (SecondaryName is { } name)
            OpenManagedApp(name);
    }

    private void CardOpen_Click(
        object sender,
        RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is
            AppHealthCard card)
        {
            OpenManagedApp(card.Name);
        }
    }

    private void FullQueue_Click(
        object sender,
        RoutedEventArgs e)
    {
        var owner =
            Window.GetWindow(this);

        var window =
            new OperationsDrillDownWindow(2);

        if (owner is not null)
            window.Owner = owner;

        window.ShowDialog();
    }

    private void DockerInspect_Click(
        object sender,
        RoutedEventArgs e)
    {
        var owner =
            Window.GetWindow(this);

        var window =
            new OperationsDrillDownWindow(0);

        if (owner is not null)
            window.Owner = owner;

        window.ShowDialog();
    }

    private async void RestartPlex_Click(
        object sender,
        RoutedEventArgs e)
    {
        if (Server is not { } server)
            return;

        var action =
            S.Config.Current.Actions
                .FirstOrDefault(
                    x => x.Name.Equals(
                        "Restart Plex",
                        StringComparison.OrdinalIgnoreCase));

        if (action is null)
        {
            GraveOpsDialog.Show(
                Window.GetWindow(this),
                "Restart Plex is not present in the GraveOps action library.",
                "Restart unavailable",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        if (GraveOpsDialog.Show(
                Window.GetWindow(this),
                $"Run '{action.Name}' on {server.Name}?\n\n{action.Command}",
                "Confirm GraveOps action",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question)
            != MessageBoxResult.Yes)
            return;

        RestartPlexButton.IsEnabled = false;

        try
        {
            var result =
                await S.ActionRunner.RunAsync(
                    action,
                    server);

            if (!result.Success)
            {
                GraveOpsDialog.Show(
                    Window.GetWindow(this),
                    string.IsNullOrWhiteSpace(result.Error)
                        ? result.Verification
                        : result.Error,
                    "Restart Plex failed",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }

            await Live.ForceAsync(_serviceKey);
            ApplyLiveCache();
        }
        finally
        {
            RestartPlexButton.IsEnabled = true;
        }
    }

    private void Logs_Click(
        object sender,
        RoutedEventArgs e)
        => S.Navigation.Request("page:Logs");

    private void Terminal_Click(
        object sender,
        RoutedEventArgs e)
        => S.Navigation.Request("page:Terminal");

    private async void MediaServiceView_LiveLoaded(
        object sender,
        RoutedEventArgs e)
    {
        Live.Updated -= Live_Updated;
        Live.Updated += Live_Updated;
        Live.SetActivePage(_serviceKey);

        var server = Server;
        var needsFreshOwnershipSample = server is not null &&
            (Live.MediaSnapshot is null || Live.MediaSnapshot.ServerId != server.Id);

        if (needsFreshOwnershipSample)
            SetLoadingState();

        ApplyLiveCache();

        if (!needsFreshOwnershipSample)
            return;

        try
        {
            await Live.ForceAsync(_serviceKey);
            ApplyLiveCache();

            if (Live.MediaSnapshot is { } refreshed && refreshed.ServerId == server!.Id)
                StatusText.Text = $"Live telemetry ready for {server.Name}.";
            else
            {
                // LOADING is transitional only. A completed forced refresh that did
                // not produce a media summary must become a truthful stale/error
                // state while queue detail can continue updating independently.
                PrimaryStateText.Text = "UNAVAILABLE";
                PrimaryStateText.Foreground = StatePresentation.BrushForText("warning");
                StatusText.Text = $"No fresh {_serviceKey} summary sample was returned from {server!.Name}. Queue detail may still be live; GraveOps will retry automatically.";
                SampleText.Text = "STALE - no successful media summary sample yet";
            }
        }
        catch (Exception ex)
        {
            StatusText.Text = ex.Message;
            SampleText.Text = "STALE - initial sample failed";
        }
    }

    private void SetLoadingState()
    {
        TargetText.Text = Server?.Name ?? "No global target";
        PrimaryStateText.Text = "LOADING";
        PrimaryStateText.Foreground = StatePresentation.BrushForText("checking");
        VersionText.Text = "--";
        QueueText.Text = "--";
        IssuesText.Text = "--";
        SampleText.Text = "LOADING - requesting fresh owner telemetry";
        StatusText.Text = $"Loading {_serviceKey} telemetry from {Server?.Name ?? "the owning host"}...";

        _cards.Clear();
        _queueRows.Clear();
        ServiceCards.ItemsSource = _cards;

        if (!IsPlex)
        {
            QueueInlineGrid.Visibility = Visibility.Collapsed;
            QueueEmptyStateText.Visibility = Visibility.Visible;
            QueueEmptyStateText.Text = "Live queue and health detail is loading...";
            QueueInlineStatusText.Text = "Loading fresh queue detail from the owning host...";
        }
        else
        {
            ResetPlexSessionSummary();
            PlexSessionStatusText.Text = "Loading Plex session telemetry from the owning host...";
        }
    }

    private void MediaServiceView_LiveUnloaded(
        object sender,
        RoutedEventArgs e)
    {
        Live.Updated -= Live_Updated;
        Live.DeactivatePage(_serviceKey);
    }

    private void Live_Updated(
        object? sender,
        Services.LiveAnalyticsUpdateEventArgs e)
    {
        if (!IsLoaded)
            return;

        switch (e.Domain)
        {
            case Services.LiveAnalyticsDomain.MediaSummary:
                if (Live.MediaSnapshot is { } media)
                {
                    BindSnapshot(media);
                }

                SampleText.Text =
                    e.BadgeText;

                if (!e.Success)
                {
                    if (Server is { } owner &&
                        (Live.MediaSnapshot is null || Live.MediaSnapshot.ServerId != owner.Id))
                    {
                        PrimaryStateText.Text = "UNAVAILABLE";
                        PrimaryStateText.Foreground = StatePresentation.BrushForText("warning");
                    }

                    StatusText.Text =
                        e.BadgeText + " - " + e.Message;
                }
                break;

            case Services.LiveAnalyticsDomain.QueueDetail:
                if (!e.PageKey.Equals(
                        _serviceKey,
                        StringComparison.OrdinalIgnoreCase))
                    return;

                ApplyLiveQueue();

                QueueInlineStatusText.Text =
                    e.Success
                        ? $"{e.BadgeText} - {_queueRows.Count} queue / health row(s)."
                        : e.BadgeText + " - " + e.Message;
                break;

            case Services.LiveAnalyticsDomain.PlexSessions:
                if (!IsPlex)
                    return;

                if (Live.PlexSnapshot is { } plex)
                    BindPlexSessions(plex);
                else if (Server is { } server &&
                         !_plexSessions.HasToken(server))
                    ResetPlexSessionSummary();

                PlexSessionStatusText.Text = e.BadgeText + " - " + e.Message;
                break;
        }
    }

    private void ApplyLiveCache()
    {
        TargetText.Text =
            Server?.Name ?? "No global target";

        var serverId = Server?.Id;

        if (serverId is { } ownerId &&
            Live.MediaSnapshot is { } media &&
            media.ServerId == ownerId)
        {
            BindSnapshot(media);

            if (Live.MediaUpdatedAt is { } updated)
                SampleText.Text =
                    $"LIVE - updated {updated.ToLocalTime():HH:mm:ss}";
        }

        if (!IsPlex)
            ApplyLiveQueue();

        if (IsPlex &&
            serverId is { } plexOwnerId &&
            Live.PlexServerId == plexOwnerId &&
            Live.PlexSnapshot is { } plex)
        {
            BindPlexSessions(plex);

            if (Live.PlexUpdatedAt is { } updated)
                PlexSessionStatusText.Text =
                    $"LIVE - updated {updated.ToLocalTime():HH:mm:ss}";
        }
    }

    private void ApplyLiveQueue()
    {
        var rows =
            Live.GetQueueRows(
                _serviceKey);

        _queueRows.Clear();

        foreach (var row in rows.Take(40))
            _queueRows.Add(row);

        UpdateQueueWorkspace(rows.Count);

        var updated =
            Live.GetQueueUpdatedAt(
                _serviceKey);

        if (updated is { } timestamp)
        {
            QueueInlineStatusText.Text =
                rows.Count > 40
                    ? $"LIVE - updated {timestamp.ToLocalTime():HH:mm:ss} - showing 40 of {rows.Count} row(s)."
                    : $"LIVE - updated {timestamp.ToLocalTime():HH:mm:ss} - {rows.Count} row(s).";
        }
    }

    private void UpdatePlexWorkspace(int sessionCount)
    {
        var hasSessions = sessionCount > 0;
        PlexSessionsGrid.Visibility = hasSessions ? Visibility.Visible : Visibility.Collapsed;
        PlexSessionSplitter.Visibility = hasSessions ? Visibility.Visible : Visibility.Collapsed;
        PlexSessionDetailPanel.Visibility = hasSessions ? Visibility.Visible : Visibility.Collapsed;
        PlexEmptyStateText.Visibility = hasSessions ? Visibility.Collapsed : Visibility.Visible;
    }

    private void UpdateQueueWorkspace(int rowCount)
    {
        var hasRows = rowCount > 0;
        QueueInlineGrid.Visibility = hasRows ? Visibility.Visible : Visibility.Collapsed;
        QueueEmptyStateText.Visibility = hasRows ? Visibility.Collapsed : Visibility.Visible;

        if (hasRows)
            return;

        if (int.TryParse(QueueText.Text, out var queued) && queued > 0)
            QueueEmptyStateText.Text = $"{queued} queued - live item detail is loading or not yet available.";
        else if (IssuesText.Text != "0" && IssuesText.Text != "--")
            QueueEmptyStateText.Text = $"{IssuesText.Text} health issue(s) reported - detail is loading or unavailable.";
        else
            QueueEmptyStateText.Text = "No active queue or health items.";
    }
}