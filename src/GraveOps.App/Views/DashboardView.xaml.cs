using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using GraveOps.App.Models;
using GraveOps.App.Services;
using Microsoft.Win32;

namespace GraveOps.App.Views;

public partial class DashboardView : UserControl
{
    private AppServices S => App.Services;
    private LiveAnalyticsService Live => LiveAnalyticsHub.Current;

    private bool _refreshingHost;
    private bool _refreshingEnvironment;
    private EnvironmentOverviewSnapshot? _lastEnvironmentSnapshot;

    private ServerProfile? Selected =>
        S.Context.Current ?? S.Config.GetSelectedServer();

    public DashboardView()
    {
        InitializeComponent();
        Loaded += DashboardView_Loaded;
        Unloaded += DashboardView_Unloaded;
    }

    private async void DashboardView_Loaded(object sender, RoutedEventArgs e)
    {
        Live.Updated -= Live_Updated;
        Live.Updated += Live_Updated;
        Live.SetActivePage("Dashboard");

        S.Context.TargetChanged -= Context_TargetChanged;
        S.Context.TargetChanged += Context_TargetChanged;

        ApplyQuickModulePreferences();
        ApplyLiveCache();

        _ = RefreshEnvironmentAsync(false);
        await RefreshHostAsync();
    }

    private void DashboardView_Unloaded(object sender, RoutedEventArgs e)
    {
        Live.Updated -= Live_Updated;
        Live.DeactivatePage("Dashboard");
        S.Context.TargetChanged -= Context_TargetChanged;
    }

    private void Context_TargetChanged(ServerProfile? _)
    {
        if (!IsLoaded)
            return;

        Dispatcher.BeginInvoke(async () =>
        {
            ApplyLiveCache();
            await RefreshHostAsync();
            await RefreshEnvironmentAsync(false);
        });
    }

    private async void Refresh_Click(object sender, RoutedEventArgs e)
    {
        await RefreshHostAsync();
        ApplyLiveCache();
    }

    private async void EnvironmentRefresh_Click(object sender, RoutedEventArgs e) =>
        await RefreshEnvironmentAsync(true);

