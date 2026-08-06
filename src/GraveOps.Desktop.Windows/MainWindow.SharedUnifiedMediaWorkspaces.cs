using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using GraveOps.Presentation.Avalonia.MediaWorkspaces;

namespace GraveOps.Desktop.Windows;

public partial class MainWindow
{
    private UnifiedMediaHubView?
        _sharedMediaHubView;

    private UnifiedPlexView?
        _sharedPlexView;

    private UnifiedArrView?
        _sharedArrView;

    private UnifiedDownloadClientView?
        _sharedSabnzbdView;

    private UnifiedDownloadClientView?
        _sharedQBittorrentView;

    private UnifiedLifecycleView?
        _sharedLifecycleView;

    private DispatcherTimer?
        _sharedMediaSyncTimer;

    private void InitializeSharedUnifiedMediaWorkspaces()
    {
        _sharedMediaHubView =
            new UnifiedMediaHubView();

        _sharedPlexView =
            new UnifiedPlexView();

        _sharedArrView =
            new UnifiedArrView();

        _sharedSabnzbdView =
            new UnifiedDownloadClientView();

        _sharedQBittorrentView =
            new UnifiedDownloadClientView();

        _sharedLifecycleView =
            new UnifiedLifecycleView();

        ReplaceSharedMediaPage(
            "MediaHubPage",
            _sharedMediaHubView);

        ReplaceSharedMediaPage(
            "PlexPage",
            _sharedPlexView);

        ReplaceSharedMediaPage(
            "ArrPage",
            _sharedArrView);

        ReplaceSharedMediaPage(
            "SABnzbdPage",
            _sharedSabnzbdView);

        ReplaceSharedMediaPage(
            "QBittorrentPage",
            _sharedQBittorrentView);

        ReplaceSharedMediaPage(
            "LifecyclePage",
            _sharedLifecycleView);

        WireSharedWindowsMediaHub();
        WireSharedWindowsPlex();
        WireSharedWindowsArr();
        WireSharedWindowsSabnzbd();
        WireSharedWindowsQBittorrent();
        WireSharedWindowsLifecycle();

        _sharedMediaSyncTimer =
            new DispatcherTimer
            {
                Interval =
                    TimeSpan.FromMilliseconds(
                        500)
            };

        _sharedMediaSyncTimer.Tick +=
            (_, _) =>
            {
                if (SharedMediaPageVisible())
                    UpdateSharedUnifiedMediaWorkspaces();
            };

        _sharedMediaSyncTimer.Start();
    }

    private void DisposeSharedUnifiedMediaWorkspaces()
    {
        _sharedMediaSyncTimer?.Stop();
        _sharedMediaSyncTimer =
            null;
    }

    private bool SharedMediaPageVisible() =>
        Get<Grid>(
                "MediaHubPage")
            .IsVisible ||
        Get<Grid>(
                "PlexPage")
            .IsVisible ||
        Get<Grid>(
                "ArrPage")
            .IsVisible ||
        Get<Grid>(
                "SABnzbdPage")
            .IsVisible ||
        Get<Grid>(
                "QBittorrentPage")
            .IsVisible ||
        Get<Grid>(
                "LifecyclePage")
            .IsVisible;

    private void ReplaceSharedMediaPage(
        string pageName,
        Control sharedView)
    {
        var page =
            Get<Grid>(
                pageName);

        foreach (var child in
                 page.Children.ToArray())
        {
            child.IsVisible =
                false;
        }

        Grid.SetRowSpan(
            sharedView,
            32);

        Grid.SetColumnSpan(
            sharedView,
            32);

        page.Children.Add(
            sharedView);
    }

    private void UpdateSharedUnifiedMediaWorkspaces()
    {
        UpdateSharedWindowsMediaHub();
        UpdateSharedWindowsPlex();
        UpdateSharedWindowsArr();
        UpdateSharedWindowsSabnzbd();
        UpdateSharedWindowsQBittorrent();
        UpdateSharedWindowsLifecycle();
    }

