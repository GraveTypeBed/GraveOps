using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using GraveOps.Core.Targets;
using GraveOps.Platform.Windows;

namespace GraveOps.Desktop.Windows;

public partial class MainWindow
{
    private string? _editingTargetId;
    private string? _pendingRemoveTargetId;
    private bool _suppressServersSelection;

    private void InitializeServersEditor()
    {
        var authentication =
            Get<ComboBox>(
                "ServersAuthenticationComboBox");

        authentication.ItemsSource =
            Enum.GetNames<
                WindowsRemoteAuthentication>();

        authentication.SelectedItem =
            nameof(
                WindowsRemoteAuthentication.Negotiate);

        Get<Border>(
            "ServersEditorPanel")
            .IsVisible =
                false;

        Get<Border>(
            "ServersEditorEmptyPanel")
            .IsVisible =
                true;

        UpdateServersActionState();
    }

    private void RefreshServersPage()
    {
        var list =
            Get<ListBox>(
                "ServersTargetList");

        var selectedTargetId =
            (list.SelectedItem as
                WindowsTargetRow)
            ?.TargetId;

        _suppressServersSelection =
            true;

        try
        {
            list.ItemsSource =
                _targetRows;

            list.SelectedItem =
                _targetRows.FirstOrDefault(
                    row =>
                        row.TargetId.Equals(
                            selectedTargetId,
                            StringComparison.Ordinal));
        }
        finally
        {
            _suppressServersSelection =
                false;
        }

        SetText(
            "ServersSummaryText",
            $"{_targetRows.Count} target(s) | " +
            $"{_targetRows.Count(row => row.IsLocal)} local | " +
            $"{_targetRows.Count(row => !row.IsLocal)} remote");

        SetText(
            "ServersVaultStatusText",
            _targetSession.CredentialVaultAvailable
                ? "Windows Credential Manager available"
                : "Windows Credential Manager unavailable");

        UpdateServersActionState();
    }

    private void ServersTargetList_OnSelectionChanged(
        object? sender,
        SelectionChangedEventArgs e)
    {
        if (_suppressServersSelection)
            return;

        ResetRemoveConfirmation();
        UpdateServersActionState();
    }

    private void ServersAddButton_OnClick(
        object? sender,
        RoutedEventArgs e)
    {
        _editingTargetId =
            null;

        Get<TextBox>(
            "ServersTargetIdTextBox")
            .IsEnabled =
                true;

        SetEditorValue(
            "ServersTargetIdTextBox",
            string.Empty);

        SetEditorValue(
            "ServersDisplayNameTextBox",
            string.Empty);

        SetEditorValue(
            "ServersHostTextBox",
            string.Empty);

        SetEditorValue(
            "ServersPortTextBox",
            RemoteWindowsConnectionParser
                .DefaultWinRmHttpsPort
                .ToString(
                    System.Globalization.CultureInfo.InvariantCulture));

        SetEditorValue(
            "ServersUsernameTextBox",
            string.Empty);

        SetEditorValue(
            "ServersPasswordTextBox",
            string.Empty);

        SetEditorValue(
            "ServersTimeoutTextBox",
            "60");

        SetEditorValue(
            "ServersCertificatePinTextBox",
            string.Empty);

        Get<ComboBox>(
            "ServersAuthenticationComboBox")
            .SelectedItem =
                nameof(
                    WindowsRemoteAuthentication.Negotiate);

        SetEditorStatus(
            "Create a remote Windows target. " +
            "The password will be stored only in Windows Credential Manager.",
            isFailure: false);

        Get<Border>(
            "ServersEditorPanel")
            .IsVisible =
                true;

        Get<Border>(
            "ServersEditorEmptyPanel")
            .IsVisible =
                false;

        Get<TextBox>(
            "ServersTargetIdTextBox")
            .Focus();
    }

