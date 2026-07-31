using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using GraveOps.App.Models;
using Microsoft.Win32;

using MessageBox = GraveOps.App.Windows.GraveOpsMessageBox;

namespace GraveOps.App.Views;

public partial class ActionsView : UserControl
{
    private Services.AppServices S => App.Services;
    private ServerProfile? Server => S.Context.Current;
    private List<QuickAction> _all = new();
    private IncidentReport? _lastIncident;

    public ActionsView()
    {
        InitializeComponent();
        ReloadActions();
        UpdateTarget();
        OutputBox.Text = "Select an action from the library to review its command, risk and result.";
        CompareBox.Text = "Capture state before or after an operation to compare changes here.";
        S.Context.TargetChanged += Context_TargetChanged;
        Unloaded += (_, _) => S.Context.TargetChanged -= Context_TargetChanged;
    }

    private void Context_TargetChanged(ServerProfile? _) => Dispatcher.Invoke(() => { UpdateTarget(); ReloadActions(); });
    private void UpdateTarget()
    {
        var name = Server?.Name ?? "No target";
        TargetText.Text = name;
        IncidentTargetText.Text = name;
    }
    private void ReloadActions()
    {
        var saved = S.Config.Current.Actions
            .Where(x => x.ServerId is null || x.ServerId == Server?.Id)
            .ToList();
        var builtIn = BuildProviderActions(Server).ToList();
        _all = builtIn
            .Concat(saved)
            .GroupBy(x => x.Name, StringComparer.OrdinalIgnoreCase)
            .Select(x => x.First())
            .OrderBy(x => x.Category)
            .ThenBy(x => x.Name)
            .ToList();

        var selected = CategoryCombo.SelectedItem as string ?? "All categories";
        var cats = new List<string> { "All categories" };
        cats.AddRange(_all.Select(x => x.Category).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(x => x));
        CategoryCombo.ItemsSource = cats;
        CategoryCombo.SelectedItem = cats.Contains(selected, StringComparer.OrdinalIgnoreCase) ? cats.First(x => x.Equals(selected, StringComparison.OrdinalIgnoreCase)) : "All categories";
        BindActions();
    }

    private static IEnumerable<QuickAction> BuildProviderActions(ServerProfile? server)
    {
        if (server is null) yield break;
        QuickAction ReadOnly(string name, string category, string command, string description) => new()
        {
            Name = name,
            Category = category,
            Command = command,
            Description = description,
            Risk = ActionRisk.ReadOnly,
            ServerId = server.Id
        };

        if (server.ConnectionKind == HostConnectionKind.RemoteLinux)
        {
            yield return ReadOnly("Host summary", "Host", "hostnamectl 2>/dev/null || hostname; echo; uptime; echo; uname -a", "Provider-safe Linux host identity and uptime.");
            yield return ReadOnly("Failed services", "Host", "systemctl --failed --no-pager 2>&1 || true", "Systemd units currently in a failed state.");
            yield return ReadOnly("Storage overview", "Storage", "df -hT; echo; findmnt -o TARGET,SOURCE,FSTYPE,OPTIONS | head -n 120", "Mounted filesystems and capacity without assuming mount names.");
            yield return ReadOnly("Docker overview", "Docker", "docker ps -a --format 'table {{.Names}}\t{{.Image}}\t{{.Status}}\t{{.Ports}}' 2>&1 || true", "Container inventory when Docker is available.");
            yield return ReadOnly("Recent host warnings", "Diagnostics", "journalctl -p warning -n 120 --no-pager 2>&1 || true", "Recent warning-or-higher journal entries.");
            yield return ReadOnly("Network snapshot", "Network", "ip -brief address 2>&1; echo; ip route 2>&1", "Host addresses and routing table.");
            yield break;
        }

        var hostSummary = "$os=Get-CimInstance Win32_OperatingSystem; [pscustomobject]@{Host=$env:COMPUTERNAME;OS=$os.Caption;Version=$os.Version;LastBoot=$os.LastBootUpTime;MemoryGB=[math]::Round($os.TotalVisibleMemorySize/1MB,1)} | Format-List | Out-String";
        var services = "Get-Service | Where-Object {$_.StartType -eq 'Automatic' -and $_.Status -ne 'Running'} | Select-Object Status,Name,DisplayName | Format-Table -AutoSize | Out-String";
        var storage = "Get-CimInstance Win32_LogicalDisk -Filter 'DriveType=3' | Select-Object DeviceID,VolumeName,FileSystem,@{N='SizeGB';E={[math]::Round($_.Size/1GB,1)}},@{N='FreeGB';E={[math]::Round($_.FreeSpace/1GB,1)}} | Format-Table -AutoSize | Out-String";
        var docker = "if (Get-Command docker -ErrorAction SilentlyContinue) { docker ps -a } else { 'Docker CLI not detected.' }";
        var events = "Get-WinEvent -FilterHashtable @{LogName='System';Level=2,3} -MaxEvents 80 -ErrorAction SilentlyContinue | Select-Object TimeCreated,Id,ProviderName,LevelDisplayName,Message | Format-List | Out-String";
        var network = "Get-NetIPConfiguration | Select-Object InterfaceAlias,IPv4Address,IPv4DefaultGateway,DNSServer | Format-List | Out-String";
        yield return ReadOnly("Host summary", "Host", hostSummary, "Windows host identity, memory and boot state.");
        yield return ReadOnly("Automatic services needing attention", "Host", services, "Automatic Windows services that are not running.");
        yield return ReadOnly("Storage overview", "Storage", storage, "Local fixed-volume capacity inventory.");
        yield return ReadOnly("Docker overview", "Docker", docker, "Container inventory when Docker is available.");
        yield return ReadOnly("Recent host warnings", "Diagnostics", events, "Recent Windows System error and warning events.");
        yield return ReadOnly("Network snapshot", "Network", network, "Windows IP, gateway and DNS configuration.");
    }