    private void WireSharedWindowsMediaHub()
    {
        if (_sharedMediaHubView is null)
            return;

        _sharedMediaHubView.RefreshRequested +=
            async (_, _) =>
                await RefreshAsync();

        _sharedMediaHubView.ProductOpenRequested +=
            (_, e) =>
                OpenSharedWindowsProduct(
                    e.Row.Product);
    }

    private void WireSharedWindowsPlex()
    {
        if (_sharedPlexView is null)
            return;

        _sharedPlexView.ActionRequested +=
            (_, e) =>
            {
                switch (e.Action)
                {
                    case UnifiedPlexAction.Refresh:
                        PlexRefreshButton_OnClick(
                            null,
                            new RoutedEventArgs());
                        break;

                    case UnifiedPlexAction.Open:
                        PlexOpenButton_OnClick(
                            null,
                            new RoutedEventArgs());
                        break;

                    case UnifiedPlexAction.SaveAndTest:
                        Get<TextBox>(
                                "PlexEndpointTextBox")
                            .Text =
                            e.Configuration.Endpoint;

                        if (!string.IsNullOrWhiteSpace(
                                e.Configuration.Secret))
                        {
                            Get<TextBox>(
                                    "PlexTokenTextBox")
                                .Text =
                                e.Configuration.Secret;
                        }

                        PlexSaveTestButton_OnClick(
                            null,
                            new RoutedEventArgs());
                        break;

                    case UnifiedPlexAction.ClearCredential:
                        PlexClearTokenButton_OnClick(
                            null,
                            new RoutedEventArgs());
                        break;
                }
            };
    }

    private void WireSharedWindowsArr()
    {
        if (_sharedArrView is null)
            return;

        _sharedArrView.ActionRequested +=
            (_, e) =>
            {
                switch (e.Request.Action)
                {
                    case UnifiedArrAction.Refresh:
                        ArrRefreshButton_OnClick(
                            null,
                            new RoutedEventArgs());
                        break;

                    case UnifiedArrAction.Open:
                    case UnifiedArrAction.OpenDetail:
                        ArrOpenButton_OnClick(
                            null,
                            new RoutedEventArgs());
                        break;

                    case UnifiedArrAction.SaveAndTest:
                        Get<TextBox>(
                                "ArrEndpointTextBox")
                            .Text =
                            e.Request.Configuration.Endpoint;

                        if (!string.IsNullOrWhiteSpace(
                                e.Request.Configuration.Secret))
                        {
                            Get<TextBox>(
                                    "ArrApiKeyTextBox")
                                .Text =
                                e.Request.Configuration.Secret;
                        }

                        ArrSaveTestButton_OnClick(
                            null,
                            new RoutedEventArgs());
                        break;

                    case UnifiedArrAction.ClearCredential:
                        ArrClearSavedKeyButton_OnClick(
                            null,
                            new RoutedEventArgs());
                        break;
                }
            };
    }

    private void WireSharedWindowsSabnzbd()
    {
        if (_sharedSabnzbdView is null)
            return;

        _sharedSabnzbdView.ActionRequested +=
            (_, e) =>
            {
                switch (e.Action)
                {
                    case UnifiedDownloadClientAction.Refresh:
                        SABnzbdRefreshButton_OnClick(
                            null,
                            new RoutedEventArgs());
                        break;

                    case UnifiedDownloadClientAction.Open:
                        SABnzbdOpenButton_OnClick(
                            null,
                            new RoutedEventArgs());
                        break;

                    case UnifiedDownloadClientAction.SaveAndTest:
                        Get<TextBox>(
                                "SABnzbdEndpointTextBox")
                            .Text =
                            e.Configuration.Endpoint;

                        if (!string.IsNullOrWhiteSpace(
                                e.Configuration.Secret))
                        {
                            Get<TextBox>(
                                    "SABnzbdApiKeyTextBox")
                                .Text =
                                e.Configuration.Secret;
                        }

                        SABnzbdSaveTestButton_OnClick(
                            null,
                            new RoutedEventArgs());
                        break;

                    case UnifiedDownloadClientAction.ClearCredential:
                        SABnzbdClearSavedApiKeyButton_OnClick(
                            null,
                            new RoutedEventArgs());
                        break;
                }
            };
    }

