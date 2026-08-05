using System.Text.Json;
using System.Text.RegularExpressions;
using GraveOps.Core.Targets;

namespace GraveOps.Core.Applications;

public sealed record CachedApplicationInventory(
    string Id,
    string ProductId,
    string DisplayName,
    string OwnerTargetId,
    ApplicationRole Role,
    ApplicationRuntimeKind Runtime,
    IReadOnlyDictionary<string, string> Metadata);

public sealed record CachedTargetApplicationInventory(
    string TargetId,
    string DisplayName,
    DateTimeOffset CapturedAt,
    IReadOnlyList<string> CapabilityIds,
    IReadOnlyList<CachedApplicationInventory> Applications);

public sealed record ApplicationInventoryCacheDocument(
    int SchemaVersion,
    DateTimeOffset SavedAt,
    IReadOnlyList<CachedTargetApplicationInventory> Targets)
{
    public const int CurrentSchemaVersion = 1;

    public static ApplicationInventoryCacheDocument Empty =>
        new(
            CurrentSchemaVersion,
            DateTimeOffset.MinValue,
            Array.Empty<CachedTargetApplicationInventory>());
}

public sealed record ApplicationInventoryCacheLoadResult(
    ApplicationInventoryCacheDocument Document,
    IReadOnlyList<string> Warnings);

public sealed class ApplicationInventoryCacheStore
{
    private const int MaximumTargets = 100;
    private const int MaximumApplicationsPerTarget = 2000;
    private const int MaximumIdentifierLength = 1024;
    private const int MaximumDisplayLength = 256;
    private const int MaximumMetadataLength = 160;

    private static readonly HashSet<string> AllowedMetadataKeys =
        new(
            new[]
            {
                "category",
                "identity-role",
                "protocol",
                "kind",
                "verification-state",
                "verified",
                "owns-health",
                "visible",
                "show-in-navigation"
            },
            StringComparer.OrdinalIgnoreCase);

    private static readonly Regex SensitiveValue =
        new(
            @"(?i)(api[_ -]?key|token|password|passphrase|secret|authorization|bearer\s+)",
            RegexOptions.Compiled |
            RegexOptions.CultureInvariant);

    private readonly JsonSerializerOptions _json =
        new()
        {
            WriteIndented = true,
            PropertyNameCaseInsensitive = true
        };

