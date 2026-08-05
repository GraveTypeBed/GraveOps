using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using GraveOps.Core.Hosts;

namespace GraveOps.Desktop.Linux;

public sealed class ApplicationIdentityProfile
{
    public string SourceKey { get; set; } = string.Empty;
    public string Product { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    public string Role { get; set; } =
        ApplicationIdentityRoles.NativeApplication;
    public string Protocol { get; set; } = string.Empty;
    public string ParentSourceKey { get; set; } = string.Empty;
    public string UrlOverride { get; set; } = string.Empty;
    public string ProbeUrlOverride { get; set; } = string.Empty;
    public bool IsVisible { get; set; } = true;
    public bool ShowInNavigation { get; set; } = true;
    public bool OwnsHealth { get; set; } = true;
    public bool Confirmed { get; set; }
}

public sealed record ApplicationIdentityRecord
{
    public required string SourceKey { get; init; }
    public required string Product { get; init; }
    public required string DisplayName { get; init; }
    public required string Category { get; init; }
    public required string Role { get; init; }
    public required string Protocol { get; init; }
    public required string ParentSourceKey { get; init; }
    public required string ParentProductHint { get; init; }
    public required string Kind { get; init; }
    public required string State { get; init; }
    public required string Evidence { get; init; }
    public required string Endpoint { get; init; }
    public required OpsSeverity Severity { get; init; }
    public required bool OwnsHealth { get; init; }
    public required bool IsVerified { get; init; }
    public required bool IsVisible { get; init; }
    public required bool ShowInNavigation { get; init; }
    public required int Confidence { get; init; }
    public string VerificationState { get; init; } =
        ApplicationVerificationStates.Candidate;
    public string VerificationDetail { get; init; } =
        string.Empty;
    public string ProbeUrl { get; init; } =
        string.Empty;
    public string LaunchUrl { get; init; } =
        string.Empty;
    public DateTimeOffset? LastVerificationAt { get; init; }
    public DateTimeOffset? LastVerifiedAt { get; init; }
    public string ApplicationVersion { get; init; } =
        string.Empty;
    public string ApiVersion { get; init; } =
        string.Empty;
    public string InstanceName { get; init; } =
        string.Empty;
    public string ApplicationDataPath { get; init; } =
        string.Empty;
    public string StartupPath { get; init; } =
        string.Empty;

    public string VerificationLabel =>
        IsVerified
            ? "VERIFIED"
            : "CANDIDATE";

    public string VisibilityText =>
        IsVisible
            ? ShowInNavigation
                ? "Fleet + navigation"
                : "Fleet only"
            : ShowInNavigation
                ? "Navigation only"
                : "Hidden";

    public string Url =>
        string.IsNullOrWhiteSpace(Endpoint)
            ? "--"
            : Endpoint;

    public string OwnerDisplay =>
        string.IsNullOrWhiteSpace(ParentSourceKey)
            ? SourceKey
            : ParentSourceKey;

    public string SourceSummary =>
        $"{Kind} · {Evidence}";
}

public sealed class ApplicationIdentityResolution
{
    public ApplicationIdentityResolution(
        IReadOnlyList<ApplicationIdentityRecord> records,
        IReadOnlyList<OpsIntegration> integrations)
    {
        Records = records;
        Integrations = integrations;
    }

    public IReadOnlyList<ApplicationIdentityRecord> Records { get; }

    public IReadOnlyList<OpsIntegration> Integrations { get; }

    public static ApplicationIdentityResolution Empty { get; } =
        new(
            Array.Empty<ApplicationIdentityRecord>(),
            Array.Empty<OpsIntegration>());
}

public sealed class IdentityOwnerOption
{
    public IdentityOwnerOption(
        string sourceKey,
        string label)
    {
        SourceKey = sourceKey;
        Label = label;
    }

    public string SourceKey { get; }

    public string Label { get; }

    public override string ToString() => Label;
}

public sealed class ApplicationIdentityStore
{
    private readonly JsonSerializerOptions _json =
        new()
        {
            WriteIndented = true,
            PropertyNameCaseInsensitive = true
        };

    private List<ApplicationIdentityProfile> _profiles;

    public ApplicationIdentityStore(
        string? configDirectory = null)
    {
        ConfigDirectory =
            string.IsNullOrWhiteSpace(configDirectory)
                ? Path.Combine(
                    Environment.GetFolderPath(
                        Environment.SpecialFolder.ApplicationData),
                    "GraveOps")
                : Path.GetFullPath(configDirectory);

        Directory.CreateDirectory(ConfigDirectory);

        FilePath = Path.Combine(
            ConfigDirectory,
            "application-identities.json");

        LegacyFilePath = Path.Combine(
            ConfigDirectory,
            "media-launchers.json");

        _profiles = Load();
    }

    public string ConfigDirectory { get; }

    public string FilePath { get; }

    public string LegacyFilePath { get; }

    public IReadOnlyList<ApplicationIdentityProfile> Profiles =>
        _profiles;

    public ApplicationIdentityProfile? Get(
        string sourceKey) =>
        _profiles.FirstOrDefault(item =>
            item.SourceKey.Equals(
                sourceKey,
                StringComparison.OrdinalIgnoreCase));

