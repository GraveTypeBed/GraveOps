using Avalonia.Controls;
using GraveOps.Core.Hosts;
using GraveOps.Core.Snapshots;
using GraveOps.Core.Targets;
using GraveOps.Platform.Linux;

namespace GraveOps.Desktop.Linux;

public partial class MainWindow
{
    private readonly TargetRefreshCoordinator
        _targetRefreshCoordinator =
            new();

    private TargetCapabilities
        _activeTargetCapabilities =
            TargetCapabilities.Empty;

    private string _acceptedTargetId =
        string.Empty;

    private string _lastBackupTargetId =
        string.Empty;

    private sealed record LinuxTargetRefreshContext(
        LinuxHostProfile Profile,
        TargetRefreshLease Lease,
        string ProviderId,
        TargetCapabilities Capabilities);

    private void InitializeTargetSessionState()
    {
        var profile =
            SnapshotTargetProfile(
                _controlPlane.ActiveProfile);

        _targetRefreshCoordinator.Select(
            profile.Id);
        _activeTargetCapabilities =
            LinuxTargetCapabilityCatalog.ForTarget(
                profile.IsLocal);
        _acceptedTargetId =
            string.Empty;

        ApplyActiveTargetCapabilities();
        ProjectActiveTargetShell(
            profile,
            snapshot: null);
    }

    private LinuxTargetRefreshContext
        BeginTargetRefreshContext()
    {
        var profile =
            SnapshotTargetProfile(
                _controlPlane.ActiveProfile);

        if (!_targetRefreshCoordinator
                .CurrentSelection
                .TargetId
                .Equals(
                    profile.Id,
                    StringComparison.Ordinal))
        {
            _targetRefreshCoordinator.Select(
                profile.Id);
        }

        var capabilities =
            LinuxTargetCapabilityCatalog.ForTarget(
                profile.IsLocal);

        return new LinuxTargetRefreshContext(
            profile,
            _targetRefreshCoordinator.BeginRefresh(),
            profile.IsLocal
                ? "linux.local"
                : "linux.ssh",
            capabilities);
    }

    private TargetSnapshotEnvelope<HostSnapshot>
        CreateTargetSnapshotEnvelope(
            LinuxTargetRefreshContext context,
            HostSnapshot snapshot) =>
        new(
            context.Lease,
            context.ProviderId,
            snapshot.CapturedAt,
            context.Capabilities,
            snapshot);

    private bool IsTargetRefreshCurrent(
        LinuxTargetRefreshContext context) =>
        _targetRefreshCoordinator.IsCurrent(
            context.Lease) &&
        _controlPlane.ActiveProfile.Id.Equals(
            context.Profile.Id,
            StringComparison.OrdinalIgnoreCase);

    private void EnsureTargetRefreshCurrent(
        LinuxTargetRefreshContext context)
    {
        if (!IsTargetRefreshCurrent(
                context))
        {
            throw new OperationCanceledException(
                "The refresh belongs to an earlier target selection or refresh generation.");
        }
    }

