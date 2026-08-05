using GraveOps.Core.Applications;
using GraveOps.Core.Providers;
using GraveOps.Core.Security;
using GraveOps.Core.Snapshots;
using GraveOps.Core.Targets;
using GraveOps.Core.Telemetry;

var tests = new (string Name, Action Run)[]
{
    ("target switching rejects earlier selection", TargetSwitchingRejectsEarlierSelection),
    ("new refresh rejects older refresh", NewRefreshRejectsOlderRefresh),
    ("capabilities are case insensitive", CapabilitiesAreCaseInsensitive),
    ("provider registry resolves by target", ProviderRegistryResolvesByTarget),
    ("application requires owner target", ApplicationRequiresOwnerTarget),
    ("application registry preserves other target inventories", ApplicationRegistryPreservesOtherTargets),
    ("application registry resolves and enforces owner target", ApplicationRegistryResolvesAndEnforcesOwner),
    ("fleet navigation resolves remote application owner", FleetNavigationResolvesRemoteApplicationOwner),
    ("shared identity catalog preserves product categories", SharedIdentityCatalogPreservesProductCategories),
    ("classifier distinguishes Plex server and desktop", ClassifierDistinguishesPlexServerAndDesktop),
    ("classifier distinguishes qBittorrent desktop and Web UI", ClassifierDistinguishesQBittorrentDesktopAndWebUi),
    ("classifier recognizes container hosted applications", ClassifierRecognizesContainerHostedApplications),
    ("classifier canonicalizes product aliases", ClassifierCanonicalizesProductAliases),
    ("classifier preserves supporting service identity", ClassifierPreservesSupportingServiceIdentity),
    ("telemetry contracts are platform neutral", TelemetryContractsArePlatformNeutral),
    ("telemetry envelope preserves ownership", TelemetryEnvelopePreservesOwnership),
    ("telemetry envelope detects stale snapshots", TelemetryEnvelopeDetectsStaleSnapshots),
    ("telemetry health classifier normalizes states", TelemetryHealthClassifierNormalizesStates),
    ("shared telemetry snapshots expose capture time", SharedTelemetrySnapshotsExposeCaptureTime),
    ("Pi-hole telemetry formats shared metrics", PiHoleTelemetryFormatsSharedMetrics),
    ("application cache omits endpoints and secrets", ApplicationCacheOmitsEndpointsAndSecrets),
    ("application cache round trips safe ownership", ApplicationCacheRoundTripsSafeOwnership),
    ("application cache ignores malformed documents", ApplicationCacheIgnoresMalformedDocuments),
    ("secret values redact and dispose", SecretValuesRedactAndDispose)
};

var failures = new List<string>();

foreach (var test in tests)
{
    try
    {
        test.Run();
        Console.WriteLine($"PASS: {test.Name}");
    }
    catch (Exception exception)
    {
        failures.Add($"{test.Name}: {exception.Message}");
        Console.WriteLine($"FAIL: {test.Name}");
    }
}

if (failures.Count == 0)
{
    Console.WriteLine($"All {tests.Length} contract tests passed.");
    return 0;
}

Console.Error.WriteLine(string.Join(Environment.NewLine, failures));
return 1;

static void TargetSwitchingRejectsEarlierSelection()
{
    var coordinator = new TargetRefreshCoordinator();
    coordinator.Select("linux-local");
    var linuxLease = coordinator.BeginRefresh();

    coordinator.Select("windows-remote");
    var windowsLease = coordinator.BeginRefresh();

    Assert(!coordinator.IsCurrent(linuxLease),
        "The previous target lease was accepted after switching targets.");
    Assert(coordinator.IsCurrent(windowsLease),
        "The active target lease was rejected.");
}

static void NewRefreshRejectsOlderRefresh()
{
    var coordinator = new TargetRefreshCoordinator();
    coordinator.Select("linux-local");

    var first = coordinator.BeginRefresh();
    var second = coordinator.BeginRefresh();

    Assert(!coordinator.IsCurrent(first),
        "The earlier refresh was accepted after a newer refresh began.");
    Assert(coordinator.IsCurrent(second),
        "The newest refresh was rejected.");
}

