using System.Text;
using System.Text.Json;
using GraveOps.Core.Hosts;

namespace GraveOps.Desktop.Linux;

public sealed class FleetHostInsightRow
{
    public string TargetId { get; init; } = string.Empty;
    public string HostName { get; init; } = string.Empty;
    public string Role { get; init; } = string.Empty;
    public string Connection { get; init; } = string.Empty;
    public string State { get; init; } = "NOT CAPTURED";
    public OpsSeverity Severity { get; init; } = OpsSeverity.Info;
    public int Applications { get; init; }
    public int Attention { get; init; }
    public int? QueueCount { get; init; }
    public string Docker { get; init; } = "--";
    public DateTimeOffset? CapturedAt { get; init; }
    public bool IsActive { get; init; }

    public string SeverityLabel =>
        LinuxOpsAnalyzer.SeverityLabel(Severity);

    public string ActiveLabel =>
        IsActive ? "ACTIVE" : string.Empty;

    public string ApplicationSummary =>
        $"{Applications} app{(Applications == 1 ? string.Empty : "s")}";

    public string AttentionSummary =>
        Attention == 0
            ? "No active attention"
            : $"{Attention} need attention";

    public string QueueSummary =>
        QueueCount.HasValue
            ? QueueCount.Value.ToString()
            : "--";

    public string CapturedText =>
        CapturedAt.HasValue
            ? $"Captured {CapturedAt.Value.ToLocalTime():g}"
            : "No successful capture stored";
}

public sealed class FleetApplicationInsightRow
{
    public string TargetId { get; init; } = string.Empty;
    public string HostName { get; init; } = string.Empty;
    public string Application { get; init; } = string.Empty;
    public string Runtime { get; init; } = string.Empty;
    public string State { get; init; } = string.Empty;
    public OpsSeverity Severity { get; init; } = OpsSeverity.Info;
    public string NavigationName { get; init; } = "MediaHubNav";

    public string SeverityLabel =>
        LinuxOpsAnalyzer.SeverityLabel(Severity);
}

public sealed class FleetAttentionInsightRow
{
    public string TargetId { get; init; } = string.Empty;
    public string HostName { get; init; } = string.Empty;
    public string Component { get; init; } = string.Empty;
    public string State { get; init; } = string.Empty;
    public string Issue { get; init; } = string.Empty;
    public string Evidence { get; init; } = string.Empty;
    public string Impact { get; init; } = string.Empty;
    public string NextStep { get; init; } = string.Empty;
    public OpsSeverity Severity { get; init; } = OpsSeverity.Info;
    public string NavigationName { get; init; } = "IntelligenceNav";

    public string SeverityLabel =>
        LinuxOpsAnalyzer.SeverityLabel(Severity);
}

public sealed class InsightDependencyRow
{
    public int Order { get; init; }
    public string Component { get; init; } = string.Empty;
    public string State { get; init; } = string.Empty;
    public OpsSeverity Severity { get; init; } = OpsSeverity.Info;
    public string Evidence { get; init; } = string.Empty;
    public string Impact { get; init; } = string.Empty;
    public string NextStep { get; init; } = string.Empty;
    public string NavigationName { get; init; } = "IntelligenceNav";

    public string SeverityLabel =>
        LinuxOpsAnalyzer.SeverityLabel(Severity);
}

public sealed class InsightRemediationRow
{
    public int Step { get; init; }
    public OpsSeverity Severity { get; init; } = OpsSeverity.Info;
    public string Component { get; init; } = string.Empty;
    public string Why { get; init; } = string.Empty;
    public string NextStep { get; init; } = string.Empty;
    public string NavigationName { get; init; } = "IntelligenceNav";

    public string SeverityLabel =>
        LinuxOpsAnalyzer.SeverityLabel(Severity);
}

public sealed class InsightLifecycleItemRow
{
    public string Item { get; init; } = string.Empty;
    public string Owner { get; init; } = string.Empty;
    public string Stage { get; init; } = string.Empty;
    public string State { get; init; } = string.Empty;
    public string Progress { get; init; } = string.Empty;
    public string Remaining { get; init; } = string.Empty;
    public string Detail { get; init; } = string.Empty;
    public OpsSeverity Severity { get; init; } = OpsSeverity.Info;
    public string NavigationName { get; init; } = "LifecycleNav";

    public string SeverityLabel =>
        LinuxOpsAnalyzer.SeverityLabel(Severity);
}