    private void WireSharedWindowsQBittorrent()
    {
        if (_sharedQBittorrentView is null)
            return;

        _sharedQBittorrentView.ActionRequested +=
            (_, e) =>
            {
                switch (e.Action)
                {
                    case UnifiedDownloadClientAction.Refresh:
                        QBittorrentRefreshButton_OnClick(
                            null,
                            new RoutedEventArgs());
                        break;

                    case UnifiedDownloadClientAction.Open:
                        QBittorrentOpenButton_OnClick(
                            null,
                            new RoutedEventArgs());
                        break;

                    case UnifiedDownloadClientAction.SaveAndTest:
                        Get<TextBox>(
                                "QBittorrentEndpointTextBox")
                            .Text =
                            e.Configuration.Endpoint;

                        Get<TextBox>(
                                "QBittorrentUsernameTextBox")
                            .Text =
                            e.Configuration.UserName;

                        if (!string.IsNullOrWhiteSpace(
                                e.Configuration.Secret))
                        {
                            Get<TextBox>(
                                    "QBittorrentPasswordTextBox")
                                .Text =
                                e.Configuration.Secret;
                        }

                        QBittorrentSaveTestButton_OnClick(
                            null,
                            new RoutedEventArgs());
                        break;

                    case UnifiedDownloadClientAction.ClearCredential:
                        QBittorrentClearSavedPasswordButton_OnClick(
                            null,
                            new RoutedEventArgs());
                        break;
                }
            };
    }

    private void WireSharedWindowsLifecycle()
    {
        if (_sharedLifecycleView is null)
            return;

        _sharedLifecycleView.ActionRequested +=
            (_, e) =>
            {
                if (e.Action ==
                    UnifiedLifecycleAction.Refresh)
                {
                    MediaLifecycleRefreshButton_OnClick(
                        null,
                        new RoutedEventArgs());
                }
            };
    }

    private void UpdateSharedWindowsMediaHub()
    {
        if (_sharedMediaHubView is null)
            return;

        var integrations =
            SharedLegacyItems(
                "IntegrationsList");

        var products =
            integrations
                .Select(item =>
                {
                    var name =
                        LegacyMediaProjection.First(
                            item,
                            "Application",
                            "Name");

                    var state =
                        LegacyMediaProjection.First(
                            item,
                            "--",
                            "State");

                    var kind =
                        LegacyMediaProjection.First(
                            item,
                            "Applications",
                            "Kind");

                    var evidence =
                        LegacyMediaProjection.Text(
                            item,
                            "Evidence");

                    var instance =
                        new UnifiedMediaInstanceRow(
                            SharedLegacyKey(
                                item,
                                "Name",
                                "Key"),
                            name,
                            state,
                            kind,
                            string.Empty,
                            evidence);

                    return
                        new UnifiedMediaProductRow(
                            instance.Key,
                            name,
                            kind,
                            state,
                            evidence,
                            new[]
                            {
                                instance
                            },
                            CanOpen:
                                CanOpenSharedWindowsProduct(
                                    name),
                            CanEditIdentity:
                                false);
                })
                .ToArray();

        var healthy =
            products.Count(row =>
                row.State.Contains(
                    "run",
                    StringComparison.OrdinalIgnoreCase) ||
                row.State.Contains(
                    "healthy",
                    StringComparison.OrdinalIgnoreCase) ||
                row.State.Contains(
                    "ready",
                    StringComparison.OrdinalIgnoreCase));

        var offline =
            products.Count(row =>
                row.State.Contains(
                    "offline",
                    StringComparison.OrdinalIgnoreCase) ||
                row.State.Contains(
                    "down",
                    StringComparison.OrdinalIgnoreCase) ||
                row.State.Contains(
                    "fail",
                    StringComparison.OrdinalIgnoreCase));

        var attention =
            Math.Max(
                0,
                products.Length -
                healthy -
                offline);

        _sharedMediaHubView.Update(
            new UnifiedMediaHubState(
                "Captured by the active Windows target",
                ActiveTargetDisplayName(),
                healthy.ToString(),
                attention.ToString(),
                offline.ToString(),
                products.Length == 0
                    ? "No applications reported by the active provider."
                    : $"{products.Length} application(s) grouped from provider discovery.",
                products,
                UnifiedMediaHubMode.Fleet,
                false,
                CanRefresh:
                    true,
                CanShowHidden:
                    false,
                IdentityAvailable:
                    false,
                "--",
                "The Windows provider reports application inventory. Identity overrides remain unavailable.",
                Array.Empty<UnifiedIdentityRow>(),
                "Windows application inventory is read-only."));
    }

