using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using GraveOps.Core.Targets;

namespace GraveOps.Desktop.Linux;

public partial class MainWindow
{
    private UnifiedInterfacePreferencesStore?
        _unifiedInterfaceStore;
    private UnifiedInterfacePreferences
        _unifiedInterface =
            UnifiedInterfacePreferences.Default;
    private readonly LinuxOperatorScriptStore
        _operatorScriptStore =
            new();
    private IReadOnlyList<UnifiedDashboardCard>
        _unifiedDashboardCards =
            Array.Empty<UnifiedDashboardCard>();
    private readonly Dictionary<string, CheckBox>
        _dashboardPickerChecks =
            new(StringComparer.OrdinalIgnoreCase);
    private int _expressSetupStep;
    private bool _unifiedInterfaceInitialized;
    private string _unifiedCurrentNavigation =
        "DashboardNav";
    private bool _dashboardLogContextApplying;
    private bool _dashboardLogContextActive;
    private string _dashboardLogContextLabel =
        string.Empty;
    private string[] _dashboardLogAliases =
        Array.Empty<string>();

    private void InitializeUnifiedInterface()
    {
        _unifiedInterfaceStore =
            new UnifiedInterfacePreferencesStore(
                _operatorSettingsStore.ConfigDirectory);
        _unifiedInterface =
            _unifiedInterfaceStore.Load();

        Get<ComboBox>("InterfaceThemeComboBox")
            .ItemsSource =
            LinuxThemeCatalog.Names;
        Get<ComboBox>("InterfaceThemeComboBox")
            .SelectedItem =
            LinuxThemeCatalog.Find(
                _unifiedInterface.ThemeName).Name;

        Get<ComboBox>("InterfaceDensityComboBox")
            .ItemsSource =
            new[]
            {
                "Compact",
                "Comfortable"
            };
        Get<ComboBox>("InterfaceDensityComboBox")
            .SelectedItem =
            NormalizeDensity(
                _unifiedInterface.Density);

        Get<CheckBox>(
                "InterfaceRestoreSessionCheckBox")
            .IsChecked =
            _unifiedInterface.RestoreLastPage;
        Get<CheckBox>(
                "InterfaceSilentRefreshCheckBox")
            .IsChecked =
            _unifiedInterface.SilentRefresh;
        Get<CheckBox>(
                "InterfaceFreshnessCheckBox")
            .IsChecked =
            _unifiedInterface.ShowFreshness;

        Get<ComboBox>("SetupModeComboBox")
            .ItemsSource =
            SetupModes;
        Get<ComboBox>("SetupModeComboBox")
            .SelectedItem =
            SetupModes.Contains(
                _unifiedInterface.SetupMode,
                StringComparer.OrdinalIgnoreCase)
                ? _unifiedInterface.SetupMode
                : SetupModes[0];

        Get<ComboBox>("SetupThemeComboBox")
            .ItemsSource =
            LinuxThemeCatalog.Names;
        Get<ComboBox>("SetupThemeComboBox")
            .SelectedItem =
            LinuxThemeCatalog.Find(
                _unifiedInterface.ThemeName).Name;

        Get<ComboBox>("SetupDensityComboBox")
            .ItemsSource =
            new[]
            {
                "Compact",
                "Comfortable"
            };
        Get<ComboBox>("SetupDensityComboBox")
            .SelectedItem =
            NormalizeDensity(
                _unifiedInterface.Density);

        Get<CheckBox>("SetupSafeModeCheckBox")
            .IsChecked =
            Get<CheckBox>(
                    "SettingsSafeModeCheckBox")
                .IsChecked;
        Get<CheckBox>(
                "SetupNotificationsCheckBox")
            .IsChecked =
            Get<CheckBox>(
                    "SettingsDesktopNotificationsCheckBox")
                .IsChecked;

        ApplyUnifiedTheme(
            _unifiedInterface.ThemeName);
        ApplyUnifiedDensity(
            _unifiedInterface.Density);
        PopulateParityWorkspace();
        PopulateOperatorScripts();
        InitializeUnifiedFiles();
        RenderExpressSetupStep();

        _unifiedInterfaceInitialized = true;
    }

    private void DisposeUnifiedInterface()
    {
        if (!_unifiedInterfaceInitialized ||
            _unifiedInterfaceStore is null)
        {
            return;
        }

        _unifiedInterface.LastNavigation =
            _unifiedCurrentNavigation;
        _unifiedInterfaceStore.Save(
            _unifiedInterface);
    }

    private static IReadOnlyList<string>
        SetupModes { get; } =
        new[]
        {
            "Local Linux — automatic discovery",
            "Remote Linux — SSH",
            "Docker or Compose host",
            "Guided media stack",
            "Monitoring only",
            "Advanced / manual"
        };

    private string UnifiedInitialNavigation()
    {
        if (!_unifiedInterface.RestoreLastPage ||
            string.IsNullOrWhiteSpace(
                _unifiedInterface.LastNavigation) ||
            !_navigation.ContainsKey(
                _unifiedInterface.LastNavigation))
        {
            return "DashboardNav";
        }

        return _unifiedInterface.LastNavigation;
    }

    private void RecordUnifiedNavigation(
        string navigationName)
    {
        _unifiedCurrentNavigation =
            navigationName;

        if (!_unifiedInterfaceInitialized ||
            _unifiedInterfaceStore is null)
        {
            return;
        }

        _unifiedInterface.LastNavigation =
            navigationName;
        _unifiedInterfaceStore.Save(
            _unifiedInterface);
    }

    private void PopulateUnifiedInterface()
    {
        if (!_unifiedInterfaceInitialized)
            return;

        PopulateUnifiedDashboard();
        PopulateUnifiedInterfaceSettings();
        ApplyActionAvailabilityReasons();
        PopulateSetupDiscoveryPreview();
    }

    private void PopulateUnifiedInterfaceSettings()
    {
        Get<ComboBox>("InterfaceThemeComboBox")
            .SelectedItem =
            LinuxThemeCatalog.Find(
                _unifiedInterface.ThemeName).Name;
        Get<ComboBox>("InterfaceDensityComboBox")
            .SelectedItem =
            NormalizeDensity(
                _unifiedInterface.Density);
        Get<CheckBox>(
                "InterfaceRestoreSessionCheckBox")
            .IsChecked =
            _unifiedInterface.RestoreLastPage;
        Get<CheckBox>(
                "InterfaceSilentRefreshCheckBox")
            .IsChecked =
            _unifiedInterface.SilentRefresh;
        Get<CheckBox>(
                "InterfaceFreshnessCheckBox")
            .IsChecked =
            _unifiedInterface.ShowFreshness;

        Get<TextBlock>(
                "InterfaceSettingsStatusText")
            .Text =
            $"Theme · {_unifiedInterface.ThemeName}   " +
            $"Density · {NormalizeDensity(_unifiedInterface.Density)}   " +
            $"Setup · {(_unifiedInterface.SetupCompleted ? "complete" : "not complete")}";
    }

    private void ApplyUnifiedTheme(
        string? themeName)
    {
        var theme =
            LinuxThemeCatalog.Find(
                themeName);

        if (Application.Current is not
            { } application)
        {
            return;
        }

        foreach (var resource in
                 theme.ResourceColors)
        {
            application.Resources[
                    resource.Key] =
                new SolidColorBrush(
                    Color.Parse(
                        resource.Value));
        }

        application.RequestedThemeVariant =
            theme.IsDark
                ? Avalonia.Styling.ThemeVariant.Dark
                : Avalonia.Styling.ThemeVariant.Light;

        _unifiedInterface.ThemeName =
            theme.Name;
    }

    private void ApplyUnifiedDensity(
        string? density)
    {
        var normalized =
            NormalizeDensity(
                density);

        Classes.Set(
            "compactDensity",
            normalized.Equals(
                "Compact",
                StringComparison.OrdinalIgnoreCase));

        Classes.Set(
            "comfortableDensity",
            normalized.Equals(
                "Comfortable",
                StringComparison.OrdinalIgnoreCase));

        _unifiedInterface.Density =
            normalized;
    }

    private static string NormalizeDensity(
        string? value) =>
        string.Equals(
            value,
            "Comfortable",
            StringComparison.OrdinalIgnoreCase)
            ? "Comfortable"
            : "Compact";

    private void InterfaceThemeComboBox_OnSelectionChanged(
        object? sender,
        SelectionChangedEventArgs e)
    {
        if (!_unifiedInterfaceInitialized)
            return;

        ApplyUnifiedTheme(
            Get<ComboBox>("InterfaceThemeComboBox")
                .SelectedItem as string);
        PopulateUnifiedDashboard();
    }

    private void InterfaceDensityComboBox_OnSelectionChanged(
        object? sender,
        SelectionChangedEventArgs e)
    {
        if (!_unifiedInterfaceInitialized)
            return;

        ApplyUnifiedDensity(
            Get<ComboBox>("InterfaceDensityComboBox")
                .SelectedItem as string);
        PopulateUnifiedDashboard();
    }

    private void SaveInterfaceSettingsButton_OnClick(
        object? sender,
        RoutedEventArgs e)
    {
        if (_unifiedInterfaceStore is null)
            return;

        ApplyUnifiedTheme(
            Get<ComboBox>("InterfaceThemeComboBox")
                .SelectedItem as string);
        ApplyUnifiedDensity(
            Get<ComboBox>("InterfaceDensityComboBox")
                .SelectedItem as string);

        _unifiedInterface.RestoreLastPage =
            Get<CheckBox>(
                    "InterfaceRestoreSessionCheckBox")
                .IsChecked == true;
        _unifiedInterface.SilentRefresh =
            Get<CheckBox>(
                    "InterfaceSilentRefreshCheckBox")
                .IsChecked != false;
        _unifiedInterface.ShowFreshness =
            Get<CheckBox>(
                    "InterfaceFreshnessCheckBox")
                .IsChecked != false;

        _unifiedInterfaceStore.Save(
            _unifiedInterface);

        Get<TextBlock>(
                "InterfaceSettingsStatusText")
            .Text =
            "Interface settings saved.";
    }

    private void ResetDashboardLayoutButton_OnClick(
        object? sender,
        RoutedEventArgs e)
    {
        var hostId =
            _controlPlane.ActiveProfile.Id;

        _unifiedInterface.DashboardLayouts
            .Remove(hostId);

        _unifiedInterfaceStore?.Save(
            _unifiedInterface);

        PopulateUnifiedDashboard();
        CloseDashboardCustomizer();

        Get<TextBlock>(
                "InterfaceSettingsStatusText")
            .Text =
            "Dashboard layout restored to the recommended provider-neutral default.";
    }

    private void PopulateUnifiedDashboard()
    {
        if (_snapshot is null ||
            _analysis is null ||
            _backup is null)
        {
            Get<TextBlock>(
                    "UnifiedDashboardStatusText")
                .Text =
                "Waiting";
            return;
        }

        var effectiveAnalysis =
            _policyEvaluation?.Analysis ??
            _analysis;
        var actionable =
            EffectiveDashboardFindings(
                    effectiveAnalysis)
                .Where(item =>
                    item.Severity >=
                    OpsSeverity.Warning)
                .OrderByDescending(item =>
                    item.Severity)
                .ThenBy(item =>
                    item.Rank)
                .ToArray();

        var builtCards =
            BuildUnifiedDashboardCards()
                .ToList();
        builtCards.Add(
            BuildDashboardAcquisitionCard());

        if (actionable.Length == 0)
        {
            builtCards.RemoveAll(card =>
                card.Key.Equals(
                    "core:health",
                    StringComparison.OrdinalIgnoreCase));
        }

        _unifiedDashboardCards =
            builtCards
                .GroupBy(
                    card => card.Key,
                    StringComparer.OrdinalIgnoreCase)
                .Select(group =>
                    group.First())
                .ToArray();

        var layout =
            ResolveDashboardLayout(
                _unifiedDashboardCards);
        var visible =
            layout
                .Where(item =>
                    item.IsVisible)
                .OrderBy(item =>
                    item.Order)
                .Select(preference =>
                    _unifiedDashboardCards
                        .FirstOrDefault(card =>
                            card.Key.Equals(
                                preference.Key,
                                StringComparison.OrdinalIgnoreCase)))
                .Where(card =>
                    card is not null)
                .Cast<UnifiedDashboardCard>()
                .ToArray();

        var panel =
            Get<StackPanel>(
                "UnifiedDashboardCardsPanel");
        var available =
            ResolveDashboardAvailableWidth(
                panel);
        var sections =
            new[]
            {
                new DashboardSectionDefinition(
                    "Infrastructure",
                    string.Empty,
                    "infrastructure",
                    false),
                new DashboardSectionDefinition(
                    "Operations",
                    string.Empty,
                    "operations",
                    false),
                new DashboardSectionDefinition(
                    "Media",
                    string.Empty,
                    "media",
                    false),
                new DashboardSectionDefinition(
                    "Applications",
                    string.Empty,
                    "applications",
                    true)
            };

        var desired =
            new List<Control>();

        foreach (var section in sections)
        {
            var sectionCards =
                visible
                    .Where(card =>
                        DashboardSectionKey(
                            card)
                            .Equals(
                                section.Key,
                                StringComparison.Ordinal))
                    .ToArray();

            if (sectionCards.Length == 0)
                continue;

            var signature =
                DashboardSectionSignature(
                    section,
                    sectionCards,
                    available);
            var existing =
                panel.Children
                    .OfType<StackPanel>()
                    .FirstOrDefault(control =>
                        control.Tag is
                            DashboardSectionRenderState state &&
                        state.Key.Equals(
                            section.Key,
                            StringComparison.Ordinal) &&
                        state.Signature.Equals(
                            signature,
                            StringComparison.Ordinal));

            desired.Add(
                existing ??
                BuildDashboardSectionControl(
                    section,
                    sectionCards,
                    available,
                    signature));
        }

        if (desired.Count == 0)
        {
            var empty =
                panel.Children
                    .OfType<Border>()
                    .FirstOrDefault(control =>
                        Equals(
                            control.Tag,
                            "dashboard:empty"));

            desired.Add(
                empty ??
                new Border
                {
                    Tag = "dashboard:empty",
                    Classes =
                    {
                        "emptyState"
                    },
                    Child =
                        new TextBlock
                        {
                            Text =
                                "No Dashboard cards are visible. Open Customize cards to restore modules.",
                            TextWrapping =
                                TextWrapping.Wrap
                        }
                });
        }

        ReconcileDashboardChildren(
            panel,
            desired);

        var attentionStrip =
            Get<Border>(
                "UnifiedDashboardAttentionStrip");
        var attentionTitle =
            Get<TextBlock>(
                "UnifiedDashboardAttentionTitleText");
        var attentionDetail =
            Get<TextBlock>(
                "UnifiedDashboardAttentionDetailText");

        if (actionable.Length == 0)
        {
            attentionStrip.Classes.Set(
                "attention",
                false);
            attentionStrip.Classes.Set(
                "healthy",
                true);
            attentionTitle.Text =
                "Healthy";
            attentionDetail.Text =
                _policyEvaluation?.Muted.Count > 0
                    ? $"0 active findings · {_policyEvaluation.Muted.Count} muted by policy"
                    : "0 active findings";
        }
        else
        {
            var top = actionable[0];
            attentionStrip.Classes.Set(
                "healthy",
                false);
            attentionStrip.Classes.Set(
                "attention",
                true);
            attentionTitle.Text =
                $"{actionable.Length} active finding{(actionable.Length == 1 ? string.Empty : "s")}";
            attentionDetail.Text =
                $"{LinuxOpsAnalyzer.SeverityLabel(top.Severity)} · {top.Component} · {top.Problem}";
        }

        if (Get<Border>(
                "DashboardCustomizerPanel")
            .IsVisible)
        {
            PopulateDashboardCardPicker();
        }

        UpdateSharedUnifiedDashboard(
            actionable,
            layout);
    }

    private IReadOnlyList<OpsFinding>
        EffectiveDashboardFindings(
            OpsAnalysis effectiveAnalysis)
    {
        if (_policyEvaluation is null)
            return effectiveAnalysis.Findings;

        return _policyEvaluation.Active
            .Select(item => item.Finding)
            .ToArray();
    }

    private sealed record DashboardSectionDefinition(
        string Title,
        string Subtitle,
        string Key,
        bool ApplicationTiles);

    private static readonly string[]
        DashboardApplicationProducts =
        {
            "Sonarr",
            "Radarr",
            "Lidarr",
            "Prowlarr",
            "Readarr",
            "Whisparr",
            "Bazarr",
            "Mylar3",
            "Recyclarr",
            "Configarr",
            "Profilarr",
            "Cleanuparr",
            "Maintainerr",
            "Unpackerr",
            "autobrr"
        };

    private static bool DashboardIsApprovedApplication(
        string product) =>
        DashboardApplicationProducts.Contains(
            product,
            StringComparer.OrdinalIgnoreCase);

    private static bool DashboardIsServarrLikeProduct(
        string product) =>
        ArrApiCatalog.IsSupportedProduct(
            product) ||
        product.EndsWith(
            "arr",
            StringComparison.OrdinalIgnoreCase);

