using System.Net;
using System.Net.Http;
using GraveOps.Core.Security;
using GraveOps.Core.Telemetry;
using GraveOps.Desktop.Windows;

var tests =
    new (string Name, Func<Task> Run)[]
    {
        ("Arr catalog preserves Sonarr and Radarr API roots", CatalogAsync),
        ("Sonarr falls back from v5 to v3 and parses queue telemetry", SonarrFallbackAsync),
        ("Radarr parses v3 status health and movie queue", RadarrAsync),
        ("Arr redirects are blocked without forwarding credentials", RedirectBlockedAsync),
        ("Arr authentication failures remain sanitized", AuthenticationRedactionAsync),
        ("Arr strict verification rejects protected endpoint failures", StrictVerificationAsync),
        ("Arr endpoint policy normalizes and rejects embedded credentials", EndpointValidationAsync),
        ("Arr endpoint configuration round trips without secrets", ConfigurationRoundTripAsync),
        ("Windows Arr config discovery returns a protected API key", ConfigDiscoveryAsync),
        ("native Arr config secrets are constrained to loopback endpoints", NativeSecretBoundaryAsync),
        ("application credential references are normalized and constrained", CredentialReferenceAsync)
    };

foreach (var test in tests)
{
    try
    {
        await test.Run();
        Console.WriteLine($"PASS: {test.Name}");
    }
    catch (Exception exception)
    {
        Console.Error.WriteLine($"FAIL: {test.Name}");
        Console.Error.WriteLine(exception);
        Environment.ExitCode = 1;
        return;
    }
}

Console.WriteLine($"All {tests.Length} Windows Arr parity tests passed.");

static Task CatalogAsync()
{
    SequenceEqual(
        new[] { "api/v5", "api/v3" },
        ArrTelemetryCatalog.ApiRootsFor("Sonarr"),
        "Sonarr API roots");

    SequenceEqual(
        new[] { "api/v3" },
        ArrTelemetryCatalog.ApiRootsFor("Radarr"),
        "Radarr API roots");

    True(ArrTelemetryCatalog.SupportsQueue("Sonarr"), "Sonarr queue support");
    True(ArrTelemetryCatalog.SupportsQueue("Radarr"), "Radarr queue support");
    return Task.CompletedTask;
}

static async Task SonarrFallbackAsync()
{
    var handler = new FixtureHandler(
        request =>
        {
            var path = request.RequestUri?.AbsolutePath ?? string.Empty;

            return path switch
            {
                "/api/v5/system/status" => Json("{}", HttpStatusCode.NotFound),
                "/api/v3/system/status" => Json(
                    """
                    {
                      "appName": "Sonarr",
                      "version": "4.0.15.2940"
                    }
                    """),
                "/api/v3/health" => Json(
                    """
                    [
                      {
                        "source": "Indexer",
                        "message": "Fixture health warning"
                      }
                    ]
                    """),
                "/api/v3/queue" => Json(
                    """
                    {
                      "totalRecords": 1,
                      "records": [
                        {
                          "series": {
                            "title": "Fixture Series"
                          },
                          "trackedDownloadStatus": "downloading",
                          "size": 1000,
                          "sizeleft": 250,
                          "timeleft": "00:10:00",
                          "downloadClient": "SABnzbd"
                        }
                      ]
                    }
                    """),
                _ => Json("{}", HttpStatusCode.NotFound)
            };
        });

    using var http = new HttpClient(handler);
    var client = new ArrTelemetryClient(http);
    using var key = new SecretValue("sonarr-fixture-api-key");

    var snapshot = await client.CaptureAsync(
        new ArrTelemetryRequest(
            new Uri("http://localhost:8989/"),
            "Sonarr",
            "local|sonarr",
            "Sonarr",
            key));

    Equal("ATTENTION", snapshot.OverallState, "Sonarr overall state");
    Equal("4.0.15.2940", snapshot.VersionSummary, "Sonarr version");
    Equal("1", snapshot.WorkSummary, "Sonarr queue count");
    Equal("1", snapshot.HealthSummary, "Sonarr health count");

    True(
        snapshot.WorkItems.Any(item =>
            item.Type.Equals("Episode", StringComparison.Ordinal) &&
            item.ItemIssue.Equals("Fixture Series", StringComparison.Ordinal) &&
            item.Progress.Equals("75%", StringComparison.Ordinal)),
        "Sonarr queue row");

    SequenceEqual(
        new[]
        {
            "/api/v5/system/status",
            "/api/v3/system/status",
            "/api/v3/health",
            "/api/v3/queue"
        },
        handler.Requests.Select(item => item.Path),
        "Sonarr request order");

    True(
        handler.Requests.All(item =>
            item.ApiKey.Equals(
                "sonarr-fixture-api-key",
                StringComparison.Ordinal)),
        "Sonarr API key header");
}

