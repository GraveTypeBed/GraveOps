using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace GraveOps.App.Windows;

public sealed class GraveOpsDialog : Window
{
    private MessageBoxResult _result = MessageBoxResult.None;
    private readonly MessageBoxResult _closeResult;

    private static Brush Theme(string key, string fallback)
        => Application.Current?.TryFindResource(key) as Brush
           ?? new SolidColorBrush((Color)ColorConverter.ConvertFromString(fallback));

    public static MessageBoxResult Show(
        Window? owner,
        string message,
        string title,
        MessageBoxButton buttons,
        MessageBoxImage image)
    {
        var dialog = new GraveOpsDialog(message, title, buttons, image);

        if (owner is not null &&
            owner.IsLoaded &&
            owner != dialog)
        {
            dialog.Owner = owner;
        }

        dialog.ShowDialog();
        return dialog._result;
    }

    public static MessageBoxResult Show(
        Window? owner,
        string message,
        string title)
        => Show(
            owner,
            message,
            title,
            MessageBoxButton.OK,
            MessageBoxImage.Information);

    private GraveOpsDialog(
        string message,
        string title,
        MessageBoxButton buttons,
        MessageBoxImage image)
    {
        _closeResult = buttons switch
        {
            MessageBoxButton.OKCancel => MessageBoxResult.Cancel,
            MessageBoxButton.YesNoCancel => MessageBoxResult.Cancel,
            MessageBoxButton.YesNo => MessageBoxResult.No,
            _ => MessageBoxResult.OK
        };

        Title = title;
        Width = 540;
        MinWidth = 420;
        MaxWidth = 760;
        SizeToContent = SizeToContent.Height;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        Foreground = Theme("GoText", "#F3F1F3");
        ShowInTaskbar = false;

        var shell = new Border
        {
            Background = Theme("GoCanvas", "#111113"),
            BorderBrush = Theme("GoBorder", "#34343A"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8)
        };

        var root = new Grid();
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(42) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var titleBar = new Border
        {
            Background = Theme("GoSurface", "#18181B"),
            BorderBrush = Theme("GoBorder", "#34343A"),
            BorderThickness = new Thickness(0, 0, 0, 1),
            CornerRadius = new CornerRadius(8, 8, 0, 0)
        };

        titleBar.MouseLeftButtonDown += (_, e) =>
        {
            if (e.ButtonState == MouseButtonState.Pressed)
                DragMove();
        };

        var titleGrid = new Grid
        {
            Margin = new Thickness(14, 0, 8, 0)
        };

        titleGrid.ColumnDefinitions.Add(
            new ColumnDefinition
            {
                Width = new GridLength(1, GridUnitType.Star)
            });

        titleGrid.ColumnDefinitions.Add(
            new ColumnDefinition
            {
                Width = GridLength.Auto
            });

        titleGrid.Children.Add(
            new TextBlock
            {
                Text = title,
                FontWeight = FontWeights.SemiBold,
                VerticalAlignment = VerticalAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis
            });

        var close = new Button
        {
            Content = "X",
            Width = 34,
            Height = 28,
            Padding = new Thickness(0),
            FontWeight = FontWeights.SemiBold,
            Background = Brushes.Transparent,
            Foreground = Theme("GoTextSecondary", "#AAB3BC"),
            BorderThickness = new Thickness(0)
        };

        close.Click += (_, _) =>
        {
            _result = _closeResult;
            Close();
        };

        Grid.SetColumn(close, 1);
        titleGrid.Children.Add(close);
        titleBar.Child = titleGrid;

        Grid.SetRow(titleBar, 0);
        root.Children.Add(titleBar);

        var body = new Grid
        {
            Margin = new Thickness(18, 17, 18, 8)
        };

        body.ColumnDefinitions.Add(
            new ColumnDefinition
            {
                Width = GridLength.Auto
            });

        body.ColumnDefinitions.Add(
            new ColumnDefinition
            {
                Width = new GridLength(1, GridUnitType.Star)
            });

        var severity = new Border
        {
            Background = image switch
            {
                MessageBoxImage.Error => Theme("GoDangerBg", "#34181C"),
                MessageBoxImage.Warning => Theme("GoWarningBg", "#392D12"),
                _ => Theme("GoSurfaceRaised", "#202024")
            },
            BorderBrush = image switch
            {
                MessageBoxImage.Error => Theme("GoDanger", "#EF6B73"),
                MessageBoxImage.Warning => Theme("GoWarning", "#E2B65A"),
                _ => Theme("GoBorder", "#34343A")
            },
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(8, 5, 8, 5),
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(0, 1, 12, 0)
        };

        severity.Child = new TextBlock
        {
            Text = image switch
            {
                MessageBoxImage.Error => "ERROR",
                MessageBoxImage.Warning => "WARNING",
                MessageBoxImage.Question => "QUESTION",
                _ => "INFO"
            },
            FontSize = 10,
            FontWeight = FontWeights.Bold,
            Foreground = image switch
            {
                MessageBoxImage.Error => Theme("GoDanger", "#EF6B73"),
                MessageBoxImage.Warning => Theme("GoWarning", "#E2B65A"),
                _ => Theme("GoInfo", "#AA96B5")
            }
        };

        body.Children.Add(severity);

        var messageText = new TextBlock
        {
            Text = message,
            TextWrapping = TextWrapping.Wrap,
            Foreground = Theme("GoText", "#F3F1F3"),
            FontSize = 12.5,
            LineHeight = 19,
            MaxWidth = 650
        };

        Grid.SetColumn(messageText, 1);
        body.Children.Add(messageText);

        Grid.SetRow(body, 1);
        root.Children.Add(body);

        var footer = new Border
        {
            BorderBrush = Theme("GoBorder", "#34343A"),
            BorderThickness = new Thickness(0, 1, 0, 0),
            Padding = new Thickness(14, 11, 14, 12),
            Margin = new Thickness(0, 8, 0, 0)
        };

        var buttonsPanel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right
        };

