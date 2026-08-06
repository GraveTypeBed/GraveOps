using System.Globalization;
using System.Text.Json;
using GraveOps.Core.Hosts;
using GraveOps.Presentation.Avalonia.Dashboard;

namespace GraveOps.Desktop.Windows;

public partial class MainWindow
{
    private readonly Dictionary<
        string,
        IReadOnlyList<DashboardCardPreference>>
        _sharedDashboardLayouts =
            new(
                StringComparer.OrdinalIgnoreCase);

    private string SharedDashboardLayoutPath =>
        Path.Combine(
            Environment.GetFolderPath(
                Environment.SpecialFolder.ApplicationData),
            "GraveOps",
            "shared-dashboard-windows.json");

    private void InitializeSharedUnifiedDashboard()
    {
        LoadSharedDashboardLayouts();

        var view =
            Get<UnifiedDashboardView>(
                "SharedDashboardView");

        view.RefreshRequested +=
            async (_, _) =>
                await RefreshAsync();

        view.ActionRequested +=
            (_, eventArgs) =>
            {
                if (!string.IsNullOrWhiteSpace(
                        eventArgs.Action.NavigationName))
                {
                    Navigate(
                        eventArgs.Action.NavigationName);
                }
            };

        view.LayoutChanged +=
            (_, eventArgs) =>
            {
                _sharedDashboardLayouts[
                        eventArgs.HostKey] =
                    eventArgs.Layout;

                SaveSharedDashboardLayouts();

                if (_snapshot is not null)
                {
                    var recommendations =
                        BuildRecommendations(
                            _snapshot);

                    UpdateSharedUnifiedDashboard(
                        _snapshot,
                        recommendations,
                        EvaluateHealth(
                            _snapshot,
                            recommendations));
                }
            };

        view.Update(
            UnifiedDashboardState.Waiting);
    }

    private void UpdateSharedUnifiedDashboard(
        HostSnapshot snapshot,
        IReadOnlyList<RecommendationRow> recommendations,
        HealthSummary health)
    {
        var hostKey =
            ActiveTargetOrThrow().Id;

        var cards =
            BuildWindowsSharedDashboardCards(
                snapshot,
                recommendations,
                health);

        _sharedDashboardLayouts.TryGetValue(
            hostKey,
            out var savedLayout);

        Get<UnifiedDashboardView>(
                "SharedDashboardView")
            .Update(
                new UnifiedDashboardState(
                    hostKey,
                    $"Captured {snapshot.CapturedAt.ToLocalTime():t}",
                    recommendations.Count == 0
                        ? "Healthy"
                        : $"{recommendations.Count} active finding{(recommendations.Count == 1 ? string.Empty : "s")}",
                    recommendations.Count == 0
                        ? "0 active findings \u00B7 all monitored dependencies ready"
                        : $"{recommendations[0].Severity} \u00B7 {recommendations[0].Component} \u00B7 {recommendations[0].Message}",
                    recommendations.Count == 0,
                    "Compact",
                    cards,
                    savedLayout ??
                    Array.Empty<DashboardCardPreference>()));
    }

    private void SetSharedUnifiedDashboardFailure(
        string detail)
    {
        Get<UnifiedDashboardView>(
                "SharedDashboardView")
            .Update(
                new UnifiedDashboardState(
                    ActiveTargetOrThrow().Id,
                    "Capture failed",
                    "Unavailable",
                    detail,
                    false,
                    "Compact",
                    Array.Empty<UnifiedDashboardCard>(),
                    Array.Empty<DashboardCardPreference>()));
    }

