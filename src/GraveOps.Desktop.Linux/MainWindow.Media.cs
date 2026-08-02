using System.Diagnostics;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;

namespace GraveOps.Desktop.Linux;

public sealed class LinuxMediaApplicationRow
{
    public required OpsIntegration Integration { get; init; }
    public required string IntegrationName { get; init; }
    public required string DisplayName { get; init; }
    public required string Category { get; init; }
    public required string RuntimeText { get; init; }
    public required string EndpointText { get; init; }
    public required string Evidence { get; init; }
    public required string OpenLabel { get; init; }
    public required string VisibilityText { get; init; }
    public required string Url { get; init; }
    public required string StateLabel { get; init; }
    public required IBrush StateForeground { get; init; }
    public required IBrush StateBackground { get; init; }
    public bool IsVisible { get; init; }
}

public partial class MainWindow
{
    private readonly LinuxMediaLauncherStore
        _mediaLauncherStore =
            new();

    private IReadOnlyList<LinuxMediaApplicationRow>
        _mediaRows =
            Array.Empty<LinuxMediaApplicationRow>();

    private bool _showHiddenMediaApplications;

    private void InitializeMediaWorkspace()
    {
        Get<TextBlock>(
                "MediaLauncherStorePathText")
            .Text =
            _mediaLauncherStore.FilePath;

        ShowMediaFleetOverview();
    }

    private void PopulateMediaHub()
    {
        var selectedName =
            SelectedMediaRow()?.IntegrationName;

        var launcherSelectedName =
            (Get<ListBox>(
                    "MediaLauncherSettingsList")
                .SelectedItem as
                LinuxMediaApplicationRow)?
                .IntegrationName;

        _mediaRows =
            _integrations
                .Select(BuildMediaApplicationRow)
                .OrderBy(item => item.Category)
                .ThenBy(item => item.DisplayName)
                .ToArray();

        var filter =
            Get<TextBox>("MediaFilterText")
                .Text?
                .Trim();

        var visibleRows =
            _mediaRows
                .Where(item =>
                    _showHiddenMediaApplications ||
                    item.IsVisible)
                .Where(item =>
                    Matches(
                        filter,
                        item.DisplayName,
                        item.IntegrationName,
                        item.Category,
                        item.RuntimeText,
                        item.EndpointText,
                        item.Evidence))
                .ToArray();

        var cards =
            Get<ListBox>("IntegrationsList");

        cards.ItemsSource =
            visibleRows;

        cards.SelectedItem =
            visibleRows.FirstOrDefault(item =>
                item.IntegrationName.Equals(
                    selectedName,
                    StringComparison.OrdinalIgnoreCase)) ??
            visibleRows.FirstOrDefault();

        Get<Border>("MediaHubEmptyState")
            .IsVisible =
            visibleRows.Length == 0;

        var launchers =
            Get<ListBox>(
                "MediaLauncherSettingsList");

        launchers.ItemsSource =
            _mediaRows;

        launchers.SelectedItem =
            _mediaRows.FirstOrDefault(item =>
                item.IntegrationName.Equals(
                    launcherSelectedName,
                    StringComparison.OrdinalIgnoreCase)) ??
            _mediaRows.FirstOrDefault(item =>
                item.IntegrationName.Equals(
                    selectedName,
                    StringComparison.OrdinalIgnoreCase)) ??
            _mediaRows.FirstOrDefault();

        var offline =
            _integrations.Count(item =>
                item.Severity >= OpsSeverity.Error ||
                item.State.Contains(
                    "offline",
                    StringComparison.OrdinalIgnoreCase) ||
                item.State.Contains(
                    "unavailable",
                    StringComparison.OrdinalIgnoreCase) ||
                item.State.Contains(
                    "not detected",
                    StringComparison.OrdinalIgnoreCase));

        var attention =
            _integrations.Count(item =>
                item.Severity ==
                    OpsSeverity.Warning &&
                !item.State.Contains(
                    "offline",
                    StringComparison.OrdinalIgnoreCase) &&
                !item.State.Contains(
                    "unavailable",
                    StringComparison.OrdinalIgnoreCase));

        var healthy =
            Math.Max(
                0,
                _integrations.Count -
                offline -
                attention);

        Get<TextBlock>("MediaHealthyMetricText")
            .Text =
            healthy.ToString();

        Get<TextBlock>("MediaAttentionMetricText")
            .Text =
            attention.ToString();

        Get<TextBlock>("MediaOfflineMetricText")
            .Text =
            offline.ToString();

        Get<TextBlock>("MediaTargetMetricText")
            .Text =
            _controlPlane.ActiveProfile.DisplayName;

        Get<TextBlock>("MediaHubSummaryText")
            .Text =
            $"{visibleRows.Length} shown · " +
            $"{_integrations.Count} detected";

        Get<TextBlock>("MediaHubSampleAgeText")
            .Text =
            _snapshot is null
                ? "Waiting for capture"
                : $"Captured " +
                  $"{_snapshot.CapturedAt.ToLocalTime():g}";

        Get<Button>("MediaHubShowHiddenButton")
            .Content =
            _showHiddenMediaApplications
                ? "Hide hidden"
                : "Show hidden";

        PopulateIntegrationWorkspace();
        PopulateMediaLauncherEditor();
    }