public sealed class InsightHistoryRow
{
    public DateTimeOffset Timestamp { get; init; }
    public string Stream { get; init; } = string.Empty;
    public string Target { get; init; } = string.Empty;
    public string Component { get; init; } = string.Empty;
    public string Transition { get; init; } = string.Empty;
    public string Detail { get; init; } = string.Empty;
    public OpsSeverity Severity { get; init; } = OpsSeverity.Info;
    public string NavigationName { get; init; } = "HistoryNav";

    public string DisplayTime =>
        Timestamp.ToLocalTime().ToString("g");

    public string SeverityLabel =>
        LinuxOpsAnalyzer.SeverityLabel(Severity);
}

public sealed class LinuxInsightCapture
{
    public string TargetId { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string HostName { get; set; } = string.Empty;
    public string Role { get; set; } = string.Empty;
    public string Connection { get; set; } = string.Empty;
    public DateTimeOffset CapturedAt { get; set; }
    public string State { get; set; } = "UNKNOWN";
    public OpsSeverity Severity { get; set; } = OpsSeverity.Info;
    public string Docker { get; set; } = "--";
    public int? QueueCount { get; set; }
    public List<LinuxInsightApplication> Applications { get; set; } = new();
    public List<LinuxInsightFinding> Findings { get; set; } = new();
    public List<LinuxInsightLifecycle> Lifecycle { get; set; } = new();
}

public sealed class LinuxInsightApplication
{
    public string Name { get; set; } = string.Empty;
    public string Runtime { get; set; } = string.Empty;
    public string State { get; set; } = string.Empty;
    public OpsSeverity Severity { get; set; } = OpsSeverity.Info;
}

public sealed class LinuxInsightFinding
{
    public string Component { get; set; } = string.Empty;
    public string State { get; set; } = string.Empty;
    public string Issue { get; set; } = string.Empty;
    public string Evidence { get; set; } = string.Empty;
    public string Impact { get; set; } = string.Empty;
    public string NextStep { get; set; } = string.Empty;
    public OpsSeverity Severity { get; set; } = OpsSeverity.Info;
    public int Rank { get; set; }
}

public sealed class LinuxInsightLifecycle
{
    public int Order { get; set; }
    public string Stage { get; set; } = string.Empty;
    public string State { get; set; } = string.Empty;
    public OpsSeverity Severity { get; set; } = OpsSeverity.Info;
    public string Evidence { get; set; } = string.Empty;
    public string Impact { get; set; } = string.Empty;
    public string NextStep { get; set; } = string.Empty;
}

public sealed class LinuxInsightStore
{
    private readonly string _filePath;
    private readonly JsonSerializerOptions _json = new()
    {
        WriteIndented = true
    };
    private InsightDocument _document;

    public LinuxInsightStore()
    {
        var root =
            Environment.GetEnvironmentVariable("XDG_DATA_HOME");

        if (string.IsNullOrWhiteSpace(root))
        {
            root = Path.Combine(
                Environment.GetFolderPath(
                    Environment.SpecialFolder.UserProfile),
                ".local",
                "share");
        }

        var directory = Path.Combine(root, "GraveOps");
        Directory.CreateDirectory(directory);

        _filePath = Path.Combine(
            directory,
            "fleet-insight-cache.json");

        _document = Load();
    }

    public string FilePath => _filePath;

