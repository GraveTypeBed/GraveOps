using System.Globalization;
using System.Text;
using Avalonia.Controls;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;

namespace GraveOps.Desktop.Linux;

public partial class MainWindow
{
    private readonly LinuxInsightStore _insightStore = new();

    private IReadOnlyList<FleetHostInsightRow>
        _dashboardFleetHosts =
            Array.Empty<FleetHostInsightRow>();

    private IReadOnlyList<FleetApplicationInsightRow>
        _dashboardApplications =
            Array.Empty<FleetApplicationInsightRow>();

    private IReadOnlyList<FleetAttentionInsightRow>
        _dashboardFleetAttention =
            Array.Empty<FleetAttentionInsightRow>();

    private IReadOnlyList<InsightDependencyRow>
        _intelligenceDependencies =
            Array.Empty<InsightDependencyRow>();

    private IReadOnlyList<InsightRemediationRow>
        _intelligenceRemediation =
            Array.Empty<InsightRemediationRow>();

    private IReadOnlyList<InsightLifecycleItemRow>
        _lifecycleItems =
            Array.Empty<InsightLifecycleItemRow>();

    private IReadOnlyList<InsightRemediationRow>
        _lifecycleRemediation =
            Array.Empty<InsightRemediationRow>();

    private IReadOnlyList<InsightHistoryRow>
        _historyRows =
            Array.Empty<InsightHistoryRow>();

    private string _selectedDashboardAttentionNavigation =
        string.Empty;

    private string _selectedIntelligenceNavigation =
        string.Empty;

    private string _selectedLifecycleNavigation =
        string.Empty;

    private string _selectedHistoryNavigation =
        string.Empty;

    private void RecordInsightCapture()
    {
        if (_snapshot is null ||
            _analysis is null ||
            _backup is null)
        {
            return;
        }

        int? queueCount = null;

        if (_arrTelemetrySnapshot is not null &&
            int.TryParse(
                _arrTelemetrySnapshot.WorkSummary,
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out var parsedQueue))
        {
            queueCount = parsedQueue;
        }

        _insightStore.RecordCapture(
            _controlPlane.ActiveProfile,
            _snapshot,
            _analysis,
            _lifecycle,
            _integrations,
            _backup,
            queueCount);
    }

    private void PopulateDashboardV43()
    {
        if (_snapshot is null ||
            _analysis is null ||
            _policyEvaluation is null)
        {
            return;
        }

        var profiles =
            _controlPlane.Profiles.Profiles;

        _dashboardFleetHosts =
            _insightStore.BuildFleetHosts(
                profiles,
                _controlPlane.ActiveProfile.Id);

        _dashboardApplications =
            _insightStore.BuildApplications(
                profiles);

        _dashboardFleetAttention =
            _insightStore.BuildAttention(
                profiles);

        var fleetSeverity =
            _dashboardFleetHosts.Count == 0
                ? OpsSeverity.Info
                : _dashboardFleetHosts.Max(item =>
                    item.Severity);

        Get<TextBlock>("DashboardFleetStateText").Text =
            LinuxOpsAnalyzer.SeverityLabel(
                fleetSeverity);

        Get<TextBlock>("DashboardFleetHostCountText").Text =
            profiles.Count.ToString(
                CultureInfo.InvariantCulture);

        Get<TextBlock>("DashboardFleetApplicationCountText").Text =
            $"{_dashboardApplications.Count.ToString(CultureInfo.InvariantCulture)} verified applications";

        Get<TextBlock>("DashboardFleetAttentionCountText").Text =
            _dashboardFleetAttention.Count.ToString(
                CultureInfo.InvariantCulture);

        Get<TextBlock>("DashboardLiveApplicationCountText").Text =
            _dashboardApplications.Count.ToString(
                CultureInfo.InvariantCulture);

        Get<TextBlock>("DashboardLiveAttentionCountText").Text =
            _dashboardFleetAttention.Count.ToString(
                CultureInfo.InvariantCulture);

        var dashboardFailures =
            _analysis.Findings.Count(item =>
                item.Severity >= OpsSeverity.Error);

        var dashboardWarnings =
            _analysis.Findings.Count(item =>
                item.Severity == OpsSeverity.Warning);

        var dashboardHealthy =
            Math.Max(
                0,
                _integrations.Count(item =>
                    item.Severity < OpsSeverity.Warning) +
                _lifecycle.Count(item =>
                    item.Severity < OpsSeverity.Warning));

        Get<TextBlock>("DashboardHealthPassText").Text =
            dashboardHealthy.ToString(
                CultureInfo.InvariantCulture);

        Get<TextBlock>("DashboardHealthWarnText").Text =
            dashboardWarnings.ToString(
                CultureInfo.InvariantCulture);

        Get<TextBlock>("DashboardHealthFailText").Text =
            dashboardFailures.ToString(
                CultureInfo.InvariantCulture);

        Get<ListBox>("DashboardRecentActivityList").ItemsSource =
            _controlPlane.State.Activities
                .Take(7)
                .ToArray();

        Get<TextBlock>("DashboardEnvironmentSummaryText").Text =
            $"{_dashboardFleetHosts.Count(item => item.CapturedAt.HasValue)} captured · " +
            $"{_dashboardFleetHosts.Count(item => !item.CapturedAt.HasValue)} awaiting first capture";

        BindDashboardEnvironment();
        BindDashboardFleetAttention();
        BindActiveTargetFindings();
        PopulateDashboardQuickModules();
    }

