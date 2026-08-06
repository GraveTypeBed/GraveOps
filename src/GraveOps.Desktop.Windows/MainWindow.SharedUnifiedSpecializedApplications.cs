using System;
using System.Linq;
using Avalonia.Controls;
using GraveOps.Core.Hosts;
using GraveOps.Presentation.Avalonia.SpecializedApplications;

namespace GraveOps.Desktop.Windows;

public partial class MainWindow
{
    private UnifiedApplicationWorkspaceView?
        _sharedWindowsApplicationWorkspaceView;

    private UnifiedRecyclarrView?
        _sharedWindowsRecyclarrView;

    private UnifiedPiHoleView?
        _sharedWindowsPiHoleView;

    private string
        _activeWindowsSpecializedApplicationKey =
            string.Empty;

    private void InitializeSharedUnifiedSpecializedApplications()
    {
        var page =
            Get<Grid>(
                "IntegrationsPage");

        var applicationView =
            new UnifiedApplicationWorkspaceView
            {
                IsVisible =
                    false
            };

        var recyclarrView =
            new UnifiedRecyclarrView
            {
                IsVisible =
                    false
            };

        var piHoleView =
            new UnifiedPiHoleView
            {
                IsVisible =
                    false
            };

        _sharedWindowsApplicationWorkspaceView =
            applicationView;

        _sharedWindowsRecyclarrView =
            recyclarrView;

        _sharedWindowsPiHoleView =
            piHoleView;

        foreach (var view in new Control[]
                 {
                     applicationView,
                     recyclarrView,
                     piHoleView
                 })
        {
            Grid.SetRowSpan(
                view,
                32);

            Grid.SetColumnSpan(
                view,
                32);

            page.Children.Add(
                view);
        }

        WireSharedWindowsApplicationWorkspace();
        WireSharedWindowsRecyclarr();
        WireSharedWindowsPiHole();
    }

    private void DisposeSharedUnifiedSpecializedApplications()
    {
        _activeWindowsSpecializedApplicationKey =
            string.Empty;
    }

    private void ShowSharedWindowsSpecializedApplication(
        string applicationKey)
    {
        if (string.IsNullOrWhiteSpace(
                applicationKey))
        {
            return;
        }

        _activeWindowsSpecializedApplicationKey =
            applicationKey.Trim();

        if (_sharedFleetApplicationsView is not null)
        {
            _sharedFleetApplicationsView.IsVisible =
                false;
        }

        HideSharedWindowsSpecializedApplicationViews();

        if (IsWindowsApplication(
                applicationKey,
                "Recyclarr"))
        {
            if (_sharedWindowsRecyclarrView is not null)
                _sharedWindowsRecyclarrView.IsVisible = true;
        }
        else if (IsWindowsApplication(
                     applicationKey,
                     "Pi-hole",
                     "PiHole",
                     "Pihole"))
        {
            if (_sharedWindowsPiHoleView is not null)
                _sharedWindowsPiHoleView.IsVisible = true;
        }
        else
        {
            if (_sharedWindowsApplicationWorkspaceView is not null)
                _sharedWindowsApplicationWorkspaceView.IsVisible = true;
        }

        UpdateVisibleSharedWindowsSpecializedApplication();
    }

    private void ShowSharedWindowsFleetApplications()
    {
        _activeWindowsSpecializedApplicationKey =
            string.Empty;

        HideSharedWindowsSpecializedApplicationViews();

        if (_sharedFleetApplicationsView is not null)
        {
            _sharedFleetApplicationsView.IsVisible =
                true;
        }

        UpdateSharedUnifiedFleetApplications(
            _snapshot);
    }

    private void HideSharedWindowsSpecializedApplicationViews()
    {
        if (_sharedWindowsApplicationWorkspaceView is not null)
            _sharedWindowsApplicationWorkspaceView.IsVisible = false;

        if (_sharedWindowsRecyclarrView is not null)
            _sharedWindowsRecyclarrView.IsVisible = false;

        if (_sharedWindowsPiHoleView is not null)
            _sharedWindowsPiHoleView.IsVisible = false;
    }

