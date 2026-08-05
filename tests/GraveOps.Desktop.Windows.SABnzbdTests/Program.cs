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
            "SABnzbd endpoint and API-key policy validates safely",
            EndpointAndCredentialPolicyAsync),

        (
            "SABnzbd client parses queue history and transfer telemetry",
            CompleteTelemetryAsync),

        (
            "SABnzbd authentication failures remain sanitized",
            AuthenticationRedactionAsync),

        (
            "SABnzbd redirects are blocked before API-key forwarding",
            RedirectBlockedAsync),

        (
            "SABnzbd strict verification rejects partial protected telemetry",
            StrictVerificationAsync),

        (
            "SABnzbd normal refresh retains queue telemetry when history fails",
            PartialHistoryTelemetryAsync),

        (
            "SABnzbd configuration round trips without API keys",
            ConfigurationRoundTripAsync),

        (
            "SABnzbd target lease rejects stale workspace completions",
            TargetLeaseAsync)
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
    $"All {tests.Length} Windows SABnzbd parity tests passed.");

static Task EndpointAndCredentialPolicyAsync()
{
    Throws<InvalidOperationException>(
        () =>
            SABnzbdTelemetryEndpoint.Normalize(
                "http://user:password@localhost:8080/"),
        "embedded endpoint credentials");

    Throws<InvalidOperationException>(
        () =>
            SABnzbdTelemetryEndpoint.Normalize(
                "file:///C:/sabnzbd.ini"),
        "non-HTTP endpoint");

    Equal(
        "http://localhost:8080/sabnzbd/",
        SABnzbdTelemetryEndpoint
            .Normalize(
                "http://localhost:8080/sabnzbd/api?apikey=secret#fragment")
            .AbsoluteUri,
        "endpoint normalization");

    True(
        SABnzbdTelemetryEndpoint.IsLoopback(
            new Uri(
                "http://127.0.0.1:8080/")),
        "IPv4 loopback detection");

    True(
        !SABnzbdTelemetryEndpoint.IsLoopback(
            new Uri(
                "http://192.168.0.50:8080/")),
        "LAN endpoint detection");

    Equal(
        "0123456789abcdef",
        WindowsSABnzbdCredentialPolicy.NormalizeApiKey(
            " 0123456789abcdef "),
        "API-key normalization");

    Throws<InvalidOperationException>(
        () =>
            WindowsSABnzbdCredentialPolicy.NormalizeApiKey(
                "line1\nline2-secret"),
        "multiline API-key rejection");

    Equal(
        "graveops/target/local-windows/application/sabnzbd/api-key",
        WindowsTargetCatalog.ApplicationCredentialReferenceFor(
            "LOCAL-WINDOWS",
            "SABnzbd",
            "API-KEY"),
        "SABnzbd credential reference");

    return Task.CompletedTask;
}

