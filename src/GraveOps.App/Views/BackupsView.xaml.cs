using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using GraveOps.App.Models;

namespace GraveOps.App.Views;

public partial class BackupsView : UserControl
{
    private Services.AppServices S => App.Services;
    private ServerProfile? Server => S.Context.Current;

    public BackupsView()
    {
        InitializeComponent();
        Loaded += async (_, _) => await RefreshStatusAsync();
        S.Context.TargetChanged += Context_TargetChanged;
        Unloaded += (_, _) => S.Context.TargetChanged -= Context_TargetChanged;
    }

    private void Context_TargetChanged(ServerProfile? _) =>
        Dispatcher.BeginInvoke(async () => await RefreshStatusAsync());

    private async void Refresh_Click(object sender, RoutedEventArgs e) =>
        await RefreshStatusAsync();

    private async Task RefreshStatusAsync()
    {
        if (Server is not { } server)
        {
            SetEmptyState("Select a host first.");
            return;
        }

        RefreshButton.IsEnabled = false;
        InventoryBox.Text = "Inspecting backup readiness...";

        try
        {
            var snapshot = await S.Backups.InspectAsync(server);

            ReadinessText.Text = snapshot.ReadinessText;
            ReadinessText.Foreground = BrushFor(snapshot.Readiness);
            ReadinessSubtext.Text = snapshot.Readiness switch
            {
                BackupReadiness.Configured => "Backup schedule or protected action is configured",
                BackupReadiness.Available => "Backup tooling is available but no schedule/action was verified",
                BackupReadiness.Attention => "Backup inventory could not be completed cleanly",
                _ => "No backup schedule, provider or protected action was verified"
            };

            ProviderText.Text = snapshot.ProviderText;
            ScheduleCountText.Text = snapshot.Schedules.Count.ToString();
            ActionCountText.Text = snapshot.Actions.Count.ToString();

            var lines = new List<string>
            {
                $"Host: {server.Name}",
                $"Readiness: {snapshot.ReadinessText}",
                $"Provider evidence: {snapshot.ProviderText}",
                $"Observed schedules: {snapshot.Schedules.Count}",
                $"Protected GraveOps actions: {snapshot.Actions.Count}",
                ""
            };

            if (snapshot.Evidence.Count > 0)
            {
                lines.Add("Evidence:");
                lines.AddRange(snapshot.Evidence.Select(x => "  " + x));
                lines.Add("");
            }

            if (snapshot.Schedules.Count > 0)
            {
                lines.Add("Schedules / tasks:");
                lines.AddRange(snapshot.Schedules.Select(x => "  " + x));
                lines.Add("");
            }

            if (snapshot.Actions.Count > 0)
            {
                lines.Add("Protected actions:");
                lines.AddRange(snapshot.Actions.Select(x => $"  {x.Name} [{x.RiskLabel}]"));
                lines.Add("");
            }

            if (snapshot.Readiness == BackupReadiness.NotConfigured)
            {
                lines.Add("Nothing was guessed. Configure backup tooling/scheduling on the host or add an explicit protected GraveOps action.");
            }

            InventoryBox.Text = string.Join(Environment.NewLine, lines).TrimEnd();
        }
        catch (Exception ex)
        {
            ReadinessText.Text = "UNAVAILABLE";
            ReadinessText.Foreground = (Brush)Application.Current.FindResource("Warn");
            ReadinessSubtext.Text = "Backup inventory could not be completed";
            ProviderText.Text = "--";
            ScheduleCountText.Text = "--";
            ActionCountText.Text = "--";
            InventoryBox.Text = ex.ToString();
        }
        finally
        {
            RefreshButton.IsEnabled = true;
        }
    }

    private void SetEmptyState(string message)
    {
        ReadinessText.Text = "--";
        ReadinessText.Foreground = (Brush)Application.Current.FindResource("Muted");
        ReadinessSubtext.Text = message;
        ProviderText.Text = "--";
        ScheduleCountText.Text = "--";
        ActionCountText.Text = "--";
        InventoryBox.Text = message;
    }

    private static Brush BrushFor(BackupReadiness readiness) =>
        (Brush)Application.Current.FindResource(readiness switch
        {
            BackupReadiness.Configured => "Success",
            BackupReadiness.Available => "Accent",
            BackupReadiness.Attention => "Warn",
            _ => "Muted"
        });

    private void Actions_Click(object sender, RoutedEventArgs e) =>
        S.Navigation.Request("page:Services");

    private void Terminal_Click(object sender, RoutedEventArgs e) =>
        S.Navigation.Request("page:Terminal");
}