    private void UpdateSharedWindowsPlex()
    {
        if (_sharedPlexView is null)
            return;

        var sessions =
            SharedLegacyItems(
                    "PlexSessionsList")
                .Select(item =>
                    new UnifiedPlexSessionRow(
                        SharedLegacyKey(
                            item,
                            "Key",
                            "Title",
                            "Player"),
                        LegacyMediaProjection.Text(
                            item,
                            "Title"),
                        LegacyMediaProjection.Text(
                            item,
                            "User"),
                        LegacyMediaProjection.Text(
                            item,
                            "Player"),
                        LegacyMediaProjection.Text(
                            item,
                            "State"),
                        LegacyMediaProjection.Text(
                            item,
                            "Progress"),
                        LegacyMediaProjection.Text(
                            item,
                            "VideoDecision"),
                        LegacyMediaProjection.Text(
                            item,
                            "AudioDecision"),
                        LegacyMediaProjection.Text(
                            item,
                            "Bandwidth"),
                        LegacyMediaProjection.Text(
                            item,
                            "Detail")))
                .ToArray();

        _sharedPlexView.Update(
            new UnifiedPlexState(
                SharedLegacyText(
                    "PlexTargetText"),
                SharedLegacyText(
                    "PlexFreshnessText"),
                SharedLegacyText(
                    "PlexServiceText"),
                SharedLegacyText(
                    "PlexServiceDetailText"),
                SharedLegacyText(
                    "PlexVersionText"),
                SharedLegacyText(
                    "PlexEndpointTextBox"),
                SharedLegacyText(
                    "PlexConnectionText"),
                "Windows target provider",
                SharedLegacyText(
                    "PlexActiveSessionsText"),
                SharedLegacyText(
                    "PlexDirectPlayText"),
                SharedLegacyText(
                    "PlexDirectStreamText"),
                SharedLegacyText(
                    "PlexTranscodeText"),
                SharedLegacyText(
                    "PlexLibrariesText"),
                "Bandwidth: " +
                SharedLegacyText(
                    "PlexBandwidthText"),
                SharedLegacyText(
                    "PlexDiscoveryEvidenceText"),
                SharedLegacyText(
                    "PlexSessionCountText"),
                sessions,
                SharedLegacyText(
                    "PlexSessionsEmptyText"),
                SharedLegacyText(
                    "PlexSecurityText"),
                SharedLegacyText(
                    "PlexStatusText"),
                CanRefresh:
                    true,
                CanOpen:
                    true,
                CanRestart:
                    false,
                CanOpenLogs:
                    false,
                CanOpenTerminal:
                    false,
                CanOpenIntelligence:
                    false,
                ConfigEditable:
                    true,
                ConfigEndpoint:
                    SharedLegacyText(
                        "PlexEndpointTextBox"),
                ConfigEvidence:
                    SharedLegacyText(
                        "PlexDiscoveryEvidenceText"),
                ConfigStatus:
                    SharedLegacyText(
                        "PlexStatusText")));
    }

