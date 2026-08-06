using System.Collections;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using GraveOps.Presentation.Avalonia.MediaWorkspaces;

namespace GraveOps.Desktop.Linux;

public partial class MainWindow
{
    private UnifiedMediaHubView?
        _sharedMediaHubView;

    private UnifiedPlexView?
        _sharedPlexView;

    private UnifiedArrView?
        _sharedArrView;

    private UnifiedDownloadClientView?
        _sharedDownloadClientView;

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

        _sharedDownloadClientView =
            new UnifiedDownloadClientView();

        _sharedLifecycleView =
            new UnifiedLifecycleView();

        ReplaceSharedMediaPage(
            "MediaHubPage",
            _sharedMediaHubView);

        ReplaceSharedMediaPage(
            "PlexWorkspacePage",
            _sharedPlexView);

        ReplaceSharedMediaPage(
            "ArrWorkspacePage",
            _sharedArrView);

        ReplaceSharedMediaPage(
            "DownloadClientWorkspacePage",
            _sharedDownloadClientView);

        ReplaceSharedMediaPage(
            "LifecyclePage",
            _sharedLifecycleView);

        WireSharedLinuxMediaHub();
        WireSharedLinuxPlex();
        WireSharedLinuxArr();
        WireSharedLinuxDownloadClient();
        WireSharedLinuxLifecycle();

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