    public ApplicationIdentityProfile Save(
        ApplicationIdentityProfile profile)
    {
        Validate(profile);

        var role =
            ApplicationIdentityRoles.All.First(item =>
                item.Equals(
                    profile.Role,
                    StringComparison.OrdinalIgnoreCase));

        var normalized =
            new ApplicationIdentityProfile
            {
                SourceKey =
                    profile.SourceKey.Trim(),
                Product =
                    profile.Product.Trim(),
                DisplayName =
                    profile.DisplayName.Trim(),
                Category =
                    profile.Category.Trim(),
                Role =
                    role,
                Protocol =
                    profile.Protocol?.Trim() ??
                    string.Empty,
                ParentSourceKey =
                    profile.ParentSourceKey?.Trim() ??
                    string.Empty,
                UrlOverride =
                    profile.UrlOverride.Trim(),
                ProbeUrlOverride =
                    profile.ProbeUrlOverride?.Trim() ??
                    string.Empty,
                IsVisible =
                    profile.IsVisible,
                ShowInNavigation =
                    profile.ShowInNavigation,
                OwnsHealth =
                    ApplicationIdentityRoles.CanOwnHealth(role) &&
                    profile.OwnsHealth,
                Confirmed =
                    !role.Equals(
                        ApplicationIdentityRoles.DiscoveryCandidate,
                        StringComparison.OrdinalIgnoreCase)
            };

        if (normalized.ParentSourceKey.Equals(
                normalized.SourceKey,
                StringComparison.OrdinalIgnoreCase))
        {
            normalized.ParentSourceKey =
                string.Empty;
        }

        var existing =
            Get(normalized.SourceKey);

        if (existing is null)
        {
            _profiles.Add(normalized);
        }
        else
        {
            _profiles[_profiles.IndexOf(existing)] =
                normalized;
        }

        SaveDocument();
        return normalized;
    }

    public bool Reset(string sourceKey)
    {
        var existing = Get(sourceKey);
        if (existing is null)
            return false;

        var removed = _profiles.Remove(existing);
        if (removed)
            SaveDocument();

        return removed;
    }

    public void MigrateLegacy(
        IReadOnlyList<ApplicationIdentityRecord> records)
    {
        if (_profiles.Count > 0 ||
            !File.Exists(LegacyFilePath))
        {
            return;
        }

        try
        {
            var legacy =
                JsonSerializer.Deserialize<
                    List<LegacyLauncherProfile>>(
                    File.ReadAllText(LegacyFilePath),
                    _json) ??
                new List<LegacyLauncherProfile>();

            foreach (var item in legacy)
            {
                var candidate =
                    records
                        .Where(record =>
                            record.Product.Equals(
                                item.IntegrationName,
                                StringComparison.OrdinalIgnoreCase) &&
                            ApplicationIdentityRoles.IsTopLevel(
                                record.Role))
                        .OrderByDescending(record =>
                            record.IsVerified)
                        .ThenByDescending(record =>
                            record.OwnsHealth)
                        .ThenBy(record =>
                            record.SourceKey)
                        .FirstOrDefault();

                if (candidate is null)
                    continue;

                _profiles.Add(
                    new ApplicationIdentityProfile
                    {
                        SourceKey =
                            candidate.SourceKey,
                        Product =
                            candidate.Product,
                        DisplayName =
                            item.DisplayName?.Trim() ??
                            string.Empty,
                        Category =
                            item.Category?.Trim() ??
                            string.Empty,
                        Role =
                            candidate.Role,
                        Protocol =
                            candidate.Protocol,
                        ParentSourceKey =
                            candidate.ParentSourceKey,
                        UrlOverride =
                            item.UrlOverride?.Trim() ??
                            string.Empty,
                        IsVisible =
                            item.IsVisible,
                        ShowInNavigation =
                            candidate.ShowInNavigation,
                        OwnsHealth =
                            candidate.OwnsHealth,
                        Confirmed =
                            candidate.IsVerified
                    });
            }

            if (_profiles.Count > 0)
                SaveDocument();
        }
        catch
        {
            // A malformed legacy launcher file is ignored. The existing
            // file remains untouched and automatic identity continues.
        }
    }

    private List<ApplicationIdentityProfile> Load()
    {
        try
        {
            if (!File.Exists(FilePath))
                return new List<ApplicationIdentityProfile>();

            return JsonSerializer.Deserialize<
                       List<ApplicationIdentityProfile>>(
                       File.ReadAllText(FilePath),
                       _json) ??
                   new List<ApplicationIdentityProfile>();
        }
        catch
        {
            return new List<ApplicationIdentityProfile>();
        }
    }

    private void SaveDocument()
    {
        var temporary = FilePath + ".tmp";

        File.WriteAllText(
            temporary,
            JsonSerializer.Serialize(
                _profiles
                    .OrderBy(item => item.Product)
                    .ThenBy(item => item.SourceKey)
                    .ToArray(),
                _json));

        File.Move(
            temporary,
            FilePath,
            overwrite: true);
    }

    private static void Validate(
        ApplicationIdentityProfile profile)
    {
        if (string.IsNullOrWhiteSpace(profile.SourceKey))
        {
            throw new InvalidOperationException(
                "A detected source is required.");
        }

        if (string.IsNullOrWhiteSpace(profile.Product))
        {
            throw new InvalidOperationException(
                "Application type is required.");
        }

        if (!ApplicationIdentityRoles.All.Any(role =>
                role.Equals(
                    profile.Role,
                    StringComparison.OrdinalIgnoreCase)))
        {
            throw new InvalidOperationException(
                "Select a supported application role.");
        }

        ValidateHttpUrl(
            profile.UrlOverride,
            "Launch URL override");
        ValidateHttpUrl(
            profile.ProbeUrlOverride,
            "Probe URL override");
    }

    private static void ValidateHttpUrl(
        string? value,
        string label)
    {
        if (string.IsNullOrWhiteSpace(value))
            return;

        if (!Uri.TryCreate(
                value.Trim(),
                UriKind.Absolute,
                out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttp &&
             uri.Scheme != Uri.UriSchemeHttps))
        {
            throw new InvalidOperationException(
                $"{label} must be a complete http:// or https:// address.");
        }
    }