static void CapabilitiesAreCaseInsensitive()
{
    var capabilities = new TargetCapabilities(
        new[] { CapabilityIds.StorageRead });

    Assert(capabilities.Supports("HOST.STORAGE.READ"),
        "Capability lookup must be case insensitive.");
}

static void ProviderRegistryResolvesByTarget()
{
    var linux = new FakeProvider(
        HostProviderIds.LocalLinux,
        target => target.ProviderId == HostProviderIds.LocalLinux);
    var windows = new FakeProvider(
        HostProviderIds.LocalWindows,
        target => target.ProviderId == HostProviderIds.LocalWindows);

    var registry = new HostProviderRegistry(new[] { linux, windows });
    var target = new TargetProfile(
        "local-windows",
        "Local Windows",
        HostProviderIds.LocalWindows,
        TargetPlatform.Windows,
        TargetLocation.Local,
        TargetConnectionProfile.Local);

    Assert(ReferenceEquals(registry.Resolve(target), windows),
        "The provider registry resolved the wrong provider.");
}

static void ApplicationRequiresOwnerTarget()
{
    var application = new ApplicationInstance(
        "plex-server-1",
        "plex-media-server",
        "Plex Media Server",
        "linux-server",
        ApplicationRole.Server,
        ApplicationRuntimeKind.SystemdService,
        new Uri("http://example.invalid:32400"),
        new TargetCapabilities(
            new[] { CapabilityIds.ApplicationApiTelemetry }));

    application.Validate();
    Assert(application.OwnerTargetId == "linux-server",
        "Application ownership was not retained.");
}

static void ApplicationRegistryPreservesOtherTargets()
{
    var registry =
        new ApplicationRegistry();

    registry.ReplaceTargetInventory(
        "local-linux",
        new[]
        {
            CreateApplication(
                "plex-local",
                "plex",
                "Plex",
                "local-linux"),
            CreateApplication(
                "sab-local",
                "sabnzbd",
                "SABnzbd",
                "local-linux")
        });

    registry.ReplaceTargetInventory(
        "pi-hole",
        new[]
        {
            CreateApplication(
                "pihole-remote",
                "pi-hole",
                "Pi-hole",
                "pi-hole")
        });

    registry.ReplaceTargetInventory(
        "local-linux",
        new[]
        {
            CreateApplication(
                "plex-local",
                "plex",
                "Plex",
                "local-linux")
        });

    Assert(
        registry.ForTarget(
            "local-linux").Count == 1,
        "Replacing one target did not remove its stale application.");
    Assert(
        registry.ForTarget(
            "pi-hole").Count == 1,
        "Replacing one target erased another target's inventory.");
}

static void ApplicationRegistryResolvesAndEnforcesOwner()
{
    var registry =
        new ApplicationRegistry();

    registry.ReplaceTargetInventory(
        "pi-hole",
        new[]
        {
            CreateApplication(
                "pihole-remote",
                "pi-hole",
                "Pi-hole",
                "pi-hole")
        });

    Assert(
        registry.ResolveOwnerTargetId(
            "pihole-remote") ==
        "pi-hole",
        "Application owner target was not resolved.");

    try
    {
        registry.ReplaceTargetInventory(
            "local-linux",
            new[]
            {
                CreateApplication(
                    "wrong-owner",
                    "plex",
                    "Plex",
                    "pi-hole")
            });

        throw new InvalidOperationException(
            "A mismatched application owner was accepted.");
    }
    catch (InvalidOperationException exception)
        when (exception.Message.Contains(
            "belongs to",
            StringComparison.OrdinalIgnoreCase))
    {
        // Expected.
    }
}

