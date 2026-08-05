using System.Net;
using System.Text.Json;
using System.Xml.Linq;
using GraveOps.Core.Security;
using GraveOps.Core.Targets;
using GraveOps.Core.Telemetry;

namespace GraveOps.Desktop.Windows;

public sealed class WindowsArrTargetConfiguration
{
    public string TargetId { get; set; } = string.Empty;
    public string Product { get; set; } = string.Empty;
    public string Endpoint { get; set; } = string.Empty;
}

public sealed class WindowsArrConfigurationDocument
{
    public List<WindowsArrTargetConfiguration> Instances { get; set; } = new();
}

public static class WindowsArrProductPolicy
{
    public static string Normalize(string product) =>
        ArrTelemetryCatalog.Resolve(product).Product;

    public static int DefaultPort(string product) =>
        Normalize(product).ToLowerInvariant() switch
        {
            "sonarr" => 8989,
            "radarr" => 7878,
            "lidarr" => 8686,
            "prowlarr" => 9696,
            "readarr" => 8787,
            "whisparr" => 6969,
            _ => throw new InvalidOperationException(
                $"No default port is registered for {product}.")
        };

    public static Uri DefaultEndpoint(TargetProfile target, string product)
    {
        ArgumentNullException.ThrowIfNull(target);

        var host = target.IsLocal
            ? "127.0.0.1"
            : target.Connection.Host ??
              throw new InvalidOperationException(
                  "The remote target host is required.");

        return ArrTelemetryEndpoint.Normalize(
            new UriBuilder(
                Uri.UriSchemeHttp,
                host,
                DefaultPort(product))
            .Uri);
    }
}