        UpdateSharedUnifiedMediaWorkspaces();
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
                "PlexWorkspacePage")
            .IsVisible ||
        Get<Grid>(
                "ArrWorkspacePage")
            .IsVisible ||
        Get<Grid>(
                "DownloadClientWorkspacePage")
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
        UpdateSharedLinuxMediaHub();
        UpdateSharedLinuxPlex();
        UpdateSharedLinuxArr();
        UpdateSharedLinuxDownloadClient();
        UpdateSharedLinuxLifecycle();
    }

    private void WireSharedLinuxMediaHub()
    {
        if (_sharedMediaHubView is null)
            return;

        _sharedMediaHubView.RefreshRequested +=
            (_, _) =>
            {
                MediaHubRefreshButton_OnClick(
                    null,
                    new RoutedEventArgs());
            };

        _sharedMediaHubView.ShowHiddenRequested +=
            (_, _) =>
            {
                MediaHubShowHiddenButton_OnClick(
                    null,
                    new RoutedEventArgs());
            };

        _sharedMediaHubView.ModeRequested +=
            (_, e) =>
            {
                if (e.Mode ==
                    UnifiedMediaHubMode.Identity)
                {
                    MediaModeLauncherButton_OnClick(
                        null,
                        new RoutedEventArgs());
                }
                else
                {
                    MediaModeFleetButton_OnClick(
                        null,
                        new RoutedEventArgs());
                }
            };

        _sharedMediaHubView.ProductOpenRequested +=
            (_, e) =>
            {
                var button =
                    new Button
                    {
                        Tag =
                            e.Row.Key
                    };

                MediaCardOpenButton_OnClick(
                    button,
                    new RoutedEventArgs());
            };

        _sharedMediaHubView.ProductIdentityRequested +=
            (_, e) =>
            {
                var button =
                    new Button
                    {
                        Tag =
                            e.Row.Key
                    };

                MediaGroupIdentityButton_OnClick(
                    button,
                    new RoutedEventArgs());
            };

        _sharedMediaHubView.IdentitySelectionRequested +=
            (_, e) =>
            {
                SelectLegacyMediaItem(
                    "MediaLauncherSettingsList",
                    e.Row.Key,
                    "SourceKey",
                    "Key");
            };

        _sharedMediaHubView.IdentitySaveRequested +=
            (_, e) =>
            {
                SelectLegacyMediaItem(
                    "MediaLauncherSettingsList",
                    e.Request.Key,
                    "SourceKey",
                    "Key");

                SetLegacyCombo(
                    "IdentityProductComboBox",
                    e.Request.Product);

                SetLegacyCombo(
                    "IdentityRoleComboBox",
                    e.Request.Role);

                Get<TextBox>(
                        "IdentityProtocolTextBox")
                    .Text =
                    e.Request.Protocol;

                Get<TextBox>(
                        "MediaLauncherDisplayNameTextBox")
                    .Text =
                    e.Request.DisplayName;

                SetLegacyCombo(
                    "IdentityParentComboBox",
                    e.Request.Parent);

                Get<TextBox>(
                        "MediaLauncherUrlTextBox")
                    .Text =
                    e.Request.Url;

                Get<TextBox>(
                        "MediaLauncherCategoryTextBox")
                    .Text =
                    e.Request.Category;

                Get<CheckBox>(
                        "IdentityOwnsHealthCheckBox")
                    .IsChecked =
                    e.Request.OwnsHealth;

                Get<CheckBox>(
                        "IdentityShowNavigationCheckBox")
                    .IsChecked =
                    e.Request.ShowNavigation;

                Get<CheckBox>(
                        "MediaLauncherVisibleCheckBox")
                    .IsChecked =
                    e.Request.IsVisible;

                MediaLauncherSaveButton_OnClick(
                    null,
                    new RoutedEventArgs());
            };

        _sharedMediaHubView.IdentityResetRequested +=
            (_, e) =>
            {
                SelectLegacyMediaItem(
                    "MediaLauncherSettingsList",
                    e.Row.Key,
                    "SourceKey",
                    "Key");

                MediaLauncherResetButton_OnClick(
                    null,
                    new RoutedEventArgs());
            };

        _sharedMediaHubView.IdentityOpenRequested +=
            (_, e) =>
            {
                SelectLegacyMediaItem(
                    "MediaLauncherSettingsList",
                    e.Row.Key,
                    "SourceKey",
                    "Key");

                MediaLauncherOpenButton_OnClick(
                    null,
                    new RoutedEventArgs());
            };
    }

    private void WireSharedLinuxPlex()
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

                    case UnifiedPlexAction.Restart:
                        PlexRestartButton_OnClick(
                            null,
                            new RoutedEventArgs());
                        break;

                    case UnifiedPlexAction.Logs:
                        PlexLogsButton_OnClick(
                            null,
                            new RoutedEventArgs());
                        break;

                    case UnifiedPlexAction.Terminal:
                        PlexTerminalButton_OnClick(
                            null,
                            new RoutedEventArgs());
                        break;

                    case UnifiedPlexAction.Intelligence:
                        PlexIntelligenceButton_OnClick(
                            null,
                            new RoutedEventArgs());
                        break;
                }
            };
    }

    private void WireSharedLinuxArr()
    {
        if (_sharedArrView is null)
            return;

        _sharedArrView.ActionRequested +=
            (_, e) =>
            {
                switch (e.Request.Action)
                {
                    case UnifiedArrAction.Refresh:
                        ArrRefreshTelemetryButton_OnClick(
                            null,
                            new RoutedEventArgs());
                        break;

                    case UnifiedArrAction.Open:
                        RaiseFirstLegacyButton(
                            "ArrOpenButtonsPanel");
                        break;

                    case UnifiedArrAction.OpenDetail:
                        ArrOpenNativeDetailButton_OnClick(
                            null,
                            new RoutedEventArgs());
                        break;

                    case UnifiedArrAction.Docker:
                        ArrDockerButton_OnClick(
                            null,
                            new RoutedEventArgs());
                        break;

                    case UnifiedArrAction.Logs:
                        ArrLogsButton_OnClick(
                            null,
                            new RoutedEventArgs());
                        break;

                    case UnifiedArrAction.Intelligence:
                        ArrWorkspaceIntelligenceButton_OnClick(
                            null,
                            new RoutedEventArgs());
                        break;

                    case UnifiedArrAction.SaveCustomization:
                        Get<TextBox>(
                                "ArrFriendlyNameTextBox")
                            .Text =
                            e.Request.Customization.FriendlyName;

                        Get<TextBox>(
                                "ArrRoleTextBox")
                            .Text =
                            e.Request.Customization.Role;

                        Get<TextBox>(
                                "ArrConfigPathTextBox")
                            .Text =
                            e.Request.Customization.ConfigPath;

                        Get<CheckBox>(
                                "ArrPrivacyModeCheckBox")
                            .IsChecked =
                            e.Request.Customization.PrivacyMode;

                        SaveArrWorkspaceButton_OnClick(
                            null,
                            new RoutedEventArgs());
                        break;

                    case UnifiedArrAction.ResetCustomization:
                        ResetArrWorkspaceButton_OnClick(
                            null,
                            new RoutedEventArgs());
                        break;
                }
            };
    }

    private void WireSharedLinuxDownloadClient()
    {
        if (_sharedDownloadClientView is null)
            return;

        _sharedDownloadClientView.ActionRequested +=
            (_, e) =>
            {
                switch (e.Action)
                {
                    case UnifiedDownloadClientAction.Refresh:
                        DownloadClientRefreshButton_OnClick(
                            null,
                            new RoutedEventArgs());
                        break;

                    case UnifiedDownloadClientAction.Open:
                        DownloadClientOpenButton_OnClick(
                            null,
                            new RoutedEventArgs());
                        break;

                    case UnifiedDownloadClientAction.Docker:
                        DownloadClientDockerButton_OnClick(
                            null,
                            new RoutedEventArgs());
                        break;

                    case UnifiedDownloadClientAction.Logs:
                        DownloadClientLogsButton_OnClick(
                            null,
                            new RoutedEventArgs());
                        break;

                    case UnifiedDownloadClientAction.Terminal:
                        DownloadClientTerminalButton_OnClick(
                            null,
                            new RoutedEventArgs());
                        break;
                }
            };
    }

    private void WireSharedLinuxLifecycle()
    {
        if (_sharedLifecycleView is null)
            return;

        _sharedLifecycleView.ItemSelectionRequested +=
            (_, e) =>
            {
                SelectLegacyMediaItem(
                    "LifecycleItemsList",
                    e.Row.Key,
                    "Item",
                    "Title",
                    "Key");
            };

        _sharedLifecycleView.RemediationSelectionRequested +=
            (_, e) =>
            {
                SelectLegacyMediaItem(
                    "LifecycleRemediationList",
                    e.Row.Key,
                    "Step",
                    "Component",
                    "Key");
            };

        _sharedLifecycleView.ActionRequested +=
            async (_, e) =>
            {
                switch (e.Action)
                {
                    case UnifiedLifecycleAction.Refresh:
                        await RefreshAsync();
                        break;

                    case UnifiedLifecycleAction.OpenOwner:
                        LifecycleOpenOwnerButton_OnClick(
                            null,
                            new RoutedEventArgs());
                        break;

                    case UnifiedLifecycleAction.Intelligence:
                        LifecycleOpenIntelligenceButton_OnClick(
                            null,
                            new RoutedEventArgs());
                        break;
                }
            };
    }

    private void UpdateSharedLinuxMediaHub()
    {
        if (_sharedMediaHubView is null)
            return;

        var products =
            SharedLegacyItems(
                    "MediaCategoryGroupsList")
                .Select(item =>
                {
                    var instances =
                        LegacyMediaProjection
                            .PropertyItems(
                                item,
                                "Instances")
                            .Select(instance =>
                                new UnifiedMediaInstanceRow(
                                    SharedLegacyKey(
                                        instance,
                                        "SourceKey",
                                        "Key",
                                        "DisplayName"),
                                    LegacyMediaProjection.First(
                                        instance,
                                        "Application",
                                        "DisplayName",
                                        "Name"),
                                    LegacyMediaProjection.First(
                                        instance,
                                        "--",
                                        "StateLabel",
                                        "State"),
                                    LegacyMediaProjection.Text(
                                        instance,
                                        "MetaText",
                                        "Role",
                                        "Kind"),
                                    LegacyMediaProjection.Text(
                                        instance,
                                        "EndpointText",
                                        "Endpoint",
                                        "FullEndpointText"),
                                    LegacyMediaProjection.Text(
                                        instance,
                                        "Detail",
                                        "Evidence")))
                            .ToArray();

                    var key =
                        LegacyMediaProjection.First(
                            item,
                            LegacyMediaProjection.First(
                                item,
                                "Application",
                                "ProductName",
                                "Product"),
                            "PrimarySourceKey",
                            "Key",
                            "ProductName");

                    return
                        new UnifiedMediaProductRow(
                            key,
                            LegacyMediaProjection.First(
                                item,
                                "Application",
                                "ProductName",
                                "Product"),
                            LegacyMediaProjection.First(
                                item,
                                "Applications",
                                "Category"),
                            LegacyMediaProjection.First(
                                item,
                                "--",
                                "StateLabel",
                                "State"),
                            LegacyMediaProjection.Text(
                                item,
                                "SummaryText",
                                "Summary"),
                            instances,
                            CanOpen:
                                true,
                            CanEditIdentity:
                                true);
                })
                .ToArray();

        var identityRows =
            SharedLegacyItems(
                    "MediaLauncherSettingsList")
                .Select(item =>
                    new UnifiedIdentityRow(
                        SharedLegacyKey(
                            item,
                            "SourceKey",
                            "Key",
                            "DisplayName"),
                        LegacyMediaProjection.First(
                            item,
                            "Application",
                            "DisplayName",
                            "Name"),
                        LegacyMediaProjection.Text(
                            item,
                            "Product"),
                        LegacyMediaProjection.Text(
                            item,
                            "Role"),
                        LegacyMediaProjection.Text(
                            item,
                            "Protocol",
                            "ApiProtocol"),
                        LegacyMediaProjection.Text(
                            item,
                            "Parent",
                            "ParentProduct"),
                        LegacyMediaProjection.Text(
                            item,
                            "Url",
                            "Endpoint",
                            "SourceSummary"),
                        LegacyMediaProjection.Text(
                            item,
                            "Category"),
                        LegacyMediaProjection.Text(
                            item,
                            "VerificationLabel",
                            "Verification"),
                        LegacyMediaProjection.Text(
                            item,
                            "Detected",
                            "SourceSummary"),
                        LegacyMediaProjection.Bool(
                            item,
                            false,
                            "OwnsHealth"),
                        LegacyMediaProjection.Bool(
                            item,
                            true,
                            "ShowNavigation"),
                        LegacyMediaProjection.Bool(
                            item,
                            true,
                            "IsVisible",
                            "Visible")))
                .ToArray();

        var mode =
            Get<Grid>(
                    "MediaLauncherSettingsPanel")
                .IsVisible
                ? UnifiedMediaHubMode.Identity
                : UnifiedMediaHubMode.Fleet;

        _sharedMediaHubView.Update(
            new UnifiedMediaHubState(
                SharedLegacyText(
                    "MediaHubSampleAgeText"),
                SharedLegacyText(
                    "MediaTargetMetricText"),
                SharedLegacyText(
                    "MediaHealthyMetricText"),
                SharedLegacyText(
                    "MediaAttentionMetricText"),
                SharedLegacyText(
                    "MediaOfflineMetricText"),
                SharedLegacyText(
                    "MediaFleetGroupingSummaryText"),
                products,
                mode,
                SharedLegacyText(
                        "MediaHubShowHiddenButton")
                    .Contains(
                        "Hide",
                        StringComparison.OrdinalIgnoreCase),
                CanRefresh:
                    Get<Button>(
                            "MediaHubRefreshButton")
                        .IsEnabled,
                CanShowHidden:
                    Get<Button>(
                            "MediaHubShowHiddenButton")
                        .IsEnabled,
                IdentityAvailable:
                    true,
                SharedLegacyText(
                    "MediaLauncherStorePathText"),
                SharedLegacyText(
                    "IdentityRegistrySummaryText"),
                identityRows,
                SharedLegacyText(
                    "MediaLauncherStatusText")));
    }

    private void UpdateSharedLinuxPlex()
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
                    "PlexEndpointText"),
                SharedLegacyText(
                    "PlexConnectionText"),
                SharedLegacyText(
                    "PlexDependencyText"),
                SharedLegacyText(
                    "PlexActiveSessionsText"),
                SharedLegacyText(
                    "PlexDirectPlayText"),
                string.Empty,
                SharedLegacyText(
                    "PlexTranscodeText"),
                SharedLegacyText(
                    "PlexLibrariesText"),
                SharedLegacyText(
                    "PlexPlaybackAnalyticsText"),
                SharedLegacyText(
                    "PlexServerContextText"),
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
                    Get<Button>(
                            "PlexRefreshButton")
                        .IsEnabled,
                CanOpen:
                    Get<Button>(
                            "PlexOpenButton")
                        .IsEnabled,
                CanRestart:
                    Get<Button>(
                            "PlexRestartButton")
                        .IsEnabled,
                CanOpenLogs:
                    true,
                CanOpenTerminal:
                    true,
                CanOpenIntelligence:
                    true,
                ConfigEditable:
                    false,
                ConfigEndpoint:
                    string.Empty,
                ConfigEvidence:
                    "Plex connection discovery remains owned by the Linux target.",
                ConfigStatus:
                    SharedLegacyText(
                        "PlexOperationsStatusText")));
    }

    private void UpdateSharedLinuxArr()
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
                            "DisplayName",
                            "Name"),
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
                            "Service"),
                        LegacyMediaProjection.Text(
                            item,
                            "Service"),
                        LegacyMediaProjection.Text(
                            item,
                            "Type"),
                        LegacyMediaProjection.Text(
                            item,
                            "ItemIssue",
                            "Item"),
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

        var modules =
            string.Join(
                ", ",
                Get<StackPanel>(
                        "ArrWorkspaceModulesPanel")
                    .Children
                    .OfType<CheckBox>()
                    .Where(checkBox =>
                        checkBox.IsChecked ==
                        true)
                    .Select(checkBox =>
                        Convert.ToString(
                            checkBox.Content) ??
                        string.Empty)
                    .Where(value =>
                        !string.IsNullOrWhiteSpace(
                            value)));

        _sharedArrView.Update(
            new UnifiedArrState(
                SharedLegacyText(
                    "ArrApplicationTitleText"),
                SharedLegacyText(
                    "ArrApplicationSubtitleText"),
                _controlPlane
                    .ActiveProfile
                    .DisplayName,
                SharedLegacyText(
                    "ArrLiveUpdatedText"),
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
                    "ArrQueueFooterText"),
                SharedLegacyText(
                    "ArrHealthMetricText"),
                SharedLegacyText(
                    "ArrOperationsSubtitleText"),
                instances,
                SharedLegacyText(
                    "ArrWorkSectionTitleText"),
                SharedLegacyText(
                    "ArrWorkSectionSubtitleText"),
                work,
                SharedLegacyText(
                    "ArrQueueFooterText"),
                CanRefresh:
                    Get<Button>(
                            "ArrRefreshTelemetryButton")
                        .IsEnabled,
                CanOpen:
                    true,
                CanOpenDetail:
                    true,
                CanOpenDocker:
                    true,
                CanOpenLogs:
                    true,
                CanOpenIntelligence:
                    true,
                ConfigEditable:
                    false,
                ConfigEndpoint:
                    string.Empty,
                ConfigEvidence:
                    SharedLegacyText(
                        "ArrWorkspaceConfigPathText"),
                Security:
                    "Protected live telemetry remains owned by the Linux target.",
                Status:
                    SharedLegacyText(
                        "ArrWorkspaceProfileStatusText"),
                Customization:
                    new UnifiedArrCustomization(
                        Available:
                            true,
                        FriendlyName:
                            SharedLegacyText(
                                "ArrFriendlyNameTextBox"),
                        Role:
                            SharedLegacyText(
                                "ArrRoleTextBox"),
                        ConfigPath:
                            SharedLegacyText(
                                "ArrConfigPathTextBox"),
                        PrivacyMode:
                            Get<CheckBox>(
                                    "ArrPrivacyModeCheckBox")
                                .IsChecked ==
                            true,
                        Modules:
                            string.IsNullOrWhiteSpace(
                                modules)
                                ? "No workspace modules enabled."
                                : modules,
                        Status:
                            SharedLegacyText(
                                "ArrWorkspaceProfileStatusText"))));
    }

    private void UpdateSharedLinuxDownloadClient()
    {
        if (_sharedDownloadClientView is null)
            return;

        var product =
            SharedLegacyText(
                "DownloadClientHeadingText");

        var qbit =
            product.Contains(
                "qBittorrent",
                StringComparison.OrdinalIgnoreCase);

        var queueName =
            qbit
                ? "DownloadClientQueueList"
                : "DownloadClientSabQueueList";

        var queue =
            SharedLegacyItems(
                    queueName)
                .Select(ProjectLegacyTransfer)
                .ToArray();

        var history =
            SharedLegacyItems(
                    "DownloadClientHistoryList")
                .Select(ProjectLegacyTransfer)
                .ToArray();

        _sharedDownloadClientView.Update(
            new UnifiedDownloadClientState(
                product,
                SharedLegacyText(
                    "DownloadClientDescriptionText"),
                SharedLegacyText(
                    "DownloadClientTargetText"),
                SharedLegacyText(
                    "DownloadClientFreshnessText"),
                SharedLegacyText(
                    "DownloadClientStateText"),
                SharedLegacyText(
                    "DownloadClientSecurityText"),
                SharedLegacyText(
                    "DownloadClientVersionText"),
                SharedLegacyText(
                    "DownloadClientConnectionText"),
                SharedLegacyText(
                    "DownloadClientActiveText"),
                SharedLegacyText(
                    "DownloadClientActiveDetailText"),
                SharedLegacyText(
                    "DownloadClientItemsLabelText"),
                SharedLegacyText(
                    "DownloadClientItemsText"),
                SharedLegacyText(
                    "DownloadClientItemsDetailText"),
                SharedLegacyText(
                    "DownloadClientMetric1LabelText"),
                SharedLegacyText(
                    "DownloadClientMetric1ValueText"),
                SharedLegacyText(
                    "DownloadClientMetric2LabelText"),
                SharedLegacyText(
                    "DownloadClientMetric2ValueText"),
                SharedLegacyText(
                    "DownloadClientMetric3LabelText"),
                SharedLegacyText(
                    "DownloadClientMetric3ValueText"),
                SharedLegacyText(
                    "DownloadClientMetric4LabelText"),
                SharedLegacyText(
                    "DownloadClientMetric4ValueText"),
                SharedLegacyText(
                    "DownloadClientOperationsHintText"),
                SharedLegacyText(
                    "DownloadClientTransferAnalyticsText"),
                SharedLegacyText(
                    "DownloadClientWorkloadAnalyticsText"),
                SharedLegacyText(
                    "DownloadClientCurrentWorkHeadingText"),
                SharedLegacyText(
                    "DownloadClientCurrentWorkHintText"),
                queue,
                SharedLegacyText(
                    "DownloadClientHistoryHeadingText"),
                SharedLegacyText(
                    "DownloadClientHistoryHintText"),
                history,
                SharedLegacyText(
                    "DownloadClientStatusText"),
                CanRefresh:
                    Get<Button>(
                            "DownloadClientRefreshButton")
                        .IsEnabled,
                CanOpen:
                    Get<Button>(
                            "DownloadClientOpenButton")
                        .IsEnabled,
                CanOpenDocker:
                    Get<Button>(
                            "DownloadClientDockerButton")
                        .IsEnabled,
                CanOpenLogs:
                    true,
                CanOpenTerminal:
                    true,
                ConfigEditable:
                    false,
                ConfigEndpoint:
                    string.Empty,
                UserNameLabel:
                    string.Empty,
                ConfigUserName:
                    string.Empty,
                SecretLabel:
                    "Credential",
                ConfigEvidence:
                    "Download-client credentials remain on the Linux target."));
    }

    private void UpdateSharedLinuxLifecycle()
    {
        if (_sharedLifecycleView is null)
            return;

        var stages =
            SharedLegacyItems(
                    "LifecycleStagesList")
                .Select(item =>
                    new UnifiedLifecycleStageRow(
                        SharedLegacyKey(
                            item,
                            "Stage",
                            "Key"),
                        LegacyMediaProjection.Text(
                            item,
                            "Stage"),
                        LegacyMediaProjection.Text(
                            item,
                            "State"),
                        LegacyMediaProjection.Text(
                            item,
                            "Evidence")))
                .ToArray();

        var items =
            SharedLegacyItems(
                    "LifecycleItemsList")
                .Select(item =>
                    new UnifiedLifecycleItemRow(
                        SharedLegacyKey(
                            item,
                            "Item",
                            "Title",
                            "Key"),
                        LegacyMediaProjection.Text(
                            item,
                            "Item",
                            "Title"),
                        LegacyMediaProjection.Text(
                            item,
                            "Owner"),
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
                            "Detail")))
                .ToArray();

        var remediation =
            SharedLegacyItems(
                    "LifecycleRemediationList")
                .Select(item =>
                    new UnifiedRemediationRow(
                        SharedLegacyKey(
                            item,
                            "Step",
                            "Component",
                            "Key"),
                        LegacyMediaProjection.Text(
                            item,
                            "Step"),
                        LegacyMediaProjection.Text(
                            item,
                            "Component"),
                        LegacyMediaProjection.Text(
                            item,
                            "SeverityLabel",
                            "Severity"),
                        LegacyMediaProjection.Text(
                            item,
                            "Why"),
                        LegacyMediaProjection.Text(
                            item,
                            "NextStep")))
                .ToArray();

        _sharedLifecycleView.Update(
            new UnifiedLifecycleState(
                SharedLegacyText(
                    "LifecycleActiveMetricText"),
                SharedLegacyText(
                    "LifecycleAttentionMetricText"),
                SharedLegacyText(
                    "LifecycleDownloadingMetricText"),
                SharedLegacyText(
                    "LifecycleImportMetricText"),
                "0",
                SharedLegacyText(
                    "LifecycleSummaryText"),
                stages,
                items,
                remediation,
                SharedLegacyText(
                    "LifecycleSelectedTitleText"),
                SharedLegacyText(
                    "LifecycleSelectedDetailText"),
                "Linux lifecycle sources are correlated from Arr, download clients and Plex.",
                SharedLegacyText(
                    "LifecycleSummaryText"),
                CanRefresh:
                    true,
                CanOpenOwner:
                    Get<Button>(
                            "LifecycleOpenOwnerButton")
                        .IsEnabled,
                CanOpenIntelligence:
                    true));
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

    private void SelectLegacyMediaItem(
        string listName,
        string key,
        params string[] propertyNames)
    {
        var list =
            Get<ListBox>(
                listName);

        var selected =
            SharedLegacyItems(
                    listName)
                .FirstOrDefault(item =>
                    SharedLegacyKey(
                            item,
                            propertyNames)
                        .Equals(
                            key,
                            StringComparison.OrdinalIgnoreCase));

        if (selected is not null)
            list.SelectedItem =
                selected;
    }

    private void SetLegacyCombo(
        string comboName,
        string value)
    {
        var combo =
            Get<ComboBox>(
                comboName);

        var rows =
            LegacyMediaProjection.Items(
                combo.ItemsSource);

        combo.SelectedItem =
            rows.FirstOrDefault(item =>
                string.Equals(
                    Convert.ToString(
                        item),
                    value,
                    StringComparison.OrdinalIgnoreCase)) ??
            value;
    }

    private void RaiseFirstLegacyButton(
        string panelName)
    {
        var button =
            Get<Panel>(
                    panelName)
                .Children
                .OfType<Button>()
                .FirstOrDefault(item =>
                    item.IsEnabled &&
                    item.IsVisible);

        button?.RaiseEvent(
            new RoutedEventArgs(
                Button.ClickEvent));
    }
}
