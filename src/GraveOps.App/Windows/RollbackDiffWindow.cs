using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shell;

namespace GraveOps.App.Windows;

public sealed class RollbackDiffWindow : Window
{
    private static readonly Brush Bg = Theme("GoCanvas", "#111113");
    private static readonly Brush Surface = Theme("GoSurface", "#18181B");
    private static readonly Brush Surface2 = Theme("GoSurfaceRaised", "#202024");
    private static readonly Brush Border = Theme("GoBorder", "#34343A");
    private static readonly Brush Text = Theme("GoText", "#F3F1F3");
    private static readonly Brush Muted = Theme("GoTextSecondary", "#9AA4AE");
    private static readonly Brush Accent = Theme("GoAccent", "#B98BA8");

    public RollbackDiffWindow(string remotePath, string backupPath, string previous, string current)
    {
        Title = "GraveOps - Rollback Diff";
        Width = 1120;
        Height = 760;
        MinWidth = 760;
        MinHeight = 520;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.CanResize;
        Background = Bg;
        Foreground = Text;

        WindowChrome.SetWindowChrome(this, new WindowChrome
        {
            CaptionHeight = 42,
            ResizeBorderThickness = new Thickness(6),
            GlassFrameThickness = new Thickness(0),
            CornerRadius = new CornerRadius(0),
            UseAeroCaptionButtons = false
        });

        var shell = new Border
        {
            Background = Bg,
            BorderBrush = Border,
            BorderThickness = new Thickness(1)
        };

        var root = new Grid();
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(42) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var titleBar = new Border
        {
            Background = Brush("#0B0D0F"),
            BorderBrush = Border,
            BorderThickness = new Thickness(0, 0, 0, 1)
        };

        var titleGrid = new Grid { Margin = new Thickness(14, 0, 8, 0) };
        titleGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        titleGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var titleStack = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            VerticalAlignment = VerticalAlignment.Center
        };

        var icon = new Border
        {
            Width = 24,
            Height = 24,
            CornerRadius = new CornerRadius(6),
            Background = Brush("#2B222A"),
            BorderBrush = Brush("#654C60"),
            BorderThickness = new Thickness(1)
        };

        icon.Child = new TextBlock
        {
            Text = "D",
            Foreground = Accent,
            FontWeight = FontWeights.Bold,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };

        titleStack.Children.Add(icon);
        titleStack.Children.Add(new TextBlock
        {
            Text = "Rollback Diff",
            Foreground = Text,
            FontWeight = FontWeights.SemiBold,
            FontSize = 13,
            Margin = new Thickness(9, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center
        });

        titleGrid.Children.Add(titleStack);

        var titleClose = MakeButton("\u00D7", false, 34);
        titleClose.Height = 30;
        titleClose.Margin = new Thickness(8, 5, 0, 5);
        WindowChrome.SetIsHitTestVisibleInChrome(titleClose, true);
        titleClose.Click += (_, _) => Close();
        Grid.SetColumn(titleClose, 1);
        titleGrid.Children.Add(titleClose);

        titleBar.Child = titleGrid;
        Grid.SetRow(titleBar, 0);
        root.Children.Add(titleBar);

        var header = new Border
        {
            Background = Surface,
            BorderBrush = Border,
            BorderThickness = new Thickness(0, 0, 0, 1),
            Padding = new Thickness(18, 14, 18, 14)
        };

        var headerStack = new StackPanel();
        headerStack.Children.Add(new TextBlock
        {
            Text = "Rollback comparison",
            FontSize = 22,
            FontWeight = FontWeights.SemiBold,
            Foreground = Text
        });
        headerStack.Children.Add(new TextBlock
        {
            Text = $"Remote: {remotePath}\nPrevious copy: {backupPath}",
            Margin = new Thickness(0, 5, 0, 0),
            Foreground = Muted,
            TextWrapping = TextWrapping.Wrap
        });

        header.Child = headerStack;
        Grid.SetRow(header, 1);
        root.Children.Add(header);

        var contentBorder = new Border
        {
            Margin = new Thickness(18, 14, 18, 0),
            Background = Surface,
            BorderBrush = Border,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(5)
        };

        var tabs = new TabControl
        {
            Background = Surface,
            Foreground = Text,
            BorderBrush = Border,
            BorderThickness = new Thickness(0)
        };

        tabs.Items.Add(MakeTab("Diff", BuildDiff(previous, current)));
        tabs.Items.Add(MakeTab("Previous", previous));
        tabs.Items.Add(MakeTab("Current", current));

        contentBorder.Child = tabs;
        Grid.SetRow(contentBorder, 2);
        root.Children.Add(contentBorder);

        var footer = new Border
        {
            Background = Surface,
            BorderBrush = Border,
            BorderThickness = new Thickness(0, 1, 0, 0),
            Padding = new Thickness(18, 12, 18, 12),
            Margin = new Thickness(0, 14, 0, 0)
        };

        var footerGrid = new Grid();
        footerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        footerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        footerGrid.Children.Add(new TextBlock
        {
            Text = BuildSummary(previous, current),
            Foreground = Muted,
            VerticalAlignment = VerticalAlignment.Center
        });

        var close = MakeButton("Close", false, 90);
        close.Click += (_, _) => Close();
        Grid.SetColumn(close, 1);
        footerGrid.Children.Add(close);

        footer.Child = footerGrid;
        Grid.SetRow(footer, 3);
        root.Children.Add(footer);

        shell.Child = root;
        Content = shell;
    }

