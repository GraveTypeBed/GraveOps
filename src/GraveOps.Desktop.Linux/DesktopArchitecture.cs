using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace GraveOps.Desktop.Linux;

public enum DesktopSubsystemKind
{
    PlatformHardening,
    ProductCatalog,
    Navigation,
    HealthPolicy,
    SignalPersistence,
    RemediationPolicy,
    RemediationPersistence,
    UiProjection,
    ProjectionTelemetry
}

public sealed record DesktopSubsystemDescriptor(
    string Name,
    DesktopSubsystemKind Kind,
    IReadOnlyList<string> Dependencies,
    string Responsibility);

public sealed record DesktopArchitectureSnapshot(
    bool Started,
    int SubsystemCount,
    int ProductContractCount,
    int RemediationContractCount,
    IReadOnlyList<DesktopSubsystemDescriptor> Subsystems,
    PlatformHardeningSnapshot Hardening)
{
    public string Summary =>
        $"{SubsystemCount} isolated subsystem boundaries · " +
        $"{ProductContractCount} product contracts · " +
        $"{RemediationContractCount} remediation contracts · " +
        $"composition {(Started ? "active" : "not started")} · " +
        Hardening.Summary;
}

public interface IPlatformHardeningPort : IDisposable
{
    string AuditPath { get; }
    string CrashLogPath { get; }
    PlatformHardeningSnapshot Snapshot { get; }
    void Start();
    string Redact(string? value, int maxCharacters = 131072);
    string SanitizeException(Exception exception);
    Task<HardenedProcessResult> RunShellAsync(
        string command,
        TimeSpan timeout,
        CancellationToken cancellationToken = default);
}

public interface IProductOperationsPort
{
    IReadOnlyList<ProductOperationalContract> All { get; }
    ProductOperationalContract? Find(string value);
    bool Supports(string value);
    bool AllowsExitedPrimary(string identity, string evidence);
    string ResolveEndpoint(string product, string suppliedEndpoint, string evidence);
    bool EndpointVerificationSucceeded(string product, string output);
    string ExpectedResult(string product);
    string CoverageSummary();
}

public interface INavigationPort
{
    string ForProduct(string product);
    string Resolve(string product, string fallback);
}

public interface IHealthPolicyPort
{
    string Summary(int excludedGroups);
    IReadOnlyList<SignalQualityObservation> Evaluate(
        IReadOnlyList<OpsIntegration> integrations,
        SignalQualityRefreshState refresh,
        SignalQualitySettings settings,
        DateTimeOffset now);
    OpsAnalysis MergeAnalysis(
        OpsAnalysis analysis,
        IReadOnlyList<SignalQualityObservation> observations,
        SignalQualitySettings settings,
        IReadOnlyList<OpsIntegration> integrations);
    IReadOnlyList<UnifiedDashboardCard> ApplyCards(
        IReadOnlyList<UnifiedDashboardCard> cards,
        SignalQualityDashboardContext context);
    string ExpectationKey(OpsIntegration integration);
    string FormatAge(TimeSpan age);
}

public interface ISignalQualityStorePort
{
    string FilePath { get; }
    SignalQualitySettings GetSettings();
    void SetSettings(SignalQualitySettings settings);
    SignalQualityRefreshState GetRefreshState(string hostId);
    void MarkRefreshSuccess(string hostId, DateTimeOffset timestamp);
    void MarkRefreshFailure(
        string hostId,
        DateTimeOffset timestamp,
        string failure);
    IReadOnlyList<SignalQualityTransition> Reconcile(
        string hostId,
        long generation,
        IReadOnlyList<SignalQualityObservation> observations,
        DateTimeOffset timestamp);
    IReadOnlyList<SignalQualityIncident> ActiveIncidents(string hostId);
    IReadOnlyList<SignalQualityIncident> RecentRecoveries(string hostId);
}

public interface IRemediationPolicyPort
{
    IReadOnlyList<VerifiedRemediationProduct> Catalog { get; }
    IReadOnlyList<UnifiedDashboardCard> AttachActions(
        IReadOnlyList<UnifiedDashboardCard> cards,
        IReadOnlyList<OpsIntegration> integrations,
        out IReadOnlyDictionary<string, VerifiedRemediationPlan> plans);
    string MutationBlockReason(
        VerifiedRemediationPlan plan,
        VerifiedRemediationSettings settings,
        bool storageFaultActive);
    bool VerificationSucceeded(
        VerifiedRemediationPlan plan,
        int exitCode,
        string output);
}

