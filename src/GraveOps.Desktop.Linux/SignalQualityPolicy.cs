namespace GraveOps.Desktop.Linux;

public static class SignalQualityPolicy
{
    private const string PortalSource =
        "xdg-desktop-portal";
    private const string BenignParentWindowMessage =
        "Unhandled parent window type";

    public static bool IsBenignPortalParentWarning(
        OpsLogGroup log)
    {
        var source =
            log.Source ?? string.Empty;
        var message =
            log.Message ?? string.Empty;

        var portalEvidence =
            source.Contains(
                PortalSource,
                StringComparison.OrdinalIgnoreCase) ||
            message.Contains(
                PortalSource,
                StringComparison.OrdinalIgnoreCase);

        return portalEvidence &&
               message.Contains(
                   BenignParentWindowMessage,
                   StringComparison.OrdinalIgnoreCase);
    }

    public static OpsSeverity DisplaySeverity(
        OpsLogGroup log) =>
        IsBenignPortalParentWarning(log)
            ? OpsSeverity.Info
            : log.Severity;

    public static IReadOnlyList<OpsLogGroup>
        ForHealthAnalysis(
            IReadOnlyList<OpsLogGroup> logs,
            out int excludedGroups)
    {
        excludedGroups =
            logs.Count(IsBenignPortalParentWarning);

        return logs
            .Where(log =>
                !IsBenignPortalParentWarning(log))
            .ToArray();
    }

    public static string Summary(
        int excludedGroups) =>
        excludedGroups == 0
            ? "No known benign desktop-portal warning was excluded from health scoring."
            : $"{excludedGroups} known benign desktop-portal " +
              $"{(excludedGroups == 1 ? "group was" : "groups were")} " +
              "demoted for display and excluded from health scoring. " +
              "Original journal evidence remains retained.";
}
