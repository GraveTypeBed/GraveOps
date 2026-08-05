using GraveOps.Core.Hosts;
using GraveOps.Core.Providers;
using GraveOps.Core.Security;
using GraveOps.Core.Snapshots;
using GraveOps.Core.Targets;
using GraveOps.Desktop.Windows;
using GraveOps.Platform.Windows;

var tests =
    new (string Name, Func<Task> Run)[]
    {
        (
            "target registry creates the required local Windows target",
            LocalTargetDefaultAsync),

        (
            "target registry round trips redacted remote Windows profiles",
            RegistryRoundTripAsync),

        (
            "active target selection persists and falls back safely",
            ActiveTargetPersistenceAsync),

        (
            "target selector rows are ordered and redacted",
            TargetProjectionAsync),

        (
            "remote target editor validates and normalizes drafts",
            TargetEditorPolicyAsync),

        (
            "target removal requires a matching second confirmation",
            TargetRemovalConfirmationAsync),

        (
            "target creation rejects duplicate and reserved identities",
            TargetCreationIdentityAsync),

        (
            "target session stores credentials through its configured vault",
            TargetSessionCredentialAsync),

        (
            "Windows composition resolves local and remote providers",
            ProviderCompositionAsync),

        (
            "remote target factory validates WinRM HTTPS options",
            RemoteFactoryAsync),

        (
            "target session rejects stale captures after target switch",
            StaleCaptureAsync),

        (
            "active target edits invalidate stale captures",
            ActiveTargetEditAsync),

        (
            "target deletion falls back to local Windows",
            DeleteFallbackAsync),

        (
            "navigation follows reported target capabilities",
            NavigationPolicyAsync),

        (
            "Windows Credential Manager adapter reports runtime availability",
            CredentialVaultAvailabilityAsync),

        (
            "Windows Credential Manager round trips a temporary secret",
            CredentialVaultRoundTripAsync)
    };

var failures = 0;

foreach (var test in tests)
{
    try
    {
        await test.Run();
        Console.WriteLine(
            $"PASS: {test.Name}");
    }
    catch (Exception exception)
    {
        failures++;

        Console.WriteLine(
            $"FAIL: {test.Name}");

        Console.WriteLine(
            exception);
    }
}

if (failures > 0)
{
    Console.WriteLine(
        $"{failures} Windows target-management test(s) failed.");

    Environment.ExitCode = 1;
}
else
{
    Console.WriteLine(
        $"All {tests.Length} Windows target-management tests passed.");
}

static async Task LocalTargetDefaultAsync()
{
    await WithStoresAsync(
        async (
            registry,
            activeStore) =>
        {
            var session =
                new WindowsTargetSession(
                    registry,
                    new HostProviderRegistry(
                        new IHostProvider[]
                        {
                            new LocalWindowsHostProvider()
                        }),
                    activeStore);

            var targets =
                await session.InitializeAsync();

            var local =
                targets.Single(
                    target =>
                        target.Id.Equals(
                            WindowsTargetCatalog.LocalTargetId,
                            StringComparison.Ordinal));

            Equal(
                HostProviderIds.LocalWindows,
                local.ProviderId,
                "local provider ID");

            Equal(
                TransportIds.Local,
                local.Connection.TransportId,
                "local transport");

            Equal(
                local.Id,
                session.SelectedTarget?.Id,
                "default selection");

            Equal(
                local.Id,
                await activeStore.LoadAsync(),
                "persisted default selection");
        });
}

static async Task RegistryRoundTripAsync()
{
    await WithStoresAsync(
        async (
            registry,
            _) =>
        {
            var remote =
                WindowsTargetCatalog.CreateRemote(
                    "media-server",
                    "Media Server",
                    "server.example.test",
                    5986,
                    "graveops",
                    WindowsRemoteAuthentication.Negotiate,
                    90);

            await registry.UpsertAsync(
                remote);

            var loaded =
                await registry.FindAsync(
                    remote.Id);

            EqualTargetProfile(
                remote,
                loaded,
                "registry round trip");

            var json =
                await File.ReadAllTextAsync(
                    registry.FilePath);

            True(
                json.Contains(
                    WindowsTargetCatalog.CredentialReferenceFor(
                        remote.Id),
                    StringComparison.Ordinal),
                "credential reference persisted");

            True(
                !json.Contains(
                    "example-password",
                    StringComparison.Ordinal),
                "plaintext secret absent");
        });
}

