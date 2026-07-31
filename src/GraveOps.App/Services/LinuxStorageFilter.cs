namespace GraveOps.App.Services;

internal static class LinuxStorageFilter
{
    private static readonly HashSet<string> PseudoFileSystems = new(StringComparer.OrdinalIgnoreCase)
    {
        "tmpfs", "devtmpfs", "efivarfs", "proc", "sysfs", "cgroup", "cgroup2",
        "pstore", "securityfs", "debugfs", "tracefs", "configfs", "mqueue",
        "hugetlbfs", "fusectl", "autofs", "ramfs", "overlay", "squashfs", "nsfs"
    };

    public static bool IsMeaningful(string source, string fileSystem, string mountPoint)
    {
        source = source.Trim();
        fileSystem = fileSystem.Trim();
        mountPoint = mountPoint.Trim();

        if (string.IsNullOrWhiteSpace(mountPoint))
            return false;

        if (PseudoFileSystems.Contains(fileSystem))
            return false;

        if (source.Equals("tmpfs", StringComparison.OrdinalIgnoreCase) ||
            source.Equals("udev", StringComparison.OrdinalIgnoreCase) ||
            source.Equals("overlay", StringComparison.OrdinalIgnoreCase) ||
            source.Equals("shm", StringComparison.OrdinalIgnoreCase) ||
            source.StartsWith("/dev/loop", StringComparison.OrdinalIgnoreCase))
            return false;

        if (mountPoint.Equals("/boot", StringComparison.OrdinalIgnoreCase) ||
            mountPoint.StartsWith("/boot/", StringComparison.OrdinalIgnoreCase))
            return false;

        if (mountPoint.Equals("/proc", StringComparison.OrdinalIgnoreCase) ||
            mountPoint.StartsWith("/proc/", StringComparison.OrdinalIgnoreCase) ||
            mountPoint.Equals("/sys", StringComparison.OrdinalIgnoreCase) ||
            mountPoint.StartsWith("/sys/", StringComparison.OrdinalIgnoreCase) ||
            mountPoint.Equals("/dev", StringComparison.OrdinalIgnoreCase) ||
            mountPoint.StartsWith("/dev/", StringComparison.OrdinalIgnoreCase))
            return false;

        if ((mountPoint.Equals("/run", StringComparison.OrdinalIgnoreCase) ||
             mountPoint.StartsWith("/run/", StringComparison.OrdinalIgnoreCase)) &&
            !mountPoint.StartsWith("/run/media/", StringComparison.OrdinalIgnoreCase))
            return false;

        return true;
    }
}
