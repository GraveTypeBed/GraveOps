using Avalonia.Controls;
using Avalonia.Interactivity;
using GraveOps.Presentation.Avalonia.Shell;

namespace GraveOps.Desktop.Linux;

public partial class MainWindow
{
    private void InitializeSharedUnifiedShell()
    {
        var shell =
            Get<UnifiedShellView>(
                "SharedShellView");

        shell.SetBrandImage(
            Get<Image>(
                    "LegacyBrandImage")
                .Source);

        shell.BindPageHeader(
            Get<TextBlock>(
                "PageTitleText"),
            Get<TextBlock>(
                "PageSubtitleText"));

        shell.BindConnection(
            Get<TextBlock>(
                "ConnectionText"),
            Get<TextBlock>(
                "ConnectionDetailText"));

        var footerTarget =
            Get<TextBlock>(
                "FooterTargetText");

        var footerConnection =
            Get<TextBlock>(
                "FooterConnectionText");

        var footerMode =
            Get<TextBlock>(
                "FooterModeText");

        var footerFindings =
            Get<TextBlock>(
                "FooterFindingsText");

        var footerFreshness =
            Get<TextBlock>(
                "FooterFreshnessText");

        shell.BindFooter(
            () =>
                new UnifiedShellFooterState(
                    $"{footerTarget.Text} / {footerConnection.Text}",
                    $"{footerMode.Text} / {footerFindings.Text} / {footerFreshness.Text}"),
            footerTarget,
            footerConnection,
            footerMode,
            footerFindings,
            footerFreshness);

        shell.BindNavigation(
            Get<ScrollViewer>(
                "SidebarNavigationScrollViewer"));

        shell.AttachTargetSelector(
            Get<ComboBox>(
                "ActiveTargetComboBox"));

        shell.AttachPageContent(
            Get<Grid>(
                "LegacyPageHost"));

        var legacyMain =
            Get<Grid>(
                "LegacyMainShellGrid");

        shell.AttachOverlays(
            legacyMain.Children
                .Where(
                    IsSharedShellOverlay)
                .ToArray());

        shell.NavigationRequested +=
            SharedShellNavigationRequested;

        shell.CommandRequested +=
            SharedShellCommandRequested;

        Get<Border>(
                "LegacyTitleBar")
            .Opacity =
            0;

        Get<Border>(
                "LegacyTitleBar")
            .IsHitTestVisible =
            false;

        Get<Grid>(
                "LegacyBody")
            .Opacity =
            0;

        Get<Grid>(
                "LegacyBody")
            .IsHitTestVisible =
            false;
    }

    private static bool IsSharedShellOverlay(
        Control control)
    {
        var name =
            control.Name ??
            string.Empty;

        return
            Grid.GetRowSpan(
                control) >
            1 &&
            (
                name.Contains(
                    "Drawer",
                    StringComparison.Ordinal) ||
                name.Contains(
                    "Overlay",
                    StringComparison.Ordinal) ||
                name.Contains(
                    "Palette",
                    StringComparison.Ordinal)
            );
    }

    private async void SharedShellNavigationRequested(
        object? sender,
        UnifiedShellNavigationRequestedEventArgs e)
    {
        if (IntegrationNavigationTargets.TryGetValue(
                e.NavigationKey,
                out var integrationName) &&
            !await EnsureNavigationApplicationOwnerActiveAsync(
                integrationName))
        {
            return;
        }

        Navigate(
            e.NavigationKey);
    }

    private void SharedShellCommandRequested(
        object? sender,
        UnifiedShellCommandRequestedEventArgs e)
    {
        var routed =
            new RoutedEventArgs();

        switch (e.CommandKey)
        {
            case "Overview":
                OverviewButton_OnClick(
                    sender,
                    routed);
                break;

            case "Jobs":
                JobsButton_OnClick(
                    sender,
                    routed);
                break;

            case "Findings":
                Navigate(
                    "IntelligenceNav");
                break;

            case "Activity":
                ActivityButton_OnClick(
                    sender,
                    routed);
                break;

            case "Terminal":
                TerminalButton_OnClick(
                    sender,
                    routed);
                break;

            case "Maintenance":
                MaintenanceButton_OnClick(
                    sender,
                    routed);
                break;

            case "Search":
                CommandPaletteButton_OnClick(
                    sender,
                    routed);
                break;

            case "Customize":
                Navigate(
                    "SettingsNav");
                break;
        }
    }
}