public interface IRemediationStorePort
{
    string FilePath { get; }
    VerifiedRemediationSettings GetSettings();
    void SetSettings(VerifiedRemediationSettings settings);
    IReadOnlyList<VerifiedRemediationJob> RecentJobs(string hostId);
    bool TryStart(
        VerifiedRemediationPlan plan,
        string hostId,
        out VerifiedRemediationJob job);
    VerifiedRemediationJob Update(
        string jobId,
        VerifiedRemediationJobState state,
        string output = "",
        string verification = "",
        bool verified = false);
}

public interface IUiProjectionPort : IDisposable
{
    UiDataPipelineSettings Settings { get; }
    string SettingsPath { get; }
    string MetricsPath { get; }
    long Generation { get; }
    long BeginRefresh();
    void SetSettings(UiDataPipelineSettings settings);
    bool Project(
        UiProjectionArea area,
        string key,
        string signature,
        int itemCount,
        Action apply,
        bool force = false);
    UiProjectionSummary Summary();
    bool FlushMetrics(TimeSpan? timeout = null);
    void Invalidate(string? scope = null);
    string Signature(IEnumerable<string?> values);
}

public sealed class GraveOpsDesktopArchitecture : IDisposable
{
    private readonly object _gate = new();
    private bool _started;
    private bool _disposed;

    private GraveOpsDesktopArchitecture(
        IPlatformHardeningPort platformHardening,
        IProductOperationsPort products,
        INavigationPort navigation,
        IHealthPolicyPort health,
        ISignalQualityStorePort signalQualityStore,
        IRemediationPolicyPort remediation,
        IRemediationStorePort remediationStore,
        IUiProjectionPort uiProjection)
    {
        PlatformHardening = platformHardening;
        Products = products;
        Navigation = navigation;
        Health = health;
        SignalQualityStore = signalQualityStore;
        Remediation = remediation;
        RemediationStore = remediationStore;
        UiProjection = uiProjection;
        Subsystems = BuildSubsystems();
    }

    public IPlatformHardeningPort PlatformHardening { get; }
    public IProductOperationsPort Products { get; }
    public INavigationPort Navigation { get; }
    public IHealthPolicyPort Health { get; }
    public ISignalQualityStorePort SignalQualityStore { get; }
    public IRemediationPolicyPort Remediation { get; }
    public IRemediationStorePort RemediationStore { get; }
    public IUiProjectionPort UiProjection { get; }
    public IReadOnlyList<DesktopSubsystemDescriptor> Subsystems { get; }
    public bool IsStarted => _started;

    public static GraveOpsDesktopArchitecture CreateDefault() =>
        Create(
            configRoot: null,
            cacheRoot: null);

    public static GraveOpsDesktopArchitecture CreateIsolated(
        string configRoot,
        string cacheRoot) =>
        Create(configRoot, cacheRoot);

    public void Start()
    {
        lock (_gate)
        {
            ThrowIfDisposed();
            if (_started)
                return;
            PlatformHardening.Start();
            ValidateComposition();
            _started = true;
        }
    }

    public DesktopArchitectureSnapshot Snapshot() =>
        new(
            _started,
            Subsystems.Count,
            Products.All.Count,
            Remediation.Catalog.Count,
            Subsystems.ToArray(),
            PlatformHardening.Snapshot);

