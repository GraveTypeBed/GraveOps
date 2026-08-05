using System.Net;
using System.Net.Http;
using GraveOps.Core.Security;
using GraveOps.Core.Telemetry;
using GraveOps.Desktop.Windows;

var tests =
    new (
        string Name,
        Func<Task> Run)[]
    {
        (
            "Plex client parses identity libraries and playback sessions",
            ParsesTelemetryAsync),

        (
            "Plex client supports identity-only telemetry without a token",
            IdentityOnlyAsync),

        (
            "Plex authentication failures never disclose the token",
            AuthenticationRedactionAsync),

        (
            "Plex strict verification rejects invalid protected access",
            StrictAuthenticationVerificationAsync),

        (
            "Plex endpoint policy rejects embedded credentials",
            EndpointValidationAsync),

        (
            "Plex endpoint configuration round trips without secrets",
            ConfigurationRoundTripAsync),

        (
            "Windows Plex registry discovery returns a protected token",
            RegistryDiscoveryAsync),

        (
            "local Plex Preferences fallback returns a protected token",
            PreferencesDiscoveryAsync)
    };

foreach (var test in tests)
{
    try
    {
        await test.Run();

        Console.WriteLine(
            $"PASS: {test.Name}");
    }
    catch (Exception exception)
    {
        Console.Error.WriteLine(
            $"FAIL: {test.Name}");

        Console.Error.WriteLine(
            exception);

        Environment.ExitCode =
            1;

        return;
    }
}

Console.WriteLine(
    $"All {tests.Length} Windows Plex parity tests passed.");

static async Task ParsesTelemetryAsync()
{
    var handler =
        new FixtureHandler(
            request =>
            {
                var path =
                    request.RequestUri?
                        .AbsolutePath ??
                    string.Empty;

                return path switch
                {
                    "/identity" =>
                        Xml(
                            """
                            <MediaContainer
                                version="1.40.0.7998"
                                machineIdentifier="fixture-machine"
                                platform="Windows" />
                            """),

                    "/status/sessions" =>
                        Xml(
                            """
                            <MediaContainer size="3">
                              <Video
                                  title="Direct Movie"
                                  type="movie"
                                  year="2025"
                                  duration="1000"
                                  viewOffset="500">
                                <User title="Anthony" />
                                <Player title="Living Room" state="playing" />
                                <Session bandwidth="8000" />
                                <Media
                                    videoDecision="directplay"
                                    audioDecision="directplay"
                                    container="mkv" />
                              </Video>
                              <Video
                                  title="Episode"
                                  grandparentTitle="Fixture Show"
                                  parentIndex="1"
                                  index="2"
                                  type="episode"
                                  duration="1000"
                                  viewOffset="250">
                                <User title="Anthony" />
                                <Player product="Plex Web" state="paused" />
                                <Session bandwidth="4000" />
                                <Media
                                    videoDecision="copy"
                                    audioDecision="copy"
                                    container="mp4" />
                              </Video>
                              <Video
                                  title="Transcoded Movie"
                                  type="movie"
                                  duration="1000"
                                  viewOffset="750">
                                <User title="Remote Viewer" />
                                <Player platform="Android" state="playing" />
                                <TranscodeSession
                                    videoDecision="transcode"
                                    audioDecision="transcode"
                                    bandwidth="12000" />
                              </Video>
                            </MediaContainer>
                            """),

                    "/library/sections" =>
                        Xml(
                            """
                            <MediaContainer size="3">
                              <Directory key="1" title="Movies" />
                              <Directory key="2" title="Shows" />
                              <Directory key="3" title="Music" />
                            </MediaContainer>
                            """),

                    _ =>
                        new HttpResponseMessage(
                            HttpStatusCode.NotFound)
                };
            });

    using var httpClient =
        new HttpClient(
            handler);

    var client =
        new PlexTelemetryClient(
            httpClient);

    using var token =
        new SecretValue(
            "fixture-token-value");

    var snapshot =
        await client.CaptureAsync(
            new PlexTelemetryRequest(
                new Uri(
                    "http://127.0.0.1:32400/"),
                "graveops-test-client",
                token));

    Equal(
        "Online",
        snapshot.State,
        "state");

    Equal(
        "1.40.0.7998",
        snapshot.Version,
        "version");

    Equal(
        3,
        snapshot.ActiveSessions,
        "active sessions");

    Equal(
        1,
        snapshot.DirectPlayCount,
        "direct play");

    Equal(
        1,
        snapshot.DirectStreamCount,
        "direct stream");

    Equal(
        1,
        snapshot.TranscodeCount,
        "transcode");

    Equal(
        3,
        snapshot.LibraryCount,
        "libraries");

    Equal(
        "24.0 Mbps",
        snapshot.TotalBandwidth,
        "bandwidth");

    True(
        snapshot.Sessions.Any(session =>
            session.Title.Equals(
                "Fixture Show Â· S01E02 Â· Episode",
                StringComparison.Ordinal)),
        "episode title");

    True(
        handler.Requests
            .Where(request =>
                request.Path is
                    "/status/sessions" or
                    "/library/sections")
            .All(request =>
                request.Token.Equals(
                    "fixture-token-value",
                    StringComparison.Ordinal)),
        "protected requests carry token header");
}

