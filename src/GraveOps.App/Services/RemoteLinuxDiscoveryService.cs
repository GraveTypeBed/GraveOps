using System.Text.RegularExpressions;
using GraveOps.App.Models;
using GraveOps.App.Services.Hosts;

namespace GraveOps.App.Services;

public sealed class RemoteLinuxDiscoveryService
{
    private readonly SshService _ssh;
    private readonly IntegrationCatalog _catalog;

    private sealed record Candidate(
        string Key,
        int[] DefaultPorts,
        string[] IdentityTokens,
        string[] FingerprintTokens,
        string Path = "");

    private static readonly Candidate[] Candidates =
    [
        new("Plex", [32400], ["plex media server", "plexmediaserver", "plexinc/pms-docker"], ["plex"], "/web"),
        new("Jellyfin", [8096], ["jellyfin"], ["jellyfin"]),
        new("Emby", [8096], ["embyserver", "emby server", "emby/embyserver"], ["emby"]),
        new("Tautulli", [8181], ["tautulli"], ["tautulli"]),
        new("Kometa", [], ["kometa", "kometateam/kometa", "plex-meta-manager"], []),
        new("Sonarr", [8989], ["sonarr"], ["sonarr"]),
        new("Radarr", [7878], ["radarr"], ["radarr"]),
        new("Lidarr", [8686], ["lidarr"], ["lidarr"]),
        new("Prowlarr", [9696], ["prowlarr"], ["prowlarr"]),
        new("Bazarr", [6767], ["bazarr"], ["bazarr"]),
        new("Seerr", [5055], ["seerr", "overseerr", "jellyseerr"], ["seerr", "overseerr", "jellyseerr"]),
        new("Profilarr", [6868], ["profilarr", "ghcr.io/dictionarry-hub/profilarr"], ["profilarr"]),
        new("autobrr", [7474], ["autobrr"], ["autobrr"]),
        new("Unpackerr", [5656], ["unpackerr", "golift/unpackerr"], ["unpackerr"]),
        new("Cleanuparr", [11011], ["cleanuparr", "ghcr.io/cleanuparr/cleanuparr"], ["cleanuparr"]),
        new("SABnzbd", [8080], ["sabnzbd", "sabnzbdplus"], ["sabnzbd"]),
        new("qBittorrent", [8080, 8081], ["qbittorrent", "qbittorrent-nox"], ["qbittorrent"]),
        new("Tdarr", [8265], ["tdarr_server", "tdarr server", "haveagitgat/tdarr"], ["tdarr"]),
        new("Maintainerr", [6246], ["maintainerr", "ghcr.io/jorenn92/maintainerr"], ["maintainerr"]),
        new("Pi-hole", [80, 443], ["pihole", "pihole-FTL"], ["pi-hole", "pihole"])
    ];

    public RemoteLinuxDiscoveryService(SshService ssh, IntegrationCatalog catalog)
    {
        _ssh = ssh;
        _catalog = catalog;
    }

