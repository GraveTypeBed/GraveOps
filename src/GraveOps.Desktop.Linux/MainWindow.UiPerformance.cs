using System.Globalization;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;

namespace GraveOps.Desktop.Linux;

public partial class MainWindow
{
    private Flyout? _uiPerformanceFlyout;
    private CheckBox? _uiPerformanceEnabledEditor;
    private CheckBox? _uiPerformanceSkipEditor;
    private TextBox? _uiPerformanceBudgetEditor;
    private TextBox? _uiPerformanceLimitEditor;
    private TextBox? _uiPerformanceRetainEditor;

    private void InitializeUiDataPipeline()
    {
        _desktopArchitecture.Start();
        UiProjection.BeginRefresh();
    }

    private void BeginUiRefreshProjection()
    {
        UiProjection.BeginRefresh();
    }

    private void ProjectCurrentPageIncrementally(
        string navigationName,
        bool navigationActivation,
        bool force)
    {
        if (_snapshot is null ||
            _backup is null ||
            _analysis is null)
        {
            return;
        }

        var key = string.IsNullOrWhiteSpace(navigationName)
            ? _unifiedCurrentNavigation
            : navigationName;
        var signature = BuildCurrentPageProjectionSignature(key);
        UiProjection.Project(
            UiProjectionArea.CurrentPage,
            key,
            signature,
            CurrentPageProjectionItemCount(key),
            () => ProjectCurrentPageFromSnapshot(
                key,
                navigationActivation),
            force || navigationActivation);
    }

    private string BuildCurrentPageProjectionSignature(string navigationName)
    {
        if (_snapshot is null ||
            _backup is null ||
            _analysis is null)
        {
            return string.Empty;
        }

        var values = new List<string?>
        {
            navigationName,
            _snapshot.Hostname,
            _snapshot.OperatingSystem,
            _snapshot.SystemState,
            _analysis.Label,
            _analysis.Headline,
            _backup.State,
            _backup.Summary,
            _policyEvaluation?.Active.Count.ToString(
                CultureInfo.InvariantCulture),
            _policyEvaluation?.Muted.Count.ToString(
                CultureInfo.InvariantCulture),
            _verifiedRemediationPlans.Count.ToString(
                CultureInfo.InvariantCulture)
        };
        values.AddRange(
            LinuxOpsAnalyzer.UniqueServices(_snapshot)
                .Select(item =>
                    $"svc:{item.Unit}:{item.ActiveState}:{item.SubState}:{item.UnitFileState}"));
        values.AddRange(
            _snapshot.Containers.Select(item =>
                $"ctr:{item.Name}:{item.State}:{item.Status}"));
        values.AddRange(
            LinuxOpsAnalyzer.OperationalStorage(_snapshot)
                .Select(item =>
                    $"vol:{item.MountPoint}:{item.PercentUsed}:{item.Available}"));
        values.AddRange(
            _integrations.Select(item =>
                $"app:{item.InstanceKey}:{item.State}:{(int)item.Severity}:{item.IsVisible}"));
        values.AddRange(
            _logs.Take(80).Select(item =>
                $"log:{item.Source}:{(int)item.Severity}:{item.Count}:{item.LastSeen.UtcDateTime.Ticks}"));
        values.AddRange(
            _lifecycle.Select(item =>
                $"life:{item.Order}:{item.State}:{(int)item.Severity}"));
        return UiProjection.Signature(values);
    }

    private int CurrentPageProjectionItemCount(string navigationName)
    {
        if (_snapshot is null)
            return 0;
        return navigationName switch
        {
            "DashboardNav" =>
                _snapshot.Services.Count +
                _snapshot.Containers.Count +
                _snapshot.Storage.Count +
                _integrations.Count,
            "LogsNav" => _logs.Count,
            "ServicesNav" => _snapshot.Services.Count,
            "DockerNav" => _snapshot.Containers.Count,
            "StorageNav" => _snapshot.Storage.Count,
            "HistoryNav" => _analysis?.Findings.Count ?? 0,
            "LifecycleNav" => _lifecycle.Count,
            "MediaHubNav" => _integrations.Count,
            _ => _integrations.Count + _logs.Count
        };
    }

    private void PopulateUiPerformanceSettings()
    {
        var settings = UiProjection.Settings;
        var summary = UiProjection.Summary();
        Get<TextBlock>("SettingsUiPerformanceSummaryText").Text =
            $"{(settings.Enabled ? "Enabled" : "Disabled")} · " +
            $"{summary.Skipped} unchanged projection(s) skipped · " +
            $"p95 {summary.P95ApplyMilliseconds} ms · " +
            $"{summary.OverBudgetCount} over {settings.SlowApplyMilliseconds} ms";
    }