static void FleetNavigationResolvesRemoteApplicationOwner()
{
    var registry =
        new ApplicationRegistry();

    registry.ReplaceTargetInventory(
        "local-linux",
        new[]
        {
            CreateApplication(
                "plex-local",
                "Plex",
                "Plex",
                "local-linux") with
            {
                Metadata =
                    NavigationMetadata(
                        visible: true)
            }
        });

    registry.ReplaceTargetInventory(
        "pi-hole-old",
        new[]
        {
            CreateApplication(
                "pihole-old",
                "Pi-hole",
                "Pi-hole",
                "pi-hole-old") with
            {
                Metadata =
                    NavigationMetadata(
                        visible: true)
            }
        });

    registry.ReplaceTargetInventory(
        "pi-hole-current",
        new[]
        {
            CreateApplication(
                "pihole-current",
                "Pi-hole",
                "Pi-hole",
                "pi-hole-current") with
            {
                Metadata =
                    NavigationMetadata(
                        visible: true)
            }
        });

    registry.ReplaceTargetInventory(
        "hidden-target",
        new[]
        {
            CreateApplication(
                "pihole-hidden",
                "Pi-hole",
                "Pi-hole",
                "hidden-target") with
            {
                Metadata =
                    NavigationMetadata(
                        visible: false)
            }
        });

    var candidates =
        ApplicationNavigationResolver
            .FindCandidates(
                registry,
                "Pi-hole");

    Assert(
        candidates.Count == 2,
        "Fleet navigation did not preserve visible remote owners or excluded a hidden application.");

    var preferred =
        ApplicationNavigationResolver
            .ResolvePreferredOwner(
                registry,
                "Pi-hole",
                "local-linux",
                targetId =>
                    targetId.Equals(
                        "pi-hole-old",
                        StringComparison.OrdinalIgnoreCase));

    Assert(
        preferred?.OwnerTargetId ==
        "pi-hole-current",
        "Fleet navigation did not resolve the current remote application owner.");

    var activeOwner =
        ApplicationNavigationResolver
            .ResolvePreferredOwner(
                registry,
                "Pi-hole",
                "pi-hole-old",
                targetId =>
                    targetId.Equals(
                        "pi-hole-old",
                        StringComparison.OrdinalIgnoreCase));

    Assert(
        activeOwner?.OwnerTargetId ==
        "pi-hole-old",
        "Fleet navigation did not preserve an already-active application owner.");
}

static IReadOnlyDictionary<string, string>
    NavigationMetadata(
        bool visible) =>
    new Dictionary<string, string>(
        StringComparer.OrdinalIgnoreCase)
    {
        [ApplicationNavigationResolver
            .ShowInNavigationMetadataKey] =
            visible.ToString()
    };

static ApplicationInstance CreateApplication(
    string id,
    string productId,
    string displayName,
    string ownerTargetId) =>
    new(
        id,
        productId,
        displayName,
        ownerTargetId,
        ApplicationRole.WebApplication,
        ApplicationRuntimeKind.RemoteApi,
        new Uri("http://example.invalid"),
        TargetCapabilities.Empty);

static void SharedIdentityCatalogPreservesProductCategories()
{
    Assert(
        ApplicationIdentityCatalog.Find(
            "Plex")?.Category ==
        "Library",
        "The shared catalog lost the Plex category.");

    Assert(
        ApplicationIdentityCatalog.Find(
            "qBittorrent")?.Category ==
        "Acquisition",
        "The shared catalog lost the qBittorrent category.");

    Assert(
        ApplicationIdentityCatalog.ProductNames.Contains(
            "Pi-hole",
            StringComparer.OrdinalIgnoreCase),
        "The shared catalog lost Pi-hole.");
}

static void ClassifierDistinguishesPlexServerAndDesktop()
{
    var server =
        ApplicationIdentityClassifier.Classify(
            new ApplicationIdentityEvidence(
                "Plex",
                ApplicationIdentityRoles.NativeApplication,
                "systemd",
                "Native service",
                "plexmediaserver.service",
                "exact unit plexmediaserver.service",
                HasManagementEndpoint: true));

    var desktop =
        ApplicationIdentityClassifier.Classify(
            new ApplicationIdentityEvidence(
                "Plex",
                string.Empty,
                "Native process",
                "Desktop client",
                "Plex.exe",
                "local desktop process Plex.exe",
                HasManagementEndpoint: false));

    Assert(
        server.ProductId == "Plex" &&
        server.Role == ApplicationRole.Server &&
        server.Runtime ==
            ApplicationRuntimeKind.SystemdService,
        "Plex Media Server was not classified as a hosted server.");

    Assert(
        desktop.ProductId == "Plex" &&
        desktop.Role ==
            ApplicationRole.DesktopClient &&
        desktop.Runtime ==
            ApplicationRuntimeKind.NativeProcess,
        "Plex Desktop was not classified as a desktop client.");
}

