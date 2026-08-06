using Avalonia.Controls;
using Avalonia.Interactivity;

namespace GraveOps.Desktop.Linux;

public partial class MainWindow
{
    private bool _historyReliabilityControlsReady;
    private bool _logReliabilityControlsReady;
    private IReadOnlyList<ReliableLogRow>
        _reliableLogRows =
            Array.Empty<ReliableLogRow>();

    private void EnsureHistoryReliabilityControls()
    {
        if (_historyReliabilityControlsReady)
            return;

        _historyReliabilityControlsReady = true;

        var classFilter =
            Get<ComboBox>("HistoryClassFilterComboBox");
        classFilter.ItemsSource =
            HistoryLogReliabilityPresenter
                .HistoryClassFilters;
        classFilter.SelectedIndex = 0;

        var severityFilter =
            Get<ComboBox>("HistorySeverityFilterComboBox");
        severityFilter.ItemsSource =
            HistoryLogReliabilityPresenter
                .HistorySeverityFilters;
        severityFilter.SelectedIndex = 0;

        var timeFilter =
            Get<ComboBox>("HistoryTimeFilterComboBox");
        timeFilter.ItemsSource =
            HistoryLogReliabilityPresenter
                .HistoryTimeFilters;
        timeFilter.SelectedIndex = 0;
    }

    private void EnsureLogReliabilityControls()
    {
        if (_logReliabilityControlsReady)
            return;

        _logReliabilityControlsReady = true;

        var severityFilter =
            Get<ComboBox>("LogsSeverityFilterComboBox");
        severityFilter.ItemsSource =
            HistoryLogReliabilityPresenter
                .LogSeverityFilters;
        severityFilter.SelectedIndex = 0;

        var timeFilter =
            Get<ComboBox>("LogsTimeFilterComboBox");
        timeFilter.ItemsSource =
            HistoryLogReliabilityPresenter
                .LogTimeFilters;
        timeFilter.SelectedIndex = 1;
    }

    private void PopulateReliableHistory()
    {
        EnsureHistoryReliabilityControls();

        _historyRows =
            _insightStore.BuildHistory(
                _history.Records,
                _controlPlane.State.Activities,
                _controlPlane.ActiveProfile.DisplayName);

        var transitionList =
            Get<ListBox>("HistoryTransitionsList");
        var activityList =
            Get<ListBox>("HistoryActivityList");

        var selected =
            transitionList.SelectedItem as
                InsightHistoryRow ??
            activityList.SelectedItem as
                InsightHistoryRow;
        var selectedKey = selected is null
            ? string.Empty
            : HistoryLogReliabilityPresenter
                .HistoryKey(selected);

        var projection =
            HistoryLogReliabilityPresenter.BuildHistory(
                _historyRows,
                SelectedText(
                    "HistoryClassFilterComboBox",
                    "All meaningful"),
                SelectedText(
                    "HistorySeverityFilterComboBox",
                    "All severities"),
                SelectedText(
                    "HistoryTimeFilterComboBox",
                    "Last 24 hours"),
                Get<TextBox>(
                        "HistorySourceFilterText")
                    .Text ?? string.Empty,
                Get<TextBox>(
                        "HistoryFilterText")
                    .Text ?? string.Empty);

        transitionList.ItemsSource =
            projection.Transitions;
        activityList.ItemsSource =
            projection.Activities;

        Get<ListBox>("HistoryIncidentList")
            .ItemsSource =
            projection.Incidents;

        Get<TextBlock>("HistoryTransitionMetricText")
            .Text =
            projection.Transitions.Count.ToString();
        Get<TextBlock>("HistoryActivityMetricText")
            .Text =
            projection.Activities.Count.ToString();
        Get<TextBlock>("HistoryIncidentMetricText")
            .Text =
            projection.Incidents.Count.ToString();
        Get<TextBlock>("HistoryRetentionMetricText")
            .Text =
            $"{projection.VisibleCount}/" +
            $"{_historyRows.Count}";

        Get<TextBlock>("HistoryFilterStatusText")
            .Text =
            projection.Summary;
        Get<TextBlock>("HistoryCachePathText")
            .Text =
            $"Fleet cache · {_insightStore.FilePath}";

        Get<Border>("HistoryTransitionsEmptyState")
            .IsVisible =
            projection.Transitions.Count == 0;
        Get<Border>("HistoryActivityEmptyState")
            .IsVisible =
            projection.Activities.Count == 0;

        Get<TextBlock>(
                "HistoryTransitionsEmptyText")
            .Text =
            _historyRows.Count == 0
                ? "No health transitions have been retained."
                : "No health transition matches the current filters.";
        Get<TextBlock>(
                "HistoryActivityEmptyText")
            .Text =
            _historyRows.Count == 0
                ? "No GraveOps activity has been retained."
                : "No activity row matches the current filters.";

        var visible = projection.Transitions
            .Concat(projection.Activities)
            .ToArray();
        var restored = visible.FirstOrDefault(item =>
            HistoryLogReliabilityPresenter
                .HistoryKey(item)
                .Equals(
                    selectedKey,
                    StringComparison.Ordinal));

        if (restored is not null)
        {
            if (restored.Stream.Equals(
                    "Health transition",
                    StringComparison.Ordinal))
            {
                transitionList.SelectedItem = restored;
            }
            else
            {
                activityList.SelectedItem = restored;
            }
        }
        else if (projection.Incidents.Count > 0)
        {
            SelectVisibleHistoryRow(
                projection.Incidents[0],
                transitionList,
                activityList);
        }
        else if (visible.Length > 0)
        {
            SelectVisibleHistoryRow(
                visible[0],
                transitionList,
                activityList);
        }
        else
        {
            ClearReliableHistorySelection();
        }
    }

