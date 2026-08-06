using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace GraveOps.Presentation.Avalonia.MediaWorkspaces;

public sealed class UnifiedArrView :
    UserControl
{
    private readonly TextBlock
        _titleText =
            MediaUi.PageTitle(
                "Arr application");

    private readonly TextBlock
        _subtitleText =
            MediaUi.Subtitle(
                "Application health, queue and operational tools.");

    private readonly TextBlock
        _targetText =
            MediaUi.Muted(
                "--");

    private readonly TextBlock
        _freshnessText =
            MediaUi.Dim(
                "Waiting for live telemetry");

    private readonly Button
        _refreshButton =
            MediaUi.Primary(
                "Refresh");

    private readonly Button
        _openButton =
            MediaUi.Primary(
                "Open");

    private readonly Button
        _detailButton =
            MediaUi.Compact(
                "Full queue drill-down");

    private readonly Button
        _dockerButton =
            MediaUi.Compact(
                "Docker / stack");

    private readonly Button
        _logsButton =
            MediaUi.Compact(
                "Logs");

    private readonly Button
        _intelligenceButton =
            MediaUi.Compact(
                "Intelligence");

    private readonly TextBlock
        _instanceCountText =
            MediaUi.Muted(
                "0 instances");

    private readonly TextBlock
        _stateText =
            MediaUi.MetricValue(
                "WAITING");

    private readonly TextBlock
        _versionText =
            MediaUi.MetricValue(
                "--",
                18);

    private readonly TextBlock
        _workLabelText =
            MediaUi.Eyebrow(
                "QUEUE");

    private readonly TextBlock
        _workText =
            MediaUi.MetricValue(
                "--");

    private readonly TextBlock
        _workHintText =
            MediaUi.Dim(
                "Telemetry pending");

    private readonly TextBlock
        _healthText =
            MediaUi.MetricValue(
                "--");

    private readonly TextBlock
        _operationsHintText =
            MediaUi.Subtitle(
                "Application and stack tools stay together.");

    private readonly StackPanel
        _instancesPanel =
            new()
            {
                Spacing = 8
            };

    private readonly Border
        _instancesEmpty =
            MediaUi.EmptyState(
                "No compatible instance detected",
                "Verify the application service, container identity or published port.");

    private readonly TextBlock
        _workTitleText =
            MediaUi.Title(
                "Queue & health");

    private readonly TextBlock
        _workSubtitleText =
            MediaUi.Subtitle(
                "Item-level work and health messages.");

    private readonly StackPanel
        _workRowsPanel =
            new()
            {
                Spacing = 0
            };

    private readonly Border
        _workEmpty =
            MediaUi.EmptyState(
                "No active queue or health items",
                "Live item detail will appear here automatically.");

    private readonly TextBlock
        _footerText =
            MediaUi.Muted(
                "Waiting for live telemetry.");

    private readonly Border
        _configPanel =
            new();

    private readonly TextBox
        _endpointInput =
            new()
            {
                PlaceholderText =
                    "Application endpoint"
            };

    private readonly TextBox
        _secretInput =
            new()
            {
                PlaceholderText =
                    "Enter an API key only to replace or save it"
            };

    private readonly TextBlock
        _configEvidenceText =
            MediaUi.Muted(
                "Configuration is managed by the active platform adapter.");

    private readonly TextBlock
        _securityText =
            MediaUi.Muted(
                "Protected telemetry.");

    private readonly TextBlock
        _statusText =
            MediaUi.Dim(
                "Waiting for application telemetry.");

    private readonly Button
        _saveConfigButton =
            MediaUi.Primary(
                "Save + test");

    private readonly Button
        _clearConfigButton =
            MediaUi.Compact(
                "Clear saved key");

    private readonly Border
        _customizationPanel =
            new();

    private readonly TextBox
        _friendlyNameInput =
            new();

    private readonly TextBox
        _roleInput =
            new();

    private readonly TextBox
        _configPathInput =
            new();

    private readonly CheckBox
        _privacyModeCheckBox =
            new()
            {
                Content =
                    "Privacy mode"
            };

    private readonly TextBlock
        _modulesText =
            MediaUi.Muted(
                "No module information.");

    private readonly TextBlock
        _customizationStatusText =
            MediaUi.Dim(
                "Workspace customization unavailable.");

    private readonly Button
        _saveCustomizationButton =
            MediaUi.Primary(
                "Save workspace");

    private readonly Button
        _resetCustomizationButton =
            MediaUi.Compact(
                "Reset workspace");

    private UnifiedArrState
        _state =
            UnifiedArrState.Empty;

    private bool
        _configDirty;

    private bool
        _configSyncing;

    private bool
        _customizationDirty;

    private bool
        _customizationSyncing;

    public UnifiedArrView()
    {
        BuildView();
        WireEvents();
        Update(
            UnifiedArrState.Empty);
    }

    public event EventHandler<UnifiedArrActionEventArgs>?
        ActionRequested;

    public void Update(
        UnifiedArrState state)
    {
        _state =
            state ??
            UnifiedArrState.Empty;

        _titleText.Text =
            _state.Product;

        _subtitleText.Text =
            _state.Subtitle;

        _targetText.Text =
            _state.Target;

        _freshnessText.Text =
            _state.Freshness;

        _instanceCountText.Text =
            _state.InstanceCount;

        _stateText.Text =
            _state.State;

        _versionText.Text =
            _state.Version;

        _workLabelText.Text =
            _state.WorkLabel;

        _workText.Text =
            _state.Work;

        _workHintText.Text =
            _state.WorkHint;

        _healthText.Text =
            _state.Health;

        _operationsHintText.Text =
            _state.OperationsHint;

        _workTitleText.Text =
            _state.WorkTitle;

        _workSubtitleText.Text =
            _state.WorkSubtitle;

        _footerText.Text =
            _state.Footer;

        _refreshButton.IsEnabled =
            _state.CanRefresh;

        _openButton.IsEnabled =
            _state.CanOpen;

        _detailButton.IsEnabled =
            _state.CanOpenDetail;

        _dockerButton.IsEnabled =
            _state.CanOpenDocker;

        _logsButton.IsEnabled =
            _state.CanOpenLogs;

        _intelligenceButton.IsEnabled =
            _state.CanOpenIntelligence;

        _configPanel.IsVisible =
            _state.ConfigEditable;

        if (!_configDirty)
        {
            _configSyncing =
                true;

            try
            {
                _endpointInput.Text =
                    _state.ConfigEndpoint;

                _secretInput.Text =
                    string.Empty;
            }
            finally
            {
                _configSyncing =
                    false;
            }
        }

        _configEvidenceText.Text =
            _state.ConfigEvidence;

        _securityText.Text =
            _state.Security;

        _statusText.Text =
            _state.Status;

        _saveConfigButton.IsEnabled =
            _state.ConfigEditable;

        _clearConfigButton.IsEnabled =
            _state.ConfigEditable;

        _customizationPanel.IsVisible =
            _state.Customization.Available;

        if (!_customizationDirty)
        {
            _customizationSyncing =
                true;

            try
            {
                _friendlyNameInput.Text =
                    _state.Customization.FriendlyName;

                _roleInput.Text =
                    _state.Customization.Role;

                _configPathInput.Text =
                    _state.Customization.ConfigPath;

                _privacyModeCheckBox.IsChecked =
                    _state.Customization.PrivacyMode;
            }
            finally
            {
                _customizationSyncing =
                    false;
            }
        }

        _modulesText.Text =
            _state.Customization.Modules;

        _customizationStatusText.Text =
            _state.Customization.Status;

        _saveCustomizationButton.IsEnabled =
            _state.Customization.Available;

        _resetCustomizationButton.IsEnabled =
            _state.Customization.Available;

        RenderInstances();
        RenderWorkRows();
    }

    private void BuildView()
    {
        var root =
            new StackPanel
            {
                Spacing = 12,
                Margin =
                    new Thickness(
                        0,
                        0,
                        4,
                        4)
            };

        root.Children.Add(
            BuildHeading());

        root.Children.Add(
            BuildMetrics());

        root.Children.Add(
            BuildOperations());

        root.Children.Add(
            MediaUi.FlatCard(
                new StackPanel
                {
                    Spacing = 9,
                    Children =
                    {
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
                                        MediaUi.Title(
                                            "Service telemetry"),
                                        MediaUi.Subtitle(
                                            "Current endpoint state, version, queue and health information.")
                                    }
                                },
                                Right(
                                    _instanceCountText)
                            }
                        },
                        new Grid
                        {
                            Children =
                            {
                                MediaUi.Scroll(
                                    _instancesPanel,
                                    310),
                                _instancesEmpty
                            }
                        }
                    }
                }));

        root.Children.Add(
            BuildWorkTable());

        root.Children.Add(
            BuildConfiguration());

        root.Children.Add(
            BuildCustomization());

        root.Children.Add(
            MediaUi.Inset(
                new StackPanel
                {
                    Children =
                    {
                        _securityText,
                        _statusText
                    }
                }));

        Content =
            MediaUi.Scroll(
                root);
    }

    private Grid BuildHeading()
    {
        var grid =
            new Grid
            {
                ColumnDefinitions =
                    new ColumnDefinitions(
                        "*,Auto")
            };

        grid.Children.Add(
            new StackPanel
            {
                Children =
                {
                    _titleText,
                    _subtitleText
                }
            });

        var actions =
            new StackPanel
            {
                Orientation =
                    Orientation.Horizontal,
                VerticalAlignment =
                    VerticalAlignment.Center,
                Spacing = 9,
                Children =
                {
                    new StackPanel
                    {
                        Children =
                        {
                            _targetText,
                            _freshnessText
                        }
                    },
                    _refreshButton
                }
            };

        Grid.SetColumn(
            actions,
            1);

        grid.Children.Add(
            actions);

        return grid;
    }

    private Grid BuildMetrics()
    {
        var workCard =
            new Border
            {
                Classes =
                {
                    "flatCard",
                    "metric"
                },
                Child =
                    new StackPanel
                    {
                        Children =
                        {
                            _workLabelText,
                            _workText,
                            _workHintText
                        }
                    }
            };

        return
            MediaUi.FourMetrics(
                MediaUi.Metric(
                    "STATE",
                    _stateText),
                MediaUi.Metric(
                    "VERSION",
                    _versionText),
                workCard,
                MediaUi.Metric(
                    "HEALTH ISSUES",
                    _healthText));
    }

    private Border BuildOperations()
    {
        var actions =
            new WrapPanel
            {
                Children =
                {
                    _openButton,
                    _dockerButton,
                    _logsButton,
                    _intelligenceButton
                }
            };

        foreach (var button in
                 actions.Children
                     .OfType<Button>())
        {
            button.Margin =
                new Thickness(
                    0,
                    0,
                    8,
                    8);
        }

        return
            MediaUi.Inset(
                new StackPanel
                {
                    Children =
                    {
                        MediaUi.Title(
                            "Operations"),
                        _operationsHintText,
                        actions
                    }
                });
    }

    private Border BuildWorkTable()
    {
        var table =
            new StackPanel
            {
                MinWidth = 1040
            };

        table.Children.Add(
            new Border
            {
                Classes =
                {
                    "tableHeader"
                },
                Child =
                    new Grid
                    {
                        ColumnDefinitions =
                            new ColumnDefinitions(
                                "120,105,1.35*,105,90,100,1.25*"),
                        ColumnSpacing = 10,
                        Children =
                        {
                            MediaUi.HeaderCell(
                                "SERVICE",
                                0),
                            MediaUi.HeaderCell(
                                "TYPE",
                                1),
                            MediaUi.HeaderCell(
                                "ITEM / ISSUE",
                                2),
                            MediaUi.HeaderCell(
                                "STATE",
                                3),
                            MediaUi.HeaderCell(
                                "PROGRESS",
                                4),
                            MediaUi.HeaderCell(
                                "REMAINING",
                                5),
                            MediaUi.HeaderCell(
                                "DETAIL",
                                6)
                        }
                    }
            });

        table.Children.Add(
            _workRowsPanel);

        return
            MediaUi.FlatCard(
                new StackPanel
                {
                    Spacing = 9,
                    Children =
                    {
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
                                        _workTitleText,
                                        _workSubtitleText
                                    }
                                },
                                Right(
                                    _detailButton)
                            }
                        },
                        new Grid
                        {
                            Children =
                            {
                                MediaUi.Scroll(
                                    table,
                                    380,
                                    horizontal:
                                        true),
                                _workEmpty
                            }
                        },
                        MediaUi.Inset(
                            _footerText)
                    }
                });
    }

    private Border BuildConfiguration()
    {
        _configPanel.Classes.Add(
            "module");

        _configPanel.Classes.Add(
            "adaptive");

        _configPanel.Padding =
            new Thickness(
                12);

        var actions =
            new WrapPanel
            {
                Children =
                {
                    _saveConfigButton,
                    _clearConfigButton
                }
            };

        _saveConfigButton.Margin =
            new Thickness(
                0,
                0,
                8,
                0);

        _configPanel.Child =
            new StackPanel
            {
                Spacing = 8,
                Children =
                {
                    MediaUi.Title(
                        "Protected application connection"),
                    MediaUi.Subtitle(
                        "The API-key field is write-only in the shared presentation and is never projected from platform storage."),
                    Labeled(
                        "Endpoint",
                        _endpointInput),
                    Labeled(
                        "Replacement API key",
                        _secretInput),
                    _configEvidenceText,
                    actions
                }
            };

        return _configPanel;
    }

    private Border BuildCustomization()
    {
        _customizationPanel.Classes.Add(
            "module");

        _customizationPanel.Classes.Add(
            "adaptive");

        _customizationPanel.Padding =
            new Thickness(
                12);

        var actions =
            new WrapPanel
            {
                Children =
                {
                    _saveCustomizationButton,
                    _resetCustomizationButton
                }
            };

        _saveCustomizationButton.Margin =
            new Thickness(
                0,
                0,
                8,
                0);

        _customizationPanel.Child =
            new StackPanel
            {
                Spacing = 8,
                Children =
                {
                    MediaUi.Title(
                        "Customize workspace"),
                    MediaUi.Subtitle(
                        "Instance name, role, config path and enabled modules."),
                    MediaUi.TwoColumns(
                        Labeled(
                            "Friendly name",
                            _friendlyNameInput),
                        Labeled(
                            "Role",
                            _roleInput)),
                    Labeled(
                        "Config path",
                        _configPathInput),
                    _privacyModeCheckBox,
                    MediaUi.Inset(
                        new StackPanel
                        {
                            Children =
                            {
                                MediaUi.Eyebrow(
                                    "ENABLED MODULES"),
                                _modulesText
                            }
                        }),
                    actions,
                    _customizationStatusText
                }
            };

        return _customizationPanel;
    }

    private void RenderInstances()
    {
        _instancesPanel.Children.Clear();

        _instancesEmpty.IsVisible =
            _state.Instances.Count == 0;

        foreach (var row in
                 _state.Instances)
        {
            _instancesPanel.Children.Add(
                MediaUi.FlatCard(
                    new Grid
                    {
                        ColumnDefinitions =
                            new ColumnDefinitions(
                                "1.15*,0.95*,0.8*,0.85*,2.15*,Auto"),
                        ColumnSpacing = 12,
                        Children =
                        {
                            InstanceIdentity(
                                row),
                            InstanceColumn(
                                "ENDPOINT",
                                row.Endpoint,
                                1),
                            InstanceColumn(
                                "VERSION",
                                row.Version,
                                2),
                            InstanceColumn(
                                "WORK",
                                row.Work +
                                Environment.NewLine +
                                row.Health,
                                3),
                            MediaUi.Cell(
                                row.Detail,
                                4,
                                cssClass:
                                    "muted"),
                            MediaUi.Cell(
                                row.State,
                                5,
                                true)
                        }
                    },
                    9));
        }
    }

    private static StackPanel InstanceIdentity(
        UnifiedArrInstanceRow row)
    {
        var panel =
            new StackPanel
            {
                VerticalAlignment =
                    VerticalAlignment.Center,
                Children =
                {
                    new TextBlock
                    {
                        Text =
                            row.DisplayName,
                        FontSize = 15,
                        FontWeight =
                            FontWeight.SemiBold,
                        TextTrimming =
                            TextTrimming.CharacterEllipsis
                    },
                    MediaUi.Dim(
                        row.State)
                }
            };

        Grid.SetColumn(
            panel,
            0);

        return panel;
    }

    private static StackPanel InstanceColumn(
        string label,
        string value,
        int column)
    {
        var panel =
            new StackPanel
            {
                VerticalAlignment =
                    VerticalAlignment.Center,
                Children =
                {
                    MediaUi.Eyebrow(
                        label),
                    MediaUi.Muted(
                        value)
                }
            };

        Grid.SetColumn(
            panel,
            column);

        return panel;
    }

    private void RenderWorkRows()
    {
        _workRowsPanel.Children.Clear();

        _workEmpty.IsVisible =
            _state.WorkRows.Count == 0;

        foreach (var row in
                 _state.WorkRows)
        {
            _workRowsPanel.Children.Add(
                MediaUi.Inset(
                    new Grid
                    {
                        ColumnDefinitions =
                            new ColumnDefinitions(
                                "120,105,1.35*,105,90,100,1.25*"),
                        ColumnSpacing = 10,
                        Children =
                        {
                            MediaUi.Cell(
                                row.Service,
                                0,
                                true),
                            MediaUi.Cell(
                                row.Type,
                                1,
                                cssClass:
                                    "muted"),
                            MediaUi.Cell(
                                row.Item,
                                2),
                            MediaUi.Cell(
                                row.State,
                                3,
                                true),
                            MediaUi.Cell(
                                row.Progress,
                                4),
                            MediaUi.Cell(
                                row.Remaining,
                                5),
                            MediaUi.Cell(
                                row.Detail,
                                6,
                                cssClass:
                                    "dim")
                        }
                    },
                    7));
        }
    }

    private void WireEvents()
    {
        _refreshButton.Click +=
            (_, _) =>
                RaiseAction(
                    UnifiedArrAction.Refresh);

        _openButton.Click +=
            (_, _) =>
                RaiseAction(
                    UnifiedArrAction.Open);

        _detailButton.Click +=
            (_, _) =>
                RaiseAction(
                    UnifiedArrAction.OpenDetail);

        _dockerButton.Click +=
            (_, _) =>
                RaiseAction(
                    UnifiedArrAction.Docker);

        _logsButton.Click +=
            (_, _) =>
                RaiseAction(
                    UnifiedArrAction.Logs);

        _intelligenceButton.Click +=
            (_, _) =>
                RaiseAction(
                    UnifiedArrAction.Intelligence);

        _endpointInput.TextChanged +=
            (_, _) =>
            {
                if (!_configSyncing)
                    _configDirty = true;
            };

        _secretInput.TextChanged +=
            (_, _) =>
            {
                if (!_configSyncing)
                    _configDirty = true;
            };

        _friendlyNameInput.TextChanged +=
            (_, _) =>
            {
                if (!_customizationSyncing)
                    _customizationDirty = true;
            };

        _roleInput.TextChanged +=
            (_, _) =>
            {
                if (!_customizationSyncing)
                    _customizationDirty = true;
            };

        _configPathInput.TextChanged +=
            (_, _) =>
            {
                if (!_customizationSyncing)
                    _customizationDirty = true;
            };

        _privacyModeCheckBox.Click +=
            (_, _) =>
            {
                if (!_customizationSyncing)
                    _customizationDirty = true;
            };

        _saveConfigButton.Click +=
            (_, _) =>
            {
                RaiseAction(
                    UnifiedArrAction.SaveAndTest);

                CompleteConfigurationAction();
            };

        _clearConfigButton.Click +=
            (_, _) =>
            {
                RaiseAction(
                    UnifiedArrAction.ClearCredential);

                CompleteConfigurationAction();
            };

        _saveCustomizationButton.Click +=
            (_, _) =>
            {
                RaiseAction(
                    UnifiedArrAction.SaveCustomization);

                _customizationDirty =
                    false;
            };

        _resetCustomizationButton.Click +=
            (_, _) =>
            {
                RaiseAction(
                    UnifiedArrAction.ResetCustomization);

                _customizationDirty =
                    false;
            };
    }

    private void CompleteConfigurationAction()
    {
        _configSyncing =
            true;

        try
        {
            _secretInput.Text =
                string.Empty;
        }
        finally
        {
            _configSyncing =
                false;
        }

        _configDirty =
            false;
    }

    private void RaiseAction(
        UnifiedArrAction action)
    {
        ActionRequested?.Invoke(
            this,
            new UnifiedArrActionEventArgs(
                new UnifiedArrActionRequest(
                    action,
                    new UnifiedSecretConfigurationRequest(
                        _endpointInput.Text ??
                        string.Empty,
                        string.Empty,
                        _secretInput.Text ??
                        string.Empty),
                    new UnifiedArrCustomization(
                        _state.Customization.Available,
                        _friendlyNameInput.Text ??
                        string.Empty,
                        _roleInput.Text ??
                        string.Empty,
                        _configPathInput.Text ??
                        string.Empty,
                        _privacyModeCheckBox.IsChecked ==
                        true,
                        _state.Customization.Modules,
                        _state.Customization.Status))));
    }

    private static StackPanel Labeled(
        string label,
        Control control) =>
        new()
        {
            Spacing = 4,
            Children =
            {
                MediaUi.Eyebrow(
                    label),
                control
            }
        };

    private static Control Right(
        Control control)
    {
        control.HorizontalAlignment =
            HorizontalAlignment.Right;

        control.VerticalAlignment =
            VerticalAlignment.Center;

        Grid.SetColumn(
            control,
            1);

        return control;
    }
}
