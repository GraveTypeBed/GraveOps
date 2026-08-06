using Avalonia.Controls;
using GraveOps.Presentation.Avalonia.Fleet;

namespace GraveOps.Desktop.Linux;

public partial class MainWindow
{
    private UnifiedFleetView?
        _sharedFleetHostsView;

    private UnifiedFleetView?
        _sharedFleetApplicationsView;

    private readonly List<LegacyFleetControlState>
        _legacyFleetHostControls =
            new();

    private Button?
        _sharedFleetHostReturnButton;

    private sealed record LegacyFleetControlState(
        Control Control,
        bool WasVisible);

    private void InitializeSharedUnifiedFleetApplications()
    {
        InitializeSharedFleetHosts();
        InitializeSharedFleetApplications();
        UpdateSharedUnifiedFleetApplications();
    }

    private void InitializeSharedFleetHosts()
    {
        var page =
            Get<Grid>(
                "ServersPage");

        foreach (var child in
                 page.Children.ToArray())
        {
            _legacyFleetHostControls.Add(
                new LegacyFleetControlState(
                    child,
                    child.IsVisible));

            child.IsVisible =
                false;
        }

        _sharedFleetHostsView =
            new UnifiedFleetView(
                UnifiedFleetFocus.Hosts);

        _sharedFleetHostsView.RefreshRequested +=
            SharedFleetRefreshRequested;

        _sharedFleetHostsView.HostRequested +=
            SharedFleetHostRequested;

        _sharedFleetHostsView.ManageConnectionsRequested +=
            (_, _) =>
                ShowLegacyFleetHostManager();

        Grid.SetColumnSpan(
            _sharedFleetHostsView,
            16);

        page.Children.Add(
            _sharedFleetHostsView);

        _sharedFleetHostReturnButton =
            new Button
            {
                Content =
                    "Back to fleet",
                HorizontalAlignment =
                    Avalonia.Layout.HorizontalAlignment.Right,
                VerticalAlignment =
                    Avalonia.Layout.VerticalAlignment.Top,
                Margin =
                    new Avalonia.Thickness(
                        0,
                        0,
                        4,
                        0),
                IsVisible =
                    false,
                Classes =
                {
                    "compact"
                }
            };

        _sharedFleetHostReturnButton.Click +=
            (_, _) =>
                ShowSharedFleetHostManager();

        Grid.SetColumnSpan(
            _sharedFleetHostReturnButton,
            16);

        page.Children.Add(
            _sharedFleetHostReturnButton);
    }

    private void InitializeSharedFleetApplications()
    {
        var page =
            Get<Grid>(
                "MediaHubPage");

        foreach (var child in
                 page.Children.ToArray())
        {
            child.IsVisible =
                false;
        }

        _sharedFleetApplicationsView =
            new UnifiedFleetView(
                UnifiedFleetFocus.Applications);

        _sharedFleetApplicationsView.RefreshRequested +=
            SharedFleetRefreshRequested;

        _sharedFleetApplicationsView.ApplicationRequested +=
            SharedFleetApplicationRequested;

        Grid.SetColumnSpan(
            _sharedFleetApplicationsView,
            16);

        page.Children.Add(
            _sharedFleetApplicationsView);
    }

    private async void SharedFleetRefreshRequested(
        object? sender,
        EventArgs e)
    {
        await RefreshAsync();
    }

    private async void SharedFleetHostRequested(
        object? sender,
        UnifiedFleetHostRequestedEventArgs e)
    {
        var profile =
            _controlPlane.Profiles.Find(
                e.TargetId);

        if (profile is null)
        {
            _sharedFleetHostsView?.SetStatus(
                $"Target '{e.TargetId}' is no longer saved.");

            return;
        }

        _sharedFleetHostsView?.SetStatus(
            $"Activating {profile.DisplayName}...");

        await SwitchActiveTargetAsync(
            profile);
    }

