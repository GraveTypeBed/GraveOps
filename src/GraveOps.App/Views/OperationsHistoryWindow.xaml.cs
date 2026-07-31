using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using GraveOps.App.Models;
using GraveOps.App.Services;
using GraveOps.App.Windows;

namespace GraveOps.App.Views;

public partial class OperationsHistoryWindow : Window
{
    private readonly int _initialTab;
    private readonly StateHistoryService _stateHistory = new(App.Services);

    private AppServices S => App.Services;
    private GraveJob? SelectedJob => JobsGrid.SelectedItem as GraveJob;
    private NotificationRecord? SelectedAlert => AlertsGrid.SelectedItem as NotificationRecord;
    private SavedStateRecord? SelectedState => StateGrid.SelectedItem as SavedStateRecord;

    private ICollectionView? _jobsView;
    private ICollectionView? _alertsView;
    private bool _loaded;

    public OperationsHistoryWindow(int initialTab = 0)
    {
        InitializeComponent();
        _initialTab = Math.Clamp(initialTab, 0, 2);
        Loaded += OperationsHistoryWindow_Loaded;
        Closed += OperationsHistoryWindow_Closed;
    }

    private void OperationsHistoryWindow_Loaded(object sender, RoutedEventArgs e)
    {
        if (_loaded) return;
        _loaded = true;

        JobsGrid.ItemsSource = S.Jobs.Items;
        AlertsGrid.ItemsSource = S.Notifications.History;
        StateGrid.ItemsSource = _stateHistory.Items;

        _jobsView = CollectionViewSource.GetDefaultView(S.Jobs.Items);
        _alertsView = CollectionViewSource.GetDefaultView(S.Notifications.History);

        JobsFilterCombo.SelectedIndex = 0;
        AlertsFilterCombo.SelectedIndex = 0;
        ModeTabs.SelectedIndex = _initialTab;

        TargetText.Text = S.Context.Current?.Name ?? "No global target";

        S.Jobs.Changed += Jobs_Changed;
        S.Notifications.Changed += Notifications_Changed;

        if (ModeTabs.SelectedIndex == 1)
            S.Notifications.MarkRead();

        RefreshSummaries();
        UpdateJobSelection();
        UpdateAlertSelection();
        UpdateStateSelection();
    }

    private void OperationsHistoryWindow_Closed(object? sender, EventArgs e)
    {
        S.Jobs.Changed -= Jobs_Changed;
        S.Notifications.Changed -= Notifications_Changed;
    }

    private void Jobs_Changed()
        => Dispatcher.Invoke(() =>
        {
            _jobsView?.Refresh();
            RefreshSummaries();
            UpdateJobSelection();
        });

    private void Notifications_Changed()
        => Dispatcher.Invoke(() =>
        {
            _alertsView?.Refresh();
            RefreshSummaries();
            UpdateAlertSelection();
        });

    private void RefreshSummaries()
    {
        JobsSummaryText.Text =
            $"{S.Jobs.Items.Count} retained | {S.Jobs.RunningCount} running";

        var unacked = S.Notifications.History.Count(x => !x.Acknowledged);
        AlertsSummaryText.Text =
            $"{S.Notifications.History.Count} retained | {unacked} unacknowledged";
    }