    public async Task<RemoteLinuxDiscoveryResult> DiscoverAsync(
        ServerProfile profile,
        HostProbeResult? knownHost = null,
        CancellationToken cancellationToken = default)
    {
        var host = knownHost ?? await new RemoteLinuxHostProvider(_ssh)
            .ProbeAsync(profile, cancellationToken);

        const string inventoryCommand =
            "printf '__LISTEN__\\n'; " +
            "((ss -ltnpH 2>/dev/null || ss -ltnH 2>/dev/null || netstat -ltnp 2>/dev/null || true) | head -n 500); " +
            "printf '__PROCESSES__\\n'; (ps -eo comm=,args= 2>/dev/null | head -n 2000 || true); " +
            "printf '__DOCKER__\\n'; (docker ps -a --format '{{.Names}}|{{.Image}}|{{.Ports}}' 2>/dev/null | head -n 700 || true); " +
            "printf '__IMAGES__\\n'; (docker images --format '{{.Repository}}:{{.Tag}}' 2>/dev/null | head -n 700 || true); " +
            "printf '__TOOLS__\\n'; command -v recyclarr 2>/dev/null || true";

        var inventory = await _ssh.ExecuteAsync(profile, inventoryCommand, 45, cancellationToken);
        if (!inventory.Success && string.IsNullOrWhiteSpace(inventory.StdOut))
            throw new InvalidOperationException(
                string.IsNullOrWhiteSpace(inventory.StdErr)
                    ? "Remote Linux integration inventory returned no data."
                    : inventory.StdErr.Trim());

        var text = inventory.StdOut.Replace("\r", "", StringComparison.Ordinal);
        var listenText = Section(text, "__LISTEN__", "__PROCESSES__");
        var processText = Section(text, "__PROCESSES__", "__DOCKER__");
        var dockerText = Section(text, "__DOCKER__", "__IMAGES__");
        var imagesText = Section(text, "__IMAGES__", "__TOOLS__");
        var toolsText = text.Contains("__TOOLS__", StringComparison.Ordinal)
            ? text[(text.IndexOf("__TOOLS__", StringComparison.Ordinal) + "__TOOLS__".Length)..]
            : "";

        var listeningPorts = ExtractListeningPorts(listenText).ToHashSet();
        var probePorts = new HashSet<int>();

        foreach (var candidate in Candidates)
        {
            foreach (var port in candidate.DefaultPorts.Where(listeningPorts.Contains))
                probePorts.Add(port);
            foreach (var port in CandidateSpecificPorts(candidate, listenText, dockerText))
                probePorts.Add(port);
        }

        var fingerprints = await ProbeHttpFingerprintsAsync(
            profile,
            probePorts.OrderBy(x => x).Take(48).ToArray(),
            cancellationToken);

        var detected = new List<DetectedIntegrationOption>();
        foreach (var candidate in Candidates)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var definition = _catalog.Find(candidate.Key);
            if (definition is null)
                continue;

            var processMatch = candidate.IdentityTokens.Any(token =>
                processText.Contains(token, StringComparison.OrdinalIgnoreCase));
            var dockerMatch = candidate.IdentityTokens.Any(token =>
                dockerText.Contains(token, StringComparison.OrdinalIgnoreCase));
            var specificPorts = CandidateSpecificPorts(candidate, listenText, dockerText)
                .Distinct()
                .OrderBy(x => x)
                .ToArray();

            var candidatePorts = specificPorts
                .Concat(candidate.DefaultPorts.Where(listeningPorts.Contains))
                .Distinct()
                .ToArray();

            int? fingerprintPort = candidatePorts
                .FirstOrDefault(port =>
                    fingerprints.TryGetValue(port, out var fingerprint) &&
                    FingerprintMatches(candidate, fingerprint));
            if (fingerprintPort == 0)
                fingerprintPort = null;

            // A verified app requires application-specific evidence: a matching HTTP
            // fingerprint, process identity, or Docker container/image identity.
            if (fingerprintPort is null && !processMatch && !dockerMatch)
                continue;

            int? port = fingerprintPort;
            if (port is null && specificPorts.Length > 0)
                port = specificPorts[0];
            if (port is null && (processMatch || dockerMatch))
            {
                var defaultListener = candidate.DefaultPorts.FirstOrDefault(listeningPorts.Contains);
                if (defaultListener > 0)
                    port = defaultListener;
            }

            var evidence = new List<string>();
            if (fingerprintPort is not null) evidence.Add($"HTTP fingerprint on {fingerprintPort}");
            if (processMatch) evidence.Add("matching process identity");
            if (dockerMatch) evidence.Add("matching Docker identity");
            if (specificPorts.Length > 0) evidence.Add("application-associated listener " + string.Join("/", specificPorts));
            if (port is null) evidence.Add("endpoint port not identified");

            detected.Add(new DetectedIntegrationOption
            {
                Key = definition.Key,
                DisplayName = definition.DisplayName,
                Category = definition.Category,
                Port = port,
                Url = port is { } finalPort
                    ? $"http://{{host}}:{finalPort}{candidate.Path}"
                    : "",
                Evidence = string.Join(" + ", evidence.Distinct()),
                Enabled = true
            });
        }

        // Kometa is commonly run as a scheduled/one-shot container. Between runs
        // there may be no process or persistent container, so an installed official
        // image is sufficient installation evidence for this CLI-style integration.
        if (!detected.Any(x => x.Key.Equals("Kometa", StringComparison.OrdinalIgnoreCase)) &&
            (imagesText.Contains("kometateam/kometa", StringComparison.OrdinalIgnoreCase) ||
             imagesText.Contains("plex-meta-manager", StringComparison.OrdinalIgnoreCase)))
        {
            var definition = _catalog.Find("Kometa");
            if (definition is not null)
            {
                detected.Add(new DetectedIntegrationOption
                {
                    Key = definition.Key,
                    DisplayName = definition.DisplayName,
                    Category = definition.Category,
                    Evidence = "Kometa Docker image installed (scheduled/one-shot runtime)",
                    Enabled = true
                });
            }
        }

        var recyclarrCli = !string.IsNullOrWhiteSpace(toolsText) &&
                           toolsText.Contains("recyclarr", StringComparison.OrdinalIgnoreCase);
        var recyclarrContainer = dockerText.Contains("recyclarr", StringComparison.OrdinalIgnoreCase);
        var recyclarrImage = imagesText.Contains("recyclarr", StringComparison.OrdinalIgnoreCase);
        if (recyclarrCli || recyclarrContainer || recyclarrImage)
        {
            var definition = _catalog.Find("Recyclarr");
            if (definition is not null)
            {
                var evidence = new List<string>();
                if (recyclarrCli) evidence.Add("recyclarr executable on remote PATH");
                if (recyclarrContainer) evidence.Add("Recyclarr Docker container identity");
                if (recyclarrImage) evidence.Add("Recyclarr Docker image installed");
                detected.Add(new DetectedIntegrationOption
                {
                    Key = definition.Key,
                    DisplayName = definition.DisplayName,
                    Category = definition.Category,
                    Evidence = string.Join(" + ", evidence),
                    Enabled = true
                });
            }
        }

