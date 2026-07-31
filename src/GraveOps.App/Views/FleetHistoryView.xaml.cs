using System.Windows;
using System.Windows.Controls;
using GraveOps.App.Models;

namespace GraveOps.App.Views;

public partial class FleetHistoryView : UserControl
{
    private Services.AppServices S => App.Services;
    private IncidentReplaySnapshot? _replay;

    public FleetHistoryView()
    {
        InitializeComponent();
        Loaded += (_, _) => Bind();
    }

    private void Bind()
    {
        HealthGrid.ItemsSource = S.FleetHistory.Items;
        ActivityGrid.ItemsSource = S.Activity.Recent;
    }

    private void Replay_Click(object sender, RoutedEventArgs e) => ReplaySelected();
    private void HealthGrid_MouseDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e) => ReplaySelected();

    private void ReplaySelected()
    {
        if (HealthGrid.SelectedItem is not FleetHistoryRecord selected)
            return;

        _replay = S.FleetHistory.ReplayAround(selected.Timestamp, S.Activity);
        ReplayHeadingText.Text = $"±10 minutes around {selected.Timestamp.ToLocalTime():g} · {selected.Host} / {selected.Component}";

        var sb = new StringBuilder();
        var events = _replay.HealthEvents
            .Select(x => (x.Timestamp, Text: $"HEALTH  {x.Host} / {x.Component}: {x.TransitionText} · {x.Detail}"))
            .Concat(_replay.ActivityEvents.Select(x => (x.Timestamp, Text: $"ACTION  {x.Title} · {x.Detail}")))
            .OrderBy(x => x.Timestamp);

        foreach (var item in events)
            sb.AppendLine($"{item.Timestamp.ToLocalTime():HH:mm:ss}  {item.Text}");

        ReplayText.Text = sb.Length == 0 ? "No nearby events were recorded." : sb.ToString().TrimEnd();
    }

    private void CopyReplay_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(ReplayText.Text))
            return;
        Clipboard.SetText($"{ReplayHeadingText.Text}\n\n{ReplayText.Text}");
    }
}