static async Task ActiveTargetPersistenceAsync()
{
    await WithStoresAsync(
        async (
            registry,
            activeStore) =>
        {
            var remote =
                TestTarget(
                    "remember-me");

            await registry.UpsertAsync(
                remote);

            var providers =
                new HostProviderRegistry(
                    new IHostProvider[]
                    {
                        new DelayedHostProvider()
                    });

            var firstSession =
                new WindowsTargetSession(
                    registry,
                    providers,
                    activeStore);

            await firstSession.InitializeAsync();
            await firstSession.SelectAsync(
                remote.Id);

            var secondSession =
                new WindowsTargetSession(
                    registry,
                    providers,
                    new JsonActiveTargetStore(
                        activeStore.FilePath));

            await secondSession.InitializeAsync();

            Equal(
                remote.Id,
                secondSession.SelectedTarget?.Id,
                "restored active target");

            await registry.RemoveAsync(
                remote.Id);

            var fallbackStore =
                new JsonActiveTargetStore(
                    activeStore.FilePath);

            var thirdSession =
                new WindowsTargetSession(
                    registry,
                    providers,
                    fallbackStore);

            await thirdSession.InitializeAsync();

            Equal(
                WindowsTargetCatalog.LocalTargetId,
                thirdSession.SelectedTarget?.Id,
                "missing active target falls back local");

            Equal(
                WindowsTargetCatalog.LocalTargetId,
                await fallbackStore.LoadAsync(),
                "fallback selection persisted");
        });
}

static Task TargetProjectionAsync()
{
    var local =
        WindowsTargetCatalog.CreateLocal();

    var remote =
        WindowsTargetCatalog.CreateRemote(
            "projection-remote",
            "Projection Remote",
            "server.example.test",
            5986,
            "graveops-user",
            WindowsRemoteAuthentication.Negotiate,
            60);

    var rows =
        WindowsTargetUiProjection.CreateRows(
            new[]
            {
                remote,
                local
            });

    Equal(
        2,
        rows.Count,
        "target row count");

    Equal(
        local.Id,
        rows[0].TargetId,
        "local target sorted first");

    var remoteRow =
        rows.Single(
            row =>
                row.TargetId.Equals(
                    remote.Id,
                    StringComparison.Ordinal));

    True(
        remoteRow.ConnectionSummary.Contains(
            "server.example.test:5986",
            StringComparison.Ordinal),
        "remote endpoint shown");

    True(
        remoteRow.ConnectionSummary.Contains(
            "WinRM HTTPS",
            StringComparison.Ordinal),
        "remote transport shown");

    True(
        !remoteRow.ConnectionSummary.Contains(
            remote.Connection.CredentialReference!,
            StringComparison.Ordinal),
        "credential reference redacted");

    True(
        !remoteRow.ConnectionSummary.Contains(
            remote.Connection.Username!,
            StringComparison.Ordinal),
        "username omitted from selector");

    return Task.CompletedTask;
}

