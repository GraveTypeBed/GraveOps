using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace GraveOps.Presentation.Avalonia.MediaWorkspaces;

public sealed class UnifiedDownloadClientView :
    UserControl
{
    private readonly TextBlock
        _titleText =
            MediaUi.PageTitle(
                "Download client");

    private readonly TextBlock
        _descriptionText =
            MediaUi.Subtitle(
                "Live download analytics and current work.");

    private readonly TextBlock
        _targetText =
            MediaUi.Muted(
                "--");

    private readonly TextBlock
        _freshnessText =
            MediaUi.Dim(
                "CHECKING...");

    private readonly Button
        _refreshButton =
            MediaUi.Primary(
                "Refresh");

    private readonly TextBlock
        _stateText =
            MediaUi.MetricValue(
                "CHECKING");

    private readonly TextBlock
        _securityText =
            MediaUi.Dim(
                "Protected telemetry");

    private readonly TextBlock
        _versionText =
            MediaUi.MetricValue(
                "--",
                21);

    private readonly TextBlock
        _connectionText =
            MediaUi.Dim(
                "--");

    private readonly TextBlock
        _activeText =
            MediaUi.MetricValue(
                "--");

    private readonly TextBlock
        _activeDetailText =
            MediaUi.Dim(
                "Telemetry pending");

    private readonly TextBlock
        _itemsLabelText =
            MediaUi.Eyebrow(
                "ITEMS");

    private readonly TextBlock
        _itemsText =
            MediaUi.MetricValue(
                "--");

    private readonly TextBlock
        _itemsDetailText =
            MediaUi.Dim(
                "Telemetry pending");

    private readonly TextBlock
        _metric1LabelText =
            MediaUi.Eyebrow(
                "DOWNLOAD");

    private readonly TextBlock
        _metric1ValueText =
            MediaUi.MetricValue(
                "--",
                19);

    private readonly TextBlock
        _metric2LabelText =
            MediaUi.Eyebrow(
                "UPLOAD");

    private readonly TextBlock
        _metric2ValueText =
            MediaUi.MetricValue(
                "--",
                19);

    private readonly TextBlock
        _metric3LabelText =
            MediaUi.Eyebrow(
                "REMAINING");

    private readonly TextBlock
        _metric3ValueText =
            MediaUi.MetricValue(
                "--",
                19);

    private readonly TextBlock
        _metric4LabelText =
            MediaUi.Eyebrow(
                "TIME");

    private readonly TextBlock
        _metric4ValueText =
            MediaUi.MetricValue(
                "--",
                19);

    private readonly TextBlock
        _operationsHintText =
            MediaUi.Subtitle(
                "Read-only analytics are automatic; operational handoffs stay explicit.");

    private readonly Button
        _openButton =
            MediaUi.Primary(
                "Open Web UI");

    private readonly Button
        _dockerButton =
            MediaUi.Compact(
                "Docker / container");

    private readonly Button
        _logsButton =
            MediaUi.Compact(
                "Logs");

    private readonly Button
        _terminalButton =
            MediaUi.Compact(
                "SSH terminal");

    private readonly TextBlock
        _transferAnalyticsText =
            MediaUi.Muted(
                "Waiting for transfer analytics.");

    private readonly TextBlock
        _workloadAnalyticsText =
            MediaUi.Muted(
                "Waiting for workload analytics.");

    private readonly TextBlock
        _queueTitleText =
            MediaUi.Title(
                "Current work");

    private readonly TextBlock
        _queueHintText =
            MediaUi.Subtitle(
                "Live work appears automatically.");

    private readonly StackPanel
        _queuePanel =
            new()
            {
                Spacing = 0
            };

    private readonly Border
        _queueEmpty =
            MediaUi.EmptyState(
                "No active queue items",
                "Current work will appear here automatically.");

    private readonly TextBlock
        _historyTitleText =
            MediaUi.Title(
                "Recent history");

    private readonly TextBlock
        _historyHintText =
            MediaUi.Subtitle(
                "Completed work appears automatically.");

    private readonly StackPanel
        _historyPanel =
            new()
            {
                Spacing = 0
            };

    private readonly Border
        _historyEmpty =
            MediaUi.EmptyState(
                "No recent history",
                "Completed work will appear here automatically.");

    private readonly TextBlock
        _statusText =
            MediaUi.Dim(
                "Waiting for download-client telemetry.");

    private readonly Border
        _configPanel =
            new();

    private readonly TextBox
        _endpointInput =
            new()
            {
                PlaceholderText =
                    "Download-client endpoint"
            };

    private readonly TextBlock
        _userNameLabelText =
            MediaUi.Eyebrow(
                "USER NAME");

    private readonly TextBox
        _userNameInput =
            new();

    private readonly TextBlock
        _secretLabelText =
            MediaUi.Eyebrow(
                "CREDENTIAL");

    private readonly TextBox
        _secretInput =
            new()
            {
                PlaceholderText =
                    "Enter a credential only to replace or save it"
            };

    private readonly TextBlock
        _configEvidenceText =
            MediaUi.Muted(
                "Configuration is managed by the active platform adapter.");

    private readonly Button
        _saveButton =
            MediaUi.Primary(
                "Save + test");

    private readonly Button
        _clearButton =
            MediaUi.Compact(
                "Clear saved credential");

    private UnifiedDownloadClientState
        _state =
            UnifiedDownloadClientState.Empty;

    private bool
        _configDirty;

    private bool
        _configSyncing;

    public UnifiedDownloadClientView()
    {
        BuildView();
        WireEvents();
        Update(
            UnifiedDownloadClientState.Empty);
    }

    public event EventHandler<UnifiedDownloadClientActionEventArgs>?
        ActionRequested;

    public void Update(
        UnifiedDownloadClientState state)
    {
        _state =
            state ??
            UnifiedDownloadClientState.Empty;

        _titleText.Text =
            _state.Product;

        _descriptionText.Text =
            _state.Description;

        _targetText.Text =
            _state.Target;

        _freshnessText.Text =
            _state.Freshness;

        _stateText.Text =
            _state.State;

        _securityText.Text =
            _state.Security;

        _versionText.Text =
            _state.Version;

        _connectionText.Text =
            _state.Connection;

        _activeText.Text =
            _state.Active;

        _activeDetailText.Text =
            _state.ActiveDetail;

        _itemsLabelText.Text =
            _state.ItemsLabel;

        _itemsText.Text =
            _state.Items;

        _itemsDetailText.Text =
            _state.ItemsDetail;

        _metric1LabelText.Text =
            _state.Metric1Label;

        _metric1ValueText.Text =
            _state.Metric1Value;

        _metric2LabelText.Text =
            _state.Metric2Label;

        _metric2ValueText.Text =
            _state.Metric2Value;

        _metric3LabelText.Text =
            _state.Metric3Label;

        _metric3ValueText.Text =
            _state.Metric3Value;

        _metric4LabelText.Text =
            _state.Metric4Label;

        _metric4ValueText.Text =
            _state.Metric4Value;

        _operationsHintText.Text =
            _state.OperationsHint;

        _transferAnalyticsText.Text =
            _state.TransferAnalytics;

        _workloadAnalyticsText.Text =
            _state.WorkloadAnalytics;

        _queueTitleText.Text =
            _state.QueueTitle;

        _queueHintText.Text =
            _state.QueueHint;

        _historyTitleText.Text =
            _state.HistoryTitle;

        _historyHintText.Text =
            _state.HistoryHint;

        _statusText.Text =
            _state.Status;

        _refreshButton.IsEnabled =
            _state.CanRefresh;

        _openButton.IsEnabled =
            _state.CanOpen;

        _dockerButton.IsEnabled =
            _state.CanOpenDocker;

        _logsButton.IsEnabled =
            _state.CanOpenLogs;

        _terminalButton.IsEnabled =
            _state.CanOpenTerminal;

        _configPanel.IsVisible =
            _state.ConfigEditable;

        _userNameLabelText.Text =
            _state.UserNameLabel.ToUpperInvariant();

        _userNameInput.IsVisible =
            !string.IsNullOrWhiteSpace(
                _state.UserNameLabel);

        _userNameLabelText.IsVisible =
            _userNameInput.IsVisible;

        _secretLabelText.Text =
            _state.SecretLabel.ToUpperInvariant();

        if (!_configDirty)
        {
            _configSyncing =
                true;

            try
            {
                _endpointInput.Text =
                    _state.ConfigEndpoint;

                _userNameInput.Text =
                    _state.ConfigUserName;

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

        _saveButton.IsEnabled =
            _state.ConfigEditable;

        _clearButton.IsEnabled =
            _state.ConfigEditable;

        RenderRows(
            _queuePanel,
            _state.Queue,
            _queueEmpty);

        RenderRows(
            _historyPanel,
            _state.History,
            _historyEmpty);
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
            BuildSummaryMetrics());

        root.Children.Add(
            BuildTransferMetrics());

        root.Children.Add(
            BuildOperations());

        root.Children.Add(
            MediaUi.TwoColumns(
                MediaUi.FlatCard(
                    new StackPanel
                    {
                        Children =
                        {
                            MediaUi.Title(
                                "Transfer analytics"),
                            _transferAnalyticsText
                        }
                    }),
                MediaUi.FlatCard(
                    new StackPanel
                    {
                        Children =
                        {
                            MediaUi.Title(
                                "Workload analytics"),
                            _workloadAnalyticsText
                        }
                    })));

        root.Children.Add(
            BuildTable(
                _queueTitleText,
                _queueHintText,
                _queuePanel,
                _queueEmpty));

        root.Children.Add(
            BuildTable(
                _historyTitleText,
                _historyHintText,
                _historyPanel,
                _historyEmpty));

        root.Children.Add(
            BuildConfiguration());

        root.Children.Add(
            MediaUi.Inset(
                _statusText));

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
                    _descriptionText
                }
            });

        var actions =
            new StackPanel
            {
                Orientation =
                    Orientation.Horizontal,
                VerticalAlignment =
                    VerticalAlignment.Center,
                Spacing = 10,
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

    private Grid BuildSummaryMetrics()
    {
        var state =
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
                            MediaUi.Eyebrow(
                                "STATE"),
                            _stateText,
                            _securityText
                        }
                    }
            };

        var version =
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
                            MediaUi.Eyebrow(
                                "VERSION"),
                            _versionText,
                            _connectionText
                        }
                    }
            };

        var active =
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
                            MediaUi.Eyebrow(
                                "ACTIVE"),
                            _activeText,
                            _activeDetailText
                        }
                    }
            };

        var items =
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
                            _itemsLabelText,
                            _itemsText,
                            _itemsDetailText
                        }
                    }
            };

        return
            MediaUi.FourMetrics(
                state,
                version,
                active,
                items);
    }

    private Grid BuildTransferMetrics() =>
        MediaUi.FourMetrics(
            Metric(
                _metric1LabelText,
                _metric1ValueText),
            Metric(
                _metric2LabelText,
                _metric2ValueText),
            Metric(
                _metric3LabelText,
                _metric3ValueText),
            Metric(
                _metric4LabelText,
                _metric4ValueText));

    private static Border Metric(
        TextBlock label,
        TextBlock value) =>
        MediaUi.Inset(
            new StackPanel
            {
                Children =
                {
                    label,
                    value
                }
            });

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
                    _terminalButton
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
                    0);
        }

        return
            MediaUi.Inset(
                new StackPanel
                {
                    Spacing = 10,
                    Children =
                    {
                        MediaUi.Title(
                            "Operations"),
                        _operationsHintText,
                        actions
                    }
                });
    }

    private static Border BuildTable(
        TextBlock title,
        TextBlock hint,
        StackPanel rows,
        Border empty)
    {
        var table =
            new StackPanel
            {
                MinWidth = 1320,
                Children =
                {
                    BuildHeader(),
                    rows
                }
            };

        return
            MediaUi.FlatCard(
                new StackPanel
                {
                    Spacing = 9,
                    Children =
                    {
                        new StackPanel
                        {
                            Children =
                            {
                                title,
                                hint
                            }
                        },
                        new Grid
                        {
                            Children =
                            {
                                MediaUi.Scroll(
                                    table,
                                    350,
                                    horizontal:
                                        true),
                                empty
                            }
                        }
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

        var userPanel =
            new StackPanel
            {
                Spacing = 4,
                Children =
                {
                    _userNameLabelText,
                    _userNameInput
                }
            };

        var secretPanel =
            new StackPanel
            {
                Spacing = 4,
                Children =
                {
                    _secretLabelText,
                    _secretInput
                }
            };

        var actions =
            new WrapPanel
            {
                Children =
                {
                    _saveButton,
                    _clearButton
                }
            };

        _saveButton.Margin =
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
                        "Protected download-client connection"),
                    MediaUi.Subtitle(
                        "Credentials are write-only in the shared presentation and are never projected from platform storage."),
                    Labeled(
                        "Endpoint",
                        _endpointInput),
                    MediaUi.TwoColumns(
                        userPanel,
                        secretPanel),
                    _configEvidenceText,
                    actions
                }
            };

        return _configPanel;
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

    private static Border BuildHeader() =>
        new()
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
                            "300,120,100,100,110,100,105,105,100,90,90,210"),
                    ColumnSpacing = 10,
                    Children =
                    {
                        MediaUi.HeaderCell(
                            "NAME",
                            0),
                        MediaUi.HeaderCell(
                            "CATEGORY",
                            1),
                        MediaUi.HeaderCell(
                            "STATE",
                            2),
                        MediaUi.HeaderCell(
                            "PROGRESS",
                            3),
                        MediaUi.HeaderCell(
                            "SIZE",
                            4),
                        MediaUi.HeaderCell(
                            "REMAINING",
                            5),
                        MediaUi.HeaderCell(
                            "DOWNLOAD",
                            6),
                        MediaUi.HeaderCell(
                            "UPLOAD",
                            7),
                        MediaUi.HeaderCell(
                            "ETA",
                            8),
                        MediaUi.HeaderCell(
                            "PEERS",
                            9),
                        MediaUi.HeaderCell(
                            "RATIO",
                            10),
                        MediaUi.HeaderCell(
                            "DETAIL",
                            11)
                    }
                }
        };

    private static void RenderRows(
        StackPanel panel,
        IReadOnlyList<UnifiedTransferRow> rows,
        Border empty)
    {
        panel.Children.Clear();

        empty.IsVisible =
            rows.Count == 0;

        foreach (var row in rows)
        {
            panel.Children.Add(
                MediaUi.Inset(
                    new Grid
                    {
                        ColumnDefinitions =
                            new ColumnDefinitions(
                                "300,120,100,100,110,100,105,105,100,90,90,210"),
                        ColumnSpacing = 10,
                        Children =
                        {
                            MediaUi.Cell(
                                row.Name,
                                0,
                                true),
                            MediaUi.Cell(
                                row.Category,
                                1,
                                cssClass:
                                    "muted"),
                            MediaUi.Cell(
                                row.State,
                                2),
                            MediaUi.Cell(
                                row.Progress,
                                3),
                            MediaUi.Cell(
                                row.Size,
                                4),
                            MediaUi.Cell(
                                row.Remaining,
                                5),
                            MediaUi.Cell(
                                row.DownloadSpeed,
                                6),
                            MediaUi.Cell(
                                row.UploadSpeed,
                                7),
                            MediaUi.Cell(
                                row.Eta,
                                8),
                            MediaUi.Cell(
                                row.Peers,
                                9),
                            MediaUi.Cell(
                                row.Ratio,
                                10),
                            MediaUi.Cell(
                                row.Detail,
                                11,
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
                    UnifiedDownloadClientAction.Refresh);

        _openButton.Click +=
            (_, _) =>
                RaiseAction(
                    UnifiedDownloadClientAction.Open);

        _dockerButton.Click +=
            (_, _) =>
                RaiseAction(
                    UnifiedDownloadClientAction.Docker);

        _logsButton.Click +=
            (_, _) =>
                RaiseAction(
                    UnifiedDownloadClientAction.Logs);

        _terminalButton.Click +=
            (_, _) =>
                RaiseAction(
                    UnifiedDownloadClientAction.Terminal);

        _endpointInput.TextChanged +=
            (_, _) =>
            {
                if (!_configSyncing)
                    _configDirty = true;
            };

        _userNameInput.TextChanged +=
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

        _saveButton.Click +=
            (_, _) =>
            {
                RaiseAction(
                    UnifiedDownloadClientAction.SaveAndTest);

                CompleteConfigurationAction();
            };

        _clearButton.Click +=
            (_, _) =>
            {
                RaiseAction(
                    UnifiedDownloadClientAction.ClearCredential);

                CompleteConfigurationAction();
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
        UnifiedDownloadClientAction action)
    {
        ActionRequested?.Invoke(
            this,
            new UnifiedDownloadClientActionEventArgs(
                action,
                new UnifiedSecretConfigurationRequest(
                    _endpointInput.Text ??
                    string.Empty,
                    _userNameInput.Text ??
                    string.Empty,
                    _secretInput.Text ??
                    string.Empty)));
    }
}
