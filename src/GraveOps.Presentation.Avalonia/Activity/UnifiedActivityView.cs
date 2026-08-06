using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace GraveOps.Presentation.Avalonia.Activity;

public sealed class UnifiedActivityView :
    UserControl
{
    public static readonly string[]
        ClassFilters =
        {
            "All meaningful",
            "All events",
            "Incidents",
            "Health transitions",
            "Actions & changes",
            "Notifications",
            "Navigation"
        };

    public static readonly string[]
        SeverityFilters =
        {
            "All severities",
            "Warnings & errors",
            "Errors only"
        };

    public static readonly string[]
        TimeFilters =
        {
            "Last 24 hours",
            "Last 7 days",
            "All retained"
        };

    private readonly TextBlock
        _transitionMetric;
    private readonly TextBlock
        _activityMetric;
    private readonly TextBlock
        _incidentMetric;
    private readonly TextBlock
        _retentionMetric;
    private readonly ComboBox
        _classFilter;
    private readonly ComboBox
        _severityFilter;
    private readonly ComboBox
        _timeFilter;
    private readonly TextBox
        _sourceFilter;
    private readonly TextBox
        _textFilter;
    private readonly TextBlock
        _filterStatus;
    private readonly TextBlock
        _retentionDetail;
    private readonly ListBox
        _transitionsList;
    private readonly ListBox
        _activityList;
    private readonly ListBox
        _incidentsList;
    private readonly TextBlock
        _selectedTitle;
    private readonly TextBox
        _selectedDetail;
    private readonly TextBox
        _replayText;
    private readonly Button
        _copyReplayButton;
    private readonly Button
        _openRelatedButton;

    private UnifiedActivityState _state =
        UnifiedActivityState.Empty;

    private string _selectedNavigationKey =
        string.Empty;

    private bool _updating;

    public UnifiedActivityView()
    {
        HorizontalAlignment =
            HorizontalAlignment.Stretch;

        VerticalAlignment =
            VerticalAlignment.Stretch;

        _transitionMetric =
            MetricValue("0");

        _activityMetric =
            MetricValue("0");

        _incidentMetric =
            MetricValue("0");

        _retentionMetric =
            MetricValue("0/0");

        _classFilter =
            FilterComboBox(
                165,
                ClassFilters,
                0);

        _severityFilter =
            FilterComboBox(
                150,
                SeverityFilters,
                0);

        _timeFilter =
            FilterComboBox(
                135,
                TimeFilters,
                0);

        _sourceFilter =
            FilterTextBox(
                190);

        _textFilter =
            FilterTextBox(
                250);

        _filterStatus =
            new TextBlock
            {
                Text =
                    "History filters are ready.",
                TextWrapping =
                    TextWrapping.Wrap,
                Classes =
                {
                    "dim"
                }
            };

        _retentionDetail =
            new TextBlock
            {
                Text =
                    "No activity source has reported data.",
                TextWrapping =
                    TextWrapping.Wrap,
                Classes =
                {
                    "dim"
                }
            };

        _transitionsList =
            DataList();

        _activityList =
            DataList();

        _incidentsList =
            DataList();

        _transitionsList.SelectionChanged +=
            (_, _) =>
                SelectFrom(
                    _transitionsList);

        _activityList.SelectionChanged +=
            (_, _) =>
                SelectFrom(
                    _activityList);

        _incidentsList.SelectionChanged +=
            (_, _) =>
                SelectFrom(
                    _incidentsList);

        _selectedTitle =
            new TextBlock
            {
                Text =
                    "No history row selected",
                FontWeight =
                    FontWeight.SemiBold
            };

        _selectedDetail =
            new TextBox
            {
                IsReadOnly =
                    true,
                AcceptsReturn =
                    true,
                TextWrapping =
                    TextWrapping.Wrap,
                MinHeight =
                    110,
                Classes =
                {
                    "console",
                    "workspaceOutput"
                }
            };

        _replayText =
            new TextBox
            {
                IsReadOnly =
                    true,
                AcceptsReturn =
                    true,
                TextWrapping =
                    TextWrapping.Wrap,
                MinHeight =
                    130,
                Classes =
                {
                    "console",
                    "workspaceOutput"
                }
            };

        _copyReplayButton =
            new Button
            {
                Content =
                    "Copy replay",
                IsEnabled =
                    false
            };

        _copyReplayButton.Click +=
            (_, _) =>
                CopyRequested?.Invoke(
                    this,
                    new UnifiedActivityCopyRequestedEventArgs(
                        _replayText.Text ??
                        string.Empty));

        _openRelatedButton =
            new Button
            {
                Content =
                    "Open related",
                IsEnabled =
                    false
            };

        _openRelatedButton.Click +=
            (_, _) =>
                RequestNavigation();

        WireFilters();

        Content =
            BuildWorkspace();

        Update(
            UnifiedActivityState.Empty);
    }

    public event EventHandler?
        ClearRequested;

    public event EventHandler<UnifiedActivityNavigationRequestedEventArgs>?
        NavigationRequested;

    public event EventHandler<UnifiedActivityCopyRequestedEventArgs>?
        CopyRequested;

    public void Update(
        UnifiedActivityState state)
    {
        _state =
            state;

        _retentionDetail.Text =
            state.RetentionDetail;

        ApplyFilters();
    }

    private Control BuildWorkspace()
    {
        var content =
            new StackPanel
            {
                Spacing = 10,
                Margin =
                    new Thickness(
                        0,
                        0,
                        4,
                        4)
            };

        content.Children.Add(
            BuildHeader());

        content.Children.Add(
            BuildMetrics());

        content.Children.Add(
            BuildFilters());

        content.Children.Add(
            BuildPrimaryLists());

        content.Children.Add(
            BuildIncidentWorkspace());

        return
            new ScrollViewer
            {
                VerticalScrollBarVisibility =
                    global::Avalonia.Controls.Primitives
                        .ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility =
                    global::Avalonia.Controls.Primitives
                        .ScrollBarVisibility.Disabled,
                Content =
                    content
            };
    }

    private Control BuildHeader()
    {
        var heading =
            new StackPanel();

        heading.Children.Add(
            new TextBlock
            {
                Text =
                    "History & incidents",
                FontSize =
                    18,
                Classes =
                {
                    "sectionTitle"
                }
            });

        heading.Children.Add(
            new TextBlock
            {
                Text =
                    "Classified transitions, meaningful control-plane activity and replayable evidence.",
                Margin =
                    new Thickness(
                        0,
                        3,
                        0,
                        0),
                TextWrapping =
                    TextWrapping.Wrap,
                Classes =
                {
                    "pageSubtitle"
                }
            });

        var clear =
            new Button
            {
                Content =
                    "Clear history",
                Classes =
                {
                    "danger",
                    "compact"
                }
            };

        clear.Click +=
            (_, _) =>
                ClearRequested?.Invoke(
                    this,
                    EventArgs.Empty);

        Grid.SetColumn(
            _copyReplayButton,
            1);

        Grid.SetColumn(
            clear,
            2);

        var header =
            new Grid
            {
                ColumnDefinitions =
                    new ColumnDefinitions(
                        "*,Auto,Auto"),
                ColumnSpacing =
                    6
            };

        header.Children.Add(
            heading);

        header.Children.Add(
            _copyReplayButton);

        header.Children.Add(
            clear);

        return header;
    }

    private Control BuildMetrics()
    {
        var grid =
            new Grid
            {
                ColumnDefinitions =
                    new ColumnDefinitions(
                        "*,*,*,*"),
                ColumnSpacing =
                    8
            };

        grid.Children.Add(
            Metric(
                "VISIBLE TRANSITIONS",
                _transitionMetric));

        var activity =
            Metric(
                "VISIBLE ACTIVITY",
                _activityMetric);

        Grid.SetColumn(
            activity,
            1);

        grid.Children.Add(
            activity);

        var incidents =
            Metric(
                "VISIBLE INCIDENTS",
                _incidentMetric);

        Grid.SetColumn(
            incidents,
            2);

        grid.Children.Add(
            incidents);

        var retention =
            Metric(
                "VISIBLE / RETAINED",
                _retentionMetric);

        Grid.SetColumn(
            retention,
            3);

        grid.Children.Add(
            retention);

        return grid;
    }

    private Control BuildFilters()
    {
        var filters =
            new WrapPanel();

        foreach (var control in new Control[]
        {
            _classFilter,
            _severityFilter,
            _timeFilter,
            _sourceFilter,
            _textFilter
        })
        {
            control.Margin =
                new Thickness(
                    0,
                    0,
                    7,
                    7);

            filters.Children.Add(
                control);
        }

        var reset =
            new Button
            {
                Content =
                    "Reset filters",
                Margin =
                    new Thickness(
                        0,
                        0,
                        0,
                        7),
                Classes =
                {
                    "compact"
                }
            };

        reset.Click +=
            (_, _) =>
                ResetFilters();

        filters.Children.Add(
            reset);

        var stack =
            new StackPanel
            {
                Spacing =
                    8
            };

        stack.Children.Add(
            filters);

        stack.Children.Add(
            _filterStatus);

        stack.Children.Add(
            _retentionDetail);

        return Module(
            stack);
    }

    private Control BuildPrimaryLists()
    {
        var transitions =
            ListModule(
                "Health transitions",
                "State changes and classified health movement.",
                _transitionsList);

        var activity =
            ListModule(
                "Meaningful activity",
                "Operator actions, policy changes, notifications and navigation.",
                _activityList);

        Grid.SetColumn(
            activity,
            1);

        var grid =
            new Grid
            {
                ColumnDefinitions =
                    new ColumnDefinitions(
                        "*,*"),
                ColumnSpacing =
                    8,
                MinHeight =
                    250
            };

        grid.Children.Add(
            transitions);

        grid.Children.Add(
            activity);

        return grid;
    }

    private Control BuildIncidentWorkspace()
    {
        var incidentModule =
            ListModule(
                "Incidents",
                "Warning-or-higher transitions and activity.",
                _incidentsList);

        var selectedHeading =
            new Grid
            {
                ColumnDefinitions =
                    new ColumnDefinitions(
                        "*,Auto")
            };

        var selectedStack =
            new StackPanel();

        selectedStack.Children.Add(
            new TextBlock
            {
                Text =
                    "Selected event detail",
                Classes =
                {
                    "sectionTitle"
                }
            });

        selectedStack.Children.Add(
            _selectedTitle);

        selectedHeading.Children.Add(
            selectedStack);

        Grid.SetColumn(
            _openRelatedButton,
            1);

        selectedHeading.Children.Add(
            _openRelatedButton);

        var selected =
            new Grid
            {
                RowDefinitions =
                    new RowDefinitions(
                        "Auto,*,Auto,*"),
                RowSpacing =
                    7
            };

        selected.Children.Add(
            selectedHeading);

        Grid.SetRow(
            _selectedDetail,
            1);

        selected.Children.Add(
            _selectedDetail);

        var replayHeading =
            new TextBlock
            {
                Text =
                    "Incident replay",
                Classes =
                {
                    "sectionTitle"
                }
            };

        Grid.SetRow(
            replayHeading,
            2);

        selected.Children.Add(
            replayHeading);

        Grid.SetRow(
            _replayText,
            3);

        selected.Children.Add(
            _replayText);

        var selectedModule =
            Module(
                selected,
                minHeight: 310);

        Grid.SetColumn(
            selectedModule,
            1);

        var grid =
            new Grid
            {
                ColumnDefinitions =
                    new ColumnDefinitions(
                        "0.8*,1.2*"),
                ColumnSpacing =
                    8,
                MinHeight =
                    330
            };

        grid.Children.Add(
            incidentModule);

        grid.Children.Add(
            selectedModule);

        return grid;
    }

    private void WireFilters()
    {
        _classFilter.SelectionChanged +=
            (_, _) =>
                ApplyFilters();

        _severityFilter.SelectionChanged +=
            (_, _) =>
                ApplyFilters();

        _timeFilter.SelectionChanged +=
            (_, _) =>
                ApplyFilters();

        _sourceFilter.TextChanged +=
            (_, _) =>
                ApplyFilters();

        _textFilter.TextChanged +=
            (_, _) =>
                ApplyFilters();
    }

    private void ResetFilters()
    {
        _updating =
            true;

        try
        {
            _classFilter.SelectedIndex =
                0;

            _severityFilter.SelectedIndex =
                0;

            _timeFilter.SelectedIndex =
                0;

            _sourceFilter.Text =
                string.Empty;

            _textFilter.Text =
                string.Empty;
        }
        finally
        {
            _updating =
                false;
        }

        ApplyFilters();
    }

    private void ApplyFilters()
    {
        if (_updating)
            return;

        _updating =
            true;

        try
        {
            var selectedKey =
                SelectedRow()?.Key ??
                string.Empty;

            var classFilter =
                SelectedText(
                    _classFilter,
                    "All meaningful");

            var severityFilter =
                SelectedText(
                    _severityFilter,
                    "All severities");

            var timeFilter =
                SelectedText(
                    _timeFilter,
                    "Last 24 hours");

            var sourceFilter =
                _sourceFilter.Text ??
                string.Empty;

            var textFilter =
                _textFilter.Text ??
                string.Empty;

            var filtered =
                _state.Events
                    .Where(item =>
                        MatchesTime(
                            item,
                            timeFilter))
                    .Where(item =>
                        MatchesClass(
                            item,
                            classFilter))
                    .Where(item =>
                        MatchesSeverity(
                            item,
                            severityFilter))
                    .Where(item =>
                        MatchesAny(
                            sourceFilter,
                            item.Target,
                            item.Component,
                            item.Stream))
                    .Where(item =>
                        MatchesAny(
                            textFilter,
                            item.Title,
                            item.Detail,
                            item.Target,
                            item.Component,
                            item.Stream))
                    .OrderByDescending(
                        item =>
                            item.Timestamp)
                    .ToArray();

            var transitions =
                filtered
                    .Where(item =>
                        item.IsHealthTransition)
                    .ToArray();

            var activities =
                filtered
                    .Where(item =>
                        !item.IsHealthTransition)
                    .ToArray();

            var incidents =
                filtered
                    .Where(item =>
                        item.IsIncident)
                    .ToArray();

            _transitionMetric.Text =
                transitions.Length.ToString();

            _activityMetric.Text =
                activities.Length.ToString();

            _incidentMetric.Text =
                incidents.Length.ToString();

            _retentionMetric.Text =
                $"{filtered.Length}/" +
                $"{_state.RetainedCount}";

            _filterStatus.Text =
                filtered.Length == 0
                    ? _state.Events.Count == 0
                        ? "No retained history exists yet."
                        : "No history row matches the current filters."
                    : $"{filtered.Length} visible event(s) from " +
                      $"{_state.RetainedCount} retained. " +
                      _state.Status;

            SetRows(
                _transitionsList,
                transitions,
                "No health transition matches the current filters.",
                BuildTransitionItem);

            SetRows(
                _activityList,
                activities,
                "No activity row matches the current filters.",
                BuildActivityItem);

            SetRows(
                _incidentsList,
                incidents,
                "No warning-or-higher incident matches the current filters.",
                BuildIncidentItem);

            if (!string.IsNullOrWhiteSpace(
                    selectedKey) &&
                SelectByKey(
                    selectedKey))
            {
                return;
            }

            var initial =
                incidents.FirstOrDefault() ??
                transitions.FirstOrDefault() ??
                activities.FirstOrDefault();

            if (initial is not null)
            {
                SelectRow(
                    initial);

                SelectListItem(
                    initial.Key);

                return;
            }

            ClearSelection();
        }
        finally
        {
            _updating =
                false;
        }
    }

    private UnifiedActivityRow?
        SelectedRow() =>
        Tagged(
            _incidentsList) ??
        Tagged(
            _transitionsList) ??
        Tagged(
            _activityList);

    private void SelectFrom(
        ListBox list)
    {
        if (_updating)
            return;

        if (Tagged(list) is not { } row)
            return;

        _updating =
            true;

        try
        {
            foreach (var other in new[]
            {
                _transitionsList,
                _activityList,
                _incidentsList
            })
            {
                if (!ReferenceEquals(
                        other,
                        list))
                {
                    other.SelectedItem =
                        null;
                }
            }

            SelectRow(
                row);
        }
        finally
        {
            _updating =
                false;
        }
    }

    private void SelectRow(
        UnifiedActivityRow row)
    {
        _selectedNavigationKey =
            row.NavigationKey;

        _selectedTitle.Text =
            $"{row.Target} - {row.Component}";

        _selectedDetail.Text =
            $"{row.DisplayTime} - {row.Stream}\n" +
            $"{row.SeverityLabel} - {row.Title}\n\n" +
            row.Detail;

        _replayText.Text =
            string.IsNullOrWhiteSpace(
                row.Replay)
                ? BuildFallbackReplay(
                    row)
                : row.Replay;

        _openRelatedButton.IsEnabled =
            !string.IsNullOrWhiteSpace(
                row.NavigationKey);

        _copyReplayButton.IsEnabled =
            true;
    }

    private void ClearSelection()
    {
        _selectedNavigationKey =
            string.Empty;

        _selectedTitle.Text =
            "No history row selected";

        _selectedDetail.Text =
            string.Empty;

        _replayText.Text =
            "Adjust the filters or wait for a meaningful transition or activity event.";

        _openRelatedButton.IsEnabled =
            false;

        _copyReplayButton.IsEnabled =
            false;
    }

    private void RequestNavigation()
    {
        if (string.IsNullOrWhiteSpace(
                _selectedNavigationKey))
        {
            return;
        }

        NavigationRequested?.Invoke(
            this,
            new UnifiedActivityNavigationRequestedEventArgs(
                _selectedNavigationKey));
    }

    private bool SelectByKey(
        string key)
    {
        foreach (var list in new[]
        {
            _incidentsList,
            _transitionsList,
            _activityList
        })
        {
            foreach (var item in
                     list.Items.OfType<ListBoxItem>())
            {
                if (item.Tag is
                        UnifiedActivityRow row &&
                    row.Key.Equals(
                        key,
                        StringComparison.Ordinal))
                {
                    list.SelectedItem =
                        item;

                    SelectRow(
                        row);

                    return true;
                }
            }
        }

        return false;
    }

    private void SelectListItem(
        string key) =>
        SelectByKey(
            key);

    private static void SetRows(
        ListBox list,
        IReadOnlyList<UnifiedActivityRow> rows,
        string emptyText,
        Func<UnifiedActivityRow, ListBoxItem> factory)
    {
        list.ItemsSource =
            rows.Count == 0
                ? new[]
                {
                    EmptyItem(
                        emptyText)
                }
                : rows
                    .Select(factory)
                    .ToArray();
    }

    private static ListBoxItem
        BuildTransitionItem(
            UnifiedActivityRow row)
    {
        var grid =
            new Grid
            {
                ColumnDefinitions =
                    new ColumnDefinitions(
                        "120,130,140,*"),
                ColumnSpacing =
                    7
            };

        grid.Children.Add(
            Cell(
                row.DisplayTime,
                dim: true));

        var target =
            Cell(
                row.Target,
                bold: true,
                trim: true);

        Grid.SetColumn(
            target,
            1);

        grid.Children.Add(
            target);

        var component =
            Cell(
                row.Component,
                trim: true);

        Grid.SetColumn(
            component,
            2);

        grid.Children.Add(
            component);

        var title =
            Cell(
                row.Title,
                trim: true);

        Grid.SetColumn(
            title,
            3);

        grid.Children.Add(
            title);

        return Item(
            row,
            grid);
    }

    private static ListBoxItem
        BuildActivityItem(
            UnifiedActivityRow row)
    {
        var grid =
            new Grid
            {
                ColumnDefinitions =
                    new ColumnDefinitions(
                        "120,120,130,*"),
                ColumnSpacing =
                    7
            };

        grid.Children.Add(
            Cell(
                row.DisplayTime,
                dim: true));

        var stream =
            Cell(
                row.Stream,
                bold: true,
                trim: true);

        Grid.SetColumn(
            stream,
            1);

        grid.Children.Add(
            stream);

        var component =
            Cell(
                row.Component,
                trim: true);

        Grid.SetColumn(
            component,
            2);

        grid.Children.Add(
            component);

        var title =
            Cell(
                row.Title,
                trim: true);

        Grid.SetColumn(
            title,
            3);

        grid.Children.Add(
            title);

        return Item(
            row,
            grid);
    }

    private static ListBoxItem
        BuildIncidentItem(
            UnifiedActivityRow row)
    {
        var stack =
            new StackPanel
            {
                Spacing =
                    3
            };

        var header =
            new Grid
            {
                ColumnDefinitions =
                    new ColumnDefinitions(
                        "*,Auto")
            };

        header.Children.Add(
            Cell(
                $"{row.SeverityLabel} - " +
                $"{row.Component}",
                bold: true,
                trim: true));

        var time =
            Cell(
                row.DisplayTime,
                dim: true);

        Grid.SetColumn(
            time,
            1);

        header.Children.Add(
            time);

        stack.Children.Add(
            header);

        stack.Children.Add(
            Cell(
                row.Title,
                trim: true));

        stack.Children.Add(
            Cell(
                row.Target,
                dim: true,
                trim: true));

        return Item(
            row,
            stack);
    }

    private static ListBoxItem Item(
        UnifiedActivityRow row,
        Control content) =>
        new()
        {
            Tag =
                row,
            Content =
                content
        };

    private static ListBoxItem EmptyItem(
        string text) =>
        new()
        {
            IsEnabled =
                false,
            Content =
                new TextBlock
                {
                    Text =
                        text,
                    TextWrapping =
                        TextWrapping.Wrap,
                    Classes =
                    {
                        "muted"
                    }
                }
        };

    private static UnifiedActivityRow?
        Tagged(
            ListBox list) =>
        (
            list.SelectedItem as
            ListBoxItem
        )?.Tag as
        UnifiedActivityRow;

    private static string SelectedText(
        ComboBox comboBox,
        string fallback) =>
        comboBox.SelectedItem?
            .ToString() ??
        fallback;

    private static bool MatchesTime(
        UnifiedActivityRow row,
        string filter)
    {
        var since =
            filter switch
            {
                "Last 24 hours" =>
                    DateTimeOffset.Now.AddHours(
                        -24),
                "Last 7 days" =>
                    DateTimeOffset.Now.AddDays(
                        -7),
                _ =>
                    DateTimeOffset.MinValue
            };

        return row.Timestamp >=
               since;
    }

    private static bool MatchesClass(
        UnifiedActivityRow row,
        string filter) =>
        filter switch
        {
            "All meaningful" =>
                !row.Stream.Equals(
                    "Navigation",
                    StringComparison.OrdinalIgnoreCase),
            "All events" =>
                true,
            "Incidents" =>
                row.IsIncident,
            "Health transitions" =>
                row.IsHealthTransition,
            "Actions & changes" =>
                row.Stream.Equals(
                    "Operator action",
                    StringComparison.OrdinalIgnoreCase) ||
                row.Stream.Equals(
                    "Policy change",
                    StringComparison.OrdinalIgnoreCase) ||
                row.Stream.Equals(
                    "Operational",
                    StringComparison.OrdinalIgnoreCase),
            "Notifications" =>
                row.Stream.Equals(
                    "Notification",
                    StringComparison.OrdinalIgnoreCase),
            "Navigation" =>
                row.Stream.Equals(
                    "Navigation",
                    StringComparison.OrdinalIgnoreCase),
            _ =>
                true
        };

    private static bool MatchesSeverity(
        UnifiedActivityRow row,
        string filter) =>
        filter switch
        {
            "Warnings & errors" =>
                row.Severity >=
                UnifiedActivitySeverity.Warning,
            "Errors only" =>
                row.Severity >=
                UnifiedActivitySeverity.Error,
            _ =>
                true
        };

    private static bool MatchesAny(
        string filter,
        params string[] values)
    {
        if (string.IsNullOrWhiteSpace(
                filter))
        {
            return true;
        }

        return values.Any(value =>
            !string.IsNullOrWhiteSpace(
                value) &&
            value.Contains(
                filter,
                StringComparison.OrdinalIgnoreCase));
    }

    private static string BuildFallbackReplay(
        UnifiedActivityRow row) =>
        string.Join(
            Environment.NewLine,
            new[]
            {
                "GRAVEOPS INCIDENT REPLAY",
                string.Empty,
                $"Time: {row.DisplayTime}",
                $"Target: {row.Target}",
                $"Stream: {row.Stream}",
                $"Component: {row.Component}",
                $"Severity: {row.SeverityLabel}",
                $"Event: {row.Title}",
                string.Empty,
                row.Detail
            });

    private static ComboBox FilterComboBox(
        double width,
        IReadOnlyList<string> items,
        int selectedIndex) =>
        new()
        {
            Width =
                width,
            ItemsSource =
                items,
            SelectedIndex =
                selectedIndex
        };

    private static TextBox FilterTextBox(
        double width) =>
        new()
        {
            Width =
                width,
            Classes =
            {
                "filter"
            }
        };

    private static ListBox DataList() =>
        new()
        {
            MinHeight =
                165,
            MaxHeight =
                310,
            Classes =
            {
                "dataList"
            }
        };

    private static Border ListModule(
        string title,
        string subtitle,
        ListBox list)
    {
        var heading =
            new StackPanel();

        heading.Children.Add(
            new TextBlock
            {
                Text =
                    title,
                Classes =
                {
                    "sectionTitle"
                }
            });

        heading.Children.Add(
            new TextBlock
            {
                Text =
                    subtitle,
                TextWrapping =
                    TextWrapping.Wrap,
                Classes =
                {
                    "pageSubtitle"
                }
            });

        var grid =
            new Grid
            {
                RowDefinitions =
                    new RowDefinitions(
                        "Auto,*"),
                RowSpacing =
                    6
            };

        grid.Children.Add(
            heading);

        Grid.SetRow(
            list,
            1);

        grid.Children.Add(
            list);

        return Module(
            grid,
            minHeight: 230);
    }

    private static Border Module(
        Control child,
        double minHeight = 0) =>
        new()
        {
            Child =
                child,
            MinHeight =
                minHeight,
            Classes =
            {
                "module",
                "adaptive"
            }
        };

    private static Border Metric(
        string label,
        Control value)
    {
        var stack =
            new StackPanel();

        stack.Children.Add(
            new TextBlock
            {
                Text =
                    label,
                Classes =
                {
                    "eyebrow"
                }
            });

        stack.Children.Add(
            value);

        return
            new Border
            {
                Child =
                    stack,
                Classes =
                {
                    "metric"
                }
            };
    }

    private static TextBlock MetricValue(
        string text) =>
        new()
        {
            Text =
                text,
            Classes =
            {
                "metricValue"
            }
        };

    private static TextBlock Cell(
        string text,
        bool bold = false,
        bool dim = false,
        bool trim = false)
    {
        var cell =
            new TextBlock
            {
                Text =
                    text,
                FontWeight =
                    bold
                        ? FontWeight.SemiBold
                        : FontWeight.Normal
            };

        if (dim)
        {
            cell.Classes.Add(
                "dim");
        }

        if (trim)
        {
            cell.TextTrimming =
                TextTrimming.CharacterEllipsis;
        }

        return cell;
    }
}