    private static TabItem MakeTab(string header, string content)
    {
        var tab = new TabItem
        {
            Header = header,
            Background = Surface2,
            Foreground = Text,
            BorderBrush = Border,
            Padding = new Thickness(12, 6, 12, 6),
            Content = new TextBox
            {
                Text = content,
                IsReadOnly = true,
                AcceptsReturn = true,
                AcceptsTab = true,
                TextWrapping = TextWrapping.NoWrap,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
                FontFamily = new FontFamily("Cascadia Mono, Consolas"),
                FontSize = 12,
                Background = Brush("#0C0F11"),
                Foreground = Text,
                CaretBrush = Text,
                SelectionBrush = Brush("#654C60"),
                BorderThickness = new Thickness(0),
                Padding = new Thickness(12)
            }
        };

        return tab;
    }

    private static Button MakeButton(string text, bool primary, double minWidth)
    {
        return new Button
        {
            Content = text,
            MinWidth = minWidth,
            Height = 32,
            Padding = new Thickness(12, 4, 12, 4),
            Background = primary ? Accent : Surface2,
            Foreground = primary ? Brush("#17130A") : Text,
            BorderBrush = primary ? Brush("#C99B3E") : Border,
            BorderThickness = new Thickness(1),
            FontWeight = FontWeights.SemiBold
        };
    }

    private static string BuildSummary(string previous, string current)
    {
        var a = Normalize(previous).Split('\n');
        var b = Normalize(current).Split('\n');
        var max = Math.Max(a.Length, b.Length);
        var changed = 0;

        for (var i = 0; i < max; i++)
        {
            var left = i < a.Length ? a[i] : null;
            var right = i < b.Length ? b[i] : null;
            if (!string.Equals(left, right, StringComparison.Ordinal))
                changed++;
        }

        return previous == current
            ? "No differences - current remote content matches this rollback copy."
            : $"{changed:N0} line position(s) differ. Restore remains a separate confirmed action.";
    }

    private static string BuildDiff(string previous, string current)
    {
        if (previous == current)
            return "  Files are identical.";

        var a = Normalize(previous).Split('\n');
        var b = Normalize(current).Split('\n');
        var max = Math.Max(a.Length, b.Length);
        var sb = new StringBuilder();

        for (var i = 0; i < max; i++)
        {
            var left = i < a.Length ? a[i] : null;
            var right = i < b.Length ? b[i] : null;

            if (string.Equals(left, right, StringComparison.Ordinal))
            {
                sb.Append("  ").AppendLine(left ?? "");
                continue;
            }

            if (left is not null) sb.Append("- ").AppendLine(left);
            if (right is not null) sb.Append("+ ").AppendLine(right);
        }

        return sb.ToString();
    }

    private static string Normalize(string value)
        => value.Replace("\r\n", "\n").Replace('\r', '\n');

    private static Brush Theme(string key, string fallback)
        => Application.Current?.TryFindResource(key) as Brush ?? Brush(fallback);
    private static SolidColorBrush Brush(string value)
        => new((Color)ColorConverter.ConvertFromString(value));
}