static async Task CompleteTelemetryAsync()
{
    const string apiKey =
        "fixture-api-key-do-not-leak";

    const string privatePath =
        "D:\\Private Downloads\\Secret Release\\episode.mkv";

    const string privateUnixPath =
        "/mnt/media/Plerver 2/Secret Release/episode.mkv";

    const string privateUrl =
        "https://indexer.example/private?id=123";

    var privatePathJson =
        privatePath.Replace(
            "\\",
            "\\\\",
            StringComparison.Ordinal);

    var handler =
        new FixtureHandler(
            request =>
            {
                return request.Mode switch
                {
                    "version" =>
                        Json(
                            """
                            {
                              "version": "4.5.3"
                            }
                            """),

                    "queue" =>
                        Json(
                            """
                            {
                              "queue": {
                                "paused": false,
                                "kbpersec": "2048",
                                "sizeleft": "3.5 GB",
                                "timeleft": "0:28:00",
                                "diskspace1": "512.25",
                                "speedlimit_abs": "0",
                                "noofslots": "2",
                                "slots": [
                                  {
                                    "filename": "Fixture Download",
                                    "cat": "tv",
                                    "status": "Downloading",
                                    "percentage": "75",
                                    "mb": "4096",
                                    "mbleft": "1024",
                                    "timeleft": "0:08:00",
                                    "time_added": "1700000000",
                                    "priority": "Normal",
                                    "script": "Default"
                                  },
                                  {
                                    "filename": "Fixture Paused",
                                    "cat": "movies",
                                    "status": "Paused",
                                    "percentage": "25",
                                    "size": "2.0 GB",
                                    "sizeleft": "1.5 GB",
                                    "timeleft": "--",
                                    "time_added": "1700000100",
                                    "priority": "Low",
                                    "script": "Cleanup"
                                  }
                                ]
                              }
                            }
                            """),

                    "history" =>
                        Json(
                            $$"""
                            {
                              "history": {
                                "day_size": "12.0 GB",
                                "week_size": "84.0 GB",
                                "month_size": "340.0 GB",
                                "total_size": "9.1 TB",
                                "slots": [
                                  {
                                    "name": "Fixture Complete",
                                    "category": "tv",
                                    "status": "Completed",
                                    "size": "4.0 GB",
                                    "completed": "1700001000",
                                    "download_time": "600",
                                    "stage_log": [
                                      {
                                        "name": "Unpack",
                                        "actions": [
                                          "Downloaded {{privateUrl}} to {{privatePathJson}}"
                                        ]
                                      }
                                    ]
                                  },
                                  {
                                    "name": "Fixture Failed",
                                    "category": "movies",
                                    "status": "Failed",
                                    "size": "2.0 GB",
                                    "completed": "1700002000",
                                    "postproc_time": "90",
                                    "fail_message": "Could not write {{privateUnixPath}} because the destination was busy"
                                  }
                                ]
                              }
                            }
                            """),

                    _ =>
                        Json(
                            "{}",
                            HttpStatusCode.NotFound)
                };
            });

    using var http =
        new HttpClient(
            handler);

    var client =
        new SABnzbdTelemetryClient(
            http);

    using var secret =
        new SecretValue(
            apiKey);

    var snapshot =
        await client.CaptureAsync(
            new SABnzbdTelemetryRequest(
                new Uri(
                    "http://sab.local:8080/sabnzbd/"),
                secret,
                RequireCompleteTelemetry:
                    true));

    Equal(
        "Online",
        snapshot.State,
        "SABnzbd state");

    Equal(
        "4.5.3",
        snapshot.Version,
        "SABnzbd version");

    Equal(
        "2.0 MB/s",
        snapshot.DownloadSpeed,
        "SABnzbd download speed");

    Equal(
        "3.5 GB",
        snapshot.Remaining,
        "SABnzbd remaining");

    Equal(
        "0:28:00",
        snapshot.Eta,
        "SABnzbd ETA");

    Equal(
        "512.3 GB",
        snapshot.DiskFree,
        "SABnzbd disk free");

    Equal(
        "Unlimited",
        snapshot.RateLimit,
        "SABnzbd rate limit");

    Equal(
        2,
        snapshot.TotalCount,
        "SABnzbd queue count");

    Equal(
        1,
        snapshot.ActiveCount,
        "SABnzbd active count");

    Equal(
        1,
        snapshot.DownloadingCount,
        "SABnzbd downloading count");

    Equal(
        1,
        snapshot.PausedCount,
        "SABnzbd paused count");

    Equal(
        1,
        snapshot.CompletedRecentCount,
        "SABnzbd completed count");

    Equal(
        1,
        snapshot.FailedRecentCount,
        "SABnzbd failed count");

    Equal(
        "12.0 GB",
        snapshot.DayDownloaded,
        "SABnzbd day total");

    Equal(
        "84.0 GB",
        snapshot.WeekDownloaded,
        "SABnzbd week total");

    Equal(
        "340.0 GB",
        snapshot.MonthDownloaded,
        "SABnzbd month total");

    Equal(
        "9.1 TB",
        snapshot.TotalDownloaded,
        "SABnzbd lifetime total");

    var download =
        snapshot.Queue.Single(item =>
            item.Name.Equals(
                "Fixture Download",
                StringComparison.Ordinal));

    Equal(
        "75.0%",
        download.Progress,
        "SABnzbd progress");

    Equal(
        "3072.0 MB",
        download.Downloaded,
        "SABnzbd downloaded amount");

    Equal(
        "1024.0 MB",
        download.Remaining,
        "SABnzbd item remaining");

    var complete =
        snapshot.History.Single(item =>
            item.Name.Equals(
                "Fixture Complete",
                StringComparison.Ordinal));

    True(
        complete.Detail.Contains(
            "[path]",
            StringComparison.Ordinal),
        "history path redaction");

    True(
        complete.Detail.Contains(
            "[url]",
            StringComparison.Ordinal),
        "history URL redaction");

    var failed =
        snapshot.History.Single(item =>
            item.Name.Equals(
                "Fixture Failed",
                StringComparison.Ordinal));

    True(
        failed.Detail.Contains(
            "[path]",
            StringComparison.Ordinal),
        "failure path redaction");

    var projected =
        string.Join(
            "|",
            snapshot.History.Select(item =>
                item.Detail));

    True(
        !projected.Contains(
            privatePath,
            StringComparison.Ordinal),
        "history omits private path");

    True(
        !projected.Contains(
            privateUrl,
            StringComparison.Ordinal),
        "history omits private URL");

    True(
        !projected.Contains(
            privateUnixPath,
            StringComparison.Ordinal),
        "history omits spaced Linux path");

    True(
        !projected.Contains(
            "Private Downloads",
            StringComparison.Ordinal),
        "history omits spaced Windows path remainder");

    True(
        !projected.Contains(
            "Plerver 2",
            StringComparison.Ordinal),
        "history omits spaced Linux path remainder");

    SequenceEqual(
        new[]
        {
            "version",
            "queue",
            "history"
        },
        handler.Requests.Select(item =>
            item.Mode),
        "SABnzbd request order");

    True(
        string.IsNullOrWhiteSpace(
            handler.Requests[0].ApiKey),
        "version request omits API key");

    True(
        handler.Requests
            .Skip(1)
            .All(item =>
                item.ApiKey.Equals(
                    apiKey,
                    StringComparison.Ordinal)),
        "protected requests carry API key");

    True(
        !snapshot.Detail.Contains(
            apiKey,
            StringComparison.Ordinal),
        "snapshot detail omits API key");
}

