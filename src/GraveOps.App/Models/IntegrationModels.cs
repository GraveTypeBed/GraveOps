namespace GraveOps.App.Models;

public enum IntegrationCategory
{
    Library,
    Acquisition,
    Downloads,
    QualityAutomation,
    Processing,
    Lifecycle,
    Network,
    Infrastructure
}

public sealed record IntegrationDefinition(
    string Key,
    string DisplayName,
    IntegrationCategory Category,
    bool FirstClass,
    string Description);
