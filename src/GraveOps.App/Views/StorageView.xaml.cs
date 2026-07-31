using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using GraveOps.App.Models;

namespace GraveOps.App.Views;

public partial class StorageView : UserControl
{
    private Services.AppServices S => App.Services;
    private ServerProfile? Server => S.Context.Current;

    public StorageView()
    {
        InitializeComponent();
        Loaded += StorageView_OrganizedLoaded;
    }

    private async void StorageView_OrganizedLoaded(object sender, RoutedEventArgs e) =>
        await RefreshAsync(deep: false);

    private async void Refresh_Click(object sender, RoutedEventArgs e) =>
        await RefreshAsync(deep: true);

    private void ServerCombo_SelectionChanged(object sender, SelectionChangedEventArgs e) { }

    private async Task RefreshAsync(bool deep)
    {
        if (Server is not { } profile)
        {
            TelemetryStatus.Text = "Select a host first.";
            return;
        }

        RefreshButton.IsEnabled = false;
        TelemetryStatus.Text = deep ? "Refreshing storage and device telemetry..." : "Loading storage snapshot...";

        try
        {
            var rows = profile.ConnectionKind switch
            {
                HostConnectionKind.LocalWindows => ProbeLocalWindows(),
                HostConnectionKind.RemoteWindows => await ProbeRemoteWindowsAsync(profile),
                HostConnectionKind.RemoteLinux => await ProbeRemoteLinuxAsync(profile, deep),
                _ => new List<DriveCard>()
            };

            DriveCards.ItemsSource = rows;
            MountSummary.Text = rows.Count.ToString();
            MountSubtext.Text = rows.Count == 1
                ? "1 meaningful storage root detected"
                : $"{rows.Count} meaningful storage roots detected";

            var smartKnown = rows.Where(x => !x.SmartText.Equals("On demand", StringComparison.OrdinalIgnoreCase)).ToList();
            var smartBad = smartKnown.Count(x => x.SmartText.Contains("fail", StringComparison.OrdinalIgnoreCase) ||
                                                x.SmartText.Contains("attention", StringComparison.OrdinalIgnoreCase));
            SmartSummary.Text = smartKnown.Count == 0 ? "ON DEMAND" : smartBad == 0 ? "HEALTHY" : "ATTENTION";
            SmartSubtext.Text = smartKnown.Count == 0
                ? "Use Refresh or Raw SMART summary for hardware health"
                : $"{smartKnown.Count} device result(s), {smartBad} need attention";

            var hottest = rows.Where(x => x.TemperatureC is not null).OrderByDescending(x => x.TemperatureC).FirstOrDefault();
            TempSummary.Text = hottest?.TemperatureC is { } c ? $"{c:0.#} °C" : "ON DEMAND";
            TempSubtext.Text = hottest is null ? "Temperature telemetry is provider/device dependent" : hottest.Name;

            var primary = rows.FirstOrDefault(x => x.IsPrimary) ?? rows.FirstOrDefault();
            RootSummary.Text = primary is null ? "--" : $"{primary.UsagePercent:0}%";
            RootSubtext.Text = primary is null ? "No filesystem data" : $"{primary.FreeText} free on {primary.MountPoint}";

            ForecastList.ItemsSource = rows.Take(8).Select(x =>
                $"{x.Name}: {x.FreeText} free | {(x.UsagePercent >= 90 ? "capacity pressure" : x.UsagePercent >= 80 ? "watch usage" : "capacity healthy")}")
                .ToArray();

            var dependencies = new List<string> { "Host -> storage roots" };
            if (profile.EnabledModules.Contains("Docker", StringComparer.OrdinalIgnoreCase))
                dependencies.Add("Storage -> Docker / container data");
            var owned = S.Config.Current.Applications.Count(x => x.ServerId == profile.Id && x.DiscoveryVerified);
            if (owned > 0)
                dependencies.Add($"Storage -> {owned} verified application(s)");
            dependencies.Add("Applications -> workflow / library availability");
            DependencyList.ItemsSource = dependencies;

            TelemetryStatus.Text = $"LIVE - storage snapshot {DateTime.Now:HH:mm:ss}. Hardware SMART stays on demand unless the provider exposes it.";
        }
        catch (Exception ex)
        {
            TelemetryStatus.Text = "Storage snapshot unavailable: " + ex.Message;
        }
        finally
        {
            RefreshButton.IsEnabled = true;
        }
    }