static void ClassifierDistinguishesQBittorrentDesktopAndWebUi()
{
    var desktop =
        ApplicationIdentityClassifier.Classify(
            new ApplicationIdentityEvidence(
                "qBittorrent",
                string.Empty,
                "Native process",
                "GUI application",
                "qBittorrent.exe",
                "desktop process",
                HasManagementEndpoint: false));

    var web =
        ApplicationIdentityClassifier.Classify(
            new ApplicationIdentityEvidence(
                "qBittorrent",
                ApplicationIdentityRoles.NativeApplication,
                "Remote API",
                "Web UI",
                "qBittorrent Web UI",
                "remote API endpoint",
                HasManagementEndpoint: true));

    Assert(
        desktop.Role ==
            ApplicationRole.DesktopClient &&
        desktop.Runtime ==
            ApplicationRuntimeKind.NativeProcess,
        "qBittorrent Desktop was not classified as a desktop client.");

    Assert(
        web.Role ==
            ApplicationRole.WebApplication &&
        web.Runtime ==
            ApplicationRuntimeKind.RemoteApi,
        "qBittorrent Web UI was not classified as a remote Web application.");
}

static void ClassifierRecognizesContainerHostedApplications()
{
    var classification =
        ApplicationIdentityClassifier.Classify(
            new ApplicationIdentityEvidence(
                "Sonarr",
                ApplicationIdentityRoles.EmbeddedApplication,
                "Docker Compose",
                "Verified Arr API",
                "Sonarr TV",
                "container image linuxserver/sonarr",
                HasManagementEndpoint: true));

    Assert(
        classification.ProductId == "Sonarr",
        "The container classification changed the Sonarr product.");
    Assert(
        classification.Role ==
            ApplicationRole.WebApplication,
        "A container-hosted Arr application was not classified as a Web application.");
    Assert(
        classification.Runtime ==
            ApplicationRuntimeKind.Container,
        "A Docker Compose application was not classified as a container runtime.");
}

static void ClassifierCanonicalizesProductAliases()
{
    var classification =
        ApplicationIdentityClassifier.Classify(
            new ApplicationIdentityEvidence(
                string.Empty,
                ApplicationIdentityRoles.NativeApplication,
                "Docker",
                string.Empty,
                "linuxserver/sonarr",
                "container image linuxserver/sonarr",
                HasManagementEndpoint: false));

    Assert(
        classification.ProductId == "Sonarr",
        "A known container alias did not resolve to the canonical product.");
    Assert(
        classification.Category == "Acquisition",
        "Canonical alias classification returned the wrong category.");
}

static void ClassifierPreservesSupportingServiceIdentity()
{
    var classification =
        ApplicationIdentityClassifier.Classify(
            new ApplicationIdentityEvidence(
                "Plex",
                ApplicationIdentityRoles.SupportingService,
                "systemd",
                "Supporting dependency",
                "mullvad-plex-bypass.service",
                "systemd unit mullvad-plex-bypass.service",
                HasManagementEndpoint: false));

    Assert(
        classification.Role ==
            ApplicationRole.Service,
        "A supporting service was promoted to an application owner.");
    Assert(
        classification.Runtime ==
            ApplicationRuntimeKind.SystemdService,
        "The supporting service runtime was not preserved.");
}

static void TelemetryContractsArePlatformNeutral()
{
    var types =
        new[]
        {
            typeof(PlexTelemetrySnapshot),
            typeof(ArrLiveTelemetrySnapshot),
            typeof(DownloadClientTelemetrySnapshot),
            typeof(PiHoleTelemetrySnapshot),
            typeof(IApplicationTelemetrySnapshot),
            typeof(IApplicationTelemetryAdapter<,>)
        };

    Assert(
        types.All(type =>
            type.Namespace ==
            "GraveOps.Core.Telemetry"),
        "A shared telemetry contract remained in a platform namespace.");

    Assert(
        !types.Any(type =>
            type.Name.StartsWith(
                "Linux",
                StringComparison.OrdinalIgnoreCase) ||
            type.Name.StartsWith(
                "Windows",
                StringComparison.OrdinalIgnoreCase)),
        "A shared telemetry contract retained a platform prefix.");
}