static Task TargetEditorPolicyAsync()
{
    var draft =
        new WindowsRemoteTargetDraft(
            "  editor-target  ",
            "  Editor Target  ",
            "  server.example.test  ",
            "5986",
            "  domain\\graveops  ",
            "Negotiate",
            "120",
            "sha256:" +
            new string(
                'b',
                64));

    var target =
        WindowsTargetEditorPolicy.CreateTarget(
            draft);

    Equal(
        "editor-target",
        target.Id,
        "editor target ID");

    Equal(
        "Editor Target",
        target.DisplayName,
        "editor display name");

    var parsed =
        RemoteWindowsConnectionParser.Parse(
            target);

    Equal(
        "server.example.test",
        parsed.Host,
        "editor host");

    Equal(
        5986,
        parsed.Port,
        "editor port");

    Equal(
        "domain\\graveops",
        parsed.Username,
        "editor username");

    Equal(
        TimeSpan.FromSeconds(
            120),
        parsed.OperationTimeout,
        "editor timeout");

    True(
        parsed.PinnedServerCertificateSha256 is not null,
        "editor certificate pin");

    True(
        WindowsTargetEditorPolicy.RequiresPassword(
            isNewTarget: true,
            password: string.Empty),
        "new target password required");

    True(
        !WindowsTargetEditorPolicy.RequiresPassword(
            isNewTarget: false,
            password: string.Empty),
        "existing target may retain credential");

    return Task.CompletedTask;
}

static Task TargetRemovalConfirmationAsync()
{
    True(
        !WindowsTargetEditorPolicy.IsRemovalConfirmed(
            pendingTargetId: null,
            targetId: "remote-one"),
        "first removal click is not confirmed");

    True(
        !WindowsTargetEditorPolicy.IsRemovalConfirmed(
            pendingTargetId: "remote-two",
            targetId: "remote-one"),
        "different pending target is not confirmed");

    True(
        WindowsTargetEditorPolicy.IsRemovalConfirmed(
            pendingTargetId: "remote-one",
            targetId: "remote-one"),
        "matching second click is confirmed");

    return Task.CompletedTask;
}

static async Task TargetCreationIdentityAsync()
{
    await ThrowsAsync<
        InvalidOperationException>(
        () =>
        {
            _ =
                WindowsTargetEditorPolicy.CreateTarget(
                    new WindowsRemoteTargetDraft(
                        WindowsTargetCatalog.LocalTargetId,
                        "Unsafe local replacement",
                        "server.example.test",
                        "5986",
                        "graveops",
                        "Negotiate",
                        "60",
                        null));

            return Task.CompletedTask;
        },
        "editor reserves local target ID");

    await WithStoresAsync(
        async (
            registry,
            activeStore) =>
        {
            var provider =
                new DelayedHostProvider();

            var session =
                new WindowsTargetSession(
                    registry,
                    new HostProviderRegistry(
                        new IHostProvider[]
                        {
                            provider
                        }),
                    activeStore);

            await session.InitializeAsync();

            var remote =
                TestTarget(
                    "duplicate-target");

            await session.CreateAsync(
                remote);

            await ThrowsAsync<
                InvalidOperationException>(
                async () =>
                    await session.CreateAsync(
                        remote with
                        {
                            DisplayName =
                                "Duplicate replacement"
                        }),
                "duplicate target creation");

            var preserved =
                await session.FindAsync(
                    remote.Id);

            Equal(
                remote.DisplayName,
                preserved?.DisplayName,
                "duplicate create preserves original profile");

            var unsafeLocal =
                remote with
                {
                    Id =
                        WindowsTargetCatalog.LocalTargetId,

                    DisplayName =
                        "Unsafe local replacement"
                };

            await ThrowsAsync<
                InvalidOperationException>(
                async () =>
                    await session.UpsertAsync(
                        unsafeLocal),
                "session protects local target identity");

            var local =
                await session.FindAsync(
                    WindowsTargetCatalog.LocalTargetId);

            Equal(
                HostProviderIds.LocalWindows,
                local?.ProviderId,
                "local target provider preserved");

            Equal(
                TransportIds.Local,
                local?.Connection.TransportId,
                "local target transport preserved");
        });
}