    private void UiPerformancePolicyButton_OnClick(
        object? sender,
        RoutedEventArgs e)
    {
        if (sender is not Control anchor)
            return;
        var settings = UiProjection.Settings;
        _uiPerformanceEnabledEditor = new CheckBox
        {
            Content = "Enable incremental UI projection",
            IsChecked = settings.Enabled
        };
        _uiPerformanceSkipEditor = new CheckBox
        {
            Content = "Skip unchanged current-page projections",
            IsChecked = settings.SkipUnchangedProjection
        };
        _uiPerformanceBudgetEditor = BuildUiPerformanceNumberEditor(
            settings.SlowApplyMilliseconds);
        _uiPerformanceLimitEditor = BuildUiPerformanceNumberEditor(
            settings.LongListLimit);
        _uiPerformanceRetainEditor = BuildUiPerformanceNumberEditor(
            settings.RetainedMetrics);

        var body = new StackPanel
        {
            Width = 560,
            Spacing = 8
        };
        body.Children.Add(new TextBlock
        {
            Text = "UI performance pipeline",
            FontSize = 18,
            FontWeight = FontWeight.SemiBold
        });
        body.Children.Add(new TextBlock
        {
            Text = "Stable projection signatures prevent unchanged pages from being rebuilt. Metrics are retained locally and never interrupt refresh work.",
            TextWrapping = TextWrapping.Wrap,
            Classes = { "muted" }
        });
        body.Children.Add(_uiPerformanceEnabledEditor);
        body.Children.Add(_uiPerformanceSkipEditor);
        AddUiPerformanceEditorRow(
            body,
            "Slow UI apply threshold (ms)",
            _uiPerformanceBudgetEditor);
        AddUiPerformanceEditorRow(
            body,
            "Long-list projection limit",
            _uiPerformanceLimitEditor);
        AddUiPerformanceEditorRow(
            body,
            "Retained performance samples",
            _uiPerformanceRetainEditor);
        body.Children.Add(new TextBlock
        {
            Text = _desktopArchitecture.Summary(),
            TextWrapping = TextWrapping.Wrap,
            Classes = { "muted" }
        });
        body.Children.Add(new TextBlock
        {
            Text = $"Settings: {UiProjection.SettingsPath}\nMetrics: {UiProjection.MetricsPath}",
            TextWrapping = TextWrapping.Wrap,
            Classes = { "dim" }
        });

        var footer = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 7,
            HorizontalAlignment = HorizontalAlignment.Right
        };
        var reset = new Button { Content = "Reset signatures" };
        reset.Click += (_, _) =>
        {
            UiProjection.Invalidate();
            PopulateUiPerformanceSettings();
        };
        var save = new Button
        {
            Content = "Save",
            Classes = { "primary" }
        };
        save.Click += (_, _) => SaveUiPerformanceSettings();
        var close = new Button { Content = "Close" };
        close.Click += (_, _) => _uiPerformanceFlyout?.Hide();
        footer.Children.Add(reset);
        footer.Children.Add(save);
        footer.Children.Add(close);
        body.Children.Add(footer);

        _uiPerformanceFlyout = new Flyout { Content = body };
        _uiPerformanceFlyout.FlyoutPresenterClasses.Add(
            "dashboardInfoFlyout");
        _uiPerformanceFlyout.ShowAt(anchor);
    }

    private static TextBox BuildUiPerformanceNumberEditor(int value) =>
        new()
        {
            Text = value.ToString(CultureInfo.InvariantCulture),
            Width = 110,
            HorizontalContentAlignment = HorizontalAlignment.Right
        };

    private static void AddUiPerformanceEditorRow(
        StackPanel panel,
        string label,
        Control editor)
    {
        var row = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,Auto"),
            ColumnSpacing = 8
        };
        row.Children.Add(new TextBlock
        {
            Text = label,
            VerticalAlignment = VerticalAlignment.Center
        });
        Grid.SetColumn(editor, 1);
        row.Children.Add(editor);
        panel.Children.Add(row);
    }

    private void SaveUiPerformanceSettings()
    {
        if (_uiPerformanceEnabledEditor is null ||
            _uiPerformanceSkipEditor is null ||
            _uiPerformanceBudgetEditor is null ||
            _uiPerformanceLimitEditor is null ||
            _uiPerformanceRetainEditor is null)
        {
            return;
        }

        var current = UiProjection.Settings;
        current.Enabled = _uiPerformanceEnabledEditor.IsChecked == true;
        current.SkipUnchangedProjection =
            _uiPerformanceSkipEditor.IsChecked == true;
        current.SlowApplyMilliseconds = ParseUiPerformanceValue(
            _uiPerformanceBudgetEditor,
            current.SlowApplyMilliseconds);
        current.LongListLimit = ParseUiPerformanceValue(
            _uiPerformanceLimitEditor,
            current.LongListLimit);
        current.RetainedMetrics = ParseUiPerformanceValue(
            _uiPerformanceRetainEditor,
            current.RetainedMetrics);
        UiProjection.SetSettings(current);
        PopulateUiPerformanceSettings();
        _uiPerformanceFlyout?.Hide();
        ProjectCurrentPageIncrementally(
            _unifiedCurrentNavigation,
            navigationActivation: false,
            force: true);
    }

    private static int ParseUiPerformanceValue(
        TextBox editor,
        int fallback) =>
        int.TryParse(
            editor.Text,
            NumberStyles.Integer,
            CultureInfo.InvariantCulture,
            out var parsed)
            ? parsed
            : fallback;

    private void DisposeUiDataPipeline()
    {
        _uiPerformanceFlyout?.Hide();
        _desktopArchitecture.Dispose();
    }
}