        return new RemoteLinuxDiscoveryResult
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
                ListeningPorts = listeningPorts.OrderBy(x => x).ToArray(),
                Evidence = detected.Select(x => $"{x.DisplayName}: {x.Evidence}").ToArray(),
                Detail = host.Detail
            },
            Integrations = detected
                .GroupBy(x => x.Key, StringComparer.OrdinalIgnoreCase)
                .Select(x => x.First())
                .OrderBy(x => x.Category)
                .ThenBy(x => x.DisplayName)
                .ToArray(),
            ListeningPorts = listeningPorts.OrderBy(x => x).ToArray()
        };
    }

    private async Task<Dictionary<int, string>> ProbeHttpFingerprintsAsync(
        ServerProfile profile,
        int[] ports,
        CancellationToken cancellationToken)
    {
        if (ports.Length == 0)
            return new Dictionary<int, string>();

        var command = new StringBuilder();
        command.Append("command -v base64 >/dev/null 2>&1 || exit 0; ");
        foreach (var port in ports.Where(x => x is > 0 and <= 65535))
        {
            command.Append($"printf '__PORT__{port}\\n'; ");
            command.Append($"if command -v curl >/dev/null 2>&1; then (curl -k -sS --connect-timeout 0.5 --max-time 1 -D - http://127.0.0.1:{port}/ 2>/dev/null | head -c 12000 | base64 | tr -d '\\n'); ");
            command.Append($"elif command -v wget >/dev/null 2>&1; then (wget -qO- -T 1 http://127.0.0.1:{port}/ 2>/dev/null | head -c 12000 | base64 | tr -d '\\n'); fi; ");
            command.Append("printf '\\n__ENDPORT__\\n'; ");
        }

        var result = await _ssh.ExecuteAsync(profile, command.ToString(), 70, cancellationToken);
        var output = result.StdOut.Replace("\r", "", StringComparison.Ordinal);
        var fingerprints = new Dictionary<int, string>();

        foreach (Match match in Regex.Matches(
                     output,
                     @"__PORT__(?<port>\d+)\n(?<payload>.*?)\n__ENDPORT__",
                     RegexOptions.Singleline))
        {
            if (!int.TryParse(match.Groups["port"].Value, out var port))
                continue;

            try
            {
                var payload = match.Groups["payload"].Value.Trim();
                if (payload.Length == 0)
                    continue;
                fingerprints[port] = Encoding.UTF8
                    .GetString(Convert.FromBase64String(payload))
                    .ToLowerInvariant();
            }
            catch
            {
                // A failed HTTP/base64 probe is not evidence; process/container
                // identity can still verify the integration.
            }
        }

        return fingerprints;
    }

    private static IEnumerable<int> CandidateSpecificPorts(
        Candidate candidate,
        string listenText,
        string dockerText)
    {
        foreach (var line in listenText.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            if (!candidate.IdentityTokens.Any(token => line.Contains(token, StringComparison.OrdinalIgnoreCase)))
                continue;
            foreach (var port in ExtractListeningPorts(line))
                yield return port;
        }

        foreach (var line in dockerText.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            if (!candidate.IdentityTokens.Any(token => line.Contains(token, StringComparison.OrdinalIgnoreCase)))
                continue;

            foreach (Match match in Regex.Matches(line, @":(?<port>\d{1,5})->"))
            {
                if (int.TryParse(match.Groups["port"].Value, out var port) && port is > 0 and <= 65535)
                    yield return port;
            }
        }
    }

    private static IEnumerable<int> ExtractListeningPorts(string text)
    {
        foreach (var line in text.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            foreach (Match match in Regex.Matches(line, @":(?<port>\d{1,5})(?=\s|$)"))
            {
                if (int.TryParse(match.Groups["port"].Value, out var port) && port is > 0 and <= 65535)
                    yield return port;
            }
        }
    }

    private static bool FingerprintMatches(Candidate candidate, string fingerprint) =>
        !string.IsNullOrWhiteSpace(fingerprint) &&
        candidate.FingerprintTokens.Any(token => fingerprint.Contains(token, StringComparison.OrdinalIgnoreCase));

    private static string Section(string text, string start, string end)
    {
        var startAt = text.IndexOf(start, StringComparison.Ordinal);
        if (startAt < 0)
            return "";
        startAt += start.Length;
        var endAt = text.IndexOf(end, startAt, StringComparison.Ordinal);
        return endAt < 0 ? text[startAt..] : text[startAt..endAt];
    }
}