    private static List<DriveCard> ProbeLocalWindows()
    {
        var systemRoot = Path.GetPathRoot(Environment.SystemDirectory) ?? "C:\\";
        return DriveInfo.GetDrives()
            .Where(d => d.IsReady)
            .Select(d =>
            {
                var total = d.TotalSize;
                var free = d.AvailableFreeSpace;
                var used = Math.Max(0, total - free);
                return BuildDrive(
                    d.Name.TrimEnd('\\'),
                    d.RootDirectory.FullName,
                    d.DriveFormat,
                    total,
                    used,
                    free,
                    "On demand",
                    null,
                    d.VolumeLabel,
                    d.DriveType.ToString(),
                    string.Equals(d.RootDirectory.FullName, systemRoot, StringComparison.OrdinalIgnoreCase));
            }).ToList();
    }

    private async Task<List<DriveCard>> ProbeRemoteWindowsAsync(ServerProfile profile)
    {
        const string script = @"
Get-CimInstance Win32_LogicalDisk -Filter ""DriveType=3"" | ForEach-Object {
  $total=[int64]$_.Size; $free=[int64]$_.FreeSpace; $used=$total-$free
  Write-Output ('DRIVE|' + $_.DeviceID + '|' + $_.FileSystem + '|' + $total + '|' + $used + '|' + $free + '|' + $_.VolumeName)
}
";
        var output = await S.PowerShellRemote.ExecuteAsync(profile, script, 35);
        var rows = new List<DriveCard>();
        foreach (var line in output.Replace("\r", "").Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            var p = line.Split('|');
            if (p.Length < 7 || p[0] != "DRIVE") continue;
            _ = long.TryParse(p[3], out var total);
            _ = long.TryParse(p[4], out var used);
            _ = long.TryParse(p[5], out var free);
            rows.Add(BuildDrive(p[1], p[1] + "\\", p[2], total, used, free, "On demand", null, p[6], "Remote Windows", p[1].Equals("C:", StringComparison.OrdinalIgnoreCase)));
        }
        return rows;
    }

    private async Task<List<DriveCard>> ProbeRemoteLinuxAsync(ServerProfile profile, bool deep)
    {
        const string command =
            "df -B1 -P -T 2>/dev/null | tail -n +2 | awk '{print \"FS|\"$1\"|\"$2\"|\"$3\"|\"$4\"|\"$5\"|\"$6\"|\"$7}'";
        var result = await S.Ssh.ExecuteAsync(profile, command, 35);
        var rows = new List<DriveCard>();

        foreach (var line in result.StdOut.Replace("\r", "").Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            var p = line.Split('|');
            if (p.Length < 8 || p[0] != "FS") continue;
            _ = long.TryParse(p[3], out var total);
            _ = long.TryParse(p[4], out var used);
            _ = long.TryParse(p[5], out var free);
            var source = p[1];
            var fileSystem = p[2];
            var mount = p[7];
            if (!Services.LinuxStorageFilter.IsMeaningful(source, fileSystem, mount))
                continue;

            rows.Add(BuildDrive(
                source,
                mount,
                fileSystem,
                total,
                used,
                free,
                "On demand",
                null,
                source,
                source,
                mount == "/"));
        }

        if (deep)
        {
            try
            {
                var smart = await S.Ssh.ExecuteAsync(
                    profile,
                    "if command -v smartctl >/dev/null 2>&1; then smartctl --scan-open 2>/dev/null | awk '{print $1}' | head -n 16; fi",
                    25);
                if (!string.IsNullOrWhiteSpace(smart.StdOut))
                {
                    SmartSubtext.Text = "smartctl detected; use Raw SMART summary for per-device details";
                }
            }
            catch { }
        }

        return rows;
    }

