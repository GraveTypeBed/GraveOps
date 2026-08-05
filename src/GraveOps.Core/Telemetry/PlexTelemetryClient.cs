using System.Globalization;
using System.Net;
using System.Net.Http;
using System.Xml.Linq;
using GraveOps.Core.Security;

namespace GraveOps.Core.Telemetry;

public sealed record PlexTelemetryRequest(
    Uri BaseUri,
    string ClientIdentifier,
    SecretValue? Token = null,
    bool RequireProtectedTelemetry = false);

public static class PlexTelemetryEndpoint
{
    public static Uri Normalize(
        string value)
    {
        if (string.IsNullOrWhiteSpace(
                value))
        {
            throw new InvalidOperationException(
                "The Plex endpoint is required.");
        }

        if (!Uri.TryCreate(
                value.Trim(),
                UriKind.Absolute,
                out var parsed))
        {
            throw new InvalidOperationException(
                "The Plex endpoint must be an absolute HTTP or HTTPS URL.");
        }

        return Normalize(
            parsed);
    }

    public static Uri Normalize(
        Uri value)
    {
        ArgumentNullException.ThrowIfNull(
            value);

        if (!value.IsAbsoluteUri ||
            (
                !value.Scheme.Equals(
                    Uri.UriSchemeHttp,
                    StringComparison.OrdinalIgnoreCase) &&
                !value.Scheme.Equals(
                    Uri.UriSchemeHttps,
                    StringComparison.OrdinalIgnoreCase)
            ))
        {
            throw new InvalidOperationException(
                "The Plex endpoint must use HTTP or HTTPS.");
        }

        if (!string.IsNullOrWhiteSpace(
                value.UserInfo))
        {
            throw new InvalidOperationException(
                "Credentials cannot be embedded in the Plex endpoint.");
        }

        var builder =
            new UriBuilder(
                value)
            {
                Query =
                    string.Empty,

                Fragment =
                    string.Empty
            };

        var path =
            builder.Path.TrimEnd('/');

        if (path.EndsWith(
                "/web",
                StringComparison.OrdinalIgnoreCase))
        {
            path =
                path[..^4];
        }

        builder.Path =
            string.IsNullOrWhiteSpace(
                path)
                ? "/"
                : path + "/";

        return builder.Uri;
    }
}

public sealed class PlexTelemetryClient
{
    private readonly HttpClient _client;

    public PlexTelemetryClient(
        HttpClient client)
    {
        _client =
            client ??
            throw new ArgumentNullException(
                nameof(client));
    }

    public async Task<PlexTelemetrySnapshot> CaptureAsync(
        PlexTelemetryRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(
            request);

        if (string.IsNullOrWhiteSpace(
                request.ClientIdentifier))
        {
            throw new InvalidOperationException(
                "The Plex client identifier is required.");
        }

        var baseUri =
            PlexTelemetryEndpoint.Normalize(
                request.BaseUri);

        var token =
            request.Token is null
                ? null
                : new string(
                    request.Token.Reveal().Span);

        XDocument identity;

        try
        {
            identity =
                await GetXmlAsync(
                    baseUri,
                    "identity",
                    token,
                    request.ClientIdentifier,
                    cancellationToken);
        }
        catch (InvalidOperationException firstFailure)
        {
            try
            {
                identity =
                    await GetXmlAsync(
                        baseUri,
                        ":/identity",
                        token,
                        request.ClientIdentifier,
                        cancellationToken);
            }
            catch (InvalidOperationException)
            {
                throw firstFailure;
            }
        }

        var identityRoot =
            identity.Root ??
            throw new InvalidOperationException(
                "Plex identity XML did not contain a root element.");

        var snapshot =
            new PlexTelemetrySnapshot
            {
                State =
                    "Online",

                Service =
                    "Plex API",

                ServiceDetail =
                    "Server identity verified",

                Version =
                    Attribute(
                        identityRoot,
                        "version",
                        "--"),

                Endpoint =
                    new Uri(
                        baseUri,
                        "web")
                    .AbsoluteUri,

                Connection =
                    "Direct HTTP API",

                Dependency =
                    "Plex Media Server API",

                Security =
                    request.Token is null
                        ? "Identity-only telemetry Â· no protected token configured"
                        : "Protected Plex telemetry Â· token used only in request headers",

                Detail =
                    string.IsNullOrWhiteSpace(
                        Attribute(
                            identityRoot,
                            "machineIdentifier"))
                        ? "Plex identity endpoint answered."
                        : "Plex server identity verified.",

                SampledAt =
                    DateTimeOffset.UtcNow
            };

        if (string.IsNullOrWhiteSpace(
                token))
        {
            return snapshot;
        }

        var protectedErrors =
            new List<string>();

        try
        {
            var sessions =
                await GetXmlAsync(
                    baseUri,
                    "status/sessions",
                    token,
                    request.ClientIdentifier,
                    cancellationToken);

            ParseSessions(
                sessions,
                snapshot);
        }
        catch (Exception exception)
            when (exception is not OperationCanceledException)
        {
            protectedErrors.Add(
                "sessions unavailable: " +
                exception.Message);
        }

        try
        {
            var libraries =
                await GetXmlAsync(
                    baseUri,
                    "library/sections",
                    token,
                    request.ClientIdentifier,
                    cancellationToken);

            snapshot.LibraryCount =
                libraries.Root?
                    .Elements()
                    .Count(element =>
                        element.Name.LocalName.Equals(
                            "Directory",
                            StringComparison.OrdinalIgnoreCase)) ??
                0;
        }
        catch (Exception exception)
            when (exception is not OperationCanceledException)
        {
            protectedErrors.Add(
                "libraries unavailable: " +
                exception.Message);
        }

        if (protectedErrors.Count > 0)
        {
            var protectedFailure =
                string.Join(
                    " Â· ",
                    protectedErrors);

            if (request.RequireProtectedTelemetry)
            {
                throw new InvalidOperationException(
                    "Plex protected telemetry verification failed: " +
                    protectedFailure);
            }

            snapshot.Detail =
                string.Join(
                    " Â· ",
                    new[]
                    {
                        snapshot.Detail
                    }.Concat(
                        protectedErrors));
        }

        return snapshot;
    }