    private IReadOnlyList<UnifiedDashboardCard>
        BuildWindowsSharedDashboardCards(
        HostSnapshot snapshot,
        IReadOnlyList<RecommendationRow> recommendations,
        HealthSummary health)
    {
        var cards =
            new List<UnifiedDashboardCard>();

        if (recommendations.Count > 0)
        {
            cards.Add(
                new UnifiedDashboardCard(
                    "core:health",
                    "Health findings",
                    "Infrastructure",
                    recommendations.Any(item =>
                        item.Severity.Equals(
                            "FAIL",
                            StringComparison.Ordinal))
                        ? "ACTION REQUIRED"
                        : "ATTENTION",
                    recommendations.Any(item =>
                        item.Severity.Equals(
                            "FAIL",
                            StringComparison.Ordinal))
                        ? DashboardSeverity.Error
                        : DashboardSeverity.Warning,
                    recommendations.Count.ToString(
                        CultureInfo.InvariantCulture),
                    $"{health.Warn} warning \u00B7 {health.Fail} failure",
                    "Read-only findings from the selected Windows provider snapshot.",
                    "Open findings",
                    "IntelligenceNav",
                    string.Empty,
                    "windows:health",
                    true)
                {
                    Rows =
                        recommendations
                            .Take(8)
                            .Select(item =>
                                new UnifiedDashboardRow(
                                    item.Component,
                                    item.Severity,
                                    item.Message,
                                    item.Severity.Equals(
                                        "FAIL",
                                        StringComparison.Ordinal)
                                        ? DashboardSeverity.Error
                                        : DashboardSeverity.Warning,
                                    item.Evidence))
                            .ToArray()
                });
        }

        cards.Add(
            new UnifiedDashboardCard(
                "core:host",
                "Host",
                "Infrastructure",
                "READY",
                DashboardSeverity.Healthy,
                snapshot.Hostname,
                snapshot.SystemState,
                $"{snapshot.OperatingSystem} \u00B7 {snapshot.Kernel} \u00B7 {snapshot.IpAddresses}",
                "Open host",
                "ServersNav",
                string.Empty,
                "windows:host",
                true)
            {
                Rows =
                    new[]
                    {
                        new UnifiedDashboardRow(
                            "Operating system",
                            snapshot.OperatingSystem,
                            snapshot.Kernel,
                            DashboardSeverity.Info),
                        new UnifiedDashboardRow(
                            "Address",
                            snapshot.IpAddresses,
                            string.Empty,
                            DashboardSeverity.Info),
                        new UnifiedDashboardRow(
                            "Uptime",
                            snapshot.Uptime,
                            string.Empty,
                            DashboardSeverity.Healthy),
                        new UnifiedDashboardRow(
                            "Memory",
                            snapshot.MemorySummary,
                            string.Empty,
                            DashboardSeverity.Info)
                    }
            });

        var storageSeverity =
            snapshot.Storage.Any(volume =>
                ParsePercent(
                    volume.PercentUsed) >= 95)
                ? DashboardSeverity.Error
                : snapshot.Storage.Any(volume =>
                    ParsePercent(
                        volume.PercentUsed) >= 85)
                    ? DashboardSeverity.Warning
                    : DashboardSeverity.Healthy;

        cards.Add(
            new UnifiedDashboardCard(
                "core:storage",
                "Storage",
                "Infrastructure",
                storageSeverity == DashboardSeverity.Healthy
                    ? "HEALTHY"
                    : storageSeverity == DashboardSeverity.Warning
                        ? "ATTENTION"
                        : "CRITICAL",
                storageSeverity,
                snapshot.Storage.Count.ToString(
                    CultureInfo.InvariantCulture),
                BuildStorageSummary(
                    snapshot.Storage),
                "Ready volumes and capacity evidence from the selected Windows provider.",
                "Open storage",
                "StorageNav",
                string.Empty,
                "windows:storage",
                true)
            {
                Rows =
                    snapshot.Storage
                        .OrderByDescending(volume =>
                            ParsePercent(
                                volume.PercentUsed))
                        .Take(8)
                        .Select(volume =>
                            new UnifiedDashboardRow(
                                volume.MountPoint,
                                volume.PercentUsed,
                                volume.Source,
                                ParsePercent(
                                    volume.PercentUsed) >= 95
                                    ? DashboardSeverity.Error
                                    : ParsePercent(
                                        volume.PercentUsed) >= 85
                                        ? DashboardSeverity.Warning
                                        : DashboardSeverity.Healthy,
                                volume.Available))
                        .ToArray()
            });

        var unhealthyContainers =
            snapshot.Containers.Count(container =>
                !IsHealthyState(
                    container.State));

        cards.Add(
            new UnifiedDashboardCard(
                "core:docker",
                "Docker",
                "Infrastructure",
                snapshot.Containers.Count == 0
                    ? "NOT DETECTED"
                    : unhealthyContainers == 0
                        ? "READY"
                        : "ATTENTION",
                snapshot.Containers.Count == 0
                    ? DashboardSeverity.Info
                    : unhealthyContainers == 0
                        ? DashboardSeverity.Healthy
                        : DashboardSeverity.Warning,
                snapshot.Containers.Count.ToString(
                    CultureInfo.InvariantCulture),
                NormalizeDisplay(
                    snapshot.DockerState),
                "Container state and image evidence reported by the selected Windows provider.",
                "Open Docker",
                "DockerNav",
                string.Empty,
                "windows:docker",
                true)
            {
                Rows =
                    snapshot.Containers
                        .Take(8)
                        .Select(container =>
                            new UnifiedDashboardRow(
                                container.Name,
                                NormalizeDisplay(
                                    container.State),
                                container.Status,
                                IsHealthyState(
                                    container.State)
                                    ? DashboardSeverity.Healthy
                                    : DashboardSeverity.Warning,
                                container.Image))
                        .ToArray()
            });

        var acquisitionNames =
            new HashSet<string>(
                new[]
                {
                    "Sonarr",
                    "Radarr",
                    "Lidarr",
                    "Prowlarr",
                    "Readarr",
                    "Whisparr",
                    "Bazarr"
                },
                StringComparer.OrdinalIgnoreCase);

        var acquisition =
            snapshot.Integrations
                .Where(item =>
                    acquisitionNames.Contains(
                        item.Name))
                .ToArray();

        cards.Add(
            new UnifiedDashboardCard(
                "core:acquisition",
                "Acquisition",
                "Operations",
                acquisition.Length == 0
                    ? "NOT CONFIGURED"
                    : acquisition.All(item =>
                        IsHealthyState(
                            item.State))
                        ? "READY"
                        : "ATTENTION",
                acquisition.Length == 0
                    ? DashboardSeverity.Info
                    : acquisition.All(item =>
                        IsHealthyState(
                            item.State))
                        ? DashboardSeverity.Healthy
                        : DashboardSeverity.Warning,
                acquisition.Length.ToString(
                    CultureInfo.InvariantCulture),
                acquisition.Length == 0
                    ? "No Arr applications detected"
                    : $"{acquisition.Count(item => IsHealthyState(item.State))} ready",
                "Detected acquisition applications and provider ownership.",
                "Open Media Hub",
                "MediaHubNav",
                string.Empty,
                "windows:acquisition",
                true)
            {
                Rows =
                    acquisition
                        .Select(item =>
                            SharedDashboardIntegrationRow(
                                item))
                        .ToArray()
            });

        var downloadNames =
            new HashSet<string>(
                new[]
                {
                    "qBittorrent",
                    "SABnzbd"
                },
                StringComparer.OrdinalIgnoreCase);

        var downloads =
            snapshot.Integrations
                .Where(item =>
                    downloadNames.Contains(
                        item.Name))
                .ToArray();

        cards.Add(
            new UnifiedDashboardCard(
                "core:downloads",
                "Downloads",
                "Operations",
                downloads.Length == 0
                    ? "NOT CONFIGURED"
                    : downloads.All(item =>
                        IsHealthyState(
                            item.State))
                        ? "READY"
                        : "ATTENTION",
                downloads.Length == 0
                    ? DashboardSeverity.Info
                    : downloads.All(item =>
                        IsHealthyState(
                            item.State))
                        ? DashboardSeverity.Healthy
                        : DashboardSeverity.Warning,
                downloads.Length.ToString(
                    CultureInfo.InvariantCulture),
                downloads.Length == 0
                    ? "No download clients detected"
                    : $"{downloads.Count(item => IsHealthyState(item.State))} ready",
                "Detected torrent and Usenet clients for the selected target.",
                "Open downloads",
                downloads.Any(item =>
                    item.Name.Equals(
                        "qBittorrent",
                        StringComparison.OrdinalIgnoreCase))
                    ? "QBittorrentNav"
                    : "SABnzbdNav",
                string.Empty,
                "windows:downloads",
                true)
            {
                Rows =
                    downloads
                        .Select(item =>
                            SharedDashboardIntegrationRow(
                                item))
                        .ToArray()
            });

        cards.Add(
            new UnifiedDashboardCard(
                "core:backups",
                "Backups",
                "Operations",
                "NOT CONFIGURED",
                DashboardSeverity.Info,
                "0",
                "No Windows backup provider is configured",
                "Backup capability remains explicit until a provider reports schedule, artifact and restore-readiness evidence.",
                "Open backups",
                "BackupsNav",
                string.Empty,
                "windows:backups",
                true));

        var plex =
            snapshot.Integrations.FirstOrDefault(item =>
                item.Name.Equals(
                    "Plex",
                    StringComparison.OrdinalIgnoreCase));

        cards.Add(
            new UnifiedDashboardCard(
                "app:plex",
                "Plex",
                "Media",
                plex is null
                    ? "NOT DETECTED"
                    : IsHealthyState(
                        plex.State)
                        ? "READY"
                        : "ATTENTION",
                plex is null
                    ? DashboardSeverity.Info
                    : IsHealthyState(
                        plex.State)
                        ? DashboardSeverity.Healthy
                        : DashboardSeverity.Warning,
                plex is null
                    ? "0"
                    : "1",
                plex is null
                    ? "Plex was not reported by the selected provider"
                    : NormalizeDisplay(
                        plex.State),
                plex?.Evidence ??
                "No Plex discovery evidence was reported.",
                "Open Plex",
                "PlexNav",
                string.Empty,
                "windows:plex",
                true)
            {
                Rows =
                    plex is null
                        ? Array.Empty<UnifiedDashboardRow>()
                        : new[]
                        {
                            SharedDashboardIntegrationRow(
                                plex)
                        }
            });

        var healthyIntegrations =
            snapshot.Integrations.Count(item =>
                IsHealthyState(
                    item.State));

        cards.Add(
            new UnifiedDashboardCard(
                "core:media",
                "Media fleet",
                "Media",
                snapshot.Integrations.Count == 0
                    ? "EMPTY"
                    : healthyIntegrations ==
                      snapshot.Integrations.Count
                        ? "READY"
                        : "ATTENTION",
                snapshot.Integrations.Count == 0
                    ? DashboardSeverity.Info
                    : healthyIntegrations ==
                      snapshot.Integrations.Count
                        ? DashboardSeverity.Healthy
                        : DashboardSeverity.Warning,
                snapshot.Integrations.Count.ToString(
                    CultureInfo.InvariantCulture),
                $"{healthyIntegrations} running",
                "All detected media applications owned by the selected target.",
                "Open Media Hub",
                "MediaHubNav",
                string.Empty,
                "windows:media",
                true)
            {
                Rows =
                    snapshot.Integrations
                        .Take(8)
                        .Select(item =>
                            SharedDashboardIntegrationRow(
                                item))
                        .ToArray()
            });

        cards.Add(
            new UnifiedDashboardCard(
                "core:activity",
                "Recent activity",
                "Media",
                _activity.Count == 0
                    ? "QUIET"
                    : "ACTIVE",
                DashboardSeverity.Info,
                _activity.Count.ToString(
                    CultureInfo.InvariantCulture),
                _activity.Count == 0
                    ? "No recent operator activity"
                    : _activity[0].Title,
                "Recent navigation, captures and read-only operator activity.",
                "Open activity",
                "HistoryNav",
                string.Empty,
                "windows:activity",
                true)
            {
                Rows =
                    _activity
                        .Take(8)
                        .Select(item =>
                            new UnifiedDashboardRow(
                                item.Time,
                                item.Title,
                                item.Detail,
                                DashboardSeverity.Info))
                        .ToArray()
            });

        foreach (var integration in
                 snapshot.Integrations
                     .Where(item =>
                         !item.Name.Equals(
                             "Plex",
                             StringComparison.OrdinalIgnoreCase))
                     .OrderBy(item =>
                         item.Name,
                         StringComparer.OrdinalIgnoreCase))
        {
            cards.Add(
                new UnifiedDashboardCard(
                    $"app:{SharedDashboardNormalizeCardKey(integration.Name)}",
                    integration.Name,
                    "Applications",
                    IsHealthyState(
                        integration.State)
                        ? "READY"
                        : "ATTENTION",
                    IsHealthyState(
                        integration.State)
                        ? DashboardSeverity.Healthy
                        : DashboardSeverity.Warning,
                    NormalizeDisplay(
                        integration.State),
                    NormalizeDisplay(
                        integration.Kind),
                    NormalizeDisplay(
                        integration.Evidence),
                    $"Open {integration.Name}",
                    SharedDashboardIntegrationNavigation(
                        integration.Name),
                    string.Empty,
                    $"windows:app:{SharedDashboardNormalizeCardKey(integration.Name)}",
                    true)
                {
                    Rows =
                        new[]
                        {
                            SharedDashboardIntegrationRow(
                                integration)
                        }
                });
        }

        return cards;
    }