    private static DriveCard BuildDrive(
        string name,
        string mount,
        string fs,
        long total,
        long used,
        long free,
        string smart,
        double? temperature,
        string model,
        string device,
        bool primary)
    {
        var pct = total > 0 ? Math.Clamp(used * 100d / total, 0, 100) : 0;
        var warn = pct >= 90;
        return new DriveCard
        {
            Name = string.IsNullOrWhiteSpace(name) ? mount : name,
            MountPoint = mount,
            FileSystem = string.IsNullOrWhiteSpace(fs) ? "--" : fs,
            UsagePercent = pct,
            UsageText = $"{pct:0}%",
            UsedText = FormatBytes(used),
            FreeText = FormatBytes(free),
            SmartText = smart,
            TemperatureC = temperature,
            TemperatureText = temperature is { } c ? $"{c:0.#} °C" : "--",
            Model = string.IsNullOrWhiteSpace(model) ? "--" : model,
            SerialDevice = string.IsNullOrWhiteSpace(device) ? "--" : device,
            StatusText = warn ? "ATTENTION" : "READY",
            StatusBrush = warn ? WarnBrush : SuccessBrush,
            StatusTint = warn ? WarnTint : SuccessTint,
            UsageBrush = warn ? WarnBrush : AccentBrush,
            SmartBrush = smart.Contains("fail", StringComparison.OrdinalIgnoreCase) ? DangerBrush : MutedBrush,
            IsPrimary = primary
        };
    }

    private async void Mounts_Click(object sender, RoutedEventArgs e)
    {
        if (Server is not { } profile) return;
        await ShowRawAsync(profile.ConnectionKind switch
        {
            HostConnectionKind.RemoteLinux => "findmnt 2>&1; echo; df -hT 2>&1; echo; lsblk -o NAME,SIZE,FSTYPE,UUID,MOUNTPOINTS,MODEL,SERIAL 2>&1",
            _ => ""
        }, "Storage details");
    }

    private async void Smart_Click(object sender, RoutedEventArgs e)
    {
        if (Server is not { } profile) return;

        if (profile.ConnectionKind == HostConnectionKind.RemoteLinux)
        {
            await ShowRawAsync(
                "if command -v smartctl >/dev/null 2>&1; then for d in $(smartctl --scan-open 2>/dev/null | awk '{print $1}' | head -n 16); do echo \"===== $d =====\"; smartctl -H -A \"$d\" 2>&1 | grep -E 'SMART overall-health|SMART Health Status|Temperature|Percentage Used|Power_On_Hours' || true; done; else echo 'smartctl is not installed'; fi",
                "SMART summary");
            return;
        }

        if (profile.ConnectionKind == HostConnectionKind.RemoteWindows)
        {
            try
            {
                OutputBox.Text = await S.PowerShellRemote.ExecuteAsync(
                    profile,
                    "Get-PhysicalDisk | Select-Object FriendlyName,SerialNumber,MediaType,HealthStatus,OperationalStatus,Size | Format-Table -AutoSize | Out-String | Write-Output",
                    35);
                DiagnosticsExpander.IsExpanded = true;
            }
            catch (Exception ex) { OutputBox.Text = ex.Message; }
            return;
        }

        if (profile.ConnectionKind == HostConnectionKind.LocalWindows)
        {
            OutputBox.Text = "Windows SMART/physical-disk detail is provider-specific. Use Storage Spaces / vendor tooling for hardware health if Windows does not expose it to the current account.";
            DiagnosticsExpander.IsExpanded = true;
        }
    }

