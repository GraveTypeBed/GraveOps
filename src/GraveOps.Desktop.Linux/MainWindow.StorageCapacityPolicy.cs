using System;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;

namespace GraveOps.Desktop.Linux;

public partial class MainWindow
{
    private static readonly string[] StorageCapacityModeOptions =
    {
        "Normal",
        "Dashboard only",
        "Muted",
        "Disabled"
    };

    private static readonly string[] StorageCapacityMuteOptions =
    {
        "Until manually restored",
        "1 hour",
        "24 hours",
        "7 days"
    };

    private async void StorageCapacityPolicyButton_OnClick(
        object? sender,
        RoutedEventArgs e)
    {
        var mountPoint = SelectedStorageCapacityMountPoint();
        var saved = await ShowStorageCapacityAlertPolicyDialogAsync(
            mountPoint);
        if (!saved)
            return;

        RefreshPolicyProjection();
        ApplyStorageFilter();
        PopulateStorageCapacityPolicySettings();
    }

    private void StorageListWithCapacityPolicy_OnSelectionChanged(
        object? sender,
        SelectionChangedEventArgs e)
    {
        StorageList_OnSelectionChanged(sender, e);
        PopulateStorageCapacityPolicySettings();
    }

    private string SelectedStorageCapacityMountPoint()
    {
        var selected = Get<ListBox>("StorageList").SelectedItem;
        if (selected is null)
            return string.Empty;

        return selected.GetType()
                   .GetProperty("MountPoint")?
                   .GetValue(selected)?
                   .ToString() ??
               string.Empty;
    }

