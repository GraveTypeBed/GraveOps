namespace GraveOps.App.Models;

public sealed class DetectedIntegrationOption
{
    public string Key { get; init; } = "";
    public string DisplayName { get; init; } = "";
    public IntegrationCategory Category { get; init; }
    public int? Port { get; init; }
    public string Url { get; init; } = "";
    public string Evidence { get; init; } = "";
    public bool Enabled { get; set; } = true;

    public string CategoryText => Category switch
    {
        IntegrationCategory.QualityAutomation => "Quality & Automation",
        _ => Category.ToString()
    };

    public string PortText => Port is { } port ? port.ToString() : "local";
}

public sealed class LocalWindowsDiscoveryResult
{
    public HostProbeResult Host { get; init; } = new();
    public IReadOnlyList<DetectedIntegrationOption> Integrations { get; init; } = Array.Empty<DetectedIntegrationOption>();
    public bool DockerAvailable => Host.Capabilities.HasFlag(HostCapability.Docker);
}

public sealed class RemoteLinuxDiscoveryResult
{
    public HostProbeResult Host { get; init; } = new();
    public IReadOnlyList<DetectedIntegrationOption> Integrations { get; init; } = Array.Empty<DetectedIntegrationOption>();
    public IReadOnlyList<int> ListeningPorts { get; init; } = Array.Empty<int>();
}
