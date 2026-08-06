using GraveOps.Core.Targets;

namespace GraveOps.Platform.Linux;

public static class LinuxTargetCapabilityCatalog
{
    private static readonly string[] CommonCapabilities =
    {
        CapabilityIds.HostSummaryRead,
        CapabilityIds.StorageRead,
        CapabilityIds.ServicesRead,
        CapabilityIds.ContainersRead,
        CapabilityIds.JournalRead,
        CapabilityIds.ApplicationDiscovery
    };

    public static TargetCapabilities ForTarget(
        bool isLocal)
    {
        var capabilities =
            new TargetCapabilities(
                CommonCapabilities);

        return isLocal
            ? capabilities.Union(
                new[]
                {
                    CapabilityIds.ApplicationApiTelemetry,
                    CapabilityIds.BackupInventoryRead
                })
            : capabilities;
    }
}