    private void BindDashboardEnvironment()
    {
        var list =
            Get<ListBox>("DashboardEnvironmentList");

        var selectedId =
            (list.SelectedItem as FleetHostInsightRow)?
                .TargetId ??
            _controlPlane.ActiveProfile.Id;

        list.ItemsSource =
            _dashboardFleetHosts;

        list.SelectedItem =
            _dashboardFleetHosts.FirstOrDefault(item =>
                item.TargetId.Equals(
                    selectedId,
                    StringComparison.OrdinalIgnoreCase)) ??
            _dashboardFleetHosts.FirstOrDefault(item =>
                item.IsActive) ??
            _dashboardFleetHosts.FirstOrDefault();

        PopulateDashboardSelectedHost();
    }

    private void DashboardEnvironmentList_OnSelectionChanged(
        object? sender,
        SelectionChangedEventArgs e) =>
        PopulateDashboardSelectedHost();

    private void PopulateDashboardSelectedHost()
    {
        var selected =
            Get<ListBox>("DashboardEnvironmentList")
                .SelectedItem as
            FleetHostInsightRow;

        var applicationList =
            Get<ListBox>("DashboardApplicationList");

        if (selected is null)
        {
            Get<TextBlock>("DashboardSelectedHostText").Text =
                "No target selected";
            Get<TextBlock>("DashboardSelectedConnectionText").Text =
                "--";
            Get<TextBlock>("DashboardSelectedApplicationsText").Text =
                "--";
            Get<TextBlock>("DashboardSelectedAttentionText").Text =
                "--";
            Get<TextBlock>("DashboardSelectedQueueText").Text =
                "--";
            Get<TextBlock>("DashboardSelectedDockerText").Text =
                "--";
            Get<TextBlock>("DashboardSummaryDockerText").Text =
                "--";
            Get<TextBlock>("DashboardLiveQueueText").Text =
                "--";
            Get<TextBlock>("DashboardLiveDockerText").Text =
                "--";
            Get<TextBlock>("DashboardSelectedCapturedText").Text =
                "--";

            applicationList.ItemsSource =
                Array.Empty<FleetApplicationInsightRow>();

            Get<Button>("DashboardFocusHostButton").IsEnabled =
                false;
            Get<Button>("DashboardOpenApplicationButton").IsEnabled =
                false;
            return;
        }

        Get<TextBlock>("DashboardSelectedHostText").Text =
            selected.HostName;
        Get<TextBlock>("DashboardSelectedConnectionText").Text =
            selected.Connection;
        Get<TextBlock>("DashboardSelectedApplicationsText").Text =
            selected.Applications.ToString(
                CultureInfo.InvariantCulture);
        Get<TextBlock>("DashboardSelectedAttentionText").Text =
            selected.Attention.ToString(
                CultureInfo.InvariantCulture);
        Get<TextBlock>("DashboardSelectedQueueText").Text =
            selected.QueueSummary;
        Get<TextBlock>("DashboardSelectedDockerText").Text =
            selected.Docker;
        Get<TextBlock>("DashboardSummaryDockerText").Text =
            selected.Docker;
        Get<TextBlock>("DashboardLiveQueueText").Text =
            selected.QueueSummary;
        Get<TextBlock>("DashboardLiveDockerText").Text =
            selected.Docker;
        Get<TextBlock>("DashboardSelectedCapturedText").Text =
            selected.CapturedText;

        var applications =
            _dashboardApplications
                .Where(item =>
                    item.TargetId.Equals(
                        selected.TargetId,
                        StringComparison.OrdinalIgnoreCase))
                .ToArray();

        applicationList.ItemsSource = applications;

        if (applicationList.SelectedItem is not
            FleetApplicationInsightRow existing ||
            !applications.Contains(existing))
        {
            applicationList.SelectedItem =
                applications.FirstOrDefault();
        }

        Get<Button>("DashboardFocusHostButton").IsEnabled =
            !selected.TargetId.Equals(
                _controlPlane.ActiveProfile.Id,
                StringComparison.OrdinalIgnoreCase);

        PopulateDashboardApplicationSelection();
    }

