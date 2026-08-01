using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using GraveOps.Core.Hosts;

namespace GraveOps.Desktop.Linux;

public enum OpsSeverity
{
    Healthy = 0,
    Info = 1,
    Warning = 2,
    Error = 3,
    Critical = 4
}

public sealed record OpsFinding(
    OpsSeverity Severity,
    string Component,
    string Problem,
    string Evidence,
    string Impact,
    string NextStep,
    int Rank);

public sealed record OpsAnalysis(
    OpsSeverity Severity,
    string Label,
    string RootCause,
    string Headline,
    IReadOnlyList<OpsFinding> Findings);

public sealed record OpsLifecycleStage(
    int Order,
    string Stage,
    string State,
    OpsSeverity Severity,
    string Evidence,
    string Impact,
    string NextStep);

public sealed record OpsIntegration(
    string Name,
    string Kind,
    string State,
    string Evidence,
    string Endpoint,
    OpsSeverity Severity);

public sealed record OpsLogGroup(
    OpsSeverity Severity,
    string Source,
    DateTimeOffset LastSeen,
    int Count,
    string Message);

public sealed record OpsBackupUnit(
    string Unit,
    string Active,
    string SubState,
    string Enabled,
    string LastRun,
    string NextRun,
    string Path,
    OpsSeverity Severity);

public sealed record OpsBackupArtifact(
    string Path,
    string Size,
    DateTimeOffset ModifiedAt)
{
    public DateTimeOffset LocalModifiedAt =>
        ModifiedAt.ToLocalTime();
}

public sealed record OpsBackupSnapshot(
    OpsSeverity Severity,
    string State,
    string Provider,
    string Summary,
    IReadOnlyList<string> Evidence,
    IReadOnlyList<OpsBackupUnit> Units,
    IReadOnlyList<OpsBackupArtifact> Artifacts);

public sealed record OpsActionResult(
    bool Success,
    string Summary,
    string Output);

public static class LinuxOpsAnalyzer
{
    private static readonly IReadOnlyDictionary<string, int[]> InferredPorts =
        new Dictionary<string, int[]>(StringComparer.OrdinalIgnoreCase)
        {
            ["DUMB"] = new[] { 3005, 5000 },
            ["Sonarr"] = new[] { 8989, 8990 },
            ["Radarr"] = new[] { 7878, 7879 },
            ["Lidarr"] = new[] { 8686 },
            ["Prowlarr"] = new[] { 9696 },
            ["Bazarr"] = new[] { 6767 },
            ["Seerr"] = new[] { 5055 },
            ["SABnzbd"] = new[] { 8080 },
            ["qBittorrent"] = new[] { 6881, 8081 },
            ["Decypharr"] = new[] { 8282 },
            ["Zurg"] = new[] { 18080 },
            ["Tautulli"] = new[] { 8181 },
            ["Tdarr"] = new[] { 8265, 8266 },
            ["FlareSolverr"] = new[] { 8191 }
        };

    public static IReadOnlyList<OpsIntegration> EnrichIntegrations(
        HostSnapshot snapshot)
    {
        var rows = snapshot.Integrations
            .Select(item => new OpsIntegration(
                item.Name,
                item.Kind,
                item.State,
                item.Evidence,
                string.Empty,
                SeverityFromState(item.State)))
            .ToList();

        foreach (var container in snapshot.Containers)
        {
            var containerSeverity = ContainerSeverity(container);
            foreach (var rule in InferredPorts)
            {
                var matched = rule.Value
                    .Where(port => Regex.IsMatch(
                        container.Ports ?? string.Empty,
                        $@"(?<!\d){port}(?!\d)"))
                    .ToArray();

                if (matched.Length == 0)
                    continue;

                rows.Add(new OpsIntegration(
                    rule.Key,
                    "Docker port inference",
                    container.Status,
                    container.Name,
                    string.Join(", ", matched),
                    containerSeverity));
            }
        }

        return rows
            .GroupBy(row => row.Name, StringComparer.OrdinalIgnoreCase)
            .Select(group => group
                .OrderBy(row => row.Severity)
                .ThenByDescending(row => row.Kind.Equals("systemd", StringComparison.OrdinalIgnoreCase))
                .First())
            .OrderBy(row => row.Name)
            .ToArray();
    }

    public static IReadOnlyList<OpsLogGroup> GroupLogs(
        IReadOnlyList<string> lines)
    {
        var parsed = lines
            .Where(line => !string.IsNullOrWhiteSpace(line))
            .Select(ParseLogLine)
            .ToArray();

        return parsed
            .GroupBy(
                item =>
                    $"{CanonicalSource(item.Source)}|" +
                    $"{NormalizeLog(CanonicalSource(item.Source), item.Message)}",
                StringComparer.OrdinalIgnoreCase)
            .Select(group =>
            {
                var newest = group.OrderByDescending(item => item.LastSeen).First();
                return new OpsLogGroup(
                    group.Max(item => item.Severity),
                    CanonicalSource(newest.Source),
                    group.Max(item => item.LastSeen),
                    group.Count(),
                    newest.Message);
            })
            .OrderByDescending(item => item.Severity)
            .ThenByDescending(item => item.LastSeen)
            .Take(80)
            .ToArray();
    }