    private void EnvironmentHostFocus_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as Button)?.DataContext is EnvironmentMapHostRow row)
            FocusEnvironmentHost(row.ServerId);
    }

    private void EnvironmentApp_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as Button)?.DataContext is not EnvironmentMapAppRow row)
            return;

        FocusEnvironmentHost(row.ServerId);
        S.Navigation.Request($"page:{row.PageKey}");
    }

    private void EnvironmentAttentionCard_Open_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as Button)?.DataContext is EnvironmentAttentionRow row)
            OpenEnvironmentAttention(row);
    }

    private void EnvironmentIntelligence_Click(object sender, RoutedEventArgs e) =>
        S.Navigation.Request("page:Intelligence");

    private void OpenEnvironmentAttention(EnvironmentAttentionRow row)
    {
        FocusEnvironmentHost(row.ServerId);
        S.Navigation.Request($"page:{row.PageKey}");
    }

    private void FocusEnvironmentHost(Guid serverId)
    {
        var server = S.Config.Current.Servers.FirstOrDefault(x => x.Id == serverId);
        if (server is not null && S.Context.Current?.Id != server.Id)
            S.Context.Select(server);
    }

    private async Task RefreshEnvironmentAsync(bool force)
    {
        if (_refreshingEnvironment)
            return;

        _refreshingEnvironment = true;
        EnvironmentRefreshButton.IsEnabled = false;
        EnvironmentFreshnessText.Text = "Refreshing environment...";
        EnvironmentStateText.Text = "CHECKING";
        EnvironmentStateText.Foreground = StatePresentation.BrushForText("checking");

        try
        {
            var snapshot = await S.Environment.GetSnapshotAsync(force);
            ApplyEnvironmentSnapshot(snapshot);
        }
        catch (Exception ex)
        {
            EnvironmentStateText.Text = "ERROR";
            EnvironmentStateText.Foreground = StatePresentation.Resource("Danger");
            EnvironmentStateDetailText.Text = ex.Message;
            EnvironmentFreshnessText.Text = "Environment refresh failed";
        }
        finally
        {
            EnvironmentRefreshButton.IsEnabled = true;
            _refreshingEnvironment = false;
        }
    }

    private void ApplyEnvironmentSnapshot(EnvironmentOverviewSnapshot snapshot)
    {
        _lastEnvironmentSnapshot = snapshot;

        EnvironmentStateText.Text = EnvironmentStateTextFor(snapshot.State);
        EnvironmentStateText.Foreground = EnvironmentStateBrush(snapshot.State);
        EnvironmentStateDetailText.Text = snapshot.HostCount == 0
            ? "Add a Windows or Linux host to begin."
            : snapshot.State switch
            {
                EnvironmentHealthState.Healthy => "All reachable hosts and verified applications are healthy.",
                EnvironmentHealthState.Attention => "One or more verified applications need attention.",
                EnvironmentHealthState.Offline => "At least one host is unreachable; owned applications are impacted.",
                _ => "Environment state is still being established."
            };

        EnvironmentHostsText.Text = snapshot.HostCount.ToString();
        EnvironmentHostsDetailText.Text = snapshot.HostCount == 0
            ? "No configured hosts"
            : $"{snapshot.OnlineHostCount} reachable | {snapshot.AttentionHostCount} affected";

        EnvironmentAppsText.Text = snapshot.VerifiedAppCount.ToString();
        EnvironmentAppsDetailText.Text = snapshot.VerifiedAppCount == 0
            ? "No verified application ownership yet"
            : $"{snapshot.HealthyAppCount} healthy | {snapshot.AttentionAppCount} need attention";

        var findingCount = snapshot.Impacts.Count;
        EnvironmentAttentionText.Text = findingCount.ToString();
        EnvironmentAttentionText.Foreground = findingCount == 0
            ? StatePresentation.Resource("Success")
            : snapshot.Hosts.Any(x => x.State == EnvironmentHealthState.Offline)
                ? StatePresentation.Resource("Danger")
                : StatePresentation.BrushForText("warning");
        EnvironmentAttentionDetailText.Text = findingCount == 0
            ? "No actionable findings"
            : $"{findingCount} finding(s) across {snapshot.AttentionHostCount} host(s)";

        var activeId = Selected?.Id;
        EnvironmentTopologyList.ItemsSource = snapshot.Hosts
            .Select(x => new EnvironmentMapHostRow(x, activeId))
            .ToList();

        var impacts = snapshot.Impacts
            .Select(x => new EnvironmentAttentionRow(x))
            .ToList();

        var compactImpacts = impacts.Take(4).ToList();
        EnvironmentAttentionList.ItemsSource = compactImpacts;
        EnvironmentAttentionList.Visibility = compactImpacts.Count == 0
            ? Visibility.Collapsed
            : Visibility.Visible;
        EnvironmentAttentionEmptyText.Visibility = compactImpacts.Count == 0
            ? Visibility.Visible
            : Visibility.Collapsed;
        EnvironmentAttentionMoreText.Text = impacts.Count > compactImpacts.Count
            ? $"+{impacts.Count - compactImpacts.Count} more in Intelligence"
            : "";

        EnvironmentFreshnessText.Text =
            $"Snapshot {snapshot.SampledAt.ToLocalTime():HH:mm:ss}";

        ApplyQuickModules(snapshot);
    }

    private void ApplyQuickModulePreferences()
    {
        var settings = S.Config.Current.Settings;
        var visibility = settings.ShowQuickModules
            ? Visibility.Visible
            : Visibility.Collapsed;

        QuickModulesHeader.Visibility = visibility;
        QuickModulesPanel.Visibility = visibility;

        if (!settings.ShowQuickModules)
            return;

        var order = settings.QuickModuleOrder
            .Select((name, index) => new { name, index })
            .ToDictionary(x => x.name, x => x.index, StringComparer.OrdinalIgnoreCase);

        var children = QuickModulesPanel.Children.Cast<UIElement>().ToList();
        children.Sort((a, b) =>
        {
            var aName = (a as FrameworkElement)?.Tag?.ToString() ?? "";
            var bName = (b as FrameworkElement)?.Tag?.ToString() ?? "";
            var ai = order.TryGetValue(aName, out var av) ? av : int.MaxValue;
            var bi = order.TryGetValue(bName, out var bv) ? bv : int.MaxValue;
            var cmp = ai.CompareTo(bi);
            return cmp != 0
                ? cmp
                : string.Compare(aName, bName, StringComparison.OrdinalIgnoreCase);
        });

        QuickModulesPanel.Children.Clear();
        foreach (var child in children)
            QuickModulesPanel.Children.Add(child);
    }

    private void ApplyQuickModules(EnvironmentOverviewSnapshot snapshot)
    {
        var findingCount = snapshot.Impacts.Count;
        ModuleIntelligenceValue.Text = EnvironmentStateTextFor(snapshot.State);
        ModuleIntelligenceValue.Foreground = EnvironmentStateBrush(snapshot.State);
        ModuleIntelligenceDetail.Text = findingCount == 0
            ? "No active fleet findings."
            : $"{findingCount} finding(s) across {snapshot.AttentionHostCount} affected host(s).";

        ModuleServersValue.Text = $"{snapshot.OnlineHostCount}/{snapshot.HostCount}";
        ModuleServersValue.Foreground = snapshot.HostCount > 0 &&
                                        snapshot.OnlineHostCount == snapshot.HostCount
            ? StatePresentation.Resource("Success")
            : snapshot.OnlineHostCount == 0
                ? StatePresentation.Resource("Danger")
                : StatePresentation.BrushForText("warning");
        ModuleServersDetail.Text = snapshot.HostCount == 0
            ? "No configured hosts."
            : $"{snapshot.OnlineHostCount} reachable | {snapshot.AttentionHostCount} affected";

        ModuleMediaValue.Text = snapshot.VerifiedAppCount.ToString();
        ModuleMediaValue.Foreground = snapshot.AttentionAppCount == 0
            ? StatePresentation.Resource("Success")
            : StatePresentation.BrushForText("warning");
        ModuleMediaDetail.Text = snapshot.VerifiedAppCount == 0
            ? "No verified applications yet."
            : $"{snapshot.HealthyAppCount} healthy | {snapshot.AttentionAppCount} attention";

        var lifecycleRows = new[] { "Sonarr", "Radarr", "Lidarr" }
            .SelectMany(key => Live.GetQueueRows(key))
            .Where(row => !string.IsNullOrWhiteSpace(row.Title) &&
                          !row.Title.Contains("Queue empty", StringComparison.OrdinalIgnoreCase))
            .ToList();

        var lifecycleAttention = lifecycleRows.Count(row =>
            row.State.Contains("warn", StringComparison.OrdinalIgnoreCase) ||
            row.State.Contains("error", StringComparison.OrdinalIgnoreCase) ||
            row.Detail.Contains("missing", StringComparison.OrdinalIgnoreCase) ||
            row.Detail.Contains("import", StringComparison.OrdinalIgnoreCase));

        ModuleLifecycleValue.Text = lifecycleRows.Count.ToString();
        ModuleLifecycleValue.Foreground = lifecycleAttention > 0
            ? StatePresentation.BrushForText("warning")
            : StatePresentation.Resource("Success");
        ModuleLifecycleDetail.Text = lifecycleRows.Count == 0
            ? "No active Arr queue work visible."
            : lifecycleAttention == 0
                ? $"{lifecycleRows.Count} active item(s) moving normally."
                : $"{lifecycleAttention} item(s) need review across the workflow.";

        var recyclarrHost = snapshot.Hosts
            .FirstOrDefault(host => host.Apps.Any(app =>
                app.Name.Equals("Recyclarr", StringComparison.OrdinalIgnoreCase)));
        var recyclarr = recyclarrHost?.Apps
            .FirstOrDefault(app =>
                app.Name.Equals("Recyclarr", StringComparison.OrdinalIgnoreCase));

        ModuleRecyclarrCard.Visibility = recyclarr is null
            ? Visibility.Collapsed
            : Visibility.Visible;

        if (recyclarr is not null)
        {
            ModuleRecyclarrValue.Text = recyclarr.State == EnvironmentHealthState.Healthy
                ? "AVAILABLE"
                : EnvironmentStateTextFor(recyclarr.State);
            ModuleRecyclarrValue.Foreground = EnvironmentStateBrush(recyclarr.State);
            ModuleRecyclarrDetail.Text =
                $"{recyclarrHost!.Name} | preview-only policy control";
        }

        var selectedId = Selected?.Id;
        var selectedHost = snapshot.Hosts.FirstOrDefault(x => x.ServerId == selectedId);
        ModuleStorageValue.Text = selectedHost is null
            ? "--"
            : selectedHost.StorageRootCount.ToString();
        ModuleStorageValue.Foreground = selectedHost?.State == EnvironmentHealthState.Offline
            ? StatePresentation.Resource("Danger")
            : StatePresentation.Resource("Accent");
        ModuleStorageDetail.Text = selectedHost is null
            ? "Select a host for storage context."
            : $"{selectedHost.StorageRootCount} storage root(s) on {selectedHost.Name}";

        ModuleBackupsValue.Text = selectedHost is null ? "--" : "ON DEMAND";
        ModuleBackupsValue.Foreground = selectedHost?.State == EnvironmentHealthState.Offline
            ? StatePresentation.Resource("Muted")
            : StatePresentation.Resource("Accent");
        ModuleBackupsDetail.Text = selectedHost is null
            ? "Select a host to inspect backup readiness."
            : "Inspect provider schedules and protected actions.";

        RefreshQuickModuleHostState();
        RefreshQuickModuleActivity();
    }

    private void RefreshQuickModuleHostState()
    {
        ModuleDockerValue.Text = DockerValue.Text;
        ModuleDockerValue.Foreground = DockerValue.Foreground;
        ModuleDockerDetail.Text = DockerDetail.Text;
    }

    private void RefreshQuickModuleActivity()
    {
        var latest = S.Activity.Recent.FirstOrDefault();
        ModuleActivityValue.Text = S.Activity.Recent.Count.ToString();
        ModuleActivityValue.Foreground = S.Activity.Recent.Count == 0
            ? StatePresentation.Resource("Muted")
            : StatePresentation.Resource("Accent");
        ModuleActivityDetail.Text = latest is null
            ? "No recent activity."
            : $"{latest.TimeText} · {latest.Title}";
    }

    private static string EnvironmentStateTextFor(EnvironmentHealthState state) =>
        state switch
        {
            EnvironmentHealthState.Healthy => "HEALTHY",
            EnvironmentHealthState.Attention => "ATTENTION",
            EnvironmentHealthState.Offline => "OFFLINE",
            _ => "UNKNOWN"
        };

    private static Brush EnvironmentStateBrush(EnvironmentHealthState state) =>
        state switch
        {
            EnvironmentHealthState.Healthy => StatePresentation.Resource("Success"),
            EnvironmentHealthState.Attention => StatePresentation.BrushForText("warning"),
            EnvironmentHealthState.Offline => StatePresentation.Resource("Danger"),
            _ => StatePresentation.Resource("Muted")
        };

    private async Task RefreshHostAsync()
    {
        if (_refreshingHost)
            return;

        if (Selected is not { } server)
        {
            ConnectionValue.Text = "NO TARGET";
            ConnectionValue.Foreground = StatePresentation.Resource("Muted");
            ConnectionDetail.Text = "Add or select a host.";
            DockerValue.Text = "--";
            DockerDetail.Text = "No selected host";
            QuickStatus.Text = "No host profile configured.";
            return;
        }

        _refreshingHost = true;
        DashboardRefreshButton.IsEnabled = false;

        try
        {
            ConnectionValue.Text = "CHECKING";
            ConnectionValue.Foreground = StatePresentation.BrushForText("checking");
            ConnectionDetail.Text = $"Connecting to {server.Name}...";
            DockerValue.Text = "CHECKING";
            DockerValue.Foreground = StatePresentation.BrushForText("checking");
            DockerDetail.Text = "Detecting container capability";
            QuickStatus.Text = "Refreshing selected host...";

            var provider = S.Hosts.Resolve(server);
            var host = await provider.ProbeAsync(server);

            ConnectionValue.Text = "ONLINE";
            ConnectionValue.Foreground = StatePresentation.Resource("Success");
            ConnectionDetail.Text = server.ConnectionKind switch
            {
                HostConnectionKind.LocalWindows => $"Native Windows access to {host.HostName}",
                HostConnectionKind.RemoteWindows => $"PowerShell remoting to {host.HostName}",
                HostConnectionKind.RemoteLinux => $"SSH provider access to {host.HostName}",
                _ => host.HostName
            };

            var dockerAvailable = host.Capabilities.HasFlag(HostCapability.Docker);
            if (!dockerAvailable)
            {
                DockerValue.Text = "NOT FOUND";
                DockerValue.Foreground = StatePresentation.Resource("Muted");
                DockerDetail.Text = "Docker is optional on this host";
            }
            else if (server.ConnectionKind == HostConnectionKind.RemoteLinux)
            {
                var result = await S.Ssh.ExecuteAsync(
                    server,
                    "docker ps -q 2>/dev/null | wc -l",
                    15);

                if (result.ExitCode == 0 &&
                    int.TryParse(result.StdOut.Trim(), out var runningContainers))
                {
                    DockerValue.Text = runningContainers.ToString();
                    DockerValue.Foreground = StatePresentation.Resource("Success");
                    DockerDetail.Text = runningContainers == 1
                        ? "1 running container"
                        : $"{runningContainers} running containers";
                }
                else
                {
                    DockerValue.Text = "AVAILABLE";
                    DockerValue.Foreground = StatePresentation.Resource("Success");
                    DockerDetail.Text = "Docker detected; container count unavailable";
                }
            }
            else
            {
                DockerValue.Text = "AVAILABLE";
                DockerValue.Foreground = StatePresentation.Resource("Success");
                DockerDetail.Text = "Docker capability detected";
            }

            QuickStatus.Text = $"Host refreshed {DateTime.Now:HH:mm:ss}.";
        }
        catch (Exception ex)
        {
            ConnectionValue.Text = "OFFLINE";
            ConnectionValue.Foreground = StatePresentation.Resource("Danger");
            ConnectionDetail.Text = ex.Message;
            DockerValue.Text = "--";
            DockerValue.Foreground = StatePresentation.Resource("Muted");
            DockerDetail.Text = "Host unavailable";
            QuickStatus.Text = ex.Message;
        }
        finally
        {
            DashboardRefreshButton.IsEnabled = true;
            _refreshingHost = false;
            RefreshQuickModuleHostState();
        }
    }

    private void Live_Updated(object? sender, LiveAnalyticsUpdateEventArgs e)
    {
        if (!IsLoaded)
            return;

        if (e.Domain is LiveAnalyticsDomain.MediaSummary or LiveAnalyticsDomain.PlexSessions)
            Dispatcher.BeginInvoke(ApplyLiveCache);
    }

    private void ApplyLiveCache()
    {
        var media = Live.MediaSnapshot;
        var plex = Live.PlexSnapshot;

        if (Selected is { } selected)
        {
            if (media is not null && media.ServerId != selected.Id)
                media = null;

            if (plex is not null && Live.PlexServerId != selected.Id)
                plex = null;
        }

        if (media is not null)
        {
            LiveHealthyText.Text = media.HealthyCount.ToString();

            var attention = media.DegradedCount + media.OfflineCount;
            LiveAttentionText.Text = attention.ToString();
            LiveAttentionText.Foreground = attention == 0
                ? StatePresentation.Resource("Success")
                : media.OfflineCount > 0
                    ? StatePresentation.Resource("Danger")
                    : StatePresentation.BrushForText("warning");

            LiveQueueText.Text = media.Apps
                .Where(x => x.QueueCount.HasValue)
                .Sum(x => x.QueueCount ?? 0)
                .ToString();
        }
        else
        {
            LiveHealthyText.Text = "--";
            LiveAttentionText.Text = "--";
            LiveAttentionText.Foreground = StatePresentation.Resource("Muted");
            LiveQueueText.Text = "--";
        }

        if (plex is not null)
        {
            LivePlexSessionsText.Text = plex.SessionCount.ToString();
            LivePlexBandwidthText.Text = plex.TotalBandwidthText;
        }
        else
        {
            LivePlexSessionsText.Text = "--";
            LivePlexBandwidthText.Text = "--";
        }

        var timestamps = new List<DateTimeOffset>();
        if (media is not null && Live.MediaUpdatedAt is { } mediaAt)
            timestamps.Add(mediaAt);
        if (plex is not null && Live.PlexUpdatedAt is { } plexAt)
            timestamps.Add(plexAt);

        if (timestamps.Count > 0)
        {
            var newest = timestamps.Max();
            WorkloadFreshnessText.Text = $"LIVE · {newest.ToLocalTime():HH:mm:ss}";
            LiveStatusText.Text = $"telemetry {newest.ToLocalTime():HH:mm:ss}";
        }
        else
        {
            WorkloadFreshnessText.Text = "Waiting for live sample";
            LiveStatusText.Text = "Live telemetry pending";
        }

        RefreshQuickModuleActivity();

        if (_lastEnvironmentSnapshot is not null)
            ApplyQuickModules(_lastEnvironmentSnapshot);
    }

    private void ServersModule_Click(object sender, RoutedEventArgs e) =>
        S.Navigation.Request("page:Servers");

    private void LifecycleModule_Click(object sender, RoutedEventArgs e) =>
        S.Navigation.Request("page:Lifecycle");

    private void RecyclarrModule_Click(object sender, RoutedEventArgs e) =>
        S.Navigation.Request("page:Recyclarr");

    private void ActivityModule_Click(object sender, RoutedEventArgs e) =>
        S.Navigation.Request("page:History");

    private void Intelligence_Click(object sender, RoutedEventArgs e) =>
        S.Navigation.Request("page:Intelligence");

    private void Docker_Click(object sender, RoutedEventArgs e) =>
        S.Navigation.Request("page:Docker");

    private void Storage_Click(object sender, RoutedEventArgs e) =>
        S.Navigation.Request("page:Storage");

    private void Backups_Click(object sender, RoutedEventArgs e) =>
        S.Navigation.Request("page:Backups");

    private void MediaHub_Click(object sender, RoutedEventArgs e) =>
        S.Navigation.Request("page:Applications");

    private async void Diagnostic_Click(object sender, RoutedEventArgs e)
    {
        if (Selected is not { } server)
            return;

        QuickStatus.Text = "Collecting diagnostic bundle...";

        try
        {
            var service = new DiagnosticsBundleService(S);
            var text = await service.CollectAsync(server);
            var dialog = new SaveFileDialog
            {
                FileName = $"graveops-{SafeFilePart(server.Name)}-{DateTime.Now:yyyyMMdd-HHmmss}.txt",
                Filter = "Text file|*.txt"
            };

            if (dialog.ShowDialog() == true)
            {
                File.WriteAllText(dialog.FileName, text);
                QuickStatus.Text = $"Diagnostic bundle saved: {dialog.FileName}";
            }
            else
            {
                QuickStatus.Text = "Diagnostic collection complete; save cancelled.";
            }
        }
        catch (Exception ex)
        {
            QuickStatus.Text = ex.Message;
        }
    }

    private static string SafeFilePart(string value)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var chars = value.Select(ch => invalid.Contains(ch) ? '-' : ch).ToArray();
        return new string(chars).Trim();
    }
}

