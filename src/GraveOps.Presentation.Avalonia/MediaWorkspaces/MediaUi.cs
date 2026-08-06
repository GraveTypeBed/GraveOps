using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;

namespace GraveOps.Presentation.Avalonia.MediaWorkspaces;

internal static class MediaUi
{
    public static TextBlock PageTitle(
        string text) =>
        new()
        {
            Text = text,
            Classes =
            {
                "pageTitle"
            }
        };

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
        string text = "--",
        double fontSize = 23) =>
        new()
        {
            Text = text,
            FontSize = fontSize,
            FontWeight = FontWeight.SemiBold,
            Margin = new Thickness(
                0,
                7,
                0,
                0),
            TextTrimming =
                TextTrimming.CharacterEllipsis
        };

    public static Border Metric(
        string label,
        TextBlock value,
        string detail = "") =>
        new()
        {
            Classes =
            {
                "flatCard",
                "metric"
            },
            Child =
                new StackPanel
                {
                    Children =
                    {
                        Eyebrow(label),
                        value,
                        Dim(detail)
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

    public static Border FlatCard(
        Control child,
        double padding = 12) =>
        new()
        {
            Classes =
            {
                "flatCard"
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
        double? maxHeight = null,
        bool horizontal = false)
    {
        var viewer =
            new ScrollViewer
            {
                VerticalScrollBarVisibility =
                    ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility =
                    horizontal
                        ? ScrollBarVisibility.Auto
                        : ScrollBarVisibility.Disabled,
                Content = content
            };

        if (maxHeight.HasValue)
            viewer.MaxHeight = maxHeight.Value;

        return viewer;
    }

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

    public static Button Primary(
        string text) =>
        new()
        {
            Content = text,
            Classes =
            {
                "primary",
                "compact"
            }
        };

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
                    TextTrimming.CharacterEllipsis,
                VerticalAlignment =
                    VerticalAlignment.Center
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

    public static TextBlock HeaderCell(
        string text,
        int column)
    {
        var block =
            new TextBlock
            {
                Text = text,
                Classes =
                {
                    "tableHeader"
                }
            };

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

    public static TextBox Console(
        string text,
        double minHeight = 100,
        double maxHeight = 360) =>
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

    public static void SetColumn(
        Control control,
        int column)
    {
        Grid.SetColumn(
            control,
            column);
    }

    public static void SetRow(
        Control control,
        int row)
    {
        Grid.SetRow(
            control,
            row);
    }

    public static Grid FourMetrics(
        params Border[] metrics)
    {
        var grid =
            new Grid
            {
                ColumnDefinitions =
                    new ColumnDefinitions(
                        "*,*,*,*"),
                ColumnSpacing = 10
            };

        for (var index = 0;
             index < metrics.Length && index < 4;
             index++)
        {
            Grid.SetColumn(
                metrics[index],
                index);

            grid.Children.Add(
                metrics[index]);
        }

        return grid;
    }

    public static Grid TwoColumns(
        Control left,
        Control right,
        string columns = "*,*",
        double spacing = 10)
    {
        var grid =
            new Grid
            {
                ColumnDefinitions =
                    new ColumnDefinitions(
                        columns),
                ColumnSpacing =
                    spacing
            };

        Grid.SetColumn(
            right,
            1);

        grid.Children.Add(
            left);

        grid.Children.Add(
            right);

        return grid;
    }
}
