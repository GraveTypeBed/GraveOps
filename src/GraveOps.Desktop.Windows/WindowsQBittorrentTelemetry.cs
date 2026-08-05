using System.Text.Json;
using GraveOps.Core.Security;
using GraveOps.Core.Targets;
using GraveOps.Core.Telemetry;

namespace GraveOps.Desktop.Windows;

public sealed class WindowsQBittorrentTargetConfiguration
{
    public string TargetId { get; set; } = string.Empty;
    public string Endpoint { get; set; } = string.Empty;
    public string Username { get; set; } = string.Empty;
}

public sealed class WindowsQBittorrentConfigurationDocument
{
    public List<WindowsQBittorrentTargetConfiguration> Targets { get; set; } =
        new();
}

public static class WindowsQBittorrentCredentialPolicy
{
    public static string NormalizeUsername(string username)
    {
        var normalized = username?.Trim() ?? string.Empty;

        if (normalized.Length is < 1 or > 256 ||
            normalized.Contains('\r') ||
            normalized.Contains('\n'))
        {
            throw new InvalidOperationException(
                "The qBittorrent WebUI username must contain 1 to 256 characters on one line.");
        }

        return normalized;
    }

    public static string ValidatePassword(string password)
    {
        if (string.IsNullOrEmpty(password) ||
            password.Length > 512 ||
            password.Contains('\r') ||
            password.Contains('\n'))
        {
            throw new InvalidOperationException(
                "The qBittorrent WebUI password must contain 1 to 512 characters on one line.");
        }

        return password;
    }
}

public static class WindowsQBittorrentTargetLease
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

public sealed class WindowsQBittorrentConfigurationStore
{
    private static readonly JsonSerializerOptions JsonOptions =
        new()
        {
            WriteIndented = true,
            PropertyNameCaseInsensitive = true
        };

    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly string _path;

    public WindowsQBittorrentConfigurationStore(string? path = null)
    {
        _path =
            path ??
            Path.Combine(
                Environment.GetFolderPath(
                    Environment.SpecialFolder.LocalApplicationData),
                "GraveOps",
                "qbittorrent-targets.json");
    }

