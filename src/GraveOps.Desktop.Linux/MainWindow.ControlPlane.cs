
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using GraveOps.Core.Hosts;
using GraveOps.Core.Snapshots;
using GraveOps.Core.Targets;

namespace GraveOps.Desktop.Linux;

public partial class MainWindow
{
    private readonly LinuxControlPlaneCoordinator
        _controlPlane = new();
    private readonly DispatcherTimer
        _controlPlaneTimer = new();
    private bool _targetSelectionBusy;
    private bool _serverProfileBindingBusy;
    private bool _controlPlaneCaptureBusy;
    private DateTimeOffset _nextBackgroundRefreshAt;
    private string _lastNotificationKey =
        string.Empty;

    private void InitializeControlPlaneFoundation()
    {
        Get<ComboBox>("ServerConnectionTypeComboBox")
            .ItemsSource =
            new[]
            {
                LinuxHostKind.Local.ToString(),
                LinuxHostKind.RemoteLinux.ToString(),
                LinuxHostKind.RemoteWindows.ToString()
            };
        Get<ComboBox>("ServerAuthenticationComboBox")
            .ItemsSource =
            TargetEditorProjectionPolicy
                .AuthenticationChoices(
                    LinuxHostKind.RemoteLinux)
                .Select(value =>
                    value.ToString())
                .ToArray();
        Get<ComboBox>("ActivityFilterComboBox")
            .ItemsSource =
            new[]
            {
                "All activity",
                "Unread",
                "Notifications",
                "Targets",
                "Actions",
                "Failures",
                "Actionable"
            };
        Get<ComboBox>("ActivityFilterComboBox")
            .SelectedIndex = 0;

        InitializeTargetSessionState();

        _controlPlaneTimer.Interval =
            TimeSpan.FromSeconds(15);
        _controlPlaneTimer.Tick +=
            ControlPlaneTimer_OnTick;
        _controlPlaneTimer.Start();

        ApplyControlPlanePreferences();
        RefreshHostProfileLists();
        PopulateControlPlaneFoundation();

        RecordRoutineControlPlaneActivity(
            "System",
            _controlPlane.ActiveProfile.DisplayName,
            "GraveOps control plane started",
            "Host profiles, jobs, activity and maintenance state loaded.",
            "DashboardNav",
            TimeSpan.FromHours(6),
            unread: false);
    }

    private void DisposeControlPlaneFoundation()
    {
        _controlPlaneTimer.Stop();
    }

    private void ApplyControlPlanePreferences()
    {
        var seconds = NormalizeBackgroundRefreshSeconds(
            _operatorSettings.BackgroundRefreshSeconds);

        Get<TextBox>(
                "SettingsBackgroundRefreshSecondsTextBox")
            .Text = seconds.ToString(
                CultureInfo.InvariantCulture);
        Get<CheckBox>(
                "SettingsDesktopNotificationsCheckBox")
            .IsChecked =
            _operatorSettings.DesktopNotifications;

        _nextBackgroundRefreshAt =
            DateTimeOffset.Now +
            TimeSpan.FromSeconds(seconds);
    }

    private static int NormalizeBackgroundRefreshSeconds(
        int value) =>
        Math.Clamp(
            value <= 0 ? 60 : value,
            15,
            3600);

    private bool TryReadBackgroundRefreshSeconds(
        out int seconds)
    {
        var text =
            Get<TextBox>(
                    "SettingsBackgroundRefreshSecondsTextBox")
                .Text?
                .Trim();

        if (!int.TryParse(
                text,
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out seconds) ||
            seconds is < 15 or > 3600)
        {
            seconds = 60;
            Get<TextBlock>("SettingsSaveStatusText").Text =
                "Background refresh must be between 15 and 3600 seconds.";
            return false;
        }

        return true;
    }

    private async void ControlPlaneTimer_OnTick(
        object? sender,
        EventArgs e)
    {
        var maintenanceChanged =
            _controlPlane.State
                .ExpireMaintenanceIfNeeded();

        if (maintenanceChanged)
        {
            _controlPlane.State.RecordActivity(
                "Maintenance",
                _controlPlane.ActiveProfile.DisplayName,
                "Maintenance Mode expired",
                "Normal notifications and alert presentation resumed.",
                "DashboardNav");
            PopulateControlPlaneFoundation();
        }

        if (_controlPlaneCaptureBusy ||
            DateTimeOffset.Now <
            _nextBackgroundRefreshAt)
        {
            return;
        }

        var seconds =
            NormalizeBackgroundRefreshSeconds(
                _operatorSettings.BackgroundRefreshSeconds);

        _nextBackgroundRefreshAt =
            DateTimeOffset.Now +
            TimeSpan.FromSeconds(seconds);

        try
        {
            await RefreshAsync(
                background: true);
        }
        catch (OperationCanceledException)
        {
            // A newer manual refresh superseded this background request.
        }
    }

    private async Task<TargetSnapshotEnvelope<HostSnapshot>>
        CaptureActiveTargetAsync(
            LinuxHostProfile profile,
            TargetRefreshLease lease,
            bool background,
            CancellationToken cancellationToken)
    {
        if (_controlPlaneCaptureBusy)
        {
            throw new InvalidOperationException(
                "A control-plane capture is already running.");
        }

        _controlPlaneCaptureBusy =
            true;

        var jobId =
            _controlPlane.State.StartJob(
                background
                    ? "Background environment refresh"
                    : "Environment refresh",
                profile.DisplayName,
                profile.ConnectionSummary,
                background);

        try
        {
            var providerMessage =
                profile.Kind switch
                {
                    LinuxHostKind.Local =>
                        "Capturing the native Linux provider.",
                    LinuxHostKind.RemoteLinux =>
                        "Opening the fingerprint-pinned SSH provider.",
                    LinuxHostKind.RemoteWindows =>
                        "Opening the certificate-validated WinRM HTTPS provider.",
                    _ =>
                        "Opening the selected target provider."
                };

            _controlPlane.State.UpdateJob(
                jobId,
                25,
                providerMessage);

            var envelope =
                await _controlPlane.CaptureAsync(
                    profile,
                    lease,
                    cancellationToken);

            _controlPlane.State.UpdateJob(
                jobId,
                80,
                "Projecting services, containers, storage, logs and integrations.");

            _controlPlane.State.CompleteJob(
                jobId,
                success: true,
                $"{envelope.Snapshot.Hostname} captured.");

            if (!background)
            {
                _controlPlane.State.RecordActivity(
                    "Capture",
                    profile.DisplayName,
                    "Environment refreshed",
                    $"{envelope.Snapshot.Hostname} · " +
                    $"{envelope.Snapshot.OperatingSystem}",
                    "DashboardNav",
                    unread: false);
            }

            return envelope;
        }
        catch (OperationCanceledException)
        {
            _controlPlane.State.CompleteJob(
                jobId,
                success: false,
                "Refresh superseded or cancelled.");
            throw;
        }
        catch (Exception exception)
        {
            _controlPlane.State.CompleteJob(
                jobId,
                success: false,
                exception.Message);

            _controlPlane.State.RecordActivity(
                "Failure",
                profile.DisplayName,
                "Environment capture failed",
                exception.Message,
                "ServersNav");
            throw;
        }
        finally
        {
            _controlPlaneCaptureBusy =
                false;
        }
    }

