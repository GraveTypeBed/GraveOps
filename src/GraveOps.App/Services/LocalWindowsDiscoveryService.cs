using System.Diagnostics;
using System.Net;
using System.Net.Http;
using System.Net.NetworkInformation;
using GraveOps.App.Models;

namespace GraveOps.App.Services;

public sealed class LocalWindowsDiscoveryService
{
    private readonly IntegrationCatalog _catalog;

    private static readonly HttpClient Client = new(
        new SocketsHttpHandler
        {
            UseProxy = false,
            ConnectTimeout = TimeSpan.FromMilliseconds(650)
        })
    {
        Timeout = TimeSpan.FromMilliseconds(1100)
    };

    private sealed record Candidate(
        string Key,
        int[] Ports,
        string[] ProcessNames,
        string Path = "");

    private static readonly Candidate[] Candidates =
    [
        new("Plex", [32400], ["plex media server", "plexmediaserver"], "/web"),
        new("Jellyfin", [8096], ["jellyfin"]),
        new("Emby", [8096], ["embyserver", "emby server"]),
        new("Tautulli", [8181], ["tautulli"]),
        new("Kometa", [], ["kometa", "plex-meta-manager"]),
        new("Sonarr", [8989], ["sonarr"]),
        new("Radarr", [7878], ["radarr"]),
        new("Lidarr", [8686], ["lidarr"]),
        new("Prowlarr", [9696], ["prowlarr"]),
        new("Bazarr", [6767], ["bazarr"]),
        new("Seerr", [5055], ["seerr", "overseerr", "jellyseerr"]),
        new("Profilarr", [6868], ["profilarr"]),
        new("autobrr", [7474], ["autobrr"]),
        new("Unpackerr", [5656], ["unpackerr"]),
        new("Cleanuparr", [11011], ["cleanuparr"]),
        new("SABnzbd", [8080], ["sabnzbd", "sabnzbd-console"]),
        new("qBittorrent", [8080, 8081], ["qbittorrent", "qbittorrent-nox"]),
        new("Tdarr", [8265], ["tdarr_server", "tdarr server"]),
        new("Maintainerr", [6246], ["maintainerr"])
    ];

    public LocalWindowsDiscoveryService(IntegrationCatalog catalog) =>
        _catalog = catalog;

    public async Task<LocalWindowsDiscoveryResult> DiscoverAsync(
        CancellationToken cancellationToken = default)
    {
        var provider = new Services.Hosts.LocalWindowsHostProvider();
        var profile = new ServerProfile
        {
            ConnectionKind = HostConnectionKind.LocalWindows,
            Name = Environment.MachineName,
            Host = "127.0.0.1",
            Username = Environment.UserName,
            Role = "Windows"
        };

        var host = await provider.ProbeAsync(profile, cancellationToken);
        var listeners = IPGlobalProperties.GetIPGlobalProperties()
            .GetActiveTcpListeners()
            .Select(x => x.Port)
            .Distinct()
            .OrderBy(x => x)
            .ToHashSet();

        var processes = Process.GetProcesses()
            .Select(x => SafeProcessName(x))
            .Where(x => x.Length > 0)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var detected = new List<DetectedIntegrationOption>();

        foreach (var candidate in Candidates)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var definition = _catalog.Find(candidate.Key);
            if (definition is null)
                continue;

            var processMatch = candidate.ProcessNames
                .Any(name => processes.Contains(name));

            var listening = candidate.Ports
                .Where(listeners.Contains)
                .ToArray();

            int? fingerprintPort = null;
            foreach (var candidatePort in listening)
            {
                var candidateFingerprint = await FingerprintAsync(candidatePort, cancellationToken);
                if (FingerprintMatches(candidate.Key, candidateFingerprint))
                {
                    fingerprintPort = candidatePort;
                    break;
                }
            }

            var fingerprintMatch = fingerprintPort is not null;
            var detectedHere = processMatch || fingerprintMatch;
            if (!detectedHere)
                continue;

            var port = fingerprintPort ?? ChoosePort(candidate, listening, processMatch);

            var evidence = new List<string>();
            if (processMatch) evidence.Add("running process");
            if (listening.Length > 0) evidence.Add("listening port " + string.Join("/", listening));
            if (fingerprintMatch) evidence.Add("HTTP fingerprint");
            if (processMatch && port is null) evidence.Add("endpoint port not identified");

            var url = port is { } finalPort
                ? $"http://{{host}}:{finalPort}{candidate.Path}"
                : "";

            detected.Add(new DetectedIntegrationOption
            {
                Key = definition.Key,
                DisplayName = definition.DisplayName,
                Category = definition.Category,
                Port = port,
                Url = url,
                Evidence = evidence.Count == 0 ? "local detection" : string.Join(" + ", evidence),
                Enabled = true
            });
        }

