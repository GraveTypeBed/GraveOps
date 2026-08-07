using System.Text.Json;
using GraveOps.Core.Providers;
using GraveOps.Core.Targets;
using GraveOps.Desktop.Linux;

var tests =
    new List<(string Name, Func<Task> Run)>
    {
        (
            "remote Windows profile maps to the Core target contract",
            RemoteWindowsMapsToCoreAsync),
        (
            "remote Linux profile preserves SSH ownership",
            RemoteLinuxMapsToCoreAsync),
        (
            "legacy hosts migrate without persisted secrets",
            LegacyHostsMigrateSafelyAsync),
        (
            "target registry round trips Windows options",
            TargetRegistryRoundTripsWindowsAsync),
        (
            "target editor projects SSH and WinRM fields",
            TargetEditorProjectsPlatformsAsync),
        (
            "credential references preserve keyring ownership",
            CredentialReferencesPreserveOwnershipAsync),
        (
            "navigation policy follows capabilities",
            NavigationPolicyUsesCapabilitiesAsync),
        (
            "desktop composition registers four providers",
            DesktopCompositionRegistersProvidersAsync),
        (
            "deleting the active remote target falls back to local",
            DeleteActiveTargetFallsBackAsync),
        (
            "unsafe Windows profiles fail closed",
            UnsafeWindowsProfilesFailClosedAsync),
        (
            "Lifecycle layout constrains lists and contains selected titles",
            LifecycleLayoutConstrainsListsAsync)
    };

var failures =
    0;

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
        Console.Error.WriteLine(
            $"FAIL: {test.Name}");
        Console.Error.WriteLine(
            exception);
    }
}

if (failures > 0)
{
    Console.Error.WriteLine(
        $"{failures} desktop target contract test(s) failed.");

    return 1;
}

Console.WriteLine(
    $"All {tests.Count} desktop target contract tests passed.");

return 0;

static Task RemoteWindowsMapsToCoreAsync()
{
    var profile =
        WindowsProfile();

    var target =
        profile.ToTargetProfile();

    Equal(
        TargetPlatform.Windows,
        target.Platform,
        "Windows platform");
    Equal(
        TargetLocation.Remote,
        target.Location,
        "remote location");
    Equal(
        HostProviderIds.RemoteWindows,
        target.ProviderId,
        "remote Windows provider");
    Equal(
        TransportIds.WinRmHttps,
        target.Connection.TransportId,
        "WinRM HTTPS transport");
    Equal(
        5986,
        target.Connection.Port ??
        0,
        "WinRM port");
    Equal(
        "fixture-user",
        target.Connection.Username,
        "Windows username");
    Equal(
        "graveops/target/windows-fixture/password",
        target.Connection.CredentialReference,
        "opaque credential reference");

    True(
        target.Connection.Options?.TryGetValue(
            "authentication",
            out var authentication) ==
        true &&
        authentication ==
        "Negotiate",
        "WinRM authentication");

    True(
        !JsonSerializer.Serialize(
                target)
            .Contains(
                "fixture-secret",
                StringComparison.Ordinal),
        "target excludes secret values");

    return Task.CompletedTask;
}

static Task RemoteLinuxMapsToCoreAsync()
{
    var profile =
        new LinuxHostProfile
        {
            Id =
                "linux-fixture",
            Name =
                "Linux fixture",
            Kind =
                LinuxHostKind.RemoteLinux,
            Host =
                "linux.example.invalid",
            Port =
                2222,
            Username =
                "operator",
            Authentication =
                LinuxHostAuthentication.PrivateKey,
            PrivateKeyPath =
                "/home/operator/.ssh/id_ed25519",
            HostKeyFingerprint =
                "SHA256:fixture"
        };

    var target =
        profile.ToTargetProfile();

    Equal(
        HostProviderIds.RemoteLinuxSsh,
        target.ProviderId,
        "remote Linux provider");
    Equal(
        TransportIds.Ssh,
        target.Connection.TransportId,
        "SSH transport");
    Equal(
        "graveops/target/linux-fixture/passphrase",
        target.Connection.CredentialReference,
        "private-key credential reference");

    return Task.CompletedTask;
}