public static class WindowsArrSecretBoundary
{
    public static bool CanUseNativeLocalConfig(
        TargetProfile target,
        Uri endpoint)
    {
        ArgumentNullException.ThrowIfNull(
            target);

        ArgumentNullException.ThrowIfNull(
            endpoint);

        if (!target.IsLocal)
            return false;

        var normalized =
            ArrTelemetryEndpoint.Normalize(
                endpoint);

        var host =
            normalized.DnsSafeHost;

        if (host.Equals(
                "localhost",
                StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return
            IPAddress.TryParse(
                host,
                out var address) &&
            IPAddress.IsLoopback(
                address);
    }
}

public sealed class WindowsArrConfigurationStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly string _path;

    public WindowsArrConfigurationStore(string? path = null)
    {
        _path =
            path ??
            Path.Combine(
                Environment.GetFolderPath(
                    Environment.SpecialFolder.LocalApplicationData),
                "GraveOps",
                "arr-targets.json");
    }

    public async Task<WindowsArrTargetConfiguration> ResolveAsync(
        TargetProfile target,
        string product,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(target);
        var normalizedProduct = WindowsArrProductPolicy.Normalize(product);

        await _gate.WaitAsync(cancellationToken);

        try
        {
            var document = await LoadUnsafeAsync(cancellationToken);
            var existing = document.Instances.FirstOrDefault(item =>
                item.TargetId.Equals(target.Id, StringComparison.Ordinal) &&
                item.Product.Equals(
                    normalizedProduct,
                    StringComparison.OrdinalIgnoreCase));

            if (existing is not null)
            {
                return new WindowsArrTargetConfiguration
                {
                    TargetId = target.Id,
                    Product = normalizedProduct,
                    Endpoint = ArrTelemetryEndpoint
                        .Normalize(existing.Endpoint)
                        .AbsoluteUri
                };
            }

            return new WindowsArrTargetConfiguration
            {
                TargetId = target.Id,
                Product = normalizedProduct,
                Endpoint = WindowsArrProductPolicy
                    .DefaultEndpoint(target, normalizedProduct)
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
        string product,
        string endpoint,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(targetId))
            throw new ArgumentException("The target ID is required.", nameof(targetId));

        var normalizedProduct = WindowsArrProductPolicy.Normalize(product);
        var normalizedEndpoint = ArrTelemetryEndpoint
            .Normalize(endpoint)
            .AbsoluteUri;

        await _gate.WaitAsync(cancellationToken);

        try
        {
            var document = await LoadUnsafeAsync(cancellationToken);

            document.Instances.RemoveAll(item =>
                item.TargetId.Equals(targetId, StringComparison.Ordinal) &&
                item.Product.Equals(
                    normalizedProduct,
                    StringComparison.OrdinalIgnoreCase));

            document.Instances.Add(
                new WindowsArrTargetConfiguration
                {
                    TargetId = targetId,
                    Product = normalizedProduct,
                    Endpoint = normalizedEndpoint
                });

            document.Instances = document.Instances
                .OrderBy(item => item.TargetId, StringComparer.Ordinal)
                .ThenBy(item => item.Product, StringComparer.OrdinalIgnoreCase)
                .ToList();

            await SaveUnsafeAsync(document, cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<WindowsArrConfigurationDocument> LoadUnsafeAsync(
        CancellationToken cancellationToken)
    {
        if (!File.Exists(_path))
            return new WindowsArrConfigurationDocument();

        try
        {
            await using var stream = File.OpenRead(_path);
            return await JsonSerializer.DeserializeAsync<
                       WindowsArrConfigurationDocument>(
                       stream,
                       JsonOptions,
                       cancellationToken) ??
                   new WindowsArrConfigurationDocument();
        }
        catch (Exception exception)
            when (exception is JsonException or IOException or UnauthorizedAccessException)
        {
            return new WindowsArrConfigurationDocument();
        }
    }

    private async Task SaveUnsafeAsync(
        WindowsArrConfigurationDocument document,
        CancellationToken cancellationToken)
    {
        var directory = Path.GetDirectoryName(_path) ??
                        throw new InvalidOperationException(
                            "The Arr configuration path has no parent directory.");

        Directory.CreateDirectory(directory);

        var temporaryPath = _path + "." + Guid.NewGuid().ToString("N") + ".tmp";

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

public sealed class WindowsResolvedArrSecret : IDisposable
{
    public WindowsResolvedArrSecret(SecretValue secret, string source)
    {
        Secret = secret ?? throw new ArgumentNullException(nameof(secret));
        Source = string.IsNullOrWhiteSpace(source)
            ? "protected source"
            : source.Trim();
    }

    public SecretValue Secret { get; }
    public string Source { get; }

    public void Dispose() => Secret.Dispose();
}

public sealed class WindowsArrApiKeyDiscovery
{
    private readonly string _product;
    private readonly IReadOnlyList<string> _candidatePaths;

    public WindowsArrApiKeyDiscovery(
        string product,
        IEnumerable<string>? candidatePaths = null)
    {
        _product = WindowsArrProductPolicy.Normalize(product);
        _candidatePaths = (candidatePaths ?? DefaultCandidatePaths(_product))
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public WindowsResolvedArrSecret? TryResolve()
    {
        foreach (var path in _candidatePaths)
        {
            try
            {
                if (!File.Exists(path))
                    continue;

                var attributes = File.GetAttributes(path);

                if (attributes.HasFlag(FileAttributes.ReparsePoint))
                    continue;

                var document = XDocument.Load(path, LoadOptions.None);
                var apiKey = document
                    .Descendants()
                    .FirstOrDefault(element =>
                        element.Name.LocalName.Equals(
                            "ApiKey",
                            StringComparison.OrdinalIgnoreCase))
                    ?.Value
                    ?.Trim();

                if (!ValidApiKey(apiKey))
                    continue;

                return new WindowsResolvedArrSecret(
                    new SecretValue(apiKey!),
                    $"local {_product} config.xml");
            }
            catch (Exception exception)
                when (exception is IOException or UnauthorizedAccessException or System.Xml.XmlException)
            {
                // Continue to the next protected candidate.
            }
        }

        return null;
    }

    private static bool ValidApiKey(string? apiKey) =>
        !string.IsNullOrWhiteSpace(apiKey) &&
        apiKey.Length is >= 8 and <= 512 &&
        !apiKey.Contains('\r') &&
        !apiKey.Contains('\n');

    private static IEnumerable<string> DefaultCandidatePaths(string product)
    {
        var roots = new[]
        {
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData)
        };

        foreach (var root in roots.Where(value => !string.IsNullOrWhiteSpace(value)))
            yield return Path.Combine(root, product, "config.xml");
    }
}

public sealed class WindowsArrTelemetryService
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
            Timeout = TimeSpan.FromSeconds(6)
        };

    private readonly WindowsTargetSession _targetSession;
    private readonly WindowsArrConfigurationStore _configuration;
    private readonly Func<string, WindowsArrApiKeyDiscovery> _discoveryFactory;
    private readonly ArrTelemetryClient _client;

    public WindowsArrTelemetryService(
        WindowsTargetSession targetSession,
        WindowsArrConfigurationStore? configuration = null,
        Func<string, WindowsArrApiKeyDiscovery>? discoveryFactory = null,
        ArrTelemetryClient? client = null)
    {
        _targetSession = targetSession ??
                         throw new ArgumentNullException(nameof(targetSession));
        _configuration = configuration ?? new WindowsArrConfigurationStore();
        _discoveryFactory =
            discoveryFactory ??
            (product => new WindowsArrApiKeyDiscovery(product));
        _client = client ?? new ArrTelemetryClient(SharedClient);
    }

    public async Task<Uri> ResolveEndpointAsync(
        TargetProfile target,
        string product,
        CancellationToken cancellationToken = default)
    {
        var configuration = await _configuration.ResolveAsync(
            target,
            product,
            cancellationToken);

        return ArrTelemetryEndpoint.Normalize(configuration.Endpoint);
    }

    public async Task<ArrLiveTelemetrySnapshot> CaptureAsync(
        TargetProfile target,
        string product,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(target);

        var normalizedProduct = WindowsArrProductPolicy.Normalize(product);
        var endpoint = await ResolveEndpointAsync(
            target,
            normalizedProduct,
            cancellationToken);

        using var secret =
            await ResolveSecretAsync(
                target,
                normalizedProduct,
                endpoint,
                cancellationToken) ??
            throw new InvalidOperationException(
                $"{normalizedProduct} API telemetry is not configured. " +
                "Enter its API key and use Save + test.");

        var snapshot = await _client.CaptureAsync(
            new ArrTelemetryRequest(
                endpoint,
                normalizedProduct,
                InstanceKey(target, normalizedProduct),
                normalizedProduct,
                secret.Secret),
            cancellationToken);

        return WithSecretSource(snapshot, secret.Source);
    }

    public async Task<ArrLiveTelemetrySnapshot> TestAndSaveAsync(
        TargetProfile target,
        string product,
        string endpoint,
        string? suppliedApiKey,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(target);

        var normalizedProduct = WindowsArrProductPolicy.Normalize(product);
        var normalizedEndpoint = ArrTelemetryEndpoint.Normalize(endpoint);
        WindowsResolvedArrSecret? resolvedSecret = null;
        var hasSuppliedKey = !string.IsNullOrWhiteSpace(suppliedApiKey);

        try
        {
            resolvedSecret = hasSuppliedKey
                ? new WindowsResolvedArrSecret(
                    new SecretValue(suppliedApiKey!.Trim()),
                    "supplied API key")
                : await ResolveSecretAsync(
                    target,
                    normalizedProduct,
                    normalizedEndpoint,
                    cancellationToken);

            if (resolvedSecret is null)
            {
                throw new InvalidOperationException(
                    $"Enter the {normalizedProduct} API key before using Save + test.");
            }

            var snapshot = await _client.CaptureAsync(
                new ArrTelemetryRequest(
                    normalizedEndpoint,
                    normalizedProduct,
                    InstanceKey(target, normalizedProduct),
                    normalizedProduct,
                    resolvedSecret.Secret,
                    RequireCompleteTelemetry: true),
                cancellationToken);

            await _configuration.SaveAsync(
                target.Id,
                normalizedProduct,
                normalizedEndpoint.AbsoluteUri,
                cancellationToken);

            if (hasSuppliedKey)
            {
                await _targetSession.StoreApplicationSecretAsync(
                    target.Id,
                    normalizedProduct,
                    "api-key",
                    suppliedApiKey!,
                    cancellationToken);

                return WithSecretSource(snapshot, "Windows Credential Manager");
            }

            return WithSecretSource(snapshot, resolvedSecret.Source);
        }
        finally
        {
            resolvedSecret?.Dispose();
        }
    }

    public Task ClearSavedApiKeyAsync(
        string targetId,
        string product,
        CancellationToken cancellationToken = default) =>
        _targetSession.DeleteApplicationSecretAsync(
            targetId,
            WindowsArrProductPolicy.Normalize(product),
            "api-key",
            cancellationToken);

    private async Task<WindowsResolvedArrSecret?> ResolveSecretAsync(
        TargetProfile target,
        string product,
        Uri endpoint,
        CancellationToken cancellationToken)
    {
        var stored = await _targetSession.RetrieveApplicationSecretAsync(
            target.Id,
            product,
            "api-key",
            cancellationToken);

        if (stored is not null)
        {
            return new WindowsResolvedArrSecret(
                stored,
                "Windows Credential Manager");
        }

        return
            WindowsArrSecretBoundary.CanUseNativeLocalConfig(
                target,
                endpoint)
                ? _discoveryFactory(product).TryResolve()
                : null;
    }

    private static ArrLiveTelemetrySnapshot WithSecretSource(
        ArrLiveTelemetrySnapshot snapshot,
        string source)
    {
        var services = snapshot.Services
            .Select(service =>
                service with
                {
                    Access = $"{service.Access} · key source: {source}"
                })
            .ToArray();

        return snapshot with
        {
            Services = services
        };
    }

    private static string InstanceKey(TargetProfile target, string product) =>
        string.Join("|", target.Id, product.ToLowerInvariant());
}
