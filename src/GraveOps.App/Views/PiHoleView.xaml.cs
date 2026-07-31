using System.Diagnostics;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using GraveOps.App.Models;
using MessageBox = GraveOps.App.Windows.GraveOpsMessageBox;

namespace GraveOps.App.Views;

public partial class PiHoleView : UserControl
{
    private Services.AppServices S => App.Services;
    private ServerProfile? Server => S.Config.Current.Servers.FirstOrDefault(x => x.Role.Contains("Pi-hole", StringComparison.OrdinalIgnoreCase)) ?? S.Context.Current;

    public PiHoleView()
    {
        InitializeComponent();
        Loaded += PiHoleLive_Loaded;
        Unloaded += PiHoleLive_Unloaded;
        Loaded += async (_, _) => await RefreshAsync();
    }

    private async void Refresh_Click(object sender, RoutedEventArgs e) => await RefreshAsync();

    private async Task RefreshAsync()
    {
        if (Server is not { } p) { OutputBox.Text = "No Pi-hole server profile found."; return; }
        RefreshButton.IsEnabled = false;
        try
        {
            var cmd = "echo '__STATUS__'; pihole status 2>&1 || true; echo '__VERSION__'; pihole -v 2>&1 || true; echo '__HOST__'; hostname; uptime -p; awk '{print $1}' /proc/loadavg; if [ -r /sys/class/thermal/thermal_zone0/temp ]; then awk '{printf \"%.1f\\n\", $1/1000}' /sys/class/thermal/thermal_zone0/temp; else echo --; fi; echo '__STATS__'; timeout 5 pihole api stats/summary 2>/dev/null || true";
            var r = await S.Ssh.ExecuteAsync(p, cmd, 30);
            Parse(r.Combined);
            OutputBox.Text = r.Combined;
        }
        catch (Exception ex) { OutputBox.Text = ex.ToString(); }
        finally { RefreshButton.IsEnabled = true; }
    }