    private void UpdateVisibleSharedWindowsSpecializedApplication()
    {
        if (string.IsNullOrWhiteSpace(
                _activeWindowsSpecializedApplicationKey))
        {
            return;
        }

        if (IsWindowsApplication(
                _activeWindowsSpecializedApplicationKey,
                "Recyclarr"))
        {
            UpdateSharedWindowsRecyclarr();
            return;
        }

        if (IsWindowsApplication(
                _activeWindowsSpecializedApplicationKey,
                "Pi-hole",
                "PiHole",
                "Pihole"))
        {
            UpdateSharedWindowsPiHole();
            return;
        }

        UpdateSharedWindowsApplicationWorkspace();
    }

    private void WireSharedWindowsApplicationWorkspace()
    {
        if (_sharedWindowsApplicationWorkspaceView is null)
            return;

        _sharedWindowsApplicationWorkspaceView.ActionRequested +=
            (_, e) =>
            {
                switch (e.Action)
                {
                    case UnifiedApplicationWorkspaceAction.Docker:
                        Navigate(
                            "DockerNav");
                        break;

                    case UnifiedApplicationWorkspaceAction.Logs:
                        Navigate(
                            "LogsNav");
                        break;

                    case UnifiedApplicationWorkspaceAction.Intelligence:
                        Navigate(
                            "IntelligenceNav");
                        break;

                    case UnifiedApplicationWorkspaceAction.Back:
                        ShowSharedWindowsFleetApplications();
                        break;

                    case UnifiedApplicationWorkspaceAction.Open:
                        _sharedWindowsApplicationWorkspaceView.SetStatus(
                            "The Windows inventory provider did not expose a verified management endpoint.");
                        break;
                }
            };
    }

    private void WireSharedWindowsRecyclarr()
    {
        if (_sharedWindowsRecyclarrView is null)
            return;

        _sharedWindowsRecyclarrView.ActionRequested +=
            async (_, e) =>
            {
                switch (e.Action)
                {
                    case UnifiedRecyclarrAction.Refresh:
                        await RefreshAsync();
                        UpdateSharedWindowsRecyclarr();
                        break;

                    case UnifiedRecyclarrAction.Docker:
                        Navigate(
                            "DockerNav");
                        break;

                    case UnifiedRecyclarrAction.Logs:
                        Navigate(
                            "LogsNav");
                        break;

                    case UnifiedRecyclarrAction.Back:
                        ShowSharedWindowsFleetApplications();
                        break;

                    case UnifiedRecyclarrAction.OpenConfig:
                    case UnifiedRecyclarrAction.Preview:
                        _sharedWindowsRecyclarrView.SetStatus(
                            "Recyclarr config access and preview require the Linux Docker/config provider and are unavailable on Windows.");
                        break;
                }
            };
    }

    private void WireSharedWindowsPiHole()
    {
        if (_sharedWindowsPiHoleView is null)
            return;

        _sharedWindowsPiHoleView.ActionRequested +=
            async (_, e) =>
            {
                switch (e.Action)
                {
                    case UnifiedPiHoleAction.Refresh:
                        await RefreshAsync();
                        UpdateSharedWindowsPiHole();
                        break;

                    case UnifiedPiHoleAction.Logs:
                        Navigate(
                            "LogsNav");
                        break;

                    case UnifiedPiHoleAction.Back:
                        ShowSharedWindowsFleetApplications();
                        break;

                    case UnifiedPiHoleAction.Open:
                    case UnifiedPiHoleAction.EnableBlocking:
                    case UnifiedPiHoleAction.DisableBlockingFiveMinutes:
                    case UnifiedPiHoleAction.ReloadDns:
                        _sharedWindowsPiHoleView.SetStatus(
                            "Pi-hole API access and Linux control commands are not provided by the Windows target provider.");
                        break;
                }
            };
    }