    private void DashboardApplicationList_OnSelectionChanged(
        object? sender,
        SelectionChangedEventArgs e) =>
        PopulateDashboardApplicationSelection();

    private void PopulateDashboardApplicationSelection()
    {
        var selected =
            Get<ListBox>("DashboardApplicationList")
                .SelectedItem as
            FleetApplicationInsightRow;

        Get<Button>("DashboardOpenApplicationButton").IsEnabled =
            selected is not null;
    }

    private void DashboardFocusHostButton_OnClick(
        object? sender,
        RoutedEventArgs e)
    {
        var selected =
            Get<ListBox>("DashboardEnvironmentList")
                .SelectedItem as
            FleetHostInsightRow;

        if (selected is null)
            return;

        var profile =
            _controlPlane.Profiles.Find(
                selected.TargetId);

        if (profile is null)
            return;

        Get<ComboBox>("ActiveTargetComboBox")
            .SelectedItem = profile;
    }

    private void DashboardOpenApplicationButton_OnClick(
        object? sender,
        RoutedEventArgs e)
    {
        if (Get<ListBox>("DashboardApplicationList")
                .SelectedItem is
            FleetApplicationInsightRow application)
        {
            Navigate(application.NavigationName);
        }
    }

    private void BindDashboardFleetAttention()
    {
        var list =
            Get<ListBox>("DashboardFleetAttentionList");

        list.ItemsSource =
            _dashboardFleetAttention
                .Take(24)
                .ToArray();

        var hasRecommendations =
            _dashboardFleetAttention.Count > 0;

        list.IsVisible =
            hasRecommendations;

        Get<Border>("DashboardRecommendationsEmptyPanel").IsVisible =
            !hasRecommendations;

        Get<TextBlock>("DashboardFleetAttentionDetailText").IsVisible =
            hasRecommendations;

        if (list.SelectedItem is null &&
            _dashboardFleetAttention.Count > 0)
        {
            list.SelectedIndex = 0;
        }

        PopulateDashboardFleetAttentionSelection();
    }

    private void DashboardFleetAttentionList_OnSelectionChanged(
        object? sender,
        SelectionChangedEventArgs e) =>
        PopulateDashboardFleetAttentionSelection();

    private void PopulateDashboardFleetAttentionSelection()
    {
        var selected =
            Get<ListBox>("DashboardFleetAttentionList")
                .SelectedItem as
            FleetAttentionInsightRow;

        if (selected is null)
        {
            _selectedDashboardAttentionNavigation =
                string.Empty;

            Get<TextBlock>("DashboardFleetAttentionDetailText").Text =
                "No fleet-wide attention item is selected.";

            Get<Button>("DashboardOpenAttentionButton").IsEnabled =
                false;
            return;
        }

        _selectedDashboardAttentionNavigation =
            selected.NavigationName;

        Get<TextBlock>("DashboardFleetAttentionDetailText").Text =
            $"{selected.HostName} · {selected.Component}\n" +
            $"{selected.Issue}\n\n" +
            $"Impact · {selected.Impact}\n" +
            $"Next · {selected.NextStep}";

        Get<Button>("DashboardOpenAttentionButton").IsEnabled =
            true;
    }

