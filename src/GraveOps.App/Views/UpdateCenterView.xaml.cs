using System.Windows;
using System.Windows.Controls;
using GraveOps.App.Models;

namespace GraveOps.App.Views;

public partial class UpdateCenterView : UserControl
{
    private Services.AppServices S => App.Services;
    private ServerProfile? Server => S.Context.Current;
    public UpdateCenterView() { InitializeComponent(); Loaded += async (_, _) => await RefreshAsync(); }
    private async void Refresh_Click(object sender, RoutedEventArgs e) => await RefreshAsync();

    private async Task RefreshAsync()
    {
        if (Server is not { } p) { OutputBox.Text = "Select a host first."; return; }
        RefreshButton.IsEnabled = false;
        OutputBox.Text = "Checking read-only update inventory...";
        try
        {
            var provider = S.Hosts.Resolve(p);
            var probe = await provider.ProbeAsync(p);
            var environment = await S.Environment.GetSnapshotAsync(false);
            var host = environment.Hosts.FirstOrDefault(x => x.ServerId == p.Id);

            HostText.Text = string.IsNullOrWhiteSpace(probe.HostName) ? p.Name : probe.HostName;
            PlatformText.Text = $"{probe.OperatingSystem} | {probe.Architecture}";
            var dockerAvailable = probe.Capabilities.HasFlag(HostCapability.Docker);
            DockerText.Text = dockerAvailable ? "Available" : "Not detected";
            IntegrationsText.Text = (host?.Apps.Count ?? 0).ToString();
            PlexText.Text = host?.Apps.FirstOrDefault(x => x.Name.Equals("Plex", StringComparison.OrdinalIgnoreCase)) is { } plex
                ? $"{plex.State} | {plex.Detail}" : "Not verified";

            var details = new List<string>
            {
                $"Host: {probe.HostName}",
                $"Platform: {probe.OperatingSystem}",
                $"Architecture: {probe.Architecture}",
                $"Uptime: {probe.Uptime}",
                $"Docker: {(dockerAvailable ? "available" : "not detected")}",
                $"Storage roots: {probe.StorageRoots.Count}",
                $"Verified applications: {host?.Apps.Count ?? 0}"
            };

            if (p.ConnectionKind == HostConnectionKind.RemoteLinux)
            {
                var cmd = "echo '__UPDATES__'; if command -v apt >/dev/null 2>&1; then apt list --upgradable 2>/dev/null | tail -n +2 | sed '/^$/d' | wc -l; elif command -v dnf >/dev/null 2>&1; then dnf -q check-update 2>/dev/null | sed '/^$/d' | wc -l; else echo NA; fi; echo '__KERNEL__'; uname -r";
                var r = await S.Ssh.ExecuteAsync(p, cmd, 45);
                var lines = r.StdOut.Replace("\r", "").Split('\n');
                PackagesText.Text = After(lines, "__UPDATES__", 1, "--");
                PackagesSubtext.Text = PackagesText.Text == "NA" ? "package manager not inventoried" : "packages reported upgradable";
                details.Add($"OS update inventory: {PackagesText.Text}");
                details.Add($"Kernel: {After(lines, "__KERNEL__", 1, "--")}");
            }
            else if (p.ConnectionKind == HostConnectionKind.RemoteWindows)
            {
                PackagesText.Text = "Windows";
                PackagesSubtext.Text = "managed by Windows Update";
                details.Add("Windows Update: managed by the operating system; GraveOps does not install updates.");
            }
            else
            {
                PackagesText.Text = "Windows";
                PackagesSubtext.Text = "managed by Windows Update";
                details.Add("Windows Update: managed by the operating system; GraveOps does not install updates.");
            }

            OutputBox.Text = string.Join(Environment.NewLine, details);
            S.Activity.Record("Update inventory checked", $"Read-only check completed for {p.Name}.", ActivityLevel.Info, serverId: p.Id, deepLink: "page:Updates");
        }
        catch (Exception ex) { OutputBox.Text = ex.ToString(); }
        finally { RefreshButton.IsEnabled = true; }
    }

    private static string After(string[] lines, string marker, int offset, string fallback)
    {
        var i = Array.FindIndex(lines, x => x.Trim() == marker);
        return i >= 0 && i + offset < lines.Length && !string.IsNullOrWhiteSpace(lines[i + offset]) ? lines[i + offset].Trim() : fallback;
    }

    private void Terminal_Click(object sender, RoutedEventArgs e)
    {
        if (Server is not { } p) return;
        if (p.ConnectionKind == HostConnectionKind.RemoteLinux)
            TerminalView.QueueSshHandoff(p, "~");
        S.Navigation.Request("page:Terminal");
    }
}
