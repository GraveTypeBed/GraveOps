using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using GraveOps.App.Models;

namespace GraveOps.App.Views;

public partial class IntegrationView : UserControl
{
    private readonly string _integrationName;
    private ManagedApp? _app;
    private ServerProfile? _owner;
    private Services.AppServices S => App.Services;

    public IntegrationView(string integrationName)
    {
        _integrationName = integrationName;
        InitializeComponent();
        TitleText.Text = integrationName;
        SubtitleText.Text = SubtitleFor(integrationName);
        RecyclarrPanel.Visibility = integrationName.Equals("Recyclarr", StringComparison.OrdinalIgnoreCase)
            ? Visibility.Visible
            : Visibility.Collapsed;
        Loaded += async (_, _) => await RefreshAsync();
    }

    private async Task RefreshAsync()
    {
        ResolveOwner();
        if (_owner is null)
        {
            PaintUnavailable("No verified owner is assigned to this integration.");
            return;
        }

        StateText.Text = "Loading";
        StateText.Foreground = (System.Windows.Media.Brush)FindResource("Muted");
        StatusText.Text = $"Checking {_integrationName} on {_owner.Name}...";

        try
        {
            var status = await S.IntegrationRuntime.ProbeAsync(_integrationName, _owner, _app);
            PaintStatus(status);
            if (_integrationName.Equals("Recyclarr", StringComparison.OrdinalIgnoreCase))
                await RefreshRecyclarrInstancesAsync(status.CanPreviewRecyclarr);
        }
        catch (Exception ex)
        {
            PaintUnavailable(ex.Message);
        }
    }

    private async Task RefreshRecyclarrInstancesAsync(bool canPreview)
    {
        if (_owner is null || !canPreview)
        {
            RecyclarrInstancesList.ItemsSource = null;
            RecyclarrInstancesList.Visibility = Visibility.Collapsed;
            RecyclarrInstancesEmptyText.Visibility = Visibility.Visible;
            RecyclarrInstancesEmptyText.Text = canPreview
                ? "No owning host is available for instance discovery."
                : "Preview runtime is not currently available.";
            return;
        }

        var instances = await S.IntegrationRuntime.DiscoverRecyclarrInstancesAsync(_owner);
        RecyclarrInstancesList.ItemsSource = instances;
        RecyclarrInstancesList.Visibility = instances.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        RecyclarrInstancesEmptyText.Visibility = instances.Count > 0 ? Visibility.Collapsed : Visibility.Visible;
        RecyclarrInstancesEmptyText.Text = instances.Count > 0
            ? ""
            : "No instance names were discovered from the standard Recyclarr config locations; service-wide preview remains available.";
    }

    private void ResolveOwner()
    {
        _app = S.Config.Current.Applications.FirstOrDefault(x =>
            x.DiscoveryVerified &&
            x.Name.Equals(_integrationName, StringComparison.OrdinalIgnoreCase));

        _owner = _app?.ServerId is { } id
            ? S.Config.Current.Servers.FirstOrDefault(x => x.Id == id)
            : S.Context.Current;

        OwnerText.Text = _owner?.Name ?? "No owner";
        OwnerCardText.Text = _owner?.Name ?? "No owner";
        TerminalButton.IsEnabled = _owner?.ConnectionKind == HostConnectionKind.RemoteLinux;
        LogsButton.IsEnabled = _owner?.ConnectionKind == HostConnectionKind.RemoteLinux;
    }

    private void PaintStatus(IntegrationRuntimeStatus status)
    {
        StateText.Text = status.StateText;
        StateText.Foreground = status.Health switch
        {
            AppHealthState.Healthy or AppHealthState.Busy => (System.Windows.Media.Brush)FindResource("Success"),
            AppHealthState.Offline => (System.Windows.Media.Brush)FindResource("Danger"),
            AppHealthState.Degraded or AppHealthState.Stale => (System.Windows.Media.Brush)FindResource("Warn"),
            _ => (System.Windows.Media.Brush)FindResource("Muted")
        };
        OwnerText.Text = status.Owner;
        OwnerCardText.Text = status.Owner;
        RuntimeText.Text = string.IsNullOrWhiteSpace(status.Runtime) ? "--" : status.Runtime;
        EndpointText.Text = string.IsNullOrWhiteSpace(status.Endpoint) ? "Not applicable" : status.Endpoint;
        DetailText.Text = status.Detail;
        EvidenceText.Text = string.IsNullOrWhiteSpace(status.DiscoveryEvidence) ? "No discovery evidence recorded." : status.DiscoveryEvidence;
        OpenButton.IsEnabled = status.CanOpen && _app is not null;
        PreviewSonarrButton.IsEnabled = status.CanPreviewRecyclarr;
        PreviewRadarrButton.IsEnabled = status.CanPreviewRecyclarr;

        HttpTelemetryText.Text = status.HttpText;
        LatencyTelemetryText.Text = status.LatencyText;
        RuntimeStateTelemetryText.Text = status.RuntimeStateText;
        CpuTelemetryText.Text = status.CpuText;
        MemoryTelemetryText.Text = status.MemoryText;
        ReadinessTelemetryText.Text = status.ReadinessText;
        BuildTelemetryText.Text = status.BuildText;
        UptimeTelemetryText.Text = status.UptimeText;
        RuntimeDetailText.Text = status.RuntimeDetail;

        StatusText.Text = $"{_integrationName} checked {DateTime.Now:HH:mm:ss} on {status.Owner}.";
    }