    public async Task<WindowsQBittorrentTargetConfiguration> ResolveAsync(
        TargetProfile target,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(target);

        await _gate.WaitAsync(cancellationToken);

        try
        {
            var document = await LoadUnsafeAsync(cancellationToken);

            var existing = document.Targets.FirstOrDefault(item =>
                item.TargetId.Equals(target.Id, StringComparison.Ordinal));

            if (existing is not null)
            {
                return new WindowsQBittorrentTargetConfiguration
                {
                    TargetId = target.Id,
                    Endpoint = QBittorrentTelemetryEndpoint
                        .Normalize(existing.Endpoint)
                        .AbsoluteUri,
                    Username = existing.Username?.Trim() ?? string.Empty
                };
            }

            var host =
                target.IsLocal
                    ? "127.0.0.1"
                    : target.Connection.Host ??
                      throw new InvalidOperationException(
                          "The remote target host is required.");

            return new WindowsQBittorrentTargetConfiguration
            {
                TargetId = target.Id,
                Endpoint = QBittorrentTelemetryEndpoint
                    .Normalize(
                        new UriBuilder(
                            Uri.UriSchemeHttp,
                            host,
                            8080)
                        .Uri)
                    .AbsoluteUri,
                Username = string.Empty
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
        string username,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(targetId))
        {
            throw new ArgumentException(
                "The target ID is required.",
                nameof(targetId));
        }

        var normalizedEndpoint =
            QBittorrentTelemetryEndpoint.Normalize(endpoint).AbsoluteUri;

        var normalizedUsername =
            WindowsQBittorrentCredentialPolicy.NormalizeUsername(username);

        await _gate.WaitAsync(cancellationToken);

        try
        {
            var document = await LoadUnsafeAsync(cancellationToken);

            document.Targets.RemoveAll(item =>
                item.TargetId.Equals(targetId, StringComparison.Ordinal));

            document.Targets.Add(
                new WindowsQBittorrentTargetConfiguration
                {
                    TargetId = targetId,
                    Endpoint = normalizedEndpoint,
                    Username = normalizedUsername
                });

            document.Targets = document.Targets
                .OrderBy(item => item.TargetId, StringComparer.Ordinal)
                .ToList();

            await SaveUnsafeAsync(document, cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<WindowsQBittorrentConfigurationDocument> LoadUnsafeAsync(
        CancellationToken cancellationToken)
    {
        if (!File.Exists(_path))
            return new WindowsQBittorrentConfigurationDocument();

        try
        {
            await using var stream = File.OpenRead(_path);

            return
                await JsonSerializer.DeserializeAsync<
                    WindowsQBittorrentConfigurationDocument>(
                    stream,
                    JsonOptions,
                    cancellationToken) ??
                new WindowsQBittorrentConfigurationDocument();
        }
        catch (Exception exception)
            when (exception is
                JsonException or
                IOException or
                UnauthorizedAccessException)
        {
            return new WindowsQBittorrentConfigurationDocument();
        }
    }

    private async Task SaveUnsafeAsync(
        WindowsQBittorrentConfigurationDocument document,
        CancellationToken cancellationToken)
    {
        var directory =
            Path.GetDirectoryName(_path) ??
            throw new InvalidOperationException(
                "The qBittorrent configuration path has no parent directory.");

        Directory.CreateDirectory(directory);

        var temporaryPath =
            _path +
            "." +
            Guid.NewGuid().ToString("N") +
            ".tmp";

        try
        {
            await using (var stream = new FileStream(
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

                await stream.FlushAsync(cancellationToken);
            }

            File.Move(temporaryPath, _path, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryPath))
                File.Delete(temporaryPath);
        }
    }
}

public sealed class WindowsQBittorrentTelemetryService
{
    private static readonly HttpClient SharedClient =
        new(
            new SocketsHttpHandler
            {
                AllowAutoRedirect = false,
                ConnectTimeout = TimeSpan.FromSeconds(3),
                PooledConnectionLifetime = TimeSpan.FromMinutes(2),
                PooledConnectionIdleTimeout = TimeSpan.FromMinutes(1),
                MaxConnectionsPerServer = 4
            })
        {
            Timeout = TimeSpan.FromSeconds(8)
        };

    private readonly WindowsTargetSession _targetSession;
    private readonly WindowsQBittorrentConfigurationStore _configuration;
    private readonly QBittorrentTelemetryClient _client;

    public WindowsQBittorrentTelemetryService(
        WindowsTargetSession targetSession,
        WindowsQBittorrentConfigurationStore? configuration = null,
        QBittorrentTelemetryClient? client = null)
    {
        _targetSession =
            targetSession ??
            throw new ArgumentNullException(nameof(targetSession));

        _configuration =
            configuration ??
            new WindowsQBittorrentConfigurationStore();

        _client =
            client ??
            new QBittorrentTelemetryClient(SharedClient);
    }

    public Task<WindowsQBittorrentTargetConfiguration> ResolveConfigurationAsync(
        TargetProfile target,
        CancellationToken cancellationToken = default) =>
        _configuration.ResolveAsync(target, cancellationToken);

    public async Task<DownloadClientTelemetrySnapshot> CaptureAsync(
        TargetProfile target,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(target);

        var configuration =
            await _configuration.ResolveAsync(target, cancellationToken);

        var username =
            WindowsQBittorrentCredentialPolicy.NormalizeUsername(
                configuration.Username);

        using var password =
            await _targetSession.RetrieveApplicationSecretAsync(
                target.Id,
                "qbittorrent",
                "webui-password",
                cancellationToken) ??
            throw new InvalidOperationException(
                "qBittorrent WebUI telemetry is not configured. " +
                "Enter the username and password, then use Save + test.");

        return await _client.CaptureAsync(
            new QBittorrentTelemetryRequest(
                QBittorrentTelemetryEndpoint.Normalize(
                    configuration.Endpoint),
                username,
                password),
            cancellationToken);
    }

    public async Task<DownloadClientTelemetrySnapshot> TestAndSaveAsync(
        TargetProfile target,
        string endpoint,
        string username,
        string? suppliedPassword,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(target);

        var normalizedEndpoint =
            QBittorrentTelemetryEndpoint.Normalize(endpoint);

        var normalizedUsername =
            WindowsQBittorrentCredentialPolicy.NormalizeUsername(username);

        var hasSuppliedPassword =
            suppliedPassword is not null &&
            suppliedPassword.Length > 0;

        SecretValue? resolvedPassword = null;
        SecretValue? previousPassword = null;

        try
        {
            resolvedPassword =
                hasSuppliedPassword
                    ? new SecretValue(
                        WindowsQBittorrentCredentialPolicy.ValidatePassword(
                            suppliedPassword!))
                    : await _targetSession.RetrieveApplicationSecretAsync(
                        target.Id,
                        "qbittorrent",
                        "webui-password",
                        cancellationToken);

            if (resolvedPassword is null)
            {
                throw new InvalidOperationException(
                    "Enter the qBittorrent WebUI password before using Save + test.");
            }

            var snapshot = await _client.CaptureAsync(
                new QBittorrentTelemetryRequest(
                    normalizedEndpoint,
                    normalizedUsername,
                    resolvedPassword,
                    RequireCompleteTelemetry: true),
                cancellationToken);

            if (hasSuppliedPassword)
            {
                previousPassword =
                    await _targetSession.RetrieveApplicationSecretAsync(
                        target.Id,
                        "qbittorrent",
                        "webui-password",
                        cancellationToken);

                await _targetSession.StoreApplicationSecretVerbatimAsync(
                    target.Id,
                    "qbittorrent",
                    "webui-password",
                    suppliedPassword!,
                    cancellationToken);
            }

            try
            {
                await _configuration.SaveAsync(
                    target.Id,
                    normalizedEndpoint.AbsoluteUri,
                    normalizedUsername,
                    cancellationToken);
            }
            catch
            {
                if (hasSuppliedPassword)
                {
                    if (previousPassword is null)
                    {
                        await _targetSession.DeleteApplicationSecretAsync(
                            target.Id,
                            "qbittorrent",
                            "webui-password",
                            cancellationToken);
                    }
                    else
                    {
                        var previous =
                            new string(previousPassword.Reveal().Span);

                        await _targetSession.StoreApplicationSecretVerbatimAsync(
                            target.Id,
                            "qbittorrent",
                            "webui-password",
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
                QBittorrentTelemetryEndpoint.IsLoopback(normalizedEndpoint)
                    ? "Password stored in Windows Credential Manager; SID cookie held in memory only."
                    : "Password stored in Windows Credential Manager; WebUI login is sent over HTTP to the configured LAN endpoint.";

            return snapshot;
        }
        finally
        {
            resolvedPassword?.Dispose();
            previousPassword?.Dispose();
        }
    }

    public Task ClearSavedPasswordAsync(
        string targetId,
        CancellationToken cancellationToken = default) =>
        _targetSession.DeleteApplicationSecretAsync(
            targetId,
            "qbittorrent",
            "webui-password",
            cancellationToken);
}