    private void UpdateSharedWindowsArr()
    {
        if (_sharedArrView is null)
            return;

        var instances =
            SharedLegacyItems(
                    "ArrInstanceTelemetryList")
                .Select(item =>
                    new UnifiedArrInstanceRow(
                        SharedLegacyKey(
                            item,
                            "Key",
                            "DisplayName",
                            "Endpoint"),
                        LegacyMediaProjection.Text(
                            item,
                            "DisplayName"),
                        LegacyMediaProjection.Text(
                            item,
                            "State"),
                        LegacyMediaProjection.Text(
                            item,
                            "Endpoint"),
                        LegacyMediaProjection.Text(
                            item,
                            "Version"),
                        LegacyMediaProjection.Text(
                            item,
                            "Work"),
                        LegacyMediaProjection.Text(
                            item,
                            "Health"),
                        LegacyMediaProjection.Text(
                            item,
                            "Detail")))
                .ToArray();

        var work =
            SharedLegacyItems(
                    "ArrQueueHealthList")
                .Select(item =>
                    new UnifiedMediaWorkRow(
                        SharedLegacyKey(
                            item,
                            "Key",
                            "ItemIssue",
                            "DisplayName"),
                        LegacyMediaProjection.First(
                            item,
                            SharedLegacyText(
                                "ArrProductTitleText"),
                            "Service",
                            "DisplayName"),
                        LegacyMediaProjection.Text(
                            item,
                            "Type"),
                        LegacyMediaProjection.Text(
                            item,
                            "ItemIssue"),
                        LegacyMediaProjection.Text(
                            item,
                            "State"),
                        LegacyMediaProjection.Text(
                            item,
                            "Progress"),
                        LegacyMediaProjection.Text(
                            item,
                            "Remaining"),
                        LegacyMediaProjection.Text(
                            item,
                            "Detail")))
                .ToArray();

        _sharedArrView.Update(
            new UnifiedArrState(
                SharedLegacyText(
                    "ArrProductTitleText"),
                SharedLegacyText(
                    "ArrProductSubtitleText"),
                SharedLegacyText(
                    "ArrTargetText"),
                SharedLegacyText(
                    "ArrFreshnessText"),
                SharedLegacyText(
                    "ArrInstanceCountText"),
                SharedLegacyText(
                    "ArrStateMetricText"),
                SharedLegacyText(
                    "ArrVersionMetricText"),
                SharedLegacyText(
                    "ArrWorkMetricLabelText"),
                SharedLegacyText(
                    "ArrWorkMetricText"),
                SharedLegacyText(
                    "ArrWorkMetricHintText"),
                SharedLegacyText(
                    "ArrHealthMetricText"),
                "Windows telemetry and connection tools stay together.",
                instances,
                SharedLegacyText(
                    "ArrWorkSectionTitleText"),
                SharedLegacyText(
                    "ArrWorkSectionSubtitleText"),
                work,
                SharedLegacyText(
                    "ArrStatusText"),
                CanRefresh:
                    true,
                CanOpen:
                    Get<Button>(
                            "ArrOpenButton")
                        .IsEnabled,
                CanOpenDetail:
                    Get<Button>(
                            "ArrOpenButton")
                        .IsEnabled,
                CanOpenDocker:
                    false,
                CanOpenLogs:
                    false,
                CanOpenIntelligence:
                    false,
                ConfigEditable:
                    true,
                ConfigEndpoint:
                    SharedLegacyText(
                        "ArrEndpointTextBox"),
                ConfigEvidence:
                    SharedLegacyText(
                        "ArrDiscoveryEvidenceText"),
                Security:
                    SharedLegacyText(
                        "ArrSecurityText"),
                Status:
                    SharedLegacyText(
                        "ArrStatusText"),
                Customization:
                    new UnifiedArrCustomization(
                        false,
                        string.Empty,
                        string.Empty,
                        string.Empty,
                        false,
                        string.Empty,
                        "Linux workspace customization is not reported by the Windows provider.")));
    }

