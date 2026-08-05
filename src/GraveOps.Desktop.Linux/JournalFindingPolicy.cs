using System.Text.RegularExpressions;
using GraveOps.Core.Hosts;

namespace GraveOps.Desktop.Linux;

public static class JournalFindingPolicy
{
    private static readonly string[] ProductTokens =
    {
        "sonarr", "radarr", "lidarr", "prowlarr", "readarr", "whisparr",
        "bazarr", "mylar3", "medusa", "sickchill", "lazylibrarian",
        "recyclarr", "configarr", "profilarr", "cleanuparr", "maintainerr",
        "huntarr", "notifiarr", "autobrr", "unpackerr",
        "plex", "jellyfin", "emby", "navidrome", "audiobookshelf",
        "kavita", "calibreweb", "tautulli", "kometa",
        "jellyseerr", "overseerr", "ombi", "petio",
        "sabnzbd", "qbittorrent", "nzbget", "transmission", "deluge",
        "rutorrent", "dumb", "decypharr", "zurg", "riven", "tdarr",
        "fileflows", "unmanic", "zilean", "rclone", "pihole",
        "adguardhome"
    };

    private static readonly string[] InfrastructureSources =
    {
        "kernel", "dockerd", "docker", "containerd", "ntfs3",
        "ext4fs", "btrfs", "xfs", "zfs", "mdadm", "smartd"
    };

    private static readonly string[] ExternalDesktopSources =
    {
        "rustdesk", "firefox", "firefoxbin", "chromium", "googlechrome",
        "chrome", "discord", "steam", "cinnamon", "muffin", "nemo",
        "xapp", "gnomeshell", "plasmashell", "kwin", "xdgdesktopportal",
        "gnomekeyringdaemon", "gvfsdnetwork", "wireplumber", "pipewire",
        "pulseaudio", "blueman", "nmapplet"
    };

    private static readonly Regex DynamicValuePattern = new(
        @"0x[0-9a-f]+|\b(?:pid|process|thread|uid|gid)[=: ]+\d+\b|\[\d+\]|\b\d{4,}\b",
        RegexOptions.Compiled |
        RegexOptions.CultureInvariant |
        RegexOptions.IgnoreCase);

    private static readonly Regex OccurrencePattern = new(
        @"\s*\(\d+\s+occurrences?\)\s*$",
        RegexOptions.Compiled |
        RegexOptions.CultureInvariant |
        RegexOptions.IgnoreCase);

    private static readonly Regex WhiteSpacePattern = new(
        @"\s+",
        RegexOptions.Compiled |
        RegexOptions.CultureInvariant);

    public static IReadOnlyList<OpsLogGroup> SelectActionable(
        IReadOnlyList<OpsLogGroup> logs,
        HostSnapshot snapshot,
        IReadOnlyList<OpsIntegration> integrations)
    {
        ArgumentNullException.ThrowIfNull(logs);
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(integrations);

        var owned = BuildOwnedIdentities(snapshot, integrations);
        return SelectActionable(logs, owned);
    }