    private void DashboardOpenAttentionButton_OnClick(
        object? sender,
        RoutedEventArgs e)
    {
        if (!string.IsNullOrWhiteSpace(
                _selectedDashboardAttentionNavigation))
        {
            Navigate(
                _selectedDashboardAttentionNavigation);
        }
    }

    private void BindActiveTargetFindings()
    {
        var storage =
            LinuxOpsAnalyzer.OperationalStorage(
                _snapshot!);

        var findings =
            _policyEvaluation!.Active
                .Where(item =>
                    item.Severity >=
                    OpsSeverity.Warning)
                .Take(12)
                .ToArray();

        var muted =
            _policyEvaluation.Muted;

        var errors = findings.Count(item =>
            item.Severity >= OpsSeverity.Error);

        var warnings = findings.Count(item =>
            item.Severity == OpsSeverity.Warning);

        var customPolicies = storage.Count(item =>
            _findingPolicies
                .HasCustomStorageThreshold(
                    item.MountPoint));

        Get<TextBlock>("DashboardFindingsSummaryText").Text =
            findings.Length == 0 &&
            muted.Count == 0
                ? "No active findings"
                : $"{errors} error · {warnings} warning · {muted.Count} muted";

        Get<TextBlock>("DashboardPolicySummaryText").Text =
            customPolicies == 0
                ? "Default monitoring"
                : $"{customPolicies} custom storage " +
                  $"{(customPolicies == 1 ? "policy" : "policies")} active";

        Get<ListBox>("DashboardAttentionList").ItemsSource =
            findings.Length == 0
                ? new[]
                {
                    _findingPolicies.CreateRow(
                        new OpsFinding(
                            OpsSeverity.Healthy,
                            "Environment",
                            "No active operational findings.",
                            "Latest capture completed successfully.",
                            "No impact detected.",
                            "Continue normal monitoring.",
                            0))
                }
                : findings;

        var mutedPanel =
            Get<Border>("MutedFindingsPanel");

        mutedPanel.IsVisible =
            muted.Count > 0;

        Get<TextBlock>("MutedFindingsSummaryText").Text =
            $"{muted.Count} muted";

        Get<ListBox>("MutedFindingsList").ItemsSource =
            muted;

        UpdateFindingPolicyButtons();
    }

    private void PopulateDashboardQuickModules()
    {
        Get<TextBlock>("DashboardQuickIntelligenceText").Text =
            _analysis!.Findings.Count == 0
                ? "No active finding"
                : _analysis.Headline;

        var lifecycleAttention =
            _lifecycle.Count(item =>
                item.Severity >= OpsSeverity.Warning);

        Get<TextBlock>("DashboardQuickLifecycleText").Text =
            lifecycleAttention == 0
                ? "No active lifecycle blocker"
                : $"{lifecycleAttention} stage(s) need attention";

        Get<TextBlock>("DashboardQuickMediaText").Text =
            $"{_integrations.Count} verified on active target";

        Get<TextBlock>("DashboardQuickStorageText").Text =
            $"{LinuxOpsAnalyzer.OperationalStorage(_snapshot!).Count} operational roots";

        Get<TextBlock>("DashboardQuickBackupsText").Text =
            _backup is null
                ? "No backup capture"
                : $"{_backup.State} · {_backup.Provider}";

        Get<TextBlock>("DashboardQuickHistoryText").Text =
            $"{_history.Records.Count} transitions · " +
            $"{_controlPlane.State.Activities.Count} activities";
    }

    private void DashboardQuickModuleButton_OnClick(
        object? sender,
        RoutedEventArgs e)
    {
        if (sender is Button button &&
            button.Tag is string navigationName)
        {
            Navigate(navigationName);
        }
    }