    private sealed class LegacyLauncherProfile
    {
        public string IntegrationName { get; set; } =
            string.Empty;
        public string DisplayName { get; set; } =
            string.Empty;
        public string Category { get; set; } =
            string.Empty;
        public string UrlOverride { get; set; } =
            string.Empty;
        public bool IsVisible { get; set; } =
            true;
    }
}

public static class ApplicationIdentityResolver
{
    private const string ProductLabel =
        "io.github.gravetypebed.graveops.application";
    private const string RoleLabel =
        "io.github.gravetypebed.graveops.role";
    private const string DisplayNameLabel =
        "io.github.gravetypebed.graveops.display-name";
    private const string ParentLabel =
        "io.github.gravetypebed.graveops.parent";
    private const string EndpointLabel =
        "io.github.gravetypebed.graveops.endpoint";
    private const string OwnsHealthLabel =
        "io.github.gravetypebed.graveops.owns-health";
    private const string VisibleLabel =
        "io.github.gravetypebed.graveops.visible";
    private const string NavigationLabel =
        "io.github.gravetypebed.graveops.navigation";

    private static readonly Regex SafeContainerName =
        new(
            "^[A-Za-z0-9][A-Za-z0-9_.-]*$",
            RegexOptions.Compiled);

    public static async Task<ApplicationIdentityResolution>
        ResolveAsync(
            HostSnapshot snapshot,
            string hostScope,
            string urlHost,
            bool inspectLocalDocker,
            ApplicationIdentityStore store,
            CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(store);

        var hostKey =
            NormalizeKey(
                string.IsNullOrWhiteSpace(hostScope)
                    ? snapshot.Hostname
                    : hostScope);

        var dockerMetadata =
            inspectLocalDocker
                ? await CaptureDockerMetadataAsync(
                    snapshot.Containers,
                    cancellationToken)
                : new Dictionary<string, DockerIdentityMetadata>(
                    StringComparer.OrdinalIgnoreCase);

        var records =
            new List<ApplicationIdentityRecord>();

        AddSystemdRecords(
            snapshot,
            hostKey,
            records);

        AddDockerRecords(
            snapshot,
            hostKey,
            urlHost,
            dockerMetadata,
            records);

        if (!inspectLocalDocker)
        {
            AddProviderRecords(
                snapshot,
                hostKey,
                records);
        }

        records =
            records
                .GroupBy(
                    item => item.SourceKey,
                    StringComparer.OrdinalIgnoreCase)
                .Select(group =>
                    group
                        .OrderByDescending(item =>
                            item.Confidence)
                        .ThenByDescending(item =>
                            item.IsVerified)
                        .First())
                .ToList();

        records =
            BindAutomaticRelationships(records);

        store.MigrateLegacy(records);

        records =
            await VerifiedArrDiscoveryService.PromoteAsync(
                snapshot,
                records,
                hostKey,
                urlHost,
                inspectLocalDocker,
                store,
                cancellationToken);

        records =
            records
                .Select(item =>
                    ApplyProfile(
                        item,
                        store.Get(item.SourceKey)))
                .ToList();

        records =
            BindProfileRelationships(records);

        var integrations =
            BuildIntegrations(records);

        return new ApplicationIdentityResolution(
            records
                .OrderBy(item => item.Category)
                .ThenBy(item => item.Product)
                .ThenBy(item => item.DisplayName)
                .ThenBy(item => item.SourceKey)
                .ToArray(),
            integrations);
    }

    public static IReadOnlyList<int> ParsePublishedHostPorts(
        string? portText)
    {
        if (string.IsNullOrWhiteSpace(portText))
            return Array.Empty<int>();

        var ports =
            new HashSet<int>();

        foreach (var segment in portText.Split(
                     ',',
                     StringSplitOptions.RemoveEmptyEntries |
                     StringSplitOptions.TrimEntries))
        {
            var arrow =
                segment.IndexOf(
                    "->",
                    StringComparison.Ordinal);

            if (arrow <= 0)
                continue;

            var hostBinding =
                segment[..arrow].Trim();
            var separator =
                hostBinding.LastIndexOf(':');

            if (separator < 0 ||
                separator ==
                hostBinding.Length - 1)
            {
                continue;
            }

            var token =
                hostBinding[(separator + 1)..]
                    .Trim()
                    .Trim('[', ']');

            if (int.TryParse(
                    token,
                    out var port) &&
                port is > 0 and <= 65535)
            {
                ports.Add(port);
            }
        }

        return ports
            .OrderBy(port => port)
            .ToArray();
    }

    private static void AddSystemdRecords(
        HostSnapshot snapshot,
        string hostKey,
        ICollection<ApplicationIdentityRecord> records)
    {
        foreach (var service in snapshot.Services)
        {
            if (IsPlexSupportUnit(service.Unit))
            {
                records.Add(
                    new ApplicationIdentityRecord
                    {
                        SourceKey =
                            StableSourceKey(
                                "systemd",
                                hostKey,
                                service.Unit),
                        Product =
                            "Plex",
                        DisplayName =
                            "Mullvad Plex bypass",
                        Category =
                            "Supporting service",
                        Role =
                            ApplicationIdentityRoles.SupportingService,
                        Protocol =
                            "Supporting dependency",
                        ParentSourceKey =
                            string.Empty,
                        ParentProductHint =
                            "Plex",
                        Kind =
                            "systemd",
                        State =
                            $"{service.ActiveState}/{service.SubState}",
                        Evidence =
                            $"systemd unit {service.Unit}",
                        Endpoint =
                            string.Empty,
                        Severity =
                            LinuxOpsAnalyzer.ServiceSeverity(service),
                        OwnsHealth =
                            false,
                        IsVerified =
                            true,
                        IsVisible =
                            false,
                        ShowInNavigation =
                            false,
                        Confidence =
                            100
                    });
                continue;
            }

            var definition =
                MatchServiceDefinition(service.Unit);

            if (definition is null)
                continue;

            var exactUnit =
                definition.ServiceUnits.Contains(
                    service.Unit,
                    StringComparer.OrdinalIgnoreCase);

            records.Add(
                new ApplicationIdentityRecord
                {
                    SourceKey =
                        StableSourceKey(
                            "systemd",
                            hostKey,
                            service.Unit),
                    Product =
                        definition.Product,
                    DisplayName =
                        exactUnit
                            ? definition.Product
                            : $"{definition.Product} candidate",
                    Category =
                        definition.Category,
                    Role =
                        exactUnit
                            ? ApplicationIdentityRoles.NativeApplication
                            : ApplicationIdentityRoles.DiscoveryCandidate,
                    Protocol =
                        exactUnit
                            ? "Native service"
                            : "Unverified service-name match",
                    ParentSourceKey =
                        string.Empty,
                    ParentProductHint =
                        string.Empty,
                    Kind =
                        "systemd",
                    State =
                        $"{service.ActiveState}/{service.SubState}",
                    Evidence =
                        exactUnit
                            ? $"exact unit {service.Unit}"
                            : $"service-name hint {service.Unit}",
                    Endpoint =
                        string.Empty,
                    Severity =
                        exactUnit
                            ? LinuxOpsAnalyzer.ServiceSeverity(service)
                            : OpsSeverity.Info,
                    OwnsHealth =
                        exactUnit,
                    IsVerified =
                        exactUnit,
                    IsVisible =
                        exactUnit,
                    ShowInNavigation =
                        exactUnit,
                    Confidence =
                        exactUnit
                            ? 100
                            : 40
                });
        }
    }