    private async void ServersEditButton_OnClick(
        object? sender,
        RoutedEventArgs e)
    {
        var row =
            SelectedServerRow();

        if (row is null ||
            row.IsLocal)
        {
            return;
        }

        try
        {
            var target =
                await _targetSession.FindAsync(
                    row.TargetId) ??
                throw new KeyNotFoundException(
                    $"Target '{row.TargetId}' was not found.");

            var options =
                RemoteWindowsConnectionParser.Parse(
                    target);

            _editingTargetId =
                target.Id;

            Get<TextBox>(
                "ServersTargetIdTextBox")
                .IsEnabled =
                    false;

            SetEditorValue(
                "ServersTargetIdTextBox",
                target.Id);

            SetEditorValue(
                "ServersDisplayNameTextBox",
                target.DisplayName);

            SetEditorValue(
                "ServersHostTextBox",
                options.Host);

            SetEditorValue(
                "ServersPortTextBox",
                options.Port.ToString(
                    System.Globalization.CultureInfo.InvariantCulture));

            SetEditorValue(
                "ServersUsernameTextBox",
                options.Username);

            SetEditorValue(
                "ServersPasswordTextBox",
                string.Empty);

            SetEditorValue(
                "ServersTimeoutTextBox",
                ((int)options.OperationTimeout.TotalSeconds)
                    .ToString(
                        System.Globalization.CultureInfo.InvariantCulture));

            SetEditorValue(
                "ServersCertificatePinTextBox",
                options.PinnedServerCertificateSha256 ??
                string.Empty);

            Get<ComboBox>(
                "ServersAuthenticationComboBox")
                .SelectedItem =
                    options.Authentication.ToString();

            SetEditorStatus(
                "Leave password blank to keep the current Credential Manager entry.",
                isFailure: false);

            Get<Border>(
                "ServersEditorPanel")
                .IsVisible =
                    true;

            Get<Border>(
                "ServersEditorEmptyPanel")
                .IsVisible =
                    false;
        }
        catch (Exception exception)
        {
            SetServersStatus(
                "Could not load target: " +
                exception.Message,
                isFailure: true);
        }
    }

    private async void ServersSaveButton_OnClick(
        object? sender,
        RoutedEventArgs e)
    {
        var saveButton =
            Get<Button>(
                "ServersSaveButton");

        saveButton.IsEnabled =
            false;

        var isNewTarget =
            string.IsNullOrWhiteSpace(
                _editingTargetId);

        var password =
            EditorValue(
                "ServersPasswordTextBox");

        var storedCredential =
            false;

        string? createdTargetId =
            null;

        try
        {
            var target =
                WindowsTargetEditorPolicy.CreateTarget(
                    new WindowsRemoteTargetDraft(
                        isNewTarget
                            ? EditorValue(
                                "ServersTargetIdTextBox")
                            : _editingTargetId!,
                        EditorValue(
                            "ServersDisplayNameTextBox"),
                        EditorValue(
                            "ServersHostTextBox"),
                        EditorValue(
                            "ServersPortTextBox"),
                        EditorValue(
                            "ServersUsernameTextBox"),
                        Get<ComboBox>(
                            "ServersAuthenticationComboBox")
                            .SelectedItem
                            ?.ToString() ??
                        string.Empty,
                        EditorValue(
                            "ServersTimeoutTextBox"),
                        EditorValue(
                            "ServersCertificatePinTextBox")));

            if (WindowsTargetEditorPolicy.RequiresPassword(
                    isNewTarget,
                    password))
            {
                throw new InvalidOperationException(
                    "A password is required for a new remote target.");
            }

            if (isNewTarget)
            {
                await _targetSession.CreateAsync(
                    target);

                createdTargetId =
                    target.Id;
            }

            if (!string.IsNullOrWhiteSpace(
                    password))
            {
                if (!_targetSession.CredentialVaultAvailable)
                {
                    throw new PlatformNotSupportedException(
                        "Windows Credential Manager is unavailable.");
                }

                await _targetSession.StoreCredentialAsync(
                    target.Id,
                    password);

                storedCredential =
                    true;
            }

            if (!isNewTarget)
            {
                await _targetSession.UpsertAsync(
                    target);
            }

            SetTargetRows(
                await _targetSession.ListAsync());

            Get<Border>(
                "ServersEditorPanel")
                .IsVisible =
                    false;

            Get<Border>(
                "ServersEditorEmptyPanel")
                .IsVisible =
                    true;

            SetServersStatus(
                $"Saved {target.DisplayName}.",
                isFailure: false);

            RecordActivity(
                "Target saved",
                $"{target.DisplayName} | " +
                $"{WindowsTargetUiProjection.ConnectionSummary(target)}.");

            if (_targetSession.SelectedTarget?.Id.Equals(
                    target.Id,
                    StringComparison.Ordinal) ==
                true)
            {
                await RefreshAsync();
            }
        }
        catch (Exception exception)
        {
            if (createdTargetId is not null)
            {
                try
                {
                    await _targetSession.RemoveAsync(
                        createdTargetId);
                }
                catch
                {
                    // Preserve the original save failure.
                }

                if (storedCredential)
                {
                    try
                    {
                        await _targetSession.DeleteCredentialAsync(
                            createdTargetId);
                    }
                    catch
                    {
                        // Preserve the original save failure.
                    }
                }
            }

            SetEditorStatus(
                exception.Message,
                isFailure: true);
        }
        finally
        {
            SetEditorValue(
                "ServersPasswordTextBox",
                string.Empty);

            saveButton.IsEnabled =
                true;
        }
    }