    private void UpdateSharedWindowsSabnzbd()
    {
        if (_sharedSabnzbdView is null)
            return;

        _sharedSabnzbdView.Update(
            new UnifiedDownloadClientState(
                "SABnzbd",
                "Usenet queue analytics, progress, history and protected connection telemetry.",
                SharedLegacyText(
                    "SABnzbdTargetText"),
                SharedLegacyText(
                    "SABnzbdFreshnessText"),
                SharedLegacyText(
                    "SABnzbdStateText"),
                SharedLegacyText(
                    "SABnzbdSecurityText"),
                SharedLegacyText(
                    "SABnzbdVersionText"),
                SharedLegacyText(
                    "SABnzbdConnectionText"),
                SharedLegacyText(
                    "SABnzbdActiveText"),
                SharedLegacyText(
                    "SABnzbdQueueText"),
                "ITEMS",
                SharedLegacyText(
                    "SABnzbdQueueCountText"),
                "Recent failures: " +
                SharedLegacyText(
                    "SABnzbdFailedRecentText"),
                "DOWNLOAD",
                SharedLegacyText(
                    "SABnzbdDownloadSpeedText"),
                "REMAINING",
                SharedLegacyText(
                    "SABnzbdRemainingText"),
                "ETA",
                SharedLegacyText(
                    "SABnzbdEtaText"),
                "FAILED RECENT",
                SharedLegacyText(
                    "SABnzbdFailedRecentText"),
                "Read-only analytics are automatic; Web UI and protected configuration remain explicit.",
                SharedLegacyText(
                    "SABnzbdTransferAnalyticsText"),
                SharedLegacyText(
                    "SABnzbdHistoryAnalyticsText"),
                "Current queue",
                SharedLegacyText(
                    "SABnzbdQueueEmptyText"),
                SharedLegacyItems(
                        "SABnzbdQueueList")
                    .Select(ProjectLegacyTransfer)
                    .ToArray(),
                "Recent history",
                SharedLegacyText(
                    "SABnzbdHistoryEmptyText"),
                SharedLegacyItems(
                        "SABnzbdHistoryList")
                    .Select(ProjectLegacyTransfer)
                    .ToArray(),
                SharedLegacyText(
                    "SABnzbdStatusText"),
                CanRefresh:
                    true,
                CanOpen:
                    true,
                CanOpenDocker:
                    false,
                CanOpenLogs:
                    false,
                CanOpenTerminal:
                    false,
                ConfigEditable:
                    true,
                ConfigEndpoint:
                    SharedLegacyText(
                        "SABnzbdEndpointTextBox"),
                UserNameLabel:
                    string.Empty,
                ConfigUserName:
                    string.Empty,
                SecretLabel:
                    "API key",
                ConfigEvidence:
                    SharedLegacyText(
                        "SABnzbdDiscoveryEvidenceText")));
    }