    public string Summary() => Snapshot().Summary;

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed)
                return;
            _disposed = true;
            try
            {
                UiProjection.Dispose();
            }
            finally
            {
                PlatformHardening.Dispose();
            }
        }
    }

    private static GraveOpsDesktopArchitecture Create(
        string? configRoot,
        string? cacheRoot)
    {
        var hardening = new PlatformHardeningService(
            configRoot,
            cacheRoot);
        hardening.Start();
        var products = new ProductOperationsAdapter();
        return new GraveOpsDesktopArchitecture(
            hardening,
            products,
            new NavigationAdapter(),
            new HealthPolicyAdapter(),
            new SignalQualityStoreAdapter(
                new SignalQualityPolicyStore(configRoot)),
            new RemediationPolicyAdapter(),
            new RemediationStoreAdapter(
                new VerifiedRemediationStore(configRoot)),
            new UiProjectionAdapter(
                new UiDataPipeline(
                    new UiDataPipelineStore(
                        configRoot,
                        cacheRoot))));
    }

    private void ValidateComposition()
    {
        var names = Subsystems
            .Select(item => item.Name)
            .ToHashSet(StringComparer.Ordinal);
        if (names.Count != Subsystems.Count)
            throw new InvalidOperationException(
                "Desktop subsystem names must be unique.");

        foreach (var subsystem in Subsystems)
        {
            var missing = subsystem.Dependencies
                .Where(dependency => !names.Contains(dependency))
                .ToArray();
            if (missing.Length > 0)
            {
                throw new InvalidOperationException(
                    $"Subsystem '{subsystem.Name}' has missing dependencies: " +
                    string.Join(", ", missing));
            }
        }

        DetectDependencyCycles(Subsystems);
        if (!PlatformHardening.Snapshot.Started)
        {
            throw new InvalidOperationException(
                "Platform hardening must start before persistence stores.");
        }
        if (Products.All.Count == 0 ||
            Products.All.Count != Remediation.Catalog.Count)
        {
            throw new InvalidOperationException(
                "Product and remediation contract counts do not match.");
        }
        foreach (var required in new[]
                 {
                     "Sonarr", "Readarr", "Whisparr",
                     "Plex", "Jellyfin", "Emby"
                 })
        {
            if (!Products.Supports(required))
            {
                throw new InvalidOperationException(
                    $"Required product contract is missing: {required}");
            }
        }
    }

    private static void DetectDependencyCycles(
        IReadOnlyList<DesktopSubsystemDescriptor> subsystems)
    {
        var map = subsystems.ToDictionary(
            item => item.Name,
            StringComparer.Ordinal);
        var visiting = new HashSet<string>(StringComparer.Ordinal);
        var visited = new HashSet<string>(StringComparer.Ordinal);

        void Visit(string name)
        {
            if (visited.Contains(name))
                return;
            if (!visiting.Add(name))
            {
                throw new InvalidOperationException(
                    $"Desktop subsystem dependency cycle detected at '{name}'.");
            }
            foreach (var dependency in map[name].Dependencies)
                Visit(dependency);
            visiting.Remove(name);
            visited.Add(name);
        }

        foreach (var name in map.Keys)
            Visit(name);
    }

    private static IReadOnlyList<DesktopSubsystemDescriptor>
        BuildSubsystems() =>
        new[]
        {
            new DesktopSubsystemDescriptor(
                "Platform hardening",
                DesktopSubsystemKind.PlatformHardening,
                Array.Empty<string>(),
                "Corrupt-state recovery, private persistence, redaction, bounded process execution and crash evidence."),
            new DesktopSubsystemDescriptor(
                "Product contracts",
                DesktopSubsystemKind.ProductCatalog,
                Array.Empty<string>(),
                "Detection aliases, runtime ownership, endpoints and product capabilities."),
            new DesktopSubsystemDescriptor(
                "Navigation resolution",
                DesktopSubsystemKind.Navigation,
                new[] { "Product contracts" },
                "Product-aware workspace routing without view-owned catalog switches."),
            new DesktopSubsystemDescriptor(
                "Health policy",
                DesktopSubsystemKind.HealthPolicy,
                new[] { "Product contracts", "Navigation resolution" },
                "Expected ownership, stale data, severity and recovery semantics."),
            new DesktopSubsystemDescriptor(
                "Signal persistence",
                DesktopSubsystemKind.SignalPersistence,
                new[] { "Platform hardening", "Health policy" },
                "Signal settings, refresh state, incident identity and recovery history."),
            new DesktopSubsystemDescriptor(
                "Remediation policy",
                DesktopSubsystemKind.RemediationPolicy,
                new[] { "Product contracts", "Health policy" },
                "Inspection, recovery, blocking and post-action verification."),
            new DesktopSubsystemDescriptor(
                "Remediation persistence",
                DesktopSubsystemKind.RemediationPersistence,
                new[] { "Platform hardening", "Remediation policy" },
                "Safe-mode settings, duplicate jobs and verified action history."),
            new DesktopSubsystemDescriptor(
                "UI projection",
                DesktopSubsystemKind.UiProjection,
                new[] { "Navigation resolution", "Health policy", "Remediation policy" },
                "Stable signatures, keyed reconciliation and bounded page projection."),
            new DesktopSubsystemDescriptor(
                "Projection telemetry",
                DesktopSubsystemKind.ProjectionTelemetry,
                new[] { "Platform hardening", "UI projection" },
                "Bounded projection timing and regression evidence."),
        };

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
    }
}

