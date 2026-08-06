using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;

namespace GraveOps.Presentation.Avalonia.Dashboard;

public sealed class UnifiedDashboardView :
    UserControl
{
    private readonly TextBlock _statusText;
    private readonly Border _attentionStrip;
    private readonly TextBlock _attentionDot;
    private readonly TextBlock _attentionTitle;
    private readonly TextBlock _attentionDetail;
    private readonly StackPanel _cardsPanel;
    private readonly Border _customizer;
    private readonly StackPanel _pickerPanel;

    private UnifiedDashboardState _state =
        UnifiedDashboardState.Waiting;

    private List<DashboardCardPreference> _layout =
        new();

    public UnifiedDashboardView()
    {
        HorizontalAlignment =
            HorizontalAlignment.Stretch;
        VerticalAlignment =
            VerticalAlignment.Stretch;

        _statusText =
            new TextBlock
            {
                Classes =
                {
                    "dim"
                },
                Width = 120,
                HorizontalAlignment =
                    HorizontalAlignment.Right,
                VerticalAlignment =
                    VerticalAlignment.Center,
                TextTrimming =
                    TextTrimming.CharacterEllipsis
            };

        var customizeButton =
            new Button
            {
                Content = "Customize cards",
                Classes =
                {
                    "compact"
                }
            };
        customizeButton.Click +=
            (_, _) =>
                OpenCustomizer();

        var refreshButton =
            new Button
            {
                Content = "\u21BB",
                Width = 30,
                Height = 28,
                MinWidth = 30,
                MinHeight = 28,
                Padding = new Thickness(0),
                FontSize = 16,
                Classes =
                {
                    "compact"
                }
            };
        ToolTip.SetTip(
            refreshButton,
            "Refresh host now");
        refreshButton.Click +=
            (_, _) =>
                RefreshRequested?.Invoke(
                    this,
                    EventArgs.Empty);

        var headerActions =
            new StackPanel
            {
                Orientation =
                    Orientation.Horizontal,
                Spacing = 8,
                VerticalAlignment =
                    VerticalAlignment.Center
            };
        headerActions.Children.Add(
            _statusText);
        headerActions.Children.Add(
            customizeButton);
        headerActions.Children.Add(
            refreshButton);

        var header =
            new Grid
            {
                ColumnDefinitions =
                    new ColumnDefinitions(
                        "*,Auto"),
                ColumnSpacing = 10
            };
        header.Children.Add(
            new TextBlock
            {
                Text =
                    "Operational dashboard",
                Classes =
                {
                    "pageTitle"
                },
                VerticalAlignment =
                    VerticalAlignment.Center
            });

        Grid.SetColumn(
            headerActions,
            1);
        header.Children.Add(
            headerActions);

        _attentionDot =
            new TextBlock
            {
                Text = "\u25CF",
                FontSize = 9,
                VerticalAlignment =
                    VerticalAlignment.Center
            };

        _attentionTitle =
            new TextBlock
            {
                FontWeight =
                    FontWeight.SemiBold,
                VerticalAlignment =
                    VerticalAlignment.Center
            };

        _attentionDetail =
            new TextBlock
            {
                Classes =
                {
                    "muted"
                },
                FontSize = 10,
                VerticalAlignment =
                    VerticalAlignment.Center,
                TextWrapping =
                    TextWrapping.NoWrap,
                TextTrimming =
                    TextTrimming.CharacterEllipsis
            };

        var findingsButton =
            new Button
            {
                Content = "Findings",
                MinHeight = 28,
                Padding =
                    new Thickness(
                        10,
                        4),
                Classes =
                {
                    "compact"
                }
            };
        findingsButton.Click +=
            (_, _) =>
                RaiseAction(
                    new UnifiedDashboardAction(
                        "Findings",
                        "IntelligenceNav",
                        IsPrimary: true));

        var attentionGrid =
            new Grid
            {
                ColumnDefinitions =
                    new ColumnDefinitions(
                        "Auto,Auto,*,Auto"),
                ColumnSpacing = 8
            };
        attentionGrid.Children.Add(
            _attentionDot);

        Grid.SetColumn(
            _attentionTitle,
            1);
        attentionGrid.Children.Add(
            _attentionTitle);

        Grid.SetColumn(
            _attentionDetail,
            2);
        attentionGrid.Children.Add(
            _attentionDetail);

        Grid.SetColumn(
            findingsButton,
            3);
        attentionGrid.Children.Add(
            findingsButton);

        _attentionStrip =
            new Border
            {
                MinHeight = 38,
                Padding =
                    new Thickness(
                        10,
                        6),
                Classes =
                {
                    "unifiedAttentionStrip",
                    "healthy"
                },
                Child =
                    attentionGrid
            };

        _cardsPanel =
            new StackPanel
            {
                Spacing = 12,
                HorizontalAlignment =
                    HorizontalAlignment.Stretch
            };


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
            header);
        content.Children.Add(
            _attentionStrip);
        content.Children.Add(
            _cardsPanel);

        var scroll =
            new ScrollViewer
            {
                VerticalScrollBarVisibility =
                    global::Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility =
                    global::Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled,
                Content =
                    content
            };

        _pickerPanel =
            new StackPanel
            {
                Margin =
                    new Thickness(
                        0,
                        0,
                        4,
                        0)
            };

        var closeButton =
            new Button
            {
                Content = "Close",
                Classes =
                {
                    "compact"
                }
            };
        closeButton.Click +=
            (_, _) =>
                CloseCustomizer();

        var customizerHeading =
            new Grid
            {
                ColumnDefinitions =
                    new ColumnDefinitions(
                        "*,Auto")
            };
        customizerHeading.Children.Add(
            new StackPanel
            {
                Children =
                {
                    new TextBlock
                    {
                        Text =
                            "Customize Dashboard",
                        Classes =
                        {
                            "sectionTitle"
                        }
                    },
                    new TextBlock
                    {
                        Text =
                            "Choose and reorder cards for the active host. Newly detected providers remain available here.",
                        Classes =
                        {
                            "pageSubtitle"
                        },
                        Margin =
                            new Thickness(
                                0,
                                3,
                                0,
                                0),
                        TextWrapping =
                            TextWrapping.Wrap
                    }
                }
            });
        Grid.SetColumn(
            closeButton,
            1);
        customizerHeading.Children.Add(
            closeButton);

        var saveButton =
            new Button
            {
                Content = "Save layout",
                Margin =
                    new Thickness(
                        0,
                        0,
                        8,
                        0),
                Classes =
                {
                    "primary",
                    "compact"
                }
            };
        saveButton.Click +=
            (_, _) =>
            {
                NormalizeWorkingOrder();

                LayoutChanged?.Invoke(
                    this,
                    new DashboardLayoutChangedEventArgs(
                        _state.HostKey,
                        _layout.ToArray()));

                CloseCustomizer();

                RenderCards();
            };

        var resetButton =
            new Button
            {
                Content =
                    "Reset recommended",
                Classes =
                {
                    "compact"
                }
            };
        resetButton.Click +=
            (_, _) =>
            {
                _layout =
                    DashboardLayoutResolver.Resolve(
                            _state.Cards,
                            Array.Empty<
                                DashboardCardPreference>())
                        .ToList();

                PopulatePicker();
                RenderCards();
            };

        var customizerActions =
            new WrapPanel();
        customizerActions.Children.Add(
            saveButton);
        customizerActions.Children.Add(
            resetButton);

        var customizerGrid =
            new Grid
            {
                RowDefinitions =
                    new RowDefinitions(
                        "Auto,*,Auto"),
                RowSpacing = 10
            };
        customizerGrid.Children.Add(
            customizerHeading);

        var pickerScroll =
            new ScrollViewer
            {
                VerticalScrollBarVisibility =
                    global::Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility =
                    global::Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled,
                Content =
                    _pickerPanel
            };
        Grid.SetRow(
            pickerScroll,
            1);
        customizerGrid.Children.Add(
            pickerScroll);

        Grid.SetRow(
            customizerActions,
            2);
        customizerGrid.Children.Add(
            customizerActions);

        _customizer =
            new Border
            {
                Width = 410,
                HorizontalAlignment =
                    HorizontalAlignment.Right,
                VerticalAlignment =
                    VerticalAlignment.Stretch,
                Padding =
                    new Thickness(16),
                BorderThickness =
                    new Thickness(
                        1,
                        0,
                        0,
                        0),
                ZIndex = 20,
                IsVisible = false,
                Classes =
                {
                    "module"
                },
                Child =
                    customizerGrid
            };

        var root =
            new Grid();
        root.Children.Add(
            scroll);
        root.Children.Add(
            _customizer);

        Content =
            root;

        SizeChanged +=
            (_, _) =>
                RenderCards();

        Update(
            UnifiedDashboardState.Waiting);
    }

    public event EventHandler?
        RefreshRequested;

    public event EventHandler<
        DashboardActionRequestedEventArgs>?
        ActionRequested;

    public event EventHandler<
        DashboardLayoutChangedEventArgs>?
        LayoutChanged;

    public void Update(
        UnifiedDashboardState state)
    {
        ArgumentNullException.ThrowIfNull(
            state);

        _state =
            state;

        _layout =
            DashboardLayoutResolver.Resolve(
                    state.Cards,
                    state.Layout)
                .ToList();

        _statusText.Text =
            state.StatusText;

        _attentionTitle.Text =
            state.AttentionTitle;

        _attentionDetail.Text =
            state.AttentionDetail;

        _attentionStrip.Classes.Set(
            "healthy",
            state.IsHealthy);

        _attentionStrip.Classes.Set(
            "attention",
            !state.IsHealthy);

        _attentionDot.Foreground =
            ResourceBrush(
                state.IsHealthy
                    ? "SuccessBrush"
                    : "WarnBrush",
                state.IsHealthy
                    ? Brushes.LimeGreen
                    : Brushes.Goldenrod);

        RenderCards();

        if (_customizer.IsVisible)
            PopulatePicker();
    }

    private void OpenCustomizer()
    {
        PopulatePicker();

        _customizer.IsVisible =
            true;
    }

    private void CloseCustomizer() =>
        _customizer.IsVisible =
            false;

    private void PopulatePicker()
    {
        _pickerPanel.Children.Clear();

        foreach (var preference in
                 _layout
                     .OrderBy(item =>
                         item.Order))
        {
            var card =
                _state.Cards.FirstOrDefault(
                    item =>
                        item.Key.Equals(
                            preference.Key,
                            StringComparison.OrdinalIgnoreCase));

            if (card is null)
                continue;

            var check =
                new CheckBox
                {
                    Content =
                        card.Title,
                    IsChecked =
                        preference.IsVisible,
                    VerticalAlignment =
                        VerticalAlignment.Center
                };

            check.Click +=
                (_, _) =>
                {
                    var index =
                        _layout.FindIndex(item =>
                            item.Key.Equals(
                                preference.Key,
                                StringComparison.OrdinalIgnoreCase));

                    if (index < 0)
                        return;

                    _layout[index] =
                        _layout[index] with
                        {
                            IsVisible =
                                check.IsChecked !=
                                false
                        };
                };

            var up =
                new Button
                {
                    Content = "\u2191",
                    Tag =
                        preference.Key,
                    Width = 30,
                    Padding =
                        new Thickness(0),
                    Classes =
                    {
                        "compact"
                    }
                };
            up.Click +=
                PickerMoveUp;

            var down =
                new Button
                {
                    Content = "\u2193",
                    Tag =
                        preference.Key,
                    Width = 30,
                    Padding =
                        new Thickness(0),
                    Classes =
                    {
                        "compact"
                    }
                };
            down.Click +=
                PickerMoveDown;

            var row =
                new Grid
                {
                    ColumnDefinitions =
                        new ColumnDefinitions(
                            "*,Auto,Auto"),
                    ColumnSpacing = 6,
                    Margin =
                        new Thickness(
                            0,
                            0,
                            0,
                            7)
                };
            row.Children.Add(
                check);

            Grid.SetColumn(
                up,
                1);
            row.Children.Add(
                up);

            Grid.SetColumn(
                down,
                2);
            row.Children.Add(
                down);

            _pickerPanel.Children.Add(
                row);
        }
    }

    private void PickerMoveUp(
        object? sender,
        RoutedEventArgs e)
    {
        if (sender is Button
            {
                Tag: string key
            })
        {
            MovePreference(
                key,
                -1);
        }
    }

    private void PickerMoveDown(
        object? sender,
        RoutedEventArgs e)
    {
        if (sender is Button
            {
                Tag: string key
            })
        {
            MovePreference(
                key,
                1);
        }
    }

    private void MovePreference(
        string key,
        int direction)
    {
        NormalizeWorkingOrder();

        var current =
            _layout.FindIndex(item =>
                item.Key.Equals(
                    key,
                    StringComparison.OrdinalIgnoreCase));

        var destination =
            current +
            direction;

        if (current < 0 ||
            destination < 0 ||
            destination >=
            _layout.Count)
        {
            return;
        }

        var temporary =
            _layout[current];

        _layout[current] =
            _layout[destination] with
            {
                Order =
                    current
            };

        _layout[destination] =
            temporary with
            {
                Order =
                    destination
            };

        PopulatePicker();
    }

    private void NormalizeWorkingOrder()
    {
        _layout =
            _layout
                .OrderBy(item =>
                    item.Order)
                .Select(
                    (item, index) =>
                        item with
                        {
                            Order =
                                index
                        })
                .ToList();
    }

    private void RenderCards()
    {
        if (_cardsPanel is null)
            return;

        _cardsPanel.Children.Clear();

        var visible =
            DashboardLayoutResolver.VisibleCards(
                _state.Cards,
                _layout);

        if (visible.Count == 0)
        {
            _cardsPanel.Children.Add(
                new Border
                {
                    Classes =
                    {
                        "emptyState"
                    },
                    Child =
                        new TextBlock
                        {
                            Text =
                                "No Dashboard cards are visible. Open Customize cards to restore modules.",
                            TextWrapping =
                                TextWrapping.Wrap
                        }
                });
            return;
        }

        var sections =
            new[]
            {
                new DashboardSection(
                    "Infrastructure",
                    "infrastructure",
                    false),
                new DashboardSection(
                    "Operations",
                    "operations",
                    false),
                new DashboardSection(
                    "Media",
                    "media",
                    false),
                new DashboardSection(
                    "Applications",
                    "applications",
                    true)
            };

        var available =
            Bounds.Width > 720
                ? Bounds.Width
                : 900;

        foreach (var section in
                 sections)
        {
            var cards =
                visible
                    .Where(card =>
                        SectionKey(card)
                            .Equals(
                                section.Key,
                                StringComparison.Ordinal))
                    .ToArray();

            if (cards.Length == 0)
                continue;

            _cardsPanel.Children.Add(
                BuildSection(
                    section,
                    cards,
                    available));
        }
    }

    private Control BuildSection(
        DashboardSection section,
        IReadOnlyList<
            UnifiedDashboardCard> cards,
        double available)
    {
        var container =
            new StackPanel
            {
                Spacing = 8
            };

        container.Children.Add(
            new TextBlock
            {
                Text =
                    section.Title,
                FontSize = 13,
                FontWeight =
                    FontWeight.SemiBold,
                Margin =
                    new Thickness(
                        0,
                        section.ApplicationTiles
                            ? 3
                            : 0,
                        0,
                        0)
            });

        var columns =
            ResolveColumns(
                cards.Count,
                available);

        var cardWidth =
            ResolveCardWidth(
                columns,
                available);

        var rows =
            new StackPanel
            {
                Spacing = 9,
                HorizontalAlignment =
                    HorizontalAlignment.Stretch
            };

        var pending =
            new List<
                UnifiedDashboardCard>();

        void Flush()
        {
            if (pending.Count == 0)
                return;

            var row =
                new Grid
                {
                    ColumnDefinitions =
                        new ColumnDefinitions(
                            string.Join(
                                ",",
                                Enumerable.Repeat(
                                    "*",
                                    columns))),
                    ColumnSpacing = 9,
                    HorizontalAlignment =
                        HorizontalAlignment.Stretch
                };

            for (var index = 0;
                 index < pending.Count;
                 index++)
            {
                var control =
                    BuildCard(
                        pending[index],
                        cardWidth,
                        section.ApplicationTiles);

                Grid.SetColumn(
                    control,
                    index);

                row.Children.Add(
                    control);
            }

            rows.Children.Add(
                row);

            pending.Clear();
        }

        foreach (var card in cards)
        {
            var fullWidth =
                card.Key.Equals(
                    "core:health",
                    StringComparison.OrdinalIgnoreCase);

            if (fullWidth)
            {
                Flush();

                var fullRow =
                    new Grid
                    {
                        ColumnDefinitions =
                            new ColumnDefinitions(
                                "*"),
                        HorizontalAlignment =
                            HorizontalAlignment.Stretch
                    };

                fullRow.Children.Add(
                    BuildCard(
                        card,
                        Math.Max(
                            292,
                            available),
                        section.ApplicationTiles));

                rows.Children.Add(
                    fullRow);

                continue;
            }

            pending.Add(
                card);

            if (pending.Count ==
                columns)
            {
                Flush();
            }
        }

        Flush();

        container.Children.Add(
            rows);

        return container;
    }

    private Border BuildCard(
        UnifiedDashboardCard card,
        double width,
        bool applicationTile)
    {
        var compact =
            !_state.Density.Equals(
                "Comfortable",
                StringComparison.OrdinalIgnoreCase);

        var kind =
            CardKind(
                card,
                applicationTile);

        var sourceRows =
            card.Rows
                .Where(row =>
                    !string.IsNullOrWhiteSpace(
                        row.Label) ||
                    !string.IsNullOrWhiteSpace(
                        row.Value) ||
                    !string.IsNullOrWhiteSpace(
                        row.SecondaryValue))
                .ToArray();

        if (sourceRows.Length == 0)
        {
            sourceRows =
                card.Facts
                    .Where(value =>
                        !string.IsNullOrWhiteSpace(
                            value))
                    .Distinct(
                        StringComparer.OrdinalIgnoreCase)
                    .Select(value =>
                        new UnifiedDashboardRow(
                            value,
                            string.Empty,
                            value))
                    .ToArray();
        }

        var visibleRows =
            sourceRows.Length <= 4
                ? sourceRows
                : sourceRows.Take(3).ToArray();

        var hiddenRows =
            Math.Max(
                0,
                sourceRows.Length -
                visibleRows.Length);

        var border =
            new Border
            {
                MinHeight =
                    applicationTile
                        ? compact
                            ? 166
                            : 174
                        : compact
                            ? 184
                            : 194,
                HorizontalAlignment =
                    HorizontalAlignment.Stretch,
                VerticalAlignment =
                    VerticalAlignment.Stretch,
                Margin =
                    new Thickness(0),
                ClipToBounds =
                    false,
                Classes =
                {
                    "dashboardProviderCard",
                    "dashboardCardShell"
                }
            };

        var grid =
            new Grid
            {
                RowDefinitions =
                    new RowDefinitions(
                        "Auto,Auto,Auto,*,Auto"),
                RowSpacing =
                    applicationTile
                        ? 5
                        : 6,
                HorizontalAlignment =
                    HorizontalAlignment.Stretch,
                VerticalAlignment =
                    VerticalAlignment.Stretch
            };

        var header =
            new Grid
            {
                ColumnDefinitions =
                    new ColumnDefinitions(
                        "*,Auto"),
                ColumnSpacing = 8,
                Classes =
                {
                    "dashboardCardHeader"
                }
            };

        var heading =
            new StackPanel
            {
                Spacing = 1
            };
        heading.Children.Add(
            new TextBlock
            {
                Text =
                    card.Title,
                FontSize =
                    applicationTile
                        ? 13.5
                        : 15,
                FontWeight =
                    FontWeight.SemiBold,
                TextWrapping =
                    TextWrapping.Wrap
            });
        heading.Children.Add(
            new TextBlock
            {
                Text =
                    card.Category
                        .ToUpperInvariant(),
                Classes =
                {
                    "eyebrow"
                },
                FontSize = 8.5,
                TextWrapping =
                    TextWrapping.Wrap
            });
        header.Children.Add(
            heading);

        var badge =
            new Border
            {
                Classes =
                {
                    "badge"
                },
                Background =
                    SeverityBackground(
                        card.Severity),
                HorizontalAlignment =
                    HorizontalAlignment.Right,
                VerticalAlignment =
                    VerticalAlignment.Top,
                Child =
                    new TextBlock
                    {
                        Text =
                            card.Status,
                        Foreground =
                            SeverityForeground(
                                card.Severity),
                        FontSize = 8.5,
                        FontWeight =
                            FontWeight.SemiBold,
                        TextWrapping =
                            TextWrapping.Wrap,
                        TextAlignment =
                            TextAlignment.Center,
                        MaxWidth = 112
                    }
            };
        Grid.SetColumn(
            badge,
            1);
        header.Children.Add(
            badge);
        grid.Children.Add(
            header);

        var primary =
            new TextBlock
            {
                Text =
                    card.PrimaryValue,
                FontSize =
                    applicationTile
                        ? 20
                        : compact
                            ? 22
                            : 25,
                FontWeight =
                    FontWeight.SemiBold,
                TextWrapping =
                    TextWrapping.Wrap
            };
        Grid.SetRow(
            primary,
            1);
        grid.Children.Add(
            primary);

        var summary =
            new TextBlock
            {
                Text =
                    card.Summary,
                IsVisible =
                    !string.IsNullOrWhiteSpace(
                        card.Summary),
                Classes =
                {
                    "muted",
                    "dashboardCardSummary"
                },
                FontSize =
                    applicationTile
                        ? 9
                        : 10,
                TextWrapping =
                    TextWrapping.Wrap
            };
        ToolTip.SetTip(
            summary,
            card.Detail);
        Grid.SetRow(
            summary,
            2);
        grid.Children.Add(
            summary);

        var body =
            new StackPanel
            {
                Spacing =
                    kind.Equals(
                        "progress",
                        StringComparison.Ordinal)
                        ? 5
                        : 3,
                VerticalAlignment =
                    VerticalAlignment.Top,
                HorizontalAlignment =
                    HorizontalAlignment.Stretch,
                ClipToBounds =
                    false,
                Classes =
                {
                    "dashboardCardBody"
                }
            };

        foreach (var row in
                 visibleRows)
        {
            body.Children.Add(
                BuildRow(
                    row,
                    kind));
        }

        if (hiddenRows > 0)
        {
            var overflow =
                new Grid
                {
                    ColumnDefinitions =
                        new ColumnDefinitions(
                            "*,Auto"),
                    ColumnSpacing = 8,
                    MinHeight = 16,
                    Classes =
                    {
                        "dashboardOverflowRow"
                    }
                };
            overflow.Children.Add(
                new TextBlock
                {
                    Text =
                        $"+{hiddenRows} more",
                    Classes =
                    {
                        "dim"
                    },
                    FontSize = 9,
                    FontWeight =
                        FontWeight.SemiBold
                });

            var disclosure =
                new TextBlock
                {
                    Text =
                        "Open details",
                    Classes =
                    {
                        "dim"
                    },
                    FontSize = 8.5,
                    HorizontalAlignment =
                        HorizontalAlignment.Right
                };
            Grid.SetColumn(
                disclosure,
                1);
            overflow.Children.Add(
                disclosure);

            body.Children.Add(
                overflow);
        }

        Grid.SetRow(
            body,
            3);
        grid.Children.Add(
            body);

        var footer =
            new Grid
            {
                ColumnDefinitions =
                    new ColumnDefinitions(
                        "*,Auto"),
                ColumnSpacing = 8,
                Margin =
                    new Thickness(
                        0,
                        6,
                        0,
                        0),
                VerticalAlignment =
                    VerticalAlignment.Bottom,
                Classes =
                {
                    "dashboardCardFooter"
                }
            };

        var actions =
            ResolveActions(
                card);

        var primaryAction =
            actions.FirstOrDefault(
                action =>
                    action.IsPrimary) ??
            actions.FirstOrDefault();

        if (primaryAction is not null)
        {
            var actionButton =
                new Button
                {
                    Content =
                        new TextBlock
                        {
                            Text =
                                primaryAction.Label,
                            TextWrapping =
                                TextWrapping.Wrap,
                            TextAlignment =
                                TextAlignment.Center
                        },
                    Tag =
                        primaryAction,
                    HorizontalAlignment =
                        HorizontalAlignment.Left,
                    Classes =
                    {
                        "compact",
                        "primary"
                    }
                };
            actionButton.Click +=
                ActionButtonOnClick;
            footer.Children.Add(
                actionButton);
        }

        var infoButton =
            new Button
            {
                Content =
                    BuildDashboardInfoIcon(),
                Classes =
                {
                    "dashboardInfoButton"
                },
                Flyout =
                    BuildInfoFlyout(
                        card,
                        width)
            };
        global::Avalonia.Automation.AutomationProperties.SetName(
            infoButton,
            $"{card.Title} details");
        Grid.SetColumn(
            infoButton,
            1);
        footer.Children.Add(
            infoButton);

        Grid.SetRow(
            footer,
            4);
        grid.Children.Add(
            footer);

        border.Child =
            grid;

        return border;
    }

    private Control BuildRow(
        UnifiedDashboardRow row,
        string kind)
    {
        var rowPanel =
            new StackPanel
            {
                Spacing = 2,
                HorizontalAlignment =
                    HorizontalAlignment.Stretch,
                ClipToBounds =
                    false,
                Classes =
                {
                    "dashboardPreviewRow"
                }
            };

        var hasSecondary =
            !string.IsNullOrWhiteSpace(
                row.SecondaryValue);

        var line =
            new Grid
            {
                ColumnDefinitions =
                    new ColumnDefinitions(
                        hasSecondary
                            ? "Auto,1.4*,0.55*,0.8*"
                            : "Auto,1.5*,1*"),
                ColumnSpacing = 6,
                MinHeight = 16,
                HorizontalAlignment =
                    HorizontalAlignment.Stretch
            };

        line.Children.Add(
            new TextBlock
            {
                Text = "\u25CF",
                FontSize = 7,
                Foreground =
                    SeverityForeground(
                        row.Severity ==
                        DashboardSeverity.Healthy
                            ? DashboardSeverity.Info
                            : row.Severity),
                VerticalAlignment =
                    VerticalAlignment.Center
            });

        var labelText =
            kind.Equals(
                    "timeline",
                    StringComparison.Ordinal) &&
                !string.IsNullOrWhiteSpace(
                    row.Detail)
                ? row.Detail
                    .Split(
                        " \u00B7 ",
                        2,
                        StringSplitOptions.TrimEntries)[0]
                : row.Label;

        var label =
            new TextBlock
            {
                Text =
                    labelText,
                Classes =
                {
                    "dim"
                },
                FontSize = 9,
                TextWrapping =
                    TextWrapping.Wrap,
                VerticalAlignment =
                    VerticalAlignment.Center
            };
        Grid.SetColumn(
            label,
            1);
        line.Children.Add(
            label);

        var value =
            new TextBlock
            {
                Text =
                    row.Value,
                FontSize = 9,
                FontWeight =
                    FontWeight.SemiBold,
                HorizontalAlignment =
                    HorizontalAlignment.Stretch,
                VerticalAlignment =
                    VerticalAlignment.Center,
                TextAlignment =
                    TextAlignment.Right,
                TextWrapping =
                    TextWrapping.Wrap
            };
        Grid.SetColumn(
            value,
            2);
        line.Children.Add(
            value);

        if (hasSecondary)
        {
            var secondary =
                new TextBlock
                {
                    Text =
                        row.SecondaryValue,
                    Classes =
                    {
                        "dim"
                    },
                    FontSize = 8.75,
                    FontWeight =
                        FontWeight.SemiBold,
                    HorizontalAlignment =
                        HorizontalAlignment.Stretch,
                    VerticalAlignment =
                        VerticalAlignment.Center,
                    TextAlignment =
                        TextAlignment.Right,
                    TextWrapping =
                        TextWrapping.Wrap
                };
            Grid.SetColumn(
                secondary,
                3);
            line.Children.Add(
                secondary);
        }

        rowPanel.Children.Add(
            line);

        if (kind.Equals(
                "progress",
                StringComparison.Ordinal) &&
            TryProgress(
                row.Value,
                out var progress))
        {
            rowPanel.Children.Add(
                new ProgressBar
                {
                    Minimum = 0,
                    Maximum = 100,
                    Value =
                        progress,
                    Height = 5,
                    IsHitTestVisible =
                        false,
                    Foreground =
                        SeverityForeground(
                            row.Severity ==
                            DashboardSeverity.Healthy
                                ? DashboardSeverity.Info
                                : row.Severity)
                });
        }

        ToolTip.SetTip(
            rowPanel,
            string.IsNullOrWhiteSpace(
                row.Detail)
                ? string.Join(
                    " \u00B7 ",
                    new[]
                    {
                        row.Label,
                        row.Value,
                        row.SecondaryValue
                    }.Where(value =>
                        !string.IsNullOrWhiteSpace(
                            value)))
                : row.Detail);

        return rowPanel;
    }

    private static PathIcon BuildDashboardInfoIcon() =>
        new()
        {
            Width = 16,
            Height = 16,
            HorizontalAlignment =
                HorizontalAlignment.Center,
            VerticalAlignment =
                VerticalAlignment.Center,
            Data =
                StreamGeometry.Parse(
                    "M8.49902 7.49998C8.49902 7.22384 8.27517 6.99998 7.99902 6.99998C7.72288 6.99998 7.49902 7.22384 7.49902 7.49998V10.5C7.49902 10.7761 7.72288 11 7.99902 11C8.27517 11 8.49902 10.7761 8.49902 10.5V7.49998ZM8.74807 5.50001C8.74807 5.91369 8.41271 6.24905 7.99903 6.24905C7.58535 6.24905 7.25 5.91369 7.25 5.50001C7.25 5.08633 7.58535 4.75098 7.99903 4.75098C8.41271 4.75098 8.74807 5.08633 8.74807 5.50001ZM8 1C4.13401 1 1 4.13401 1 8C1 11.866 4.13401 15 8 15C11.866 15 15 11.866 15 8C15 4.13401 11.866 1 8 1ZM2 8C2 4.68629 4.68629 2 8 2C11.3137 2 14 4.68629 14 8C14 11.3137 11.3137 14 8 14C4.68629 14 2 11.3137 2 8Z")
        };

    private Flyout BuildInfoFlyout(
        UnifiedDashboardCard card,
        double width)
    {
        var details =
            new StackPanel
            {
                Spacing = 7,
                MaxWidth =
                    Math.Clamp(
                        width - 40,
                        260,
                        520)
            };

        details.Children.Add(
            new TextBlock
            {
                Text =
                    card.Title,
                FontSize = 16,
                FontWeight =
                    FontWeight.SemiBold
            });

        details.Children.Add(
            new TextBlock
            {
                Text =
                    card.Detail,
                Classes =
                {
                    "muted"
                },
                TextWrapping =
                    TextWrapping.Wrap
            });

        foreach (var row in
                 card.Rows)
        {
            details.Children.Add(
                new TextBlock
                {
                    Text =
                        string.Join(
                            " \u00B7 ",
                            new[]
                            {
                                row.Label,
                                row.Value,
                                row.SecondaryValue
                            }.Where(value =>
                                !string.IsNullOrWhiteSpace(
                                    value))),
                    TextWrapping =
                        TextWrapping.Wrap
                });
        }

        if (card.Rows.Count == 0)
        {
            foreach (var fact in
                     card.Facts
                         .Where(value =>
                             !string.IsNullOrWhiteSpace(
                                 value))
                         .Distinct(
                             StringComparer.OrdinalIgnoreCase))
            {
                details.Children.Add(
                    new TextBlock
                    {
                        Text =
                            fact,
                        TextWrapping =
                            TextWrapping.Wrap
                    });
            }
        }

        var actions =
            ResolveActions(
                card);

        if (actions.Count > 0)
        {
            var actionPanel =
                new WrapPanel
                {
                    Margin =
                        new Thickness(
                            0,
                            4,
                            0,
                            0)
                };

            foreach (var action in actions)
            {
                var button =
                    new Button
                    {
                        Content =
                            action.Label,
                        Tag =
                            action,
                        Margin =
                            new Thickness(
                                0,
                                0,
                                6,
                                6),
                        Classes =
                        {
                            "compact"
                        }
                    };

                if (action.IsPrimary)
                {
                    button.Classes.Add(
                        "primary");
                }

                button.Click +=
                    ActionButtonOnClick;

                actionPanel.Children.Add(
                    button);
            }

            details.Children.Add(
                actionPanel);
        }

        var flyout =
            new Flyout
            {
                Placement =
                    PlacementMode.TopEdgeAlignedRight,
                ShowMode =
                    FlyoutShowMode.Standard,
                VerticalOffset =
                    -4,
                Content =
                    new Border
                    {
                        Padding =
                            new Thickness(14),
                        Classes =
                        {
                            "module"
                        },
                        Child =
                            details
                    }
            };

        flyout.FlyoutPresenterClasses.Add(
            "dashboardInfoFlyout");

        return flyout;
    }

    private void ActionButtonOnClick(
        object? sender,
        RoutedEventArgs e)
    {
        if (sender is Button
            {
                Tag:
                    UnifiedDashboardAction action
            })
        {
            RaiseAction(
                action);
        }
    }

    private void RaiseAction(
        UnifiedDashboardAction action) =>
        ActionRequested?.Invoke(
            this,
            new DashboardActionRequestedEventArgs(
                action));

    private IReadOnlyList<
        UnifiedDashboardAction> ResolveActions(
        UnifiedDashboardCard card)
    {
        var actions =
            card.Actions.Count > 0
                ? card.Actions
                : new[]
                {
                    new UnifiedDashboardAction(
                        card.ActionLabel,
                        card.NavigationName,
                        card.Endpoint,
                        IsPrimary: true)
                };

        return actions
            .Where(action =>
                !string.IsNullOrWhiteSpace(
                    action.Label) &&
                (!string.IsNullOrWhiteSpace(
                     action.NavigationName) ||
                 !string.IsNullOrWhiteSpace(
                     action.Endpoint)))
            .GroupBy(
                action =>
                    !string.IsNullOrWhiteSpace(
                        action.Endpoint)
                        ? $"endpoint:{action.Endpoint}"
                        : $"navigation:{action.NavigationName}",
                StringComparer.OrdinalIgnoreCase)
            .Select(group =>
                group.First())
            .ToArray();
    }

    private string CardKind(
        UnifiedDashboardCard card,
        bool applicationTile)
    {
        if (applicationTile)
            return "application";

        if (card.Key.Equals(
                "core:storage",
                StringComparison.OrdinalIgnoreCase) ||
            card.Key.Equals(
                "core:downloads",
                StringComparison.OrdinalIgnoreCase))
        {
            return "progress";
        }

        if (card.Key.Equals(
                "core:activity",
                StringComparison.OrdinalIgnoreCase))
        {
            return "timeline";
        }

        if (card.Key.Equals(
                "core:docker",
                StringComparison.OrdinalIgnoreCase) ||
            card.Key.Equals(
                "core:acquisition",
                StringComparison.OrdinalIgnoreCase) ||
            card.Key.Equals(
                "core:media",
                StringComparison.OrdinalIgnoreCase) ||
            card.Key.Equals(
                "core:health",
                StringComparison.OrdinalIgnoreCase))
        {
            return "status";
        }

        return "metric";
    }

    private static string SectionKey(
        UnifiedDashboardCard card)
    {
        if (card.Key.Equals(
                "core:health",
                StringComparison.OrdinalIgnoreCase) ||
            card.Key.Equals(
                "core:host",
                StringComparison.OrdinalIgnoreCase) ||
            card.Key.Equals(
                "core:storage",
                StringComparison.OrdinalIgnoreCase) ||
            card.Key.Equals(
                "core:docker",
                StringComparison.OrdinalIgnoreCase))
        {
            return "infrastructure";
        }

        if (card.Key.Equals(
                "core:acquisition",
                StringComparison.OrdinalIgnoreCase) ||
            card.Key.Equals(
                "core:downloads",
                StringComparison.OrdinalIgnoreCase) ||
            card.Key.Equals(
                "core:backups",
                StringComparison.OrdinalIgnoreCase))
        {
            return "operations";
        }

        if (card.Key.Equals(
                "app:plex",
                StringComparison.OrdinalIgnoreCase) ||
            card.Key.Equals(
                "core:media",
                StringComparison.OrdinalIgnoreCase) ||
            card.Key.Equals(
                "core:activity",
                StringComparison.OrdinalIgnoreCase))
        {
            return "media";
        }

        return "applications";
    }

    private static int ResolveColumns(
        int count,
        double available)
    {
        if (count <= 1)
            return 1;

        if (available >= 900)
            return Math.Min(
                3,
                count);

        if (available >= 610)
            return Math.Min(
                2,
                count);

        return 1;
    }

    private static double ResolveCardWidth(
        int columns,
        double available)
    {
        var gaps =
            Math.Max(
                0,
                columns - 1) *
            9.0;

        return Math.Max(
            250,
            (available - gaps) /
            Math.Max(
                1,
                columns));
    }

    private static bool TryProgress(
        string value,
        out double progress)
    {
        progress = 0;

        var match =
            System.Text.RegularExpressions.Regex.Match(
                value ??
                string.Empty,
                @"(?<value>\d+(?:\.\d+)?)%",
                System.Text.RegularExpressions.RegexOptions.CultureInvariant);

        return match.Success &&
               double.TryParse(
                   match.Groups["value"].Value,
                   NumberStyles.Float,
                   CultureInfo.InvariantCulture,
                   out progress);
    }

    private IBrush SeverityForeground(
        DashboardSeverity severity) =>
        ResourceBrush(
            severity switch
            {
                DashboardSeverity.Healthy =>
                    "SuccessBrush",

                DashboardSeverity.Warning =>
                    "WarnBrush",

                DashboardSeverity.Error =>
                    "DangerBrush",

                _ =>
                    "AccentBrush"
            },
            Brushes.Gray);

    private IBrush SeverityBackground(
        DashboardSeverity severity) =>
        ResourceBrush(
            severity switch
            {
                DashboardSeverity.Healthy =>
                    "SuccessTintBrush",

                DashboardSeverity.Warning =>
                    "WarnTintBrush",

                DashboardSeverity.Error =>
                    "DangerTintBrush",

                _ =>
                    "AccentTintBrush"
            },
            Brushes.Transparent);

    private IBrush ResourceBrush(
        string key,
        IBrush fallback)
    {
        if (Application.Current?
                .TryFindResource(
                    key,
                    ActualThemeVariant,
                    out var resource) ==
            true &&
            resource is IBrush brush)
        {
            return brush;
        }

        return fallback;
    }

    private sealed record DashboardSection(
        string Title,
        string Key,
        bool ApplicationTiles);
}