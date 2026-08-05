using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Layout;

namespace GraveOps.Desktop.Linux;

public partial class MainWindow
{
    private int _signalQualityExcludedGroups;
    private readonly Dictionary<string, ComboBox> _signalQualityModeEditors =
        new(StringComparer.OrdinalIgnoreCase);
    private Flyout? _signalQualityFlyout;
    private CheckBox? _signalQualityEnabledEditor;
    private CheckBox? _signalQualityExpectedEditor;
    private TextBox? _signalQualityHostStaleEditor;
    private TextBox? _signalQualityApplicationStaleEditor;
    private TextBox? _signalQualityBackupStaleEditor;
    private long _signalQualityGeneration =
        DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

    private void RecordRoutineControlPlaneActivity(
        string kind,
        string target,
        string title,
        string detail,
        string navigationName,
        TimeSpan deduplicationWindow,
        bool unread = false)
    {
        var now = DateTimeOffset.Now;
        var duplicate = _controlPlane.State.Activities.Any(row =>
            row.Kind.Equals(kind, StringComparison.OrdinalIgnoreCase) &&
            row.Target.Equals(target, StringComparison.OrdinalIgnoreCase) &&
            row.Title.Equals(title, StringComparison.OrdinalIgnoreCase) &&
            now - row.Timestamp >= TimeSpan.Zero &&
            now - row.Timestamp <= deduplicationWindow);
        if (duplicate)
            return;

        _controlPlane.State.RecordActivity(
            kind,
            target,
            title,
            detail,
            navigationName,
            unread);
    }

    private IReadOnlyList<SignalQualityObservation>
        SignalQualityRefreshSucceeded(
            IReadOnlyList<OpsIntegration> integrations)
    {
        var hostId = _controlPlane.ActiveProfile.Id;
        var now = DateTimeOffset.UtcNow;
        SignalQualityStore.MarkRefreshSuccess(hostId, now);
        _signalQualityGeneration++;
        var settings = SignalQualityStore.GetSettings();
        if (!settings.Enabled)
            return Array.Empty<SignalQualityObservation>();
        var observations = HealthPolicy.Evaluate(
            integrations,
            SignalQualityStore.GetRefreshState(hostId),
            settings,
            now);
        RecordSignalQualityTransitions(
            SignalQualityStore.Reconcile(
                hostId,
                _signalQualityGeneration,
                observations,
                now));
        return observations;
    }

    private void SignalQualityRefreshFailed(Exception exception)
    {
        var hostId = _controlPlane.ActiveProfile.Id;
        var now = DateTimeOffset.UtcNow;
        SignalQualityStore.MarkRefreshFailure(
            hostId,
            now,
            exception.Message);
        _signalQualityGeneration++;
        var settings = SignalQualityStore.GetSettings();
        if (!settings.Enabled)
        {
            if (_rawAnalysis is not null)
            {
                _rawAnalysis = HealthPolicy.MergeAnalysis(
                    _rawAnalysis,
                    Array.Empty<SignalQualityObservation>(),
                    settings,
                    _integrations);
                ApplyFindingPolicies();
            }
            PopulateSignalQualitySettings();
            return;
        }
        var observations = HealthPolicy.Evaluate(
            _integrations,
            SignalQualityStore.GetRefreshState(hostId),
            settings,
            now);
        RecordSignalQualityTransitions(
            SignalQualityStore.Reconcile(
                hostId,
                _signalQualityGeneration,
                observations,
                now));

        if (_rawAnalysis is not null)
        {
            _rawAnalysis = HealthPolicy.MergeAnalysis(
                _rawAnalysis,
                observations,
                settings,
                _integrations);
            ApplyFindingPolicies();
        }

        if (_unifiedCurrentNavigation.Equals(
                "DashboardNav",
                StringComparison.Ordinal) &&
            _snapshot is not null &&
            _backup is not null &&
            _analysis is not null)
        {
            PopulateUnifiedDashboard();
        }
        PopulateSignalQualitySettings();
    }

    private IReadOnlyList<UnifiedDashboardCard>
        ApplySignalQualityToDashboardCards(
            IReadOnlyList<UnifiedDashboardCard> cards)
    {
        var hostId = _controlPlane.ActiveProfile.Id;
        var projected = HealthPolicy.ApplyCards(
            cards,
            new SignalQualityDashboardContext(
                hostId,
                DateTimeOffset.UtcNow,
                SignalQualityStore.GetRefreshState(hostId),
                SignalQualityStore.GetSettings(),
                _integrations));
        return AttachVerifiedRemediationActions(
            projected);
    }