    private void PopulateIntelligenceV43()
    {
        if (_analysis is null)
            return;

        var profiles =
            _controlPlane.Profiles.Profiles;

        var fleetImpact =
            _insightStore.BuildAttention(profiles);

        _intelligenceDependencies =
            _insightStore.BuildDependencies(
                _lifecycle);

        _intelligenceRemediation =
            _insightStore.BuildRemediation(
                _analysis.Findings);

        var blockers =
            fleetImpact.Count(item =>
                item.Severity >= OpsSeverity.Error);

        var warnings =
            fleetImpact.Count(item =>
                item.Severity == OpsSeverity.Warning);

        var border =
            Get<Border>("IntelligenceSeverityBorder");

        var severity =
            Get<TextBlock>("IntelligenceSeverityText");

        border.Background =
            OpsPalette.Background(
                _analysis.Severity);

        severity.Foreground =
            OpsPalette.Foreground(
                _analysis.Severity);

        severity.Text =
            _analysis.Label;

        Get<TextBlock>("IntelligenceOverallMetricText").Text =
            _analysis.Label;

        Get<TextBlock>("IntelligenceBlockersMetricText").Text =
            blockers.ToString(
                CultureInfo.InvariantCulture);

        Get<TextBlock>("IntelligenceWarningsMetricText").Text =
            warnings.ToString(
                CultureInfo.InvariantCulture);

        Get<TextBlock>("IntelligenceRootCauseMetricText").Text =
            _analysis.RootCause;

        Get<TextBlock>("IntelligenceRootCauseText").Text =
            _analysis.RootCause;

        Get<TextBlock>("IntelligenceHeadlineText").Text =
            _analysis.Headline;

        Get<TextBlock>("IntelligenceCountText").Text =
            $"{_analysis.Findings.Count} finding(s)";

        Get<ListBox>("IntelligenceImpactList").ItemsSource =
            fleetImpact.Take(40).ToArray();

        Get<ListBox>("IntelligenceDependencyList").ItemsSource =
            _intelligenceDependencies;

        Get<ListBox>("IntelligenceFindingsList").ItemsSource =
            _analysis.Findings.Count == 0
                ? new[]
                {
                    new OpsFinding(
                        OpsSeverity.Healthy,
                        "Environment",
                        "No active findings.",
                        "Latest capture returned no warning-or-higher condition.",
                        "No impact detected.",
                        "Continue normal monitoring.",
                        0)
                }
                : _analysis.Findings;

        Get<ListBox>("IntelligenceRemediationList").ItemsSource =
            _intelligenceRemediation.Count == 0
                ? new[]
                {
                    new InsightRemediationRow
                    {
                        Step = 1,
                        Severity = OpsSeverity.Healthy,
                        Component = "Environment",
                        Why =
                            "No warning-or-higher finding requires remediation.",
                        NextStep =
                            "Continue normal monitoring.",
                        NavigationName = "DashboardNav"
                    }
                }
                : _intelligenceRemediation;

        EnsureListSelection(
            "IntelligenceFindingsList");

        PopulateIntelligenceSelectedFinding();
        UpdateSharedUnifiedFindings();
    }

    private void IntelligenceImpactList_OnSelectionChanged(
        object? sender,
        SelectionChangedEventArgs e)
    {
        if (Get<ListBox>("IntelligenceImpactList")
                .SelectedItem is not
            FleetAttentionInsightRow row)
        {
            return;
        }

        SetIntelligenceSelection(
            $"{row.HostName} · {row.Component}",
            $"{row.Issue}\n\n" +
            $"Evidence · {row.Evidence}\n\n" +
            $"Impact · {row.Impact}\n\n" +
            $"Next · {row.NextStep}",
            row.NavigationName);
    }

    private void IntelligenceDependencyList_OnSelectionChanged(
        object? sender,
        SelectionChangedEventArgs e)
    {
        if (Get<ListBox>("IntelligenceDependencyList")
                .SelectedItem is not
            InsightDependencyRow row)
        {
            return;
        }

        SetIntelligenceSelection(
            $"{row.Order}. {row.Component} · {row.State}",
            $"{row.Evidence}\n\n" +
            $"Impact · {row.Impact}\n\n" +
            $"Next · {row.NextStep}",
            row.NavigationName);
    }

    private void IntelligenceFindingsList_OnSelectionChanged(
        object? sender,
        SelectionChangedEventArgs e) =>
        PopulateIntelligenceSelectedFinding();