static Task LegacyHostsMigrateSafelyAsync()
{
    using var temporary =
        new TemporaryDirectory();

    var legacy =
        new object[]
        {
            new
            {
                Id =
                    "local",
                Name =
                    "Fixture local",
                Kind =
                    0,
                Host =
                    "127.0.0.1",
                Port =
                    22,
                Username =
                    "fixture",
                Role =
                    "Local control plane",
                Authentication =
                    0,
                PrivateKeyPath =
                    "",
                HostKeyFingerprint =
                    "",
                Secret =
                    "fixture-secret-do-not-store"
            },
            new
            {
                Id =
                    "legacy-remote",
                Name =
                    "Legacy remote",
                Kind =
                    1,
                Host =
                    "192.0.2.50",
                Port =
                    22,
                Username =
                    "operator",
                Role =
                    "Server",
                Authentication =
                    2,
                PrivateKeyPath =
                    "",
                HostKeyFingerprint =
                    "SHA256:legacy"
            }
        };

    File.WriteAllText(
        Path.Combine(
            temporary.Path,
            "hosts.json"),
        JsonSerializer.Serialize(
            legacy));

    var store =
        new LinuxHostProfileStore(
            temporary.Path);

    Equal(
        2,
        store.Profiles.Count,
        "migrated target count");

    var remote =
        store.Find(
            "legacy-remote") ??
        throw new InvalidOperationException(
            "Migrated remote target missing.");

    Equal(
        LinuxHostKind.RemoteLinux,
        remote.Kind,
        "migrated kind");
    Equal(
        LinuxHostAuthentication.Password,
        remote.Authentication,
        "migrated authentication");

    var persisted =
        File.ReadAllText(
            store.FilePath);

    True(
        !persisted.Contains(
            "fixture-secret-do-not-store",
            StringComparison.Ordinal),
        "migrated registry excludes unknown secret fields");

    True(
        File.Exists(
            store.LegacyFilePath),
        "legacy source retained for rollback");

    return Task.CompletedTask;
}

static async Task TargetRegistryRoundTripsWindowsAsync()
{
    using var temporary =
        new TemporaryDirectory();

    ITargetRegistry registry =
        new LinuxHostProfileStore(
            temporary.Path);

    var target =
        WindowsProfile()
            .ToTargetProfile();

    await registry.UpsertAsync(
        target);

    var loaded =
        await registry.FindAsync(
            target.Id) ??
        throw new InvalidOperationException(
            "Round-tripped Windows target missing.");

    Equal(
        target.Id,
        loaded.Id,
        "round-tripped ID");
    Equal(
        target.DisplayName,
        loaded.DisplayName,
        "round-tripped display name");
    Equal(
        target.ProviderId,
        loaded.ProviderId,
        "round-tripped provider");
    Equal(
        target.Connection.TransportId,
        loaded.Connection.TransportId,
        "round-tripped transport");
    Equal(
        target.Connection.Host,
        loaded.Connection.Host,
        "round-tripped host");
    Equal(
        target.Connection.Port,
        loaded.Connection.Port,
        "round-tripped port");
    Equal(
        target.Connection.Username,
        loaded.Connection.Username,
        "round-tripped username");
    Equal(
        target.Connection.CredentialReference,
        loaded.Connection.CredentialReference,
        "round-tripped credential reference");

    True(
        loaded.Connection.Options?.TryGetValue(
            "operation-timeout-seconds",
            out var timeout) ==
        true &&
        timeout ==
        "60",
        "round-tripped timeout");

    var serialized =
        File.ReadAllText(
            Path.Combine(
                temporary.Path,
                "targets.json"));

    True(
        serialized.Contains(
            "windows.example.invalid",
            StringComparison.Ordinal),
        "endpoint persisted");

    True(
        !serialized.Contains(
            "fixture-secret",
            StringComparison.Ordinal),
        "secret absent from target registry");
}

static Task TargetEditorProjectsPlatformsAsync()
{
    var ssh =
        TargetEditorProjectionPolicy.Create(
            LinuxHostKind.RemoteLinux,
            LinuxHostAuthentication.PrivateKey,
            credentialVaultAvailable:
                true);

    True(
        ssh.ShowPrivateKey,
        "SSH private key panel");
    True(
        ssh.ShowFingerprintScan,
        "SSH fingerprint scan");
    True(
        !ssh.ShowWindowsOptions,
        "SSH hides Windows options");
    Equal(
        22,
        ssh.DefaultPort,
        "SSH default port");

    var windows =
        TargetEditorProjectionPolicy.Create(
            LinuxHostKind.RemoteWindows,
            LinuxHostAuthentication.WinRmNegotiate,
            credentialVaultAvailable:
                true);

    True(
        windows.ShowWindowsOptions,
        "WinRM options");
    True(
        windows.ShowSecret,
        "WinRM credential");
    True(
        !windows.ShowFingerprintScan,
        "WinRM does not use SSH scan");
    True(
        windows.PinnedIdentityLabel.Contains(
            "certificate",
            StringComparison.OrdinalIgnoreCase),
        "Windows certificate label");
    Equal(
        5986,
        windows.DefaultPort,
        "WinRM default port");

    return Task.CompletedTask;
}