    private async Task<XDocument> GetXmlAsync(
        Uri baseUri,
        string relativePath,
        string? token,
        string clientIdentifier,
        CancellationToken cancellationToken)
    {
        var requestUri =
            new Uri(
                baseUri,
                relativePath);

        using var request =
            new HttpRequestMessage(
                HttpMethod.Get,
                requestUri);

        request.Headers.TryAddWithoutValidation(
            "Accept",
            "application/xml");

        request.Headers.TryAddWithoutValidation(
            "X-Plex-Product",
            "GraveOps");

        request.Headers.TryAddWithoutValidation(
            "X-Plex-Client-Identifier",
            clientIdentifier);

        if (!string.IsNullOrWhiteSpace(
                token))
        {
            request.Headers.TryAddWithoutValidation(
                "X-Plex-Token",
                token);
        }

        HttpResponseMessage response;

        try
        {
            response =
                await _client.SendAsync(
                    request,
                    HttpCompletionOption.ResponseContentRead,
                    cancellationToken);
        }
        catch (OperationCanceledException)
            when (!cancellationToken.IsCancellationRequested)
        {
            throw new InvalidOperationException(
                $"Plex did not respond at {baseUri.GetLeftPart(UriPartial.Authority)} before the request timed out.");
        }
        catch (HttpRequestException exception)
        {
            throw new InvalidOperationException(
                $"Could not reach Plex at {baseUri.GetLeftPart(UriPartial.Authority)}.",
                exception);
        }

        using (response)
        {
            if (response.StatusCode is
                HttpStatusCode.Unauthorized or
                HttpStatusCode.Forbidden)
            {
                throw new InvalidOperationException(
                    "Plex rejected the configured token.");
            }

            if (!response.IsSuccessStatusCode)
            {
                throw new InvalidOperationException(
                    $"Plex API returned HTTP {(int)response.StatusCode} {response.ReasonPhrase}.");
            }

            var xml =
                await response.Content.ReadAsStringAsync(
                    cancellationToken);

            try
            {
                return XDocument.Parse(
                    xml);
            }
            catch (Exception exception)
            {
                throw new InvalidOperationException(
                    "Plex returned data that was not valid XML.",
                    exception);
            }
        }
    }

    private static void ParseSessions(
        XDocument document,
        PlexTelemetrySnapshot snapshot)
    {
        var root =
            document.Root;

        if (root is null)
            return;

        long totalBandwidth =
            0;

        foreach (var item in
                 root.Elements()
                     .Where(element =>
                         element.Name.LocalName is
                             "Video" or
                             "Track" or
                             "Photo"))
        {
            var user =
                Child(
                    item,
                    "User");

            var player =
                Child(
                    item,
                    "Player");

            var session =
                Child(
                    item,
                    "Session");

            var media =
                Child(
                    item,
                    "Media");

            var transcode =
                Child(
                    item,
                    "TranscodeSession");

            var videoDecision =
                Decision(
                    transcode,
                    media,
                    "videoDecision");

            var audioDecision =
                Decision(
                    transcode,
                    media,
                    "audioDecision");

            var combined =
                $"{videoDecision} {audioDecision}";

            if (combined.Contains(
                    "Transcode",
                    StringComparison.OrdinalIgnoreCase))
            {
                snapshot.TranscodeCount++;
            }
            else if (combined.Contains(
                         "Direct Stream",
                         StringComparison.OrdinalIgnoreCase))
            {
                snapshot.DirectStreamCount++;
            }
            else
            {
                snapshot.DirectPlayCount++;
            }

            var bandwidth =
                FirstPositive(
                    LongAttribute(
                        session,
                        "bandwidth"),
                    LongAttribute(
                        transcode,
                        "bandwidth"),
                    LongAttribute(
                        media,
                        "bitrate"));

            totalBandwidth +=
                bandwidth;

            snapshot.Sessions.Add(
                new PlexSessionTelemetry
                {
                    Title =
                        SessionTitle(
                            item),

                    User =
                        Attribute(
                            user,
                            "title",
                            "Unknown user"),

                    Player =
                        First(
                            Attribute(
                                player,
                                "title"),
                            Attribute(
                                player,
                                "product"),
                            Attribute(
                                player,
                                "platform"),
                            "Unknown player"),

                    State =
                        Attribute(
                            player,
                            "state",
                            "playing")
                        .ToUpperInvariant(),

                    Progress =
                        Progress(
                            item),

                    VideoDecision =
                        videoDecision,

                    AudioDecision =
                        audioDecision,

                    Bandwidth =
                        FormatBandwidth(
                            bandwidth),

                    Detail =
                        string.Join(
                            " Â· ",
                            new[]
                            {
                                Attribute(
                                    item,
                                    "type"),
                                Attribute(
                                    item,
                                    "year"),
                                Attribute(
                                    media,
                                    "container")
                            }.Where(value =>
                                !string.IsNullOrWhiteSpace(
                                    value)))
                });
        }

        snapshot.ActiveSessions =
            snapshot.Sessions.Count;

        snapshot.TotalBandwidth =
            FormatBandwidth(
                totalBandwidth);
    }