    private async Task<OpsBackupSnapshot>
        CaptureTargetBackupAsync(
            LinuxHostProfile profile,
            HostSnapshot snapshot,
            CancellationToken cancellationToken)
    {
        if (profile.IsLocalLinux)
        {
            return await Task.Run(
                () =>
                    _backupProbe.CaptureAsync(
                        cancellationToken),
                cancellationToken);
        }

        return _controlPlane
            .CreateRemoteBackupSnapshot(
                snapshot);
    }

    private string ControlPlaneConnectionDetail() =>
        ControlPlaneConnectionDetail(
            _controlPlane.ActiveProfile);

    private static string ControlPlaneConnectionDetail(
        LinuxHostProfile profile) =>
        profile.Kind switch
        {
            LinuxHostKind.Local =>
                "Native Linux provider",
            LinuxHostKind.RemoteLinux =>
                $"SSH · {profile.Username}@" +
                $"{profile.Host}:{profile.Port}",
            LinuxHostKind.RemoteWindows =>
                $"WinRM HTTPS · {profile.Username}@" +
                $"{profile.Host}:{profile.Port}",
            LinuxHostKind.LocalWindows =>
                "Native Windows provider",
            _ =>
                profile.ConnectionSummary
        };

    private bool CanRunLocalMutations() =>
        _controlPlane.ActiveProfile.IsLocalLinux;

    private string ActiveTargetUrlHost() =>
        ActiveTargetUrlHost(
            _controlPlane.ActiveProfile);

    private static string ActiveTargetUrlHost(
        LinuxHostProfile profile)
    {
        var host =
            profile.IsLocal
                ? "127.0.0.1"
                : profile.Host.Trim();

        return host.Contains(
                ':',
                StringComparison.Ordinal) &&
               !host.StartsWith(
                   "[",
                   StringComparison.Ordinal)
            ? $"[{host}]"
            : host;
    }

    private void RecordRefreshFailure(
        LinuxHostProfile profile,
        Exception exception)
    {
        _controlPlane.State.RecordActivity(
            "Failure",
            profile.DisplayName,
            "Control-plane projection failed",
            exception.Message,
            "DashboardNav");

        PopulateControlPlaneFoundation();
    }

    private async void RecordRefreshSuccessAndNotify(
        LinuxHostProfile profile)
    {
        if (_snapshot is null ||
            _analysis is null)
        {
            return;
        }

        _controlPlane.Profiles.TouchDetection(
            profile.Id,
            _snapshot.CapturedAt);

        if (_analysis.Severity <
                OpsSeverity.Error ||
            _controlPlane.State
                .IsMaintenanceActive ||
            !_operatorSettings
                .DesktopNotifications)
        {
            PopulateControlPlaneFoundation();
            return;
        }

        var key =
            $"{profile.Id}|" +
            $"{_analysis.Severity}|" +
            $"{_analysis.Headline}";

        if (key.Equals(
                _lastNotificationKey,
                StringComparison.Ordinal))
        {
            PopulateControlPlaneFoundation();
            return;
        }

        _lastNotificationKey = key;

        await LinuxDesktopNotifier.NotifyAsync(
            $"GraveOps · {_analysis.Label}",
            $"{profile.DisplayName}: " +
            _analysis.Headline);

        _controlPlane.State.RecordActivity(
            "Notification",
            profile.DisplayName,
            _analysis.Headline,
            _analysis.RootCause,
            "IntelligenceNav");

        PopulateControlPlaneFoundation();
    }

    private void PopulateControlPlaneFoundation()
    {
        RefreshActiveTargetSelector();
        PopulateJobsDrawer();
        PopulateActivityDrawer();
        PopulateMaintenanceProjection();

        var activeProfile =
            _controlPlane.ActiveProfile;
        var currentSnapshot =
            _snapshot is not null &&
            _acceptedTargetId.Equals(
                activeProfile.Id,
                StringComparison.OrdinalIgnoreCase)
                ? _snapshot
                : null;

        ProjectActiveTargetShell(
            activeProfile,
            currentSnapshot);
        ApplyActiveTargetCapabilities();

        Get<TextBlock>("ServerKeyringStatusText").Text =
            _controlPlane.Credentials.CapabilityText;
        var savedTargets =
            _controlPlane.Profiles.Profiles;
        var remoteTargets =
            savedTargets.Count(item =>
                !item.IsLocal);
        var windowsTargets =
            savedTargets.Count(item =>
                item.IsWindows);

        Get<TextBlock>("ServerProfilesSummaryText").Text =
            $"{savedTargets.Count} saved " +
            $"{(savedTargets.Count == 1 ? "target" : "targets")} · " +
            $"{remoteTargets} remote · " +
            $"{windowsTargets} Windows";

        if (currentSnapshot is not null)
        {
            Get<TextBlock>("SidebarHostname").Text =
                currentSnapshot.Hostname;
            Get<TextBlock>("SidebarOperatingSystem").Text =
                currentSnapshot.OperatingSystem;
        }
        else
        {
            Get<TextBlock>("SidebarHostname").Text =
                activeProfile.DisplayName;
            Get<TextBlock>("SidebarOperatingSystem").Text =
                activeProfile.KindLabel;
        }

        var local = CanRunLocalMutations();

        if (!local)
        {
            Get<TextBlock>("ServiceActionStatusText").Text =
                "Remote target selected · service mutations are disabled in V4.2.";
            Get<TextBlock>("DockerActionStatusText").Text =
                "Remote target selected · Docker mutations are disabled in V4.2.";
        }
    }

