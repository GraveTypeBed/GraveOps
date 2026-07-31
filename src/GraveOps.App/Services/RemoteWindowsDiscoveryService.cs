using GraveOps.App.Models;

namespace GraveOps.App.Services;

/// <summary>
/// Verified Remote Windows integration discovery from process/container identity
/// and listening ports. It does not enable WinRM or change TrustedHosts.
/// </summary>
public sealed class RemoteWindowsDiscoveryService
{
    private readonly PowerShellRemotingService _remoting;
    private readonly IntegrationCatalog _catalog;

    private sealed record Signature(string Key, int? Port, string[] Tokens);

    private static readonly Signature[] Signatures =
    [
        new("Plex", 32400, ["plex media server", "plexmediaserver"]),
        new("Sonarr", 8989, ["sonarr"]),
        new("Radarr", 7878, ["radarr"]),
        new("Lidarr", 8686, ["lidarr"]),
        new("Prowlarr", 9696, ["prowlarr"]),
        new("SABnzbd", 8080, ["sabnzbd"]),
        new("qBittorrent", 8081, ["qbittorrent", "qbittorrent-nox"]),
        new("Tautulli", 8181, ["tautulli"]),
        new("Bazarr", 6767, ["bazarr"]),
        new("Seerr", 5055, ["overseerr", "jellyseerr", "seerr"]),
        new("Recyclarr", null, ["recyclarr"]),
        new("Profilarr", null, ["profilarr"]),
        new("autobrr", 7474, ["autobrr"]),
        new("Unpackerr", null, ["unpackerr"]),
        new("Cleanuparr", 11011, ["cleanuparr"]),
        new("Tdarr", 8265, ["tdarr"]),
        new("Maintainerr", 6246, ["maintainerr"]),
        new("Kometa", null, ["kometa", "plex-meta-manager"])
    ];

    public RemoteWindowsDiscoveryService(
        PowerShellRemotingService remoting,
        IntegrationCatalog catalog)
    {
        _remoting = remoting;
        _catalog = catalog;
    }

    public async Task<IReadOnlyList<DetectedIntegrationOption>> DiscoverAsync(
        ServerProfile profile,
        HostProbeResult host,
        CancellationToken cancellationToken = default)
    {
        const string script = @"
Get-Process -ErrorAction SilentlyContinue | Select-Object -ExpandProperty ProcessName -Unique |
  ForEach-Object { Write-Output ('PROC|' + $_) }
if (Get-Command docker -ErrorAction SilentlyContinue) {
  docker ps --format '{{.Names}}|{{.Image}}' 2>$null | ForEach-Object { Write-Output ('CTR|' + $_) }
}
";
        var output = await _remoting.ExecuteAsync(profile, script, 30, cancellationToken);
        var evidence = output.Replace("\r", "").Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(x => x.Trim()).ToArray();
        var haystack = string.Join("\n", evidence).ToLowerInvariant();
        var ports = host.ListeningPorts.ToHashSet();

        var found = new List<DetectedIntegrationOption>();
        foreach (var signature in Signatures)
        {
            var tokenMatch = signature.Tokens.Any(token => haystack.Contains(token, StringComparison.OrdinalIgnoreCase));
            var portMatch = signature.Port is { } candidatePort && ports.Contains(candidatePort);
            if (!tokenMatch && !portMatch)
                continue;

            var definition = _catalog.Find(signature.Key);
            if (definition is null)
                continue;

            var matchedPort = signature.Port is { } expected && ports.Contains(expected) ? expected : (int?)null;
            var url = matchedPort is { } p ? $"http://{profile.Host}:{p}" : "";
            var reason = tokenMatch && portMatch
                ? $"Remote process/container identity + TCP {matchedPort}"
                : tokenMatch
                    ? "Remote process/container identity"
                    : $"Listening TCP {matchedPort}";

            found.Add(new DetectedIntegrationOption
            {
                Key = definition.Key,
                DisplayName = definition.DisplayName,
                Category = definition.Category,
                Port = matchedPort,
                Url = url,
                Evidence = reason,
                Enabled = true
            });
        }

        return found;
    }
}