static void TelemetryEnvelopePreservesOwnership()
{
    var sampledAt =
        DateTimeOffset.UtcNow;

    var snapshot =
        new PlexTelemetrySnapshot
        {
            State =
                "Online",
            SampledAt =
                sampledAt
        };

    var envelope =
        new ApplicationTelemetryEnvelope<
            PlexTelemetrySnapshot>(
            "local-linux",
            "plex-local",
            "Plex",
            ApplicationTelemetrySourceKind.Hybrid,
            sampledAt,
            snapshot,
            Array.Empty<string>());

    envelope.Validate();

    Assert(
        envelope.TargetId == "local-linux",
        "Telemetry target ownership changed.");
    Assert(
        envelope.ApplicationId == "plex-local",
        "Telemetry application ownership changed.");
    Assert(
        envelope.ProductId == "Plex",
        "Telemetry product identity changed.");
}

static void TelemetryEnvelopeDetectsStaleSnapshots()
{
    var now =
        DateTimeOffset.UtcNow;

    var snapshot =
        new DownloadClientTelemetrySnapshot
        {
            ClientKey =
                "qBittorrent",
            SampledAt =
                now.AddMinutes(-10)
        };

    var envelope =
        new ApplicationTelemetryEnvelope<
            DownloadClientTelemetrySnapshot>(
            "local-linux",
            "qbit-local",
            "qBittorrent",
            ApplicationTelemetrySourceKind.ApplicationApi,
            now,
            snapshot,
            Array.Empty<string>());

    Assert(
        envelope.IsStale(
            TimeSpan.FromMinutes(5),
            now),
        "An old telemetry snapshot was treated as current.");

    Assert(
        !envelope.IsStale(
            TimeSpan.FromMinutes(15),
            now),
        "A telemetry snapshot inside the freshness window was stale.");
}

static void TelemetryHealthClassifierNormalizesStates()
{
    Assert(
        ApplicationTelemetryHealthClassifier
            .FromState("ONLINE") ==
        ApplicationTelemetryHealth.Healthy,
        "ONLINE did not normalize to healthy.");

    Assert(
        ApplicationTelemetryHealthClassifier
            .FromState("blocking disabled") ==
        ApplicationTelemetryHealth.Warning,
        "Disabled blocking did not normalize to warning.");

    Assert(
        ApplicationTelemetryHealthClassifier
            .FromState("DNS OFFLINE") ==
        ApplicationTelemetryHealth.Error,
        "DNS offline did not normalize to error.");

    Assert(
        ApplicationTelemetryHealthClassifier
            .FromState(string.Empty) ==
        ApplicationTelemetryHealth.Unknown,
        "An empty state did not normalize to unknown.");
}

static void SharedTelemetrySnapshotsExposeCaptureTime()
{
    var capturedAt =
        DateTimeOffset.UtcNow;

    IApplicationTelemetrySnapshot plex =
        new PlexTelemetrySnapshot
        {
            SampledAt =
                capturedAt
        };

    IApplicationTelemetrySnapshot download =
        new DownloadClientTelemetrySnapshot
        {
            SampledAt =
                capturedAt
        };

    IApplicationTelemetrySnapshot arr =
        new ArrLiveTelemetrySnapshot(
            capturedAt,
            Array.Empty<ArrServiceTelemetryRow>(),
            Array.Empty<ArrWorkItemRow>(),
            "ONLINE",
            "--",
            "0",
            "0");

    Assert(
        plex.CapturedAt == capturedAt,
        "Plex capture time was not exposed through the shared contract.");
    Assert(
        download.CapturedAt == capturedAt,
        "Download-client capture time was not exposed through the shared contract.");
    Assert(
        arr.CapturedAt == capturedAt,
        "Arr capture time was not exposed through the shared contract.");
}