    private void ApplyReliableLogsFilter()
    {
        EnsureLogReliabilityControls();

        var list =
            Get<ListBox>("LogsList");
        var selectedKey =
            (list.SelectedItem as ReliableLogRow)?
                .Key ??
            string.Empty;

        var projection =
            HistoryLogReliabilityPresenter.BuildLogs(
                _logs,
                Get<CheckBox>(
                        "ShowInformationalLogsCheckBox")
                    .IsChecked == true,
                SelectedText(
                    "LogsSeverityFilterComboBox",
                    "Warnings & errors"),
                SelectedText(
                    "LogsTimeFilterComboBox",
                    "Last 24 hours"),
                Get<TextBox>(
                        "LogsSourceFilterText")
                    .Text ?? string.Empty,
                Get<TextBox>(
                        "LogsTextFilterText")
                    .Text ?? string.Empty,
                _snapshot?.Warnings);

        _reliableLogRows =
            projection.Rows;
        list.ItemsSource =
            _reliableLogRows;

        list.SelectedItem =
            _reliableLogRows.FirstOrDefault(item =>
                item.Key.Equals(
                    selectedKey,
                    StringComparison.Ordinal));

        Get<TextBlock>("LogsActiveMetricText")
            .Text =
            projection.ActiveCount.ToString();
        Get<TextBlock>("LogsBackgroundMetricText")
            .Text =
            projection.BackgroundCount.ToString();
        Get<TextBlock>("LogsVisibleMetricText")
            .Text =
            projection.Rows.Count.ToString();
        Get<TextBlock>("LogsSourceMetricText")
            .Text =
            projection.SourceCount.ToString();
        Get<TextBlock>("LogsSummaryText")
            .Text =
            projection.Summary;
        Get<TextBlock>("LogsFilterStatusText")
            .Text =
            projection.Summary;

        var empty =
            projection.Rows.Count == 0;
        Get<Border>("LogsEmptyState")
            .IsVisible =
            empty;
        Get<TextBlock>("LogsEmptyTitleText")
            .Text =
            projection.EmptyTitle;
        Get<TextBlock>("LogsEmptyDetailText")
            .Text =
            projection.EmptyDetail;

        if (list.SelectedItem is null &&
            _reliableLogRows.Count > 0)
        {
            list.SelectedIndex = 0;
        }

        PopulateReliableLogSelection();
        ApplyDashboardLogContextProjection();
    }

    private void PopulateReliableLogSelection()
    {
        var selected =
            Get<ListBox>("LogsList")
                .SelectedItem as
            ReliableLogRow;

        if (selected is null)
        {
            Get<TextBlock>("LogsSelectedTitleText")
                .Text =
                "No journal group selected";
            Get<TextBox>("LogDetailText")
                .Text =
                Get<Border>("LogsEmptyState")
                    .IsVisible
                    ? Get<TextBlock>(
                            "LogsEmptyDetailText")
                        .Text
                    : "Select a journal group to inspect its evidence.";
            Get<Button>("LogsCopyDetailButton")
                .IsEnabled =
                false;
            return;
        }

        Get<TextBlock>("LogsSelectedTitleText")
            .Text =
            $"{selected.SeverityLabel} · " +
            $"{selected.Source}";
        Get<TextBox>("LogDetailText")
            .Text =
            FormatLog(selected.Original);
        Get<Button>("LogsCopyDetailButton")
            .IsEnabled =
            true;
    }

