using Avalonia.Controls;
using Avalonia.Interactivity;
using GraveOps.Presentation.Avalonia.Findings;

namespace GraveOps.Desktop.Linux;

public partial class MainWindow
{
    private UnifiedFindingsView?
        _sharedFindingsView;

    private void InitializeSharedUnifiedFindings()
    {
        var page =
            Get<Grid>(
                "IntelligencePage");

        foreach (var child in
                 page.Children)
        {
            child.IsVisible =
                false;
        }

        _sharedFindingsView =
            new UnifiedFindingsView();

        _sharedFindingsView.HistoryRequested +=
            (_, _) =>
                Navigate(
                    "HistoryNav");

        _sharedFindingsView.EnvironmentRequested +=
            (_, _) =>
                Navigate(
                    "DashboardNav");

        _sharedFindingsView.AnalyzeRequested +=
            SharedFindingsAnalyzeRequested;

        _sharedFindingsView.NavigationRequested +=
            (_, e) =>
                Navigate(
                    e.NavigationKey);

        _sharedFindingsView.CopyReportRequested +=
            SharedFindingsCopyRequested;

        page.Children.Add(
            _sharedFindingsView);

        _sharedFindingsView.Update(
            UnifiedFindingsState.Waiting);
    }

    private async void SharedFindingsAnalyzeRequested(
        object? sender,
        EventArgs e) =>
        await RefreshAsync();

    private async void SharedFindingsCopyRequested(
        object? sender,
        UnifiedFindingsCopyRequestedEventArgs e) =>
        await CopyInsightTextAsync(
            e.ReportText,
            "Intelligence report copied.");

    private void UpdateSharedUnifiedFindings()
    {
        if (_sharedFindingsView is null ||
            _analysis is null)
        {
            return;
        }

        var fleetImpact =
            _insightStore.BuildAttention(
                _controlPlane.Profiles.Profiles);

        var state =
            new UnifiedFindingsState(
                ToUnifiedSeverity(
                    _analysis.Severity),
                _analysis.Label,
                _analysis.Headline,
                _analysis.RootCause,
                fleetImpact.Count(item =>
                    item.Severity >=
                    OpsSeverity.Error),
                fleetImpact.Count(item =>
                    item.Severity ==
                    OpsSeverity.Warning),
                fleetImpact
                    .Take(40)
                    .Select(item =>
                        new UnifiedImpactRow(
                            item.HostName,
                            item.Component,
                            item.Issue,
                            item.Evidence,
                            item.Impact,
                            item.NextStep,
                            ToUnifiedSeverity(
                                item.Severity),
                            item.NavigationName))
                    .ToArray(),
                _intelligenceDependencies
                    .Select(item =>
                        new UnifiedDependencyRow(
                            item.Order,
                            item.Component,
                            item.State,
                            item.Evidence,
                            item.Impact,
                            item.NextStep,
                            ToUnifiedSeverity(
                                item.Severity),
                            item.NavigationName))
                    .ToArray(),
                _analysis.Findings
                    .Select(item =>
                        new UnifiedFindingRow(
                            ToUnifiedSeverity(
                                item.Severity),
                            item.Component,
                            item.Problem,
                            item.Evidence,
                            item.Impact,
                            item.NextStep,
                            LinuxInsightStore
                                .NavigationForComponent(
                                    item.Component)))
                    .ToArray(),
                _intelligenceRemediation
                    .Select(item =>
                        new UnifiedRemediationRow(
                            item.Step,
                            ToUnifiedSeverity(
                                item.Severity),
                            item.Component,
                            item.Why,
                            item.NextStep,
                            item.NavigationName))
                    .ToArray(),
                _insightStore.BuildIntelligenceReport(
                    _controlPlane.ActiveProfile,
                    _analysis,
                    _intelligenceDependencies,
                    _intelligenceRemediation),
                $"Captured {_snapshot?.CapturedAt.ToLocalTime():g}");

        _sharedFindingsView.Update(
            state);
    }

    private static UnifiedFindingSeverity
        ToUnifiedSeverity(
            OpsSeverity severity) =>
        severity switch
        {
            OpsSeverity.Healthy =>
                UnifiedFindingSeverity.Healthy,
            OpsSeverity.Info =>
                UnifiedFindingSeverity.Info,
            OpsSeverity.Warning =>
                UnifiedFindingSeverity.Warning,
            OpsSeverity.Error =>
                UnifiedFindingSeverity.Error,
            OpsSeverity.Critical =>
                UnifiedFindingSeverity.Critical,
            _ =>
                UnifiedFindingSeverity.Info
        };
}