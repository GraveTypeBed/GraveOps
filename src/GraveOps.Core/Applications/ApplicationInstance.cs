using GraveOps.Core.Targets;

namespace GraveOps.Core.Applications;

public enum ApplicationRole
{
    Unknown = 0,
    Server = 1,
    DesktopClient = 2,
    Service = 3,
    WebApplication = 4,
    Agent = 5
}

public enum ApplicationRuntimeKind
{
    Unknown = 0,
    NativeProcess = 1,
    WindowsService = 2,
    SystemdService = 3,
    Container = 4,
    RemoteApi = 5
}

public sealed record ApplicationInstance(
    string Id,
    string ProductId,
    string DisplayName,
    string OwnerTargetId,
    ApplicationRole Role,
    ApplicationRuntimeKind Runtime,
    Uri? ManagementEndpoint,
    TargetCapabilities Capabilities,
    IReadOnlyDictionary<string, string>? Metadata = null)
{
    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(Id))
            throw new InvalidOperationException("Application instance ID is required.");

        if (string.IsNullOrWhiteSpace(ProductId))
            throw new InvalidOperationException("Application product ID is required.");

        if (string.IsNullOrWhiteSpace(DisplayName))
            throw new InvalidOperationException("Application display name is required.");

        if (string.IsNullOrWhiteSpace(OwnerTargetId))
            throw new InvalidOperationException("Application owner target ID is required.");
    }
}

public sealed record ApplicationInventorySnapshot(
    DateTimeOffset CapturedAt,
    IReadOnlyList<ApplicationInstance> Applications,
    IReadOnlyList<string> Warnings);
