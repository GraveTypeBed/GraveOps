using Avalonia.Controls;
using Avalonia.Input.Platform;
using GraveOps.Core.Hosts;
using GraveOps.Presentation.Avalonia.SystemWorkspaces;

namespace GraveOps.Desktop.Windows;

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

        _sharedLogsView.LogActionRequested +=
            SharedLogActionRequested;

        UpdateSharedUnifiedSystemWorkspaces(
            _snapshot);
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

    private void UpdateSharedUnifiedSystemWorkspaces(
        HostSnapshot? snapshot)
    {
        if (_sharedServicesView is null ||
            _sharedStorageView is null ||
            _sharedLogsView is null)
        {
            return;
        }

        if (snapshot is null)
        {
            _sharedServicesView.Update(
                UnifiedServicesState.Empty);

            _sharedStorageView.Update(
                UnifiedStorageState.Empty);

            _sharedLogsView.Update(
                UnifiedLogsState.Empty);

            return;
        }

        var services =
            snapshot.Services
                .Select(service =>
                    new UnifiedServiceRow(
                        service.Unit,
                        service.Description,
                        service.ActiveState,
                        service.SubState,
                        $"Startup policy · {service.UnitFileState}",
                        $"{service.Unit}\n" +
                        $"{service.Description}\n\n" +
                        $"State: {service.ActiveState}/{service.SubState}\n" +
                        $"Startup policy: {service.UnitFileState}\n\n" +
                        "The Windows provider currently exposes read-only service inventory.",
                        CanStart:
                            false,
                        CanStop:
                            false,
                        CanRestart:
                            false))
                .ToArray();

        _sharedServicesView.Update(
            new UnifiedServicesState(
                services,
                $"{services.Length} Windows service(s) captured",
                "Read-only Windows provider · service mutations are not exposed.",
                SafeModeEnabled:
                    false,
                CanToggleSafeMode:
                    false));

        var storage =
            snapshot.Storage
                .Select(volume =>
                {
                    var percent =
                        ParseSystemWorkspacePercent(
                            volume.PercentUsed);

                    var status =
                        percent >= 95
                            ? "CRITICAL"
                            : percent >= 85
                                ? "REVIEW"
                                : "READY";

                    return
                        new UnifiedStorageRow(
                            volume.Source,
                            volume.MountPoint,
                            volume.FileSystem,
                            volume.Size,
                            volume.Used,
                            volume.Available,
                            volume.PercentUsed,
                            percent,
                            status,
                            "Windows provider · read-only",
                            CanConfigureCapacity:
                                false,
                            CanConfigureThreshold:
                                false,
                            CanRestoreDefaults:
                                false);
                })
                .ToArray();

        var attention =
            storage.Count(row =>
                row.PercentValue >=
                85);

        _sharedStorageView.Update(
            new UnifiedStorageState(
                storage,
                $"{storage.Length} Windows volume(s) · " +
                $"{attention} capacity attention",
                "Windows storage inventory is read-only in the current provider.",
                "Capacity thresholds remain visible; policy editing is not available for Windows targets."));

        var logs =
            BuildWindowsUnifiedLogs(
                snapshot);

        var active =
            logs.Count(row =>
                row.Severity >=
                UnifiedLogSeverity.Warning);

        var background =
            logs.Count(row =>
                row.Severity ==
                UnifiedLogSeverity.Information);

        _sharedLogsView.Update(
            new UnifiedLogsState(
                logs,
                $"{logs.Length} Windows log group(s) · " +
                $"{active} warning/error · " +
                $"{background} informational",
                logs.Length == 0
                    ? "No Windows event or provider evidence was returned"
                    : string.Empty,
                logs.Length == 0
                    ? "The active Windows target returned no recent events or provider warnings."
                    : "Windows event and provider filters are ready."));
    }

    private static UnifiedLogRow[] BuildWindowsUnifiedLogs(
        HostSnapshot snapshot)
    {
        var captured =
            snapshot.CapturedAt
                .ToLocalTime()
                .ToString(
                    "g");

        var provider =
            snapshot.Warnings
                .Where(message =>
                    !string.IsNullOrWhiteSpace(
                        message))
                .Select(message =>
                    new
                    {
                        Severity =
                            UnifiedLogSeverity.Warning,
                        Label =
                            "WARNING",
                        Source =
                            "Provider",
                        Message =
                            message.Trim()
                    });

        var events =
            snapshot.RecentLogs
                .Where(message =>
                    !string.IsNullOrWhiteSpace(
                        message))
                .Select(message =>
                {
                    var severity =
                        WindowsLogSeverity(
                            message);

                    return new
                    {
                        Severity =
                            severity,
                        Label =
                            severity ==
                            UnifiedLogSeverity.Error
                                ? "ERROR"
                                : severity ==
                                  UnifiedLogSeverity.Warning
                                    ? "WARNING"
                                    : "INFO",
                        Source =
                            "Windows Event Log",
                        Message =
                            message.Trim()
                    };
                });

        return provider
            .Concat(
                events)
            .GroupBy(item =>
                new
                {
                    item.Severity,
                    item.Label,
                    item.Source,
                    item.Message
                })
            .Select(group =>
                new UnifiedLogRow(
                    $"{group.Key.Source}|{group.Key.Message}",
                    group.Key.Severity,
                    group.Key.Label,
                    group.Key.Source,
                    captured,
                    group.Count(),
                    group.Key.Message,
                    $"{group.Key.Label} · {group.Key.Source}\n" +
                    $"Captured: {captured}\n" +
                    $"Count: {group.Count()}\n\n" +
                    group.Key.Message))
            .OrderByDescending(row =>
                row.Severity)
            .ThenBy(row =>
                row.Source,
                StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static UnifiedLogSeverity WindowsLogSeverity(
        string message)
    {
        if (message.Contains(
                "critical",
                StringComparison.OrdinalIgnoreCase) ||
            message.Contains(
                "error",
                StringComparison.OrdinalIgnoreCase) ||
            message.Contains(
                "failed",
                StringComparison.OrdinalIgnoreCase))
        {
            return UnifiedLogSeverity.Error;
        }

        if (message.Contains(
                "warning",
                StringComparison.OrdinalIgnoreCase) ||
            message.Contains(
                "warn",
                StringComparison.OrdinalIgnoreCase))
        {
            return UnifiedLogSeverity.Warning;
        }

        return UnifiedLogSeverity.Information;
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