static Task CredentialReferencesPreserveOwnershipAsync()
{
    var reference =
        LinuxHostProfile.CredentialReferenceFor(
            "fixture-target",
            "password");

    Equal(
        "graveops/target/fixture-target/password",
        reference.Value,
        "credential reference");

    var parsed =
        LinuxCredentialStore.ParseCredentialReference(
            reference);

    Equal(
        "fixture-target",
        parsed.TargetId,
        "credential target ID");
    Equal(
        "password",
        parsed.Kind,
        "credential kind");

    Throws<InvalidOperationException>(
        () =>
            LinuxCredentialStore.ParseCredentialReference(
                new GraveOps.Core.Security.CredentialReference(
                    "graveops/target/fixture-target/token")),
        "unsupported credential kind");

    return Task.CompletedTask;
}

static Task NavigationPolicyUsesCapabilitiesAsync()
{
    var linuxLocal =
        GraveOps.Platform.Linux
            .LinuxTargetCapabilityCatalog
            .ForTarget(
                isLocal: true);

    True(
        TargetNavigationPolicy.IsSupported(
            "LogsNav",
            linuxLocal),
        "local Linux journal");
    True(
        TargetNavigationPolicy.IsSupported(
            "BackupsNav",
            linuxLocal),
        "local Linux backups");

    var windowsRemote =
        GraveOps.Platform.Windows
            .WindowsTargetCapabilityCatalog
            .ForRemoteTarget();

    True(
        TargetNavigationPolicy.IsSupported(
            "LogsNav",
            windowsRemote),
        "remote Windows event log");
    True(
        !TargetNavigationPolicy.IsSupported(
            "BackupsNav",
            windowsRemote),
        "remote Windows backup boundary");
    True(
        TargetNavigationPolicy.IsSupported(
            "ServicesNav",
            windowsRemote),
        "remote Windows service inventory");

    return Task.CompletedTask;
}

static Task DesktopCompositionRegistersProvidersAsync()
{
    using var temporary =
        new TemporaryDirectory();

    var coordinator =
        new LinuxControlPlaneCoordinator(
            temporary.Path);

    var providerIds =
        coordinator.HostProviders.Providers
            .Select(provider =>
                provider.Descriptor.Id)
            .ToHashSet(
                StringComparer.OrdinalIgnoreCase);

    Equal(
        4,
        providerIds.Count,
        "provider count");

    foreach (var expected in new[]
             {
                 HostProviderIds.LocalLinux,
                 HostProviderIds.RemoteLinuxSsh,
                 HostProviderIds.LocalWindows,
                 HostProviderIds.RemoteWindows
             })
    {
        True(
            providerIds.Contains(
                expected),
            expected);
    }

    return Task.CompletedTask;
}

static async Task DeleteActiveTargetFallsBackAsync()
{
    using var temporary =
        new TemporaryDirectory();

    var coordinator =
        new LinuxControlPlaneCoordinator(
            temporary.Path);

    var profile =
        WindowsProfile();

    coordinator.Profiles.Upsert(
        profile);
    coordinator.SetActive(
        profile.Id);

    Equal(
        profile.Id,
        coordinator.ActiveProfile.Id,
        "active remote target");

    await coordinator.DeleteProfileAsync(
        profile.Id);

    Equal(
        "local",
        coordinator.ActiveProfile.Id,
        "local fallback");
}

static Task UnsafeWindowsProfilesFailClosedAsync()
{
    var invalidAuthentication =
        WindowsProfile();

    invalidAuthentication.Authentication =
        LinuxHostAuthentication.Password;

    Throws<InvalidOperationException>(
        () =>
            invalidAuthentication.ToTargetProfile(),
        "Windows authentication");

    var invalidTimeout =
        WindowsProfile();

    invalidTimeout.OperationTimeoutSeconds =
        5;

    Throws<InvalidOperationException>(
        () =>
            invalidTimeout.ToTargetProfile(),
        "Windows timeout");

    var invalidPin =
        WindowsProfile();

    invalidPin.HostKeyFingerprint =
        "not-a-sha256-pin";

    Throws<InvalidOperationException>(
        () =>
            invalidPin.ToTargetProfile(),
        "Windows certificate pin");

    var localWindows =
        WindowsProfile();

    localWindows.Kind =
        LinuxHostKind.LocalWindows;

    Throws<InvalidOperationException>(
        () =>
            localWindows.ToTargetProfile(),
        "local Windows from Linux client");

    return Task.CompletedTask;
}

