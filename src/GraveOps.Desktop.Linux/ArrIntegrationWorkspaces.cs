using System.Text.Json;

namespace GraveOps.Desktop.Linux;

public sealed record ArrModuleDefinition(
    string Id,
    string Title,
    string Description);

public sealed record ArrIntegrationDefinition(
    string ProductName,
    string Family,
    string DefaultRole,
    string Summary,
    IReadOnlyList<ArrModuleDefinition> Modules);

public sealed class ArrWorkspaceProfile
{
    public string FriendlyName { get; set; } = string.Empty;
    public string Role { get; set; } = string.Empty;
    public bool PrivacyMode { get; set; }
    public List<string> EnabledModules { get; set; } = new();
}

public sealed record ArrWorkspaceView(
    string InstanceKey,
    OpsIntegration Integration,
    ArrIntegrationDefinition Definition,
    ArrWorkspaceProfile Profile)
{
    public string ProductName => Definition.ProductName;

    public string DisplayName =>
        Profile.PrivacyMode
            ? "Private instance"
            : string.IsNullOrWhiteSpace(Profile.FriendlyName)
                ? Integration.Name
                : Profile.FriendlyName;

    public string Family => Definition.Family;

    public string Role =>
        string.IsNullOrWhiteSpace(Profile.Role)
            ? Definition.DefaultRole
            : Profile.Role;

    public string Summary => Definition.Summary;
    public string State => Integration.State;
    public string Detection => Integration.Kind;
    public string Evidence => Integration.Evidence;
    public string Endpoint => Integration.Endpoint;

    public string SeverityLabel =>
        LinuxOpsAnalyzer.SeverityLabel(
            Integration.Severity);

    public int EnabledModuleCount =>
        Profile.EnabledModules.Count;

    public int AvailableModuleCount =>
        Definition.Modules.Count;
}

public sealed class ArrWorkspaceProfileStore
{
    private readonly string _filePath;
    private readonly JsonSerializerOptions _json = new()
    {
        WriteIndented = true
    };
    private WorkspaceDocument _document;

    public ArrWorkspaceProfileStore()
    {
        var root =
            Environment.GetEnvironmentVariable(
                "XDG_CONFIG_HOME");

        if (string.IsNullOrWhiteSpace(root))
        {
            root = Path.Combine(
                Environment.GetFolderPath(
                    Environment.SpecialFolder.UserProfile),
                ".config");
        }

        _filePath = Path.Combine(
            root,
            "GraveOps",
            "integration-workspaces.json");

        _document = Load();
    }

    public string FilePath => _filePath;

    public ArrWorkspaceProfile Get(
        string instanceKey,
        ArrIntegrationDefinition definition)
    {
        if (!_document.Profiles.TryGetValue(
                instanceKey,
                out var profile))
        {
            return DefaultProfile(definition);
        }

        profile.EnabledModules ??= new List<string>();

        var supported = definition.Modules
            .Select(module => module.Id)
            .ToHashSet(
                StringComparer.OrdinalIgnoreCase);

        return new ArrWorkspaceProfile
        {
            FriendlyName = profile.FriendlyName,
            Role = profile.Role,
            PrivacyMode = profile.PrivacyMode,
            EnabledModules = profile.EnabledModules
                .Where(supported.Contains)
                .Distinct(
                    StringComparer.OrdinalIgnoreCase)
                .ToList()
        };
    }

    public void Save(
        string instanceKey,
        ArrIntegrationDefinition definition,
        string friendlyName,
        string role,
        bool privacyMode,
        IEnumerable<string> enabledModules)
    {
        var supported = definition.Modules
            .Select(module => module.Id)
            .ToHashSet(
                StringComparer.OrdinalIgnoreCase);

        _document.Profiles[instanceKey] =
            new ArrWorkspaceProfile
            {
                FriendlyName =
                    friendlyName?.Trim() ?? string.Empty,
                Role =
                    role?.Trim() ?? string.Empty,
                PrivacyMode = privacyMode,
                EnabledModules = enabledModules
                    .Where(supported.Contains)
                    .Distinct(
                        StringComparer.OrdinalIgnoreCase)
                    .OrderBy(value => value)
                    .ToList()
            };

        Persist();
    }