    private async Task SwitchActiveTargetAsync(
        LinuxHostProfile profile)
    {
        ArgumentNullException.ThrowIfNull(
            profile);

        if (profile.Id.Equals(
                _controlPlane.ActiveProfile.Id,
                StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        CancelActiveRefreshForTargetSwitch();

        _controlPlane.SetActive(
            profile.Id);
        _targetRefreshCoordinator.Select(
            profile.Id);
        _activeTargetCapabilities =
            LinuxTargetCapabilityCatalog.ForTarget(
                profile.IsLocal);

        ResetTargetScopedState(
            profile);

        _controlPlane.State.RecordActivity(
            "Target",
            profile.DisplayName,
            "Active target changed",
            profile.ConnectionSummary,
            "ServersNav");

        SetControlPlaneState(
            OpsSeverity.Info,
            "SWITCHING",
            profile.ConnectionSummary);

        RefreshHostProfileLists(
            profile.Id);
        UpdateActionButtons();
        PopulateControlPlaneFoundation();

        await RefreshAsync();
    }

    private void CancelActiveRefreshForTargetSwitch()
    {
        try
        {
            Volatile.Read(
                    ref _activeRefreshCancellation)
                ?.Cancel();
        }
        catch (ObjectDisposedException)
        {
            // The completing refresh already disposed its request.
        }
    }

    private void ResetTargetScopedState(
        LinuxHostProfile profile)
    {
        _snapshot = null;
        _backup = null;
        _rawAnalysis = null;
        _analysis = null;
        _policyEvaluation = null;
        _rawLifecycle =
            Array.Empty<OpsLifecycleStage>();
        _lifecycle =
            Array.Empty<OpsLifecycleStage>();
        _integrations =
            Array.Empty<OpsIntegration>();
        _identityResolution =
            ApplicationIdentityResolution.Empty;
        _logs =
            Array.Empty<OpsLogGroup>();
        _arrWorkspaceRows =
            Array.Empty<ArrWorkspaceView>();
        _mediaRows =
            Array.Empty<LinuxMediaApplicationRow>();
        _arrTelemetrySnapshot = null;
        _arrTelemetryProduct =
            string.Empty;
        _downloadClientCache.Clear();
        _commandPaletteSnapshotKey =
            string.Empty;
        _unifiedDashboardCards =
            Array.Empty<UnifiedDashboardCard>();
        _lastNotificationKey =
            string.Empty;
        _acceptedTargetId =
            string.Empty;
        _lastBackupTargetId =
            string.Empty;
        _lastBackupCaptureAt =
            DateTimeOffset.MinValue;

        var dashboard =
            this.FindControl<StackPanel>(
                "UnifiedDashboardCardsPanel");
        dashboard?.Children.Clear();

        var dashboardStatus =
            this.FindControl<TextBlock>(
                "UnifiedDashboardStatusText");
        if (dashboardStatus is not null)
        {
            dashboardStatus.Text =
                $"Switching to {profile.DisplayName}";
        }

        ProjectActiveTargetShell(
            profile,
            snapshot: null);
        ApplyActiveTargetCapabilities();
    }

    private void ProjectActiveTargetShell(
        LinuxHostProfile profile,
        HostSnapshot? snapshot)
    {
        var footer =
            this.FindControl<TextBlock>(
                "FooterTargetText");
        if (footer is not null)
        {
            footer.Text =
                ActiveTargetFooterText(
                    profile,
                    snapshot);
        }

        var hostname =
            this.FindControl<TextBlock>(
                "SidebarHostname");
        if (hostname is not null)
        {
            hostname.Text =
                snapshot?.Hostname ??
                profile.DisplayName;
        }

        var operatingSystem =
            this.FindControl<TextBlock>(
                "SidebarOperatingSystem");
        if (operatingSystem is not null)
        {
            operatingSystem.Text =
                snapshot?.OperatingSystem ??
                profile.KindLabel;
        }
    }

    private string ActiveTargetFooterText(
        HostSnapshot? snapshot) =>
        ActiveTargetFooterText(
            _controlPlane.ActiveProfile,
            snapshot);

    private static string ActiveTargetFooterText(
        LinuxHostProfile profile,
        HostSnapshot? snapshot)
    {
        if (snapshot is null ||
            string.IsNullOrWhiteSpace(
                snapshot.Hostname) ||
            snapshot.Hostname.Equals(
                profile.DisplayName,
                StringComparison.OrdinalIgnoreCase))
        {
            return profile.DisplayName;
        }

        return
            $"{profile.DisplayName} · {snapshot.Hostname}";
    }

    private void ApplyActiveTargetCapabilities()
    {
        var backupSupported =
            SupportsTargetCapability(
                CapabilityIds.BackupInventoryRead);

        var backupsNav =
            this.FindControl<Button>(
                "BackupsNav");
        if (backupsNav is not null)
        {
            backupsNav.IsVisible =
                backupSupported;
        }

        if (!backupSupported &&
            _unifiedInterfaceInitialized &&
            _unifiedCurrentNavigation.Equals(
                "BackupsNav",
                StringComparison.Ordinal))
        {
            Navigate(
                "DashboardNav");
        }
    }

    private bool SupportsTargetCapability(
        string capabilityId) =>
        _activeTargetCapabilities.Supports(
            capabilityId);

    private IReadOnlyList<ControlPlaneActivityRow>
        ActiveTargetActivities()
    {
        var target =
            _controlPlane.ActiveProfile.DisplayName;

        return _controlPlane.State.Activities
            .Where(item =>
                item.Target.Equals(
                    target,
                    StringComparison.OrdinalIgnoreCase))
            .ToArray();
    }

    private IReadOnlyList<ControlPlaneJobRow>
        ActiveTargetJobs()
    {
        var target =
            _controlPlane.ActiveProfile.DisplayName;

        return _controlPlane.State.Jobs
            .Where(item =>
                item.Target.Equals(
                    target,
                    StringComparison.OrdinalIgnoreCase))
            .ToArray();
    }

    private static LinuxHostProfile
        SnapshotTargetProfile(
            LinuxHostProfile source) =>
        new()
        {
            Id = source.Id,
            Name = source.Name,
            Kind = source.Kind,
            Host = source.Host,
            Port = source.Port,
            Username = source.Username,
            Role = source.Role,
            Authentication =
                source.Authentication,
            PrivateKeyPath =
                source.PrivateKeyPath,
            HostKeyFingerprint =
                source.HostKeyFingerprint,
            LastDetectedAt =
                source.LastDetectedAt
        };
}
