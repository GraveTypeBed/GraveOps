using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace GraveOps.Presentation.Avalonia.Fleet;

public sealed class UnifiedFleetView :
    UserControl
{
    private readonly UnifiedFleetFocus
        _focus;

    private readonly TextBlock
        _primaryMetric;
    private readonly TextBlock
        _secondaryMetric;
    private readonly TextBlock
        _tertiaryMetric;
    private readonly TextBlock
        _statusText;
    private readonly TextBlock
        _inventoryDetail;
    private readonly TextBox
        _filter;
    private readonly StackPanel
        _rows;
    private readonly TextBlock
        _selectedTitle;
    private readonly TextBlock
        _selectedMeta;
    private readonly TextBlock
        _selectedDetail;
    private readonly Button
        _primaryAction;
    private readonly Button
        _secondaryAction;

    private UnifiedFleetState _state =
        UnifiedFleetState.Empty;

    private UnifiedFleetHostRow?
        _selectedHost;

    private UnifiedFleetApplicationRow?
        _selectedApplication;

    public UnifiedFleetView(
        UnifiedFleetFocus focus)
    {
        _focus =
            focus;

        HorizontalAlignment =
            HorizontalAlignment.Stretch;

        VerticalAlignment =
            VerticalAlignment.Stretch;

        _primaryMetric =
            MetricValue();

        _secondaryMetric =
            MetricValue();

        _tertiaryMetric =
            MetricValue();

        _statusText =
            new TextBlock
            {
                Text =
                    "Waiting for fleet inventory.",
                TextWrapping =
                    TextWrapping.Wrap,
                Classes =
                {
                    "dim"
                }
            };

        _inventoryDetail =
            new TextBlock
            {
                Text =
                    "No platform adapter has projected inventory yet.",
                TextWrapping =
                    TextWrapping.Wrap,
                Classes =
                {
                    "dim"
                }
            };

        _filter =
            new TextBox
            {
                PlaceholderText =
                    focus == UnifiedFleetFocus.Hosts
                        ? "Filter hosts"
                        : "Filter applications, owners or state",
                MinWidth =
                    280
            };

        _filter.TextChanged +=
            (_, _) =>
                RenderRows();

        _rows =
            new StackPanel
            {
                Spacing =
                    6
            };

        _selectedTitle =
            new TextBlock
            {
                Text =
                    focus == UnifiedFleetFocus.Hosts
                        ? "No host selected"
                        : "No application selected",
                FontWeight =
                    FontWeight.SemiBold,
                FontSize =
                    14
            };

        _selectedMeta =
            new TextBlock
            {
                TextWrapping =
                    TextWrapping.Wrap,
                Classes =
                {
                    "dim"
                }
            };

        _selectedDetail =
            new TextBlock
            {
                TextWrapping =
                    TextWrapping.Wrap
            };

        _primaryAction =
            new Button
            {
                Content =
                    focus == UnifiedFleetFocus.Hosts
                        ? "Activate host"
                        : "Open application",
                IsEnabled =
                    false
            };

        _primaryAction.Click +=
            (_, _) =>
                InvokePrimaryAction();

        _secondaryAction =
            new Button
            {
                Content =
                    focus == UnifiedFleetFocus.Hosts
                        ? "Manage connections"
                        : "Edit identity",
                IsEnabled =
                    focus == UnifiedFleetFocus.Hosts
            };

        _secondaryAction.Click +=
            (_, _) =>
                InvokeSecondaryAction();

        Content =
            BuildWorkspace();

        Update(
            UnifiedFleetState.Empty);
    }

    public event EventHandler?
        RefreshRequested;

    public event EventHandler?
        ManageConnectionsRequested;

    public event EventHandler<UnifiedFleetHostRequestedEventArgs>?
        HostRequested;

    public event EventHandler<UnifiedFleetApplicationRequestedEventArgs>?
        ApplicationRequested;

    public void Update(
        UnifiedFleetState state)
    {
        _state =
            state ?? UnifiedFleetState.Empty;

        _statusText.Text =
            _state.Status;

        _inventoryDetail.Text =
            _state.InventoryDetail;

        UpdateMetrics();
        RenderRows();
    }

    public void SetStatus(
        string status)
    {
        _statusText.Text =
            status;
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

        content.Children.Add(
            BuildHeader());

        content.Children.Add(
            BuildMetrics());

        content.Children.Add(
            BuildFilter());

        content.Children.Add(
            BuildRows());

        content.Children.Add(
            BuildSelection());

        return
            new ScrollViewer
            {
                VerticalScrollBarVisibility =
                    global::Avalonia.Controls.Primitives
                        .ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility =
                    global::Avalonia.Controls.Primitives
                        .ScrollBarVisibility.Disabled,
                Content =
                    content
            };
    }

    private Control BuildHeader()
    {
        var heading =
            new StackPanel();

        heading.Children.Add(
            new TextBlock
            {
                Text =
                    _focus == UnifiedFleetFocus.Hosts
                        ? "Fleet & connections"
                        : "Applications",
                FontSize =
                    18,
                Classes =
                {
                    "sectionTitle"
                }
            });

        heading.Children.Add(
            new TextBlock
            {
                Text =
                    _focus == UnifiedFleetFocus.Hosts
                        ? "Saved targets, active ownership, capture freshness and capability coverage."
                        : "Owned and discovered applications grouped by target without leaking platform operations into presentation.",
                Margin =
                    new Thickness(
                        0,
                        3,
                        0,
                        0),
                TextWrapping =
                    TextWrapping.Wrap,
                Classes =
                {
                    "pageSubtitle"
                }
            });

        var refresh =
            new Button
            {
                Content =
                    "Refresh fleet",
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

        Grid.SetColumn(
            refresh,
            1);

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
            heading);

        header.Children.Add(
            refresh);

        return header;
    }

    private Control BuildMetrics()
    {
        var grid =
            new Grid
            {
                ColumnDefinitions =
                    new ColumnDefinitions(
                        "*,*,*"),
                ColumnSpacing =
                    8
            };

        var first =
            Metric(
                _focus == UnifiedFleetFocus.Hosts
                    ? "SAVED HOSTS"
                    : "APPLICATIONS",
                _primaryMetric);

        var second =
            Metric(
                _focus == UnifiedFleetFocus.Hosts
                    ? "ACTIVE / READY"
                    : "VERIFIED",
                _secondaryMetric);

        Grid.SetColumn(
            second,
            1);

        var third =
            Metric(
                _focus == UnifiedFleetFocus.Hosts
                    ? "STALE INVENTORIES"
                    : "OWNING TARGETS",
                _tertiaryMetric);

        Grid.SetColumn(
            third,
            2);

        grid.Children.Add(
            first);

        grid.Children.Add(
            second);

        grid.Children.Add(
            third);

        return grid;
    }

    private Control BuildFilter()
    {
        var reset =
            new Button
            {
                Content =
                    "Reset filter",
                Classes =
                {
                    "compact"
                }
            };

        reset.Click +=
            (_, _) =>
                _filter.Text =
                    string.Empty;

        Grid.SetColumn(
            reset,
            1);

        var grid =
            new Grid
            {
                ColumnDefinitions =
                    new ColumnDefinitions(
                        "*,Auto"),
                ColumnSpacing =
                    8
            };

        grid.Children.Add(
            _filter);

        grid.Children.Add(
            reset);

        return
            new Border
            {
                Classes =
                {
                    "module",
                    "adaptive"
                },
                Padding =
                    new Thickness(
                        10),
                Child =
                    new StackPanel
                    {
                        Spacing =
                            7,
                        Children =
                        {
                            grid,
                            _statusText,
                            _inventoryDetail
                        }
                    }
            };
    }

    private Control BuildRows()
    {
        return
            new Border
            {
                Classes =
                {
                    "module",
                    "adaptive"
                },
                Padding =
                    new Thickness(
                        8),
                MinHeight =
                    250,
                Child =
                    _rows
            };
    }

    private Control BuildSelection()
    {
        Grid.SetColumn(
            _secondaryAction,
            1);

        var actions =
            new Grid
            {
                ColumnDefinitions =
                    new ColumnDefinitions(
                        "Auto,Auto,*"),
                ColumnSpacing =
                    7
            };

        actions.Children.Add(
            _primaryAction);

        actions.Children.Add(
            _secondaryAction);

        return
            new Border
            {
                Classes =
                {
                    "module",
                    "adaptive"
                },
                Padding =
                    new Thickness(
                        12),
                Child =
                    new StackPanel
                    {
                        Spacing =
                            7,
                        Children =
                        {
                            new TextBlock
                            {
                                Text =
                                    _focus == UnifiedFleetFocus.Hosts
                                        ? "Selected host"
                                        : "Selected application",
                                Classes =
                                {
                                    "eyebrow"
                                }
                            },
                            _selectedTitle,
                            _selectedMeta,
                            _selectedDetail,
                            actions
                        }
                    }
            };
    }

    private void UpdateMetrics()
    {
        if (_focus ==
            UnifiedFleetFocus.Hosts)
        {
            _primaryMetric.Text =
                _state.Hosts.Count.ToString();

            _secondaryMetric.Text =
                _state.Hosts.Count(item =>
                    item.IsActive &&
                    !item.IsStale)
                .ToString();

            _tertiaryMetric.Text =
                _state.Hosts.Count(item =>
                    item.IsStale)
                .ToString();

            return;
        }

        _primaryMetric.Text =
            _state.Applications.Count.ToString();

        _secondaryMetric.Text =
            _state.Applications.Count(item =>
                item.IsVerified &&
                !item.IsStale)
            .ToString();

        _tertiaryMetric.Text =
            _state.Applications
                .Select(item =>
                    item.OwnerTargetId)
                .Where(value =>
                    !string.IsNullOrWhiteSpace(
                        value))
                .Distinct(
                    StringComparer.OrdinalIgnoreCase)
                .Count()
                .ToString();
    }

    private void RenderRows()
    {
        _rows.Children.Clear();

        var query =
            _filter.Text?
                .Trim() ??
            string.Empty;

        if (_focus ==
            UnifiedFleetFocus.Hosts)
        {
            var hosts =
                _state.Hosts
                    .Where(item =>
                        Matches(
                            query,
                            item.DisplayName,
                            item.Platform,
                            item.Connection,
                            item.State,
                            item.CapabilitySummary))
                    .OrderByDescending(item =>
                        item.IsActive)
                    .ThenBy(item =>
                        item.IsStale)
                    .ThenBy(item =>
                        item.DisplayName,
                        StringComparer.OrdinalIgnoreCase)
                    .ToArray();

            foreach (var host in hosts)
                _rows.Children.Add(
                    BuildHostRow(
                        host));

            if (hosts.Length == 0)
                _rows.Children.Add(
                    EmptyState(
                        "No hosts match the current filter."));

            SelectHost(
                hosts.FirstOrDefault(item =>
                    item.TargetId.Equals(
                        _selectedHost?.TargetId,
                        StringComparison.OrdinalIgnoreCase)) ??
                hosts.FirstOrDefault(item =>
                    item.IsActive) ??
                hosts.FirstOrDefault());

            return;
        }

        var applications =
            _state.Applications
                .Where(item =>
                    Matches(
                        query,
                        item.DisplayName,
                        item.Product,
                        item.Category,
                        item.Role,
                        item.Runtime,
                        item.OwnerTargetName,
                        item.State,
                        item.Summary))
                .OrderBy(item =>
                    item.Category,
                    StringComparer.OrdinalIgnoreCase)
                .ThenBy(item =>
                    item.OwnerTargetName,
                    StringComparer.OrdinalIgnoreCase)
                .ThenBy(item =>
                    item.DisplayName,
                    StringComparer.OrdinalIgnoreCase)
                .ToArray();

        foreach (var application in applications)
            _rows.Children.Add(
                BuildApplicationRow(
                    application));

        if (applications.Length == 0)
            _rows.Children.Add(
                EmptyState(
                    "No applications match the current filter."));

        SelectApplication(
            applications.FirstOrDefault(item =>
                item.ApplicationKey.Equals(
                    _selectedApplication?.ApplicationKey,
                    StringComparison.OrdinalIgnoreCase) &&
                item.OwnerTargetId.Equals(
                    _selectedApplication?.OwnerTargetId,
                    StringComparison.OrdinalIgnoreCase)) ??
            applications.FirstOrDefault());
    }

    private Control BuildHostRow(
        UnifiedFleetHostRow host)
    {
        var title =
            new TextBlock
            {
                Text =
                    host.DisplayName,
                FontWeight =
                    FontWeight.SemiBold
            };

        var meta =
            new TextBlock
            {
                Text =
                    $"{host.Platform} · {host.Connection}",
                TextWrapping =
                    TextWrapping.Wrap,
                Classes =
                {
                    "dim"
                }
            };

        var detail =
            new TextBlock
            {
                Text =
                    $"{host.StatusLabel} · {host.ApplicationCount} application(s) · {host.CapabilitySummary} · {host.CaptureLabel}",
                TextWrapping =
                    TextWrapping.Wrap,
                Classes =
                {
                    "dim"
                }
            };

        var button =
            new Button
            {
                HorizontalContentAlignment =
                    HorizontalAlignment.Stretch,
                Classes =
                {
                    "listRow"
                },
                Content =
                    new StackPanel
                    {
                        Spacing =
                            3,
                        Children =
                        {
                            title,
                            meta,
                            detail
                        }
                    }
            };

        button.Click +=
            (_, _) =>
                SelectHost(
                    host);

        return button;
    }

    private Control BuildApplicationRow(
        UnifiedFleetApplicationRow application)
    {
        var title =
            new TextBlock
            {
                Text =
                    application.DisplayName,
                FontWeight =
                    FontWeight.SemiBold
            };

        var meta =
            new TextBlock
            {
                Text =
                    $"{application.Product} · {application.Category} · {application.OwnerLabel}",
                TextWrapping =
                    TextWrapping.Wrap,
                Classes =
                {
                    "dim"
                }
            };

        var detail =
            new TextBlock
            {
                Text =
                    $"{application.VerificationLabel} · {application.State} · {application.Runtime}",
                TextWrapping =
                    TextWrapping.Wrap,
                Classes =
                {
                    "dim"
                }
            };

        var button =
            new Button
            {
                HorizontalContentAlignment =
                    HorizontalAlignment.Stretch,
                Classes =
                {
                    "listRow"
                },
                Content =
                    new StackPanel
                    {
                        Spacing =
                            3,
                        Children =
                        {
                            title,
                            meta,
                            detail
                        }
                    }
            };

        button.Click +=
            (_, _) =>
                SelectApplication(
                    application);

        return button;
    }

    private void SelectHost(
        UnifiedFleetHostRow? host)
    {
        _selectedHost =
            host;

        _selectedApplication =
            null;

        if (host is null)
        {
            _selectedTitle.Text =
                "No host selected";

            _selectedMeta.Text =
                string.Empty;

            _selectedDetail.Text =
                "Select a saved target to inspect ownership and capture state.";

            _primaryAction.IsEnabled =
                false;

            _secondaryAction.IsEnabled =
                true;

            return;
        }

        _selectedTitle.Text =
            host.DisplayName;

        _selectedMeta.Text =
            $"{host.Platform} · {host.Connection}";

        _selectedDetail.Text =
            $"{host.State} · {host.ApplicationCount} remembered application(s) · {host.CapabilitySummary} · captured {host.CaptureLabel}";

        _primaryAction.IsEnabled =
            host.CanActivate;

        _secondaryAction.IsEnabled =
            true;
    }

    private void SelectApplication(
        UnifiedFleetApplicationRow? application)
    {
        _selectedApplication =
            application;

        _selectedHost =
            null;

        if (application is null)
        {
            _selectedTitle.Text =
                "No application selected";

            _selectedMeta.Text =
                string.Empty;

            _selectedDetail.Text =
                "Select an application to inspect ownership and available adapter actions.";

            _primaryAction.IsEnabled =
                false;

            _secondaryAction.IsEnabled =
                false;

            return;
        }

        _selectedTitle.Text =
            application.DisplayName;

        _selectedMeta.Text =
            $"{application.Product} · {application.Role} · {application.Runtime} · owner {application.OwnerLabel}";

        _selectedDetail.Text =
            $"{application.VerificationLabel} · {application.State} · {application.Summary}";

        _primaryAction.IsEnabled =
            application.CanOpen;

        _secondaryAction.IsEnabled =
            application.CanEditIdentity;
    }

    private void InvokePrimaryAction()
    {
        if (_focus ==
            UnifiedFleetFocus.Hosts)
        {
            if (_selectedHost is null ||
                !_selectedHost.CanActivate)
            {
                return;
            }

            HostRequested?.Invoke(
                this,
                new UnifiedFleetHostRequestedEventArgs(
                    _selectedHost.TargetId));

            return;
        }

        if (_selectedApplication is null ||
            !_selectedApplication.CanOpen)
        {
            return;
        }

        ApplicationRequested?.Invoke(
            this,
            new UnifiedFleetApplicationRequestedEventArgs(
                _selectedApplication.ApplicationKey,
                _selectedApplication.OwnerTargetId,
                editIdentity: false));
    }

    private void InvokeSecondaryAction()
    {
        if (_focus ==
            UnifiedFleetFocus.Hosts)
        {
            ManageConnectionsRequested?.Invoke(
                this,
                EventArgs.Empty);

            return;
        }

        if (_selectedApplication is null ||
            !_selectedApplication.CanEditIdentity)
        {
            return;
        }

        ApplicationRequested?.Invoke(
            this,
            new UnifiedFleetApplicationRequestedEventArgs(
                _selectedApplication.ApplicationKey,
                _selectedApplication.OwnerTargetId,
                editIdentity: true));
    }

    private static Border Metric(
        string label,
        TextBlock value) =>
        new()
        {
            Classes =
            {
                "metricCard"
            },
            Padding =
                new Thickness(
                    10),
            Child =
                new StackPanel
                {
                    Spacing =
                        4,
                    Children =
                    {
                        new TextBlock
                        {
                            Text =
                                label,
                            Classes =
                            {
                                "eyebrow"
                            }
                        },
                        value
                    }
                }
        };

    private static TextBlock MetricValue() =>
        new()
        {
            Text =
                "0",
            FontSize =
                20,
            FontWeight =
                FontWeight.SemiBold
        };

    private static Border EmptyState(
        string text) =>
        new()
        {
            Classes =
            {
                "emptyState"
            },
            Padding =
                new Thickness(
                    12),
            Child =
                new TextBlock
                {
                    Text =
                        text,
                    TextWrapping =
                        TextWrapping.Wrap
                }
        };

    private static bool Matches(
        string query,
        params string?[] values)
    {
        if (string.IsNullOrWhiteSpace(
                query))
        {
            return true;
        }

        return values.Any(value =>
            !string.IsNullOrWhiteSpace(
                value) &&
            value.Contains(
                query,
                StringComparison.OrdinalIgnoreCase));
    }
}