    private void PopulateIntelligenceSelectedFinding()
    {
        if (Get<ListBox>("IntelligenceFindingsList")
                .SelectedItem is not
            OpsFinding finding)
        {
            return;
        }

        SetIntelligenceSelection(
            $"{LinuxOpsAnalyzer.SeverityLabel(finding.Severity)} · " +
            finding.Component,
            $"{finding.Problem}\n\n" +
            $"Evidence · {finding.Evidence}\n\n" +
            $"Impact · {finding.Impact}\n\n" +
            $"Next · {finding.NextStep}",
            LinuxInsightStore.NavigationForComponent(
                finding.Component));
    }

    private void IntelligenceRemediationList_OnSelectionChanged(
        object? sender,
        SelectionChangedEventArgs e)
    {
        if (Get<ListBox>("IntelligenceRemediationList")
                .SelectedItem is not
            InsightRemediationRow row)
        {
            return;
        }

        SetIntelligenceSelection(
            $"Step {row.Step} · {row.Component}",
            $"Why · {row.Why}\n\n" +
            $"Next · {row.NextStep}",
            row.NavigationName);
    }

    private void SetIntelligenceSelection(
        string title,
        string detail,
        string navigationName)
    {
        _selectedIntelligenceNavigation =
            navigationName;

        Get<TextBlock>("IntelligenceSelectedTitleText").Text =
            title;

        Get<TextBox>("IntelligenceSelectedDetailText").Text =
            detail;

        Get<Button>("IntelligenceOpenRelatedButton").IsEnabled =
            !string.IsNullOrWhiteSpace(
                navigationName);

        Get<Button>("IntelligenceStageActionButton").IsEnabled =
            !string.IsNullOrWhiteSpace(
                navigationName);
    }

    private void IntelligenceOpenRelatedButton_OnClick(
        object? sender,
        RoutedEventArgs e)
    {
        if (!string.IsNullOrWhiteSpace(
                _selectedIntelligenceNavigation))
        {
            Navigate(
                _selectedIntelligenceNavigation);
        }
    }

    private void IntelligenceStageActionButton_OnClick(
        object? sender,
        RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(
                _selectedIntelligenceNavigation))
        {
            return;
        }

        _controlPlane.State.RecordActivity(
            "Guided action",
            _controlPlane.ActiveProfile.DisplayName,
            "Suggested action staged",
            "GraveOps opened the owning page. No command was executed.",
            _selectedIntelligenceNavigation);

