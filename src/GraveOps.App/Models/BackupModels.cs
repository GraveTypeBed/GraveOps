namespace GraveOps.App.Models;

public enum BackupReadiness
{
    NotConfigured,
    Available,
    Configured,
    Attention
}

public sealed class BackupInventorySnapshot
{
    public BackupReadiness Readiness { get; init; }
    public string ProviderText { get; init; } = "Not configured";
    public IReadOnlyList<string> Evidence { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> Schedules { get; init; } = Array.Empty<string>();
    public IReadOnlyList<QuickAction> Actions { get; init; } = Array.Empty<QuickAction>();

    public string ReadinessText => Readiness switch
    {
        BackupReadiness.Configured => "CONFIGURED",
        BackupReadiness.Available => "AVAILABLE",
        BackupReadiness.Attention => "ATTENTION",
        _ => "NOT CONFIGURED"
    };
}