    private LinuxMediaApplicationRow
        BuildMediaApplicationRow(
            OpsIntegration integration)
    {
        var profile =
            _mediaLauncherStore.Get(
                integration.Name);

        var displayName =
            string.IsNullOrWhiteSpace(
                profile?.DisplayName)
                ? integration.Name
                : profile.DisplayName.Trim();

        var category =
            string.IsNullOrWhiteSpace(
                profile?.Category)
                ? DefaultMediaCategory(
                    integration.Name)
                : profile.Category.Trim();

        var url =
            ResolveIntegrationUrl(
                integration);

        return new LinuxMediaApplicationRow
        {
            Integration =
                integration,
            IntegrationName =
                integration.Name,
            DisplayName =
                displayName,
            Category =
                category,
            RuntimeText =
                string.IsNullOrWhiteSpace(
                    integration.Kind)
                    ? "Detected"
                    : integration.Kind,
            EndpointText =
                url ??
                (string.IsNullOrWhiteSpace(
                    integration.Endpoint)
                    ? "No verified endpoint"
                    : integration.Endpoint),
            Evidence =
                string.IsNullOrWhiteSpace(
                    integration.Evidence)
                    ? "Detected without additional provider evidence."
                    : integration.Evidence,
            OpenLabel =
                NavigationForIntegration(
                    integration.Name) is null
                    ? "Open interface"
                    : "Open in GraveOps",
            VisibilityText =
                profile?.IsVisible == false
                    ? "Hidden from Fleet overview"
                    : "Visible in Fleet overview",
            Url =
                url ??
                "No verified URL",
            StateLabel =
                LinuxOpsAnalyzer.SeverityLabel(
                    integration.Severity),
            StateForeground =
                OpsPalette.Foreground(
                    integration.Severity),
            StateBackground =
                OpsPalette.Background(
                    integration.Severity),
            IsVisible =
                profile?.IsVisible != false
        };
    }

    private static string DefaultMediaCategory(
        string name) =>
        name.ToLowerInvariant() switch
        {
            "plex" or
            "tautulli" or
            "kometa" or
            "jellyfin" or
            "emby" =>
                "Library",
            "sonarr" or
            "radarr" or
            "lidarr" or
            "prowlarr" or
            "readarr" or
            "whisparr" or
            "mylar3" or
            "sabnzbd" or
            "qbittorrent" =>
                "Acquisition",
            "decypharr" or
            "recyclarr" or
            "bazarr" or
            "zurg" or
            "tdarr" or
            "unpackerr" =>
                "Processing",
            "dumb" =>
                "Orchestration",
            _ =>
                "Supporting service"
        };

    private string? NavigationForIntegration(
        string integrationName) =>
        IntegrationNavigationTargets
            .FirstOrDefault(item =>
                item.Value.Equals(
                    integrationName,
                    StringComparison.OrdinalIgnoreCase))
            .Key;

    private LinuxMediaApplicationRow?
        SelectedMediaRow()
    {
        if (Get<ListBox>("IntegrationsList")
                .SelectedItem is
            LinuxMediaApplicationRow selected)
        {
            return selected;
        }

        return Get<ListBox>(
                   "MediaLauncherSettingsList")
               .SelectedItem as
            LinuxMediaApplicationRow;
    }

    private OpsIntegration?
        SelectedMediaIntegration() =>
        SelectedMediaRow()?.Integration;