static async Task RadarrAsync()
{
    var handler = new FixtureHandler(
        request =>
        {
            var path = request.RequestUri?.AbsolutePath ?? string.Empty;

            return path switch
            {
                "/api/v3/system/status" => Json(
                    """
                    {
                      "appName": "Radarr",
                      "version": "5.22.4.9896"
                    }
                    """),
                "/api/v3/health" => Json("[]"),
                "/api/v3/queue" => Json(
                    """
                    {
                      "totalRecords": 1,
                      "records": [
                        {
                          "movie": {
                            "title": "Fixture Movie"
                          },
                          "status": "queued",
                          "size": 2000,
                          "sizeleft": 1000,
                          "timeleft": "00:05:00"
                        }
                      ]
                    }
                    """),
                _ => Json("{}", HttpStatusCode.NotFound)
            };
        });

    using var http = new HttpClient(handler);
    var client = new ArrTelemetryClient(http);
    using var key = new SecretValue("radarr-fixture-api-key");

    var snapshot = await client.CaptureAsync(
        new ArrTelemetryRequest(
            new Uri("http://localhost:7878/"),
            "Radarr",
            "local|radarr",
            "Radarr",
            key));

    Equal("ONLINE", snapshot.OverallState, "Radarr overall state");
    Equal("5.22.4.9896", snapshot.VersionSummary, "Radarr version");

    True(
        snapshot.WorkItems.Any(item =>
            item.Type.Equals("Movie", StringComparison.Ordinal) &&
            item.ItemIssue.Equals("Fixture Movie", StringComparison.Ordinal) &&
            item.Progress.Equals("50%", StringComparison.Ordinal)),
        "Radarr queue row");
}

static async Task RedirectBlockedAsync()
{
    const string apiKey = "redirect-fixture-api-key";

    var handler = new FixtureHandler(
        _ =>
            new HttpResponseMessage(HttpStatusCode.Redirect)
            {
                Headers =
                {
                    Location = new Uri("http://untrusted.example/")
                }
            });

    using var http = new HttpClient(handler);
    var client = new ArrTelemetryClient(http);
    using var secret = new SecretValue(apiKey);

    try
    {
        await client.CaptureAsync(
            new ArrTelemetryRequest(
                new Uri("http://localhost:8989/"),
                "Sonarr",
                "redirect",
                "Sonarr",
                secret));
    }
    catch (InvalidOperationException exception)
    {
        True(
            exception.Message.Contains(
                "redirect",
                StringComparison.OrdinalIgnoreCase),
            "redirect failure message");

        True(
            !exception.Message.Contains(apiKey, StringComparison.Ordinal),
            "redirect failure omits API key");

        Equal(1, handler.Requests.Count, "redirect request count");
        return;
    }

    throw new InvalidOperationException("Redirected Arr request was accepted.");
}

static async Task AuthenticationRedactionAsync()
{
    const string apiKey = "authentication-fixture-api-key";

    var handler = new FixtureHandler(
        _ => new HttpResponseMessage(HttpStatusCode.Unauthorized));

    using var http = new HttpClient(handler);
    var client = new ArrTelemetryClient(http);
    using var secret = new SecretValue(apiKey);

    try
    {
        await client.CaptureAsync(
            new ArrTelemetryRequest(
                new Uri("http://localhost:7878/"),
                "Radarr",
                "authentication",
                "Radarr",
                secret));
    }
    catch (InvalidOperationException exception)
    {
        Equal(
            "The Arr API rejected the configured API key.",
            exception.Message,
            "sanitized authentication message");

        True(
            !exception.Message.Contains(apiKey, StringComparison.Ordinal),
            "authentication failure omits API key");

        return;
    }

    throw new InvalidOperationException("Invalid Arr API key was accepted.");
}

