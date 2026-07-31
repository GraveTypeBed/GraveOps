using System.Windows;
using MessageBox = GraveOps.App.Windows.GraveOpsMessageBox;
using System.Windows.Controls;
using GraveOps.App.Models;

namespace GraveOps.App.Views;

public partial class DockerView : UserControl
{
    private Services.AppServices S => App.Services;
    public DockerView() { InitializeComponent(); ServerCombo.ItemsSource = S.Config.Current.Servers; ServerCombo.SelectedItem = S.Config.GetSelectedServer(); Loaded += DockerView_Loaded; }
    private bool _loadedOnce;
    private ServerProfile? Server => ServerCombo.SelectedItem as ServerProfile;
    private DockerContainerRow? Container => ContainerGrid.SelectedItem as DockerContainerRow;
    private async void DockerView_Loaded(object sender, RoutedEventArgs e)
    {
        if (_loadedOnce || Server is null) return;
        _loadedOnce = true;
        await LoadContainersAsync();
    }

    private async Task LoadContainersAsync()
    {
        if (Server is not { } p) return;
        OutputBox.Text = "Loading containers...";
        try
        {
            var r = await S.Ssh.ExecuteAsync(p, "docker ps -a --format '{{.Names}}|{{.Image}}|{{.Status}}|{{.Ports}}'", 30);
            var rows = r.StdOut.Split('\n', StringSplitOptions.RemoveEmptyEntries)
                .Select(line => line.Split('|'))
                .Where(x => x.Length >= 4)
                .Select(x => new DockerContainerRow(x[0], x[1], x[2], string.Join('|', x.Skip(3))))
                .ToList();
            ContainerGrid.ItemsSource = rows;
            OutputBox.Text = rows.Count == 0
                ? "No Docker containers were returned for this host."
                : $"Loaded {rows.Count} containers. Select one for logs, inspection or lifecycle actions.";
        }
        catch (Exception ex)
        {
            OutputBox.Text = ex.ToString();
        }
    }
    private async void Refresh_Click(object sender, RoutedEventArgs e)
        => await LoadContainersAsync();
    private async void Logs_Click(object sender, RoutedEventArgs e) { if (Server is not { } p || Container is not { } c) return; try { var r = await S.Ssh.ExecuteAsync(p, $"docker logs --tail 150 {Shell(c.Name)} 2>&1", 60); OutputBox.Text = r.Combined; } catch (Exception ex) { OutputBox.Text = ex.Message; } }
    private async void Restart_Click(object sender, RoutedEventArgs e) { if (S.Config.Current.Settings.SafeMode) { MessageBox.Show("Safe Mode blocks Docker restart/stop operations.", "GraveOps Safe Mode"); return; } if (Server is not { } p || Container is not { } c) return; if (MessageBox.Show($"Restart container {c.Name}?", "Restart", MessageBoxButton.YesNo) != MessageBoxResult.Yes) return; var r = await S.Ssh.ExecuteAsync(p, $"docker restart {Shell(c.Name)}", 120); OutputBox.Text = r.Combined; Refresh_Click(sender, e); }
    private async void Stop_Click(object sender, RoutedEventArgs e) { if (S.Config.Current.Settings.SafeMode) { MessageBox.Show("Safe Mode blocks Docker restart/stop operations.", "GraveOps Safe Mode"); return; } if (Server is not { } p || Container is not { } c) return; var dlg = new ConfirmDangerWindow($"Stop Docker container {c.Name}", $"docker stop {c.Name}", "STOP"); if (dlg.ShowDialog() != true) return; var r = await S.Ssh.ExecuteAsync(p, $"docker stop {Shell(c.Name)}", 120); OutputBox.Text = r.Combined; Refresh_Click(sender, e); }
    private static string Shell(string value) => "'" + value.Replace("'", "'\\''") + "'";

    private void DockerDrilldown_Click(object sender, RoutedEventArgs e)
    {
        var window = new OperationsDrillDownWindow(0);
        if (Window.GetWindow(this) is Window owner) window.Owner = owner;
        window.ShowDialog();
    }
}
