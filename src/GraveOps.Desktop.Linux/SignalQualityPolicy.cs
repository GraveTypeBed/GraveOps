using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace GraveOps.Desktop.Linux;

public enum SignalExpectationMode
{
    Auto,
    Expected,
    Optional,
    Ignored
}

public enum SignalQualityTransitionKind
{
    Opened,
    Recovered
}

public sealed class SignalQualitySettings
{
    public bool Enabled { get; set; } = true;
    public bool EvaluateExpectedServices { get; set; } = true;
    public int HostStaleMinutes { get; set; } = 5;
    public int ApplicationStaleMinutes { get; set; } = 10;
    public int BackupStaleMinutes { get; set; } = 15;
    public Dictionary<string, SignalExpectationMode> ServiceModes { get; set; } =
        new(StringComparer.OrdinalIgnoreCase);

    public SignalQualitySettings Clone() => new()
    {
        Enabled = Enabled,
        EvaluateExpectedServices = EvaluateExpectedServices,
        HostStaleMinutes = HostStaleMinutes,
        ApplicationStaleMinutes = ApplicationStaleMinutes,
        BackupStaleMinutes = BackupStaleMinutes,
        ServiceModes = new Dictionary<string, SignalExpectationMode>(
            ServiceModes ?? new Dictionary<string, SignalExpectationMode>(),
            StringComparer.OrdinalIgnoreCase)
    };

    public static SignalQualitySettings Defaults() => new();
}

public sealed record SignalQualityObservation(
    string Fingerprint,
    OpsSeverity Severity,
    string Component,
    string Resource,
    string Problem,
    string Evidence,
    string Impact,
    string NextStep,
    string NavigationName,
    int Rank)
{
    public OpsFinding ToFinding() => new(
        Severity,
        Component,
        Problem,
        $"{Evidence} · [signal:{Fingerprint}]",
        Impact,
        NextStep,
        Rank);
}

public sealed class SignalQualityIncident
{
    public string Fingerprint { get; set; } = string.Empty;
    public string HostId { get; set; } = string.Empty;
    public OpsSeverity Severity { get; set; } = OpsSeverity.Info;
    public string Component { get; set; } = string.Empty;
    public string Resource { get; set; } = string.Empty;
    public string Problem { get; set; } = string.Empty;
    public string Evidence { get; set; } = string.Empty;
    public string NavigationName { get; set; } = string.Empty;
    public DateTimeOffset FirstSeen { get; set; }
    public DateTimeOffset LastSeen { get; set; }
    public DateTimeOffset? RecoveredAt { get; set; }
    public int OccurrenceCount { get; set; }
    public bool IsActive { get; set; }
}

public sealed record SignalQualityTransition(
    SignalQualityTransitionKind Kind,
    SignalQualityIncident Incident);

public sealed record SignalQualityRefreshState(
    DateTimeOffset? LastSuccessAt,
    DateTimeOffset? LastFailureAt,
    string LastFailure);

public sealed record SignalQualityDashboardContext(
    string HostId,
    DateTimeOffset Now,
    SignalQualityRefreshState Refresh,
    SignalQualitySettings Settings,
    IReadOnlyList<OpsIntegration> Integrations);