    public static OpsAnalysis Analyze(
        HostSnapshot snapshot,
        OpsBackupSnapshot backup,
        IReadOnlyList<OpsLogGroup> logs,
        IReadOnlyList<OpsIntegration> integrations)
    {
        var findings = new List<OpsFinding>();

        if (!snapshot.SystemState.Equals("running", StringComparison.OrdinalIgnoreCase))
        {
            findings.Add(new OpsFinding(
                OpsSeverity.Error,
                "Host",
                $"systemd reports '{snapshot.SystemState}'.",
                $"Kernel {snapshot.Kernel}; {snapshot.Uptime}",
                "Host degradation can affect every service and media workflow.",
                "Inspect failed units and the warning journal before touching child applications.",
                0));
        }

        foreach (var volume in OperationalStorage(snapshot)
                     .OrderByDescending(item => UsePercent(item.PercentUsed)))
        {
            var percent = UsePercent(volume.PercentUsed);
            var volumeSeverity = StorageSeverity(percent);
            if (volumeSeverity < OpsSeverity.Warning)
                continue;

            findings.Add(new OpsFinding(
                volumeSeverity,
                "Storage",
                $"{volume.MountPoint} is {percent}% full.",
                $"{volume.Source} · {volume.Used} used · {volume.Available} free · {volume.FileSystem}",
                "Low free space can block downloads, imports, databases, transcodes and backups.",
                percent >= 95
                    ? "Free space immediately or move data before continuing media operations."
                    : "Plan cleanup or expansion now and inspect the largest consumers.",
                1));
        }

        foreach (var failed in snapshot.FailedUnits.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            findings.Add(new OpsFinding(
                OpsSeverity.Error,
                "systemd",
                $"Failed unit: {failed}",
                failed,
                "The owning service and dependent applications can be unavailable.",
                "Inspect the unit and its journal, then use guarded restart only after finding the cause.",
                2));
        }

        foreach (var service in UniqueServices(snapshot)
                     .Where(service => ServiceSeverity(service) >= OpsSeverity.Warning))
        {
            findings.Add(new OpsFinding(
                ServiceSeverity(service),
                service.Unit,
                $"{service.Description} is {service.ActiveState}/{service.SubState}.",
                $"Unit file: {service.UnitFileState}",
                DependencyImpact(service.Unit),
                "Inspect service logs and dependencies before restarting it.",
                DependencyRank(service.Unit)));
        }

        foreach (var container in snapshot.Containers
                     .Where(container => ContainerSeverity(container) >= OpsSeverity.Warning))
        {
            findings.Add(new OpsFinding(
                ContainerSeverity(container),
                container.Name,
                $"Container is {container.State}: {container.Status}",
                $"Image {container.Image}",
                DependencyImpact(container.Name),
                "Inspect Docker logs, storage and network dependencies before restarting it.",
                DependencyRank(container.Name)));
        }

        foreach (var log in logs.Where(log => log.Severity >= OpsSeverity.Warning).Take(8))
        {
            if (log.Message.Contains("world-inaccessible", StringComparison.OrdinalIgnoreCase))
                continue;

            findings.Add(new OpsFinding(
                log.Message.Contains("dumped core", StringComparison.OrdinalIgnoreCase)
                    ? OpsSeverity.Warning
                    : log.Severity,
                log.Source,
                log.Count > 1 ? $"{log.Message} ({log.Count} occurrences)" : log.Message,
                $"Last seen {log.LastSeen.LocalDateTime:g}",
                log.Message.Contains("dumped core", StringComparison.OrdinalIgnoreCase)
                    ? "A process crash occurred and may indicate an application or runtime defect."
                    : "A recurring journal warning can reveal an unresolved configuration or runtime problem.",
                "Open Logs and resolve the newest unique event first.",
                8));
        }

        if (backup.Severity >= OpsSeverity.Warning)
        {
            findings.Add(new OpsFinding(
                backup.Severity,
                "Backups",
                backup.Summary,
                string.Join(" · ", backup.Evidence.Take(4)),
                "Missing or stale backups increase recovery risk.",
                "Open Backups and verify schedules, recent artifacts and restore-test coverage.",
                9));
        }

        foreach (var warning in snapshot.Warnings.Take(6))
        {
            findings.Add(new OpsFinding(
                OpsSeverity.Warning,
                "Provider",
                warning,
                warning,
                "Incomplete telemetry can hide an operational issue.",
                "Verify command availability and current-user permissions.",
                10));
        }

        var ordered = findings
            .GroupBy(item => $"{item.Component}|{item.Problem}", StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .OrderByDescending(item => item.Severity)
            .ThenBy(item => item.Rank)
            .ThenBy(item => item.Component)
            .ToArray();

        var actionable = ordered.Where(item => item.Severity >= OpsSeverity.Warning).ToArray();
        if (actionable.Length == 0)
        {
            return new OpsAnalysis(
                OpsSeverity.Healthy,
                "HEALTHY",
                "No active fault detected",
                "No host, storage, service, container, journal or backup finding requires attention.",
                ordered);
        }

        var top = actionable[0];
        var severity = actionable.Max(item => item.Severity);
        return new OpsAnalysis(
            severity,
            SeverityLabel(severity),
            top.Component,
            $"Highest-priority finding: {top.Component} — {top.Problem}",
            ordered);
    }

    public static IReadOnlyList<OpsLifecycleStage> BuildLifecycle(
        HostSnapshot snapshot,
        IReadOnlyList<OpsIntegration> integrations,
        OpsAnalysis analysis)
    {
        return new[]
        {
            new OpsLifecycleStage(
                1,
                "Host",
                snapshot.SystemState.Equals("running", StringComparison.OrdinalIgnoreCase) ? "READY" : "DEGRADED",
                snapshot.SystemState.Equals("running", StringComparison.OrdinalIgnoreCase) ? OpsSeverity.Healthy : OpsSeverity.Error,
                $"{snapshot.Hostname} · systemd {snapshot.SystemState}",
                "Every media workflow depends on host reachability and runtime health.",
                "Resolve host and failed-service findings before changing media applications."),
            StorageStage(snapshot),
            IntegrationStage(3, "Requests", integrations, new[] { "Seerr" }, false,
                "Request management feeds acquisition but is optional.",
                "Configure Seerr/Jellyseerr only when request intake is part of this environment."),
            IntegrationStage(4, "Discovery", integrations, new[] { "Prowlarr" }, false,
                "Indexer discovery sits upstream of the Arr applications.",
                "Inspect Prowlarr and indexers before restarting downstream Arr services."),
            IntegrationStage(5, "Acquisition", integrations, new[] { "Sonarr", "Radarr", "Lidarr" }, true,
                "Arr applications own release selection and import state.",
                "Inspect the owning Arr queue and health messages for the affected media type."),
            IntegrationStage(6, "Downloads", integrations, new[] { "SABnzbd", "qBittorrent" }, true,
                "Download clients sit between grabs and imports.",
                "Inspect queue, connectivity and free space before changing Arr services."),
            IntegrationStage(7, "Processing", integrations,
                new[] { "Decypharr", "Bazarr", "Tdarr", "Unpackerr", "Recyclarr" }, false,
                "Processing and policy tools are downstream or supporting layers.",
                "Resolve processing after acquisition and storage dependencies are healthy."),
            IntegrationStage(8, "Library", integrations, new[] { "Plex", "Jellyfin", "Emby" }, true,
                "The library server is the final availability and playback layer.",
                "Validate storage and imports before restarting the library server.")
        };
    }

    public static IReadOnlyList<StorageVolumeSnapshot> OperationalStorage(HostSnapshot snapshot) =>
        snapshot.Storage
            .Where(item => !item.FileSystem.Equals("efivarfs", StringComparison.OrdinalIgnoreCase))
            .ToArray();

    public static IReadOnlyList<ServiceSnapshot> UniqueServices(HostSnapshot snapshot) =>
        snapshot.Services
            .GroupBy(item => item.Unit, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .OrderBy(item => item.Unit)
            .ToArray();

    public static int UsePercent(string value) =>
        int.TryParse((value ?? string.Empty).Trim().TrimEnd('%'), out var parsed) ? parsed : 0;

    public static OpsSeverity StorageSeverity(int percent) => percent switch
    {
        >= 95 => OpsSeverity.Critical,
        >= 90 => OpsSeverity.Error,
        >= 85 => OpsSeverity.Warning,
        _ => OpsSeverity.Healthy
    };

    public static string SeverityLabel(OpsSeverity severity) =>
        severity switch
        {
            OpsSeverity.Critical => "CRITICAL",
            OpsSeverity.Error => "ERROR",
            OpsSeverity.Warning => "ATTENTION",
            OpsSeverity.Info => "INFO",
            _ => "HEALTHY"
        };

    public static OpsSeverity ServiceSeverity(ServiceSnapshot service)
    {
        if (service.ActiveState.Equals("active", StringComparison.OrdinalIgnoreCase))
            return OpsSeverity.Healthy;
        if (service.ActiveState.Equals("failed", StringComparison.OrdinalIgnoreCase))
            return OpsSeverity.Error;
        if (service.UnitFileState.Equals("disabled", StringComparison.OrdinalIgnoreCase))
            return OpsSeverity.Info;
        return OpsSeverity.Warning;
    }

    public static OpsSeverity ContainerSeverity(DockerContainerSnapshot container)
    {
        if (container.State.Equals("running", StringComparison.OrdinalIgnoreCase))
            return OpsSeverity.Healthy;
        if (container.Status.Contains("Exited (0)", StringComparison.OrdinalIgnoreCase))
            return OpsSeverity.Info;
        return container.Status.Contains("Exited", StringComparison.OrdinalIgnoreCase)
            ? OpsSeverity.Error
            : OpsSeverity.Warning;
    }

    private static OpsLifecycleStage StorageStage(HostSnapshot snapshot)
    {
        var fullest = OperationalStorage(snapshot)
            .OrderByDescending(item => UsePercent(item.PercentUsed))
            .FirstOrDefault();

        if (fullest is null)
        {
            return new OpsLifecycleStage(2, "Storage", "UNKNOWN", OpsSeverity.Warning,
                "No operational filesystems were returned.",
                "Downloads, imports, libraries and backups require visible storage.",
                "Open Storage and verify mounts before continuing.");
        }

        var percent = UsePercent(fullest.PercentUsed);
        var severity = StorageSeverity(percent);
        return new OpsLifecycleStage(2, "Storage",
            severity >= OpsSeverity.Error ? "BLOCKED" : severity == OpsSeverity.Warning ? "ATTENTION" : "READY",
            severity,
            $"{fullest.MountPoint} is the fullest mount at {percent}% ({fullest.Available} free).",
            "Every downstream stage reads from or writes to storage.",
            severity >= OpsSeverity.Warning
                ? "Free space or expand the affected mount before queue growth creates an outage."
                : "No storage-capacity blocker is detected.");
    }

    private static OpsLifecycleStage IntegrationStage(
        int order,
        string stage,
        IReadOnlyList<OpsIntegration> integrations,
        IReadOnlyCollection<string> names,
        bool required,
        string impact,
        string nextStep)
    {
        var matches = integrations
            .Where(item => names.Contains(item.Name, StringComparer.OrdinalIgnoreCase))
            .ToArray();

        if (matches.Length == 0)
        {
            return new OpsLifecycleStage(order, stage,
                required ? "NOT DETECTED" : "NOT CONFIGURED",
                required ? OpsSeverity.Warning : OpsSeverity.Info,
                $"No verified {stage.ToLowerInvariant()} integration was detected.",
                impact,
                nextStep);
        }

        var severity = matches.Max(item => item.Severity);
        return new OpsLifecycleStage(order, stage,
            severity >= OpsSeverity.Error ? "BLOCKED" : severity == OpsSeverity.Warning ? "DEGRADED" : "READY",
            severity,
            string.Join(" · ", matches.Select(item => $"{item.Name}: {item.State} ({item.Evidence})")),
            impact,
            nextStep);
    }

    private static OpsLogGroup ParseLogLine(string line)
    {
        var message = line.Trim();
        var source = "journal";
        var timestamp = DateTimeOffset.Now;

        var firstSpace = message.IndexOf(' ');
        if (firstSpace > 0 && DateTimeOffset.TryParse(message[..firstSpace], out var parsed))
        {
            timestamp = parsed;
            message = message[(firstSpace + 1)..].Trim();
        }

        var sourceMatch = Regex.Match(message, @"\s([A-Za-z0-9_.@-]+)(?:\[\d+\])?:\s");
        if (sourceMatch.Success)
            source = sourceMatch.Groups[1].Value;

        var canonicalSource = CanonicalSource(source);
        var lowered = message.ToLowerInvariant();
        var severity = IsDesktopSessionObservation(
                canonicalSource,
                message)
            ? OpsSeverity.Info
            : lowered.Contains("world-inaccessible")
                ? OpsSeverity.Info
                : lowered.Contains("dumped core") ||
                  lowered.Contains("core dump") ||
                  lowered.Contains("segfault")
                    ? OpsSeverity.Error
                    : OpsSeverity.Warning;

        return new OpsLogGroup(
            severity,
            canonicalSource,
            timestamp,
            1,
            message);
    }

    private static string CanonicalSource(string value)
    {
        var source = value.Trim();

        if (source.StartsWith(
                "gnome-keyring",
                StringComparison.OrdinalIgnoreCase))
        {
            return "gnome-keyring-daemon";
        }

        if (source.StartsWith(
                "xdg-desktop-por",
                StringComparison.OrdinalIgnoreCase))
        {
            return "xdg-desktop-portal";
        }

        if (source.StartsWith(
                "gvfsd-network",
                StringComparison.OrdinalIgnoreCase))
        {
            return "gvfsd-network";
        }

        return source;
    }

    private static string NormalizeLog(
        string source,
        string value)
    {
        var lowered = value.ToLowerInvariant();

        if (source.Equals(
                "gnome-keyring-daemon",
                StringComparison.OrdinalIgnoreCase) &&
            lowered.Contains("assertion"))
        {
            return "gnome-keyring assertion family";
        }

        if (source.Equals(
                "xdg-desktop-portal",
                StringComparison.OrdinalIgnoreCase) &&
            lowered.Contains("application id not specified"))
        {
            return "xdg desktop portal missing application id";
        }

        if (source.Equals(
                "gvfsd-network",
                StringComparison.OrdinalIgnoreCase) &&
            (lowered.Contains("wsdd") ||
             lowered.Contains("automount failed") ||
             lowered.Contains("directory monitor")))
        {
            return "gvfs network discovery helper unavailable";
        }

        if (lowered.Contains("world-inaccessible"))
            return "systemd unit world-inaccessible";

        if (lowered.Contains("dumped core") ||
            lowered.Contains("core dump") ||
            lowered.Contains("segfault"))
        {
            return "process crash or core dump";
        }

        var text = Regex.Replace(
            value,
            @"0x[0-9a-f]+",
            "0x#",
            RegexOptions.IgnoreCase);

        text = Regex.Replace(
            text,
            @"\b\d{5,}\b",
            "#");

        return text.Trim();
    }

    private static bool IsDesktopSessionObservation(
        string source,
        string message)
    {
        var lowered = message.ToLowerInvariant();

        if (source.Equals(
                "xdg-desktop-portal",
                StringComparison.OrdinalIgnoreCase) &&
            (lowered.Contains("application id not specified") ||
             lowered.Contains("backend call failed")))
        {
            return true;
        }

        if (source.Equals(
                "gnome-keyring-daemon",
                StringComparison.OrdinalIgnoreCase) &&
            lowered.Contains("assertion"))
        {
            return true;
        }

        if (source.Equals(
                "gvfsd-network",
                StringComparison.OrdinalIgnoreCase) &&
            (lowered.Contains("wsdd") ||
             lowered.Contains("automount failed") ||
             lowered.Contains("directory monitor")))
        {
            return true;
        }

        return false;
    }

    private static OpsSeverity SeverityFromState(string state)
    {
        var text = state.ToLowerInvariant();
        if (text.Contains("running") || text.Contains("active") || text.Contains("healthy"))
            return OpsSeverity.Healthy;
        if (text.Contains("failed") || text.Contains("error") || text.Contains("unhealthy"))
            return OpsSeverity.Error;
        if (text.Contains("exited") || text.Contains("inactive") || text.Contains("degraded"))
            return text.Contains("exited (0)") ? OpsSeverity.Info : OpsSeverity.Warning;
        return OpsSeverity.Info;
    }

    private static int DependencyRank(string component)
    {
        var name = component.ToLowerInvariant();
        if (name.Contains("host")) return 0;
        if (name.Contains("storage") || name.Contains("mount")) return 1;
        if (name.Contains("docker") || name.Contains("containerd")) return 2;
        if (name.Contains("prowlarr")) return 3;
        if (name.Contains("sab") || name.Contains("qbittorrent")) return 4;
        if (name.Contains("sonarr") || name.Contains("radarr") || name.Contains("lidarr")) return 5;
        if (name.Contains("plex") || name.Contains("jellyfin") || name.Contains("emby")) return 7;
        return 8;
    }

    private static string DependencyImpact(string component)
    {
        var name = component.ToLowerInvariant();
        if (name.Contains("prowlarr")) return "Discovery failures can block multiple Arr applications.";
        if (name.Contains("sab") || name.Contains("qbittorrent")) return "Downloads can stop between grabs and imports.";
        if (name.Contains("sonarr") || name.Contains("radarr") || name.Contains("lidarr")) return "Acquisition and import can stop for the owning media type.";
        if (name.Contains("plex") || name.Contains("jellyfin") || name.Contains("emby")) return "Library availability and playback can be affected.";
        if (name.Contains("docker") || name.Contains("containerd")) return "Every containerized application can be affected.";
        return "Dependent host and media workflows may be degraded.";
    }
}

public sealed class LinuxBackupProbe
{
    public async Task<OpsBackupSnapshot> CaptureAsync(CancellationToken cancellationToken = default)
    {
        var units = await CaptureUnitsAsync(cancellationToken);
        var artifacts = await CaptureArtifactsAsync(cancellationToken);
        var tools = new[] { "restic", "borg", "rclone", "duplicity", "rsnapshot", "rsync" }
            .Where(CommandExists)
            .ToArray();

        var evidence = new List<string>();
        if (tools.Length > 0)
            evidence.Add($"Tools: {string.Join(", ", tools)}");
        evidence.AddRange(units.Select(item => $"{item.Unit}: {item.Active}/{item.SubState}, {item.Enabled}"));
        if (artifacts.FirstOrDefault() is { } newest)
        {
            evidence.Add(
                $"Newest verified artifact: {newest.Path} · " +
                $"{newest.LocalModifiedAt.LocalDateTime:g}");
        }

        var timers = units
            .Where(item =>
                item.Unit.EndsWith(
                    ".timer",
                    StringComparison.OrdinalIgnoreCase))
            .ToArray();

        var activeTimers = timers
            .Where(item =>
                item.Active.Equals(
                    "active",
                    StringComparison.OrdinalIgnoreCase))
            .ToArray();

        var latest = artifacts.FirstOrDefault();

        if (timers.Length == 0 &&
            artifacts.Count == 0)
        {
            var summary = tools.Length == 0
                ? "No configured backup schedule or verified backup artifact was detected."
                : "Backup-capable tools are installed, but no configured schedule or verified artifact was detected.";

            return new OpsBackupSnapshot(
                OpsSeverity.Info,
                "NOT CONFIGURED",
                tools.Length == 0
                    ? "No provider detected"
                    : string.Join(" / ", tools),
                summary,
                evidence,
                units,
                artifacts);
        }

        if (timers.Length == 0)
        {
            return new OpsBackupSnapshot(
                OpsSeverity.Warning,
                "ARTIFACTS / UNVERIFIED",
                "No matching schedule",
                "Relevant-looking artifacts were found, but no matching backup timer was detected.",
                evidence,
                units,
                artifacts);
        }

        if (activeTimers.Length == 0)
        {
            return new OpsBackupSnapshot(
                OpsSeverity.Error,
                "ATTENTION",
                "systemd timers",
                "Media-configuration backup timers exist, but none are active.",
                evidence,
                units,
                artifacts);
        }

        if (timers.Any(item =>
                item.Enabled.Equals(
                    "disabled",
                    StringComparison.OrdinalIgnoreCase)))
        {
            return new OpsBackupSnapshot(
                OpsSeverity.Warning,
                "ATTENTION",
                "systemd timers",
                "At least one media-configuration backup timer is disabled.",
                evidence,
                units,
                artifacts);
        }

        if (latest is null)
        {
            return new OpsBackupSnapshot(
                OpsSeverity.Warning,
                "SCHEDULED / UNVERIFIED",
                "systemd timers",
                "Backup scheduling is active, but no verified media-configuration artifact was found.",
                evidence,
                units,
                artifacts);
        }

        var age =
            DateTimeOffset.Now -
            latest.LocalModifiedAt;

        if (age > TimeSpan.FromDays(14))
        {
            return new OpsBackupSnapshot(
                OpsSeverity.Warning,
                "STALE",
                "systemd timers",
                $"The newest verified backup artifact is {Math.Floor(age.TotalDays)} days old.",
                evidence,
                units,
                artifacts);
        }

        return new OpsBackupSnapshot(
            OpsSeverity.Healthy,
            "READY",
            "systemd timers",
            $"Backup scheduling is active and the newest verified artifact is {RelativeAge(age)} old.",
            evidence,
            units,
            artifacts);
    }

    private static async Task<IReadOnlyList<OpsBackupUnit>> CaptureUnitsAsync(CancellationToken cancellationToken)
    {
        var known = new[]
        {
            "media-config-backup.timer",
            "media-config-backup-maintenance.timer",
            "media-config-backup-restore-test.timer"
        };

        var discovered = (await RunAsync("systemctl",
            new[] { "list-unit-files", "--type=timer", "--no-legend", "--no-pager" }, cancellationToken))
            .Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(line => line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault())
            .Where(unit =>
                !string.IsNullOrWhiteSpace(unit) &&
                IsRelevantBackupUnit(unit!))
            .Cast<string>();

        var rows = new List<OpsBackupUnit>();
        foreach (var unit in known.Concat(discovered).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var output = await RunAsync("systemctl", new[]
            {
                "show", unit, "--no-pager",
                "--property=Id", "--property=Description", "--property=LoadState",
                "--property=ActiveState", "--property=SubState", "--property=UnitFileState",
                "--property=LastTriggerUSec", "--property=NextElapseUSecRealtime", "--property=FragmentPath"
            }, cancellationToken);

            var values = ParseProperties(output);
            if (Value(values, "LoadState", "not-found").Equals("not-found", StringComparison.OrdinalIgnoreCase))
                continue;

            var active = Value(values, "ActiveState", "unknown");
            var enabled = Value(values, "UnitFileState", "unknown");
            var severity = active.Equals("active", StringComparison.OrdinalIgnoreCase)
                ? enabled.Equals("disabled", StringComparison.OrdinalIgnoreCase) ? OpsSeverity.Warning : OpsSeverity.Healthy
                : OpsSeverity.Warning;

            rows.Add(new OpsBackupUnit(
                Value(values, "Id", unit),
                active,
                Value(values, "SubState", "unknown"),
                enabled,
                Value(values, "LastTriggerUSec", "--"),
                Value(values, "NextElapseUSecRealtime", "--"),
                Value(values, "FragmentPath", "--"),
                severity));
        }

        return rows.OrderBy(item => item.Unit).ToArray();
    }

    private static async Task<IReadOnlyList<OpsBackupArtifact>> CaptureArtifactsAsync(CancellationToken cancellationToken)
    {
        var roots = new[]
        {
            "/var/backups",
            "/opt/dumb",
            "/opt/recyclarr",
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)
        }.Where(Directory.Exists).Distinct().ToArray();

        if (roots.Length == 0)
            return Array.Empty<OpsBackupArtifact>();

        var args = new List<string>(roots);
        args.AddRange(new[]
        {
            "-maxdepth", "6", "-type", "f", "-path", "*backup*", "-mtime", "-30",
            "-printf", "%T@\t%s\t%p\n"
        });

        var output = await RunAsync("find", args, cancellationToken);
        var rows = new List<OpsBackupArtifact>();
        foreach (var line in output.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            var columns = line.Split('\t', 3);
            if (columns.Length != 3 ||
                !double.TryParse(columns[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var epoch))
                continue;

            var path = columns[2];

            if (!IsRelevantArtifactPath(path))
                continue;

            long.TryParse(columns[1], out var bytes);
            rows.Add(new OpsBackupArtifact(
                path,
                FormatBytes(bytes),
                DateTimeOffset
                    .FromUnixTimeMilliseconds(
                        (long)(epoch * 1000))
                    .ToLocalTime()));
        }

        return rows.OrderByDescending(item => item.ModifiedAt).Take(50).ToArray();
    }

    private static bool CommandExists(string command) =>
        File.Exists($"/usr/bin/{command}") || File.Exists($"/usr/local/bin/{command}");

    private static bool IsRelevantBackupUnit(string value)
    {
        var text = value.ToLowerInvariant();

        if (text.StartsWith("media-config-backup"))
            return true;

        return text.Contains("restic") ||
               text.Contains("borg") ||
               text.Contains("rclone") ||
               text.Contains("duplicity") ||
               text.Contains("rsnapshot");
    }

    private static bool IsRelevantArtifactPath(string value)
    {
        var path = value
            .Replace(
                Path.DirectorySeparatorChar,
                '/')
            .ToLowerInvariant();

        string[] exclusions =
        {
            "/.mozilla/",
            "/.cache/",
            "/.git/",
            "/.local/share/trash/",
            "/downloads/",
            "/graveops-linux-operational-parity-backups/",
            "/graveops-linux-parity-backups/",
            "/graveops-linux-checkout-artifacts/",
            "/graveops-linux-trust-calibration-backups/",
            "/sessionstore-backups/"
        };

        if (exclusions.Any(path.Contains))
            return false;

        var fileName =
            Path.GetFileName(path);

        var isArchive =
            path.EndsWith(".tar") ||
            path.EndsWith(".tar.gz") ||
            path.EndsWith(".tgz") ||
            path.EndsWith(".zip") ||
            path.EndsWith(".zst") ||
            path.EndsWith(".bz2") ||
            path.EndsWith(".xz");

        if (path.Contains("media-config-backup") ||
            path.Contains("/media-config/"))
        {
            return true;
        }

        if (path.Contains("/var/backups/") &&
            (fileName.Contains("media-config") ||
             fileName.Contains("restic") ||
             fileName.Contains("borg") ||
             fileName.Contains("rclone") ||
             fileName.Contains("snapshot") ||
             (fileName.Contains("backup") &&
              isArchive)))
        {
            return true;
        }

        if ((path.Contains("/backup/") ||
             path.Contains("/backups/")) &&
            isArchive)
        {
            return true;
        }

        return false;
    }

    private static IReadOnlyDictionary<string, string> ParseProperties(string output) =>
        output.Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(line => line.Split('=', 2))
            .Where(parts => parts.Length == 2)
            .GroupBy(parts => parts[0], StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.Last()[1], StringComparer.OrdinalIgnoreCase);

    private static string Value(IReadOnlyDictionary<string, string> values, string key, string fallback) =>
        values.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value) ? value : fallback;

    private static async Task<string> RunAsync(
        string executable,
        IReadOnlyList<string> arguments,
        CancellationToken cancellationToken)
    {
        try
        {
            using var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = executable,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                }
            };
            foreach (var argument in arguments)
                process.StartInfo.ArgumentList.Add(argument);
            process.Start();
            var stdout = process.StandardOutput.ReadToEndAsync(cancellationToken);
            await process.WaitForExitAsync(cancellationToken);
            return (await stdout).Trim();
        }
        catch
        {
            return string.Empty;
        }
    }

    private static string FormatBytes(long bytes)
    {
        string[] units = { "B", "KiB", "MiB", "GiB", "TiB" };
        var value = (double)Math.Max(0, bytes);
        var index = 0;
        while (value >= 1024 && index < units.Length - 1)
        {
            value /= 1024;
            index++;
        }
        return $"{value:0.##} {units[index]}";
    }

    private static string RelativeAge(TimeSpan age) =>
        age.TotalHours < 1 ? $"{Math.Max(1, Math.Round(age.TotalMinutes))} minutes" :
        age.TotalDays < 1 ? $"{Math.Round(age.TotalHours, 1)} hours" :
        $"{Math.Round(age.TotalDays, 1)} days";
}