    private void Parse(string text)
    {
        var status = Slice(text, "__STATUS__", "__VERSION__");
        var versions = Slice(text, "__VERSION__", "__HOST__");
        var host = Slice(text, "__HOST__", "__STATS__").Replace("\r", "").Split('\n', StringSplitOptions.RemoveEmptyEntries);
        var stats = text.Contains("__STATS__", StringComparison.Ordinal) ? text[(text.IndexOf("__STATS__", StringComparison.Ordinal) + 9)..].Trim() : "";

        var dns = status.Contains("FTL is listening on port 53", StringComparison.OrdinalIgnoreCase);
        var blocking = status.Contains("blocking is enabled", StringComparison.OrdinalIgnoreCase);
        DnsStateText.Text = dns ? "ONLINE" : "OFFLINE";
        DnsStateText.Foreground = Brush(dns ? "Success" : "Danger");
        DnsSubtext.Text = dns ? "FTL listening on port 53" : "DNS service needs attention";
        BlockingText.Text = blocking ? "ENABLED" : "DISABLED";
        BlockingText.Foreground = Brush(blocking ? "Success" : "Warn");

        CoreVersionText.Text = Version(versions, "Core version is");
        WebVersionText.Text = Version(versions, "Web version is");
        FtlVersionText.Text = Version(versions, "FTL version is");
        HostText.Text = host.ElementAtOrDefault(0) ?? "--";
        UptimeText.Text = (host.ElementAtOrDefault(1) ?? "--").Replace("up ", "", StringComparison.OrdinalIgnoreCase);
        LoadText.Text = host.ElementAtOrDefault(2) ?? "--";
        PiTempText.Text = double.TryParse(host.ElementAtOrDefault(3), out var t) ? $"{t:0.0} C" : "--";

        QueriesText.Text = "--";
        BlockedText.Text = "--";
        QueriesSubtext.Text = "API stats unavailable";
        BlockedSubtext.Text = "Rolling 24-hour window";
        ActiveClientsText.Text = "--";
        TotalClientsText.Text = "-- total known";
        QueryRateText.Text = "--";
        GravityDomainsText.Text = "--";
        GravityUpdatedText.Text = "--";
        try
        {
            var jsonStart = stats.IndexOf('{');
            if (jsonStart >= 0)
            {
                using var doc = JsonDocument.Parse(stats[jsonStart..]);
                var root = doc.RootElement;

                long total = 0, blocked = 0, clientsActive = 0, clientsTotal = 0, gravityDomains = 0, gravityUpdated = 0;
                double percent = 0, frequency = 0;

                if (root.TryGetProperty("queries", out var q))
                {
                    if (q.TryGetProperty("total", out var qt) && qt.TryGetInt64(out var x)) total = x;
                    if (q.TryGetProperty("blocked", out var qb) && qb.TryGetInt64(out var y)) blocked = y;
                    if (q.TryGetProperty("percent_blocked", out var qp) && qp.TryGetDouble(out var z)) percent = z;
                    if (q.TryGetProperty("frequency", out var qf) && qf.TryGetDouble(out var f)) frequency = f;
                }
                else
                {
                    if (root.TryGetProperty("total_queries", out var tq) && tq.TryGetInt64(out var x)) total = x;
                    if (root.TryGetProperty("blocked_queries", out var bq) && bq.TryGetInt64(out var y)) blocked = y;
                    if (root.TryGetProperty("percent_blocked", out var pb) && pb.TryGetDouble(out var z)) percent = z;
                }

                if (root.TryGetProperty("clients", out var clients))
                {
                    if (clients.TryGetProperty("active", out var ca) && ca.TryGetInt64(out var x)) clientsActive = x;
                    if (clients.TryGetProperty("total", out var ct) && ct.TryGetInt64(out var y)) clientsTotal = y;
                }

                if (root.TryGetProperty("gravity", out var gravity))
                {
                    if (gravity.TryGetProperty("domains_being_blocked", out var gd) && gd.TryGetInt64(out var x)) gravityDomains = x;
                    if (gravity.TryGetProperty("last_update", out var gu) && gu.TryGetInt64(out var y)) gravityUpdated = y;
                }

                if (total > 0 || blocked > 0)
                {
                    QueriesText.Text = total.ToString("N0");
                    BlockedText.Text = blocked.ToString("N0");
                    QueriesSubtext.Text = "Rolling 24-hour total";
                    BlockedSubtext.Text = $"{percent:0.0}% of queries";
                }

                ActiveClientsText.Text = clientsActive > 0 ? clientsActive.ToString("N0") : "0";
                TotalClientsText.Text = clientsTotal > 0 ? $"{clientsTotal:N0} total known" : "No client total";
                QueryRateText.Text = frequency > 0 ? $"{frequency:0.0} q/s" : "--";
                GravityDomainsText.Text = gravityDomains > 0 ? gravityDomains.ToString("N0") : "--";

                if (gravityUpdated > 0)
                {
                    var updated = DateTimeOffset.FromUnixTimeSeconds(gravityUpdated).ToLocalTime();
                    var age = DateTimeOffset.Now - updated;
                    GravityUpdatedText.Text = age.TotalDays >= 1
                        ? $"{age.TotalDays:0.#}d ago"
                        : age.TotalHours >= 1
                            ? $"{age.TotalHours:0.#}h ago"
                            : $"{Math.Max(0, age.TotalMinutes):0}m ago";
                    GravityUpdatedText.ToolTip = updated.ToString("f");
                }
            }
        }
        catch { }
    }

    private static string Slice(string text, string start, string end)
    {
        var a = text.IndexOf(start, StringComparison.Ordinal);
        if (a < 0) return "";
        a += start.Length;
        var b = text.IndexOf(end, a, StringComparison.Ordinal);
        return b < 0 ? text[a..] : text[a..b];
    }
    private static string Version(string text, string prefix)
    {
        var m = Regex.Match(text, Regex.Escape(prefix) + @"\s+([^\s]+)", RegexOptions.IgnoreCase);
        return m.Success ? m.Groups[1].Value : "--";
    }
    private static Brush Brush(string key) => (Brush)Application.Current.FindResource(key);