    private async Task<bool> ShowStorageCapacityAlertPolicyDialogAsync(
        string mountPoint)
    {
        var global =
            _findingPolicies.GetGlobalStorageCapacityAlertPolicy();
        var hasOverride =
            !string.IsNullOrWhiteSpace(mountPoint) &&
            _findingPolicies.HasStorageCapacityAlertOverride(mountPoint);
        var mount =
            string.IsNullOrWhiteSpace(mountPoint)
                ? global.Clone()
                : _findingPolicies.GetStorageCapacityAlertPolicy(mountPoint);

        ComboBox ModeBox(StorageCapacityAlertMode mode) =>
            new()
            {
                Width = 190,
                ItemsSource = StorageCapacityModeOptions,
                SelectedItem = StorageCapacityModeDisplay(mode)
            };

        ComboBox MuteBox() =>
            new()
            {
                Width = 190,
                ItemsSource = StorageCapacityMuteOptions,
                SelectedIndex = 0
            };

        var globalEnabled = new CheckBox
        {
            Content = "Monitor drive capacity globally",
            IsChecked = global.MonitoringEnabled
        };
        var globalMode = ModeBox(global.Mode);
        var globalMute = MuteBox();

        var useGlobal = new CheckBox
        {
            Content = "Use the global capacity policy for this mount",
            IsChecked = !hasOverride,
            IsEnabled = !string.IsNullOrWhiteSpace(mountPoint)
        };
        var mountEnabled = new CheckBox
        {
            Content = "Monitor capacity for this mount",
            IsChecked = mount.MonitoringEnabled,
            IsEnabled = !string.IsNullOrWhiteSpace(mountPoint)
        };
        var mountMode = ModeBox(mount.Mode);
        mountMode.IsEnabled = !string.IsNullOrWhiteSpace(mountPoint);
        var mountMute = MuteBox();
        mountMute.IsEnabled = !string.IsNullOrWhiteSpace(mountPoint);
        var ignoreMount = new CheckBox
        {
            Content = "Ignore capacity for this mount (mount/filesystem failures still alert)",
            IsChecked = mount.IgnoreMount,
            IsEnabled = !string.IsNullOrWhiteSpace(mountPoint)
        };

        var validation = new TextBlock
        {
            Foreground = OpsPalette.Foreground(OpsSeverity.Error),
            TextWrapping = TextWrapping.Wrap
        };

        var dialog = new Window
        {
            Title = "Storage capacity alerts",
            Width = 720,
            Height = 680,
            CanResize = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Background = new SolidColorBrush(Color.Parse("#111113"))
        };

        var cancel = new Button { Content = "Cancel" };
        var defaults = new Button { Content = "Restore policy defaults" };
        var save = new Button { Content = "Save capacity policy" };
        save.Classes.Add("primary");

        cancel.Click += (_, _) => dialog.Close(string.Empty);
        defaults.Click += (_, _) =>
        {
            _findingPolicies.SetStorageCapacityAlertPolicies(
                StorageCapacityAlertPolicy.Defaults(),
                mountPoint,
                null,
                useGlobalForMount: true);
            dialog.Close("saved");
        };
        save.Click += (_, _) =>
        {
            try
            {
                var globalSelected = StorageCapacityModeFromDisplay(
                    globalMode.SelectedItem?.ToString());
                var globalPolicy = new StorageCapacityAlertPolicy
                {
                    MonitoringEnabled =
                        globalEnabled.IsChecked == true &&
                        globalSelected != StorageCapacityAlertMode.Disabled,
                    Mode = globalEnabled.IsChecked == true
                        ? globalSelected
                        : StorageCapacityAlertMode.Disabled,
                    MutedUntil = StorageCapacityMuteUntil(
                        globalSelected,
                        globalMute.SelectedItem?.ToString()),
                    IgnoreMount = false
                };
                StorageCapacityAlertPolicy? mountPolicy = null;
                if (!string.IsNullOrWhiteSpace(mountPoint) &&
                    useGlobal.IsChecked != true)
                {
                    var mountSelected = StorageCapacityModeFromDisplay(
                        mountMode.SelectedItem?.ToString());
                    mountPolicy = new StorageCapacityAlertPolicy
                    {
                        MonitoringEnabled =
                            mountEnabled.IsChecked == true &&
                            mountSelected !=
                                StorageCapacityAlertMode.Disabled,
                        Mode = mountEnabled.IsChecked == true
                            ? mountSelected
                            : StorageCapacityAlertMode.Disabled,
                        MutedUntil = StorageCapacityMuteUntil(
                            mountSelected,
                            mountMute.SelectedItem?.ToString()),
                        IgnoreMount =
                            ignoreMount.IsChecked == true
                    };
                }

                _findingPolicies.SetStorageCapacityAlertPolicies(
                    globalPolicy,
                    mountPoint,
                    mountPolicy,
                    useGlobalForMount:
                        string.IsNullOrWhiteSpace(mountPoint) ||
                        useGlobal.IsChecked == true);

                dialog.Close("saved");
            }
            catch (Exception exception)
            {
                validation.Text = exception.Message;
            }
        };

        Control LabeledControl(string label, Control control) =>
            new StackPanel
            {
                Spacing = 5,
                Children =
                {
                    new TextBlock
                    {
                        Text = label,
                        Classes = { "eyebrow" }
                    },
                    control
                }
            };

        Grid TwoColumnGrid(Control left, Control right)
        {
            var grid = new Grid
            {
                ColumnDefinitions = new ColumnDefinitions("*,*"),
                ColumnSpacing = 12
            };
            Grid.SetColumn(right, 1);
            grid.Children.Add(left);
            grid.Children.Add(right);
            return grid;
        }

        var mountTitle = string.IsNullOrWhiteSpace(mountPoint)
            ? "Per-mount override"
            : $"Per-mount override · {mountPoint}";
        var mountDescription = string.IsNullOrWhiteSpace(mountPoint)
            ? "Select a mount on the Storage page to configure an override. Global settings can still be changed here."
            : "Use global settings, assign a mount-specific mode, temporarily mute it, or ignore only its capacity classification.";

        dialog.Content = new Border
        {
            Padding = new Thickness(24),
            Child = new ScrollViewer
            {
                HorizontalScrollBarVisibility =
                    Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled,
                VerticalScrollBarVisibility =
                    Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
                Content = new StackPanel
                {
                    Spacing = 16,
                    Children =
                    {
                        new TextBlock
                        {
                            Text = "Storage capacity alert policy",
                            FontSize = 21,
                            FontWeight = FontWeight.SemiBold
                        },
                        new TextBlock
                        {
                            Text = "Capacity classification is independent from mount, filesystem, permission and I/O health. Disabling capacity alerts never suppresses an unavailable, read-only or failing filesystem.",
                            Classes = { "muted" },
                            TextWrapping = TextWrapping.Wrap
                        },
                        new Border
                        {
                            Classes = { "inset" },
                            Child = new StackPanel
                            {
                                Spacing = 10,
                                Children =
                                {
                                    new TextBlock
                                    {
                                        Text = "Global capacity policy",
                                        FontSize = 15,
                                        FontWeight = FontWeight.SemiBold
                                    },
                                    globalEnabled,
                                    TwoColumnGrid(
                                        LabeledControl(
                                            "ALERT MODE",
                                            globalMode),
                                        LabeledControl(
                                            "MUTE DURATION",
                                            globalMute)),
                                    new TextBlock
                                    {
                                        Text = "Normal creates findings. Dashboard only changes the card without findings. Muted suppresses warning/error capacity alerts but not critical capacity. Disabled makes capacity informational only.",
                                        Classes = { "dim" },
                                        FontSize = 10,
                                        TextWrapping = TextWrapping.Wrap
                                    }
                                }
                            }
                        },
                        new Border
                        {
                            Classes = { "inset" },
                            Child = new StackPanel
                            {
                                Spacing = 10,
                                Children =
                                {
                                    new TextBlock
                                    {
                                        Text = mountTitle,
                                        FontSize = 15,
                                        FontWeight = FontWeight.SemiBold,
                                        TextWrapping = TextWrapping.Wrap
                                    },
                                    new TextBlock
                                    {
                                        Text = mountDescription,
                                        Classes = { "muted" },
                                        FontSize = 10.5,
                                        TextWrapping = TextWrapping.Wrap
                                    },
                                    useGlobal,
                                    mountEnabled,
                                    TwoColumnGrid(
                                        LabeledControl(
                                            "MOUNT MODE",
                                            mountMode),
                                        LabeledControl(
                                            "MUTE DURATION",
                                            mountMute)),
                                    ignoreMount
                                }
                            }
                        },
                        validation,
                        new WrapPanel
                        {
                            HorizontalAlignment = HorizontalAlignment.Right,
                            Children =
                            {
                                defaults,
                                cancel,
                                save
                            }
                        }
                    }
                }
            }
        };

        defaults.Margin = new Thickness(0, 0, 8, 0);
        cancel.Margin = new Thickness(0, 0, 8, 0);

        var result = await dialog.ShowDialog<string>(this);
        return string.Equals(
            result,
            "saved",
            StringComparison.Ordinal);
    }

