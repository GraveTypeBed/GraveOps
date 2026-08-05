using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace GraveOps.Core.Telemetry;

public sealed record MediaLifecycleSourceRow(
    string Source,
    string State,
    string Summary);

public sealed record MediaLifecycleItemRow(
    string Title,
    string MediaType,
    string ManagedBy,
    string Stage,
    string DownloadClient,
    string Progress,
    string Remaining,
    string Plex,
    string Confidence,
    string Evidence);

public sealed record MediaLifecycleSnapshot(
    DateTimeOffset CapturedAt,
    string OverallState,
    int TotalCount,
    int AcquisitionCount,
    int TransferCount,
    int ProcessingCount,
    int PlayingCount,
    int AttentionCount,
    string SourceSummary,
    IReadOnlyList<MediaLifecycleSourceRow> Sources,
    IReadOnlyList<MediaLifecycleItemRow> Items) :
    IApplicationTelemetrySnapshot;

public static class MediaLifecycleCorrelator
{
    private static readonly Regex SeparatorPattern =
        new(
            @"[^a-z0-9]+",
            RegexOptions.Compiled |
            RegexOptions.IgnoreCase);

    private static readonly Regex EpisodePattern =
        new(
            @"^(?:s\d{1,3}e\d{1,3}|season\d{1,3}|episode\d{1,4})$",
            RegexOptions.Compiled |
            RegexOptions.IgnoreCase);

    private static readonly Regex ReleaseYearPattern =
        new(
            @"\b(?:19|20)\d{2}\b",
            RegexOptions.Compiled);

    private static readonly HashSet<string> NoiseTokens =
        new(
            new[]
            {
                "a",
                "an",
                "and",
                "at",
                "by",
                "for",
                "from",
                "in",
                "of",
                "on",
                "the",
                "to",
                "with",
                "web",
                "webdl",
                "webrip",
                "bluray",
                "brrip",
                "hdtv",
                "remux",
                "proper",
                "repack",
                "internal",
                "complete",
                "multi",
                "dubbed",
                "subbed",
                "amzn",
                "nf",
                "dsnp",
                "hmax",
                "atvp",
                "uhd",
                "hdr",
                "dv",
                "dolby",
                "atmos",
                "aac",
                "ac3",
                "eac3",
                "dts",
                "truehd",
                "x264",
                "x265",
                "h264",
                "h265",
                "hevc",
                "av1",
                "flac",
                "mp3",
                "2160p",
                "1080p",
                "720p",
                "480p"
            },
            StringComparer.OrdinalIgnoreCase);

    private static readonly IReadOnlyDictionary<string, int>
        StagePriority =
            new Dictionary<string, int>(
                StringComparer.OrdinalIgnoreCase)
            {
                ["Attention"] = 0,
                ["Playing"] = 1,
                ["Processing"] = 2,
                ["Downloading"] = 3,
                ["Paused"] = 4,
                ["Awaiting import"] = 5,
                ["Acquisition"] = 6,
                ["Completed"] = 7
            };

