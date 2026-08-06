using Avalonia.Controls;
using GraveOps.Presentation.Avalonia.Activity;
using Avalonia.Input.Platform;

namespace GraveOps.Desktop.Windows;

public partial class MainWindow
{
    private UnifiedActivityView?
        _sharedActivityView;

    private void InitializeSharedUnifiedActivity()
    {
        var page =
            Get<Grid>(
                "HistoryPage");

        _sharedActivityView =
            new UnifiedActivityView();

        _sharedActivityView.NavigationRequested +=
            (_, e) =>
                Navigate(
                    e.NavigationKey);

        _sharedActivityView.CopyRequested +=
            SharedActivityCopyRequested;

        _sharedActivityView.ClearRequested +=
            (_, _) =>
                ClearSharedActivityHistory();

        page.Children.Add(
            _sharedActivityView);

        UpdateSharedUnifiedActivity();
    }

    private async void SharedActivityCopyRequested(
        object? sender,
        UnifiedActivityCopyRequestedEventArgs e)
    {
        var clipboard =
            TopLevel.GetTopLevel(
                    this)
                ?.Clipboard;

        if (clipboard is null)
            return;

        await clipboard.SetTextAsync(
            e.Text);
    }

    private void ClearSharedActivityHistory()
    {
        _activity.Clear();

        PopulateActivity();
        UpdateSharedUnifiedActivity();
    }

    private void UpdateSharedUnifiedActivity()
    {
        if (_sharedActivityView is null)
            return;

        var events =
            _activity
                .Select(item =>
                    new UnifiedActivityRow(
                        item.Timestamp,
                        ActivityStream(
                            item),
                        ActiveTargetDisplayName(),
                        ActivityComponent(
                            item),
                        item.Title,
                        item.Detail,
                        ActivitySeverity(
                            item),
                        ActivityNavigation(
                            item),
                        BuildWindowsActivityReplay(
                            item)))
                .ToArray();

        _sharedActivityView.Update(
            new UnifiedActivityState(
                events,
                _activity.Count,
                "Current Windows GraveOps session.",
                "Session-only history - Windows persistence has not been implemented."));
    }

    private static string ActivityStream(
        ActivityRow row)
    {
        var combined =
            $"{row.Title} {row.Detail}";

        if (row.Title.Equals(
                "Navigation",
                StringComparison.OrdinalIgnoreCase))
        {
            return "Navigation";
        }

        if (combined.Contains(
                "failed",
                StringComparison.OrdinalIgnoreCase) ||
            combined.Contains(
                "blocked",
                StringComparison.OrdinalIgnoreCase))
        {
            return "Incident";
        }

        if (combined.Contains(
                "saved",
                StringComparison.OrdinalIgnoreCase) ||
            combined.Contains(
                "cleared",
                StringComparison.OrdinalIgnoreCase) ||
            combined.Contains(
                "changed",
                StringComparison.OrdinalIgnoreCase) ||
            combined.Contains(
                "target",
                StringComparison.OrdinalIgnoreCase) ||
            combined.Contains(
                "capture",
                StringComparison.OrdinalIgnoreCase))
        {
            return "Operator action";
        }

        return "Operational";
    }

    private static string ActivityComponent(
        ActivityRow row)
    {
        if (row.Title.Contains(
                "Snapshot",
                StringComparison.OrdinalIgnoreCase))
        {
            return "Capture";
        }

        if (row.Title.Contains(
                "Navigation",
                StringComparison.OrdinalIgnoreCase))
        {
            return "Navigation";
        }

        if (row.Title.Contains(
                "Target",
                StringComparison.OrdinalIgnoreCase))
        {
            return "Target session";
        }

        return "GraveOps";
    }

    private static UnifiedActivitySeverity
        ActivitySeverity(
            ActivityRow row)
    {
        var combined =
            $"{row.Title} {row.Detail}";

        if (combined.Contains(
                "failed",
                StringComparison.OrdinalIgnoreCase))
        {
            return UnifiedActivitySeverity.Error;
        }

        if (combined.Contains(
                "blocked",
                StringComparison.OrdinalIgnoreCase) ||
            combined.Contains(
                "warning",
                StringComparison.OrdinalIgnoreCase))
        {
            return UnifiedActivitySeverity.Warning;
        }

        return UnifiedActivitySeverity.Info;
    }

    private static string ActivityNavigation(
        ActivityRow row)
    {
        if (row.Title.Equals(
                "Navigation",
                StringComparison.OrdinalIgnoreCase))
        {
            foreach (var navigation in
                     Navigation)
            {
                if (row.Detail.Contains(
                        navigation.Value.Title,
                        StringComparison.OrdinalIgnoreCase))
                {
                    return navigation.Key;
                }
            }
        }

        if (row.Title.Contains(
                "Snapshot",
                StringComparison.OrdinalIgnoreCase) ||
            row.Title.Contains(
                "Target",
                StringComparison.OrdinalIgnoreCase))
        {
            return "ServersNav";
        }

        return "DashboardNav";
    }

    private static string BuildWindowsActivityReplay(
        ActivityRow row) =>
        string.Join(
            Environment.NewLine,
            new[]
            {
                "GRAVEOPS WINDOWS SESSION REPLAY",
                string.Empty,
                $"Time: {row.Timestamp.ToLocalTime():g}",
                $"Event: {row.Title}",
                $"Stream: {ActivityStream(row)}",
                $"Component: {ActivityComponent(row)}",
                $"Severity: {UnifiedActivityLabels.Severity(ActivitySeverity(row))}",
                string.Empty,
                row.Detail,
                string.Empty,
                "Retention: current process session only"
            });
}