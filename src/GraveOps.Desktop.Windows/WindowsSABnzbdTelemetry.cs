using System.Text.Json;
using GraveOps.Core.Security;
using GraveOps.Core.Targets;
using GraveOps.Core.Telemetry;

namespace GraveOps.Desktop.Windows;

public sealed class WindowsSABnzbdTargetConfiguration
{
    public string TargetId { get; set; } =
        string.Empty;

    public string Endpoint { get; set; } =
        string.Empty;
}

public sealed class WindowsSABnzbdConfigurationDocument
{
    public List<WindowsSABnzbdTargetConfiguration>
        Targets { get; set; } =
            new();
}

public static class WindowsSABnzbdCredentialPolicy
{
    public static string NormalizeApiKey(
        string apiKey)
    {
        var normalized =
            apiKey?.Trim() ??
            string.Empty;

        if (normalized.Length is
            < 8 or > 512 ||
            normalized.Contains('\r') ||
            normalized.Contains('\n'))
        {
            throw new InvalidOperationException(
                "The SABnzbd API key must contain 8 to 512 characters on one line.");
        }

        return normalized;
    }
}

public static class WindowsSABnzbdTargetLease
{
    public static bool IsCurrent(
        string requestedTargetId,
        TargetProfile? currentTarget)
    {
        if (string.IsNullOrWhiteSpace(
                requestedTargetId) ||
            currentTarget is null)
        {
            return false;
        }

        return currentTarget.Id.Equals(
            requestedTargetId,
            StringComparison.Ordinal);
    }
}

