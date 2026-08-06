using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace GraveOps.Presentation.Avalonia.MediaWorkspaces;

public sealed class UnifiedMediaHubView :
    UserControl
{
    private readonly TextBlock
        _sampleAgeText =
            MediaUi.Dim(
                "Waiting for environment capture");

    private readonly TextBlock
        _healthyText =
            MediaUi.MetricValue(
                "0");

    private readonly TextBlock
        _attentionText =
            MediaUi.MetricValue(
                "0");

    private readonly TextBlock
        _offlineText =
            MediaUi.MetricValue(
                "0");

    private readonly TextBlock
        _targetText =
            MediaUi.MetricValue(
                "--",
                17);

    private readonly TextBlock
        _groupingSummaryText =
            MediaUi.Dim(
                "Waiting for grouped fleet projection.");

    private readonly Button
        _fleetButton =
            MediaUi.Compact(
                "Fleet overview");

    private readonly Button
        _identityButton =
            MediaUi.Compact(
                "Identity registry");

    private readonly Button
        _refreshButton =
            MediaUi.Primary(
                "Refresh telemetry");

    private readonly TextBox
        _filterText =
            new()
            {
                Width = 240,
                PlaceholderText = "Filter applications",
                Classes =
                {
                    "filter"
                }
            };

    private readonly Button
        _showHiddenButton =
            MediaUi.Compact(
                "Show hidden");

    private readonly WrapPanel
        _productsPanel =
            new()
            {
                Orientation =
                    Orientation.Horizontal
            };

    private readonly Border
        _fleetEmpty =
            MediaUi.EmptyState(
                "No matching applications",
                "Clear the filter or show hidden launchers to restore the full fleet.");

    private readonly Grid
        _fleetPanel =
            new();

    private readonly TextBlock
        _identityStoreText =
            MediaUi.Dim(
                "--");

    private readonly TextBlock
        _identitySummaryText =
            MediaUi.Muted(
                "Identity registry unavailable.");

    private readonly StackPanel
        _identityRowsPanel =
            new()
            {
                Spacing = 6
            };

    private readonly TextBlock
        _identitySelectedText =
            MediaUi.Muted(
                "Select a detected source.");

    private readonly TextBox
        _identityProductText =
            new();

    private readonly TextBox
        _identityRoleText =
            new();

    private readonly TextBox
        _identityProtocolText =
            new();

    private readonly TextBox
        _identityDisplayNameText =
            new();

    private readonly TextBox
        _identityParentText =
            new();

    private readonly TextBox
        _identityUrlText =
            new();

    private readonly TextBox
        _identityCategoryText =
            new();

    private readonly CheckBox
        _identityOwnsHealth =
            new()
            {
                Content =
                    "Use this source as an application health owner"
            };

    private readonly CheckBox
        _identityShowNavigation =
            new()
            {
                Content =
                    "Show application in GraveOps navigation"
            };

    private readonly CheckBox
        _identityVisible =
            new()
            {
                Content =
                    "Show instance in Media Hub"
            };

    private readonly TextBlock
        _identityVerificationText =
            MediaUi.Muted(
                "--");

    private readonly TextBlock
        _identityDetectedText =
            MediaUi.Muted(
                "--");

    private readonly TextBlock
        _identityStatusText =
            MediaUi.Dim(
                "No application identity selected.");

    private readonly Button
        _identitySaveButton =
            MediaUi.Primary(
                "Confirm and save");

    private readonly Button
        _identityResetButton =
            MediaUi.Compact(
                "Reset automatic");

    private readonly Button
        _identityOpenButton =
            MediaUi.Compact(
                "Open selected");

    private readonly Grid
        _identityPanel =
            new();

    private UnifiedMediaHubState
        _state =
            UnifiedMediaHubState.Empty;

    private string
        _selectedIdentityKey =
            string.Empty;

    private string
        _identityEditorKey =
            string.Empty;

    private bool
        _identityEditorDirty;

    private bool
        _identityEditorSyncing;

    public UnifiedMediaHubView()
    {
        BuildView();
        WireEvents();
        Update(
            UnifiedMediaHubState.Empty);
    }

    public event EventHandler?
        RefreshRequested;

    public event EventHandler?
        ShowHiddenRequested;

    public event EventHandler<UnifiedMediaHubModeEventArgs>?
        ModeRequested;

    public event EventHandler<UnifiedMediaProductEventArgs>?
        ProductOpenRequested;

    public event EventHandler<UnifiedMediaProductEventArgs>?
        ProductIdentityRequested;

    public event EventHandler<UnifiedIdentityEventArgs>?
        IdentitySelectionRequested;

    public event EventHandler<UnifiedIdentitySaveEventArgs>?
        IdentitySaveRequested;

    public event EventHandler<UnifiedIdentityEventArgs>?
        IdentityResetRequested;

    public event EventHandler<UnifiedIdentityEventArgs>?
        IdentityOpenRequested;

    public void Update(
        UnifiedMediaHubState state)
    {
        _state =
            state ??
            UnifiedMediaHubState.Empty;

        _sampleAgeText.Text =
            _state.SampleAge;

        _healthyText.Text =
            _state.Healthy;

        _attentionText.Text =
            _state.Attention;

        _offlineText.Text =
            _state.Offline;

        _targetText.Text =
            _state.Target;

        _groupingSummaryText.Text =
            _state.GroupingSummary;

        _refreshButton.IsEnabled =
            _state.CanRefresh;

        _showHiddenButton.IsEnabled =
            _state.CanShowHidden;

        _showHiddenButton.Content =
            _state.ShowHidden
                ? "Hide hidden"
                : "Show hidden";

        _identityButton.IsEnabled =
            _state.IdentityAvailable;

        _identityStoreText.Text =
            _state.IdentityStorePath;

        _identitySummaryText.Text =
            _state.IdentitySummary;

        _identityStatusText.Text =
            _state.IdentityStatus;

        var previousIdentityKey =
            _selectedIdentityKey;

        if (string.IsNullOrWhiteSpace(
                _selectedIdentityKey) ||
            !_state.IdentityRows.Any(row =>
                row.Key.Equals(
                    _selectedIdentityKey,
                    StringComparison.OrdinalIgnoreCase)))
        {
            _selectedIdentityKey =
                _state.IdentityRows
                    .FirstOrDefault()
                    ?.Key ??
                string.Empty;
        }

        if (!previousIdentityKey.Equals(
                _selectedIdentityKey,
                StringComparison.OrdinalIgnoreCase))
        {
            _identityEditorDirty =
                false;
        }

        RenderProducts();
        RenderIdentityRows();
        RenderSelectedIdentity();
        ApplyMode();
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

        var headingText =
            new StackPanel
            {
                Children =
                {
                    MediaUi.PageTitle(
                        "Media operations"),
                    MediaUi.Subtitle(
                        "Live application health and launcher ownership from the active server.")
                }
            };

        var headingActions =
            new StackPanel
            {
                Orientation =
                    Orientation.Horizontal,
                VerticalAlignment =
                    VerticalAlignment.Center,
                Spacing = 8,
                Children =
                {
                    _sampleAgeText,
                    _fleetButton,
                    _identityButton,
                    _refreshButton
                }
            };

        Grid.SetColumn(
            headingActions,
            1);

        heading.Children.Add(
            headingText);

        heading.Children.Add(
            headingActions);

        root.Children.Add(
            heading);

        root.Children.Add(
            MediaUi.FourMetrics(
                MediaUi.Metric(
                    "HEALTHY",
                    _healthyText,
                    "Applications operating normally"),
                MediaUi.Metric(
                    "DEGRADED / BUSY",
                    _attentionText,
                    "Reachable but needs attention"),
                MediaUi.Metric(
                    "OFFLINE",
                    _offlineText,
                    "No verified application response"),
                MediaUi.Metric(
                    "TARGET",
                    _targetText,
                    "Uses active control-plane context")));

        BuildFleetPanel();
        BuildIdentityPanel();

        root.Children.Add(
            _fleetPanel);

        root.Children.Add(
            _identityPanel);

        Content =
            MediaUi.Scroll(
                root);
    }

    private void BuildFleetPanel()
    {
        _fleetPanel.RowDefinitions =
            new RowDefinitions(
                "Auto,*");

        _fleetPanel.RowSpacing =
            10;

        var header =
            new Grid
            {
                ColumnDefinitions =
                    new ColumnDefinitions(
                        "*,Auto,Auto"),
                ColumnSpacing =
                    8
            };

        var text =
            new StackPanel
            {
                Children =
                {
                    MediaUi.Title(
                        "Application health"),
                    MediaUi.Subtitle(
                        "Applications are grouped by category and product; each verified runtime stays visible as a compact instance row."),
                    _groupingSummaryText
                }
            };

        Grid.SetColumn(
            _filterText,
            1);

        Grid.SetColumn(
            _showHiddenButton,
            2);

        header.Children.Add(
            text);

        header.Children.Add(
            _filterText);

        header.Children.Add(
            _showHiddenButton);

        _fleetPanel.Children.Add(
            header);

        var body =
            new Grid();

        body.Children.Add(
            MediaUi.Scroll(
                _productsPanel));

        body.Children.Add(
            _fleetEmpty);

        Grid.SetRow(
            body,
            1);

        _fleetPanel.Children.Add(
            body);
    }

    private void BuildIdentityPanel()
    {
        _identityPanel.ColumnDefinitions =
            new ColumnDefinitions(
                "1.15*,430");

        _identityPanel.ColumnSpacing =
            10;

        _identityPanel.MinHeight =
            520;

        var listCard =
            MediaUi.FlatCard(
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
                                    "Application identity registry"),
                                MediaUi.Subtitle(
                                    "Logical application type, stable runtime owner, role, endpoint and operator overrides remain separate."),
                                _identityStoreText
                            }
                        },
                        MediaUi.Inset(
                            _identitySummaryText),
                        MediaUi.Scroll(
                            _identityRowsPanel,
                            420)
                    }
                });

        var editor =
            new StackPanel
            {
                Spacing = 8,
                Children =
                {
                    MediaUi.Title(
                        "Edit identity"),
                    _identitySelectedText,
                    Labeled(
                        "Application type",
                        _identityProductText),
                    Labeled(
                        "Role",
                        _identityRoleText),
                    Labeled(
                        "API / protocol",
                        _identityProtocolText),
                    Labeled(
                        "Display name",
                        _identityDisplayNameText),
                    Labeled(
                        "Parent / owning application",
                        _identityParentText),
                    Labeled(
                        "Verified URL override",
                        _identityUrlText),
                    Labeled(
                        "Category",
                        _identityCategoryText),
                    MediaUi.Inset(
                        new StackPanel
                        {
                            Spacing = 7,
                            Children =
                            {
                                _identityOwnsHealth,
                                _identityShowNavigation,
                                _identityVisible
                            }
                        }),
                    MediaUi.Inset(
                        new StackPanel
                        {
                            Spacing = 4,
                            Children =
                            {
                                MediaUi.Eyebrow(
                                    "DETECTED CONTEXT"),
                                _identityVerificationText,
                                _identityDetectedText
                            }
                        }),
                    _identityStatusText,
                    new WrapPanel
                    {
                        Children =
                        {
                            _identitySaveButton,
                            _identityResetButton,
                            _identityOpenButton
                        }
                    }
                }
            };

        _identitySaveButton.Margin =
            new Thickness(
                0,
                0,
                8,
                8);

        _identityResetButton.Margin =
            new Thickness(
                0,
                0,
                8,
                8);

        _identityOpenButton.Margin =
            new Thickness(
                0,
                0,
                0,
                8);

        var editorCard =
            MediaUi.FlatCard(
                MediaUi.Scroll(
                    editor));

        Grid.SetColumn(
            editorCard,
            1);

        _identityPanel.Children.Add(
            listCard);

        _identityPanel.Children.Add(
            editorCard);
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

    private void WireEvents()
    {
        _refreshButton.Click +=
            (_, _) =>
                RefreshRequested?.Invoke(
                    this,
                    EventArgs.Empty);

        _fleetButton.Click +=
            (_, _) =>
            {
                _state =
                    _state with
                    {
                        Mode =
                            UnifiedMediaHubMode.Fleet
                    };

                ApplyMode();

                ModeRequested?.Invoke(
                    this,
                    new UnifiedMediaHubModeEventArgs(
                        UnifiedMediaHubMode.Fleet));
            };

        _identityButton.Click +=
            (_, _) =>
            {
                if (!_state.IdentityAvailable)
                    return;

                _state =
                    _state with
                    {
                        Mode =
                            UnifiedMediaHubMode.Identity
                    };

                ApplyMode();

                ModeRequested?.Invoke(
                    this,
                    new UnifiedMediaHubModeEventArgs(
                        UnifiedMediaHubMode.Identity));
            };

        _showHiddenButton.Click +=
            (_, _) =>
                ShowHiddenRequested?.Invoke(
                    this,
                    EventArgs.Empty);

        _filterText.TextChanged +=
            (_, _) =>
                RenderProducts();

        foreach (var editor in
                 new[]
                 {
                     _identityProductText,
                     _identityRoleText,
                     _identityProtocolText,
                     _identityDisplayNameText,
                     _identityParentText,
                     _identityUrlText,
                     _identityCategoryText
                 })
        {
            editor.TextChanged +=
                (_, _) =>
                    MarkIdentityEditorDirty();
        }

        foreach (var editor in
                 new[]
                 {
                     _identityOwnsHealth,
                     _identityShowNavigation,
                     _identityVisible
                 })
        {
            editor.Click +=
                (_, _) =>
                    MarkIdentityEditorDirty();
        }

        _identitySaveButton.Click +=
            (_, _) =>
            {
                var selected =
                    SelectedIdentity();

                if (selected is null)
                    return;

                IdentitySaveRequested?.Invoke(
                    this,
                    new UnifiedIdentitySaveEventArgs(
                        new UnifiedIdentityEditRequest(
                            selected.Key,
                            _identityDisplayNameText.Text ??
                            string.Empty,
                            _identityProductText.Text ??
                            string.Empty,
                            _identityRoleText.Text ??
                            string.Empty,
                            _identityProtocolText.Text ??
                            string.Empty,
                            _identityParentText.Text ??
                            string.Empty,
                            _identityUrlText.Text ??
                            string.Empty,
                            _identityCategoryText.Text ??
                            string.Empty,
                            _identityOwnsHealth.IsChecked ==
                            true,
                            _identityShowNavigation.IsChecked ==
                            true,
                            _identityVisible.IsChecked ==
                            true)));

                _identityEditorDirty =
                    false;
            };

        _identityResetButton.Click +=
            (_, _) =>
            {
                var selected =
                    SelectedIdentity();

                if (selected is null)
                    return;

                IdentityResetRequested?.Invoke(
                    this,
                    new UnifiedIdentityEventArgs(
                        selected));

                _identityEditorDirty =
                    false;
            };

        _identityOpenButton.Click +=
            (_, _) =>
            {
                var selected =
                    SelectedIdentity();

                if (selected is null)
                    return;

                IdentityOpenRequested?.Invoke(
                    this,
                    new UnifiedIdentityEventArgs(
                        selected));
            };
    }

    private void ApplyMode()
    {
        var identity =
            _state.IdentityAvailable &&
            _state.Mode ==
            UnifiedMediaHubMode.Identity;

        _fleetPanel.IsVisible =
            !identity;

        _identityPanel.IsVisible =
            identity;

        _fleetButton.Classes.Set(
            "selected",
            !identity);

        _identityButton.Classes.Set(
            "selected",
            identity);
    }

    private void RenderProducts()
    {
        _productsPanel.Children.Clear();

        var filter =
            _filterText.Text
                ?.Trim() ??
            string.Empty;

        var rows =
            _state.Products
                .Where(row =>
                    string.IsNullOrWhiteSpace(
                        filter) ||
                    row.Product.Contains(
                        filter,
                        StringComparison.OrdinalIgnoreCase) ||
                    row.Category.Contains(
                        filter,
                        StringComparison.OrdinalIgnoreCase) ||
                    row.Summary.Contains(
                        filter,
                        StringComparison.OrdinalIgnoreCase) ||
                    row.Instances.Any(instance =>
                        instance.DisplayName.Contains(
                            filter,
                            StringComparison.OrdinalIgnoreCase) ||
                        instance.State.Contains(
                            filter,
                            StringComparison.OrdinalIgnoreCase)))
                .ToArray();

        _fleetEmpty.IsVisible =
            rows.Length == 0;

        foreach (var row in rows)
        {
            _productsPanel.Children.Add(
                BuildProductCard(
                    row));
        }
    }

    private Border BuildProductCard(
        UnifiedMediaProductRow row)
    {
        var instances =
            new StackPanel
            {
                Spacing = 5
            };

        foreach (var instance in
                 row.Instances)
        {
            instances.Children.Add(
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
                                        "*,Auto"),
                                Children =
                                {
                                    new TextBlock
                                    {
                                        Text =
                                            instance.DisplayName,
                                        FontWeight =
                                            FontWeight.SemiBold,
                                        TextTrimming =
                                            TextTrimming.CharacterEllipsis
                                    },
                                    BuildRightCell(
                                        instance.State)
                                }
                            },
                            MediaUi.Dim(
                                instance.Meta),
                            MediaUi.Muted(
                                instance.Endpoint)
                        }
                    },
                    8));
        }

        var identity =
            MediaUi.Compact(
                "Identity");

        identity.IsEnabled =
            row.CanEditIdentity;

        identity.Click +=
            (_, _) =>
                ProductIdentityRequested?.Invoke(
                    this,
                    new UnifiedMediaProductEventArgs(
                        row));

        var open =
            MediaUi.Compact(
                "Open");

        open.IsEnabled =
            row.CanOpen;

        open.Click +=
            (_, _) =>
                ProductOpenRequested?.Invoke(
                    this,
                    new UnifiedMediaProductEventArgs(
                        row));

        return
            MediaUi.FlatCard(
                new StackPanel
                {
                    Spacing = 8,
                    Width = 390,
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
                                            row.Product,
                                            15),
                                        MediaUi.Eyebrow(
                                            row.Category)
                                    }
                                },
                                BuildRightCell(
                                    row.State)
                            }
                        },
                        instances,
                        MediaUi.Dim(
                            row.Summary),
                        new WrapPanel
                        {
                            HorizontalAlignment =
                                HorizontalAlignment.Right,
                            Children =
                            {
                                identity,
                                open
                            }
                        }
                    }
                });
    }

    private static TextBlock BuildRightCell(
        string text)
    {
        var block =
            new TextBlock
            {
                Text = text,
                FontWeight =
                    FontWeight.SemiBold,
                VerticalAlignment =
                    VerticalAlignment.Center,
                TextTrimming =
                    TextTrimming.CharacterEllipsis
            };

        Grid.SetColumn(
            block,
            1);

        return block;
    }

    private void RenderIdentityRows()
    {
        _identityRowsPanel.Children.Clear();

        foreach (var row in
                 _state.IdentityRows)
        {
            var button =
                MediaUi.RowButton(
                    MediaUi.Inset(
                        new Grid
                        {
                            ColumnDefinitions =
                                new ColumnDefinitions(
                                    "1.05*,0.7*,0.9*,1.45*,Auto"),
                            ColumnSpacing = 10,
                            Children =
                            {
                                MediaUi.Cell(
                                    row.DisplayName,
                                    0,
                                    true),
                                MediaUi.Cell(
                                    row.Product,
                                    1,
                                    cssClass:
                                        "muted"),
                                MediaUi.Cell(
                                    row.Role,
                                    2,
                                    cssClass:
                                        "muted"),
                                MediaUi.Cell(
                                    row.Url,
                                    3,
                                    cssClass:
                                        "dim"),
                                MediaUi.Cell(
                                    row.Verification,
                                    4)
                            }
                        },
                        8));

            button.Click +=
                (_, _) =>
                {
                    _selectedIdentityKey =
                        row.Key;

                    _identityEditorDirty =
                        false;

                    RenderIdentityRows();
                    RenderSelectedIdentity();

                    IdentitySelectionRequested?.Invoke(
                        this,
                        new UnifiedIdentityEventArgs(
                            row));
                };

            button.Classes.Set(
                "selected",
                row.Key.Equals(
                    _selectedIdentityKey,
                    StringComparison.OrdinalIgnoreCase));

            _identityRowsPanel.Children.Add(
                button);
        }
    }

    private void RenderSelectedIdentity()
    {
        var row =
            SelectedIdentity();

        var enabled =
            _state.IdentityAvailable &&
            row is not null;

        foreach (var control in
                 new Control[]
                 {
                     _identityProductText,
                     _identityRoleText,
                     _identityProtocolText,
                     _identityDisplayNameText,
                     _identityParentText,
                     _identityUrlText,
                     _identityCategoryText,
                     _identityOwnsHealth,
                     _identityShowNavigation,
                     _identityVisible,
                     _identitySaveButton,
                     _identityResetButton,
                     _identityOpenButton
                 })
        {
            control.IsEnabled =
                enabled;
        }

        if (row is null)
        {
            SynchronizeIdentityEditor(
                null);
            return;
        }

        _identitySelectedText.Text =
            row.DisplayName;

        _identityVerificationText.Text =
            row.Verification;

        _identityDetectedText.Text =
            row.Detected;

        if (_identityEditorDirty &&
            _identityEditorKey.Equals(
                row.Key,
                StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        SynchronizeIdentityEditor(
            row);
    }

    private void SynchronizeIdentityEditor(
        UnifiedIdentityRow? row)
    {
        _identityEditorSyncing =
            true;

        try
        {
            _identitySelectedText.Text =
                row?.DisplayName ??
                "Select a detected source.";

            _identityProductText.Text =
                row?.Product ??
                string.Empty;

            _identityRoleText.Text =
                row?.Role ??
                string.Empty;

            _identityProtocolText.Text =
                row?.Protocol ??
                string.Empty;

            _identityDisplayNameText.Text =
                row?.DisplayName ??
                string.Empty;

            _identityParentText.Text =
                row?.Parent ??
                string.Empty;

            _identityUrlText.Text =
                row?.Url ??
                string.Empty;

            _identityCategoryText.Text =
                row?.Category ??
                string.Empty;

            _identityOwnsHealth.IsChecked =
                row?.OwnsHealth ??
                false;

            _identityShowNavigation.IsChecked =
                row?.ShowNavigation ??
                false;

            _identityVisible.IsChecked =
                row?.IsVisible ??
                false;

            _identityVerificationText.Text =
                row?.Verification ??
                "--";

            _identityDetectedText.Text =
                row?.Detected ??
                "--";
        }
        finally
        {
            _identityEditorSyncing =
                false;
        }

        _identityEditorKey =
            row?.Key ??
            string.Empty;

        _identityEditorDirty =
            false;
    }

    private void MarkIdentityEditorDirty()
    {
        if (_identityEditorSyncing)
            return;

        _identityEditorKey =
            _selectedIdentityKey;

        _identityEditorDirty =
            true;
    }

    private UnifiedIdentityRow? SelectedIdentity() =>
        _state.IdentityRows
            .FirstOrDefault(row =>
                row.Key.Equals(
                    _selectedIdentityKey,
                    StringComparison.OrdinalIgnoreCase));
}