        if (FindExecutableOnPath("recyclarr.exe") is not null ||
            FindExecutableOnPath("recyclarr") is not null)
        {
            var definition = _catalog.Find("Recyclarr");
            if (definition is not null)
            {
                detected.Add(new DetectedIntegrationOption
                {
                    Key = definition.Key,
                    DisplayName = definition.DisplayName,
                    Category = definition.Category,
                    Evidence = "recyclarr executable on PATH",
                    Enabled = true
                });
            }
        }

        return new LocalWindowsDiscoveryResult
        {
            Host = new HostProbeResult
            {
                ConnectionKind = host.ConnectionKind,
                Platform = host.Platform,
                HostName = host.HostName,
                OperatingSystem = host.OperatingSystem,
                Architecture = host.Architecture,
                Uptime = host.Uptime,
                Capabilities = host.Capabilities,
                StorageRoots = host.StorageRoots,
                ListeningPorts = listeners.ToArray(),
                Evidence = detected.Select(x => $"{x.DisplayName}: {x.Evidence}").ToArray(),
                Detail = host.Detail
            },
            Integrations = detected
                .GroupBy(x => x.Key, StringComparer.OrdinalIgnoreCase)
                .Select(x => x.First())
                .OrderBy(x => x.Category)
                .ThenBy(x => x.DisplayName)
                .ToArray()
        };
    }

    private static string SafeProcessName(Process process)
    {
        try { return process.ProcessName.Trim().ToLowerInvariant(); }
        catch { return ""; }
        finally { process.Dispose(); }
    }

    private static int? ChoosePort(Candidate candidate, int[] listening, bool processMatch)
    {
        if (listening.Length == 1)
            return listening[0];

        if (candidate.Key.Equals("qBittorrent", StringComparison.OrdinalIgnoreCase))
        {
            if (listening.Contains(8081)) return 8081;
            if (listening.Contains(8080) && processMatch) return 8080;
        }

        return listening.FirstOrDefault() is var first && first > 0
            ? first
            : processMatch && candidate.Ports.Length > 0
                ? candidate.Ports[0]
                : null;
    }

    private static async Task<string> FingerprintAsync(int port, CancellationToken cancellationToken)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, $"http://127.0.0.1:{port}/");
            using var response = await Client.SendAsync(request, HttpCompletionOption.ResponseContentRead, cancellationToken);
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            return ($"{(int)response.StatusCode} {response.Headers.Server} {body}")
                .ToLowerInvariant();
        }
        catch
        {
            return "";
        }
    }

    private static bool FingerprintMatches(string key, string fingerprint)
    {
        if (string.IsNullOrWhiteSpace(fingerprint))
            return false;

        return key.ToLowerInvariant() switch
        {
            "plex" => fingerprint.Contains("plex"),
            "jellyfin" => fingerprint.Contains("jellyfin"),
            "emby" => fingerprint.Contains("emby"),
            "tautulli" => fingerprint.Contains("tautulli"),
            "kometa" => fingerprint.Contains("kometa") || fingerprint.Contains("plex meta manager"),
            "sonarr" => fingerprint.Contains("sonarr"),
            "radarr" => fingerprint.Contains("radarr"),
            "lidarr" => fingerprint.Contains("lidarr"),
            "prowlarr" => fingerprint.Contains("prowlarr"),
            "bazarr" => fingerprint.Contains("bazarr"),
            "seerr" => fingerprint.Contains("seerr") || fingerprint.Contains("overseerr") || fingerprint.Contains("jellyseerr"),
            "profilarr" => fingerprint.Contains("profilarr"),
            "autobrr" => fingerprint.Contains("autobrr"),
            "unpackerr" => fingerprint.Contains("unpackerr"),
            "cleanuparr" => fingerprint.Contains("cleanuparr"),
            "sabnzbd" => fingerprint.Contains("sabnzbd"),
            "qbittorrent" => fingerprint.Contains("qbittorrent"),
            "tdarr" => fingerprint.Contains("tdarr"),
            "maintainerr" => fingerprint.Contains("maintainerr"),
            _ => false
        };
    }

    private static string? FindExecutableOnPath(string name)
    {
        var path = Environment.GetEnvironmentVariable("PATH") ?? "";
        foreach (var segment in path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            try
            {
                var candidate = Path.Combine(segment.Trim(), name);
                if (File.Exists(candidate))
                    return candidate;
            }
            catch { }
        }
        return null;
    }
}