    private void UpdateSharedWindowsQBittorrent()
    {
        if (_sharedQBittorrentView is null)
            return;

        _sharedQBittorrentView.Update(
            new UnifiedDownloadClientState(
                "qBittorrent",
                "Torrent transfer analytics, progress, seeding and protected connection telemetry.",
                SharedLegacyText(
                    "QBittorrentTargetText"),
                SharedLegacyText(
                    "QBittorrentFreshnessText"),
                SharedLegacyText(
                    "QBittorrentStateText"),
                SharedLegacyText(
                    "QBittorrentSecurityText"),
                SharedLegacyText(
                    "QBittorrentVersionText"),
                SharedLegacyText(
                    "QBittorrentConnectionText"),
                SharedLegacyText(
                    "QBittorrentActiveText"),
                SharedLegacyText(
                    "QBittorrentTotalText"),
                "TORRENTS",
                SharedLegacyText(
                    "QBittorrentTotalText"),
                "Categories: " +
                SharedLegacyText(
                    "QBittorrentCategoryCountText"),
                "DOWNLOAD",
                SharedLegacyText(
                    "QBittorrentDownloadSpeedText"),
                "UPLOAD",
                SharedLegacyText(
                    "QBittorrentUploadSpeedText"),
                "REMAINING",
                SharedLegacyText(
                    "QBittorrentRemainingText"),
                "ETA",
                SharedLegacyText(
                    "QBittorrentEtaText"),
                "Read-only analytics are automatic; Web UI and protected configuration remain explicit.",
                SharedLegacyText(
                    "QBittorrentTransferAnalyticsText"),
                SharedLegacyText(
                    "QBittorrentWorkloadAnalyticsText"),
                "Current torrents",
                SharedLegacyText(
                    "QBittorrentQueueEmptyText"),
                SharedLegacyItems(
                        "QBittorrentQueueList")
                    .Select(ProjectLegacyTransfer)
                    .ToArray(),
                "Recent history",
                SharedLegacyText(
                    "QBittorrentHistoryEmptyText"),
                SharedLegacyItems(
                        "QBittorrentHistoryList")
                    .Select(ProjectLegacyTransfer)
                    .ToArray(),
                SharedLegacyText(
                    "QBittorrentStatusText"),
                CanRefresh:
                    true,
                CanOpen:
                    true,
                CanOpenDocker:
                    false,
                CanOpenLogs:
                    false,
                CanOpenTerminal:
                    false,
                ConfigEditable:
                    true,
                ConfigEndpoint:
                    SharedLegacyText(
                        "QBittorrentEndpointTextBox"),
                UserNameLabel:
                    "User name",
                ConfigUserName:
                    SharedLegacyText(
                        "QBittorrentUsernameTextBox"),
                SecretLabel:
                    "Password",
                ConfigEvidence:
                    SharedLegacyText(
                        "QBittorrentDiscoveryEvidenceText")));
    }

    private void UpdateSharedWindowsLifecycle()
    {
        if (_sharedLifecycleView is null)
            return;

        var items =
            SharedLegacyItems(
                    "MediaLifecycleItemsList")
                .Select(item =>
                    new UnifiedLifecycleItemRow(
                        SharedLegacyKey(
                            item,
                            "Key",
                            "Title"),
                        LegacyMediaProjection.Text(
                            item,
                            "Title"),
                        LegacyMediaProjection.First(
                            item,
                            LegacyMediaProjection.Text(
                                item,
                                "ManagedBy"),
                            "ManagedBy",
                            "Source"),
                        LegacyMediaProjection.Text(
                            item,
                            "Stage"),
                        LegacyMediaProjection.Text(
                            item,
                            "State"),
                        LegacyMediaProjection.Text(
                            item,
                            "Progress"),
                        LegacyMediaProjection.Text(
                            item,
                            "Remaining"),
                        LegacyMediaProjection.Text(
                            item,
                            "MediaType"),
                        LegacyMediaProjection.Text(
                            item,
                            "Confidence"),
                        LegacyMediaProjection.Text(
                            item,
                            "Evidence",
                            "Summary")))
                .ToArray();

        var stages =
            items
                .GroupBy(
                    item => item.Stage,
                    StringComparer.OrdinalIgnoreCase)
                .Select(group =>
                    new UnifiedLifecycleStageRow(
                        group.Key,
                        group.Key,
                        group.Count()
                            .ToString(),
                        string.Join(
                            ", ",
                            group.Select(item =>
                                    item.State)
                                .Where(value =>
                                    !string.IsNullOrWhiteSpace(
                                        value))
                                .Distinct(
                                    StringComparer.OrdinalIgnoreCase))))
                .ToArray();

        var selected =
            items.FirstOrDefault();

        _sharedLifecycleView.Update(
            new UnifiedLifecycleState(
                SharedLegacyText(
                    "MediaLifecycleTotalText"),
                SharedLegacyText(
                    "MediaLifecycleAttentionText"),
                SharedLegacyText(
                    "MediaLifecycleTransferText"),
                SharedLegacyText(
                    "MediaLifecycleProcessingText"),
                SharedLegacyText(
                    "MediaLifecyclePlayingText"),
                SharedLegacyText(
                    "MediaLifecycleStateText"),
                stages,
                items,
                Array.Empty<UnifiedRemediationRow>(),
                selected?.Item ??
                "No lifecycle item selected",
                selected?.Evidence ??
                string.Empty,
                SharedLegacyText(
                    "MediaLifecycleSourceSummaryText"),
                SharedLegacyText(
                    "MediaLifecycleStatusText"),
                CanRefresh:
                    true,
                CanOpenOwner:
                    false,
                CanOpenIntelligence:
                    false));
    }