        Navigate(
            _selectedIntelligenceNavigation);
    }

    private async void IntelligenceCopyReportButton_OnClick(
        object? sender,
        RoutedEventArgs e)
    {
        if (_analysis is null)
            return;

        var report =
            _insightStore.BuildIntelligenceReport(
                _controlPlane.ActiveProfile,
                _analysis,
                _intelligenceDependencies,
                _intelligenceRemediation);

        await CopyInsightTextAsync(
            report,
            "Intelligence report copied.");
    }

    private void IntelligenceOpenHistoryButton_OnClick(
        object? sender,
        RoutedEventArgs e) =>
        Navigate("HistoryNav");

    private void PopulateLifecycleV43()
    {
        if (_analysis is null)
            return;

        _lifecycleItems =
            _insightStore.BuildLifecycleItems(
                _lifecycle,
                _arrTelemetrySnapshot?.WorkItems);

        _lifecycleRemediation =
            _insightStore.BuildRemediation(
                _analysis.Findings);

        var attention =
            _lifecycleItems.Count(item =>
                item.Severity >=
                OpsSeverity.Warning);

        var downloading =
            _lifecycleItems.Count(item =>
                item.Stage.Equals(
                    "Download",
                    StringComparison.OrdinalIgnoreCase) &&
                !item.State.Contains(
                    "healthy",
                    StringComparison.OrdinalIgnoreCase));

        var import =
            _lifecycleItems.Count(item =>
                item.Stage.Equals(
                    "Import",
                    StringComparison.OrdinalIgnoreCase) ||
                item.State.Contains(
                    "import",
                    StringComparison.OrdinalIgnoreCase));

        Get<TextBlock>("LifecycleActiveMetricText").Text =
            _lifecycleItems.Count.ToString(
                CultureInfo.InvariantCulture);

        Get<TextBlock>("LifecycleAttentionMetricText").Text =
            attention.ToString(
                CultureInfo.InvariantCulture);

        Get<TextBlock>("LifecycleDownloadingMetricText").Text =
            downloading.ToString(
                CultureInfo.InvariantCulture);

        Get<TextBlock>("LifecycleImportMetricText").Text =
            import.ToString(
                CultureInfo.InvariantCulture);

        var blocked =
            _lifecycle.Count(item =>
                item.Severity >=
                OpsSeverity.Error);

        var warning =
            _lifecycle.Count(item =>
                item.Severity ==
                OpsSeverity.Warning);

        Get<TextBlock>("LifecycleSummaryText").Text =
            blocked > 0
                ? $"{blocked} blocked · {warning} attention"
                : warning > 0
                    ? $"{warning} stage(s) need attention"
                    : "No active lifecycle blocker detected";

        Get<ListBox>("LifecycleStagesList").ItemsSource =
            _lifecycle;

        Get<ListBox>("LifecycleItemsList").ItemsSource =
            _lifecycleItems;

        Get<ListBox>("LifecycleRemediationList").ItemsSource =
            _lifecycleRemediation.Count == 0
                ? new[]
                {
                    new InsightRemediationRow
                    {
                        Step = 1,
                        Severity = OpsSeverity.Healthy,
                        Component = "Lifecycle",
                        Why =
                            "No warning-or-higher lifecycle blocker is active.",
                        NextStep =
                            "Continue normal monitoring.",
                        NavigationName = "DashboardNav"
                    }
                }
                : _lifecycleRemediation;

        EnsureListSelection("LifecycleItemsList");
        PopulateLifecycleItemSelection();
    }

    private void LifecycleItemsList_OnSelectionChanged(
        object? sender,
        SelectionChangedEventArgs e) =>
        PopulateLifecycleItemSelection();

    private void PopulateLifecycleItemSelection()
    {
        if (Get<ListBox>("LifecycleItemsList")
                .SelectedItem is not
            InsightLifecycleItemRow item)
        {
            _selectedLifecycleNavigation =
                string.Empty;

            Get<TextBlock>("LifecycleSelectedTitleText").Text =
                "No lifecycle item selected";

            Get<TextBox>("LifecycleSelectedDetailText").Text =
                string.Empty;

            Get<Button>("LifecycleOpenOwnerButton").IsEnabled =
                false;
            return;
        }

        _selectedLifecycleNavigation =
            item.NavigationName;

        Get<TextBlock>("LifecycleSelectedTitleText").Text =
            $"{item.Owner} · {item.Item}";

        Get<TextBox>("LifecycleSelectedDetailText").Text =
            $"Stage · {item.Stage}\n" +
            $"State · {item.State}\n" +
            $"Progress · {BlankAsDash(item.Progress)}\n" +
            $"Remaining · {BlankAsDash(item.Remaining)}\n\n" +
            item.Detail;

        Get<Button>("LifecycleOpenOwnerButton").IsEnabled =
            !string.IsNullOrWhiteSpace(
                item.NavigationName);
    }

    private void LifecycleRemediationList_OnSelectionChanged(
        object? sender,
        SelectionChangedEventArgs e)
    {
        if (Get<ListBox>("LifecycleRemediationList")
                .SelectedItem is not
            InsightRemediationRow row)
        {
            return;
        }

        _selectedLifecycleNavigation =
            row.NavigationName;

        Get<TextBlock>("LifecycleSelectedTitleText").Text =
            $"Step {row.Step} · {row.Component}";

        Get<TextBox>("LifecycleSelectedDetailText").Text =
            $"Why · {row.Why}\n\n" +
            $"Next · {row.NextStep}";

        Get<Button>("LifecycleOpenOwnerButton").IsEnabled =
            true;
    }

    private void LifecycleOpenOwnerButton_OnClick(
        object? sender,
        RoutedEventArgs e)
    {
        if (!string.IsNullOrWhiteSpace(
                _selectedLifecycleNavigation))
        {
            Navigate(
                _selectedLifecycleNavigation);
        }
    }

    private void LifecycleOpenIntelligenceButton_OnClick(
        object? sender,
        RoutedEventArgs e) =>
        Navigate("IntelligenceNav");

    private void PopulateHistoryV43() =>
        PopulateReliableHistory();

    private void HistoryTransitionsList_OnSelectionChanged(
        object? sender,
        SelectionChangedEventArgs e)
    {
        if (Get<ListBox>("HistoryTransitionsList")
                .SelectedItem is
            InsightHistoryRow row)
        {
            SetHistorySelection(row);
        }
    }

    private void HistoryActivityList_OnSelectionChanged(
        object? sender,
        SelectionChangedEventArgs e)
    {
        if (Get<ListBox>("HistoryActivityList")
                .SelectedItem is
            InsightHistoryRow row)
        {
            SetHistorySelection(row);
        }
    }

    private void HistoryIncidentList_OnSelectionChanged(
        object? sender,
        SelectionChangedEventArgs e) =>
        PopulateHistoryIncidentSelection();

    private void PopulateHistoryIncidentSelection()
    {
        if (Get<ListBox>("HistoryIncidentList")
                .SelectedItem is
            InsightHistoryRow row)
        {
            SetHistorySelection(row);
            return;
        }

        _selectedHistoryNavigation =
            string.Empty;

        Get<TextBlock>("HistorySelectedTitleText").Text =
            "No incident selected";

        Get<TextBox>("HistorySelectedDetailText").Text =
            string.Empty;

        Get<TextBox>("HistoryReplayText").Text =
            "Select a transition or activity row to build an incident replay.";

        Get<Button>("HistoryOpenRelatedButton").IsEnabled =
            false;

        Get<Button>("HistoryCopyReplayButton").IsEnabled =
            false;
    }

    private void SetHistorySelection(
        InsightHistoryRow row)
    {
        _selectedHistoryNavigation =
            row.NavigationName;

        Get<TextBlock>("HistorySelectedTitleText").Text =
            $"{row.Target} · {row.Component}";

        Get<TextBox>("HistorySelectedDetailText").Text =
            $"{row.DisplayTime} · {row.Stream}\n" +
            $"{row.SeverityLabel} · {row.Transition}\n\n" +
            row.Detail;

        Get<TextBox>("HistoryReplayText").Text =
            _insightStore.BuildIncidentReplay(row);

        Get<Button>("HistoryOpenRelatedButton").IsEnabled =
            !string.IsNullOrWhiteSpace(
                row.NavigationName);

        Get<Button>("HistoryCopyReplayButton").IsEnabled =
            true;
    }

    private void HistoryReplaySelectedButton_OnClick(
        object? sender,
        RoutedEventArgs e)
    {
        if (Get<ListBox>("HistoryIncidentList")
                .SelectedItem is
            InsightHistoryRow row)
        {
            SetHistorySelection(row);
        }
    }

    private async void HistoryCopyReplayButton_OnClick(
        object? sender,
        RoutedEventArgs e)
    {
        await CopyInsightTextAsync(
            Get<TextBox>("HistoryReplayText").Text ??
            string.Empty,
            "Incident replay copied.");
    }

    private void HistoryOpenRelatedButton_OnClick(
        object? sender,
        RoutedEventArgs e)
    {
        if (!string.IsNullOrWhiteSpace(
                _selectedHistoryNavigation))
        {
            Navigate(
                _selectedHistoryNavigation);
        }
    }

    private void HistoryClearAllButton_OnClick(
        object? sender,
        RoutedEventArgs e)
    {
        _history.Clear();
        _controlPlane.State.ClearActivities();
        PopulateHistoryV43();
        PopulateControlPlaneFoundation();

        Get<TextBox>("HistoryReplayText").Text =
            "Transition and activity history cleared. Fleet capture summaries remain available.";
    }

    private async Task CopyInsightTextAsync(
        string value,
        string status)
    {
        var clipboard =
            TopLevel.GetTopLevel(this)?
                .Clipboard;

        if (clipboard is null)
            return;

        await clipboard.SetTextAsync(value);

        Get<TextBlock>("HistoryCopyStatusText").Text =
            status;

        Get<TextBlock>("IntelligenceCopyStatusText").Text =
            status;
    }

    private void EnsureListSelection(
        string listName)
    {
        var list =
            Get<ListBox>(listName);

        if (list.SelectedItem is null &&
            list.Items.Count > 0)
        {
            list.SelectedIndex = 0;
        }
    }

    private static string BlankAsDash(
        string value) =>
        string.IsNullOrWhiteSpace(value)
            ? "--"
            : value;
}
