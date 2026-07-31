using GraveOps.App.Models;

namespace GraveOps.App.Services;

public sealed class IntegrationCatalog
{
    public IReadOnlyList<IntegrationDefinition> All { get; } =
    [
        new("Plex", "Plex", IntegrationCategory.Library, true, "Media server and session visibility."),
        new("Jellyfin", "Jellyfin", IntegrationCategory.Library, true, "Open media-server integration target."),
        new("Emby", "Emby", IntegrationCategory.Library, true, "Media-server integration target."),
        new("Tautulli", "Tautulli", IntegrationCategory.Library, true, "Plex analytics and history."),
        new("Kometa", "Kometa", IntegrationCategory.Library, true, "Library metadata, collections and overlays."),
        new("Sonarr", "Sonarr", IntegrationCategory.Acquisition, true, "TV acquisition and import automation."),
        new("Radarr", "Radarr", IntegrationCategory.Acquisition, true, "Movie acquisition and import automation."),
        new("Lidarr", "Lidarr", IntegrationCategory.Acquisition, true, "Music acquisition and import automation."),
        new("Prowlarr", "Prowlarr", IntegrationCategory.Acquisition, true, "Indexer management and health."),
        new("Bazarr", "Bazarr", IntegrationCategory.Acquisition, true, "Subtitle automation."),
        new("Seerr", "Seerr", IntegrationCategory.Acquisition, true, "Media request management."),
        new("SABnzbd", "SABnzbd", IntegrationCategory.Downloads, true, "Usenet download client."),
        new("qBittorrent", "qBittorrent", IntegrationCategory.Downloads, true, "BitTorrent download client."),
        new("Recyclarr", "Recyclarr", IntegrationCategory.QualityAutomation, true, "TRaSH-backed profile and custom-format synchronization."),
        new("Profilarr", "Profilarr", IntegrationCategory.QualityAutomation, true, "Profile and quality automation alternative."),
        new("autobrr", "autobrr", IntegrationCategory.QualityAutomation, true, "Release automation and filtering."),
        new("Unpackerr", "Unpackerr", IntegrationCategory.QualityAutomation, true, "Archive extraction pipeline."),
        new("Cleanuparr", "Cleanuparr", IntegrationCategory.QualityAutomation, true, "Queue and unwanted-download cleanup."),
        new("Tdarr", "Tdarr", IntegrationCategory.Processing, true, "Distributed media processing and transcoding."),
        new("Maintainerr", "Maintainerr", IntegrationCategory.Lifecycle, true, "Media lifecycle and retention automation."),
        new("Pi-hole", "Pi-hole", IntegrationCategory.Network, true, "DNS and filtering visibility."),
        new("Docker", "Docker", IntegrationCategory.Infrastructure, true, "Container inventory and operations.")
    ];

    public IntegrationDefinition? Find(string key) =>
        All.FirstOrDefault(x =>
            x.Key.Equals(key, StringComparison.OrdinalIgnoreCase));
}