static async Task StrictVerificationAsync()
{
    const string apiKey = "strict-fixture-api-key";

    var handler = new FixtureHandler(
        request =>
        {
            var path = request.RequestUri?.AbsolutePath ?? string.Empty;

            return path switch
            {
                "/api/v3/system/status" => Json(
                    """
                    {
                      "appName": "Radarr",
                      "version": "5.0"
                    }
                    """),
                "/api/v3/health" =>
                    new HttpResponseMessage(HttpStatusCode.Unauthorized),
                _ => Json("{}", HttpStatusCode.NotFound)
            };
        });

    using var http = new HttpClient(handler);
    var client = new ArrTelemetryClient(http);
    using var secret = new SecretValue(apiKey);

    try
    {
        await client.CaptureAsync(
            new ArrTelemetryRequest(
                new Uri("http://localhost:7878/"),
                "Radarr",
                "strict",
                "Radarr",
                secret,
                RequireCompleteTelemetry: true));
    }
    catch (InvalidOperationException exception)
    {
        True(
            exception.Message.Contains(
                "Protected Radarr telemetry verification failed:",
                StringComparison.Ordinal),
            "strict verification failure message");

        True(
            exception.Message.Contains(
                "The Arr API rejected the configured API key.",
                StringComparison.Ordinal),
            "strict verification sanitizes authentication failure");

        True(
            !exception.Message.Contains(apiKey, StringComparison.Ordinal),
            "strict verification omits API key");

        return;
    }

    throw new InvalidOperationException(
        "Strict Arr verification accepted a protected endpoint failure.");
}

static Task EndpointValidationAsync()
{
    Throws<InvalidOperationException>(
        () => ArrTelemetryEndpoint.Normalize(
            "http://user:password@localhost:8989/"),
        "embedded credentials");

    Throws<InvalidOperationException>(
        () => ArrTelemetryEndpoint.Normalize(
            "file:///C:/Sonarr/config.xml"),
        "non-HTTP endpoint");

    var normalized = ArrTelemetryEndpoint.Normalize(
        "http://localhost:8989/sonarr?key=value#fragment");

    Equal(
        "http://localhost:8989/sonarr/",
        normalized.AbsoluteUri,
        "Arr endpoint normalization");

    Equal(
        "http://127.0.0.1:8989/",
        WindowsArrProductPolicy
            .DefaultEndpoint(
                WindowsTargetCatalog.CreateLocal(),
                "Sonarr")
            .AbsoluteUri,
        "Sonarr local default");

    Equal(
        "http://127.0.0.1:7878/",
        WindowsArrProductPolicy
            .DefaultEndpoint(
                WindowsTargetCatalog.CreateLocal(),
                "Radarr")
            .AbsoluteUri,
        "Radarr local default");

    return Task.CompletedTask;
}

static async Task ConfigurationRoundTripAsync()
{
    var root = Path.Combine(
        Path.GetTempPath(),
        "graveops-arr-tests-" + Guid.NewGuid().ToString("N"));

    Directory.CreateDirectory(root);
    var path = Path.Combine(root, "arr-targets.json");

    try
    {
        var store = new WindowsArrConfigurationStore(path);

        await store.SaveAsync(
            "local-windows",
            "Sonarr",
            "http://localhost:8989/sonarr");

        var resolved = await store.ResolveAsync(
            WindowsTargetCatalog.CreateLocal(),
            "Sonarr");

        Equal(
            "http://localhost:8989/sonarr/",
            resolved.Endpoint,
            "stored Sonarr endpoint");

        var raw = await File.ReadAllTextAsync(path);

        True(raw.Contains("Sonarr", StringComparison.Ordinal), "configuration contains product");
        True(
            !raw.Contains("ApiKey", StringComparison.OrdinalIgnoreCase),
            "configuration omits API key fields");
        True(
            !raw.Contains("fixture-api-key", StringComparison.OrdinalIgnoreCase),
            "configuration omits API key values");
        True(
            !raw.Contains("password", StringComparison.OrdinalIgnoreCase),
            "configuration omits passwords");
    }
    finally
    {
        Directory.Delete(root, recursive: true);
    }
}

