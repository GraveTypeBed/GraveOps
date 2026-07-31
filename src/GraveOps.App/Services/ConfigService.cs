using GraveOps.App.Models;

namespace GraveOps.App.Services;

public sealed class ConfigService
{
    private readonly JsonSerializerOptions _json = new() { WriteIndented = true, PropertyNameCaseInsensitive = true };
    public string DirectoryPath { get; } = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), ProductIdentity.DataDirectoryName);
    public string FilePath => Path.Combine(DirectoryPath, "config.json");
    public AppConfig Current { get; private set; } = new();

    public void Load()
    {
        Directory.CreateDirectory(DirectoryPath);
        if (!File.Exists(FilePath)) { Current = new AppConfig(); return; }
        try { Current = JsonSerializer.Deserialize<AppConfig>(File.ReadAllText(FilePath), _json) ?? new AppConfig(); }
        catch { Current = new AppConfig(); }
    }

    public void Save()
    {
        Directory.CreateDirectory(DirectoryPath);
        var temp = FilePath + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(Current, _json));
        File.Move(temp, FilePath, true);
    }

    public ServerProfile? GetSelectedServer()
    {
        var id = Current.SelectedServerId;
        return id is null ? Current.Servers.FirstOrDefault() : Current.Servers.FirstOrDefault(s => s.Id == id) ?? Current.Servers.FirstOrDefault();
    }
}
