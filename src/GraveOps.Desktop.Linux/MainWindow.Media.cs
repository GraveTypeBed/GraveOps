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
    public required string OwnerTargetId { get; init; }
    public required string OwnerTargetName { get; init; }
    public required string DisplayName { get; init; }
    public required string Category { get; init; }
    public required string RuntimeText { get; init; }
    public required string RuntimeLabel { get; init; }
    public required string VersionText { get; init; }
    public required string CompactDisplayName { get; init; }
    public required string CompactEndpointText { get; init; }
    public required string EndpointText { get; init; }
    public required string Evidence { get; init; }
    public required string OpenLabel { get; init; }
    public required string VisibilityText { get; init; }
    public required string Url { get; init; }
    public required string StateLabel { get; init; }
    public required IBrush StateForeground { get; init; }
    public required IBrush StateBackground { get; init; }
    public required OpsSeverity Severity { get; init; }
    public bool IsVerified { get; init; }
    public bool IsVisible { get; init; }
    public bool IsActiveTarget { get; init; }
}

public sealed class LinuxMediaInstanceRow
{
    public required string SourceKey { get; init; }
    public required string DisplayName { get; init; }
    public required string MetaText { get; init; }
    public required string EndpointText { get; init; }
    public required string FullEndpointText { get; init; }
    public required string StateLabel { get; init; }
    public required IBrush StateForeground { get; init; }
    public required IBrush StateBackground { get; init; }
}

public sealed class LinuxMediaProductGroup
{
    public required string ProductName { get; init; }
    public required string OwnerTargetId { get; init; }
    public required string OwnerTargetName { get; init; }
    public required string Category { get; init; }
    public required string InstanceCountText { get; init; }
    public required string SummaryText { get; init; }
    public required string OpenLabel { get; init; }
    public required string PrimarySourceKey { get; init; }
    public required string StateLabel { get; init; }
    public required IBrush StateForeground { get; init; }
    public required IBrush StateBackground { get; init; }
    public required IReadOnlyList<LinuxMediaInstanceRow>
        Instances { get; init; }
}