static async Task TargetSessionCredentialAsync()
{
    await WithStoresAsync(
        async (
            registry,
            activeStore) =>
        {
            var vault =
                new MemoryCredentialVault();

            var session =
                new WindowsTargetSession(
                    registry,
                    new HostProviderRegistry(
                        new IHostProvider[]
                        {
                            new LocalWindowsHostProvider()
                        }),
                    activeStore,
                    vault);

            await session.InitializeAsync();

            const string TargetId =
                "credential-session";

            const string Password =
                "credential-session-password";

            await session.StoreCredentialAsync(
                TargetId,
                Password);

            using var retrieved =
                await vault.RetrieveAsync(
                    new CredentialReference(
                        WindowsTargetCatalog.CredentialReferenceFor(
                            TargetId)));

            True(
                retrieved is not null,
                "session credential retrieved");

            Equal(
                Password,
                new string(
                    retrieved!.Reveal().Span),
                "session credential value");

            await session.DeleteCredentialAsync(
                TargetId);

            using var deleted =
                await vault.RetrieveAsync(
                    new CredentialReference(
                        WindowsTargetCatalog.CredentialReferenceFor(
                            TargetId)));

            True(
                deleted is null,
                "session credential deleted");
        });
}

static Task ProviderCompositionAsync()
{
    var providers =
        WindowsHostProviderComposition.Create(
            new MemoryCredentialVault());

    var local =
        WindowsTargetCatalog.CreateLocal();

    var remote =
        WindowsTargetCatalog.CreateRemote(
            "remote-provider-test",
            "Remote Provider Test",
            "server.example.test",
            5986,
            "graveops",
            WindowsRemoteAuthentication.Basic,
            60);

    Equal(
        HostProviderIds.LocalWindows,
        providers.Resolve(
            local).Descriptor.Id,
        "local provider resolution");

    Equal(
        HostProviderIds.RemoteWindows,
        providers.Resolve(
            remote).Descriptor.Id,
        "remote provider resolution");

    return Task.CompletedTask;
}

static Task RemoteFactoryAsync()
{
    var target =
        WindowsTargetCatalog.CreateRemote(
            "validated-remote",
            "Validated Remote",
            "server.example.test",
            5986,
            "domain\\graveops",
            WindowsRemoteAuthentication.Negotiate,
            120,
            "sha256:" +
            new string(
                'A',
                64));

    var parsed =
        RemoteWindowsConnectionParser.Parse(
            target);

    Equal(
        5986,
        parsed.Port,
        "WinRM HTTPS port");

    Equal(
        TimeSpan.FromSeconds(
            120),
        parsed.OperationTimeout,
        "operation timeout");

    Equal(
        WindowsRemoteAuthentication.Negotiate,
        parsed.Authentication,
        "authentication");

    True(
        parsed.PinnedServerCertificateSha256 is not null,
        "certificate pin");

    return Task.CompletedTask;
}

static async Task StaleCaptureAsync()
{
    await WithStoresAsync(
        async (
            registry,
            activeStore) =>
        {
            var first =
                TestTarget(
                    "first");

            var second =
                TestTarget(
                    "second");

            await registry.UpsertAsync(
                first);

            await registry.UpsertAsync(
                second);

            var provider =
                new DelayedHostProvider();

            var session =
                new WindowsTargetSession(
                    registry,
                    new HostProviderRegistry(
                        new[]
                        {
                            provider
                        }),
                    activeStore);

            await session.InitializeAsync();
            await session.SelectAsync(
                first.Id);

            var capture =
                session.CaptureAsync();

            await provider.Started.Task;

            await session.SelectAsync(
                second.Id);

            provider.Release.TrySetResult(
                true);

            await ThrowsAsync<
                OperationCanceledException>(
                async () =>
                    await capture,
                "stale capture");
        });
}