    private void RecordSignalQualityTransitions(
        IReadOnlyList<SignalQualityTransition> transitions)
    {
        foreach (var transition in transitions)
        {
            var incident = transition.Incident;
            if (transition.Kind == SignalQualityTransitionKind.Opened)
            {
                RecordRoutineControlPlaneActivity(
                    "Failure",
                    incident.Resource,
                    incident.Problem,
                    $"{incident.Evidence} · first seen {incident.FirstSeen.ToLocalTime():g}",
                    incident.NavigationName,
                    TimeSpan.Zero,
                    unread: true);
                continue;
            }

            var duration = incident.RecoveredAt is { } recovered
                ? recovered - incident.FirstSeen
                : TimeSpan.Zero;
            RecordRoutineControlPlaneActivity(
                "Recovery",
                incident.Resource,
                $"{incident.Component} recovered",
                $"{incident.Problem} cleared after {HealthPolicy.FormatAge(duration)} · " +
                $"{incident.OccurrenceCount} observation(s)",
                incident.NavigationName,
                TimeSpan.Zero,
                unread: false);
        }
    }

    private void PopulateSignalQualitySummary()
    {
        Get<TextBlock>("ServerSignalQualityText").Text =
            HealthPolicy.Summary(_signalQualityExcludedGroups);
    }

    private void PopulateSignalQualitySettings()
    {
        var summary = this.FindControl<TextBlock>("SettingsSignalQualitySummaryText");
        if (summary is null)
            return;
        var hostId = _controlPlane.ActiveProfile.Id;
        var settings = SignalQualityStore.GetSettings();
        var active = SignalQualityStore.ActiveIncidents(hostId).Count;
        var recovered = SignalQualityStore.RecentRecoveries(hostId).Count;
        summary.Text = settings.Enabled
            ? $"Signal quality active · {active} active · {recovered} recent recovery · " +
              $"stale {settings.HostStaleMinutes}/{settings.ApplicationStaleMinutes}/{settings.BackupStaleMinutes}m"
            : "Signal quality disabled · raw telemetry remains visible";
    }

    private void SignalQualityPolicyButton_OnClick(
        object? sender,
        RoutedEventArgs e)
    {
        if (sender is not Button anchor)
            return;

        var settings = SignalQualityStore.GetSettings();
        _signalQualityModeEditors.Clear();
        _signalQualityEnabledEditor = new CheckBox
        {
            Content = "Enable signal-quality evaluation",
            IsChecked = settings.Enabled
        };
        _signalQualityExpectedEditor = new CheckBox
        {
            Content = "Evaluate expected services",
            IsChecked = settings.EvaluateExpectedServices
        };
        _signalQualityHostStaleEditor = StaleEditor(settings.HostStaleMinutes);
        _signalQualityApplicationStaleEditor = StaleEditor(settings.ApplicationStaleMinutes);
        _signalQualityBackupStaleEditor = StaleEditor(settings.BackupStaleMinutes);

        var content = new StackPanel
        {
            Spacing = 9,
            Width = 600
        };
        content.Children.Add(new TextBlock
        {
            Text = "Signal quality",
            FontSize = 18,
            FontWeight = Avalonia.Media.FontWeight.SemiBold
        });
        content.Children.Add(new TextBlock
        {
            Text = "Expected-service ownership, stale telemetry, finding deduplication and recovery history.",
            TextWrapping = Avalonia.Media.TextWrapping.Wrap,
            Classes = { "muted" }
        });
        content.Children.Add(_signalQualityEnabledEditor);
        content.Children.Add(_signalQualityExpectedEditor);

        var staleGrid = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,Auto,Auto,Auto"),
            ColumnSpacing = 6
        };
        staleGrid.Children.Add(new TextBlock
        {
            Text = "Stale after (minutes)",
            VerticalAlignment = VerticalAlignment.Center
        });
        AddStaleColumn(staleGrid, _signalQualityHostStaleEditor, "Host", 1);
        AddStaleColumn(staleGrid, _signalQualityApplicationStaleEditor, "Apps", 2);
        AddStaleColumn(staleGrid, _signalQualityBackupStaleEditor, "Backup", 3);
        content.Children.Add(staleGrid);