    private static XElement? Child(
        XElement parent,
        string localName) =>
        parent.Elements()
            .FirstOrDefault(element =>
                element.Name.LocalName.Equals(
                    localName,
                    StringComparison.OrdinalIgnoreCase));

    private static string Decision(
        XElement? transcode,
        XElement? media,
        string attribute)
    {
        var raw =
            First(
                Attribute(
                    transcode,
                    attribute),
                Attribute(
                    media,
                    attribute));

        if (string.IsNullOrWhiteSpace(
                raw))
        {
            return transcode is null
                ? "Direct Play"
                : "Transcode";
        }

        return raw.Trim()
            .ToLowerInvariant() switch
            {
                "copy" =>
                    "Direct Stream",

                "directstream" =>
                    "Direct Stream",

                "direct stream" =>
                    "Direct Stream",

                "transcode" =>
                    "Transcode",

                "directplay" =>
                    "Direct Play",

                "direct play" =>
                    "Direct Play",

                _ =>
                    CultureInfo.InvariantCulture
                        .TextInfo
                        .ToTitleCase(
                            raw.Trim()
                                .ToLowerInvariant())
            };
    }

    private static string SessionTitle(
        XElement item)
    {
        var title =
            Attribute(
                item,
                "title",
                "Unknown media");

        var grandparent =
            Attribute(
                item,
                "grandparentTitle");

        var parent =
            Attribute(
                item,
                "parentTitle");

        var season =
            IntAttribute(
                item,
                "parentIndex");

        var episode =
            IntAttribute(
                item,
                "index");

        if (!string.IsNullOrWhiteSpace(
                grandparent))
        {
            var parts =
                new List<string>
                {
                    grandparent
                };

            if (season > 0 &&
                episode > 0)
            {
                parts.Add(
                    $"S{season:00}E{episode:00}");
            }

            parts.Add(
                title);

            return string.Join(
                " Â· ",
                parts);
        }

        if (!string.IsNullOrWhiteSpace(
                parent) &&
            !parent.Equals(
                title,
                StringComparison.OrdinalIgnoreCase))
        {
            return
                $"{parent} Â· {title}";
        }

        return title;
    }

    private static string Progress(
        XElement item)
    {
        var duration =
            LongAttribute(
                item,
                "duration");

        var offset =
            LongAttribute(
                item,
                "viewOffset");

        if (duration <= 0)
            return "--";

        var percent =
            Math.Clamp(
                offset * 100d /
                duration,
                0d,
                100d);

        return
            $"{percent:0}%";
    }

    private static string FormatBandwidth(
        long kilobitsPerSecond)
    {
        if (kilobitsPerSecond >= 1000)
        {
            return
                $"{kilobitsPerSecond / 1000d:0.0} Mbps";
        }

        return
            $"{Math.Max(0, kilobitsPerSecond)} Kbps";
    }

    private static long FirstPositive(
        params long[] values) =>
        values.FirstOrDefault(
            value =>
                value > 0);

    private static string First(
        params string[] values) =>
        values.FirstOrDefault(value =>
            !string.IsNullOrWhiteSpace(
                value)) ??
        string.Empty;

    private static string Attribute(
        XElement? element,
        string name,
        string fallback = "")
    {
        var value =
            element?
                .Attributes()
                .FirstOrDefault(attribute =>
                    attribute.Name.LocalName.Equals(
                        name,
                        StringComparison.OrdinalIgnoreCase))
                ?.Value;

        return string.IsNullOrWhiteSpace(
                value)
            ? fallback
            : value.Trim();
    }

    private static long LongAttribute(
        XElement? element,
        string name) =>
        long.TryParse(
            Attribute(
                element,
                name),
            NumberStyles.Integer,
            CultureInfo.InvariantCulture,
            out var parsed)
            ? parsed
            : 0;

    private static int IntAttribute(
        XElement? element,
        string name) =>
        int.TryParse(
            Attribute(
                element,
                name),
            NumberStyles.Integer,
            CultureInfo.InvariantCulture,
            out var parsed)
            ? parsed
            : 0;
}