public sealed class SignalQualityPolicyStore
{
    private readonly string _filePath;
    private readonly JsonSerializerOptions _json = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };
    private SignalQualityDocument _document;
    private DateTimeOffset _lastPersistedAt;

    public SignalQualityPolicyStore(string? configRoot = null)
    {
        var root = configRoot;
        if (string.IsNullOrWhiteSpace(root))
        {
            root = Environment.GetEnvironmentVariable("XDG_CONFIG_HOME");
        }
        if (string.IsNullOrWhiteSpace(root))
        {
            root = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                ".config");
        }

        _filePath = Path.Combine(root, "GraveOps", "signal-quality.json");
        _document = Normalize(Load());
        _lastPersistedAt = File.Exists(_filePath)
            ? File.GetLastWriteTimeUtc(_filePath)
            : DateTimeOffset.MinValue;
    }

    public string FilePath => _filePath;

    public SignalQualitySettings GetSettings() =>
        NormalizeSettings(_document.Settings).Clone();

    public void SetSettings(SignalQualitySettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        _document.Settings = NormalizeSettings(settings);
        Save();
    }

    public SignalQualityRefreshState GetRefreshState(string hostId)
    {
        var key = NormalizeHost(hostId);
        _document.RefreshStates.TryGetValue(key, out var state);
        return state is null
            ? new SignalQualityRefreshState(null, null, string.Empty)
            : new SignalQualityRefreshState(
                state.LastSuccessAt,
                state.LastFailureAt,
                state.LastFailure ?? string.Empty);
    }

    public void MarkRefreshSuccess(string hostId, DateTimeOffset timestamp)
    {
        var state = GetOrCreateRefreshState(hostId);
        var recoveredFromFailure =
            state.LastFailureAt is { } failure &&
            (state.LastSuccessAt is null || failure > state.LastSuccessAt);
        state.LastSuccessAt = timestamp;
        state.LastFailure = string.Empty;
        if (recoveredFromFailure ||
            timestamp < _lastPersistedAt ||
            timestamp - _lastPersistedAt >= TimeSpan.FromMinutes(1))
        {
            Save();
        }
    }

    public void MarkRefreshFailure(
        string hostId,
        DateTimeOffset timestamp,
        string failure)
    {
        var state = GetOrCreateRefreshState(hostId);
        var text = failure ?? string.Empty;
        var newFailure =
            state.LastFailureAt is not { } previousFailure ||
            (state.LastSuccessAt is { } success && previousFailure <= success) ||
            !state.LastFailure.Equals(text, StringComparison.Ordinal);
        state.LastFailureAt = timestamp;
        state.LastFailure = text;
        if (newFailure ||
            timestamp < _lastPersistedAt ||
            timestamp - _lastPersistedAt >= TimeSpan.FromMinutes(1))
        {
            Save();
        }
    }

    public IReadOnlyList<SignalQualityTransition> Reconcile(
        string hostId,
        long generation,
        IReadOnlyList<SignalQualityObservation> observations,
        DateTimeOffset timestamp)
    {
        var host = NormalizeHost(hostId);
        if (_document.LastGeneration.TryGetValue(host, out var previous) &&
            previous == generation)
        {
            return Array.Empty<SignalQualityTransition>();
        }

        _document.LastGeneration[host] = generation;
        var persist = false;
        var current = observations
            .GroupBy(item => item.Fingerprint, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.OrderByDescending(item => item.Severity).First())
            .ToDictionary(item => item.Fingerprint, StringComparer.OrdinalIgnoreCase);
        var transitions = new List<SignalQualityTransition>();

        foreach (var observation in current.Values)
        {
            var incident = _document.Incidents.FirstOrDefault(item =>
                item.HostId.Equals(host, StringComparison.OrdinalIgnoreCase) &&
                item.Fingerprint.Equals(
                    observation.Fingerprint,
                    StringComparison.OrdinalIgnoreCase));

            if (incident is null)
            {
                incident = new SignalQualityIncident
                {
                    Fingerprint = observation.Fingerprint,
                    HostId = host,
                    Severity = observation.Severity,
                    Component = observation.Component,
                    Resource = observation.Resource,
                    Problem = observation.Problem,
                    Evidence = observation.Evidence,
                    NavigationName = observation.NavigationName,
                    FirstSeen = timestamp,
                    LastSeen = timestamp,
                    OccurrenceCount = 1,
                    IsActive = true
                };
                _document.Incidents.Add(incident);
                persist = true;
                transitions.Add(new SignalQualityTransition(
                    SignalQualityTransitionKind.Opened,
                    CloneIncident(incident)));
                continue;
            }

            var wasActive = incident.IsActive;
            var materialChange =
                incident.Severity != observation.Severity ||
                !incident.Problem.Equals(observation.Problem, StringComparison.Ordinal) ||
                !incident.NavigationName.Equals(
                    observation.NavigationName,
                    StringComparison.Ordinal);
            incident.Severity = observation.Severity;
            incident.Component = observation.Component;
            incident.Resource = observation.Resource;
            incident.Problem = observation.Problem;
            incident.Evidence = observation.Evidence;
            incident.NavigationName = observation.NavigationName;
            incident.LastSeen = timestamp;
            incident.OccurrenceCount = Math.Max(1, incident.OccurrenceCount + 1);
            incident.RecoveredAt = null;
            incident.IsActive = true;

            if (materialChange)
                persist = true;

            if (!wasActive)
            {
                persist = true;
                incident.FirstSeen = timestamp;
                incident.OccurrenceCount = 1;
                transitions.Add(new SignalQualityTransition(
                    SignalQualityTransitionKind.Opened,
                    CloneIncident(incident)));
            }
        }

        foreach (var incident in _document.Incidents.Where(item =>
                     item.HostId.Equals(host, StringComparison.OrdinalIgnoreCase) &&
                     item.IsActive &&
                     !current.ContainsKey(item.Fingerprint)))
        {
            incident.IsActive = false;
            incident.RecoveredAt = timestamp;
            persist = true;
            transitions.Add(new SignalQualityTransition(
                SignalQualityTransitionKind.Recovered,
                CloneIncident(incident)));
        }

        _document.Incidents = _document.Incidents
            .OrderByDescending(item => item.IsActive)
            .ThenByDescending(item => item.LastSeen)
            .Take(500)
            .ToList();
        if (persist ||
            timestamp < _lastPersistedAt ||
            timestamp - _lastPersistedAt >= TimeSpan.FromMinutes(5))
        {
            Save();
        }
        return transitions;
    }

    public IReadOnlyList<SignalQualityIncident> ActiveIncidents(string hostId) =>
        _document.Incidents
            .Where(item =>
                item.HostId.Equals(NormalizeHost(hostId), StringComparison.OrdinalIgnoreCase) &&
                item.IsActive)
            .OrderByDescending(item => item.Severity)
            .ThenBy(item => item.Component)
            .Select(CloneIncident)
            .ToArray();

    public IReadOnlyList<SignalQualityIncident> RecentRecoveries(string hostId) =>
        _document.Incidents
            .Where(item =>
                item.HostId.Equals(NormalizeHost(hostId), StringComparison.OrdinalIgnoreCase) &&
                !item.IsActive &&
                item.RecoveredAt is not null)
            .OrderByDescending(item => item.RecoveredAt)
            .Take(50)
            .Select(CloneIncident)
            .ToArray();

    private SignalRefreshStateDocument GetOrCreateRefreshState(string hostId)
    {
        var key = NormalizeHost(hostId);
        if (!_document.RefreshStates.TryGetValue(key, out var state))
        {
            state = new SignalRefreshStateDocument();
            _document.RefreshStates[key] = state;
        }
        return state;
    }

    private SignalQualityDocument Load()
    {
        try
        {
            if (!File.Exists(_filePath))
                return new SignalQualityDocument();
            return JsonSerializer.Deserialize<SignalQualityDocument>(
                       File.ReadAllText(_filePath),
                       _json) ??
                   new SignalQualityDocument();
        }
        catch
        {
            return new SignalQualityDocument();
        }
    }

    private void Save()
    {
        var directory = Path.GetDirectoryName(_filePath)!;
        Directory.CreateDirectory(directory);
        var temporary = _filePath + ".tmp";
        File.WriteAllText(
            temporary,
            JsonSerializer.Serialize(_document, _json));
        File.Move(temporary, _filePath, overwrite: true);
        _lastPersistedAt = DateTimeOffset.UtcNow;
    }

    private static SignalQualityDocument Normalize(SignalQualityDocument document)
    {
        document.Settings = NormalizeSettings(document.Settings);
        document.RefreshStates = new Dictionary<string, SignalRefreshStateDocument>(
            document.RefreshStates ?? new Dictionary<string, SignalRefreshStateDocument>(),
            StringComparer.OrdinalIgnoreCase);
        document.LastGeneration = new Dictionary<string, long>(
            document.LastGeneration ?? new Dictionary<string, long>(),
            StringComparer.OrdinalIgnoreCase);
        document.Incidents ??= new List<SignalQualityIncident>();
        return document;
    }

    private static SignalQualitySettings NormalizeSettings(SignalQualitySettings? settings)
    {
        var result = settings?.Clone() ?? SignalQualitySettings.Defaults();
        result.HostStaleMinutes = Math.Clamp(result.HostStaleMinutes, 1, 1440);
        result.ApplicationStaleMinutes = Math.Clamp(result.ApplicationStaleMinutes, 1, 1440);
        result.BackupStaleMinutes = Math.Clamp(result.BackupStaleMinutes, 1, 10080);
        result.ServiceModes = new Dictionary<string, SignalExpectationMode>(
            result.ServiceModes ?? new Dictionary<string, SignalExpectationMode>(),
            StringComparer.OrdinalIgnoreCase);
        return result;
    }

    private static string NormalizeHost(string hostId) =>
        string.IsNullOrWhiteSpace(hostId) ? "local" : hostId.Trim();

    private static SignalQualityIncident CloneIncident(SignalQualityIncident source) => new()
    {
        Fingerprint = source.Fingerprint,
        HostId = source.HostId,
        Severity = source.Severity,
        Component = source.Component,
        Resource = source.Resource,
        Problem = source.Problem,
        Evidence = source.Evidence,
        NavigationName = source.NavigationName,
        FirstSeen = source.FirstSeen,
        LastSeen = source.LastSeen,
        RecoveredAt = source.RecoveredAt,
        OccurrenceCount = source.OccurrenceCount,
        IsActive = source.IsActive
    };

    public sealed class SignalQualityDocument
    {
        public int Version { get; set; } = 1;
        public SignalQualitySettings Settings { get; set; } =
            SignalQualitySettings.Defaults();
        public Dictionary<string, SignalRefreshStateDocument> RefreshStates { get; set; } =
            new(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, long> LastGeneration { get; set; } =
            new(StringComparer.OrdinalIgnoreCase);
        public List<SignalQualityIncident> Incidents { get; set; } = new();
    }

    public sealed class SignalRefreshStateDocument
    {
        public DateTimeOffset? LastSuccessAt { get; set; }
        public DateTimeOffset? LastFailureAt { get; set; }
        public string LastFailure { get; set; } = string.Empty;
    }
}