    private void ModeTabs_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!IsLoaded || !_loaded || e.Source != ModeTabs) return;

        if (ModeTabs.SelectedIndex == 1)
            S.Notifications.MarkRead();

        RefreshSummaries();
    }

    private void JobsFilter_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_jobsView is null) return;

        _jobsView.Filter = item =>
        {
            if (item is not GraveJob job) return false;

            return JobsFilterCombo.SelectedIndex switch
            {
                1 => job.State is GraveJobState.Running or GraveJobState.Queued,
                2 => job.State is GraveJobState.Failed or GraveJobState.Cancelled,
                3 => job.State is GraveJobState.Success or GraveJobState.Failed or GraveJobState.Cancelled,
                _ => true
            };
        };

        _jobsView.Refresh();
    }

    private void AlertsFilter_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_alertsView is null) return;

        _alertsView.Filter = item =>
        {
            if (item is not NotificationRecord alert) return false;

            return AlertsFilterCombo.SelectedIndex switch
            {
                1 => !alert.Acknowledged,
                2 => alert.Severity.Equals("WARNING", StringComparison.OrdinalIgnoreCase) ||
                     alert.Severity.Equals("ERROR", StringComparison.OrdinalIgnoreCase),
                3 => alert.Severity.Equals("ERROR", StringComparison.OrdinalIgnoreCase),
                _ => true
            };
        };

        _alertsView.Refresh();
    }

    private void JobsGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
        => UpdateJobSelection();

    private void UpdateJobSelection()
    {
        var job = SelectedJob;

        if (job is null)
        {
            JobSelectionText.Text = "Select a job for contextual actions.";
            CancelJobButton.IsEnabled = false;
            RetryJobButton.IsEnabled = false;
            OpenJobButton.IsEnabled = false;
            return;
        }

        JobSelectionText.Text =
            $"{job.Title} | {job.StateText} | {job.Detail}";

        CancelJobButton.IsEnabled = S.Jobs.CanCancel(job);

        RetryJobButton.IsEnabled =
            job.State is GraveJobState.Failed or GraveJobState.Cancelled &&
            job.DeepLink.StartsWith("action:", StringComparison.OrdinalIgnoreCase);

        OpenJobButton.IsEnabled = !string.IsNullOrWhiteSpace(job.DeepLink);
    }

    private async void RetryJob_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedJob is not { } job ||
            !job.DeepLink.StartsWith("action:", StringComparison.OrdinalIgnoreCase))
            return;

        var actionName = job.DeepLink["action:".Length..];

        var action =
            S.Config.Current.Actions.FirstOrDefault(
                x => x.Name.Equals(actionName, StringComparison.OrdinalIgnoreCase));

        if (action is null)
        {
            GraveOpsDialog.Show(
                this,
                "The original action is no longer present in the GraveOps action library.",
                "Retry unavailable",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        var server =
            job.ServerId is { } serverId
                ? S.Config.Current.Servers.FirstOrDefault(x => x.Id == serverId)
                : S.Context.Current;

        if (server is null)
        {
            GraveOpsDialog.Show(
                this,
                "The server associated with this job is no longer available.",
                "Retry unavailable",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        if (action.Risk == ActionRisk.Dangerous)
        {
            var phrase =
                action.Name.Contains("reboot", StringComparison.OrdinalIgnoreCase)
                    ? "REBOOT"
                    : "RUN";

            var dangerous =
                new ConfirmDangerWindow(
                    action.Name,
                    action.Command,
                    phrase)
                {
                    Owner = this
                };

            if (dangerous.ShowDialog() != true)
                return;
        }
        else
        {
            if (GraveOpsDialog.Show(
                    this,
                    $"Retry '{action.Name}' on {server.Name}?",
                    "Retry job",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Question)
                != MessageBoxResult.Yes)
                return;
        }

        RetryJobButton.IsEnabled = false;
        StatusText.Text = $"Retrying {action.Name}...";

        var result =
            await S.ActionRunner.RunAsync(
                action,
                server);

        StatusText.Text =
            result.Success
                ? $"Retry succeeded: {action.Name}"
                : $"Retry failed: {action.Name}";

        if (!result.Success)
        {
            GraveOpsDialog.Show(
                this,
                string.IsNullOrWhiteSpace(result.Error)
                    ? result.Verification
                    : result.Error,
                $"{action.Name} failed",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    private void CancelJob_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedJob is not { } job || !S.Jobs.CanCancel(job))
            return;

        if (GraveOpsDialog.Show(
                this,
                $"Request cancellation for '{job.Title}'?",
                "Cancel job",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning)
            != MessageBoxResult.Yes)
            return;

        StatusText.Text =
            S.Jobs.RequestCancel(job)
                ? $"Cancellation requested: {job.Title}"
                : "This job is no longer cancellable.";

        UpdateJobSelection();
    }

    private void ClearCompletedJobs_Click(object sender, RoutedEventArgs e)
    {
        if (S.Jobs.Items.All(
                x => x.State is GraveJobState.Running or GraveJobState.Queued))
            return;

        if (GraveOpsDialog.Show(
                this,
                "Clear completed, failed and cancelled jobs from local GraveOps history? Running jobs are preserved.",
                "Clear completed jobs",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question)
            != MessageBoxResult.Yes)
            return;

        var removed = S.Jobs.ClearCompleted();
        StatusText.Text = $"Cleared {removed} completed job(s).";
        RefreshSummaries();
    }

    private void OpenJobRelated_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedJob is not { } job ||
            string.IsNullOrWhiteSpace(job.DeepLink))
            return;

        OpenDeepLink(job.DeepLink);
    }

    private void AlertsGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
        => UpdateAlertSelection();

    private void UpdateAlertSelection()
    {
        var alert = SelectedAlert;

        if (alert is null)
        {
            AlertSelectionText.Text =
                "Select an alert to acknowledge or open its related control.";
            AcknowledgeAlertButton.IsEnabled = false;
            OpenAlertButton.IsEnabled = false;
            return;
        }

        AlertSelectionText.Text =
            $"{alert.Severity} | {alert.Title} | {alert.Message}";

        AcknowledgeAlertButton.IsEnabled = !alert.Acknowledged;
        OpenAlertButton.IsEnabled = !string.IsNullOrWhiteSpace(alert.DeepLink);
    }

    private void AcknowledgeSelected_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedAlert is not { } alert) return;

        S.Notifications.Acknowledge(alert);
        StatusText.Text = $"Acknowledged: {alert.Title}";
        _alertsView?.Refresh();
        RefreshSummaries();
    }

    private void AcknowledgeAll_Click(object sender, RoutedEventArgs e)
    {
        if (S.Notifications.History.Count == 0) return;

        S.Notifications.AcknowledgeAll();
        StatusText.Text = "All alerts acknowledged.";
        _alertsView?.Refresh();
        RefreshSummaries();
    }

    private void ClearAcknowledged_Click(object sender, RoutedEventArgs e)
    {
        if (!S.Notifications.History.Any(x => x.Acknowledged))
            return;

        if (GraveOpsDialog.Show(
                this,
                "Clear acknowledged alerts from local GraveOps history?",
                "Clear acknowledged alerts",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question)
            != MessageBoxResult.Yes)
            return;

        var removed = S.Notifications.ClearAcknowledged();
        StatusText.Text = $"Cleared {removed} acknowledged alert(s).";
        _alertsView?.Refresh();
        RefreshSummaries();
    }

    private void OpenAlertRelated_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedAlert is not { } alert ||
            string.IsNullOrWhiteSpace(alert.DeepLink))
            return;

        alert.IsRead = true;
        OpenDeepLink(alert.DeepLink);
    }

    private void OpenDeepLink(string deepLink)
    {
        if (deepLink.StartsWith("action:", StringComparison.OrdinalIgnoreCase))
            S.Navigation.Request("page:Services");
        else if (deepLink.StartsWith("app:", StringComparison.OrdinalIgnoreCase))
            S.Navigation.Request("page:Applications");
        else
            S.Navigation.Request(deepLink);

        Close();
    }

    private void StateGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
        => UpdateStateSelection();

    private void UpdateStateSelection()
    {
        var item = SelectedState;

        CompareStateButton.IsEnabled = item is not null;
        DeleteStateButton.IsEnabled = item is not null;

        if (item is null)
        {
            StateSelectionText.Text =
                "Capture the current target or select a saved snapshot.";
            StateDetailBox.Text = "";
            return;
        }

        StateSelectionText.Text =
            $"{item.ServerName} | {item.TimeText} | {item.Label}";

        StateDetailBox.Text =
            string.Join(
                Environment.NewLine,
                item.Snapshot.Lines());
    }

    private async void CaptureState_Click(object sender, RoutedEventArgs e)
    {
        if (S.Context.Current is not { } server)
        {
            GraveOpsDialog.Show(
                this,
                "Select a global server target before capturing state.",
                "No target",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        CaptureStateButton.IsEnabled = false;
        StatusText.Text = $"Capturing state from {server.Name}...";

        try
        {
            var item =
                await _stateHistory.CaptureAsync(
                    server);

            StateGrid.SelectedItem = item;
            StateGrid.ScrollIntoView(item);
            StatusText.Text = $"Saved state snapshot for {server.Name}.";
        }
        catch (OperationCanceledException)
        {
            StatusText.Text = "State capture cancelled.";
        }
        catch (Exception ex)
        {
            StatusText.Text = ex.Message;

            GraveOpsDialog.Show(
                this,
                ex.Message,
                "State capture failed",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
        finally
        {
            CaptureStateButton.IsEnabled = true;
        }
    }

    private async void CompareState_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedState is not { } item) return;

        CompareStateButton.IsEnabled = false;
        StatusText.Text = "Capturing current state for comparison...";

        try
        {
            var diff =
                await _stateHistory.CompareToLiveAsync(
                    item);

            StateDetailBox.Text =
                $"SAVED SNAPSHOT\n{item.ServerName} | {item.TimeText}\n\nCOMPARE TO CURRENT\n{diff}";

            StatusText.Text = "Comparison completed.";
        }
        catch (Exception ex)
        {
            StatusText.Text = ex.Message;

            GraveOpsDialog.Show(
                this,
                ex.Message,
                "State comparison failed",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
        finally
        {
            CompareStateButton.IsEnabled = SelectedState is not null;
        }
    }

    private void DeleteState_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedState is not { } item) return;

        if (GraveOpsDialog.Show(
                this,
                $"Delete the saved state captured {item.TimeText}?",
                "Delete state snapshot",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning)
            != MessageBoxResult.Yes)
            return;

        _stateHistory.Delete(item);
        StatusText.Text = "Saved state deleted.";
        UpdateStateSelection();
    }

    private void Close_Click(object sender, RoutedEventArgs e)
        => Close();
}