static Task LifecycleLayoutConstrainsListsAsync()
{
    var markup =
        File.ReadAllText(
            FindRepositoryFile(
                "src/GraveOps.Desktop.Linux/MainWindow.axaml"));

    var lifecycleStart =
        markup.IndexOf(
            "<!-- Lifecycle -->",
            StringComparison.Ordinal);
    var lifecycleEnd =
        markup.IndexOf(
            "<!-- History -->",
            lifecycleStart,
            StringComparison.Ordinal);

    True(
        lifecycleStart >=
        0 &&
        lifecycleEnd >
        lifecycleStart,
        "Lifecycle markup boundaries");

    var lifecycleMarkup =
        markup[
            lifecycleStart..
            lifecycleEnd];

    True(
        lifecycleMarkup.Contains(
            "x:Name=\"LifecyclePage\"\n                IsVisible=\"False\"\n                RowDefinitions=\"Auto,Auto,*,Auto\"",
            StringComparison.Ordinal),
        "Lifecycle owns remaining vertical height");

    True(
        lifecycleMarkup.Contains(
            "Grid.Row=\"2\" ColumnDefinitions=\"1.25*,0.75*\" ColumnSpacing=\"8\" MinHeight=\"220\"",
            StringComparison.Ordinal),
        "Lifecycle workspace minimum height");

    True(
        CountOccurrences(
            lifecycleMarkup,
            "ScrollViewer.VerticalScrollBarVisibility=\"Visible\"") >= 2,
        "Lifecycle list scrollbars remain visible");

    True(
        lifecycleMarkup.Contains(
            "ColumnDefinitions=\"220,*,Auto\" ColumnSpacing=\"12\"",
            StringComparison.Ordinal),
        "selected Lifecycle panel columns");

    True(
        lifecycleMarkup.Contains(
            "x:Name=\"LifecycleSelectedTitleText\"\n                        Text=\"No lifecycle item selected\"\n                        FontWeight=\"SemiBold\"\n                        TextWrapping=\"NoWrap\"\n                        TextTrimming=\"CharacterEllipsis\"",
            StringComparison.Ordinal),
        "selected Lifecycle title containment");

    True(
        !lifecycleMarkup.Contains(
            "RowDefinitions=\"Auto,Auto,Auto,Auto\"",
            StringComparison.Ordinal),
        "obsolete unconstrained Lifecycle rows removed");

    return Task.CompletedTask;
}

static string FindRepositoryFile(
    string relativePath)
{
    var normalized =
        relativePath.Replace(
            '/',
            Path.DirectorySeparatorChar);

    foreach (var start in new[]
             {
                 Directory.GetCurrentDirectory(),
                 AppContext.BaseDirectory
             })
    {
        var current =
            new DirectoryInfo(
                start);

        while (current is not null)
        {
            var candidate =
                Path.Combine(
                    current.FullName,
                    normalized);

            if (File.Exists(
                    candidate))
            {
                return candidate;
            }

            current =
                current.Parent;
        }
    }

    throw new FileNotFoundException(
        $"Could not locate repository file: {relativePath}");
}

static int CountOccurrences(
    string value,
    string fragment)
{
    var count =
        0;
    var offset =
        0;

    while ((offset =
                value.IndexOf(
                    fragment,
                    offset,
                    StringComparison.Ordinal)) >=
           0)
    {
        count++;
        offset +=
            fragment.Length;
    }

    return count;
}

static LinuxHostProfile WindowsProfile() =>
    new()
    {
        Id =
            "windows-fixture",
        Name =
            "Windows fixture",
        Kind =
            LinuxHostKind.RemoteWindows,
        Host =
            "windows.example.invalid",
        Port =
            5986,
        Username =
            "fixture-user",
        Role =
            "Windows server",
        Authentication =
            LinuxHostAuthentication.WinRmNegotiate,
        OperationTimeoutSeconds =
            60,
        CredentialReference =
            "graveops/target/windows-fixture/password"
    };

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
            $"Mismatch for {name}. " +
            $"Expected {expected}; actual {actual}.");
    }
}

static void Throws<TException>(
    Action action,
    string name)
    where TException : Exception
{
    try
    {
        action();
    }
    catch (TException)
    {
        return;
    }

    throw new InvalidOperationException(
        $"Expected {typeof(TException).Name}: {name}.");
}

internal sealed class TemporaryDirectory :
    IDisposable
{
    public TemporaryDirectory()
    {
        Path =
            System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                "graveops-target-tests",
                Guid.NewGuid().ToString("N"));

        Directory.CreateDirectory(
            Path);
    }

    public string Path { get; }

    public void Dispose()
    {
        try
        {
            Directory.Delete(
                Path,
                recursive: true);
        }
        catch
        {
            // Test cleanup is best effort.
        }
    }
}
