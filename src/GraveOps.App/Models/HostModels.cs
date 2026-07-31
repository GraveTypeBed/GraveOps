namespace GraveOps.App.Models;

public enum HostConnectionKind
{
    RemoteLinux = 0,
    LocalWindows = 1,
    RemoteWindows = 2,
    LocalLinux = 3
}

public enum HostPlatform
{
    Unknown = 0,
    Windows = 1,
    Linux = 2
}

[Flags]
public enum HostCapability
{
    None = 0,
    Local = 1 << 0,
    Remote = 1 << 1,
    ProcessInspection = 1 << 2,
    ServiceControl = 1 << 3,
    FileSystem = 1 << 4,
    Storage = 1 << 5,
    LocalHttp = 1 << 6,
    Docker = 1 << 7,
    Smart = 1 << 8,
    EventLog = 1 << 9,
    Journal = 1 << 10,
    Ssh = 1 << 11,
    PowerShell = 1 << 12,
    Systemd = 1 << 13,
    WakeOnLan = 1 << 14
}

public sealed class HostProbeResult
{
    public HostConnectionKind ConnectionKind { get; init; }
    public HostPlatform Platform { get; init; }
    public string HostName { get; init; } = "--";
    public string OperatingSystem { get; init; } = "--";
    public string Architecture { get; init; } = "--";
    public TimeSpan? Uptime { get; init; }
    public HostCapability Capabilities { get; init; }
    public IReadOnlyList<string> StorageRoots { get; init; } = Array.Empty<string>();
    public IReadOnlyList<int> ListeningPorts { get; init; } = Array.Empty<int>();
    public IReadOnlyList<string> Evidence { get; init; } = Array.Empty<string>();
    public string Detail { get; init; } = "";
}
