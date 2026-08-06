using System.Collections;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using GraveOps.Core.Targets;
using GraveOps.Presentation.Avalonia.OperationsWorkspaces;

namespace GraveOps.Desktop.Linux;

public partial class MainWindow
{
    private UnifiedDockerView?
        _sharedDockerView;

    private UnifiedBackupsView?
        _sharedBackupsView;

    private UnifiedSettingsView?
        _sharedSettingsView;

    private UnifiedToolsView?
        _sharedToolsView;

    private DispatcherTimer?
        _sharedOperationsSyncTimer;

    private void InitializeSharedUnifiedOperationsWorkspaces()
    {
        _sharedDockerView =
            new UnifiedDockerView();

        _sharedBackupsView =
            new UnifiedBackupsView();

        _sharedSettingsView =
            new UnifiedSettingsView();

        _sharedToolsView =
            new UnifiedToolsView();

        ReplaceOperationsWorkspacePage(
            "DockerPage",
            _sharedDockerView);

        ReplaceOperationsWorkspacePage(
            "BackupsPage",
            _sharedBackupsView);

        ReplaceOperationsWorkspacePage(
            "SettingsPage",
            _sharedSettingsView);

        ReplaceOperationsWorkspacePage(
            "ToolsPage",
            _sharedToolsView);

        WireSharedDockerWorkspace();
        WireSharedBackupsWorkspace();
        WireSharedSettingsWorkspace();
        WireSharedToolsWorkspace();

        _sharedOperationsSyncTimer =
            new DispatcherTimer
            {
                Interval =
                    TimeSpan.FromMilliseconds(
                        500)
            };

        _sharedOperationsSyncTimer.Tick +=
            (_, _) =>
            {
                if (SharedOperationsPageVisible())
                    UpdateSharedUnifiedOperationsWorkspaces();
            };

        _sharedOperationsSyncTimer.Start();

        UpdateSharedUnifiedOperationsWorkspaces();
    }

    private void DisposeSharedUnifiedOperationsWorkspaces()
    {
        _sharedOperationsSyncTimer?.Stop();
        _sharedOperationsSyncTimer =
            null;
    }

    private bool SharedOperationsPageVisible() =>
        Get<Grid>(
                "DockerPage")
            .IsVisible ||
        Get<Grid>(
                "BackupsPage")
            .IsVisible ||
        Get<Grid>(
                "SettingsPage")
            .IsVisible ||
        Get<Grid>(
                "ToolsPage")
            .IsVisible;

