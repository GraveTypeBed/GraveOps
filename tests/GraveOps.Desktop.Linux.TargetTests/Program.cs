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
            LifecycleLayoutConstrainsListsAsync),
        (
            "borderless Linux window exposes all resize directions",
            BorderlessWindowExposesResizeDirectionsAsync),
        (
            "responsive shell keeps Lifecycle and Settings reachable",
            ResponsiveShellKeepsLifecycleAndSettingsReachableAsync),
        (
            "Policy management uses structured status rows",
            PolicyManagementUsesStructuredStatusRowsAsync)
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
            "x:Name=\"LifecyclePageScrollViewer\"",
            StringComparison.Ordinal),
        "Lifecycle owns a page-level scroll path");

    True(
        lifecycleMarkup.Contains(
            "x:Name=\"LifecycleContentPanel\"",
            StringComparison.Ordinal),
        "Lifecycle content remains one reachable vertical sequence");

    True(
        lifecycleMarkup.Contains(
            "x:Name=\"LifecycleWorkspaceGrid\"",
            StringComparison.Ordinal) &&
        lifecycleMarkup.Contains(
            "Height=\"260\"",
            StringComparison.Ordinal),
        "Lifecycle workspace remains bounded at normal width");

    True(
        lifecycleMarkup.Contains(
            "x:Name=\"LifecycleSelectedModule\"",
            StringComparison.Ordinal) &&
        lifecycleMarkup.Contains(
            "Height=\"132\"",
            StringComparison.Ordinal),
        "selected Lifecycle module remains bounded");

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
            "x:Name=\"LifecycleSelectedTitleText\"\n                            Text=\"No lifecycle item selected\"\n                            FontWeight=\"SemiBold\"\n                            TextWrapping=\"NoWrap\"\n                            TextTrimming=\"CharacterEllipsis\"",
            StringComparison.Ordinal),
        "selected Lifecycle title containment");

    True(
        !lifecycleMarkup.Contains(
            "RowDefinitions=\"Auto,Auto,*,Auto\"",
            StringComparison.Ordinal) &&
        !lifecycleMarkup.Contains(
            "RowDefinitions=\"Auto,Auto,Auto,Auto\"",
            StringComparison.Ordinal),
        "obsolete viewport-bound Lifecycle rows removed");

    return Task.CompletedTask;
}

static Task BorderlessWindowExposesResizeDirectionsAsync()
{
    var markup =
        File.ReadAllText(
            FindRepositoryFile(
                "src/GraveOps.Desktop.Linux/MainWindow.axaml"));
    var codeBehind =
        File.ReadAllText(
            FindRepositoryFile(
                "src/GraveOps.Desktop.Linux/MainWindow.axaml.cs"));

    True(
        markup.Contains(
            "CanResize=\"True\"",
            StringComparison.Ordinal),
        "Linux window remains resizable");

    var handlers =
        new[]
        {
            "ResizeNorth_OnPointerPressed",
            "ResizeSouth_OnPointerPressed",
            "ResizeWest_OnPointerPressed",
            "ResizeEast_OnPointerPressed",
            "ResizeNorthWest_OnPointerPressed",
            "ResizeNorthEast_OnPointerPressed",
            "ResizeSouthWest_OnPointerPressed",
            "ResizeSouthEast_OnPointerPressed"
        };

    foreach (var handler in handlers)
    {
        Equal(
            2,
            CountOccurrences(
                markup +
                codeBehind,
                handler),
            $"{handler} declaration and binding");
    }

    var edges =
        new[]
        {
            "WindowEdge.North",
            "WindowEdge.South",
            "WindowEdge.West",
            "WindowEdge.East",
            "WindowEdge.NorthWest",
            "WindowEdge.NorthEast",
            "WindowEdge.SouthWest",
            "WindowEdge.SouthEast"
        };

    foreach (var edge in edges)
    {
        True(
            codeBehind.Contains(
                edge,
                StringComparison.Ordinal),
            $"{edge} resize direction");
    }

    foreach (var cursor in new[]
             {
                 "SizeNorthSouth",
                 "SizeWestEast",
                 "TopLeftCorner",
                 "TopRightCorner",
                 "BottomLeftCorner",
                 "BottomRightCorner"
             })
    {
        True(
            markup.Contains(
                $"Cursor=\"{cursor}\"",
                StringComparison.Ordinal),
            $"{cursor} resize cursor");
    }

    True(
        codeBehind.Contains(
            "WindowState.Normal",
            StringComparison.Ordinal),
        "resize is limited to the restored state");

    True(
        codeBehind.Contains(
            "BeginResizeDrag(",
            StringComparison.Ordinal),
        "native Avalonia resize drag");

    True(
        CountOccurrences(
            markup,
            "Grid.RowSpan=\"2\"") >=
        8,
        "resize grips span the complete custom shell");

    return Task.CompletedTask;
}