    private void UpdateSharedWindowsApplicationWorkspace()
    {
        if (_sharedWindowsApplicationWorkspaceView is null)
            return;

        var integration =
            FindWindowsIntegration(
                _activeWindowsSpecializedApplicationKey);

        var installed =
            FindWindowsInstalledApplication(
                _activeWindowsSpecializedApplicationKey);

        var displayName =
            installed?.Name ??
            (string.IsNullOrWhiteSpace(
                _activeWindowsSpecializedApplicationKey)
                ? "Application"
                : _activeWindowsSpecializedApplicationKey);

        var targetName =
            _targetSession
                .SelectedTarget
                ?.DisplayName ??
            _snapshot?.Hostname ??
            "--";

        var related =
            _snapshot?.Warnings
                .Where(item =>
                    item.Contains(
                        displayName,
                        StringComparison.OrdinalIgnoreCase))
                .ToArray() ??
            Array.Empty<string>();

        var detected =
            integration is not null ||
            installed is not null;

        var hasContainerEvidence =
            _snapshot?.Containers.Any(item =>
                item.Name.Contains(
                    displayName,
                    StringComparison.OrdinalIgnoreCase) ||
                item.Image.Contains(
                    displayName,
                    StringComparison.OrdinalIgnoreCase)) ==
            true;

        var canOpenDocker =
            hasContainerEvidence ||
            integration?.Kind.Contains(
                "container",
                StringComparison.OrdinalIgnoreCase) ==
            true;

        var installedEvidence =
            installed is null
                ? string.Empty
                : string.Join(
                    Environment.NewLine,
                    new[]
                    {
                        $"Version · {ValueOrDash(installed.Version)}",
                        $"Publisher · {ValueOrDash(installed.Publisher)}",
                        $"Source · {ValueOrDash(installed.Source)}"
                    });

        _sharedWindowsApplicationWorkspaceView.Update(
            new UnifiedApplicationWorkspaceState(
                targetName,
                displayName,
                "Verified application state and operational ownership.",
                integration?.State ??
                (installed is null
                    ? "NOT DETECTED"
                    : "INSTALLED"),
                integration?.Kind ??
                installed?.Source ??
                "--",
                integration is not null
                    ? "Provider-reported integration"
                    : installed is not null
                        ? ValueOrDash(
                            installed.Publisher)
                        : "Application inventory",
                related.Length.ToString(),
                "Application readiness",
                "No verified endpoint",
                "Dependencies",
                targetName,
                integration?.Evidence ??
                (installed is null
                    ? "No verified runtime, service, container or management endpoint was returned by the active Windows provider."
                    : installedEvidence),
                related.Length == 0
                    ? "No active provider warning is associated with this application."
                    : string.Join(
                        Environment.NewLine +
                        Environment.NewLine,
                        related),
                detected
                    ? "Windows provider evidence is read-only. Docker, logs and Intelligence remain available."
                    : "Refresh the active Windows target or review application discovery.",
                false,
                canOpenDocker,
                true,
                related.Length > 0,
                true));
    }

    private void UpdateSharedWindowsRecyclarr()
    {
        if (_sharedWindowsRecyclarrView is null)
            return;

        var integration =
            FindWindowsIntegration(
                "Recyclarr");

        var container =
            _snapshot?.Containers
                .FirstOrDefault(item =>
                    item.Name.Contains(
                        "recyclarr",
                        StringComparison.OrdinalIgnoreCase) ||
                    item.Image.Contains(
                        "recyclarr",
                        StringComparison.OrdinalIgnoreCase));

        var targetName =
            _targetSession
                .SelectedTarget
                ?.DisplayName ??
            _snapshot?.Hostname ??
            "--";

        var detected =
            integration is not null ||
            container is not null;

        var evidence =
            JoinEvidence(
                integration?.Evidence,
                container is null
                    ? null
                    : $"Container {container.Name} · {container.Image} · {container.State} · {container.Status}");

        _sharedWindowsRecyclarrView.Update(
            new UnifiedRecyclarrState(
                targetName,
                _snapshot is null
                    ? "Capture pending"
                    : $"Captured {_snapshot.CapturedAt.ToLocalTime():g}",
                container?.State ??
                integration?.State ??
                "NOT DETECTED",
                "--",
                "--",
                "--",
                container?.Name ??
                "--",
                container?.Image ??
                "--",
                "Windows provider evidence only",
                "--",
                "--",
                "--",
                string.IsNullOrWhiteSpace(
                    evidence)
                    ? "No Recyclarr service or container evidence was returned."
                    : evidence,
                Array.Empty<UnifiedRecyclarrTargetRow>(),
                Array.Empty<UnifiedRecyclarrConfigFileRow>(),
                "Preview unavailable on Windows",
                "Recyclarr preview requires the Linux Docker/config provider. No synchronization command was run.",
                detected
                    ? "Recyclarr inventory is visible, but config parsing and preview remain Linux-only."
                    : "Recyclarr was not detected on the active Windows target.",
                true,
                false,
                false,
                true,
                true,
                true));
    }

