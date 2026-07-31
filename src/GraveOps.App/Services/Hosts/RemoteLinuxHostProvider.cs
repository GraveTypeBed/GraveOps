using GraveOps.App.Models;
using GraveOps.App.Services;

namespace GraveOps.App.Services.Hosts;

public sealed class RemoteLinuxHostProvider : IHostProvider
{
    private readonly SshService _ssh;

    public RemoteLinuxHostProvider(SshService ssh) => _ssh = ssh;

    public HostConnectionKind Kind => HostConnectionKind.RemoteLinux;

    public bool CanHandle(ServerProfile profile) =>
        profile.ConnectionKind == HostConnectionKind.RemoteLinux;

    public async Task<HostProbeResult> ProbeAsync(
        ServerProfile profile,
        CancellationToken cancellationToken = default)
    {
        const string command =
            "printf '__HOST__\\n'; hostname; " +
            "printf '__OS__\\n'; (grep '^PRETTY_NAME=' /etc/os-release 2>/dev/null | cut -d= -f2- | tr -d '\"' || uname -s); " +
            "printf '__ARCH__\\n'; uname -m; " +
            "printf '__UP__\\n'; cat /proc/uptime 2>/dev/null | cut -d' ' -f1 || true; " +
            "printf '__CAPS__\\n'; command -v docker >/dev/null 2>&1 && echo docker; command -v systemctl >/dev/null 2>&1 && echo systemd; command -v journalctl >/dev/null 2>&1 && echo journal; command -v smartctl >/dev/null 2>&1 && echo smart; " +
            "printf '__ROOTS__\\n'; findmnt -rn -o SOURCE,TARGET,FSTYPE 2>/dev/null | head -n 120 || true";

        var response = await _ssh.ExecuteAsync(
            profile,
            command,
            20,
            cancellationToken);

        if (!response.Success && string.IsNullOrWhiteSpace(response.StdOut))
            throw new InvalidOperationException(
                string.IsNullOrWhiteSpace(response.StdErr)
                    ? "Remote Linux host probe returned no data."
                    : response.StdErr.Trim());

        var text = response.StdOut.Replace("\r", "", StringComparison.Ordinal);
        var host = Section(text, "__HOST__", "__OS__").Trim();
        var os = Section(text, "__OS__", "__ARCH__").Trim();
        var arch = Section(text, "__ARCH__", "__UP__").Trim();
        var upText = Section(text, "__UP__", "__CAPS__").Trim();
        var capsText = Section(text, "__CAPS__", "__ROOTS__");
        var rootsText = text.Contains("__ROOTS__", StringComparison.Ordinal)
            ? text[(text.IndexOf("__ROOTS__", StringComparison.Ordinal) + "__ROOTS__".Length)..]
            : "";

        var caps =
            HostCapability.Remote |
            HostCapability.Ssh |
            HostCapability.ProcessInspection |
            HostCapability.FileSystem |
            HostCapability.Storage |
            HostCapability.LocalHttp;

        if (capsText.Contains("docker", StringComparison.OrdinalIgnoreCase))
            caps |= HostCapability.Docker;
        if (capsText.Contains("systemd", StringComparison.OrdinalIgnoreCase))
            caps |= HostCapability.Systemd | HostCapability.ServiceControl;
        if (capsText.Contains("journal", StringComparison.OrdinalIgnoreCase))
            caps |= HostCapability.Journal;
        if (capsText.Contains("smart", StringComparison.OrdinalIgnoreCase))
            caps |= HostCapability.Smart;

        TimeSpan? uptime = null;
        if (double.TryParse(
                upText,
                System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture,
                out var seconds))
            uptime = TimeSpan.FromSeconds(seconds);

        var roots = rootsText
            .Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(x => x.Trim())
            .Where(x => x.Length > 0)
            .Select(ParseStorageRoot)
            .Where(x => x is not null)
            .Select(x => x!)
            .ToArray();

        return new HostProbeResult
        {
            ConnectionKind = HostConnectionKind.RemoteLinux,
            Platform = HostPlatform.Linux,
            HostName = string.IsNullOrWhiteSpace(host) ? profile.Host : host,
            OperatingSystem = string.IsNullOrWhiteSpace(os) ? "Linux" : os,
            Architecture = string.IsNullOrWhiteSpace(arch) ? "--" : arch,
            Uptime = uptime,
            Capabilities = caps,
            StorageRoots = roots,
            Detail = "Remote Linux provider over the existing GraveOps SSH trust model."
        };
    }

    private static string? ParseStorageRoot(string line)
    {
        var parts = line.Split(' ', 3, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length < 3)
            return null;

        var source = parts[0];
        var mount = parts[1];
        var fileSystem = parts[2];
        return LinuxStorageFilter.IsMeaningful(source, fileSystem, mount)
            ? $"{mount} [{fileSystem}] <- {source}"
            : null;
    }

    private static string Section(string text, string start, string end)
    {
        var startAt = text.IndexOf(start, StringComparison.Ordinal);
        if (startAt < 0)
            return "";

        startAt += start.Length;
        var endAt = text.IndexOf(end, startAt, StringComparison.Ordinal);
        return endAt < 0
            ? text[startAt..]
            : text[startAt..endAt];
    }
}
