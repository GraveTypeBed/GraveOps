using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using GraveOps.App.Models;
using Microsoft.Win32;
using MessageBox = GraveOps.App.Windows.GraveOpsMessageBox;

namespace GraveOps.App.Views;

public partial class SettingsView : UserControl
{
    private Services.AppServices S => App.Services;
    public SettingsView()
    {
        InitializeComponent();
        var x = S.Config.Current.Settings;
        RefreshBox.Text = x.DashboardRefreshSeconds.ToString(); MonitorBox.Text = x.MonitorIntervalSeconds.ToString();
        EmbeddedCheck.IsChecked = x.OpenAppsEmbedded; ConfirmNormalCheck.IsChecked = x.ConfirmNormalActions; NotificationsCheck.IsChecked = x.EnableDesktopNotifications;
        MaintenanceCheck.IsChecked = x.MaintenanceMode; SafeModeCheck.IsChecked = x.SafeMode; StartTrayCheck.IsChecked = x.StartMinimizedToTray; CloseTrayCheck.IsChecked = x.CloseToTray; OverviewCheck.IsChecked = x.ShowOverviewDrawer; CompactCheck.IsChecked = x.CompactLayout; QuickModulesCheck.IsChecked = x.ShowQuickModules; FleetHistoryCheck.IsChecked = x.EnableFleetHistory;
        ConfigPathText.Text = S.Config.FilePath;
        WakeServerCombo.ItemsSource = S.Config.Current.Servers; WakeServerCombo.SelectedItem = S.Context.Current ?? S.Config.Current.Servers.FirstOrDefault();
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        if (!int.TryParse(RefreshBox.Text, out var sec) || sec < 5) { StatusText.Text = "Refresh interval must be at least 5 seconds."; return; }
        if (!int.TryParse(MonitorBox.Text, out var monitor) || monitor < 30) { StatusText.Text = "Monitor interval must be at least 30 seconds."; return; }
        var x = S.Config.Current.Settings;
        x.DashboardRefreshSeconds = sec; x.MonitorIntervalSeconds = monitor; x.EnableDesktopNotifications = NotificationsCheck.IsChecked == true; x.OpenAppsEmbedded = EmbeddedCheck.IsChecked == true; x.ConfirmNormalActions = ConfirmNormalCheck.IsChecked == true;
        x.MaintenanceMode = MaintenanceCheck.IsChecked == true; x.SafeMode = SafeModeCheck.IsChecked == true; x.StartMinimizedToTray = StartTrayCheck.IsChecked == true; x.CloseToTray = CloseTrayCheck.IsChecked == true; x.ShowOverviewDrawer = OverviewCheck.IsChecked == true; x.CompactLayout = CompactCheck.IsChecked == true; x.ShowQuickModules = QuickModulesCheck.IsChecked == true; x.EnableFleetHistory = FleetHistoryCheck.IsChecked == true;
        S.Config.Save();
        S.Activity.Record("Settings changed", x.MaintenanceMode ? "Maintenance Mode enabled." : "Desktop settings saved.", ActivityLevel.Info, deepLink: "page:Settings");
        StatusText.Text = "Settings saved. Layout changes apply immediately after navigating or restarting GraveOps.";
    }

    private void OpenFolder_Click(object sender, RoutedEventArgs e) => Process.Start(new ProcessStartInfo(S.Config.DirectoryPath) { UseShellExecute = true });
    private void Export_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new SaveFileDialog { FileName = $"graveops-profile-{DateTime.Now:yyyyMMdd}.json", Filter = "JSON|*.json" };
        if (dlg.ShowDialog() == true) { S.Profiles.Export(dlg.FileName); StatusText.Text = "Exported non-secret GraveOps profile. Credentials were not included."; }
    }
    private void Import_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFileDialog { Filter = "GraveOps profile|*.json|JSON|*.json" };
        if (dlg.ShowDialog() != true) return;
        if (MessageBox.Show("Import this GraveOps profile? The current config will be snapshotted first. Imported server credentials must already exist in Windows Credential Manager or be re-entered.", "Import GraveOps profile", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
        try { S.Profiles.Import(dlg.FileName); StatusText.Text = "Profile imported. Restart GraveOps to fully reload all pages."; }
        catch (Exception ex) { StatusText.Text = ex.Message; }
    }
    private void Setup_Click(object sender, RoutedEventArgs e) { var w = new SetupWizardWindow { Owner = Window.GetWindow(this) }; w.ShowDialog(); }

    private void WakeServer_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        WakeMacBox.Text = (WakeServerCombo.SelectedItem as ServerProfile)?.WakeMacAddress ?? "";
    }
    private void SaveWake_Click(object sender, RoutedEventArgs e)
    {
        if (WakeServerCombo.SelectedItem is not ServerProfile p) return;
        p.WakeMacAddress = WakeMacBox.Text.Trim(); S.Config.Save(); StatusText.Text = $"Saved Wake-on-LAN MAC for {p.Name}.";
    }
    private async void Wake_Click(object sender, RoutedEventArgs e)
    {
        if (WakeServerCombo.SelectedItem is not ServerProfile p) return;
        try { await S.WakeOnLan.SendAsync(WakeMacBox.Text.Trim()); p.WakeMacAddress = WakeMacBox.Text.Trim(); S.Config.Save(); S.Activity.Record("Wake-on-LAN sent", p.Name, ActivityLevel.Info, serverId: p.Id, deepLink: $"server:{p.Id}"); StatusText.Text = $"Wake packet sent for {p.Name}."; }
        catch (Exception ex) { StatusText.Text = ex.Message; }
    }
}