    private void PopulateStorageCapacityPolicySettings()
    {
        var global =
            _findingPolicies.GetGlobalStorageCapacityAlertPolicy();
        var globalLabel =
            global.MonitoringEnabled &&
            global.Mode != StorageCapacityAlertMode.Disabled
                ? StorageCapacityModeDisplay(global.Mode)
                : "Disabled";
        var summary =
            $"Capacity alerts: {globalLabel} · " +
            $"{_findingPolicies.StorageCapacityAlertOverrideCount} mount override(s)";

        if (this.FindControl<TextBlock>(
                "SettingsCapacityPolicySummaryText") is { } settings)
        {
            settings.Text = summary;
        }

        if (this.FindControl<TextBlock>(
                "StorageCapacityAlertStatusText") is not { } storage)
        {
            return;
        }

        var mountPoint = SelectedStorageCapacityMountPoint();
        if (string.IsNullOrWhiteSpace(mountPoint))
        {
            storage.Text =
                summary +
                ". Select a mount to inspect or override its effective policy.";
            return;
        }

        var effective =
            _findingPolicies.GetStorageCapacityAlertPolicy(mountPoint);
        var scope =
            _findingPolicies.HasStorageCapacityAlertOverride(mountPoint)
                ? "mount override"
                : "global policy";
        storage.Text =
            $"Capacity alerts · {scope} · " +
            $"{StorageCapacityModeDisplay(effective.Mode)}" +
            (effective.IgnoreMount
                ? " · capacity ignored"
                : effective.MonitoringEnabled
                    ? string.Empty
                    : " · monitoring off");
    }

    private static string StorageCapacityModeDisplay(
        StorageCapacityAlertMode mode) =>
        LinuxFindingPolicyStore.StorageCapacityAlertModeLabel(mode);

    private static StorageCapacityAlertMode
        StorageCapacityModeFromDisplay(string? value) =>
        value?.Trim() switch
        {
            "Dashboard only" => StorageCapacityAlertMode.DashboardOnly,
            "Muted" => StorageCapacityAlertMode.Muted,
            "Disabled" => StorageCapacityAlertMode.Disabled,
            _ => StorageCapacityAlertMode.Normal
        };

    private static DateTimeOffset? StorageCapacityMuteUntil(
        StorageCapacityAlertMode mode,
        string? duration)
    {
        if (mode != StorageCapacityAlertMode.Muted)
            return null;

        return duration?.Trim() switch
        {
            "1 hour" => DateTimeOffset.Now.AddHours(1),
            "24 hours" => DateTimeOffset.Now.AddHours(24),
            "7 days" => DateTimeOffset.Now.AddDays(7),
            _ => null
        };
    }
}