    public void RecordCapture(
        LinuxHostProfile profile,
        HostSnapshot snapshot,
        OpsAnalysis analysis,
        IReadOnlyList<OpsLifecycleStage> lifecycle,
        IReadOnlyList<OpsIntegration> integrations,
        OpsBackupSnapshot backup,
        int? queueCount)
    {
        var findings = analysis.Findings
            .OrderByDescending(item => item.Severity)
            .ThenBy(item => item.Rank)
            .Select(item => new LinuxInsightFinding
            {
                Component = item.Component,
                State =
                    LinuxOpsAnalyzer.SeverityLabel(item.Severity),
                Issue = item.Problem,
                Evidence = item.Evidence,
                Impact = item.Impact,
                NextStep = item.NextStep,
                Severity = item.Severity,
                Rank = item.Rank
            })
            .ToList();

        if (backup.Severity >= OpsSeverity.Warning &&
            findings.All(item =>
                !item.Component.Equals(
                    "Backups",
                    StringComparison.OrdinalIgnoreCase)))
        {
            findings.Add(new LinuxInsightFinding
            {
                Component = "Backups",
                State =
                    LinuxOpsAnalyzer.SeverityLabel(backup.Severity),
                Issue = backup.Summary,
                Evidence =
                    string.Join(" · ", backup.Evidence.Take(3)),
                Impact =
                    "Restore readiness may be reduced until verified backup evidence is available.",
                NextStep =
                    "Inspect backup schedules and recent artifacts before relying on recovery.",
                Severity = backup.Severity,
                Rank = 90
            });
        }

        _document.Captures[profile.Id] =
            new LinuxInsightCapture
            {
                TargetId = profile.Id,
                DisplayName = profile.DisplayName,
                HostName = snapshot.Hostname,
                Role = profile.Role,
                Connection = profile.ConnectionSummary,
                CapturedAt = snapshot.CapturedAt,
                State = analysis.Label,
                Severity = analysis.Severity,
                Docker = snapshot.DockerState,
                QueueCount = queueCount,
                Applications = integrations
                    .GroupBy(
                        item => item.Name,
                        StringComparer.OrdinalIgnoreCase)
                    .Select(group => group
                        .OrderBy(item => item.Severity)
                        .First())
                    .OrderBy(item => item.Name)
                    .Select(item => new LinuxInsightApplication
                    {
                        Name = item.Name,
                        Runtime = item.Kind,
                        State = item.State,
                        Severity = item.Severity
                    })
                    .ToList(),
                Findings = findings,
                Lifecycle = lifecycle
                    .OrderBy(item => item.Order)
                    .Select(item => new LinuxInsightLifecycle
                    {
                        Order = item.Order,
                        Stage = item.Stage,
                        State = item.State,
                        Severity = item.Severity,
                        Evidence = item.Evidence,
                        Impact = item.Impact,
                        NextStep = item.NextStep
                    })
                    .ToList()
            };

        Persist();
    }

    public IReadOnlyList<FleetHostInsightRow> BuildFleetHosts(
        IReadOnlyList<LinuxHostProfile> profiles,
        string activeTargetId)
    {
        return profiles
            .Select(profile =>
            {
                _document.Captures.TryGetValue(
                    profile.Id,
                    out var capture);

                return new FleetHostInsightRow
                {
                    TargetId = profile.Id,
                    HostName =
                        capture?.HostName ??
                        profile.DisplayName,
                    Role = profile.Role,
                    Connection = profile.ConnectionSummary,
                    State =
                        capture?.State ??
                        "NOT CAPTURED",
                    Severity =
                        capture?.Severity ??
                        OpsSeverity.Info,
                    Applications =
                        capture?.Applications.Count ?? 0,
                    Attention =
                        capture?.Findings.Count(item =>
                            item.Severity >=
                            OpsSeverity.Warning) ?? 0,
                    QueueCount =
                        capture?.QueueCount,
                    Docker =
                        capture?.Docker ?? "--",
                    CapturedAt =
                        capture?.CapturedAt ??
                        profile.LastDetectedAt,
                    IsActive =
                        profile.Id.Equals(
                            activeTargetId,
                            StringComparison.OrdinalIgnoreCase)
                };
            })
            .OrderByDescending(item => item.IsActive)
            .ThenByDescending(item => item.Severity)
            .ThenBy(item => item.HostName)
            .ToArray();
    }

    public IReadOnlyList<FleetApplicationInsightRow>
        BuildApplications(
            IReadOnlyList<LinuxHostProfile> profiles)
    {
        var profileMap = profiles.ToDictionary(
            item => item.Id,
            item => item,
            StringComparer.OrdinalIgnoreCase);

        return _document.Captures.Values
            .SelectMany(capture =>
                capture.Applications.Select(application =>
                    new FleetApplicationInsightRow
                    {
                        TargetId = capture.TargetId,
                        HostName =
                            profileMap.TryGetValue(
                                capture.TargetId,
                                out var profile)
                                ? profile.DisplayName
                                : capture.DisplayName,
                        Application = application.Name,
                        Runtime = application.Runtime,
                        State = application.State,
                        Severity = application.Severity,
                        NavigationName =
                            NavigationForApplication(
                                application.Name)
                    }))
            .OrderBy(item => item.HostName)
            .ThenBy(item => item.Application)
            .ToArray();
    }