    private static void SelectVisibleHistoryRow(
        InsightHistoryRow row,
        ListBox transitionList,
        ListBox activityList)
    {
        if (row.Stream.Equals(
                "Health transition",
                StringComparison.Ordinal))
        {
            transitionList.SelectedItem = row;
        }
        else
        {
            activityList.SelectedItem = row;
        }
    }

    private void ClearReliableHistorySelection()
    {
        _selectedHistoryNavigation =
            string.Empty;
        Get<TextBlock>("HistorySelectedTitleText")
            .Text =
            "No history row selected";
        Get<TextBox>("HistorySelectedDetailText")
            .Text =
            string.Empty;
        Get<TextBox>("HistoryReplayText")
            .Text =
            "Adjust the filters or wait for a meaningful transition or activity event.";
        Get<Button>("HistoryOpenRelatedButton")
            .IsEnabled =
            false;
        Get<Button>("HistoryCopyReplayButton")
            .IsEnabled =
            false;
    }

    private string SelectedText(
        string controlName,
        string fallback) =>
        Get<ComboBox>(controlName)
            .SelectedItem?
            .ToString() ??
        fallback;

    private void HistoryReliabilityFilter_OnChanged(
        object? sender,
        SelectionChangedEventArgs e) =>
        PopulateReliableHistory();

    private void HistoryFilterText_OnTextChanged(
        object? sender,
        TextChangedEventArgs e) =>
        PopulateReliableHistory();

    private void HistorySourceFilterText_OnTextChanged(
        object? sender,
        TextChangedEventArgs e) =>
        PopulateReliableHistory();

    private void HistoryResetFiltersButton_OnClick(
        object? sender,
        RoutedEventArgs e)
    {
        Get<ComboBox>("HistoryClassFilterComboBox")
            .SelectedIndex = 0;
        Get<ComboBox>("HistorySeverityFilterComboBox")
            .SelectedIndex = 0;
        Get<ComboBox>("HistoryTimeFilterComboBox")
            .SelectedIndex = 0;
        Get<TextBox>("HistorySourceFilterText")
            .Text =
            string.Empty;
        Get<TextBox>("HistoryFilterText")
            .Text =
            string.Empty;
        PopulateReliableHistory();
    }

    private void LogsReliabilityFilter_OnChanged(
        object? sender,
        SelectionChangedEventArgs e)
    {
        ClearDashboardLogContext();
        ApplyReliableLogsFilter();
    }

    private void LogsSourceFilterText_OnTextChanged(
        object? sender,
        TextChangedEventArgs e)
    {
        ClearDashboardLogContext();
        ApplyReliableLogsFilter();
    }

    private void LogsTextFilterText_OnTextChanged(
        object? sender,
        TextChangedEventArgs e)
    {
        ClearDashboardLogContext();
        ApplyReliableLogsFilter();
    }

    private void LogsResetFiltersButton_OnClick(
        object? sender,
        RoutedEventArgs e)
    {
        ClearDashboardLogContext();
        Get<ComboBox>("LogsSeverityFilterComboBox")
            .SelectedIndex = 0;
        Get<ComboBox>("LogsTimeFilterComboBox")
            .SelectedIndex = 1;
        Get<CheckBox>("ShowInformationalLogsCheckBox")
            .IsChecked =
            false;
        Get<TextBox>("LogsSourceFilterText")
            .Text =
            string.Empty;
        Get<TextBox>("LogsTextFilterText")
            .Text =
            string.Empty;
        ApplyReliableLogsFilter();
    }

    private async void LogsCopyDetailButton_OnClick(
        object? sender,
        RoutedEventArgs e)
    {
        var clipboard =
            TopLevel.GetTopLevel(this)?
                .Clipboard;
        if (clipboard is null)
        {
            Get<TextBlock>("LogsFilterStatusText")
                .Text =
                "Clipboard access is unavailable.";
            return;
        }

        await Avalonia.Input.Platform
            .ClipboardExtensions.SetTextAsync(
                clipboard,
                Get<TextBox>("LogDetailText")
                    .Text ??
                string.Empty);
        Get<TextBlock>("LogsFilterStatusText")
            .Text =
            "Selected journal detail copied.";
    }

    private void LogsOpenIntelligenceButton_OnClick(
        object? sender,
        RoutedEventArgs e) =>
        Navigate("IntelligenceNav");
}