internal sealed class ProductOperationsAdapter : IProductOperationsPort
{
    public IReadOnlyList<ProductOperationalContract> All =>
        ProductOperationalCatalog.All;

    public ProductOperationalContract? Find(string value) =>
        ProductOperationalCatalog.Find(value);

    public bool Supports(string value) =>
        ProductOperationalCatalog.Supports(value);

    public bool AllowsExitedPrimary(string identity, string evidence) =>
        ProductOperationalCatalog.AllowsExitedPrimary(identity, evidence);

    public string ResolveEndpoint(
        string product,
        string suppliedEndpoint,
        string evidence) =>
        ProductOperationalCatalog.ResolveEndpoint(
            product,
            suppliedEndpoint,
            evidence);

    public bool EndpointVerificationSucceeded(
        string product,
        string output) =>
        ProductOperationalCatalog.EndpointVerificationSucceeded(
            product,
            output);

    public string ExpectedResult(string product) =>
        ProductOperationalCatalog.ExpectedResult(product);

    public string CoverageSummary() =>
        ProductOperationalCatalog.CoverageSummary();
}

internal sealed class NavigationAdapter : INavigationPort
{
    public string ForProduct(string product) =>
        ProductOperationalCatalog.NavigationFor(product);

    public string Resolve(string product, string fallback) =>
        ProductOperationalCatalog.ResolveNavigation(product, fallback);
}

internal sealed class HealthPolicyAdapter : IHealthPolicyPort
{
    public string Summary(int excludedGroups) =>
        SignalQualityPolicy.Summary(excludedGroups);

    public IReadOnlyList<SignalQualityObservation> Evaluate(
        IReadOnlyList<OpsIntegration> integrations,
        SignalQualityRefreshState refresh,
        SignalQualitySettings settings,
        DateTimeOffset now) =>
        SignalQualityPolicy.Evaluate(
            integrations,
            refresh,
            settings,
            now);

    public OpsAnalysis MergeAnalysis(
        OpsAnalysis analysis,
        IReadOnlyList<SignalQualityObservation> observations,
        SignalQualitySettings settings,
        IReadOnlyList<OpsIntegration> integrations) =>
        SignalQualityPolicy.MergeAnalysis(
            analysis,
            observations,
            settings,
            integrations);

    public IReadOnlyList<UnifiedDashboardCard> ApplyCards(
        IReadOnlyList<UnifiedDashboardCard> cards,
        SignalQualityDashboardContext context) =>
        SignalQualityPolicy.ApplyCards(cards, context);

    public string ExpectationKey(OpsIntegration integration) =>
        SignalQualityPolicy.ExpectationKey(integration);

    public string FormatAge(TimeSpan age) =>
        SignalQualityPolicy.FormatAge(age);
}

internal sealed class SignalQualityStoreAdapter : ISignalQualityStorePort
{
    private readonly SignalQualityPolicyStore _store;

    public SignalQualityStoreAdapter(SignalQualityPolicyStore store)
    {
        _store = store;
    }