        content.Children.Add(new TextBlock
        {
            Text = "Service expectations",
            FontWeight = Avalonia.Media.FontWeight.SemiBold
        });
        var servicePanel = new StackPanel { Spacing = 5 };
        var integrations = _integrations
            .Where(item => item.IsVisible)
            .GroupBy(item =>
                string.IsNullOrWhiteSpace(item.InstanceKey)
                    ? $"{item.Name}|{item.DisplayName}|{item.Kind}"
                    : item.InstanceKey,
                StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .OrderBy(item => item.Name)
            .ThenBy(item => item.DisplayName)
            .ToArray();

        foreach (var integration in integrations)
        {
            var key = HealthPolicy.ExpectationKey(integration);
            var combo = new ComboBox
            {
                Width = 125,
                ItemsSource = Enum.GetNames<SignalExpectationMode>(),
                SelectedItem = settings.ServiceModes.TryGetValue(key, out var configured)
                    ? configured.ToString()
                    : SignalExpectationMode.Auto.ToString()
            };
            _signalQualityModeEditors[key] = combo;
            var row = new Grid
            {
                ColumnDefinitions = new ColumnDefinitions("*,Auto"),
                ColumnSpacing = 8
            };
            row.Children.Add(new TextBlock
            {
                Text = string.IsNullOrWhiteSpace(integration.DisplayName)
                    ? integration.Name
                    : integration.DisplayName,
                TextWrapping = Avalonia.Media.TextWrapping.Wrap,
                VerticalAlignment = VerticalAlignment.Center
            });
            Grid.SetColumn(combo, 1);
            row.Children.Add(combo);
            servicePanel.Children.Add(row);
        }

        if (integrations.Length == 0)
        {
            servicePanel.Children.Add(new TextBlock
            {
                Text = "No discovered services are available yet.",
                Classes = { "dim" }
            });
        }

        content.Children.Add(new ScrollViewer
        {
            MaxHeight = 330,
            HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled,
            VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
            Content = servicePanel
        });

        var footer = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 7,
            HorizontalAlignment = HorizontalAlignment.Right
        };
        var reset = new Button { Content = "Reset automatic" };
        reset.Click += (_, _) =>
        {
            foreach (var combo in _signalQualityModeEditors.Values)
                combo.SelectedItem = SignalExpectationMode.Auto.ToString();
        };
        var save = new Button
        {
            Content = "Save",
            Classes = { "primary" }
        };
        save.Click += (_, _) => SaveSignalQualitySettings();
        var close = new Button { Content = "Close" };
        close.Click += (_, _) => _signalQualityFlyout?.Hide();
        footer.Children.Add(reset);
        footer.Children.Add(save);
        footer.Children.Add(close);
        content.Children.Add(footer);

        _signalQualityFlyout = new Flyout { Content = content };
        _signalQualityFlyout.FlyoutPresenterClasses.Add(
            "dashboardInfoFlyout");
        _signalQualityFlyout.ShowAt(anchor);
    }

    private void SaveSignalQualitySettings()
    {
        var settings = SignalQualityStore.GetSettings();
        settings.Enabled = _signalQualityEnabledEditor?.IsChecked != false;
        settings.EvaluateExpectedServices = _signalQualityExpectedEditor?.IsChecked != false;
        settings.HostStaleMinutes = ParseStaleMinutes(
            _signalQualityHostStaleEditor,
            settings.HostStaleMinutes);
        settings.ApplicationStaleMinutes = ParseStaleMinutes(
            _signalQualityApplicationStaleEditor,
            settings.ApplicationStaleMinutes);
        settings.BackupStaleMinutes = ParseStaleMinutes(
            _signalQualityBackupStaleEditor,
            settings.BackupStaleMinutes);

        foreach (var item in _signalQualityModeEditors)
        {
            if (!Enum.TryParse<SignalExpectationMode>(
                    item.Value.SelectedItem?.ToString(),
                    ignoreCase: true,
                    out var mode) ||
                mode == SignalExpectationMode.Auto)
            {
                settings.ServiceModes.Remove(item.Key);
            }
            else
            {
                settings.ServiceModes[item.Key] = mode;
            }
        }

        SignalQualityStore.SetSettings(settings);
        _signalQualityFlyout?.Hide();
        PopulateSignalQualitySettings();
        _ = RunCoordinatedRefreshAsync(background: false);
    }

    private static TextBox StaleEditor(int value) => new()
    {
        Width = 64,
        Text = value.ToString(CultureInfo.InvariantCulture),
        HorizontalContentAlignment = HorizontalAlignment.Right
    };

    private static void AddStaleColumn(
        Grid grid,
        TextBox editor,
        string label,
        int column)
    {
        var panel = new StackPanel { Spacing = 2 };
        panel.Children.Add(new TextBlock
        {
            Text = label,
            Classes = { "dim" },
            FontSize = 9,
            HorizontalAlignment = HorizontalAlignment.Center
        });
        panel.Children.Add(editor);
        Grid.SetColumn(panel, column);
        grid.Children.Add(panel);
    }

    private static int ParseStaleMinutes(TextBox? editor, int fallback) =>
        int.TryParse(
            editor?.Text,
            NumberStyles.Integer,
            CultureInfo.InvariantCulture,
            out var value)
            ? Math.Clamp(value, 1, 10080)
            : fallback;
}