    private static void AddDockerRecords(
        HostSnapshot snapshot,
        string hostKey,
        string urlHost,
        IReadOnlyDictionary<string, DockerIdentityMetadata> metadata,
        ICollection<ApplicationIdentityRecord> records)
    {
        foreach (var container in snapshot.Containers)
        {
            metadata.TryGetValue(
                container.Name,
                out var inspected);

            var labels =
                inspected?.Labels ??
                new Dictionary<string, string>(
                    StringComparer.OrdinalIgnoreCase);

            var explicitProduct =
                LabelValue(
                    labels,
                    ProductLabel);

            var match =
                MatchContainerDefinition(
                    explicitProduct,
                    inspected?.ComposeService,
                    container.Image,
                    container.Name);

            ApplicationIdentityRecord? owner =
                null;

            if (match.Definition is not null ||
                !string.IsNullOrWhiteSpace(
                    explicitProduct))
            {
                var product =
                    match.Definition?.Product ??
                    explicitProduct.Trim();
                var category =
                    match.Definition?.Category ??
                    ApplicationIdentityCatalog.DefaultCategory(
                        product);
                var identityVerified =
                    !string.IsNullOrWhiteSpace(
                        explicitProduct) ||
                    match.Confidence >= 90;
                var role =
                    NormalizeRole(
                        LabelValue(
                            labels,
                            RoleLabel),
                        identityVerified
                            ? ApplicationIdentityRoles.NativeApplication
                            : ApplicationIdentityRoles.DiscoveryCandidate);
                var sourceKey =
                    DockerSourceKey(
                        hostKey,
                        container.Name,
                        inspected);
                var endpoint =
                    LabelValue(
                        labels,
                        EndpointLabel);
                var displayName =
                    LabelValue(
                        labels,
                        DisplayNameLabel);

                owner =
                    new ApplicationIdentityRecord
                    {
                        SourceKey =
                            sourceKey,
                        Product =
                            product,
                        DisplayName =
                            string.IsNullOrWhiteSpace(
                                displayName)
                                ? product
                                : displayName.Trim(),
                        Category =
                            category,
                        Role =
                            role,
                        Protocol =
                            "Container-native / not fingerprinted",
                        ParentSourceKey =
                            LabelValue(
                                labels,
                                ParentLabel),
                        ParentProductHint =
                            string.Empty,
                        Kind =
                            string.IsNullOrWhiteSpace(
                                inspected?.ComposeProject)
                                ? "Docker"
                                : "Docker Compose",
                        State =
                            container.Status,
                        Evidence =
                            BuildDockerEvidence(
                                container,
                                inspected,
                                match.Provenance),
                        Endpoint =
                            ValidHttpUrl(endpoint)
                                ? endpoint.Trim()
                                : string.Empty,
                        Severity =
                            LinuxOpsAnalyzer.ContainerSeverity(
                                container),
                        OwnsHealth =
                            identityVerified &&
                            ApplicationIdentityRoles.CanOwnHealth(
                                role) &&
                            LabelBool(
                                labels,
                                OwnsHealthLabel,
                                fallback: true),
                        IsVerified =
                            identityVerified,
                        IsVisible =
                            LabelBool(
                                labels,
                                VisibleLabel,
                                fallback: identityVerified),
                        ShowInNavigation =
                            LabelBool(
                                labels,
                                NavigationLabel,
                                fallback: identityVerified),
                        Confidence =
                            match.Confidence
                    };

                records.Add(owner);
            }

            var allowPortHints =
                owner is null ||
                owner.Product.Equals(
                    "DUMB",
                    StringComparison.OrdinalIgnoreCase);

            if (!allowPortHints)
                continue;

            var published =
                inspected?.PublishedHostPorts.Count > 0
                    ? inspected.PublishedHostPorts
                    : ParsePublishedHostPorts(
                        container.Ports);

            foreach (var definition in
                     ApplicationIdentityCatalog.Definitions)
            {
                var matchedPorts =
                    definition.DiscoveryPorts
                        .Where(port =>
                            published.Contains(port))
                        .Distinct()
                        .OrderBy(port => port)
                        .ToArray();

                foreach (var port in matchedPorts)
                {
                    if (owner is not null &&
                        owner.Product.Equals(
                            definition.Product,
                            StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    records.Add(
                        new ApplicationIdentityRecord
                        {
                            SourceKey =
                                StableSourceKey(
                                    "hint",
                                    DockerSourceKey(
                                        hostKey,
                                        container.Name,
                                        inspected),
                                    definition.Product,
                                    port.ToString()),
                            Product =
                                definition.Product,
                            DisplayName =
                                $"{definition.Product} candidate",
                            Category =
                                definition.Category,
                            Role =
                                ApplicationIdentityRoles.DiscoveryCandidate,
                            Protocol =
                                "Unverified HTTP endpoint",
                            ParentSourceKey =
                                owner?.SourceKey ??
                                string.Empty,
                            ParentProductHint =
                                owner?.Product ??
                                string.Empty,
                            Kind =
                                "Port hint",
                            State =
                                container.Status,
                            Evidence =
                                $"Host port {port} is published by " +
                                $"{container.Name}; application identity is not verified.",
                            Endpoint =
                                $"http://{urlHost}:{port}" +
                                definition.PathSuffix,
                            Severity =
                                OpsSeverity.Info,
                            OwnsHealth =
                                false,
                            IsVerified =
                                false,
                            IsVisible =
                                false,
                            ShowInNavigation =
                                false,
                            Confidence =
                                20
                        });
                }
            }
        }
    }

    private static void AddProviderRecords(
        HostSnapshot snapshot,
        string hostKey,
        ICollection<ApplicationIdentityRecord> records)
    {
        foreach (var integration in snapshot.Integrations)
        {
            var source =
                string.IsNullOrWhiteSpace(
                    integration.Evidence)
                    ? integration.Kind
                    : integration.Evidence;
            var sourceKey =
                StableSourceKey(
                    "provider",
                    hostKey,
                    integration.Kind,
                    source,
                    integration.Name);

            if (records.Any(item =>
                    item.SourceKey.Equals(
                        sourceKey,
                        StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            var portOnly =
                integration.Kind.Contains(
                    "port",
                    StringComparison.OrdinalIgnoreCase);

            records.Add(
                new ApplicationIdentityRecord
                {
                    SourceKey =
                        sourceKey,
                    Product =
                        integration.Name,
                    DisplayName =
                        integration.Name,
                    Category =
                        ApplicationIdentityCatalog.DefaultCategory(
                            integration.Name),
                    Role =
                        portOnly
                            ? ApplicationIdentityRoles.DiscoveryCandidate
                            : ApplicationIdentityRoles.NativeApplication,
                    Protocol =
                        portOnly
                            ? "Unverified provider endpoint"
                            : "Provider-reported",
                    ParentSourceKey =
                        string.Empty,
                    ParentProductHint =
                        string.Empty,
                    Kind =
                        integration.Kind,
                    State =
                        integration.State,
                    Evidence =
                        source,
                    Endpoint =
                        string.Empty,
                    Severity =
                        portOnly
                            ? OpsSeverity.Info
                            : SeverityFromState(
                                integration.State),
                    OwnsHealth =
                        !portOnly,
                    IsVerified =
                        !portOnly,
                    IsVisible =
                        !portOnly,
                    ShowInNavigation =
                        !portOnly,
                    Confidence =
                        portOnly
                            ? 20
                            : 70
                });
        }
    }

    private static List<ApplicationIdentityRecord>
        BindAutomaticRelationships(
            IReadOnlyList<ApplicationIdentityRecord> records)
    {
        var primaryByProduct =
            records
                .Where(item =>
                    ApplicationIdentityRoles.IsTopLevel(
                        item.Role))
                .GroupBy(
                    item => item.Product,
                    StringComparer.OrdinalIgnoreCase)
                .ToDictionary(
                    group => group.Key,
                    group => group
                        .OrderByDescending(item =>
                            item.IsVerified)
                        .ThenByDescending(item =>
                            item.OwnsHealth)
                        .ThenByDescending(item =>
                            item.Confidence)
                        .First(),
                    StringComparer.OrdinalIgnoreCase);

        return records
            .Select(item =>
            {
                if (!string.IsNullOrWhiteSpace(
                        item.ParentSourceKey) ||
                    string.IsNullOrWhiteSpace(
                        item.ParentProductHint) ||
                    !primaryByProduct.TryGetValue(
                        item.ParentProductHint,
                        out var parent) ||
                    parent.SourceKey.Equals(
                        item.SourceKey,
                        StringComparison.OrdinalIgnoreCase))
                {
                    return item;
                }

                return item with
                {
                    ParentSourceKey =
                        parent.SourceKey
                };
            })
            .ToList();
    }

    private static List<ApplicationIdentityRecord>
        BindProfileRelationships(
            IReadOnlyList<ApplicationIdentityRecord> records)
    {
        var sourceKeys =
            records
                .Select(item => item.SourceKey)
                .ToHashSet(
                    StringComparer.OrdinalIgnoreCase);

        var primaryByProduct =
            records
                .Where(item =>
                    ApplicationIdentityRoles.IsTopLevel(
                        item.Role))
                .GroupBy(
                    item => item.Product,
                    StringComparer.OrdinalIgnoreCase)
                .ToDictionary(
                    group => group.Key,
                    group => group
                        .OrderByDescending(item =>
                            item.IsVerified)
                        .ThenByDescending(item =>
                            item.OwnsHealth)
                        .ThenByDescending(item =>
                            item.Confidence)
                        .First(),
                    StringComparer.OrdinalIgnoreCase);

        return records
            .Select(item =>
            {
                var parent =
                    item.ParentSourceKey;

                if (string.IsNullOrWhiteSpace(parent) ||
                    sourceKeys.Contains(parent))
                {
                    return item;
                }

                if (primaryByProduct.TryGetValue(
                        parent,
                        out var resolved) &&
                    !resolved.SourceKey.Equals(
                        item.SourceKey,
                        StringComparison.OrdinalIgnoreCase))
                {
                    return item with
                    {
                        ParentSourceKey =
                            resolved.SourceKey
                    };
                }

                return item with
                {
                    ParentSourceKey =
                        string.Empty
                };
            })
            .ToList();
    }

    private static ApplicationIdentityRecord ApplyProfile(
        ApplicationIdentityRecord detected,
        ApplicationIdentityProfile? profile)
    {
        if (profile is null)
            return detected;

        var product =
            string.IsNullOrWhiteSpace(
                profile.Product)
                ? detected.Product
                : profile.Product.Trim();
        var role =
            NormalizeRole(
                profile.Role,
                detected.Role);
        var ownsHealth =
            ApplicationIdentityRoles.CanOwnHealth(role) &&
            profile.OwnsHealth;

        return detected with
        {
            Product =
                product,
            DisplayName =
                string.IsNullOrWhiteSpace(
                    profile.DisplayName)
                    ? product
                    : profile.DisplayName.Trim(),
            Category =
                string.IsNullOrWhiteSpace(
                    profile.Category)
                    ? ApplicationIdentityCatalog.DefaultCategory(
                        product)
                    : profile.Category.Trim(),
            Role =
                role,
            Protocol =
                string.IsNullOrWhiteSpace(
                    profile.Protocol)
                    ? detected.Protocol
                    : profile.Protocol.Trim(),
            ParentSourceKey =
                profile.ParentSourceKey?.Trim() ??
                string.Empty,
            Endpoint =
                string.IsNullOrWhiteSpace(
                    profile.UrlOverride)
                    ? detected.Endpoint
                    : profile.UrlOverride.Trim(),
            ProbeUrl =
                string.IsNullOrWhiteSpace(
                    profile.ProbeUrlOverride)
                    ? string.IsNullOrWhiteSpace(
                        detected.ProbeUrl)
                        ? detected.Endpoint
                        : detected.ProbeUrl
                    : profile.ProbeUrlOverride.Trim(),
            LaunchUrl =
                string.IsNullOrWhiteSpace(
                    profile.UrlOverride)
                    ? string.IsNullOrWhiteSpace(
                        detected.LaunchUrl)
                        ? detected.Endpoint
                        : detected.LaunchUrl
                    : profile.UrlOverride.Trim(),
            OwnsHealth =
                ownsHealth,
            IsVerified =
                detected.IsVerified ||
                profile.Confirmed,
            IsVisible =
                profile.IsVisible,
            ShowInNavigation =
                profile.ShowInNavigation,
            Confidence =
                profile.Confirmed
                    ? 110
                    : detected.Confidence,
            Evidence =
                profile.Confirmed
                    ? detected.Evidence +
                      " · operator-confirmed identity"
                    : detected.Evidence
        };
    }

    private static IReadOnlyList<OpsIntegration>
        BuildIntegrations(
            IReadOnlyList<ApplicationIdentityRecord> records)
    {
        var children =
            records
                .Where(item =>
                    !ApplicationIdentityRoles.IsTopLevel(
                        item.Role) &&
                    !string.IsNullOrWhiteSpace(
                        item.ParentSourceKey))
                .GroupBy(
                    item => item.ParentSourceKey,
                    StringComparer.OrdinalIgnoreCase)
                .ToDictionary(
                    group => group.Key,
                    group => group.ToArray(),
                    StringComparer.OrdinalIgnoreCase);

        return records
            .Where(item =>
                ApplicationIdentityRoles.IsTopLevel(
                    item.Role))
            .Select(item =>
            {
                var related =
                    children.TryGetValue(
                        item.SourceKey,
                        out var values)
                        ? values
                        : Array.Empty<ApplicationIdentityRecord>();
                var evidence =
                    item.Evidence;

                if (related.Length > 0)
                {
                    evidence +=
                        " · Related: " +
                        string.Join(
                            "; ",
                            related.Select(child =>
                                $"{child.DisplayName} " +
                                $"[{child.Role}] {child.State}"));
                }

                return new OpsIntegration(
                    item.Product,
                    item.Kind,
                    item.State,
                    evidence,
                    item.Endpoint,
                    item.IsVerified &&
                    item.OwnsHealth
                        ? item.Severity
                        : OpsSeverity.Info)
                {
                    InstanceKey =
                        item.SourceKey,
                    DisplayName =
                        item.DisplayName,
                    Category =
                        item.Category,
                    Role =
                        item.Role,
                    Protocol =
                        item.Protocol,
                    OwnerKey =
                        item.ParentSourceKey,
                    OwnsHealth =
                        item.OwnsHealth,
                    IsVerified =
                        item.IsVerified,
                    IsVisible =
                        item.IsVisible,
                    ShowInNavigation =
                        item.ShowInNavigation,
                    Provenance =
                        item.Kind
                };
            })
            .OrderBy(item =>
                item.Category)
            .ThenBy(item =>
                item.Name)
            .ThenBy(item =>
                item.DisplayName)
            .ThenBy(item =>
                item.InstanceKey)
            .ToArray();
    }

    private static ApplicationProductDefinition?
        MatchServiceDefinition(
            string unit)
    {
        var exact =
            ApplicationIdentityCatalog.Definitions
                .FirstOrDefault(definition =>
                    definition.ServiceUnits.Contains(
                        unit,
                        StringComparer.OrdinalIgnoreCase));

        if (exact is not null)
            return exact;

        return ApplicationIdentityCatalog.Definitions
            .FirstOrDefault(definition =>
                definition.IdentityTokens.Any(token =>
                    MatchesToken(
                        unit,
                        token)));
    }

    private static ContainerMatch MatchContainerDefinition(
        string explicitProduct,
        string? composeService,
        string image,
        string name)
    {
        if (!string.IsNullOrWhiteSpace(
                explicitProduct))
        {
            var explicitDefinition =
                ApplicationIdentityCatalog.Find(
                    explicitProduct);

            return new ContainerMatch(
                explicitDefinition,
                "explicit GraveOps label",
                110);
        }

        foreach (var source in new[]
                 {
                     (Value: composeService ?? string.Empty,
                      Provenance: "Compose service",
                      Confidence: 95),
                     (Value: image,
                      Provenance: "container image",
                      Confidence: 100),
                     (Value: name,
                      Provenance: "container name",
                      Confidence: 60)
                 })
        {
            var definition =
                ApplicationIdentityCatalog.Definitions
                    .FirstOrDefault(item =>
                        item.IdentityTokens.Any(token =>
                            MatchesToken(
                                source.Value,
                                token)));

            if (definition is not null)
            {
                return new ContainerMatch(
                    definition,
                    source.Provenance,
                    source.Confidence);
            }
        }

        return new ContainerMatch(
            null,
            "no strong container identity",
            0);
    }

    private static string BuildDockerEvidence(
        DockerContainerSnapshot container,
        DockerIdentityMetadata? metadata,
        string provenance)
    {
        var parts =
            new List<string>
            {
                $"{provenance}: {container.Name}",
                $"image {container.Image}"
            };

        if (!string.IsNullOrWhiteSpace(
                metadata?.ComposeProject))
        {
            parts.Add(
                $"Compose {metadata.ComposeProject}/" +
                $"{metadata.ComposeService}");
        }

        return string.Join(
            " · ",
            parts);
    }

    private static string DockerSourceKey(
        string hostKey,
        string containerName,
        DockerIdentityMetadata? metadata)
    {
        if (!string.IsNullOrWhiteSpace(
                metadata?.ComposeService))
        {
            var deployment =
                !string.IsNullOrWhiteSpace(
                    metadata.ComposeWorkingDirectory)
                    ? metadata.ComposeWorkingDirectory
                    : metadata.ComposeProject;
            var replica =
                LabelValue(
                    metadata.Labels,
                    "com.docker.compose.container-number");

            return StableSourceKey(
                "compose",
                hostKey,
                deployment,
                metadata.ComposeService,
                string.IsNullOrWhiteSpace(replica)
                    ? "1"
                    : replica);
        }

        return StableSourceKey(
            "docker",
            hostKey,
            containerName);
    }

    private static async Task<
        IReadOnlyDictionary<string, DockerIdentityMetadata>>
        CaptureDockerMetadataAsync(
            IReadOnlyList<DockerContainerSnapshot> containers,
            CancellationToken cancellationToken)
    {
        var names =
            containers
                .Select(item => item.Name)
                .Where(name =>
                    !string.IsNullOrWhiteSpace(name) &&
                    SafeContainerName.IsMatch(name))
                .Distinct(
                    StringComparer.OrdinalIgnoreCase)
                .OrderBy(name => name)
                .ToArray();

        if (names.Length == 0)
        {
            return new Dictionary<string, DockerIdentityMetadata>(
                StringComparer.OrdinalIgnoreCase);
        }

        try
        {
            using var process =
                new Process
                {
                    StartInfo =
                        new ProcessStartInfo
                        {
                            FileName =
                                "docker",
                            RedirectStandardOutput =
                                true,
                            RedirectStandardError =
                                true,
                            UseShellExecute =
                                false,
                            CreateNoWindow =
                                true
                        }
                };

            process.StartInfo.ArgumentList.Add(
                "inspect");

            foreach (var name in names)
            {
                process.StartInfo.ArgumentList.Add(
                    name);
            }

            if (!process.Start())
            {
                return new Dictionary<string, DockerIdentityMetadata>(
                    StringComparer.OrdinalIgnoreCase);
            }

            var stdout =
                process.StandardOutput.ReadToEndAsync(
                    cancellationToken);
            var stderr =
                process.StandardError.ReadToEndAsync(
                    cancellationToken);

            using var timeout =
                CancellationTokenSource
                    .CreateLinkedTokenSource(
                        cancellationToken);
            timeout.CancelAfter(
                TimeSpan.FromSeconds(20));

            await process.WaitForExitAsync(
                timeout.Token);

            var output =
                await stdout;
            _ = await stderr;

            if (process.ExitCode != 0 ||
                string.IsNullOrWhiteSpace(output))
            {
                return new Dictionary<string, DockerIdentityMetadata>(
                    StringComparer.OrdinalIgnoreCase);
            }

            using var document =
                JsonDocument.Parse(output);

            var result =
                new Dictionary<string, DockerIdentityMetadata>(
                    StringComparer.OrdinalIgnoreCase);

            foreach (var root in
                     document.RootElement.EnumerateArray())
            {
                var name =
                    StringProperty(
                        root,
                        "Name")
                        .TrimStart('/');

                if (string.IsNullOrWhiteSpace(name))
                    continue;

                var config =
                    ObjectProperty(
                        root,
                        "Config");
                var labels =
                    ReadStringObject(
                        config,
                        "Labels");
                var network =
                    ObjectProperty(
                        root,
                        "NetworkSettings");
                var ports =
                    ReadPublishedPorts(
                        network);
                var id =
                    StringProperty(
                        root,
                        "Id");

                result[name] =
                    new DockerIdentityMetadata(
                        name,
                        id,
                        LabelValue(
                            labels,
                            "com.docker.compose.project"),
                        LabelValue(
                            labels,
                            "com.docker.compose.service"),
                        LabelValue(
                            labels,
                            "com.docker.compose.project.working_dir"),
                        labels,
                        ports);
            }

            return result;
        }
        catch
        {
            return new Dictionary<string, DockerIdentityMetadata>(
                StringComparer.OrdinalIgnoreCase);
        }
    }

    private static IReadOnlyList<int> ReadPublishedPorts(
        JsonElement network)
    {
        if (network.ValueKind !=
                JsonValueKind.Object ||
            !network.TryGetProperty(
                "Ports",
                out var ports) ||
            ports.ValueKind !=
                JsonValueKind.Object)
        {
            return Array.Empty<int>();
        }

        var result =
            new HashSet<int>();

        foreach (var port in
                 ports.EnumerateObject())
        {
            if (port.Value.ValueKind !=
                JsonValueKind.Array)
            {
                continue;
            }

            foreach (var binding in
                     port.Value.EnumerateArray())
            {
                var value =
                    StringProperty(
                        binding,
                        "HostPort");

                if (int.TryParse(
                        value,
                        out var hostPort) &&
                    hostPort is > 0 and <= 65535)
                {
                    result.Add(hostPort);
                }
            }
        }

        return result
            .OrderBy(port => port)
            .ToArray();
    }

    private static Dictionary<string, string>
        ReadStringObject(
            JsonElement parent,
            string property)
    {
        var result =
            new Dictionary<string, string>(
                StringComparer.OrdinalIgnoreCase);

        if (parent.ValueKind !=
                JsonValueKind.Object ||
            !parent.TryGetProperty(
                property,
                out var value) ||
            value.ValueKind !=
                JsonValueKind.Object)
        {
            return result;
        }

        foreach (var item in
                 value.EnumerateObject())
        {
            result[item.Name] =
                item.Value.ValueKind ==
                    JsonValueKind.String
                    ? item.Value.GetString() ??
                      string.Empty
                    : item.Value.ToString();
        }

        return result;
    }

    private static JsonElement ObjectProperty(
        JsonElement parent,
        string property) =>
        parent.ValueKind ==
            JsonValueKind.Object &&
        parent.TryGetProperty(
            property,
            out var value) &&
        value.ValueKind ==
            JsonValueKind.Object
            ? value
            : default;

    private static string StringProperty(
        JsonElement parent,
        string property)
    {
        if (parent.ValueKind !=
                JsonValueKind.Object ||
            !parent.TryGetProperty(
                property,
                out var value))
        {
            return string.Empty;
        }

        return value.ValueKind ==
            JsonValueKind.String
            ? value.GetString() ??
              string.Empty
            : value.ToString();
    }

    private static string LabelValue(
        IReadOnlyDictionary<string, string> labels,
        string key) =>
        labels.TryGetValue(
            key,
            out var value)
            ? value?.Trim() ??
              string.Empty
            : string.Empty;

    private static bool LabelBool(
        IReadOnlyDictionary<string, string> labels,
        string key,
        bool fallback)
    {
        var value =
            LabelValue(
                labels,
                key);

        return bool.TryParse(
            value,
            out var parsed)
                ? parsed
                : fallback;
    }

    private static string NormalizeRole(
        string? value,
        string fallback) =>
        ApplicationIdentityRoles.All
            .FirstOrDefault(role =>
                role.Equals(
                    value?.Trim(),
                    StringComparison.OrdinalIgnoreCase)) ??
        fallback;

    private static bool MatchesToken(
        string? value,
        string token)
    {
        if (string.IsNullOrWhiteSpace(value) ||
            string.IsNullOrWhiteSpace(token))
        {
            return false;
        }

        return Regex.IsMatch(
            value,
            $"(^|[^a-z0-9]){Regex.Escape(token)}([^a-z0-9]|$)",
            RegexOptions.IgnoreCase |
            RegexOptions.CultureInvariant);
    }

    private static bool IsPlexSupportUnit(
        string unit) =>
        unit.Contains(
            "plex",
            StringComparison.OrdinalIgnoreCase) &&
        (
            unit.Contains(
                "mullvad",
                StringComparison.OrdinalIgnoreCase) ||
            unit.Contains(
                "bypass",
                StringComparison.OrdinalIgnoreCase)
        ) &&
        !unit.Equals(
            "plexmediaserver.service",
            StringComparison.OrdinalIgnoreCase);

    private static string StableSourceKey(
        string kind,
        params string?[] parts)
    {
        var seed =
            string.Join(
                "",
                parts.Select(part =>
                    part?.Trim().ToLowerInvariant() ??
                    string.Empty));
        var digest =
            Convert.ToHexString(
                SHA256.HashData(
                    Encoding.UTF8.GetBytes(seed)))
                .ToLowerInvariant();

        return
            $"{NormalizeKey(kind)}|" +
            digest[..24];
    }

    private static string NormalizeKey(
        string value)
    {
        var normalized =
            Regex.Replace(
                value.Trim().ToLowerInvariant(),
                @"[^a-z0-9_.:@/-]+",
                "-")
                .Trim('-');

        return string.IsNullOrWhiteSpace(
            normalized)
            ? "unknown"
            : normalized;
    }

    private static bool ValidHttpUrl(
        string? value) =>
        !string.IsNullOrWhiteSpace(value) &&
        Uri.TryCreate(
            value.Trim(),
            UriKind.Absolute,
            out var uri) &&
        (uri.Scheme == Uri.UriSchemeHttp ||
         uri.Scheme == Uri.UriSchemeHttps);

    private static OpsSeverity SeverityFromState(
        string state)
    {
        var text =
            state?.ToLowerInvariant() ??
            string.Empty;

        if (text.Contains("failed") ||
            text.Contains("error") ||
            text.Contains("unhealthy"))
        {
            return OpsSeverity.Error;
        }

        if (text.Contains("running") ||
            text.Contains("active") ||
            text.Contains("healthy") ||
            text.StartsWith(
                "up ",
                StringComparison.Ordinal))
        {
            return OpsSeverity.Healthy;
        }

        if (text.Contains("exited") ||
            text.Contains("inactive") ||
            text.Contains("degraded"))
        {
            return text.Contains("exited (0)")
                ? OpsSeverity.Info
                : OpsSeverity.Warning;
        }

        return OpsSeverity.Info;
    }

    private sealed record ContainerMatch(
        ApplicationProductDefinition? Definition,
        string Provenance,
        int Confidence);

    private sealed record DockerIdentityMetadata(
        string Name,
        string Id,
        string ComposeProject,
        string ComposeService,
        string ComposeWorkingDirectory,
        IReadOnlyDictionary<string, string> Labels,
        IReadOnlyList<int> PublishedHostPorts);
}
