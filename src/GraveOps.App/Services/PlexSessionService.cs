using System.Globalization;
using System.Net;
using System.Net.Http;
using System.Xml.Linq;
using GraveOps.App.Models;

namespace GraveOps.App.Services;

public sealed class PlexSessionService
{
    private readonly AppServices _services;
    private readonly PlexTokenStore _tokens = new();

    private static readonly HttpClient Client =
        new(
            new SocketsHttpHandler
            {
                UseProxy = false,
                ConnectTimeout = TimeSpan.FromSeconds(5)
            })
        {
            Timeout = TimeSpan.FromSeconds(10)
        };

    public PlexSessionService(AppServices services)
        => _services = services;

    public bool HasToken(ServerProfile server)
        => _tokens.HasToken(server);

    public void ClearToken(ServerProfile server)
    {
        _tokens.Delete(server);

        _services.Activity.Record(
            "Plex session token removed",
            $"Removed the Windows Credential Manager token for {server.Name}.",
            ActivityLevel.Info,
            serverId: server.Id,
            deepLink: "page:Applications");
    }

    public async Task<PlexSessionSnapshot> TestAndSaveAsync(
        ServerProfile server,
        string token,
        CancellationToken cancellationToken = default)
    {
        token = (token ?? "").Trim();

        if (token.Length == 0)
            throw new InvalidOperationException(
                "Enter a Plex token first.");

        var snapshot =
            await FetchWithTokenAsync(
                server,
                token,
                cancellationToken);

        _tokens.Save(server, token);

        _services.Activity.Record(
            "Plex session token saved",
            $"Verified Plex session API access for {server.Name}. The token is stored in Windows Credential Manager.",
            ActivityLevel.Success,
            serverId: server.Id,
            deepLink: "page:Applications");

        return snapshot;
    }

    public async Task<PlexSessionSnapshot> GetAsync(
        ServerProfile server,
        CancellationToken cancellationToken = default)
    {
        if (!_tokens.TryRead(server, out var token))
            throw new InvalidOperationException(
                "Plex session analytics is not configured. Enter the server token and use Save + test.");

        return await FetchWithTokenAsync(
            server,
            token,
            cancellationToken);
    }

    private async Task<PlexSessionSnapshot> FetchWithTokenAsync(
        ServerProfile server,
        string token,
        CancellationToken cancellationToken)
    {
        var baseUri =
            new Uri(
                $"http://{server.Host}:32400/",
                UriKind.Absolute);

        var identityTask =
            GetXmlAsync(
                new Uri(baseUri, "identity"),
                token,
                server,
                cancellationToken);

        var sessionsTask =
            GetXmlAsync(
                new Uri(baseUri, "status/sessions"),
                token,
                server,
                cancellationToken);

        await Task.WhenAll(
            identityTask,
            sessionsTask);

        var identity = identityTask.Result;
        var sessions = sessionsTask.Result;

        var snapshot = new PlexSessionSnapshot
        {
            ServerName = server.Name,
            Version = Attr(identity.Root, "version", "--"),
            MachineIdentifier =
                Attr(identity.Root, "machineIdentifier", ""),
            Platform =
                Attr(identity.Root, "platform", "")
        };

        var root = sessions.Root;

        if (root is null)
            return snapshot;

        foreach (var item in root.Elements()
                     .Where(
                         x => x.Name.LocalName is
                             "Video" or
                             "Track" or
                             "Photo"))
        {
            snapshot.Sessions.Add(
                ParseSession(item));
        }

        return snapshot;
    }

