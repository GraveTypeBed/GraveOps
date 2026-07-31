using GraveOps.App.Models;

namespace GraveOps.App.Services;

/// <summary>
/// Sanitized, provider-aware diagnostic bundle. It gathers host/platform facts and
/// generic operating-system diagnostics without assuming a particular homelab layout.
/// </summary>
public sealed class DiagnosticsBundleService
{
    private readonly AppServices _services;
    public DiagnosticsBundleService(AppServices services) => _services = services;

    public async Task<string> CollectAsync(ServerProfile profile, CancellationToken cancellationToken = default)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"GRAVEOPS DIAGNOSTIC BUNDLE - {DateTimeOffset.Now:O}");
        sb.AppendLine($"PROFILE: {profile.Name} ({profile.ConnectionKind})");

        try
        {
            var probe = await _services.Hosts.Resolve(profile).ProbeAsync(profile, cancellationToken);
            sb.AppendLine($"HOST: {probe.HostName}");
            sb.AppendLine($"OS: {probe.OperatingSystem}");
            sb.AppendLine($"ARCH: {probe.Architecture}");
            sb.AppendLine($"UPTIME: {probe.Uptime}");
            sb.AppendLine($"CAPABILITIES: {probe.Capabilities}");
            sb.AppendLine($"STORAGE ROOTS: {string.Join(", ", probe.StorageRoots)}");
            sb.AppendLine($"LISTENING PORTS: {string.Join(", ", probe.ListeningPorts.Take(120))}");
            foreach (var evidence in probe.Evidence)
                sb.AppendLine("EVIDENCE: " + evidence);
        }
        catch (Exception ex)
        {
            sb.AppendLine("HOST PROBE ERROR: " + ex.Message);
        }

        try
        {
            var environment = await _services.Environment.GetSnapshotAsync(false, cancellationToken);
            var host = environment.Hosts.FirstOrDefault(x => x.ServerId == profile.Id);
            if (host is not null)
            {
                sb.AppendLine();
                sb.AppendLine("VERIFIED APPLICATIONS");
                foreach (var app in host.Apps)
                    sb.AppendLine($"{app.Name} | {app.State} | {app.Detail}");
            }
        }
        catch (Exception ex)
        {
            sb.AppendLine("ENVIRONMENT SNAPSHOT ERROR: " + ex.Message);
        }

        if (profile.ConnectionKind == HostConnectionKind.RemoteLinux)
            await AppendLinuxAsync(sb, profile, cancellationToken);
        else if (profile.ConnectionKind == HostConnectionKind.RemoteWindows)
            await AppendRemoteWindowsAsync(sb, profile, cancellationToken);
        else if (profile.ConnectionKind == HostConnectionKind.LocalWindows)
            AppendLocalWindows(sb);

        return sb.ToString().TrimEnd();
    }

    private async Task AppendLinuxAsync(StringBuilder sb, ServerProfile profile, CancellationToken token)
    {
        var commands = new (string Name, string Command, int Timeout)[]
        {
            ("HOST", "hostnamectl 2>/dev/null || uname -a", 20),
            ("FAILED UNITS", "systemctl --failed --no-pager 2>&1 || true", 30),
            ("MOUNTS", "findmnt 2>&1 || true", 30),
            ("BLOCK DEVICES", "lsblk -f 2>&1 || true", 30),
            ("FILESYSTEM", "df -hT 2>&1 || true", 30),
            ("DOCKER", "docker ps -a --format '{{.Names}} | {{.Image}} | {{.Status}}' 2>&1 || true", 30),
            ("RECENT WARNINGS", "journalctl -p warning -n 120 --no-pager 2>&1 || true", 60)
        };

        foreach (var item in commands)
        {
            sb.AppendLine();
            sb.AppendLine(new string('=', 72));
            sb.AppendLine(item.Name);
            sb.AppendLine(new string('=', 72));
            try
            {
                var result = await _services.Ssh.ExecuteAsync(profile, item.Command, item.Timeout, token);
                sb.AppendLine(result.Combined);
            }
            catch (Exception ex) { sb.AppendLine("ERROR: " + ex.Message); }
        }
    }

    private async Task AppendRemoteWindowsAsync(StringBuilder sb, ServerProfile profile, CancellationToken token)
    {
        const string script = @"
Write-Output '--- SERVICES NOT RUNNING (AUTO START) ---'
Get-CimInstance Win32_Service | Where-Object {$_.StartMode -eq 'Auto' -and $_.State -ne 'Running'} |
  Select-Object -First 80 Name,State,StartMode | Format-Table -AutoSize | Out-String | Write-Output
Write-Output '--- FILESYSTEM ---'
Get-PSDrive -PSProvider FileSystem | Format-Table Name,Root,Used,Free -AutoSize | Out-String | Write-Output
if (Get-Command docker -ErrorAction SilentlyContinue) {
  Write-Output '--- DOCKER ---'
  docker ps -a --format '{{.Names}} | {{.Image}} | {{.Status}}'
}
Write-Output '--- RECENT SYSTEM WARNINGS ---'
Get-WinEvent -FilterHashtable @{LogName='System'; Level=2,3} -MaxEvents 80 -ErrorAction SilentlyContinue |
  Select-Object TimeCreated,ProviderName,Id,LevelDisplayName,Message |
  Format-List | Out-String | Write-Output
";
        try { sb.AppendLine(await _services.PowerShellRemote.ExecuteAsync(profile, script, 60, token)); }
        catch (Exception ex) { sb.AppendLine("REMOTE WINDOWS DETAIL ERROR: " + ex.Message); }
    }

    private static void AppendLocalWindows(StringBuilder sb)
    {
        sb.AppendLine();
        sb.AppendLine("LOCAL WINDOWS");
        sb.AppendLine($"Machine: {Environment.MachineName}");
        sb.AppendLine($"OS: {Environment.OSVersion}");
        sb.AppendLine($"64-bit OS: {Environment.Is64BitOperatingSystem}");
        sb.AppendLine($"Processor count: {Environment.ProcessorCount}");
    }
}