    public void Reset(string instanceKey)
    {
        if (_document.Profiles.Remove(instanceKey))
            Persist();
    }

    public bool IsCustomized(string instanceKey) =>
        _document.Profiles.ContainsKey(instanceKey);

    private static ArrWorkspaceProfile DefaultProfile(
        ArrIntegrationDefinition definition) =>
        new()
        {
            FriendlyName = string.Empty,
            Role = definition.DefaultRole,
            PrivacyMode = false,
            EnabledModules = definition.Modules
                .Select(module => module.Id)
                .ToList()
        };

    private WorkspaceDocument Load()
    {
        try
        {
            if (!File.Exists(_filePath))
                return new WorkspaceDocument();

            return JsonSerializer.Deserialize<WorkspaceDocument>(
                       File.ReadAllText(_filePath),
                       _json) ??
                   new WorkspaceDocument();
        }
        catch
        {
            return new WorkspaceDocument();
        }
    }

    private void Persist()
    {
        var directory =
            Path.GetDirectoryName(_filePath);

        if (!string.IsNullOrWhiteSpace(directory))
            Directory.CreateDirectory(directory);

        var temporary = _filePath + ".tmp";

        File.WriteAllText(
            temporary,
            JsonSerializer.Serialize(
                _document,
                _json));

        File.Move(
            temporary,
            _filePath,
            overwrite: true);
    }

    private sealed class WorkspaceDocument
    {
        public Dictionary<string, ArrWorkspaceProfile> Profiles
        {
            get;
            set;
        } = new(StringComparer.OrdinalIgnoreCase);
    }
}

public static class ArrWorkspaceRegistry
{
    private static readonly IReadOnlyDictionary<
        string,
        ArrIntegrationDefinition> Definitions =
        BuildDefinitions();

    private static ArrModuleDefinition M(
        string id,
        string title,
        string description) =>
        new(id, title, description);

    public static ArrIntegrationDefinition? Resolve(
        string name)
    {
        if (Definitions.TryGetValue(
                name,
                out var definition))
        {
            return definition;
        }

        var normalized =
            name?.Trim() ?? string.Empty;

        if (normalized.EndsWith(
                "arr",
                StringComparison.OrdinalIgnoreCase) ||
            normalized.Contains(
                "servarr",
                StringComparison.OrdinalIgnoreCase))
        {
            return GenericServarr(normalized);
        }

        return null;
    }

    public static IReadOnlyList<ArrWorkspaceView> BuildViews(
        IReadOnlyList<OpsIntegration> integrations,
        ArrWorkspaceProfileStore store)
    {
        return integrations
            .Select(integration => new
            {
                Integration = integration,
                Definition = Resolve(integration.Name)
            })
            .Where(item => item.Definition is not null)
            .Select(item =>
            {
                var definition = item.Definition!;
                var key = InstanceKey(item.Integration);

                return new ArrWorkspaceView(
                    key,
                    item.Integration,
                    definition,
                    store.Get(key, definition));
            })
            .OrderBy(item => item.ProductName)
            .ThenBy(item => item.DisplayName)
            .ThenBy(item => item.Evidence)
            .ToArray();
    }

    public static string InstanceKey(
        OpsIntegration integration) =>
        string.Join(
            "|",
            integration.Name.Trim(),
            integration.Kind.Trim(),
            integration.Endpoint?.Trim() ?? string.Empty,
            integration.Evidence?.Trim() ?? string.Empty);