    private void RefreshActiveTargetSelector()
    {
        var combo =
            Get<ComboBox>("ActiveTargetComboBox");
        var profiles =
            _controlPlane.Profiles.Profiles;
        var activeId =
            _controlPlane.ActiveProfile.Id;

        _targetSelectionBusy = true;

        try
        {
            combo.ItemsSource = profiles;
            combo.SelectedItem =
                profiles.FirstOrDefault(profile =>
                    profile.Id.Equals(
                        activeId,
                        StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            _targetSelectionBusy = false;
        }
    }

    private void RefreshHostProfileLists(
        string? selectedId = null)
    {
        var profiles =
            _controlPlane.Profiles.Profiles;
        var list =
            Get<ListBox>("ServerProfilesList");

        selectedId ??=
            (list.SelectedItem as LinuxHostProfile)?
                .Id ??
            _controlPlane.ActiveProfile.Id;

        _serverProfileBindingBusy = true;

        try
        {
            list.ItemsSource = profiles;
            list.SelectedItem =
                profiles.FirstOrDefault(profile =>
                    profile.Id.Equals(
                        selectedId,
                        StringComparison.OrdinalIgnoreCase)) ??
                profiles.FirstOrDefault();
        }
        finally
        {
            _serverProfileBindingBusy = false;
        }

        PopulateServerProfileForm();
        RefreshActiveTargetSelector();
    }

    private async void ActiveTargetComboBox_OnSelectionChanged(
        object? sender,
        SelectionChangedEventArgs e)
    {
        if (_targetSelectionBusy ||
            Get<ComboBox>("ActiveTargetComboBox")
                .SelectedItem is not
            LinuxHostProfile profile ||
            profile.Id.Equals(
                _controlPlane.ActiveProfile.Id,
                StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        await SwitchActiveTargetAsync(
            profile);
    }

    private void ServerProfilesList_OnSelectionChanged(
        object? sender,
        SelectionChangedEventArgs e)
    {
        if (_serverProfileBindingBusy)
            return;

        PopulateServerProfileForm();
    }

    private void PopulateServerProfileForm()
    {
        var selected =
            Get<ListBox>("ServerProfilesList")
                .SelectedItem as
            LinuxHostProfile;

        _serverProfileBindingBusy =
            true;

        try
        {
            if (selected is null)
            {
                Get<TextBlock>("ServerEditingIdText").Text =
                    string.Empty;
                Get<TextBox>("ServerNameTextBox").Text =
                    string.Empty;
                Get<TextBox>("ServerRoleTextBox").Text =
                    "Server";
                Get<ComboBox>("ServerConnectionTypeComboBox")
                    .SelectedItem =
                    LinuxHostKind.RemoteLinux.ToString();
                Get<TextBox>("ServerHostTextBox").Text =
                    string.Empty;
                Get<TextBox>("ServerPortTextBox").Text =
                    "22";
                Get<TextBox>("ServerUsernameTextBox").Text =
                    Environment.UserName;
                Get<ComboBox>("ServerAuthenticationComboBox")
                    .SelectedItem =
                    LinuxHostAuthentication.Agent.ToString();
                Get<TextBox>("ServerPrivateKeyPathTextBox").Text =
                    string.Empty;
                Get<TextBox>("ServerFingerprintTextBox").Text =
                    string.Empty;
                Get<TextBox>("ServerOperationTimeoutTextBox").Text =
                    "60";
                Get<TextBox>("ServerSecretTextBox").Text =
                    string.Empty;
                Get<CheckBox>("ServerSaveSecretCheckBox")
                    .IsChecked =
                    false;
                Get<Button>("DeleteServerButton").IsEnabled =
                    false;
                Get<Button>("SetActiveServerButton").IsEnabled =
                    false;
                Get<TextBlock>("ServerProfileStatusText").Text =
                    "Create a remote Linux or remote Windows target.";
                Get<ListBox>("ServerDetectedIntegrationsList")
                    .ItemsSource =
                    Array.Empty<string>();
            }
            else
            {
                Get<TextBlock>("ServerEditingIdText").Text =
                    selected.Id;
                Get<TextBox>("ServerNameTextBox").Text =
                    selected.Name;
                Get<TextBox>("ServerRoleTextBox").Text =
                    selected.Role;
                Get<ComboBox>("ServerConnectionTypeComboBox")
                    .SelectedItem =
                    selected.Kind.ToString();
                Get<TextBox>("ServerHostTextBox").Text =
                    selected.Host;
                Get<TextBox>("ServerPortTextBox").Text =
                    selected.Port.ToString(
                        CultureInfo.InvariantCulture);
                Get<TextBox>("ServerUsernameTextBox").Text =
                    selected.Username;
                var authenticationCombo =
                    Get<ComboBox>(
                        "ServerAuthenticationComboBox");
                authenticationCombo.ItemsSource =
                    TargetEditorProjectionPolicy
                        .AuthenticationChoices(
                            selected.Kind)
                        .Select(value =>
                            value.ToString())
                        .ToArray();
                authenticationCombo.SelectedItem =
                    selected.Authentication.ToString();
                Get<TextBox>("ServerPrivateKeyPathTextBox").Text =
                    selected.PrivateKeyPath;
                Get<TextBox>("ServerFingerprintTextBox").Text =
                    selected.HostKeyFingerprint;
                Get<TextBox>("ServerOperationTimeoutTextBox").Text =
                    selected.OperationTimeoutSeconds.ToString(
                        CultureInfo.InvariantCulture);
                Get<TextBox>("ServerSecretTextBox").Text =
                    string.Empty;
                Get<CheckBox>("ServerSaveSecretCheckBox")
                    .IsChecked =
                    false;

                Get<Button>("DeleteServerButton").IsEnabled =
                    !selected.IsLocal;
                Get<Button>("SetActiveServerButton").IsEnabled =
                    !selected.Id.Equals(
                        _controlPlane.ActiveProfile.Id,
                        StringComparison.OrdinalIgnoreCase);

                Get<TextBlock>("ServerProfileStatusText").Text =
                    selected.IsLocalLinux
                        ? "Local Linux uses the native provider and does not loop back through SSH."
                        : selected.LastDetectedAt is { } detected
                            ? $"Last detected {detected.ToLocalTime():g}."
                            : "The target has not completed provider detection.";

                Get<ListBox>("ServerDetectedIntegrationsList")
                    .ItemsSource =
                    selected.Id.Equals(
                            _controlPlane.ActiveProfile.Id,
                            StringComparison.OrdinalIgnoreCase) &&
                        _identityResolution.Records.Count > 0
                        ? _identityResolution.Records
                            .Select(IdentityServerSummary)
                            .ToArray()
                        : Array.Empty<string>();
            }
        }
        finally
        {
            _serverProfileBindingBusy =
                false;
        }

        UpdateServerFormCapability();
    }

    private void ServerConnectionTypeComboBox_OnSelectionChanged(
        object? sender,
        SelectionChangedEventArgs e)
    {
        if (_serverProfileBindingBusy)
            return;

        var kind =
            ParseEnum(
                Get<ComboBox>(
                        "ServerConnectionTypeComboBox")
                    .SelectedItem as string,
                LinuxHostKind.RemoteLinux);

        var projection =
            TargetEditorProjectionPolicy.Create(
                kind,
                ParseEnum(
                    Get<ComboBox>(
                            "ServerAuthenticationComboBox")
                        .SelectedItem as string,
                    LinuxHostAuthentication.Agent),
                _controlPlane.Credentials.IsAvailable);

        if (Get<ListBox>("ServerProfilesList")
                .SelectedItem is null)
        {
            Get<TextBox>("ServerPortTextBox").Text =
                projection.DefaultPort.ToString(
                    CultureInfo.InvariantCulture);
        }

        UpdateServerFormCapability();
    }

    private void ServerAuthenticationComboBox_OnSelectionChanged(
        object? sender,
        SelectionChangedEventArgs e)
    {
        if (_serverProfileBindingBusy)
            return;

        UpdateServerFormCapability();
    }

    private static string IdentityServerSummary(
        ApplicationIdentityRecord item) =>
        $"{item.DisplayName} · {item.Product} · " +
        $"{item.Role} · {item.VerificationLabel} · " +
        $"{item.Protocol} · {item.Kind} · {item.Evidence}";

    private void UpdateServerFormCapability()
    {
        var selected =
            Get<ListBox>("ServerProfilesList")
                .SelectedItem as
            LinuxHostProfile;
        var kind =
            ParseEnum(
                Get<ComboBox>(
                        "ServerConnectionTypeComboBox")
                    .SelectedItem as string,
                LinuxHostKind.RemoteLinux);
        var requestedAuthentication =
            ParseEnum(
                Get<ComboBox>(
                        "ServerAuthenticationComboBox")
                    .SelectedItem as string,
                LinuxHostAuthentication.Agent);

        var choices =
            TargetEditorProjectionPolicy
                .AuthenticationChoices(
                    kind);
        var authentication =
            TargetEditorProjectionPolicy
                .NormalizeAuthentication(
                    kind,
                    requestedAuthentication);
        var authenticationCombo =
            Get<ComboBox>(
                "ServerAuthenticationComboBox");

        _serverProfileBindingBusy =
            true;

        try
        {
            authenticationCombo.ItemsSource =
                choices
                    .Select(value =>
                        value.ToString())
                    .ToArray();
            authenticationCombo.SelectedItem =
                authentication.ToString();
        }
        finally
        {
            _serverProfileBindingBusy =
                false;
        }

        var projection =
            TargetEditorProjectionPolicy.Create(
                kind,
                authentication,
                _controlPlane.Credentials.IsAvailable);

        var localProfile =
            selected?.IsLocal ==
            true;

        Get<ComboBox>("ServerConnectionTypeComboBox")
            .IsEnabled =
            !localProfile;

        Get<Border>("ServerLocalProviderPanel")
            .IsVisible =
            projection.IsLocal;
        Get<Border>("ServerRemoteConnectionPanel")
            .IsVisible =
            projection.ShowRemoteConnection;
        Get<Border>("ServerPrivateKeyPanel")
            .IsVisible =
            projection.ShowPrivateKey;
        Get<Border>("ServerFingerprintPanel")
            .IsVisible =
            projection.ShowPinnedIdentity;
        Get<Border>("ServerSecretPanel")
            .IsVisible =
            projection.ShowSecret;
        Get<Border>("ServerWindowsOptionsPanel")
            .IsVisible =
            projection.ShowWindowsOptions;

        Get<TextBox>("ServerHostTextBox")
            .IsEnabled =
            projection.ShowRemoteConnection;
        Get<TextBox>("ServerPortTextBox")
            .IsEnabled =
            projection.ShowRemoteConnection;
        Get<TextBox>("ServerUsernameTextBox")
            .IsEnabled =
            projection.ShowRemoteConnection;
        authenticationCombo.IsEnabled =
            projection.ShowRemoteConnection;
        Get<TextBox>("ServerPrivateKeyPathTextBox")
            .IsEnabled =
            projection.ShowPrivateKey;
        Get<Button>("BrowsePrivateKeyButton")
            .IsEnabled =
            projection.ShowPrivateKey;
        Get<TextBox>("ServerFingerprintTextBox")
            .IsEnabled =
            projection.ShowPinnedIdentity;
        Get<Button>("ScanFingerprintButton")
            .IsVisible =
            projection.ShowFingerprintScan;
        Get<Button>("ScanFingerprintButton")
            .IsEnabled =
            projection.ShowFingerprintScan;
        Get<TextBox>("ServerSecretTextBox")
            .IsEnabled =
            projection.ShowSecret;
        Get<CheckBox>("ServerSaveSecretCheckBox")
            .IsEnabled =
            projection.CanSaveSecret;
        Get<TextBox>("ServerOperationTimeoutTextBox")
            .IsEnabled =
            projection.ShowWindowsOptions;

        Get<TextBlock>("ServerFingerprintLabelText")
            .Text =
            projection.PinnedIdentityLabel;
        Get<TextBlock>("ServerFingerprintHelpText")
            .Text =
            projection.PinnedIdentityHelp;
        Get<TextBlock>("ServerSecretLabelText")
            .Text =
            projection.SecretLabel;
        Get<TextBlock>("ServerProfileModeText")
            .Text =
            projection.ModeText;

        Get<Button>("ServerSaveButton")
            .IsEnabled =
            kind !=
            LinuxHostKind.LocalWindows;
        var testButton =
            Get<Button>(
                "ServerTestButton");
        testButton.IsVisible =
            projection.ShowRemoteConnection;
        testButton.IsEnabled =
            projection.ShowRemoteConnection;
        testButton.Content =
            projection.IsRemoteWindows
                ? "Test WinRM"
                : "Test SSH";
        Get<Button>("ServerDetectButton")
            .IsEnabled =
            selected is not null;
    }

    private static T ParseEnum<T>(
        string? value,
        T fallback)
        where T : struct, Enum =>
        Enum.TryParse<T>(
            value,
            ignoreCase: true,
            out var parsed)
            ? parsed
            : fallback;

    private LinuxHostProfile ReadServerProfileForm()
    {
        var id =
            Get<TextBlock>("ServerEditingIdText")
                .Text?
                .Trim();

        if (string.IsNullOrWhiteSpace(
                id))
        {
            id =
                Guid.NewGuid()
                    .ToString("N");
        }

        var kind =
            ParseEnum(
                Get<ComboBox>(
                        "ServerConnectionTypeComboBox")
                    .SelectedItem as string,
                LinuxHostKind.RemoteLinux);

        var projection =
            TargetEditorProjectionPolicy.Create(
                kind,
                ParseEnum(
                    Get<ComboBox>(
                            "ServerAuthenticationComboBox")
                        .SelectedItem as string,
                    LinuxHostAuthentication.Agent),
                _controlPlane.Credentials.IsAvailable);

        if (!int.TryParse(
                Get<TextBox>("ServerPortTextBox")
                    .Text,
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out var port))
        {
            port =
                projection.DefaultPort;
        }

        if (!int.TryParse(
                Get<TextBox>(
                        "ServerOperationTimeoutTextBox")
                    .Text,
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out var operationTimeout))
        {
            operationTimeout =
                60;
        }

        var existing =
            _controlPlane.Profiles.Find(
                id);

        return new LinuxHostProfile
        {
            Id =
                id,
            Name =
                Get<TextBox>("ServerNameTextBox")
                    .Text?
                    .Trim() ??
                string.Empty,
            Role =
                Get<TextBox>("ServerRoleTextBox")
                    .Text?
                    .Trim() ??
                "Server",
            Kind =
                kind,
            Host =
                Get<TextBox>("ServerHostTextBox")
                    .Text?
                    .Trim() ??
                string.Empty,
            Port =
                port,
            Username =
                Get<TextBox>("ServerUsernameTextBox")
                    .Text?
                    .Trim() ??
                string.Empty,
            Authentication =
                projection.Authentication,
            PrivateKeyPath =
                Get<TextBox>(
                        "ServerPrivateKeyPathTextBox")
                    .Text?
                    .Trim() ??
                string.Empty,
            HostKeyFingerprint =
                Get<TextBox>(
                        "ServerFingerprintTextBox")
                    .Text?
                    .Trim() ??
                string.Empty,
            OperationTimeoutSeconds =
                operationTimeout,
            CredentialReference =
                existing?.CredentialReference ??
                string.Empty,
            LastDetectedAt =
                existing?.LastDetectedAt
        };
    }

    private void NewServerButton_OnClick(
        object? sender,
        RoutedEventArgs e)
    {
        _serverProfileBindingBusy = true;

        try
        {
            Get<ListBox>("ServerProfilesList")
                .SelectedItem = null;
        }
        finally
        {
            _serverProfileBindingBusy = false;
        }

        PopulateServerProfileForm();
        Get<TextBlock>("ServerProfileStatusText").Text =
            "New remote Linux or remote Windows target.";
    }

    private async void SaveServerButton_OnClick(
        object? sender,
        RoutedEventArgs e)
    {
        var status =
            Get<TextBlock>(
                "ServerProfileStatusText");

        try
        {
            var profile =
                ReadServerProfileForm();
            var secret =
                Get<TextBox>("ServerSecretTextBox")
                    .Text ??
                string.Empty;
            var saveSecret =
                Get<CheckBox>(
                        "ServerSaveSecretCheckBox")
                    .IsChecked == true;

            await _controlPlane.SaveProfileAsync(
                profile,
                secret,
                saveSecret);

            Get<TextBox>("ServerSecretTextBox").Text =
                string.Empty;
            Get<CheckBox>("ServerSaveSecretCheckBox")
                .IsChecked = false;

            _controlPlane.State.RecordActivity(
                "Target",
                profile.DisplayName,
                "Host profile saved",
                profile.ConnectionSummary,
                "ServersNav");

            RefreshHostProfileLists(
                profile.Id);
            status.Text =
                "Profile saved. Secrets remain in the desktop keyring only.";
            PopulateControlPlaneFoundation();
        }
        catch (Exception exception)
        {
            status.Text =
                $"Could not save profile: {exception.Message}";
        }
    }

    private async void DeleteServerButton_OnClick(
        object? sender,
        RoutedEventArgs e)
    {
        if (Get<ListBox>("ServerProfilesList")
                .SelectedItem is not
            LinuxHostProfile profile ||
            profile.IsLocal)
        {
            return;
        }

        if (!await ConfirmActionAsync(
                $"Delete {profile.DisplayName}?",
                "This removes the target profile, its pinned identity reference and stored GraveOps credentials. It does not modify the remote system."))
        {
            return;
        }

        var wasActive =
            profile.Id.Equals(
                _controlPlane.ActiveProfile.Id,
                StringComparison.OrdinalIgnoreCase);

        if (wasActive)
        {
            var local =
                _controlPlane.Profiles.Find(
                    "local") ??
                throw new InvalidOperationException(
                    "The local Linux target is missing.");

            await SwitchActiveTargetAsync(
                local);
        }

        await _controlPlane.DeleteProfileAsync(
            profile.Id);
        ForgetApplicationInventory(
            profile.Id);

        _controlPlane.State.RecordActivity(
            "Target",
            profile.DisplayName,
            "Host profile deleted",
            "The remote system was not modified.",
            "ServersNav");

        RefreshHostProfileLists("local");
        PopulateControlPlaneFoundation();
    }

    private async void ScanFingerprintButton_OnClick(
        object? sender,
        RoutedEventArgs e)
    {
        var status =
            Get<TextBlock>(
                "ServerProfileStatusText");

        try
        {
            var profile =
                ReadServerProfileForm();

            if (!profile.IsRemoteLinux)
            {
                status.Text =
                    "Automatic scanning is available only for SSH host keys. A Windows certificate pin is optional and entered manually after verifying the server certificate.";
                return;
            }

            var job =
                _controlPlane.State.StartJob(
                    "SSH fingerprint scan",
                    profile.DisplayName,
                    $"{profile.Host}:{profile.Port}",
                    background: false);

            PopulateControlPlaneFoundation();

            var result =
                await _controlPlane
                    .ScanFingerprintAsync(
                        profile);

            _controlPlane.State.CompleteJob(
                job,
                result.Success,
                result.Success
                    ? result.Fingerprint
                    : result.Detail);

            if (result.Success)
            {
                Get<TextBox>(
                        "ServerFingerprintTextBox")
                    .Text =
                    result.Fingerprint;
            }

            status.Text =
                result.Success
                    ? $"Scanned {result.Fingerprint}. Save the profile to pin it."
                    : $"{result.Summary} {result.Detail}";
        }
        catch (Exception exception)
        {
            status.Text =
                $"Fingerprint scan failed: {exception.Message}";
        }
        finally
        {
            PopulateControlPlaneFoundation();
        }
    }

    private async void TestServerButton_OnClick(
        object? sender,
        RoutedEventArgs e)
    {
        var status =
            Get<TextBlock>(
                "ServerProfileStatusText");

        try
        {
            var profile =
                ReadServerProfileForm();
            var secret =
                Get<TextBox>("ServerSecretTextBox")
                    .Text;
            var job =
                _controlPlane.State.StartJob(
                    "Test host connection",
                    profile.DisplayName,
                    profile.ConnectionSummary,
                    background: false);

            PopulateControlPlaneFoundation();

            var result =
                await _controlPlane.TestAsync(
                    profile,
                    secret);

            _controlPlane.State.CompleteJob(
                job,
                result.Success,
                result.Detail);

            if (!string.IsNullOrWhiteSpace(
                    result.Fingerprint))
            {
                Get<TextBox>(
                        "ServerFingerprintTextBox")
                    .Text =
                    result.Fingerprint;
            }

            _controlPlane.State.RecordActivity(
                result.Success
                    ? "Target"
                    : "Failure",
                profile.DisplayName,
                result.Success
                    ? "Connection test passed"
                    : "Connection test failed",
                result.Detail,
                "ServersNav");

            status.Text =
                $"{result.Summary} {result.Detail}";
        }
        catch (Exception exception)
        {
            status.Text =
                $"Connection test failed: {exception.Message}";
        }
        finally
        {
            PopulateControlPlaneFoundation();
        }
    }

    private async void DetectServerButton_OnClick(
        object? sender,
        RoutedEventArgs e)
    {
        var selected =
            Get<ListBox>("ServerProfilesList")
                .SelectedItem as
            LinuxHostProfile;

        if (selected is null)
        {
            Get<TextBlock>("ServerProfileStatusText").Text =
                "Save the profile before detecting integrations.";
            return;
        }

        if (!_controlPlane.ActiveProfile.Id.Equals(
                selected.Id,
                StringComparison.OrdinalIgnoreCase))
        {
            await SwitchActiveTargetAsync(
                selected);
        }
        else
        {
            await RefreshAsync();
        }

        Get<ListBox>("ServerDetectedIntegrationsList")
            .ItemsSource =
            _identityResolution.Records
                .Select(IdentityServerSummary)
                .ToArray();

        Get<TextBlock>("ServerProfileStatusText").Text =
            $"{_integrations.Count} integrations detected for {selected.DisplayName}.";
    }

    private async void SetActiveServerButton_OnClick(
        object? sender,
        RoutedEventArgs e)
    {
        if (Get<ListBox>("ServerProfilesList")
                .SelectedItem is not
            LinuxHostProfile profile)
        {
            return;
        }

        await SwitchActiveTargetAsync(
            profile);
    }

    private async void BrowsePrivateKeyButton_OnClick(
        object? sender,
        RoutedEventArgs e)
    {
        var files =
            await StorageProvider
                .OpenFilePickerAsync(
                    new FilePickerOpenOptions
                    {
                        Title =
                            "Select SSH private key",
                        AllowMultiple = false
                    });

        var path =
            files.FirstOrDefault()?
                .TryGetLocalPath();

        if (!string.IsNullOrWhiteSpace(path))
        {
            Get<TextBox>(
                    "ServerPrivateKeyPathTextBox")
                .Text = path;
        }
    }

    private async void DiscoverLanButton_OnClick(
        object? sender,
        RoutedEventArgs e)
    {
        var status =
            Get<TextBlock>(
                "ServerProfileStatusText");
        status.Text =
            "Reading the local neighbor table...";

        var rows =
            await _controlPlane
                .DiscoverLanAsync();

        Get<ListBox>("ServerLanCandidatesList")
            .ItemsSource = rows;

        status.Text =
            rows.Count == 0
                ? "No reachable LAN neighbors were present in the current neighbor table."
                : $"{rows.Count} LAN neighbor candidate(s) found.";
    }

    private void ServerLanCandidatesList_OnSelectionChanged(
        object? sender,
        SelectionChangedEventArgs e)
    {
        if (Get<ListBox>(
                "ServerLanCandidatesList")
                .SelectedItem is
            LinuxLanCandidate candidate)
        {
            Get<TextBox>("ServerHostTextBox").Text =
                candidate.Address;
        }
    }

    private void JobsButton_OnClick(
        object? sender,
        RoutedEventArgs e)
    {
        CloseControlPlaneDrawers(
            except: "JobsDrawer");
        var drawer =
            Get<Border>("JobsDrawer");
        drawer.IsVisible =
            !drawer.IsVisible;
        PopulateJobsDrawer();
    }

    private void JobsCloseButton_OnClick(
        object? sender,
        RoutedEventArgs e) =>
        Get<Border>("JobsDrawer")
            .IsVisible = false;

    private void ClearCompletedJobsButton_OnClick(
        object? sender,
        RoutedEventArgs e)
    {
        _controlPlane.State
            .ClearCompletedJobs();
        PopulateJobsDrawer();
    }

    private void ActivityButton_OnClick(
        object? sender,
        RoutedEventArgs e)
    {
        CloseControlPlaneDrawers(
            except: "ActivityDrawer");
        var drawer =
            Get<Border>("ActivityDrawer");
        drawer.IsVisible =
            !drawer.IsVisible;

        if (drawer.IsVisible)
        {
            _controlPlane.State
                .MarkAllActivitiesRead();
        }

        PopulateActivityDrawer();
    }

    private void ActivityCloseButton_OnClick(
        object? sender,
        RoutedEventArgs e) =>
        Get<Border>("ActivityDrawer")
            .IsVisible = false;

    private void ActivityFilterComboBox_OnSelectionChanged(
        object? sender,
        SelectionChangedEventArgs e) =>
        PopulateActivityDrawer();

    private void MarkActivityReadButton_OnClick(
        object? sender,
        RoutedEventArgs e)
    {
        _controlPlane.State
            .MarkAllActivitiesRead();
        PopulateActivityDrawer();
    }

    private async void ClearActivityButton_OnClick(
        object? sender,
        RoutedEventArgs e)
    {
        if (!await ConfirmActionAsync(
                "Clear GraveOps activity?",
                "This removes the local bounded control-plane activity timeline. It does not modify system or application logs."))
        {
            return;
        }

        _controlPlane.State
            .ClearActivities();
        PopulateActivityDrawer();
    }

    private void ActivityList_OnDoubleTapped(
        object? sender,
        TappedEventArgs e)
    {
        if (Get<ListBox>("ActivityList")
                .SelectedItem is not
            ControlPlaneActivityRow row ||
            string.IsNullOrWhiteSpace(
                row.NavigationName))
        {
            return;
        }

        Get<Border>("ActivityDrawer")
            .IsVisible = false;
        Navigate(row.NavigationName);
    }

    private async void MaintenanceButton_OnClick(
        object? sender,
        RoutedEventArgs e)
    {
        var duration =
            await ShowMaintenanceDialogAsync();

        if (duration == TimeSpan.MinValue)
            return;

        _controlPlane.State.SetMaintenance(
            duration is null ||
            duration == TimeSpan.Zero
                ? null
                : duration);

        _controlPlane.State.RecordActivity(
            "Maintenance",
            _controlPlane.ActiveProfile.DisplayName,
            duration is null ||
            duration == TimeSpan.Zero
                ? "Maintenance Mode disabled"
                : "Maintenance Mode enabled",
            duration is null ||
            duration == TimeSpan.Zero
                ? "Normal notifications resumed."
                : $"Expected-noise suppression expires in {FormatDuration(duration.Value)}.",
            "DashboardNav");

        PopulateControlPlaneFoundation();
    }

    private async Task<TimeSpan?>
        ShowMaintenanceDialogAsync()
    {
        var dialog = new Window
        {
            Title = "Maintenance Mode",
            Width = 530,
            Height = 320,
            CanResize = false,
            WindowStartupLocation =
                WindowStartupLocation.CenterOwner,
            Background =
                new SolidColorBrush(
                    Color.Parse("#111113"))
        };

        var off =
            new Button { Content = "Turn off" };
        var thirty =
            new Button { Content = "30 minutes" };
        var oneHour =
            new Button { Content = "1 hour" };
        var fourHours =
            new Button { Content = "4 hours" };
        var eightHours =
            new Button { Content = "8 hours" };
        var cancel =
            new Button { Content = "Cancel" };

        off.Click += (_, _) =>
            dialog.Close("off");
        thirty.Click += (_, _) =>
            dialog.Close("30");
        oneHour.Click += (_, _) =>
            dialog.Close("60");
        fourHours.Click += (_, _) =>
            dialog.Close("240");
        eightHours.Click += (_, _) =>
            dialog.Close("480");
        cancel.Click += (_, _) =>
            dialog.Close("cancel");

        dialog.Content = new Border
        {
            Padding = new Thickness(24),
            Child = new StackPanel
            {
                Spacing = 16,
                Children =
                {
                    new TextBlock
                    {
                        Text = "Timed Maintenance Mode",
                        FontSize = 20,
                        FontWeight =
                            FontWeight.SemiBold
                    },
                    new TextBlock
                    {
                        Text =
                            "Monitoring continues and critical conditions remain visible. Desktop notifications are suppressed until the timer expires.",
                        Classes = { "muted" },
                        TextWrapping =
                            TextWrapping.Wrap
                    },
                    new WrapPanel
                    {
                        Children =
                        {
                            thirty,
                            oneHour,
                            fourHours,
                            eightHours
                        }
                    },
                    new StackPanel
                    {
                        Orientation =
                            Orientation.Horizontal,
                        HorizontalAlignment =
                            HorizontalAlignment.Right,
                        Spacing = 8,
                        Children =
                        {
                            off,
                            cancel
                        }
                    }
                }
            }
        };

        var result =
            await dialog.ShowDialog<string>(
                this);

        if (result is null ||
            result.Equals(
                "cancel",
                StringComparison.Ordinal))
        {
            return TimeSpan.MinValue;
        }

        if (result.Equals(
                "off",
                StringComparison.Ordinal))
        {
            return null;
        }

        return double.TryParse(
                result,
                NumberStyles.Float,
                CultureInfo.InvariantCulture,
                out var minutes)
            ? TimeSpan.FromMinutes(minutes)
            : TimeSpan.MinValue;
    }

    private void PopulateJobsDrawer()
    {
        Get<ListBox>("JobsList").ItemsSource =
            _controlPlane.State.Jobs;
        Get<Button>("JobsButton").Content =
            _controlPlane.State.RunningJobCount > 0
                ? $"Jobs · {_controlPlane.State.RunningJobCount}"
                : "Jobs";
        Get<TextBlock>("JobsSummaryText").Text =
            $"{_controlPlane.State.RunningJobCount} running · " +
            $"{_controlPlane.State.Jobs.Count} retained";
    }

    private void PopulateActivityDrawer()
    {
        var filter =
            Get<ComboBox>(
                    "ActivityFilterComboBox")
                .SelectedItem as string ??
            "All activity";

        var rows =
            _controlPlane.State.Activities
                .Where(row =>
                    filter switch
                    {
                        "Unread" =>
                            row.IsUnread,
                        "Notifications" =>
                            row.Kind.Equals(
                                "Notification",
                                StringComparison.OrdinalIgnoreCase),
                        "Targets" =>
                            row.Kind.Equals(
                                "Target",
                                StringComparison.OrdinalIgnoreCase),
                        "Actions" =>
                            row.Kind.Equals(
                                "Action",
                                StringComparison.OrdinalIgnoreCase),
                        "Failures" =>
                            row.Kind.Equals(
                                "Failure",
                                StringComparison.OrdinalIgnoreCase),
                        "Actionable" =>
                            row.Kind.Equals(
                                "Failure",
                                StringComparison.OrdinalIgnoreCase) ||
                            row.Kind.Equals(
                                "Notification",
                                StringComparison.OrdinalIgnoreCase) ||
                            (
                                row.Kind.Equals(
                                    "Action",
                                    StringComparison.OrdinalIgnoreCase) &&
                                (
                                    row.Title.Contains(
                                        "failed",
                                        StringComparison.OrdinalIgnoreCase) ||
                                    row.Detail.Contains(
                                        "failed",
                                        StringComparison.OrdinalIgnoreCase) ||
                                    row.Detail.Contains(
                                        "error",
                                        StringComparison.OrdinalIgnoreCase)
                                )
                            ),
                        _ => true
                    })
                .ToArray();

        Get<ListBox>("ActivityList").ItemsSource =
            rows;
        Get<Button>("ActivityButton").Content =
            _controlPlane.State
                .UnreadActivityCount > 0
                ? $"Activity · {_controlPlane.State.UnreadActivityCount}"
                : "Activity";
        Get<TextBlock>("ActivitySummaryText").Text =
            $"{rows.Length} shown · " +
            $"{_controlPlane.State.Activities.Count} retained";
    }

    private void PopulateMaintenanceProjection()
    {
        var active =
            _controlPlane.State
                .IsMaintenanceActive;

        Get<Button>("MaintenanceButton").Content =
            active
                ? $"Maintenance · {FormatDuration(_controlPlane.State.MaintenanceRemaining)}"
                : "Maintenance";

        if (active)
        {
            Get<TextBlock>("FooterModeText").Text =
                Get<CheckBox>("SafeModeCheckBox")
                    .IsChecked == true
                    ? "MAINTENANCE · SAFE MODE"
                    : "MAINTENANCE · NORMAL";
        }
    }

    private static string FormatDuration(
        TimeSpan duration)
    {
        if (duration.TotalHours >= 1)
            return $"{Math.Ceiling(duration.TotalHours):0}h";

        return $"{Math.Max(1, Math.Ceiling(duration.TotalMinutes)):0}m";
    }

    private void CloseControlPlaneDrawers(
        string? except = null)
    {
        foreach (var name in new[]
                 {
                     "OverviewDrawer",
                     "JobsDrawer",
                     "ActivityDrawer"
                 })
        {
            if (!name.Equals(
                    except,
                    StringComparison.Ordinal))
            {
                Get<Border>(name)
                    .IsVisible = false;
            }
        }
    }

    private void ApplyRemoteArrTelemetryBoundary()
    {
        var profile =
            _controlPlane.ActiveProfile;
        var instances =
            ActiveArrInstances();

        Get<ListBox>("ArrInstanceTelemetryList")
            .ItemsSource =
            instances.Select(instance =>
                new ArrServiceTelemetryRow(
                    instance.InstanceKey,
                    instance.DisplayName,
                    ResolveIntegrationUrl(
                        instance.Integration) ??
                    instance.Endpoint ??
                    "--",
                    "--",
                    "Remote",
                    "--",
                    "Host evidence only",
                    instance.SeverityLabel))
            .ToArray();

        Get<ListBox>("ArrQueueHealthList")
            .ItemsSource =
            new[]
            {
                new ArrWorkItemRow(
                    _activeArrProduct,
                    "Remote",
                    "Credential-safe remote application API connector is scheduled for the media parity wave",
                    "Host evidence",
                    string.Empty,
                    string.Empty,
                    $"Target · {profile.ConnectionSummary}")
            };

        Get<TextBlock>("ArrStateMetricText").Text =
            instances.Length > 0
                ? "REMOTE"
                : "NOT DETECTED";
        Get<TextBlock>("ArrVersionMetricText").Text =
            "--";
        Get<TextBlock>("ArrWorkMetricText").Text =
            "--";
        Get<TextBlock>("ArrHealthMetricText").Text =
            "--";
        Get<TextBlock>("ArrLiveUpdatedText").Text =
            "Remote host evidence";
        Get<TextBlock>("ArrQueueFooterText").Text =
            "No local API endpoint was queried while a remote target was selected.";
    }
}