    private static UnifiedDashboardRow SharedDashboardIntegrationRow(
        IntegrationSnapshot integration) =>
        new(
            integration.Name,
            NormalizeDisplay(
                integration.State),
            NormalizeDisplay(
                integration.Evidence),
            IsHealthyState(
                integration.State)
                ? DashboardSeverity.Healthy
                : DashboardSeverity.Warning,
            NormalizeDisplay(
                integration.Kind));

    private static string SharedDashboardIntegrationNavigation(
        string name) =>
        name.ToLowerInvariant() switch
        {
            "plex" =>
                "PlexNav",

            "sonarr" =>
                "SonarrNav",

            "radarr" =>
                "RadarrNav",

            "lidarr" =>
                "LidarrNav",

            "prowlarr" =>
                "ProwlarrNav",

            "qbittorrent" =>
                "QBittorrentNav",

            "sabnzbd" =>
                "SABnzbdNav",

            _ =>
                "MediaHubNav"
        };

    private static string SharedDashboardNormalizeCardKey(
        string value) =>
        new(
            value
                .ToLowerInvariant()
                .Where(character =>
                    char.IsLetterOrDigit(
                        character))
                .ToArray());

    private void LoadSharedDashboardLayouts()
    {
        try
        {
            if (!File.Exists(
                    SharedDashboardLayoutPath))
            {
                return;
            }

            var loaded =
                JsonSerializer.Deserialize<
                    Dictionary<
                        string,
                        List<
                            DashboardCardPreference>>>(
                    File.ReadAllText(
                        SharedDashboardLayoutPath));

            if (loaded is null)
                return;

            foreach (var item in loaded)
            {
                _sharedDashboardLayouts[
                        item.Key] =
                    item.Value;
            }
        }
        catch
        {
            _sharedDashboardLayouts.Clear();
        }
    }

    private void SaveSharedDashboardLayouts()
    {
        try
        {
            var directory =
                Path.GetDirectoryName(
                    SharedDashboardLayoutPath);

            if (!string.IsNullOrWhiteSpace(
                    directory))
            {
                Directory.CreateDirectory(
                    directory);
            }

            var payload =
                _sharedDashboardLayouts
                    .ToDictionary(
                        item => item.Key,
                        item => item.Value.ToList(),
                        StringComparer.OrdinalIgnoreCase);

            var temporary =
                SharedDashboardLayoutPath +
                ".tmp";

            File.WriteAllText(
                temporary,
                JsonSerializer.Serialize(
                    payload,
                    new JsonSerializerOptions
                    {
                        WriteIndented = true
                    }));

            File.Move(
                temporary,
                SharedDashboardLayoutPath,
                overwrite: true);
        }
        catch
        {
            // Layout persistence must not prevent the dashboard from rendering.
        }
    }
}