    private void ServersCancelButton_OnClick(
        object? sender,
        RoutedEventArgs e)
    {
        _editingTargetId =
            null;

        SetEditorValue(
            "ServersPasswordTextBox",
            string.Empty);

        Get<Border>(
            "ServersEditorPanel")
            .IsVisible =
                false;

        Get<Border>(
            "ServersEditorEmptyPanel")
            .IsVisible =
                true;
    }

    private async void ServersRemoveButton_OnClick(
        object? sender,
        RoutedEventArgs e)
    {
        var row =
            SelectedServerRow();

        if (row is null ||
            row.IsLocal)
        {
            return;
        }

        if (!WindowsTargetEditorPolicy.IsRemovalConfirmed(
                _pendingRemoveTargetId,
                row.TargetId))
        {
            _pendingRemoveTargetId =
                row.TargetId;

            Get<Button>(
                "ServersRemoveButton")
                .Content =
                    "Confirm remove";

            SetServersStatus(
                $"Click Confirm remove to delete {row.DisplayName}.",
                isFailure: true);

            return;
        }

        try
        {
            var wasActive =
                _targetSession.SelectedTarget?.Id.Equals(
                    row.TargetId,
                    StringComparison.Ordinal) ==
                true;

            if (!await _targetSession.RemoveAsync(
                    row.TargetId))
            {
                throw new InvalidOperationException(
                    "The selected target could not be removed.");
            }

            if (_targetSession.CredentialVaultAvailable)
            {
                try
                {
                    await _targetSession.DeleteCredentialAsync(
                        row.TargetId);
                }
                catch (Exception credentialException)
                {
                    RecordActivity(
                        "Credential cleanup warning",
                        credentialException.Message);
                }
            }

            SetTargetRows(
                await _targetSession.ListAsync());

            SetServersStatus(
                $"Removed {row.DisplayName}.",
                isFailure: false);

            RecordActivity(
                "Target removed",
                row.DisplayName);

            if (wasActive)
            {
                await RefreshAsync();
            }
        }
        catch (Exception exception)
        {
            SetServersStatus(
                "Target removal failed: " +
                exception.Message,
                isFailure: true);
        }
        finally
        {
            ResetRemoveConfirmation();
            UpdateServersActionState();
        }
    }

    private WindowsTargetRow? SelectedServerRow() =>
        Get<ListBox>(
            "ServersTargetList")
        .SelectedItem as
            WindowsTargetRow;

    private void UpdateServersActionState()
    {
        var selected =
            SelectedServerRow();

        var editable =
            selected is not null &&
            !selected.IsLocal;

        Get<Button>(
            "ServersEditButton")
            .IsEnabled =
                editable;

        Get<Button>(
            "ServersRemoveButton")
            .IsEnabled =
                editable;
    }

    private void ResetRemoveConfirmation()
    {
        _pendingRemoveTargetId =
            null;

        Get<Button>(
            "ServersRemoveButton")
            .Content =
                "Remove";
    }

    private string EditorValue(
        string controlName) =>
        Get<TextBox>(
            controlName)
        .Text
        ?.Trim() ??
        string.Empty;

    private void SetEditorValue(
        string controlName,
        string value)
    {
        Get<TextBox>(
            controlName)
        .Text =
            value;
    }

    private void SetEditorStatus(
        string message,
        bool isFailure)
    {
        var status =
            Get<TextBlock>(
                "ServersEditorStatusText");

        status.Text =
            message;

        status.Foreground =
            Application.Current?
                .TryFindResource(
                    isFailure
                        ? "DangerBrush"
                        : "MutedBrush",
                    ActualThemeVariant,
                    out var resource) ==
                true
                    ? resource as
                        Avalonia.Media.IBrush
                    : null;
    }

    private void SetServersStatus(
        string message,
        bool isFailure)
    {
        var status =
            Get<TextBlock>(
                "ServersStatusText");

        status.Text =
            message;

        status.Foreground =
            Application.Current?
                .TryFindResource(
                    isFailure
                        ? "DangerBrush"
                        : "MutedBrush",
                    ActualThemeVariant,
                    out var resource) ==
                true
                    ? resource as
                        Avalonia.Media.IBrush
                    : null;
    }
}