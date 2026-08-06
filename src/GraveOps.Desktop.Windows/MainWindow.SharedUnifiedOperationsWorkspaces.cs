using System.Runtime.InteropServices;
using Avalonia.Controls;
using GraveOps.Core.Hosts;
using GraveOps.Presentation.Avalonia.OperationsWorkspaces;

namespace GraveOps.Desktop.Windows;

public partial class MainWindow
{
    private UnifiedDockerView?
        _sharedDockerView;

    private UnifiedBackupsView?
        _sharedBackupsView;

    private UnifiedSettingsView?
        _sharedSettingsView;

    private UnifiedToolsView?
        _sharedToolsView;

    private string
        _sharedWindowsDockerSelectionKey =
            string.Empty;

    private void InitializeSharedUnifiedOperationsWorkspaces()
    {
        _sharedDockerView =
            new UnifiedDockerView();

        _sharedBackupsView =
            new UnifiedBackupsView();

        _sharedSettingsView =
            new UnifiedSettingsView();

        _sharedToolsView =
            new UnifiedToolsView();

        ReplaceOperationsWorkspacePage(
            "DockerPage",
            _sharedDockerView);

        ReplaceOperationsWorkspacePage(
            "BackupsPage",
            _sharedBackupsView);

        ReplaceOperationsWorkspacePage(
            "SettingsPage",
            _sharedSettingsView);

        ReplaceOperationsWorkspacePage(
            "ToolsPage",
            _sharedToolsView);

        _sharedDockerView.RefreshRequested +=
            async (_, _) =>
                await RefreshAsync();

        _sharedDockerView.SelectionRequested +=
            (_, e) =>
            {
                _sharedWindowsDockerSelectionKey =
                    e.Row.Key;

                UpdateSharedUnifiedOperationsWorkspaces(
                    _snapshot);
            };

        _sharedDockerView.DetailRefreshRequested +=
            (_, e) =>
            {
                _sharedWindowsDockerSelectionKey =
                    e.Row.Key;

                UpdateSharedUnifiedOperationsWorkspaces(
                    _snapshot);
            };

        _sharedBackupsView.RefreshRequested +=
            async (_, _) =>
                await RefreshAsync();

        _sharedBackupsView.ServicesRequested +=
            (_, _) =>
                Navigate(
                    "ServicesNav");

        _sharedBackupsView.ToolsRequested +=
            (_, _) =>
                Navigate(
                    "ToolsNav");

        UpdateSharedUnifiedOperationsWorkspaces(
            _snapshot);
    }

    private void ReplaceOperationsWorkspacePage(
        string pageName,
        Control sharedView)
    {
        var page =
            Get<Grid>(
                pageName);

        foreach (var child in
                 page.Children.ToArray())
        {
            child.IsVisible =
                false;
        }

        Grid.SetRowSpan(
            sharedView,
            32);

        Grid.SetColumnSpan(
            sharedView,
            32);

        page.Children.Add(
            sharedView);
    }

    private void UpdateSharedUnifiedOperationsWorkspaces(
        HostSnapshot? snapshot)
    {
        UpdateSharedWindowsDocker(
            snapshot);

        UpdateSharedWindowsBackups();

        UpdateSharedWindowsSettings();

        UpdateSharedWindowsTools();
    }

