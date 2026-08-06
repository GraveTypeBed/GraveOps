using System;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using GraveOps.Presentation.Avalonia.MediaWorkspaces;
using GraveOps.Presentation.Avalonia.SpecializedApplications;

namespace GraveOps.Desktop.Linux;

public partial class MainWindow
{
    private UnifiedApplicationWorkspaceView?
        _sharedApplicationWorkspaceView;

    private UnifiedRecyclarrView?
        _sharedRecyclarrView;

    private UnifiedPiHoleView?
        _sharedPiHoleView;

    private DispatcherTimer?
        _sharedSpecializedApplicationSyncTimer;

    private void InitializeSharedUnifiedSpecializedApplications()
    {
        _sharedApplicationWorkspaceView =
            new UnifiedApplicationWorkspaceView();

        _sharedRecyclarrView =
            new UnifiedRecyclarrView();

        _sharedPiHoleView =
            new UnifiedPiHoleView();

        ReplaceSharedSpecializedApplicationPage(
            "ApplicationWorkspacePage",
            _sharedApplicationWorkspaceView);

        ReplaceSharedSpecializedApplicationPage(
            "RecyclarrWorkspacePage",
            _sharedRecyclarrView);

        ReplaceSharedSpecializedApplicationPage(
            "PiHoleWorkspacePage",
            _sharedPiHoleView);

        WireSharedLinuxApplicationWorkspace();
        WireSharedLinuxRecyclarr();
        WireSharedLinuxPiHole();

        _sharedSpecializedApplicationSyncTimer =
            new DispatcherTimer
            {
                Interval =
                    TimeSpan.FromMilliseconds(
                        500)
            };

        _sharedSpecializedApplicationSyncTimer.Tick +=
            (_, _) =>
            {
                if (SharedSpecializedApplicationPageVisible())
                    UpdateSharedUnifiedSpecializedApplications();
            };

        _sharedSpecializedApplicationSyncTimer.Start();

        UpdateSharedUnifiedSpecializedApplications();
    }

    private void DisposeSharedUnifiedSpecializedApplications()
    {
        _sharedSpecializedApplicationSyncTimer?.Stop();

        _sharedSpecializedApplicationSyncTimer =
            null;
    }

    private bool SharedSpecializedApplicationPageVisible() =>
        Get<Grid>(
                "ApplicationWorkspacePage")
            .IsVisible ||
        Get<Grid>(
                "RecyclarrWorkspacePage")
            .IsVisible ||
        Get<Grid>(
                "PiHoleWorkspacePage")
            .IsVisible;