    private async void SharedFleetApplicationRequested(
        object? sender,
        UnifiedFleetApplicationRequestedEventArgs e)
    {
        var row =
            _mediaRows.FirstOrDefault(item =>
                item.SourceKey.Equals(
                    e.ApplicationKey,
                    StringComparison.OrdinalIgnoreCase) &&
                item.OwnerTargetId.Equals(
                    e.OwnerTargetId,
                    StringComparison.OrdinalIgnoreCase));

        if (row is null)
        {
            _sharedFleetApplicationsView?.SetStatus(
                "The selected application is no longer present in fleet inventory.");

            return;
        }

        _sharedFleetApplicationsView?.SetStatus(
            e.EditIdentity
                ? $"Opening identity settings for {row.DisplayName}..."
                : $"Opening {row.DisplayName} on {row.OwnerTargetName}...");

        await ActivateOwnedApplicationAsync(
            row,
            e.EditIdentity);
    }

    private void ShowLegacyFleetHostManager()
    {
        if (_sharedFleetHostsView is null ||
            _sharedFleetHostReturnButton is null)
        {
            return;
        }

        _sharedFleetHostsView.IsVisible =
            false;

        foreach (var state in
                 _legacyFleetHostControls)
        {
            state.Control.IsVisible =
                state.WasVisible;
        }

        _sharedFleetHostReturnButton.IsVisible =
            true;
    }

    private void ShowSharedFleetHostManager()
    {
        if (_sharedFleetHostsView is null ||
            _sharedFleetHostReturnButton is null)
        {
            return;
        }

        foreach (var state in
                 _legacyFleetHostControls)
        {
            state.Control.IsVisible =
                false;
        }

        _sharedFleetHostReturnButton.IsVisible =
            false;

        _sharedFleetHostsView.IsVisible =
            true;

        UpdateSharedUnifiedFleetApplications();
    }

    private void UpdateSharedUnifiedFleetApplications()
    {
        if (_sharedFleetHostsView is null ||
            _sharedFleetApplicationsView is null)
        {
            return;
        }

        var activeTargetId =
            _controlPlane.ActiveProfile.Id;

        var hosts =
            _controlPlane.Profiles.Profiles
                .Select(profile =>
                {
                    var hasInventory =
                        _targetApplicationInventories
                            .TryGetValue(
                                profile.Id,
                                out var inventory);

                    var applicationCount =
                        _applicationRegistry
                            .ForTarget(
                                profile.Id)
                            .Count;

                    var isActive =
                        profile.Id.Equals(
                            activeTargetId,
                            StringComparison.OrdinalIgnoreCase);

                    var isStale =
                        hasInventory
                            ? inventory!.IsStale
                            : !isActive ||
                              !_acceptedTargetId.Equals(
                                  profile.Id,
                                  StringComparison.OrdinalIgnoreCase);

                    var state =
                        isActive
                            ? _acceptedTargetId.Equals(
                                profile.Id,
                                StringComparison.OrdinalIgnoreCase)
                                ? "Ready"
                                : "Active - capture pending"
                            : hasInventory
                                ? inventory!.IsStale
                                    ? "Cached"
                                    : "Ready"
                                : "Saved";

                    var capabilitySummary =
                        hasInventory
                            ? $"{inventory!.Capabilities.Values.Count} capabilities"
                            : "Capabilities pending capture";

                    return new UnifiedFleetHostRow(
                        profile.Id,
                        profile.DisplayName,
                        profile.KindLabel,
                        profile.ConnectionSummary,
                        state,
                        capabilitySummary,
                        applicationCount,
                        hasInventory
                            ? inventory!.CapturedAt
                            : profile.LastDetectedAt,
                        isActive,
                        isStale,
                        CanActivate:
                            !isActive);
                })
                .ToArray();

        var applications =
            _mediaRows
                .Select(row =>
                    new UnifiedFleetApplicationRow(
                        row.SourceKey,
                        row.IntegrationName,
                        row.DisplayName,
                        row.Category,
                        row.Integration.Role,
                        row.RuntimeText,
                        row.OwnerTargetId,
                        row.OwnerTargetName,
                        row.StateLabel,
                        row.Evidence,
                        row.IsVerified,
                        row.IsStale,
                        CanOpen:
                            true,
                        CanEditIdentity:
                            true,
                        NavigationForIntegration(
                            row.IntegrationName) ??
                        string.Empty))
                .ToArray();

        var state =
            new UnifiedFleetState(
                hosts,
                applications,
                $"{hosts.Length} saved target(s) · {applications.Length} application instance(s)",
                $"Persistent redacted fleet inventory · {hosts.Count(item => item.IsStale)} stale target(s)");

        _sharedFleetHostsView.Update(
            state);

        _sharedFleetApplicationsView.Update(
            state);
    }
}