    private static bool DashboardIsApprovedApplicationKey(
        string key)
    {
        if (!key.StartsWith(
                "app:",
                StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var normalized =
            key["app:".Length..];
        return DashboardApplicationProducts.Any(product =>
                   NormalizeCardKey(product).Equals(
                       normalized,
                       StringComparison.OrdinalIgnoreCase)) ||
               normalized.EndsWith(
                   "arr",
                   StringComparison.OrdinalIgnoreCase);
    }

    private static bool DashboardCardEligibleIntegration(
        OpsIntegration item) =>
        item.IsVisible &&
        (ApplicationIdentityRoles.IsTopLevel(
             item.Role) ||
         DashboardIsApprovedApplication(
             item.Name) ||
         DashboardIsServarrLikeProduct(
             item.Name));

    private double ResolveDashboardAvailableWidth(
        Control panel)
    {
        if (panel.Bounds.Width > 720)
            return panel.Bounds.Width;

        return Math.Max(
            620,
            Bounds.Width - 310);
    }

    private sealed record DashboardSectionRenderState(
        string Key,
        string Signature);

    private StackPanel BuildDashboardSectionControl(
        DashboardSectionDefinition section,
        IReadOnlyList<UnifiedDashboardCard> cards,
        double available,
        string signature)
    {
        var container =
            new StackPanel
            {
                Tag =
                    new DashboardSectionRenderState(
                        section.Key,
                        signature),
                Spacing = 8
            };

        container.Children.Add(
            new TextBlock
            {
                Text = section.Title,
                FontSize = 13,
                FontWeight =
                    FontWeight.SemiBold,
                Margin =
                    new Thickness(
                        0,
                        section.ApplicationTiles
                            ? 3
                            : 0,
                        0,
                        0)
            });

        var columns =
            ResolveDashboardSectionColumns(
                section.ApplicationTiles,
                cards.Count,
                available);
        var cardWidth =
            ResolveDashboardSectionCardWidth(
                columns,
                available);
        var cardRows =
            new StackPanel
            {
                Spacing = 9,
                HorizontalAlignment =
                    HorizontalAlignment.Stretch
            };

        var pending =
            new List<UnifiedDashboardCard>();

        void FlushPendingRow()
        {
            if (pending.Count == 0)
                return;

            var row =
                new Grid
                {
                    ColumnDefinitions =
                        new ColumnDefinitions(
                            string.Join(
                                ",",
                                Enumerable.Repeat(
                                    "*",
                                    columns))),
                    ColumnSpacing = 9,
                    HorizontalAlignment =
                        HorizontalAlignment.Stretch
                };

            for (var index = 0;
                 index < pending.Count;
                 index++)
            {
                var control =
                    BuildDashboardCardControl(
                        pending[index],
                        cardWidth,
                        section.ApplicationTiles);
                Grid.SetColumn(
                    control,
                    index);
                row.Children.Add(
                    control);
            }

            cardRows.Children.Add(
                row);
            pending.Clear();
        }

        foreach (var card in cards)
        {
            var fullWidth =
                card.Key.Equals(
                    "core:health",
                    StringComparison.OrdinalIgnoreCase);

            if (fullWidth)
            {
                FlushPendingRow();

                var fullRow =
                    new Grid
                    {
                        ColumnDefinitions =
                            new ColumnDefinitions("*"),
                        HorizontalAlignment =
                            HorizontalAlignment.Stretch
                    };
                fullRow.Children.Add(
                    BuildDashboardCardControl(
                        card,
                        Math.Max(
                            292,
                            available),
                        section.ApplicationTiles));
                cardRows.Children.Add(
                    fullRow);
                continue;
            }

            pending.Add(
                card);
            if (pending.Count == columns)
            {
                FlushPendingRow();
            }
        }

        FlushPendingRow();
        container.Children.Add(
            cardRows);
        return container;
    }

    private string DashboardSectionSignature(
        DashboardSectionDefinition section,
        IReadOnlyList<UnifiedDashboardCard> cards,
        double available)
    {
        var widthBucket =
            Math.Round(
                available / 8.0) *
            8.0;
        var cardSignatures =
            cards.Select(card =>
                string.Join(
                    '\u001f',
                    new[]
                    {
                        card.Key,
                        card.Title,
                        card.Category,
                        card.Status,
                        ((int)card.Severity).ToString(
                            CultureInfo.InvariantCulture),
                        card.PrimaryValue,
                        card.Summary,
                        card.Detail,
                        card.ActionLabel,
                        card.NavigationName,
                        card.Endpoint,
                        card.SourceKey,
                        card.DefaultVisible.ToString(),
                        string.Join(
                            '\u001e',
                            card.Facts),
                        string.Join(
                            '\u001e',
                            card.Rows.Select(row =>
                                $"{row.Label}\u001d{row.Value}\u001d{row.SecondaryValue}\u001d{row.Detail}\u001d{(int)row.Severity}")),
                        string.Join(
                            '\u001e',
                            card.Actions.Select(action =>
                                $"{action.Label}\u001d{action.NavigationName}\u001d{action.Endpoint}\u001d{action.IsPrimary}\u001d{action.LogSource}\u001d{action.LogText}\u001d{action.IncludeInformationalLogs}"))
                    }));

        return string.Join(
            '\u001c',
            new[]
            {
                section.Key,
                section.ApplicationTiles.ToString(),
                _unifiedInterface.Density,
                widthBucket.ToString(
                    CultureInfo.InvariantCulture),
                string.Join(
                    '\u001b',
                    cardSignatures)
            });
    }

    private static void ReconcileDashboardChildren(
        StackPanel panel,
        IReadOnlyList<Control> desired)
    {
        for (var index = 0;
             index < desired.Count;
             index++)
        {
            var control =
                desired[index];

            if (index < panel.Children.Count &&
                ReferenceEquals(
                    panel.Children[index],
                    control))
            {
                continue;
            }

            if (panel.Children.Contains(
                    control))
            {
                panel.Children.Remove(
                    control);
            }

            panel.Children.Insert(
                index,
                control);
        }

        while (panel.Children.Count >
               desired.Count)
        {
            panel.Children.RemoveAt(
                panel.Children.Count - 1);
        }
    }

    private static int ResolveDashboardSectionColumns(
        bool applicationTiles,
        int count,
        double available)
    {
        if (count <= 1)
            return 1;

        // Every Dashboard section now follows the same responsive grid.
        // Applications deliberately top out at three equal columns so the
        // actual panel width, rather than a fixed tile width, owns geometry.
        if (available >= 900)
            return Math.Min(3, count);
        if (available >= 610)
            return Math.Min(2, count);
        return 1;
    }

    private static double ResolveDashboardSectionCardWidth(
        int columns,
        double available)
    {
        var gaps =
            Math.Max(
                0,
                columns - 1) *
            9.0;
        return Math.Max(
            250,
            (available - gaps) /
            Math.Max(
                1,
                columns));
    }

    private static string DashboardSectionKey(
        UnifiedDashboardCard card)
    {
        if (card.Key.Equals(
                "core:health",
                StringComparison.OrdinalIgnoreCase) ||
            card.Key.Equals(
                "core:host",
                StringComparison.OrdinalIgnoreCase) ||
            card.Key.Equals(
                "core:storage",
                StringComparison.OrdinalIgnoreCase) ||
            card.Key.Equals(
                "core:docker",
                StringComparison.OrdinalIgnoreCase))
        {
            return "infrastructure";
        }

        if (card.Key.Equals(
                "core:acquisition",
                StringComparison.OrdinalIgnoreCase) ||
            card.Key.Equals(
                "core:downloads",
                StringComparison.OrdinalIgnoreCase) ||
            card.Key.Equals(
                "core:backups",
                StringComparison.OrdinalIgnoreCase))
        {
            return "operations";
        }

        if (card.Key.Equals(
                "app:plex",
                StringComparison.OrdinalIgnoreCase) ||
            card.Key.Equals(
                "core:media",
                StringComparison.OrdinalIgnoreCase) ||
            card.Key.Equals(
                "core:activity",
                StringComparison.OrdinalIgnoreCase))
        {
            return "media";
        }

        return "applications";
    }

    private static IReadOnlyList<UnifiedDashboardCard>
        OrderDashboardCardsForDisplay(
            IReadOnlyList<UnifiedDashboardCard> cards) =>
        cards
            .OrderBy(card =>
                DashboardDisplayPriority(
                    card.Key))
            .ThenBy(card =>
                card.Title,
                StringComparer.OrdinalIgnoreCase)
            .ToArray();

    private static int DashboardDisplayPriority(
        string key) =>
        key.ToLowerInvariant() switch
        {
            "core:health" => 0,
            "core:host" => 1,
            "core:storage" => 2,
            "core:docker" => 3,
            "core:acquisition" => 4,
            "core:downloads" => 5,
            "core:backups" => 6,
            "app:plex" => 7,
            "core:media" => 8,
            "core:activity" => 9,
            "app:sonarr" => 20,
            "app:radarr" => 21,
            "app:lidarr" => 22,
            "app:prowlarr" => 23,
            "app:readarr" => 24,
            "app:whisparr" => 25,
            "app:bazarr" => 26,
            "app:mylar3" => 27,
            "app:recyclarr" => 30,
            "app:configarr" => 31,
            "app:profilarr" => 32,
            "app:cleanuparr" => 33,
            "app:maintainerr" => 34,
            "app:unpackerr" => 35,
            "app:autobrr" => 36,
            "app:dumb" => 40,
            _ when key.StartsWith(
                "app:",
                StringComparison.OrdinalIgnoreCase) =>
                50,
            _ => 60
        };

    private UnifiedDashboardCard
        BuildDashboardAcquisitionCard()
    {
        var products =
            new[]
            {
                "Sonarr",
                "Radarr",
                "Lidarr",
                "Prowlarr",
                "Readarr",
                "Whisparr",
                "Bazarr",
                "Mylar3"
            };
        var instances =
            _integrations
                .Where(item =>
                    item.IsVisible &&
                    item.IsVerified &&
                    item.OwnsHealth &&
                    products.Contains(
                        item.Name,
                        StringComparer.OrdinalIgnoreCase))
                .ToArray();
        var groups =
            instances
                .GroupBy(
                    item => item.Name,
                    StringComparer.OrdinalIgnoreCase)
                .OrderByDescending(group =>
                    group.Max(item =>
                        item.Severity))
                .ThenBy(group =>
                    group.Key)
                .ToArray();
        var severity =
            instances
                .Select(item =>
                    item.Severity)
                .DefaultIfEmpty(
                    OpsSeverity.Info)
                .Max();
        var attention =
            instances.Count(item =>
                item.Severity >=
                OpsSeverity.Warning);
        var aliases =
            string.Join(
                "|",
                products);

        return new UnifiedDashboardCard(
            "core:acquisition",
            "Acquisition",
            "Operations",
            instances.Length == 0
                ? "NOT CONFIGURED"
                : attention == 0
                    ? "READY"
                    : "ATTENTION",
            instances.Length == 0
                ? OpsSeverity.Info
                : severity,
            groups.Length == 0
                ? "0 apps"
                : $"{groups.Length} apps",
            instances.Length == 0
                ? "No verified Arr acquisition application detected"
                : $"{instances.Length} verified instance(s) · {attention} attention",
            groups.Length == 0
                ? "Configure a supported acquisition application to populate acquisition health."
                : string.Join(
                    Environment.NewLine,
                    groups.Select(group =>
                        $"{group.Key} · {group.Count()} instance(s) · " +
                        LinuxOpsAnalyzer.SeverityLabel(
                            group.Max(item =>
                                item.Severity)))),
            "Lifecycle",
            "LifecycleNav",
            string.Empty,
            "acquisition",
            true)
        {
            Rows =
                groups
                    .Select(group =>
                        new UnifiedDashboardRow(
                            group.Key,
                            $"{group.Count()} instance(s)",
                            string.Join(
                                " · ",
                                group
                                    .OrderBy(item =>
                                        item.DisplayName)
                                    .Select(item =>
                                        $"{item.DisplayName}: {item.State}")),
                            group.Max(item =>
                                item.Severity)))
                    .ToArray(),
            Actions =
                new[]
                {
                    new UnifiedDashboardAction(
                        "Lifecycle",
                        "LifecycleNav",
                        IsPrimary: true),
                    new UnifiedDashboardAction(
                        "Media Hub",
                        "MediaHubNav"),
                    new UnifiedDashboardAction(
                        "Logs",
                        "@logs",
                        LogSource: aliases,
                        IncludeInformationalLogs: true,
                        LogContext: "Acquisition")
                }
        };
    }

    private IReadOnlyList<UnifiedDashboardCard>
        BuildUnifiedDashboardCards()
    {
        var cards =
            new List<UnifiedDashboardCard>();

        var snapshot = _snapshot!;
        var analysis =
            _policyEvaluation?.Analysis ??
            _analysis!;
        var backup = _backup!;

        var effectiveFindings =
            EffectiveDashboardFindings(
                analysis);
        var activeFindings =
            effectiveFindings
                .Where(item =>
                    item.Severity >=
                    OpsSeverity.Warning)
                .ToArray();
        var errorFindings =
            activeFindings.Count(item =>
                item.Severity >=
                OpsSeverity.Error);
        var warningFindings =
            activeFindings.Count(item =>
                item.Severity ==
                OpsSeverity.Warning);
        var mutedFindings =
            _policyEvaluation?.Muted.Count ??
            0;

        var operationalStorage =
            LinuxOpsAnalyzer
                .OperationalStorage(snapshot);
        var fullest =
            operationalStorage
                .OrderByDescending(volume =>
                    LinuxOpsAnalyzer.UsePercent(
                        volume.PercentUsed))
                .FirstOrDefault();

        cards.Add(
            new UnifiedDashboardCard(
                "core:host",
                "Host",
                "Infrastructure",
                snapshot.SystemState.Equals(
                    "running",
                    StringComparison.OrdinalIgnoreCase)
                    ? "ONLINE"
                    : snapshot.SystemState.ToUpperInvariant(),
                snapshot.SystemState.Equals(
                    "running",
                    StringComparison.OrdinalIgnoreCase)
                    ? OpsSeverity.Healthy
                    : OpsSeverity.Error,
                CompactDashboardUptime(
                    snapshot.Uptime),
                $"{snapshot.OperatingSystem} · {snapshot.Kernel}",
                $"CPU · {snapshot.CpuModel}\n" +
                $"Load · {snapshot.LoadAverage}\n" +
                $"Memory · {snapshot.MemorySummary}\n" +
                $"Failed units · {snapshot.FailedUnits.Count}",
                "Services",
                "ServicesNav",
                string.Empty,
                "host",
                true)
            {
                Facts =
                    new[]
                    {
                        $"Load {snapshot.LoadAverage}",
                        $"Memory {snapshot.MemorySummary}",
                        $"{snapshot.FailedUnits.Count} failed unit(s)"
                    },
                Rows =
                    new[]
                    {
                        new UnifiedDashboardRow(
                            "Load",
                            snapshot.LoadAverage,
                            "One, five and fifteen minute Linux load averages.",
                            OpsSeverity.Info),
                        new UnifiedDashboardRow(
                            "Memory",
                            DashboardCompactMemory(
                                snapshot.MemorySummary),
                            snapshot.MemorySummary,
                            OpsSeverity.Info),
                        new UnifiedDashboardRow(
                            "Failed units",
                            snapshot.FailedUnits.Count.ToString(
                                CultureInfo.InvariantCulture),
                            snapshot.FailedUnits.Count == 0
                                ? "No failed systemd units were reported."
                                : string.Join(
                                    " · ",
                                    snapshot.FailedUnits.Take(5)),
                            snapshot.FailedUnits.Count == 0
                                ? OpsSeverity.Healthy
                                : OpsSeverity.Error)
                    },
                Actions =
                    new[]
                    {
                        new UnifiedDashboardAction(
                            "Services",
                            "ServicesNav",
                            IsPrimary: true),
                        new UnifiedDashboardAction(
                            "Logs",
                            "@logs",
                            LogSource:
                                "systemd|kernel|unit|service",
                            IncludeInformationalLogs: true,
                            LogContext: "Host")
                    }
            });

        cards.Add(
            new UnifiedDashboardCard(
                "core:health",
                "Active findings",
                "Health",
                analysis.Label,
                analysis.Severity,
                activeFindings.Length.ToString(
                    CultureInfo.InvariantCulture),
                activeFindings.Length == 0
                    ? "No active warning-or-higher finding"
                    : analysis.Headline,
                effectiveFindings.Count == 0
                    ? "The current policy-aware host and application dependency graph is healthy."
                    : string.Join(
                        Environment.NewLine,
                        effectiveFindings
                            .Take(10)
                            .Select(item =>
                                $"{LinuxOpsAnalyzer.SeverityLabel(item.Severity)} · {item.Component} · {item.Problem}")),
                "Findings",
                "IntelligenceNav",
                string.Empty,
                "health",
                true)
            {
                Facts =
                    new[]
                    {
                        $"{errorFindings} error",
                        $"{warningFindings} warning",
                        $"{mutedFindings} muted"
                    },
                Rows =
                    activeFindings.Length == 0
                        ? new[]
                        {
                            new UnifiedDashboardRow(
                                "Active findings",
                                "None",
                                "No policy-aware warning-or-higher finding is active.",
                                OpsSeverity.Healthy),
                            new UnifiedDashboardRow(
                                "Muted",
                                mutedFindings.ToString(
                                    CultureInfo.InvariantCulture),
                                "Operator policy suppressions remain visible but do not own overall health.",
                                mutedFindings > 0
                                    ? OpsSeverity.Info
                                    : OpsSeverity.Healthy)
                        }
                        : activeFindings
                            .OrderByDescending(item =>
                                item.Severity)
                            .ThenBy(item =>
                                item.Rank)
                            .Take(4)
                            .Select(item =>
                                new UnifiedDashboardRow(
                                    item.Component,
                                    LinuxOpsAnalyzer.SeverityLabel(
                                        item.Severity),
                                    item.Problem,
                                    item.Severity))
                            .ToArray(),
                Actions =
                    new[]
                    {
                        new UnifiedDashboardAction(
                            "Findings",
                            "IntelligenceNav",
                            IsPrimary: true),
                        new UnifiedDashboardAction(
                            "Logs",
                            "@logs",
                            LogContext:
                                "System health")
                    }
            });

        var activeStorageFindings =
            _policyEvaluation?.Active
                .Where(item =>
                    LinuxFindingPolicyStore
                        .IsStorageCapacityKey(
                            item.Key))
                .ToArray() ??
            Array.Empty<OpsPolicyFinding>();
        var mutedStorageFindings =
            _policyEvaluation?.Muted
                .Where(item =>
                    LinuxFindingPolicyStore
                        .IsStorageCapacityKey(
                            item.Key))
                .ToArray() ??
            Array.Empty<OpsMutedFinding>();
        var storageMountFaultCount =
            _policyEvaluation?.Active.Count(item =>
                item.Component.Equals(
                    "Storage",
                    StringComparison.OrdinalIgnoreCase) &&
                !LinuxFindingPolicyStore.IsStorageCapacityKey(
                    item.Key)) ??
            0;

        var storageRows =
            operationalStorage
                .Select(volume =>
                {
                    var capacity =
                        _findingPolicies
                            .EvaluateStorageCapacity(
                                volume);
                    var active =
                        activeStorageFindings
                            .FirstOrDefault(item =>
                                item.Resource.Equals(
                                    volume.MountPoint,
                                    StringComparison.OrdinalIgnoreCase));
                    var mutedRule =
                        mutedStorageFindings
                            .FirstOrDefault(item =>
                                item.Resource.Equals(
                                    volume.MountPoint,
                                    StringComparison.OrdinalIgnoreCase));
                    var severity =
                        active?.Severity ??
                        (mutedRule is not null
                            ? OpsSeverity.Info
                            : capacity.Severity);
                    var status =
                        mutedRule is not null
                            ? "MUTED"
                            : capacity.StatusLabel;
                    var threshold =
                        _findingPolicies
                            .GetStorageThreshold(
                                volume.MountPoint);
                    var thresholdLabel =
                        $"{(_findingPolicies.HasCustomStorageThreshold(volume.MountPoint) ? "custom" : "default")} " +
                        $"{threshold.WarningPercent}/{threshold.ErrorPercent}/{threshold.CriticalPercent}%";

                    return new
                    {
                        Volume = volume,
                        Capacity = capacity,
                        Severity = severity,
                        Status = status,
                        Muted = mutedRule is not null || capacity.IsMuted,
                        Row = new UnifiedDashboardRow(
                            DashboardMountLabel(
                                volume.MountPoint),
                            volume.PercentUsed,
                            $"{volume.MountPoint} · {volume.Source} · {volume.FileSystem} · " +
                            $"{thresholdLabel} · {capacity.PolicyLabel}",
                            severity,
                            $"{volume.Available} free")
                    };
                })
                // Full drives stay prominent even when intentionally muted.
                .OrderByDescending(item =>
                    LinuxOpsAnalyzer.UsePercent(
                        item.Volume.PercentUsed))
                .ThenByDescending(item =>
                    item.Severity)
                .ToArray();

        var storageSeverity =
            storageRows.Length == 0
                ? OpsSeverity.Warning
                : storageRows.Max(item =>
                    item.Severity);
        var storageMutedCount =
            storageRows.Count(item => item.Muted);
        var storageUnmonitoredCount =
            storageRows.Count(item =>
                !item.Capacity.MonitoringEnabled &&
                !item.Capacity.IsIgnored);
        var storageIgnoredCount =
            storageRows.Count(item =>
                item.Capacity.IsIgnored);
        var storageDashboardOnlyCount =
            storageRows.Count(item =>
                item.Capacity.Mode ==
                StorageCapacityAlertMode.DashboardOnly);
        var storageCustomCount =
            operationalStorage.Count(volume =>
                _findingPolicies
                    .HasCustomStorageThreshold(
                        volume.MountPoint));
        var storageOverrideCount =
            operationalStorage.Count(volume =>
                _findingPolicies
                    .HasStorageCapacityAlertOverride(
                        volume.MountPoint));

        var storageStatus =
            storageRows.Length == 0
                ? "UNAVAILABLE"
                : storageSeverity >= OpsSeverity.Warning
                    ? LinuxOpsAnalyzer.SeverityLabel(
                        storageSeverity)
                    : storageMutedCount > 0
                        ? "MUTED"
                        : storageIgnoredCount == storageRows.Length
                            ? "IGNORED"
                            : storageUnmonitoredCount == storageRows.Length
                                ? "UNMONITORED"
                                : storageUnmonitoredCount + storageIgnoredCount > 0
                                    ? "POLICY MIXED"
                                    : LinuxOpsAnalyzer.SeverityLabel(
                                        storageSeverity);

        var storageSummary =
            storageRows.Length == 0
                ? "No operational filesystem returned"
                : storageMountFaultCount > 0
                    ? $"{storageRows.Length} mounted · {storageMountFaultCount} mount " +
                      $"{(storageMountFaultCount == 1 ? "fault" : "faults")}"
                    : activeStorageFindings.Length > 0
                        ? $"{storageRows.Length} mounted · {activeStorageFindings.Length} capacity " +
                          $"{(activeStorageFindings.Length == 1 ? "warning" : "warnings")}"
                        : storageUnmonitoredCount + storageIgnoredCount == storageRows.Length
                            ? $"{storageRows.Length} mounted · capacity alerts disabled"
                            : storageMutedCount > 0
                                ? $"{storageRows.Length} mounted · {storageMutedCount} capacity " +
                                  $"{(storageMutedCount == 1 ? "warning" : "warnings")} muted"
                                : storageDashboardOnlyCount > 0
                                    ? $"{storageRows.Length} mounted · capacity alerts dashboard only"
                                    : $"{storageRows.Length} mounted · 0 mount faults";

        cards.Add(
            new UnifiedDashboardCard(
                "core:storage",
                "Storage",
                "Infrastructure",
                storageStatus,
                storageSeverity,
                operationalStorage.Count == 0
                    ? "--"
                    : $"{operationalStorage.Count} mounts",
                storageSummary,
                string.Join(
                    Environment.NewLine,
                    storageRows.Select(item =>
                        $"{item.Volume.MountPoint} · {item.Volume.PercentUsed} used · " +
                        $"{item.Volume.Available} free · {item.Status} · " +
                        item.Row.Detail)),
                "Storage",
                "StorageNav",
                string.Empty,
                "storage",
                true)
            {
                Facts =
                    new[]
                    {
                        $"{activeStorageFindings.Length} active capacity alert(s)",
                        $"{storageMutedCount} muted",
                        $"{storageUnmonitoredCount} unmonitored",
                        $"{storageDashboardOnlyCount} dashboard-only",
                        $"{storageOverrideCount} mount override(s)",
                        $"{storageCustomCount} custom threshold mount(s)"
                    },
                Rows =
                    storageRows
                        .Select(item =>
                            item.Row)
                        .ToArray(),
                Actions =
                    new[]
                    {
                        new UnifiedDashboardAction(
                            "Storage",
                            "StorageNav",
                            IsPrimary: true),
                        new UnifiedDashboardAction(
                            "Logs",
                            "@logs",
                            LogText:
                                fullest is null
                                    ? "mount"
                                    : DashboardMountLogToken(
                                        fullest.MountPoint),
                            IncludeInformationalLogs: true),
                        new UnifiedDashboardAction(
                            "Capacity policy",
                            "StorageNav")
                    }
            });

        var verifiedHealthOwners =
            _integrations
                .Where(item =>
                    item.IsVerified &&
                    item.OwnsHealth)
                .ToArray();

        var mediaAttention =
            verifiedHealthOwners.Count(item =>
                item.Severity >=
                OpsSeverity.Warning);
        var mediaProducts =
            verifiedHealthOwners
                .Select(item => item.Name)
                .Distinct(
                    StringComparer.OrdinalIgnoreCase)
                .Count();

        cards.Add(
            new UnifiedDashboardCard(
                "core:media",
                "Media fleet",
                "Applications",
                mediaAttention == 0
                    ? "HEALTHY"
                    : "ATTENTION",
                mediaAttention == 0
                    ? OpsSeverity.Healthy
                    : OpsSeverity.Warning,
                verifiedHealthOwners.Length.ToString(
                    CultureInfo.InvariantCulture),
                $"{mediaProducts} product(s) · " +
                $"{verifiedHealthOwners.Length} verified instance(s)",
                string.Join(
                    Environment.NewLine,
                    verifiedHealthOwners
                        .GroupBy(item =>
                            item.Name,
                            StringComparer.OrdinalIgnoreCase)
                        .OrderBy(group =>
                            group.Key)
                        .Select(group =>
                            $"{group.Key} · {group.Count()} instance(s) · " +
                            $"{LinuxOpsAnalyzer.SeverityLabel(group.Max(item => item.Severity))}")),
                "Media Hub",
                "MediaHubNav",
                string.Empty,
                "media",
                true)
            {
                Facts =
                    new[]
                    {
                        $"{mediaAttention} attention",
                        $"{mediaProducts} products",
                        $"{verifiedHealthOwners.Length} instances"
                    },
                Rows =
                    verifiedHealthOwners
                        .GroupBy(item =>
                            item.Name,
                            StringComparer.OrdinalIgnoreCase)
                        .OrderByDescending(group =>
                            group.Max(item =>
                                item.Severity))
                        .ThenBy(group =>
                            group.Key)
                        .Take(4)
                        .Select(group =>
                            new UnifiedDashboardRow(
                                group.Key,
                                $"{group.Count()} instance(s)",
                                string.Join(
                                    " · ",
                                    group
                                        .OrderBy(item =>
                                            item.DisplayName)
                                        .Select(item =>
                                            $"{item.DisplayName}: {item.State}")),
                                group.Max(item =>
                                    item.Severity)))
                        .ToArray(),
                Actions =
                    new[]
                    {
                        new UnifiedDashboardAction(
                            "Media Hub",
                            "MediaHubNav",
                            IsPrimary: true),
                        new UnifiedDashboardAction(
                            "Lifecycle",
                            "LifecycleNav")
                    }
            });

        var downloadNames =
            new HashSet<string>(
                new[]
                {
                    "SABnzbd",
                    "qBittorrent",
                    "Transmission",
                    "Deluge",
                    "NZBGet"
                },
                StringComparer.OrdinalIgnoreCase);
        var downloaders =
            verifiedHealthOwners
                .Where(item =>
                    downloadNames.Contains(
                        item.Name) ||
                    item.Category.Contains(
                        "download",
                        StringComparison.OrdinalIgnoreCase))
                .ToArray();
        var downloadSnapshots =
            SupportsTargetCapability(
                CapabilityIds.ApplicationApiTelemetry)
                ? downloaders
                    .Select(item =>
                        _downloadClientCache.TryGetValue(
                            item.Name,
                            out var sample)
                            ? sample
                            : null)
                    .Where(item => item is not null)
                    .Cast<DownloadClientTelemetrySnapshot>()
                    .ToArray()
                : Array.Empty<DownloadClientTelemetrySnapshot>();
        var activeTransfers =
            downloadSnapshots.Sum(item =>
                item.ActiveCount);
        var failedRecent =
            downloadSnapshots.Sum(item =>
                item.FailedRecentCount);
        var downloadSeverity =
            downloaders.Any(item =>
                item.Severity >=
                OpsSeverity.Warning)
                ? OpsSeverity.Warning
                : downloaders.Length == 0
                    ? OpsSeverity.Info
                    : OpsSeverity.Healthy;

        cards.Add(
            new UnifiedDashboardCard(
                "core:downloads",
                "Downloads",
                "Acquisition",
                downloadSeverity >=
                    OpsSeverity.Warning
                    ? "ATTENTION"
                    : downloaders.Length == 0
                        ? "NOT CONFIGURED"
                        : "READY",
                downloadSeverity,
                downloadSnapshots.Length > 0
                    ? activeTransfers.ToString(
                        CultureInfo.InvariantCulture)
                    : downloaders.Length.ToString(
                        CultureInfo.InvariantCulture),
                downloadSnapshots.Length > 0
                    ? $"{activeTransfers} active transfer(s) across {downloaders.Length} client(s)"
                    : downloaders.Length == 0
                        ? "No verified downloader detected"
                        : $"{downloaders.Length} verified client(s) · " +
                          string.Join(
                              " + ",
                              downloaders
                                  .Select(item => item.Name)
                                  .Distinct(
                                      StringComparer.OrdinalIgnoreCase)),
                string.Join(
                    Environment.NewLine,
                    downloaders.Select(item =>
                    {
                        var sample =
                            downloadSnapshots.FirstOrDefault(value =>
                                value.ClientKey.Equals(
                                    item.Name,
                                    StringComparison.OrdinalIgnoreCase));

                        return sample is null
                            ? $"{item.DisplayName} · {item.State}"
                            : $"{item.DisplayName} · {sample.ActiveCount} active · " +
                              $"{sample.DownloadSpeed} down · {sample.FailedRecentCount} recent failed";
                    })),
                "Downloads",
                downloaders.Any(item =>
                    item.Name.Equals(
                        "SABnzbd",
                        StringComparison.OrdinalIgnoreCase))
                    ? "SabnzbdNav"
                    : "QBittorrentNav",
                string.Empty,
                "downloads",
                true)
            {
                Facts =
                    downloadSnapshots.Length > 0
                        ? downloadSnapshots
                            .Select(item =>
                                $"{item.DisplayName} {item.DownloadSpeed}")
                            .Append(
                                $"{failedRecent} recent failed")
                            .Take(3)
                            .ToArray()
                        : downloaders
                            .Select(item =>
                                $"{item.DisplayName} {item.State}")
                            .Take(3)
                            .ToArray(),
                Rows =
                    downloaders
                        .OrderBy(item =>
                            item.Name)
                        .Select(item =>
                        {
                            var sample =
                                downloadSnapshots
                                    .FirstOrDefault(value =>
                                        value.ClientKey.Equals(
                                            item.Name,
                                            StringComparison.OrdinalIgnoreCase));

                            return sample is null
                                ? new UnifiedDashboardRow(
                                    item.Name,
                                    item.State,
                                    item.Evidence,
                                    item.Severity)
                                : new UnifiedDashboardRow(
                                    item.Name,
                                    $"{sample.ActiveCount} active · {sample.DownloadSpeed}",
                                    $"{sample.State} · {sample.Remaining} remaining · " +
                                    $"{sample.FailedRecentCount} recent failed",
                                    item.Severity);
                        })
                        .Take(4)
                        .ToArray(),
                Actions =
                    DashboardDownloaderActions(
                        downloaders)
            });

        var runningContainers =
            snapshot.Containers.Count(container =>
                container.State.Equals(
                    "running",
                    StringComparison.OrdinalIgnoreCase));
        var informationalContainers =
            snapshot.Containers.Count(container =>
                LinuxOpsAnalyzer.ContainerSeverity(
                    container) ==
                OpsSeverity.Info);
        var unhealthyContainers =
            snapshot.Containers.Count(container =>
                LinuxOpsAnalyzer.ContainerSeverity(
                    container) >=
                OpsSeverity.Warning);
        var dockerSeverity =
            snapshot.Containers
                .Select(
                    LinuxOpsAnalyzer.ContainerSeverity)
                .DefaultIfEmpty(
                    OpsSeverity.Info)
                .Max();
        var hasDumb =
            _integrations.Any(item =>
                item.Name.Equals(
                    "DUMB",
                    StringComparison.OrdinalIgnoreCase) &&
                item.IsVerified);

        cards.Add(
            new UnifiedDashboardCard(
                "core:docker",
                "Docker",
                "Infrastructure",
                snapshot.Containers.Count == 0
                    ? "NOT DETECTED"
                    : LinuxOpsAnalyzer.SeverityLabel(
                        dockerSeverity),
                dockerSeverity,
                $"{runningContainers}/{snapshot.Containers.Count}",
                snapshot.Containers.Count == 0
                    ? "No containers reported"
                    : $"{runningContainers} running · " +
                      $"{snapshot.Containers.Count - runningContainers} not running",
                string.Join(
                    Environment.NewLine,
                    snapshot.Containers
                        .OrderBy(container =>
                            container.Name)
                        .Select(container =>
                            $"{container.Name} · {container.Status}")),
                "Containers",
                "DockerNav",
                string.Empty,
                "docker",
                true)
            {
                Facts =
                    new[]
                    {
                        $"{unhealthyContainers} unhealthy",
                        $"{informationalContainers} expected stopped",
                        $"{snapshot.Containers.Count} total"
                    },
                Rows =
                    snapshot.Containers
                        .OrderByDescending(container =>
                            LinuxOpsAnalyzer.ContainerSeverity(
                                container))
                        .ThenBy(container =>
                            container.Name)
                        .Take(4)
                        .Select(container =>
                            new UnifiedDashboardRow(
                                container.Name,
                                container.State,
                                $"{container.Status} · {container.Image}",
                                LinuxOpsAnalyzer.ContainerSeverity(
                                    container)))
                        .ToArray(),
                Actions =
                    hasDumb
                        ? new[]
                        {
                            new UnifiedDashboardAction(
                                "Containers",
                                "DockerNav",
                                IsPrimary: true),
                            new UnifiedDashboardAction(
                                "DUMB stack",
                                "DumbNav"),
                            new UnifiedDashboardAction(
                                "Logs",
                                "@logs",
                                LogSource:
                                    "docker|dockerd|containerd|container",
                                IncludeInformationalLogs: true,
                                LogContext: "Docker")
                        }
                        : new[]
                        {
                            new UnifiedDashboardAction(
                                "Containers",
                                "DockerNav",
                                IsPrimary: true),
                            new UnifiedDashboardAction(
                                "Logs",
                                "@logs",
                                LogSource:
                                    "docker|dockerd|containerd|container",
                                IncludeInformationalLogs: true,
                                LogContext: "Docker")
                        }
            });

        var newestArtifact =
            backup.Artifacts
                .OrderByDescending(item =>
                    item.ModifiedAt)
                .FirstOrDefault();

        cards.Add(
            new UnifiedDashboardCard(
                "core:backups",
                "Backups",
                "Recovery",
                backup.State,
                backup.Severity,
                backup.Artifacts.Count.ToString(
                    CultureInfo.InvariantCulture),
                backup.Summary,
                string.Join(
                    Environment.NewLine,
                    backup.Evidence.Take(12)),
                "Backups",
                "BackupsNav",
                string.Empty,
                "backups",
                true)
            {
                Facts =
                    new[]
                    {
                        $"{backup.Units.Count} schedule unit(s)",
                        newestArtifact is null
                            ? "No recent artifact"
                            : $"Newest {FormatDuration(DateTimeOffset.Now - newestArtifact.ModifiedAt)} ago",
                        LinuxOpsAnalyzer.SeverityLabel(
                            backup.Severity)
                    },
                Rows =
                    new[]
                    {
                        new UnifiedDashboardRow(
                            "Schedules",
                            backup.Units.Count.ToString(
                                CultureInfo.InvariantCulture),
                            string.Join(
                                " · ",
                                backup.Units
                                    .Take(5)
                                    .Select(item =>
                                        $"{item.Unit}: {item.Active}/{item.Enabled}")),
                            backup.Units.Count > 0
                                ? OpsSeverity.Healthy
                                : OpsSeverity.Warning),
                        new UnifiedDashboardRow(
                            "Newest",
                            newestArtifact is null
                                ? "None"
                                : FormatDuration(
                                    DateTimeOffset.Now -
                                    newestArtifact.ModifiedAt) +
                                  " ago",
                            newestArtifact?.Path ??
                            "No backup artifact was returned.",
                            newestArtifact is null
                                ? OpsSeverity.Warning
                                : backup.Severity),
                        new UnifiedDashboardRow(
                            "Provider",
                            backup.Provider,
                            backup.Summary,
                            backup.Severity)
                    },
                Actions =
                    new[]
                    {
                        new UnifiedDashboardAction(
                            "Backups",
                            "BackupsNav",
                            IsPrimary: true),
                        new UnifiedDashboardAction(
                            "History",
                            "HistoryNav"),
                        new UnifiedDashboardAction(
                            "Logs",
                            "@logs",
                            LogText:
                                "backup|restic|borg|rsync|snapshot|timer",
                            IncludeInformationalLogs: true,
                            LogContext: "Backups")
                    }
            });

        var targetActivities =
            ActiveTargetActivities();
        var meaningfulActivity =
            targetActivities
                .Where(item =>
                    !item.Kind.Equals(
                        "Navigation",
                        StringComparison.OrdinalIgnoreCase))
                .Take(8)
                .ToArray();
        var targetJobs =
            ActiveTargetJobs();
        var targetUnread =
            targetActivities.Count(item =>
                item.IsUnread);
        var targetRunningJobs =
            targetJobs.Count(item =>
                item.State.Equals(
                    "Running",
                    StringComparison.OrdinalIgnoreCase) ||
                item.State.Equals(
                    "Queued",
                    StringComparison.OrdinalIgnoreCase));
        var recentFailures =
            meaningfulActivity.Count(item =>
                item.Kind.Equals(
                    "Failure",
                    StringComparison.OrdinalIgnoreCase));
        var recentNotifications =
            meaningfulActivity.Count(item =>
                item.Kind.Equals(
                    "Notification",
                    StringComparison.OrdinalIgnoreCase));

        cards.Add(
            new UnifiedDashboardCard(
                "core:activity",
                "Recent activity",
                "Operations",
                targetUnread > 0
                    ? "NEW"
                    : "CURRENT",
                targetUnread > 0
                    ? OpsSeverity.Info
                    : OpsSeverity.Healthy,
                meaningfulActivity.Length.ToString(
                    CultureInfo.InvariantCulture),
                $"{targetUnread} unread · " +
                $"{targetRunningJobs} running job(s)",
                string.Join(
                    Environment.NewLine,
                    meaningfulActivity.Select(item =>
                        $"{item.DisplayTime} · {item.Title} · {item.Detail}")),
                "Activity",
                "@activity",
                string.Empty,
                "activity",
                true)
            {
                Facts =
                    new[]
                    {
                        $"{recentFailures} recent failure(s)",
                        $"{recentNotifications} notification(s)",
                        $"{targetRunningJobs} job(s)"
                    },
                Rows =
                    meaningfulActivity
                        .Take(4)
                        .Select(item =>
                            new UnifiedDashboardRow(
                                item.Kind,
                                item.DisplayTime,
                                $"{item.Title} · {item.Detail}",
                                item.Kind.Equals(
                                    "Failure",
                                    StringComparison.OrdinalIgnoreCase)
                                    ? OpsSeverity.Error
                                    : item.Kind.Equals(
                                        "Notification",
                                        StringComparison.OrdinalIgnoreCase)
                                        ? OpsSeverity.Info
                                        : OpsSeverity.Healthy))
                        .ToArray(),
                Actions =
                    new[]
                    {
                        new UnifiedDashboardAction(
                            "Activity",
                            "@activity",
                            IsPrimary: true),
                        new UnifiedDashboardAction(
                            "Jobs",
                            "@jobs")
                    }
            });

        foreach (var group in
                 _integrations
                     .Where(
                         DashboardCardEligibleIntegration)
                     .GroupBy(
                         item => item.Name,
                         StringComparer.OrdinalIgnoreCase)
                     .OrderBy(group =>
                         DashboardDisplayPriority(
                             $"app:{NormalizeCardKey(group.Key)}"))
                     .ThenBy(group =>
                         group.Key,
                         StringComparer.OrdinalIgnoreCase))
        {
            cards.Add(
                BuildProviderDashboardCard(
                    group.Key,
                    group.ToArray()));
        }

        if (!SupportsTargetCapability(
                CapabilityIds.BackupInventoryRead))
        {
            cards.RemoveAll(card =>
                card.Key.Equals(
                    "core:backups",
                    StringComparison.OrdinalIgnoreCase));
        }

        var distinctCards =
            cards
                .GroupBy(
                    item => item.Key,
                    StringComparer.OrdinalIgnoreCase)
                .Select(group =>
                    group.First())
                .ToArray();

        return ApplySignalQualityToDashboardCards(
            distinctCards);
    }

    private IReadOnlyList<UnifiedDashboardAction>
        DashboardDownloaderActions(
            IReadOnlyList<OpsIntegration> downloaders)
    {
        var actions =
            new List<UnifiedDashboardAction>();

        if (downloaders.Any(item =>
                item.Name.Equals(
                    "SABnzbd",
                    StringComparison.OrdinalIgnoreCase)))
        {
            actions.Add(
                new UnifiedDashboardAction(
                    "SABnzbd",
                    "SabnzbdNav",
                    IsPrimary:
                        actions.Count == 0));
        }

        if (downloaders.Any(item =>
                item.Name.Equals(
                    "qBittorrent",
                    StringComparison.OrdinalIgnoreCase)))
        {
            actions.Add(
                new UnifiedDashboardAction(
                    "qBittorrent",
                    "QBittorrentNav",
                    IsPrimary:
                        actions.Count == 0));
        }

        if (actions.Count == 0)
        {
            actions.Add(
                new UnifiedDashboardAction(
                    "Media Hub",
                    "MediaHubNav",
                    IsPrimary: true));
        }

        return actions;
    }

    private UnifiedDashboardCard
        BuildProviderDashboardCard(
            string product,
            IReadOnlyList<OpsIntegration> instances)
    {
        var verified =
            instances
                .Where(item =>
                    item.IsVerified)
                .ToArray();
        var severity =
            verified
                .Where(item =>
                    item.OwnsHealth)
                .Select(item =>
                    item.Severity)
                .DefaultIfEmpty(
                    OpsSeverity.Info)
                .Max();
        var category =
            instances
                .Select(item =>
                    item.Category)
                .FirstOrDefault(value =>
                    !string.IsNullOrWhiteSpace(value)) ??
            "Application";
        var dedicatedNavigation =
            NavigationForIntegration(
                product);
        var navigation =
            dedicatedNavigation ??
            $"@integration:{product}";
        var endpoint =
            verified
                .Select(
                    ResolveIntegrationUrl)
                .FirstOrDefault(value =>
                    !string.IsNullOrWhiteSpace(value)) ??
            string.Empty;
        var status =
            verified.Length == 0
                ? "CANDIDATE"
                : LinuxOpsAnalyzer.SeverityLabel(
                    severity);
        var attention =
            instances.Count(item =>
                item.Severity >=
                OpsSeverity.Warning);
        var labels =
            instances
                .Select(item =>
                    DashboardInstanceLabel(
                        product,
                        item.DisplayName))
                .Where(value =>
                    !string.IsNullOrWhiteSpace(value))
                .Distinct(
                    StringComparer.OrdinalIgnoreCase)
                .ToArray();
        var dockerManaged =
            instances.Count(
                DashboardDockerManaged);
        var systemManaged =
            instances.Count(item =>
                item.Kind.Contains(
                    "systemd",
                    StringComparison.OrdinalIgnoreCase));
        var management =
            dockerManaged > 0
                ? $"{dockerManaged} Docker"
                : systemManaged > 0
                    ? $"{systemManaged} systemd"
                    : $"{verified.Length} verified";
        var summary =
            verified.Length == 0
                ? $"{instances.Count} unverified candidate instance(s)"
                : $"{instances.Count} instance(s) · " +
                  (labels.Length == 0
                      ? $"{verified.Length} verified"
                      : string.Join(
                          " + ",
                          labels.Take(3)));

        var detail =
            string.Join(
                Environment.NewLine,
                instances
                    .OrderBy(item =>
                        item.DisplayName)
                    .Select(item =>
                    {
                        var ownership =
                            DashboardDockerManaged(
                                item)
                                ? "Docker managed"
                                : item.Kind.Contains(
                                    "systemd",
                                    StringComparison.OrdinalIgnoreCase)
                                    ? "System service"
                                    : item.IsVerified
                                        ? "Verified application"
                                        : "Discovery candidate";

                        return
                            $"{item.DisplayName} · {ownership} · {item.State}" +
                            (string.IsNullOrWhiteSpace(item.Endpoint)
                                ? string.Empty
                                : $" · {item.Endpoint}");
                    }));

        var primaryValue =
            instances.Count.ToString(
                CultureInfo.InvariantCulture);
        IReadOnlyList<string> facts =
            new[]
            {
                $"{instances.Count} instance(s)",
                management,
                $"{attention} attention"
            };
        IReadOnlyList<UnifiedDashboardRow> rows =
            instances
                .OrderByDescending(item =>
                    item.Severity)
                .ThenBy(item =>
                    item.DisplayName)
                .Select(item =>
                    new UnifiedDashboardRow(
                        DashboardInstanceLabel(
                            product,
                            item.DisplayName),
                        item.State,
                        $"{item.DisplayName} · " +
                        $"{(DashboardDockerManaged(item) ? "Docker managed" : item.Kind)}" +
                        (string.IsNullOrWhiteSpace(item.Endpoint)
                            ? string.Empty
                            : $" · {item.Endpoint}"),
                        item.Severity))
                .ToArray();

        if (SupportsTargetCapability(
                CapabilityIds.ApplicationApiTelemetry) &&
            product.Equals(
                "Plex",
                StringComparison.OrdinalIgnoreCase) &&
            _plexCache.TryGetValue(
                _controlPlane.ActiveProfile.Id,
                out var plex))
        {
            primaryValue =
                plex.ActiveSessions.ToString(
                    CultureInfo.InvariantCulture);
            summary =
                $"{plex.ActiveSessions} session(s) · " +
                $"{plex.DirectPlayCount} direct play · " +
                $"{plex.TranscodeCount} transcode";
            detail =
                $"Libraries · {plex.LibraryCount}" +
                Environment.NewLine +
                $"Bandwidth · {plex.TotalBandwidth}" +
                Environment.NewLine +
                detail;
            severity =
                PlexSeverity(
                    plex.State);
            status =
                LinuxOpsAnalyzer.SeverityLabel(
                    severity);
            facts =
                new[]
                {
                    $"{plex.LibraryCount} libraries",
                    $"{plex.DirectPlayCount} direct play",
                    $"{plex.TranscodeCount} transcode"
                };
            rows =
                new[]
                {
                    new UnifiedDashboardRow(
                        "Sessions",
                        plex.ActiveSessions.ToString(
                            CultureInfo.InvariantCulture),
                        $"Current bandwidth · {plex.TotalBandwidth}",
                        plex.ActiveSessions > 0
                            ? OpsSeverity.Info
                            : OpsSeverity.Healthy),
                    new UnifiedDashboardRow(
                        "Direct play",
                        plex.DirectPlayCount.ToString(
                            CultureInfo.InvariantCulture),
                        "Sessions playing without video transcoding.",
                        OpsSeverity.Healthy),
                    new UnifiedDashboardRow(
                        "Transcodes",
                        plex.TranscodeCount.ToString(
                            CultureInfo.InvariantCulture),
                        "Active transcoding sessions.",
                        plex.TranscodeCount > 0
                            ? OpsSeverity.Info
                            : OpsSeverity.Healthy),
                    new UnifiedDashboardRow(
                        "Libraries",
                        plex.LibraryCount.ToString(
                            CultureInfo.InvariantCulture),
                        plex.State,
                        severity)
                };
        }
        else if (product.Equals(
                     "Jellyfin",
                     StringComparison.OrdinalIgnoreCase) ||
                 product.Equals(
                     "Emby",
                     StringComparison.OrdinalIgnoreCase))
        {
            summary =
                $"{instances.Count} media-server instance(s) · " +
                $"{(endpoint.Length > 0 ? "interface verified" : "managed locally")}";
        }
        else if (product.Equals(
                     "Bazarr",
                     StringComparison.OrdinalIgnoreCase))
        {
            summary =
                $"{instances.Count} subtitle instance(s) · " +
                $"{verified.Length} verified · {attention} attention";
        }
        else if (product.Equals(
                     "Mylar3",
                     StringComparison.OrdinalIgnoreCase))
        {
            summary =
                $"{instances.Count} comics instance(s) · " +
                $"{verified.Length} verified · {attention} attention";
        }
        else if (product.Equals(
                     "Recyclarr",
                     StringComparison.OrdinalIgnoreCase))
        {
            summary =
                $"{instances.Count} configuration instance(s) · " +
                $"{verified.Length} verified · {attention} attention";
        }
        else if (DashboardIsApprovedApplication(
                     product))
        {
            summary =
                $"{instances.Count} managed instance(s) · " +
                $"{verified.Length} verified · {attention} attention";
        }
        else if (ArrApiCatalog.IsSupportedProduct(
                     product))
        {
            summary =
                $"{instances.Count} instance(s) · " +
                (labels.Length == 0
                    ? status.ToLowerInvariant()
                    : string.Join(
                        " + ",
                        labels.Take(3)));
            rows =
                instances
                    .OrderBy(item =>
                        item.DisplayName)
                    .Select(item =>
                        new UnifiedDashboardRow(
                            DashboardInstanceLabel(
                                product,
                                item.DisplayName),
                            item.IsVerified
                                ? "VERIFIED"
                                : "CANDIDATE",
                            $"{item.State} · {item.Protocol} · {item.Endpoint}",
                            item.Severity))
                    .ToArray();
        }

        var actions =
            new List<UnifiedDashboardAction>
            {
                new(
                    dedicatedNavigation is null
                        ? "Open app"
                        : "Workspace",
                    navigation,
                    IsPrimary: true)
            };

        if (!string.IsNullOrWhiteSpace(
                endpoint))
        {
            actions.Add(
                new UnifiedDashboardAction(
                    "Web UI",
                    string.Empty,
                    endpoint));
        }
        else if (dockerManaged > 0)
        {
            actions.Add(
                new UnifiedDashboardAction(
                    "Container",
                    "DockerNav"));
        }

        actions.Add(
            new UnifiedDashboardAction(
                "Logs",
                "@logs",
                LogSource:
                    DashboardLogAliases(
                        product),
                IncludeInformationalLogs: true,
                LogContext: product));

        return new UnifiedDashboardCard(
            $"app:{NormalizeCardKey(product)}",
            product,
            string.IsNullOrWhiteSpace(category)
                ? "Application"
                : category,
            status,
            severity,
            primaryValue,
            summary,
            detail,
            actions[0].Label,
            actions[0].NavigationName,
            actions[0].Endpoint,
            instances[0].InstanceKey,
            category.Equals(
                "Library",
                StringComparison.OrdinalIgnoreCase) ||
            DashboardIsApprovedApplication(
                product) ||
            DashboardIsServarrLikeProduct(
                product) ||
            severity >= OpsSeverity.Warning)
        {
            Facts = facts,
            Rows = rows,
            Actions = actions
        };
    }

    private static string DashboardInstanceLabel(
        string product,
        string displayName)
    {
        var label =
            string.IsNullOrWhiteSpace(
                displayName)
                ? product
                : displayName.Trim();

        if (label.StartsWith(
                product,
                StringComparison.OrdinalIgnoreCase))
        {
            label =
                label[product.Length..]
                    .Trim(
                        ' ',
                        '-',
                        '·',
                        ':');
        }

        return string.IsNullOrWhiteSpace(
                label)
            ? "Default"
            : label;
    }

    private static bool DashboardDockerManaged(
        OpsIntegration item) =>
        item.Kind.Contains(
            "docker",
            StringComparison.OrdinalIgnoreCase) ||
        item.Kind.Contains(
            "container",
            StringComparison.OrdinalIgnoreCase) ||
        item.Evidence.Contains(
            "compose",
            StringComparison.OrdinalIgnoreCase) ||
        item.Evidence.Contains(
            "container",
            StringComparison.OrdinalIgnoreCase);

    private static string DashboardMountLabel(
        string mountPoint)
    {
        var normalized =
            (mountPoint ?? string.Empty)
                .Trim()
                .TrimEnd(
                    Path.DirectorySeparatorChar,
                    Path.AltDirectorySeparatorChar);
        var label =
            Path.GetFileName(
                normalized);

        return string.IsNullOrWhiteSpace(
                label)
            ? string.IsNullOrWhiteSpace(normalized)
                ? "Mount"
                : normalized
            : label;
    }

    private static string DashboardMountLogToken(
        string mountPoint)
    {
        var label =
            DashboardMountLabel(
                mountPoint);

        return label.Equals(
                "Mount",
                StringComparison.OrdinalIgnoreCase)
            ? "mount"
            : label;
    }

    private static string DashboardStorageLogAliases(
        IReadOnlyList<GraveOps.Core.Hosts.StorageVolumeSnapshot> volumes,
        GraveOps.Core.Hosts.StorageVolumeSnapshot? fullest)
    {
        var aliases =
            volumes
                .SelectMany(volume =>
                    new[]
                    {
                        DashboardMountLabel(
                            volume.MountPoint),
                        DashboardMountLogToken(
                            volume.MountPoint),
                        Path.GetFileName(
                            volume.Source ?? string.Empty),
                        volume.FileSystem
                    })
                .Concat(
                    new[]
                    {
                        "storage",
                        "disk",
                        "filesystem",
                        "no space",
                        "full"
                    })
                .Where(value =>
                    !string.IsNullOrWhiteSpace(
                        value))
                .Distinct(
                    StringComparer.OrdinalIgnoreCase)
                .ToList();

        if (fullest is not null)
        {
            aliases.Insert(
                0,
                DashboardMountLogToken(
                    fullest.MountPoint));
        }

        return string.Join(
            "|",
            aliases);
    }

    private static string DashboardLogAliases(
        string product) =>
        product.Trim().ToLowerInvariant() switch
        {
            "plex" =>
                "plex|plexmediaserver",
            "sabnzbd" =>
                "sabnzbd|sab",
            "qbittorrent" =>
                "qbittorrent|qbit",
            "dumb" =>
                "dumb|docker compose|compose",
            "recyclarr" =>
                "recyclarr|cron|schedule",
            "pihole" or "pi-hole" =>
                "pihole|pi-hole|pihole-ftl|dnsmasq",
            _ =>
                product
        };

    private static string DashboardCompactMemory(
        string value)
    {
        if (string.IsNullOrWhiteSpace(
                value))
        {
            return "--";
        }

        var compact =
            value
                .Replace(
                    "Memory ",
                    string.Empty,
                    StringComparison.OrdinalIgnoreCase)
                .Replace(
                    " available",
                    " avail",
                    StringComparison.OrdinalIgnoreCase);

        return compact.Length <= 34
            ? compact
            : compact[..31] + "...";
    }

    private static string CompactDashboardUptime(
        string value)
    {
        var compact =
            (value ?? string.Empty)
                .Replace(
                    "Up ",
                    string.Empty,
                    StringComparison.OrdinalIgnoreCase)
                .Replace(
                    " hours",
                    "h",
                    StringComparison.OrdinalIgnoreCase)
                .Replace(
                    " hour",
                    "h",
                    StringComparison.OrdinalIgnoreCase)
                .Replace(
                    " minutes",
                    "m",
                    StringComparison.OrdinalIgnoreCase)
                .Replace(
                    " minute",
                    "m",
                    StringComparison.OrdinalIgnoreCase)
                .Replace(
                    ", ",
                    " ",
                    StringComparison.Ordinal);

        return string.IsNullOrWhiteSpace(
                compact)
            ? "--"
            : compact;
    }

    private static string NormalizeCardKey(
        string value) =>
        new string(
            value
                .Trim()
                .ToLowerInvariant()
                .Select(character =>
                    char.IsLetterOrDigit(character)
                        ? character
                        : '-')
                .ToArray())
            .Trim('-');

    private List<DashboardCardPreference>
        ResolveDashboardLayout(
            IReadOnlyList<UnifiedDashboardCard> cards)
    {
        var hostId =
            _controlPlane.ActiveProfile.Id;
        var hasStored =
            _unifiedInterface.DashboardLayouts
                .TryGetValue(
                    hostId,
                    out var stored);

        var result =
            hasStored
                ? stored!
                    .Where(preference =>
                        cards.Any(card =>
                            card.Key.Equals(
                                preference.Key,
                                StringComparison.OrdinalIgnoreCase)))
                    .OrderBy(preference =>
                        preference.Order)
                    .Select(CloneDashboardPreference)
                    .ToList()
                : new List<DashboardCardPreference>();

        var changed =
            !hasStored;

        foreach (var card in cards)
        {
            if (result.Any(item =>
                    item.Key.Equals(
                        card.Key,
                        StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            result.Add(
                new DashboardCardPreference
                {
                    Key = card.Key,
                    IsVisible =
                        card.DefaultVisible,
                    VisibilityExplicit =
                        false,
                    Order = result.Count
                });
            changed =
                true;
        }

        foreach (var preference in result)
        {
            var card =
                cards.First(item =>
                    item.Key.Equals(
                        preference.Key,
                        StringComparison.OrdinalIgnoreCase));

            if (!DashboardIsApprovedApplicationKey(
                    preference.Key) ||
                preference.VisibilityExplicit ||
                preference.IsVisible ==
                    card.DefaultVisible)
            {
                continue;
            }

            preference.IsVisible =
                card.DefaultVisible;
            changed =
                true;
        }

        if (_unifiedInterface.DashboardLayoutRevision < 5)
        {
            _unifiedInterface.DashboardLayoutRevision =
                5;
            changed =
                true;
        }

        result =
            result
                .OrderBy(item =>
                    DashboardDisplayPriority(
                        item.Key))
                .ThenBy(item =>
                    item.Order)
                .ToList();

        for (var index = 0;
             index < result.Count;
             index++)
        {
            if (result[index].Order !=
                index)
            {
                changed =
                    true;
            }

            result[index].Order =
                index;
        }

        if (changed)
        {
            PersistDashboardLayout(
                hostId,
                result);
        }

        return result;
    }

    private static DashboardCardPreference
        CloneDashboardPreference(
            DashboardCardPreference preference) =>
        new()
        {
            Key = preference.Key,
            IsVisible =
                preference.IsVisible,
            VisibilityExplicit =
                preference.VisibilityExplicit,
            Order =
                preference.Order
        };

    private void PersistDashboardLayout(
        string hostId,
        IReadOnlyList<DashboardCardPreference> layout)
    {
        var currentKeys =
            layout
                .Select(item =>
                    item.Key)
                .ToHashSet(
                    StringComparer.OrdinalIgnoreCase);
        var preserved =
            _unifiedInterface.DashboardLayouts
                .TryGetValue(
                    hostId,
                    out var stored)
                ? stored
                    .Where(item =>
                        !currentKeys.Contains(
                            item.Key))
                    .OrderBy(item =>
                        item.Order)
                    .Select(CloneDashboardPreference)
                : Enumerable.Empty<
                    DashboardCardPreference>();

        var persisted =
            layout
                .Select(CloneDashboardPreference)
                .Concat(
                    preserved)
                .ToList();

        for (var index = 0;
             index < persisted.Count;
             index++)
        {
            persisted[index].Order =
                index;
        }

        _unifiedInterface.DashboardLayouts[
                hostId] =
            persisted;
        _unifiedInterfaceStore?.Save(
            _unifiedInterface);
    }

    private sealed record DashboardCardLayoutState(
        string CardKey,
        int TotalRows,
        int VisibleRows,
        int HiddenRows);

    private static UnifiedDashboardRow[] DashboardCardPreviewRows(
        IReadOnlyList<UnifiedDashboardRow> source)
    {
        // The locked card contract is explicit: show every row when there are
        // four or fewer. Five or more records show the three highest-priority
        // rows plus a disclosure row. The complete set remains in the details
        // flyout and destination workspace.
        return source.Count <= 4
            ? source.ToArray()
            : source.Take(3).ToArray();
    }

    private Border BuildDashboardCardControl(
        UnifiedDashboardCard card,
        double width,
        bool applicationTile)
    {
        var compact =
            _unifiedInterface.Density.Equals(
                "Compact",
                StringComparison.OrdinalIgnoreCase);
        var kind =
            DashboardCardKind(
                card,
                applicationTile);

        var sourceRows =
            card.Rows
                .Where(row =>
                    !string.IsNullOrWhiteSpace(row.Label) ||
                    !string.IsNullOrWhiteSpace(row.Value) ||
                    !string.IsNullOrWhiteSpace(row.SecondaryValue))
                .ToArray();

        if (sourceRows.Length == 0)
        {
            sourceRows =
                card.Facts
                    .Where(value =>
                        !string.IsNullOrWhiteSpace(value))
                    .Distinct(
                        StringComparer.OrdinalIgnoreCase)
                    .Select(value =>
                        new UnifiedDashboardRow(
                            value,
                            string.Empty,
                            value,
                            OpsSeverity.Info))
                    .ToArray();
        }

        var visibleRows =
            DashboardCardPreviewRows(
                sourceRows);
        var hiddenRows =
            Math.Max(
                0,
                sourceRows.Length - visibleRows.Length);

        var border =
            new Border
            {
                MinHeight =
                    applicationTile
                        ? compact
                            ? 166
                            : 174
                        : compact
                            ? 184
                            : 194,
                HorizontalAlignment =
                    HorizontalAlignment.Stretch,
                VerticalAlignment =
                    VerticalAlignment.Stretch,
                Margin = new Thickness(0),
                ClipToBounds = false,
                Tag = new DashboardCardLayoutState(
                    card.Key,
                    sourceRows.Length,
                    visibleRows.Length,
                    hiddenRows),
                Classes =
                {
                    "dashboardProviderCard",
                    "dashboardCardShell"
                }
            };

        var grid =
            new Grid
            {
                RowDefinitions =
                    new RowDefinitions(
                        "Auto,Auto,Auto,*,Auto"),
                RowSpacing =
                    applicationTile
                        ? 5
                        : 6,
                HorizontalAlignment =
                    HorizontalAlignment.Stretch,
                VerticalAlignment =
                    VerticalAlignment.Stretch
            };

        var header =
            new Grid
            {
                ColumnDefinitions =
                    new ColumnDefinitions(
                        "*,Auto"),
                ColumnSpacing = 8,
                Classes =
                {
                    "dashboardCardHeader"
                }
            };
        var heading =
            new StackPanel
            {
                Spacing = 1
            };
        heading.Children.Add(
            new TextBlock
            {
                Text = card.Title,
                FontSize =
                    applicationTile
                        ? 13.5
                        : 15,
                FontWeight =
                    FontWeight.SemiBold,
                TextWrapping =
                    TextWrapping.Wrap
            });
        heading.Children.Add(
            new TextBlock
            {
                Text = card.Category.ToUpperInvariant(),
                Classes =
                {
                    "eyebrow"
                },
                FontSize = 8.5,
                TextWrapping =
                    TextWrapping.Wrap
            });
        header.Children.Add(
            heading);

        var badge =
            new Border
            {
                Classes =
                {
                    "badge"
                },
                Background =
                    OpsPalette.Background(
                        card.Severity),
                HorizontalAlignment =
                    HorizontalAlignment.Right,
                VerticalAlignment =
                    VerticalAlignment.Top
            };
        badge.Child =
            new TextBlock
            {
                Text = card.Status,
                Foreground =
                    OpsPalette.Foreground(
                        card.Severity),
                FontSize = 8.5,
                FontWeight =
                    FontWeight.SemiBold,
                TextWrapping =
                    TextWrapping.Wrap,
                TextAlignment =
                    TextAlignment.Center,
                MaxWidth = 112
            };
        Grid.SetColumn(
            badge,
            1);
        header.Children.Add(
            badge);
        grid.Children.Add(
            header);

        var primary =
            new TextBlock
            {
                Text = card.PrimaryValue,
                FontSize =
                    applicationTile
                        ? 20
                        : compact
                            ? 22
                            : 25,
                FontWeight =
                    FontWeight.SemiBold,
                TextWrapping =
                    TextWrapping.Wrap
            };
        Grid.SetRow(
            primary,
            1);
        grid.Children.Add(
            primary);

        var summary =
            new TextBlock
            {
                Text = card.Summary,
                IsVisible =
                    !string.IsNullOrWhiteSpace(
                        card.Summary),
                Classes =
                {
                    "muted",
                    "dashboardCardSummary"
                },
                FontSize =
                    applicationTile
                        ? 9
                        : 10,
                TextWrapping =
                    TextWrapping.Wrap
            };
        ToolTip.SetTip(
            summary,
            card.Detail);
        Grid.SetRow(
            summary,
            2);
        grid.Children.Add(
            summary);

        var contentPanel =
            new StackPanel
            {
                Spacing =
                    kind.Equals(
                        "progress",
                        StringComparison.Ordinal)
                        ? 5
                        : 3,
                VerticalAlignment =
                    VerticalAlignment.Top,
                HorizontalAlignment =
                    HorizontalAlignment.Stretch,
                ClipToBounds = false,
                Classes =
                {
                    "dashboardCardBody"
                }
            };

        foreach (var row in visibleRows)
        {
            contentPanel.Children.Add(
                BuildDashboardContractRowControl(
                    row,
                    kind));
        }

        if (hiddenRows > 0)
        {
            var overflow =
                new Grid
                {
                    ColumnDefinitions =
                        new ColumnDefinitions(
                            "*,Auto"),
                    ColumnSpacing = 8,
                    MinHeight = 16,
                    Classes =
                    {
                        "dashboardOverflowRow"
                    }
                };
            overflow.Children.Add(
                new TextBlock
                {
                    Text = $"+{hiddenRows} more",
                    Classes =
                    {
                        "dim"
                    },
                    FontSize = 9,
                    FontWeight =
                        FontWeight.SemiBold,
                    TextWrapping =
                        TextWrapping.Wrap
                });
            var disclosure =
                new TextBlock
                {
                    Text = "Open details",
                    Classes =
                    {
                        "dim"
                    },
                    FontSize = 8.5,
                    HorizontalAlignment =
                        HorizontalAlignment.Right,
                    TextWrapping =
                        TextWrapping.Wrap
                };
            Grid.SetColumn(
                disclosure,
                1);
            overflow.Children.Add(
                disclosure);
            contentPanel.Children.Add(
                overflow);
        }

        Grid.SetRow(
            contentPanel,
            3);
        grid.Children.Add(
            contentPanel);

        var footer =
            new Grid
            {
                ColumnDefinitions =
                    new ColumnDefinitions(
                        "*,Auto"),
                ColumnSpacing = 8,
                Margin =
                    new Thickness(
                        0,
                        6,
                        0,
                        0),
                VerticalAlignment =
                    VerticalAlignment.Bottom,
                Classes =
                {
                    "dashboardCardFooter"
                }
            };

        var actionsPanel =
            new StackPanel
            {
                Orientation =
                    Orientation.Horizontal,
                Spacing = 6,
                VerticalAlignment =
                    VerticalAlignment.Center
            };
        var actions =
            ResolveDashboardActions(
                card);
        var primaryAction =
            actions.FirstOrDefault(action =>
                action.IsPrimary) ??
            actions.FirstOrDefault();

        if (primaryAction is not null)
        {
            var primaryButton =
                new Button
                {
                    Content =
                        new TextBlock
                        {
                            Text = primaryAction.Label,
                            TextWrapping =
                                TextWrapping.Wrap,
                            TextAlignment =
                                TextAlignment.Center
                        },
                    Tag = primaryAction,
                    HorizontalAlignment =
                        HorizontalAlignment.Left,
                    Classes =
                    {
                        "compact",
                        "primary"
                    }
                };
            primaryButton.Click +=
                UnifiedDashboardActionButton_OnClick;
            actionsPanel.Children.Add(
                primaryButton);
        }

        footer.Children.Add(
            actionsPanel);

        var infoButton =
            new Button
            {
                Content =
                    BuildDashboardInfoIcon(),
                Classes =
                {
                    "dashboardInfoButton"
                },
                Flyout =
                    BuildDashboardInfoFlyout(
                        card,
                        width)
            };
        Avalonia.Automation.AutomationProperties.SetName(
            infoButton,
            $"{card.Title} details");
        Grid.SetColumn(
            infoButton,
            1);
        footer.Children.Add(
            infoButton);

        Grid.SetRow(
            footer,
            4);
        grid.Children.Add(
            footer);

        border.Child =
            grid;
        return border;
    }

    private static string DashboardCardKind(
        UnifiedDashboardCard card,
        bool applicationTile)
    {
        if (applicationTile)
            return "application";
        if (card.Key.Equals(
                "core:storage",
                StringComparison.OrdinalIgnoreCase) ||
            card.Key.Equals(
                "core:downloads",
                StringComparison.OrdinalIgnoreCase))
        {
            return "progress";
        }
        if (card.Key.Equals(
                "core:activity",
                StringComparison.OrdinalIgnoreCase))
        {
            return "timeline";
        }
        if (card.Key.Equals(
                "core:docker",
                StringComparison.OrdinalIgnoreCase) ||
            card.Key.Equals(
                "core:acquisition",
                StringComparison.OrdinalIgnoreCase) ||
            card.Key.Equals(
                "core:media",
                StringComparison.OrdinalIgnoreCase) ||
            card.Key.Equals(
                "core:health",
                StringComparison.OrdinalIgnoreCase))
        {
            return "status";
        }
        return "metric";
    }

    private Control BuildDashboardContractRowControl(
        UnifiedDashboardRow row,
        string kind)
    {
        var rowPanel =
            new StackPanel
            {
                Spacing = 2,
                HorizontalAlignment =
                    HorizontalAlignment.Stretch,
                ClipToBounds = false,
                Classes =
                {
                    "dashboardPreviewRow"
                }
            };
        var hasSecondary =
            !string.IsNullOrWhiteSpace(
                row.SecondaryValue);
        var line =
            new Grid
            {
                ColumnDefinitions =
                    new ColumnDefinitions(
                        hasSecondary
                            ? "Auto,1.4*,0.55*,0.8*"
                            : "Auto,1.5*,1*"),
                ColumnSpacing = 6,
                MinHeight = 16,
                HorizontalAlignment =
                    HorizontalAlignment.Stretch
            };
        line.Children.Add(
            new TextBlock
            {
                Text = "●",
                FontSize = 7,
                Foreground =
                    OpsPalette.Foreground(
                        row.Severity ==
                        OpsSeverity.Healthy
                            ? OpsSeverity.Info
                            : row.Severity),
                VerticalAlignment =
                    VerticalAlignment.Center
            });

        var labelText =
            kind.Equals(
                    "timeline",
                    StringComparison.Ordinal) &&
                !string.IsNullOrWhiteSpace(
                    row.Detail)
                ? row.Detail
                    .Split(
                        " · ",
                        2,
                        StringSplitOptions.TrimEntries)[0]
                : row.Label;
        var label =
            new TextBlock
            {
                Text = labelText,
                Classes =
                {
                    "dim"
                },
                FontSize = 9,
                TextWrapping =
                    TextWrapping.Wrap,
                VerticalAlignment =
                    VerticalAlignment.Center
            };
        Grid.SetColumn(
            label,
            1);
        line.Children.Add(
            label);

        var value =
            new TextBlock
            {
                Text = row.Value,
                FontSize = 9,
                FontWeight =
                    FontWeight.SemiBold,
                HorizontalAlignment =
                    HorizontalAlignment.Stretch,
                VerticalAlignment =
                    VerticalAlignment.Center,
                TextAlignment =
                    TextAlignment.Right,
                TextWrapping =
                    TextWrapping.Wrap
            };
        Grid.SetColumn(
            value,
            2);
        line.Children.Add(
            value);

        if (hasSecondary)
        {
            var secondary =
                new TextBlock
                {
                    Text = row.SecondaryValue,
                    Classes =
                    {
                        "dim"
                    },
                    FontSize = 8.75,
                    FontWeight =
                        FontWeight.SemiBold,
                    HorizontalAlignment =
                        HorizontalAlignment.Stretch,
                    VerticalAlignment =
                        VerticalAlignment.Center,
                    TextAlignment =
                        TextAlignment.Right,
                    TextWrapping =
                        TextWrapping.Wrap
                };
            Grid.SetColumn(
                secondary,
                3);
            line.Children.Add(
                secondary);
        }

        rowPanel.Children.Add(
            line);

        if (kind.Equals(
                "progress",
                StringComparison.Ordinal) &&
            TryDashboardProgress(
                row.Value,
                out var progress))
        {
            rowPanel.Children.Add(
                new ProgressBar
                {
                    Minimum = 0,
                    Maximum = 100,
                    Value = progress,
                    Height = 5,
                    IsHitTestVisible = false,
                    Foreground =
                        OpsPalette.Foreground(
                            row.Severity ==
                            OpsSeverity.Healthy
                                ? OpsSeverity.Info
                                : row.Severity)
                });
        }

        ToolTip.SetTip(
            rowPanel,
            string.IsNullOrWhiteSpace(
                row.Detail)
                ? string.Join(
                    " · ",
                    new[]
                    {
                        row.Label,
                        row.Value,
                        row.SecondaryValue
                    }.Where(value =>
                        !string.IsNullOrWhiteSpace(value)))
                : row.Detail);
        return rowPanel;
    }

    private static bool TryDashboardProgress(
        string value,
        out double progress)
    {
        progress = 0;
        var match =
            System.Text.RegularExpressions.Regex.Match(
                value ?? string.Empty,
                @"(?<value>\d+(?:\.\d+)?)%",
                System.Text.RegularExpressions.RegexOptions.CultureInvariant);
        return match.Success &&
               double.TryParse(
                   match.Groups["value"].Value,
                   NumberStyles.Float,
                   CultureInfo.InvariantCulture,
                   out progress);
    }

    private IReadOnlyList<UnifiedDashboardAction>
        ResolveDashboardActions(
            UnifiedDashboardCard card)
    {
        var actions =
            card.Actions.Count > 0
                ? card.Actions
                : new[]
                {
                    new UnifiedDashboardAction(
                        card.ActionLabel,
                        card.NavigationName,
                        card.Endpoint,
                        IsPrimary: true)
                };

        return actions
            .Where(action =>
                !string.IsNullOrWhiteSpace(
                    action.Label) &&
                (!string.IsNullOrWhiteSpace(
                     action.NavigationName) ||
                 !string.IsNullOrWhiteSpace(
                     action.Endpoint)))
            .GroupBy(
                action =>
                    !string.IsNullOrWhiteSpace(
                        action.Endpoint)
                        ? $"endpoint:{action.Endpoint}"
                        : $"navigation:{action.NavigationName}",
                StringComparer.OrdinalIgnoreCase)
            .Select(group =>
                group.First())
            .ToArray();
    }

    private void UnifiedDashboardActionButton_OnClick(
        object? sender,
        RoutedEventArgs e)
    {
        if (sender is not Button
            {
                Tag: UnifiedDashboardAction action
            })
        {
            return;
        }

        Get<Border>(
                "UnifiedDetailsDrawer")
            .IsVisible =
            false;

        if (!string.IsNullOrWhiteSpace(
                action.Endpoint))
        {
            try
            {
                Process.Start(
                    new ProcessStartInfo
                    {
                        FileName =
                            action.Endpoint,
                        UseShellExecute =
                            true
                    });
            }
            catch
            {
                OpenUnifiedDetails(
                    "Could not open interface",
                    action.Label,
                    action.Endpoint);
            }

            return;
        }

        if (action.NavigationName.StartsWith(
                "@integration:",
                StringComparison.OrdinalIgnoreCase))
        {
            var product =
                action.NavigationName[
                    "@integration:".Length..]
                    .Trim();
            Navigate(
                "MediaHubNav");
            SelectIntegrationByName(
                product);
            return;
        }

        switch (action.NavigationName)
        {
            case "@activity":
                ActivityButton_OnClick(
                    sender,
                    e);
                break;

            case "@jobs":
                JobsButton_OnClick(
                    sender,
                    e);
                break;

            case "@terminal":
                TerminalButton_OnClick(
                    sender,
                    e);
                break;

            case "@logs":
                OpenDashboardLogContext(
                    action);
                break;

            case "@refresh":
                _ = RunCoordinatedRefreshAsync(
                    background: false);
                break;

            case var remediation when remediation.StartsWith(
                "@remediate:",
                StringComparison.OrdinalIgnoreCase):
                OpenVerifiedRemediation(
                    action,
                    sender as Control);
                break;

            default:
                Navigate(
                    action.NavigationName);
                break;
        }
    }

    private void OpenDashboardLogContext(
        UnifiedDashboardAction action)
    {
        _dashboardLogContextApplying =
            true;

        try
        {
            EnsureLogReliabilityControls();
            Navigate(
                "LogsNav");
            Get<ComboBox>(
                    "LogsSeverityFilterComboBox")
                .SelectedIndex = 0;
            Get<ComboBox>(
                    "LogsTimeFilterComboBox")
                .SelectedIndex = 1;
            Get<CheckBox>(
                    "ShowInformationalLogsCheckBox")
                .IsChecked =
                action.IncludeInformationalLogs;
            Get<TextBox>(
                    "LogsSourceFilterText")
                .Text =
                string.Empty;
            Get<TextBox>(
                    "LogsTextFilterText")
                .Text =
                string.Empty;
        }
        catch
        {
            _dashboardLogContextApplying =
                false;
            throw;
        }

        _dashboardLogContextLabel =
            string.IsNullOrWhiteSpace(
                action.LogContext)
                ? "Dashboard card"
                : action.LogContext;
        _dashboardLogAliases =
            SplitDashboardLogAliases(
                    action.LogSource)
                .Concat(
                    SplitDashboardLogAliases(
                        action.LogText))
                .Distinct(
                    StringComparer.OrdinalIgnoreCase)
                .ToArray();
        _dashboardLogContextActive =
            true;

        ApplyReliableLogsFilter();

        Avalonia.Threading.Dispatcher.UIThread.Post(
            () =>
            {
                try
                {
                    if (_dashboardLogContextActive)
                    {
                        ApplyReliableLogsFilter();
                    }
                }
                finally
                {
                    _dashboardLogContextApplying =
                        false;
                }
            });
    }

    private static IReadOnlyList<string>
        SplitDashboardLogAliases(
            string value) =>
        (value ?? string.Empty)
            .Split(
                '|',
                StringSplitOptions.RemoveEmptyEntries |
                StringSplitOptions.TrimEntries)
            .Where(alias =>
                !string.IsNullOrWhiteSpace(
                    alias))
            .Distinct(
                StringComparer.OrdinalIgnoreCase)
            .ToArray();

    private void ApplyDashboardLogContextProjection()
    {
        if (!_dashboardLogContextActive)
            return;

        var rows =
            _dashboardLogAliases.Length == 0
                ? _reliableLogRows
                : _reliableLogRows
                    .Where(row =>
                        _dashboardLogAliases.Any(alias =>
                            row.Source.Contains(
                                alias,
                                StringComparison.OrdinalIgnoreCase) ||
                            row.Message.Contains(
                                alias,
                                StringComparison.OrdinalIgnoreCase)))
                    .ToArray();

        _reliableLogRows =
            rows;

        var list =
            Get<ListBox>(
                "LogsList");
        list.ItemsSource =
            _reliableLogRows;

        Get<TextBlock>(
                "LogsVisibleMetricText")
            .Text =
            _reliableLogRows.Count.ToString(
                CultureInfo.InvariantCulture);
        Get<TextBlock>(
                "LogsSourceMetricText")
            .Text =
            _reliableLogRows
                .Select(row =>
                    row.Source)
                .Distinct(
                    StringComparer.OrdinalIgnoreCase)
                .Count()
                .ToString(
                    CultureInfo.InvariantCulture);

        var aliasSummary =
            _dashboardLogAliases.Length == 0
                ? "all warning and error journal groups"
                : string.Join(
                    ", ",
                    _dashboardLogAliases.Take(6));

        var summary =
            $"Dashboard context · {_dashboardLogContextLabel} · " +
            $"{_reliableLogRows.Count} matching group(s) · " +
            aliasSummary;

        Get<TextBlock>(
                "LogsSummaryText")
            .Text =
            summary;
        Get<TextBlock>(
                "LogsFilterStatusText")
            .Text =
            summary;

        var empty =
            _reliableLogRows.Count == 0;
        Get<Border>(
                "LogsEmptyState")
            .IsVisible =
            empty;

        if (empty)
        {
            Get<TextBlock>(
                    "LogsEmptyTitleText")
                .Text =
                $"No retained logs match {_dashboardLogContextLabel}";
            Get<TextBlock>(
                    "LogsEmptyDetailText")
                .Text =
                "The Dashboard context was applied successfully, but no retained journal group matched its source or message aliases.";
            list.SelectedItem =
                null;
        }
        else
        {
            list.SelectedIndex =
                0;
        }

        PopulateReliableLogSelection();
    }

    private void ClearDashboardLogContext()
    {
        if (_dashboardLogContextApplying)
            return;

        _dashboardLogContextActive =
            false;
        _dashboardLogContextLabel =
            string.Empty;
        _dashboardLogAliases =
            Array.Empty<string>();
    }

    private static PathIcon BuildDashboardInfoIcon() =>
        new()
        {
            Width = 16,
            Height = 16,
            HorizontalAlignment =
                HorizontalAlignment.Center,
            VerticalAlignment =
                VerticalAlignment.Center,
            Data =
                Avalonia.Media.StreamGeometry.Parse(
                    "M8.49902 7.49998C8.49902 7.22384 8.27517 6.99998 7.99902 6.99998C7.72288 6.99998 7.49902 7.22384 7.49902 7.49998V10.5C7.49902 10.7761 7.72288 11 7.99902 11C8.27517 11 8.49902 10.7761 8.49902 10.5V7.49998ZM8.74807 5.50001C8.74807 5.91369 8.41271 6.24905 7.99903 6.24905C7.58535 6.24905 7.25 5.91369 7.25 5.50001C7.25 5.08633 7.58535 4.75098 7.99903 4.75098C8.41271 4.75098 8.74807 5.08633 8.74807 5.50001ZM8 1C4.13401 1 1 4.13401 1 8C1 11.866 4.13401 15 8 15C11.866 15 15 11.866 15 8C15 4.13401 11.866 1 8 1ZM2 8C2 4.68629 4.68629 2 8 2C11.3137 2 14 4.68629 14 8C14 11.3137 11.3137 14 8 14C4.68629 14 2 11.3137 2 8Z")
        };

    private Flyout BuildDashboardInfoFlyout(
        UnifiedDashboardCard card,
        double cardWidth)
    {
        var actions =
            ResolveDashboardActions(
                card);
        var rows =
            card.Rows
                .Where(row =>
                    !string.IsNullOrWhiteSpace(
                        row.Label) ||
                    !string.IsNullOrWhiteSpace(
                        row.Value) ||
                    !string.IsNullOrWhiteSpace(
                        row.Detail))
                .ToArray();
        var facts =
            card.Facts
                .Where(value =>
                    !string.IsNullOrWhiteSpace(
                        value))
                .Distinct(
                    StringComparer.OrdinalIgnoreCase)
                .ToArray();
        var detailLines =
            DashboardDetailLines(
                card.Detail);
        var dense =
            detailLines.Count > 1 ||
            rows.Length > 2 ||
            facts.Length > 3 ||
            card.Key.Equals(
                "core:storage",
                StringComparison.OrdinalIgnoreCase) ||
            card.Key.Equals(
                "core:docker",
                StringComparison.OrdinalIgnoreCase) ||
            card.Key.Equals(
                "core:activity",
                StringComparison.OrdinalIgnoreCase) ||
            card.Key.Equals(
                "core:backups",
                StringComparison.OrdinalIgnoreCase);
        var flyoutWidth =
            dense
                ? Math.Clamp(
                    Math.Max(
                        cardWidth * 1.42,
                        440),
                    420,
                    620)
                : Math.Clamp(
                    Math.Max(
                        cardWidth,
                        320),
                    300,
                    460);

        var flyout =
            new Flyout
            {
                Placement =
                    PlacementMode.TopEdgeAlignedRight,
                ShowMode =
                    FlyoutShowMode.Standard,
                VerticalOffset = -4
            };
        flyout.FlyoutPresenterClasses.Add(
            "dashboardInfoFlyout");

        var root =
            new Grid
            {
                Width = flyoutWidth,
                MaxWidth = flyoutWidth,
                ClipToBounds = true,
                RowDefinitions =
                    new RowDefinitions(
                        "Auto,*,Auto")
            };

        var headerContent =
            new StackPanel
            {
                Spacing = 3
            };
        headerContent.Children.Add(
            new TextBlock
            {
                Text = card.Title,
                FontSize = 16,
                FontWeight =
                    FontWeight.SemiBold,
                TextWrapping =
                    TextWrapping.Wrap
            });
        headerContent.Children.Add(
            new TextBlock
            {
                Text =
                    $"{card.Category} · {card.Status}",
                Classes =
                {
                    "eyebrow"
                }
            });

        var header =
            new Border
            {
                Classes =
                {
                    "dashboardInfoHeader"
                },
                Child = headerContent
            };
        root.Children.Add(
            header);

        var bodyStack =
            new StackPanel
            {
                Spacing = 8,
                HorizontalAlignment =
                    HorizontalAlignment.Stretch
            };

        if (!string.IsNullOrWhiteSpace(
                card.Summary))
        {
            bodyStack.Children.Add(
                new TextBlock
                {
                    Text = card.Summary,
                    Classes =
                    {
                        "muted"
                    },
                    FontSize = 10.5,
                    TextWrapping =
                        TextWrapping.Wrap
                });
        }

        var renderedStructuredDetail = false;

        if (detailLines.Count > 1)
        {
            bodyStack.Children.Add(
                BuildDashboardInfoSectionLabel(
                    "Details"));

            foreach (var line in detailLines)
            {
                bodyStack.Children.Add(
                    BuildDashboardDetailLineControl(
                        line));
            }

            renderedStructuredDetail = true;
        }
        else if (rows.Length > 0)
        {
            bodyStack.Children.Add(
                BuildDashboardInfoSectionLabel(
                    "Details"));

            foreach (var row in rows)
            {
                bodyStack.Children.Add(
                    BuildDashboardInfoRowControl(
                        row));
            }

            renderedStructuredDetail = true;
        }
        else if (facts.Length > 0)
        {
            bodyStack.Children.Add(
                BuildDashboardInfoSectionLabel(
                    "Details"));

            foreach (var fact in facts)
            {
                bodyStack.Children.Add(
                    BuildDashboardDetailLineControl(
                        fact));
            }

            renderedStructuredDetail = true;
        }

        if (!renderedStructuredDetail &&
            detailLines.Count == 1)
        {
            bodyStack.Children.Add(
                BuildDashboardDetailLineControl(
                    detailLines[0]));
        }
        else if (renderedStructuredDetail &&
                 detailLines.Count == 1 &&
                 !string.Equals(
                     detailLines[0],
                     card.Summary,
                     StringComparison.OrdinalIgnoreCase) &&
                 !rows.Any(row =>
                     string.Equals(
                         row.Detail,
                         detailLines[0],
                         StringComparison.OrdinalIgnoreCase)))
        {
            bodyStack.Children.Add(
                BuildDashboardInfoSectionLabel(
                    "Context"));
            bodyStack.Children.Add(
                BuildDashboardDetailLineControl(
                    detailLines[0]));
        }

        var body =
            new Border
            {
                Classes =
                {
                    "dashboardInfoBody"
                },
                Child =
                    new ScrollViewer
                    {
                        MaxHeight =
                            dense
                                ? 430
                                : 340,
                        VerticalScrollBarVisibility =
                            Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
                        HorizontalScrollBarVisibility =
                            Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled,
                        Content = bodyStack
                    }
            };
        Grid.SetRow(
            body,
            1);
        root.Children.Add(
            body);

        if (actions.Count > 0)
        {
            var actionPanel =
                new WrapPanel();

            foreach (var action in actions)
            {
                var button =
                    new Button
                    {
                        Content = action.Label,
                        Tag = action,
                        Classes =
                        {
                            "compact"
                        },
                        Margin =
                            new Thickness(
                                0,
                                0,
                                6,
                                6)
                    };
                button.Classes.Set(
                    "primary",
                    action.IsPrimary);
                button.Click +=
                    UnifiedDashboardActionButton_OnClick;
                button.Click +=
                    (_, _) =>
                        flyout.Hide();
                actionPanel.Children.Add(
                    button);
            }

            var footer =
                new Border
                {
                    Classes =
                    {
                        "dashboardInfoFooter"
                    },
                    Child = actionPanel
                };
            Grid.SetRow(
                footer,
                2);
            root.Children.Add(
                footer);
        }

        flyout.Content = root;
        return flyout;
    }

    private static TextBlock BuildDashboardInfoSectionLabel(
        string text) =>
        new()
        {
            Text = text.ToUpperInvariant(),
            Classes =
            {
                "eyebrow"
            },
            Margin =
                new Thickness(
                    0,
                    3,
                    0,
                    0)
        };

    private Control BuildDashboardInfoRowControl(
        UnifiedDashboardRow row) =>
        BuildDashboardInfoDataRow(
            row.Label,
            row.Value,
            row.Detail,
            row.Severity);

    private Control BuildDashboardDetailLineControl(
        string line)
    {
        var parts =
            (line ?? string.Empty)
                .Split(
                    " · ",
                    StringSplitOptions.RemoveEmptyEntries |
                    StringSplitOptions.TrimEntries);

        return BuildDashboardInfoDataRow(
            parts.FirstOrDefault() ?? string.Empty,
            parts.Length > 1
                ? parts[1]
                : string.Empty,
            parts.Length > 2
                ? string.Join(
                    " · ",
                    parts.Skip(2))
                : string.Empty,
            OpsSeverity.Info);
    }

    private Control BuildDashboardInfoDataRow(
        string label,
        string value,
        string detail,
        OpsSeverity severity)
    {
        var content =
            new StackPanel
            {
                Spacing = 7,
                HorizontalAlignment =
                    HorizontalAlignment.Stretch
            };
        var line =
            new Grid
            {
                ColumnDefinitions =
                    new ColumnDefinitions(
                        "*,Auto"),
                ColumnSpacing = 12,
                HorizontalAlignment =
                    HorizontalAlignment.Stretch
            };

        var labelTechnical =
            DashboardInfoTechnicalText(
                label);
        var labelBlock =
            new TextBlock
            {
                Text =
                    labelTechnical
                        ? DashboardInfoBreakableText(
                            label)
                        : label,
                Classes =
                {
                    "dashboardInfoLabel"
                },
                MinWidth = 0,
                TextWrapping =
                    TextWrapping.Wrap,
                TextTrimming =
                    TextTrimming.CharacterEllipsis
            };
        if (labelTechnical)
        {
            labelBlock.Classes.Add(
                "dashboardInfoTechnical");
        }
        line.Children.Add(
            labelBlock);

        if (!string.IsNullOrWhiteSpace(
                value))
        {
            var valueBlock =
                new TextBlock
                {
                    Text = value,
                    Classes =
                    {
                        "dashboardInfoValue"
                    },
                    HorizontalAlignment =
                        HorizontalAlignment.Right,
                    TextAlignment =
                        TextAlignment.Right,
                    TextWrapping =
                        TextWrapping.Wrap,
                    MaxWidth = 170
                };
            Grid.SetColumn(
                valueBlock,
                1);
            line.Children.Add(
                valueBlock);
        }

        content.Children.Add(
            line);

        var metadata =
            DashboardInfoMetadataSegments(
                detail,
                label,
                value);

        if (metadata.Count > 1)
        {
            var metadataPanel =
                new WrapPanel
                {
                    HorizontalAlignment =
                        HorizontalAlignment.Stretch
                };

            foreach (var segment in metadata)
            {
                metadataPanel.Children.Add(
                    BuildDashboardInfoMetadataToken(
                        segment));
            }

            content.Children.Add(
                metadataPanel);
        }
        else if (metadata.Count == 1)
        {
            var segment =
                metadata[0];
            var technical =
                DashboardInfoTechnicalText(
                    segment);
            var detailBlock =
                new TextBlock
                {
                    Text =
                        technical
                            ? DashboardInfoBreakableText(
                                segment)
                            : segment,
                    Classes =
                    {
                        "muted"
                    },
                    FontSize = 9.5,
                    LineHeight = 14,
                    MaxWidth = 500,
                    TextWrapping =
                        TextWrapping.Wrap
                };
            if (technical)
            {
                detailBlock.Classes.Add(
                    "dashboardInfoTechnical");
            }
            content.Children.Add(
                detailBlock);
        }

        if (TryDashboardProgress(
                value,
                out var progress))
        {
            content.Children.Add(
                new ProgressBar
                {
                    Minimum = 0,
                    Maximum = 100,
                    Value = progress,
                    Height = 5,
                    IsHitTestVisible = false,
                    Foreground =
                        OpsPalette.Foreground(
                            severity ==
                            OpsSeverity.Healthy
                                ? OpsSeverity.Info
                                : severity)
                });
        }

        return new Border
        {
            Classes =
            {
                "dashboardInfoDataRow"
            },
            HorizontalAlignment =
                HorizontalAlignment.Stretch,
            Child = content
        };
    }

    private static Border BuildDashboardInfoMetadataToken(
        string value)
    {
        var technical =
            DashboardInfoTechnicalText(
                value);
        var text =
            new TextBlock
            {
                Text =
                    technical
                        ? DashboardInfoBreakableText(
                            value)
                        : value,
                Classes =
                {
                    "dashboardInfoMetaText"
                },
                MaxWidth = 250,
                TextWrapping =
                    TextWrapping.Wrap
            };
        if (technical)
        {
            text.Classes.Add(
                "dashboardInfoTechnical");
        }

        return new Border
        {
            Classes =
            {
                "dashboardInfoMetaToken"
            },
            Margin =
                new Thickness(
                    0,
                    0,
                    6,
                    6),
            Child = text
        };
    }

    private static IReadOnlyList<string>
        DashboardInfoMetadataSegments(
            string detail,
            string label,
            string value) =>
        (detail ?? string.Empty)
            .Split(
                " · ",
                StringSplitOptions.RemoveEmptyEntries |
                StringSplitOptions.TrimEntries)
            .Where(segment =>
                !string.IsNullOrWhiteSpace(
                    segment) &&
                !string.Equals(
                    segment,
                    label,
                    StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(
                    segment,
                    value,
                    StringComparison.OrdinalIgnoreCase))
            .Distinct(
                StringComparer.OrdinalIgnoreCase)
            .ToArray();

    private static string DashboardInfoBreakableText(
        string value) =>
        (value ?? string.Empty)
            .Replace(
                "/",
                "/\u200B")
            .Replace(
                "\\",
                "\\\u200B")
            .Replace(
                ":",
                ":\u200B")
            .Replace(
                "_",
                "_\u200B")
            .Replace(
                "-",
                "-\u200B");

    private static IReadOnlyList<string>
        DashboardDetailLines(
            string detail) =>
        (detail ?? string.Empty)
            .Split(
                new[]
                {
                    '\r',
                    '\n'
                },
                StringSplitOptions.RemoveEmptyEntries |
                StringSplitOptions.TrimEntries)
            .Where(line =>
                !string.IsNullOrWhiteSpace(
                    line))
            .Distinct(
                StringComparer.OrdinalIgnoreCase)
            .ToArray();

    private static bool DashboardInfoTechnicalText(
        string value) =>
        !string.IsNullOrWhiteSpace(
            value) &&
        (value.Contains(
             "/",
             StringComparison.Ordinal) ||
         value.Contains(
             "\\",
             StringComparison.Ordinal) ||
         value.Contains(
             ".service",
             StringComparison.OrdinalIgnoreCase) ||
         value.Contains(
             "/dev/",
             StringComparison.OrdinalIgnoreCase) ||
         value.Contains(
             "://",
             StringComparison.OrdinalIgnoreCase));

    private void DashboardCustomizeCardsButton_OnClick(
        object? sender,
        RoutedEventArgs e)
    {
        PopulateDashboardCardPicker();
        Get<Border>(
                "DashboardCustomizerPanel")
            .IsVisible =
            true;
    }

    private void PopulateDashboardCardPicker()
    {
        var panel =
            Get<StackPanel>(
                "DashboardCardPickerPanel");
        panel.Children.Clear();
        _dashboardPickerChecks.Clear();

        var layout =
            ResolveDashboardLayout(
                _unifiedDashboardCards)
                .OrderBy(item =>
                    item.Order)
                .ToArray();

        for (var index = 0;
             index < layout.Length;
             index++)
        {
            var preference =
                layout[index];
            var card =
                _unifiedDashboardCards
                    .First(item =>
                        item.Key.Equals(
                            preference.Key,
                            StringComparison.OrdinalIgnoreCase));

            var row =
                new Grid
                {
                    ColumnDefinitions =
                        new ColumnDefinitions(
                            "*,Auto,Auto"),
                    MinHeight = 54,
                    Margin =
                        new Thickness(
                            0,
                            0,
                            0,
                            7)
                };

            var label =
                new StackPanel
                {
                    Spacing = 2
                };
            label.Children.Add(
                new TextBlock
                {
                    Text =
                        $"{card.Title} · {card.Category}",
                    FontWeight =
                        FontWeight.SemiBold,
                    TextWrapping =
                        TextWrapping.Wrap
                });
            label.Children.Add(
                new TextBlock
                {
                    Text =
                        $"{card.Summary} · " +
                        $"{ResolveDashboardActions(card).Count} shortcut(s)",
                    Classes =
                    {
                        "dim"
                    },
                    FontSize = 9.5,
                    TextWrapping =
                        TextWrapping.Wrap,
                    MaxHeight = 30
                });

            var check =
                new CheckBox
                {
                    Content = label,
                    IsChecked =
                        preference.IsVisible,
                    Tag =
                        preference.Key,
                    VerticalAlignment =
                        VerticalAlignment.Center
                };
            ToolTip.SetTip(
                check,
                card.Detail);
            row.Children.Add(
                check);
            _dashboardPickerChecks[
                    preference.Key] =
                check;

            var up =
                new Button
                {
                    Content = "↑",
                    Tag =
                        $"{preference.Key}|-1",
                    Width = 34,
                    IsEnabled =
                        index > 0,
                    Classes =
                    {
                        "compact"
                    },
                    Margin =
                        new Thickness(
                            5,
                            0,
                            0,
                            0)
                };
            ToolTip.SetTip(
                up,
                "Move card earlier");
            up.Click +=
                DashboardCardMoveButton_OnClick;
            Grid.SetColumn(
                up,
                1);
            row.Children.Add(
                up);

            var down =
                new Button
                {
                    Content = "↓",
                    Tag =
                        $"{preference.Key}|1",
                    Width = 34,
                    IsEnabled =
                        index <
                        layout.Length - 1,
                    Classes =
                    {
                        "compact"
                    },
                    Margin =
                        new Thickness(
                            5,
                            0,
                            0,
                            0)
                };
            ToolTip.SetTip(
                down,
                "Move card later");
            down.Click +=
                DashboardCardMoveButton_OnClick;
            Grid.SetColumn(
                down,
                2);
            row.Children.Add(
                down);

            panel.Children.Add(
                row);
        }
    }

    private void DashboardCardMoveButton_OnClick(
        object? sender,
        RoutedEventArgs e)
    {
        if (sender is not Button
            {
                Tag: string tag
            })
        {
            return;
        }

        var separator =
            tag.LastIndexOf('|');

        if (separator < 1 ||
            !int.TryParse(
                tag[(separator + 1)..],
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out var delta))
        {
            return;
        }

        var key =
            tag[..separator];
        var hostId =
            _controlPlane.ActiveProfile.Id;
        var layout =
            ResolveDashboardLayout(
                _unifiedDashboardCards);
        var currentIndex =
            layout.FindIndex(item =>
                item.Key.Equals(
                    key,
                    StringComparison.OrdinalIgnoreCase));

        if (currentIndex < 0)
            return;

        var targetIndex =
            Math.Clamp(
                currentIndex + delta,
                0,
                layout.Count - 1);

        if (targetIndex ==
            currentIndex)
        {
            return;
        }

        var current =
            layout[currentIndex];
        layout.RemoveAt(
            currentIndex);
        layout.Insert(
            targetIndex,
            current);

        for (var index = 0;
             index < layout.Count;
             index++)
        {
            layout[index].Order =
                index;
            if (_dashboardPickerChecks
                    .TryGetValue(
                        layout[index].Key,
                        out var check))
            {
                layout[index].IsVisible =
                    check.IsChecked == true;
            }
        }

        PersistDashboardLayout(
            hostId,
            layout);
        PopulateDashboardCardPicker();
    }

    private void DashboardSaveCardsButton_OnClick(
        object? sender,
        RoutedEventArgs e)
    {
        var hostId =
            _controlPlane.ActiveProfile.Id;
        var layout =
            ResolveDashboardLayout(
                _unifiedDashboardCards);

        foreach (var preference in layout)
        {
            if (_dashboardPickerChecks
                    .TryGetValue(
                        preference.Key,
                        out var check))
            {
                preference.IsVisible =
                    check.IsChecked == true;
                preference.VisibilityExplicit =
                    true;
            }
        }

        PersistDashboardLayout(
            hostId,
            layout);

        PopulateUnifiedDashboard();
        CloseDashboardCustomizer();
    }

    private void DashboardResetCardsButton_OnClick(
        object? sender,
        RoutedEventArgs e) =>
        ResetDashboardLayoutButton_OnClick(
            sender,
            e);

    private void DashboardCloseCustomizerButton_OnClick(
        object? sender,
        RoutedEventArgs e) =>
        CloseDashboardCustomizer();

    private void CloseDashboardCustomizer() =>
        Get<Border>(
                "DashboardCustomizerPanel")
            .IsVisible =
            false;

    private void UnifiedDashboardOpenAttentionButton_OnClick(
        object? sender,
        RoutedEventArgs e) =>
        Navigate(
            "IntelligenceNav");

    private void CloseUnifiedOverlays()
    {
        Get<Border>("UnifiedDetailsDrawer").IsVisible = false;
        Get<Border>("DashboardCustomizerPanel").IsVisible = false;

        if (_unifiedInterface.SetupCompleted)
            Get<Grid>("ExpressSetupOverlay").IsVisible = false;
    }

    private void OpenUnifiedDetails(
        string title,
        string subtitle,
        string detail)
    {
        Get<StackPanel>(
                "UnifiedDetailsActionsPanel")
            .Children
            .Clear();
        Get<TextBlock>(
                "UnifiedDetailsTitleText")
            .Text =
            title;
        Get<TextBlock>(
                "UnifiedDetailsSubtitleText")
            .Text =
            subtitle;
        Get<TextBox>(
                "UnifiedDetailsText")
            .Text =
            detail;
        Get<Border>(
                "UnifiedDetailsDrawer")
            .IsVisible =
            true;
    }

    private void UnifiedDetailsCloseButton_OnClick(
        object? sender,
        RoutedEventArgs e) =>
        Get<Border>(
                "UnifiedDetailsDrawer")
            .IsVisible =
            false;

    private async void UnifiedDetailsCopyButton_OnClick(
        object? sender,
        RoutedEventArgs e)
    {
        var clipboard =
            TopLevel.GetTopLevel(
                this)?.Clipboard;

        if (clipboard is null)
            return;

        await clipboard.SetTextAsync(
            Get<TextBox>(
                    "UnifiedDetailsText")
                .Text ??
            string.Empty);
    }

    private void OpenExpressSetupButton_OnClick(
        object? sender,
        RoutedEventArgs e)
    {
        _expressSetupStep =
            0;
        RenderExpressSetupStep();
        Get<Grid>(
                "ExpressSetupOverlay")
            .IsVisible =
            true;
    }

    private void ShowExpressSetupIfNeeded()
    {
        if (_unifiedInterface.SetupCompleted)
            return;

        _expressSetupStep =
            0;
        RenderExpressSetupStep();
        Get<Grid>(
                "ExpressSetupOverlay")
            .IsVisible =
            true;
    }

    private void RenderExpressSetupStep()
    {
        for (var index = 0;
             index < 4;
             index++)
        {
            Get<Control>(
                    $"SetupStep{index + 1}Panel")
                .IsVisible =
                index == _expressSetupStep;
        }

        Get<TextBlock>(
                "SetupProgressText")
            .Text =
            $"Step {_expressSetupStep + 1} of 4";
        Get<Button>(
                "SetupBackButton")
            .IsEnabled =
            _expressSetupStep > 0;
        Get<Button>(
                "SetupNextButton")
            .IsVisible =
            _expressSetupStep < 3;
        Get<Button>(
                "SetupFinishButton")
            .IsVisible =
            _expressSetupStep == 3;

        if (_expressSetupStep == 2)
            PopulateSetupDiscoveryPreview();

        if (_expressSetupStep == 3)
            PopulateSetupReview();
    }

    private void SetupNextButton_OnClick(
        object? sender,
        RoutedEventArgs e)
    {
        _expressSetupStep =
            Math.Min(
                3,
                _expressSetupStep + 1);
        RenderExpressSetupStep();
    }

    private void SetupBackButton_OnClick(
        object? sender,
        RoutedEventArgs e)
    {
        _expressSetupStep =
            Math.Max(
                0,
                _expressSetupStep - 1);
        RenderExpressSetupStep();
    }

    private void SetupSkipButton_OnClick(
        object? sender,
        RoutedEventArgs e)
    {
        _unifiedInterface.SetupCompleted =
            true;
        _unifiedInterfaceStore?.Save(
            _unifiedInterface);
        Get<Grid>(
                "ExpressSetupOverlay")
            .IsVisible =
            false;
    }

    private void SetupFinishButton_OnClick(
        object? sender,
        RoutedEventArgs e)
    {
        var mode =
            Get<ComboBox>("SetupModeComboBox")
                .SelectedItem as string ??
            SetupModes[0];
        var theme =
            Get<ComboBox>("SetupThemeComboBox")
                .SelectedItem as string ??
            LinuxThemeCatalog.All[0].Name;
        var density =
            Get<ComboBox>("SetupDensityComboBox")
                .SelectedItem as string ??
            "Compact";

        _unifiedInterface.SetupMode =
            mode;
        _unifiedInterface.SetupCompleted =
            true;
        ApplyUnifiedTheme(
            theme);
        ApplyUnifiedDensity(
            density);

        Get<CheckBox>(
                "SettingsSafeModeCheckBox")
            .IsChecked =
            Get<CheckBox>(
                    "SetupSafeModeCheckBox")
                .IsChecked;
        Get<CheckBox>(
                "SettingsDesktopNotificationsCheckBox")
            .IsChecked =
            Get<CheckBox>(
                    "SetupNotificationsCheckBox")
                .IsChecked;

        SaveOperatorSettingsButton_OnClick(
            null,
            new RoutedEventArgs());

        _unifiedInterfaceStore?.Save(
            _unifiedInterface);

        Get<Grid>(
                "ExpressSetupOverlay")
            .IsVisible =
            false;

        Navigate(
            mode.StartsWith(
                "Remote",
                StringComparison.OrdinalIgnoreCase)
                ? "ServersNav"
                : mode.Contains(
                    "media",
                    StringComparison.OrdinalIgnoreCase)
                    ? "MediaHubNav"
                    : "DashboardNav");
    }

    private void PopulateSetupDiscoveryPreview()
    {
        var list =
            this.FindControl<ListBox>(
                "SetupDiscoveryList");

        if (list is null)
            return;

        var rows =
            _integrations
                .Where(item =>
                    item.IsVisible)
                .OrderBy(item =>
                    item.Category)
                .ThenBy(item =>
                    item.DisplayName)
                .Select(item =>
                    $"{item.DisplayName} · " +
                    $"{(item.IsVerified ? "verified" : "candidate")} · " +
                    $"{item.Role} · " +
                    $"{(string.IsNullOrWhiteSpace(item.Endpoint) ? "locally managed" : item.Endpoint)}")
                .ToArray();

        list.ItemsSource =
            rows.Length == 0
                ? new[]
                {
                    "No application identity has been captured yet. Setup can still continue."
                }
                : rows;
    }

    private void PopulateSetupReview()
    {
        var mode =
            Get<ComboBox>("SetupModeComboBox")
                .SelectedItem as string ??
            SetupModes[0];
        var theme =
            Get<ComboBox>("SetupThemeComboBox")
                .SelectedItem as string ??
            LinuxThemeCatalog.All[0].Name;
        var density =
            Get<ComboBox>("SetupDensityComboBox")
                .SelectedItem as string ??
            "Compact";

        Get<TextBlock>(
                "SetupReviewText")
            .Text =
            $"Setup type · {mode}\n" +
            $"Theme · {theme}\n" +
            $"Density · {density}\n" +
            $"Safe Mode · {(Get<CheckBox>("SetupSafeModeCheckBox").IsChecked == true ? "enabled" : "disabled")}\n" +
            $"Desktop notifications · {(Get<CheckBox>("SetupNotificationsCheckBox").IsChecked == true ? "enabled" : "disabled")}\n" +
            $"Detected visible instances · {_integrations.Count(item => item.IsVisible)}\n\n" +
            "GraveOps will save operator preferences only. It will not rewrite Docker, Compose, Plex, Arr, SSH, mounts, DNS, VPN or firewall configuration.";
    }

    private void InitializeUnifiedFiles()
    {
        Get<TextBox>(
                "UnifiedFilesPathTextBox")
            .Text =
            Environment.GetFolderPath(
                Environment.SpecialFolder.UserProfile);
        RefreshUnifiedFiles();
    }

    private void UnifiedFilesRefreshButton_OnClick(
        object? sender,
        RoutedEventArgs e) =>
        RefreshUnifiedFiles();

    private void RefreshUnifiedFiles()
    {
        var path =
            Get<TextBox>(
                    "UnifiedFilesPathTextBox")
                .Text?
                .Trim();

        if (string.IsNullOrWhiteSpace(path))
            path =
                Environment.GetFolderPath(
                    Environment.SpecialFolder.UserProfile);

        if (!_controlPlane.ActiveProfile.IsLocal)
        {
            Get<ListBox>(
                    "UnifiedFilesList")
                .ItemsSource =
                Array.Empty<UnifiedFileEntry>();
            Get<TextBlock>(
                    "UnifiedFilesStatusText")
                .Text =
                "Remote target selected. Use SFTP handoff to open a credential-safe terminal session.";
            return;
        }

        try
        {
            var full =
                Path.GetFullPath(path);
            var entries =
                Directory
                    .EnumerateFileSystemEntries(full)
                    .Select(item =>
                    {
                        var directory =
                            Directory.Exists(item);
                        var info =
                            directory
                                ? (FileSystemInfo)
                                    new DirectoryInfo(item)
                                : new FileInfo(item);
                        var size =
                            directory
                                ? "--"
                                : FormatBytes(
                                    ((FileInfo)info).Length);

                        return new UnifiedFileEntry(
                            info.Name,
                            info.FullName,
                            directory
                                ? "Folder"
                                : "File",
                            size,
                            info.LastWriteTime
                                .ToString("g"),
                            directory);
                    })
                    .OrderByDescending(item =>
                        item.IsDirectory)
                    .ThenBy(item =>
                        item.Name)
                    .Take(500)
                    .ToArray();

            Get<ListBox>(
                    "UnifiedFilesList")
                .ItemsSource =
                entries;
            Get<TextBlock>(
                    "UnifiedFilesStatusText")
                .Text =
                $"{entries.Length} item(s) · {full}";
        }
        catch (Exception exception)
        {
            Get<ListBox>(
                    "UnifiedFilesList")
                .ItemsSource =
                Array.Empty<UnifiedFileEntry>();
            Get<TextBlock>(
                    "UnifiedFilesStatusText")
                .Text =
                exception.Message;
        }
    }

    private static string FormatBytes(
        long bytes)
    {
        var value =
            (double)bytes;
        var units =
            new[]
            {
                "B",
                "KiB",
                "MiB",
                "GiB",
                "TiB"
            };
        var index = 0;

        while (value >= 1024 &&
               index <
               units.Length - 1)
        {
            value /= 1024;
            index++;
        }

        return
            $"{value:0.##} {units[index]}";
    }

    private void UnifiedFilesList_OnDoubleTapped(
        object? sender,
        TappedEventArgs e)
    {
        if (Get<ListBox>(
                "UnifiedFilesList")
            .SelectedItem is not
            UnifiedFileEntry entry)
        {
            return;
        }

        if (entry.IsDirectory)
        {
            Get<TextBox>(
                    "UnifiedFilesPathTextBox")
                .Text =
                entry.FullPath;
            RefreshUnifiedFiles();
            return;
        }

        OpenPathWithDesktop(
            entry.FullPath);
    }

    private void UnifiedFilesOpenButton_OnClick(
        object? sender,
        RoutedEventArgs e)
    {
        if (Get<ListBox>(
                "UnifiedFilesList")
            .SelectedItem is
            UnifiedFileEntry entry)
        {
            OpenPathWithDesktop(
                entry.FullPath);
        }
    }

    private void UnifiedFilesParentButton_OnClick(
        object? sender,
        RoutedEventArgs e)
    {
        var path =
            Get<TextBox>(
                    "UnifiedFilesPathTextBox")
                .Text;

        if (string.IsNullOrWhiteSpace(path))
            return;

        var parent =
            Directory.GetParent(
                Path.GetFullPath(path));

        if (parent is null)
            return;

        Get<TextBox>(
                "UnifiedFilesPathTextBox")
            .Text =
            parent.FullName;
        RefreshUnifiedFiles();
    }

    private void UnifiedSftpButton_OnClick(
        object? sender,
        RoutedEventArgs e)
    {
        var profile =
            _controlPlane.ActiveProfile;

        if (profile.IsLocal)
        {
            Get<TextBlock>(
                    "UnifiedFilesStatusText")
                .Text =
                "Select a remote Linux profile before opening SFTP.";
            return;
        }

        var target =
            $"{profile.Username}@{profile.Host}";

        if (TryLaunchTerminal(
                "sftp",
                new[]
                {
                    "-P",
                    profile.Port.ToString(
                        CultureInfo.InvariantCulture),
                    target
                },
                null))
        {
            Get<TextBlock>(
                    "UnifiedFilesStatusText")
                .Text =
                $"Opened SFTP handoff for {target}.";
        }
        else
        {
            Get<TextBlock>(
                    "UnifiedFilesStatusText")
                .Text =
                $"No supported terminal emulator was found. Run: sftp -P {profile.Port} {target}";
        }
    }

    private void UnifiedLocalTerminalButton_OnClick(
        object? sender,
        RoutedEventArgs e)
    {
        var path =
            (sender as Button)?.Tag as string;

        path =
            path switch
            {
                "config" =>
                    _operatorSettingsStore.ConfigDirectory,
                "data" =>
                    _operatorSettingsStore.DataDirectory,
                "diagnostics" =>
                    _operatorSettingsStore.DiagnosticsDirectory,
                "repository" =>
                    _repositoryPath,
                _ =>
                    Environment.GetFolderPath(
                        Environment.SpecialFolder.UserProfile)
            };

        Get<TextBlock>(
                "UnifiedTerminalStatusText")
            .Text =
            TryLaunchTerminal(
                null,
                Array.Empty<string>(),
                path)
                ? $"Opened terminal in {path}."
                : "No supported terminal emulator was found.";
    }

    private void UnifiedSshTerminalButton_OnClick(
        object? sender,
        RoutedEventArgs e)
    {
        var profile =
            _controlPlane.ActiveProfile;

        if (profile.IsLocal)
        {
            Get<TextBlock>(
                    "UnifiedTerminalStatusText")
                .Text =
                "The active profile is local. Use Open local terminal.";
            return;
        }

        var target =
            $"{profile.Username}@{profile.Host}";

        Get<TextBlock>(
                "UnifiedTerminalStatusText")
            .Text =
            TryLaunchTerminal(
                "ssh",
                new[]
                {
                    "-p",
                    profile.Port.ToString(
                        CultureInfo.InvariantCulture),
                    target
                },
                null)
                ? $"Opened SSH handoff for {target}."
                : $"No supported terminal emulator was found. Run: ssh -p {profile.Port} {target}";
    }

    private static bool TryLaunchTerminal(
        string? command,
        IReadOnlyList<string> arguments,
        string? workingDirectory)
    {
        var candidates =
            new[]
            {
                "x-terminal-emulator",
                "gnome-terminal",
                "konsole",
                "xfce4-terminal",
                "mate-terminal",
                "kitty"
            };

        foreach (var candidate in candidates)
        {
            if (!CommandExists(
                    candidate))
            {
                continue;
            }

            try
            {
                var start =
                    new ProcessStartInfo
                    {
                        FileName =
                            candidate,
                        UseShellExecute =
                            false
                    };

                if (!string.IsNullOrWhiteSpace(
                        workingDirectory))
                {
                    start.WorkingDirectory =
                        workingDirectory;
                }

                if (!string.IsNullOrWhiteSpace(
                        command))
                {
                    switch (candidate)
                    {
                        case "gnome-terminal":
                        case "mate-terminal":
                            start.ArgumentList.Add("--");
                            break;
                        case "xfce4-terminal":
                            start.ArgumentList.Add("-x");
                            break;
                        default:
                            start.ArgumentList.Add("-e");
                            break;
                    }

                    start.ArgumentList.Add(
                        command);

                    foreach (var argument in arguments)
                        start.ArgumentList.Add(argument);
                }

                Process.Start(start);
                return true;
            }
            catch
            {
                // Try the next available terminal.
            }
        }

        return false;
    }

    private static bool CommandExists(
        string command)
    {
        var path =
            Environment.GetEnvironmentVariable(
                "PATH") ??
            string.Empty;

        return path
            .Split(
                Path.PathSeparator,
                StringSplitOptions.RemoveEmptyEntries)
            .Any(directory =>
                File.Exists(
                    Path.Combine(
                        directory,
                        command)));
    }

    private static void OpenPathWithDesktop(
        string path)
    {
        try
        {
            var start =
                new ProcessStartInfo
                {
                    FileName =
                        "xdg-open",
                    UseShellExecute =
                        false
                };
            start.ArgumentList.Add(path);
            Process.Start(start);
        }
        catch
        {
            // The caller keeps the path visible for manual use.
        }
    }

    private void PopulateOperatorScripts()
    {
        Get<ListBox>(
                "OperatorScriptsList")
            .ItemsSource =
            _operatorScriptStore.Scripts;
        Get<TextBlock>(
                "OperatorScriptStatusText")
            .Text =
            $"{_operatorScriptStore.Scripts.Count} curated read-only script(s).";
    }

    private async void RunOperatorScriptButton_OnClick(
        object? sender,
        RoutedEventArgs e)
    {
        if (Get<ListBox>(
                "OperatorScriptsList")
            .SelectedItem is not
            OperatorScriptDefinition script)
        {
            Get<TextBlock>(
                    "OperatorScriptStatusText")
                .Text =
                "Select a script first.";
            return;
        }

        if (!_controlPlane.ActiveProfile.IsLocal)
        {
            Get<TextBlock>(
                    "OperatorScriptStatusText")
                .Text =
                "Remote scripts are not run invisibly. Open the SSH terminal and review the command first.";
            return;
        }

        if (script.IsMutating &&
            Get<CheckBox>(
                    "SettingsSafeModeCheckBox")
                .IsChecked == true)
        {
            Get<TextBlock>(
                    "OperatorScriptStatusText")
                .Text =
                "Safe Mode blocks mutating scripts.";
            return;
        }

        if (script.IsMutating &&
            !await ConfirmActionAsync(
                $"Run {script.Name}?",
                script.Command))
        {
            return;
        }

        var output =
            Get<TextBox>(
                "OperatorScriptOutputText");
        output.Text =
            $"$ {script.Command}\n\nRunning...";

        try
        {
            using var process =
                new Process
                {
                    StartInfo =
                        new ProcessStartInfo
                        {
                            FileName =
                                "bash",
                            RedirectStandardOutput =
                                true,
                            RedirectStandardError =
                                true,
                            UseShellExecute =
                                false,
                            CreateNoWindow =
                                true
                        }
                };
            process.StartInfo.ArgumentList.Add(
                "-lc");
            process.StartInfo.ArgumentList.Add(
                script.Command);
            process.Start();

            var stdout =
                process.StandardOutput.ReadToEndAsync();
            var stderr =
                process.StandardError.ReadToEndAsync();

            await process.WaitForExitAsync();

            var text =
                (await stdout).TrimEnd();
            var error =
                (await stderr).TrimEnd();

            output.Text =
                $"$ {script.Command}\n\n" +
                (string.IsNullOrWhiteSpace(text)
                    ? "(no standard output)"
                    : text) +
                (string.IsNullOrWhiteSpace(error)
                    ? string.Empty
                    : $"\n\nSTDERR\n{error}") +
                $"\n\nExit code · {process.ExitCode}";

            Get<TextBlock>(
                    "OperatorScriptStatusText")
                .Text =
                process.ExitCode == 0
                    ? "Script completed."
                    : $"Script exited with code {process.ExitCode}.";
        }
        catch (Exception exception)
        {
            output.Text =
                exception.ToString();
            Get<TextBlock>(
                    "OperatorScriptStatusText")
                .Text =
                exception.Message;
        }
    }

    private async void CopyOperatorScriptButton_OnClick(
        object? sender,
        RoutedEventArgs e)
    {
        if (Get<ListBox>(
                "OperatorScriptsList")
            .SelectedItem is not
            OperatorScriptDefinition script)
        {
            return;
        }

        var clipboard =
            TopLevel.GetTopLevel(
                this)?.Clipboard;

        if (clipboard is not null)
            await clipboard.SetTextAsync(
                script.Command);
    }

    private async void RefreshUpdateInventoryButton_OnClick(
        object? sender,
        RoutedEventArgs e)
    {
        var button =
            Get<Button>(
                "RefreshUpdateInventoryButton");
        button.IsEnabled =
            false;

        try
        {
            Get<TextBox>(
                    "UpdateInventoryOutputText")
                .Text =
                "Capturing read-only update inventory...";

            Get<TextBox>(
                    "UpdateInventoryOutputText")
                .Text =
                await LinuxReadOnlyUpdateInventory
                    .CaptureAsync();

            Get<TextBlock>(
                    "UpdateInventoryStatusText")
                .Text =
                $"Captured {DateTimeOffset.Now:g}. No update was installed.";
        }
        finally
        {
            button.IsEnabled =
                true;
        }
    }

    private void PopulateParityWorkspace()
    {
        Get<ListBox>(
                "ParityMatrixList")
            .ItemsSource =
            LinuxParityCatalog.Items;

        var classifications =
            LinuxParityCatalog.Items
                .GroupBy(item =>
                    item.Classification)
                .OrderBy(group =>
                    group.Key)
                .Select(group =>
                    $"{group.Key} · {group.Count()}")
                .ToArray();

        Get<TextBlock>(
                "ParitySummaryText")
            .Text =
            $"{LinuxParityCatalog.Items.Count} capability checks · " +
            string.Join(
                " · ",
                classifications);
    }

    private void ParityMatrixList_OnDoubleTapped(
        object? sender,
        TappedEventArgs e)
    {
        if (Get<ListBox>(
                "ParityMatrixList")
            .SelectedItem is not
            LinuxParityItem item)
        {
            return;
        }

        OpenUnifiedDetails(
            item.Capability,
            item.Classification,
            $"Windows reference\n{item.WindowsReference}\n\n" +
            $"Linux implementation\n{item.LinuxImplementation}\n\n" +
            $"Evidence\n{item.Evidence}");
    }

    private void ExportProfileButton_OnClick(
        object? sender,
        RoutedEventArgs e)
    {
        try
        {
            Directory.CreateDirectory(
                _operatorSettingsStore
                    .DiagnosticsDirectory);

            var path =
                Path.Combine(
                    _operatorSettingsStore
                        .DiagnosticsDirectory,
                    $"graveops-profile-{DateTimeOffset.Now:yyyyMMdd-HHmmss}.json");

            var document =
                new
                {
                    Format =
                        "GraveOps Linux redacted profile",
                    ExportedAt =
                        DateTimeOffset.Now,
                    Interface =
                        new
                        {
                            _unifiedInterface.ThemeName,
                            _unifiedInterface.Density,
                            _unifiedInterface.RestoreLastPage,
                            _unifiedInterface.SilentRefresh,
                            _unifiedInterface.ShowFreshness,
                            _unifiedInterface.SetupMode
                        },
                    Hosts =
                        _controlPlane.Profiles.Profiles
                            .Select(profile =>
                                new
                                {
                                    profile.Id,
                                    profile.Name,
                                    Kind =
                                        profile.Kind.ToString(),
                                    profile.Host,
                                    profile.Port,
                                    profile.Username,
                                    profile.Role,
                                    Authentication =
                                        profile.Authentication.ToString(),
                                    PrivateKeyConfigured =
                                        !string.IsNullOrWhiteSpace(
                                            profile.PrivateKeyPath),
                                    FingerprintPinned =
                                        !string.IsNullOrWhiteSpace(
                                            profile.HostKeyFingerprint)
                                })
                            .ToArray(),
                    Applications =
                        _integrations
                            .Select(item =>
                                new
                                {
                                    item.Name,
                                    item.DisplayName,
                                    item.Category,
                                    item.Role,
                                    item.Protocol,
                                    item.IsVerified,
                                    item.OwnsHealth,
                                    item.IsVisible,
                                    item.ShowInNavigation,
                                    item.Provenance
                                })
                            .ToArray(),
                    Security =
                        new[]
                        {
                            "Passwords excluded",
                            "API keys excluded",
                            "Tokens excluded",
                            "Private-key paths excluded",
                            "Secret values excluded"
                        }
                };

            File.WriteAllText(
                path,
                JsonSerializer.Serialize(
                    document,
                    new JsonSerializerOptions
                    {
                        WriteIndented =
                            true
                    }));

            Get<TextBlock>(
                    "InterfaceSettingsStatusText")
                .Text =
                $"Redacted profile exported to {path}";
        }
        catch (Exception exception)
        {
            Get<TextBlock>(
                    "InterfaceSettingsStatusText")
                .Text =
                $"Profile export failed: {exception.Message}";
        }
    }

    private void TerminalButton_OnClick(
        object? sender,
        RoutedEventArgs e)
    {
        Navigate(
            "ToolsNav");
        Get<TabControl>(
                "OperatorToolsTabControl")
            .SelectedIndex =
            0;
    }

    private void ApplyActionAvailabilityReasons()
    {
        var plexIntegration =
            _integrations.FirstOrDefault(item =>
                item.Name.Equals(
                    "Plex",
                    StringComparison.OrdinalIgnoreCase));

        SetDisabledReason(
            "PlexOpenButton",
            plexIntegration is null ||
            ResolveIntegrationUrl(
                plexIntegration) is null
                ? "No verified Plex web endpoint is available."
                : "The Plex open action is available.");

        SetDisabledReason(
            "PlexRestartButton",
            Get<CheckBox>(
                    "SettingsSafeModeCheckBox")
                .IsChecked == true
                ? "Unavailable because Safe Mode is enabled."
                : "No restartable Plex systemd service or Docker container was detected.");

        SetDisabledReason(
            "StartServiceButton",
            "Select a service first. Safe Mode must also allow the action.");
        SetDisabledReason(
            "StopServiceButton",
            "Select a running service first. Safe Mode must also allow the action.");
        SetDisabledReason(
            "RestartServiceButton",
            "Select a service first. Safe Mode must also allow the action.");
        SetDisabledReason(
            "DockerStartButton",
            "Select a stopped container first. Safe Mode must also allow the action.");
        SetDisabledReason(
            "DockerStopButton",
            "Select a running container first. Safe Mode must also allow the action.");
        SetDisabledReason(
            "DockerRestartButton",
            "Select a container first. Safe Mode must also allow the action.");
        SetDisabledReason(
            "OpenIntegrationButton",
            "No verified web endpoint is available for the selected application.");
        SetDisabledReason(
            "DashboardOpenApplicationButton",
            "Select an application card first.");
    }

    private void SetDisabledReason(
        string controlName,
        string reason)
    {
        var button =
            this.FindControl<Button>(
                controlName);

        if (button is null)
            return;

        ToolTip.SetTip(
            button,
            button.IsEnabled
                ? null
                : reason);
    }
}
