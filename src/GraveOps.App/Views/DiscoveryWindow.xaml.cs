using System.Windows;

namespace GraveOps.App.Views;

public partial class DiscoveryWindow : Window
{
    public DiscoveryWindow() => InitializeComponent();
    private async void Scan_Click(object sender, RoutedEventArgs e)
    {
        ScanButton.IsEnabled = false; ProgressText.Text = "Starting...";
        try
        {
            var progress = new Progress<(int Done, int Total)>(x => ProgressText.Text = $"Scanning {x.Done}/{x.Total}...");
            var rows = await App.Services.Discovery.ScanLocal24Async(progress); ResultsGrid.ItemsSource = rows; ProgressText.Text = $"Found {rows.Count} host(s) with matching ports.";
        }
        catch (Exception ex) { ProgressText.Text = ex.Message; }
        finally { ScanButton.IsEnabled = true; }
    }
}