public static class SignalQualityPolicy
{
    private const string PortalSource = "xdg-desktop-portal";
    private const string BenignParentWindowMessage = "Unhandled parent window type";

    private static readonly string[] DegradedTokens =
    {
        "failed", "failure", "error", "critical", "offline", "unavailable",
        "unhealthy", "stopped", "exited", "dead", "down", "disconnected",
        "inactive", "not running", "unreachable", "timeout", "timed out"
    };

    private static readonly HashSet<string> KnownExpectedProducts = new(
        new[]
        {
            "dumb", "plex", "sonarr", "radarr", "lidarr", "prowlarr",
            "sabnzbd", "qbittorrent", "recyclarr", "pihole", "decypharr",
            "zurg", "zilean", "postgres", "postgresql",
            "flaresolverr"
        }.Select(NormalizeToken),
        StringComparer.OrdinalIgnoreCase);

    private static readonly Regex SystemdStatePattern = new(
        @"(?<active>active|inactive|failed|activating|deactivating)/(?<sub>[a-z0-9_-]+)",
        RegexOptions.IgnoreCase |
        RegexOptions.CultureInvariant |
        RegexOptions.Compiled);

    private static readonly Regex SupportingServicePattern = new(
        @"(?:^|[·;])\s*(?:Related:\s*)?(?<name>[^·;\[]+?)\s+\[Supporting service\]\s+(?<state>[a-z-]+/[a-z0-9_-]+)",
        RegexOptions.IgnoreCase |
        RegexOptions.CultureInvariant |
        RegexOptions.Compiled);

    public static bool IsBenignPortalParentWarning(OpsLogGroup log)
    {
        var source = log.Source ?? string.Empty;
        var message = log.Message ?? string.Empty;
        var portalEvidence =
            source.Contains(PortalSource, StringComparison.OrdinalIgnoreCase) ||
            message.Contains(PortalSource, StringComparison.OrdinalIgnoreCase);
        return portalEvidence &&
               message.Contains(
                   BenignParentWindowMessage,
                   StringComparison.OrdinalIgnoreCase);
    }

