using GraveOps.Core.Telemetry;
using GraveOps.Desktop.Windows;
using GraveOps.Platform.Windows;

var tests =
    new (
        string Name,
        Action Run)[]
    {
        (
            "Lifecycle correlates Arr work with qBittorrent queue",
            CorrelatesArrWithQBittorrent),

        (
            "Lifecycle correlates Arr work with SABnzbd completion",
            CorrelatesArrWithSABnzbdHistory),

        (
            "Lifecycle promotes an active Plex title match",
            PromotesPlexSessionMatch),

        (
            "Lifecycle avoids unsafe short single-token matches",
            AvoidsShortSingleTokenOvermatch),

        (
            "Lifecycle rejects conflicting release years",
            RejectsConflictingReleaseYears),

        (
            "Lifecycle includes unmatched active downloads",
            IncludesUnmatchedActiveDownload),

        (
            "Lifecycle evidence excludes paths URLs and secrets",
            KeepsEvidencePrivacySafe),

        (
            "Lifecycle reports stale source failures without discarding cached data",
            ReportsStaleSourceFailure),

        (
            "Lifecycle target lease rejects stale completions",
            TargetLeaseRejectsStaleCompletion)
    };

foreach (var test in tests)
{
    try
    {
        test.Run();

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
    $"All {tests.Length} Windows Media Lifecycle tests passed.");

static void CorrelatesArrWithQBittorrent()
{
    var snapshot =
        MediaLifecycleCorrelator.Build(
            new[]
            {
                ArrSnapshot(
                    "Sonarr",
                    new ArrWorkItemRow(
                        "Sonarr",
                        "Episode",
                        "Jury Duty",
                        "Downloading",
                        "43%",
                        "12m",
                        string.Empty))
            },
            DownloadSnapshot(
                "qBittorrent",
                queue:
                    new[]
                    {
                        Queue(
                            "Jury.Duty.S01E07.Deliberations.2160p.AMZN.WEB-DL",
                            "tv",
                            "downloading",
                            "44.0%",
                            "2.1 GB")
                    }),
            sabnzbd:
                null,
            PlexOnline());

    var item =
        snapshot.Items.Single(row =>
            row.Title.Equals(
                "Jury Duty",
                StringComparison.Ordinal));

    Equal(
        "qBittorrent",
        item.DownloadClient,
        "matched download client");

    Equal(
        "Downloading",
        item.Stage,
        "matched lifecycle stage");

    Equal(
        "44.0%",
        item.Progress,
        "matched transfer progress");

    True(
        item.Confidence.StartsWith(
            "High",
            StringComparison.Ordinal),
        "high-confidence qBittorrent title match");

    Equal(
        1,
        snapshot.TransferCount,
        "transfer count");
}

static void CorrelatesArrWithSABnzbdHistory()
{
    var sab =
        DownloadSnapshot(
            "SABnzbd",
            history:
                new[]
                {
                    History(
                        "The.Matrix.1999.2160p.BluRay",
                        "movies",
                        "Completed")
                });

    var snapshot =
        MediaLifecycleCorrelator.Build(
            new[]
            {
                ArrSnapshot(
                    "Radarr",
                    new ArrWorkItemRow(
                        "Radarr",
                        "Movie",
                        "The Matrix",
                        "Downloaded",
                        "100%",
                        "0s",
                        string.Empty))
            },
            qbittorrent:
                null,
            sab,
            PlexOnline());

    var item =
        snapshot.Items.Single(row =>
            row.Title.Equals(
                "The Matrix",
                StringComparison.Ordinal));

    Equal(
        "SABnzbd",
        item.DownloadClient,
        "SABnzbd history correlation");

    Equal(
        "Completed",
        item.Stage,
        "completed lifecycle stage");

    Equal(
        1,
        snapshot.ProcessingCount,
        "processing/completed count");
}

static void PromotesPlexSessionMatch()
{
    var plex =
        PlexOnline(
            new PlexSessionTelemetry
            {
                Title =
                    "Jury Duty",

                State =
                    "playing",

                Progress =
                    "21%"
            });

    var snapshot =
        MediaLifecycleCorrelator.Build(
            new[]
            {
                ArrSnapshot(
                    "Sonarr",
                    new ArrWorkItemRow(
                        "Sonarr",
                        "Episode",
                        "Jury Duty",
                        "Downloaded",
                        "100%",
                        "0s",
                        string.Empty))
            },
            DownloadSnapshot(
                "qBittorrent",
                history:
                    new[]
                    {
                        History(
                            "Jury.Duty.S01E07.2160p.WEB-DL",
                            "tv",
                            "Completed")
                    }),
            sabnzbd:
                null,
            plex);

    var item =
        snapshot.Items.Single(row =>
            row.Title.Equals(
                "Jury Duty",
                StringComparison.Ordinal));

    Equal(
        "Playing",
        item.Stage,
        "Plex promotion");

    Equal(
        "Playing now",
        item.Plex,
        "Plex item evidence");

    Equal(
        1,
        snapshot.PlayingCount,
        "playing count");
}

static void AvoidsShortSingleTokenOvermatch()
{
    var snapshot =
        MediaLifecycleCorrelator.Build(
            new[]
            {
                ArrSnapshot(
                    "Lidarr",
                    new ArrWorkItemRow(
                        "Lidarr",
                        "Album",
                        "Rush",
                        "Queued",
                        string.Empty,
                        string.Empty,
                        string.Empty))
            },
            DownloadSnapshot(
                "SABnzbd",
                queue:
                    new[]
                    {
                        Queue(
                            "Rush-Hemispheres-LP-24BIT-FLAC-1978",
                            "music",
                            "Downloading",
                            "80%",
                            "300 MB")
                    }),
            sabnzbd:
                null,
            PlexOnline());

    var arrItem =
        snapshot.Items.Single(row =>
            row.Title.Equals(
                "Rush",
                StringComparison.Ordinal));

    Equal(
        "--",
        arrItem.DownloadClient,
        "short title remains unmatched");

    Equal(
        "Acquisition",
        arrItem.Stage,
        "Arr-only stage");

    True(
        snapshot.Items.Any(row =>
            row.Title.Contains(
                "Hemispheres",
                StringComparison.Ordinal)),
        "unmatched transfer remains visible");
}

static void RejectsConflictingReleaseYears()
{
    var snapshot =
        MediaLifecycleCorrelator.Build(
            new[]
            {
                ArrSnapshot(
                    "Radarr",
                    new ArrWorkItemRow(
                        "Radarr",
                        "Movie",
                        "The Thing (1982)",
                        "Downloading",
                        "10%",
                        "20m",
                        string.Empty))
            },
            DownloadSnapshot(
                "qBittorrent",
                queue:
                    new[]
                    {
                        Queue(
                            "The.Thing.2011.2160p.BluRay",
                            "movies",
                            "downloading",
                            "50%",
                            "8 GB")
                    }),
            sabnzbd:
                null,
            PlexOnline());

    var arrItem =
        snapshot.Items.Single(item =>
            item.Title.Equals(
                "The Thing (1982)",
                StringComparison.Ordinal));

    Equal(
        "--",
        arrItem.DownloadClient,
        "conflicting release years remain unmatched");

    Equal(
        0d,
        MediaLifecycleCorrelator.TitleScore(
            "The Thing (1982)",
            "The Thing 2011 2160p"),
        "conflicting release-year score");

    True(
        snapshot.Items.Any(item =>
            item.Title.Contains(
                "2011",
                StringComparison.Ordinal)),
        "conflicting transfer remains independently visible");
}

static void IncludesUnmatchedActiveDownload()
{
    var snapshot =
        MediaLifecycleCorrelator.Build(
            Array.Empty<ArrLiveTelemetrySnapshot>(),
            DownloadSnapshot(
                "qBittorrent",
                queue:
                    new[]
                    {
                        Queue(
                            "Standalone.Release.2026.1080p.WEB-DL",
                            "movies",
                            "downloading",
                            "12%",
                            "7.2 GB")
                    }),
            sabnzbd:
                null,
            plex:
                null);

    var item =
        snapshot.Items.Single();

    Equal(
        "Download",
        item.MediaType,
        "unmatched transfer type");

    Equal(
        "Downloading",
        item.Stage,
        "unmatched transfer stage");

    Equal(
        "qBittorrent",
        item.DownloadClient,
        "unmatched transfer client");
}

static void KeepsEvidencePrivacySafe()
{
    const string privatePath =
        @"D:\Private Downloads\Secret Release\episode.mkv";

    const string privateUrl =
        "https://tracker.example/private?passkey=secret";

    const string apiKey =
        "fixture-api-key-do-not-leak";

    var queue =
        Queue(
            "Private.Show.S01E01.2160p.WEB-DL",
            "tv",
            "downloading",
            "50%",
            "1 GB");

    queue.Detail =
        privatePath +
        " " +
        privateUrl +
        " " +
        apiKey;

    queue.Tracker =
        privateUrl;

    var snapshot =
        MediaLifecycleCorrelator.Build(
            new[]
            {
                ArrSnapshot(
                    "Sonarr",
                    new ArrWorkItemRow(
                        "Sonarr",
                        "Episode",
                        "Private Show",
                        "Downloading",
                        "50%",
                        "10m",
                        privatePath))
            },
            DownloadSnapshot(
                "qBittorrent",
                queue:
                    new[]
                    {
                        queue
                    }),
            sabnzbd:
                null,
            PlexOnline());

    var projected =
        string.Join(
            "|",
            snapshot.Items.Select(item =>
                string.Join(
                    "|",
                    item.Title,
                    item.Evidence,
                    item.Plex,
                    item.Confidence)));

    True(
        !projected.Contains(
            privatePath,
            StringComparison.Ordinal),
        "lifecycle evidence omits storage path");

    True(
        !projected.Contains(
            privateUrl,
            StringComparison.Ordinal),
        "lifecycle evidence omits URL");

    True(
        !projected.Contains(
            apiKey,
            StringComparison.Ordinal),
        "lifecycle evidence omits API key");
}

static void ReportsStaleSourceFailure()
{
    var snapshot =
        MediaLifecycleCorrelator.Build(
            new[]
            {
                ArrSnapshot(
                    "Sonarr",
                    new ArrWorkItemRow(
                        "Sonarr",
                        "Episode",
                        "Cached Show",
                        "Queued",
                        string.Empty,
                        string.Empty,
                        string.Empty))
            },
            qbittorrent:
                null,
            sabnzbd:
                null,
            PlexOnline(),
            new[]
            {
                new MediaLifecycleSourceRow(
                    "Sonarr",
                    "Stale",
                    "Last live snapshot retained while Sonarr retries."),

                new MediaLifecycleSourceRow(
                    "SABnzbd",
                    "Unavailable",
                    "SABnzbd telemetry is not configured.")
            });

    var sonarr =
        snapshot.Sources.Single(source =>
            source.Source.Equals(
                "Sonarr",
                StringComparison.Ordinal));

    Equal(
        "Stale",
        sonarr.State,
        "stale source overrides cached source state");

    Equal(
        "Attention",
        snapshot.OverallState,
        "source failure overall state");

    Equal(
        2,
        snapshot.AttentionCount,
        "source-failure attention count");
}

static void TargetLeaseRejectsStaleCompletion()
{
    var local =
        WindowsTargetCatalog.CreateLocal();

    True(
        WindowsMediaLifecycleTargetLease.IsCurrent(
            local.Id,
            local),
        "matching lifecycle target lease");

    True(
        !WindowsMediaLifecycleTargetLease.IsCurrent(
            "different-target",
            local),
        "stale lifecycle target lease");

    True(
        !WindowsMediaLifecycleTargetLease.IsCurrent(
            local.Id,
            currentTarget:
                null),
        "missing current lifecycle target");

    True(
        WindowsMediaLifecycleTargetLease.ShouldRefreshCurrent(
            "completed-old-target",
            local,
            pageVisible:
                true),
        "visible current target recovers after stale completion");

    True(
        !WindowsMediaLifecycleTargetLease.ShouldRefreshCurrent(
            local.Id,
            local,
            pageVisible:
                true),
        "matching target does not schedule duplicate recovery");

    True(
        !WindowsMediaLifecycleTargetLease.ShouldRefreshCurrent(
            "completed-old-target",
            local,
            pageVisible:
                false),
        "hidden Lifecycle page does not schedule recovery traffic");
}

static ArrLiveTelemetrySnapshot ArrSnapshot(
    string product,
    params ArrWorkItemRow[] work) =>
    new(
        DateTimeOffset.UtcNow,
        new[]
        {
            new ArrServiceTelemetryRow(
                product.ToLowerInvariant(),
                product,
                "http://configured.local/",
                "1.0.0",
                $"{work.Length} work",
                "0 issues",
                "protected",
                "Online")
        },
        work,
        "Online",
        "v1.0.0",
        $"{work.Length} work",
        "0 issues");

static DownloadClientTelemetrySnapshot DownloadSnapshot(
    string client,
    IEnumerable<DownloadQueueTelemetry>? queue = null,
    IEnumerable<DownloadHistoryTelemetry>? history = null) =>
    new()
    {
        ClientKey =
            client,

        DisplayName =
            client,

        State =
            "Online",

        SampledAt =
            DateTimeOffset.UtcNow,

        Queue =
            (queue ??
             Array.Empty<DownloadQueueTelemetry>())
            .ToList(),

        History =
            (history ??
             Array.Empty<DownloadHistoryTelemetry>())
            .ToList()
    };

static DownloadQueueTelemetry Queue(
    string name,
    string category,
    string state,
    string progress,
    string remaining) =>
    new()
    {
        Name =
            name,

        Category =
            category,

        State =
            state,

        Progress =
            progress,

        Remaining =
            remaining
    };

static DownloadHistoryTelemetry History(
    string name,
    string category,
    string state) =>
    new()
    {
        Name =
            name,

        Category =
            category,

        State =
            state,

        Completed =
            "2026-08-05 15:00"
    };

static PlexTelemetrySnapshot PlexOnline(
    params PlexSessionTelemetry[] sessions) =>
    new()
    {
        State =
            "Online",

        LibraryCount =
            6,

        ActiveSessions =
            sessions.Length,

        Sessions =
            sessions.ToList(),

        SampledAt =
            DateTimeOffset.UtcNow
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
