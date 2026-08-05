namespace GraveOps.Core.Targets;

public enum TargetPlatform
{
    Unknown = 0,
    Linux = 1,
    Windows = 2
}

public enum TargetLocation
{
    Local = 0,
    Remote = 1
}

public static class HostProviderIds
{
    public const string LocalLinux = "local-linux";
    public const string LocalWindows = "local-windows";
    public const string RemoteLinuxSsh = "remote-linux-ssh";
    public const string RemoteWindows = "remote-windows";
}

public static class TransportIds
{
    public const string Local = "local";
    public const string Ssh = "ssh";
    public const string WinRmHttps = "winrm-https";
}

public sealed record TargetConnectionProfile(
    string TransportId,
    string? Host = null,
    int? Port = null,
    string? Username = null,
    string? CredentialReference = null,
    string? PinnedIdentity = null,
    IReadOnlyDictionary<string, string>? Options = null)
{
    public static TargetConnectionProfile Local { get; } =
        new(TransportIds.Local);
}

public sealed record TargetProfile(
    string Id,
    string DisplayName,
    string ProviderId,
    TargetPlatform Platform,
    TargetLocation Location,
    TargetConnectionProfile Connection,
    IReadOnlyDictionary<string, string>? Metadata = null)
{
    public bool IsLocal => Location == TargetLocation.Local;

    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(Id))
            throw new InvalidOperationException("Target ID is required.");

        if (string.IsNullOrWhiteSpace(DisplayName))
            throw new InvalidOperationException("Target display name is required.");

        if (string.IsNullOrWhiteSpace(ProviderId))
            throw new InvalidOperationException("Target provider ID is required.");

        if (string.IsNullOrWhiteSpace(Connection.TransportId))
            throw new InvalidOperationException("Target transport ID is required.");

        if (IsLocal)
            return;

        if (string.IsNullOrWhiteSpace(Connection.Host))
            throw new InvalidOperationException("Remote target host is required.");

        if (Connection.Port is < 1 or > 65535)
            throw new InvalidOperationException(
                "Remote target port must be between 1 and 65535.");
    }
}
