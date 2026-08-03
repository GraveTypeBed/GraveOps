using System.Diagnostics;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;

namespace GraveOps.Desktop.Linux;

public sealed class LinuxMediaApplicationRow
{
    public required OpsIntegration Integration { get; init; }
    public required string IntegrationName { get; init; }
    public required string SourceKey { get; init; }
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
    private string _selectedIdentitySourceKey =
        string.Empty;

    private IReadOnlyList<LinuxMediaApplicationRow>
        _mediaRows =
            Array.Empty<LinuxMediaApplicationRow>();

    private bool _showHiddenMediaApplications;

    private void InitializeMediaWorkspace()
    {
        Get<TextBlock>(
                "MediaLauncherStorePathText")
            .Text =
            _applicationIdentityStore.FilePath;

        Get<ComboBox>(
                "IdentityProductComboBox")
            .ItemsSource =
            ApplicationIdentityCatalog.ProductNames;

        Get<ComboBox>(
                "IdentityRoleComboBox")
            .ItemsSource =
            ApplicationIdentityRoles.All;

        ShowMediaFleetOverview();
    }

    private void PopulateMediaHub()
    {
        var selectedSource =
            SelectedMediaRow()?.SourceKey;

        var registrySelectedSource =
            SelectedMediaLauncherRow()?.SourceKey ??
            _selectedIdentitySourceKey;

        _mediaRows =
            _integrations
                .Select(BuildMediaApplicationRow)
                .OrderBy(item => item.Category)
                .ThenBy(item => item.DisplayName)
                .ThenBy(item => item.SourceKey)
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
                item.SourceKey.Equals(
                    selectedSource,
                    StringComparison.OrdinalIgnoreCase)) ??
            visibleRows.FirstOrDefault();

        Get<Border>("MediaHubEmptyState")
            .IsVisible =
            visibleRows.Length == 0;

        var identities =
            Get<ListBox>(
                "MediaLauncherSettingsList");

        identities.ItemsSource =
            _identityResolution.Records;

        identities.SelectedItem =
            _identityResolution.Records
                .FirstOrDefault(item =>
                    item.SourceKey.Equals(
                        registrySelectedSource,
                        StringComparison.OrdinalIgnoreCase)) ??
            _identityResolution.Records
                .FirstOrDefault();

        Get<TextBlock>(
                "IdentityRegistrySummaryText")
            .Text =
            $"{_identityResolution.Records.Count} detected source(s) · " +
            $"{_integrations.Count(item => item.IsVerified && item.OwnsHealth)} verified health owner(s) · " +
            $"{_identityResolution.Records.Count(item => !item.IsVerified)} candidate(s)";

        var offline =
            _integrations.Count(item =>
                item.OwnsHealth &&
                item.IsVerified &&
                (item.Severity >= OpsSeverity.Error ||
                 item.State.Contains(
                     "offline",
                     StringComparison.OrdinalIgnoreCase) ||
                 item.State.Contains(
                     "unavailable",
                     StringComparison.OrdinalIgnoreCase) ||
                 item.State.Contains(
                     "not detected",
                     StringComparison.OrdinalIgnoreCase)));

        var attention =
            _integrations.Count(item =>
                item.OwnsHealth &&
                item.IsVerified &&
                item.Severity ==
                    OpsSeverity.Warning &&
                !item.State.Contains(
                    "offline",
                    StringComparison.OrdinalIgnoreCase) &&
                !item.State.Contains(
                    "unavailable",
                    StringComparison.OrdinalIgnoreCase));

        var healthOwners =
            _integrations.Count(item =>
                item.OwnsHealth &&
                item.IsVerified);