    public IReadOnlyList<FleetAttentionInsightRow>
        BuildAttention(
            IReadOnlyList<LinuxHostProfile> profiles)
    {
        var profileMap = profiles.ToDictionary(
            item => item.Id,
            item => item,
            StringComparer.OrdinalIgnoreCase);

        return _document.Captures.Values
            .SelectMany(capture =>
                capture.Findings
                    .Where(item =>
                        item.Severity >=
                        OpsSeverity.Warning)
                    .Select(finding =>
                        new FleetAttentionInsightRow
                        {
                            TargetId = capture.TargetId,
                            HostName =
                                profileMap.TryGetValue(
                                    capture.TargetId,
                                    out var profile)
                                    ? profile.DisplayName
                                    : capture.DisplayName,
                            Component = finding.Component,
                            State = finding.State,
                            Issue = finding.Issue,
                            Evidence = finding.Evidence,
                            Impact = finding.Impact,
                            NextStep = finding.NextStep,
                            Severity = finding.Severity,
                            NavigationName =
                                NavigationForComponent(
                                    finding.Component)
                        }))
            .OrderByDescending(item => item.Severity)
            .ThenBy(item => item.HostName)
            .ThenBy(item => item.Component)
            .ToArray();
    }

    public IReadOnlyList<InsightDependencyRow>
        BuildDependencies(
            IReadOnlyList<OpsLifecycleStage> lifecycle)
    {
        return lifecycle
            .OrderBy(item => item.Order)
            .Select(item => new InsightDependencyRow
            {
                Order = item.Order,
                Component = item.Stage,
                State = item.State,
                Severity = item.Severity,
                Evidence = item.Evidence,
                Impact = item.Impact,
                NextStep = item.NextStep,
                NavigationName =
                    NavigationForComponent(item.Stage)
            })
            .ToArray();
    }

    public IReadOnlyList<InsightRemediationRow>
        BuildRemediation(
            IReadOnlyList<OpsFinding> findings)
    {
        var actionable = findings
            .Where(item =>
                item.Severity >= OpsSeverity.Warning)
            .OrderByDescending(item => item.Severity)
            .ThenBy(item => item.Rank)
            .ToArray();

        return actionable
            .Select((finding, index) =>
                new InsightRemediationRow
                {
                    Step = index + 1,
                    Severity = finding.Severity,
                    Component = finding.Component,
                    Why =
                        $"{finding.Problem} {finding.Impact}".Trim(),
                    NextStep = finding.NextStep,
                    NavigationName =
                        NavigationForComponent(
                            finding.Component)
                })
            .ToArray();
    }

    public IReadOnlyList<InsightLifecycleItemRow>
        BuildLifecycleItems(
            IReadOnlyList<OpsLifecycleStage> lifecycle,
            IReadOnlyList<ArrWorkItemRow>? liveWork)
    {
        var rows =
            new List<InsightLifecycleItemRow>();

        if (liveWork is not null)
        {
            foreach (var work in liveWork)
            {
                var stage = LifecycleStageForWork(work);
                var severity = SeverityFromWork(work);

                rows.Add(new InsightLifecycleItemRow
                {
                    Item = work.ItemIssue,
                    Owner = work.Service,
                    Stage = stage,
                    State = work.State,
                    Progress = work.Progress,
                    Remaining = work.Remaining,
                    Detail = work.Detail,
                    Severity = severity,
                    NavigationName =
                        NavigationForApplication(work.Service)
                });
            }
        }

        foreach (var stage in lifecycle)
        {
            if (rows.Any(item =>
                    item.Stage.Equals(
                        stage.Stage,
                        StringComparison.OrdinalIgnoreCase)) &&
                stage.Severity < OpsSeverity.Warning)
            {
                continue;
            }

            rows.Add(new InsightLifecycleItemRow
            {
                Item = $"{stage.Stage} readiness",
                Owner = stage.Stage,
                Stage = stage.Stage,
                State = stage.State,
                Progress = string.Empty,
                Remaining = string.Empty,
                Detail =
                    string.Join(
                        " · ",
                        new[]
                        {
                            stage.Evidence,
                            stage.Impact,
                            stage.NextStep
                        }.Where(value =>
                            !string.IsNullOrWhiteSpace(value))),
                Severity = stage.Severity,
                NavigationName =
                    NavigationForComponent(stage.Stage)
            });
        }

        return rows
            .OrderByDescending(item => item.Severity)
            .ThenBy(item =>
                LifecycleOrder(item.Stage))
            .ThenBy(item => item.Owner)
            .ThenBy(item => item.Item)
            .ToArray();
    }