    private void BindActions()
    {
        var cat = CategoryCombo.SelectedItem as string;
        ActionList.ItemsSource = string.IsNullOrWhiteSpace(cat) || cat == "All categories" ? _all : _all.Where(x => x.Category == cat).ToList();
    }
    private void CategoryCombo_SelectionChanged(object sender, SelectionChangedEventArgs e) { if (IsLoaded) BindActions(); }

    private void ActionList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ActionList.SelectedItem is not QuickAction a) return;
        ActionName.Text = a.Name;
        ActionDescription.Text = string.IsNullOrWhiteSpace(a.Description) ? $"{a.Category} | {a.RiskLabel}" : a.Description;
        CommandText.Text = a.Command;
        RiskText.Text = a.RiskLabel;
        var key = a.Risk == ActionRisk.Dangerous ? "Danger" : a.Risk == ActionRisk.Normal ? "Warn" : "Success";
        RiskText.Foreground = Brush(key); RiskBadge.Background = Brush(key + "Bg");
        RunButton.Background = Brush(a.Risk == ActionRisk.Dangerous ? "Danger" : a.Risk == ActionRisk.Normal ? "Accent" : "Surface3");
    }

    private async void Run_Click(object sender, RoutedEventArgs e)
    {
        if (ActionList.SelectedItem is not QuickAction a || Server is not { } selected) return;
        var server = a.ServerId is { } id ? S.Config.Current.Servers.FirstOrDefault(x => x.Id == id) ?? selected : selected;
        if (a.Risk == ActionRisk.Dangerous)
        {
            var phrase = a.Name.Contains("reboot", StringComparison.OrdinalIgnoreCase) ? "REBOOT" : "RUN";
            var dialog = new ConfirmDangerWindow(a.Name, a.Command, phrase) { Owner = Window.GetWindow(this) };
            if (dialog.ShowDialog() != true) return;
        }
        else if (a.Risk == ActionRisk.Normal && S.Config.Current.Settings.ConfirmNormalActions && MessageBox.Show($"Run '{a.Name}' on {server.Name}?", "Confirm action", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;

        RunButton.IsEnabled = false; StatusText.Text = "Capturing pre-state..."; OutputBox.Text = "RUNNING\n"; CompareBox.Text = "Capturing state before action...";
        SystemStateSnapshot? before = null;
        try
        {
            before = await S.Incident.CaptureStateAsync(server);
            StatusText.Text = "Running action...";
            var r = await S.ActionRunner.RunAsync(a, server);
            StatusText.Text = "Verifying post-state...";
            var after = await S.Incident.CaptureStateAsync(server);
            CompareBox.Text = Services.IncidentService.Compare(before, after);
            var sb = new StringBuilder();
            sb.AppendLine(r.Success ? "SUCCESS" : "FAILED");
            sb.AppendLine(); sb.AppendLine($"Action: {a.Name}"); sb.AppendLine($"Elapsed: {r.Duration.TotalSeconds:0.0}s");
            if (!string.IsNullOrWhiteSpace(r.Output)) { sb.AppendLine(); sb.AppendLine("COMMAND OUTPUT"); sb.AppendLine(r.Output); }
            if (!string.IsNullOrWhiteSpace(r.Error)) { sb.AppendLine(); sb.AppendLine("ERROR OUTPUT"); sb.AppendLine(r.Error); }
            if (!string.IsNullOrWhiteSpace(r.Verification)) { sb.AppendLine(); sb.AppendLine("POST-ACTION VERIFICATION"); sb.AppendLine(r.Verification); }
            OutputBox.Text = sb.ToString().TrimEnd();
            StatusText.Text = r.Success ? $"Success in {r.Duration.TotalSeconds:0.0}s" : "Failed - review output";
        }
        catch (Exception ex) { OutputBox.Text = ex.ToString(); StatusText.Text = "Error"; }
        finally { RunButton.IsEnabled = true; }
    }

    private async void Snapshot_Click(object sender, RoutedEventArgs e)
    {
        if (Server is not { } p) return;
        StatusText.Text = "Capturing state...";
        try { var s = await S.Incident.CaptureStateAsync(p); CompareBox.Text = string.Join(Environment.NewLine, s.Lines()); StatusText.Text = "State captured"; }
        catch (Exception ex) { CompareBox.Text = ex.ToString(); StatusText.Text = "Error"; }
    }

    private async void Analyze_Click(object sender, RoutedEventArgs e)
    {
        if (Server is not { } p) { IncidentOutputBox.Text = "Select a global server target first."; return; }
        AnalyzeButton.IsEnabled = false; IncidentSeverityText.Text = "ANALYZING"; IncidentOutputBox.Text = "Collecting dependency state...";
        try
        {
            _lastIncident = await S.Incident.AnalyzeAsync(p);
            RenderIncident(_lastIncident);
            S.Activity.Record("What's Wrong analysis", _lastIncident.Headline, _lastIncident.Severity is "HEALTHY" ? ActivityLevel.Success : _lastIncident.Severity is "WARNING" ? ActivityLevel.Warning : ActivityLevel.Error, serverId: p.Id, deepLink: "page:Services");
        }
        catch (Exception ex) { IncidentSeverityText.Text = "ERROR"; IncidentSeverityText.Foreground = Brush("Danger"); IncidentOutputBox.Text = ex.ToString(); }
        finally { AnalyzeButton.IsEnabled = true; }
    }

    private void RenderIncident(IncidentReport r)
    {
        IncidentSeverityText.Text = r.Severity;
        IncidentSeverityText.Foreground = Brush(r.Severity == "HEALTHY" ? "Success" : r.Severity == "WARNING" ? "Warn" : "Danger");
        RootCauseText.Text = r.RootCause; IncidentHeadlineText.Text = r.Headline; IncidentRawBox.Text = r.Raw;
        var sb = new StringBuilder();
        foreach (var x in r.Findings) sb.AppendLine("- " + x);
        if (r.Recommendations.Count > 0) { sb.AppendLine(); sb.AppendLine("RECOMMENDATIONS"); foreach (var x in r.Recommendations) sb.AppendLine("- " + x); }
        IncidentOutputBox.Text = sb.ToString().TrimEnd();
    }

    private async void ExportIncident_Click(object sender, RoutedEventArgs e)
    {
        if (Server is not { } p) return;
        var dlg = new SaveFileDialog { FileName = $"GraveOps-incident-{p.Name.Replace(' ', '-')}-{DateTime.Now:yyyyMMdd-HHmmss}.txt", Filter = "Text files|*.txt|All files|*.*" };
        if (dlg.ShowDialog() != true) return;
        try
        {
            IncidentOutputBox.Text = "Building diagnostic bundle...";
            var bundle = await S.Incident.BuildDiagnosticBundleAsync(p);
            File.WriteAllText(dlg.FileName, bundle, new UTF8Encoding(false));
            IncidentOutputBox.Text = $"Diagnostic bundle exported:\n{dlg.FileName}";
            S.Activity.Record("Diagnostic bundle exported", dlg.FileName, ActivityLevel.Success, serverId: p.Id, deepLink: "page:Services");
        }
        catch (Exception ex) { IncidentOutputBox.Text = ex.ToString(); }
    }

    private static Brush Brush(string key) => (Brush)Application.Current.FindResource(key);
}