static async Task AuthenticationRedactionAsync()
{
    const string apiKey =
        "authentication-secret-do-not-leak";

    var handler =
        new FixtureHandler(
            request =>
                request.Mode switch
                {
                    "version" =>
                        Json(
                            """
                            {
                              "version": "4.5.3"
                            }
                            """),

                    "queue" =>
                        Json(
                            $$"""
                            {
                              "status": false,
                              "error": "API Key Incorrect: {{apiKey}}"
                            }
                            """),

                    _ =>
                        Json(
                            "{}",
                            HttpStatusCode.NotFound)
                });

    using var http =
        new HttpClient(
            handler);

    var client =
        new SABnzbdTelemetryClient(
            http);

    using var secret =
        new SecretValue(
            apiKey);

    try
    {
        await client.CaptureAsync(
            new SABnzbdTelemetryRequest(
                new Uri(
                    "http://localhost:8080/"),
                secret));
    }
    catch (InvalidOperationException exception)
    {
        Equal(
            "SABnzbd rejected the configured API key.",
            exception.Message,
            "sanitized authentication message");

        True(
            !exception.Message.Contains(
                apiKey,
                StringComparison.Ordinal),
            "authentication failure omits API key");

        return;
    }

    throw new InvalidOperationException(
        "Invalid SABnzbd API key was accepted.");
}