static void PiHoleTelemetryFormatsSharedMetrics()
{
    var snapshot =
        new PiHoleTelemetrySnapshot(
            DateTimeOffset.UtcNow,
            ApplicationTelemetryHealth.Healthy,
            "HEALTHY",
            DnsOnline: true,
            BlockingEnabled: true,
            CoreVersion: "v6",
            WebVersion: "v6",
            FtlVersion: "v6",
            Host: "raspberrypi",
            Uptime: "2 days",
            Load: "0.2",
            Temperature: "40.0 °C",
            Queries: 1000,
            Blocked: 250,
            PercentBlocked: 25.0,
            ActiveClients: 12,
            TotalClients: 20,
            QueryRate: 1.5,
            GravityDomains: 200000,
            GravityUpdatedAt:
                DateTimeOffset.Now.AddHours(-2),
            WebUrl:
                "http://example.invalid/admin",
            RawEvidence:
                string.Empty);

    Assert(
        snapshot.QueriesText.Contains(
            "1",
            StringComparison.Ordinal),
        "Pi-hole query formatting returned no value.");
    Assert(
        snapshot.PercentBlockedText == "25.0%",
        "Pi-hole blocked percentage formatting changed.");
    Assert(
        snapshot.ClientText.Contains(
            "12",
            StringComparison.Ordinal) &&
        snapshot.ClientText.Contains(
            "20",
            StringComparison.Ordinal),
        "Pi-hole client formatting changed.");
}

static void ApplicationCacheOmitsEndpointsAndSecrets()
{
    var directory =
        TemporaryContractDirectory();
    var path =
        Path.Combine(
            directory,
            "application-inventory-cache.json");

    try
    {
        var store =
            new ApplicationInventoryCacheStore(path);

        var application =
            new ApplicationInstance(
                "plex-local",
                "Plex",
                "Plex Media Server",
                "local-linux",
                ApplicationRole.Server,
                ApplicationRuntimeKind.SystemdService,
                new Uri(
                    "http://user:password@192.168.0.2:32400/web?token=do-not-store"),
                new TargetCapabilities(
                    new[]
                    {
                        CapabilityIds.ApplicationDiscovery
                    }),
                new Dictionary<string, string>
                {
                    ["category"] =
                        "Library",
                    ["token"] =
                        "do-not-store",
                    ["evidence"] =
                        "raw command output",
                    ["protocol"] =
                        "Native service"
                });

        store.Save(
            new[]
            {
                ApplicationInventoryCacheStore.CreateTarget(
                    "local-linux",
                    "Local Linux",
                    DateTimeOffset.UtcNow,
                    application.Capabilities,
                    new[]
                    {
                        application
                    })
            });

        var json =
            File.ReadAllText(path);

        Assert(
            !json.Contains(
                "192.168.0.2",
                StringComparison.OrdinalIgnoreCase),
            "The cache retained an endpoint host.");
        Assert(
            !json.Contains(
                "password",
                StringComparison.OrdinalIgnoreCase),
            "The cache retained endpoint credentials.");
        Assert(
            !json.Contains(
                "do-not-store",
                StringComparison.OrdinalIgnoreCase),
            "The cache retained a secret value.");
        Assert(
            !json.Contains(
                "raw command output",
                StringComparison.OrdinalIgnoreCase),
            "The cache retained arbitrary evidence.");
        Assert(
            json.Contains(
                "Library",
                StringComparison.Ordinal),
            "The cache removed an allowlisted category.");
    }
    finally
    {
        Directory.Delete(
            directory,
            recursive: true);
    }
}

