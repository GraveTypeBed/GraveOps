using Avalonia.Controls;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using GraveOps.Presentation.Avalonia.SystemWorkspaces;

namespace GraveOps.Desktop.Linux;

public partial class MainWindow
{
    private UnifiedServicesView?
        _sharedServicesView;

    private UnifiedStorageView?
        _sharedStorageView;

    private UnifiedLogsView?
        _sharedLogsView;

    private void InitializeSharedUnifiedSystemWorkspaces()
    {
        _sharedServicesView =
            new UnifiedServicesView();

        _sharedStorageView =
            new UnifiedStorageView();

        _sharedLogsView =
            new UnifiedLogsView();

        ReplaceSystemWorkspacePage(
            "ServicesPage",
            _sharedServicesView);

        ReplaceSystemWorkspacePage(
            "StoragePage",
            _sharedStorageView);

        ReplaceSystemWorkspacePage(
            "LogsPage",
            _sharedLogsView);

        _sharedServicesView.RefreshRequested +=
            async (_, _) =>
                await RefreshAsync();

        _sharedStorageView.RefreshRequested +=
            async (_, _) =>
                await RefreshAsync();

        _sharedLogsView.RefreshRequested +=
            async (_, _) =>
                await RefreshAsync();

        _sharedServicesView.ServiceActionRequested +=
            SharedServiceActionRequested;

        _sharedServicesView.SafeModeRequested +=
            SharedSafeModeRequested;

        _sharedStorageView.StorageActionRequested +=
            SharedStorageActionRequested;

        _sharedLogsView.LogActionRequested +=
            SharedLogActionRequested;

        UpdateSharedUnifiedSystemWorkspaces();
    }

    private void ReplaceSystemWorkspacePage(
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
            16);

        Grid.SetColumnSpan(
            sharedView,
            16);