    private static async Task<XDocument> GetXmlAsync(
        Uri uri,
        string token,
        ServerProfile server,
        CancellationToken cancellationToken)
    {
        using var request =
            new HttpRequestMessage(
                HttpMethod.Get,
                uri);

        request.Headers.TryAddWithoutValidation(
            "Accept",
            "application/xml");

        request.Headers.TryAddWithoutValidation(
            "X-Plex-Token",
            token);

        request.Headers.TryAddWithoutValidation(
            "X-Plex-Product",
            "GraveOps");

        request.Headers.TryAddWithoutValidation(
            "X-Plex-Version",
            GraveOps.App.BuildInfo.DisplayVersion);

        request.Headers.TryAddWithoutValidation(
            "X-Plex-Client-Identifier",
            $"graveops-{server.Id:N}");

        request.Headers.TryAddWithoutValidation(
            "X-Plex-Platform",
            "Windows");

        HttpResponseMessage response;

        try
        {
            response =
                await Client.SendAsync(
                    request,
                    HttpCompletionOption.ResponseContentRead,
                    cancellationToken);
        }
        catch (Exception ex)
            when (ex is HttpRequestException or TaskCanceledException)
        {
            throw new InvalidOperationException(
                $"Could not reach Plex directly at {uri.Scheme}://{uri.Host}:{uri.Port}. " +
                "Verify the Windows PC can reach the Plex LAN endpoint on port 32400.",
                ex);
        }

        using (response)
        {
            if (response.StatusCode is
                HttpStatusCode.Unauthorized or
                HttpStatusCode.Forbidden)
            {
                throw new InvalidOperationException(
                    "Plex rejected the token. Verify the X-Plex-Token and try again.");
            }

            if (!response.IsSuccessStatusCode)
            {
                throw new InvalidOperationException(
                    $"Plex session API returned HTTP {(int)response.StatusCode} {response.ReasonPhrase}.");
            }

            var xml =
                await response.Content.ReadAsStringAsync(
                    cancellationToken);

            try
            {
                return XDocument.Parse(xml);
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException(
                    "Plex returned a response that GraveOps could not parse as XML.",
                    ex);
            }
        }
    }

    private static PlexSessionRow ParseSession(
        XElement item)
    {
        var user =
            item.Elements()
                .FirstOrDefault(
                    x => x.Name.LocalName == "User");

        var player =
            item.Elements()
                .FirstOrDefault(
                    x => x.Name.LocalName == "Player");

        var session =
            item.Elements()
                .FirstOrDefault(
                    x => x.Name.LocalName == "Session");

        var media =
            item.Elements()
                .FirstOrDefault(
                    x => x.Name.LocalName == "Media");

        var transcode =
            item.Elements()
                .FirstOrDefault(
                    x => x.Name.LocalName == "TranscodeSession");

        var videoDecision =
            First(
                Attr(transcode, "videoDecision"),
                Attr(media, "videoDecision"));

        var audioDecision =
            First(
                Attr(transcode, "audioDecision"),
                Attr(media, "audioDecision"));

        var containerDecision =
            First(
                Attr(transcode, "containerDecision"),
                Attr(media, "containerDecision"));

        var decision =
            ResolveDecision(
                transcode,
                videoDecision,
                audioDecision,
                containerDecision);

        var duration =
            LongAttr(item, "duration");

        var offset =
            LongAttr(item, "viewOffset");

        var progress =
            duration > 0
                ? Math.Clamp(
                    offset * 100d / duration,
                    0,
                    100)
                : 0;

        var bandwidth =
            LongAttr(session, "bandwidth");

        if (bandwidth <= 0)
            bandwidth =
                LongAttr(media, "bitrate");

        var width =
            LongAttr(media, "width");

        var height =
            LongAttr(media, "height");

        var resolution =
            Attr(media, "videoResolution");

        var quality =
            resolution.Length > 0
                ? resolution
                : width > 0 && height > 0
                    ? $"{width}x{height}"
                    : "--";

        var bitrate =
            LongAttr(media, "bitrate");

        if (bitrate > 0)
            quality +=
                $" | {PlexSessionSnapshot.FormatKbps(bitrate)}";

        return new PlexSessionRow
        {
            User =
                Attr(user, "title", "Unknown user"),

            Title =
                BuildTitle(item),

            MediaType =
                Attr(item, "type", item.Name.LocalName),

            Player =
                Attr(player, "title", "Unknown client"),

            Product =
                Attr(player, "product"),

            Platform =
                Attr(player, "platform"),

            State =
                Attr(player, "state"),

            Location =
                First(
                    Attr(session, "location"),
                    Attr(player, "local") == "1"
                        ? "lan"
                        : "wan"),

            Address =
                Attr(player, "address"),

            Decision =
                decision,

            ProgressPercent =
                progress,

            ProgressText =
                duration > 0
                    ? $"{progress:0.0}%"
                    : "--",

            BandwidthKbps =
                bandwidth,

            BandwidthText =
                PlexSessionSnapshot.FormatKbps(
                    bandwidth),

            Quality =
                quality,

            VideoCodec =
                First(
                    Attr(transcode, "videoCodec"),
                    Attr(media, "videoCodec"),
                    "--"),

            AudioCodec =
                First(
                    Attr(transcode, "audioCodec"),
                    Attr(media, "audioCodec"),
                    "--"),

            Container =
                First(
                    Attr(transcode, "container"),
                    Attr(media, "container"),
                    "--"),

            VideoDecision =
                videoDecision,

            AudioDecision =
                audioDecision,

            ContainerDecision =
                containerDecision,

            TranscodeSpeed =
                Attr(transcode, "speed"),

            TranscodeProgress =
                Attr(transcode, "progress"),

            HardwareTranscode =
                HardwareText(transcode),

            SecureText =
                BoolText(
                    Attr(player, "secure")),

            RelayedText =
                BoolText(
                    Attr(player, "relayed")),

            SessionId =
                First(
                    Attr(session, "id"),
                    Attr(transcode, "key"))
        };
    }