        var healthy =
            Math.Max(
                0,
                healthOwners -
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
            $"{_integrations.Count} application instance(s)";

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
        var displayName =
            string.IsNullOrWhiteSpace(
                integration.DisplayName)
                ? integration.Name
                : integration.DisplayName.Trim();

        var category =
            string.IsNullOrWhiteSpace(
                integration.Category)
                ? DefaultMediaCategory(
                    integration.Name)
                : integration.Category.Trim();

        var url =
            ResolveIntegrationUrl(
                integration);

        LinuxPlexSnapshot? plexSnapshot =
            null;

        if (integration.Name.Equals(
                "Plex",
                StringComparison.OrdinalIgnoreCase))
        {
            _plexCache.TryGetValue(
                _controlPlane.ActiveProfile.Id,
                out plexSnapshot);
        }

        var liveSeverity =
            !integration.IsVerified ||
            !integration.OwnsHealth
                ? OpsSeverity.Info
                : plexSnapshot is null
                    ? integration.Severity
                    : PlexSeverity(
                        plexSnapshot.State);

        var runtimeText =
            plexSnapshot is null
                ? $"{integration.Kind} · {integration.Role} · " +
                  $"{(string.IsNullOrWhiteSpace(integration.Protocol) ? "--" : integration.Protocol)}"
                : $"{plexSnapshot.Service} · " +
                  $"{plexSnapshot.ActiveSessions} active";

        var endpointText =
            url ??
            (string.IsNullOrWhiteSpace(
                integration.Endpoint)
                ? "No verified endpoint"
                : integration.IsVerified
                    ? integration.Endpoint
                    : $"Suggested · {integration.Endpoint}");

        var evidence =
            plexSnapshot is null
                ? $"{(integration.IsVerified ? "Verified" : "Candidate")} · " +
                  $"{integration.Evidence}"
                : $"Live Plex · " +
                  $"{plexSnapshot.ActiveSessions} sessions · " +
                  $"{plexSnapshot.TotalBandwidth} · " +
                  $"{plexSnapshot.LibraryCount} libraries";

        return new LinuxMediaApplicationRow
        {
            Integration =
                integration,
            IntegrationName =
                integration.Name,
            SourceKey =
                integration.InstanceKey,
            DisplayName =
                displayName,
            Category =
                category,
            RuntimeText =
                runtimeText,
            EndpointText =
                endpointText,
            Evidence =
                evidence,
            OpenLabel =
                NavigationForIntegration(
                    integration.Name) is null
                    ? "Open interface"
                    : "Open in GraveOps",
            VisibilityText =
                integration.IsVisible
                    ? "Visible in Fleet overview"
                    : "Hidden from Fleet overview",
            Url =
                url ??
                "No verified URL",
            StateLabel =
                integration.IsVerified
                    ? LinuxOpsAnalyzer.SeverityLabel(
                        liveSeverity)
                    : "UNVERIFIED",
            StateForeground =
                OpsPalette.Foreground(
                    liveSeverity),
            StateBackground =
                OpsPalette.Background(
                    liveSeverity),
            IsVisible =
                integration.IsVisible
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
        SelectedMediaRow() =>
        Get<ListBox>("IntegrationsList")
            .SelectedItem as
        LinuxMediaApplicationRow;

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

    private ApplicationIdentityRecord?
        SelectedMediaLauncherRow() =>
        Get<ListBox>(
                "MediaLauncherSettingsList")
            .SelectedItem as
        ApplicationIdentityRecord;

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
                "Select a detected source.";
            Get<TextBox>("MediaLauncherDisplayNameTextBox")
                .Text =
                string.Empty;
            Get<TextBox>("IdentityProtocolTextBox")
                .Text =
                string.Empty;
            Get<TextBox>("MediaLauncherUrlTextBox")
                .Text =
                string.Empty;
            Get<TextBox>("MediaLauncherCategoryTextBox")
                .Text =
                string.Empty;
            Get<ComboBox>("IdentityProductComboBox")
                .SelectedItem =
                null;
            Get<ComboBox>("IdentityRoleComboBox")
                .SelectedItem =
                null;
            Get<ComboBox>("IdentityParentComboBox")
                .ItemsSource =
                new[]
                {
                    new IdentityOwnerOption(
                        string.Empty,
                        "No parent / independent instance")
                };
            Get<ComboBox>("IdentityParentComboBox")
                .SelectedIndex =
                0;
            Get<CheckBox>("IdentityOwnsHealthCheckBox")
                .IsChecked =
                false;
            Get<CheckBox>("IdentityShowNavigationCheckBox")
                .IsChecked =
                false;
            Get<CheckBox>("MediaLauncherVisibleCheckBox")
                .IsChecked =
                true;
            Get<TextBlock>("IdentityVerificationText")
                .Text =
                "--";
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

        _selectedIdentitySourceKey =
            selected.SourceKey;

        var profile =
            _applicationIdentityStore.Get(
                selected.SourceKey);

        Get<TextBlock>("MediaLauncherSelectedText")
            .Text =
            selected.SourceKey;
        Get<ComboBox>("IdentityProductComboBox")
            .SelectedItem =
            selected.Product;
        Get<ComboBox>("IdentityRoleComboBox")
            .SelectedItem =
            selected.Role;
        Get<TextBox>("MediaLauncherDisplayNameTextBox")
            .Text =
            selected.DisplayName;
        Get<TextBox>("IdentityProtocolTextBox")
            .Text =
            selected.Protocol;
        Get<TextBox>("MediaLauncherUrlTextBox")
            .Text =
            profile?.UrlOverride ??
            selected.Endpoint;
        Get<TextBox>("MediaLauncherCategoryTextBox")
            .Text =
            selected.Category;
        Get<CheckBox>("IdentityOwnsHealthCheckBox")
            .IsChecked =
            selected.OwnsHealth;
        Get<CheckBox>("IdentityShowNavigationCheckBox")
            .IsChecked =
            selected.ShowInNavigation;
        Get<CheckBox>("MediaLauncherVisibleCheckBox")
            .IsChecked =
            selected.IsVisible;

        PopulateIdentityOwnerOptions(selected);

        Get<TextBlock>("IdentityVerificationText")
            .Text =
            $"{selected.VerificationLabel} · " +
            $"{selected.Role} · confidence {selected.Confidence}";
        Get<TextBlock>("MediaLauncherDetectedText")
            .Text =
            $"{selected.Kind} · {selected.State}" +
            Environment.NewLine +
            selected.Evidence;

        save.IsEnabled =
            true;
        reset.IsEnabled =
            profile is not null;

        var integration =
            _integrations.FirstOrDefault(item =>
                item.InstanceKey.Equals(
                    selected.SourceKey,
                    StringComparison.OrdinalIgnoreCase));
        open.IsEnabled =
            integration is not null &&
            ResolveIntegrationUrl(integration) is not null;
    }

    private void PopulateIdentityOwnerOptions(
        ApplicationIdentityRecord selected)
    {
        var options =
            new[]
            {
                new IdentityOwnerOption(
                    string.Empty,
                    "No parent / independent instance")
            }
            .Concat(
                _identityResolution.Records
                    .Where(item =>
                        !item.SourceKey.Equals(
                            selected.SourceKey,
                            StringComparison.OrdinalIgnoreCase) &&
                        ApplicationIdentityRoles.IsTopLevel(
                            item.Role))
                    .Select(item =>
                        new IdentityOwnerOption(
                            item.SourceKey,
                            $"{item.DisplayName} · {item.Product}")))
            .ToArray();

        var combo =
            Get<ComboBox>(
                "IdentityParentComboBox");

        combo.ItemsSource =
            options;
        combo.SelectedItem =
            options.FirstOrDefault(item =>
                item.SourceKey.Equals(
                    selected.ParentSourceKey,
                    StringComparison.OrdinalIgnoreCase)) ??
            options[0];
    }

    private async void MediaLauncherSaveButton_OnClick(
        object? sender,
        RoutedEventArgs e)
    {
        var selected =
            SelectedMediaLauncherRow();

        if (selected is null)
            return;

        try
        {
            var product =
                Get<ComboBox>(
                        "IdentityProductComboBox")
                    .SelectedItem as string ??
                selected.Product;
            var role =
                Get<ComboBox>(
                        "IdentityRoleComboBox")
                    .SelectedItem as string ??
                selected.Role;
            var parent =
                Get<ComboBox>(
                        "IdentityParentComboBox")
                    .SelectedItem as
                IdentityOwnerOption;

            _applicationIdentityStore.Save(
                new ApplicationIdentityProfile
                {
                    SourceKey =
                        selected.SourceKey,
                    Product =
                        product,
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
                    Role =
                        role,
                    Protocol =
                        Get<TextBox>(
                                "IdentityProtocolTextBox")
                            .Text ??
                        string.Empty,
                    ParentSourceKey =
                        parent?.SourceKey ??
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
                        false,
                    ShowInNavigation =
                        Get<CheckBox>(
                                "IdentityShowNavigationCheckBox")
                            .IsChecked ==
                        true,
                    OwnsHealth =
                        Get<CheckBox>(
                                "IdentityOwnsHealthCheckBox")
                            .IsChecked ==
                        true,
                    Confirmed =
                        true
                });

            _selectedIdentitySourceKey =
                selected.SourceKey;

            Get<TextBlock>("MediaLauncherStatusText")
                .Text =
                $"Saved identity for {selected.SourceKey}.";

            await RefreshAsync();
            ShowMediaLauncherSettings();
            SelectIdentityRegistrySource(
                selected.SourceKey);
        }
        catch (Exception exception)
        {
            Get<TextBlock>("MediaLauncherStatusText")
                .Text =
                exception.Message;
        }
    }

    private async void MediaLauncherResetButton_OnClick(
        object? sender,
        RoutedEventArgs e)
    {
        var selected =
            SelectedMediaLauncherRow();

        if (selected is null)
            return;

        _applicationIdentityStore.Reset(
            selected.SourceKey);

        _selectedIdentitySourceKey =
            selected.SourceKey;

        Get<TextBlock>("MediaLauncherStatusText")
            .Text =
            $"Automatic identity restored for " +
            $"{selected.SourceKey}.";

        await RefreshAsync();
        ShowMediaLauncherSettings();
        SelectIdentityRegistrySource(
            selected.SourceKey);
    }

    private void SelectIdentityRegistrySource(
        string sourceKey)
    {
        var list =
            Get<ListBox>(
                "MediaLauncherSettingsList");

        list.SelectedItem =
            _identityResolution.Records
                .FirstOrDefault(item =>
                    item.SourceKey.Equals(
                        sourceKey,
                        StringComparison.OrdinalIgnoreCase)) ??
            _identityResolution.Records
                .FirstOrDefault();

        PopulateMediaLauncherEditor();
    }

    private void MediaLauncherOpenButton_OnClick(
        object? sender,
        RoutedEventArgs e)
    {
        var selected =
            SelectedMediaLauncherRow();

        if (selected is null)
            return;

        var integration =
            _integrations.FirstOrDefault(item =>
                item.InstanceKey.Equals(
                    selected.SourceKey,
                    StringComparison.OrdinalIgnoreCase));

        if (integration is null)
        {
            Get<TextBlock>("MediaLauncherStatusText")
                .Text =
                "Supporting and compatibility records do not own a standalone interface.";
            return;
        }

        _ = OpenMediaIntegrationAsync(
            integration,
            "MediaLauncherStatusText");
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