    private void ReplaceOperationsWorkspacePage(
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

    private void WireSharedDockerWorkspace()
    {
        if (_sharedDockerView is null)
            return;

        _sharedDockerView.RefreshRequested +=
            (_, _) =>
            {
                DockerRefreshButton_OnClick(
                    null,
                    new RoutedEventArgs());

                BeginSharedOperationsSyncBurst();
            };

        _sharedDockerView.ShowExitedChanged +=
            (_, _) =>
            {
                Get<CheckBox>(
                        "ShowInformationalContainersCheckBox")
                    .IsChecked =
                    _sharedDockerView.ShowExited;

                ApplyDockerWorkspaceFilter();

                BeginSharedOperationsSyncBurst();
            };

        _sharedDockerView.SelectionRequested +=
            (_, e) =>
            {
                SelectLegacyDockerRow(
                    e.Row.Key);

                BeginSharedOperationsSyncBurst();
            };

        _sharedDockerView.DetailRefreshRequested +=
            (_, e) =>
            {
                SelectLegacyDockerRow(
                    e.Row.Key);

                DockerRefreshDetailButton_OnClick(
                    null,
                    new RoutedEventArgs());

                BeginSharedOperationsSyncBurst();
            };

        _sharedDockerView.ActionRequested +=
            (_, e) =>
            {
                SelectLegacyDockerRow(
                    e.Row.Key);

                switch (e.Action)
                {
                    case UnifiedDockerAction.Start:
                        DockerStartButton_OnClick(
                            null,
                            new RoutedEventArgs());
                        break;

                    case UnifiedDockerAction.Stop:
                        DockerStopButton_OnClick(
                            null,
                            new RoutedEventArgs());
                        break;

                    case UnifiedDockerAction.Restart:
                        DockerRestartButton_OnClick(
                            null,
                            new RoutedEventArgs());
                        break;

                    case UnifiedDockerAction.RestartProject:
                        DockerRestartDumbButton_OnClick(
                            null,
                            new RoutedEventArgs());
                        break;
                }

                BeginSharedOperationsSyncBurst();
            };

        _sharedDockerView.CopyRequested +=
            async (_, e) =>
            {
                var clipboard =
                    TopLevel.GetTopLevel(
                            this)
                        ?.Clipboard;

                if (clipboard is null)
                    return;

                await global::Avalonia.Input.Platform
                    .ClipboardExtensions
                    .SetTextAsync(
                        clipboard,
                        e.Text);

                Get<TextBlock>(
                        "DockerLogsStatusText")
                    .Text =
                    e.SuccessMessage;

                UpdateSharedUnifiedDocker();
            };
    }

    private void WireSharedBackupsWorkspace()
    {
        if (_sharedBackupsView is null)
            return;

        _sharedBackupsView.RefreshRequested +=
            async (_, _) =>
            {
                await RefreshAsync();
                UpdateSharedUnifiedBackups();
            };

        _sharedBackupsView.ServicesRequested +=
            (_, _) =>
                Navigate(
                    "ServicesNav");

        _sharedBackupsView.ToolsRequested +=
            (_, _) =>
                Navigate(
                    "ToolsNav");
    }

    private void WireSharedSettingsWorkspace()
    {
        if (_sharedSettingsView is null)
            return;

        _sharedSettingsView.ActionRequested +=
            SharedSettingsActionRequested;

        _sharedSettingsView.PathActionRequested +=
            (_, e) =>
            {
                var button =
                    new Button
                    {
                        Tag =
                            e.Row.Key
                    };

                if (e.Action ==
                    UnifiedPathAction.Open)
                {
                    OpenOperatorPathButton_OnClick(
                        button,
                        new RoutedEventArgs());
                }
                else
                {
                    OpenOperatorTerminalButton_OnClick(
                        button,
                        new RoutedEventArgs());
                }

                BeginSharedOperationsSyncBurst();
            };
    }

    private void WireSharedToolsWorkspace()
    {
        if (_sharedToolsView is null)
            return;

        _sharedToolsView.TerminalActionRequested +=
            (_, e) =>
            {
                if (e.Action ==
                    UnifiedTerminalAction.Ssh)
                {
                    UnifiedSshTerminalButton_OnClick(
                        null,
                        new RoutedEventArgs());
                }
                else
                {
                    var tag =
                        e.Action switch
                        {
                            UnifiedTerminalAction.Repository =>
                                "repository",

                            UnifiedTerminalAction.Config =>
                                "config",

                            UnifiedTerminalAction.Diagnostics =>
                                "diagnostics",

                            _ =>
                                "home"
                        };

                    UnifiedLocalTerminalButton_OnClick(
                        new Button
                        {
                            Tag =
                                tag
                        },
                        new RoutedEventArgs());
                }

                BeginSharedOperationsSyncBurst();
            };

        _sharedToolsView.DiagnosticsRequested +=
            (_, _) =>
            {
                CreateDiagnosticsButton_OnClick(
                    null,
                    new RoutedEventArgs());

                BeginSharedOperationsSyncBurst();
            };

        _sharedToolsView.ValidationRequested +=
            (_, _) =>
            {
                RunValidationButton_OnClick(
                    null,
                    new RoutedEventArgs());

                BeginSharedOperationsSyncBurst();
            };

        _sharedToolsView.FilesActionRequested +=
            SharedFilesActionRequested;

        _sharedToolsView.ScriptActionRequested +=
            (_, e) =>
            {
                SelectLegacyItem(
                    "OperatorScriptsList",
                    "Name",
                    e.Row.Name);

                if (e.Action ==
                    UnifiedScriptAction.Run)
                {
                    RunOperatorScriptButton_OnClick(
                        null,
                        new RoutedEventArgs());
                }

                BeginSharedOperationsSyncBurst();
            };

        _sharedToolsView.UpdateRequested +=
            (_, _) =>
            {
                RefreshUpdateInventoryButton_OnClick(
                    null,
                    new RoutedEventArgs());

                BeginSharedOperationsSyncBurst();
            };

        _sharedToolsView.CopyRequested +=
            async (_, e) =>
            {
                var clipboard =
                    TopLevel.GetTopLevel(
                            this)
                        ?.Clipboard;

                if (clipboard is null)
                    return;

                await global::Avalonia.Input.Platform
                    .ClipboardExtensions
                    .SetTextAsync(
                        clipboard,
                        e.Text);

                Get<TextBlock>(
                        "OperatorScriptStatusText")
                    .Text =
                    e.SuccessMessage;

                UpdateSharedUnifiedTools();
            };
    }

    private void SharedSettingsActionRequested(
        object? sender,
        UnifiedSettingsActionRequestedEventArgs e)
    {
        ApplySharedSettingsInputs(
            e);

        switch (e.Action)
        {
            case UnifiedSettingsAction.PreviewInterface:
                ApplyUnifiedTheme(
                    e.InterfaceSettings.Theme);

                ApplyUnifiedDensity(
                    e.InterfaceSettings.Density);

                PopulateUnifiedDashboard();
                break;

            case UnifiedSettingsAction.SaveInterface:
                SaveInterfaceSettingsButton_OnClick(
                    null,
                    new RoutedEventArgs());
                break;

            case UnifiedSettingsAction.ExpressSetup:
                OpenExpressSetupButton_OnClick(
                    null,
                    new RoutedEventArgs());
                break;

            case UnifiedSettingsAction.ResetDashboard:
                ResetDashboardLayoutButton_OnClick(
                    null,
                    new RoutedEventArgs());
                break;

            case UnifiedSettingsAction.ExportProfile:
                ExportProfileButton_OnClick(
                    null,
                    new RoutedEventArgs());
                break;

            case UnifiedSettingsAction.SaveOperatorDefaults:
                SaveOperatorSettingsButton_OnClick(
                    null,
                    new RoutedEventArgs());
                break;

            case UnifiedSettingsAction.RestoreOperatorDefaults:
                ResetOperatorSettingsButton_OnClick(
                    null,
                    new RoutedEventArgs());
                break;

            case UnifiedSettingsAction.CapacityAlerts:
                StorageCapacityPolicyButton_OnClick(
                    null,
                    new RoutedEventArgs());
                break;

            case UnifiedSettingsAction.SignalQuality:
                SignalQualityPolicyButton_OnClick(
                    null,
                    new RoutedEventArgs());
                break;

            case UnifiedSettingsAction.RemediationSafety:
                VerifiedRemediationPolicyButton_OnClick(
                    null,
                    new RoutedEventArgs());
                break;

            case UnifiedSettingsAction.UiPerformance:
                UiPerformancePolicyButton_OnClick(
                    null,
                    new RoutedEventArgs());
                break;

            case UnifiedSettingsAction.StorageThresholds:
                Navigate(
                    "StorageNav");
                break;

            case UnifiedSettingsAction.DashboardPolicies:
                Navigate(
                    "DashboardNav");
                break;

            case UnifiedSettingsAction.RefreshVersion:
                RefreshVersionInfoButton_OnClick(
                    null,
                    new RoutedEventArgs());
                break;
        }

        BeginSharedOperationsSyncBurst();
    }

    private void ApplySharedSettingsInputs(
        UnifiedSettingsActionRequestedEventArgs e)
    {
        Get<ComboBox>(
                "InterfaceThemeComboBox")
            .SelectedItem =
            e.InterfaceSettings.Theme;

        Get<ComboBox>(
                "InterfaceDensityComboBox")
            .SelectedItem =
            e.InterfaceSettings.Density;

        Get<CheckBox>(
                "InterfaceRestoreSessionCheckBox")
            .IsChecked =
            e.InterfaceSettings.RestoreSession;

        Get<CheckBox>(
                "InterfaceSilentRefreshCheckBox")
            .IsChecked =
            e.InterfaceSettings.SilentRefresh;

        Get<CheckBox>(
                "InterfaceFreshnessCheckBox")
            .IsChecked =
            e.InterfaceSettings.ShowFreshness;

        Get<CheckBox>(
                "SettingsSafeModeCheckBox")
            .IsChecked =
            e.OperatorSettings.StartSafeMode;

        Get<CheckBox>(
                "SettingsInformationalLogsCheckBox")
            .IsChecked =
            e.OperatorSettings.ShowInformationalLogs;

        Get<CheckBox>(
                "SettingsInformationalContainersCheckBox")
            .IsChecked =
            e.OperatorSettings.ShowInformationalContainers;

        Get<CheckBox>(
                "SettingsOpenOverviewCheckBox")
            .IsChecked =
            e.OperatorSettings.OpenOverview;

        Get<CheckBox>(
                "SettingsDesktopNotificationsCheckBox")
            .IsChecked =
            e.OperatorSettings.DesktopNotifications;

        Get<TextBox>(
                "SettingsBackgroundRefreshSecondsTextBox")
            .Text =
            e.OperatorSettings.BackgroundRefreshSeconds;
    }

    private void SharedFilesActionRequested(
        object? sender,
        UnifiedFilesActionRequestedEventArgs e)
    {
        Get<TextBox>(
                "UnifiedFilesPathTextBox")
            .Text =
            e.Path;

        if (e.Selected is not null)
        {
            SelectLegacyItem(
                "UnifiedFilesList",
                "FullPath",
                e.Selected.FullPath);
        }

        switch (e.Action)
        {
            case UnifiedFilesAction.Refresh:
                UnifiedFilesRefreshButton_OnClick(
                    null,
                    new RoutedEventArgs());
                break;

            case UnifiedFilesAction.Parent:
                UnifiedFilesParentButton_OnClick(
                    null,
                    new RoutedEventArgs());
                break;

            case UnifiedFilesAction.OpenSelected:
                UnifiedFilesOpenButton_OnClick(
                    null,
                    new RoutedEventArgs());
                break;

            case UnifiedFilesAction.Sftp:
                UnifiedSftpButton_OnClick(
                    null,
                    new RoutedEventArgs());
                break;
        }

        BeginSharedOperationsSyncBurst();
    }

    private void SelectLegacyDockerRow(
        string key)
    {
        var row =
            _dockerFleetSnapshot?
                .Containers
                .FirstOrDefault(item =>
                    item.Id.Equals(
                        key,
                        StringComparison.OrdinalIgnoreCase) ||
                    item.Name.Equals(
                        key,
                        StringComparison.OrdinalIgnoreCase));

        if (row is null)
            return;

        Get<CheckBox>(
                "ShowInformationalContainersCheckBox")
            .IsChecked =
            !row.IsRunning ||
            Get<CheckBox>(
                    "ShowInformationalContainersCheckBox")
                .IsChecked ==
            true;

        ApplyDockerWorkspaceFilter(
            row.Name);

        Get<ListBox>(
                "DockerList")
            .SelectedItem =
            row;
    }

    private void SelectLegacyItem(
        string listName,
        string property,
        string value)
    {
        var list =
            Get<ListBox>(
                listName);

        var item =
            EnumerateItems(
                    list.ItemsSource)
                .FirstOrDefault(candidate =>
                    ReadString(
                            candidate,
                            property)
                        .Equals(
                            value,
                            StringComparison.OrdinalIgnoreCase));

        if (item is not null)
            list.SelectedItem = item;
    }

    private void BeginSharedOperationsSyncBurst()
    {
        UpdateSharedUnifiedOperationsWorkspaces();
    }

    private void UpdateSharedUnifiedOperationsWorkspaces()
    {
        UpdateSharedUnifiedDocker();
        UpdateSharedUnifiedBackups();
        UpdateSharedUnifiedSettings();
        UpdateSharedUnifiedTools();
    }

    private void UpdateSharedUnifiedDocker()
    {
        if (_sharedDockerView is null)
            return;

        var safeMode =
            Get<CheckBox>(
                    "SafeModeCheckBox")
                .IsChecked ==
            true;

        var local =
            CanRunLocalMutations();

        var rows =
            (_dockerFleetSnapshot?
                 .Containers ??
             Array.Empty<DockerFleetRow>())
            .Select(row =>
                new UnifiedDockerRow(
                    row.Id,
                    row.Group,
                    row.Name,
                    row.Image,
                    row.StateLabel,
                    row.HealthLabel,
                    row.RestartSummary,
                    row.Resources,
                    row.Ports,
                    row.ComposeProject ==
                    "--"
                        ? "Standalone container"
                        : $"{row.ComposeProject} / {row.ComposeService}",
                    row.ShortId,
                    row.IsRunning,
                    row.HasAttention,
                    CanStart:
                        local &&
                        !safeMode &&
                        !row.IsRunning &&
                        !_dockerFleetBusy &&
                        !_dockerActionBusy,
                    CanStop:
                        local &&
                        !safeMode &&
                        row.IsRunning &&
                        !_dockerFleetBusy &&
                        !_dockerActionBusy,
                    CanRestart:
                        local &&
                        !safeMode &&
                        row.IsRunning &&
                        !_dockerFleetBusy &&
                        !_dockerActionBusy,
                    CanRestartProject:
                        local &&
                        !safeMode &&
                        !_dockerFleetBusy &&
                        !_dockerActionBusy &&
                        IsDumbComposeOwner(
                            row)))
            .ToArray();

        var detail =
            _dockerDetailSnapshot is null
                ? UnifiedDockerDetail.Empty
                : new UnifiedDockerDetail(
                    _dockerDetailSnapshot.Container.Id,
                    _dockerDetailSnapshot.Container.Name,
                    $"{_dockerDetailSnapshot.Container.StateLabel} | " +
                    _dockerDetailSnapshot.Container.HealthLabel,
                    _dockerDetailSnapshot.Container.Image,
                    _dockerDetailSnapshot.ComposeOwnership,
                    _dockerDetailSnapshot.Lifecycle,
                    _dockerDetailSnapshot.Container.Resources,
                    _dockerDetailSnapshot.Container.ShortId,
                    $"{_dockerDetailSnapshot.Container.RestartPolicy} | " +
                    $"{_dockerDetailSnapshot.Container.RestartCount} restart(s)",
                    _dockerDetailSnapshot.Networks,
                    _dockerDetailSnapshot.Ports,
                    _dockerDetailSnapshot.Mounts,
                    _dockerDetailSnapshot.EnvironmentNames,
                    _dockerDetailSnapshot.CleanedLogs,
                    _dockerDetailSnapshot.RawLogs,
                    _dockerDetailSnapshot.Evidence,
                    Get<TextBlock>(
                            "DockerActionStatusText")
                        .Text ??
                    "No container action run.",
                    CountSharedOperationsLines(
                        _dockerDetailSnapshot.Ports),
                    CountSharedOperationsLines(
                        _dockerDetailSnapshot.Mounts),
                    CountSharedOperationsLines(
                        _dockerDetailSnapshot.EnvironmentNames),
                    _dockerDetailSnapshot.CleanedLogEntryCount,
                    _dockerDetailSnapshot.RawLogLineCount,
                    _dockerDetailSnapshot.CollapsedLogLineCount);

        _sharedDockerView.Update(
            new UnifiedDockerState(
                rows,
                detail,
                Get<TextBlock>(
                        "DockerDaemonText")
                    .Text ??
                "Capture pending",
                Get<TextBlock>(
                        "DockerSummaryText")
                    .Text ??
                "0 shown",
                Get<TextBlock>(
                        "DockerWorkspaceStatusText")
                    .Text ??
                "Docker workspace capture pending.",
                Get<CheckBox>(
                        "ShowInformationalContainersCheckBox")
                    .IsChecked ==
                true,
                CanRefresh:
                    !_dockerFleetBusy,
                CanInspect:
                    _controlPlane
                        .ActiveProfile
                        .IsLocal));
    }

    private void UpdateSharedUnifiedBackups()
    {
        if (_sharedBackupsView is null)
            return;

        var capability =
            SupportsTargetCapability(
                CapabilityIds.BackupInventoryRead);

        if (_backup is null ||
            !capability)
        {
            _sharedBackupsView.Update(
                new UnifiedBackupsState(
                    capability,
                    capability
                        ? "WAITING"
                        : "UNAVAILABLE",
                    "Information",
                    capability
                        ? "--"
                        : "Provider capability absent",
                    capability
                        ? "Waiting for backup inventory."
                        : "The active provider does not report backup inventory capability.",
                    "PROTECTED",
                    Array.Empty<string>(),
                    Array.Empty<UnifiedBackupUnitRow>(),
                    Array.Empty<UnifiedBackupArtifactRow>()));

            return;
        }

        _sharedBackupsView.Update(
            new UnifiedBackupsState(
                true,
                _backup.State,
                LinuxOpsAnalyzer.SeverityLabel(
                    _backup.Severity),
                _backup.Provider,
                _backup.Summary,
                "PROTECTED",
                _backup.Evidence,
                _backup.Units
                    .Select(unit =>
                        new UnifiedBackupUnitRow(
                            unit.Unit,
                            unit.Active,
                            unit.SubState,
                            unit.Enabled,
                            unit.LastRun,
                            unit.NextRun,
                            unit.Path,
                            LinuxOpsAnalyzer.SeverityLabel(
                                unit.Severity)))
                    .ToArray(),
                _backup.Artifacts
                    .Select(artifact =>
                        new UnifiedBackupArtifactRow(
                            artifact.Path,
                            artifact.Size,
                            artifact.LocalModifiedAt
                                .ToString(
                                    "g")))
                    .ToArray()));
    }

    private void UpdateSharedUnifiedSettings()
    {
        if (_sharedSettingsView is null)
            return;

        var paths =
            new[]
            {
                new UnifiedPathRow(
                    "config",
                    "Config",
                    Get<TextBlock>(
                            "SettingsConfigPathText")
                        .Text ??
                    _operatorSettingsStore.ConfigDirectory,
                    true,
                    true),

                new UnifiedPathRow(
                    "data",
                    "Data",
                    Get<TextBlock>(
                            "SettingsDataPathText")
                        .Text ??
                    _operatorSettingsStore.DataDirectory,
                    true,
                    true),

                new UnifiedPathRow(
                    "repository",
                    "Repository",
                    Get<TextBlock>(
                            "SettingsRepositoryPathText")
                        .Text ??
                    _repositoryPath,
                    true,
                    true),

                new UnifiedPathRow(
                    "diagnostics",
                    "Diagnostics",
                    Get<TextBlock>(
                            "SettingsDiagnosticsPathText")
                        .Text ??
                    _operatorSettingsStore.DiagnosticsDirectory,
                    true,
                    true)
            };

        _sharedSettingsView.Update(
            new UnifiedSettingsState(
                ReadComboOptions(
                    "InterfaceThemeComboBox"),
                SelectedText(
                    "InterfaceThemeComboBox",
                    _unifiedInterface.ThemeName),
                ReadComboOptions(
                    "InterfaceDensityComboBox"),
                SelectedText(
                    "InterfaceDensityComboBox",
                    _unifiedInterface.Density),
                Get<CheckBox>(
                        "InterfaceRestoreSessionCheckBox")
                    .IsChecked ==
                true,
                Get<CheckBox>(
                        "InterfaceSilentRefreshCheckBox")
                    .IsChecked !=
                false,
                Get<CheckBox>(
                        "InterfaceFreshnessCheckBox")
                    .IsChecked !=
                false,
                InterfaceEditable:
                    true,
                Get<TextBlock>(
                        "InterfaceSettingsStatusText")
                    .Text ??
                "Interface settings are ready.",
                Get<CheckBox>(
                        "SettingsSafeModeCheckBox")
                    .IsChecked ==
                true,
                Get<CheckBox>(
                        "SettingsInformationalLogsCheckBox")
                    .IsChecked ==
                true,
                Get<CheckBox>(
                        "SettingsInformationalContainersCheckBox")
                    .IsChecked ==
                true,
                Get<CheckBox>(
                        "SettingsOpenOverviewCheckBox")
                    .IsChecked ==
                true,
                Get<CheckBox>(
                        "SettingsDesktopNotificationsCheckBox")
                    .IsChecked ==
                true,
                Get<TextBox>(
                        "SettingsBackgroundRefreshSecondsTextBox")
                    .Text ??
                "60",
                OperatorDefaultsEditable:
                    true,
                Get<TextBlock>(
                        "SettingsSaveStatusText")
                    .Text ??
                "Settings loaded.",
                Get<TextBlock>(
                        "SettingsPolicySummaryText")
                    .Text ??
                "Waiting for policy state.",
                Get<TextBlock>(
                        "SettingsCapacityPolicySummaryText")
                    .Text ??
                "Capacity alerts are loading.",
                Get<TextBlock>(
                        "SettingsSignalQualitySummaryText")
                    .Text ??
                "Signal quality is loading.",
                Get<TextBlock>(
                        "SettingsVerifiedRemediationSummaryText")
                    .Text ??
                "Verified remediation is loading.",
                Get<TextBlock>(
                        "SettingsUiPerformanceSummaryText")
                    .Text ??
                "UI performance is loading.",
                paths,
                Get<TextBlock>(
                        "SettingsPathStatusText")
                    .Text ??
                "No path action run.",
                new UnifiedVersionState(
                    Get<TextBlock>(
                            "SettingsBranchText")
                        .Text ??
                    "--",
                    Get<TextBlock>(
                            "SettingsCommitText")
                        .Text ??
                    "--",
                    Get<TextBlock>(
                            "SettingsWorktreeText")
                        .Text ??
                    "--",
                    Get<TextBlock>(
                            "SettingsOriginText")
                        .Text ??
                    "--",
                    Get<TextBlock>(
                            "SettingsDotnetText")
                        .Text ??
                    "--"),
                PolicyActionsAvailable:
                    true));
    }

    private void UpdateSharedUnifiedTools()
    {
        if (_sharedToolsView is null)
            return;

        var local =
            _controlPlane
                .ActiveProfile
                .IsLocal;

        var safeMode =
            Get<CheckBox>(
                    "SettingsSafeModeCheckBox")
                .IsChecked ==
            true;

        var files =
            EnumerateItems(
                    Get<ListBox>(
                            "UnifiedFilesList")
                        .ItemsSource)
                .Select(item =>
                    new UnifiedFileRow(
                        ReadString(
                            item,
                            "FullPath"),
                        ReadString(
                            item,
                            "Name"),
                        ReadString(
                            item,
                            "FullPath"),
                        ReadString(
                            item,
                            "Kind"),
                        ReadString(
                            item,
                            "Size"),
                        ReadString(
                            item,
                            "Modified"),
                        ReadBoolean(
                            item,
                            "IsDirectory")))
                .ToArray();

        var scripts =
            EnumerateItems(
                    Get<ListBox>(
                            "OperatorScriptsList")
                        .ItemsSource)
                .Select(item =>
                {
                    var mutating =
                        ReadBoolean(
                            item,
                            "IsMutating");

                    return
                        new UnifiedScriptRow(
                            ReadString(
                                item,
                                "Name"),
                            ReadString(
                                item,
                                "Name"),
                            ReadString(
                                item,
                                "Description"),
                            ReadString(
                                item,
                                "Command"),
                            mutating,
                            CanRun:
                                local &&
                                (!mutating ||
                                 !safeMode));
                })
                .ToArray();

        var parity =
            EnumerateItems(
                    Get<ListBox>(
                            "ParityMatrixList")
                        .ItemsSource)
                .Select(item =>
                    new UnifiedParityRow(
                        ReadString(
                            item,
                            "Capability"),
                        ReadString(
                            item,
                            "Capability"),
                        ReadString(
                            item,
                            "Classification"),
                        ReadString(
                            item,
                            "LinuxImplementation"),
                        ReadString(
                            item,
                            "WindowsReference"),
                        ReadString(
                            item,
                            "Evidence")))
                .ToArray();

        _sharedToolsView.Update(
            new UnifiedToolsState(
                Get<TextBlock>(
                        "UnifiedTerminalStatusText")
                    .Text ??
                "No terminal handoff run.",
                CanOpenLocalTerminal:
                    local,
                CanOpenSsh:
                    !local,
                Get<TextBlock>(
                        "DiagnosticsStatusText")
                    .Text ??
                "Ready to export.",
                CanCreateDiagnostics:
                    Get<Button>(
                            "CreateDiagnosticsButton")
                        .IsEnabled,
                Get<TextBox>(
                        "ValidationOutputText")
                    .Text ??
                "Validation has not been run.",
                CanRunValidation:
                    Get<Button>(
                            "RunValidationButton")
                        .IsEnabled,
                Get<TextBox>(
                        "UnifiedFilesPathTextBox")
                    .Text ??
                string.Empty,
                files,
                Get<TextBlock>(
                        "UnifiedFilesStatusText")
                    .Text ??
                "Ready.",
                CanBrowseFiles:
                    local,
                CanOpenSftp:
                    !local,
                scripts,
                Get<TextBox>(
                        "OperatorScriptOutputText")
                    .Text ??
                "Select a script to inspect or run.",
                Get<TextBlock>(
                        "OperatorScriptStatusText")
                    .Text ??
                "Script library ready.",
                Get<TextBox>(
                        "UpdateInventoryOutputText")
                    .Text ??
                "Update inventory has not been captured.",
                Get<TextBlock>(
                        "UpdateInventoryStatusText")
                    .Text ??
                "Manual read-only capture only.",
                CanCaptureUpdates:
                    local,
                parity,
                Get<TextBlock>(
                        "ParitySummaryText")
                    .Text ??
                "Parity matrix loading."));
    }

    private IReadOnlyList<string> ReadComboOptions(
        string name)
    {
        var combo =
            Get<ComboBox>(
                name);

        return EnumerateItems(
                combo.ItemsSource)
            .Select(item =>
                item.ToString() ??
                string.Empty)
            .Where(item =>
                !string.IsNullOrWhiteSpace(
                    item))
            .ToArray();
    }

    private static IReadOnlyList<object> EnumerateItems(
        IEnumerable? source)
    {
        if (source is null)
            return Array.Empty<object>();

        return source
            .Cast<object>()
            .ToArray();
    }

    private static string ReadString(
        object item,
        string property)
    {
        return item.GetType()
                   .GetProperty(
                       property)
                   ?.GetValue(
                       item)
                   ?.ToString() ??
               string.Empty;
    }

    private static bool ReadBoolean(
        object item,
        string property)
    {
        var value =
            item.GetType()
                .GetProperty(
                    property)
                ?.GetValue(
                    item);

        return value is bool flag &&
               flag;
    }

    private static int CountSharedOperationsLines(
        string value)
    {
        if (string.IsNullOrWhiteSpace(
                value) ||
            value.Equals(
                "--",
                StringComparison.Ordinal))
        {
            return 0;
        }

        return value
            .Replace(
                "\r\n",
                "\n",
                StringComparison.Ordinal)
            .Split(
                '\n',
                StringSplitOptions.RemoveEmptyEntries |
                StringSplitOptions.TrimEntries)
            .Length;
    }
}
