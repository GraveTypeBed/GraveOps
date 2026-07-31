namespace GraveOps.App.Models;

public enum SshAuthType
{
    Password,
    PrivateKey
}

public sealed class ServerProfile
{
    public HostConnectionKind ConnectionKind { get; set; } = HostConnectionKind.RemoteLinux;
    public string DetectedOperatingSystem { get; set; } = "";
    public List<string> EnabledModules { get; set; } = new();
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "New Server";
    public string Host { get; set; } = "";
    public int Port { get; set; } = 22;
    public string Username { get; set; } = "";
    public SshAuthType AuthType { get; set; } = SshAuthType.Password;
    public string PrivateKeyPath { get; set; } = "";
    public string HostKeyFingerprint { get; set; } = "";
    public string Role { get; set; } = "Linux";
    public bool UseForDashboard { get; set; }
    public string WakeMacAddress { get; set; } = "";
    public DateTimeOffset? LastIntegrationDiscoveryUtc { get; set; }
    public string IntegrationDiscoverySummary { get; set; } = "";

    public string PasswordCredentialTarget => $"{GraveOps.App.ProductIdentity.CredentialNamespace}/SSH/{Id:N}";
    public string KeyPassphraseCredentialTarget => $"{GraveOps.App.ProductIdentity.CredentialNamespace}/SSH-Key/{Id:N}";

    public override string ToString() => string.IsNullOrWhiteSpace(Name) ? Host : Name;
}