    private void UpdateSharedWindowsPiHole()
    {
        if (_sharedWindowsPiHoleView is null)
            return;

        var integration =
            FindWindowsIntegration(
                "Pi-hole",
                "PiHole",
                "Pihole");

        var container =
            _snapshot?.Containers
                .FirstOrDefault(item =>
                    item.Name.Contains(
                        "pihole",
                        StringComparison.OrdinalIgnoreCase) ||
                    item.Image.Contains(
                        "pihole",
                        StringComparison.OrdinalIgnoreCase));

        var service =
            _snapshot?.Services
                .FirstOrDefault(item =>
                    item.Unit.Contains(
                        "pihole",
                        StringComparison.OrdinalIgnoreCase) ||
                    item.Description.Contains(
                        "pi-hole",
                        StringComparison.OrdinalIgnoreCase));

        var targetName =
            _targetSession
                .SelectedTarget
                ?.DisplayName ??
            _snapshot?.Hostname ??
            "--";

        var detected =
            integration is not null ||
            container is not null ||
            service is not null;

        var evidence =
            JoinEvidence(
                integration?.Evidence,
                container is null
                    ? null
                    : $"Container {container.Name} · {container.Image} · {container.State} · {container.Status}",
                service is null
                    ? null
                    : $"Service {service.Unit} · {service.ActiveState}/{service.SubState}");

        _sharedWindowsPiHoleView.Update(
            new UnifiedPiHoleState(
                targetName,
                _snapshot is null
                    ? "Not captured"
                    : $"Captured {_snapshot.CapturedAt.ToLocalTime():g}",
                detected
                    ? integration?.State ??
                      container?.State ??
                      service?.ActiveState ??
                      "DETECTED"
                    : "NOT DETECTED",
                "--",
                "--",
                "--",
                "--",
                "Core -- · Web -- · FTL --",
                detected
                    ? $"{targetName} · Windows inventory evidence"
                    : "Host context unavailable.",
                "Pi-hole client and query-rate telemetry are not exposed by the Windows provider.",
                "Gravity inventory is not exposed by the Windows provider.",
                string.IsNullOrWhiteSpace(
                    evidence)
                    ? "No verified Pi-hole service or container evidence was returned."
                    : evidence,
                detected
                    ? "Pi-hole inventory is read-only on Windows. DNS statistics and control commands require the Linux Pi-hole adapter."
                    : "Pi-hole was not detected on the active Windows target.",
                true,
                false,
                false,
                false,
                false,
                true,
                true));
    }

    private InstalledApplicationSnapshot?
        FindWindowsInstalledApplication(
            string applicationKey)
    {
        if (_snapshot is null)
            return null;

        var installedPrefix =
            "installed:";

        if (applicationKey.StartsWith(
                installedPrefix,
                StringComparison.OrdinalIgnoreCase))
        {
            return _snapshot.InstalledApplications
                .FirstOrDefault(item =>
                    applicationKey.Equals(
                        $"{installedPrefix}{item.Name}:{item.Version}",
                        StringComparison.OrdinalIgnoreCase));
        }

        return _snapshot.InstalledApplications
            .FirstOrDefault(item =>
                item.Name.Equals(
                    applicationKey,
                    StringComparison.OrdinalIgnoreCase));
    }

    private static string ValueOrDash(
        string? value) =>
        string.IsNullOrWhiteSpace(
            value)
            ? "--"
            : value;

    private static string JoinEvidence(
        params string?[] values) =>
        string.Join(
            Environment.NewLine,
            values
                .Where(item =>
                    !string.IsNullOrWhiteSpace(
                        item))
                .Select(item =>
                    item!));

    private IntegrationSnapshot?
        FindWindowsIntegration(
            params string[] names)
    {
        if (_snapshot is null)
            return null;

        return _snapshot.Integrations
            .FirstOrDefault(item =>
                names.Any(name =>
                    item.Name.Equals(
                        name,
                        StringComparison.OrdinalIgnoreCase)));
    }

    private static bool IsWindowsApplication(
        string applicationKey,
        params string[] names) =>
        names.Any(name =>
            applicationKey.Equals(
                name,
                StringComparison.OrdinalIgnoreCase));
}
