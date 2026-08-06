using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace GraveOps.Presentation.Avalonia.OperationsWorkspaces;

public sealed class UnifiedDockerView :
    UserControl
{
    private readonly TextBlock _daemon;
    private readonly TextBlock _total;
    private readonly TextBlock _running;
    private readonly TextBlock _attention;
    private readonly TextBlock _projects;
    private readonly TextBox _filter;
    private readonly CheckBox _showExited;
    private readonly TextBlock _summary;
    private readonly StackPanel _rows;
    private readonly Border _empty;

    private readonly TextBlock _selectedName;
    private readonly TextBlock _selectedState;
    private readonly TextBlock _detailStatus;
    private readonly Button _start;
    private readonly Button _restart;
    private readonly Button _stop;
    private readonly Button _restartProject;
    private readonly Button _refreshDetail;
    private readonly TextBlock _image;
    private readonly TextBlock _compose;
    private readonly TextBlock _lifecycle;
    private readonly TextBlock _resources;
    private readonly TextBlock _containerId;
    private readonly TextBlock _restartPolicy;
    private readonly TextBlock _networks;
    private readonly TextBlock _actionStatus;

    private readonly Button _overviewTab;
    private readonly Button _portsTab;
    private readonly Button _mountsTab;
    private readonly Button _environmentTab;
    private readonly Border _overviewPanel;
    private readonly Border _portsPanel;
    private readonly Border _mountsPanel;
    private readonly Border _environmentPanel;
    private readonly TextBlock _ports;
    private readonly TextBox _mounts;
    private readonly TextBox _environment;

    private readonly Button _cleanedLogs;
    private readonly Button _rawLogs;
    private readonly TextBox _logFilter;
    private readonly TextBlock _logsStatus;
    private readonly TextBox _logs;
    private readonly Button _copyCleaned;
    private readonly Button _copyRaw;
    private readonly TextBlock _workspaceStatus;

    private UnifiedDockerState _state =
        UnifiedDockerState.Empty;

    private UnifiedDockerRow? _selected;
    private string _detailTab =
        "overview";
    private bool _showRawLogs;

    public UnifiedDockerView()
    {
        HorizontalAlignment =
            HorizontalAlignment.Stretch;
        VerticalAlignment =
            VerticalAlignment.Stretch;

        _daemon =
            OperationsUi.Dim(
                "Capture pending");

        _total =
            OperationsUi.MetricValue();
        _running =
            OperationsUi.MetricValue();
        _attention =
            OperationsUi.MetricValue();
        _projects =
            OperationsUi.MetricValue();

        _filter =
            new TextBox
            {
                Width = 280,
                PlaceholderText =
                    "Filter group, container, image, state or port",
                Classes =
                {
                    "filter"
                }
            };

        _showExited =
            new CheckBox
            {
                Content = "Show exited",
                VerticalAlignment =
                    VerticalAlignment.Center
            };

        _summary =
            OperationsUi.Dim(
                "0 shown");

        _rows =
            new StackPanel
            {
                Spacing = 2
            };

        _empty =
            OperationsUi.EmptyState(
                "No containers match the current view",
                "Refresh Docker, clear the filter or enable Show exited.");

        _selectedName =
            OperationsUi.Title(
                "No container selected");

        _selectedState =
            OperationsUi.Subtitle(
                "--");

        _detailStatus =
            OperationsUi.Dim(
                "Select a container to inspect.");

        _start =
            OperationsUi.Compact(
                "Start");

        _restart =
            OperationsUi.Compact(
                "Restart");
        _restart.Classes.Add(
            "primary");

        _stop =
            OperationsUi.Compact(
                "Stop");
        _stop.Classes.Add(
            "danger");

        _restartProject =
            OperationsUi.Compact(
                "Restart DUMB project");
        _restartProject.Classes.Add(
            "danger");

        _refreshDetail =
            OperationsUi.Compact(
                "Refresh detail");

        _image =
            DetailValue();
        _compose =
            DetailValue();
        _lifecycle =
            DetailValue();
        _resources =
            DetailValue();
        _containerId =
            DetailValue();
        _restartPolicy =
            DetailValue();
        _networks =
            DetailValue();
        _actionStatus =
            DetailValue();

        _overviewTab =
            SegmentButton(
                "Overview");

        _portsTab =
            SegmentButton(
                "Ports");

        _mountsTab =
            SegmentButton(
                "Mounts");

        _environmentTab =
            SegmentButton(
                "Environment");

        _ports =
            new TextBlock
            {
                Text = "--",
                TextWrapping =
                    TextWrapping.NoWrap,
                Classes =
                {
                    "mono"
                }
            };

        _mounts =
            OperationsUi.Console(
                "Select a container to inspect mounts.",
                90,
                170);
        _mounts.TextWrapping =
            TextWrapping.NoWrap;

        _environment =
            OperationsUi.Console(
                "Environment-variable names only. Values are never displayed.",
                90,
                170);

        _overviewPanel =
            BuildOverviewPanel();

        _portsPanel =
            BuildTextPanel(
                "Published ports",
                "Container ports and host bindings.",
                _ports);

        _mountsPanel =
            BuildTextPanel(
                "Mounts",
                "Source, destination and access mode.",
                _mounts);

        _environmentPanel =
            BuildTextPanel(
                "Environment-variable names",
                "Names only. Values never enter the shared presentation.",
                _environment);

        _cleanedLogs =
            SegmentButton(
                "Cleaned");

        _rawLogs =
            SegmentButton(
                "Raw");

        _logFilter =
            new TextBox
            {
                Width = 230,
                PlaceholderText =
                    "Filter source, file or message",
                Classes =
                {
                    "filter"
                }
            };

        _logsStatus =
            OperationsUi.Subtitle(
                "Not captured");

        _logs =
            OperationsUi.Console(
                "Select a container to capture the last 200 log lines on demand.",
                190,
                500);

        _copyCleaned =
            OperationsUi.Compact(
                "Copy cleaned");

        _copyRaw =
            OperationsUi.Compact(
                "Copy raw");

        _workspaceStatus =
            OperationsUi.Muted(
                "Docker workspace capture pending.");

        _filter.TextChanged +=
            (_, _) =>
                RenderRows();

        _showExited.Click +=
            (_, _) =>
            {
                ShowExitedChanged?.Invoke(
                    this,
                    EventArgs.Empty);
                RenderRows();
            };

        _overviewTab.Click +=
            (_, _) =>
                SetDetailTab(
                    "overview");

        _portsTab.Click +=
            (_, _) =>
                SetDetailTab(
                    "ports");

        _mountsTab.Click +=
            (_, _) =>
                SetDetailTab(
                    "mounts");

        _environmentTab.Click +=
            (_, _) =>
                SetDetailTab(
                    "environment");

        _cleanedLogs.Click +=
            (_, _) =>
            {
                _showRawLogs =
                    false;
                RenderLogs();
            };

        _rawLogs.Click +=
            (_, _) =>
            {
                _showRawLogs =
                    true;
                RenderLogs();
            };

        _logFilter.TextChanged +=
            (_, _) =>
                RenderLogs();

        _start.Click +=
            (_, _) =>
                RequestAction(
                    UnifiedDockerAction.Start);

        _stop.Click +=
            (_, _) =>
                RequestAction(
                    UnifiedDockerAction.Stop);

        _restart.Click +=
            (_, _) =>
                RequestAction(
                    UnifiedDockerAction.Restart);

        _restartProject.Click +=
            (_, _) =>
                RequestAction(
                    UnifiedDockerAction.RestartProject);

        _refreshDetail.Click +=
            (_, _) =>
            {
                if (_selected is not null)
                {
                    DetailRefreshRequested?.Invoke(
                        this,
                        new UnifiedDockerSelectionRequestedEventArgs(
                            _selected));
                }
            };

        _copyCleaned.Click +=
            (_, _) =>
                CopyRequested?.Invoke(
                    this,
                    new UnifiedDockerCopyRequestedEventArgs(
                        _state.Detail.CleanedLogs,
                        "Cleaned Docker log summary copied."));

        _copyRaw.Click +=
            (_, _) =>
                CopyRequested?.Invoke(
                    this,
                    new UnifiedDockerCopyRequestedEventArgs(
                        _state.Detail.RawLogs,
                        "Redacted raw Docker log output copied."));

        Content =
            BuildWorkspace();

        Update(
            UnifiedDockerState.Empty);
    }

    public event EventHandler?
        RefreshRequested;

    public event EventHandler<UnifiedDockerSelectionRequestedEventArgs>?
        SelectionRequested;

    public event EventHandler<UnifiedDockerSelectionRequestedEventArgs>?
        DetailRefreshRequested;

    public event EventHandler<UnifiedDockerActionRequestedEventArgs>?
        ActionRequested;

    public event EventHandler<UnifiedDockerCopyRequestedEventArgs>?
        CopyRequested;

    public event EventHandler?
        ShowExitedChanged;

    public bool ShowExited =>
        _showExited.IsChecked ==
        true;

    public void Update(
        UnifiedDockerState state)
    {
        var selectedKey =
            _selected?.Key;

        _state =
            state ?? UnifiedDockerState.Empty;

        _daemon.Text =
            _state.DaemonStatus;

        _workspaceStatus.Text =
            _state.WorkspaceStatus;

        _showExited.IsChecked =
            _state.ShowExited;

        _total.Text =
            _state.Rows.Count.ToString();

        _running.Text =
            _state.Rows
                .Count(row =>
                    row.IsRunning)
                .ToString();

        _attention.Text =
            _state.Rows
                .Count(row =>
                    row.HasAttention)
                .ToString();

        _projects.Text =
            _state.Rows
                .Select(row =>
                    row.Group)
                .Where(value =>
                    !string.IsNullOrWhiteSpace(
                        value) &&
                    !value.Equals(
                        "Unclassified",
                        StringComparison.OrdinalIgnoreCase))
                .Distinct(
                    StringComparer.OrdinalIgnoreCase)
                .Count()
                .ToString();

        _selected =
            _state.Rows.FirstOrDefault(row =>
                row.Key.Equals(
                    selectedKey,
                    StringComparison.OrdinalIgnoreCase)) ??
            _state.Rows.FirstOrDefault();

        RenderRows();
    }

    private Control BuildWorkspace()
    {
        var content =
            new StackPanel
            {
                Spacing = 10,
                Margin = new Thickness(
                    0,
                    0,
                    4,
                    4)
            };

        var refresh =
            OperationsUi.Compact(
                "Refresh");

        refresh.Classes.Add(
            "primary");

        refresh.Click +=
            (_, _) =>
                RefreshRequested?.Invoke(
                    this,
                    EventArgs.Empty);

        var header =
            new Grid
            {
                ColumnDefinitions =
                    new ColumnDefinitions(
                        "*,Auto,Auto"),
                ColumnSpacing = 10
            };

        header.Children.Add(
            new StackPanel
            {
                Children =
                {
                    OperationsUi.Title(
                        "Docker",
                        18),
                    OperationsUi.Subtitle(
                        "Compose-aware fleet health, selected-container inspection, bounded logs and guarded verified actions.")
                }
            });

        Grid.SetColumn(
            _daemon,
            1);
        Grid.SetColumn(
            refresh,
            2);

        header.Children.Add(
            _daemon);
        header.Children.Add(
            refresh);

        content.Children.Add(
            header);

        content.Children.Add(
            BuildMetrics());

        content.Children.Add(
            BuildFilterBar());

        content.Children.Add(
            BuildContainerTable());

        content.Children.Add(
            BuildSelectedWorkspace());

        content.Children.Add(
            BuildLogsWorkspace());

        content.Children.Add(
            new Border
            {
                Classes =
                {
                    "arrStatusBar"
                },
                Child =
                    _workspaceStatus
            });

        return
            OperationsUi.Scroll(
                content);
    }

    private Control BuildMetrics()
    {
        var metrics =
            new Grid
            {
                ColumnDefinitions =
                    new ColumnDefinitions(
                        "*,*,*,*"),
                ColumnSpacing = 10
            };

        var running =
            OperationsUi.Metric(
                "RUNNING",
                _running);

        var attention =
            OperationsUi.Metric(
                "NEEDS ATTENTION",
                _attention);

        var projects =
            OperationsUi.Metric(
                "COMPOSE PROJECTS",
                _projects);

        Grid.SetColumn(
            running,
            1);
        Grid.SetColumn(
            attention,
            2);
        Grid.SetColumn(
            projects,
            3);

        metrics.Children.Add(
            OperationsUi.Metric(
                "CONTAINERS",
                _total));
        metrics.Children.Add(
            running);
        metrics.Children.Add(
            attention);
        metrics.Children.Add(
            projects);

        return metrics;
    }

    private Control BuildFilterBar()
    {
        var grid =
            new Grid
            {
                ColumnDefinitions =
                    new ColumnDefinitions(
                        "280,Auto,*"),
                ColumnSpacing = 10
            };

        Grid.SetColumn(
            _showExited,
            1);

        Grid.SetColumn(
            _summary,
            2);

        _summary.HorizontalAlignment =
            HorizontalAlignment.Right;
        _summary.VerticalAlignment =
            VerticalAlignment.Center;

        grid.Children.Add(
            _filter);
        grid.Children.Add(
            _showExited);
        grid.Children.Add(
            _summary);

        return
            OperationsUi.Inset(
                grid);
    }

    private Control BuildContainerTable()
    {
        var header =
            new Grid
            {
                ColumnDefinitions =
                    new ColumnDefinitions(
                        "95,150,1.2*,105,100,105,145,1.1*"),
                ColumnSpacing = 7,
                Children =
                {
                    OperationsUi.ColumnHeader(
                        "GROUP",
                        0),
                    OperationsUi.ColumnHeader(
                        "CONTAINER",
                        1),
                    OperationsUi.ColumnHeader(
                        "IMAGE",
                        2),
                    OperationsUi.ColumnHeader(
                        "STATE",
                        3),
                    OperationsUi.ColumnHeader(
                        "HEALTH",
                        4),
                    OperationsUi.ColumnHeader(
                        "RESTARTS",
                        5),
                    OperationsUi.ColumnHeader(
                        "RESOURCES",
                        6),
                    OperationsUi.ColumnHeader(
                        "PUBLISHED PORTS",
                        7)
                }
            };

        var body =
            new Grid
            {
                MinHeight = 180,
                MaxHeight = 270,
                Children =
                {
                    OperationsUi.Scroll(
                        _rows,
                        260),
                    _empty
                }
            };

        var table =
            new Grid
            {
                RowDefinitions =
                    new RowDefinitions(
                        "Auto,*"),
                RowSpacing = 6,
                Children =
                {
                    new Border
                    {
                        Classes =
                        {
                            "tableHeaderBar"
                        },
                        Child = header
                    },
                    body
                }
            };

        Grid.SetRow(
            body,
            1);

        return
            OperationsUi.Module(
                table);
    }

    private Control BuildSelectedWorkspace()
    {
        var heading =
            new Grid
            {
                ColumnDefinitions =
                    new ColumnDefinitions(
                        "*,Auto"),
                ColumnSpacing = 10
            };

        heading.Children.Add(
            new StackPanel
            {
                Children =
                {
                    _selectedName,
                    _selectedState,
                    _detailStatus
                }
            });

        Grid.SetColumn(
            _refreshDetail,
            1);

        heading.Children.Add(
            _refreshDetail);

        var actions =
            new WrapPanel
            {
                Children =
                {
                    _start,
                    _restart,
                    _stop,
                    _restartProject
                }
            };

        foreach (var child in
                 actions.Children)
        {
            child.Margin =
                new Thickness(
                    0,
                    0,
                    6,
                    6);
        }

        var tabs =
            OperationsUi.Inset(
                new WrapPanel
                {
                    Children =
                    {
                        _overviewTab,
                        _portsTab,
                        _mountsTab,
                        _environmentTab
                    }
                },
                6);

        foreach (var child in
                 ((WrapPanel)tabs.Child!).Children)
        {
            child.Margin =
                new Thickness(
                    0,
                    0,
                    6,
                    0);
        }

        var panels =
            new Grid
            {
                Children =
                {
                    _overviewPanel,
                    _portsPanel,
                    _mountsPanel,
                    _environmentPanel
                }
            };

        var grid =
            new Grid
            {
                RowDefinitions =
                    new RowDefinitions(
                        "Auto,Auto,Auto,*"),
                RowSpacing = 8,
                Children =
                {
                    heading,
                    actions,
                    tabs,
                    panels
                }
            };

        Grid.SetRow(
            actions,
            1);
        Grid.SetRow(
            tabs,
            2);
        Grid.SetRow(
            panels,
            3);

        return
            OperationsUi.Module(
                grid);
    }

    private Border BuildOverviewPanel()
    {
        var first =
            new Grid
            {
                ColumnDefinitions =
                    new ColumnDefinitions(
                        "*,*,*,*"),
                ColumnSpacing = 8
            };

        first.Children.Add(
            DetailCard(
                "IMAGE",
                _image,
                0));
        first.Children.Add(
            DetailCard(
                "COMPOSE",
                _compose,
                1));
        first.Children.Add(
            DetailCard(
                "LIFECYCLE",
                _lifecycle,
                2));
        first.Children.Add(
            DetailCard(
                "RESOURCES",
                _resources,
                3));

        var second =
            new Grid
            {
                ColumnDefinitions =
                    new ColumnDefinitions(
                        "0.75*,1.05*,1.1*,1.5*"),
                ColumnSpacing = 8
            };

        second.Children.Add(
            DetailBlock(
                "CONTAINER ID",
                _containerId,
                0));
        second.Children.Add(
            DetailBlock(
                "RESTART POLICY",
                _restartPolicy,
                1));
        second.Children.Add(
            DetailBlock(
                "NETWORKS",
                _networks,
                2));
        second.Children.Add(
            DetailBlock(
                "ACTION RESULT",
                _actionStatus,
                3));

        return
            OperationsUi.Inset(
                new StackPanel
                {
                    Spacing = 8,
                    Children =
                    {
                        first,
                        second
                    }
                });
    }

    private static Border BuildTextPanel(
        string title,
        string subtitle,
        Control content)
    {
        var grid =
            new Grid
            {
                RowDefinitions =
                    new RowDefinitions(
                        "Auto,*"),
                RowSpacing = 6
            };

        grid.Children.Add(
            new StackPanel
            {
                Children =
                {
                    OperationsUi.Title(
                        title),
                    OperationsUi.Subtitle(
                        subtitle)
                }
            });

        Grid.SetRow(
            content,
            1);

        grid.Children.Add(
            content);

        return
            OperationsUi.Inset(
                grid);
    }

    private Control BuildLogsWorkspace()
    {
        var controls =
            new WrapPanel
            {
                Children =
                {
                    _cleanedLogs,
                    _rawLogs,
                    _logFilter,
                    _copyCleaned,
                    _copyRaw
                }
            };

        foreach (var child in
                 controls.Children)
        {
            child.Margin =
                new Thickness(
                    0,
                    0,
                    7,
                    6);
        }

        var heading =
            new Grid
            {
                ColumnDefinitions =
                    new ColumnDefinitions(
                        "*,Auto"),
                ColumnSpacing = 10
            };

        heading.Children.Add(
            new StackPanel
            {
                Children =
                {
                    OperationsUi.Title(
                        "Recent container logs"),
                    _logsStatus
                }
            });

        Grid.SetColumn(
            controls,
            1);

        heading.Children.Add(
            controls);

        var grid =
            new Grid
            {
                RowDefinitions =
                    new RowDefinitions(
                        "Auto,*"),
                RowSpacing = 8,
                Children =
                {
                    heading,
                    _logs
                }
            };

        Grid.SetRow(
            _logs,
            1);

        return
            OperationsUi.Module(
                grid);
    }

    private void RenderRows()
    {
        var filter =
            _filter.Text?.Trim() ??
            string.Empty;

        var showExited =
            _showExited.IsChecked ==
            true;

        var rows =
            _state.Rows
                .Where(row =>
                    showExited ||
                    row.IsRunning ||
                    row.HasAttention)
                .Where(row =>
                    Matches(
                        filter,
                        row.Group,
                        row.Name,
                        row.Image,
                        row.State,
                        row.Health,
                        row.Ports,
                        row.ComposeOwnership))
                .ToArray();

        _rows.Children.Clear();

        foreach (var row in rows)
        {
            var grid =
                new Grid
                {
                    ColumnDefinitions =
                        new ColumnDefinitions(
                            "95,150,1.2*,105,100,105,145,1.1*"),
                    ColumnSpacing = 7,
                    Children =
                    {
                        OperationsUi.Cell(
                            row.Group,
                            0,
                            true),
                        OperationsUi.Cell(
                            row.Name,
                            1,
                            true),
                        OperationsUi.Cell(
                            row.Image,
                            2),
                        OperationsUi.Cell(
                            row.State,
                            3),
                        OperationsUi.Cell(
                            row.Health,
                            4,
                            false,
                            "muted"),
                        OperationsUi.Cell(
                            row.RestartSummary,
                            5),
                        OperationsUi.Cell(
                            row.Resources,
                            6,
                            false,
                            "muted"),
                        OperationsUi.Cell(
                            row.Ports,
                            7,
                            false,
                            "dim")
                    }
                };

            var button =
                OperationsUi.RowButton(
                    grid);

            button.Click +=
                (_, _) =>
                {
                    _selected =
                        row;

                    SelectionRequested?.Invoke(
                        this,
                        new UnifiedDockerSelectionRequestedEventArgs(
                            row));

                    RenderSelection();
                };

            _rows.Children.Add(
                button);
        }

        var hidden =
            _state.Rows.Count -
            rows.Length;

        _summary.Text =
            $"{rows.Length} shown | " +
            $"{_state.Rows.Count(row => row.IsRunning)} running | " +
            $"{_state.Rows.Count(row => row.HasAttention)} attention | " +
            $"{hidden} hidden";

        _empty.IsVisible =
            rows.Length == 0;
        _rows.IsVisible =
            rows.Length > 0;

        if (_selected is null ||
            !rows.Any(row =>
                row.Key.Equals(
                    _selected.Key,
                    StringComparison.OrdinalIgnoreCase)))
        {
            _selected =
                rows.FirstOrDefault();
        }

        RenderSelection();
    }

    private void RenderSelection()
    {
        var row =
            _selected;

        var detail =
            row is not null &&
            _state.Detail.ContainerKey.Equals(
                row.Key,
                StringComparison.OrdinalIgnoreCase)
                ? _state.Detail
                : UnifiedDockerDetail.Empty;

        if (row is null)
        {
            _selectedName.Text =
                "No container selected";
            _selectedState.Text =
                "--";
            _detailStatus.Text =
                _state.WorkspaceStatus;
            _image.Text =
                "--";
            _compose.Text =
                "--";
            _lifecycle.Text =
                "--";
            _resources.Text =
                "--";
            _containerId.Text =
                "--";
            _restartPolicy.Text =
                "--";
            _networks.Text =
                "--";
            _actionStatus.Text =
                "No container action run.";
            _ports.Text =
                "--";
            _mounts.Text =
                "Select a container to inspect mounts.";
            _environment.Text =
                "Environment-variable names only. Values are never displayed.";
        }
        else
        {
            _selectedName.Text =
                row.Name;
            _selectedState.Text =
                $"{row.State} | {row.Health}";
            _detailStatus.Text =
                detail.ContainerKey.Length == 0
                    ? "Detail capture is unavailable for this provider."
                    : detail.Evidence;
            _image.Text =
                detail.ContainerKey.Length == 0
                    ? row.Image
                    : detail.Image;
            _compose.Text =
                detail.ContainerKey.Length == 0
                    ? row.ComposeOwnership
                    : detail.Compose;
            _lifecycle.Text =
                detail.ContainerKey.Length == 0
                    ? row.State
                    : detail.Lifecycle;
            _resources.Text =
                detail.ContainerKey.Length == 0
                    ? row.Resources
                    : detail.Resources;
            _containerId.Text =
                detail.ContainerKey.Length == 0
                    ? row.ContainerId
                    : detail.ContainerId;
            _restartPolicy.Text =
                detail.ContainerKey.Length == 0
                    ? row.RestartSummary
                    : detail.RestartPolicy;
            _networks.Text =
                detail.ContainerKey.Length == 0
                    ? "--"
                    : detail.Networks;
            _actionStatus.Text =
                detail.ContainerKey.Length == 0
                    ? "Read-only provider."
                    : detail.ActionStatus;
            _ports.Text =
                detail.ContainerKey.Length == 0
                    ? row.Ports
                    : detail.Ports;
            _mounts.Text =
                detail.ContainerKey.Length == 0
                    ? "Mount inspection is unavailable for this provider."
                    : detail.Mounts;
            _environment.Text =
                detail.ContainerKey.Length == 0
                    ? "Environment-name inspection is unavailable for this provider."
                    : detail.EnvironmentNames;
        }

        _start.IsEnabled =
            row?.CanStart == true;
        _stop.IsEnabled =
            row?.CanStop == true;
        _restart.IsEnabled =
            row?.CanRestart == true;
        _restartProject.IsEnabled =
            row?.CanRestartProject == true;
        _refreshDetail.IsEnabled =
            row is not null &&
            _state.CanInspect;

        _portsTab.Content =
            $"Ports ({detail.PortCount})";
        _mountsTab.Content =
            $"Mounts ({detail.MountCount})";
        _environmentTab.Content =
            $"Environment ({detail.EnvironmentCount})";

        RenderDetailTab();
        RenderLogs();
    }

    private void SetDetailTab(
        string tab)
    {
        _detailTab =
            tab;
        RenderDetailTab();
    }

    private void RenderDetailTab()
    {
        _overviewPanel.IsVisible =
            _detailTab ==
            "overview";
        _portsPanel.IsVisible =
            _detailTab ==
            "ports";
        _mountsPanel.IsVisible =
            _detailTab ==
            "mounts";
        _environmentPanel.IsVisible =
            _detailTab ==
            "environment";

        _overviewTab.Classes.Set(
            "selected",
            _overviewPanel.IsVisible);
        _portsTab.Classes.Set(
            "selected",
            _portsPanel.IsVisible);
        _mountsTab.Classes.Set(
            "selected",
            _mountsPanel.IsVisible);
        _environmentTab.Classes.Set(
            "selected",
            _environmentPanel.IsVisible);
    }

    private void RenderLogs()
    {
        var detail =
            _selected is not null &&
            _state.Detail.ContainerKey.Equals(
                _selected.Key,
                StringComparison.OrdinalIgnoreCase)
                ? _state.Detail
                : UnifiedDockerDetail.Empty;

        var source =
            _showRawLogs
                ? detail.RawLogs
                : detail.CleanedLogs;

        var filter =
            _logFilter.Text?.Trim();

        if (!string.IsNullOrWhiteSpace(
                filter))
        {
            var normalized =
                source.Replace(
                    "\r\n",
                    "\n",
                    StringComparison.Ordinal);

            var blocks =
                (_showRawLogs
                    ? normalized.Split(
                        '\n',
                        StringSplitOptions.RemoveEmptyEntries)
                    : normalized.Split(
                        "\n\n",
                        StringSplitOptions.RemoveEmptyEntries))
                .Where(block =>
                    block.Contains(
                        filter,
                        StringComparison.OrdinalIgnoreCase))
                .ToArray();

            source =
                blocks.Length == 0
                    ? $"No log entry contains '{filter}'."
                    : string.Join(
                        _showRawLogs
                            ? Environment.NewLine
                            : Environment.NewLine +
                              Environment.NewLine,
                        blocks);
        }

        _logs.Text =
            source;

        _logs.TextWrapping =
            _showRawLogs
                ? TextWrapping.NoWrap
                : TextWrapping.Wrap;

        _logsStatus.Text =
            detail.ContainerKey.Length == 0
                ? "Not captured"
                : $"{(_showRawLogs ? "Raw" : "Cleaned")} | " +
                  $"{detail.CleanedIncidentCount} cleaned incident(s) from " +
                  $"{detail.RawLineCount} raw line(s) | " +
                  $"{detail.CollapsedLineCount} collapsed";

        _cleanedLogs.Classes.Set(
            "selected",
            !_showRawLogs);
        _rawLogs.Classes.Set(
            "selected",
            _showRawLogs);

        var canCopy =
            detail.ContainerKey.Length > 0;

        _copyCleaned.IsEnabled =
            canCopy;
        _copyRaw.IsEnabled =
            canCopy;
    }

    private void RequestAction(
        UnifiedDockerAction action)
    {
        if (_selected is null)
            return;

        ActionRequested?.Invoke(
            this,
            new UnifiedDockerActionRequestedEventArgs(
                _selected,
                action));
    }

    private static Button SegmentButton(
        string text)
    {
        var button =
            OperationsUi.Compact(
                text);

        button.Classes.Add(
            "segment");

        return button;
    }

    private static TextBlock DetailValue() =>
        new()
        {
            Text = "--",
            FontWeight =
                FontWeight.SemiBold,
            TextWrapping =
                TextWrapping.Wrap
        };

    private static Border DetailCard(
        string label,
        TextBlock value,
        int column)
    {
        var card =
            new Border
            {
                Classes =
                {
                    "flatCard",
                    "compact"
                },
                Child =
                    new StackPanel
                    {
                        Children =
                        {
                            OperationsUi.Eyebrow(
                                label),
                            value
                        }
                    }
            };

        Grid.SetColumn(
            card,
            column);

        return card;
    }

    private static StackPanel DetailBlock(
        string label,
        TextBlock value,
        int column)
    {
        var panel =
            new StackPanel
            {
                Children =
                {
                    OperationsUi.Eyebrow(
                        label),
                    value
                }
            };

        Grid.SetColumn(
            panel,
            column);

        return panel;
    }

    private static bool Matches(
        string filter,
        params string[] values)
    {
        if (string.IsNullOrWhiteSpace(
                filter))
        {
            return true;
        }

        return values.Any(value =>
            value.Contains(
                filter,
                StringComparison.OrdinalIgnoreCase));
    }
}