    private void UpdateSharedWindowsDocker(
        HostSnapshot? snapshot)
    {
        if (_sharedDockerView is null)
            return;

        if (snapshot is null)
        {
            _sharedDockerView.Update(
                UnifiedDockerState.Empty);
            return;
        }

        var rows =
            snapshot.Containers
                .Select(container =>
                {
                    var running =
                        container.State.Equals(
                            "running",
                            StringComparison.OrdinalIgnoreCase);

                    var attention =
                        container.State.Equals(
                            "dead",
                            StringComparison.OrdinalIgnoreCase) ||
                        container.Status.Contains(
                            "unhealthy",
                            StringComparison.OrdinalIgnoreCase) ||
                        container.Status.Contains(
                            "restarting",
                            StringComparison.OrdinalIgnoreCase);

                    return
                        new UnifiedDockerRow(
                            container.Name,
                            "Windows",
                            container.Name,
                            container.Image,
                            container.State.ToUpperInvariant(),
                            attention
                                ? "ATTENTION"
                                : "REPORTED",
                            "--",
                            container.Status,
                            container.Ports,
                            "Windows provider inventory",
                            "--",
                            running,
                            attention,
                            CanStart:
                                false,
                            CanStop:
                                false,
                            CanRestart:
                                false,
                            CanRestartProject:
                                false);
                })
                .ToArray();

        if (string.IsNullOrWhiteSpace(
                _sharedWindowsDockerSelectionKey) ||
            !rows.Any(row =>
                row.Key.Equals(
                    _sharedWindowsDockerSelectionKey,
                    StringComparison.OrdinalIgnoreCase)))
        {
            _sharedWindowsDockerSelectionKey =
                rows.FirstOrDefault()?.Key ??
                string.Empty;
        }

        var selected =
            rows.FirstOrDefault(row =>
                row.Key.Equals(
                    _sharedWindowsDockerSelectionKey,
                    StringComparison.OrdinalIgnoreCase));

        var detail =
            selected is null
                ? UnifiedDockerDetail.Empty
                : new UnifiedDockerDetail(
                    selected.Key,
                    selected.Name,
                    $"{selected.State} / {selected.Health}",
                    selected.Image,
                    selected.ComposeOwnership,
                    "Lifecycle detail is not exposed by the Windows provider.",
                    selected.Resources,
                    selected.ContainerId,
                    selected.RestartSummary,
                    "--",
                    selected.Ports,
                    "Mount inspection is not exposed by the Windows provider.",
                    "Environment-variable names are not exposed by the Windows provider.",
                    "Container logs are not exposed by the Windows provider.",
                    "Container logs are not exposed by the Windows provider.",
                    "Windows currently provides read-only container inventory.",
                    "Container mutations are not exposed by the Windows provider.",
                    CountSharedOperationsLines(
                        selected.Ports),
                    0,
                    0,
                    0,
                    0,
                    0);

        _sharedDockerView.Update(
            new UnifiedDockerState(
                rows,
                detail,
                NormalizeDisplay(
                    snapshot.DockerState),
                $"{rows.Length} shown | " +
                $"{rows.Count(row => row.IsRunning)} running | " +
                $"{rows.Count(row => row.HasAttention)} attention",
                "Windows provider boundary: inventory only. " +
                "Inspect metadata, logs and container mutations are unavailable.",
                ShowExited:
                    true,
                CanRefresh:
                    true,
                CanInspect:
                    false));
    }

    private void UpdateSharedWindowsBackups()
    {
        if (_sharedBackupsView is null)
            return;

        _sharedBackupsView.Update(
            new UnifiedBackupsState(
                CapabilityAvailable:
                    false,
                State:
                    "UNAVAILABLE",
                Severity:
                    "Information",
                Provider:
                    "No Windows backup provider",
                Summary:
                    "The active Windows provider does not report " +
                    "host.backups.read. No schedule, artifact or " +
                    "restore-readiness evidence is available.",
                OperationsState:
                    "CAPABILITY REQUIRED",
                Evidence:
                    new[]
                    {
                        "Backup inventory remains provider-owned.",
                        "No Windows backup capability has been advertised.",
                        "GraveOps will not infer readiness from unrelated files or services."
                    },
                Units:
                    Array.Empty<UnifiedBackupUnitRow>(),
                Artifacts:
                    Array.Empty<UnifiedBackupArtifactRow>()));
    }