    private void Recover_Click(object sender, RoutedEventArgs e)
    {
        OutputBox.Text = "No generic storage-repair command is configured. GraveOps intentionally avoids guessing at mount or filesystem repairs. Use provider-specific diagnostics, verify device identity, then perform recovery explicitly.";
        DiagnosticsExpander.IsExpanded = true;
    }

    private void StorageDrilldown_Click(object sender, RoutedEventArgs e)
    {
        var window = new OperationsDrillDownWindow(1);
        if (Window.GetWindow(this) is Window owner) window.Owner = owner;
        window.ShowDialog();
    }

    private async Task ShowRawAsync(string linuxCommand, string title)
    {
        if (Server is not { } profile) return;
        try
        {
            if (profile.ConnectionKind == HostConnectionKind.RemoteLinux)
                OutputBox.Text = await RunLinuxRawAsync(profile, linuxCommand);
            else if (profile.ConnectionKind == HostConnectionKind.RemoteWindows)
                OutputBox.Text = await S.PowerShellRemote.ExecuteAsync(
                    profile,
                    "Get-PSDrive -PSProvider FileSystem | Format-Table Name,Root,Used,Free -AutoSize | Out-String | Write-Output",
                    35);
            else
                OutputBox.Text = string.Join(Environment.NewLine, ProbeLocalWindows().Select(x => $"{x.Name} | {x.FileSystem} | {x.UsageText} used | {x.FreeText} free"));

            DiagnosticsExpander.IsExpanded = true;
            TelemetryStatus.Text = $"{title} loaded {DateTime.Now:HH:mm:ss}.";
        }
        catch (Exception ex)
        {
            OutputBox.Text = ex.Message;
            DiagnosticsExpander.IsExpanded = true;
        }
    }

    private async Task<string> RunLinuxRawAsync(ServerProfile profile, string command)
    {
        var result = await S.Ssh.ExecuteAsync(profile, command, 90);
        return result.Combined;
    }

    private static string FormatBytes(long bytes)
    {
        if (bytes <= 0) return "0 B";
        string[] units = ["B", "KiB", "MiB", "GiB", "TiB", "PiB"];
        var value = (double)bytes;
        var index = 0;
        while (value >= 1024 && index < units.Length - 1)
        {
            value /= 1024;
            index++;
        }
        return $"{value:0.#} {units[index]}";
    }

    private static readonly Brush SuccessBrush = new SolidColorBrush(Color.FromRgb(96, 190, 134));
    private static readonly Brush WarnBrush = new SolidColorBrush(Color.FromRgb(215, 178, 96));
    private static readonly Brush DangerBrush = new SolidColorBrush(Color.FromRgb(205, 96, 108));
    private static readonly Brush AccentBrush = new SolidColorBrush(Color.FromRgb(185, 139, 168));
    private static readonly Brush MutedBrush = new SolidColorBrush(Color.FromRgb(150, 154, 161));
    private static readonly Brush SuccessTint = new SolidColorBrush(Color.FromArgb(28, 96, 190, 134));
    private static readonly Brush WarnTint = new SolidColorBrush(Color.FromArgb(28, 215, 178, 96));

    private sealed class DriveCard
    {
        public string Name { get; init; } = "";
        public string MountPoint { get; init; } = "";
        public string FileSystem { get; init; } = "";
        public double UsagePercent { get; init; }
        public string UsageText { get; init; } = "--";
        public string UsedText { get; init; } = "--";
        public string FreeText { get; init; } = "--";
        public string SmartText { get; init; } = "--";
        public double? TemperatureC { get; init; }
        public string TemperatureText { get; init; } = "--";
        public string Model { get; init; } = "--";
        public string SerialDevice { get; init; } = "--";
        public string StatusText { get; init; } = "--";
        public Brush StatusBrush { get; init; } = MutedBrush;
        public Brush StatusTint { get; init; } = Brushes.Transparent;
        public Brush UsageBrush { get; init; } = AccentBrush;
        public Brush SmartBrush { get; init; } = MutedBrush;
        public bool IsPrimary { get; init; }
    }
}