static async Task RedirectBlockedAsync()
{
    const string apiKey =
        "redirect-secret-do-not-leak";

    var handler =
        new FixtureHandler(
            request =>
                request.Mode.Equals(
                    "version",
                    StringComparison.Ordinal)
                    ? Json(
                        """
                        {
                          "version": "4.5.3"
                        }
                        """)
                    : new HttpResponseMessage(
                        HttpStatusCode.Redirect)
                    {
                        Headers =
                        {
                            Location =
                                new Uri(
                                    "http://untrusted.example/")
                        }
                    });

    using var http =
        new HttpClient(
            handler);

    var client =
        new SABnzbdTelemetryClient(
            http);

    using var secret =
        new SecretValue(
            apiKey);

    try
    {
        await client.CaptureAsync(
            new SABnzbdTelemetryRequest(
                new Uri(
                    "http://localhost:8080/"),
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
                apiKey,
                StringComparison.Ordinal),
            "redirect failure omits API key");

        Equal(
            2,
            handler.Requests.Count,
            "redirect request count");

        True(
            handler.Requests.All(item =>
                item.Host.Equals(
                    "localhost",
                    StringComparison.OrdinalIgnoreCase)),
            "redirect was not followed");

        return;
    }

    throw new InvalidOperationException(
        "Redirected SABnzbd queue request was accepted.");
}

static async Task StrictVerificationAsync()
{
    var handler =
        HistoryFailureHandler();

    using var http =
        new HttpClient(
            handler);

    var client =
        new SABnzbdTelemetryClient(
            http);

    using var secret =
        new SecretValue(
            "strict-sabnzbd-api-key");

    try
    {
        await client.CaptureAsync(
            new SABnzbdTelemetryRequest(
                new Uri(
                    "http://localhost:8080/"),
                secret,
                RequireCompleteTelemetry:
                    true));
    }
    catch (InvalidOperationException exception)
    {
        True(
            exception.Message.Contains(
                "Protected SABnzbd telemetry verification failed:",
                StringComparison.Ordinal),
            "strict verification failure message");

        True(
            exception.Message.Contains(
                "rejected the configured API key",
                StringComparison.Ordinal),
            "strict protected failure is sanitized");

        True(
            !exception.Message.Contains(
                "strict-sabnzbd-api-key",
                StringComparison.Ordinal),
            "strict verification omits API key");

        return;
    }

    throw new InvalidOperationException(
        "Strict SABnzbd verification accepted partial protected telemetry.");
}

static async Task PartialHistoryTelemetryAsync()
{
    var handler =
        HistoryFailureHandler();

    using var http =
        new HttpClient(
            handler);

    var client =
        new SABnzbdTelemetryClient(
            http);

    using var secret =
        new SecretValue(
            "partial-sabnzbd-api-key");

    var snapshot =
        await client.CaptureAsync(
            new SABnzbdTelemetryRequest(
                new Uri(
                    "http://localhost:8080/"),
                secret));

    Equal(
        "Attention",
        snapshot.State,
        "partial history state");

    Equal(
        1,
        snapshot.TotalCount,
        "partial queue count");

    True(
        snapshot.Detail.Contains(
            "recent history is unavailable",
            StringComparison.Ordinal),
        "partial history detail");

    Equal(
        0,
        snapshot.History.Count,
        "partial history rows");
}

static async Task ConfigurationRoundTripAsync()
{
    var root =
        Path.Combine(
            Path.GetTempPath(),
            "graveops-sab-tests-" +
            Guid.NewGuid()
                .ToString("N"));

    Directory.CreateDirectory(
        root);

    var path =
        Path.Combine(
            root,
            "sabnzbd-targets.json");

    try
    {
        var store =
            new WindowsSABnzbdConfigurationStore(
                path);

        await store.SaveAsync(
            "local-windows",
            "http://server.local:8080/sabnzbd/api");

        var resolved =
            await store.ResolveAsync(
                WindowsTargetCatalog.CreateLocal());

        Equal(
            "http://server.local:8080/sabnzbd/",
            resolved.Endpoint,
            "stored endpoint");

        var raw =
            await File.ReadAllTextAsync(
                path);

        True(
            raw.Contains(
                "server.local",
                StringComparison.Ordinal),
            "configuration contains endpoint");

        True(
            !raw.Contains(
                "api-key",
                StringComparison.OrdinalIgnoreCase),
            "configuration omits API-key fields");

        True(
            !raw.Contains(
                "apikey",
                StringComparison.OrdinalIgnoreCase),
            "configuration omits API-key query names");

        True(
            !raw.Contains(
                "fixture-api-key",
                StringComparison.OrdinalIgnoreCase),
            "configuration omits API-key values");
    }
    finally
    {
        Directory.Delete(
            root,
            recursive:
                true);
    }
}

