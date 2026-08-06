using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;

namespace GraveOps.Presentation.Avalonia.SystemWorkspaces;

internal static class UnifiedSystemUi
{
    public static TextBlock SectionTitle(
        string text,
        double size = 16) =>
        new()
        {
            Text = text,
            FontSize = size,
            FontWeight = FontWeight.SemiBold,
            Classes =
            {
                "sectionTitle"
            }
        };

    public static TextBlock Subtitle(
        string text) =>
        new()
        {
            Text = text,
            TextWrapping = TextWrapping.Wrap,
            Classes =
            {
                "pageSubtitle"
            }
        };

    public static TextBlock Eyebrow(
        string text) =>
        new()
        {
            Text = text,
            Classes =
            {
                "eyebrow"
            }
        };

    public static TextBlock Muted(
        string text) =>
        new()
        {
            Text = text,
            TextWrapping = TextWrapping.Wrap,
            Classes =
            {
                "muted"
            }
        };

    public static Border Module(
        Control child,
        double padding = 18) =>
        new()
        {
            Classes =
            {
                "module",
                "adaptive"
            },
            Padding = new Thickness(padding),
            Child = child
        };

    public static Border Metric(
        string label,
        TextBlock value) =>
        new()
        {
            Classes =
            {
                "metric"
            },
            Child =
                new StackPanel
                {
                    Children =
                    {
                        Eyebrow(label),
                        value
                    }
                }
        };

    public static TextBlock MetricValue(
        string text = "0") =>
        new()
        {
            Text = text,
            Classes =
            {
                "metricValue"
            }
        };

    public static Button RowButton(
        Control content) =>
        new()
        {
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            Padding = new Thickness(0),
            HorizontalContentAlignment =
                HorizontalAlignment.Stretch,
            Content = content
        };

    public static ScrollViewer Scroll(
        Control content) =>
        new()
        {
            VerticalScrollBarVisibility =
                ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility =
                ScrollBarVisibility.Disabled,
            Content = content
        };
}

