using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace GraveOps.Presentation.Avalonia.MediaWorkspaces;

public sealed class UnifiedPlexView :
    UserControl
{
    private readonly TextBlock
        _targetText =
            MediaUi.Muted(
                "--");

    private readonly TextBlock
        _freshnessText =
            MediaUi.Dim(
                "CHECKING...");

    private readonly TextBlock
        _serviceText =
            MediaUi.MetricValue(
                "CHECKING");

    private readonly TextBlock
        _serviceDetailText =
            MediaUi.Dim(
                "Waiting for runtime ownership");

    private readonly TextBlock
        _versionText =
            MediaUi.MetricValue(
                "--",
                20);

    private readonly TextBlock
        _endpointText =
            MediaUi.MetricValue(
                "--",
                18);

    private readonly TextBlock
        _connectionText =
            MediaUi.Dim(
                "Waiting for identity probe");

    private readonly TextBlock
        _dependencyText =
            MediaUi.MetricValue(
                "--",
                17);

    private readonly Button
        _refreshButton =
            MediaUi.Primary(
                "Refresh");

    private readonly Button
        _openButton =
            MediaUi.Primary(
                "Open Plex");

    private readonly Button
        _restartButton =
            MediaUi.Compact(
                "Restart Plex");

    private readonly Button
        _logsButton =
            MediaUi.Compact(
                "Plex logs");

    private readonly Button
        _terminalButton =
            MediaUi.Compact(
                "SSH terminal");

    private readonly Button
        _intelligenceButton =
            MediaUi.Compact(
                "Intelligence");

    private readonly TextBlock
        _operationsStatusText =
            MediaUi.Dim(
                "Waiting for Plex discovery.");

    private readonly TextBlock
        _activeText =
            MediaUi.MetricValue(
                "--",
                22);

    private readonly TextBlock
        _directPlayText =
            MediaUi.MetricValue(
                "--",
                22);

    private readonly TextBlock
        _transcodeText =
            MediaUi.MetricValue(
                "--",
                22);

    private readonly TextBlock
        _librariesText =
            MediaUi.MetricValue(
                "--",
                22);

    private readonly TextBlock
        _playbackAnalyticsText =
            MediaUi.Muted(
                "Waiting for live session telemetry...");

    private readonly TextBlock
        _serverContextText =
            MediaUi.Muted(
                "Waiting for Plex identity and library context...");

    private readonly TextBlock
        _sessionCountText =
            MediaUi.Dim(
                "--");

    private readonly StackPanel
        _sessionsPanel =
            new()
            {
                Spacing = 0
            };

    private readonly Border
        _sessionsEmpty =
            MediaUi.EmptyState(
                "No active Plex sessions",
                "No viewers are currently streaming from this Plex server.");

    private readonly TextBlock
        _securityText =
            MediaUi.Muted(
                "Protected telemetry.");

    private readonly TextBlock
        _statusText =
            MediaUi.Dim(
                "Waiting for Plex telemetry.");

    private readonly TextBox
        _endpointInput =
            new()
            {
                PlaceholderText =
                    "Plex endpoint"
            };

    private readonly TextBox
        _secretInput =
            new()
            {
                PlaceholderText =
                    "Enter a token only to replace or save it"
            };

    private readonly TextBlock
        _configEvidenceText =
            MediaUi.Muted(
                "Configuration is managed by the active platform adapter.");

    private readonly TextBlock
        _configStatusText =
            MediaUi.Dim(
                "No configuration action run.");

    private readonly Button
        _saveButton =
            MediaUi.Primary(
                "Save + test");

    private readonly Button
        _clearButton =
            MediaUi.Compact(
                "Clear saved token");

    private readonly Border
        _configPanel =
            new();

    private UnifiedPlexState
        _state =
            UnifiedPlexState.Empty;

    private bool
        _configDirty;

    private bool
        _configSyncing;

    public UnifiedPlexView()
    {
        BuildView();
        WireEvents();
        Update(
            UnifiedPlexState.Empty);
    }

    public event EventHandler<UnifiedPlexActionEventArgs>?
        ActionRequested;

    public void Update(
        UnifiedPlexState state)
    {
        _state =
            state ??
            UnifiedPlexState.Empty;

        _targetText.Text =
            _state.Target;

        _freshnessText.Text =
            _state.Freshness;

        _serviceText.Text =
            _state.Service;

        _serviceDetailText.Text =
            _state.ServiceDetail;

        _versionText.Text =
            _state.Version;

        _endpointText.Text =
            _state.Endpoint;

        _connectionText.Text =
            _state.Connection;

        _dependencyText.Text =
            _state.Dependency;

        _operationsStatusText.Text =
            _state.Status;

        _activeText.Text =
            _state.ActiveSessions;

        _directPlayText.Text =
            _state.DirectPlay;

        _transcodeText.Text =
            _state.Transcoding;

        _librariesText.Text =
            _state.Libraries;

        _playbackAnalyticsText.Text =
            string.IsNullOrWhiteSpace(
                _state.DirectStream)
                ? _state.PlaybackAnalytics
                : _state.PlaybackAnalytics +
                  Environment.NewLine +
                  "Direct stream: " +
                  _state.DirectStream;

        _serverContextText.Text =
            _state.ServerContext;

        _sessionCountText.Text =
            _state.SessionCount;

        _securityText.Text =
            _state.Security;

        _statusText.Text =
            _state.Status;

        _refreshButton.IsEnabled =
            _state.CanRefresh;

        _openButton.IsEnabled =
            _state.CanOpen;

        _restartButton.IsEnabled =
            _state.CanRestart;

        _logsButton.IsEnabled =
            _state.CanOpenLogs;

        _terminalButton.IsEnabled =
            _state.CanOpenTerminal;

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

        _configStatusText.Text =
            _state.ConfigStatus;

        _saveButton.IsEnabled =
            _state.ConfigEditable;

        _clearButton.IsEnabled =
            _state.ConfigEditable;

        RenderSessions();
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

        var heading =
            new Grid
            {
                ColumnDefinitions =
                    new ColumnDefinitions(
                        "*,Auto")
            };

        heading.Children.Add(
            new StackPanel
            {
                Children =
                {
                    MediaUi.PageTitle(
                        "Plex"),
                    MediaUi.Subtitle(
                        "Library availability, live sessions, playback decisions and guarded operations.")
                }
            });

        var headingActions =
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
            headingActions,
            1);

        heading.Children.Add(
            headingActions);

        root.Children.Add(
            heading);

        root.Children.Add(
            MediaUi.FourMetrics(
                MetricWithDetail(
                    "SERVICE",
                    _serviceText,
                    _serviceDetailText),
                MediaUi.Metric(
                    "PLEX VERSION",
                    _versionText,
                    "From the active Plex identity endpoint"),
                MetricWithDetail(
                    "ENDPOINT",
                    _endpointText,
                    _connectionText),
                MediaUi.Metric(
                    "DEPENDENCY",
                    _dependencyText,
                    "Stack and library-path context")));

        root.Children.Add(
            BuildOperations());

        root.Children.Add(
            MediaUi.FourMetrics(
                MediaUi.Metric(
                    "ACTIVE SESSIONS",
                    _activeText),
                MediaUi.Metric(
                    "DIRECT PLAY",
                    _directPlayText),
                MediaUi.Metric(
                    "TRANSCODING",
                    _transcodeText),
                MediaUi.Metric(
                    "LIBRARIES",
                    _librariesText)));

        root.Children.Add(
            MediaUi.TwoColumns(
                MediaUi.FlatCard(
                    new StackPanel
                    {
                        Children =
                        {
                            MediaUi.Title(
                                "Playback analytics"),
                            _playbackAnalyticsText
                        }
                    }),
                MediaUi.FlatCard(
                    new StackPanel
                    {
                        Children =
                        {
                            MediaUi.Title(
                                "Server context"),
                            _serverContextText
                        }
                    })));

        root.Children.Add(
            BuildSessions());

        root.Children.Add(
            BuildConfiguration());

        root.Children.Add(
            MediaUi.Inset(
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
                                _securityText,
                                _statusText
                            }
                        },
                        BuildReadOnlyLabel()
                    }
                }));

        Content =
            MediaUi.Scroll(
                root);
    }

    private Border BuildOperations()
    {
        var actions =
            new WrapPanel
            {
                Children =
                {
                    _openButton,
                    _restartButton,
                    _logsButton,
                    _terminalButton,
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
                    0);
        }

        return
            MediaUi.Inset(
                new StackPanel
                {
                    Spacing = 10,
                    Children =
                    {
                        new StackPanel
                        {
                            Children =
                            {
                                MediaUi.Title(
                                    "Plex operations"),
                                MediaUi.Subtitle(
                                    "Routine Plex management stays inline. Mutations remain capability and safety gated.")
                            }
                        },
                        actions,
                        _operationsStatusText
                    }
                });
    }

    private Border BuildSessions()
    {
        var table =
            new StackPanel
            {
                MinWidth = 1280,
                Spacing = 0
            };

        table.Children.Add(
            BuildSessionHeader());

        table.Children.Add(
            _sessionsPanel);

        var body =
            new Grid
            {
                Children =
                {
                    MediaUi.Scroll(
                        table,
                        360,
                        horizontal:
                            true),
                    _sessionsEmpty
                }
            };

        return
            MediaUi.FlatCard(
                new StackPanel
                {
                    Spacing = 10,
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
                                            "Session analytics"),
                                        MediaUi.Subtitle(
                                            "Current viewers, players, progress, playback decisions, bandwidth and transcode context.")
                                    }
                                },
                                RightAligned(
                                    _sessionCountText)
                            }
                        },
                        body
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

        var buttons =
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
                        "Protected Plex connection"),
                    MediaUi.Subtitle(
                        "The secret field is write-only in the shared presentation and is never projected from platform storage."),
                    Labeled(
                        "Endpoint",
                        _endpointInput),
                    Labeled(
                        "Replacement token",
                        _secretInput),
                    _configEvidenceText,
                    buttons,
                    _configStatusText
                }
            };

        return _configPanel;
    }

    private static Border MetricWithDetail(
        string label,
        TextBlock value,
        TextBlock detail) =>
        new()
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
                            label),
                        value,
                        detail
                    }
                }
        };

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

    private static TextBlock BuildReadOnlyLabel()
    {
        var label =
            MediaUi.Eyebrow(
                "READ-ONLY SESSION DATA");

        Grid.SetColumn(
            label,
            1);

        return label;
    }

    private static Control RightAligned(
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

    private static Border BuildSessionHeader()
    {
        var grid =
            new Grid
            {
                ColumnDefinitions =
                    new ColumnDefinitions(
                        "300,120,160,90,100,105,105,105,220"),
                ColumnSpacing = 10,
                Children =
                {
                    MediaUi.HeaderCell(
                        "ITEM",
                        0),
                    MediaUi.HeaderCell(
                        "USER",
                        1),
                    MediaUi.HeaderCell(
                        "PLAYER",
                        2),
                    MediaUi.HeaderCell(
                        "STATE",
                        3),
                    MediaUi.HeaderCell(
                        "PROGRESS",
                        4),
                    MediaUi.HeaderCell(
                        "VIDEO",
                        5),
                    MediaUi.HeaderCell(
                        "AUDIO",
                        6),
                    MediaUi.HeaderCell(
                        "BANDWIDTH",
                        7),
                    MediaUi.HeaderCell(
                        "DETAIL",
                        8)
                }
            };

        return
            new Border
            {
                Classes =
                {
                    "tableHeader"
                },
                Child = grid
            };
    }

    private void RenderSessions()
    {
        _sessionsPanel.Children.Clear();

        _sessionsEmpty.IsVisible =
            _state.Sessions.Count == 0;

        if (_state.Sessions.Count == 0)
        {
            if (_sessionsEmpty.Child is
                StackPanel panel &&
                panel.Children
                    .OfType<TextBlock>()
                    .LastOrDefault() is
                { } detail)
            {
                detail.Text =
                    _state.EmptyText;
            }

            return;
        }

        foreach (var row in
                 _state.Sessions)
        {
            _sessionsPanel.Children.Add(
                MediaUi.Inset(
                    new Grid
                    {
                        ColumnDefinitions =
                            new ColumnDefinitions(
                                "300,120,160,90,100,105,105,105,220"),
                        ColumnSpacing = 10,
                        Children =
                        {
                            MediaUi.Cell(
                                row.Title,
                                0,
                                true),
                            MediaUi.Cell(
                                row.User,
                                1,
                                cssClass:
                                    "muted"),
                            MediaUi.Cell(
                                row.Player,
                                2),
                            MediaUi.Cell(
                                row.State,
                                3),
                            MediaUi.Cell(
                                row.Progress,
                                4),
                            MediaUi.Cell(
                                row.Video,
                                5),
                            MediaUi.Cell(
                                row.Audio,
                                6),
                            MediaUi.Cell(
                                row.Bandwidth,
                                7),
                            MediaUi.Cell(
                                row.Detail,
                                8,
                                cssClass:
                                    "muted")
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
                    UnifiedPlexAction.Refresh);

        _openButton.Click +=
            (_, _) =>
                RaiseAction(
                    UnifiedPlexAction.Open);

        _restartButton.Click +=
            (_, _) =>
                RaiseAction(
                    UnifiedPlexAction.Restart);

        _logsButton.Click +=
            (_, _) =>
                RaiseAction(
                    UnifiedPlexAction.Logs);

        _terminalButton.Click +=
            (_, _) =>
                RaiseAction(
                    UnifiedPlexAction.Terminal);

        _intelligenceButton.Click +=
            (_, _) =>
                RaiseAction(
                    UnifiedPlexAction.Intelligence);

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

        _saveButton.Click +=
            (_, _) =>
            {
                RaiseAction(
                    UnifiedPlexAction.SaveAndTest);

                CompleteConfigurationAction();
            };

        _clearButton.Click +=
            (_, _) =>
            {
                RaiseAction(
                    UnifiedPlexAction.ClearCredential);

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
        UnifiedPlexAction action)
    {
        ActionRequested?.Invoke(
            this,
            new UnifiedPlexActionEventArgs(
                action,
                new UnifiedSecretConfigurationRequest(
                    _endpointInput.Text ??
                    string.Empty,
                    string.Empty,
                    _secretInput.Text ??
                    string.Empty)));
    }
}
