using GraveOps.App.Models;

namespace GraveOps.App.Services;

public sealed class ProfileTransferService
{
    private readonly AppServices _services;
    private readonly JsonSerializerOptions _json = new() { WriteIndented = true, PropertyNameCaseInsensitive = true };
    public ProfileTransferService(AppServices services) => _services = services;

    public void Export(string path)
    {
        File.WriteAllText(path, JsonSerializer.Serialize(_services.Config.Current, _json), new UTF8Encoding(false));
    }

    public void Import(string path)
    {
        var incoming = JsonSerializer.Deserialize<AppConfig>(File.ReadAllText(path), _json) ?? throw new InvalidOperationException("The selected file is not a valid GraveOps configuration.");
        var dir = _services.Config.DirectoryPath;
        Directory.CreateDirectory(Path.Combine(dir, "profile-backups"));
        if (File.Exists(_services.Config.FilePath))
        {
            var backup = Path.Combine(dir, "profile-backups", $"config-before-import-{DateTime.Now:yyyyMMdd-HHmmss}.json");
            File.Copy(_services.Config.FilePath, backup, true);
        }
        File.WriteAllText(_services.Config.FilePath, JsonSerializer.Serialize(incoming, _json), new UTF8Encoding(false));
        _services.Config.Load();
    }
}