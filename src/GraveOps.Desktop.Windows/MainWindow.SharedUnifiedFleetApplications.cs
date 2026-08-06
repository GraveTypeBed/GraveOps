using Avalonia.Controls;
using GraveOps.Presentation.Avalonia.Fleet;
using GraveOps.Core.Hosts;

namespace GraveOps.Desktop.Windows;

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
        UpdateSharedUnifiedFleetHosts();
        UpdateSharedUnifiedFleetApplications(
            _snapshot);
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
                "IntegrationsPage");

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
        var row =
            _targetRows.FirstOrDefault(item =>
                item.TargetId.Equals(
                    e.TargetId,
                    StringComparison.Ordinal));

        if (row is null)
        {
            _sharedFleetHostsView?.SetStatus(
                $"Target '{e.TargetId}' is no longer saved.");

            return;
        }

        _sharedFleetHostsView?.SetStatus(
            $"Activating {row.DisplayName}...");

        await SelectActiveTargetAsync(
            row);
    }

    private void SharedFleetApplicationRequested(
        object? sender,
        UnifiedFleetApplicationRequestedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(
                e.ApplicationKey))
        {
            return;
        }

        var navigation =
            NavigationForWindowsApplication(
                e.ApplicationKey);

        if (string.IsNullOrWhiteSpace(
                navigation))
        {
            _sharedFleetApplicationsView?.SetStatus(
                "This provider-reported application has no dedicated Windows workspace yet.");

            return;
        }

        Navigate(
            navigation);
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

        RefreshServersPage();
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

        UpdateSharedUnifiedFleetHosts();
    }

    private void UpdateSharedUnifiedFleetHosts()
    {
        if (_sharedFleetHostsView is null)
            return;

        var activeTargetId =
            _targetSession
                .SelectedTarget
                ?.Id ??
            string.Empty;

        var hosts =
            _targetRows
                .Select(row =>
                {
                    var isActive =
                        row.TargetId.Equals(
                            activeTargetId,
                            StringComparison.Ordinal);

                    return new UnifiedFleetHostRow(
                        row.TargetId,
                        row.DisplayName,
                        row.IsLocal
                            ? "Local Windows"
                            : "Remote Windows",
                        row.ConnectionSummary,
                        isActive
                            ? _snapshot is null
                                ? "Active - capture pending"
                                : "Ready"
                            : "Saved",
                        isActive
                            ? $"{_targetSession.CurrentCapabilities.Values.Count} capabilities"
                            : "Capabilities activate with target",
                        isActive
                            ? (_snapshot?.Integrations.Count ?? 0)
                            : 0,
                        isActive
                            ? _snapshot?.CapturedAt
                            : null,
                        isActive,
                        IsStale:
                            isActive &&
                            _snapshot is null,
                        CanActivate:
                            !isActive);
                })
                .ToArray();

        _sharedFleetHostsView.Update(
            new UnifiedFleetState(
                hosts,
                BuildWindowsFleetApplications(
                    _snapshot),
                $"{hosts.Length} saved Windows target(s)",
                _targetSession.CredentialVaultAvailable
                    ? "Windows Credential Manager available · target credentials remain outside presentation."
                    : "Windows Credential Manager unavailable · unsafe target operations remain blocked."));
    }

    private void UpdateSharedUnifiedFleetApplications(
        HostSnapshot? snapshot)
    {
        if (_sharedFleetApplicationsView is null)
            return;

        var applications =
            BuildWindowsFleetApplications(
                snapshot);

        var hosts =
            _targetRows
                .Select(row =>
                    new UnifiedFleetHostRow(
                        row.TargetId,
                        row.DisplayName,
                        row.IsLocal
                            ? "Local Windows"
                            : "Remote Windows",
                        row.ConnectionSummary,
                        row.TargetId.Equals(
                            _targetSession.SelectedTarget?.Id,
                            StringComparison.Ordinal)
                            ? "Ready"
                            : "Saved",
                        "Capabilities owned by target session",
                        row.TargetId.Equals(
                            _targetSession.SelectedTarget?.Id,
                            StringComparison.Ordinal)
                            ? applications.Count
                            : 0,
                        row.TargetId.Equals(
                            _targetSession.SelectedTarget?.Id,
                            StringComparison.Ordinal)
                            ? snapshot?.CapturedAt
                            : null,
                        row.TargetId.Equals(
                            _targetSession.SelectedTarget?.Id,
                            StringComparison.Ordinal),
                        IsStale:
                            false,
                        CanActivate:
                            !row.TargetId.Equals(
                                _targetSession.SelectedTarget?.Id,
                                StringComparison.Ordinal)))
                .ToArray();

        _sharedFleetApplicationsView.Update(
            new UnifiedFleetState(
                hosts,
                applications,
                snapshot is null
                    ? "Waiting for the active Windows target capture."
                    : $"{applications.Count} provider-reported application record(s)",
                "Windows application inventory is active-target scoped; no Linux persistence or identity-store behavior is implied."));
    }

    private IReadOnlyList<UnifiedFleetApplicationRow>
        BuildWindowsFleetApplications(
            HostSnapshot? snapshot)
    {
        if (snapshot is null)
            return Array.Empty<UnifiedFleetApplicationRow>();

        var ownerTargetId =
            _targetSession
                .SelectedTarget
                ?.Id ??
            string.Empty;

        var ownerTargetName =
            _targetSession
                .SelectedTarget
                ?.DisplayName ??
            snapshot.Hostname;

        var integrations =
            snapshot.Integrations
                .Select(integration =>
                {
                    var navigation =
                        NavigationForWindowsApplication(
                            integration.Name);

                    return new UnifiedFleetApplicationRow(
                        integration.Name,
                        integration.Name,
                        integration.Name,
                        "Managed integration",
                        integration.Kind,
                        integration.Kind,
                        ownerTargetId,
                        ownerTargetName,
                        integration.State,
                        integration.Evidence,
                        IsVerified:
                            true,
                        IsStale:
                            false,
                        CanOpen:
                            !string.IsNullOrWhiteSpace(
                                navigation),
                        CanEditIdentity:
                            false,
                        navigation);
                });

        var installed =
            snapshot.InstalledApplications
                .Select(application =>
                    new UnifiedFleetApplicationRow(
                        $"installed:{application.Name}:{application.Version}",
                        application.Name,
                        application.Name,
                        "Installed software",
                        string.IsNullOrWhiteSpace(
                            application.Publisher)
                            ? "Installed application"
                            : application.Publisher,
                        application.Source,
                        ownerTargetId,
                        ownerTargetName,
                        string.IsNullOrWhiteSpace(
                            application.Version)
                            ? "Installed"
                            : application.Version,
                        string.IsNullOrWhiteSpace(
                            application.Publisher)
                            ? "Provider-reported installed software."
                            : $"Publisher: {application.Publisher}",
                        IsVerified:
                            true,
                        IsStale:
                            false,
                        CanOpen:
                            false,
                        CanEditIdentity:
                            false,
                        NavigationKey:
                            string.Empty));

        return integrations
            .Concat(
                installed)
            .GroupBy(
                item =>
                    $"{item.ApplicationKey}|{item.OwnerTargetId}",
                StringComparer.OrdinalIgnoreCase)
            .Select(group =>
                group.First())
            .OrderBy(item =>
                item.Category,
                StringComparer.OrdinalIgnoreCase)
            .ThenBy(item =>
                item.DisplayName,
                StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static string NavigationForWindowsApplication(
        string applicationKey) =>
        applicationKey.Trim() switch
        {
            var value when value.Equals(
                "Plex",
                StringComparison.OrdinalIgnoreCase) =>
                "PlexNav",

            var value when value.Equals(
                "Sonarr",
                StringComparison.OrdinalIgnoreCase) =>
                "SonarrNav",

            var value when value.Equals(
                "Radarr",
                StringComparison.OrdinalIgnoreCase) =>
                "RadarrNav",

            var value when value.Equals(
                "Lidarr",
                StringComparison.OrdinalIgnoreCase) =>
                "LidarrNav",

            var value when value.Equals(
                "Prowlarr",
                StringComparison.OrdinalIgnoreCase) =>
                "ProwlarrNav",

            var value when value.Equals(
                "SABnzbd",
                StringComparison.OrdinalIgnoreCase) =>
                "SABnzbdNav",

            var value when value.Equals(
                "qBittorrent",
                StringComparison.OrdinalIgnoreCase) =>
                "QBittorrentNav",

            _ =>
                string.Empty
        };
}