    public static OpsSeverity DisplaySeverity(OpsLogGroup log) =>
        IsBenignPortalParentWarning(log)
            ? OpsSeverity.Info
            : log.Severity;

    public static IReadOnlyList<OpsLogGroup> ForHealthAnalysis(
        IReadOnlyList<OpsLogGroup> logs,
        out int excludedGroups)
    {
        excludedGroups = logs.Count(IsBenignPortalParentWarning);
        return logs.Where(log => !IsBenignPortalParentWarning(log)).ToArray();
    }

    public static string Summary(int excludedGroups) =>
        excludedGroups == 0
            ? "No known benign desktop-portal warning was excluded from health scoring."
            : $"{excludedGroups} known benign desktop-portal " +
              $"{(excludedGroups == 1 ? "group was" : "groups were")} " +
              "demoted for display and excluded from health scoring. " +
              "Original journal evidence remains retained.";

    public static IReadOnlyList<SignalQualityObservation> Evaluate(
        IReadOnlyList<OpsIntegration> integrations,
        SignalQualityRefreshState refresh,
        SignalQualitySettings settings,
        DateTimeOffset now)
    {
        if (!settings.Enabled)
            return Array.Empty<SignalQualityObservation>();

        var observations = new List<SignalQualityObservation>();
        if (settings.EvaluateExpectedServices)
        {
            foreach (var integration in integrations
                         .Where(item => item.IsVisible || item.OwnsHealth)
                         .GroupBy(item =>
                             string.IsNullOrWhiteSpace(item.InstanceKey)
                                 ? $"{item.Name}|{item.DisplayName}|{item.Kind}"
                                 : item.InstanceKey,
                             StringComparer.OrdinalIgnoreCase)
                         .Select(group => group.First()))
            {
                var mode = ResolveExpectation(settings, integration, integration.DisplayName);
                observations.AddRange(EvaluateSupportingServices(
                    settings,
                    integration,
                    mode));
                if (mode is SignalExpectationMode.Optional or SignalExpectationMode.Ignored)
                    continue;
                if (!IsIntegrationDegraded(integration))
                    continue;

                var severity = NormalizedIntegrationSeverity(integration);
                var display = string.IsNullOrWhiteSpace(integration.DisplayName)
                    ? integration.Name
                    : integration.DisplayName;
                observations.Add(CreateObservation(
                    severity,
                    integration.Name,
                    string.IsNullOrWhiteSpace(integration.InstanceKey)
                        ? display
                        : integration.InstanceKey,
                    $"{display} is not in its expected running state",
                    $"{integration.State} · {integration.Evidence}",
                    "An expected service is unavailable or degraded.",
                    "Open the application workspace or logs and restore the expected service.",
                    NavigationForProduct(integration.Name),
                    20));
            }
        }

        if (refresh.LastFailureAt is { } failure &&
            (refresh.LastSuccessAt is null || failure > refresh.LastSuccessAt))
        {
            var noLastGood = refresh.LastSuccessAt is null;
            var age = refresh.LastSuccessAt is { } success
                ? now - success
                : now - failure;
            var stale = !noLastGood &&
                age >= TimeSpan.FromMinutes(settings.HostStaleMinutes);
            observations.Add(CreateObservation(
                OpsSeverity.Warning,
                "Refresh",
                "environment",
                noLastGood
                    ? "Environment telemetry is unavailable"
                    : stale
                        ? "Environment telemetry is stale"
                        : "Latest refresh failed; last-good telemetry is being shown",
                noLastGood
                    ? string.IsNullOrWhiteSpace(refresh.LastFailure)
                        ? "No successful environment capture is available."
                        : $"{refresh.LastFailure} · no successful environment capture is available"
                    : string.IsNullOrWhiteSpace(refresh.LastFailure)
                        ? $"Last successful capture was {FormatAge(age)} ago."
                        : $"{refresh.LastFailure} · last successful capture {FormatAge(age)} ago",
                noLastGood
                    ? "Current environment state is unknown."
                    : "Current values may no longer represent the active host.",
                "Retry refresh and inspect provider or network logs if the failure persists.",
                "DashboardNav",
                5));
        }

        return observations
            .GroupBy(item => item.Fingerprint, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.OrderByDescending(item => item.Severity).First())
            .OrderByDescending(item => item.Severity)
            .ThenBy(item => item.Rank)
            .ToArray();
    }

