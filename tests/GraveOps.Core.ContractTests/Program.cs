using GraveOps.Core.Applications;
using GraveOps.Core.Providers;
using GraveOps.Core.Security;
using GraveOps.Core.Snapshots;
using GraveOps.Core.Targets;

var tests = new (string Name, Action Run)[]
{
    ("target switching rejects earlier selection", TargetSwitchingRejectsEarlierSelection),
    ("new refresh rejects older refresh", NewRefreshRejectsOlderRefresh),
    ("capabilities are case insensitive", CapabilitiesAreCaseInsensitive),
    ("provider registry resolves by target", ProviderRegistryResolvesByTarget),
    ("application requires owner target", ApplicationRequiresOwnerTarget),
    ("application registry preserves other target inventories", ApplicationRegistryPreservesOtherTargets),
    ("application registry resolves and enforces owner target", ApplicationRegistryResolvesAndEnforcesOwner),
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