    private void ReplaceSharedSpecializedApplicationPage(
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

    private void UpdateSharedUnifiedSpecializedApplications()
    {
        UpdateSharedLinuxApplicationWorkspace();
        UpdateSharedLinuxRecyclarr();
        UpdateSharedLinuxPiHole();
    }

    private void WireSharedLinuxApplicationWorkspace()
    {
        if (_sharedApplicationWorkspaceView is null)
            return;

        _sharedApplicationWorkspaceView.ActionRequested +=
            (_, e) =>
            {
                switch (e.Action)
                {
                    case UnifiedApplicationWorkspaceAction.Open:
                        DirectIntegrationOpenButton_OnClick(
                            null,
                            new RoutedEventArgs());
                        break;

                    case UnifiedApplicationWorkspaceAction.Docker:
                        DirectIntegrationDockerButton_OnClick(
                            null,
                            new RoutedEventArgs());
                        break;

                    case UnifiedApplicationWorkspaceAction.Logs:
                        DirectIntegrationLogsButton_OnClick(
                            null,
                            new RoutedEventArgs());
                        break;

                    case UnifiedApplicationWorkspaceAction.Intelligence:
                        DirectIntegrationIntelligenceButton_OnClick(
                            null,
                            new RoutedEventArgs());
                        break;

                    case UnifiedApplicationWorkspaceAction.Back:
                        Navigate(
                            "IntegrationsNav");
                        break;
                }
            };
    }

    private void WireSharedLinuxRecyclarr()
    {
        if (_sharedRecyclarrView is null)
            return;

        _sharedRecyclarrView.ActionRequested +=
            (_, e) =>
            {
                switch (e.Action)
                {
                    case UnifiedRecyclarrAction.Refresh:
                        RecyclarrRefreshButton_OnClick(
                            null,
                            new RoutedEventArgs());
                        break;

                    case UnifiedRecyclarrAction.OpenConfig:
                        RecyclarrOpenConfigButton_OnClick(
                            null,
                            new RoutedEventArgs());
                        break;

                    case UnifiedRecyclarrAction.Preview:
                        RecyclarrPreviewButton_OnClick(
                            null,
                            new RoutedEventArgs());
                        break;

                    case UnifiedRecyclarrAction.Docker:
                        RecyclarrDockerButton_OnClick(
                            null,
                            new RoutedEventArgs());
                        break;

                    case UnifiedRecyclarrAction.Logs:
                        RecyclarrLogsButton_OnClick(
                            null,
                            new RoutedEventArgs());
                        break;

                    case UnifiedRecyclarrAction.Back:
                        Navigate(
                            "IntegrationsNav");
                        break;
                }
            };
    }

    private void WireSharedLinuxPiHole()
    {
        if (_sharedPiHoleView is null)
            return;

        _sharedPiHoleView.ActionRequested +=
            (_, e) =>
            {
                switch (e.Action)
                {
                    case UnifiedPiHoleAction.Refresh:
                        PiHoleRefreshButton_OnClick(
                            null,
                            new RoutedEventArgs());
                        break;

                    case UnifiedPiHoleAction.Open:
                        PiHoleOpenButton_OnClick(
                            null,
                            new RoutedEventArgs());
                        break;

                    case UnifiedPiHoleAction.EnableBlocking:
                        PiHoleEnableButton_OnClick(
                            null,
                            new RoutedEventArgs());
                        break;

                    case UnifiedPiHoleAction.DisableBlockingFiveMinutes:
                        PiHoleDisableButton_OnClick(
                            null,
                            new RoutedEventArgs());
                        break;

                    case UnifiedPiHoleAction.ReloadDns:
                        PiHoleReloadButton_OnClick(
                            null,
                            new RoutedEventArgs());
                        break;

                    case UnifiedPiHoleAction.Logs:
                        PiHoleLogsButton_OnClick(
                            null,
                            new RoutedEventArgs());
                        break;

                    case UnifiedPiHoleAction.Back:
                        Navigate(
                            "IntegrationsNav");
                        break;
                }
            };
    }

    private void UpdateSharedLinuxApplicationWorkspace()
    {
        if (_sharedApplicationWorkspaceView is null)
            return;

        _sharedApplicationWorkspaceView.Update(
            new UnifiedApplicationWorkspaceState(
                SharedSpecializedText(
                    "DirectIntegrationOwnerText"),
                SharedSpecializedText(
                    "DirectIntegrationNameText"),
                SharedSpecializedText(
                    "DirectIntegrationSubtitleText"),
                SharedSpecializedText(
                    "DirectIntegrationStateText"),
                SharedSpecializedText(
                    "DirectIntegrationRuntimeText"),
                SharedSpecializedText(
                    "DirectIntegrationRoleText"),
                SharedSpecializedText(
                    "DirectIntegrationFindingsText"),
                SharedSpecializedText(
                    "DirectIntegrationPrimaryTitleText"),
                SharedSpecializedText(
                    "DirectIntegrationEndpointText"),
                SharedSpecializedText(
                    "DirectIntegrationSecondaryTitleText"),
                SharedSpecializedText(
                    "DirectIntegrationOwnerText"),
                SharedSpecializedTextBoxText(
                    "DirectIntegrationEvidenceText"),
                SharedSpecializedTextBoxText(
                    "DirectIntegrationRelatedText"),
                SharedSpecializedText(
                    "DirectIntegrationOperationsText"),
                Get<Button>(
                        "DirectIntegrationOpenButton")
                    .IsEnabled,
                true,
                true,
                Get<Button>(
                        "DirectIntegrationIntelligenceButton")
                    .IsEnabled,
                false));
    }

    private void UpdateSharedLinuxRecyclarr()
    {
        if (_sharedRecyclarrView is null)
            return;

        var targetRows =
            LegacyMediaProjection
                .Items(
                    Get<ListBox>(
                            "RecyclarrTargetsList")
                        .ItemsSource)
                .Select(row =>
                    new UnifiedRecyclarrTargetRow(
                        LegacyMediaProjection.First(
                            row,
                            "--",
                            "Service"),
                        LegacyMediaProjection.First(
                            row,
                            "--",
                            "Instance"),
                        LegacyMediaProjection.First(
                            row,
                            "--",
                            "ConfigFile"),
                        LegacyMediaProjection.First(
                            row,
                            "--",
                            "Endpoint")))
                .ToArray();

        var configRows =
            LegacyMediaProjection
                .Items(
                    Get<ListBox>(
                            "RecyclarrConfigFilesList")
                        .ItemsSource)
                .Select(row =>
                    new UnifiedRecyclarrConfigFileRow(
                        LegacyMediaProjection.First(
                            row,
                            "--",
                            "File"),
                        LegacyMediaProjection.First(
                            row,
                            "--",
                            "RelativePath"),
                        LegacyMediaProjection.First(
                            row,
                            "--",
                            "Size"),
                        LegacyMediaProjection.First(
                            row,
                            "--",
                            "Modified"),
                        LegacyMediaProjection.First(
                            row,
                            "--",
                            "Targets")))
                .ToArray();

        _sharedRecyclarrView.Update(
            new UnifiedRecyclarrState(
                SharedSpecializedText(
                    "RecyclarrTargetText"),
                SharedSpecializedText(
                    "RecyclarrFreshnessText"),
                SharedSpecializedText(
                    "RecyclarrRuntimeMetricText"),
                SharedSpecializedText(
                    "RecyclarrVersionMetricText"),
                SharedSpecializedText(
                    "RecyclarrConfigMetricText"),
                SharedSpecializedText(
                    "RecyclarrTargetMetricText"),
                SharedSpecializedText(
                    "RecyclarrContainerNameText"),
                SharedSpecializedText(
                    "RecyclarrImageText"),
                SharedSpecializedText(
                    "RecyclarrComposeText"),
                SharedSpecializedText(
                    "RecyclarrScheduleText"),
                SharedSpecializedText(
                    "RecyclarrConfigPathText"),
                SharedSpecializedText(
                    "RecyclarrLastRunText"),
                SharedSpecializedText(
                    "RecyclarrEvidenceText"),
                targetRows,
                configRows,
                SharedSpecializedText(
                    "RecyclarrPreviewStatusText"),
                SharedSpecializedTextBoxText(
                    "RecyclarrOutputText"),
                SharedSpecializedText(
                    "RecyclarrStatusText"),
                Get<Button>(
                        "RecyclarrRefreshButton")
                    .IsEnabled,
                Get<Button>(
                        "RecyclarrOpenConfigButton")
                    .IsEnabled,
                Get<Button>(
                        "RecyclarrPreviewButton")
                    .IsEnabled,
                true,
                true,
                false));
    }

    private void UpdateSharedLinuxPiHole()
    {
        if (_sharedPiHoleView is null)
            return;

        _sharedPiHoleView.Update(
            new UnifiedPiHoleState(
                SharedSpecializedText(
                    "PiHoleTargetText"),
                SharedSpecializedText(
                    "PiHoleFreshnessText"),
                SharedSpecializedText(
                    "PiHoleStateText"),
                SharedSpecializedText(
                    "PiHoleDnsText"),
                SharedSpecializedText(
                    "PiHoleBlockingText"),
                SharedSpecializedText(
                    "PiHoleQueriesText"),
                SharedSpecializedText(
                    "PiHoleBlockedText"),
                SharedSpecializedText(
                    "PiHoleVersionsText"),
                SharedSpecializedText(
                    "PiHoleHostContextText"),
                SharedSpecializedText(
                    "PiHoleClientContextText"),
                SharedSpecializedText(
                    "PiHoleGravityContextText"),
                SharedSpecializedTextBoxText(
                    "PiHoleEvidenceText"),
                SharedSpecializedText(
                    "PiHoleStatusText"),
                Get<Button>(
                        "PiHoleRefreshButton")
                    .IsEnabled,
                Get<Button>(
                        "PiHoleOpenButton")
                    .IsEnabled,
                Get<Button>(
                        "PiHoleEnableButton")
                    .IsEnabled,
                Get<Button>(
                        "PiHoleDisableButton")
                    .IsEnabled,
                Get<Button>(
                        "PiHoleReloadButton")
                    .IsEnabled,
                true,
                false));
    }

    private string SharedSpecializedText(
        string name) =>
        Get<TextBlock>(
                name)
            .Text ??
        string.Empty;

    private string SharedSpecializedTextBoxText(
        string name) =>
        Get<TextBox>(
                name)
            .Text ??
        string.Empty;
}