    private UnifiedTransferRow ProjectLegacyTransfer(
        object item) =>
        new(
            SharedLegacyKey(
                item,
                "Key",
                "Name",
                "Title"),
            LegacyMediaProjection.Text(
                item,
                "Name",
                "Title"),
            LegacyMediaProjection.Text(
                item,
                "Category"),
            LegacyMediaProjection.Text(
                item,
                "State",
                "Status"),
            LegacyMediaProjection.Text(
                item,
                "Progress"),
            LegacyMediaProjection.Text(
                item,
                "Size"),
            LegacyMediaProjection.Text(
                item,
                "Remaining"),
            LegacyMediaProjection.Text(
                item,
                "DownloadSpeed"),
            LegacyMediaProjection.Text(
                item,
                "UploadSpeed"),
            LegacyMediaProjection.Text(
                item,
                "Eta"),
            LegacyMediaProjection.Text(
                item,
                "Peers"),
            LegacyMediaProjection.Text(
                item,
                "Ratio"),
            LegacyMediaProjection.Text(
                item,
                "Added"),
            LegacyMediaProjection.Text(
                item,
                "Completed"),
            LegacyMediaProjection.Text(
                item,
                "Duration"),
            LegacyMediaProjection.Text(
                item,
                "Detail"));

    private IReadOnlyList<object> SharedLegacyItems(
        string controlName)
    {
        var list =
            Get<ListBox>(
                controlName);

        return
            LegacyMediaProjection.Items(
                list.ItemsSource);
    }

    private string SharedLegacyText(
        string controlName)
    {
        var control =
            this.FindControl<Control>(
                controlName);

        return control switch
        {
            TextBlock block =>
                block.Text ??
                string.Empty,

            TextBox box =>
                box.Text ??
                string.Empty,

            Button button =>
                Convert.ToString(
                    button.Content) ??
                string.Empty,

            ContentControl content =>
                Convert.ToString(
                    content.Content) ??
                string.Empty,

            _ =>
                string.Empty
        };
    }

    private static string SharedLegacyKey(
        object item,
        params string[] propertyNames)
    {
        var key =
            LegacyMediaProjection.Text(
                item,
                propertyNames);

        return string.IsNullOrWhiteSpace(
            key)
            ? item.GetHashCode()
                .ToString()
            : key;
    }

    private static bool CanOpenSharedWindowsProduct(
        string product) =>
        product.Equals(
            "Plex",
            StringComparison.OrdinalIgnoreCase) ||
        product.Equals(
            "Sonarr",
            StringComparison.OrdinalIgnoreCase) ||
        product.Equals(
            "Radarr",
            StringComparison.OrdinalIgnoreCase) ||
        product.Equals(
            "Lidarr",
            StringComparison.OrdinalIgnoreCase) ||
        product.Equals(
            "Prowlarr",
            StringComparison.OrdinalIgnoreCase) ||
        product.Equals(
            "SABnzbd",
            StringComparison.OrdinalIgnoreCase) ||
        product.Equals(
            "qBittorrent",
            StringComparison.OrdinalIgnoreCase);

    private void OpenSharedWindowsProduct(
        string product)
    {
        var navigation =
            product.ToLowerInvariant() switch
            {
                "plex" =>
                    "PlexNav",

                "sonarr" =>
                    "SonarrNav",

                "radarr" =>
                    "RadarrNav",

                "lidarr" =>
                    "LidarrNav",

                "prowlarr" =>
                    "ProwlarrNav",

                "sabnzbd" =>
                    "SABnzbdNav",

                "qbittorrent" =>
                    "QBittorrentNav",

                _ =>
                    string.Empty
            };

        if (!string.IsNullOrWhiteSpace(
                navigation))
        {
            Navigate(
                navigation);
        }
    }
}
