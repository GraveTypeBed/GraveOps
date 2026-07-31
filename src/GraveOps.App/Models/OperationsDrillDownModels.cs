namespace GraveOps.App.Models;

public sealed class DockerDrillRow
{
    public string Name { get; set; } = "";
    public string Image { get; set; } = "";
    public string State { get; set; } = "";
    public string Health { get; set; } = "";
    public int Restarts { get; set; }
    public string Cpu { get; set; } = "--";
    public string Memory { get; set; } = "--";
    public string Started { get; set; } = "";
}

public sealed class StorageDrillRow
{
    public string Target { get; set; } = "";
    public string Source { get; set; } = "";
    public string FileSystem { get; set; } = "";
    public long Size { get; set; }
    public long Used { get; set; }
    public long Available { get; set; }
    public bool Writable { get; set; }

    public double UsagePercent => Size <= 0 ? 0 : Used * 100d / Size;
    public string UsageText => $"{UsagePercent:0.#}%";
    public string SizeText => FormatBytes(Size);
    public string UsedText => FormatBytes(Used);
    public string AvailableText => FormatBytes(Available);
    public string WritableText => Writable ? "Yes" : "No";

    private static string FormatBytes(long value)
    {
        var n = Math.Max(0d, value);
        string[] units = ["B", "KB", "MB", "GB", "TB", "PB"];
        var i = 0;
        while (n >= 1024d && i < units.Length - 1)
        {
            n /= 1024d;
            i++;
        }
        return $"{n:0.##} {units[i]}";
    }
}

public sealed class QueueDrillRow
{
    public string Service { get; set; } = "";
    public string Kind { get; set; } = "";
    public string Title { get; set; } = "";
    public string State { get; set; } = "";
    public string Progress { get; set; } = "";
    public string Remaining { get; set; } = "";
    public string Detail { get; set; } = "";
}