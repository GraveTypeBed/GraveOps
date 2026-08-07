using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;

namespace GraveOps.Desktop.Linux;

public partial class MainWindow
{
    private const double CompactWindowWidth =
        1320;

    private bool _responsiveLayoutReady;
    private bool? _compactLayoutApplied;

    private void InitializeResponsiveLayout()
    {
        _responsiveLayoutReady =
            true;

        ApplyResponsiveLayout(
            Bounds.Width >
            0
                ? Bounds.Width
                : Width);
    }

    protected override void OnSizeChanged(
        SizeChangedEventArgs e)
    {
        base.OnSizeChanged(
            e);

        if (_responsiveLayoutReady)
        {
            ApplyResponsiveLayout(
                e.NewSize.Width);
        }
    }

    private void ApplyResponsiveLayout(
        double windowWidth)
    {
        var compact =
            windowWidth <
            CompactWindowWidth;

        if (_compactLayoutApplied ==
            compact)
        {
            return;
        }

        _compactLayoutApplied =
            compact;

        var shell =
            Get<Grid>(
                "ShellBodyGrid");
        shell.ColumnDefinitions =
            new ColumnDefinitions(
                compact
                    ? "230,*"
                    : "260,*");

        var header =
            Get<Grid>(
                "MainHeaderGrid");
        var title =
            Get<StackPanel>(
                "MainHeaderTitlePanel");
        var commands =
            Get<WrapPanel>(
                "MainHeaderCommandsPanel");

        header.ColumnDefinitions =
            new ColumnDefinitions(
                compact
                    ? "*"
                    : "*,Auto");
        header.RowDefinitions =
            new RowDefinitions(
                compact
                    ? "Auto,Auto"
                    : "Auto");
        header.Margin =
            compact
                ? new Thickness(
                    18,
                    8)
                : new Thickness(
                    28,
                    8);

        Grid.SetColumn(
            title,
            0);
        Grid.SetRow(
            title,
            0);
        Grid.SetColumn(
            commands,
            compact
                ? 0
                : 1);
        Grid.SetRow(
            commands,
            compact
                ? 1
                : 0);

        commands.HorizontalAlignment =
            compact
                ? HorizontalAlignment.Left
                : HorizontalAlignment.Right;
        commands.VerticalAlignment =
            VerticalAlignment.Center;
        commands.Margin =
            compact
                ? new Thickness(
                    0,
                    7,
                    0,
                    0)
                : new Thickness(
                    0);

        Get<TextBlock>(
                "QuickSearchHintText")
            .IsVisible =
            !compact;

        Get<Grid>(
                "PageContentHost")
            .Margin =
            compact
                ? new Thickness(
                    14)
                : new Thickness(
                    24);

        var lifecycleWorkspace =
            Get<Grid>(
                "LifecycleWorkspaceGrid");
        var lifecycleRemediation =
            Get<Border>(
                "LifecycleRemediationModule");

        lifecycleWorkspace.ColumnDefinitions =
            new ColumnDefinitions(
                compact
                    ? "*"
                    : "1.25*,0.75*");
        lifecycleWorkspace.RowDefinitions =
            new RowDefinitions(
                compact
                    ? "220,220"
                    : "*");
        lifecycleWorkspace.Height =
            compact
                ? 448
                : 260;

        Grid.SetColumn(
            lifecycleRemediation,
            compact
                ? 0
                : 1);
        Grid.SetRow(
            lifecycleRemediation,
            compact
                ? 1
                : 0);

        var settingsInterface =
            Get<Grid>(
                "SettingsInterfaceGrid");
        var settingsInterfaceActions =
            Get<StackPanel>(
                "SettingsInterfaceActionsPanel");

        settingsInterface.ColumnDefinitions =
            new ColumnDefinitions(
                compact
                    ? "*"
                    : "1.15*,0.85*");
        settingsInterface.RowDefinitions =
            new RowDefinitions(
                compact
                    ? "Auto,Auto"
                    : "*");

        Grid.SetColumn(
            settingsInterfaceActions,
            compact
                ? 0
                : 1);
        Grid.SetRow(
            settingsInterfaceActions,
            compact
                ? 1
                : 0);
        settingsInterfaceActions.Margin =
            compact
                ? new Thickness(
                    0,
                    8,
                    0,
                    0)
                : new Thickness(
                    0);

        var settingsBody =
            Get<Grid>(
                "SettingsBodyGrid");
        var settingsDefaults =
            Get<Border>(
                "SettingsOperatorDefaultsModule");
        var settingsPolicy =
            Get<Border>(
                "SettingsPolicyModule");
        var settingsPaths =
            Get<Border>(
                "SettingsPathsModule");
        var settingsVersion =
            Get<Border>(
                "SettingsVersionModule");

        settingsBody.ColumnDefinitions =
            new ColumnDefinitions(
                compact
                    ? "*"
                    : "1.05*,0.95*");
        settingsBody.RowDefinitions =
            new RowDefinitions(
                compact
                    ? "Auto,Auto,Auto,Auto"
                    : "Auto,Auto,Auto");

        Grid.SetColumn(
            settingsDefaults,
            0);
        Grid.SetRow(
            settingsDefaults,
            0);
        Grid.SetColumnSpan(
            settingsDefaults,
            1);

        Grid.SetColumn(
            settingsPolicy,
            compact
                ? 0
                : 1);
        Grid.SetRow(
            settingsPolicy,
            compact
                ? 1
                : 0);
        Grid.SetColumnSpan(
            settingsPolicy,
            1);

        Grid.SetColumn(
            settingsPaths,
            0);
        Grid.SetRow(
            settingsPaths,
            compact
                ? 2
                : 1);
        Grid.SetColumnSpan(
            settingsPaths,
            compact
                ? 1
                : 2);

        Grid.SetColumn(
            settingsVersion,
            0);
        Grid.SetRow(
            settingsVersion,
            compact
                ? 3
                : 2);
        Grid.SetColumnSpan(
            settingsVersion,
            compact
                ? 1
                : 2);
    }
}