static async Task IdentityOnlyAsync()
{
    var handler =
        new FixtureHandler(
            request =>
            {
                if (request.RequestUri?
                        .AbsolutePath ==
                    "/identity")
                {
                    return Xml(
                        """
                        <MediaContainer
                            version="1.0"
                            machineIdentifier="identity-only" />
                        """);
                }

                return new HttpResponseMessage(
                    HttpStatusCode.InternalServerError);
            });

    using var httpClient =
        new HttpClient(
            handler);

    var client =
        new PlexTelemetryClient(
            httpClient);

    var snapshot =
        await client.CaptureAsync(
            new PlexTelemetryRequest(
                new Uri(
                    "http://localhost:32400/"),
                "graveops-identity-test"));

    Equal(
        "Online",
        snapshot.State,
        "identity-only state");

    Equal(
        0,
        snapshot.ActiveSessions,
        "identity-only sessions");

    True(
        snapshot.Security.Contains(
            "Identity-only",
            StringComparison.OrdinalIgnoreCase),
        "identity-only security status");

    Equal(
        1,
        handler.Requests.Count,
        "identity-only request count");
}

static async Task AuthenticationRedactionAsync()
{
    const string tokenText =
        "do-not-leak-this-token";

    var handler =
        new FixtureHandler(
            request =>
            {
                if (request.RequestUri?
                        .AbsolutePath ==
                    "/identity")
                {
                    return Xml(
                        """
                        <MediaContainer
                            version="1.0"
                            machineIdentifier="redaction-test" />
                        """);
                }

                return new HttpResponseMessage(
                    HttpStatusCode.Unauthorized);
            });

    using var httpClient =
        new HttpClient(
            handler);

    var client =
        new PlexTelemetryClient(
            httpClient);

    using var token =
        new SecretValue(
            tokenText);

    var snapshot =
        await client.CaptureAsync(
            new PlexTelemetryRequest(
                new Uri(
                    "http://localhost:32400/"),
                "graveops-redaction-test",
                token));

    True(
        snapshot.Detail.Contains(
            "Plex rejected the configured token.",
            StringComparison.Ordinal),
        "sanitized authentication error");

    True(
        !snapshot.Detail.Contains(
            tokenText,
            StringComparison.Ordinal),
        "token absent from snapshot detail");
}

static async Task StrictAuthenticationVerificationAsync()
{
    const string tokenText =
        "strict-do-not-leak-token";

    var handler =
        new FixtureHandler(
            request =>
            {
                if (request.RequestUri?
                        .AbsolutePath ==
                    "/identity")
                {
                    return Xml(
                        """
                        <MediaContainer
                            version="1.0"
                            machineIdentifier="strict-redaction-test" />
                        """);
                }

                return new HttpResponseMessage(
                    HttpStatusCode.Unauthorized);
            });

    using var httpClient =
        new HttpClient(
            handler);

    var client =
        new PlexTelemetryClient(
            httpClient);

    using var token =
        new SecretValue(
            tokenText);

    try
    {
        await client.CaptureAsync(
            new PlexTelemetryRequest(
                new Uri(
                    "http://localhost:32400/"),
                "graveops-strict-redaction-test",
                token,
                RequireProtectedTelemetry:
                    true));
    }
    catch (InvalidOperationException exception)
    {
        True(
            exception.Message.Contains(
                "Plex protected telemetry verification failed:",
                StringComparison.Ordinal),
            "strict verification failure message");

        True(
            exception.Message.Contains(
                "Plex rejected the configured token.",
                StringComparison.Ordinal),
            "strict authentication failure is sanitized");

        True(
            !exception.Message.Contains(
                tokenText,
                StringComparison.Ordinal),
            "strict failure does not disclose token");

        return;
    }

    throw new InvalidOperationException(
        "Strict protected telemetry verification accepted an invalid token.");
}