static async Task ActiveTargetEditAsync()
{
    await WithStoresAsync(
        async (
            registry,
            activeStore) =>
        {
            var original =
                TestTarget(
                    "editable");

            await registry.UpsertAsync(
                original);

            var provider =
                new DelayedHostProvider();

            var session =
                new WindowsTargetSession(
                    registry,
                    new HostProviderRegistry(
                        new[]
                        {
                            provider
                        }),
                    activeStore);

            await session.InitializeAsync();
            await session.SelectAsync(
                original.Id);

            var capture =
                session.CaptureAsync();

            var started =
                await provider.Started.Task;

            Equal(
                original.Connection.Host,
                started.Target.Connection.Host,
                "original capture target");

            var updated =
                original with
                {
                    DisplayName =
                        "Updated editable target",

                    Connection =
                        original.Connection with
                        {
                            Host =
                                "updated.example.test"
                        }
                };

            await session.UpsertAsync(
                updated);

            EqualTargetProfile(
                updated,
                session.SelectedTarget,
                "updated selected target");

            provider.Release.TrySetResult(
                true);

            await ThrowsAsync<
                OperationCanceledException>(
                async () =>
                    await capture,
                "capture invalidated by active-target edit");
        });
}

static async Task DeleteFallbackAsync()
{
    await WithStoresAsync(
        async (
            registry,
            activeStore) =>
        {
            var remote =
                TestTarget(
                    "delete-me");

            await registry.UpsertAsync(
                remote);

            var provider =
                new DelayedHostProvider();

            var session =
                new WindowsTargetSession(
                    registry,
                    new HostProviderRegistry(
                        new[]
                        {
                            provider
                        }),
                    activeStore);

            await session.InitializeAsync();
            await session.SelectAsync(
                remote.Id);

            True(
                await session.RemoveAsync(
                    remote.Id),
                "remote target removed");

            Equal(
                WindowsTargetCatalog.LocalTargetId,
                session.SelectedTarget?.Id,
                "local fallback selected");

            Equal(
                WindowsTargetCatalog.LocalTargetId,
                await activeStore.LoadAsync(),
                "local fallback persisted");

            True(
                !await session.RemoveAsync(
                    WindowsTargetCatalog.LocalTargetId),
                "local target protected");
        });
}

static Task NavigationPolicyAsync()
{
    var capabilities =
        new TargetCapabilities(
            new[]
            {
                CapabilityIds.ServicesRead,
                CapabilityIds.StorageRead,
                CapabilityIds.EventLogRead
            });

    True(
        WindowsTargetNavigationPolicy.IsSupported(
            "ServicesNav",
            capabilities),
        "services supported");

    True(
        WindowsTargetNavigationPolicy.IsSupported(
            "StorageNav",
            capabilities),
        "storage supported");

    True(
        WindowsTargetNavigationPolicy.IsSupported(
            "LogsNav",
            capabilities),
        "logs supported");

    True(
        !WindowsTargetNavigationPolicy.IsSupported(
            "DockerNav",
            capabilities),
        "containers rejected");

    True(
        !WindowsTargetNavigationPolicy.IsSupported(
            "BackupsNav",
            capabilities),
        "backups rejected");

    return Task.CompletedTask;
}

static Task CredentialVaultAvailabilityAsync()
{
    var vault =
        new WindowsCredentialVault();

    Equal(
        OperatingSystem.IsWindows(),
        vault.IsAvailable,
        "Windows Credential Manager availability");

    Equal(
        "windows-credential-manager",
        vault.VaultId,
        "vault ID");

    return Task.CompletedTask;
}

static async Task CredentialVaultRoundTripAsync()
{
    if (!OperatingSystem.IsWindows())
        return;

    var vault =
        new WindowsCredentialVault();

    var reference =
        new CredentialReference(
            "graveops/tests/" +
            Guid.NewGuid().ToString("N"));

    var expected =
        "graveops-test-" +
        Guid.NewGuid().ToString("N");

    try
    {
        using (
            var secret =
                new SecretValue(
                    expected))
        {
            await vault.StoreAsync(
                reference,
                secret);
        }

        using var retrieved =
            await vault.RetrieveAsync(
                reference);

        True(
            retrieved is not null,
            "temporary credential retrieved");

        Equal(
            expected,
            new string(
                retrieved!.Reveal().Span),
            "temporary credential value");
    }
    finally
    {
        await vault.DeleteAsync(
            reference);
    }

    using var missing =
        await vault.RetrieveAsync(
            reference);

    True(
        missing is null,
        "temporary credential deleted");
}

