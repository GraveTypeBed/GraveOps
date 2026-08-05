using System.Net;
using System.Net.Http;
using GraveOps.Core.Security;
using GraveOps.Core.Telemetry;
using GraveOps.Desktop.Windows;

var tests =
    new (string Name, Func<Task> Run)[]
    {
        (
            "qBittorrent endpoint and credential policy validates safely",
            EndpointAndCredentialPolicyAsync),

        (
            "qBittorrent client authenticates and parses complete telemetry",
            CompleteTelemetryAsync),

        (
            "qBittorrent authentication failures remain sanitized",
            AuthenticationRedactionAsync),

        (
            "qBittorrent redirects are blocked before credential forwarding",
            RedirectBlockedAsync),

        (
            "qBittorrent strict verification rejects partial protected telemetry",
            StrictVerificationAsync),

        (
            "qBittorrent normal refresh retains core telemetry when categories fail",
            PartialCategoryTelemetryAsync),

        (
            "qBittorrent configuration round trips without passwords or cookies",
            ConfigurationRoundTripAsync),

        (
            "qBittorrent target lease rejects stale workspace completions",
            TargetLeaseAsync)
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

Console.WriteLine(
    $"All {tests.Length} Windows qBittorrent parity tests passed.");

static Task EndpointAndCredentialPolicyAsync()
{
    Throws<InvalidOperationException>(
        () => QBittorrentTelemetryEndpoint.Normalize(
            "http://user:password@localhost:8080/"),
        "embedded endpoint credentials");

    Throws<InvalidOperationException>(
        () => QBittorrentTelemetryEndpoint.Normalize(
            "file:///C:/qBittorrent/qBittorrent.conf"),
        "non-HTTP endpoint");

    Equal(
        "http://localhost:8080/qbit/",
        QBittorrentTelemetryEndpoint
            .Normalize(
                "http://localhost:8080/qbit?secret=value#fragment")
            .AbsoluteUri,
        "endpoint normalization");

    True(
        QBittorrentTelemetryEndpoint.IsLoopback(
            new Uri("http://127.0.0.1:8080/")),
        "IPv4 loopback detection");

    True(
        !QBittorrentTelemetryEndpoint.IsLoopback(
            new Uri("http://192.168.0.50:8080/")),
        "LAN endpoint detection");

    Equal(
        "admin",
        WindowsQBittorrentCredentialPolicy.NormalizeUsername(" admin "),
        "username normalization");

    Equal(
        " password with spaces ",
        WindowsQBittorrentCredentialPolicy.ValidatePassword(
            " password with spaces "),
        "password whitespace preservation");

    Throws<InvalidOperationException>(
        () => WindowsQBittorrentCredentialPolicy.ValidatePassword(
            "line1\nline2"),
        "multiline password rejection");

    Equal(
        "graveops/target/local-windows/application/qbittorrent/webui-password",
        WindowsTargetCatalog.ApplicationCredentialReferenceFor(
            "LOCAL-WINDOWS",
            "qBittorrent",
            "WEBUI-PASSWORD"),
        "qBittorrent credential reference");

    return Task.CompletedTask;
}

static async Task CompleteTelemetryAsync()
{
    const string password =
        "fixture-password-do-not-leak";

    const string trackerPasskey =
        "private-passkey-do-not-leak";

    var handler = new FixtureHandler(
        request =>
        {
            return request.Path switch
            {
                "/api/v2/auth/login" =>
                    LoginSuccess(),

                "/api/v2/app/version" =>
                    Text("v5.0.3"),

                "/api/v2/transfer/info" =>
                    Json(
                        """
                        {
                          "connection_status": "connected",
                          "dl_info_speed": 2097152,
                          "up_info_speed": 524288,
                          "dl_info_data": 1073741824,
                          "up_info_data": 268435456,
                          "dl_rate_limit": 0,
                          "up_rate_limit": 0,
                          "dht_nodes": 321
                        }
                        """),

                "/api/v2/torrents/info" =>
                    Json(
                        $$"""
                        [
                          {
                            "name": "Fixture Download",
                            "category": "tv",
                            "state": "downloading",
                            "progress": 0.75,
                            "total_size": 4294967296,
                            "downloaded": 3221225472,
                            "amount_left": 1073741824,
                            "dlspeed": 2097152,
                            "upspeed": 131072,
                            "eta": 600,
                            "ratio": 0.35,
                            "num_seeds": 14,
                            "num_leechs": 5,
                            "tracker": "https://tracker.example/announce?passkey={{trackerPasskey}}",
                            "trackers_count": 3,
                            "added_on": 1700000000,
                            "completion_on": 0,
                            "time_active": 1800,
                            "uploaded": 123456
                          },
                          {
                            "name": "Fixture Complete",
                            "category": "movies",
                            "state": "uploading",
                            "progress": 1.0,
                            "total_size": 2147483648,
                            "downloaded": 2147483648,
                            "amount_left": 0,
                            "dlspeed": 0,
                            "upspeed": 262144,
                            "eta": 8640000,
                            "ratio": 2.5,
                            "num_seeds": 20,
                            "num_leechs": 1,
                            "tracker": "udp://tracker.second.example:6969/announce",
                            "trackers_count": 2,
                            "added_on": 1700000000,
                            "completion_on": 4102440000,
                            "time_active": 7200,
                            "uploaded": 5368709120
                          },
                          {
                            "name": "Fixture Stalled",
                            "category": "",
                            "state": "stalledDL",
                            "progress": 0.20,
                            "total_size": 1073741824,
                            "downloaded": 214748364,
                            "amount_left": 858993460,
                            "dlspeed": 0,
                            "upspeed": 0,
                            "eta": 8640000,
                            "ratio": 0,
                            "num_seeds": 0,
                            "num_leechs": 0,
                            "tracker": "",
                            "trackers_count": 0,
                            "added_on": 1700000000,
                            "completion_on": 0,
                            "time_active": 300,
                            "uploaded": 0
                          }
                        ]
                        """),

                "/api/v2/torrents/categories" =>
                    Json(
                        """
                        {
                          "tv": {
                            "savePath": "D:\\Secret\\TV"
                          },
                          "movies": {
                            "savePath": "D:\\Secret\\Movies"
                          },
                          "music": {
                            "savePath": "D:\\Secret\\Music"
                          }
                        }
                        """),

                "/api/v2/auth/logout" =>
                    Text("Ok."),

                _ =>
                    Json("{}", HttpStatusCode.NotFound)
            };
        });

    using var http = new HttpClient(handler);
    var client = new QBittorrentTelemetryClient(http);
    using var secret = new SecretValue(password);

    var snapshot = await client.CaptureAsync(
        new QBittorrentTelemetryRequest(
            new Uri("http://qbittorrent.local:8080/"),
            "fixture-admin",
            secret,
            RequireCompleteTelemetry: true));

    Equal("Online", snapshot.State, "qBittorrent state");
    Equal("5.0.3", snapshot.Version, "qBittorrent version");
    Equal(3, snapshot.TotalCount, "torrent count");
    Equal(2, snapshot.ActiveCount, "active torrent count");
    Equal(2, snapshot.DownloadingCount, "downloading count");
    Equal(1, snapshot.SeedingCount, "seeding count");
    Equal(1, snapshot.StalledCount, "stalled count");
    Equal(4, snapshot.CategoryCount, "category count");
    Equal(5, snapshot.TrackerCount, "tracker association count");
    Equal(321, snapshot.DhtNodes, "DHT node count");

    var download = snapshot.Queue.Single(item =>
        item.Name.Equals(
            "Fixture Download",
            StringComparison.Ordinal));

    Equal("75.0%", download.Progress, "torrent progress");
    Equal("tracker.example", download.Tracker, "safe tracker host");
    Equal("14/5", download.Peers, "seed and peer count");

    True(
        snapshot.Categories.Any(item =>
            item.Name.Equals(
                "music",
                StringComparison.Ordinal) &&
            item.TorrentCount == 0),
        "configured empty category");

    True(
        snapshot.Categories.Any(item =>
            item.Name.Equals(
                "Uncategorized",
                StringComparison.Ordinal) &&
            item.TorrentCount == 1),
        "uncategorized torrent count");

    Equal(1, snapshot.History.Count, "recent history count");

    var projectedText =
        string.Join(
            "|",
            snapshot.Queue.Select(item =>
                string.Join(
                    "|",
                    item.Name,
                    item.Category,
                    item.State,
                    item.Progress,
                    item.Tracker,
                    item.Detail)))
        +
        "|" +
        string.Join(
            "|",
            snapshot.Categories.Select(item => item.Name));

    True(
        !projectedText.Contains(
            trackerPasskey,
            StringComparison.Ordinal),
        "tracker passkey redaction");

    True(
        !projectedText.Contains(
            "D:\\Secret",
            StringComparison.Ordinal),
        "category save path redaction");

    SequenceEqual(
        new[]
        {
            "/api/v2/auth/login",
            "/api/v2/app/version",
            "/api/v2/transfer/info",
            "/api/v2/torrents/info",
            "/api/v2/torrents/categories",
            "/api/v2/auth/logout"
        },
        handler.Requests.Select(item => item.Path),
        "qBittorrent request order");

    var login = handler.Requests[0];

    Equal(
        "http://qbittorrent.local:8080/",
        login.Referrer,
        "login Referer");

    Equal(
        "http://qbittorrent.local:8080",
        login.Origin,
        "login Origin");

    True(
        login.Body.Contains(
            "username=fixture-admin",
            StringComparison.Ordinal),
        "login username form body");

    True(
        login.Body.Contains(
            "password=fixture-password-do-not-leak",
            StringComparison.Ordinal),
        "login password form body");

    True(
        handler.Requests
            .Skip(1)
            .Take(4)
            .All(item =>
                item.Cookie.Equals(
                    "SID=fixture-session",
                    StringComparison.Ordinal)),
        "protected requests use SID cookie");

    True(
        !snapshot.Detail.Contains(
            password,
            StringComparison.Ordinal),
        "snapshot omits WebUI password");

    True(
        !snapshot.Security.Contains(
            "fixture-session",
            StringComparison.Ordinal),
        "snapshot omits SID cookie");
}

static async Task AuthenticationRedactionAsync()
{
    const string password =
        "authentication-secret-do-not-leak";

    var handler = new FixtureHandler(
        request =>
            request.Path.Equals(
                "/api/v2/auth/login",
                StringComparison.Ordinal)
                ? Text("Fails.")
                : Json("{}", HttpStatusCode.NotFound));

    using var http = new HttpClient(handler);
    var client = new QBittorrentTelemetryClient(http);
    using var secret = new SecretValue(password);

    try
    {
        await client.CaptureAsync(
            new QBittorrentTelemetryRequest(
                new Uri("http://localhost:8080/"),
                "admin",
                secret));
    }
    catch (InvalidOperationException exception)
    {
        Equal(
            "qBittorrent rejected the configured WebUI credentials.",
            exception.Message,
            "sanitized authentication message");

        True(
            !exception.Message.Contains(
                password,
                StringComparison.Ordinal),
            "authentication failure omits password");

        return;
    }

    throw new InvalidOperationException(
        "Invalid qBittorrent credentials were accepted.");
}

static async Task RedirectBlockedAsync()
{
    const string password =
        "redirect-secret-do-not-leak";

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
    var client = new QBittorrentTelemetryClient(http);
    using var secret = new SecretValue(password);

    try
    {
        await client.CaptureAsync(
            new QBittorrentTelemetryRequest(
                new Uri("http://localhost:8080/"),
                "admin",
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
            !exception.Message.Contains(
                password,
                StringComparison.Ordinal),
            "redirect failure omits password");

        Equal(1, handler.Requests.Count, "redirect request count");
        return;
    }

    throw new InvalidOperationException(
        "Redirected qBittorrent login was accepted.");
}

static async Task StrictVerificationAsync()
{
    var handler = CategoryFailureHandler();
    using var http = new HttpClient(handler);
    var client = new QBittorrentTelemetryClient(http);
    using var secret = new SecretValue("strict-fixture-password");

    try
    {
        await client.CaptureAsync(
            new QBittorrentTelemetryRequest(
                new Uri("http://localhost:8080/"),
                "admin",
                secret,
                RequireCompleteTelemetry: true));
    }
    catch (InvalidOperationException exception)
    {
        True(
            exception.Message.Contains(
                "Protected qBittorrent telemetry verification failed:",
                StringComparison.Ordinal),
            "strict verification failure message");

        True(
            exception.Message.Contains(
                "authenticated WebUI session",
                StringComparison.Ordinal),
            "strict protected failure is sanitized");

        True(
            !exception.Message.Contains(
                "strict-fixture-password",
                StringComparison.Ordinal),
            "strict verification omits password");

        return;
    }

    throw new InvalidOperationException(
        "Strict qBittorrent verification accepted partial protected telemetry.");
}

static async Task PartialCategoryTelemetryAsync()
{
    var handler = CategoryFailureHandler();
    using var http = new HttpClient(handler);
    var client = new QBittorrentTelemetryClient(http);
    using var secret = new SecretValue("partial-fixture-password");

    var snapshot = await client.CaptureAsync(
        new QBittorrentTelemetryRequest(
            new Uri("http://localhost:8080/"),
            "admin",
            secret));

    Equal("Attention", snapshot.State, "partial category state");

    True(
        snapshot.Detail.Contains(
            "category inventory is unavailable",
            StringComparison.Ordinal),
        "partial category detail");

    Equal(0, snapshot.CategoryCount, "partial category count");
}

static async Task ConfigurationRoundTripAsync()
{
    var root = Path.Combine(
        Path.GetTempPath(),
        "graveops-qbit-tests-" +
        Guid.NewGuid().ToString("N"));

    Directory.CreateDirectory(root);
    var path = Path.Combine(root, "qbittorrent-targets.json");

    try
    {
        var store = new WindowsQBittorrentConfigurationStore(path);

        await store.SaveAsync(
            "local-windows",
            "http://server.local:8080/qbit",
            "fixture-admin");

        var resolved = await store.ResolveAsync(
            WindowsTargetCatalog.CreateLocal());

        Equal(
            "http://server.local:8080/qbit/",
            resolved.Endpoint,
            "stored endpoint");

        Equal(
            "fixture-admin",
            resolved.Username,
            "stored username");

        var raw = await File.ReadAllTextAsync(path);

        True(
            raw.Contains(
                "fixture-admin",
                StringComparison.Ordinal),
            "configuration contains username");

        True(
            !raw.Contains(
                "password",
                StringComparison.OrdinalIgnoreCase),
            "configuration omits password fields");

        True(
            !raw.Contains(
                "SID=",
                StringComparison.OrdinalIgnoreCase),
            "configuration omits SID cookies");

        True(
            !raw.Contains(
                "fixture-password",
                StringComparison.OrdinalIgnoreCase),
            "configuration omits password values");
    }
    finally
    {
        Directory.Delete(root, recursive: true);
    }
}

static Task TargetLeaseAsync()
{
    var local =
        WindowsTargetCatalog.CreateLocal();

    True(
        WindowsQBittorrentTargetLease.IsCurrent(
            local.Id,
            local),
        "matching target lease");

    True(
        !WindowsQBittorrentTargetLease.IsCurrent(
            "different-target",
            local),
        "stale target lease");

    True(
        !WindowsQBittorrentTargetLease.IsCurrent(
            local.Id,
            currentTarget:
                null),
        "missing current target lease");

    return Task.CompletedTask;
}

static FixtureHandler CategoryFailureHandler() =>
    new(
        request =>
            request.Path switch
            {
                "/api/v2/auth/login" =>
                    LoginSuccess(),

                "/api/v2/app/version" =>
                    Text("5.0.3"),

                "/api/v2/transfer/info" =>
                    Json(
                        """
                        {
                          "connection_status": "connected",
                          "dl_info_speed": 0,
                          "up_info_speed": 0,
                          "dl_info_data": 0,
                          "up_info_data": 0,
                          "dht_nodes": 0
                        }
                        """),

                "/api/v2/torrents/info" =>
                    Json("[]"),

                "/api/v2/torrents/categories" =>
                    new HttpResponseMessage(
                        HttpStatusCode.Unauthorized),

                "/api/v2/auth/logout" =>
                    Text("Ok."),

                _ =>
                    Json("{}", HttpStatusCode.NotFound)
            });

static HttpResponseMessage LoginSuccess()
{
    var response = Text("Ok.");

    response.Headers.TryAddWithoutValidation(
        "Set-Cookie",
        "SID=fixture-session; path=/; HttpOnly");

    return response;
}

static HttpResponseMessage Json(
    string content,
    HttpStatusCode statusCode = HttpStatusCode.OK) =>
    new(statusCode)
    {
        Content = new StringContent(content)
    };

static HttpResponseMessage Text(
    string content,
    HttpStatusCode statusCode = HttpStatusCode.OK) =>
    new(statusCode)
    {
        Content = new StringContent(content)
    };

static void Equal<T>(
    T expected,
    T actual,
    string description)
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
            $"{description}: expected " +
            $"[{string.Join(", ", expectedArray)}], got " +
            $"[{string.Join(", ", actualArray)}].");
    }
}

static void True(bool condition, string description)
{
    if (!condition)
        throw new InvalidOperationException(description);
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

sealed class FixtureHandler : HttpMessageHandler
{
    private readonly Func<RequestRecord, HttpResponseMessage> _response;

    public FixtureHandler(
        Func<RequestRecord, HttpResponseMessage> response)
    {
        _response = response;
    }

    public List<RequestRecord> Requests { get; } = new();

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        var body =
            request.Content is null
                ? string.Empty
                : await request.Content.ReadAsStringAsync(
                    cancellationToken);

        var record = new RequestRecord(
            request.RequestUri?.AbsolutePath ?? string.Empty,
            request.Method.Method,
            request.Headers.Referrer?.AbsoluteUri ?? string.Empty,
            request.Headers.TryGetValues("Origin", out var origins)
                ? origins.Single()
                : string.Empty,
            request.Headers.TryGetValues("Cookie", out var cookies)
                ? cookies.Single()
                : string.Empty,
            body);

        Requests.Add(record);
        return _response(record);
    }
}

sealed record RequestRecord(
    string Path,
    string Method,
    string Referrer,
    string Origin,
    string Cookie,
    string Body);