    public static OpsAnalysis MergeAnalysis(
        OpsAnalysis analysis,
        IReadOnlyList<SignalQualityObservation> observations,
        SignalQualitySettings settings,
        IReadOnlyList<OpsIntegration> integrations)
    {
        var optionalIntegrations = settings.EvaluateExpectedServices
            ? integrations
                .Where(item => ResolveExpectation(settings, item, item.DisplayName) is
                    SignalExpectationMode.Optional or SignalExpectationMode.Ignored)
                .ToArray()
            : Array.Empty<OpsIntegration>();
        var optionalServiceNames = optionalIntegrations
            .SelectMany(item => new[] { item.Name, item.DisplayName })
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var baseFindings = analysis.Findings
            .Where(item => !IsManagedFinding(item))
            .Where(item =>
                item.Severity >= OpsSeverity.Critical ||
                !optionalServiceNames.Any(name =>
                    FindingOwnedByService(item, name)))
            .ToArray();
        var signalFindings = observations.Select(item => item.ToFinding()).ToArray();
        var findings = baseFindings
            .Concat(signalFindings)
            .GroupBy(FindingFingerprint, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.OrderByDescending(item => item.Severity).First())
            .OrderByDescending(item => item.Severity)
            .ThenBy(item => item.Rank)
            .ThenBy(item => item.Component)
            .ToArray();
        var severity = findings
            .Select(item => item.Severity)
            .DefaultIfEmpty(OpsSeverity.Healthy)
            .Max();
        var lead = findings.FirstOrDefault();

        return new OpsAnalysis(
            severity,
            CanonicalStatus(severity),
            lead?.Problem ?? analysis.RootCause,
            lead?.Problem ?? analysis.Headline,
            findings);
    }

    public static IReadOnlyList<UnifiedDashboardCard> ApplyCards(
        IReadOnlyList<UnifiedDashboardCard> cards,
        SignalQualityDashboardContext context)
    {
        if (!context.Settings.Enabled)
            return cards;

        var refreshFailed =
            context.Refresh.LastFailureAt is { } failure &&
            (context.Refresh.LastSuccessAt is null ||
             failure > context.Refresh.LastSuccessAt);
        var refreshAge = context.Refresh.LastSuccessAt is { } success
            ? context.Now - success
            : context.Refresh.LastFailureAt is { } failureAt
                ? context.Now - failureAt
                : TimeSpan.Zero;

        return cards.Select(card =>
        {
            var serviceAware = IsServiceAwareCard(card.Key);
            var projectedRows = card.Rows
                .Select((row, index) => new
                {
                    Row = serviceAware && context.Settings.EvaluateExpectedServices
                        ? ApplyExpectationToRow(card, row, context)
                        : row,
                    Index = index
                })
                .ToArray();
            var rows = card.Key.Equals("core:storage", StringComparison.OrdinalIgnoreCase)
                ? projectedRows.OrderBy(item => item.Index).Select(item => item.Row).ToList()
                : projectedRows
                    .OrderByDescending(item => item.Row.Severity)
                    .ThenBy(item => item.Index)
                    .Select(item => item.Row)
                    .ToList();

            var severity = card.Severity;
            if (serviceAware && rows.Count > 0)
            {
                severity = AggregateOperationalSeverity(
                    card.Severity,
                    rows);
            }

            var summary = card.Summary;
            var firstProblem = rows.FirstOrDefault(item => item.Severity >= OpsSeverity.Warning);
            if (firstProblem is not null && !card.Key.Equals("core:storage", StringComparison.OrdinalIgnoreCase))
            {
                summary = $"{firstProblem.Label} · {firstProblem.Value}";
            }

            var status = PreserveSpecialStatus(card.Status) &&
                         severity < OpsSeverity.Error
                ? card.Status
                : CanonicalStatus(severity);
            if (serviceAware &&
                severity < OpsSeverity.Warning &&
                rows.Count > 0 &&
                rows.All(IsUnknownRow))
            {
                severity = OpsSeverity.Info;
                status = "UNKNOWN";
                summary = "Telemetry did not return a current service state";
            }
            if (card.Key.Equals("core:docker", StringComparison.OrdinalIgnoreCase) &&
                firstProblem is null)
            {
                var optionalStopped = rows.Count(item =>
                    item.Detail.Contains(
                        "stopped state does not affect health.",
                        StringComparison.OrdinalIgnoreCase));
                if (optionalStopped > 0)
                {
                    summary = $"0 expected container faults · {optionalStopped} optional stopped";
                }
            }
            var actions = card.Actions.ToList();

            if (refreshFailed)
            {
                var staleMinutes = StaleMinutesForCard(card, context.Settings);
                var noLastGood = context.Refresh.LastSuccessAt is null;
                var stale = !noLastGood &&
                    refreshAge >= TimeSpan.FromMinutes(staleMinutes);
                severity = MaxSeverity(severity, OpsSeverity.Warning);
                status = noLastGood
                    ? "UNKNOWN"
                    : stale
                        ? "STALE"
                        : "LAST GOOD";
                var freshness = new UnifiedDashboardRow(
                    "Freshness",
                    status,
                    context.Refresh.LastSuccessAt is { } last
                        ? $"Last successful refresh {FormatAge(context.Now - last)} ago. " +
                          context.Refresh.LastFailure
                        : string.IsNullOrWhiteSpace(context.Refresh.LastFailure)
                            ? "No successful refresh is available."
                            : context.Refresh.LastFailure + " · no successful refresh is available",
                    OpsSeverity.Warning);
                rows.Insert(0, freshness);
                summary = noLastGood
                    ? "Current telemetry is unavailable · no successful refresh"
                    : stale
                        ? $"Telemetry is stale · last success {FormatAge(refreshAge)} ago"
                        : "Latest refresh failed · displaying last-good values";
                EnsureAction(actions, new UnifiedDashboardAction(
                    "Refresh",
                    "@refresh"));
            }

            if (severity >= OpsSeverity.Warning)
            {
                EnsureAction(actions, new UnifiedDashboardAction(
                    "Logs",
                    "@logs",
                    LogSource: card.Title,
                    IncludeInformationalLogs: true));
            }

            return card with
            {
                Status = status,
                Severity = severity,
                Summary = summary,
                Rows = rows,
                Actions = actions
            };
        }).ToArray();
    }

