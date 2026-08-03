using Avalonia.Controls;

namespace GraveOps.Desktop.Linux;

public partial class MainWindow
{
    private int _signalQualityExcludedGroups;

    private void RecordRoutineControlPlaneActivity(
        string kind,
        string target,
        string title,
        string detail,
        string navigationName,
        TimeSpan deduplicationWindow,
        bool unread = false)
    {
        var now =
            DateTimeOffset.Now;

        var duplicate =
            _controlPlane.State.Activities.Any(row =>
                row.Kind.Equals(
                    kind,
                    StringComparison.OrdinalIgnoreCase) &&
                row.Target.Equals(
                    target,
                    StringComparison.OrdinalIgnoreCase) &&
                row.Title.Equals(
                    title,
                    StringComparison.OrdinalIgnoreCase) &&
                now - row.Timestamp >= TimeSpan.Zero &&
                now - row.Timestamp <=
                    deduplicationWindow);

        if (duplicate)
            return;

        _controlPlane.State.RecordActivity(
            kind,
            target,
            title,
            detail,
            navigationName,
            unread);
    }

    private void PopulateSignalQualitySummary()
    {
        Get<TextBlock>("ServerSignalQualityText")
            .Text =
            SignalQualityPolicy.Summary(
                _signalQualityExcludedGroups);
    }
}
