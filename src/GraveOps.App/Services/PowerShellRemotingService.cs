using System.Diagnostics;
using GraveOps.App.Models;

namespace GraveOps.App.Services;

/// <summary>
/// Remote Windows transport for GraveOps. Passwords stay in Windows Credential
/// Manager and are passed to the child PowerShell process through its environment,
/// never on the process command line. The remote account receives only the rights
/// assigned by Windows/PowerShell remoting policy.
/// </summary>
public sealed class PowerShellRemotingService
{
    private readonly CredentialManagerService _credentials;

    public PowerShellRemotingService(CredentialManagerService credentials) =>
        _credentials = credentials;

    public async Task<string> ExecuteAsync(
        ServerProfile profile,
        string remoteScript,
        int timeoutSeconds = 30,
        CancellationToken cancellationToken = default)
    {
        if (profile.ConnectionKind != HostConnectionKind.RemoteWindows)
            throw new InvalidOperationException("PowerShell remoting requires a Remote Windows host profile.");

        var secret = _credentials.ReadSecret(profile.PasswordCredentialTarget);
        if (string.IsNullOrEmpty(secret))
            throw new InvalidOperationException("No Windows remoting credential is saved for this host.");

        var useSsl = profile.Port == 5986;
        var portClause = profile.Port is 5985 or 5986 ? $" -Port {profile.Port}" : "";
        var sslClause = useSsl ? " -UseSSL" : "";

        var wrapper = $@"
$ErrorActionPreference='Stop'
$sec = ConvertTo-SecureString $env:GRAVEOPS_REMOTE_SECRET -AsPlainText -Force
$cred = [pscredential]::new($env:GRAVEOPS_REMOTE_USER,$sec)
try {{
  Invoke-Command -ComputerName $env:GRAVEOPS_REMOTE_HOST{portClause}{sslClause} -Credential $cred -ScriptBlock {{
{remoteScript}
  }}
}} finally {{
  Remove-Item Env:GRAVEOPS_REMOTE_SECRET -ErrorAction SilentlyContinue
}}
";

        var psi = new ProcessStartInfo
        {
            FileName = "powershell.exe",
            Arguments = "-NoProfile -NonInteractive -ExecutionPolicy Bypass -Command -",
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };
        psi.Environment["GRAVEOPS_REMOTE_HOST"] = profile.Host;
        psi.Environment["GRAVEOPS_REMOTE_USER"] = profile.Username;
        psi.Environment["GRAVEOPS_REMOTE_SECRET"] = secret;

        using var process = new Process { StartInfo = psi };
        process.Start();
        await process.StandardInput.WriteAsync(wrapper);
        process.StandardInput.Close();

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(Math.Clamp(timeoutSeconds, 5, 180)));

        var stdOutTask = process.StandardOutput.ReadToEndAsync(timeout.Token);
        var stdErrTask = process.StandardError.ReadToEndAsync(timeout.Token);
        await process.WaitForExitAsync(timeout.Token);

        var stdout = await stdOutTask;
        var stderr = await stdErrTask;

        if (process.ExitCode != 0)
        {
            var detail = string.IsNullOrWhiteSpace(stderr) ? stdout : stderr;
            throw new InvalidOperationException(
                $"Remote Windows PowerShell failed. Verify WinRM/PowerShell remoting, network trust and the saved account.\n{detail.Trim()}");
        }

        return stdout.Trim();
    }
}