static Task ResponsiveShellKeepsLifecycleAndSettingsReachableAsync()
{
    var markup =
        File.ReadAllText(
            FindRepositoryFile(
                "src/GraveOps.Desktop.Linux/MainWindow.axaml"));
    var responsiveCode =
        File.ReadAllText(
            FindRepositoryFile(
                "src/GraveOps.Desktop.Linux/MainWindow.ResponsiveLayout.cs"));
    var mainCode =
        File.ReadAllText(
            FindRepositoryFile(
                "src/GraveOps.Desktop.Linux/MainWindow.axaml.cs"));

    foreach (var marker in new[]
             {
                 "x:Name=\"ShellBodyGrid\"",
                 "x:Name=\"MainWorkspaceGrid\"",
                 "RowDefinitions=\"Auto,42,*,26\"",
                 "x:Name=\"MainHeaderGrid\"",
                 "x:Name=\"MainHeaderTitlePanel\"",
                 "x:Name=\"MainHeaderCommandsPanel\"",
                 "x:Name=\"QuickSearchHintText\"",
                 "x:Name=\"PageContentHost\"",
                 "x:Name=\"LifecyclePageScrollViewer\"",
                 "x:Name=\"LifecycleWorkspaceGrid\"",
                 "x:Name=\"LifecycleRemediationModule\"",
                 "x:Name=\"LifecycleSelectedModule\"",
                 "Height=\"132\"",
                 "x:Name=\"SettingsPageScrollViewer\"",
                 "x:Name=\"SettingsInterfaceGrid\"",
                 "x:Name=\"SettingsInterfaceActionsPanel\"",
                 "x:Name=\"SettingsBodyGrid\"",
                 "x:Name=\"SettingsOperatorDefaultsModule\"",
                 "x:Name=\"SettingsPolicyModule\"",
                 "x:Name=\"SettingsPathsModule\"",
                 "x:Name=\"SettingsVersionModule\""
             })
    {
        True(
            markup.Contains(
                marker,
                StringComparison.Ordinal),
            $"responsive markup marker {marker}");
    }

    var lifecycleStart =
        markup.IndexOf(
            "<!-- Lifecycle -->",
            StringComparison.Ordinal);
    var lifecycleEnd =
        markup.IndexOf(
            "<!-- History -->",
            lifecycleStart,
            StringComparison.Ordinal);
    var settingsStart =
        markup.IndexOf(
            "<!-- Settings -->",
            StringComparison.Ordinal);
    var settingsEnd =
        markup.IndexOf(
            "<!-- Unified Linux-native Operator workspace -->",
            settingsStart,
            StringComparison.Ordinal);

    True(
        lifecycleStart >=
        0 &&
        lifecycleEnd >
        lifecycleStart,
        "responsive Lifecycle boundaries");
    True(
        settingsStart >=
        0 &&
        settingsEnd >
        settingsStart,
        "responsive Settings boundaries");

    var lifecycleMarkup =
        markup[
            lifecycleStart..
            lifecycleEnd];
    var settingsMarkup =
        markup[
            settingsStart..
            settingsEnd];

    True(
        lifecycleMarkup.Contains(
            "VerticalScrollBarVisibility=\"Auto\"",
            StringComparison.Ordinal),
        "Lifecycle page-level scroll owner");
    True(
        settingsMarkup.Contains(
            "VerticalScrollBarVisibility=\"Auto\"",
            StringComparison.Ordinal),
        "Settings page-level scroll owner");
    True(
        !settingsMarkup.Contains(
            "RowDefinitions=\"Auto,Auto,*\"",
            StringComparison.Ordinal),
        "obsolete Settings viewport-bound rows removed");
    True(
        !settingsMarkup.Contains(
            "Text=\"Settings\" Classes=\"sectionTitle\"",
            StringComparison.Ordinal),
        "duplicate in-page Settings heading removed");
    True(
        !settingsMarkup.Contains(
            "x:Name=\"SettingsRightPanel\"",
            StringComparison.Ordinal),
        "unbalanced Settings right-column stack removed");
    True(
        settingsMarkup.Contains(
            "x:Name=\"SettingsPolicyModule\"",
            StringComparison.Ordinal) &&
        settingsMarkup.Contains(
            "x:Name=\"SettingsPathsModule\"",
            StringComparison.Ordinal) &&
        settingsMarkup.Contains(
            "x:Name=\"SettingsVersionModule\"",
            StringComparison.Ordinal),
        "balanced Settings modules remain named");
    True(
        CountOccurrences(
            settingsMarkup,
            "Grid.ColumnSpan=\"2\"") >=
        2,
        "wide Settings lower modules span both columns");
    True(
        CountOccurrences(
            settingsMarkup,
            "ToolTip.Tip=\"{Binding Text, RelativeSource={RelativeSource Self}}\"") >=
        10,
        "Settings long values expose full tooltips");

    foreach (var marker in new[]
             {
                 "CompactWindowWidth",
                 "1320",
                 "OnSizeChanged(",
                 "new ColumnDefinitions(",
                 "new RowDefinitions(",
                 "\"230,*\"",
                 "\"260,*\"",
                 "\"1.25*,0.75*\"",
                 "\"1.05*,0.95*\"",
                 "\"Auto,Auto,Auto,Auto\"",
                 "SettingsPolicyModule",
                 "SettingsPathsModule",
                 "SettingsVersionModule",
                 "lifecycleWorkspace.Height",
                 "448",
                 "260"
             })
    {
        True(
            responsiveCode.Contains(
                marker,
                StringComparison.Ordinal),
            $"responsive code marker {marker}");
    }

    True(
        mainCode.Contains(
            "InitializeResponsiveLayout();",
            StringComparison.Ordinal),
        "responsive layout initializes after XAML");

    return Task.CompletedTask;
}