    public string FilePath => _store.FilePath;
    public SignalQualitySettings GetSettings() => _store.GetSettings();
    public void SetSettings(SignalQualitySettings settings) =>
        _store.SetSettings(settings);
    public SignalQualityRefreshState GetRefreshState(string hostId) =>
        _store.GetRefreshState(hostId);
    public void MarkRefreshSuccess(string hostId, DateTimeOffset timestamp) =>
        _store.MarkRefreshSuccess(hostId, timestamp);
    public void MarkRefreshFailure(
        string hostId,
        DateTimeOffset timestamp,
        string failure) =>
        _store.MarkRefreshFailure(hostId, timestamp, failure);
    public IReadOnlyList<SignalQualityTransition> Reconcile(
        string hostId,
        long generation,
        IReadOnlyList<SignalQualityObservation> observations,
        DateTimeOffset timestamp) =>
        _store.Reconcile(hostId, generation, observations, timestamp);
    public IReadOnlyList<SignalQualityIncident> ActiveIncidents(
        string hostId) =>
        _store.ActiveIncidents(hostId);
    public IReadOnlyList<SignalQualityIncident> RecentRecoveries(
        string hostId) =>
        _store.RecentRecoveries(hostId);
}

internal sealed class RemediationPolicyAdapter : IRemediationPolicyPort
{
    public IReadOnlyList<VerifiedRemediationProduct> Catalog =>
        VerifiedRemediationPolicy.Catalog;

    public IReadOnlyList<UnifiedDashboardCard> AttachActions(
        IReadOnlyList<UnifiedDashboardCard> cards,
        IReadOnlyList<OpsIntegration> integrations,
        out IReadOnlyDictionary<string, VerifiedRemediationPlan> plans) =>
        VerifiedRemediationPolicy.AttachActions(
            cards,
            integrations,
            out plans);

    public string MutationBlockReason(
        VerifiedRemediationPlan plan,
        VerifiedRemediationSettings settings,
        bool storageFaultActive) =>
        VerifiedRemediationPolicy.MutationBlockReason(
            plan,
            settings,
            storageFaultActive);

    public bool VerificationSucceeded(
        VerifiedRemediationPlan plan,
        int exitCode,
        string output) =>
        VerifiedRemediationPolicy.VerificationSucceeded(
            plan,
            exitCode,
            output);
}

internal sealed class RemediationStoreAdapter : IRemediationStorePort
{
    private readonly VerifiedRemediationStore _store;

    public RemediationStoreAdapter(VerifiedRemediationStore store)
    {
        _store = store;
    }

    public string FilePath => _store.FilePath;
    public VerifiedRemediationSettings GetSettings() => _store.GetSettings();
    public void SetSettings(VerifiedRemediationSettings settings) =>
        _store.SetSettings(settings);
    public IReadOnlyList<VerifiedRemediationJob> RecentJobs(string hostId) =>
        _store.RecentJobs(hostId);
    public bool TryStart(
        VerifiedRemediationPlan plan,
        string hostId,
        out VerifiedRemediationJob job) =>
        _store.TryStart(plan, hostId, out job);
    public VerifiedRemediationJob Update(
        string jobId,
        VerifiedRemediationJobState state,
        string output = "",
        string verification = "",
        bool verified = false) =>
        _store.Update(jobId, state, output, verification, verified);
}

internal sealed class UiProjectionAdapter : IUiProjectionPort
{
    private readonly UiDataPipeline _pipeline;

    public UiProjectionAdapter(UiDataPipeline pipeline)
    {
        _pipeline = pipeline;
    }

    public UiDataPipelineSettings Settings => _pipeline.Settings;
    public string SettingsPath => _pipeline.SettingsPath;
    public string MetricsPath => _pipeline.MetricsPath;
    public long Generation => _pipeline.Generation;
    public long BeginRefresh() => _pipeline.BeginRefresh();
    public void SetSettings(UiDataPipelineSettings settings) =>
        _pipeline.SetSettings(settings);
    public bool Project(
        UiProjectionArea area,
        string key,
        string signature,
        int itemCount,
        Action apply,
        bool force = false) =>
        _pipeline.Project(
            area,
            key,
            signature,
            itemCount,
            apply,
            force);
    public UiProjectionSummary Summary() => _pipeline.Summary();
    public bool FlushMetrics(TimeSpan? timeout = null) =>
        _pipeline.FlushMetrics(timeout);
    public void Invalidate(string? scope = null) =>
        _pipeline.Invalidate(scope);
    public string Signature(IEnumerable<string?> values) =>
        UiDataPipeline.Signature(values);
    public void Dispose() => _pipeline.Dispose();
}
