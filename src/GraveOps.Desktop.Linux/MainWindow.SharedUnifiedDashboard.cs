using Avalonia.Controls;
using Avalonia.Interactivity;
using GraveOps.Presentation.Avalonia.Dashboard;
using SharedAction =
    GraveOps.Presentation.Avalonia.Dashboard.UnifiedDashboardAction;
using SharedCard =
    GraveOps.Presentation.Avalonia.Dashboard.UnifiedDashboardCard;
using SharedRow =
    GraveOps.Presentation.Avalonia.Dashboard.UnifiedDashboardRow;

namespace GraveOps.Desktop.Linux;

public partial class MainWindow
{
    private void InitializeSharedUnifiedDashboard()
    {
        var view =
            Get<UnifiedDashboardView>(
                "SharedDashboardView");

        view.RefreshRequested +=
            async (_, _) =>
                await RefreshAsync();

        view.ActionRequested +=
            (_, eventArgs) =>
            {
                var action =
                    eventArgs.Action;

                UnifiedDashboardActionButton_OnClick(
                    new Button
                    {
                        Tag =
                            new UnifiedDashboardAction(
                                action.Label,
                                action.NavigationName,
                                action.Endpoint,
                                action.IsPrimary,
                                action.LogSource,
                                action.LogText,
                                action.IncludeInformationalLogs,
                                action.LogContext)
                    },
                    new RoutedEventArgs());
            };

        view.LayoutChanged +=
            (_, eventArgs) =>
            {
                _unifiedInterface.DashboardLayouts[
                        eventArgs.HostKey] =
                    eventArgs.Layout
                        .Select(item =>
                            new DashboardCardPreference
                            {
                                Key =
                                    item.Key,
                                IsVisible =
                                    item.IsVisible,
                                VisibilityExplicit =
                                    true,
                                Order =
                                    item.Order
                            })
                        .ToList();

                _unifiedInterfaceStore?.Save(
                    _unifiedInterface);

                PopulateUnifiedDashboard();
            };

        view.Update(
            UnifiedDashboardState.Waiting);
    }

    private void UpdateSharedUnifiedDashboard(
        IReadOnlyList<OpsFinding> actionable,
        IReadOnlyList<DashboardCardPreference> layout)
    {
        var cards =
            _unifiedDashboardCards
                .Select(
                    MapSharedDashboardCard)
                .ToArray();

        var sharedLayout =
            layout
                .Select(item =>
                    new GraveOps.Presentation.Avalonia.Dashboard
                        .DashboardCardPreference(
                            item.Key,
                            item.IsVisible,
                            item.Order))
                .ToArray();

        var top =
            actionable.FirstOrDefault();

        Get<UnifiedDashboardView>(
                "SharedDashboardView")
            .Update(
                new UnifiedDashboardState(
                    _controlPlane.ActiveProfile.Id,
                    Get<TextBlock>(
                            "UnifiedDashboardStatusText")
                        .Text ??
                    string.Empty,
                    actionable.Count == 0
                        ? "Healthy"
                        : $"{actionable.Count} active finding{(actionable.Count == 1 ? string.Empty : "s")}",
                    top is null
                        ? _policyEvaluation?.Muted.Count > 0
                            ? $"0 active findings \u00B7 {_policyEvaluation.Muted.Count} muted by policy"
                            : "0 active findings"
                        : $"{LinuxOpsAnalyzer.SeverityLabel(top.Severity)} \u00B7 {top.Component} \u00B7 {top.Problem}",
                    actionable.Count == 0,
                    _unifiedInterface.Density,
                    cards,
                    sharedLayout));
    }

    private static SharedCard MapSharedDashboardCard(
        UnifiedDashboardCard card) =>
        new(
            card.Key,
            card.Title,
            card.Category,
            card.Status,
            MapSharedSeverity(
                card.Severity),
            card.PrimaryValue,
            card.Summary,
            card.Detail,
            card.ActionLabel,
            card.NavigationName,
            card.Endpoint,
            card.SourceKey,
            card.DefaultVisible)
        {
            Facts =
                card.Facts,
            Rows =
                card.Rows
                    .Select(row =>
                        new SharedRow(
                            row.Label,
                            row.Value,
                            row.Detail,
                            MapSharedSeverity(
                                row.Severity),
                            row.SecondaryValue))
                    .ToArray(),
            Actions =
                card.Actions
                    .Select(action =>
                        new SharedAction(
                            action.Label,
                            action.NavigationName,
                            action.Endpoint,
                            action.IsPrimary,
                            action.LogSource,
                            action.LogText,
                            action.IncludeInformationalLogs,
                            action.LogContext))
                    .ToArray()
        };

    private static DashboardSeverity MapSharedSeverity(
        OpsSeverity severity) =>
        severity switch
        {
            OpsSeverity.Critical =>
                DashboardSeverity.Error,

            OpsSeverity.Error =>
                DashboardSeverity.Error,

            OpsSeverity.Warning =>
                DashboardSeverity.Warning,

            OpsSeverity.Healthy =>
                DashboardSeverity.Healthy,

            _ =>
                DashboardSeverity.Info
        };
}