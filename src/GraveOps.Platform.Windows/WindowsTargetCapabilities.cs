using GraveOps.Core.Targets;

namespace GraveOps.Platform.Windows;

public static class WindowsTargetCapabilityCatalog
{
    private static readonly string[] HostReadCapabilities =
    {
        CapabilityIds.HostSummaryRead,
        CapabilityIds.StorageRead,
        CapabilityIds.ServicesRead,
        CapabilityIds.ProcessesRead,
        CapabilityIds.InstalledApplicationsRead,
        CapabilityIds.NetworkListenersRead,
        CapabilityIds.ContainersRead,
        CapabilityIds.EventLogRead,
        CapabilityIds.ApplicationDiscovery
    };

    public static TargetCapabilities ForLocalTarget() =>
        new(
            HostReadCapabilities);

    public static TargetCapabilities ForRemoteTarget() =>
        new(
            HostReadCapabilities);
}