    private static IReadOnlyDictionary<
        string,
        ArrIntegrationDefinition> BuildDefinitions()
    {
        var rows =
            new Dictionary<
                string,
                ArrIntegrationDefinition>(
                StringComparer.OrdinalIgnoreCase);

        void Add(
            string name,
            string family,
            string role,
            string summary,
            params ArrModuleDefinition[] modules) =>
            rows[name] =
                new ArrIntegrationDefinition(
                    name,
                    family,
                    role,
                    summary,
                    modules);

        Add(
            "Sonarr",
            "Servarr · television",
            "Series acquisition and import",
            "Series, seasons and episodes with configurable queue, calendar, profile and dependency modules.",
            M("overview", "Instance overview", "Version, runtime, endpoint, deployment identity and role."),
            M("health", "Health and tasks", "Health checks, scheduled tasks and provider warnings."),
            M("library", "Series and episode coverage", "Series, seasons, monitored, missing, unaired and cutoff coverage."),
            M("queue", "Episode queue and imports", "Downloading, delayed, stalled, failed and import-blocked work."),
            M("calendar", "Calendar", "Upcoming monitored episodes and expected availability."),
            M("profiles", "Profiles and release rules", "Quality, language, custom-format and release-profile distribution."),
            M("indexers", "Indexer dependencies", "Indexer availability, tags, categories and rejection context."),
            M("clients", "Download clients", "Client availability, categories, mappings and import handoff."),
            M("storage", "Root folders and storage", "Root access, free space and destination readiness."),
            M("activity", "Activity and history", "Recent grabs, imports, upgrades and failures."),
            M("dependencies", "Dependency graph", "Prowlarr, clients, storage and companion relationships."),
            M("policies", "Configurable rules", "User rules by tag, profile, root or series type."));

        Add(
            "Radarr",
            "Servarr · movies",
            "Movie acquisition and import",
            "Movies, availability, editions, collections, upgrades, queues and dependencies.",
            M("overview", "Instance overview", "Version, runtime, endpoint, deployment identity and role."),
            M("health", "Health and tasks", "Health checks, scheduled tasks and provider warnings."),
            M("library", "Movie coverage", "Monitored, missing, available and cutoff-unmet movies."),
            M("collections", "Collections and editions", "Collection completeness, editions and duplicate context."),
            M("queue", "Movie queue and imports", "Downloading, stalled, failed and import-blocked work."),
            M("profiles", "Profiles and custom formats", "Quality, scores, custom formats and upgrade distribution."),
            M("lists", "Import lists", "List connectivity, additions, exclusions and library flow."),
            M("indexers", "Indexer dependencies", "Indexer availability, categories, tags and rejection context."),
            M("clients", "Download clients", "Client availability, categories, mappings and import handoff."),
            M("storage", "Root folders and storage", "Root access, free space and destination readiness."),
            M("activity", "Activity and history", "Recent grabs, imports, upgrades, deletions and failures."),
            M("policies", "Configurable rules", "User rules by tag, profile, list, collection or root."));

        Add(
            "Lidarr",
            "Servarr · music",
            "Music acquisition and import",
            "Artists, albums, releases and tracks with metadata, format, queue and dependency modules.",
            M("overview", "Instance overview", "Version, runtime, endpoint, deployment identity and role."),
            M("health", "Health and tasks", "Health checks, scheduled tasks and provider warnings."),
            M("library", "Artist and album coverage", "Artists, albums, releases, tracks and completeness."),
            M("queue", "Music queue and imports", "Downloading, stalled, failed, unidentified and blocked work."),
            M("metadata", "Metadata services", "Metadata refresh, provider connectivity and release identification."),
            M("audio", "Audio properties", "Codec, bitrate, sample rate, bit depth and channels."),
            M("profiles", "Profiles and releases", "Quality, metadata, release and monitoring profiles."),
            M("indexers", "Indexer dependencies", "Music-category coverage and rejection context."),
            M("clients", "Download clients", "Client availability, categories, mappings and handoff."),
            M("storage", "Root folders and storage", "Root access, free space and destination readiness."),
            M("activity", "Activity and history", "Recent grabs, imports, renames and failures."),
            M("policies", "Configurable audits", "Optional folder, tag, format, edition and completeness rules."));

        Add(
            "Prowlarr",
            "Servarr · indexers",
            "Indexer and application synchronization",
            "Indexer health, categories, statistics, proxies, application sync and coverage.",
            M("overview", "Instance overview", "Version, runtime, endpoint, deployment identity and role."),
            M("health", "Health and tasks", "Health checks, scheduled tasks and warnings."),
            M("indexers", "Indexer health matrix", "Enabled, disabled, unavailable, backoff and failed indexers."),
            M("sync", "Application synchronization", "Connected applications, sync mode, tags and delivery."),
            M("categories", "Category coverage", "Torrent and Usenet category support by application."),
            M("statistics", "Indexer statistics", "Queries, grabs, failures, response time and success trends."),
            M("limits", "Limits and backoff", "Rate limits, temporary disablement and recovery windows."),
            M("proxies", "Proxy dependencies", "Proxy mappings and FlareSolverr-style dependencies."),
            M("clients", "Download clients", "Configured clients and protocol handoff readiness."),
            M("activity", "Search and grab activity", "Recent searches, grabs, failures and attribution."),
            M("dependencies", "Coverage graph", "Which indexers and tags reach each application."),
            M("policies", "Coverage rules", "User minimum-coverage and health rules per app or tag."));

        Add(
            "Readarr",
            "Servarr · books",
            "Book and audiobook acquisition",
            "Authors, books, editions and files with metadata, formats and Calibre dependencies.",
            M("overview", "Instance overview", "Version, retirement state, runtime, endpoint and role."),
            M("health", "Health and tasks", "Health checks, scheduled tasks and warnings."),
            M("library", "Authors and books", "Authors, books, editions, monitoring and missing coverage."),
            M("queue", "Book queue and imports", "Downloading, stalled, failed and blocked work."),
            M("formats", "Formats and editions", "Ebook, audiobook, edition and file-format distribution."),
            M("metadata", "Metadata services", "Provider health, refresh status and edition resolution."),
            M("calibre", "Calibre integration", "Calibre connectivity and import flow."),
            M("indexers", "Indexer dependencies", "Book-category coverage and rejection context."),
            M("clients", "Download clients", "Client availability, categories, mappings and handoff."),
            M("storage", "Root folders and storage", "Root access, free space and readiness."),
            M("migration", "Legacy and migration", "Inventory and retirement-aware migration planning."),
            M("policies", "Configurable rules", "User rules by format, edition, profile, tag or root."));

        Add(
            "Whisparr",
            "Servarr · version-aware scene and movie media",
            "Scene, studio and movie acquisition",
            "Version-aware content workflows with configurable privacy and coverage modules.",
            M("overview", "Instance overview", "Version model, runtime, endpoint and role."),
            M("privacy", "Privacy presentation", "Optional masking for titles, people, artwork and activity."),
            M("health", "Health and tasks", "Health checks, tasks and warnings."),
            M("library", "Content coverage", "Version-aware studio, scene, movie and file coverage."),
            M("queue", "Queue and imports", "Downloading, stalled, failed and blocked work."),
            M("profiles", "Profiles and formats", "Quality, edition, custom-format and upgrade distribution."),
            M("indexers", "Indexer dependencies", "Category coverage and rejection context."),
            M("clients", "Download clients", "Client availability, categories and mappings."),
            M("storage", "Root folders and storage", "Root access, free space and readiness."),
            M("activity", "Activity and history", "Recent grabs, imports, upgrades and failures."),
            M("dependencies", "Dependency graph", "Prowlarr, clients, storage and companions."),
            M("policies", "Configurable rules", "User monitoring, privacy and coverage rules."));

        Add(
            "Mylar3",
            "Arr ecosystem · comics",
            "Comic acquisition and post-processing",
            "Series, issues, pull lists, providers and post-processing.",
            M("overview", "Instance overview", "Runtime, endpoint, deployment and role."),
            M("health", "Health and tasks", "Warnings, jobs and schedules."),
            M("library", "Series and issue coverage", "Series, issues, annuals, wanted and archived coverage."),
            M("pull", "Weekly pull list", "Expected issues, matches, gaps and future monitoring."),
            M("queue", "Queue and acquisition", "Wanted, snatched, downloaded and failed work."),
            M("postprocess", "Post-processing", "Import, rename, conversion and processing outcomes."),
            M("metadata", "Metadata dependencies", "Comic metadata health and lookup failures."),
            M("clients", "Download clients", "Client availability, categories and handoff."),
            M("storage", "Comic locations", "Paths, free space and destination readiness."),
            M("formats", "Archive formats", "CBR, CBZ and other format distribution."),
            M("activity", "Activity and history", "Recent grabs, imports and failures."),
            M("policies", "Configurable rules", "Rules by publisher, series, age, format or retention."));

        Add(
            "Bazarr",
            "Arr companion · subtitles",
            "Subtitle acquisition and synchronization",
            "Subtitle coverage, languages, providers, upgrades and Sonarr/Radarr synchronization.",
            M("overview", "Instance overview", "Runtime, endpoint, deployment and role."),
            M("health", "Health and tasks", "Health, jobs and provider warnings."),
            M("sync", "Sonarr and Radarr sync", "Connected libraries, lag and mapping readiness."),
            M("coverage", "Subtitle coverage", "Missing coverage by title and language."),
            M("languages", "Language profiles", "Regular, forced and hearing-impaired requirements."),
            M("providers", "Subtitle providers", "Availability, authentication, limits and failures."),
            M("quality", "Scores and upgrades", "Scores, upgrade eligibility and preferences."),
            M("timing", "Synchronization quality", "Timing and embedded-versus-external visibility."),
            M("duplicates", "Duplicate detection", "Duplicate or conflicting subtitle files."),
            M("activity", "Search and download history", "Recent searches, downloads and failures."),
            M("storage", "Subtitle paths", "Path visibility, permissions and readiness."),
            M("policies", "Language rules", "User language, score, provider and upgrade policies."));

        Add(
            "Recyclarr",
            "Arr companion · configuration",
            "TRaSH-based configuration synchronization",
            "Targets, drift, previews, templates, validation and rollback evidence.",
            M("overview", "Instance overview", "Deployment, config roots and target count."),
            M("targets", "Configured targets", "Resolved targets and compatibility."),
            M("drift", "Current versus desired", "Profile, format, naming and definition drift."),
            M("preview", "Preview changes", "Read-only change preview before writes."),
            M("templates", "Templates and includes", "Template resolution and inheritance."),
            M("validation", "Configuration validation", "Syntax, deprecation and compatibility."),
            M("history", "Synchronization history", "Prior success, failure and changed objects."),
            M("snapshots", "Rollback snapshots", "Pre-sync snapshots and restoration evidence."),
            M("dependencies", "Target dependencies", "Reachability, authentication and versions."),
            M("policies", "Guarded sync policy", "Safe Mode and confirmation requirements."));

        Add(
            "Configarr",
            "Arr companion · configuration",
            "Template and custom configuration synchronization",
            "Sources, targets, drift, dry runs, deletion scope and guarded synchronization.",
            M("overview", "Instance overview", "Deployment, repositories and targets."),
            M("targets", "Configured targets", "Supported and experimental target capabilities."),
            M("sources", "Configuration sources", "Repositories, local sources and includes."),
            M("drift", "Managed-object drift", "Managed versus unmanaged state."),
            M("preview", "Dry-run preview", "Creates, updates and deletions before sync."),
            M("deletion", "Deletion scope", "Exact scope and conflict warnings."),
            M("validation", "Configuration validation", "Syntax, merge ordering and compatibility."),
            M("history", "Synchronization history", "Prior outcomes and per-target changes."),
            M("snapshots", "Rollback snapshots", "Pre-sync snapshots and restoration evidence."),
            M("policies", "Guarded sync policy", "Safe Mode and confirmation requirements."));

        Add(
            "Cleanuparr",
            "Arr companion · download hygiene",
            "Download cleanup governance",
            "Strike tracking, stalled work, orphan detection, seeding policy and simulations.",
            M("overview", "Instance overview", "Deployment, connected apps and clients."),
            M("health", "Health and schedules", "Service health and job schedules."),
            M("strikes", "Active strikes", "Downloads with strikes, reasons and age."),
            M("stalled", "Stalled and suspicious work", "Slow, metadata-stuck or blocked candidates."),
            M("imports", "Import failures", "Downloads that failed application import."),
            M("orphans", "Orphan detection", "Downloads no longer owned by an app."),
            M("seeding", "Seeding policy", "Ratio, time, tracker, category and tag rules."),
            M("simulation", "Cleanup simulation", "Exact candidates without removal."),
            M("history", "Cleanup history", "Prior decisions and replacement results."),
            M("policies", "Per-app and client rules", "Rules by app, client, category, tag or tracker."));

        Add(
            "Maintainerr",
            "Arr companion · lifecycle policy",
            "Rule-driven media lifecycle",
            "Rule evaluation, matches, countdowns, dependencies and projected recovery.",
            M("overview", "Instance overview", "Deployment, connected systems and rule count."),
            M("health", "Health and schedules", "Service health and evaluation schedules."),
            M("rules", "Rule health", "Validity, last evaluation and conflicts."),
            M("matches", "Matched and excluded media", "Matches, exclusions and rule attribution."),
            M("countdowns", "Planned actions", "Leaving-soon collections and countdowns."),
            M("dependencies", "Dependency graph", "Media, request and Arr readiness."),
            M("storage", "Projected recovery", "Estimated recovery by rule."),
            M("simulation", "Dry-run evaluation", "Outcomes without deletion or unmonitoring."),
            M("history", "Rule history", "Prior evaluations and exclusions."),
            M("policies", "Approval policy", "Explicit approval and Safe Mode requirements."));

        Add(
            "Profilarr",
            "Arr companion · profile management",
            "Reusable profile deployment",
            "Profile sources, targets, revisions, drift, previews and rollback.",
            M("overview", "Instance overview", "Deployment, sources and target count."),
            M("sources", "Profile sources", "Repositories, revisions and definitions."),
            M("targets", "Target applications", "Connections and capabilities."),
            M("profiles", "Profile inventory", "Profiles, custom formats and scoring bundles."),
            M("drift", "Deployment drift", "Target differences from the source."),
            M("preview", "Change preview", "Creates, updates and removals."),
            M("history", "Deployment history", "Prior deployments and outcomes."),
            M("snapshots", "Rollback evidence", "Pre-deployment snapshots."),
            M("policies", "Guarded deployment", "Safe Mode and confirmation requirements."));

        Add(
            "Unpackerr",
            "Arr companion · extraction",
            "Archive extraction and import handoff",
            "Extraction queue, failures, retries, storage and Arr callbacks.",
            M("overview", "Instance overview", "Deployment, connected apps and paths."),
            M("health", "Health and schedules", "Health, polling and warnings."),
            M("queue", "Extraction queue", "Pending, active, completed and failed work."),
            M("failures", "Failure analysis", "Password, corruption, permission and space failures."),
            M("retries", "Retry policy", "Counts, backoff and abandoned work."),
            M("storage", "Temporary and destination storage", "Free space and path readiness."),
            M("callbacks", "Arr handoff", "Application notification and rescan outcomes."),
            M("activity", "Extraction history", "Recent results and source attribution."),
            M("policies", "Extraction rules", "Timeout, cleanup and retry policies."));

        Add(
            "autobrr",
            "Arr ecosystem · release automation",
            "Release matching and action routing",
            "Filters, sources, matches, actions, clients and application delivery.",
            M("overview", "Instance overview", "Deployment, sources and action count."),
            M("health", "Health and schedules", "Health, source connectivity and warnings."),
            M("sources", "IRC and feed sources", "Sources, feeds and authentication state."),
            M("filters", "Filter inventory", "Rules, priorities and schedules."),
            M("matches", "Matched releases", "Matches, rejections and filter attribution."),
            M("actions", "Action routing", "Arr, client, webhook and script outcomes."),
            M("clients", "Client dependencies", "Connectivity and category routing."),
            M("activity", "Activity history", "Announcements, matches and failures."),
            M("policies", "Automation rules", "User guardrails for filters and actions."));

        return rows;
    }

    private static ArrIntegrationDefinition GenericServarr(
        string name) =>
        new(
            string.IsNullOrWhiteSpace(name)
                ? "Servarr-compatible application"
                : name,
            "Generic Servarr-compatible adapter",
            "User-defined acquisition workflow",
            "Common health, queue, library, profile and dependency modules.",
            new[]
            {
                M("overview", "Instance overview", "Version, runtime, endpoint and role."),
                M("health", "Health and tasks", "Health checks, tasks and warnings."),
                M("library", "Library coverage", "Monitored, missing and file coverage."),
                M("queue", "Queue and imports", "Downloading, stalled, failed and blocked work."),
                M("profiles", "Profiles", "Quality, format, language or domain profiles."),
                M("indexers", "Indexer dependencies", "Indexer health and rejection context."),
                M("clients", "Download clients", "Availability, categories and handoff."),
                M("storage", "Root folders and storage", "Access, free space and readiness."),
                M("activity", "Activity and history", "Recent changes and failures."),
                M("dependencies", "Dependency graph", "Upstream and downstream relationships."),
                M("policies", "Configurable rules", "User-defined operational rules.")
            });
}

