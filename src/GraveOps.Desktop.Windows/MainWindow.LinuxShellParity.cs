using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using GraveOps.Core.Hosts;

namespace GraveOps.Desktop.Windows;

public partial class MainWindow
{
    private IReadOnlyList<CommandRow> _linuxShellCommands =
        Array.Empty<CommandRow>();

    private bool _suppressTargetSelection;

    private void InitializeLinuxShellParity()
    {
        _linuxShellCommands = Navigation
            .Where(item => item.Key is not "IntegrationsNav" and not "WarningsNav")
            .Select(item => new CommandRow(
                item.Key,
                item.Value.Title,
                item.Value.Subtitle))
            .ToArray();


        Get<ListBox>("CommandPaletteList").ItemsSource =
            _linuxShellCommands;

        CloseLinuxShellDrawers();
        CloseLinuxCommandPalette();
    }

    private void MainWindow_OnKeyDown(
        object? sender,
        KeyEventArgs e)
    {
        if (e.Key == Key.K &&
            e.KeyModifiers.HasFlag(KeyModifiers.Control))
        {
            OpenLinuxCommandPalette();
            e.Handled = true;
            return;
        }

        if (e.Key == Key.Escape)
        {
            CloseLinuxCommandPalette();
            CloseLinuxShellDrawers();
            e.Handled = true;
        }
    }

    private async void ActiveTargetComboBox_OnSelectionChanged(
        object? sender,
        SelectionChangedEventArgs e)
    {
        if (_suppressTargetSelection)
            return;

        if (Get<ComboBox>(
                "ActiveTargetComboBox")
            .SelectedItem is not
            WindowsTargetRow targetRow)
        {
            return;
        }

        await SelectActiveTargetAsync(
            targetRow);
    }

    private void DashboardQuickModuleButton_OnClick(
        object? sender,
        RoutedEventArgs e)
    {
        if (sender is Button button &&
            button.Tag is string navigationName)
        {
            Navigate(navigationName);
        }
    }

    private void OverviewButton_OnClick(
        object? sender,
        RoutedEventArgs e)
    {
        var drawer = Get<Border>("OverviewDrawer");
        var open = !drawer.IsVisible;

        CloseLinuxShellDrawers();
        drawer.IsVisible = open;
    }

    private void JobsButton_OnClick(
        object? sender,
        RoutedEventArgs e)
    {
        var drawer = Get<Border>("JobsDrawer");
        var open = !drawer.IsVisible;

        CloseLinuxShellDrawers();
        drawer.IsVisible = open;
    }

    private void ActivityButton_OnClick(
        object? sender,
        RoutedEventArgs e)
    {
        var drawer = Get<Border>("ActivityDrawer");
        var open = !drawer.IsVisible;

        CloseLinuxShellDrawers();
        Get<ListBox>("ActivityDrawerList").ItemsSource =
            _activity.ToArray();
        drawer.IsVisible = open;
    }

    private void CloseDrawersButton_OnClick(
        object? sender,
        RoutedEventArgs e) =>
        CloseLinuxShellDrawers();

    private void CloseLinuxShellDrawers()
    {
        Get<Border>("OverviewDrawer").IsVisible = false;
        Get<Border>("JobsDrawer").IsVisible = false;
        Get<Border>("ActivityDrawer").IsVisible = false;
    }

    private void MaintenanceButton_OnClick(
        object? sender,
        RoutedEventArgs e)
    {
        SetText(
            "FooterStatusText",
            "Maintenance actions remain disabled during read-only parity.");

        RecordActivity(
            "Maintenance",
            "Mutation controls remain disabled.");
    }

    private void CommandPaletteButton_OnClick(
        object? sender,
        RoutedEventArgs e) =>
        OpenLinuxCommandPalette();

    private void OpenLinuxCommandPalette()
    {
        CloseLinuxShellDrawers();

        Get<Border>("CommandPalette").IsVisible = true;
        Get<TextBox>("CommandPaletteSearchText").Text =
            string.Empty;
        Get<ListBox>("CommandPaletteList").ItemsSource =
            _linuxShellCommands;
        Get<TextBox>("CommandPaletteSearchText").Focus();
    }

    private void CloseCommandPaletteButton_OnClick(
        object? sender,
        RoutedEventArgs e) =>
        CloseLinuxCommandPalette();

    private void CloseLinuxCommandPalette()
    {
        Get<Border>("CommandPalette").IsVisible = false;
        Get<ListBox>("CommandPaletteList").SelectedItem = null;
    }

    private void CommandPaletteSearchText_OnTextChanged(
        object? sender,
        TextChangedEventArgs e)
    {
        var query =
            Get<TextBox>("CommandPaletteSearchText").Text?.Trim() ??
            string.Empty;

        Get<ListBox>("CommandPaletteList").ItemsSource =
            string.IsNullOrWhiteSpace(query)
                ? _linuxShellCommands
                : _linuxShellCommands
                    .Where(item =>
                        item.Title.Contains(
                            query,
                            StringComparison.OrdinalIgnoreCase) ||
                        item.Subtitle.Contains(
                            query,
                            StringComparison.OrdinalIgnoreCase))
                    .ToArray();
    }

    private void CommandPaletteList_OnSelectionChanged(
        object? sender,
        SelectionChangedEventArgs e)
    {
        var list = Get<ListBox>("CommandPaletteList");

        if (list.SelectedItem is not CommandRow command)
            return;

        list.SelectedItem = null;
        Navigate(command.NavigationName);
    }

    private void PopulateLinuxShellParity(
        HostSnapshot snapshot)
    {

        SetText("OverviewHostText", snapshot.Hostname);
        SetText(
            "OverviewSystemText",
            $"{snapshot.OperatingSystem} | {snapshot.IpAddresses}");
        SetText(
            "OverviewStorageText",
            snapshot.Storage.Count.ToString());
        var recommendations =
            BuildRecommendations(snapshot);

        SetText(
            "OverviewAttentionText",
            recommendations.Count.ToString());
        SetText(
            "OverviewCaptureText",
            snapshot.CapturedAt.ToLocalTime().ToString("g"));

        var intelligenceRows =
            recommendations
                .Select(item =>
                    $"{item.Severity} | {item.Component} | {item.Message}")
                .Concat(
                    snapshot.Warnings.Select(warning =>
                        $"WARN | Provider | {warning}"))
                .Distinct(StringComparer.Ordinal)
                .ToArray();

        Get<ListBox>("WarningsList").ItemsSource =
            intelligenceRows.Length == 0
                ? new[] { "No current findings." }
                : intelligenceRows;

        Get<ListBox>("ActivityDrawerList").ItemsSource =
            _activity.ToArray();

        SetText(
            "FooterStatusText",
            $"{snapshot.Hostname} | " +
            $"{ActiveTargetConnectionSummary()} | read-only");
    }

    private sealed record CommandRow(
        string NavigationName,
        string Title,
        string Subtitle);

}
