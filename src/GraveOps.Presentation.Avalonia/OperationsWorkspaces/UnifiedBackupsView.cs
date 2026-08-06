using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;

namespace GraveOps.Presentation.Avalonia.OperationsWorkspaces;

public sealed class UnifiedBackupsView :
    UserControl
{
    private readonly TextBlock _state;
    private readonly TextBlock _provider;
    private readonly TextBlock _summary;
    private readonly TextBlock _operations;
    private readonly StackPanel _evidence;
    private readonly StackPanel _units;
    private readonly StackPanel _artifacts;
    private readonly TextBlock _capability;

    private UnifiedBackupsState _current =
        UnifiedBackupsState.Empty;

    public UnifiedBackupsView()
    {
        HorizontalAlignment =
            HorizontalAlignment.Stretch;
        VerticalAlignment =
            VerticalAlignment.Stretch;

        _state =
            OperationsUi.MetricValue(
                "WAITING");

        _provider =
            OperationsUi.MetricValue(
                "--");

        _summary =
            new TextBlock
            {
                Text =
                    "Waiting for capture",
                FontSize =
                    12,
                FontWeight =
                    global::Avalonia.Media.FontWeight.SemiBold,
                TextWrapping =
                    global::Avalonia.Media.TextWrapping.Wrap
            };

        _operations =
            OperationsUi.MetricValue(
                "READ ONLY");

        _evidence =
            new StackPanel
            {
                Spacing =
                    5
            };

        _units =
            new StackPanel
            {
                Spacing =
                    3
            };

        _artifacts =
            new StackPanel
            {
                Spacing =
                    3
            };

        _capability =
            OperationsUi.Muted(
                "Backup capability state is loading.");

        Content =
            BuildWorkspace();

        Update(
            UnifiedBackupsState.Empty);
    }

    public event EventHandler?
        RefreshRequested;

    public event EventHandler?
        ServicesRequested;

    public event EventHandler?
        ToolsRequested;

    public void Update(
        UnifiedBackupsState state)
    {
        _current =
            state ?? UnifiedBackupsState.Empty;

        _state.Text =
            _current.State;

        _provider.Text =
            _current.Provider;

        _summary.Text =
            _current.Summary;

        _operations.Text =
            _current.OperationsState;

        _capability.Text =
            _current.CapabilityAvailable
                ? "The active provider reports backup inventory capability."
                : "The active provider does not report backup inventory capability. No schedule or artifact state is inferred.";

        RenderEvidence();
        RenderUnits();
        RenderArtifacts();
    }

    private Control BuildWorkspace()
    {
        var content =
            new StackPanel
            {
                Spacing =
                    8,
                Margin =
                    new Thickness(
                        0,
                        0,
                        4,
                        4)
            };

        var refresh =
            OperationsUi.Compact(
                "Refresh");

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
                        "*,Auto"),
                ColumnSpacing =
                    8
            };

        header.Children.Add(
            new StackPanel
            {
                Children =
                {
                    OperationsUi.Title(
                        "Backups",
                        18),
                    OperationsUi.Subtitle(
                        "Schedule, artifact and restore-readiness evidence from the active provider.")
                }
            });

        Grid.SetColumn(
            refresh,
            1);

        header.Children.Add(
            refresh);

        content.Children.Add(
            header);

        content.Children.Add(
            BuildMetrics());

        content.Children.Add(
            BuildInventoryAndActions());

        content.Children.Add(
            BuildUnitsAndArtifacts());

        content.Children.Add(
            OperationsUi.Module(
                new StackPanel
                {
                    Children =
                    {
                        OperationsUi.Title(
                            "Shareable by design"),
                        OperationsUi.Muted(
                            "No repository path, password, host mount or backup schedule is hard-coded into GraveOps. This page reports only what the active provider can safely observe."),
                        _capability
                    }
                }));

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
                ColumnSpacing =
                    8
            };

        var provider =
            OperationsUi.Metric(
                "PROVIDER",
                _provider);

        var summary =
            OperationsUi.Metric(
                "SUMMARY",
                _summary);

        var operations =
            OperationsUi.Metric(
                "OPERATIONS",
                _operations);

        Grid.SetColumn(
            provider,
            1);
        Grid.SetColumn(
            summary,
            2);
        Grid.SetColumn(
            operations,
            3);

        metrics.Children.Add(
            OperationsUi.Metric(
                "READINESS",
                _state));

        metrics.Children.Add(
            provider);

        metrics.Children.Add(
            summary);

        metrics.Children.Add(
            operations);

        return metrics;
    }

    private Control BuildInventoryAndActions()
    {
        var inventory =
            OperationsUi.Module(
                new Grid
                {
                    RowDefinitions =
                        new RowDefinitions(
                            "Auto,*"),
                    RowSpacing =
                        5,
                    Children =
                    {
                        new StackPanel
                        {
                            Children =
                            {
                                OperationsUi.Title(
                                    "Backup inventory"),
                                OperationsUi.Subtitle(
                                    "Schedule, recovery work and provider evidence reported by the active host.")
                            }
                        },
                        OperationsUi.Scroll(
                            _evidence,
                            125)
                    }
                });

        Grid.SetRow(
            ((Grid)inventory.Child!).Children[1],
            1);

        var services =
            new Button
            {
                Content =
                    "Services & Actions"
            };

        var tools =
            new Button
            {
                Content =
                    "Open terminal"
            };

        services.Click +=
            (_, _) =>
                ServicesRequested?.Invoke(
                    this,
                    EventArgs.Empty);

        tools.Click +=
            (_, _) =>
                ToolsRequested?.Invoke(
                    this,
                    EventArgs.Empty);

        var actions =
            OperationsUi.Module(
                new StackPanel
                {
                    Spacing =
                        6,
                    Children =
                    {
                        OperationsUi.Title(
                            "Operations"),
                        OperationsUi.Subtitle(
                            "Backup mutations belong in protected actions or the provider integration."),
                        services,
                        tools
                    }
                });

        Grid.SetColumn(
            actions,
            1);

        return
            new Grid
            {
                ColumnDefinitions =
                    new ColumnDefinitions(
                        "1.5*,0.5*"),
                ColumnSpacing =
                    8,
                Height =
                    170,
                Children =
                {
                    inventory,
                    actions
                }
            };
    }

    private Control BuildUnitsAndArtifacts()
    {
        var units =
            OperationsUi.Module(
                BuildTable(
                    "Schedules & units",
                    new[]
                    {
                        "UNIT",
                        "ACTIVE",
                        "SUBSTATE",
                        "ENABLED"
                    },
                    new ColumnDefinitions(
                        "170,100,100,*"),
                    _units));

        var artifacts =
            OperationsUi.Module(
                BuildTable(
                    "Recent artifacts",
                    new[]
                    {
                        "PATH",
                        "SIZE",
                        "MODIFIED"
                    },
                    new ColumnDefinitions(
                        "*,110,130"),
                    _artifacts));

        Grid.SetColumn(
            artifacts,
            1);

        return
            new Grid
            {
                ColumnDefinitions =
                    new ColumnDefinitions(
                        "*,*"),
                ColumnSpacing =
                    8,
                Children =
                {
                    units,
                    artifacts
                }
            };
    }

    private static Control BuildTable(
        string title,
        IReadOnlyList<string> headers,
        ColumnDefinitions columns,
        StackPanel rows)
    {
        var header =
            new Grid
            {
                ColumnDefinitions =
                    columns,
                ColumnSpacing =
                    6
            };

        for (var index = 0;
             index < headers.Count;
             index++)
        {
            header.Children.Add(
                OperationsUi.ColumnHeader(
                    headers[index],
                    index));
        }

        var body =
            OperationsUi.Scroll(
                rows,
                260);

        var grid =
            new Grid
            {
                RowDefinitions =
                    new RowDefinitions(
                        "Auto,Auto,*"),
                RowSpacing =
                    5,
                Children =
                {
                    OperationsUi.Title(
                        title),
                    new Border
                    {
                        Classes =
                        {
                            "tableHeaderBar"
                        },
                        Child =
                            header
                    },
                    body
                }
            };

        Grid.SetRow(
            grid.Children[1],
            1);

        Grid.SetRow(
            body,
            2);

        return grid;
    }

    private void RenderEvidence()
    {
        _evidence.Children.Clear();

        var rows =
            _current.Evidence.Count == 0
                ? new[]
                {
                    _current.CapabilityAvailable
                        ? "No backup evidence was returned."
                        : "Backup inventory is unavailable for the active target."
                }
                : _current.Evidence;

        foreach (var item in rows)
        {
            _evidence.Children.Add(
                OperationsUi.Muted(
                    item));
        }
    }

    private void RenderUnits()
    {
        _units.Children.Clear();

        foreach (var row in
                 _current.Units)
        {
            _units.Children.Add(
                new Grid
                {
                    ColumnDefinitions =
                        new ColumnDefinitions(
                            "170,100,100,*"),
                    ColumnSpacing =
                        6,
                    Children =
                    {
                        OperationsUi.Cell(
                            row.Unit,
                            0,
                            true),
                        OperationsUi.Cell(
                            row.Active,
                            1),
                        OperationsUi.Cell(
                            row.SubState,
                            2),
                        OperationsUi.Cell(
                            row.Enabled,
                            3,
                            false,
                            "dim")
                    }
                });
        }

        if (_current.Units.Count == 0)
        {
            _units.Children.Add(
                OperationsUi.Muted(
                    "No backup schedule units were returned."));
        }
    }

    private void RenderArtifacts()
    {
        _artifacts.Children.Clear();

        foreach (var row in
                 _current.Artifacts)
        {
            _artifacts.Children.Add(
                new Grid
                {
                    ColumnDefinitions =
                        new ColumnDefinitions(
                            "*,110,130"),
                    ColumnSpacing =
                        6,
                    Children =
                    {
                        OperationsUi.Cell(
                            row.Path,
                            0,
                            true),
                        OperationsUi.Cell(
                            row.Size,
                            1),
                        OperationsUi.Cell(
                            row.Modified,
                            2,
                            false,
                            "dim")
                    }
                });
        }

        if (_current.Artifacts.Count == 0)
        {
            _artifacts.Children.Add(
                OperationsUi.Muted(
                    "No verified backup artifacts were returned."));
        }
    }
}
