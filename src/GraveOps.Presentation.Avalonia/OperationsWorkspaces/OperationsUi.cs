using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;

namespace GraveOps.Presentation.Avalonia.OperationsWorkspaces;

internal static class OperationsUi
{
    public static TextBlock Title(
        string text,
        double size = 16) =>
        new()
        {
            Text = text,
            FontSize = size,
            FontWeight = FontWeight.SemiBold,
            Classes =
            {
                "sectionTitle"
            }
        };

    public static TextBlock Subtitle(
        string text) =>
        new()
        {
            Text = text,
            TextWrapping = TextWrapping.Wrap,
            Classes =
            {
                "pageSubtitle"
            }
        };

    public static TextBlock Eyebrow(
        string text) =>
        new()
        {
            Text = text,
            Classes =
            {
                "eyebrow"
            }
        };

    public static TextBlock Muted(
        string text) =>
        new()
        {
            Text = text,
            TextWrapping = TextWrapping.Wrap,
            Classes =
            {
                "muted"
            }
        };

    public static TextBlock Dim(
        string text) =>
        new()
        {
            Text = text,
            TextWrapping = TextWrapping.Wrap,
            Classes =
            {
                "dim"
            }
        };

    public static TextBlock MetricValue(
        string text = "0") =>
        new()
        {
            Text = text,
            FontSize = 23,
            Margin = new Thickness(
                0,
                7,
                0,
                0),
            Classes =
            {
                "metricValue"
            }
        };

    public static Border Metric(
        string label,
        TextBlock value) =>
        new()
        {
            Classes =
            {
                "metric"
            },
            Child =
                new StackPanel
                {
                    Children =
                    {
                        Eyebrow(label),
                        value
                    }
                }
        };

    public static Border Module(
        Control child,
        double padding = 12) =>
        new()
        {
            Classes =
            {
                "module",
                "adaptive"
            },
            Padding = new Thickness(
                padding),
            Child = child
        };

    public static Border Inset(
        Control child,
        double padding = 10) =>
        new()
        {
            Classes =
            {
                "inset"
            },
            Padding = new Thickness(
                padding),
            Child = child
        };

    public static ScrollViewer Scroll(
        Control content,
        double? maxHeight = null)
    {
        var viewer =
            new ScrollViewer
            {
                VerticalScrollBarVisibility =
                    ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility =
                    ScrollBarVisibility.Disabled,
                Content = content
            };

        if (maxHeight.HasValue)
            viewer.MaxHeight = maxHeight.Value;

        return viewer;
    }

    public static Button RowButton(
        Control content) =>
        new()
        {
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(
                0),
            Padding = new Thickness(
                0),
            HorizontalContentAlignment =
                HorizontalAlignment.Stretch,
            Content = content
        };

    public static TextBox Console(
        string text,
        double minHeight = 120,
        double maxHeight = 400) =>
        new()
        {
            Text = text,
            MinHeight = minHeight,
            MaxHeight = maxHeight,
            IsReadOnly = true,
            AcceptsReturn = true,
            TextWrapping = TextWrapping.Wrap,
            Classes =
            {
                "console",
                "workspaceOutput"
            }
        };

    public static Button Compact(
        string text) =>
        new()
        {
            Content = text,
            Classes =
            {
                "compact"
            }
        };

    public static TextBlock ColumnHeader(
        string text,
        int column)
    {
        var block =
            new TextBlock
            {
                Text = text,
                Classes =
                {
                    "tableColumnHeader"
                }
            };

        Grid.SetColumn(
            block,
            column);

        return block;
    }

    public static TextBlock Cell(
        string text,
        int column,
        bool strong = false,
        string? cssClass = null)
    {
        var block =
            new TextBlock
            {
                Text = text,
                FontWeight =
                    strong
                        ? FontWeight.SemiBold
                        : FontWeight.Normal,
                TextTrimming =
                    TextTrimming.CharacterEllipsis
            };

        if (!string.IsNullOrWhiteSpace(
                cssClass))
        {
            block.Classes.Add(
                cssClass);
        }

        Grid.SetColumn(
            block,
            column);

        return block;
    }

    public static Border EmptyState(
        string title,
        string detail) =>
        new()
        {
            Classes =
            {
                "emptyState"
            },
            Child =
                new StackPanel
                {
                    Spacing = 4,
                    Children =
                    {
                        new TextBlock
                        {
                            Text = title,
                            FontWeight =
                                FontWeight.SemiBold
                        },
                        Muted(
                            detail)
                    }
                }
        };
}