    public ApplicationInventoryCacheStore(
        string filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath))
        {
            throw new ArgumentException(
                "A cache path is required.",
                nameof(filePath));
        }

        FilePath =
            Path.GetFullPath(filePath);
    }

    public string FilePath { get; }

    public static CachedTargetApplicationInventory CreateTarget(
        string targetId,
        string displayName,
        DateTimeOffset capturedAt,
        TargetCapabilities capabilities,
        IEnumerable<ApplicationInstance> applications)
    {
        ArgumentNullException.ThrowIfNull(capabilities);
        ArgumentNullException.ThrowIfNull(applications);

        return NormalizeTarget(
            new CachedTargetApplicationInventory(
                targetId,
                displayName,
                capturedAt,
                capabilities.Values
                    .OrderBy(
                        value => value,
                        StringComparer.OrdinalIgnoreCase)
                    .ToArray(),
                applications
                    .Select(ToCachedApplication)
                    .ToArray()));
    }

    public static ApplicationInstance ToApplicationInstance(
        CachedApplicationInventory cached,
        TargetCapabilities capabilities)
    {
        ArgumentNullException.ThrowIfNull(cached);
        ArgumentNullException.ThrowIfNull(capabilities);

        var application =
            new ApplicationInstance(
                cached.Id,
                cached.ProductId,
                cached.DisplayName,
                cached.OwnerTargetId,
                cached.Role,
                cached.Runtime,
                ManagementEndpoint: null,
                capabilities,
                cached.Metadata);

        application.Validate();
        return application;
    }

    public void Save(
        IEnumerable<CachedTargetApplicationInventory> targets)
    {
        ArgumentNullException.ThrowIfNull(targets);

        var normalized =
            targets
                .Take(MaximumTargets)
                .Select(NormalizeTarget)
                .Where(target =>
                    !string.IsNullOrWhiteSpace(target.TargetId))
                .GroupBy(
                    target => target.TargetId,
                    StringComparer.OrdinalIgnoreCase)
                .Select(group =>
                    group
                        .OrderByDescending(item => item.CapturedAt)
                        .First())
                .OrderBy(
                    target => target.DisplayName,
                    StringComparer.OrdinalIgnoreCase)
                .ThenBy(
                    target => target.TargetId,
                    StringComparer.OrdinalIgnoreCase)
                .ToArray();

        var document =
            new ApplicationInventoryCacheDocument(
                ApplicationInventoryCacheDocument.CurrentSchemaVersion,
                DateTimeOffset.UtcNow,
                normalized);

        var directory =
            Path.GetDirectoryName(FilePath);

        if (!string.IsNullOrWhiteSpace(directory))
            Directory.CreateDirectory(directory);

        var temporary =
            FilePath + ".tmp";

        File.WriteAllText(
            temporary,
            JsonSerializer.Serialize(document, _json));

        File.Move(
            temporary,
            FilePath,
            overwrite: true);

        RestrictFilePermissions();
    }

    public ApplicationInventoryCacheLoadResult Load()
    {
        var warnings =
            new List<string>();

        if (!File.Exists(FilePath))
        {
            return new ApplicationInventoryCacheLoadResult(
                ApplicationInventoryCacheDocument.Empty,
                warnings);
        }

        try
        {
            var document =
                JsonSerializer.Deserialize<
                    ApplicationInventoryCacheDocument>(
                    File.ReadAllText(FilePath),
                    _json);

            if (document is null)
            {
                warnings.Add(
                    "The fleet inventory cache was empty.");

                return new ApplicationInventoryCacheLoadResult(
                    ApplicationInventoryCacheDocument.Empty,
                    warnings);
            }

            if (document.SchemaVersion !=
                ApplicationInventoryCacheDocument.CurrentSchemaVersion)
            {
                warnings.Add(
                    $"Unsupported fleet inventory cache schema " +
                    $"{document.SchemaVersion}.");

                return new ApplicationInventoryCacheLoadResult(
                    ApplicationInventoryCacheDocument.Empty,
                    warnings);
            }

            var targets =
                (document.Targets ??
                 Array.Empty<CachedTargetApplicationInventory>())
                    .Take(MaximumTargets)
                    .Select(NormalizeTarget)
                    .Where(target =>
                        !string.IsNullOrWhiteSpace(target.TargetId))
                    .GroupBy(
                        target => target.TargetId,
                        StringComparer.OrdinalIgnoreCase)
                    .Select(group =>
                        group
                            .OrderByDescending(item => item.CapturedAt)
                            .First())
                    .ToArray();

            return new ApplicationInventoryCacheLoadResult(
                document with
                {
                    Targets = targets
                },
                warnings);
        }
        catch (Exception exception)
        {
            warnings.Add(
                $"Could not read fleet inventory cache: " +
                $"{exception.Message}");

            return new ApplicationInventoryCacheLoadResult(
                ApplicationInventoryCacheDocument.Empty,
                warnings);
        }
    }

    private static CachedApplicationInventory ToCachedApplication(
        ApplicationInstance application)
    {
        application.Validate();

        return NormalizeApplication(
            new CachedApplicationInventory(
                application.Id,
                application.ProductId,
                application.DisplayName,
                application.OwnerTargetId,
                application.Role,
                application.Runtime,
                SanitizeMetadata(application.Metadata)));
    }

    private static CachedTargetApplicationInventory NormalizeTarget(
        CachedTargetApplicationInventory target)
    {
        var targetId =
            SanitizeText(
                target.TargetId,
                MaximumIdentifierLength);
        var displayName =
            SanitizeText(
                target.DisplayName,
                MaximumDisplayLength);

        var capabilities =
            (target.CapabilityIds ??
             Array.Empty<string>())
                .Select(value =>
                    SanitizeText(
                        value,
                        MaximumMetadataLength))
                .Where(value =>
                    !string.IsNullOrWhiteSpace(value))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(
                    value => value,
                    StringComparer.OrdinalIgnoreCase)
                .ToArray();

        var applications =
            (target.Applications ??
             Array.Empty<CachedApplicationInventory>())
                .Take(MaximumApplicationsPerTarget)
                .Select(NormalizeApplication)
                .Where(application =>
                    !string.IsNullOrWhiteSpace(application.Id) &&
                    application.OwnerTargetId.Equals(
                        targetId,
                        StringComparison.OrdinalIgnoreCase))
                .GroupBy(
                    application => application.Id,
                    StringComparer.OrdinalIgnoreCase)
                .Select(group => group.First())
                .OrderBy(
                    application => application.ProductId,
                    StringComparer.OrdinalIgnoreCase)
                .ThenBy(
                    application => application.DisplayName,
                    StringComparer.OrdinalIgnoreCase)
                .ThenBy(
                    application => application.Id,
                    StringComparer.OrdinalIgnoreCase)
                .ToArray();

        return new CachedTargetApplicationInventory(
            targetId,
            string.IsNullOrWhiteSpace(displayName)
                ? targetId
                : displayName,
            target.CapturedAt,
            capabilities,
            applications);
    }

    private static CachedApplicationInventory NormalizeApplication(
        CachedApplicationInventory application) =>
        new(
            SanitizeText(
                application.Id,
                MaximumIdentifierLength),
            SanitizeText(
                application.ProductId,
                MaximumDisplayLength),
            SanitizeText(
                application.DisplayName,
                MaximumDisplayLength),
            SanitizeText(
                application.OwnerTargetId,
                MaximumIdentifierLength),
            Enum.IsDefined(application.Role)
                ? application.Role
                : ApplicationRole.Unknown,
            Enum.IsDefined(application.Runtime)
                ? application.Runtime
                : ApplicationRuntimeKind.Unknown,
            SanitizeMetadata(application.Metadata));

    private static IReadOnlyDictionary<string, string> SanitizeMetadata(
        IReadOnlyDictionary<string, string>? metadata)
    {
        if (metadata is null ||
            metadata.Count == 0)
        {
            return new Dictionary<string, string>(
                StringComparer.OrdinalIgnoreCase);
        }

        var sanitized =
            new Dictionary<string, string>(
                StringComparer.OrdinalIgnoreCase);

        foreach (var item in metadata)
        {
            if (!AllowedMetadataKeys.Contains(item.Key))
                continue;

            var value =
                SanitizeText(
                    item.Value,
                    MaximumMetadataLength);

            if (string.IsNullOrWhiteSpace(value) ||
                SensitiveValue.IsMatch(value))
            {
                continue;
            }

            sanitized[item.Key.ToLowerInvariant()] =
                value;
        }

        return sanitized;
    }

    private static string SanitizeText(
        string? value,
        int maximumLength)
    {
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;

        var sanitized =
            new string(
                value
                    .Where(character =>
                        !char.IsControl(character))
                    .ToArray())
                .Trim();

        return sanitized.Length <= maximumLength
            ? sanitized
            : sanitized[..maximumLength];
    }

    private void RestrictFilePermissions()
    {
        if (!OperatingSystem.IsLinux() &&
            !OperatingSystem.IsMacOS())
        {
            return;
        }

        try
        {
            File.SetUnixFileMode(
                FilePath,
                UnixFileMode.UserRead |
                UnixFileMode.UserWrite);
        }
        catch
        {
            // Filesystems without Unix modes remain supported.
        }
    }
}
