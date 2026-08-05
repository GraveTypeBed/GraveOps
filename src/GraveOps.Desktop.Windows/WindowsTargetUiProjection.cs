using GraveOps.Core.Targets;
using GraveOps.Platform.Windows;

namespace GraveOps.Desktop.Windows;

public sealed record WindowsTargetRow(
    string TargetId,
    string DisplayName,
    string ConnectionSummary,
    bool IsLocal);

public static class WindowsTargetUiProjection
{
    public static IReadOnlyList<WindowsTargetRow>
        CreateRows(
            IEnumerable<TargetProfile> targets)
    {
        ArgumentNullException.ThrowIfNull(
            targets);

        return targets
            .Select(
                target =>
                {
                    target.Validate();

                    return new WindowsTargetRow(
                        target.Id,
                        target.DisplayName,
                        ConnectionSummary(
                            target),
                        target.IsLocal);
                })
            .OrderByDescending(
                row =>
                    row.IsLocal)
            .ThenBy(
                row =>
                    row.DisplayName,
                StringComparer.OrdinalIgnoreCase)
            .ThenBy(
                row =>
                    row.TargetId,
                StringComparer.Ordinal)
            .ToArray();
    }

    public static string ConnectionSummary(
        TargetProfile target)
    {
        ArgumentNullException.ThrowIfNull(
            target);
        target.Validate();

        if (target.IsLocal)
        {
            return
                "Local | native Windows provider";
        }

        var host =
            target.Connection.Host?.Trim() ??
            "unknown-host";

        var port =
            target.Connection.Port ??
            RemoteWindowsConnectionParser
                .DefaultWinRmHttpsPort;

        return
            $"Remote | WinRM HTTPS | {host}:{port}";
    }

    public static string ProviderSummary(
        TargetProfile target)
    {
        ArgumentNullException.ThrowIfNull(
            target);

        return target.IsLocal
            ? "Native local Windows provider"
            : ConnectionSummary(
                target);
    }

    public static string CaptureStatus(
        TargetProfile target)
    {
        ArgumentNullException.ThrowIfNull(
            target);

        return
            $"Capturing {target.DisplayName} through " +
            $"{ProviderSummary(target)}...";
    }
}