    public static SignalExpectationMode ResolveExpectation(
        SignalQualitySettings settings,
        OpsIntegration? integration,
        string serviceName)
    {
        foreach (var key in ExpectationKeys(integration, serviceName))
        {
            if (settings.ServiceModes.TryGetValue(key, out var mode) &&
                mode != SignalExpectationMode.Auto)
            {
                return mode;
            }
        }

        if (integration is not null)
        {
            return integration.IsVerified && integration.OwnsHealth
                ? SignalExpectationMode.Expected
                : SignalExpectationMode.Optional;
        }

        return KnownExpectedProducts.Contains(NormalizeToken(serviceName))
            ? SignalExpectationMode.Expected
            : SignalExpectationMode.Optional;
    }

    public static string ExpectationKey(OpsIntegration integration) =>
        !string.IsNullOrWhiteSpace(integration.InstanceKey)
            ? integration.InstanceKey
            : $"product:{NormalizeToken(integration.Name)}";

    public static string CanonicalStatus(OpsSeverity severity) => severity switch
    {
        OpsSeverity.Critical => "CRITICAL",
        OpsSeverity.Error => "WARNING",
        OpsSeverity.Warning => "ATTENTION",
        OpsSeverity.Healthy => "HEALTHY",
        _ => "INFO"
    };

    public static bool IsManagedFinding(OpsFinding finding) =>
        finding.Evidence.Contains("[signal:", StringComparison.OrdinalIgnoreCase);

    public static string FormatAge(TimeSpan age)
    {
        if (age < TimeSpan.Zero)
            age = TimeSpan.Zero;
        if (age.TotalDays >= 1)
            return $"{age.TotalDays:0.#}d";
        if (age.TotalHours >= 1)
            return $"{age.TotalHours:0.#}h";
        if (age.TotalMinutes >= 1)
            return $"{age.TotalMinutes:0}m";
        return $"{Math.Max(0, age.TotalSeconds):0}s";
    }

    private static UnifiedDashboardRow ApplyExpectationToRow(
        UnifiedDashboardCard card,
        UnifiedDashboardRow row,
        SignalQualityDashboardContext context)
    {
        var integration = FindIntegration(card, row, context.Integrations);
        var mode = ResolveExpectation(context.Settings, integration, row.Label);
        var normalized = NormalizeServiceRow(row, integration);
        var degraded = IsDashboardRowDegraded(normalized, integration);

        if (mode is SignalExpectationMode.Optional or SignalExpectationMode.Ignored)
        {
            return degraded
                ? normalized with
                {
                    Severity = OpsSeverity.Info,
                    Detail = string.IsNullOrWhiteSpace(normalized.Detail)
                        ? "Optional service; its stopped state does not affect health."
                        : normalized.Detail + " · Optional service; stopped state does not affect health."
                }
                : normalized;
        }

        return degraded && normalized.Severity < OpsSeverity.Warning
            ? normalized with { Severity = OpsSeverity.Warning }
            : normalized;
    }

    private static OpsIntegration? FindIntegration(
        UnifiedDashboardCard card,
        UnifiedDashboardRow row,
        IReadOnlyList<OpsIntegration> integrations)
    {
        var rowToken = NormalizeToken(row.Label);
        var cardToken = NormalizeToken(card.Title);
        return integrations.FirstOrDefault(item =>
        {
            var names = new[]
            {
                NormalizeToken(item.Name),
                NormalizeToken(item.DisplayName),
                NormalizeToken(item.InstanceKey)
            };
            return names.Any(name =>
                name.Length > 0 &&
                (name.Equals(rowToken, StringComparison.OrdinalIgnoreCase) ||
                 name.Contains(rowToken, StringComparison.OrdinalIgnoreCase) ||
                 rowToken.Contains(name, StringComparison.OrdinalIgnoreCase))) ||
                   (card.Key.StartsWith("app:", StringComparison.OrdinalIgnoreCase) &&
                    names.Any(name => name.Equals(cardToken, StringComparison.OrdinalIgnoreCase)));
        });
    }

    private static bool IsServiceAwareCard(string key) =>
        key.Equals("core:docker", StringComparison.OrdinalIgnoreCase) ||
        key.Equals("core:downloads", StringComparison.OrdinalIgnoreCase) ||
        key.Equals("core:acquisition", StringComparison.OrdinalIgnoreCase) ||
        key.Equals("core:media", StringComparison.OrdinalIgnoreCase) ||
        key.StartsWith("app:", StringComparison.OrdinalIgnoreCase);

    private static bool PreserveSpecialStatus(string status) =>
        status.Equals("UNMONITORED", StringComparison.OrdinalIgnoreCase) ||
        status.Equals("MUTED", StringComparison.OrdinalIgnoreCase) ||
        status.Equals("IGNORED", StringComparison.OrdinalIgnoreCase) ||
        status.Equals("POLICY MIXED", StringComparison.OrdinalIgnoreCase) ||
        status.Equals("CANDIDATE", StringComparison.OrdinalIgnoreCase) ||
        status.Equals("NOT CONFIGURED", StringComparison.OrdinalIgnoreCase) ||
        status.Equals("CURRENT", StringComparison.OrdinalIgnoreCase);