public sealed class UnifiedServicesView :
    UserControl
{
    private readonly TextBox _filter;
    private readonly StackPanel _rows;
    private readonly TextBlock _summary;
    private readonly TextBlock _selectedTitle;
    private readonly TextBlock _selectedState;
    private readonly TextBlock _selectedDescription;
    private readonly TextBlock _selectedPolicy;
    private readonly TextBox _detail;
    private readonly TextBlock _actionStatus;
    private readonly Button _start;
    private readonly Button _stop;
    private readonly Button _restart;
    private readonly CheckBox _safeMode;

    private UnifiedServicesState _state =
        UnifiedServicesState.Empty;

    private UnifiedServiceRow? _selected;

    public UnifiedServicesView()
    {
        HorizontalAlignment =
            HorizontalAlignment.Stretch;
        VerticalAlignment =
            VerticalAlignment.Stretch;

        _filter =
            new TextBox
            {
                PlaceholderText =
                    "Filter services",
                MinWidth =
                    210,
                Classes =
                {
                    "filter"
                }
            };

        _filter.TextChanged +=
            (_, _) =>
                RenderRows();

        _rows =
            new StackPanel
            {
                Spacing =
                    4
            };

        _summary =
            new TextBlock
            {
                Text =
                    "0 services",
                Classes =
                {
                    "dim"
                }
            };

        _selectedTitle =
            UnifiedSystemUi.SectionTitle(
                "Select an action");

        _selectedState =
            new TextBlock
            {
                Text =
                    "--",
                Foreground =
                    Application.Current?.Resources[
                        "AccentBrush"] as IBrush
            };

        _selectedDescription =
            UnifiedSystemUi.Subtitle(
                "Select a service to review state, policy and guarded controls.");

        _selectedPolicy =
            UnifiedSystemUi.Muted(
                "No service selected.");

        _detail =
            new TextBox
            {
                IsReadOnly =
                    true,
                AcceptsReturn =
                    true,
                TextWrapping =
                    TextWrapping.Wrap,
                Text =
                    "Select a service from the action library to review its state and safe controls.",
                Classes =
                {
                    "console",
                    "workspaceOutput"
                }
            };

        _actionStatus =
            new TextBlock
            {
                Text =
                    "No action run.",
                HorizontalAlignment =
                    HorizontalAlignment.Right,
                Classes =
                {
                    "dim"
                }
            };

        _start =
            new Button
            {
                Content =
                    "Start"
            };

        _stop =
            new Button
            {
                Content =
                    "Stop",
                Classes =
                {
                    "danger"
                }
            };

        _restart =
            new Button
            {
                Content =
                    "Restart",
                Classes =
                {
                    "primary"
                }
            };

        _safeMode =
            new CheckBox
            {
                Content =
                    "Safe Mode",
                VerticalAlignment =
                    VerticalAlignment.Center
            };

        _start.Click +=
            (_, _) =>
                RequestAction(
                    "start");

        _stop.Click +=
            (_, _) =>
                RequestAction(
                    "stop");

        _restart.Click +=
            (_, _) =>
                RequestAction(
                    "restart");

        _safeMode.Click +=
            (_, _) =>
                SafeModeRequested?.Invoke(
                    this,
                    new UnifiedSafeModeRequestedEventArgs(
                        _safeMode.IsChecked == true));

        Content =
            BuildWorkspace();

        Update(
            UnifiedServicesState.Empty);
    }

    public event EventHandler?
        RefreshRequested;

    public event EventHandler<UnifiedServiceActionRequestedEventArgs>?
        ServiceActionRequested;

    public event EventHandler<UnifiedSafeModeRequestedEventArgs>?
        SafeModeRequested;

    public void Update(
        UnifiedServicesState state)
    {
        _state =
            state ?? UnifiedServicesState.Empty;

        _actionStatus.Text =
            _state.ActionStatus;

        _safeMode.IsEnabled =
            _state.CanToggleSafeMode;

        _safeMode.IsChecked =
            _state.SafeModeEnabled;

        RenderRows();
    }

    public void SetActionStatus(
        string status)
    {
        _actionStatus.Text =
            status;
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

        var heading =
            new StackPanel
            {
                Children =
                {
                    UnifiedSystemUi.SectionTitle(
                        "Operations & troubleshooting",
                        18),
                    UnifiedSystemUi.Subtitle(
                        "Native services, guarded actions and selected-state comparison.")
                }
            };

        var refresh =
            new Button
            {
                Content =
                    "Refresh",
                Classes =
                {
                    "compact"
                }
            };

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
                        "*,210,Auto,Auto,Auto,Auto,Auto"),
                ColumnSpacing =
                    6
            };

        Grid.SetColumn(
            _filter,
            1);
        Grid.SetColumn(
            _start,
            2);
        Grid.SetColumn(
            _stop,
            3);
        Grid.SetColumn(
            _restart,
            4);
        Grid.SetColumn(
            _safeMode,
            5);
        Grid.SetColumn(
            refresh,
            6);

        header.Children.Add(
            heading);
        header.Children.Add(
            _filter);
        header.Children.Add(
            _start);
        header.Children.Add(
            _stop);
        header.Children.Add(
            _restart);
        header.Children.Add(
            _safeMode);
        header.Children.Add(
            refresh);

        content.Children.Add(
            header);

        var library =
            new Grid
            {
                RowDefinitions =
                    new RowDefinitions(
                        "Auto,Auto,*"),
                RowSpacing =
                    6
            };

        library.Children.Add(
            new StackPanel
            {
                Children =
                {
                    UnifiedSystemUi.SectionTitle(
                        "Action library"),
                    _summary
                }
            });

        var hostSummary =
            new Border
            {
                Classes =
                {
                    "inset"
                },
                Child =
                    new StackPanel
                    {
                        Children =
                        {
                            UnifiedSystemUi.Eyebrow(
                                "HOST SUMMARY"),
                            UnifiedSystemUi.Muted(
                                "Read-only inventory and health-aware service actions.")
                        }
                    }
            };

        Grid.SetRow(
            hostSummary,
            1);

        var rowsScroller =
            UnifiedSystemUi.Scroll(
                _rows);

        Grid.SetRow(
            rowsScroller,
            2);

        library.Children.Add(
            hostSummary);
        library.Children.Add(
            rowsScroller);

        var selectedHeader =
            new Grid
            {
                ColumnDefinitions =
                    new ColumnDefinitions(
                        "*,Auto")
            };

        selectedHeader.Children.Add(
            new StackPanel
            {
                Children =
                {
                    _selectedTitle,
                    _selectedDescription
                }
            });

        Grid.SetColumn(
            _selectedState,
            1);

        selectedHeader.Children.Add(
            _selectedState);

        var statusRow =
            new Grid
            {
                ColumnDefinitions =
                    new ColumnDefinitions(
                        "Auto,*")
            };

        statusRow.Children.Add(
            UnifiedSystemUi.Eyebrow(
                "RESULT / BEFORE & AFTER"));

        Grid.SetColumn(
            _actionStatus,
            1);

        statusRow.Children.Add(
            _actionStatus);

        var detail =
            new Grid
            {
                RowDefinitions =
                    new RowDefinitions(
                        "Auto,Auto,Auto,*"),
                RowSpacing =
                    7
            };

        detail.Children.Add(
            selectedHeader);

        var policy =
            new Border
            {
                Classes =
                {
                    "inset"
                },
                Child =
                    _selectedPolicy
            };

        Grid.SetRow(
            policy,
            1);

        Grid.SetRow(
            statusRow,
            2);

        Grid.SetRow(
            _detail,
            3);

        detail.Children.Add(
            policy);
        detail.Children.Add(
            statusRow);
        detail.Children.Add(
            _detail);

        var workspace =
            new Grid
            {
                ColumnDefinitions =
                    new ColumnDefinitions(
                        "260,*"),
                ColumnSpacing =
                    8,
                MinHeight =
                    440
            };

        workspace.Children.Add(
            UnifiedSystemUi.Module(
                library));

        var detailModule =
            UnifiedSystemUi.Module(
                detail);

        Grid.SetColumn(
            detailModule,
            1);

        workspace.Children.Add(
            detailModule);

        content.Children.Add(
            workspace);

        return
            UnifiedSystemUi.Scroll(
                content);
    }

    private void RenderRows()
    {
        var filter =
            _filter.Text?.Trim() ??
            string.Empty;

        var rows =
            _state.Rows
                .Where(row =>
                    Matches(
                        filter,
                        row.Unit,
                        row.Description,
                        row.ActiveState,
                        row.SubState,
                        row.Policy))
                .ToArray();

        _rows.Children.Clear();

        foreach (var row in rows)
        {
            var state =
                new TextBlock
                {
                    Text =
                        $"{row.ActiveState}/{row.SubState}",
                    FontWeight =
                        FontWeight.SemiBold
                };

            Grid.SetColumn(
                state,
                1);

            var grid =
                new Grid
                {
                    ColumnDefinitions =
                        new ColumnDefinitions(
                            "*,Auto"),
                    ColumnSpacing =
                        8,
                    Children =
                    {
                        new StackPanel
                        {
                            Spacing =
                                2,
                            Children =
                            {
                                new TextBlock
                                {
                                    Text =
                                        row.Unit,
                                    FontWeight =
                                        FontWeight.SemiBold,
                                    TextTrimming =
                                        TextTrimming.CharacterEllipsis
                                },
                                new TextBlock
                                {
                                    Text =
                                        row.Description,
                                    FontSize =
                                        8.5,
                                    TextTrimming =
                                        TextTrimming.CharacterEllipsis,
                                    Classes =
                                    {
                                        "dim"
                                    }
                                }
                            }
                        },
                        state
                    }
                };

            var button =
                UnifiedSystemUi.RowButton(
                    grid);

            button.Click +=
                (_, _) =>
                {
                    _selected =
                        row;
                    RenderSelection();
                };

            _rows.Children.Add(
                button);
        }

        _summary.Text =
            $"{rows.Length} shown · {_state.Rows.Count} captured";

        if (_selected is null ||
            !rows.Any(row =>
                row.Unit.Equals(
                    _selected.Unit,
                    StringComparison.OrdinalIgnoreCase)))
        {
            _selected =
                rows.FirstOrDefault();
        }

        RenderSelection();
    }

    private void RenderSelection()
    {
        if (_selected is null)
        {
            _selectedTitle.Text =
                "No service selected";
            _selectedState.Text =
                "--";
            _selectedDescription.Text =
                "Select a service to inspect its current state and unit-file policy.";
            _selectedPolicy.Text =
                "--";
            _detail.Text =
                _state.Rows.Count == 0
                    ? _state.Status
                    : "Select a service from the action library.";
            _start.IsEnabled =
                false;
            _stop.IsEnabled =
                false;
            _restart.IsEnabled =
                false;
            return;
        }

        _selectedTitle.Text =
            _selected.Unit;
        _selectedState.Text =
            $"{_selected.ActiveState}/{_selected.SubState}";
        _selectedDescription.Text =
            _selected.Description;
        _selectedPolicy.Text =
            _selected.Policy;
        _detail.Text =
            _selected.Evidence;
        _start.IsEnabled =
            _selected.CanStart;
        _stop.IsEnabled =
            _selected.CanStop;
        _restart.IsEnabled =
            _selected.CanRestart;
    }

    private void RequestAction(
        string action)
    {
        if (_selected is null)
            return;

        ServiceActionRequested?.Invoke(
            this,
            new UnifiedServiceActionRequestedEventArgs(
                _selected,
                action));
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

public sealed class UnifiedStorageView :
    UserControl
{
    private readonly TextBox _filter;
    private readonly WrapPanel _rows;
    private readonly TextBlock _rootsMetric;
    private readonly TextBlock _attentionMetric;
    private readonly TextBlock _hottestMetric;
    private readonly TextBlock _policyMetric;
    private readonly TextBlock _policyStatus;
    private readonly TextBlock _capacityStatus;
    private readonly Button _capacityPolicy;
    private readonly Button _thresholds;
    private readonly Button _restoreDefaults;

    private UnifiedStorageState _state =
        UnifiedStorageState.Empty;

    private UnifiedStorageRow? _selected;

    public UnifiedStorageView()
    {
        HorizontalAlignment =
            HorizontalAlignment.Stretch;
        VerticalAlignment =
            VerticalAlignment.Stretch;

        _filter =
            new TextBox
            {
                PlaceholderText =
                    "Filter mounts",
                MinWidth =
                    220,
                Classes =
                {
                    "filter"
                }
            };

        _filter.TextChanged +=
            (_, _) =>
                RenderRows();

        _rows =
            new WrapPanel
            {
                Orientation =
                    Orientation.Horizontal
            };

        _rootsMetric =
            UnifiedSystemUi.MetricValue(
                "0 mounts");

        _attentionMetric =
            UnifiedSystemUi.MetricValue(
                "0");

        _hottestMetric =
            UnifiedSystemUi.MetricValue(
                "ON DEMAND");

        _policyMetric =
            UnifiedSystemUi.MetricValue(
                "Select a mount");

        _policyStatus =
            UnifiedSystemUi.Muted(
                "Select a mount to inspect its threshold policy.");

        _capacityStatus =
            new TextBlock
            {
                Text =
                    "Capacity policy is loading.",
                Margin =
                    new Thickness(
                        0,
                        5,
                        0,
                        0),
                TextWrapping =
                    TextWrapping.Wrap,
                Classes =
                {
                    "dim"
                }
            };

        _capacityPolicy =
            new Button
            {
                Content =
                    "Capacity alerts..."
            };

        _thresholds =
            new Button
            {
                Content =
                    "Thresholds..."
            };

        _restoreDefaults =
            new Button
            {
                Content =
                    "Restore defaults"
            };

        _capacityPolicy.Click +=
            (_, _) =>
                RequestAction(
                    UnifiedStorageAction.CapacityPolicy);

        _thresholds.Click +=
            (_, _) =>
                RequestAction(
                    UnifiedStorageAction.Thresholds);

        _restoreDefaults.Click +=
            (_, _) =>
                RequestAction(
                    UnifiedStorageAction.RestoreDefaults);

        Content =
            BuildWorkspace();

        Update(
            UnifiedStorageState.Empty);
    }

    public event EventHandler?
        RefreshRequested;

    public event EventHandler<UnifiedStorageActionRequestedEventArgs>?
        StorageActionRequested;

    public void Update(
        UnifiedStorageState state)
    {
        _state =
            state ?? UnifiedStorageState.Empty;

        _policyStatus.Text =
            _state.PolicyStatus;
        _capacityStatus.Text =
            _state.CapacityStatus;

        RenderRows();
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

        var heading =
            new StackPanel
            {
                Children =
                {
                    UnifiedSystemUi.SectionTitle(
                        "Storage & capacity",
                        18),
                    UnifiedSystemUi.Subtitle(
                        "Mount identity, free space, capacity policy and dependency ownership.")
                }
            };

        var refresh =
            new Button
            {
                Content =
                    "Refresh",
                Classes =
                {
                    "compact"
                }
            };

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
                        "*,220,Auto,Auto,Auto,Auto"),
                ColumnSpacing =
                    6
            };

        Grid.SetColumn(
            _filter,
            1);
        Grid.SetColumn(
            _capacityPolicy,
            2);
        Grid.SetColumn(
            _thresholds,
            3);
        Grid.SetColumn(
            _restoreDefaults,
            4);
        Grid.SetColumn(
            refresh,
            5);

        header.Children.Add(
            heading);
        header.Children.Add(
            _filter);
        header.Children.Add(
            _capacityPolicy);
        header.Children.Add(
            _thresholds);
        header.Children.Add(
            _restoreDefaults);
        header.Children.Add(
            refresh);

        content.Children.Add(
            header);

        var metrics =
            new Grid
            {
                ColumnDefinitions =
                    new ColumnDefinitions(
                        "*,*,*,*"),
                ColumnSpacing =
                    8
            };

        var attention =
            UnifiedSystemUi.Metric(
                "CAPACITY ATTENTION",
                _attentionMetric);

        var hottest =
            UnifiedSystemUi.Metric(
                "HOTTEST DRIVE",
                _hottestMetric);

        var policy =
            UnifiedSystemUi.Metric(
                "POLICY",
                _policyMetric);

        Grid.SetColumn(
            attention,
            1);
        Grid.SetColumn(
            hottest,
            2);
        Grid.SetColumn(
            policy,
            3);

        metrics.Children.Add(
            UnifiedSystemUi.Metric(
                "STORAGE ROOTS",
                _rootsMetric));
        metrics.Children.Add(
            attention);
        metrics.Children.Add(
            hottest);
        metrics.Children.Add(
            policy);

        content.Children.Add(
            metrics);

        content.Children.Add(
            UnifiedSystemUi.Module(
                _rows,
                8));

        var capacity =
            UnifiedSystemUi.Module(
                new StackPanel
                {
                    Children =
                    {
                        UnifiedSystemUi.SectionTitle(
                            "Capacity policy"),
                        _policyStatus,
                        _capacityStatus
                    }
                });

        var dependency =
            UnifiedSystemUi.Module(
                new StackPanel
                {
                    Children =
                    {
                        UnifiedSystemUi.SectionTitle(
                            "Dependency map"),
                        UnifiedSystemUi.Muted(
                            "Host -> storage roots\nStorage -> Docker / container data\nStorage -> media applications\nApplications -> workflow / library availability")
                    }
                });

        Grid.SetColumn(
            dependency,
            1);

        var lower =
            new Grid
            {
                ColumnDefinitions =
                    new ColumnDefinitions(
                        "1.3*,0.7*"),
                ColumnSpacing =
                    8,
                Children =
                {
                    capacity,
                    dependency
                }
            };

        content.Children.Add(
            lower);

        return
            UnifiedSystemUi.Scroll(
                content);
    }

    private void RenderRows()
    {
        var filter =
            _filter.Text?.Trim() ??
            string.Empty;

        var rows =
            _state.Rows
                .Where(row =>
                    Matches(
                        filter,
                        row.Source,
                        row.MountPoint,
                        row.FileSystem,
                        row.PercentUsed,
                        row.PolicyLabel,
                        row.StatusLabel))
                .ToArray();

        _rows.Children.Clear();

        foreach (var row in rows)
        {
            var status =
                new TextBlock
                {
                    Text =
                        row.StatusLabel,
                    FontWeight =
                        FontWeight.SemiBold
                };

            Grid.SetColumn(
                status,
                1);

            var heading =
                new Grid
                {
                    ColumnDefinitions =
                        new ColumnDefinitions(
                            "*,Auto"),
                    Children =
                    {
                        new StackPanel
                        {
                            Children =
                            {
                                new TextBlock
                                {
                                    Text =
                                        row.Source,
                                    FontSize =
                                        14,
                                    FontWeight =
                                        FontWeight.SemiBold
                                },
                                new TextBlock
                                {
                                    Text =
                                        row.MountPoint,
                                    FontSize =
                                        8.5,
                                    Classes =
                                    {
                                        "dim"
                                    }
                                }
                            }
                        },
                        status
                    }
                };

            var percent =
                new TextBlock
                {
                    Text =
                        row.PercentUsed,
                    FontSize =
                        16,
                    FontWeight =
                        FontWeight.SemiBold
                };

            Grid.SetColumn(
                percent,
                1);

            var capacity =
                new Grid
                {
                    ColumnDefinitions =
                        new ColumnDefinitions(
                            "*,Auto"),
                    Children =
                    {
                        UnifiedSystemUi.Eyebrow(
                            "CAPACITY"),
                        percent
                    }
                };

            var detail =
                new Grid
                {
                    ColumnDefinitions =
                        new ColumnDefinitions(
                            "*,*,*"),
                    RowDefinitions =
                        new RowDefinitions(
                            "Auto,Auto"),
                    ColumnSpacing =
                        6
                };

            AddStorageDetail(
                detail,
                0,
                0,
                "USED",
                row.Used);
            AddStorageDetail(
                detail,
                0,
                1,
                "FREE",
                row.Available);
            AddStorageDetail(
                detail,
                0,
                2,
                "FILESYSTEM",
                row.FileSystem);
            AddStorageDetail(
                detail,
                1,
                0,
                "POLICY",
                row.PolicyLabel);

            var size =
                new StackPanel
                {
                    Children =
                    {
                        UnifiedSystemUi.Eyebrow(
                            "SIZE"),
                        new TextBlock
                        {
                            Text =
                                row.Size
                        }
                    }
                };

            Grid.SetRow(
                size,
                1);
            Grid.SetColumn(
                size,
                1);
            Grid.SetColumnSpan(
                size,
                2);

            detail.Children.Add(
                size);

            var card =
                new Border
                {
                    Classes =
                    {
                        "module"
                    },
                    Width =
                        345,
                    Height =
                        155,
                    Margin =
                        new Thickness(
                            0,
                            0,
                            7,
                            7),
                    Child =
                        new Grid
                        {
                            RowDefinitions =
                                new RowDefinitions(
                                    "Auto,Auto,Auto,*"),
                            RowSpacing =
                                5,
                            Children =
                            {
                                heading,
                                capacity,
                                new Border
                                {
                                    Height =
                                        5,
                                    Background =
                                        Application.Current?.Resources[
                                            "Surface3Brush"] as IBrush,
                                    CornerRadius =
                                        new CornerRadius(
                                            3)
                                },
                                detail
                            }
                        }
                };

            Grid.SetRow(
                capacity,
                1);
            var cardGrid =
                (Grid)card.Child!;

            Grid.SetRow(
                cardGrid.Children[2],
                2);
            Grid.SetRow(
                detail,
                3);

            var button =
                UnifiedSystemUi.RowButton(
                    card);

            button.Margin =
                new Thickness(
                    0);

            button.Click +=
                (_, _) =>
                {
                    _selected =
                        row;
                    RenderSelection();
                };

            _rows.Children.Add(
                button);
        }

        _rootsMetric.Text =
            $"{rows.Length} mounts";
        _attentionMetric.Text =
            rows.Count(row =>
                    row.PercentValue >= 85)
                .ToString();

        var hottest =
            rows
                .OrderByDescending(row =>
                    row.PercentValue)
                .FirstOrDefault();

        _hottestMetric.Text =
            hottest is null
                ? "ON DEMAND"
                : $"{hottest.MountPoint} · {hottest.PercentUsed}";

        if (_selected is null ||
            !rows.Any(row =>
                row.MountPoint.Equals(
                    _selected.MountPoint,
                    StringComparison.OrdinalIgnoreCase)))
        {
            _selected =
                rows.FirstOrDefault();
        }

        RenderSelection();
    }

    private void RenderSelection()
    {
        if (_selected is null)
        {
            _policyMetric.Text =
                "Select a mount";
            _capacityPolicy.IsEnabled =
                false;
            _thresholds.IsEnabled =
                false;
            _restoreDefaults.IsEnabled =
                false;
            _policyStatus.Text =
                _state.Rows.Count == 0
                    ? _state.Status
                    : _state.PolicyStatus;
            return;
        }

        _policyMetric.Text =
            $"{_selected.MountPoint} · {_selected.PolicyLabel}";
        _capacityPolicy.IsEnabled =
            _selected.CanConfigureCapacity;
        _thresholds.IsEnabled =
            _selected.CanConfigureThreshold;
        _restoreDefaults.IsEnabled =
            _selected.CanRestoreDefaults;
        _policyStatus.Text =
            $"{_selected.MountPoint} · {_selected.StatusLabel} · {_selected.PolicyLabel}";
    }

    private void RequestAction(
        UnifiedStorageAction action)
    {
        if (_selected is null)
            return;

        StorageActionRequested?.Invoke(
            this,
            new UnifiedStorageActionRequestedEventArgs(
                _selected,
                action));
    }

    private static void AddStorageDetail(
        Grid grid,
        int row,
        int column,
        string label,
        string value)
    {
        var panel =
            new StackPanel
            {
                Children =
                {
                    UnifiedSystemUi.Eyebrow(
                        label),
                    new TextBlock
                    {
                        Text =
                            value
                    }
                }
            };

        Grid.SetRow(
            panel,
            row);
        Grid.SetColumn(
            panel,
            column);

        grid.Children.Add(
            panel);
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

public sealed class UnifiedLogsView :
    UserControl
{
    private readonly TextBlock _activeMetric;
    private readonly TextBlock _backgroundMetric;
    private readonly TextBlock _visibleMetric;
    private readonly TextBlock _sourceMetric;
    private readonly ComboBox _severity;
    private readonly TextBox _sourceFilter;
    private readonly TextBox _messageFilter;
    private readonly CheckBox _includeInformational;
    private readonly TextBlock _filterStatus;
    private readonly StackPanel _rows;
    private readonly Border _emptyState;
    private readonly TextBlock _emptyTitle;
    private readonly TextBlock _emptyDetail;
    private readonly TextBlock _selectedTitle;
    private readonly TextBox _detail;
    private readonly Button _copy;
    private readonly Button _intelligence;
    private readonly TextBlock _summary;

    private UnifiedLogsState _state =
        UnifiedLogsState.Empty;

    private UnifiedLogRow? _selected;

    public UnifiedLogsView()
    {
        HorizontalAlignment =
            HorizontalAlignment.Stretch;
        VerticalAlignment =
            VerticalAlignment.Stretch;

        _activeMetric =
            UnifiedSystemUi.MetricValue();
        _backgroundMetric =
            UnifiedSystemUi.MetricValue();
        _visibleMetric =
            UnifiedSystemUi.MetricValue();
        _sourceMetric =
            UnifiedSystemUi.MetricValue();

        _severity =
            new ComboBox
            {
                Width =
                    150,
                ItemsSource =
                    new[]
                    {
                        "Warnings & errors",
                        "Errors only",
                        "All severities"
                    },
                SelectedIndex =
                    0,
                Margin =
                    new Thickness(
                        0,
                        0,
                        7,
                        7)
            };

        _sourceFilter =
            new TextBox
            {
                Width =
                    175,
                PlaceholderText =
                    "Filter source",
                Margin =
                    new Thickness(
                        0,
                        0,
                        7,
                        7),
                Classes =
                {
                    "filter"
                }
            };

        _messageFilter =
            new TextBox
            {
                Width =
                    240,
                PlaceholderText =
                    "Filter message",
                Margin =
                    new Thickness(
                        0,
                        0,
                        7,
                        7),
                Classes =
                {
                    "filter"
                }
            };

        _includeInformational =
            new CheckBox
            {
                Content =
                    "Include informational",
                VerticalAlignment =
                    VerticalAlignment.Center,
                Margin =
                    new Thickness(
                        0,
                        0,
                        10,
                        7)
            };

        _severity.SelectionChanged +=
            (_, _) =>
                RenderRows();

        _sourceFilter.TextChanged +=
            (_, _) =>
                RenderRows();

        _messageFilter.TextChanged +=
            (_, _) =>
                RenderRows();

        _includeInformational.Click +=
            (_, _) =>
                RenderRows();

        _filterStatus =
            new TextBlock
            {
                Text =
                    "Log filters are ready.",
                TextWrapping =
                    TextWrapping.Wrap,
                Classes =
                {
                    "dim"
                }
            };

        _rows =
            new StackPanel
            {
                Spacing =
                    2
            };

        _emptyTitle =
            new TextBlock
            {
                Text =
                    "No visible log evidence",
                FontWeight =
                    FontWeight.SemiBold
            };

        _emptyDetail =
            UnifiedSystemUi.Muted(
                "No log group matches the current filters.");

        _emptyState =
            new Border
            {
                Classes =
                {
                    "emptyState"
                },
                IsVisible =
                    false,
                VerticalAlignment =
                    VerticalAlignment.Top,
                Child =
                    new StackPanel
                    {
                        Children =
                        {
                            _emptyTitle,
                            _emptyDetail
                        }
                    }
            };

        _selectedTitle =
            UnifiedSystemUi.Subtitle(
                "No log group selected");

        _detail =
            new TextBox
            {
                MinHeight =
                    120,
                IsReadOnly =
                    true,
                AcceptsReturn =
                    true,
                TextWrapping =
                    TextWrapping.Wrap,
                Classes =
                {
                    "console",
                    "workspaceOutput"
                }
            };

        _copy =
            new Button
            {
                Content =
                    "Copy detail",
                IsEnabled =
                    false,
                Classes =
                {
                    "compact"
                }
            };

        _intelligence =
            new Button
            {
                Content =
                    "Intelligence",
                IsEnabled =
                    false,
                Classes =
                {
                    "compact"
                }
            };

        _copy.Click +=
            (_, _) =>
                RequestAction(
                    UnifiedLogAction.CopyDetail);

        _intelligence.Click +=
            (_, _) =>
                RequestAction(
                    UnifiedLogAction.OpenIntelligence);

        _summary =
            UnifiedSystemUi.Muted(
                "0 shown · 0 warning/error · 0 informational");

        Content =
            BuildWorkspace();

        Update(
            UnifiedLogsState.Empty);
    }

    public event EventHandler?
        RefreshRequested;

    public event EventHandler<UnifiedLogActionRequestedEventArgs>?
        LogActionRequested;

    public void Update(
        UnifiedLogsState state)
    {
        _state =
            state ?? UnifiedLogsState.Empty;

        RenderRows();
    }

    private Control BuildWorkspace()
    {
        var content =
            new StackPanel
            {
                Spacing =
                    10,
                Margin =
                    new Thickness(
                        0,
                        0,
                        4,
                        4)
            };

        var refresh =
            new Button
            {
                Content =
                    "Refresh",
                Classes =
                {
                    "compact"
                }
            };

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
                        "*,Auto")
            };

        header.Children.Add(
            new StackPanel
            {
                Children =
                {
                    UnifiedSystemUi.SectionTitle(
                        "Log Center",
                        18),
                    UnifiedSystemUi.Subtitle(
                        "Grouped host evidence with explicit filters, empty-state reasons and selected-event detail.")
                }
            });

        Grid.SetColumn(
            refresh,
            1);

        header.Children.Add(
            refresh);

        content.Children.Add(
            header);

        var metrics =
            new Grid
            {
                ColumnDefinitions =
                    new ColumnDefinitions(
                        "*,*,*,*"),
                ColumnSpacing =
                    8
            };

        var background =
            UnifiedSystemUi.Metric(
                "INFORMATIONAL",
                _backgroundMetric);

        var visible =
            UnifiedSystemUi.Metric(
                "VISIBLE",
                _visibleMetric);

        var sources =
            UnifiedSystemUi.Metric(
                "VISIBLE SOURCES",
                _sourceMetric);

        Grid.SetColumn(
            background,
            1);
        Grid.SetColumn(
            visible,
            2);
        Grid.SetColumn(
            sources,
            3);

        metrics.Children.Add(
            UnifiedSystemUi.Metric(
                "WARNING / ERROR",
                _activeMetric));
        metrics.Children.Add(
            background);
        metrics.Children.Add(
            visible);
        metrics.Children.Add(
            sources);

        content.Children.Add(
            metrics);

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
            {
                _severity.SelectedIndex =
                    0;
                _sourceFilter.Text =
                    string.Empty;
                _messageFilter.Text =
                    string.Empty;
                _includeInformational.IsChecked =
                    false;
                RenderRows();
            };

        var filters =
            new WrapPanel
            {
                Children =
                {
                    _severity,
                    _sourceFilter,
                    _messageFilter,
                    _includeInformational,
                    reset
                }
            };

        content.Children.Add(
            UnifiedSystemUi.Module(
                new StackPanel
                {
                    Spacing =
                        8,
                    Children =
                    {
                        filters,
                        _filterStatus
                    }
                },
                10));

        var headerBar =
            new Border
            {
                Classes =
                {
                    "tableHeaderBar"
                },
                Child =
                    new Grid
                    {
                        ColumnDefinitions =
                            new ColumnDefinitions(
                                "95,150,135,70,*"),
                        ColumnSpacing =
                            8,
                        Children =
                        {
                            Header(
                                "SEVERITY",
                                0),
                            Header(
                                "SOURCE",
                                1),
                            Header(
                                "LAST SEEN",
                                2),
                            Header(
                                "COUNT",
                                3),
                            Header(
                                "MESSAGE",
                                4)
                        }
                    }
            };

        var tableBody =
            new Grid
            {
                Children =
                {
                    UnifiedSystemUi.Scroll(
                        _rows),
                    _emptyState
                }
            };

        var table =
            new Grid
            {
                RowDefinitions =
                    new RowDefinitions(
                        "Auto,*"),
                RowSpacing =
                    6,
                MinHeight =
                    270,
                MaxHeight =
                    430,
                Children =
                {
                    headerBar,
                    tableBody
                }
            };

        Grid.SetRow(
            tableBody,
            1);

        content.Children.Add(
            UnifiedSystemUi.Module(
                table));

        var detailHeader =
            new Grid
            {
                ColumnDefinitions =
                    new ColumnDefinitions(
                        "*,Auto,Auto")
            };

        detailHeader.Children.Add(
            new StackPanel
            {
                Children =
                {
                    UnifiedSystemUi.SectionTitle(
                        "Selected event detail"),
                    _selectedTitle
                }
            });

        Grid.SetColumn(
            _copy,
            1);
        Grid.SetColumn(
            _intelligence,
            2);

        _copy.Margin =
            new Thickness(
                0,
                0,
                6,
                0);

        detailHeader.Children.Add(
            _copy);
        detailHeader.Children.Add(
            _intelligence);

        var detail =
            new Grid
            {
                RowDefinitions =
                    new RowDefinitions(
                        "Auto,*"),
                RowSpacing =
                    6,
                MinHeight =
                    180,
                MaxHeight =
                    270,
                Children =
                {
                    detailHeader,
                    _detail
                }
            };

        Grid.SetRow(
            _detail,
            1);

        content.Children.Add(
            UnifiedSystemUi.Module(
                detail));

        content.Children.Add(
            new Border
            {
                Classes =
                {
                    "arrStatusBar"
                },
                Child =
                    _summary
            });

        return
            UnifiedSystemUi.Scroll(
                content);
    }

    private void RenderRows()
    {
        var sourceFilter =
            _sourceFilter.Text?.Trim() ??
            string.Empty;
        var messageFilter =
            _messageFilter.Text?.Trim() ??
            string.Empty;
        var severityFilter =
            _severity.SelectedItem?.ToString() ??
            "Warnings & errors";
        var includeInformation =
            _includeInformational.IsChecked ==
            true;

        var minimum =
            severityFilter.Equals(
                "Errors only",
                StringComparison.Ordinal)
                ? UnifiedLogSeverity.Error
                : severityFilter.Equals(
                    "All severities",
                    StringComparison.Ordinal) ||
                  includeInformation
                    ? UnifiedLogSeverity.Information
                    : UnifiedLogSeverity.Warning;

        var rows =
            _state.Rows
                .Where(row =>
                    row.Severity >= minimum)
                .Where(row =>
                    string.IsNullOrWhiteSpace(
                        sourceFilter) ||
                    row.Source.Contains(
                        sourceFilter,
                        StringComparison.OrdinalIgnoreCase))
                .Where(row =>
                    string.IsNullOrWhiteSpace(
                        messageFilter) ||
                    row.Message.Contains(
                        messageFilter,
                        StringComparison.OrdinalIgnoreCase) ||
                    row.Detail.Contains(
                        messageFilter,
                        StringComparison.OrdinalIgnoreCase))
                .ToArray();

        _rows.Children.Clear();

        foreach (var row in rows)
        {
            var grid =
                new Grid
                {
                    ColumnDefinitions =
                        new ColumnDefinitions(
                            "95,150,135,70,*"),
                    ColumnSpacing =
                        8,
                    Children =
                    {
                        Cell(
                            row.SeverityLabel,
                            0,
                            true),
                        Cell(
                            row.Source,
                            1,
                            true),
                        Cell(
                            row.DisplayTime,
                            2),
                        Cell(
                            row.Count.ToString(),
                            3),
                        Cell(
                            row.Message,
                            4)
                    }
                };

            var button =
                UnifiedSystemUi.RowButton(
                    grid);

            button.Click +=
                (_, _) =>
                {
                    _selected =
                        row;
                    RenderSelection();
                };

            _rows.Children.Add(
                button);
        }

        var active =
            _state.Rows.Count(row =>
                row.Severity >=
                UnifiedLogSeverity.Warning);

        var background =
            _state.Rows.Count(row =>
                row.Severity ==
                UnifiedLogSeverity.Information);

        _activeMetric.Text =
            active.ToString();
        _backgroundMetric.Text =
            background.ToString();
        _visibleMetric.Text =
            rows.Length.ToString();
        _sourceMetric.Text =
            rows
                .Select(row =>
                    row.Source)
                .Distinct(
                    StringComparer.OrdinalIgnoreCase)
                .Count()
                .ToString();

        _summary.Text =
            $"{rows.Length} shown · {active} warning/error · " +
            $"{background} informational";

        _filterStatus.Text =
            rows.Length == 0
                ? _state.EmptyDetail
                : _summary.Text;

        _emptyState.IsVisible =
            rows.Length == 0;
        _emptyTitle.Text =
            _state.EmptyTitle;
        _emptyDetail.Text =
            _state.EmptyDetail;

        if (_selected is null ||
            !rows.Any(row =>
                row.Key.Equals(
                    _selected.Key,
                    StringComparison.Ordinal)))
        {
            _selected =
                rows.FirstOrDefault();
        }

        RenderSelection();
    }

    private void RenderSelection()
    {
        if (_selected is null)
        {
            _selectedTitle.Text =
                "No log group selected";
            _detail.Text =
                _state.Rows.Count == 0
                    ? _state.EmptyDetail
                    : "Select a log group to inspect its evidence.";
            _copy.IsEnabled =
                false;
            _intelligence.IsEnabled =
                false;
            return;
        }

        _selectedTitle.Text =
            $"{_selected.SeverityLabel} · {_selected.Source}";
        _detail.Text =
            _selected.Detail;
        _copy.IsEnabled =
            true;
        _intelligence.IsEnabled =
            true;
    }

    private void RequestAction(
        UnifiedLogAction action)
    {
        if (_selected is null)
            return;

        LogActionRequested?.Invoke(
            this,
            new UnifiedLogActionRequestedEventArgs(
                _selected,
                action));
    }

    private static TextBlock Header(
        string text,
        int column)
    {
        var block =
            new TextBlock
            {
                Text =
                    text,
                Classes =
                {
                    "tableColumnHeader"
                }
            };

        Grid.SetColumn(
            block,
            column);

        return block;
    }

    private static TextBlock Cell(
        string text,
        int column,
        bool strong = false)
    {
        var block =
            new TextBlock
            {
                Text =
                    text,
                FontWeight =
                    strong
                        ? FontWeight.SemiBold
                        : FontWeight.Normal,
                TextTrimming =
                    TextTrimming.CharacterEllipsis
            };

        Grid.SetColumn(
            block,
            column);

        return block;
    }
}