static void ApplicationCacheRoundTripsSafeOwnership()
{
    var directory =
        TemporaryContractDirectory();
    var path =
        Path.Combine(
            directory,
            "application-inventory-cache.json");

    try
    {
        var store =
            new ApplicationInventoryCacheStore(path);
        var capabilities =
            new TargetCapabilities(
                new[]
                {
                    CapabilityIds.ApplicationDiscovery,
                    CapabilityIds.StorageRead
                });
        var application =
            new ApplicationInstance(
                "pihole-remote",
                "Pi-hole",
                "Pi-hole",
                "pi-hole",
                ApplicationRole.WebApplication,
                ApplicationRuntimeKind.RemoteApi,
                new Uri(
                    "http://192.168.0.210/admin"),
                capabilities,
                new Dictionary<string, string>
                {
                    ["category"] =
                        "Network",
                    ["verified"] =
                        "True",
                    ["visible"] =
                        "True"
                });

        store.Save(
            new[]
            {
                ApplicationInventoryCacheStore.CreateTarget(
                    "pi-hole",
                    "Pi-hole",
                    DateTimeOffset.UtcNow,
                    capabilities,
                    new[]
                    {
                        application
                    })
            });

        var loaded =
            store.Load();

        Assert(
            loaded.Warnings.Count == 0,
            "A valid cache produced warnings.");
        Assert(
            loaded.Document.Targets.Count == 1,
            "The target inventory did not round trip.");

        var target =
            loaded.Document.Targets[0];
        var cached =
            target.Applications.Single();

        Assert(
            cached.OwnerTargetId == "pi-hole",
            "Cached application ownership changed.");
        Assert(
            target.CapabilityIds.Contains(
                CapabilityIds.StorageRead,
                StringComparer.OrdinalIgnoreCase),
            "Cached target capabilities changed.");

        var restored =
            ApplicationInventoryCacheStore
                .ToApplicationInstance(
                    cached,
                    new TargetCapabilities(
                        target.CapabilityIds));

        Assert(
            restored.OwnerTargetId == "pi-hole",
            "Restored application ownership changed.");
        Assert(
            restored.ManagementEndpoint is null,
            "A cached endpoint was restored as trusted state.");
    }
    finally
    {
        Directory.Delete(
            directory,
            recursive: true);
    }
}

static void ApplicationCacheIgnoresMalformedDocuments()
{
    var directory =
        TemporaryContractDirectory();
    var path =
        Path.Combine(
            directory,
            "application-inventory-cache.json");

    try
    {
        File.WriteAllText(
            path,
            "{ definitely not valid json");

        var store =
            new ApplicationInventoryCacheStore(path);
        var loaded =
            store.Load();

        Assert(
            loaded.Document.Targets.Count == 0,
            "A malformed cache produced target inventory.");
        Assert(
            loaded.Warnings.Count == 1,
            "A malformed cache did not report one warning.");
    }
    finally
    {
        Directory.Delete(
            directory,
            recursive: true);
    }
}

static string TemporaryContractDirectory()
{
    var directory =
        Path.Combine(
            Path.GetTempPath(),
            "graveops-contract-" +
            Guid.NewGuid().ToString("N"));

    Directory.CreateDirectory(directory);
    return directory;
}

static void SecretValuesRedactAndDispose()
{
    using var secret = new SecretValue("do-not-log-this");
    Assert(secret.ToString() == "[REDACTED]",
        "Secret ToString exposed its value.");
    Assert(new string(secret.Reveal().Span) == "do-not-log-this",
        "Secret retrieval returned the wrong value.");

    secret.Dispose();

    try
    {
        _ = secret.Reveal();
        throw new InvalidOperationException(
            "Disposed secret remained readable.");
    }
    catch (ObjectDisposedException)
    {
        // Expected.
    }
}

static void Assert(bool condition, string message)
{
    if (!condition)
        throw new InvalidOperationException(message);
}

file sealed class FakeProvider : IHostProvider
{
    private readonly Func<TargetProfile, bool> _canHandle;

    public FakeProvider(
        string id,
        Func<TargetProfile, bool> canHandle)
    {
        _canHandle = canHandle;
        Descriptor = new HostProviderDescriptor(
            id,
            id,
            new HashSet<TargetPlatform>(),
            new HashSet<TargetLocation>());
    }

    public HostProviderDescriptor Descriptor { get; }

    public bool CanHandle(TargetProfile target) =>
        _canHandle(target);

    public Task<HostProviderProbeResult> ProbeAsync(
        TargetProfile target,
        CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();

    public Task<GraveOps.Core.Snapshots.TargetSnapshotEnvelope<
        GraveOps.Core.Hosts.HostSnapshot>> CaptureAsync(
        TargetProfile target,
        TargetRefreshLease refreshLease,
        CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();
}
