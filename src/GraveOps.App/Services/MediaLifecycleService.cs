using System.Text.RegularExpressions;
using GraveOps.App.Models;

namespace GraveOps.App.Services;

/// <summary>
/// Correlates data GraveOps already owns into a media workflow view. It deliberately
/// does not poll in the background. Refreshing the Lifecycle page asks the existing
/// LiveAnalyticsService for fresh samples, then correlates Arr and downloader work.
/// </summary>
public sealed class MediaLifecycleService
{
    private readonly AppServices _services;

    public MediaLifecycleService(AppServices services) => _services = services;

    public async Task<MediaLifecycleSnapshot> GetSnapshotAsync(
        ServerProfile server,
        bool force = false,
        CancellationToken cancellationToken = default)
    {
        var live = LiveAnalyticsHub.Current;

        if (force)
        {
            foreach (var key in new[] { "Sonarr", "Radarr", "Lidarr", "SABnzbd", "qBittorrent" })
            {
                cancellationToken.ThrowIfCancellationRequested();
                try { await live.ForceAsync(key); }
                catch { /* one provider must not suppress the rest of the lifecycle */ }
            }
        }

        var snapshot = new MediaLifecycleSnapshot
        {
            ServerId = server.Id,
            ServerName = server.Name
        };

        var owned = _services.Config.Current.Applications
            .Where(x => x.DiscoveryVerified && x.ServerId == server.Id)
            .Select(x => x.Name)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        snapshot.HasSeerr = owned.Contains("Seerr");
        snapshot.HasBazarr = owned.Contains("Bazarr");
        snapshot.HasTdarr = owned.Contains("Tdarr");
        snapshot.HasLibrary = owned.Contains("Plex") || owned.Contains("Jellyfin") || owned.Contains("Emby");

        var downloads = new List<(string Client, DownloadQueueItem Item)>();
        foreach (var client in new[] { "SABnzbd", "qBittorrent" })
        {
            var clientSnapshot = live.GetDownloadSnapshot(client);
            if (clientSnapshot is null)
                continue;
            downloads.AddRange(clientSnapshot.Queue.Select(x => (client, x)));
        }

        var matchedDownloads = new HashSet<DownloadQueueItem>();
        foreach (var page in new[] { "Sonarr", "Radarr", "Lidarr" })
        {
            foreach (var row in live.GetQueueRows(page))
            {
                if (string.IsNullOrWhiteSpace(row.Title) ||
                    row.Title.Contains("Queue empty", StringComparison.OrdinalIgnoreCase))
                    continue;

                var match = downloads.FirstOrDefault(x => IsMatch(row.Title, x.Item.Name));
                if (match.Item is not null)
                    matchedDownloads.Add(match.Item);

                snapshot.Items.Add(BuildFromArrRow(page, row, match.Item, match.Client));
            }
        }

        foreach (var (client, item) in downloads)
        {
            if (matchedDownloads.Contains(item) || string.IsNullOrWhiteSpace(item.Name))
                continue;

            snapshot.Items.Add(new MediaLifecycleItem
            {
                Title = item.Name,
                MediaType = "Download",
                OwnerApp = client,
                Stage = LifecycleStage.Download,
                State = string.IsNullOrWhiteSpace(item.State) ? "Active" : item.State,
                Progress = string.IsNullOrWhiteSpace(item.Progress) ? "--" : item.Progress,
                Remaining = string.IsNullOrWhiteSpace(item.Eta) ? item.Remaining : item.Eta,
                Detail = string.IsNullOrWhiteSpace(item.Detail)
                    ? $"Active in {client}; no matching Arr queue row is currently visible."
                    : item.Detail,
                NeedsAttention = ContainsAttention(item.State) || ContainsAttention(item.Detail),
                DeepLink = $"page:{client}"
            });
        }

        snapshot.Items = snapshot.Items
            .OrderByDescending(x => x.NeedsAttention)
            .ThenBy(x => x.Stage)
            .ThenBy(x => x.Title)
            .ToList();

        snapshot.SampledAt = DateTimeOffset.Now;
        return snapshot;
    }