    private void PaintUnavailable(string detail)
    {
        StateText.Text = "Unavailable";
        StateText.Foreground = (System.Windows.Media.Brush)FindResource("Danger");
        RuntimeText.Text = "--";
        EndpointText.Text = "--";
        DetailText.Text = detail;
        EvidenceText.Text = _app?.DiscoveryEvidence ?? "No verified discovery record.";
        OpenButton.IsEnabled = false;
        PreviewSonarrButton.IsEnabled = false;
        PreviewRadarrButton.IsEnabled = false;
        HttpTelemetryText.Text = "--";
        LatencyTelemetryText.Text = "--";
        RuntimeStateTelemetryText.Text = "--";
        CpuTelemetryText.Text = "--";
        MemoryTelemetryText.Text = "--";
        ReadinessTelemetryText.Text = "--";
        BuildTelemetryText.Text = "--";
        UptimeTelemetryText.Text = "--";
        RuntimeDetailText.Text = detail;
        StatusText.Text = detail;
    }

    private async void Refresh_Click(object sender, RoutedEventArgs e) => await RefreshAsync();

    private void FocusOwner_Click(object sender, RoutedEventArgs e)
    {
        ResolveOwner();
        if (_owner is not null) S.Context.Select(_owner);
    }

    private void Open_Click(object sender, RoutedEventArgs e)
    {
        ResolveOwner();
        if (_owner is null || _app is null || string.IsNullOrWhiteSpace(_app.Url)) return;
        S.Context.Select(_owner);
        var resolved = Services.IntegrationRuntimeService.ResolveEndpoint(_app, _owner);
        if (_app.OpenEmbedded) new EmbeddedBrowserWindow(_app.Name, resolved).Show();
        else Process.Start(new ProcessStartInfo(resolved) { UseShellExecute = true });
        S.Activity.Record($"Opened {_integrationName}", resolved, ActivityLevel.Info, serverId: _owner.Id, deepLink: $"app:{_integrationName}");
    }

    private void Terminal_Click(object sender, RoutedEventArgs e)
    {
        FocusOwner_Click(sender, e);
        S.Navigation.Request("page:Terminal");
    }

    private void Logs_Click(object sender, RoutedEventArgs e)
    {
        FocusOwner_Click(sender, e);
        S.Navigation.Request("page:Logs");
    }

    private async void PreviewSonarr_Click(object sender, RoutedEventArgs e) => await RunPreviewAsync("sonarr");
    private async void PreviewRadarr_Click(object sender, RoutedEventArgs e) => await RunPreviewAsync("radarr");

    private async void PreviewInstance_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: RecyclarrInstanceInfo instance })
            return;
        await RunPreviewAsync(instance.Service, instance.Name);
    }

    private async Task RunPreviewAsync(string service, string? instance = null)
    {
        ResolveOwner();
        if (_owner is null) return;

        PreviewSonarrButton.IsEnabled = false;
        PreviewRadarrButton.IsEnabled = false;
        RecyclarrInstancesList.IsEnabled = false;
        var target = string.IsNullOrWhiteSpace(instance) ? service : $"{service}/{instance}";
        PreviewOutputBox.Text = $"Running Recyclarr {target} preview on {_owner.Name}...";
        StatusText.Text = $"Recyclarr {target} preview running...";
        try
        {
            var result = await S.IntegrationRuntime.RunRecyclarrPreviewAsync(_owner, service, instance);
            PreviewOutputBox.Text = result.Output;
            StatusText.Text = result.Success
                ? $"Recyclarr {target} preview completed. No sync write was requested."
                : $"Recyclarr {target} preview did not complete successfully.";
            S.Activity.Record(
                $"Recyclarr {target} preview",
                result.Success ? "Preview completed." : "Preview failed or was unavailable.",
                result.Success ? ActivityLevel.Success : ActivityLevel.Warning,
                serverId: _owner.Id,
                deepLink: "page:Recyclarr");
        }
        catch (Exception ex)
        {
            PreviewOutputBox.Text = ex.Message;
            StatusText.Text = "Recyclarr preview failed.";
        }
        finally
        {
            RecyclarrInstancesList.IsEnabled = true;
            await RefreshAsync();
        }
    }

    private static string SubtitleFor(string name) => name.ToLowerInvariant() switch
    {
        "tautulli" => "Plex analytics, activity and historical visibility.",
        "kometa" => "Plex metadata, collection, overlay and playlist automation runtime.",
        "bazarr" => "Subtitle automation health and owner routing.",
        "seerr" => "Media request service health and request-workflow ownership.",
        "recyclarr" => "TRaSH-backed Sonarr/Radarr quality-policy synchronization and safe previews.",
        "profilarr" => "Configuration management, testing and deployment for Sonarr/Radarr profiles.",
        "autobrr" => "IRC/RSS release automation, filtering and acquisition handoff health.",
        "unpackerr" => "Archive extraction runtime and import-pipeline visibility.",
        "cleanuparr" => "Queue cleanup, download hygiene and replacement-search automation health.",
        "tdarr" => "Distributed media processing and transcoding runtime visibility.",
        "maintainerr" => "Media lifecycle and retention automation health.",
        _ => "Integration health and operations."
    };
}