    public static IReadOnlyList<OpsLogGroup> SelectActionable(
        IReadOnlyList<OpsLogGroup> logs,
        IEnumerable<string> ownedIdentities)
    {
        ArgumentNullException.ThrowIfNull(logs);
        ArgumentNullException.ThrowIfNull(ownedIdentities);

        var owned = ownedIdentities
            .Select(Compact)
            .Where(value => value.Length >= 4)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return logs
            .Where(log => log.Severity >= OpsSeverity.Warning)
            .Where(log => ShouldPromote(log, owned))
            .GroupBy(Fingerprint, StringComparer.OrdinalIgnoreCase)
            .Select(group =>
            {
                var newest = group
                    .OrderByDescending(item => item.LastSeen)
                    .First();
                return new OpsLogGroup(
                    group.Max(item => item.Severity),
                    newest.Source,
                    group.Max(item => item.LastSeen),
                    group.Sum(item => Math.Max(1, item.Count)),
                    newest.Message);
            })
            .OrderByDescending(item => item.Severity)
            .ThenByDescending(item => item.LastSeen)
            .ThenBy(item => item.Source, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public static bool ShouldPromote(
        OpsLogGroup log,
        IEnumerable<string> ownedIdentities)
    {
        ArgumentNullException.ThrowIfNull(log);
        ArgumentNullException.ThrowIfNull(ownedIdentities);

        if (log.Severity < OpsSeverity.Warning)
            return false;

        var source = Compact(log.Source);
        var message = log.Message ?? string.Empty;
        var compactMessage = Compact(message);

        if (IsAlwaysIgnored(message))
            return false;

        if (IsExternalDesktopSource(source))
            return false;

        if (IsToolkitNoise(message))
            return false;

        if (InfrastructureSources.Any(item => source.Contains(item, StringComparison.OrdinalIgnoreCase)))
            return true;

        if (IsHostCritical(message))
            return true;

        if (ProductTokens.Any(token =>
                source.Contains(token, StringComparison.OrdinalIgnoreCase) ||
                compactMessage.Contains(token, StringComparison.OrdinalIgnoreCase)))
        {
            return true;
        }

        return ownedIdentities
            .Select(Compact)
            .Where(value => value.Length >= 4)
            .Any(identity =>
                source.Contains(identity, StringComparison.OrdinalIgnoreCase) ||
                compactMessage.Contains(identity, StringComparison.OrdinalIgnoreCase));
    }

    public static string Fingerprint(OpsLogGroup log)
    {
        ArgumentNullException.ThrowIfNull(log);
        var message = OccurrencePattern.Replace(log.Message ?? string.Empty, string.Empty);
        message = DynamicValuePattern.Replace(message, "#");
        message = WhiteSpacePattern.Replace(message.ToLowerInvariant(), " ").Trim();
        return $"{Compact(log.Source)}|{message}";
    }

    private static IReadOnlyList<string> BuildOwnedIdentities(
        HostSnapshot snapshot,
        IReadOnlyList<OpsIntegration> integrations)
    {
        var values = new List<string>();

        foreach (var integration in integrations)
        {
            values.Add(integration.Name);
            values.Add(integration.DisplayName);
            values.Add(integration.InstanceKey);
            values.Add(integration.OwnerKey);
        }

        foreach (var container in snapshot.Containers)
        {
            values.Add(container.Name);
            values.Add(container.Image);
        }

        foreach (var service in snapshot.Services)
        {
            if (ProductTokens.Any(token =>
                    Compact(service.Unit).Contains(token, StringComparison.OrdinalIgnoreCase) ||
                    Compact(service.Description).Contains(token, StringComparison.OrdinalIgnoreCase)))
            {
                values.Add(service.Unit);
                values.Add(service.Description);
            }
        }

        return values
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static bool IsExternalDesktopSource(string source) =>
        ExternalDesktopSources.Any(item =>
            source.Contains(item, StringComparison.OrdinalIgnoreCase));

    private static bool IsAlwaysIgnored(string message) =>
        message.Contains("world-inaccessible", StringComparison.OrdinalIgnoreCase);

    private static bool IsToolkitNoise(string message)
    {
        var lowered = message.ToLowerInvariant();
        if (lowered.Contains("gtk_widget_set_opacity") ||
            lowered.Contains("gtk_is_widget") ||
            lowered.Contains("gtk-critical") ||
            lowered.Contains("gdk-critical") ||
            lowered.Contains("g_object_unref") ||
            lowered.Contains("g_signal_handler"))
        {
            return true;
        }

        return lowered.Contains("assertion") &&
               (lowered.Contains("gtk") ||
                lowered.Contains("gdk") ||
                lowered.Contains("glib") ||
                lowered.Contains("gobject"));
    }

    private static bool IsHostCritical(string message)
    {
        var lowered = message.ToLowerInvariant();
        return lowered.Contains("input/output error") ||
               lowered.Contains("i/o error") ||
               lowered.Contains("read-only file system") ||
               lowered.Contains("filesystem corruption") ||
               lowered.Contains("corrupt filesystem") ||
               lowered.Contains("failed to mount") ||
               lowered.Contains("dependency failed for") ||
               lowered.Contains("out of memory") ||
               lowered.Contains("oom-kill") ||
               lowered.Contains("kernel panic") ||
               lowered.Contains("ext4-fs error") ||
               lowered.Contains("btrfs error") ||
               lowered.Contains("xfs error") ||
               lowered.Contains("ntfs3") && lowered.Contains("error") ||
               lowered.Contains("nvme") && lowered.Contains("error") ||
               lowered.Contains("ata") && lowered.Contains("error");
    }

    private static string Compact(string? value) =>
        Regex.Replace(
            value?.ToLowerInvariant() ?? string.Empty,
            @"[^a-z0-9]+",
            string.Empty,
            RegexOptions.CultureInvariant);
}
