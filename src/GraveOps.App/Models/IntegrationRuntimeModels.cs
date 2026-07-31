namespace GraveOps.App.Models;

public sealed class IntegrationRuntimeStatus
{
    public string Name { get; init; } = "Integration";
    public AppHealthState Health { get; init; } = AppHealthState.Stale;
    public string StateText { get; init; } = "Unknown";
    public string Owner { get; init; } = "Unknown";
    public string Endpoint { get; init; } = "Not identified";
    public string Runtime { get; init; } = "Unknown";
    public string Detail { get; init; } = "";
    public string DiscoveryEvidence { get; init; } = "";
    public bool CanOpen { get; init; }
    public bool CanPreviewRecyclarr { get; init; }

    // GraveOps operational telemetry. These fields intentionally avoid
    // application secrets: endpoint reachability and host/container telemetry
    // are gathered through the owning host provider.
    public string HttpText { get; init; } = "--";
    public string LatencyText { get; init; } = "--";
    public string RuntimeStateText { get; init; } = "--";
    public string BuildText { get; init; } = "--";
    public string CpuText { get; init; } = "--";
    public string MemoryText { get; init; } = "--";
    public string UptimeText { get; init; } = "--";
    public string ReadinessText { get; init; } = "--";
    public string RuntimeDetail { get; init; } = "No runtime detail available.";
}

public sealed class RecyclarrPreviewResult
{
    public bool Success { get; init; }
    public string Output { get; init; } = "";
}

public sealed class RecyclarrInstanceInfo
{
    public string Service { get; init; } = "";
    public string Name { get; init; } = "";
    public string Display => $"{Service} / {Name}";
}