static Task TargetLeaseAsync()
{
    var local =
        WindowsTargetCatalog.CreateLocal();

    True(
        WindowsSABnzbdTargetLease.IsCurrent(
            local.Id,
            local),
        "matching target lease");

    True(
        !WindowsSABnzbdTargetLease.IsCurrent(
            "different-target",
            local),
        "stale target lease");

    True(
        !WindowsSABnzbdTargetLease.IsCurrent(
            local.Id,
            currentTarget:
                null),
        "missing current target lease");

    return Task.CompletedTask;
}

static FixtureHandler HistoryFailureHandler() =>
    new(
        request =>
            request.Mode switch
            {
                "version" =>
                    Json(
                        """
                        {
                          "version": "4.5.3"
                        }
                        """),

                "queue" =>
                    Json(
                        """
                        {
                          "queue": {
                            "paused": false,
                            "kbpersec": "0",
                            "sizeleft": "1.0 GB",
                            "timeleft": "1:00:00",
                            "diskspace1": "100",
                            "speedlimit_abs": "0",
                            "noofslots": "1",
                            "slots": [
                              {
                                "filename": "Fixture Queue Item",
                                "cat": "tv",
                                "status": "Queued",
                                "percentage": "0",
                                "size": "1.0 GB",
                                "sizeleft": "1.0 GB"
                              }
                            ]
                          }
                        }
                        """),

                "history" =>
                    new HttpResponseMessage(
                        HttpStatusCode.Unauthorized),

                _ =>
                    Json(
                        "{}",
                        HttpStatusCode.NotFound)
            });

static HttpResponseMessage Json(
    string content,
    HttpStatusCode statusCode =
        HttpStatusCode.OK) =>
    new(
        statusCode)
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

static void SequenceEqual<T>(
    IEnumerable<T> expected,
    IEnumerable<T> actual,
    string description)
{
    var expectedArray =
        expected.ToArray();

    var actualArray =
        actual.ToArray();

    if (!expectedArray.SequenceEqual(
            actualArray))
    {
        throw new InvalidOperationException(
            $"{description}: expected " +
            $"[{string.Join(", ", expectedArray)}], got " +
            $"[{string.Join(", ", actualArray)}].");
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
        RequestRecord,
        HttpResponseMessage>
        _response;

    public FixtureHandler(
        Func<
            RequestRecord,
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
        var query =
            ParseQuery(
                request.RequestUri?.Query);

        query.TryGetValue(
            "mode",
            out var mode);

        query.TryGetValue(
            "apikey",
            out var apiKey);

        var record =
            new RequestRecord(
                request.RequestUri?
                    .AbsolutePath ??
                string.Empty,
                request.RequestUri?
                    .Host ??
                string.Empty,
                mode ??
                string.Empty,
                apiKey ??
                string.Empty);

        Requests.Add(
            record);

        return Task.FromResult(
            _response(
                record));
    }

    private static Dictionary<string, string>
        ParseQuery(
            string? query)
    {
        var result =
            new Dictionary<string, string>(
                StringComparer.OrdinalIgnoreCase);

        if (string.IsNullOrWhiteSpace(
                query))
        {
            return result;
        }

        foreach (var pair in
                 query.TrimStart('?')
                     .Split(
                         '&',
                         StringSplitOptions.RemoveEmptyEntries))
        {
            var parts =
                pair.Split(
                    '=',
                    2);

            var key =
                Uri.UnescapeDataString(
                    parts[0]);

            var value =
                parts.Length >
                1
                    ? Uri.UnescapeDataString(
                        parts[1])
                    : string.Empty;

            result[key] =
                value;
        }

        return result;
    }
}

sealed record RequestRecord(
    string Path,
    string Host,
    string Mode,
    string ApiKey);
