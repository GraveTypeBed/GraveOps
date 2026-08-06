using System.Security;
using System.Text.Json;
using System.Xml.Linq;
using Microsoft.Win32;
using GraveOps.Core.Security;
using GraveOps.Core.Targets;
using GraveOps.Core.Telemetry;

namespace GraveOps.Desktop.Windows;

public sealed class WindowsPlexTargetConfiguration
{
    public string TargetId { get; set; } =
        string.Empty;

    public string Endpoint { get; set; } =
        string.Empty;
}

public sealed class WindowsPlexConfigurationDocument
{
    public List<WindowsPlexTargetConfiguration>
        Targets { get; set; } =
            new();
}

public static class WindowsPlexEndpointPolicy
{
    public static Uri DefaultFor(
        TargetProfile target)
    {
        ArgumentNullException.ThrowIfNull(
            target);

        var host =
            target.IsLocal
                ? "127.0.0.1"
                : target.Connection.Host ??
                  throw new InvalidOperationException(
                      "The remote target host is required.");

        return PlexTelemetryEndpoint.Normalize(
            new UriBuilder(
                Uri.UriSchemeHttp,
                host,
                32400)
            .Uri);
    }

    public static Uri Normalize(
        string endpoint) =>
        PlexTelemetryEndpoint.Normalize(
            endpoint);
}

public sealed class WindowsPlexConfigurationStore
{
    private static readonly JsonSerializerOptions
        JsonOptions =
            new()
            {
                WriteIndented =
                    true,

                PropertyNameCaseInsensitive =
                    true
            };

    private readonly SemaphoreSlim _gate =
        new(
            1,
            1);

    private readonly string _path;

    public WindowsPlexConfigurationStore(
        string? path = null)
    {
        _path =
            path ??
            Path.Combine(
                Environment.GetFolderPath(
                    Environment.SpecialFolder.LocalApplicationData),
                "GraveOps",
                "plex-targets.json");
    }

