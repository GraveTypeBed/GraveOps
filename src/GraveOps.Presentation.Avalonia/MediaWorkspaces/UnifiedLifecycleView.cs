using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace GraveOps.Presentation.Avalonia.MediaWorkspaces;

public sealed class UnifiedLifecycleView :
    UserControl
{
    private readonly TextBlock
        _activeText =
            MediaUi.MetricValue(
                "0");

    private readonly TextBlock
        _attentionText =
            MediaUi.MetricValue(
                "0");

    private readonly TextBlock
        _downloadingText =
            MediaUi.MetricValue(
                "0");

    private readonly TextBlock
        _importingText =
            MediaUi.MetricValue(
                "0");

    private readonly TextBlock
        _playingText =
            MediaUi.MetricValue(
                "0",
                18);

    private readonly TextBlock
        _summaryText =
            MediaUi.Dim(
                "Waiting for capture");

    private readonly Button
        _refreshButton =
            MediaUi.Primary(
                "Refresh lifecycle");

    private readonly WrapPanel
        _stagesPanel =
            new()
            {
                Orientation =
                    Orientation.Horizontal
            };

    private readonly StackPanel
        _itemsPanel =
            new()
            {
                Spacing = 0
            };

    private readonly Border
        _itemsEmpty =
            MediaUi.EmptyState(
                "No active lifecycle items",
                "Correlated media work will appear after source telemetry is available.");

    private readonly StackPanel
        _remediationPanel =
            new()
            {
                Spacing = 6
            };

    private readonly Border
        _remediationEmpty =
            MediaUi.EmptyState(
                "No guided remediation",
                "No upstream-first remediation steps are currently available.");

    private readonly Button
        _openOwnerButton =
            MediaUi.Compact(
                "Open selected step");

    private readonly Button
        _intelligenceButton =
            MediaUi.Compact(
                "Intelligence");

    private readonly TextBlock
        _selectedTitleText =
            MediaUi.Title(
                "No lifecycle item selected",
                14);

    private readonly TextBox
        _selectedDetailText =
            MediaUi.Console(
                string.Empty,
                88,
                220);

    private readonly TextBlock
        _sourceSummaryText =
            MediaUi.Muted(
                "No source telemetry captured.");

    private readonly TextBlock
        _statusText =
            MediaUi.Dim(
                "Waiting for lifecycle telemetry.");

    private UnifiedLifecycleState
        _state =
            UnifiedLifecycleState.Empty;

    private string
        _selectedItemKey =
            string.Empty;

    private string
        _selectedRemediationKey =
            string.Empty;

    public UnifiedLifecycleView()
    {
        BuildView();
        WireEvents();
        Update(
            UnifiedLifecycleState.Empty);
    }

    public event EventHandler<UnifiedLifecycleItemEventArgs>?
        ItemSelectionRequested;

    public event EventHandler<UnifiedRemediationEventArgs>?
        RemediationSelectionRequested;

    public event EventHandler<UnifiedLifecycleActionEventArgs>?
        ActionRequested;

    public void Update(
        UnifiedLifecycleState state)
    {
        _state =
            state ??
            UnifiedLifecycleState.Empty;

        _activeText.Text =
            _state.Active;

        _attentionText.Text =
            _state.Attention;

        _downloadingText.Text =
            _state.Downloading;

        _importingText.Text =
            _state.Importing;

        _playingText.Text =
            _state.Playing;

        _summaryText.Text =
            _state.Summary;

        _selectedTitleText.Text =
            _state.SelectedTitle;

        _selectedDetailText.Text =
            _state.SelectedDetail;

        _sourceSummaryText.Text =
            _state.SourceSummary;

        _statusText.Text =
            _state.Status;

        _refreshButton.IsEnabled =
            _state.CanRefresh;

        _openOwnerButton.IsEnabled =
            _state.CanOpenOwner;

        _intelligenceButton.IsEnabled =
            _state.CanOpenIntelligence;

        if (string.IsNullOrWhiteSpace(
                _selectedItemKey) ||
            !_state.Items.Any(row =>
                row.Key.Equals(
                    _selectedItemKey,
                    StringComparison.OrdinalIgnoreCase)))
        {
            _selectedItemKey =
                _state.Items
                    .FirstOrDefault()
                    ?.Key ??
                string.Empty;
        }

        if (string.IsNullOrWhiteSpace(
                _selectedRemediationKey) ||
            !_state.Remediation.Any(row =>
                row.Key.Equals(
                    _selectedRemediationKey,
                    StringComparison.OrdinalIgnoreCase)))
        {
            _selectedRemediationKey =
                _state.Remediation
                    .FirstOrDefault()
                    ?.Key ??
                string.Empty;
        }

        RenderStages();
        RenderItems();
        RenderRemediation();
    }

    private void BuildView()
    {
        var root =
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

        root.Children.Add(
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
                            MediaUi.PageTitle(
                                "Media Lifecycle"),
                            MediaUi.Subtitle(
                                "Track media from requests and acquisition through downloads, processing and library availability.")
                        }
                    },
                    Right(
                        _refreshButton)
                }
            });

        root.Children.Add(
            MediaUi.FourMetrics(
                MediaUi.Metric(
                    "ACTIVE ITEMS",
                    _activeText),
                MediaUi.Metric(
                    "NEEDS ATTENTION",
                    _attentionText),
                MediaUi.Metric(
                    "DOWNLOADING",
                    _downloadingText),
                MediaUi.Metric(
                    "IMPORT / RECONCILE",
                    _importingText)));

        root.Children.Add(
            MediaUi.Inset(
                new StackPanel
                {
                    Children =
                    {
                        MediaUi.Eyebrow(
                            "PLAYING / AVAILABLE"),
                        _playingText
                    }
                }));

        root.Children.Add(
            MediaUi.Module(
                new StackPanel
                {
                    Spacing = 6,
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
                                            "Workflow"),
                                        MediaUi.Subtitle(
                                            "Request -> Discovery -> Arr -> Download -> Import -> Processing -> Library")
                                    }
                                },
                                Right(
                                    _summaryText)
                            }
                        },
                        _stagesPanel
                    }
                }));

        root.Children.Add(
            MediaUi.TwoColumns(
                BuildItems(),
                BuildRemediation(),
                "1.25*,0.75*"));

        root.Children.Add(
            MediaUi.Module(
                new Grid
                {
                    ColumnDefinitions =
                        new ColumnDefinitions(
                            "180,*,Auto"),
                    ColumnSpacing = 8,
                    Children =
                    {
                        new StackPanel
                        {
                            Children =
                            {
                                _selectedTitleText,
                                MediaUi.Dim(
                                    "Selected item")
                            }
                        },
                        Column(
                            _selectedDetailText,
                            1),
                        Column(
                            _intelligenceButton,
                            2)
                    }
                }));

        root.Children.Add(
            MediaUi.Inset(
                new StackPanel
                {
                    Children =
                    {
                        _sourceSummaryText,
                        _statusText
                    }
                }));

        Content =
            MediaUi.Scroll(
                root);
    }

    private Border BuildItems()
    {
        var table =
            new StackPanel
            {
                MinWidth = 820,
                Children =
                {
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
                                        "1.25*,110,90,100,80,80"),
                                ColumnSpacing = 6,
                                Children =
                                {
                                    MediaUi.HeaderCell(
                                        "ITEM",
                                        0),
                                    MediaUi.HeaderCell(
                                        "OWNER",
                                        1),
                                    MediaUi.HeaderCell(
                                        "STAGE",
                                        2),
                                    MediaUi.HeaderCell(
                                        "STATE",
                                        3),
                                    MediaUi.HeaderCell(
                                        "PROGRESS",
                                        4),
                                    MediaUi.HeaderCell(
                                        "REMAINING",
                                        5)
                                }
                            }
                    },
                    _itemsPanel
                }
            };

        return
            MediaUi.Module(
                new StackPanel
                {
                    Spacing = 5,
                    Children =
                    {
                        MediaUi.Title(
                            "Active lifecycle items"),
                        MediaUi.Subtitle(
                            "Correlated Arr and downloader work. Select an item to open its owner."),
                        new Grid
                        {
                            Children =
                            {
                                MediaUi.Scroll(
                                    table,
                                    330,
                                    horizontal:
                                        true),
                                _itemsEmpty
                            }
                        }
                    }
                });
    }

    private Border BuildRemediation()
    {
        return
            MediaUi.Module(
                new StackPanel
                {
                    Spacing = 5,
                    Children =
                    {
                        MediaUi.Title(
                            "Guided remediation"),
                        MediaUi.Subtitle(
                            "Upstream-first next steps. GraveOps avoids downstream restarts when an earlier dependency explains the symptom."),
                        new Grid
                        {
                            Children =
                            {
                                MediaUi.Scroll(
                                    _remediationPanel,
                                    300),
                                _remediationEmpty
                            }
                        },
                        _openOwnerButton
                    }
                });
    }

    private void WireEvents()
    {
        _refreshButton.Click +=
            (_, _) =>
                ActionRequested?.Invoke(
                    this,
                    new UnifiedLifecycleActionEventArgs(
                        UnifiedLifecycleAction.Refresh));

        _openOwnerButton.Click +=
            (_, _) =>
                ActionRequested?.Invoke(
                    this,
                    new UnifiedLifecycleActionEventArgs(
                        UnifiedLifecycleAction.OpenOwner));

        _intelligenceButton.Click +=
            (_, _) =>
                ActionRequested?.Invoke(
                    this,
                    new UnifiedLifecycleActionEventArgs(
                        UnifiedLifecycleAction.Intelligence));
    }

    private void RenderStages()
    {
        _stagesPanel.Children.Clear();

        foreach (var row in
                 _state.Stages)
        {
            _stagesPanel.Children.Add(
                MediaUi.Inset(
                    new StackPanel
                    {
                        Width = 155,
                        Height = 60,
                        Spacing = 3,
                        Children =
                        {
                            MediaUi.Eyebrow(
                                row.Stage),
                            new TextBlock
                            {
                                Text =
                                    row.State,
                                FontWeight =
                                    FontWeight.SemiBold
                            },
                            MediaUi.Dim(
                                row.Evidence)
                        }
                    },
                    8));
        }
    }

    private void RenderItems()
    {
        _itemsPanel.Children.Clear();

        _itemsEmpty.IsVisible =
            _state.Items.Count == 0;

        foreach (var row in
                 _state.Items)
        {
            var button =
                MediaUi.RowButton(
                    MediaUi.Inset(
                        new Grid
                        {
                            ColumnDefinitions =
                                new ColumnDefinitions(
                                    "1.25*,110,90,100,80,80"),
                            ColumnSpacing = 6,
                            Children =
                            {
                                MediaUi.Cell(
                                    row.Item,
                                    0,
                                    true),
                                MediaUi.Cell(
                                    row.Owner,
                                    1),
                                MediaUi.Cell(
                                    row.Stage,
                                    2),
                                MediaUi.Cell(
                                    row.State,
                                    3),
                                MediaUi.Cell(
                                    row.Progress,
                                    4),
                                MediaUi.Cell(
                                    row.Remaining,
                                    5)
                            }
                        },
                        7));

            button.Classes.Set(
                "selected",
                row.Key.Equals(
                    _selectedItemKey,
                    StringComparison.OrdinalIgnoreCase));

            button.Click +=
                (_, _) =>
                {
                    _selectedItemKey =
                        row.Key;

                    _selectedTitleText.Text =
                        row.Item;

                    _selectedDetailText.Text =
                        string.Join(
                            Environment.NewLine,
                            new[]
                            {
                                row.Evidence,
                                row.MediaType,
                                row.Confidence
                            }.Where(value =>
                                !string.IsNullOrWhiteSpace(
                                    value)));

                    RenderItems();

                    ItemSelectionRequested?.Invoke(
                        this,
                        new UnifiedLifecycleItemEventArgs(
                            row));
                };

            _itemsPanel.Children.Add(
                button);
        }
    }

    private void RenderRemediation()
    {
        _remediationPanel.Children.Clear();

        _remediationEmpty.IsVisible =
            _state.Remediation.Count == 0;

        foreach (var row in
                 _state.Remediation)
        {
            var button =
                MediaUi.RowButton(
                    MediaUi.Inset(
                        new StackPanel
                        {
                            Spacing = 3,
                            Children =
                            {
                                new Grid
                                {
                                    ColumnDefinitions =
                                        new ColumnDefinitions(
                                            "32,*,Auto"),
                                    Children =
                                    {
                                        MediaUi.Cell(
                                            row.Step,
                                            0),
                                        MediaUi.Cell(
                                            row.Component,
                                            1,
                                            true),
                                        MediaUi.Cell(
                                            row.Severity,
                                            2)
                                    }
                                },
                                MediaUi.Muted(
                                    row.Why),
                                MediaUi.Dim(
                                    row.NextStep)
                            }
                        },
                        8));

            button.Classes.Set(
                "selected",
                row.Key.Equals(
                    _selectedRemediationKey,
                    StringComparison.OrdinalIgnoreCase));

            button.Click +=
                (_, _) =>
                {
                    _selectedRemediationKey =
                        row.Key;

                    RenderRemediation();

                    RemediationSelectionRequested?.Invoke(
                        this,
                        new UnifiedRemediationEventArgs(
                            row));
                };

            _remediationPanel.Children.Add(
                button);
        }
    }

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

    private static Control Column(
        Control control,
        int column)
    {
        Grid.SetColumn(
            control,
            column);

        control.VerticalAlignment =
            VerticalAlignment.Center;

        return control;
    }
}