    private static string BuildTitle(
        XElement item)
    {
        var type =
            Attr(item, "type").ToLowerInvariant();

        var title =
            Attr(item, "title", "Untitled");

        var parent =
            Attr(item, "parentTitle");

        var grandparent =
            Attr(item, "grandparentTitle");

        if (type == "episode")
        {
            var season =
                IntAttr(item, "parentIndex");

            var episode =
                IntAttr(item, "index");

            var code =
                season > 0 && episode > 0
                    ? $" S{season:00}E{episode:00}"
                    : "";

            return grandparent.Length > 0
                ? $"{grandparent}{code} - {title}"
                : title;
        }

        if (type == "track")
        {
            var prefix =
                grandparent.Length > 0
                    ? grandparent
                    : parent;

            return prefix.Length > 0
                ? $"{prefix} - {title}"
                : title;
        }

        var year =
            Attr(item, "year");

        return year.Length > 0
            ? $"{title} ({year})"
            : title;
    }

    private static string ResolveDecision(
        XElement? transcode,
        string videoDecision,
        string audioDecision,
        string containerDecision)
    {
        var decisions =
            new[]
            {
                videoDecision,
                audioDecision,
                containerDecision
            }
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(x => x.ToLowerInvariant())
            .ToArray();

        if (decisions.Any(x => x == "transcode"))
            return "TRANSCODE";

        if (decisions.Any(x => x == "copy") ||
            transcode is not null)
            return "DIRECT STREAM";

        return "DIRECT PLAY";
    }

    private static string HardwareText(
        XElement? transcode)
    {
        if (transcode is null)
            return "";

        var requested =
            Attr(transcode, "transcodeHwRequested");

        var decoding =
            Attr(transcode, "transcodeHwDecoding");

        var encoding =
            Attr(transcode, "transcodeHwEncoding");

        if (requested == "1" ||
            decoding == "1" ||
            encoding == "1")
        {
            return $"Requested {BoolText(requested)} | Decode {BoolText(decoding)} | Encode {BoolText(encoding)}";
        }

        return "No hardware transcode flags reported";
    }

    private static string BoolText(
        string value)
        => value == "1"
            ? "Yes"
            : value == "0"
                ? "No"
                : value.Length == 0
                    ? "--"
                    : value;

    private static string First(
        params string[] values)
        => values.FirstOrDefault(
               x => !string.IsNullOrWhiteSpace(x))
           ?? "";

    private static string Attr(
        XElement? element,
        string name,
        string fallback = "")
        => element?.Attribute(name)?.Value
           ?? fallback;

    private static long LongAttr(
        XElement? element,
        string name)
        => long.TryParse(
                Attr(element, name),
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out var value)
            ? value
            : 0;

    private static int IntAttr(
        XElement? element,
        string name)
        => int.TryParse(
                Attr(element, name),
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out var value)
            ? value
            : 0;
}