static TargetProfile TestTarget(
    string id) =>
    new(
        id,
        id,
        "test-provider",
        TargetPlatform.Windows,
        TargetLocation.Remote,
        new TargetConnectionProfile(
            TransportIds.WinRmHttps,
            $"{id}.example.test",
            5986,
            "test",
            $"graveops/target/{id}/password"));

static async Task WithStoresAsync(
    Func<
        JsonTargetRegistry,
        JsonActiveTargetStore,
        Task> action)
{
    var directory =
        Path.Combine(
            Path.GetTempPath(),
            "graveops-windows-target-tests",
            Guid.NewGuid().ToString("N"));

    Directory.CreateDirectory(
        directory);

    try
    {
        using var registry =
            new JsonTargetRegistry(
                Path.Combine(
                    directory,
                    "targets.json"));

        var activeStore =
            new JsonActiveTargetStore(
                Path.Combine(
                    directory,
                    "active-target.json"));

        await action(
            registry,
            activeStore);
    }
    finally
    {
        if (Directory.Exists(
                directory))
        {
            Directory.Delete(
                directory,
                recursive: true);
        }
    }
}

static void EqualTargetProfile(
    TargetProfile expected,
    TargetProfile? actual,
    string name)
{
    True(
        actual is not null,
        $"{name}: profile exists");

    var candidate =
        actual!;

    Equal(
        expected.Id,
        candidate.Id,
        $"{name}: ID");

    Equal(
        expected.DisplayName,
        candidate.DisplayName,
        $"{name}: display name");

    Equal(
        expected.ProviderId,
        candidate.ProviderId,
        $"{name}: provider");

    Equal(
        expected.Platform,
        candidate.Platform,
        $"{name}: platform");

    Equal(
        expected.Location,
        candidate.Location,
        $"{name}: location");

    Equal(
        expected.Connection.TransportId,
        candidate.Connection.TransportId,
        $"{name}: transport");

    Equal(
        expected.Connection.Host,
        candidate.Connection.Host,
        $"{name}: host");

    Equal(
        expected.Connection.Port,
        candidate.Connection.Port,
        $"{name}: port");

    Equal(
        expected.Connection.Username,
        candidate.Connection.Username,
        $"{name}: username");

    Equal(
        expected.Connection.CredentialReference,
        candidate.Connection.CredentialReference,
        $"{name}: credential reference");

    Equal(
        expected.Connection.PinnedIdentity,
        candidate.Connection.PinnedIdentity,
        $"{name}: pinned identity");

    EqualDictionary(
        expected.Connection.Options,
        candidate.Connection.Options,
        $"{name}: options");

    EqualDictionary(
        expected.Metadata,
        candidate.Metadata,
        $"{name}: metadata");
}

static void EqualDictionary(
    IReadOnlyDictionary<string, string>? expected,
    IReadOnlyDictionary<string, string>? actual,
    string name)
{
    var expectedPairs =
        (expected ??
         new Dictionary<string, string>())
        .OrderBy(
            pair => pair.Key,
            StringComparer.Ordinal)
        .ToArray();

    var actualPairs =
        (actual ??
         new Dictionary<string, string>())
        .OrderBy(
            pair => pair.Key,
            StringComparer.Ordinal)
        .ToArray();

    Equal(
        expectedPairs.Length,
        actualPairs.Length,
        $"{name}: count");

    for (var index = 0;
         index < expectedPairs.Length;
         index++)
    {
        Equal(
            expectedPairs[index].Key,
            actualPairs[index].Key,
            $"{name}: key {index}");

        Equal(
            expectedPairs[index].Value,
            actualPairs[index].Value,
            $"{name}: value {expectedPairs[index].Key}");
    }
}

