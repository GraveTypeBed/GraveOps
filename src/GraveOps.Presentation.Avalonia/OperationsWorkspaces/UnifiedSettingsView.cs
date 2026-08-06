using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;

namespace GraveOps.Presentation.Avalonia.OperationsWorkspaces;

public sealed class UnifiedSettingsView :
    UserControl
{
    private readonly ComboBox _theme;
    private readonly ComboBox _density;
    private readonly CheckBox _restoreSession;
    private readonly CheckBox _silentRefresh;
    private readonly CheckBox _showFreshness;
    private readonly TextBlock _interfaceStatus;

    private readonly CheckBox _safeMode;
    private readonly CheckBox _infoLogs;
    private readonly CheckBox _infoContainers;
    private readonly CheckBox _openOverview;
    private readonly CheckBox _notifications;
    private readonly TextBox _refreshSeconds;
    private readonly TextBlock _operatorStatus;

    private readonly TextBlock _policySummary;
    private readonly TextBlock _capacitySummary;
    private readonly TextBlock _signalSummary;
    private readonly TextBlock _remediationSummary;
    private readonly TextBlock _performanceSummary;

    private readonly StackPanel _paths;
    private readonly TextBlock _pathStatus;

    private readonly TextBlock _branch;
    private readonly TextBlock _commit;
    private readonly TextBlock _worktree;
    private readonly TextBlock _origin;
    private readonly TextBlock _dotnet;

    private readonly List<Button> _interfaceButtons =
        new();

    private readonly List<Button> _operatorButtons =
        new();

    private readonly List<Button> _policyButtons =
        new();

    private readonly List<Button> _versionButtons =
        new();

    private UnifiedSettingsState _state =
        UnifiedSettingsState.Empty;

    private bool _suppressChanges;
    private bool _dirty;

    public UnifiedSettingsView()
    {
        HorizontalAlignment =
            HorizontalAlignment.Stretch;
        VerticalAlignment =
            VerticalAlignment.Stretch;

        _theme =
            new ComboBox();

        _density =
            new ComboBox();

        _restoreSession =
            new CheckBox
            {
                Content =
                    "Restore the last active page"
            };

        _silentRefresh =
            new CheckBox
            {
                Content =
                    "Keep background refresh silent"
            };

        _showFreshness =
            new CheckBox
            {
                Content =
                    "Show quiet freshness labels"
            };

        _interfaceStatus =
            OperationsUi.Muted(
                "Interface settings are loading.");

        _safeMode =
            new CheckBox
            {
                Content =
                    "Start in Safe Mode"
            };

        _infoLogs =
            new CheckBox
            {
                Content =
                    "Show informational log observations by default"
            };

        _infoContainers =
            new CheckBox
            {
                Content =
                    "Show informational exited containers by default"
            };

        _openOverview =
            new CheckBox
            {
                Content =
                    "Open Overview after startup"
            };

        _notifications =
            new CheckBox
            {
                Content =
                    "Show desktop notifications for new error-level incidents"
            };

        _refreshSeconds =
            new TextBox
            {
                Width =
                    100
            };

        _operatorStatus =
            OperationsUi.Dim(
                "Settings loaded.");

        _policySummary =
            new TextBlock
            {
                Text =
                    "0 active policies",
                FontWeight =
                    global::Avalonia.Media.FontWeight.SemiBold
            };

        _capacitySummary =
            OperationsUi.Muted(
                "Capacity alerts are loading.");

        _signalSummary =
            OperationsUi.Muted(
                "Signal quality is loading.");

        _remediationSummary =
            OperationsUi.Muted(
                "Verified remediation is loading.");

        _performanceSummary =
            OperationsUi.Muted(
                "UI performance is loading.");

        _paths =
            new StackPanel
            {
                Spacing =
                    5
            };

        _pathStatus =
            OperationsUi.Dim(
                "No path action run.");

        _branch =
            VersionValue();
        _commit =
            VersionValue();
        _worktree =
            VersionValue();
        _origin =
            VersionValue();
        _dotnet =
            VersionValue();

        WireDirty(
            _restoreSession,
            _silentRefresh,
            _showFreshness,
            _safeMode,
            _infoLogs,
            _infoContainers,
            _openOverview,
            _notifications);

        _theme.SelectionChanged +=
            (_, _) =>
            {
                if (_suppressChanges)
                    return;

                _dirty =
                    true;

                RaiseAction(
                    UnifiedSettingsAction.PreviewInterface);
            };

        _density.SelectionChanged +=
            (_, _) =>
            {
                if (_suppressChanges)
                    return;

                _dirty =
                    true;

                RaiseAction(
                    UnifiedSettingsAction.PreviewInterface);
            };

        _refreshSeconds.TextChanged +=
            (_, _) =>
            {
                if (!_suppressChanges)
                    _dirty = true;
            };

        Content =
            BuildWorkspace();

        Update(
            UnifiedSettingsState.Empty);
    }

    public event EventHandler<UnifiedSettingsActionRequestedEventArgs>?
        ActionRequested;

    public event EventHandler<UnifiedPathActionRequestedEventArgs>?
        PathActionRequested;

    public void Update(
        UnifiedSettingsState state)
    {
        _state =
            state ?? UnifiedSettingsState.Empty;

        _interfaceStatus.Text =
            _state.InterfaceStatus;

        _operatorStatus.Text =
            _state.OperatorStatus;

        _policySummary.Text =
            _state.PolicySummary;

        _capacitySummary.Text =
            _state.CapacityPolicySummary;

        _signalSummary.Text =
            _state.SignalQualitySummary;

        _remediationSummary.Text =
            _state.RemediationSummary;

        _performanceSummary.Text =
            _state.UiPerformanceSummary;

        _pathStatus.Text =
            _state.PathStatus;

        _branch.Text =
            _state.Version.Branch;

        _commit.Text =
            _state.Version.Commit;

        _worktree.Text =
            _state.Version.Worktree;

        _origin.Text =
            _state.Version.Origin;

        _dotnet.Text =
            _state.Version.Dotnet;

        if (!_dirty)
        {
            _suppressChanges =
                true;

            try
            {
                _theme.ItemsSource =
                    _state.ThemeOptions;

                _theme.SelectedItem =
                    _state.ThemeOptions.FirstOrDefault(item =>
                        item.Equals(
                            _state.Theme,
                            StringComparison.OrdinalIgnoreCase)) ??
                    _state.ThemeOptions.FirstOrDefault();

                _density.ItemsSource =
                    _state.DensityOptions;

                _density.SelectedItem =
                    _state.DensityOptions.FirstOrDefault(item =>
                        item.Equals(
                            _state.Density,
                            StringComparison.OrdinalIgnoreCase)) ??
                    _state.DensityOptions.FirstOrDefault();

                _restoreSession.IsChecked =
                    _state.RestoreSession;

                _silentRefresh.IsChecked =
                    _state.SilentRefresh;

                _showFreshness.IsChecked =
                    _state.ShowFreshness;

                _safeMode.IsChecked =
                    _state.StartSafeMode;

                _infoLogs.IsChecked =
                    _state.ShowInformationalLogs;

                _infoContainers.IsChecked =
                    _state.ShowInformationalContainers;

                _openOverview.IsChecked =
                    _state.OpenOverview;

                _notifications.IsChecked =
                    _state.DesktopNotifications;

                _refreshSeconds.Text =
                    _state.BackgroundRefreshSeconds;
            }
            finally
            {
                _suppressChanges =
                    false;
            }
        }

        SetEditableState();
        RenderPaths();
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

        content.Children.Add(
            new StackPanel
            {
                Children =
                {
                    OperationsUi.Title(
                        "Settings",
                        18),
                    OperationsUi.Subtitle(
                        "Paths, operator defaults, policies and version state in the shared GraveOps visual system.")
                }
            });

        content.Children.Add(
            BuildInterfaceModule());

        var lower =
            new Grid
            {
                ColumnDefinitions =
                    new ColumnDefinitions(
                        "1.05*,0.95*"),
                ColumnSpacing =
                    8
            };

        lower.Children.Add(
            BuildOperatorDefaults());

        var right =
            new StackPanel
            {
                Spacing =
                    8,
                Children =
                {
                    BuildPolicyModule(),
                    BuildPathsModule(),
                    BuildVersionModule()
                }
            };

        Grid.SetColumn(
            right,
            1);

        lower.Children.Add(
            right);

        content.Children.Add(
            lower);

        return
            OperationsUi.Scroll(
                content);
    }

    private Control BuildInterfaceModule()
    {
        var selectors =
            new Grid
            {
                ColumnDefinitions =
                    new ColumnDefinitions(
                        "*,*"),
                ColumnSpacing =
                    8
            };

        selectors.Children.Add(
            LabeledControl(
                "Theme",
                _theme));

        var density =
            LabeledControl(
                "Density",
                _density);

        Grid.SetColumn(
            density,
            1);

        selectors.Children.Add(
            density);

        var checks =
            new WrapPanel
            {
                Children =
                {
                    _restoreSession,
                    _silentRefresh,
                    _showFreshness
                }
            };

        foreach (var child in
                 checks.Children)
        {
            child.Margin =
                new Thickness(
                    0,
                    0,
                    18,
                    7);
        }

        var save =
            OperationsUi.Compact(
                "Save interface");
        save.Classes.Add(
            "primary");

        var setup =
            OperationsUi.Compact(
                "Express Setup");

        var reset =
            OperationsUi.Compact(
                "Reset Dashboard");

        var export =
            OperationsUi.Compact(
                "Export redacted profile");

        save.Click +=
            (_, _) =>
                RaiseAndCommit(
                    UnifiedSettingsAction.SaveInterface);

        setup.Click +=
            (_, _) =>
                RaiseAction(
                    UnifiedSettingsAction.ExpressSetup);

        reset.Click +=
            (_, _) =>
                RaiseAction(
                    UnifiedSettingsAction.ResetDashboard);

        export.Click +=
            (_, _) =>
                RaiseAction(
                    UnifiedSettingsAction.ExportProfile);

        _interfaceButtons.AddRange(
            new[]
            {
                save,
                setup,
                reset,
                export
            });

        var actions =
            new WrapPanel
            {
                Children =
                {
                    save,
                    setup,
                    reset,
                    export
                }
            };

        foreach (var child in
                 actions.Children)
        {
            child.Margin =
                new Thickness(
                    0,
                    0,
                    8,
                    8);
        }

        var left =
            new StackPanel
            {
                Spacing =
                    8,
                Children =
                {
                    new StackPanel
                    {
                        Children =
                        {
                            OperationsUi.Title(
                                "Interface & setup"),
                            OperationsUi.Subtitle(
                                "Themes, density, session restoration, silent refresh and first-run setup.")
                        }
                    },
                    selectors,
                    checks
                }
            };

        var right =
            new StackPanel
            {
                Spacing =
                    8,
                Children =
                {
                    actions,
                    OperationsUi.Inset(
                        _interfaceStatus)
                }
            };

        Grid.SetColumn(
            right,
            1);

        return
            OperationsUi.Module(
                new Grid
                {
                    ColumnDefinitions =
                        new ColumnDefinitions(
                            "1.15*,0.85*"),
                    ColumnSpacing =
                        14,
                    Children =
                    {
                        left,
                        right
                    }
                });
    }

    private Control BuildOperatorDefaults()
    {
        var save =
            new Button
            {
                Content =
                    "Save settings",
                Classes =
                {
                    "primary"
                }
            };

        var reset =
            new Button
            {
                Content =
                    "Restore defaults"
            };

        _operatorButtons.AddRange(
            new[]
            {
                save,
                reset
            });

        save.Click +=
            (_, _) =>
                RaiseAndCommit(
                    UnifiedSettingsAction.SaveOperatorDefaults);

        reset.Click +=
            (_, _) =>
                RaiseAndCommit(
                    UnifiedSettingsAction.RestoreOperatorDefaults);

        var refreshRow =
            new Grid
            {
                ColumnDefinitions =
                    new ColumnDefinitions(
                        "220,100,*"),
                ColumnSpacing =
                    6,
                Children =
                {
                    new TextBlock
                    {
                        Text =
                            "Background refresh seconds",
                        VerticalAlignment =
                            VerticalAlignment.Center
                    },
                    _refreshSeconds,
                    new WrapPanel
                    {
                        Children =
                        {
                            save,
                            reset
                        }
                    }
                }
            };

        Grid.SetColumn(
            _refreshSeconds,
            1);

        Grid.SetColumn(
            refreshRow.Children[2],
            2);

        return
            OperationsUi.Module(
                new StackPanel
                {
                    Spacing =
                        7,
                    Children =
                    {
                        new StackPanel
                        {
                            Children =
                            {
                                OperationsUi.Title(
                                    "Operator defaults"),
                                OperationsUi.Subtitle(
                                    "Persisted startup defaults and information-view preferences.")
                            }
                        },
                        _safeMode,
                        _infoLogs,
                        _infoContainers,
                        _openOverview,
                        _notifications,
                        refreshRow,
                        _operatorStatus
                    }
                });
    }

    private Control BuildPolicyModule()
    {
        var dashboard =
            new Button
            {
                Content =
                    "Dashboard policies"
            };

        var capacity =
            new Button
            {
                Content =
                    "Capacity alerts"
            };

        var signal =
            new Button
            {
                Content =
                    "Signal quality"
            };

        var remediation =
            new Button
            {
                Content =
                    "Remediation safety"
            };

        var performance =
            new Button
            {
                Content =
                    "UI performance"
            };

        var thresholds =
            new Button
            {
                Content =
                    "Storage thresholds"
            };

        dashboard.Click +=
            (_, _) =>
                RaiseAction(
                    UnifiedSettingsAction.DashboardPolicies);

        capacity.Click +=
            (_, _) =>
                RaiseAction(
                    UnifiedSettingsAction.CapacityAlerts);

        signal.Click +=
            (_, _) =>
                RaiseAction(
                    UnifiedSettingsAction.SignalQuality);

        remediation.Click +=
            (_, _) =>
                RaiseAction(
                    UnifiedSettingsAction.RemediationSafety);

        performance.Click +=
            (_, _) =>
                RaiseAction(
                    UnifiedSettingsAction.UiPerformance);

        thresholds.Click +=
            (_, _) =>
                RaiseAction(
                    UnifiedSettingsAction.StorageThresholds);

        _policyButtons.AddRange(
            new[]
            {
                dashboard,
                capacity,
                signal,
                remediation,
                performance,
                thresholds
            });

        var actions =
            new WrapPanel
            {
                Children =
                {
                    dashboard,
                    capacity,
                    signal,
                    remediation,
                    performance,
                    thresholds
                }
            };

        foreach (var child in
                 actions.Children)
        {
            child.Margin =
                new Thickness(
                    0,
                    0,
                    5,
                    5);
        }

        return
            OperationsUi.Module(
                new StackPanel
                {
                    Spacing =
                        5,
                    Children =
                    {
                        OperationsUi.Title(
                            "Policy management"),
                        _policySummary,
                        _capacitySummary,
                        _signalSummary,
                        _remediationSummary,
                        _performanceSummary,
                        actions
                    }
                });
    }

    private Control BuildPathsModule()
    {
        return
            OperationsUi.Module(
                new StackPanel
                {
                    Spacing =
                        5,
                    Children =
                    {
                        OperationsUi.Title(
                            "Application paths"),
                        _paths,
                        _pathStatus
                    }
                });
    }

    private Control BuildVersionModule()
    {
        var refresh =
            new Button
            {
                Content =
                    "Refresh version info"
            };

        _versionButtons.Add(
            refresh);

        refresh.Click +=
            (_, _) =>
                RaiseAction(
                    UnifiedSettingsAction.RefreshVersion);

        var heading =
            new Grid
            {
                ColumnDefinitions =
                    new ColumnDefinitions(
                        "*,Auto"),
                Children =
                {
                    OperationsUi.Title(
                        "Version and update state"),
                    refresh
                }
            };

        Grid.SetColumn(
            refresh,
            1);

        return
            OperationsUi.Module(
                new StackPanel
                {
                    Spacing =
                        5,
                    Children =
                    {
                        heading,
                        VersionRow(
                            "Branch",
                            _branch),
                        VersionRow(
                            "Commit",
                            _commit),
                        VersionRow(
                            "Worktree",
                            _worktree),
                        VersionRow(
                            "Origin",
                            _origin),
                        VersionRow(
                            ".NET SDK",
                            _dotnet)
                    }
                });
    }

    private void RenderPaths()
    {
        _paths.Children.Clear();

        foreach (var row in
                 _state.Paths)
        {
            var open =
                new Button
                {
                    Content =
                        "Open",
                    IsEnabled =
                        row.CanOpen
                };

            var terminal =
                new Button
                {
                    Content =
                        "Terminal",
                    IsEnabled =
                        row.CanOpenTerminal
                };

            open.Click +=
                (_, _) =>
                    PathActionRequested?.Invoke(
                        this,
                        new UnifiedPathActionRequestedEventArgs(
                            row,
                            UnifiedPathAction.Open));

            terminal.Click +=
                (_, _) =>
                    PathActionRequested?.Invoke(
                        this,
                        new UnifiedPathActionRequestedEventArgs(
                            row,
                            UnifiedPathAction.Terminal));

            var grid =
                new Grid
                {
                    ColumnDefinitions =
                        new ColumnDefinitions(
                            "115,*,Auto,Auto"),
                    ColumnSpacing =
                        5,
                    Children =
                    {
                        OperationsUi.Eyebrow(
                            row.Label),
                        new TextBlock
                        {
                            Text =
                                row.Path,
                            TextTrimming =
                                global::Avalonia.Media.TextTrimming.CharacterEllipsis
                        },
                        open,
                        terminal
                    }
                };

            Grid.SetColumn(
                grid.Children[1],
                1);

            Grid.SetColumn(
                open,
                2);

            Grid.SetColumn(
                terminal,
                3);

            _paths.Children.Add(
                grid);
        }

        if (_state.Paths.Count == 0)
        {
            _paths.Children.Add(
                OperationsUi.Muted(
                    "No application paths are available."));
        }
    }

    private void SetEditableState()
    {
        _theme.IsEnabled =
            _state.InterfaceEditable;

        _density.IsEnabled =
            _state.InterfaceEditable;

        _restoreSession.IsEnabled =
            _state.InterfaceEditable;

        _silentRefresh.IsEnabled =
            _state.InterfaceEditable;

        _showFreshness.IsEnabled =
            _state.InterfaceEditable;

        _safeMode.IsEnabled =
            _state.OperatorDefaultsEditable;

        _infoLogs.IsEnabled =
            _state.OperatorDefaultsEditable;

        _infoContainers.IsEnabled =
            _state.OperatorDefaultsEditable;

        _openOverview.IsEnabled =
            _state.OperatorDefaultsEditable;

        _notifications.IsEnabled =
            _state.OperatorDefaultsEditable;

        _refreshSeconds.IsEnabled =
            _state.OperatorDefaultsEditable;

        foreach (var button in
                 _interfaceButtons)
        {
            button.IsEnabled =
                _state.InterfaceEditable;
        }

        foreach (var button in
                 _operatorButtons)
        {
            button.IsEnabled =
                _state.OperatorDefaultsEditable;
        }

        foreach (var button in
                 _policyButtons)
        {
            button.IsEnabled =
                _state.PolicyActionsAvailable;
        }

        foreach (var button in
                 _versionButtons)
        {
            button.IsEnabled =
                _state.InterfaceEditable ||
                _state.OperatorDefaultsEditable;
        }
    }

    private void RaiseAndCommit(
        UnifiedSettingsAction action)
    {
        RaiseAction(
            action);

        _dirty =
            false;
    }

    private void RaiseAction(
        UnifiedSettingsAction action)
    {
        ActionRequested?.Invoke(
            this,
            new UnifiedSettingsActionRequestedEventArgs(
                action,
                CurrentInterfaceSettings(),
                CurrentOperatorSettings()));
    }

    private UnifiedInterfaceSettingsRequest
        CurrentInterfaceSettings() =>
        new(
            _theme.SelectedItem?.ToString() ??
            _state.Theme,
            _density.SelectedItem?.ToString() ??
            _state.Density,
            _restoreSession.IsChecked ==
            true,
            _silentRefresh.IsChecked !=
            false,
            _showFreshness.IsChecked !=
            false);

    private UnifiedOperatorSettingsRequest
        CurrentOperatorSettings() =>
        new(
            _safeMode.IsChecked ==
            true,
            _infoLogs.IsChecked ==
            true,
            _infoContainers.IsChecked ==
            true,
            _openOverview.IsChecked ==
            true,
            _notifications.IsChecked ==
            true,
            _refreshSeconds.Text ??
            string.Empty);

    private void WireDirty(
        params CheckBox[] boxes)
    {
        foreach (var box in boxes)
        {
            box.Click +=
                (_, _) =>
                {
                    if (!_suppressChanges)
                        _dirty = true;
                };
        }
    }

    private static StackPanel LabeledControl(
        string label,
        Control control) =>
        new()
        {
            Children =
            {
                OperationsUi.Eyebrow(
                    label),
                control
            }
        };

    private static TextBlock VersionValue() =>
        new()
        {
            Text = "--",
            TextTrimming =
                global::Avalonia.Media.TextTrimming.CharacterEllipsis
        };

    private static Grid VersionRow(
        string label,
        TextBlock value)
    {
        var grid =
            new Grid
            {
                ColumnDefinitions =
                    new ColumnDefinitions(
                        "120,*"),
                Children =
                {
                    OperationsUi.Eyebrow(
                        label),
                    value
                }
            };

        Grid.SetColumn(
            value,
            1);

        return grid;
    }
}