public sealed class LinuxMediaCategoryGroup
{
    public required string Category { get; init; }
    public required string Summary { get; init; }
    public required string ProductCountText { get; init; }
    public required IReadOnlyList<LinuxMediaProductGroup>
        Products { get; init; }
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
            OwnedApplicationProjections()
                .Select(
                    BuildMediaApplicationRow)
                .OrderByDescending(item =>
                    item.IsActiveTarget)
                .ThenBy(item =>
                    item.Category)
                .ThenBy(item =>
                    item.OwnerTargetName)
                .ThenBy(item =>
                    item.DisplayName)
                .ThenBy(item =>
                    item.SourceKey)
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
                        item.OwnerTargetName,
                        item.Category,
                        item.RuntimeText,
                        item.EndpointText,
                        item.Evidence))
                .ToArray();

        var categoryGroups =
            BuildMediaCategoryGroups(
                visibleRows);

        var productGroups =
            categoryGroups
                .SelectMany(group =>
                    group.Products)
                .OrderBy(group =>
                    MediaCategoryRank(
                        group.Category))
                .ThenByDescending(group =>
                    group.OwnerTargetId.Equals(
                        _controlPlane.ActiveProfile.Id,
                        StringComparison.OrdinalIgnoreCase))
                .ThenBy(group =>
                    group.ProductName)
                .ThenBy(group =>
                    group.OwnerTargetName)
                .ToArray();

        Get<ItemsControl>(
                "MediaCategoryGroupsList")
            .ItemsSource =
            productGroups;

        var candidateCount =
            _mediaRows.Count(item =>
                !item.IsVerified);
        var targetCount =
            _mediaRows
                .Select(item =>
                    item.OwnerTargetId)
                .Distinct(
                    StringComparer.OrdinalIgnoreCase)
                .Count();

        Get<TextBlock>(
                "MediaFleetGroupingSummaryText")
            .Text =
            $"{productGroups.Length} " +
            $"application target group(s) · " +
            $"{visibleRows.Length} visible instance(s) · " +
            $"{targetCount} target(s) · " +
            $"{candidateCount} candidate(s)";

        var cards =
            Get<ListBox>("IntegrationsList");

        cards.ItemsSource =
            visibleRows;

        cards.SelectedItem =
            visibleRows.FirstOrDefault(item =>
                item.SourceKey.Equals(
                    selectedSource,
                    StringComparison.OrdinalIgnoreCase)) ??
            visibleRows.FirstOrDefault(item =>
                item.IsActiveTarget) ??
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
            $"{_identityResolution.Records.Count} active-target source(s) · " +
            $"{_integrations.Count(item => item.IsVerified && item.OwnsHealth)} " +
            $"verified health owner(s) · " +
            $"{_identityResolution.Records.Count(item => !item.IsVerified)} candidate(s)";

        var healthRows =
            _mediaRows
                .Where(item =>
                    item.Integration.OwnsHealth &&
                    item.IsVerified)
                .ToArray();

        var offline =
            healthRows.Count(item =>
                item.Severity >= OpsSeverity.Error ||
                item.Integration.State.Contains(
                    "offline",
                    StringComparison.OrdinalIgnoreCase) ||
                item.Integration.State.Contains(
                    "unavailable",
                    StringComparison.OrdinalIgnoreCase) ||
                item.Integration.State.Contains(
                    "not detected",
                    StringComparison.OrdinalIgnoreCase));

        var attention =
            healthRows.Count(item =>
                item.Severity ==
                    OpsSeverity.Warning &&
                !item.Integration.State.Contains(
                    "offline",
                    StringComparison.OrdinalIgnoreCase) &&
                !item.Integration.State.Contains(
                    "unavailable",
                    StringComparison.OrdinalIgnoreCase));

        var healthy =
            Math.Max(
                0,
                healthRows.Length -
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
            targetCount == 1
                ? _mediaRows.FirstOrDefault()?
                    .OwnerTargetName ??
                  _controlPlane.ActiveProfile.DisplayName
                : $"{targetCount} targets";

        Get<TextBlock>("MediaHubSummaryText")
            .Text =
            $"{visibleRows.Length} shown · " +
            $"{_applicationRegistry.Applications.Count} remembered application source(s)";

        var newestCapture =
            MostRecentApplicationInventoryCapture();

        Get<TextBlock>("MediaHubSampleAgeText")
            .Text =
            newestCapture is null
                ? "Waiting for capture"
                : $"Newest capture " +
                  $"{newestCapture.Value.ToLocalTime():g}";

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
            OwnedApplicationProjection owned)
    {
        var integration =
            owned.Integration;
        var identity =
            owned.Identity;
        var activeTarget =
            owned.Profile.Id.Equals(
                _controlPlane.ActiveProfile.Id,
                StringComparison.OrdinalIgnoreCase);

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

        if (activeTarget &&
            SupportsTargetCapability(
                GraveOps.Core.Targets.CapabilityIds.ApplicationApiTelemetry) &&
            integration.Name.Equals(
                "Plex",
                StringComparison.OrdinalIgnoreCase))
        {
            _plexCache.TryGetValue(
                owned.Profile.Id,
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
                ? integration.IsVerified
                    ? integration.Kind.Contains(
                        "systemd",
                        StringComparison.OrdinalIgnoreCase)
                        ? "System service"
                        : integration.Kind.Contains(
                            "docker",
                            StringComparison.OrdinalIgnoreCase) ||
                          integration.Evidence.Contains(
                              "compose",
                              StringComparison.OrdinalIgnoreCase)
                            ? "Docker managed"
                            : "Managed application"
                    : "Endpoint not confirmed"
                : integration.IsVerified
                    ? integration.Endpoint
                    : $"Suggested · {integration.Endpoint}");

        var evidence =
            $"Target · {owned.Profile.DisplayName}" +
            Environment.NewLine +
            (plexSnapshot is null
                ? $"{(integration.IsVerified ? "Verified" : "Candidate")} · " +
                  $"{integration.Evidence}"
                : $"Live Plex · " +
                  $"{plexSnapshot.ActiveSessions} sessions · " +
                  $"{plexSnapshot.TotalBandwidth} · " +
                  $"{plexSnapshot.LibraryCount} libraries");

        return new LinuxMediaApplicationRow
        {
            Integration =
                integration,
            IntegrationName =
                integration.Name,
            SourceKey =
                integration.InstanceKey,
            OwnerTargetId =
                owned.Profile.Id,
            OwnerTargetName =
                owned.Profile.DisplayName,
            DisplayName =
                displayName,
            Category =
                category,
            RuntimeText =
                runtimeText,
            RuntimeLabel =
                CompactRuntimeLabel(
                    integration),
            VersionText =
                string.IsNullOrWhiteSpace(
                    identity?.ApplicationVersion)
                    ? "--"
                    : identity.ApplicationVersion,
            CompactDisplayName =
                CompactInstanceName(
                    integration.Name,
                    displayName),
            CompactEndpointText =
                CompactEndpoint(
                    endpointText),
            EndpointText =
                endpointText,
            Evidence =
                evidence,
            OpenLabel =
                activeTarget
                    ? NavigationForIntegration(
                        integration.Name) is null
                        ? "Open interface"
                        : "Open in GraveOps"
                    : "Switch & open",
            VisibilityText =
                $"{(integration.IsVisible ? "Visible" : "Hidden")} · " +
                $"{owned.Profile.DisplayName}",
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
            Severity =
                liveSeverity,
            IsVerified =
                integration.IsVerified,
            IsVisible =
                integration.IsVisible,
            IsActiveTarget =
                activeTarget
        };
    }

    private IReadOnlyList<LinuxMediaCategoryGroup>
        BuildMediaCategoryGroups(
            IReadOnlyList<LinuxMediaApplicationRow> rows) =>
        rows
            .GroupBy(
                item => item.Category,
                StringComparer.OrdinalIgnoreCase)
            .OrderBy(group =>
                MediaCategoryRank(
                    group.Key))
            .ThenBy(group =>
                group.Key)
            .Select(group =>
            {
                var products =
                    group
                        .GroupBy(
                            item =>
                                $"{item.IntegrationName}\u001f{item.OwnerTargetId}",
                            StringComparer.OrdinalIgnoreCase)
                        .Select(product =>
                        {
                            var first =
                                product.First();

                            return BuildMediaProductGroup(
                                first.IntegrationName,
                                group.Key,
                                product.ToArray());
                        })
                        .OrderBy(product =>
                            product.ProductName)
                        .ThenBy(product =>
                            product.OwnerTargetName)
                        .ToArray();

                return new LinuxMediaCategoryGroup
                {
                    Category =
                        group.Key,
                    Summary =
                        MediaCategorySummary(
                            group.Key),
                    ProductCountText =
                        $"{products.Length} " +
                        $"{(products.Length == 1 ? "application target" : "application targets")}",
                    Products =
                        products
                };
            })
            .ToArray();

    private LinuxMediaProductGroup
        BuildMediaProductGroup(
            string productName,
            string category,
            IReadOnlyList<LinuxMediaApplicationRow> rows)
    {
        var ordered =
            rows
                .OrderBy(item =>
                    item.CompactDisplayName)
                .ThenBy(item =>
                    item.SourceKey)
                .ToArray();

        var verified =
            ordered.Count(item =>
                item.IsVerified);
        var healthy =
            ordered.Count(item =>
                item.IsVerified &&
                item.Severity < OpsSeverity.Warning);
        var attention =
            ordered.Count(item =>
                item.IsVerified &&
                item.Severity >= OpsSeverity.Warning);

        var groupSeverity =
            ordered
                .Where(item =>
                    item.IsVerified)
                .Select(item =>
                    item.Severity)
                .DefaultIfEmpty(
                    OpsSeverity.Info)
                .Max();

        var stateLabel =
            verified == 0
                ? "UNVERIFIED"
                : verified != ordered.Length
                    ? "MIXED"
                    : LinuxOpsAnalyzer.SeverityLabel(
                        groupSeverity);

        var healthSummary =
            verified == 0
                ? $"{ordered.Length} candidate " +
                  $"{(ordered.Length == 1 ? "instance" : "instances")}"
                : attention == 0
                    ? "Healthy"
                    : $"{healthy} healthy · {attention} attention";

        var instances =
            ordered
                .Select(item =>
                    new LinuxMediaInstanceRow
                    {
                        SourceKey =
                            item.SourceKey,
                        DisplayName =
                            item.CompactDisplayName,
                        MetaText =
                            item.VersionText == "--"
                                ? item.RuntimeLabel
                                : $"v{item.VersionText} · " +
                                  item.RuntimeLabel,
                        EndpointText =
                            item.CompactEndpointText,
                        FullEndpointText =
                            item.EndpointText,
                        StateLabel =
                            item.StateLabel,
                        StateForeground =
                            item.StateForeground,
                        StateBackground =
                            item.StateBackground
                    })
                .ToArray();

        var primary =
            ordered
                .OrderByDescending(item =>
                    item.IsVerified)
                .ThenByDescending(item =>
                    item.Severity <
                    OpsSeverity.Warning)
                .First();

        return new LinuxMediaProductGroup
        {
            ProductName =
                productName,
            OwnerTargetId =
                primary.OwnerTargetId,
            OwnerTargetName =
                primary.OwnerTargetName,
            Category =
                category,
            InstanceCountText =
                $"{ordered.Length} " +
                $"{(ordered.Length == 1 ? "instance" : "instances")}",
            SummaryText =
                $"{primary.OwnerTargetName} · {healthSummary}",
            OpenLabel =
                primary.IsActiveTarget
                    ? NavigationForIntegration(
                        productName) is null
                        ? "Open interface"
                        : "Open workspace"
                    : "Switch & open",
            PrimarySourceKey =
                primary.SourceKey,
            StateLabel =
                stateLabel,
            StateForeground =
                OpsPalette.Foreground(
                    groupSeverity),
            StateBackground =
                OpsPalette.Background(
                    groupSeverity),
            Instances =
                instances
        };
    }

    private static int MediaCategoryRank(
        string category) =>
        category.ToLowerInvariant() switch
        {
            "library" => 0,
            "acquisition" => 1,
            "processing" => 2,
            "orchestration" => 3,
            "requests" => 4,
            "network" => 5,
            "supporting service" => 6,
            _ => 7
        };

    private static string MediaCategorySummary(
        string category) =>
        category.ToLowerInvariant() switch
        {
            "library" =>
                "Playback, libraries and metadata ownership",
            "acquisition" =>
                "Automation, indexers and download services",
            "processing" =>
                "Import, post-processing and maintenance",
            "orchestration" =>
                "Stack ownership and control-plane services",
            "requests" =>
                "User-facing request and discovery services",
            "network" =>
                "DNS, access and network dependencies",
            "supporting service" =>
                "Supporting runtime dependencies",
            _ =>
                "Verified application instances"
        };

    private static string CompactInstanceName(
        string product,
        string displayName)
    {
        var prefix =
            product + " — ";

        if (displayName.StartsWith(
                prefix,
                StringComparison.OrdinalIgnoreCase))
        {
            return displayName[prefix.Length..];
        }

        return displayName.Equals(
                product,
                StringComparison.OrdinalIgnoreCase)
            ? "Local instance"
            : displayName;
    }

    private static string CompactRuntimeLabel(
        OpsIntegration integration)
    {
        var role =
            integration.Role
                ?.Replace(
                    " application",
                    string.Empty,
                    StringComparison.OrdinalIgnoreCase)
                .Trim();

        if (!string.IsNullOrWhiteSpace(role))
            return role;

        return string.IsNullOrWhiteSpace(
            integration.Kind)
            ? "Runtime"
            : integration.Kind;
    }

    private static string CompactEndpoint(
        string endpoint)
    {
        if (!Uri.TryCreate(
                endpoint,
                UriKind.Absolute,
                out var uri))
        {
            return endpoint;
        }

        var path =
            uri.AbsolutePath.TrimEnd('/');

        return
            $"{uri.DnsSafeHost}:{uri.Port}" +
            (string.IsNullOrWhiteSpace(path) ||
             path == "/"
                ? string.Empty
                : path);
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
            "pi-hole" or
            "pihole" =>
                "Network",
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
        var activeTargetId =
            _controlPlane.ActiveProfile.Id;

        var row =
            _mediaRows
                .Where(item =>
                    item.IntegrationName.Equals(
                        integrationName,
                        StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(item =>
                    item.OwnerTargetId.Equals(
                        activeTargetId,
                        StringComparison.OrdinalIgnoreCase))
                .ThenByDescending(item =>
                    item.IsVerified)
                .FirstOrDefault();

        if (row is null)
        {
            PopulateMediaHub();

            row =
                _mediaRows
                    .Where(item =>
                        item.IntegrationName.Equals(
                            integrationName,
                            StringComparison.OrdinalIgnoreCase))
                    .OrderByDescending(item =>
                        item.OwnerTargetId.Equals(
                            activeTargetId,
                            StringComparison.OrdinalIgnoreCase))
                    .ThenByDescending(item =>
                        item.IsVerified)
                    .FirstOrDefault();
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

        if (row.OwnerTargetId.Equals(
                activeTargetId,
                StringComparison.OrdinalIgnoreCase))
        {
            Get<ListBox>(
                    "MediaLauncherSettingsList")
                .SelectedItem =
                _identityResolution.Records
                    .FirstOrDefault(item =>
                        item.SourceKey.Equals(
                            row.SourceKey,
                            StringComparison.OrdinalIgnoreCase));
        }

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

        try
        {
            await RefreshAsync();
        }
        finally
        {
            button.IsEnabled =
                true;
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

    private async void MediaCardOpenButton_OnClick(
        object? sender,
        RoutedEventArgs e)
    {
        if (sender is not Button button ||
            button.Tag is not string sourceKey)
        {
            return;
        }

        var row =
            FindOwnedMediaRow(
                sourceKey);

        if (row is null)
            return;

        await ActivateOwnedApplicationAsync(
            row,
            openIdentityEditor: false);
    }

    private async void MediaGroupIdentityButton_OnClick(
        object? sender,
        RoutedEventArgs e)
    {
        if (sender is not Button button ||
            button.Tag is not string sourceKey ||
            string.IsNullOrWhiteSpace(
                sourceKey))
        {
            return;
        }

        var row =
            FindOwnedMediaRow(
                sourceKey);

        if (row is null)
            return;

        await ActivateOwnedApplicationAsync(
            row,
            openIdentityEditor: true);
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
            $"{selected.VerificationState} · " +
            $"{selected.Role} · confidence {selected.Confidence}";
        Get<TextBlock>("MediaLauncherDetectedText")
            .Text =
            $"{selected.Kind} · {selected.State}" +
            Environment.NewLine +
            selected.Evidence +
            (string.IsNullOrWhiteSpace(
                selected.VerificationDetail)
                ? string.Empty
                : Environment.NewLine +
                  selected.VerificationDetail) +
            (string.IsNullOrWhiteSpace(
                selected.InstanceName)
                ? string.Empty
                : Environment.NewLine +
                  $"Instance · {selected.InstanceName}") +
            (string.IsNullOrWhiteSpace(
                selected.ApplicationVersion)
                ? string.Empty
                : Environment.NewLine +
                  $"Version · {selected.ApplicationVersion}") +
            (string.IsNullOrWhiteSpace(
                selected.ApiVersion)
                ? string.Empty
                : $" · API {selected.ApiVersion}") +
            (string.IsNullOrWhiteSpace(
                selected.ProbeUrl)
                ? string.Empty
                : Environment.NewLine +
                  $"Probe · {selected.ProbeUrl}") +
            (selected.LastVerificationAt is null
                ? string.Empty
                : Environment.NewLine +
                  $"Verified at · " +
                  $"{selected.LastVerificationAt.Value.ToLocalTime():g}");

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