public sealed class WindowsSABnzbdConfigurationStore
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

    public WindowsSABnzbdConfigurationStore(
        string? path = null)
    {
        _path =
            path ??
            Path.Combine(
                Environment.GetFolderPath(
                    Environment.SpecialFolder.LocalApplicationData),
                "GraveOps",
                "sabnzbd-targets.json");
    }

    public async Task<WindowsSABnzbdTargetConfiguration>
        ResolveAsync(
            TargetProfile target,
            CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(target);

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
                return new WindowsSABnzbdTargetConfiguration
                {
                    TargetId =
                        target.Id,

                    Endpoint =
                        SABnzbdTelemetryEndpoint
                            .Normalize(
                                existing.Endpoint)
                            .AbsoluteUri
                };
            }

            var host =
                target.IsLocal
                    ? "127.0.0.1"
                    : target.Connection.Host ??
                      throw new InvalidOperationException(
                          "The remote target host is required.");

            return new WindowsSABnzbdTargetConfiguration
            {
                TargetId =
                    target.Id,

                Endpoint =
                    SABnzbdTelemetryEndpoint
                        .Normalize(
                            new UriBuilder(
                                Uri.UriSchemeHttp,
                                host,
                                8080)
                            .Uri)
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

        var normalizedEndpoint =
            SABnzbdTelemetryEndpoint
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
                new WindowsSABnzbdTargetConfiguration
                {
                    TargetId =
                        targetId,

                    Endpoint =
                        normalizedEndpoint
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

    private async Task<WindowsSABnzbdConfigurationDocument>
        LoadUnsafeAsync(
            CancellationToken cancellationToken)
    {
        if (!File.Exists(
                _path))
        {
            return new WindowsSABnzbdConfigurationDocument();
        }

        try
        {
            await using var stream =
                File.OpenRead(
                    _path);

            return
                await JsonSerializer.DeserializeAsync<
                    WindowsSABnzbdConfigurationDocument>(
                    stream,
                    JsonOptions,
                    cancellationToken) ??
                new WindowsSABnzbdConfigurationDocument();
        }
        catch (
            Exception exception)
            when (exception is
                JsonException or
                IOException or
                UnauthorizedAccessException)
        {
            return new WindowsSABnzbdConfigurationDocument();
        }
    }

    private async Task SaveUnsafeAsync(
        WindowsSABnzbdConfigurationDocument document,
        CancellationToken cancellationToken)
    {
        var directory =
            Path.GetDirectoryName(
                _path) ??
            throw new InvalidOperationException(
                "The SABnzbd configuration path has no parent directory.");

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
                overwrite:
                    true);
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

public sealed class WindowsSABnzbdTelemetryService
{
    private static readonly HttpClient
        SharedClient =
            new(
                new SocketsHttpHandler
                {
                    AllowAutoRedirect =
                        false,

                    ConnectTimeout =
                        TimeSpan.FromSeconds(
                            3),

                    PooledConnectionLifetime =
                        TimeSpan.FromMinutes(
                            2),

                    PooledConnectionIdleTimeout =
                        TimeSpan.FromMinutes(
                            1),

                    MaxConnectionsPerServer =
                        4
                })
            {
                Timeout =
                    TimeSpan.FromSeconds(
                        8)
            };

    private readonly WindowsTargetSession
        _targetSession;

    private readonly WindowsSABnzbdConfigurationStore
        _configuration;

    private readonly SABnzbdTelemetryClient
        _client;

    public WindowsSABnzbdTelemetryService(
        WindowsTargetSession targetSession,
        WindowsSABnzbdConfigurationStore? configuration = null,
        SABnzbdTelemetryClient? client = null)
    {
        _targetSession =
            targetSession ??
            throw new ArgumentNullException(
                nameof(targetSession));

        _configuration =
            configuration ??
            new WindowsSABnzbdConfigurationStore();

        _client =
            client ??
            new SABnzbdTelemetryClient(
                SharedClient);
    }

    public Task<WindowsSABnzbdTargetConfiguration>
        ResolveConfigurationAsync(
            TargetProfile target,
            CancellationToken cancellationToken = default) =>
        _configuration.ResolveAsync(
            target,
            cancellationToken);

    public async Task<DownloadClientTelemetrySnapshot>
        CaptureAsync(
            TargetProfile target,
            CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(target);

        var configuration =
            await _configuration.ResolveAsync(
                target,
                cancellationToken);

        using var apiKey =
            await _targetSession.RetrieveApplicationSecretAsync(
                target.Id,
                "sabnzbd",
                "api-key",
                cancellationToken) ??
            throw new InvalidOperationException(
                "SABnzbd telemetry is not configured. " +
                "Enter the API key, then use Save + test.");

        return
            await _client.CaptureAsync(
                new SABnzbdTelemetryRequest(
                    SABnzbdTelemetryEndpoint.Normalize(
                        configuration.Endpoint),
                    apiKey),
                cancellationToken);
    }

    public async Task<DownloadClientTelemetrySnapshot>
        TestAndSaveAsync(
            TargetProfile target,
            string endpoint,
            string? suppliedApiKey,
            CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(target);

        var normalizedEndpoint =
            SABnzbdTelemetryEndpoint.Normalize(
                endpoint);

        var hasSuppliedApiKey =
            !string.IsNullOrWhiteSpace(
                suppliedApiKey);

        SecretValue? resolvedApiKey =
            null;

        SecretValue? previousApiKey =
            null;

        try
        {
            resolvedApiKey =
                hasSuppliedApiKey
                    ? new SecretValue(
                        WindowsSABnzbdCredentialPolicy
                            .NormalizeApiKey(
                                suppliedApiKey!))
                    : await _targetSession.RetrieveApplicationSecretAsync(
                        target.Id,
                        "sabnzbd",
                        "api-key",
                        cancellationToken);

            if (resolvedApiKey is null)
            {
                throw new InvalidOperationException(
                    "Enter the SABnzbd API key before using Save + test.");
            }

            var snapshot =
                await _client.CaptureAsync(
                    new SABnzbdTelemetryRequest(
                        normalizedEndpoint,
                        resolvedApiKey,
                        RequireCompleteTelemetry:
                            true),
                    cancellationToken);

            if (hasSuppliedApiKey)
            {
                previousApiKey =
                    await _targetSession.RetrieveApplicationSecretAsync(
                        target.Id,
                        "sabnzbd",
                        "api-key",
                        cancellationToken);

                await _targetSession.StoreApplicationSecretAsync(
                    target.Id,
                    "sabnzbd",
                    "api-key",
                    suppliedApiKey!,
                    cancellationToken);
            }

            try
            {
                await _configuration.SaveAsync(
                    target.Id,
                    normalizedEndpoint.AbsoluteUri,
                    cancellationToken);
            }
            catch
            {
                if (hasSuppliedApiKey)
                {
                    if (previousApiKey is null)
                    {
                        await _targetSession.DeleteApplicationSecretAsync(
                            target.Id,
                            "sabnzbd",
                            "api-key",
                            cancellationToken);
                    }
                    else
                    {
                        var previous =
                            new string(
                                previousApiKey.Reveal().Span);

                        await _targetSession.StoreApplicationSecretAsync(
                            target.Id,
                            "sabnzbd",
                            "api-key",
                            previous,
                            cancellationToken);
                    }
                }

                throw;
            }

            snapshot.Security =
                normalizedEndpoint.Scheme.Equals(
                    Uri.UriSchemeHttps,
                    StringComparison.OrdinalIgnoreCase) ||
                SABnzbdTelemetryEndpoint.IsLoopback(
                    normalizedEndpoint)
                    ? "API key stored in Windows Credential Manager."
                    : "API key stored in Windows Credential Manager; SABnzbd requests use HTTP to the configured LAN endpoint.";

            return snapshot;
        }
        finally
        {
            resolvedApiKey?.Dispose();
            previousApiKey?.Dispose();
        }
    }

    public Task ClearSavedApiKeyAsync(
        string targetId,
        CancellationToken cancellationToken = default) =>
        _targetSession.DeleteApplicationSecretAsync(
            targetId,
            "sabnzbd",
            "api-key",
            cancellationToken);
}
