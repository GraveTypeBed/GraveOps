using System.Diagnostics;
using GraveOps.App.Models;

namespace GraveOps.App.Services.Hosts;

public sealed class LocalWindowsHostProvider : IHostProvider
{
    public HostConnectionKind Kind => HostConnectionKind.LocalWindows;

    public bool CanHandle(ServerProfile profile) =>
        profile.ConnectionKind == HostConnectionKind.LocalWindows;

    public Task<HostProbeResult> ProbeAsync(
        ServerProfile profile,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var roots = DriveInfo.GetDrives()
            .Where(x => x.IsReady)
            .Select(x => $"{x.Name} [{x.DriveFormat}] {FormatBytes(x.AvailableFreeSpace)} free of {FormatBytes(x.TotalSize)}")
            .ToArray();

        var capabilities =
            HostCapability.Local |
            HostCapability.ProcessInspection |
            HostCapability.ServiceControl |
            HostCapability.FileSystem |
            HostCapability.Storage |
            HostCapability.LocalHttp |
            HostCapability.EventLog |
            HostCapability.PowerShell;

        var evidence = new List<string>
        {
            "Native Windows process and filesystem access",
            "Local HTTP/API access without a network hop"
        };

        if (DockerAvailable())
        {
            capabilities |= HostCapability.Docker;
            evidence.Add("Docker CLI detected");
        }

        return Task.FromResult(new HostProbeResult
        {
            ConnectionKind = HostConnectionKind.LocalWindows,
            Platform = HostPlatform.Windows,
            HostName = Environment.MachineName,
            OperatingSystem = System.Runtime.InteropServices.RuntimeInformation.OSDescription,
            Architecture = System.Runtime.InteropServices.RuntimeInformation.OSArchitecture.ToString(),
            Uptime = TimeSpan.FromMilliseconds(Environment.TickCount64),
            Capabilities = capabilities,
            StorageRoots = roots,
            Evidence = evidence,
            Detail = "Native local Windows provider. No SSH or WinRM hop is required."
        });
    }

    private static bool DockerAvailable()
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "docker.exe",
                Arguments = "version --format {{.Server.Version}}",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };
            using var process = Process.Start(psi);
            if (process is null)
                return false;
            if (!process.WaitForExit(1600))
            {
                try { process.Kill(true); } catch { }
                return false;
            }
            return process.ExitCode == 0;
        }
        catch
        {
            return false;
        }
    }

    private static string FormatBytes(long bytes)
    {
        var value = Math.Max(0, bytes);
        string[] units = ["B", "KB", "MB", "GB", "TB", "PB"];
        var size = (double)value;
        var unit = 0;

        while (size >= 1024 && unit < units.Length - 1)
        {
            size /= 1024;
            unit++;
        }

        return $"{size:0.#} {units[unit]}";
    }
}
