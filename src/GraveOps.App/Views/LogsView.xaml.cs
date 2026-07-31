using System.Diagnostics;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using GraveOps.App.Models;
using Microsoft.Win32;

namespace GraveOps.App.Views;

public partial class LogsView : UserControl
{
    private static readonly Regex AnsiRegex = new(@"\x1B\[[0-?]*[ -/]*[@-~]", RegexOptions.Compiled);
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(5) };
    private string _loaded = "";
    private bool _loading;
    private Services.AppServices S => App.Services;
    private ServerProfile? Server => S.Context.Current;

    public LogsView()
    {
        InitializeComponent();
        SeverityCombo.ItemsSource = new[] { "All", "Errors", "Warnings", "Info" };
        SeverityCombo.SelectedIndex = 0;
        RebuildSources();
        _timer.Tick += async (_, _) => await LoadAsync(silent: true);
        S.Context.TargetChanged += Context_TargetChanged;
        Unloaded += (_, _) => { _timer.Stop(); S.Context.TargetChanged -= Context_TargetChanged; };
    }

    private void Context_TargetChanged(ServerProfile? p) => Dispatcher.Invoke(() =>
    {
        TargetText.Text = p?.Name ?? "No target";
        RebuildSources();
    });

    private void RebuildSources()
    {
        TargetText.Text = Server?.Name ?? "No target";
        var prior = SourceCombo.SelectedItem as string;
        SourceCombo.ItemsSource = SourceNames(Server).ToArray();
        SourceCombo.SelectedItem = SourceCombo.Items.Contains(prior) ? prior : SourceCombo.Items.Cast<object>().FirstOrDefault();
    }

    private static IEnumerable<string> SourceNames(ServerProfile? server)
    {
        if (server?.ConnectionKind == HostConnectionKind.RemoteLinux)
            return new[] { "Plex", "Docker service", "Docker containers", "SABnzbd", "System warnings" };
        return new[] { "System warnings", "Application events", "Services needing attention", "Docker inventory", "Plex events" };
    }

    private static string LinuxCommand(string key) => key switch
    {
        "Plex" => "journalctl -u plexmediaserver -n 350 --no-pager 2>&1",
        "Docker service" => "journalctl -u docker -n 350 --no-pager 2>&1",
        "Docker containers" => "docker ps --format '{{.Names}}' 2>/dev/null | head -n 25 | while read n; do echo \"===== $n =====\"; docker logs --tail 35 \"$n\" 2>&1 | tail -n 35; done",
        "SABnzbd" => "journalctl -u sabnzbdplus -n 350 --no-pager 2>&1",
        _ => "journalctl -p warning -n 350 --no-pager 2>&1"
    };

    private static string WindowsScript(string key) => key switch
    {
        "Application events" => "$e=Get-WinEvent -LogName Application -MaxEvents 350 -ErrorAction SilentlyContinue; $e | Select-Object TimeCreated,LevelDisplayName,ProviderName,Id,Message | Format-List | Out-String -Width 240",
        "Services needing attention" => "Get-CimInstance Win32_Service -ErrorAction SilentlyContinue | Where-Object { $_.StartMode -eq 'Auto' -and $_.State -ne 'Running' } | Select-Object Name,DisplayName,State,StartMode,ExitCode | Format-Table -AutoSize | Out-String -Width 240",
        "Docker inventory" => "if(Get-Command docker -ErrorAction SilentlyContinue){ docker ps -a --format 'table {{.Names}}\t{{.Image}}\t{{.Status}}\t{{.Ports}}' 2>&1 | Out-String -Width 240 } else { 'Docker CLI is not installed on this host.' }",
        "Plex events" => "$svc=Get-Service -Name '*Plex*' -ErrorAction SilentlyContinue; $svc | Format-Table Name,Status,StartType -AutoSize | Out-String -Width 240; Get-WinEvent -LogName Application -MaxEvents 500 -ErrorAction SilentlyContinue | Where-Object { $_.ProviderName -match 'Plex' -or $_.Message -match 'Plex' } | Select-Object -First 150 TimeCreated,LevelDisplayName,ProviderName,Id,Message | Format-List | Out-String -Width 240",
        _ => "Get-WinEvent -FilterHashtable @{LogName='System';Level=1,2,3} -MaxEvents 350 -ErrorAction SilentlyContinue | Select-Object TimeCreated,LevelDisplayName,ProviderName,Id,Message | Format-List | Out-String -Width 240"
    };

    private void SourceCombo_SelectionChanged(object sender, SelectionChangedEventArgs e) { }
    private async void Load_Click(object sender, RoutedEventArgs e) => await LoadAsync(false);

    private async Task LoadAsync(bool silent)
    {
        if (_loading || Server is not { } p || SourceCombo.SelectedItem is not string key) return;
        _loading = true;
        if (!silent) StatusText.Text = "Loading...";
        try
        {
            string text;
            if (p.ConnectionKind == HostConnectionKind.RemoteLinux)
            {
                var r = await S.Ssh.ExecuteAsync(p, LinuxCommand(key), 90);
                text = r.Combined;
                StatusText.Text = $"{DateTime.Now:T} | exit {r.ExitCode}";
            }
            else if (p.ConnectionKind == HostConnectionKind.RemoteWindows)
            {
                text = await S.PowerShellRemote.ExecuteAsync(p, WindowsScript(key), 90);
                StatusText.Text = $"{DateTime.Now:T} | remote Windows";
            }
            else if (p.ConnectionKind == HostConnectionKind.LocalWindows)
            {
                text = await RunLocalPowerShellAsync(WindowsScript(key), 90);
                StatusText.Text = $"{DateTime.Now:T} | local Windows";
            }
            else
            {
                text = "This host provider does not expose a Log Center transport yet.";
                StatusText.Text = "Provider unavailable";
            }
            _loaded = StripAnsi(text);
            ApplyFilter();
            AnalyzeRepeats();
        }
        catch (Exception ex)
        {
            LogsEmptyStateText.Visibility = Visibility.Collapsed;
            OutputBox.Text = ex.ToString();
            StatusText.Text = "Error";
        }
        finally { _loading = false; }
    }

    private static async Task<string> RunLocalPowerShellAsync(string script, int timeoutSeconds)
    {
        var psi = new ProcessStartInfo
        {
            FileName = "powershell.exe",
            Arguments = "-NoProfile -NonInteractive -ExecutionPolicy Bypass -Command -",
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };
        using var process = Process.Start(psi) ?? throw new InvalidOperationException("Unable to start PowerShell.");
        await process.StandardInput.WriteAsync(script);
        process.StandardInput.Close();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(Math.Clamp(timeoutSeconds, 5, 180)));
        var stdout = process.StandardOutput.ReadToEndAsync(timeout.Token);
        var stderr = process.StandardError.ReadToEndAsync(timeout.Token);
        await process.WaitForExitAsync(timeout.Token);
        var text = string.Join(Environment.NewLine, new[] { await stdout, await stderr }.Where(x => !string.IsNullOrWhiteSpace(x))).Trim();
        if (process.ExitCode != 0) throw new InvalidOperationException(text.Length == 0 ? $"PowerShell exited with code {process.ExitCode}." : text);
        return text;
    }

    private void SearchBox_TextChanged(object sender, TextChangedEventArgs e) => ApplyFilter();
    private void FilterChanged(object sender, SelectionChangedEventArgs e) { if (IsLoaded) ApplyFilter(); }
    private void ApplyFilter()
    {
        var q = SearchBox.Text?.Trim() ?? ""; var severity = SeverityCombo.SelectedItem as string ?? "All";
        IEnumerable<string> lines = _loaded.Replace("\r", "").Split('\n');
        if (!string.IsNullOrEmpty(q)) lines = lines.Where(x => x.Contains(q, StringComparison.OrdinalIgnoreCase));
        lines = severity switch { "Errors" => lines.Where(IsError), "Warnings" => lines.Where(IsWarning), "Info" => lines.Where(x => !IsError(x) && !IsWarning(x)), _ => lines };
        var result = string.Join(Environment.NewLine, lines);
        LogsEmptyStateText.Visibility = string.IsNullOrWhiteSpace(result) ? Visibility.Visible : Visibility.Collapsed;
        LogsEmptyStateText.Text = string.IsNullOrWhiteSpace(_loaded) ? "Select a log source and press Load." : "No loaded lines match the current filters.";
        OutputBox.Text = result;
        OutputBox.ScrollToEnd();
    }
    private void AnalyzeRepeats()
    {
        var groups = _loaded.Replace("\r", "").Split('\n', StringSplitOptions.RemoveEmptyEntries).Select(Normalize).Where(x => x.Length > 12)
            .GroupBy(x => x, StringComparer.OrdinalIgnoreCase).Select(g => new { Text = g.Key, Count = g.Count() }).Where(x => x.Count >= 3).OrderByDescending(x => x.Count).Take(3).ToList();
        RepeatSummaryText.Text = groups.Count == 0 ? "No message repeated 3+ times in the loaded window." : "Repeated: " + string.Join(" | ", groups.Select(x => $"{x.Count}x {Trim(x.Text, 90)}"));
    }
    private static string StripAnsi(string text) { if (string.IsNullOrEmpty(text)) return text; return AnsiRegex.Replace(text, "").Replace("\0", ""); }
    private static string Normalize(string line) { line = Regex.Replace(line, @"^\s*(?:[A-Z][a-z]{2}\s+\d+\s+\d\d:\d\d:\d\d\s+\S+\s+)?", ""); line = Regex.Replace(line, @"\b\d{3,}\b", "#"); return line.Trim(); }
    private static bool IsError(string x) => x.Contains("error", StringComparison.OrdinalIgnoreCase) || x.Contains("failed", StringComparison.OrdinalIgnoreCase) || x.Contains("fatal", StringComparison.OrdinalIgnoreCase) || x.Contains("exception", StringComparison.OrdinalIgnoreCase);
    private static bool IsWarning(string x) => x.Contains("warn", StringComparison.OrdinalIgnoreCase) || x.Contains("degraded", StringComparison.OrdinalIgnoreCase);
    private static string Trim(string s, int n) => s.Length <= n ? s : s[..n] + "...";
    private void FollowCheck_Changed(object sender, RoutedEventArgs e) { if (FollowCheck.IsChecked == true) _timer.Start(); else _timer.Stop(); }
    private void Copy_Click(object sender, RoutedEventArgs e) { if (!string.IsNullOrEmpty(OutputBox.Text)) Clipboard.SetText(OutputBox.Text); }
    private void Export_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new SaveFileDialog { FileName = $"GraveOps-{SourceCombo.SelectedItem}-logs-{DateTime.Now:yyyyMMdd-HHmmss}.txt", Filter = "Text files|*.txt|All files|*.*" };
        if (dlg.ShowDialog() == true) File.WriteAllText(dlg.FileName, OutputBox.Text, new UTF8Encoding(false));
    }
}
