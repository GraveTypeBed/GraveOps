namespace GraveOps.App.Models;

public sealed class RemoteFileItem
{
    public string Name { get; set; } = "";
    public string FullPath { get; set; } = "";
    public bool IsDirectory { get; set; }
    public long Size { get; set; }
    public DateTime LastWriteTime { get; set; }
    public string Type => IsDirectory ? "Folder" : "File";
    public string DisplaySize => IsDirectory ? "" : FormatBytes(Size);

    private static string FormatBytes(long bytes)
    {
        string[] units = ["B", "KB", "MB", "GB", "TB"];
        double value = bytes;
        int i = 0;
        while (value >= 1024 && i < units.Length - 1) { value /= 1024; i++; }
        return $"{value:0.##} {units[i]}";
    }
}