    public IReadOnlyList<InsightHistoryRow>
        BuildHistory(
            IReadOnlyList<OpsHistoryRecord> transitions,
            IReadOnlyList<ControlPlaneActivityRow> activities,
            string fallbackTarget)
    {
        var health = transitions.Select(item =>
            new InsightHistoryRow
            {
                Timestamp = item.Timestamp,
                Stream = "Health transition",
                Target = fallbackTarget,
                Component = item.Component,
                Transition =
                    string.IsNullOrWhiteSpace(item.FromState)
                        ? item.ToState
                        : $"{item.FromState} → {item.ToState}",
                Detail = item.Detail,
                Severity = item.Severity,
                NavigationName =
                    NavigationForComponent(item.Component)
            });

        var operatorRows = activities.Select(item =>
            new InsightHistoryRow
            {
                Timestamp = item.Timestamp,
                Stream =
                    item.Kind.Equals(
                        "Notification",
                        StringComparison.OrdinalIgnoreCase)
                        ? "Notification"
                        : "GraveOps activity",
                Target = item.Target,
                Component = item.Kind,
                Transition = item.Title,
                Detail = item.Detail,
                Severity =
                    ActivitySeverity(item),
                NavigationName =
                    string.IsNullOrWhiteSpace(
                        item.NavigationName)
                        ? NavigationForComponent(item.Kind)
                        : item.NavigationName
            });

        return health
            .Concat(operatorRows)
            .OrderByDescending(item => item.Timestamp)
            .ToArray();
    }

    public string BuildIntelligenceReport(
        LinuxHostProfile profile,
        OpsAnalysis analysis,
        IReadOnlyList<InsightDependencyRow> dependencies,
        IReadOnlyList<InsightRemediationRow> remediation)
    {
        var builder = new StringBuilder();

        builder.AppendLine("GRAVEOPS INTELLIGENCE REPORT");
        builder.AppendLine($"Target: {profile.DisplayName}");
        builder.AppendLine($"Connection: {profile.ConnectionSummary}");
        builder.AppendLine($"State: {analysis.Label}");
        builder.AppendLine($"Root cause: {analysis.RootCause}");
        builder.AppendLine($"Headline: {analysis.Headline}");
        builder.AppendLine();

        builder.AppendLine("DEPENDENCY STATE");
        foreach (var dependency in dependencies)
        {
            builder.AppendLine(
                $"{dependency.Order}. {dependency.Component} · " +
                $"{dependency.State} · {dependency.Evidence}");
        }

        builder.AppendLine();
        builder.AppendLine("GUIDED REMEDIATION");

        if (remediation.Count == 0)
        {
            builder.AppendLine(
                "No active warning-or-higher remediation step.");
        }
        else
        {
            foreach (var step in remediation)
            {
                builder.AppendLine(
                    $"{step.Step}. [{step.SeverityLabel}] " +
                    $"{step.Component}");
                builder.AppendLine($"   Why: {step.Why}");
                builder.AppendLine($"   Next: {step.NextStep}");
            }
        }

        return builder.ToString().TrimEnd();
    }

    public string BuildIncidentReplay(
        InsightHistoryRow row)
    {
        return string.Join(
            Environment.NewLine,
            new[]
            {
                "GRAVEOPS INCIDENT REPLAY",
                $"Time: {row.DisplayTime}",
                $"Stream: {row.Stream}",
                $"Target: {row.Target}",
                $"Component: {row.Component}",
                $"Severity: {row.SeverityLabel}",
                $"Transition: {row.Transition}",
                $"Detail: {row.Detail}",
                $"Owning page: {row.NavigationName}"
            });
    }

    public static string NavigationForApplication(
        string application)
    {
        var normalized = application.Trim();

        return normalized.ToLowerInvariant() switch
        {
            "plex" => "PlexNav",
            "tautulli" => "TautulliNav",
            "kometa" => "KometaNav",
            "sonarr" => "SonarrNav",
            "radarr" => "RadarrNav",
            "lidarr" => "LidarrNav",
            "prowlarr" => "ProwlarrNav",
            "readarr" => "ReadarrNav",
            "whisparr" => "WhisparrNav",
            "mylar3" => "Mylar3Nav",
            "bazarr" => "BazarrNav",
            "sabnzbd" => "SabnzbdNav",
            "qbittorrent" => "QBittorrentNav",
            "decypharr" => "DecypharrNav",
            "recyclarr" => "RecyclarrNav",
            "zurg" => "ZurgNav",
            "dumb" => "DumbNav",
            _ => "MediaHubNav"
        };
    }