public sealed class LinuxHostActionService
{
    private static readonly Regex SafeIdentifier =
        new(@"^[A-Za-z0-9_.@:-]+$", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public Task<OpsActionResult> ServiceAsync(string unit, string action, CancellationToken cancellationToken = default)
    {
        Validate(unit);
        ValidateAction(action);
        return ExecuteAsync("pkexec", new[] { "systemctl", action, unit }, $"{action} {unit}", cancellationToken);
    }

    public Task<OpsActionResult> ContainerAsync(string container, string action, CancellationToken cancellationToken = default)
    {
        Validate(container);
        ValidateAction(action);
        return ExecuteAsync("docker", new[] { action, container }, $"{action} container {container}", cancellationToken);
    }

    private static async Task<OpsActionResult> ExecuteAsync(
        string executable,
        IReadOnlyList<string> arguments,
        string summary,
        CancellationToken cancellationToken)
    {
        try
        {
            using var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = executable,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = false
                }
            };
            foreach (var argument in arguments)
                process.StartInfo.ArgumentList.Add(argument);
            process.Start();
            var stdout = process.StandardOutput.ReadToEndAsync(cancellationToken);
            var stderr = process.StandardError.ReadToEndAsync(cancellationToken);
            await process.WaitForExitAsync(cancellationToken);
            var output = string.Join(Environment.NewLine,
                new[] { (await stdout).Trim(), (await stderr).Trim() }.Where(value => !string.IsNullOrWhiteSpace(value)));
            return new OpsActionResult(process.ExitCode == 0,
                process.ExitCode == 0 ? $"{summary} completed." : $"{summary} failed with exit code {process.ExitCode}.",
                output);
        }
        catch (Exception exception)
        {
            return new OpsActionResult(false, $"{summary} could not be started.", exception.Message);
        }
    }

    private static void Validate(string value)
    {
        if (string.IsNullOrWhiteSpace(value) || !SafeIdentifier.IsMatch(value))
            throw new ArgumentException("Unsafe host-action identifier.", nameof(value));
    }

    private static void ValidateAction(string action)
    {
        if (action is not ("start" or "stop" or "restart"))
            throw new ArgumentOutOfRangeException(nameof(action));
    }
}