    public async Task<WindowsPlexTargetConfiguration>
        ResolveAsync(
            TargetProfile target,
            CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(
            target);

        await _gate.WaitAsync(
            cancellationToken);

        try
        {
            var document =
                await LoadUnsafeAsync(
                    cancellationToken);

            var existing =
                document.Targets
                    .FirstOrDefault(item =>
                        item.TargetId.Equals(
                            target.Id,
                            StringComparison.Ordinal));

            if (existing is not null)
            {
                return new WindowsPlexTargetConfiguration
                {
                    TargetId =
                        target.Id,

                    Endpoint =
                        WindowsPlexEndpointPolicy
                            .Normalize(
                                existing.Endpoint)
                            .AbsoluteUri
                };
            }

            return new WindowsPlexTargetConfiguration
            {
                TargetId =
                    target.Id,

                Endpoint =
                    WindowsPlexEndpointPolicy
                        .DefaultFor(
                            target)
                        .AbsoluteUri
            };
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task SaveAsync(
        string targetId,
        string endpoint,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(
                targetId))
        {
            throw new ArgumentException(
                "The target ID is required.",
                nameof(targetId));
        }

        var normalized =
            WindowsPlexEndpointPolicy
                .Normalize(
                    endpoint)
                .AbsoluteUri;

        await _gate.WaitAsync(
            cancellationToken);

        try
        {
            var document =
                await LoadUnsafeAsync(
                    cancellationToken);

            document.Targets.RemoveAll(item =>
                item.TargetId.Equals(
                    targetId,
                    StringComparison.Ordinal));

            document.Targets.Add(
                new WindowsPlexTargetConfiguration
                {
                    TargetId =
                        targetId,

                    Endpoint =
                        normalized
                });

            document.Targets =
                document.Targets
                    .OrderBy(item =>
                        item.TargetId,
                        StringComparer.Ordinal)
                    .ToList();

            await SaveUnsafeAsync(
                document,
                cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<WindowsPlexConfigurationDocument>
        LoadUnsafeAsync(
            CancellationToken cancellationToken)
    {
        if (!File.Exists(
                _path))
        {
            return new WindowsPlexConfigurationDocument();
        }

        try
        {
            await using var stream =
                File.OpenRead(
                    _path);

            return
                await JsonSerializer.DeserializeAsync<
                    WindowsPlexConfigurationDocument>(
                    stream,
                    JsonOptions,
                    cancellationToken) ??
                new WindowsPlexConfigurationDocument();
        }
        catch (
            Exception exception)
            when (exception is
                JsonException or
                IOException or
                UnauthorizedAccessException)
        {
            return new WindowsPlexConfigurationDocument();
        }
    }

    private async Task SaveUnsafeAsync(
        WindowsPlexConfigurationDocument document,
        CancellationToken cancellationToken)
    {
        var directory =
            Path.GetDirectoryName(
                _path) ??
            throw new InvalidOperationException(
                "The Plex configuration path has no parent directory.");

        Directory.CreateDirectory(
            directory);

        var temporaryPath =
            _path +
            "." +
            Guid.NewGuid()
                .ToString("N") +
            ".tmp";

        try
        {
            await using (
                var stream =
                    new FileStream(
                        temporaryPath,
                        FileMode.CreateNew,
                        FileAccess.Write,
                        FileShare.None,
                        4096,
                        FileOptions.Asynchronous))
            {
                await JsonSerializer.SerializeAsync(
                    stream,
                    document,
                    JsonOptions,
                    cancellationToken);

                await stream.FlushAsync(
                    cancellationToken);
            }

            File.Move(
                temporaryPath,
                _path,
                overwrite: true);
        }
        finally
        {
            if (File.Exists(
                    temporaryPath))
            {
                File.Delete(
                    temporaryPath);
            }
        }
    }
}

public sealed class WindowsResolvedPlexSecret :
    IDisposable
{
    public WindowsResolvedPlexSecret(
        SecretValue secret,
        string source)
    {
        Secret =
            secret ??
            throw new ArgumentNullException(
                nameof(secret));

        Source =
            string.IsNullOrWhiteSpace(
                source)
                ? "protected source"
                : source.Trim();
    }

    public SecretValue Secret { get; }

    public string Source { get; }

    public void Dispose() =>
        Secret.Dispose();
}

public sealed class WindowsPlexTokenDiscovery
{
    private const string PlexRegistryPath =
        @"Software\Plex, Inc.\Plex Media Server";

    private const string PlexTokenValueName =
        "PlexOnlineToken";

    private readonly IReadOnlyList<string>
        _candidatePaths;

    private readonly Func<string?>
        _registryTokenReader;

    public WindowsPlexTokenDiscovery(
        IEnumerable<string>? candidatePaths = null,
        Func<string?>? registryTokenReader = null)
    {
        _candidatePaths =
            (
                candidatePaths ??
                DefaultCandidatePaths()
            )
            .Where(path =>
                !string.IsNullOrWhiteSpace(
                    path))
            .Distinct(
                StringComparer.OrdinalIgnoreCase)
            .ToArray();

        _registryTokenReader =
            registryTokenReader ??
            ReadWindowsRegistryToken;
    }

    public WindowsResolvedPlexSecret?
        TryResolve()
    {
        try
        {
            var registryToken =
                _registryTokenReader()
                    ?.Trim();

            if (ValidToken(
                    registryToken))
            {
                return new WindowsResolvedPlexSecret(
                    new SecretValue(
                        registryToken!),
                    "Windows Plex registry");
            }
        }
        catch (
            Exception exception)
            when (exception is
                IOException or
                UnauthorizedAccessException or
                SecurityException)
        {
            // Continue to the protected file fallback.
        }

        foreach (var path in
                 _candidatePaths)
        {
            try
            {
                if (!File.Exists(
                        path))
                {
                    continue;
                }

                var attributes =
                    File.GetAttributes(
                        path);

                if (attributes.HasFlag(
                        FileAttributes.ReparsePoint))
                {
                    continue;
                }

                var document =
                    XDocument.Load(
                        path,
                        LoadOptions.None);

                var token =
                    document.Root?
                        .Attributes()
                        .FirstOrDefault(attribute =>
                            attribute.Name.LocalName.Equals(
                                PlexTokenValueName,
                                StringComparison.OrdinalIgnoreCase))
                        ?.Value
                        ?.Trim();

                if (!ValidToken(
                        token))
                {
                    continue;
                }

                return new WindowsResolvedPlexSecret(
                    new SecretValue(
                        token!),
                    "local Plex Preferences.xml fallback");
            }
            catch (
                Exception exception)
                when (exception is
                    IOException or
                    UnauthorizedAccessException or
                    SecurityException or
                    System.Xml.XmlException)
            {
                // Continue to the next protected candidate.
            }
        }

        return null;
    }

    private static string?
        ReadWindowsRegistryToken()
    {
        if (!OperatingSystem.IsWindows())
            return null;

        using var key =
            Registry.CurrentUser.OpenSubKey(
                PlexRegistryPath,
                writable: false);

        return key?
            .GetValue(
                PlexTokenValueName,
                null,
                RegistryValueOptions.DoNotExpandEnvironmentNames)
            as string;
    }

    private static bool ValidToken(
        string? token) =>
        !string.IsNullOrWhiteSpace(
            token) &&
        token.Length is >= 8 and <= 512 &&
        !token.Contains('\r') &&
        !token.Contains('\n');

    private static IEnumerable<string>
        DefaultCandidatePaths()
    {
        var localApplicationData =
            Environment.GetFolderPath(
                Environment.SpecialFolder.LocalApplicationData);

        if (!string.IsNullOrWhiteSpace(
                localApplicationData))
        {
            yield return Path.Combine(
                localApplicationData,
                "Plex Media Server",
                "Preferences.xml");
        }
    }
}

public sealed class WindowsPlexTelemetryService
{
    private static readonly HttpClient
        SharedClient =
            new(
                new SocketsHttpHandler
                {
                    UseProxy =
                        false,

                    ConnectTimeout =
                        TimeSpan.FromSeconds(
                            5)
                })
            {
                Timeout =
                    TimeSpan.FromSeconds(
                        10)
            };

    private readonly WindowsTargetSession
        _targetSession;

    private readonly WindowsPlexConfigurationStore
        _configuration;

    private readonly WindowsPlexTokenDiscovery
        _discovery;

    private readonly PlexTelemetryClient
        _client;

    public WindowsPlexTelemetryService(
        WindowsTargetSession targetSession,
        WindowsPlexConfigurationStore? configuration = null,
        WindowsPlexTokenDiscovery? discovery = null,
        PlexTelemetryClient? client = null)
    {
        _targetSession =
            targetSession ??
            throw new ArgumentNullException(
                nameof(targetSession));

        _configuration =
            configuration ??
            new WindowsPlexConfigurationStore();

        _discovery =
            discovery ??
            new WindowsPlexTokenDiscovery();

        _client =
            client ??
            new PlexTelemetryClient(
                SharedClient);
    }

    public async Task<Uri> ResolveEndpointAsync(
        TargetProfile target,
        CancellationToken cancellationToken = default)
    {
        var configuration =
            await _configuration.ResolveAsync(
                target,
                cancellationToken);

        return WindowsPlexEndpointPolicy.Normalize(
            configuration.Endpoint);
    }

    public async Task<PlexTelemetrySnapshot>
        CaptureAsync(
            TargetProfile target,
            CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(
            target);

        var endpoint =
            await ResolveEndpointAsync(
                target,
                cancellationToken);

        using var resolvedSecret =
            await ResolveSecretAsync(
                target,
                cancellationToken);

        var snapshot =
            await _client.CaptureAsync(
                new PlexTelemetryRequest(
                    endpoint,
                    ClientIdentifier(
                        target),
                    resolvedSecret?.Secret),
                cancellationToken);

        snapshot.Security =
            resolvedSecret is null
                ? snapshot.Security
                : "Protected Plex telemetry · " +
                  $"token source: {resolvedSecret.Source}";

        return snapshot;
    }

    public async Task<PlexTelemetrySnapshot>
        TestAndSaveAsync(
            TargetProfile target,
            string endpoint,
            string? suppliedToken,
            CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(
            target);

        var normalizedEndpoint =
            WindowsPlexEndpointPolicy.Normalize(
                endpoint);

        WindowsResolvedPlexSecret? resolvedSecret =
            null;

        var hasSuppliedToken =
            !string.IsNullOrWhiteSpace(
                suppliedToken);

        try
        {
            resolvedSecret =
                hasSuppliedToken
                    ? new WindowsResolvedPlexSecret(
                        new SecretValue(
                            suppliedToken!.Trim()),
                        "supplied token")
                    : await ResolveSecretAsync(
                        target,
                        cancellationToken);

            var snapshot =
                await _client.CaptureAsync(
                    new PlexTelemetryRequest(
                        normalizedEndpoint,
                        ClientIdentifier(
                            target),
                        resolvedSecret?.Secret,
                        RequireProtectedTelemetry:
                            resolvedSecret is not null),
                    cancellationToken);

            await _configuration.SaveAsync(
                target.Id,
                normalizedEndpoint.AbsoluteUri,
                cancellationToken);

            if (hasSuppliedToken)
            {
                await _targetSession.StorePlexTokenAsync(
                    target.Id,
                    suppliedToken!,
                    cancellationToken);

                snapshot.Security =
                    "Protected Plex telemetry · token stored in Windows Credential Manager";
            }
            else if (resolvedSecret is not null)
            {
                snapshot.Security =
                    "Protected Plex telemetry · " +
                    $"token source: {resolvedSecret.Source}";
            }

            return snapshot;
        }
        finally
        {
            resolvedSecret?.Dispose();
        }
    }

    public Task ClearTokenAsync(
        string targetId,
        CancellationToken cancellationToken = default) =>
        _targetSession.DeletePlexTokenAsync(
            targetId,
            cancellationToken);

    private async Task<WindowsResolvedPlexSecret?>
        ResolveSecretAsync(
            TargetProfile target,
            CancellationToken cancellationToken)
    {
        var stored =
            await _targetSession.RetrievePlexTokenAsync(
                target.Id,
                cancellationToken);

        if (stored is not null)
        {
            return new WindowsResolvedPlexSecret(
                stored,
                "Windows Credential Manager");
        }

        return target.IsLocal
            ? _discovery.TryResolve()
            : null;
    }

    private static string ClientIdentifier(
        TargetProfile target) =>
        "graveops-windows-" +
        Convert.ToHexString(
            System.Security.Cryptography.SHA256.HashData(
                System.Text.Encoding.UTF8.GetBytes(
                    target.Id)))
        .ToLowerInvariant()[..16];
}