public sealed class EnvironmentMapHostRow
{
    public Guid ServerId { get; }
    public string Name { get; }
    public string Summary { get; }
    public string ActiveText { get; }
    public Brush StateBrush { get; }
    public List<EnvironmentMapAppRow> Apps { get; }

    public EnvironmentMapHostRow(EnvironmentHostSnapshot host, Guid? activeId)
    {
        ServerId = host.ServerId;
        Name = host.Name;
        Summary =
            $"{host.PlatformText} / {host.ConnectionText} | " +
            $"{host.StorageRootCount} storage root(s) | {host.Detail}";
        ActiveText = host.ServerId == activeId ? "ACTIVE" : "";
        StateBrush = EnvironmentRowBrushes.For(host.State);
        Apps = host.Apps
            .Select(app => new EnvironmentMapAppRow(host.ServerId, app))
            .ToList();
    }
}

public sealed class EnvironmentMapAppRow
{
    public Guid ServerId { get; }
    public string Name { get; }
    public string Detail { get; }
    public string PageKey { get; }
    public Brush StateBrush { get; }

    public EnvironmentMapAppRow(Guid serverId, EnvironmentAppSnapshot app)
    {
        ServerId = serverId;
        Name = app.Name;
        Detail = app.Detail;
        PageKey = EnvironmentImpactSnapshot.ResolvePageKey(app.Name);
        StateBrush = EnvironmentRowBrushes.For(app.State);
    }
}

public sealed class EnvironmentAttentionRow
{
    public Guid ServerId { get; }
    public string Host { get; }
    public string Component { get; }
    public string State { get; }
    public string Impact { get; }
    public string PageKey { get; }
    public Brush StateBrush { get; }

    public EnvironmentAttentionRow(EnvironmentImpactSnapshot impact)
    {
        ServerId = impact.ServerId;
        Host = impact.HostName;
        Component = impact.Component;
        State = impact.State switch
        {
            EnvironmentHealthState.Healthy => "Healthy",
            EnvironmentHealthState.Attention => "Attention",
            EnvironmentHealthState.Offline => "Offline",
            _ => "Unknown"
        };
        Impact = impact.Impact;
        PageKey = impact.PageKey;
        StateBrush = EnvironmentRowBrushes.For(impact.State);
    }
}

internal static class EnvironmentRowBrushes
{
    public static Brush For(EnvironmentHealthState state) =>
        state switch
        {
            EnvironmentHealthState.Healthy => StatePresentation.Resource("Success"),
            EnvironmentHealthState.Attention => StatePresentation.BrushForText("warning"),
            EnvironmentHealthState.Offline => StatePresentation.Resource("Danger"),
            _ => StatePresentation.Resource("Muted")
        };
}