static async Task ThrowsAsync<TException>(
    Func<Task> action,
    string name)
    where TException : Exception
{
    try
    {
        await action();
    }
    catch (TException)
    {
        return;
    }

    throw new InvalidOperationException(
        $"Expected {typeof(TException).Name}: {name}");
}

static void True(
    bool condition,
    string name)
{
    if (!condition)
    {
        throw new InvalidOperationException(
            $"Expected true: {name}");
    }
}

static void Equal<T>(
    T expected,
    T actual,
    string name)
{
    if (!EqualityComparer<T>.Default.Equals(
            expected,
            actual))
    {
        throw new InvalidOperationException(
            $"Expected '{expected}', got '{actual}': {name}");
    }
}

sealed class MemoryCredentialVault :
    ICredentialVault
{
    private readonly Dictionary<string, string>
        _values =
            new(
                StringComparer.Ordinal);

    public string VaultId =>
        "memory";

    public bool IsAvailable =>
        true;

    public Task StoreAsync(
        CredentialReference reference,
        SecretValue secret,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        _values[reference.Value] =
            new string(
                secret.Reveal().Span);

        return Task.CompletedTask;
    }

    public Task<SecretValue?> RetrieveAsync(
        CredentialReference reference,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        return Task.FromResult<
            SecretValue?>(
            _values.TryGetValue(
                reference.Value,
                out var value)
                ? new SecretValue(
                    value)
                : null);
    }

    public Task DeleteAsync(
        CredentialReference reference,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        _values.Remove(
            reference.Value);

        return Task.CompletedTask;
    }
}

sealed class DelayedHostProvider :
    IHostProvider
{
    public TaskCompletionSource<
        (TargetProfile Target, TargetRefreshLease Lease)>
        Started { get; } =
            new(
                TaskCreationOptions.RunContinuationsAsynchronously);

    public TaskCompletionSource<bool>
        Release { get; } =
            new(
                TaskCreationOptions.RunContinuationsAsynchronously);

    public HostProviderDescriptor Descriptor { get; } =
        new(
            "test-provider",
            "Test provider",
            new HashSet<TargetPlatform>
            {
                TargetPlatform.Windows
            },
            new HashSet<TargetLocation>
            {
                TargetLocation.Local,
                TargetLocation.Remote
            });

    public bool CanHandle(
        TargetProfile target) =>
        target.ProviderId.Equals(
            "test-provider",
            StringComparison.Ordinal) ||
        target.Id.Equals(
            WindowsTargetCatalog.LocalTargetId,
            StringComparison.Ordinal);

    public Task<HostProviderProbeResult> ProbeAsync(
        TargetProfile target,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(
            new HostProviderProbeResult(
                TargetCapabilities.Empty,
                "Test provider",
                Array.Empty<string>(),
                Array.Empty<string>()));

    public async Task<
        TargetSnapshotEnvelope<HostSnapshot>>
        CaptureAsync(
            TargetProfile target,
            TargetRefreshLease refreshLease,
            CancellationToken cancellationToken = default)
    {
        Started.TrySetResult(
            (
                target,
                refreshLease));

        await Release.Task.WaitAsync(
            cancellationToken);

        var snapshot =
            new HostSnapshot(
                DateTimeOffset.UtcNow,
                target.DisplayName,
                "Windows test",
                "test",
                "1 minute",
                "running",
                "not installed",
                "Test CPU",
                "0",
                "1 GB",
                "127.0.0.1",
                Array.Empty<StorageVolumeSnapshot>(),
                Array.Empty<ServiceSnapshot>(),
                Array.Empty<DockerContainerSnapshot>(),
                Array.Empty<IntegrationSnapshot>(),
                Array.Empty<string>(),
                Array.Empty<string>(),
                Array.Empty<string>());

        return new TargetSnapshotEnvelope<HostSnapshot>(
            refreshLease,
            Descriptor.Id,
            snapshot.CapturedAt,
            new TargetCapabilities(
                new[]
                {
                    CapabilityIds.HostSummaryRead
                }),
            snapshot);
    }
}