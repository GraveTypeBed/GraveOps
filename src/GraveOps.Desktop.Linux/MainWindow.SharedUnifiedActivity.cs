using Avalonia.Controls;
using GraveOps.Presentation.Avalonia.Activity;
using Avalonia.Input.Platform;

namespace GraveOps.Desktop.Linux;

public partial class MainWindow
{
    private UnifiedActivityView?
        _sharedActivityView;

    private void InitializeSharedUnifiedActivity()
    {
        var page =
            Get<Grid>(
                "HistoryPage");

        foreach (var child in
                 page.Children)
        {
            child.IsVisible =
                false;
        }

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

        _sharedActivityView.Update(
            UnifiedActivityState.Empty);
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
        _history.Clear();
        _controlPlane.State.ClearActivities();

        PopulateHistoryV43();
        PopulateControlPlaneFoundation();
        UpdateSharedUnifiedActivity();
    }

    private void UpdateSharedUnifiedActivity()
    {
        if (_sharedActivityView is null)
            return;

        var projection =
            HistoryLogReliabilityPresenter.BuildHistory(
                _historyRows,
                "All events",
                "All severities",
                "All retained",
                string.Empty,
                string.Empty);

        var events =
            projection.Transitions
                .Concat(
                    projection.Activities)
                .OrderByDescending(item =>
                    item.Timestamp)
                .Select(item =>
                    new UnifiedActivityRow(
                        item.Timestamp,
                        item.Stream,
                        item.Target,
                        item.Component,
                        item.Transition,
                        item.Detail,
                        ToUnifiedActivitySeverity(
                            item.Severity),
                        item.NavigationName,
                        _insightStore.BuildIncidentReplay(
                            item)))
                .ToArray();

        _sharedActivityView.Update(
            new UnifiedActivityState(
                events,
                _historyRows.Count,
                projection.Summary,
                $"Fleet cache - {_insightStore.FilePath}"));
    }

    private static UnifiedActivitySeverity
        ToUnifiedActivitySeverity(
            OpsSeverity severity) =>
        severity switch
        {
            OpsSeverity.Healthy =>
                UnifiedActivitySeverity.Healthy,
            OpsSeverity.Info =>
                UnifiedActivitySeverity.Info,
            OpsSeverity.Warning =>
                UnifiedActivitySeverity.Warning,
            OpsSeverity.Error =>
                UnifiedActivitySeverity.Error,
            OpsSeverity.Critical =>
                UnifiedActivitySeverity.Critical,
            _ =>
                UnifiedActivitySeverity.Info
        };
}