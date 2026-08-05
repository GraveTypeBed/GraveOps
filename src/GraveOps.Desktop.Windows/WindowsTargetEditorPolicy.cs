using GraveOps.Core.Targets;
using GraveOps.Platform.Windows;

namespace GraveOps.Desktop.Windows;

public sealed record WindowsRemoteTargetDraft(
    string TargetId,
    string DisplayName,
    string Host,
    string Port,
    string Username,
    string Authentication,
    string OperationTimeoutSeconds,
    string? PinnedServerCertificateSha256);

public static class WindowsTargetEditorPolicy
{
    public static TargetProfile CreateTarget(
        WindowsRemoteTargetDraft draft)
    {
        ArgumentNullException.ThrowIfNull(
            draft);

        var targetId =
            Required(
                draft.TargetId,
                "Target ID");

        if (targetId.Equals(
                WindowsTargetCatalog.LocalTargetId,
                StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                "The local Windows target ID is reserved.");
        }

        var displayName =
            Required(
                draft.DisplayName,
                "Display name");

        var host =
            Required(
                draft.Host,
                "Host");

        var username =
            Required(
                draft.Username,
                "Username");

        if (!int.TryParse(
                draft.Port?.Trim(),
                out var port))
        {
            throw new InvalidOperationException(
                "The WinRM HTTPS port must be a number.");
        }

        if (!int.TryParse(
                draft.OperationTimeoutSeconds?.Trim(),
                out var operationTimeoutSeconds))
        {
            throw new InvalidOperationException(
                "The operation timeout must be a number.");
        }

        if (!Enum.TryParse<
                WindowsRemoteAuthentication>(
                draft.Authentication?.Trim(),
                ignoreCase: true,
                out var authentication) ||
            authentication is not
                WindowsRemoteAuthentication.Negotiate and not
                WindowsRemoteAuthentication.Basic)
        {
            throw new InvalidOperationException(
                "Authentication must be Negotiate or Basic.");
        }

        return WindowsTargetCatalog.CreateRemote(
            targetId,
            displayName,
            host,
            port,
            username,
            authentication,
            operationTimeoutSeconds,
            NullIfWhiteSpace(
                draft.PinnedServerCertificateSha256));
    }

    public static bool RequiresPassword(
        bool isNewTarget,
        string? password) =>
        isNewTarget &&
        string.IsNullOrWhiteSpace(
            password);

    public static bool IsRemovalConfirmed(
        string? pendingTargetId,
        string targetId)
    {
        if (string.IsNullOrWhiteSpace(
                targetId))
        {
            throw new ArgumentException(
                "The selected target ID is required.",
                nameof(targetId));
        }

        return string.Equals(
            pendingTargetId,
            targetId,
            StringComparison.Ordinal);
    }

    private static string Required(
        string? value,
        string label)
    {
        var normalized =
            value?.Trim();

        return string.IsNullOrWhiteSpace(
                normalized)
            ? throw new InvalidOperationException(
                $"{label} is required.")
            : normalized;
    }

    private static string? NullIfWhiteSpace(
        string? value)
    {
        var normalized =
            value?.Trim();

        return string.IsNullOrWhiteSpace(
                normalized)
            ? null
            : normalized;
    }
}