    public static MediaLifecycleSnapshot Build(
        IEnumerable<ArrLiveTelemetrySnapshot>? arrSnapshots,
        DownloadClientTelemetrySnapshot? qbittorrent,
        DownloadClientTelemetrySnapshot? sabnzbd,
        PlexTelemetrySnapshot? plex,
        IEnumerable<MediaLifecycleSourceRow>? sourceFailures = null,
        DateTimeOffset? capturedAt = null)
    {
        var arr =
            (arrSnapshots ??
             Array.Empty<ArrLiveTelemetrySnapshot>())
            .Where(snapshot =>
                snapshot is not null)
            .ToArray();

        var failures =
            (sourceFailures ??
             Array.Empty<MediaLifecycleSourceRow>())
            .Where(row =>
                row is not null)
            .ToArray();

        var sources =
            BuildSources(
                arr,
                qbittorrent,
                sabnzbd,
                plex,
                failures);

        var plexSessions =
            plex?.Sessions ??
            new List<PlexSessionTelemetry>();

        var transfers =
            BuildTransferCandidates(
                qbittorrent,
                sabnzbd);

        var usedTransfers =
            new HashSet<int>();

        var usedPlexSessions =
            new HashSet<int>();

        var items =
            new List<MediaLifecycleItemRow>();

        foreach (var work in
                 arr.SelectMany(snapshot =>
                     snapshot.WorkItems)
                    .Where(item =>
                        !item.Type.Equals(
                            "Indexer",
                            StringComparison.OrdinalIgnoreCase)))
        {
            var transferMatch =
                BestTransferMatch(
                    work.ItemIssue,
                    transfers,
                    usedTransfers);

            var sessionMatch =
                BestPlexMatch(
                    work.ItemIssue,
                    plexSessions,
                    usedPlexSessions);

            if (transferMatch is not null)
            {
                usedTransfers.Add(
                    transferMatch.Index);
            }

            if (sessionMatch is not null)
            {
                usedPlexSessions.Add(
                    sessionMatch.Index);
            }

            items.Add(
                BuildArrRow(
                    work,
                    transferMatch?.Candidate,
                    transferMatch?.Score,
                    sessionMatch?.Session,
                    sessionMatch?.Score,
                    plex));
        }

        foreach (var transfer in transfers)
        {
            if (usedTransfers.Contains(
                    transfer.Index))
            {
                continue;
            }

            if (!transfer.IsQueue &&
                !transfer.State.Equals(
                    "Completed",
                    StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var sessionMatch =
                BestPlexMatch(
                    transfer.Title,
                    plexSessions,
                    usedPlexSessions);

            if (sessionMatch is not null)
            {
                usedPlexSessions.Add(
                    sessionMatch.Index);
            }

            items.Add(
                BuildTransferOnlyRow(
                    transfer,
                    sessionMatch?.Session,
                    sessionMatch?.Score,
                    plex));
        }

        for (var index = 0;
             index < plexSessions.Count;
             index++)
        {
            if (usedPlexSessions.Contains(
                    index))
            {
                continue;
            }

            items.Add(
                new MediaLifecycleItemRow(
                    plexSessions[index].Title,
                    "Playback",
                    "Plex",
                    "Playing",
                    "--",
                    plexSessions[index].Progress,
                    "--",
                    "Playing now",
                    "Direct Plex evidence",
                    "Active Plex session; no transfer correlation was required."));
        }

        var ordered =
            items
                .Where(item =>
                    !string.IsNullOrWhiteSpace(
                        item.Title))
                .GroupBy(
                    item =>
                        string.Join(
                            "|",
                            NormalizeTitle(
                                item.Title),
                            item.Stage,
                            item.DownloadClient),
                    StringComparer.Ordinal)
                .Select(group =>
                    group.First())
                .OrderBy(item =>
                    StagePriority.TryGetValue(
                        item.Stage,
                        out var priority)
                        ? priority
                        : 99)
                .ThenBy(
                    item =>
                        item.Title,
                    StringComparer.OrdinalIgnoreCase)
                .Take(100)
                .ToArray();

        var attention =
            ordered.Count(item =>
                item.Stage.Equals(
                    "Attention",
                    StringComparison.OrdinalIgnoreCase)) +
            failures.Length;

        var acquisition =
            ordered.Count(item =>
                item.Stage.Equals(
                    "Acquisition",
                    StringComparison.OrdinalIgnoreCase));

        var transferCount =
            ordered.Count(item =>
                item.Stage is
                    "Downloading" or
                    "Paused");

        var processing =
            ordered.Count(item =>
                item.Stage is
                    "Processing" or
                    "Awaiting import" or
                    "Completed");

        var playing =
            ordered.Count(item =>
                item.Stage.Equals(
                    "Playing",
                    StringComparison.OrdinalIgnoreCase));

        var overallState =
            attention > 0
                ? "Attention"
                : ordered.Any(item =>
                    item.Stage is
                        "Downloading" or
                        "Processing" or
                        "Playing")
                    ? "Active"
                    : sources.Any(source =>
                        IsOnline(
                            source.State))
                        ? "Online"
                        : "Unavailable";

        var sampleTime =
            capturedAt ??
            LatestCapture(
                arr,
                qbittorrent,
                sabnzbd,
                plex);

        return new MediaLifecycleSnapshot(
            sampleTime,
            overallState,
            ordered.Length,
            acquisition,
            transferCount,
            processing,
            playing,
            attention,
            SourceSummary(
                sources),
            sources,
            ordered);
    }

    private static IReadOnlyList<MediaLifecycleSourceRow>
        BuildSources(
            IReadOnlyList<ArrLiveTelemetrySnapshot> arr,
            DownloadClientTelemetrySnapshot? qbittorrent,
            DownloadClientTelemetrySnapshot? sabnzbd,
            PlexTelemetrySnapshot? plex,
            IReadOnlyList<MediaLifecycleSourceRow> failures)
    {
        var result =
            new List<MediaLifecycleSourceRow>();

        foreach (var snapshot in arr)
        {
            var service =
                snapshot.Services.FirstOrDefault();

            result.Add(
                new MediaLifecycleSourceRow(
                    service?.Service ??
                    "Arr",
                    snapshot.OverallState,
                    string.Join(
                        " · ",
                        new[]
                        {
                            snapshot.WorkSummary,
                            snapshot.HealthSummary
                        }
                        .Where(value =>
                            !string.IsNullOrWhiteSpace(
                                value)))));
        }

        AddDownloadSource(
            result,
            qbittorrent,
            "qBittorrent");

        AddDownloadSource(
            result,
            sabnzbd,
            "SABnzbd");

        if (plex is not null)
        {
            result.Add(
                new MediaLifecycleSourceRow(
                    "Plex",
                    plex.State,
                    $"{plex.LibraryCount} libraries · " +
                    $"{plex.ActiveSessions} active sessions"));
        }

        foreach (var failure in failures)
        {
            var existingIndex =
                result.FindIndex(item =>
                    item.Source.Equals(
                        failure.Source,
                        StringComparison.OrdinalIgnoreCase));

            if (existingIndex >=
                0)
            {
                result[existingIndex] =
                    failure;
            }
            else
            {
                result.Add(failure);
            }
        }

        return result
            .OrderBy(
                row =>
                    SourceOrder(
                        row.Source))
            .ThenBy(
                row =>
                    row.Source,
                StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static void AddDownloadSource(
        ICollection<MediaLifecycleSourceRow> result,
        DownloadClientTelemetrySnapshot? snapshot,
        string fallbackName)
    {
        if (snapshot is null)
            return;

        result.Add(
            new MediaLifecycleSourceRow(
                string.IsNullOrWhiteSpace(
                    snapshot.DisplayName)
                    ? fallbackName
                    : snapshot.DisplayName,
                snapshot.State,
                $"{snapshot.ActiveCount} active · " +
                $"{snapshot.DownloadSpeed} down · " +
                $"{snapshot.FailedRecentCount} failed recent"));
    }

    private static IReadOnlyList<TransferCandidate>
        BuildTransferCandidates(
            DownloadClientTelemetrySnapshot? qbittorrent,
            DownloadClientTelemetrySnapshot? sabnzbd)
    {
        var result =
            new List<TransferCandidate>();

        AddTransferCandidates(
            result,
            qbittorrent,
            "qBittorrent");

        AddTransferCandidates(
            result,
            sabnzbd,
            "SABnzbd");

        return result;
    }

    private static void AddTransferCandidates(
        ICollection<TransferCandidate> result,
        DownloadClientTelemetrySnapshot? snapshot,
        string fallbackName)
    {
        if (snapshot is null)
            return;

        var client =
            string.IsNullOrWhiteSpace(
                snapshot.DisplayName)
                ? fallbackName
                : snapshot.DisplayName;

        foreach (var queue in
                 snapshot.Queue ??
                 new List<DownloadQueueTelemetry>())
        {
            result.Add(
                new TransferCandidate(
                    result.Count,
                    client,
                    queue.Name,
                    queue.Category,
                    queue.State,
                    queue.Progress,
                    queue.Remaining,
                    queue.Eta,
                    IsQueue: true));
        }

        foreach (var history in
                 (snapshot.History ??
                  new List<DownloadHistoryTelemetry>())
                 .Take(20))
        {
            result.Add(
                new TransferCandidate(
                    result.Count,
                    client,
                    history.Name,
                    history.Category,
                    history.State,
                    "--",
                    "--",
                    history.Completed,
                    IsQueue: false));
        }
    }

    private static MediaLifecycleItemRow BuildArrRow(
        ArrWorkItemRow work,
        TransferCandidate? transfer,
        double? transferScore,
        PlexSessionTelemetry? plexSession,
        double? plexScore,
        PlexTelemetrySnapshot? plex)
    {
        var stage =
            plexSession is not null
                ? "Playing"
                : transfer is not null
                    ? StageFromTransfer(
                        transfer)
                    : StageFromArr(
                        work);

        var confidence =
            plexSession is not null
                ? ConfidenceLabel(
                    plexScore,
                    "Plex title")
                : transfer is not null
                    ? ConfidenceLabel(
                        transferScore,
                        "Transfer title")
                    : "Arr evidence only";

        var evidence =
            new List<string>
            {
                $"{work.Service} {work.Type.ToLowerInvariant()} queue"
            };

        if (transfer is not null)
        {
            evidence.Add(
                $"{transfer.Client} title match " +
                $"{Math.Round(transferScore ?? 0d):0}%");
        }

        if (plexSession is not null)
        {
            evidence.Add(
                "active Plex session title match " +
                $"{Math.Round(plexScore ?? 0d):0}%");
        }
        else
        {
            evidence.Add(
                PlexServerEvidence(
                    plex));
        }

        return new MediaLifecycleItemRow(
            work.ItemIssue,
            work.Type,
            work.Service,
            stage,
            transfer?.Client ??
            "--",
            FirstText(
                transfer?.Progress,
                work.Progress,
                "--"),
            FirstText(
                transfer?.Remaining,
                work.Remaining,
                "--"),
            PlexItemEvidence(
                plexSession,
                plex),
            confidence,
            string.Join(
                " · ",
                evidence));
    }

    private static MediaLifecycleItemRow
        BuildTransferOnlyRow(
            TransferCandidate transfer,
            PlexSessionTelemetry? plexSession,
            double? plexScore,
            PlexTelemetrySnapshot? plex)
    {
        var stage =
            plexSession is not null
                ? "Playing"
                : StageFromTransfer(
                    transfer);

        return new MediaLifecycleItemRow(
            transfer.Title,
            "Download",
            string.IsNullOrWhiteSpace(
                transfer.Category)
                ? transfer.Client
                : transfer.Category,
            stage,
            transfer.Client,
            FirstText(
                transfer.Progress,
                "--"),
            FirstText(
                transfer.Remaining,
                "--"),
            PlexItemEvidence(
                plexSession,
                plex),
            plexSession is not null
                ? ConfidenceLabel(
                    plexScore,
                    "Plex title")
                : "Download evidence only",
            plexSession is not null
                ? $"{transfer.Client} transfer · active Plex session title match " +
                  $"{Math.Round(plexScore ?? 0d):0}%"
                : $"{transfer.Client} transfer · {PlexServerEvidence(plex)}");
    }

    private static string StageFromArr(
        ArrWorkItemRow work)
    {
        var state =
            work.State ??
            string.Empty;

        var detail =
            work.Detail ??
            string.Empty;

        var combined =
            state +
            " " +
            detail;

        if (ContainsAny(
                combined,
                "fail",
                "error",
                "warning",
                "missing",
                "blocked"))
        {
            return "Attention";
        }

        if (ContainsAny(
                combined,
                "import",
                "processing",
                "moving"))
        {
            return "Awaiting import";
        }

        if (ContainsAny(
                combined,
                "download",
                "tracked",
                "queued"))
        {
            return "Acquisition";
        }

        return "Acquisition";
    }

    private static string StageFromTransfer(
        TransferCandidate transfer)
    {
        var state =
            transfer.State ??
            string.Empty;

        if (ContainsAny(
                state,
                "fail",
                "error",
                "missing",
                "stalled"))
        {
            return "Attention";
        }

        if (ContainsAny(
                state,
                "repair",
                "verify",
                "extract",
                "unpack",
                "move",
                "process"))
        {
            return "Processing";
        }

        if (ContainsAny(
                state,
                "pause"))
        {
            return "Paused";
        }

        if (!transfer.IsQueue ||
            ContainsAny(
                state,
                "complete",
                "finished"))
        {
            return "Completed";
        }

        return "Downloading";
    }

    private static TransferMatch? BestTransferMatch(
        string title,
        IReadOnlyList<TransferCandidate> candidates,
        ISet<int> used)
    {
        TransferMatch? best =
            null;

        foreach (var candidate in candidates)
        {
            if (used.Contains(
                    candidate.Index))
            {
                continue;
            }

            var score =
                TitleScore(
                    title,
                    candidate.Title);

            if (score <
                72d)
            {
                continue;
            }

            if (best is null ||
                score >
                best.Score ||
                Math.Abs(
                    score -
                    best.Score) <
                0.001d &&
                candidate.IsQueue &&
                !best.Candidate.IsQueue)
            {
                best =
                    new TransferMatch(
                        candidate.Index,
                        candidate,
                        score);
            }
        }

        return best;
    }

    private static PlexMatch? BestPlexMatch(
        string title,
        IReadOnlyList<PlexSessionTelemetry> sessions,
        ISet<int> used)
    {
        PlexMatch? best =
            null;

        for (var index = 0;
             index < sessions.Count;
             index++)
        {
            if (used.Contains(
                    index))
            {
                continue;
            }

            var score =
                TitleScore(
                    title,
                    sessions[index].Title);

            if (score <
                78d)
            {
                continue;
            }

            if (best is null ||
                score >
                best.Score)
            {
                best =
                    new PlexMatch(
                        index,
                        sessions[index],
                        score);
            }
        }

        return best;
    }

    public static double TitleScore(
        string? left,
        string? right)
    {
        var normalizedLeft =
            NormalizeTitle(
                left);

        var normalizedRight =
            NormalizeTitle(
                right);

        if (normalizedLeft.Length <
                3 ||
            normalizedRight.Length <
                3)
        {
            return 0d;
        }

        if (normalizedLeft.Equals(
                normalizedRight,
                StringComparison.Ordinal))
        {
            return 100d;
        }

        var leftYears =
            ReleaseYears(
                normalizedLeft);

        var rightYears =
            ReleaseYears(
                normalizedRight);

        if (leftYears.Count >
                0 &&
            rightYears.Count >
                0 &&
            !leftYears.Overlaps(
                rightYears))
        {
            return 0d;
        }

        var leftTokens =
            SignificantTokens(
                normalizedLeft);

        var rightTokens =
            SignificantTokens(
                normalizedRight);

        if (leftTokens.Count ==
                0 ||
            rightTokens.Count ==
                0)
        {
            return 0d;
        }

        if (leftTokens.Count ==
            1)
        {
            var token =
                leftTokens[0];

            if (token.Length <
                5)
            {
                return 0d;
            }

            return rightTokens.Contains(
                    token,
                    StringComparer.Ordinal)
                ? normalizedRight.StartsWith(
                    token,
                    StringComparison.Ordinal)
                    ? 88d
                    : 72d
                : 0d;
        }

        var overlap =
            leftTokens.Count(token =>
                rightTokens.Contains(
                    token,
                    StringComparer.Ordinal));

        if (overlap <
            2)
        {
            return 0d;
        }

        var coverage =
            (double)overlap /
            leftTokens.Count;

        var precision =
            (double)overlap /
            Math.Max(
                leftTokens.Count,
                rightTokens.Count);

        var containmentBonus =
            normalizedRight.Contains(
                normalizedLeft,
                StringComparison.Ordinal)
                ? 12d
                : 0d;

        return Math.Clamp(
            coverage *
            75d +
            precision *
            20d +
            containmentBonus,
            0d,
            100d);
    }

    public static string NormalizeTitle(
        string? value)
    {
        if (string.IsNullOrWhiteSpace(
                value))
        {
            return string.Empty;
        }

        var normalized =
            value.Normalize(
                NormalizationForm.FormKD);

        var builder =
            new StringBuilder(
                normalized.Length);

        foreach (var character in normalized)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(
                    character) ==
                UnicodeCategory.NonSpacingMark)
            {
                continue;
            }

            builder.Append(
                char.ToLowerInvariant(
                    character));
        }

        return SeparatorPattern
            .Replace(
                builder.ToString(),
                " ")
            .Trim();
    }

    private static HashSet<string> ReleaseYears(
        string normalized) =>
        ReleaseYearPattern
            .Matches(
                normalized)
            .Select(match =>
                match.Value)
            .ToHashSet(
                StringComparer.Ordinal);

    private static IReadOnlyList<string>
        SignificantTokens(
            string normalized) =>
        normalized
            .Split(
                ' ',
                StringSplitOptions.RemoveEmptyEntries |
                StringSplitOptions.TrimEntries)
            .Where(token =>
                token.Length >=
                2)
            .Where(token =>
                !NoiseTokens.Contains(
                    token))
            .Where(token =>
                !EpisodePattern.IsMatch(
                    token))
            .Where(token =>
                !int.TryParse(
                    token,
                    NumberStyles.Integer,
                    CultureInfo.InvariantCulture,
                    out var number) ||
                number is <
                    1900 or >
                    2100)
            .Distinct(
                StringComparer.Ordinal)
            .ToArray();

    private static string PlexItemEvidence(
        PlexSessionTelemetry? session,
        PlexTelemetrySnapshot? plex)
    {
        if (session is not null)
            return "Playing now";

        if (plex is null)
            return "No Plex sample";

        if (!IsOnline(
                plex.State))
        {
            return "Plex unavailable";
        }

        return plex.LibraryCount >
            0
            ? $"Server ready · {plex.LibraryCount} libraries · item unverified"
            : "Server online · no library count";
    }

    private static string PlexServerEvidence(
        PlexTelemetrySnapshot? plex)
    {
        if (plex is null)
            return "no Plex sample";

        if (!IsOnline(
                plex.State))
        {
            return "Plex unavailable";
        }

        return plex.LibraryCount >
            0
            ? $"Plex ready with {plex.LibraryCount} libraries; item not individually verified"
            : "Plex online; item not individually verified";
    }

    private static string ConfidenceLabel(
        double? score,
        string source)
    {
        if (!score.HasValue)
            return source;

        return score.Value >=
            90d
            ? $"High · {source}"
            : score.Value >=
                78d
                ? $"Medium · {source}"
                : $"Conservative · {source}";
    }

    private static bool ContainsAny(
        string value,
        params string[] terms) =>
        terms.Any(term =>
            value.Contains(
                term,
                StringComparison.OrdinalIgnoreCase));

    private static bool IsOnline(
        string? state) =>
        state?.Contains(
            "online",
            StringComparison.OrdinalIgnoreCase) ==
        true ||
        state?.Contains(
            "ready",
            StringComparison.OrdinalIgnoreCase) ==
        true ||
        state?.Contains(
            "active",
            StringComparison.OrdinalIgnoreCase) ==
        true ||
        state?.Contains(
            "paused",
            StringComparison.OrdinalIgnoreCase) ==
        true;

    private static DateTimeOffset LatestCapture(
        IReadOnlyList<ArrLiveTelemetrySnapshot> arr,
        DownloadClientTelemetrySnapshot? qbittorrent,
        DownloadClientTelemetrySnapshot? sabnzbd,
        PlexTelemetrySnapshot? plex)
    {
        var times =
            new List<DateTimeOffset>();

        times.AddRange(
            arr.Select(snapshot =>
                snapshot.CapturedAt));

        if (qbittorrent is not null)
            times.Add(qbittorrent.SampledAt);

        if (sabnzbd is not null)
            times.Add(sabnzbd.SampledAt);

        if (plex is not null)
            times.Add(plex.SampledAt);

        return times.Count >
            0
            ? times.Max()
            : DateTimeOffset.UtcNow;
    }

    private static string SourceSummary(
        IReadOnlyList<MediaLifecycleSourceRow> sources)
    {
        var online =
            sources.Count(source =>
                IsOnline(
                    source.State));

        var attention =
            sources.Count -
            online;

        return $"{online} sources online · " +
               $"{attention} need configuration or attention";
    }

    private static int SourceOrder(
        string source) =>
        source.ToLowerInvariant() switch
        {
            "sonarr" => 0,
            "radarr" => 1,
            "lidarr" => 2,
            "qbittorrent" => 3,
            "sabnzbd" => 4,
            "plex" => 5,
            _ => 9
        };

    private static string FirstText(
        params string?[] values) =>
        values.FirstOrDefault(value =>
            !string.IsNullOrWhiteSpace(
                value) &&
            !value.Equals(
                "--",
                StringComparison.Ordinal)) ??
        "--";

    private sealed record TransferCandidate(
        int Index,
        string Client,
        string Title,
        string Category,
        string State,
        string Progress,
        string Remaining,
        string Eta,
        bool IsQueue);

    private sealed record TransferMatch(
        int Index,
        TransferCandidate Candidate,
        double Score);

    private sealed record PlexMatch(
        int Index,
        PlexSessionTelemetry Session,
        double Score);
}