    private async Task RunControl(string command, string activity)
    {
        if (S.Config.Current.Settings.SafeMode)
        {
            MessageBox.Show("Safe Mode blocks Pi-hole control changes.", "GraveOps Safe Mode");
            return;
        }
        if (Server is not { } p) return;
        var sw = Stopwatch.StartNew();
        try
        {
            var r = await S.Ssh.ExecuteAsync(p, command, 90);
            S.Activity.Record(activity, r.Success ? "Completed successfully." : r.Combined, r.Success ? ActivityLevel.Success : ActivityLevel.Error, sw.Elapsed.TotalSeconds, p.Id, "page:Pi-hole");
            OutputBox.Text = r.Combined;
            await RefreshAsync();
        }
        catch (Exception ex)
        {
            S.Activity.Record(activity, ex.Message, ActivityLevel.Error, sw.Elapsed.TotalSeconds, p.Id, "page:Pi-hole");
            OutputBox.Text = ex.ToString();
        }
    }
    private async void Enable_Click(object sender, RoutedEventArgs e) => await RunControl("sudo -n pihole enable 2>&1", "Pi-hole blocking enabled");
    private async void Disable_Click(object sender, RoutedEventArgs e) { if (MessageBox.Show("Disable Pi-hole blocking for 5 minutes?", "Pi-hole", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes) await RunControl("sudo -n pihole disable 5m 2>&1", "Pi-hole blocking disabled for 5 minutes"); }
    private async void RestartDns_Click(object sender, RoutedEventArgs e) { if (MessageBox.Show("Reload Pi-hole DNS?", "Pi-hole", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes) await RunControl("sudo -n pihole reloaddns 2>&1", "Pi-hole DNS reloaded"); }
    private async void Version_Click(object sender, RoutedEventArgs e) { if (Server is { } p) OutputBox.Text = (await S.Ssh.ExecuteAsync(p, "pihole -v 2>&1", 30)).Combined; }
    private void OpenPiHole_Click(object sender, RoutedEventArgs e)
    {
        if (Server is not { } p) return;
        var url = $"http://{p.Host}/admin";
        var w = new EmbeddedBrowserWindow("Pi-hole", url) { Owner = Window.GetWindow(this) };
        w.Show();
        S.Activity.Record("Opened Pi-hole", url, ActivityLevel.Info, serverId: p.Id, deepLink: "page:Pi-hole");
    }

    private void PiHoleLive_Loaded(
        object sender,
        RoutedEventArgs e)
    {
        var live =
            Services.LiveAnalyticsHub.Current;

        live.Updated -= PiHoleLive_Updated;
        live.Updated += PiHoleLive_Updated;
        live.SetActivePage("Pi-hole");

        if (live.PiHoleSnapshot is { } snapshot)
            ApplyLivePiHole(snapshot, null);
    }

    private void PiHoleLive_Unloaded(
        object sender,
        RoutedEventArgs e)
    {
        var live =
            Services.LiveAnalyticsHub.Current;

        live.Updated -= PiHoleLive_Updated;
        live.DeactivatePage("Pi-hole");
    }

    private void PiHoleLive_Updated(
        object? sender,
        Services.LiveAnalyticsUpdateEventArgs e)
    {
        if (!IsLoaded ||
            e.Domain !=
                Services.LiveAnalyticsDomain.PiHole)
            return;

        var live =
            Services.LiveAnalyticsHub.Current;

        if (live.PiHoleSnapshot is { } snapshot)
            ApplyLivePiHole(
                snapshot,
                e);

        if (!e.Success)
        {
            DnsSubtext.Text =
                e.BadgeText;

            QueriesSubtext.Text =
                e.BadgeText;
        }
    }

    private void ApplyLivePiHole(
        Services.PiHoleLiveSnapshot snapshot,
        Services.LiveAnalyticsUpdateEventArgs? update)
    {
        DnsStateText.Text =
            snapshot.DnsOnline
                ? "ONLINE"
                : "OFFLINE";

        DnsStateText.Foreground =
            Brush(
                snapshot.DnsOnline
                    ? "Success"
                    : "Danger");

        BlockingText.Text =
            snapshot.BlockingEnabled
                ? "ENABLED"
                : "DISABLED";

        BlockingText.Foreground =
            Brush(
                snapshot.BlockingEnabled
                    ? "Success"
                    : "Warn");

        HostText.Text =
            snapshot.Host;

        UptimeText.Text =
            snapshot.Uptime;

        LoadText.Text =
            snapshot.Load;

        PiTempText.Text =
            snapshot.TemperatureC is { } temp
                ? $"{temp:0.0} C"
                : "--";

        if (snapshot.StatsAvailable)
        {
            QueriesText.Text =
                snapshot.Queries.ToString("N0");

            BlockedText.Text =
                snapshot.Blocked.ToString("N0");

            BlockedSubtext.Text =
                $"{snapshot.PercentBlocked:0.0}% of queries";
        }

        var liveText =
            update?.BadgeText ??
            $"LIVE - updated {snapshot.SampledAt.ToLocalTime():HH:mm:ss}";

        DnsSubtext.Text =
            snapshot.DnsOnline
                ? $"FTL listening on port 53 - {liveText}"
                : $"DNS service needs attention - {liveText}";

        QueriesSubtext.Text =
            snapshot.StatsAvailable
                ? $"Rolling 24-hour total - {liveText}"
                : $"API stats unavailable - {liveText}";
    }
}