    public async Task<IReadOnlyList<RemediationStep>> BuildRemediationAsync(
        ServerProfile? selectedServer,
        EnvironmentOverviewSnapshot environment,
        MediaLifecycleSnapshot? lifecycle = null,
        CancellationToken cancellationToken = default)
    {
        var steps = new List<RemediationStep>();
        var order = 1;

        foreach (var host in environment.Hosts.Where(x => x.State == EnvironmentHealthState.Offline))
        {
            steps.Add(new RemediationStep
            {
                Order = order++,
                Severity = "BLOCKER",
                Component = host.Name,
                Title = $"Restore reachability to {host.Name}",
                Why = $"{host.Apps.Count} verified application(s) depend on this host.",
                NextAction = "Open Servers, verify network/credentials, then re-run environment discovery before touching child services.",
                DeepLink = "page:Servers"
            });
        }

        var impacts = environment.Impacts;
        foreach (var name in new[] { "Prowlarr", "SABnzbd", "qBittorrent", "Sonarr", "Radarr", "Lidarr", "Bazarr", "Tdarr", "Plex" })
        {
            foreach (var impact in impacts.Where(x => x.Component.Equals(name, StringComparison.OrdinalIgnoreCase)))
            {
                var (why, next) = name.ToLowerInvariant() switch
                {
                    "prowlarr" => ("Prowlarr sits upstream of Arr discovery; downstream grab symptoms can originate here.",
                                   "Inspect Prowlarr health/indexers before restarting Sonarr, Radarr or Lidarr."),
                    "sabnzbd" or "qbittorrent" => ("Download clients sit between Arr grabs and import.",
                                                  "Inspect the download client queue and connection state, then validate Arr import."),
                    "sonarr" or "radarr" or "lidarr" => ("The Arr service owns acquisition/import state for this media type.",
                                                        "Open its queue/health detail and resolve the concrete queue item or health message."),
                    "bazarr" => ("Subtitle processing is downstream of import and should not block acquisition.",
                                 "Resolve subtitle provider/missing-item issues after the media item is imported."),
                    "tdarr" => ("Processing/transcoding is downstream of import.",
                                "Inspect node/queue health and avoid restarting acquisition services unless they are independently unhealthy."),
                    "plex" => ("Library playback is the final workflow layer.",
                               "Validate library availability and server health after storage/import dependencies are healthy."),
                    _ => (impact.Impact, "Open the owning component and inspect its detailed telemetry.")
                };

                steps.Add(new RemediationStep
                {
                    Order = order++,
                    Severity = impact.State == EnvironmentHealthState.Offline ? "BLOCKER" : "WARNING",
                    Component = impact.Component,
                    Title = impact.Detail,
                    Why = why,
                    NextAction = next,
                    DeepLink = $"page:{impact.PageKey}"
                });
            }
        }

        if (lifecycle is not null)
        {
            foreach (var item in lifecycle.Items.Where(x => x.NeedsAttention).Take(6))
            {
                if (steps.Any(x => x.Title.Contains(item.Title, StringComparison.OrdinalIgnoreCase)))
                    continue;

                steps.Add(new RemediationStep
                {
                    Order = order++,
                    Severity = item.Stage == LifecycleStage.Import ? "WARNING" : "INFO",
                    Component = item.OwnerApp,
                    Title = $"{item.Title} is blocked at {item.StageText}",
                    Why = string.IsNullOrWhiteSpace(item.Detail) ? "The workflow is not advancing normally." : item.Detail,
                    NextAction = item.Stage == LifecycleStage.Import
                        ? "Inspect the owning Arr queue, then Storage if the import target/path is unavailable."
                        : "Open the owning application and inspect this exact item.",
                    DeepLink = item.DeepLink
                });
            }
        }

        return steps
            .OrderBy(x => x.Severity == "BLOCKER" ? 0 : x.Severity == "WARNING" ? 1 : 2)
            .ThenBy(x => x.Order)
            .Select((x, i) =>
            {
                x.Order = i + 1;
                return x;
            })
            .ToList();
    }

    private static MediaLifecycleItem BuildFromArrRow(
        string page,
        QueueDrillRow row,
        DownloadQueueItem? download,
        string downloadClient)
    {
        var attention = ContainsAttention(row.State) || ContainsAttention(row.Detail);
        var importSignal =
            row.Progress.Contains("100", StringComparison.OrdinalIgnoreCase) &&
            (row.Remaining.Contains("00:00", StringComparison.OrdinalIgnoreCase) ||
             row.Detail.Contains("import", StringComparison.OrdinalIgnoreCase) ||
             row.Detail.Contains("missing", StringComparison.OrdinalIgnoreCase));

        var stage = download is not null
            ? LifecycleStage.Download
            : importSignal
                ? LifecycleStage.Import
                : LifecycleStage.Arr;

        return new MediaLifecycleItem
        {
            Title = row.Title,
            MediaType = page == "Lidarr" ? "Music" : page == "Sonarr" ? "TV" : "Movie",
            OwnerApp = page,
            Stage = stage,
            State = string.IsNullOrWhiteSpace(row.State) ? "Active" : row.State,
            Progress = download?.Progress ?? (string.IsNullOrWhiteSpace(row.Progress) ? "--" : row.Progress),
            Remaining = download?.Eta ?? (string.IsNullOrWhiteSpace(row.Remaining) ? "--" : row.Remaining),
            Detail = download is not null
                ? $"{page} owns the item; {downloadClient} is actively handling the download. {row.Detail}".Trim()
                : row.Detail,
            NeedsAttention = attention,
            DeepLink = $"page:{page}"
        };
    }

    private static bool ContainsAttention(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return false;
        var text = value.ToLowerInvariant();
        return text.Contains("warning") ||
               text.Contains("error") ||
               text.Contains("failed") ||
               text.Contains("missing") ||
               text.Contains("stalled") ||
               text.Contains("blocked");
    }

    private static bool IsMatch(string left, string right)
    {
        var a = Normalize(left);
        var b = Normalize(right);
        if (a.Length < 8 || b.Length < 8)
            return false;
        if (a.Contains(b, StringComparison.Ordinal) || b.Contains(a, StringComparison.Ordinal))
            return true;

        var aTokens = Tokenize(left);
        var bTokens = Tokenize(right);
        var common = aTokens.Intersect(bTokens).Count();
        return common >= Math.Min(4, Math.Min(aTokens.Count, bTokens.Count));
    }

    private static string Normalize(string value) =>
        Regex.Replace(value.ToLowerInvariant(), "[^a-z0-9]+", "");

    private static HashSet<string> Tokenize(string value) =>
        Regex.Split(value.ToLowerInvariant(), "[^a-z0-9]+")
            .Where(x => x.Length >= 3 && !int.TryParse(x, out _))
            .Take(12)
            .ToHashSet(StringComparer.Ordinal);
}
