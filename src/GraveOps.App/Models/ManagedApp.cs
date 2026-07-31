namespace GraveOps.App.Models;

public sealed class ManagedApp
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "Application";
    public string Url { get; set; } = "http://localhost";
    public Guid? ServerId { get; set; }
    public string Category { get; set; } = "Media";
    public bool OpenEmbedded { get; set; } = true;
    public bool DiscoveryVerified { get; set; }
    public string DiscoveryEvidence { get; set; } = "";
    public DateTimeOffset? DiscoveredUtc { get; set; }
    public override string ToString() => Name;
}
