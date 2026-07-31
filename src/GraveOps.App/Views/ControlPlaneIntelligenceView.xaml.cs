using System.Windows;
using System.Windows.Controls;
using GraveOps.App.Models;
using GraveOps.App.Services;
using GraveOps.App.Windows;

namespace GraveOps.App.Views;

public partial class ControlPlaneIntelligenceView : UserControl
{
    private readonly ControlPlaneIntelligenceService _intelligence =
        new(App.Services);

    private AppServices S => App.Services;
    private ServerProfile? Server => S.Context.Current;
    private ControlPlaneIntelligenceSnapshot? _snapshot;
    private EnvironmentOverviewSnapshot? _environmentSnapshot;
    private bool _refreshing;

    private ControlPlaneNode? SelectedNode =>
        NodesGrid.SelectedItem as ControlPlaneNode;

    private ControlPlaneFinding? SelectedFinding =>
        FindingsGrid.SelectedItem as ControlPlaneFinding;

    public ControlPlaneIntelligenceView()
    {
        InitializeComponent();
        Loaded += async (_, _) => await RefreshAsync();
    }

    private async void Refresh_Click(
        object sender,
        RoutedEventArgs e)
        => await RefreshAsync();

    private async Task RefreshAsync()
    {
        if (_refreshing)
            return;

        _refreshing = true;
        RefreshButton.IsEnabled = false;
        SuggestedActionButton.IsEnabled = false;
        DeepInspectButton.IsEnabled = false;
        OpenRelatedButton.IsEnabled = false;

        try
        {
            FleetOverallText.Text = "CHECKING";
            FleetHostsText.Text = "--";
            FleetAppsText.Text = "--";
            FleetImpactText.Text = "--";
            FleetHealthyText.Text = "Refreshing environment context...";

            _environmentSnapshot = await S.Environment.GetSnapshotAsync(false);
            BindEnvironment(_environmentSnapshot);

            if (Server is not { } server)
            {
                TargetText.Text = "No active host";
                OverallText.Text = "--";
                BlockersText.Text = "--";
                WarningsText.Text = "--";
                RootCauseText.Text = "Select a host for deep analysis";
                RemediationGrid.ItemsSource = null;
                RemediationGrid.Visibility = Visibility.Collapsed;
                RemediationEmptyText.Visibility = Visibility.Visible;
                RemediationEmptyText.Text = "Select a host to build a remediation path.";
                RemediationOpenButton.IsEnabled = false;
                StatusText.Text = "Environment context is ready. Select a host for dependency analysis.";
                return;
            }

            TargetText.Text = server.Name;
            OverallText.Text = "ANALYZING";
            BlockersText.Text = "--";
            WarningsText.Text = "--";
            RootCauseText.Text = "Evaluating control plane";
            StatusText.Text =
                "Analyzing selected host, storage, containers, applications, queues and Plex...";

            _snapshot = await _intelligence.AnalyzeAsync(server);
            Bind(_snapshot);

            MediaLifecycleSnapshot? lifecycle = null;
            try
            {
                lifecycle = await S.Lifecycle.GetSnapshotAsync(server, force: false);
            }
            catch
            {
                // Guided remediation can still use fleet context if lifecycle telemetry is unavailable.
            }

            var remediation = await S.Lifecycle.BuildRemediationAsync(server, _environmentSnapshot, lifecycle);
            BindRemediation(remediation);

            StatusText.Text =
                $"Analysis completed {DateTime.Now:HH:mm:ss}. {_snapshot.StatusLine}";
        }
        catch (Exception ex)
        {
            StatusText.Text = ex.Message;

            GraveOpsDialog.Show(
                Window.GetWindow(this),
                ex.Message,
                "Control plane analysis failed",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
        finally
        {
            RefreshButton.IsEnabled = true;
            _refreshing = false;
        }
    }

    private void BindEnvironment(EnvironmentOverviewSnapshot snapshot)
    {
        FleetOverallText.Text = snapshot.State switch
        {
            EnvironmentHealthState.Healthy => "HEALTHY",
            EnvironmentHealthState.Attention => "ATTENTION",
            EnvironmentHealthState.Offline => "OFFLINE",
            _ => "UNKNOWN"
        };
        FleetHostsText.Text = $"{snapshot.OnlineHostCount}/{snapshot.HostCount}";
        FleetAppsText.Text = $"{snapshot.HealthyAppCount}/{snapshot.VerifiedAppCount}";
        FleetImpactText.Text = snapshot.Impacts.Count.ToString();

        var rows = snapshot.Impacts
            .Select(x => new FleetImpactRow(x))
            .ToList();
        FleetImpactGrid.ItemsSource = rows;
        FleetImpactGrid.Visibility = rows.Count == 0
            ? Visibility.Collapsed
            : Visibility.Visible;
        FleetOpenButton.IsEnabled = false;
        CopyReportButton.IsEnabled = true;

        FleetHealthyText.Text = rows.Count == 0
            ? $"No active environment findings. {snapshot.OnlineHostCount} host(s) reachable and {snapshot.HealthyAppCount} verified app(s) healthy."
            : $"{rows.Count} environment finding(s) across {snapshot.AttentionHostCount} affected host(s). Select one to jump to the owning host or application.";
    }

    private void BindRemediation(IReadOnlyList<RemediationStep> steps)
    {
        RemediationGrid.ItemsSource = steps;
        RemediationGrid.Visibility = steps.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
        RemediationEmptyText.Visibility = steps.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        RemediationEmptyText.Text = steps.Count == 0
            ? "No remediation required. The current dependency chain has no active blockers."
            : "";
        RemediationOpenButton.IsEnabled = false;
    }

    private void RemediationGrid_SelectionChanged(object sender, SelectionChangedEventArgs e) =>
        RemediationOpenButton.IsEnabled = RemediationGrid.SelectedItem is RemediationStep step &&
                                          !string.IsNullOrWhiteSpace(step.DeepLink);

    private void RemediationGrid_MouseDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (RemediationGrid.SelectedItem is RemediationStep step)
            OpenRemediation(step);
    }

    private void RemediationOpen_Click(object sender, RoutedEventArgs e)
    {
        if (RemediationGrid.SelectedItem is RemediationStep step)
            OpenRemediation(step);
    }

    private void OpenRemediation(RemediationStep step)
    {
        if (!string.IsNullOrWhiteSpace(step.DeepLink))
            S.Navigation.Request(step.DeepLink);
    }

    private void Bind(
        ControlPlaneIntelligenceSnapshot snapshot)
    {
        OverallText.Text = snapshot.OverallSeverity;
        BlockersText.Text = snapshot.BlockerCount.ToString();
        WarningsText.Text = snapshot.WarningCount.ToString();
        RootCauseText.Text = snapshot.RootCause;
        NodesGrid.ItemsSource = snapshot.Nodes;
        FindingsGrid.ItemsSource = snapshot.Findings;
        CopyReportButton.IsEnabled = true;

        if (snapshot.Findings.Count > 0)
            FindingsGrid.SelectedIndex = 0;
        else if (snapshot.Nodes.Count > 0)
            NodesGrid.SelectedIndex = 0;

        UpdateSelection();
    }

    private void NodesGrid_SelectionChanged(
        object sender,
        SelectionChangedEventArgs e)
    {
        if (NodesGrid.SelectedItem is not null)
            FindingsGrid.SelectedItem = null;

        UpdateSelection();
    }

    private void FindingsGrid_SelectionChanged(
        object sender,
        SelectionChangedEventArgs e)
    {
        if (FindingsGrid.SelectedItem is not null)
            NodesGrid.SelectedItem = null;

        UpdateSelection();
    }

    private void UpdateSelection()
    {
        if (SelectedFinding is { } finding)
        {
            SelectionTitleText.Text =
                $"{finding.Severity} | {finding.Component} | {finding.Problem}";

            SelectionSummaryText.Text =
                $"{finding.Impact} Next: {finding.NextStep}";

            OpenRelatedButton.IsEnabled =
                !string.IsNullOrWhiteSpace(finding.DeepLink);

            DeepInspectButton.IsEnabled =
                !string.IsNullOrWhiteSpace(finding.DrillTarget);

            SuggestedActionButton.IsEnabled =
                !string.IsNullOrWhiteSpace(finding.ActionName);

            SuggestedActionButton.Content =
                string.IsNullOrWhiteSpace(finding.ActionName)
                    ? "Run suggested action"
                    : finding.ActionName;

            return;
        }

        if (SelectedNode is { } node)
        {
            SelectionTitleText.Text =
                $"{node.Component} | {node.State} | {node.Severity}";

            SelectionSummaryText.Text =
                $"{node.Summary} {node.DependencyText}. {node.ImpactText}.";

            OpenRelatedButton.IsEnabled =
                !string.IsNullOrWhiteSpace(node.DeepLink);

            DeepInspectButton.IsEnabled =
                !string.IsNullOrWhiteSpace(node.DrillTarget);

            SuggestedActionButton.IsEnabled = false;
            SuggestedActionButton.Content =
                "Run suggested action";

            return;
        }

        SelectionTitleText.Text =
            "Select a dependency or finding.";

        SelectionSummaryText.Text =
            "GraveOps will show why it matters and which control to use next.";

        OpenRelatedButton.IsEnabled = false;
        DeepInspectButton.IsEnabled = false;
        SuggestedActionButton.IsEnabled = false;
        SuggestedActionButton.Content =
            "Run suggested action";
    }

    private void FleetImpactGrid_SelectionChanged(
        object sender,
        SelectionChangedEventArgs e)
        => FleetOpenButton.IsEnabled = FleetImpactGrid.SelectedItem is FleetImpactRow;

    private void FleetImpactGrid_MouseDoubleClick(
        object sender,
        System.Windows.Input.MouseButtonEventArgs e)
    {
        if (FleetImpactGrid.SelectedItem is FleetImpactRow row)
            OpenFleetImpact(row);
    }

    private void FleetOpen_Click(
        object sender,
        RoutedEventArgs e)
    {
        if (FleetImpactGrid.SelectedItem is FleetImpactRow row)
            OpenFleetImpact(row);
    }

    private void OpenEnvironmentDashboard_Click(
        object sender,
        RoutedEventArgs e)
        => S.Navigation.Request("page:Dashboard");

    private void OpenFleetImpact(FleetImpactRow row)
    {
        var server = S.Config.Current.Servers.FirstOrDefault(x => x.Id == row.ServerId);
        if (server is not null && S.Context.Current?.Id != server.Id)
            S.Context.Select(server);

        S.Navigation.Request($"page:{row.PageKey}");
    }

    private void OpenRelated_Click(
        object sender,
        RoutedEventArgs e)
    {
        var deepLink =
            SelectedFinding?.DeepLink ??
            SelectedNode?.DeepLink ??
            "";

        if (!string.IsNullOrWhiteSpace(deepLink))
            S.Navigation.Request(deepLink);
    }

    private void DeepInspect_Click(
        object sender,
        RoutedEventArgs e)
    {
        var target =
            SelectedFinding?.DrillTarget ??
            SelectedNode?.DrillTarget ??
            "";

        var owner =
            Window.GetWindow(this);

        switch (target.ToLowerInvariant())
        {
            case "docker":
                var docker =
                    new OperationsDrillDownWindow(0);
                if (owner is not null) docker.Owner = owner;
                docker.ShowDialog();
                break;

            case "storage":
                var storage =
                    new OperationsDrillDownWindow(1);
                if (owner is not null) storage.Owner = owner;
                storage.ShowDialog();
                break;

            case "queues":
                var queues =
                    new OperationsDrillDownWindow(2);
                if (owner is not null) queues.Owner = owner;
                queues.ShowDialog();
                break;

            case "plex":
                S.Navigation.Request("page:Plex");
                break;
        }
    }

    private async void SuggestedAction_Click(
        object sender,
        RoutedEventArgs e)
    {
        if (SelectedFinding is not { } finding ||
            string.IsNullOrWhiteSpace(finding.ActionName) ||
            Server is not { } server)
            return;

        var action =
            S.Config.Current.Actions
                .FirstOrDefault(
                    x => x.Name.Equals(
                        finding.ActionName,
                        StringComparison.OrdinalIgnoreCase));

        if (action is null)
        {
            GraveOpsDialog.Show(
                Window.GetWindow(this),
                $"The suggested action '{finding.ActionName}' is not present in the GraveOps action library.",
                "Suggested action unavailable",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        if (GraveOpsDialog.Show(
                Window.GetWindow(this),
                $"{finding.Problem}\n\nRun protected action '{action.Name}' on {server.Name}?\n\n{finding.NextStep}",
                "Run suggested GraveOps action",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question)
            != MessageBoxResult.Yes)
            return;

        SuggestedActionButton.IsEnabled = false;
        StatusText.Text =
            $"Running {action.Name} through the protected action runner...";

        var result =
            await S.ActionRunner.RunAsync(
                action,
                server);

        if (result.Success)
        {
            StatusText.Text =
                $"{action.Name} succeeded and verification passed. Re-analyzing...";

            await RefreshAsync();
        }
        else
        {
            StatusText.Text =
                $"{action.Name} did not complete successfully.";

            GraveOpsDialog.Show(
                Window.GetWindow(this),
                string.IsNullOrWhiteSpace(result.Error)
                    ? result.Verification
                    : result.Error,
                $"{action.Name} failed",
                MessageBoxButton.OK,
                MessageBoxImage.Error);

            UpdateSelection();
        }
    }

    private void CopyReport_Click(
        object sender,
        RoutedEventArgs e)
    {
        if (_snapshot is null && _environmentSnapshot is null)
            return;

        var report = new System.Text.StringBuilder();
        if (_environmentSnapshot is { } environment)
        {
            report.AppendLine("GRAVEOPS ENVIRONMENT CONTEXT");
            report.AppendLine($"Generated: {DateTimeOffset.Now:O}");
            report.AppendLine($"State: {FleetOverallText.Text}");
            report.AppendLine($"Hosts: {environment.OnlineHostCount}/{environment.HostCount} reachable");
            report.AppendLine($"Verified apps: {environment.HealthyAppCount}/{environment.VerifiedAppCount} healthy");
            report.AppendLine($"Impacted: {environment.Impacts.Count}");

            foreach (var impact in environment.Impacts)
            {
                report.AppendLine($"- [{impact.State}] {impact.HostName} / {impact.Component}: {impact.Detail}");
                report.AppendLine($"  Impact: {impact.Impact}");
            }

            report.AppendLine();
        }

        if (_snapshot is not null)
            report.AppendLine(_snapshot.BuildReport());

        Clipboard.SetText(report.ToString().TrimEnd());
        StatusText.Text =
            "Environment and selected-host intelligence report copied to clipboard.";
    }

    private void OpenHistory_Click(
        object sender,
        RoutedEventArgs e)
    {
        var owner =
            Window.GetWindow(this);

        var window =
            new OperationsHistoryWindow(2);

        if (owner is not null)
            window.Owner = owner;

        window.ShowDialog();
    }
}

public sealed class FleetImpactRow
{
    public Guid ServerId { get; }
    public string Host { get; }
    public string Component { get; }
    public string State { get; }
    public string Detail { get; }
    public string Impact { get; }
    public string PageKey { get; }

    public FleetImpactRow(EnvironmentImpactSnapshot impact)
    {
        ServerId = impact.ServerId;
        Host = impact.HostName;
        Component = impact.Component;
        State = impact.State switch
        {
            EnvironmentHealthState.Attention => "Attention",
            EnvironmentHealthState.Offline => "Offline",
            EnvironmentHealthState.Healthy => "Healthy",
            _ => "Unknown"
        };
        Detail = impact.Detail;
        Impact = impact.Impact;
        PageKey = impact.PageKey;
    }
}