static Task ConfigDiscoveryAsync()
{
    var root = Path.Combine(
        Path.GetTempPath(),
        "graveops-arr-config-" + Guid.NewGuid().ToString("N"));

    Directory.CreateDirectory(root);
    var path = Path.Combine(root, "config.xml");

    try
    {
        File.WriteAllText(
            path,
            """
            <Config>
              <Port>8989</Port>
              <ApiKey>local-sonarr-api-key</ApiKey>
            </Config>
            """);

        var discovery = new WindowsArrApiKeyDiscovery(
            "Sonarr",
            new[] { path });

        using var resolved =
            discovery.TryResolve() ??
            throw new InvalidOperationException(
                "Expected a protected Arr API key.");

        Equal(
            "local-sonarr-api-key",
            new string(resolved.Secret.Reveal().Span),
            "discovered Arr API key");

        Equal(
            "local Sonarr config.xml",
            resolved.Source,
            "Arr API key source");
    }
    finally
    {
        Directory.Delete(root, recursive: true);
    }

    return Task.CompletedTask;
}

static Task NativeSecretBoundaryAsync()
{
    var local =
        WindowsTargetCatalog.CreateLocal();

    True(
        WindowsArrSecretBoundary.CanUseNativeLocalConfig(
            local,
            new Uri(
                "http://localhost:8989/")),
        "localhost permits native config discovery");

    True(
        WindowsArrSecretBoundary.CanUseNativeLocalConfig(
            local,
            new Uri(
                "http://127.0.0.1:8989/")),
        "IPv4 loopback permits native config discovery");

    True(
        WindowsArrSecretBoundary.CanUseNativeLocalConfig(
            local,
            new Uri(
                "http://[::1]:8989/")),
        "IPv6 loopback permits native config discovery");

    True(
        !WindowsArrSecretBoundary.CanUseNativeLocalConfig(
            local,
            new Uri(
                "http://192.168.0.50:8989/")),
        "LAN endpoint blocks native config discovery");

    True(
        !WindowsArrSecretBoundary.CanUseNativeLocalConfig(
            local,
            new Uri(
                "http://linux-server.local:8989/")),
        "named remote endpoint blocks native config discovery");

    return Task.CompletedTask;
}

static Task CredentialReferenceAsync()
{
    Equal(
        "graveops/target/local-windows/application/sonarr/api-key",
        WindowsTargetCatalog.ApplicationCredentialReferenceFor(
            "LOCAL-WINDOWS",
            "Sonarr",
            "API-KEY"),
        "Arr credential reference");

    Throws<ArgumentException>(
        () => WindowsTargetCatalog.ApplicationCredentialReferenceFor(
            "local-windows",
            "../sonarr",
            "api-key"),
        "unsafe application credential segment");

    Throws<ArgumentException>(
        () => WindowsTargetCatalog.ApplicationCredentialReferenceFor(
            "local-windows",
            "sonarr",
            "api/key"),
        "unsafe secret credential segment");

    return Task.CompletedTask;
}

static HttpResponseMessage Json(
    string content,
    HttpStatusCode statusCode = HttpStatusCode.OK) =>
    new(statusCode)
    {
        Content = new StringContent(content)
    };

static void Equal<T>(T expected, T actual, string description)
{
    if (!EqualityComparer<T>.Default.Equals(expected, actual))
    {
        throw new InvalidOperationException(
            $"{description}: expected '{expected}', got '{actual}'.");
    }
}

static void SequenceEqual<T>(
    IEnumerable<T> expected,
    IEnumerable<T> actual,
    string description)
{
    var expectedArray = expected.ToArray();
    var actualArray = actual.ToArray();

    if (!expectedArray.SequenceEqual(actualArray))
    {
        throw new InvalidOperationException(
            $"{description}: expected [{string.Join(", ", expectedArray)}], " +
            $"got [{string.Join(", ", actualArray)}].");
    }
}

static void True(bool condition, string description)
{
    if (!condition)
        throw new InvalidOperationException(description);
}

static void Throws<TException>(Action action, string description)
    where TException : Exception
{
    try
    {
        action();
    }
    catch (TException)
    {
        return;
    }

    throw new InvalidOperationException(
        $"{description}: expected {typeof(TException).Name}.");
}

sealed class FixtureHandler : HttpMessageHandler
{
    private readonly Func<HttpRequestMessage, HttpResponseMessage> _response;

    public FixtureHandler(
        Func<HttpRequestMessage, HttpResponseMessage> response) =>
        _response = response;

    public List<RequestRecord> Requests { get; } = new();

    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        Requests.Add(
            new RequestRecord(
                request.RequestUri?.AbsolutePath ?? string.Empty,
                request.Headers.TryGetValues("X-Api-Key", out var values)
                    ? values.Single()
                    : string.Empty));

        return Task.FromResult(_response(request));
    }
}

sealed record RequestRecord(string Path, string ApiKey);
