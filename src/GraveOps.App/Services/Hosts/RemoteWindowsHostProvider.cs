using GraveOps.App.Models;

namespace GraveOps.App.Services.Hosts;

public sealed class RemoteWindowsHostProvider : IHostProvider
{
    private readonly PowerShellRemotingService _remoting;

    public RemoteWindowsHostProvider(PowerShellRemotingService remoting) =>
        _remoting = remoting;

    public HostConnectionKind Kind => HostConnectionKind.RemoteWindows;
    public bool CanHandle(ServerProfile profile) => profile.ConnectionKind == Kind;

    public async Task<HostProbeResult> ProbeAsync(
        ServerProfile profile,
        CancellationToken cancellationToken = default)
    {
        const string script = @"
$os = Get-CimInstance Win32_OperatingSystem
Write-Output ('HOST|' + $env:COMPUTERNAME)
Write-Output ('OS|' + $os.Caption + ' ' + $os.Version)
Write-Output ('ARCH|' + $env:PROCESSOR_ARCHITECTURE)
$up = [int64]((Get-Date) - $os.LastBootUpTime).TotalSeconds
Write-Output ('UP|' + $up)
Get-PSDrive -PSProvider FileSystem | Where-Object {$_.Root} | ForEach-Object {
  Write-Output ('DRIVE|' + $_.Root + '|' + [int64]$_.Free + '|' + [int64]($_.Used + $_.Free))
}
if (Get-Command Get-NetTCPConnection -ErrorAction SilentlyContinue) {
  (Get-NetTCPConnection -State Listen -ErrorAction SilentlyContinue | Select-Object -ExpandProperty LocalPort -Unique | Sort-Object) |
    ForEach-Object { Write-Output ('PORT|' + $_) }
}
if (Get-Command docker -ErrorAction SilentlyContinue) { Write-Output 'DOCKER|1' } else { Write-Output 'DOCKER|0' }
";

        var output = await _remoting.ExecuteAsync(profile, script, 35, cancellationToken);
        var lines = output.Replace("\r", "").Split('\n', StringSplitOptions.RemoveEmptyEntries);

        string Field(string prefix) => lines.FirstOrDefault(x => x.StartsWith(prefix + "|", StringComparison.OrdinalIgnoreCase))?.Split('|', 2)[1].Trim() ?? "--";
        var ports = lines.Where(x => x.StartsWith("PORT|", StringComparison.OrdinalIgnoreCase))
            .Select(x => int.TryParse(x.Split('|')[1], out var p) ? p : 0)
            .Where(x => x > 0).Distinct().OrderBy(x => x).ToArray();
        var drives = lines.Where(x => x.StartsWith("DRIVE|", StringComparison.OrdinalIgnoreCase))
            .Select(x => x.Split('|'))
            .Where(x => x.Length >= 2)
            .Select(x => x[1])
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var docker = Field("DOCKER") == "1";

        TimeSpan? uptime = long.TryParse(Field("UP"), out var seconds)
            ? TimeSpan.FromSeconds(Math.Max(0, seconds))
            : null;

        var capabilities =
            HostCapability.Remote |
            HostCapability.PowerShell |
            HostCapability.ProcessInspection |
            HostCapability.ServiceControl |
            HostCapability.FileSystem |
            HostCapability.Storage |
            HostCapability.EventLog |
            HostCapability.LocalHttp;
        if (docker) capabilities |= HostCapability.Docker;

        return new HostProbeResult
        {
            ConnectionKind = Kind,
            Platform = HostPlatform.Windows,
            HostName = Field("HOST"),
            OperatingSystem = Field("OS"),
            Architecture = Field("ARCH"),
            Uptime = uptime,
            Capabilities = capabilities,
            StorageRoots = drives,
            ListeningPorts = ports,
            Evidence = new[]
            {
                "PowerShell remoting verified",
                $"{drives.Length} filesystem root(s)",
                $"{ports.Length} listening TCP port(s)",
                docker ? "Docker CLI available" : "Docker CLI not detected"
            },
            Detail = "Remote Windows host reachable through PowerShell remoting."
        };
    }
}