    private void UpdateSharedWindowsSettings()
    {
        if (_sharedSettingsView is null)
            return;

        var applicationData =
            Environment.GetFolderPath(
                Environment.SpecialFolder.ApplicationData);

        var localApplicationData =
            Environment.GetFolderPath(
                Environment.SpecialFolder.LocalApplicationData);

        var paths =
            new[]
            {
                new UnifiedPathRow(
                    "application",
                    "Application",
                    AppContext.BaseDirectory,
                    CanOpen:
                        false,
                    CanOpenTerminal:
                        false),

                new UnifiedPathRow(
                    "roaming",
                    "Roaming data",
                    applicationData,
                    CanOpen:
                        false,
                    CanOpenTerminal:
                        false),

                new UnifiedPathRow(
                    "local",
                    "Local data",
                    localApplicationData,
                    CanOpen:
                        false,
                    CanOpenTerminal:
                        false)
            };

        _sharedSettingsView.Update(
            new UnifiedSettingsState(
                ThemeOptions:
                    new[]
                    {
                        "Shared Linux visual system"
                    },
                Theme:
                    "Shared Linux visual system",
                DensityOptions:
                    new[]
                    {
                        "Desktop"
                    },
                Density:
                    "Desktop",
                RestoreSession:
                    false,
                SilentRefresh:
                    true,
                ShowFreshness:
                    true,
                InterfaceEditable:
                    false,
                InterfaceStatus:
                    "Windows consumes the shared Linux presentation. " +
                    "Interface persistence is not exposed in this provider batch.",
                StartSafeMode:
                    true,
                ShowInformationalLogs:
                    false,
                ShowInformationalContainers:
                    true,
                OpenOverview:
                    false,
                DesktopNotifications:
                    false,
                BackgroundRefreshSeconds:
                    "manual",
                OperatorDefaultsEditable:
                    false,
                OperatorStatus:
                    "Windows operator-default persistence is unavailable.",
                PolicySummary:
                    "Policy management remains Linux-provider owned.",
                CapacityPolicySummary:
                    "Capacity policy editing is unavailable for Windows targets.",
                SignalQualitySummary:
                    "Signal quality policy editing is unavailable for Windows targets.",
                RemediationSummary:
                    "Verified remediation settings are unavailable for Windows targets.",
                UiPerformanceSummary:
                    "UI performance policy editing is unavailable for Windows targets.",
                Paths:
                    paths,
                PathStatus:
                    "Paths are shown for context only. Open and terminal actions are disabled.",
                Version:
                    new UnifiedVersionState(
                        Branch:
                            "--",
                        Commit:
                            "--",
                        Worktree:
                            "Not inspected",
                        Origin:
                            "Not inspected",
                        Dotnet:
                            RuntimeInformation.FrameworkDescription),
                PolicyActionsAvailable:
                    false));
    }

    private void UpdateSharedWindowsTools()
    {
        if (_sharedToolsView is null)
            return;

        var parity =
            new[]
            {
                new UnifiedParityRow(
                    "docker",
                    "Docker inventory",
                    "Read-only parity",
                    "Linux provides Compose ownership, inspect detail, " +
                    "redacted logs and guarded verified actions.",
                    "Windows provides container name, image, state, status and ports.",
                    "Capability: host.containers.read"),

                new UnifiedParityRow(
                    "backups",
                    "Backup readiness",
                    "Capability gap",
                    "Linux probes backup timers, tools and verified artifacts.",
                    "Windows advertises no backup inventory provider capability.",
                    "Capability host.backups.read is absent."),

                new UnifiedParityRow(
                    "settings",
                    "Operator settings",
                    "Presentation only",
                    "Linux persists interface, operator defaults and policy stores.",
                    "Windows renders the shared page in an explicit read-only state.",
                    "No Windows settings adapter has been implemented."),

                new UnifiedParityRow(
                    "tools",
                    "Operator tools",
                    "Presentation only",
                    "Linux provides terminal, diagnostics, files, scripts and update inventory.",
                    "Windows renders capability boundaries without launching or mutating.",
                    "No Windows operator-tools capability is advertised.")
            };

        _sharedToolsView.Update(
            new UnifiedToolsState(
                TerminalStatus:
                    "Terminal and SSH handoff are unavailable in the Windows adapter.",
                CanOpenLocalTerminal:
                    false,
                CanOpenSsh:
                    false,
                DiagnosticsStatus:
                    "Redacted diagnostics export is not implemented for Windows.",
                CanCreateDiagnostics:
                    false,
                ValidationOutput:
                    "Windows read-only validation is not implemented.",
                CanRunValidation:
                    false,
                FilesPath:
                    AppContext.BaseDirectory,
                Files:
                    Array.Empty<UnifiedFileRow>(),
                FilesStatus:
                    "Local file browsing and SFTP handoff are unavailable.",
                CanBrowseFiles:
                    false,
                CanOpenSftp:
                    false,
                Scripts:
                    Array.Empty<UnifiedScriptRow>(),
                ScriptOutput:
                    "The Windows adapter does not expose a script runner.",
                ScriptStatus:
                    "Script execution remains disabled.",
                UpdateOutput:
                    "Windows update inventory has not been implemented.",
                UpdateStatus:
                    "No update action is available.",
                CanCaptureUpdates:
                    false,
                Parity:
                    parity,
                ParitySummary:
                    $"{parity.Length} operations capability boundaries documented."));
    }

    private static int CountSharedOperationsLines(
        string value)
    {
        if (string.IsNullOrWhiteSpace(
                value) ||
            value.Equals(
                "--",
                StringComparison.Ordinal))
        {
            return 0;
        }

        return value
            .Replace(
                "\r\n",
                "\n",
                StringComparison.Ordinal)
            .Split(
                '\n',
                StringSplitOptions.RemoveEmptyEntries |
                StringSplitOptions.TrimEntries)
            .Length;
    }
}
