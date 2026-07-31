namespace GraveOps.App.Models;

public enum ActionRisk
{
    ReadOnly,
    Normal,
    Dangerous
}

public sealed class QuickAction
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "Action";
    public string Category { get; set; } = "Server";
    public string Command { get; set; } = "uname -a";
    public Guid? ServerId { get; set; }
    public ActionRisk Risk { get; set; } = ActionRisk.ReadOnly;
    public string RiskLabel => Risk == ActionRisk.Normal ? "STANDARD" : Risk.ToString().ToUpperInvariant();
    public string Description { get; set; } = "";
    public override string ToString() => Name;
}