        foreach (var spec in ButtonsFor(buttons))
        {
            buttonsPanel.Children.Add(
                MakeButton(
                    spec.Text,
                    spec.Result,
                    spec.Primary));
        }

        footer.Child = buttonsPanel;
        Grid.SetRow(footer, 2);
        root.Children.Add(footer);

        shell.Child = root;
        Content = shell;

        PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape)
            {
                _result = _closeResult;
                Close();
                e.Handled = true;
            }
        };
    }

    private static IEnumerable<(string Text, MessageBoxResult Result, bool Primary)>
        ButtonsFor(MessageBoxButton buttons)
        => buttons switch
        {
            MessageBoxButton.OKCancel =>
            [
                ("Cancel", MessageBoxResult.Cancel, false),
                ("OK", MessageBoxResult.OK, true)
            ],
            MessageBoxButton.YesNo =>
            [
                ("No", MessageBoxResult.No, false),
                ("Yes", MessageBoxResult.Yes, true)
            ],
            MessageBoxButton.YesNoCancel =>
            [
                ("Cancel", MessageBoxResult.Cancel, false),
                ("No", MessageBoxResult.No, false),
                ("Yes", MessageBoxResult.Yes, true)
            ],
            _ =>
            [
                ("OK", MessageBoxResult.OK, true)
            ]
        };

    private Button MakeButton(
        string text,
        MessageBoxResult result,
        bool primary)
    {
        var button = new Button
        {
            Content = text,
            MinWidth = 88,
            Height = 34,
            Margin = new Thickness(7, 0, 0, 0),
            Cursor = Cursors.Hand
        };

        var style =
            Application.Current?.TryFindResource(
                primary
                    ? "GoCompactPrimaryButton"
                    : "GoCompactSecondaryButton") as Style;

        if (style is not null)
            button.Style = style;

        button.Click += (_, _) =>
        {
            _result = result;
            Close();
        };

        return button;
    }
}