static Task EndpointValidationAsync()
{
    Throws<InvalidOperationException>(
        () =>
            WindowsPlexEndpointPolicy.Normalize(
                "http://user:password@localhost:32400/"),
        "embedded credentials");

    var normalized =
        WindowsPlexEndpointPolicy.Normalize(
            "http://localhost:32400/web");

    Equal(
        "http://localhost:32400/",
        normalized.AbsoluteUri,
        "web suffix normalization");

    return Task.CompletedTask;
}

static async Task ConfigurationRoundTripAsync()
{
    var root =
        Path.Combine(
            Path.GetTempPath(),
            "graveops-plex-tests-" +
            Guid.NewGuid()
                .ToString("N"));

    Directory.CreateDirectory(
        root);

    var path =
        Path.Combine(
            root,
            "plex-targets.json");

    try
    {
        var store =
            new WindowsPlexConfigurationStore(
                path);

        await store.SaveAsync(
            "local-windows",
            "http://localhost:32400/web");

        var target =
            WindowsTargetCatalog.CreateLocal();

        var resolved =
            await store.ResolveAsync(
                target);

        Equal(
            "http://localhost:32400/",
            resolved.Endpoint,
            "configuration endpoint");

        var raw =
            await File.ReadAllTextAsync(
                path);

        True(
            !raw.Contains(
                "fixture-token",
                StringComparison.OrdinalIgnoreCase),
            "configuration omits token values");

        True(
            !raw.Contains(
                "password",
                StringComparison.OrdinalIgnoreCase),
            "configuration omits passwords");
    }
    finally
    {
        Directory.Delete(
            root,
            recursive: true);
    }
}

static Task RegistryDiscoveryAsync()
{
    var discovery =
        new WindowsPlexTokenDiscovery(
            candidatePaths:
                Array.Empty<string>(),
            registryTokenReader:
                () =>
                    "windows-registry-token");

    using var resolved =
        discovery.TryResolve() ??
        throw new InvalidOperationException(
            "Expected a protected Windows registry Plex token.");

    Equal(
        "windows-registry-token",
        new string(
            resolved.Secret.Reveal().Span),
        "registry token");

    Equal(
        "Windows Plex registry",
        resolved.Source,
        "registry token source");

    return Task.CompletedTask;
}

static Task PreferencesDiscoveryAsync()
{
    var root =
        Path.Combine(
            Path.GetTempPath(),
            "graveops-plex-preferences-" +
            Guid.NewGuid()
                .ToString("N"));

    Directory.CreateDirectory(
        root);

    var path =
        Path.Combine(
            root,
            "Preferences.xml");

    try
    {
        File.WriteAllText(
            path,
            """
            <Preferences PlexOnlineToken="local-preferences-token" />
            """);

        var discovery =
            new WindowsPlexTokenDiscovery(
                new[]
                {
                    path
                });

        using var resolved =
            discovery.TryResolve() ??
            throw new InvalidOperationException(
                "Expected a protected Plex token.");

        Equal(
            "local-preferences-token",
            new string(
                resolved.Secret.Reveal().Span),
            "discovered token");

        Equal(
            "local Plex Preferences.xml fallback",
            resolved.Source,
            "token source");
    }
    finally
    {
        Directory.Delete(
            root,
            recursive: true);
    }

    return Task.CompletedTask;
}

static HttpResponseMessage Xml(
    string content) =>
    new(
        HttpStatusCode.OK)
    {
        Content =
            new StringContent(
                content)
    };

static void Equal<T>(
    T expected,
    T actual,
    string description)
{
    if (!EqualityComparer<T>.Default.Equals(
            expected,
            actual))
    {
        throw new InvalidOperationException(
            $"{description}: expected '{expected}', got '{actual}'.");
    }
}

static void True(
    bool condition,
    string description)
{
    if (!condition)
    {
        throw new InvalidOperationException(
            description);
    }
}

static void Throws<TException>(
    Action action,
    string description)
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

sealed class FixtureHandler :
    HttpMessageHandler
{
    private readonly Func<
        HttpRequestMessage,
        HttpResponseMessage>
        _response;

    public FixtureHandler(
        Func<
            HttpRequestMessage,
            HttpResponseMessage>
            response)
    {
        _response =
            response;
    }

    public List<RequestRecord> Requests { get; } =
        new();

    protected override Task<HttpResponseMessage>
        SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
    {
        Requests.Add(
            new RequestRecord(
                request.RequestUri?
                    .AbsolutePath ??
                string.Empty,
                request.Headers
                    .TryGetValues(
                        "X-Plex-Token",
                        out var values)
                    ? values.Single()
                    : string.Empty));

        return Task.FromResult(
            _response(
                request));
    }
}

sealed record RequestRecord(
    string Path,
    string Token);