static Task PolicyManagementUsesStructuredStatusRowsAsync()
{
    var markup =
        File.ReadAllText(
            FindRepositoryFile(
                "src/GraveOps.Desktop.Linux/MainWindow.axaml"));

    var settingsStart =
        markup.IndexOf(
            "<!-- Settings -->",
            StringComparison.Ordinal);
    var settingsEnd =
        markup.IndexOf(
            "<!-- Unified Linux-native Operator workspace -->",
            settingsStart,
            StringComparison.Ordinal);

    True(
        settingsStart >=
        0 &&
        settingsEnd >
        settingsStart,
        "Policy management Settings boundaries");

    var settingsMarkup =
        markup[
            settingsStart..
            settingsEnd];

    var policyStart =
        settingsMarkup.IndexOf(
            "x:Name=\"SettingsPolicyModule\"",
            StringComparison.Ordinal);
    var policyEnd =
        settingsMarkup.IndexOf(
            "x:Name=\"SettingsPathsModule\"",
            policyStart,
            StringComparison.Ordinal);

    True(
        policyStart >=
        0 &&
        policyEnd >
        policyStart,
        "Policy management module boundaries");

    var policyMarkup =
        settingsMarkup[
            policyStart..
            policyEnd];

    foreach (var marker in new[]
             {
                 "x:Name=\"SettingsPolicyHeaderGrid\"",
                 "x:Name=\"SettingsPolicyRowsPanel\"",
                 "x:Name=\"SettingsCapacityPolicyRow\"",
                 "x:Name=\"SettingsSignalQualityRow\"",
                 "x:Name=\"SettingsRemediationPolicyRow\"",
                 "x:Name=\"SettingsUiPerformanceRow\"",
                 "x:Name=\"SettingsPolicyFileRow\"",
                 "x:Name=\"SettingsPolicySummaryText\"",
                 "x:Name=\"SettingsCapacityPolicySummaryText\"",
                 "x:Name=\"SettingsSignalQualitySummaryText\"",
                 "x:Name=\"SettingsVerifiedRemediationSummaryText\"",
                 "x:Name=\"SettingsUiPerformanceSummaryText\"",
                 "x:Name=\"SettingsPolicyPathText\""
             })
    {
        True(
            policyMarkup.Contains(
                marker,
                StringComparison.Ordinal),
            $"structured Policy management marker {marker}");
    }

    Equal(
        4,
        CountOccurrences(
            policyMarkup,
            "ColumnDefinitions=\"130,*,Auto\""),
        "four structured Policy management rows");

    True(
        CountOccurrences(
            policyMarkup,
            "Classes=\"inset\"") >=
        4,
        "Policy status rows use inset grouping");

    foreach (var action in new[]
             {
                 "Dashboard policies",
                 "Capacity alerts",
                 "Storage thresholds",
                 "Signal quality",
                 "Remediation safety",
                 "UI performance"
             })
    {
        True(
            policyMarkup.Contains(
                $"Content=\"{action}\"",
                StringComparison.Ordinal),
            $"Policy management action {action}");
    }

    var capacityStart =
        policyMarkup.IndexOf(
            "x:Name=\"SettingsCapacityPolicyRow\"",
            StringComparison.Ordinal);
    var signalStart =
        policyMarkup.IndexOf(
            "x:Name=\"SettingsSignalQualityRow\"",
            capacityStart,
            StringComparison.Ordinal);
    var capacityMarkup =
        policyMarkup[
            capacityStart..
            signalStart];

    True(
        capacityMarkup.Contains(
            "SettingsCapacityAlertsButton",
            StringComparison.Ordinal) &&
        capacityMarkup.Contains(
            "Storage thresholds",
            StringComparison.Ordinal),
        "capacity actions stay with the capacity status");

    True(
        policyMarkup.IndexOf(
            "x:Name=\"SettingsPolicyFileRow\"",
            StringComparison.Ordinal) >
        policyMarkup.IndexOf(
            "x:Name=\"SettingsUiPerformanceRow\"",
            StringComparison.Ordinal),
        "Policy file remains a subdued footer");

    True(
        !policyMarkup.Contains(
            "<WrapPanel>\n                            <Button Content=\"Dashboard policies\"",
            StringComparison.Ordinal),
        "obsolete undifferentiated Policy action toolbar removed");

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
