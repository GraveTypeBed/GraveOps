using System.Windows;
using System.Windows.Controls;
using GraveOps.App.Models;

namespace GraveOps.App.Views;

public partial class MediaLifecycleView : UserControl
{
    private Services.AppServices S => App.Services;
    private MediaLifecycleSnapshot? _snapshot;
    private EnvironmentOverviewSnapshot? _environment;

    private ServerProfile? Server => S.Context.Current ?? S.Config.GetSelectedServer();

    public MediaLifecycleView()
    {
        InitializeComponent();
        Loaded += async (_, _) => await RefreshAsync(true);
    }

    private async Task RefreshAsync(bool force)
    {
        if (Server is not { } server)
        {
            StatusText.Text = "No active host is selected.";
            return;
        }

        RefreshButton.IsEnabled = false;
        TargetText.Text = server.Name;
        StatusText.Text = "Refreshing lifecycle and dependency context...";

        try
        {
            _snapshot = await S.Lifecycle.GetSnapshotAsync(server, force);
            _environment = await S.Environment.GetSnapshotAsync(force);
            var steps = await S.Lifecycle.BuildRemediationAsync(server, _environment, _snapshot);

            ActiveText.Text = _snapshot.ActiveCount.ToString();
            AttentionText.Text = _snapshot.AttentionCount.ToString();
            AttentionText.Foreground = _snapshot.AttentionCount == 0
                ? (System.Windows.Media.Brush)FindResource("Success")
                : (System.Windows.Media.Brush)FindResource("Warn");
            DownloadingText.Text = _snapshot.DownloadingCount.ToString();
            ImportText.Text = _snapshot.ImportCount.ToString();

            RequestStageText.Text = _snapshot.HasSeerr ? "Seerr detected" : "Optional";
            ProcessingStageText.Text = _snapshot.HasTdarr || _snapshot.HasBazarr
                ? string.Join(" · ", new[] { _snapshot.HasTdarr ? "Tdarr" : "", _snapshot.HasBazarr ? "Bazarr" : "" }.Where(x => x.Length > 0))
                : "Optional";
            LibraryStageText.Text = _snapshot.HasLibrary ? "Library detected" : "Optional";

            LifecycleGrid.ItemsSource = _snapshot.Items;
            LifecycleGrid.Visibility = _snapshot.Items.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
            LifecycleEmptyText.Visibility = _snapshot.Items.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

            RemediationList.ItemsSource = steps;
            RemediationList.Visibility = steps.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
            RemediationEmptyText.Visibility = steps.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

            StatusText.Text = $"Lifecycle updated {DateTime.Now:HH:mm:ss} · {_snapshot.ActiveCount} active item(s) · {_snapshot.AttentionCount} needing attention.";
        }
        catch (Exception ex)
        {
            StatusText.Text = ex.Message;
        }
        finally
        {
            RefreshButton.IsEnabled = true;
        }
    }

    private async void Refresh_Click(object sender, RoutedEventArgs e) => await RefreshAsync(true);

    private void LifecycleGrid_MouseDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (LifecycleGrid.SelectedItem is MediaLifecycleItem item)
            S.Navigation.Request(item.DeepLink);
    }

    private void RemediationList_MouseDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
        => OpenSelectedRemediation();

    private void OpenSelectedRemediation_Click(object sender, RoutedEventArgs e)
        => OpenSelectedRemediation();

    private void OpenSelectedRemediation()
    {
        if (RemediationList.SelectedItem is RemediationStep step &&
            !string.IsNullOrWhiteSpace(step.DeepLink))
            S.Navigation.Request(step.DeepLink);
    }
}
