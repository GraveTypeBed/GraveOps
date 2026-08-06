using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace GraveOps.Presentation.Avalonia.Findings;

public sealed class UnifiedFindingsView :
    UserControl
{
    private readonly Border _severityBorder;
    private readonly TextBlock _severityText;
    private readonly TextBlock _headlineText;
    private readonly TextBlock _statusText;
    private readonly ListBox _impactList;
    private readonly ListBox _remediationList;
    private readonly TextBlock _overallMetric;
    private readonly TextBlock _blockersMetric;
    private readonly TextBlock _warningsMetric;
    private readonly TextBlock _rootCauseMetric;
    private readonly ListBox _dependencyList;
    private readonly ListBox _findingsList;
    private readonly TextBlock _findingsCount;
    private readonly TextBlock _selectedTitle;
    private readonly TextBlock _selectedRootCause;
    private readonly TextBox _selectedDetail;
    private readonly Button _openRelatedButton;
    private readonly Button _stageActionButton;

    private UnifiedFindingsState _state =
        UnifiedFindingsState.Waiting;

    private string _selectedNavigationKey =
        string.Empty;

    public UnifiedFindingsView()
    {
        HorizontalAlignment =
            HorizontalAlignment.Stretch;

        VerticalAlignment =
            VerticalAlignment.Stretch;

        _severityText =
            new TextBlock
            {
                Text = "WAITING",
                FontWeight =
                    FontWeight.SemiBold
            };

        _severityBorder =
            new Border
            {
                HorizontalAlignment =
                    HorizontalAlignment.Left,
                Child =
                    _severityText,
                Classes =
                {
                    "badge"
                }
            };

        _headlineText =
            new TextBlock
            {
                Text =
                    "Refresh the environment to run analysis.",
                FontWeight =
                    FontWeight.SemiBold,
                TextWrapping =
                    TextWrapping.Wrap
            };

        _statusText =
            new TextBlock
            {
                Text =
                    "Waiting for provider evidence.",
                FontSize = 8.5,
                Classes =
                {
                    "dim"
                }
            };

        _impactList =
            NewDataList(
                minHeight: 54,
                maxHeight: 112);

        _impactList.SelectionChanged +=
            (_, _) =>
                SelectImpact();

        _remediationList =
            NewDataList(
                minHeight: 42,
                maxHeight: 98);

        _remediationList.SelectionChanged +=
            (_, _) =>
                SelectRemediation();

        _overallMetric =
            NewMetricValue(
                "WAITING");

        _blockersMetric =
            NewMetricValue(
                "0");

        _warningsMetric =
            NewMetricValue(
                "0");

        _rootCauseMetric =
            new TextBlock
            {
                Text = "--",
                FontSize = 13,
                FontWeight =
                    FontWeight.SemiBold,
                TextWrapping =
                    TextWrapping.Wrap
            };

        _dependencyList =
            NewDataList();

        _dependencyList.SelectionChanged +=
            (_, _) =>
                SelectDependency();

        _findingsList =
            NewDataList();

        _findingsList.SelectionChanged +=
            (_, _) =>
                SelectFinding();

        _findingsCount =
            new TextBlock
            {
                Text = "0 findings",
                Classes =
                {
                    "dim"
                }
            };

        _selectedTitle =
            new TextBlock
            {
                Text =
                    "No finding selected",
                FontWeight =
                    FontWeight.SemiBold
            };

        _selectedRootCause =
            new TextBlock
            {
                Text =
                    "No active fault detected",
                TextTrimming =
                    TextTrimming.CharacterEllipsis,
                Classes =
                {
                    "dim"
                }
            };

        _selectedDetail =
            new TextBox
            {
                IsReadOnly = true,
                AcceptsReturn = true,
                TextWrapping =
                    TextWrapping.Wrap,
                MinHeight = 70,
                Classes =
                {
                    "console",
                    "workspaceOutput"
                }
            };

        _openRelatedButton =
            new Button
            {
                Content =
                    "Open related",
                IsEnabled =
                    false
            };

        _openRelatedButton.Click +=
            (_, _) =>
                RequestSelectedNavigation();

        _stageActionButton =
            new Button
            {
                Content =
                    "Open selected",
                IsEnabled =
                    false,
                VerticalAlignment =
                    VerticalAlignment.Bottom,
                Margin =
                    new Thickness(
                        8,
                        0,
                        0,
                        0)
            };

        _stageActionButton.Click +=
            (_, _) =>
                RequestSelectedNavigation();

        Content =
            BuildWorkspace();

        Update(
            UnifiedFindingsState.Waiting);
    }

    public event EventHandler?
        HistoryRequested;

    public event EventHandler?
        AnalyzeRequested;

    public event EventHandler?
        EnvironmentRequested;

    public event EventHandler<UnifiedFindingsNavigationRequestedEventArgs>?
        NavigationRequested;

    public event EventHandler<UnifiedFindingsCopyRequestedEventArgs>?
        CopyReportRequested;

    public void Update(
        UnifiedFindingsState state)
    {
        _state =
            state;

        _severityText.Text =
            state.OverallLabel;

        _headlineText.Text =
            state.Headline;

        _statusText.Text =
            state.StatusText;

        _overallMetric.Text =
            state.OverallLabel;

        _blockersMetric.Text =
            state.Blockers.ToString();

        _warningsMetric.Text =
            state.Warnings.ToString();

        _rootCauseMetric.Text =
            state.RootCause;

        _selectedRootCause.Text =
            state.RootCause;

        _findingsCount.Text =
            $"{state.Findings.Count} " +
            (
                state.Findings.Count == 1
                    ? "finding"
                    : "findings"
            );

        ApplySeverity(
            state.Severity);

        _impactList.ItemsSource =
            state.Impact
                .Select(
                    BuildImpactItem)
                .ToArray();

        _remediationList.ItemsSource =
            state.Remediation
                .Select(
                    BuildRemediationItem)
                .ToArray();

        _dependencyList.ItemsSource =
            state.Dependencies
                .Select(
                    BuildDependencyItem)
                .ToArray();

        var findingRows =
            state.Findings.Count == 0
                ? new[]
                {
                    new UnifiedFindingRow(
                        UnifiedFindingSeverity.Healthy,
                        "Environment",
                        "No active findings.",
                        "Latest provider capture returned no warning-or-higher condition.",
                        "No impact detected.",
                        "Continue normal monitoring.",
                        "DashboardNav")
                }
                : state.Findings;

        _findingsList.ItemsSource =
            findingRows
                .Select(
                    BuildFindingItem)
                .ToArray();

        SelectInitialItem();
    }

    private Control BuildWorkspace()
    {
        var content =
            new StackPanel
            {
                Spacing = 8,
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
            BuildEnvironmentContext());

        content.Children.Add(
            BuildRemediation());

        content.Children.Add(
            BuildMetrics());

        content.Children.Add(
            BuildEvidenceGrid());

        content.Children.Add(
            BuildSelectedDetail());

        return
            new ScrollViewer
            {
                VerticalScrollBarVisibility =
                    global::Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility =
                    global::Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled,
                Content =
                    content
            };
    }

    private Control BuildHeader()
    {
        var title =
            new StackPanel();

        title.Children.Add(
            new TextBlock
            {
                Text =
                    "Control plane intelligence",
                FontSize = 18,
                Classes =
                {
                    "sectionTitle"
                }
            });

        title.Children.Add(
            new TextBlock
            {
                Text =
                    "Root cause, dependency impact and the safest next control from live GraveOps telemetry.",
                Classes =
                {
                    "pageSubtitle"
                }
            });

        var history =
            new Button
            {
                Content =
                    "State history",
                Margin =
                    new Thickness(
                        0,
                        0,
                        5,
                        0)
            };

        history.Click +=
            (_, _) =>
                HistoryRequested?.Invoke(
                    this,
                    EventArgs.Empty);

        Grid.SetColumn(
            history,
            1);

        var analyze =
            new Button
            {
                Content =
                    "Analyze now",
                Classes =
                {
                    "primary"
                }
            };

        analyze.Click +=
            (_, _) =>
                AnalyzeRequested?.Invoke(
                    this,
                    EventArgs.Empty);

        Grid.SetColumn(
            analyze,
            2);

        var grid =
            new Grid
            {
                ColumnDefinitions =
                    new ColumnDefinitions(
                        "*,Auto,Auto")
            };

        grid.Children.Add(
            title);

        grid.Children.Add(
            history);

        grid.Children.Add(
            analyze);

        return grid;
    }

    private Control BuildEnvironmentContext()
    {
        var heading =
            new StackPanel();

        heading.Children.Add(
            NewSectionTitle(
                "Environment context"));

        heading.Children.Add(
            NewSubtitle(
                "Fleet-wide impact before selected-host root-cause analysis."));

        var dashboard =
            new Button
            {
                Content =
                    "Environment dashboard"
            };

        dashboard.Click +=
            (_, _) =>
                EnvironmentRequested?.Invoke(
                    this,
                    EventArgs.Empty);

        Grid.SetColumn(
            dashboard,
            1);

        var header =
            new Grid
            {
                ColumnDefinitions =
                    new ColumnDefinitions(
                        "*,Auto")
            };

        header.Children.Add(
            heading);

        header.Children.Add(
            dashboard);

        var environment =
            new StackPanel();

        environment.Children.Add(
            NewEyebrow(
                "ENVIRONMENT"));

        environment.Children.Add(
            _severityBorder);

        var environmentCard =
            new Border
            {
                Child =
                    environment,
                Classes =
                {
                    "inset"
                }
            };

        var summary =
            new StackPanel
            {
                VerticalAlignment =
                    VerticalAlignment.Center
            };

        summary.Children.Add(
            _headlineText);

        summary.Children.Add(
            _statusText);

        Grid.SetColumn(
            summary,
            1);

        Grid.SetColumn(
            _impactList,
            2);

        _impactList.Width =
            340;

        var context =
            new Grid
            {
                ColumnDefinitions =
                    new ColumnDefinitions(
                        "170,*,Auto"),
                ColumnSpacing = 8
            };

        context.Children.Add(
            environmentCard);

        context.Children.Add(
            summary);

        context.Children.Add(
            _impactList);

        Grid.SetRow(
            context,
            1);

        var body =
            new Grid
            {
                RowDefinitions =
                    new RowDefinitions(
                        "Auto,*"),
                RowSpacing = 5
            };

        body.Children.Add(
            header);

        body.Children.Add(
            context);

        return
            Module(
                body,
                minHeight: 108);
    }

    private Control BuildRemediation()
    {
        var text =
            new StackPanel();

        text.Children.Add(
            NewSectionTitle(
                "Guided remediation"));

        text.Children.Add(
            NewSubtitle(
                "Upstream-first troubleshooting from environment ownership and live telemetry."));

        _remediationList.Margin =
            new Thickness(
                0,
                6,
                0,
                0);

        text.Children.Add(
            _remediationList);

        var grid =
            new Grid
            {
                ColumnDefinitions =
                    new ColumnDefinitions(
                        "*,Auto")
            };

        grid.Children.Add(
            text);

        Grid.SetColumn(
            _stageActionButton,
            1);

        grid.Children.Add(
            _stageActionButton);

        return
            Module(
                grid,
                minHeight: 94);
    }

    private Control BuildMetrics()
    {
        var grid =
            new Grid
            {
                ColumnDefinitions =
                    new ColumnDefinitions(
                        "*,*,*,*"),
                ColumnSpacing = 8
            };

        grid.Children.Add(
            Metric(
                "OVERALL",
                _overallMetric));

        var blockers =
            Metric(
                "BLOCKERS",
                _blockersMetric);

        Grid.SetColumn(
            blockers,
            1);

        grid.Children.Add(
            blockers);

        var warnings =
            Metric(
                "WARNINGS",
                _warningsMetric);

        Grid.SetColumn(
            warnings,
            2);

        grid.Children.Add(
            warnings);

        var rootCause =
            Metric(
                "ROOT CAUSE",
                _rootCauseMetric);

        Grid.SetColumn(
            rootCause,
            3);

        grid.Children.Add(
            rootCause);

        return grid;
    }

    private Control BuildEvidenceGrid()
    {
        var dependencies =
            new Grid
            {
                RowDefinitions =
                    new RowDefinitions(
                        "Auto,*"),
                RowSpacing = 5
            };

        var dependencyHeading =
            new StackPanel();

        dependencyHeading.Children.Add(
            NewSectionTitle(
                "Dependency state"));

        dependencyHeading.Children.Add(
            NewSubtitle(
                "Read upstream to downstream. A higher fault can explain several lower-level symptoms."));

        dependencies.Children.Add(
            dependencyHeading);

        Grid.SetRow(
            _dependencyList,
            1);

        dependencies.Children.Add(
            _dependencyList);

        var findings =
            new Grid
            {
                RowDefinitions =
                    new RowDefinitions(
                        "Auto,*"),
                RowSpacing = 5
            };

        var findingHeading =
            new StackPanel();

        findingHeading.Children.Add(
            NewSectionTitle(
                "Priority findings"));

        findingHeading.Children.Add(
            NewSubtitle(
                "Ranked by impact. Healthy context stays compact."));

        var findingHeader =
            new Grid
            {
                ColumnDefinitions =
                    new ColumnDefinitions(
                        "*,Auto")
            };

        findingHeader.Children.Add(
            findingHeading);

        Grid.SetColumn(
            _findingsCount,
            1);

        findingHeader.Children.Add(
            _findingsCount);

        findings.Children.Add(
            findingHeader);

        Grid.SetRow(
            _findingsList,
            1);

        findings.Children.Add(
            _findingsList);

        var grid =
            new Grid
            {
                ColumnDefinitions =
                    new ColumnDefinitions(
                        "0.9*,1.1*"),
                ColumnSpacing = 8,
                MinHeight = 270
            };

        grid.Children.Add(
            Module(
                dependencies));

        var findingsModule =
            Module(
                findings);

        Grid.SetColumn(
            findingsModule,
            1);

        grid.Children.Add(
            findingsModule);

        return grid;
    }

    private Control BuildSelectedDetail()
    {
        var title =
            new StackPanel();

        title.Children.Add(
            _selectedTitle);

        title.Children.Add(
            _selectedRootCause);

        var copy =
            new Button
            {
                Content =
                    "Copy report"
            };

        copy.Click +=
            (_, _) =>
                CopyReportRequested?.Invoke(
                    this,
                    new UnifiedFindingsCopyRequestedEventArgs(
                        UnifiedFindingsReport.Build(
                            _state)));

        var grid =
            new Grid
            {
                ColumnDefinitions =
                    new ColumnDefinitions(
                        "180,*,Auto,Auto"),
                ColumnSpacing = 8
            };

        grid.Children.Add(
            title);

        Grid.SetColumn(
            _selectedDetail,
            1);

        grid.Children.Add(
            _selectedDetail);

        Grid.SetColumn(
            _openRelatedButton,
            2);

        grid.Children.Add(
            _openRelatedButton);

        Grid.SetColumn(
            copy,
            3);

        grid.Children.Add(
            copy);

        return
            Module(
                grid,
                minHeight: 88);
    }

    private ListBoxItem BuildImpactItem(
        UnifiedImpactRow row)
    {
        var grid =
            new Grid
            {
                ColumnDefinitions =
                    new ColumnDefinitions(
                        "100,100,*"),
                ColumnSpacing = 6
            };

        grid.Children.Add(
            Cell(
                row.Target,
                bold: true));

        var component =
            Cell(
                row.Component);

        Grid.SetColumn(
            component,
            1);

        grid.Children.Add(
            component);

        var issue =
            Cell(
                row.Issue,
                trim: true);

        Grid.SetColumn(
            issue,
            2);

        grid.Children.Add(
            issue);

        return Item(
            row,
            grid);
    }

    private ListBoxItem BuildRemediationItem(
        UnifiedRemediationRow row)
    {
        var grid =
            new Grid
            {
                ColumnDefinitions =
                    new ColumnDefinitions(
                        "32,90,135,*,*"),
                ColumnSpacing = 6
            };

        grid.Children.Add(
            Cell(
                row.Step.ToString()));

        var severity =
            SeverityCell(
                row.SeverityLabel,
                row.Severity);

        Grid.SetColumn(
            severity,
            1);

        grid.Children.Add(
            severity);

        var component =
            Cell(
                row.Component,
                bold: true);

        Grid.SetColumn(
            component,
            2);

        grid.Children.Add(
            component);

        var why =
            Cell(
                row.Why,
                trim: true);

        Grid.SetColumn(
            why,
            3);

        grid.Children.Add(
            why);

        var next =
            Cell(
                row.NextStep,
                trim: true,
                dim: true);

        Grid.SetColumn(
            next,
            4);

        grid.Children.Add(
            next);

        return Item(
            row,
            grid);
    }

    private ListBoxItem BuildDependencyItem(
        UnifiedDependencyRow row)
    {
        var grid =
            new Grid
            {
                ColumnDefinitions =
                    new ColumnDefinitions(
                        "34,120,90,90,*"),
                ColumnSpacing = 6
            };

        grid.Children.Add(
            Cell(
                row.Order.ToString()));

        var component =
            Cell(
                row.Component,
                bold: true);

        Grid.SetColumn(
            component,
            1);

        grid.Children.Add(
            component);

        var state =
            Cell(
                row.State);

        Grid.SetColumn(
            state,
            2);

        grid.Children.Add(
            state);

        var severity =
            SeverityCell(
                row.SeverityLabel,
                row.Severity);

        Grid.SetColumn(
            severity,
            3);

        grid.Children.Add(
            severity);

        var evidence =
            Cell(
                row.Evidence,
                trim: true);

        Grid.SetColumn(
            evidence,
            4);

        grid.Children.Add(
            evidence);

        return Item(
            row,
            grid);
    }

    private ListBoxItem BuildFindingItem(
        UnifiedFindingRow row)
    {
        var grid =
            new Grid
            {
                ColumnDefinitions =
                    new ColumnDefinitions(
                        "90,130,*,*"),
                ColumnSpacing = 6
            };

        grid.Children.Add(
            SeverityCell(
                row.SeverityLabel,
                row.Severity));

        var component =
            Cell(
                row.Component,
                bold: true);

        Grid.SetColumn(
            component,
            1);

        grid.Children.Add(
            component);

        var problem =
            Cell(
                row.Problem,
                trim: true);

        Grid.SetColumn(
            problem,
            2);

        grid.Children.Add(
            problem);

        var impact =
            Cell(
                row.Impact,
                trim: true,
                dim: true);

        Grid.SetColumn(
            impact,
            3);

        grid.Children.Add(
            impact);

        return Item(
            row,
            grid);
    }

    private void SelectInitialItem()
    {
        _selectedNavigationKey =
            string.Empty;

        _openRelatedButton.IsEnabled =
            false;

        _stageActionButton.IsEnabled =
            false;

        if (_findingsList.ItemCount > 0)
        {
            _findingsList.SelectedIndex =
                0;

            return;
        }

        if (_remediationList.ItemCount > 0)
        {
            _remediationList.SelectedIndex =
                0;

            return;
        }

        if (_dependencyList.ItemCount > 0)
        {
            _dependencyList.SelectedIndex =
                0;

            return;
        }

        _selectedTitle.Text =
            "No finding selected";

        _selectedDetail.Text =
            string.Empty;
    }

    private void SelectImpact()
    {
        if (Tagged<UnifiedImpactRow>(
                _impactList) is not { } row)
        {
            return;
        }

        SetSelection(
            $"{row.Target} \u00B7 {row.Component}",
            $"{row.Issue}\n\n" +
            $"Evidence \u00B7 {row.Evidence}\n\n" +
            $"Impact \u00B7 {row.Impact}\n\n" +
            $"Next \u00B7 {row.NextStep}",
            row.NavigationKey);
    }

    private void SelectRemediation()
    {
        if (Tagged<UnifiedRemediationRow>(
                _remediationList) is not { } row)
        {
            return;
        }

        SetSelection(
            $"Step {row.Step} \u00B7 {row.Component}",
            $"Why \u00B7 {row.Why}\n\n" +
            $"Next \u00B7 {row.NextStep}",
            row.NavigationKey);
    }

    private void SelectDependency()
    {
        if (Tagged<UnifiedDependencyRow>(
                _dependencyList) is not { } row)
        {
            return;
        }

        SetSelection(
            $"{row.Order}. {row.Component} \u00B7 {row.State}",
            $"{row.Evidence}\n\n" +
            $"Impact \u00B7 {row.Impact}\n\n" +
            $"Next \u00B7 {row.NextStep}",
            row.NavigationKey);
    }

    private void SelectFinding()
    {
        if (Tagged<UnifiedFindingRow>(
                _findingsList) is not { } row)
        {
            return;
        }

        SetSelection(
            $"{row.SeverityLabel} \u00B7 {row.Component}",
            $"{row.Problem}\n\n" +
            $"Evidence \u00B7 {row.Evidence}\n\n" +
            $"Impact \u00B7 {row.Impact}\n\n" +
            $"Next \u00B7 {row.NextStep}",
            row.NavigationKey);
    }

    private void SetSelection(
        string title,
        string detail,
        string navigationKey)
    {
        _selectedNavigationKey =
            navigationKey;

        _selectedTitle.Text =
            title;

        _selectedRootCause.Text =
            _state.RootCause;

        _selectedDetail.Text =
            detail;

        var enabled =
            !string.IsNullOrWhiteSpace(
                navigationKey);

        _openRelatedButton.IsEnabled =
            enabled;

        _stageActionButton.IsEnabled =
            enabled;
    }

    private void RequestSelectedNavigation()
    {
        if (string.IsNullOrWhiteSpace(
                _selectedNavigationKey))
        {
            return;
        }

        NavigationRequested?.Invoke(
            this,
            new UnifiedFindingsNavigationRequestedEventArgs(
                _selectedNavigationKey));
    }

    private void ApplySeverity(
        UnifiedFindingSeverity severity)
    {
        _severityBorder.Classes.Set(
            "healthy",
            severity <=
                UnifiedFindingSeverity.Healthy);

        _severityBorder.Classes.Set(
            "warning",
            severity ==
                UnifiedFindingSeverity.Warning);

        _severityBorder.Classes.Set(
            "error",
            severity >=
                UnifiedFindingSeverity.Error);

        var key =
            severity switch
            {
                UnifiedFindingSeverity.Healthy =>
                    "SuccessBrush",
                UnifiedFindingSeverity.Warning =>
                    "WarnBrush",
                UnifiedFindingSeverity.Error or
                UnifiedFindingSeverity.Critical =>
                    "DangerBrush",
                _ =>
                    "AccentBrush"
            };

        if (this.TryFindResource(
                key,
                ActualThemeVariant,
                out var resource) &&
            resource is IBrush brush)
        {
            _severityText.Foreground =
                brush;
        }
    }

    private static ListBox NewDataList(
        double minHeight = 0,
        double maxHeight =
            double.PositiveInfinity) =>
        new()
        {
            MinHeight =
                minHeight,
            MaxHeight =
                maxHeight,
            Classes =
            {
                "dataList"
            }
        };

    private static Border Module(
        Control child,
        double minHeight = 0) =>
        new()
        {
            Child =
                child,
            MinHeight =
                minHeight,
            Classes =
            {
                "module",
                "adaptive"
            }
        };

    private static Border Metric(
        string label,
        Control value)
    {
        var stack =
            new StackPanel();

        stack.Children.Add(
            NewEyebrow(
                label));

        stack.Children.Add(
            value);

        return
            new Border
            {
                Child =
                    stack,
                Classes =
                {
                    "metric"
                }
            };
    }

    private static TextBlock NewMetricValue(
        string text) =>
        new()
        {
            Text =
                text,
            Classes =
            {
                "metricValue"
            }
        };

    private static TextBlock NewEyebrow(
        string text) =>
        new()
        {
            Text =
                text,
            Classes =
            {
                "eyebrow"
            }
        };

    private static TextBlock NewSectionTitle(
        string text) =>
        new()
        {
            Text =
                text,
            Classes =
            {
                "sectionTitle"
            }
        };

    private static TextBlock NewSubtitle(
        string text) =>
        new()
        {
            Text =
                text,
            Classes =
            {
                "pageSubtitle"
            }
        };

    private TextBlock SeverityCell(
        string text,
        UnifiedFindingSeverity severity)
    {
        var cell =
            Cell(
                text,
                bold: true);

        var key =
            severity switch
            {
                UnifiedFindingSeverity.Healthy =>
                    "SuccessBrush",
                UnifiedFindingSeverity.Warning =>
                    "WarnBrush",
                UnifiedFindingSeverity.Error or
                UnifiedFindingSeverity.Critical =>
                    "DangerBrush",
                _ =>
                    "AccentBrush"
            };

        if (this.TryFindResource(
                key,
                ActualThemeVariant,
                out var resource) &&
            resource is IBrush brush)
        {
            cell.Foreground =
                brush;
        }

        return cell;
    }

    private static TextBlock Cell(
        string text,
        bool bold = false,
        bool trim = false,
        bool dim = false)
    {
        var cell =
            new TextBlock
            {
                Text =
                    text,
                FontWeight =
                    bold
                        ? FontWeight.SemiBold
                        : FontWeight.Normal
            };

        if (trim)
        {
            cell.TextTrimming =
                TextTrimming.CharacterEllipsis;
        }

        if (dim)
        {
            cell.Classes.Add(
                "dim");
        }

        return cell;
    }

    private static ListBoxItem Item(
        object tag,
        Control content) =>
        new()
        {
            Tag =
                tag,
            Content =
                content
        };

    private static T? Tagged<T>(
        ListBox list)
        where T : class =>
        (
            list.SelectedItem as
            ListBoxItem
        )?.Tag as T;
}