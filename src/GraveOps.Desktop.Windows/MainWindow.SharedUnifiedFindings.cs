using System.Text;
using Avalonia.Controls;
using Avalonia.Input.Platform;
using GraveOps.Core.Hosts;
using GraveOps.Presentation.Avalonia.Findings;

namespace GraveOps.Desktop.Windows;

public partial class MainWindow
{
    private UnifiedFindingsView?
        _sharedFindingsView;

    private void InitializeSharedUnifiedFindings()
    {
        var page =
            Get<Grid>(
                "WarningsPage");

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
        UnifiedFindingsCopyRequestedEventArgs e)
    {
        var clipboard =
            TopLevel.GetTopLevel(
                    this)
                ?.Clipboard;

        if (clipboard is null)
            return;

        await clipboard.SetTextAsync(
            e.ReportText);
    }

    private void UpdateSharedUnifiedFindings(
        HostSnapshot snapshot,
        IReadOnlyList<RecommendationRow> recommendations,
        HealthSummary health)
    {
        if (_sharedFindingsView is null)
            return;

        var severity =
            health.Fail > 0
                ? UnifiedFindingSeverity.Error
                : health.Warn > 0
                    ? UnifiedFindingSeverity.Warning
                    : UnifiedFindingSeverity.Healthy;

        var findings =
            recommendations
                .Select(item =>
                    new UnifiedFindingRow(
                        RecommendationSeverity(
                            item),
                        item.Component,
                        item.Message,
                        item.Evidence,
                        RecommendationImpact(
                            item),
                        RecommendationNextStep(
                            item),
                        RecommendationNavigation(
                            item)))
                .ToArray();

        var dependencies =
            BuildWindowsDependencies(
                snapshot,
                recommendations);

        var remediation =
            findings
                .Where(item =>
                    item.Severity >=
                    UnifiedFindingSeverity.Warning)
                .Select((item, index) =>
                    new UnifiedRemediationRow(
                        index + 1,
                        item.Severity,
                        item.Component,
                        $"{item.Problem} {item.Impact}".Trim(),
                        item.NextStep,
                        item.NavigationKey))
                .ToArray();

        var root =
            findings.FirstOrDefault(item =>
                item.Severity >=
                UnifiedFindingSeverity.Error) ??
            findings.FirstOrDefault(item =>
                item.Severity >=
                UnifiedFindingSeverity.Warning);

        var headline =
            root?.Problem ??
            "No active findings.";

        var rootCause =
            root is null
                ? "No active fault detected"
                : $"{root.Component}: {root.Problem}";

        var impacts =
            findings
                .Take(40)
                .Select(item =>
                    new UnifiedImpactRow(
                        ActiveTargetDisplayName(),
                        item.Component,
                        item.Problem,
                        item.Evidence,
                        item.Impact,
                        item.NextStep,
                        item.Severity,
                        item.NavigationKey))
                .ToArray();

        var state =
            new UnifiedFindingsState(
                severity,
                UnifiedFindingLabels.Severity(
                    severity),
                headline,
                rootCause,
                health.Fail,
                health.Warn,
                impacts,
                dependencies,
                findings,
                remediation,
                string.Empty,
                $"Captured {snapshot.CapturedAt.ToLocalTime():g} from " +
                ActiveTargetConnectionSummary());

        _sharedFindingsView.Update(
            state);
    }

    private static IReadOnlyList<UnifiedDependencyRow>
        BuildWindowsDependencies(
            HostSnapshot snapshot,
            IReadOnlyList<RecommendationRow> recommendations)
    {
        UnifiedFindingSeverity SeverityFor(
            string component)
        {
            var matching =
                recommendations
                    .Where(item =>
                        item.Component.Equals(
                            component,
                            StringComparison.OrdinalIgnoreCase))
                    .ToArray();

            if (matching.Any(item =>
                    item.Severity.Equals(
                        "FAIL",
                        StringComparison.Ordinal)))
            {
                return UnifiedFindingSeverity.Error;
            }

            return matching.Length > 0
                ? UnifiedFindingSeverity.Warning
                : UnifiedFindingSeverity.Healthy;
        }

        var failedServices =
            snapshot.Services.Count(item =>
                !IsHealthyState(
                    item.ActiveState));

        var unhealthyContainers =
            snapshot.Containers.Count(item =>
                !IsHealthyState(
                    item.State));

        return new[]
        {
            new UnifiedDependencyRow(
                1,
                "Host",
                NormalizeDisplay(
                    snapshot.SystemState),
                $"{snapshot.OperatingSystem} | {snapshot.Kernel}",
                "Host state can affect every downstream dependency.",
                "Review provider evidence before changing downstream services.",
                snapshot.Warnings.Count > 0
                    ? UnifiedFindingSeverity.Warning
                    : UnifiedFindingSeverity.Healthy,
                "ServersNav"),

            new UnifiedDependencyRow(
                2,
                "Storage",
                $"{snapshot.Storage.Count} volume(s)",
                BuildStorageSummary(
                    snapshot.Storage),
                "Capacity pressure can block downloads, imports and application data.",
                "Open Storage and review the highest-used volume.",
                SeverityFor(
                    "Storage"),
                "StorageNav"),

            new UnifiedDependencyRow(
                3,
                "Services",
                $"{failedServices} unhealthy",
                $"{snapshot.Services.Count} service(s) reported",
                "Stopped or failed services can explain application unavailability.",
                "Open Services & Actions and inspect the reported unit.",
                SeverityFor(
                    "Service"),
                "ServicesNav"),

            new UnifiedDependencyRow(
                4,
                "Docker",
                NormalizeDisplay(
                    snapshot.DockerState),
                $"{unhealthyContainers} unhealthy of " +
                $"{snapshot.Containers.Count} container(s)",
                "Container runtime state can affect hosted applications.",
                "Open Docker and inspect container evidence.",
                SeverityFor(
                    "Docker"),
                "DockerNav"),

            new UnifiedDependencyRow(
                5,
                "Applications",
                $"{snapshot.Integrations.Count} detected",
                "Provider-reported integration inventory",
                "Application state affects acquisition and library workflows.",
                "Open Media Hub and review the owning application.",
                UnifiedFindingSeverity.Healthy,
                "MediaHubNav")
        };
    }

    private static UnifiedFindingSeverity
        RecommendationSeverity(
            RecommendationRow row) =>
        row.Severity.Equals(
            "FAIL",
            StringComparison.Ordinal)
            ? UnifiedFindingSeverity.Error
            : UnifiedFindingSeverity.Warning;

    private static string RecommendationImpact(
        RecommendationRow row) =>
        row.Component switch
        {
            "Storage" =>
                "Capacity pressure may interrupt downloads, imports or application data.",
            "Service" =>
                "The reported service may be unavailable to dependent applications.",
            "Docker" =>
                "The reported container state may reduce hosted application availability.",
            _ =>
                "The provider-reported condition requires operator review."
        };

    private static string RecommendationNextStep(
        RecommendationRow row) =>
        row.Component switch
        {
            "Storage" =>
                "Open Storage and review volume usage before capacity becomes critical.",
            "Service" =>
                "Open Services & Actions and inspect the reported unit.",
            "Docker" =>
                "Open Docker and inspect the reported container.",
            _ =>
                "Review provider evidence and refresh the active target."
        };

    private static string RecommendationNavigation(
        RecommendationRow row) =>
        row.Component switch
        {
            "Storage" =>
                "StorageNav",
            "Service" =>
                "ServicesNav",
            "Docker" =>
                "DockerNav",
            _ =>
                "ServersNav"
        };
}