    private void SelectMediaIntegrationByName(
        string integrationName)
    {
        var row =
            _mediaRows.FirstOrDefault(item =>
                item.IntegrationName.Equals(
                    integrationName,
                    StringComparison.OrdinalIgnoreCase));

        if (row is null)
        {
            PopulateMediaHub();

            row =
                _mediaRows.FirstOrDefault(item =>
                    item.IntegrationName.Equals(
                        integrationName,
                        StringComparison.OrdinalIgnoreCase));
        }

        if (row is null)
            return;

        var cards =
            Get<ListBox>("IntegrationsList");

        if (cards.ItemsSource is
            IEnumerable<LinuxMediaApplicationRow>
            visible &&
            visible.Contains(row))
        {
            cards.SelectedItem =
                row;
        }

        Get<ListBox>(
                "MediaLauncherSettingsList")
            .SelectedItem =
            row;

        PopulateIntegrationWorkspace();
        PopulateMediaLauncherEditor();
    }

    private void MediaModeFleetButton_OnClick(
        object? sender,
        RoutedEventArgs e) =>
        ShowMediaFleetOverview();

    private void MediaModeLauncherButton_OnClick(
        object? sender,
        RoutedEventArgs e) =>
        ShowMediaLauncherSettings();

    private void ShowMediaFleetOverview()
    {
        Get<Grid>("MediaFleetOverviewPanel")
            .IsVisible =
            true;

        Get<Grid>("MediaLauncherSettingsPanel")
            .IsVisible =
            false;

        Get<Button>("MediaModeFleetButton")
            .Classes.Set(
                "selected",
                true);

        Get<Button>("MediaModeLauncherButton")
            .Classes.Set(
                "selected",
                false);
    }

    private void ShowMediaLauncherSettings()
    {
        Get<Grid>("MediaFleetOverviewPanel")
            .IsVisible =
            false;

        Get<Grid>("MediaLauncherSettingsPanel")
            .IsVisible =
            true;

        Get<Button>("MediaModeFleetButton")
            .Classes.Set(
                "selected",
                false);

        Get<Button>("MediaModeLauncherButton")
            .Classes.Set(
                "selected",
                true);

        PopulateMediaLauncherEditor();
    }

    private async void MediaHubRefreshButton_OnClick(
        object? sender,
        RoutedEventArgs e)
    {
        var button =
            Get<Button>("MediaHubRefreshButton");

        button.IsEnabled =
            false;

        button.Content =
            "Refreshing...";

        try
        {
            await RefreshAsync();
        }
        finally
        {
            button.IsEnabled =
                true;

            button.Content =
                "Refresh telemetry";
        }
    }

    private void MediaHubShowHiddenButton_OnClick(
        object? sender,
        RoutedEventArgs e)
    {
        _showHiddenMediaApplications =
            !_showHiddenMediaApplications;

        PopulateMediaHub();
    }

    private void MediaCardOpenButton_OnClick(
        object? sender,
        RoutedEventArgs e)
    {
        if (sender is not Button button ||
            button.Tag is not string
                integrationName)
        {
            return;
        }

        var navigationName =
            NavigationForIntegration(
                integrationName);

        if (!string.IsNullOrWhiteSpace(
                navigationName))
        {
            Navigate(navigationName);
            return;
        }

        var integration =
            _integrations.FirstOrDefault(item =>
                item.Name.Equals(
                    integrationName,
                    StringComparison.OrdinalIgnoreCase));

        if (integration is not null)
            _ = OpenMediaIntegrationAsync(
                integration,
                "IntegrationActionStatusText");
    }

    private void
        MediaLauncherSettingsList_OnSelectionChanged(
            object? sender,
            SelectionChangedEventArgs e) =>
        PopulateMediaLauncherEditor();

    private LinuxMediaApplicationRow?
        SelectedMediaLauncherRow() =>
        Get<ListBox>(
                "MediaLauncherSettingsList")
            .SelectedItem as
        LinuxMediaApplicationRow;