    public static string NavigationForComponent(
        string component)
    {
        var value = component.ToLowerInvariant();

        if (value.Contains("storage") ||
            value.Contains("mount"))
        {
            return "StorageNav";
        }

        if (value.Contains("docker") ||
            value.Contains("container"))
        {
            return "DockerNav";
        }

        if (value.Contains("backup"))
            return "BackupsNav";

        if (value.Contains("log") ||
            value.Contains("journal"))
        {
            return "LogsNav";
        }

        if (value.Contains("service") ||
            value.Contains("systemd") ||
            value.EndsWith(".service"))
        {
            return "ServicesNav";
        }

        if (value.Contains("download"))
            return "MediaHubNav";

        if (value.Contains("request") ||
            value.Contains("discovery") ||
            value.Contains("processing") ||
            value.Contains("library") ||
            value.Contains("arr"))
        {
            return "LifecycleNav";
        }

        return NavigationForApplication(component);
    }

    private InsightDocument Load()
    {
        try
        {
            if (!File.Exists(_filePath))
                return new InsightDocument();

            var document =
                JsonSerializer.Deserialize<InsightDocument>(
                    File.ReadAllText(_filePath),
                    _json) ??
                new InsightDocument();

            document.Captures ??=
                new Dictionary<string, LinuxInsightCapture>(
                    StringComparer.OrdinalIgnoreCase);

            if (document.Captures.Comparer !=
                StringComparer.OrdinalIgnoreCase)
            {
                document.Captures =
                    new Dictionary<string, LinuxInsightCapture>(
                        document.Captures,
                        StringComparer.OrdinalIgnoreCase);
            }

            return document;
        }
        catch
        {
            return new InsightDocument();
        }
    }

    private void Persist()
    {
        var temporary = _filePath + ".tmp";

        File.WriteAllText(
            temporary,
            JsonSerializer.Serialize(
                _document,
                _json),
            new UTF8Encoding(false));

        File.Move(
            temporary,
            _filePath,
            overwrite: true);
    }

    private static string LifecycleStageForWork(
        ArrWorkItemRow work)
    {
        if (work.Type.Equals(
                "Health",
                StringComparison.OrdinalIgnoreCase))
        {
            return "Arr";
        }

        if (work.Type.Equals(
                "Access",
                StringComparison.OrdinalIgnoreCase))
        {
            return "Discovery";
        }

        if (work.State.Contains(
                "import",
                StringComparison.OrdinalIgnoreCase) ||
            work.Detail.Contains(
                "import",
                StringComparison.OrdinalIgnoreCase))
        {
            return "Import";
        }

        return "Download";
    }

    private static OpsSeverity SeverityFromWork(
        ArrWorkItemRow work)
    {
        var text =
            $"{work.State} {work.Detail}".ToLowerInvariant();

        if (text.Contains("failed") ||
            text.Contains("error") ||
            text.Contains("unavailable") ||
            text.Contains("blocked"))
        {
            return OpsSeverity.Error;
        }

        if (text.Contains("warning") ||
            text.Contains("stalled") ||
            text.Contains("attention") ||
            text.Contains("pending"))
        {
            return OpsSeverity.Warning;
        }

        if (text.Contains("healthy") ||
            text.Contains("completed") ||
            text.Contains("online"))
        {
            return OpsSeverity.Healthy;
        }

        return OpsSeverity.Info;
    }

    private static int LifecycleOrder(string stage)
    {
        var value = stage.ToLowerInvariant();

        if (value.Contains("host"))
            return 0;
        if (value.Contains("storage"))
            return 1;
        if (value.Contains("request"))
            return 2;
        if (value.Contains("discovery"))
            return 3;
        if (value.Contains("arr"))
            return 4;
        if (value.Contains("download"))
            return 5;
        if (value.Contains("import"))
            return 6;
        if (value.Contains("processing"))
            return 7;
        if (value.Contains("library"))
            return 8;

        return 99;
    }

    private static OpsSeverity ActivitySeverity(
        ControlPlaneActivityRow row)
    {
        if (row.Kind.Equals(
                "Failure",
                StringComparison.OrdinalIgnoreCase))
        {
            return OpsSeverity.Error;
        }

        if (row.Kind.Equals(
                "Notification",
                StringComparison.OrdinalIgnoreCase))
        {
            return OpsSeverity.Warning;
        }

        return OpsSeverity.Info;
    }

    private sealed class InsightDocument
    {
        public Dictionary<string, LinuxInsightCapture>
            Captures { get; set; } =
            new(StringComparer.OrdinalIgnoreCase);
    }
}