        page.Children.Add(
            sharedView);
    }

    private async void SharedServiceActionRequested(
        object? sender,
        UnifiedServiceActionRequestedEventArgs e)
    {
        if (_snapshot is null)
            return;

        var service =
            LinuxOpsAnalyzer
                .UniqueServices(
                    _snapshot)
                .FirstOrDefault(item =>
                    item.Unit.Equals(
                        e.Row.Unit,
                        StringComparison.OrdinalIgnoreCase));

        if (service is null)
            return;

        Get<ListBox>(
                "ServicesList")
            .SelectedItem =
                service;

        _sharedServicesView?.SetActionStatus(
            $"{e.Action} in progress...");

        await RunServiceActionAsync(
            e.Action);

        UpdateSharedUnifiedServices();
    }

    private void SharedSafeModeRequested(
        object? sender,
        UnifiedSafeModeRequestedEventArgs e)
    {
        Get<CheckBox>(
                "SafeModeCheckBox")
            .IsChecked =
                e.Enabled;

        SafeModeCheckBox_OnClick(
            sender,
            new RoutedEventArgs());

        UpdateSharedUnifiedServices();
    }

    private void SharedStorageActionRequested(
        object? sender,
        UnifiedStorageActionRequestedEventArgs e)
    {
        var list =
            Get<ListBox>(
                "StorageList");

        var selected =
            list.ItemsSource?
                .Cast<object>()
                .FirstOrDefault(item =>
                    item.GetType()
                        .GetProperty(
                            "MountPoint")
                        ?.GetValue(
                            item)
                        ?.ToString()
                        ?.Equals(
                            e.Row.MountPoint,
                            StringComparison.OrdinalIgnoreCase) ==
                    true);

        if (selected is null)
            return;

        list.SelectedItem =
            selected;

        switch (e.Action)
        {
            case UnifiedStorageAction.CapacityPolicy:
                StorageCapacityPolicyButton_OnClick(
                    sender,
                    new RoutedEventArgs());
                break;

            case UnifiedStorageAction.Thresholds:
                StorageThresholdButton_OnClick(
                    sender,
                    new RoutedEventArgs());
                break;

            case UnifiedStorageAction.RestoreDefaults:
                RestoreStorageThresholdButton_OnClick(
                    sender,
                    new RoutedEventArgs());
                break;
        }
    }

    private async void SharedLogActionRequested(
        object? sender,
        UnifiedLogActionRequestedEventArgs e)
    {
        if (e.Action ==
            UnifiedLogAction.OpenIntelligence)
        {
            Navigate(
                "IntelligenceNav");
            return;
        }

        var clipboard =
            TopLevel.GetTopLevel(
                    this)
                ?.Clipboard;

        if (clipboard is null)
            return;

        await clipboard.SetTextAsync(
            e.Row.Detail);
    }

    private void UpdateSharedUnifiedSystemWorkspaces()
    {
        UpdateSharedUnifiedServices();
        UpdateSharedUnifiedStorage();
        UpdateSharedUnifiedLogs();
    }

    private void UpdateSharedUnifiedServices()
    {
        if (_sharedServicesView is null)
            return;

        if (_snapshot is null)
        {
            _sharedServicesView.Update(
                UnifiedServicesState.Empty);
            return;
        }

        var safeMode =
            Get<CheckBox>(
                    "SafeModeCheckBox")
                .IsChecked ==
            true;

        var localMutations =
            CanRunLocalMutations();

        var rows =
            LinuxOpsAnalyzer
                .UniqueServices(
                    _snapshot)
                .Select(service =>
                    new UnifiedServiceRow(
                        service.Unit,
                        service.Description,
                        service.ActiveState,
                        service.SubState,
                        $"Unit-file state · {service.UnitFileState}",
                        $"{service.Unit}\n" +
                        $"{service.Description}\n\n" +
                        $"State: {service.ActiveState}/{service.SubState}\n" +
                        $"Unit-file state: {service.UnitFileState}",
                        CanStart:
                            localMutations,
                        CanStop:
                            localMutations &&
                            !safeMode,
                        CanRestart:
                            localMutations &&
                            !safeMode))
                .ToArray();

        _sharedServicesView.Update(
            new UnifiedServicesState(
                rows,
                $"{rows.Length} service(s) · " +
                $"{_snapshot.FailedUnits.Count} failed",
                Get<TextBlock>(
                        "ServiceActionStatusText")
                    .Text ??
                "No action run.",
                safeMode,
                CanToggleSafeMode:
                    true));
    }

    private void UpdateSharedUnifiedStorage()
    {
        if (_sharedStorageView is null)
            return;

        if (_snapshot is null)
        {
            _sharedStorageView.Update(
                UnifiedStorageState.Empty);
            return;
        }

        var rows =
            LinuxOpsAnalyzer
                .OperationalStorage(
                    _snapshot)
                .Select(volume =>
                {
                    var capacity =
                        _findingPolicies
                            .EvaluateStorageCapacity(
                                volume);

                    var custom =
                        _findingPolicies
                            .HasCustomStorageThreshold(
                                volume.MountPoint);

                    var alertOverride =
                        _findingPolicies
                            .HasStorageCapacityAlertOverride(
                                volume.MountPoint);

                    var policyLabel =
                        $"{(custom ? "Custom" : "Default")} · " +
                        $"{(alertOverride ? "Mount" : "Global")} " +
                        LinuxFindingPolicyStore
                            .StorageCapacityAlertModeLabel(
                                capacity.Mode);

                    return
                        new UnifiedStorageRow(
                            volume.Source,
                            volume.MountPoint,
                            volume.FileSystem,
                            volume.Size,
                            volume.Used,
                            volume.Available,
                            volume.PercentUsed,
                            ParseSystemWorkspacePercent(
                                volume.PercentUsed),
                            capacity.StatusLabel,
                            policyLabel,
                            CanConfigureCapacity:
                                true,
                            CanConfigureThreshold:
                                true,
                            CanRestoreDefaults:
                                custom);
                })
                .ToArray();

        var attention =
            rows.Count(row =>
                row.PercentValue >=
                85);

        _sharedStorageView.Update(
            new UnifiedStorageState(
                rows,
                $"{rows.Length} storage root(s) · " +
                $"{attention} capacity attention",
                Get<TextBlock>(
                        "StoragePolicyStatusText")
                    .Text ??
                "Select a mount to inspect its threshold policy.",
                Get<TextBlock>(
                        "StorageCapacityAlertStatusText")
                    .Text ??
                "Capacity alert policy is ready."));
    }

    private void UpdateSharedUnifiedLogs()
    {
        if (_sharedLogsView is null)
            return;

        var rows =
            _reliableLogRows
                .Select(row =>
                    new UnifiedLogRow(
                        row.Key,
                        row.Severity >=
                        OpsSeverity.Error
                            ? UnifiedLogSeverity.Error
                            : row.Severity >=
                              OpsSeverity.Warning
                                ? UnifiedLogSeverity.Warning
                                : UnifiedLogSeverity.Information,
                        row.SeverityLabel,
                        row.Source,
                        row.DisplayTime,
                        row.Count,
                        row.Message,
                        FormatLog(
                            row.Original)))
                .ToArray();

        var active =
            rows.Count(row =>
                row.Severity >=
                UnifiedLogSeverity.Warning);

        var background =
            rows.Count(row =>
                row.Severity ==
                UnifiedLogSeverity.Information);

        _sharedLogsView.Update(
            new UnifiedLogsState(
                rows,
                $"{rows.Length} retained group(s) · " +
                $"{active} warning/error · " +
                $"{background} informational",
                rows.Length == 0
                    ? "No journal observations were returned"
                    : string.Empty,
                rows.Length == 0
                    ? "The current capture contains no grouped journal evidence."
                    : "Log filters are ready."));
    }

    private static double ParseSystemWorkspacePercent(
        string value)
    {
        var normalized =
            (value ?? string.Empty)
                .Replace(
                    "%",
                    string.Empty,
                    StringComparison.Ordinal)
                .Trim();

        return double.TryParse(
            normalized,
            out var parsed)
            ? parsed
            : 0;
    }
}