    private void PopulateMediaLauncherEditor()
    {
        var selected =
            SelectedMediaLauncherRow();

        var save =
            Get<Button>("MediaLauncherSaveButton");

        var reset =
            Get<Button>("MediaLauncherResetButton");

        var open =
            Get<Button>("MediaLauncherOpenButton");

        if (selected is null)
        {
            Get<TextBlock>("MediaLauncherSelectedText")
                .Text =
                "Select a detected application.";

            Get<TextBox>(
                    "MediaLauncherDisplayNameTextBox")
                .Text =
                string.Empty;

            Get<TextBox>(
                    "MediaLauncherUrlTextBox")
                .Text =
                string.Empty;

            Get<TextBox>(
                    "MediaLauncherCategoryTextBox")
                .Text =
                string.Empty;

            Get<CheckBox>(
                    "MediaLauncherVisibleCheckBox")
                .IsChecked =
                true;

            Get<TextBlock>("MediaLauncherDetectedText")
                .Text =
                "--";

            save.IsEnabled =
                false;

            reset.IsEnabled =
                false;

            open.IsEnabled =
                false;

            return;
        }

        var profile =
            _mediaLauncherStore.Get(
                selected.IntegrationName);

        Get<TextBlock>("MediaLauncherSelectedText")
            .Text =
            selected.IntegrationName;

        Get<TextBox>(
                "MediaLauncherDisplayNameTextBox")
            .Text =
            profile?.DisplayName ??
            string.Empty;

        Get<TextBox>(
                "MediaLauncherUrlTextBox")
            .Text =
            profile?.UrlOverride ??
            string.Empty;

        Get<TextBox>(
                "MediaLauncherCategoryTextBox")
            .Text =
            profile?.Category ??
            string.Empty;

        Get<CheckBox>(
                "MediaLauncherVisibleCheckBox")
            .IsChecked =
            profile?.IsVisible ??
            true;

        Get<TextBlock>("MediaLauncherDetectedText")
            .Text =
            $"{selected.RuntimeText} · " +
            $"{selected.EndpointText}";

        save.IsEnabled =
            true;

        reset.IsEnabled =
            profile is not null;

        open.IsEnabled =
            ResolveIntegrationUrl(
                selected.Integration) is not null;
    }

    private void MediaLauncherSaveButton_OnClick(
        object? sender,
        RoutedEventArgs e)
    {
        var selected =
            SelectedMediaLauncherRow();

        if (selected is null)
            return;

        try
        {
            _mediaLauncherStore.Save(
                new LinuxMediaLauncherProfile
                {
                    IntegrationName =
                        selected.IntegrationName,
                    DisplayName =
                        Get<TextBox>(
                                "MediaLauncherDisplayNameTextBox")
                            .Text ??
                        string.Empty,
                    Category =
                        Get<TextBox>(
                                "MediaLauncherCategoryTextBox")
                            .Text ??
                        string.Empty,
                    UrlOverride =
                        Get<TextBox>(
                                "MediaLauncherUrlTextBox")
                            .Text ??
                        string.Empty,
                    IsVisible =
                        Get<CheckBox>(
                                "MediaLauncherVisibleCheckBox")
                            .IsChecked !=
                        false
                });

            Get<TextBlock>("MediaLauncherStatusText")
                .Text =
                $"Saved launcher for " +
                $"{selected.IntegrationName}.";

            PopulateMediaHub();
            SelectMediaIntegrationByName(
                selected.IntegrationName);
        }
        catch (Exception exception)
        {
            Get<TextBlock>("MediaLauncherStatusText")
                .Text =
                exception.Message;
        }
    }

    private void MediaLauncherResetButton_OnClick(
        object? sender,
        RoutedEventArgs e)
    {
        var selected =
            SelectedMediaLauncherRow();

        if (selected is null)
            return;

        _mediaLauncherStore.Reset(
            selected.IntegrationName);

        Get<TextBlock>("MediaLauncherStatusText")
            .Text =
            $"Default launcher restored for " +
            $"{selected.IntegrationName}.";

        PopulateMediaHub();
        SelectMediaIntegrationByName(
            selected.IntegrationName);
    }

    private void MediaLauncherOpenButton_OnClick(
        object? sender,
        RoutedEventArgs e)
    {
        var selected =
            SelectedMediaLauncherRow();

        if (selected is not null)
        {
            _ = OpenMediaIntegrationAsync(
                selected.Integration,
                "MediaLauncherStatusText");
        }
    }

    private async Task OpenMediaIntegrationAsync(
        OpsIntegration integration,
        string statusControlName)
    {
        var url =
            ResolveIntegrationUrl(
                integration);

        var status =
            Get<TextBlock>(
                statusControlName);

        if (url is null)
        {
            status.Text =
                "No verified application URL is available.";
            return;
        }

        try
        {
            using var process =
                new Process
                {
                    StartInfo =
                        new ProcessStartInfo
                        {
                            FileName =
                                "xdg-open",
                            UseShellExecute =
                                false,
                            CreateNoWindow =
                                true
                        }
                };

            process.StartInfo.ArgumentList.Add(
                url);

            process.Start();

            status.Text =
                $"Opened {url}";

            await Task.CompletedTask;
        }
        catch (Exception exception)
        {
            status.Text =
                $"Could not open interface: " +
                $"{exception.Message}";
        }
    }
}
