using GraveOps.Core.Targets;

namespace GraveOps.Desktop.Linux;

public static class TargetNavigationPolicy
{
    public static bool IsSupported(
        string navigationName,
        TargetCapabilities capabilities)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(
            navigationName);
        ArgumentNullException.ThrowIfNull(
            capabilities);

        return navigationName switch
        {
            "ServicesNav" =>
                capabilities.Supports(
                    CapabilityIds.ServicesRead),
            "DockerNav" =>
                capabilities.Supports(
                    CapabilityIds.ContainersRead),
            "StorageNav" =>
                capabilities.Supports(
                    CapabilityIds.StorageRead),
            "LogsNav" =>
                capabilities.Supports(
                    CapabilityIds.JournalRead) ||
                capabilities.Supports(
                    CapabilityIds.EventLogRead),
            "BackupsNav" =>
                capabilities.Supports(
                    CapabilityIds.BackupInventoryRead),
            _ =>
                true
        };
    }

    public static string UnsupportedReason(
        string navigationName,
        TargetCapabilities capabilities)
    {
        if (IsSupported(
                navigationName,
                capabilities))
        {
            return string.Empty;
        }

        return navigationName switch
        {
            "ServicesNav" =>
                "The selected target does not report service inventory.",
            "DockerNav" =>
                "The selected target does not report container inventory.",
            "StorageNav" =>
                "The selected target does not report storage inventory.",
            "LogsNav" =>
                "The selected target reports neither Linux journal nor Windows event-log capability.",
            "BackupsNav" =>
                "Backup inventory is available only from a provider that explicitly reports it.",
            _ =>
                "The selected target does not support this workspace."
        };
    }
}
