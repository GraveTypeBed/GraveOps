using System.Text.Json;

namespace GraveOps.Desktop.Linux;

public sealed class LinuxMediaLauncherProfile
{
    public string IntegrationName { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    public string UrlOverride { get; set; } = string.Empty;
    public bool IsVisible { get; set; } = true;
}

public sealed class LinuxMediaLauncherStore
{
    private readonly JsonSerializerOptions _json =
        new()
        {
            WriteIndented = true
        };

    private List<LinuxMediaLauncherProfile> _profiles;

    public LinuxMediaLauncherStore()
    {
        ConfigDirectory = Path.Combine(
            Environment.GetFolderPath(
                Environment.SpecialFolder.ApplicationData),
            "GraveOps");

        Directory.CreateDirectory(ConfigDirectory);

        FilePath = Path.Combine(
            ConfigDirectory,
            "media-launchers.json");

        _profiles = Load();
    }

    public string ConfigDirectory { get; }

    public string FilePath { get; }

    public LinuxMediaLauncherProfile? Get(
        string integrationName) =>
        _profiles.FirstOrDefault(item =>
            item.IntegrationName.Equals(
                integrationName,
                StringComparison.OrdinalIgnoreCase));

    public string? ResolveUrl(
        string integrationName)
    {
        var value =
            Get(integrationName)?.UrlOverride?.Trim();

        return string.IsNullOrWhiteSpace(value)
            ? null
            : value;
    }

    public LinuxMediaLauncherProfile Save(
        LinuxMediaLauncherProfile profile)
    {
        Validate(profile);

        var normalized =
            new LinuxMediaLauncherProfile
            {
                IntegrationName =
                    profile.IntegrationName.Trim(),
                DisplayName =
                    profile.DisplayName.Trim(),
                Category =
                    profile.Category.Trim(),
                UrlOverride =
                    profile.UrlOverride.Trim(),
                IsVisible =
                    profile.IsVisible
            };

        var existing =
            Get(normalized.IntegrationName);

        if (existing is null)
        {
            _profiles.Add(normalized);
        }
        else
        {
            var index =
                _profiles.IndexOf(existing);

            _profiles[index] =
                normalized;
        }

        SaveDocument();
        return normalized;
    }

    public bool Reset(
        string integrationName)
    {
        var existing =
            Get(integrationName);

        if (existing is null)
            return false;

        var removed =
            _profiles.Remove(existing);

        if (removed)
            SaveDocument();

        return removed;
    }

    private List<LinuxMediaLauncherProfile> Load()
    {
        try
        {
            if (!File.Exists(FilePath))
                return new List<LinuxMediaLauncherProfile>();

            return JsonSerializer.Deserialize<
                       List<LinuxMediaLauncherProfile>>(
                       File.ReadAllText(FilePath),
                       _json) ??
                   new List<LinuxMediaLauncherProfile>();
        }
        catch
        {
            return new List<LinuxMediaLauncherProfile>();
        }
    }

    private void SaveDocument()
    {
        var temporary =
            FilePath + ".tmp";

        File.WriteAllText(
            temporary,
            JsonSerializer.Serialize(
                _profiles
                    .OrderBy(item =>
                        item.IntegrationName)
                    .ToArray(),
                _json));

        File.Move(
            temporary,
            FilePath,
            overwrite: true);
    }

    private static void Validate(
        LinuxMediaLauncherProfile profile)
    {
        if (string.IsNullOrWhiteSpace(
                profile.IntegrationName))
        {
            throw new InvalidOperationException(
                "A detected application is required.");
        }

        if (!string.IsNullOrWhiteSpace(
                profile.UrlOverride) &&
            (!Uri.TryCreate(
                 profile.UrlOverride.Trim(),
                 UriKind.Absolute,
                 out var uri) ||
             (uri.Scheme != Uri.UriSchemeHttp &&
              uri.Scheme != Uri.UriSchemeHttps)))
        {
            throw new InvalidOperationException(
                "URL override must be a complete http:// or https:// address.");
        }
    }
}
