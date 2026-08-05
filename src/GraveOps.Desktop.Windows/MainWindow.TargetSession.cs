using Avalonia.Controls;
using GraveOps.Core.Targets;

namespace GraveOps.Desktop.Windows;

public partial class MainWindow
{
    private static readonly IReadOnlyList<string>
        CapabilityAwareNavigation =
            new[]
            {
                "ServicesNav",
                "DockerNav",
                "StorageNav",
                "LogsNav",
                "BackupsNav"
            };

    private readonly WindowsTargetSession
        _targetSession =
            WindowsTargetSession.CreateDefault();

    private IReadOnlyList<WindowsTargetRow>
        _targetRows =
            Array.Empty<WindowsTargetRow>();

    private async Task<bool>
        InitializeTargetSessionAsync()
    {
        try
        {
            var targets =
                await _targetSession.InitializeAsync();

            SetTargetRows(
                targets);

            ApplyTargetCapabilities(
                TargetCapabilities.Empty);

            var selected =
                ActiveTargetOrThrow();

            RecordActivity(
                "Target session",
                $"Restored {selected.DisplayName}.");

            return true;
        }
        catch (Exception exception)
        {
            SetConnectionState(
                "FAILED",
                exception.Message,
                isHealthy: false,
                isFailure: true);

            SetText(
                "CaptureStatusText",
                "Target initialization failed: " +
                exception.Message);

            SetList(
                "WarningsList",
                new[]
                {
                    exception.ToString()
                });

            RecordActivity(
                "Target initialization failed",
                exception.Message);

            return false;
        }
    }

    private async Task SelectActiveTargetAsync(
        WindowsTargetRow targetRow)
    {
        try
        {
            _refreshCancellation?.Cancel();

            var selected =
                await _targetSession.SelectAsync(
                    targetRow.TargetId);

            SetTargetRows(
                await _targetSession.ListAsync());

            ApplyTargetCapabilities(
                TargetCapabilities.Empty);

            RecordActivity(
                "Active target",
                $"Selected {selected.DisplayName} | " +
                $"{WindowsTargetUiProjection.ConnectionSummary(selected)}.");

            await RefreshAsync();
            OnPlexTargetChanged();
            OnArrTargetChanged();
        }
        catch (Exception exception)
        {
            SetConnectionState(
                "FAILED",
                exception.Message,
                isHealthy: false,
                isFailure: true);

            SetText(
                "CaptureStatusText",
                "Target selection failed: " +
                exception.Message);

            RecordActivity(
                "Target selection failed",
                exception.Message);
        }
    }

    private void SetTargetRows(
        IEnumerable<TargetProfile> targets)
    {
        _targetRows =
            WindowsTargetUiProjection.CreateRows(
                targets);

        var selectedTargetId =
            _targetSession
                .SelectedTarget
                ?.Id;

        var selectedRow =
            _targetRows.FirstOrDefault(
                row =>
                    row.TargetId.Equals(
                        selectedTargetId,
                        StringComparison.Ordinal));

        var comboBox =
            Get<ComboBox>(
                "ActiveTargetComboBox");

        _suppressTargetSelection =
            true;

        try
        {
            comboBox.ItemsSource =
                _targetRows;

            comboBox.SelectedItem =
                selectedRow;
        }
        finally
        {
            _suppressTargetSelection =
                false;
        }

        RefreshServersPage();
    }

    private TargetProfile ActiveTargetOrThrow() =>
        _targetSession.SelectedTarget ??
        throw new InvalidOperationException(
            "No Windows target is selected.");

    private string ActiveTargetConnectionSummary() =>
        WindowsTargetUiProjection.ConnectionSummary(
            ActiveTargetOrThrow());

    private string ActiveTargetDisplayName() =>
        ActiveTargetOrThrow().DisplayName;

    private void ApplyTargetCapabilities(
        TargetCapabilities capabilities)
    {
        ArgumentNullException.ThrowIfNull(
            capabilities);

        foreach (var navigationName in
                 CapabilityAwareNavigation)
        {
            var button =
                Get<Button>(
                    navigationName);

            var supported =
                WindowsTargetNavigationPolicy.IsSupported(
                    navigationName,
                    capabilities);

            button.IsEnabled =
                supported;

            ToolTip.SetTip(
                button,
                supported
                    ? null
                    : WindowsTargetNavigationPolicy
                        .UnsupportedReason(
                            navigationName));
        }

        var unsupportedSelection =
            CapabilityAwareNavigation.Any(
                navigationName =>
                {
                    var button =
                        Get<Button>(
                            navigationName);

                    return
                        button.Classes.Contains(
                            "selected") &&
                        !button.IsEnabled;
                });

        if (unsupportedSelection)
        {
            Navigate(
                "DashboardNav");
        }
    }
}