public sealed class OpsHistoryRecord
{
    public DateTimeOffset Timestamp { get; set; } = DateTimeOffset.Now;
    public OpsSeverity Severity { get; set; } = OpsSeverity.Info;
    public string Component { get; set; } = string.Empty;
    public string FromState { get; set; } = string.Empty;
    public string ToState { get; set; } = string.Empty;
    public string Detail { get; set; } = string.Empty;
}

public sealed class LinuxHistoryStore
{
    private readonly string _filePath;
    private readonly JsonSerializerOptions _json = new() { WriteIndented = true };
    private HistoryDocument _document;

    public LinuxHistoryStore()
    {
        var root = Environment.GetEnvironmentVariable("XDG_DATA_HOME");
        if (string.IsNullOrWhiteSpace(root))
            root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".local", "share");
        _filePath = Path.Combine(root, "GraveOps", "fleet-history.json");
        _document = Load();
    }

    public IReadOnlyList<OpsHistoryRecord> Records =>
        _document.Records.OrderByDescending(item => item.Timestamp).ToArray();

    public void Record(
        HostSnapshot snapshot,
        OpsAnalysis analysis,
        IReadOnlyList<OpsLifecycleStage> lifecycle,
        OpsBackupSnapshot backup,
        Func<StorageVolumeSnapshot, OpsSeverity>? storageSeverity = null)
    {
        var states = new Dictionary<string, StateValue>(StringComparer.OrdinalIgnoreCase)
        {
            ["environment"] = new("Environment", analysis.Label, analysis.Severity, analysis.Headline),
            ["backups"] = new("Backups", backup.State, backup.Severity, backup.Summary)
        };

        foreach (var volume in LinuxOpsAnalyzer.OperationalStorage(snapshot))
        {
            var severity = storageSeverity?.Invoke(volume) ??
                LinuxOpsAnalyzer.StorageSeverity(
                    LinuxOpsAnalyzer.UsePercent(volume.PercentUsed));
            states[$"storage:{volume.MountPoint}"] = new StateValue(
                $"Storage {volume.MountPoint}",
                LinuxOpsAnalyzer.SeverityLabel(severity),
                severity,
                $"{volume.PercentUsed} used; {volume.Available} free.");
        }

        foreach (var service in LinuxOpsAnalyzer.UniqueServices(snapshot))
            states[$"service:{service.Unit}"] = new StateValue(service.Unit,
                $"{service.ActiveState}/{service.SubState}", LinuxOpsAnalyzer.ServiceSeverity(service), service.Description);

        foreach (var container in snapshot.Containers)
            states[$"container:{container.Name}"] = new StateValue(container.Name, container.State,
                LinuxOpsAnalyzer.ContainerSeverity(container), container.Status);

        foreach (var stage in lifecycle)
            states[$"lifecycle:{stage.Stage}"] = new StateValue($"Lifecycle {stage.Stage}", stage.State, stage.Severity, stage.Evidence);

        if (_document.LastStates.Count == 0)
        {
            _document.LastStates = states.ToDictionary(item => item.Key, item => item.Value.State, StringComparer.OrdinalIgnoreCase);
            Add(OpsSeverity.Info, "Environment", string.Empty, analysis.Label, "Initial Linux control-plane baseline captured.");
            Save();
            return;
        }

        foreach (var item in states)
        {
            if (!_document.LastStates.TryGetValue(item.Key, out var previous))
            {
                _document.LastStates[item.Key] = item.Value.State;
                Add(OpsSeverity.Info, item.Value.Component, "NOT OBSERVED", item.Value.State, item.Value.Detail);
                continue;
            }
            if (previous.Equals(item.Value.State, StringComparison.OrdinalIgnoreCase))
                continue;
            _document.LastStates[item.Key] = item.Value.State;
            Add(item.Value.Severity, item.Value.Component, previous, item.Value.State, item.Value.Detail);
        }
        Save();
    }

    public void RecordAction(string component, string action, OpsActionResult result)
    {
        Add(result.Success ? OpsSeverity.Info : OpsSeverity.Error, component, "ACTION",
            result.Success ? "COMPLETED" : "FAILED", $"{action}: {result.Summary} {result.Output}".Trim());
        Save();
    }

    public void RecordPolicy(
        string component,
        string policyState,
        string detail)
    {
        Add(
            OpsSeverity.Info,
            component,
            "POLICY",
            policyState,
            detail);
        Save();
    }

    public void Clear()
    {
        _document = new HistoryDocument();
        Save();
    }

    private void Add(OpsSeverity severity, string component, string from, string to, string detail)
    {
        _document.Records.Insert(0, new OpsHistoryRecord
        {
            Timestamp = DateTimeOffset.Now,
            Severity = severity,
            Component = component,
            FromState = from,
            ToState = to,
            Detail = detail
        });
        while (_document.Records.Count > 750)
            _document.Records.RemoveAt(_document.Records.Count - 1);
    }

    private HistoryDocument Load()
    {
        try
        {
            if (!File.Exists(_filePath)) return new HistoryDocument();
            return JsonSerializer.Deserialize<HistoryDocument>(File.ReadAllText(_filePath), _json) ?? new HistoryDocument();
        }
        catch { return new HistoryDocument(); }
    }

    private void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_filePath)!);
            var temp = _filePath + ".tmp";
            File.WriteAllText(temp, JsonSerializer.Serialize(_document, _json), new UTF8Encoding(false));
            File.Move(temp, _filePath, true);
        }
        catch { }
    }

    private sealed class HistoryDocument
    {
        public Dictionary<string, string> LastStates { get; set; } = new(StringComparer.OrdinalIgnoreCase);
        public List<OpsHistoryRecord> Records { get; set; } = new();
    }

    private sealed record StateValue(string Component, string State, OpsSeverity Severity, string Detail);
}