    private static int StaleMinutesForCard(
        UnifiedDashboardCard card,
        SignalQualitySettings settings)
    {
        if (card.Key.Equals("core:host", StringComparison.OrdinalIgnoreCase) ||
            card.Key.Equals("core:health", StringComparison.OrdinalIgnoreCase) ||
            card.Key.Equals("core:storage", StringComparison.OrdinalIgnoreCase) ||
            card.Key.Equals("core:docker", StringComparison.OrdinalIgnoreCase))
        {
            return settings.HostStaleMinutes;
        }
        if (card.Key.Equals("core:backups", StringComparison.OrdinalIgnoreCase))
            return settings.BackupStaleMinutes;
        return settings.ApplicationStaleMinutes;
    }

    private static bool IsUnknownRow(UnifiedDashboardRow row) =>
        Regex.IsMatch(
            $"{row.Value} {row.Detail}",
            @"(^|\W)(unknown|not reported|no data)($|\W)",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    public static bool IsHealthySystemdServiceState(
        string value,
        bool allowExited = false)
    {
        if (string.IsNullOrWhiteSpace(value))
            return false;

        var match = SystemdStatePattern.Match(value);
        if (match.Success)
        {
            if (!match.Groups["active"].Value.Equals(
                    "active",
                    StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            var sub = match.Groups["sub"].Value;
            return sub.Equals("running", StringComparison.OrdinalIgnoreCase) ||
                   sub.Equals("listening", StringComparison.OrdinalIgnoreCase) ||
                   sub.Equals("waiting", StringComparison.OrdinalIgnoreCase) ||
                   sub.Equals("mounted", StringComparison.OrdinalIgnoreCase) ||
                   sub.Equals("plugged", StringComparison.OrdinalIgnoreCase) ||
                   sub.Equals("exited", StringComparison.OrdinalIgnoreCase) && allowExited;
        }

        return Regex.IsMatch(
            value,
            @"(^|\W)(online|ready|healthy|running|up)(\W|$)",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant) &&
               !Regex.IsMatch(
                   value,
                   @"(^|\W)(offline|unhealthy|not running|failed|failure|error|critical|down)(\W|$)",
                   RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    }

    public static bool IsIntegrationDegraded(OpsIntegration integration)
    {
        ArgumentNullException.ThrowIfNull(integration);
        var primaryEvidence = PrimaryEvidence(integration.Evidence);
        var allowExited = PrimaryAllowsExited(integration, primaryEvidence);
        if (IsHealthySystemdServiceState(integration.State, allowExited) ||
            IsHealthySystemdServiceState(primaryEvidence, allowExited))
        {
            return false;
        }

        return IsDegraded(
            integration.Severity,
            integration.State,
            primaryEvidence);
    }

    private static OpsSeverity NormalizedIntegrationSeverity(
        OpsIntegration integration) =>
        IsIntegrationDegraded(integration)
            ? integration.Severity >= OpsSeverity.Error
                ? integration.Severity
                : OpsSeverity.Warning
            : OpsSeverity.Healthy;

    private static UnifiedDashboardRow NormalizeServiceRow(
        UnifiedDashboardRow row,
        OpsIntegration? integration)
    {
        var primaryEvidence = PrimaryEvidence(row.Detail);
        var allowExited = integration is not null
            ? PrimaryAllowsExited(integration, PrimaryEvidence(integration.Evidence))
            : PrimaryAllowsExited(row.Label, primaryEvidence);
        if (!IsHealthySystemdServiceState(row.Value, allowExited) &&
            !IsHealthySystemdServiceState(primaryEvidence, allowExited))
        {
            return row;
        }

        return row with { Severity = OpsSeverity.Healthy };
    }

    private static bool IsDashboardRowDegraded(
        UnifiedDashboardRow row,
        OpsIntegration? integration)
    {
        var primaryEvidence = PrimaryEvidence(row.Detail);
        var allowExited = integration is not null
            ? PrimaryAllowsExited(integration, PrimaryEvidence(integration.Evidence))
            : PrimaryAllowsExited(row.Label, primaryEvidence);
        if (IsHealthySystemdServiceState(row.Value, allowExited) ||
            IsHealthySystemdServiceState(primaryEvidence, allowExited))
        {
            return false;
        }

        return IsDegraded(row.Severity, row.Value, primaryEvidence);
    }

    private static IReadOnlyList<SignalQualityObservation>
        EvaluateSupportingServices(
            SignalQualitySettings settings,
            OpsIntegration integration,
            SignalExpectationMode parentMode)
    {
        var observations = new List<SignalQualityObservation>();
        foreach (Match match in SupportingServicePattern.Matches(
                     integration.Evidence ?? string.Empty))
        {
            var name = match.Groups["name"].Value.Trim();
            var state = match.Groups["state"].Value.Trim();
            if (string.IsNullOrWhiteSpace(name) ||
                IsHealthySystemdServiceState(state, allowExited: true))
            {
                continue;
            }

            var mode = ResolveSupportingExpectation(
                settings,
                parentMode,
                name);
            if (mode is SignalExpectationMode.Optional or SignalExpectationMode.Ignored)
                continue;

            observations.Add(CreateObservation(
                state.StartsWith("failed/", StringComparison.OrdinalIgnoreCase)
                    ? OpsSeverity.Error
                    : OpsSeverity.Warning,
                name,
                $"support:{NormalizeToken(name)}",
                $"{name} is not in a valid supporting-service state",
                $"{state} · supporting {integration.Name}",
                $"A supporting service for {integration.Name} is unavailable or degraded.",
                "Open logs for the supporting service and restore it without changing the healthy primary application service.",
                NavigationForProduct(integration.Name),
                21));
        }

        return observations;
    }

    private static SignalExpectationMode ResolveSupportingExpectation(
        SignalQualitySettings settings,
        SignalExpectationMode parentMode,
        string serviceName)
    {
        var key = $"service:{NormalizeToken(serviceName)}";
        if (settings.ServiceModes.TryGetValue(key, out var explicitMode) &&
            explicitMode != SignalExpectationMode.Auto)
        {
            return explicitMode;
        }

        return parentMode is SignalExpectationMode.Expected
            ? SignalExpectationMode.Expected
            : SignalExpectationMode.Optional;
    }

    private static string PrimaryEvidence(string evidence)
    {
        if (string.IsNullOrWhiteSpace(evidence))
            return string.Empty;
        var marker = evidence.IndexOf(
            "Related:",
            StringComparison.OrdinalIgnoreCase);
        return marker < 0
            ? evidence
            : evidence[..marker].TrimEnd(' ', '·', ';');
    }

    private static bool PrimaryAllowsExited(
        OpsIntegration integration,
        string primaryEvidence) =>
        PrimaryAllowsExited(
            $"{integration.Kind} {integration.Role} {integration.Provenance}",
            primaryEvidence);

    private static bool PrimaryAllowsExited(
        string identity,
        string primaryEvidence) =>
        ProductOperationalCatalog.AllowsExitedPrimary(
            identity,
            primaryEvidence);

    private static bool IsDegraded(
        OpsSeverity severity,
        params string[] values)
    {
        if (severity >= OpsSeverity.Warning)
            return true;
        var joined = string.Join(" ", values.Where(value => !string.IsNullOrWhiteSpace(value)));
        return DegradedTokens.Any(token =>
            Regex.IsMatch(
                joined,
                $@"(^|\W){Regex.Escape(token)}($|\W)",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant));
    }

    private static IEnumerable<string> ExpectationKeys(
        OpsIntegration? integration,
        string serviceName)
    {
        if (integration is not null)
        {
            if (!string.IsNullOrWhiteSpace(integration.InstanceKey))
                yield return integration.InstanceKey;
            yield return $"product:{NormalizeToken(integration.Name)}";
            if (!string.IsNullOrWhiteSpace(integration.DisplayName))
                yield return $"service:{NormalizeToken(integration.DisplayName)}";
        }
        yield return $"service:{NormalizeToken(serviceName)}";
    }

    private static SignalQualityObservation CreateObservation(
        OpsSeverity severity,
        string component,
        string resource,
        string problem,
        string evidence,
        string impact,
        string nextStep,
        string navigationName,
        int rank)
    {
        var fingerprint = Hash(
            $"{component}\u001f{resource}");
        return new SignalQualityObservation(
            fingerprint,
            severity,
            component,
            resource,
            problem,
            evidence,
            impact,
            nextStep,
            navigationName,
            rank);
    }

    private static string NavigationForProduct(string product) =>
        ProductOperationalCatalog.NavigationFor(product);

    private static string FindingFingerprint(OpsFinding finding) =>
        IsManagedFinding(finding)
            ? ManagedFingerprint(finding.Evidence)
            : Hash($"{finding.Component}\u001f{finding.Problem}");

    private static string ManagedFingerprint(string evidence)
    {
        var marker = evidence.IndexOf("[signal:", StringComparison.OrdinalIgnoreCase);
        if (marker < 0)
            return Hash(evidence);
        var start = marker + "[signal:".Length;
        var end = evidence.IndexOf(']', start);
        return end > start
            ? evidence[start..end]
            : Hash(evidence);
    }

    private static bool FindingOwnedByService(OpsFinding finding, string value) =>
        finding.Component.Contains(value, StringComparison.OrdinalIgnoreCase) ||
        finding.Problem.Contains(value, StringComparison.OrdinalIgnoreCase);

    private static void EnsureAction(
        List<UnifiedDashboardAction> actions,
        UnifiedDashboardAction action)
    {
        if (actions.Any(item =>
                item.NavigationName.Equals(action.NavigationName, StringComparison.OrdinalIgnoreCase) &&
                item.Endpoint.Equals(action.Endpoint, StringComparison.OrdinalIgnoreCase)))
        {
            return;
        }
        actions.Add(action);
    }

    private static OpsSeverity AggregateOperationalSeverity(
        OpsSeverity original,
        IReadOnlyList<UnifiedDashboardRow> rows)
    {
        var actionable = rows
            .Where(item => item.Severity >= OpsSeverity.Warning)
            .Select(item => item.Severity)
            .DefaultIfEmpty(OpsSeverity.Healthy)
            .Max();
        if (actionable >= OpsSeverity.Warning)
            return actionable;
        if (original == OpsSeverity.Healthy ||
            rows.Any(item => item.Severity == OpsSeverity.Healthy))
        {
            return OpsSeverity.Healthy;
        }
        return OpsSeverity.Info;
    }

    private static OpsSeverity MaxSeverity(OpsSeverity left, OpsSeverity right) =>
        left >= right ? left : right;

    private static string NormalizeToken(string value) =>
        new string((value ?? string.Empty)
            .Where(char.IsLetterOrDigit)
            .Select(char.ToLowerInvariant)
            .ToArray());

    private static string Hash(string value) =>
        Convert.ToHexString(
            SHA256.HashData(Encoding.UTF8.GetBytes(value)))
            .ToLowerInvariant()[..20];
}
