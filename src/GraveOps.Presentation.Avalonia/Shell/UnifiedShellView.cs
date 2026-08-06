using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Path = Avalonia.Controls.Shapes.Path;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;

namespace GraveOps.Presentation.Avalonia.Shell;

public sealed class UnifiedShellView :
    UserControl
{
    private const string ExpandedGlyph =
        "M2,4 L6,8 L10,4";

    private const string CollapsedGlyph =
        "M4,2 L8,6 L4,10";

    private const string FallbackIcon =
        "M2,2 L14,2 L14,14 L2,14 Z M5,5 L11,5 M5,8 L11,8 M5,11 L9,11";

    private readonly Image _brandImage;
    private readonly TextBlock _connectionDot;
    private readonly TextBlock _connectionText;
    private readonly TextBlock _connectionDetail;
    private readonly ContentControl _targetHost;
    private readonly StackPanel _navigationPanel;
    private readonly TextBlock _pageTitle;
    private readonly TextBlock _pageSubtitle;
    private readonly ContentControl _pageHost;
    private readonly Grid _overlayHost;
    private readonly TextBlock _footerLeft;
    private readonly TextBlock _footerRight;

    private readonly Dictionary<string, Button>
        _navigationButtons =
            new(
                StringComparer.Ordinal);

    private readonly Dictionary<string, StackPanel>
        _navigationGroups =
            new(
                StringComparer.Ordinal);

    private readonly Dictionary<string, Path>
        _navigationGroupGlyphs =
            new(
                StringComparer.Ordinal);

    private readonly List<IDisposable> _bindings =
        new();

    public UnifiedShellView()
    {
        HorizontalAlignment =
            HorizontalAlignment.Stretch;

        VerticalAlignment =
            VerticalAlignment.Stretch;

        _brandImage =
            new Image
            {
                Width = 24,
                Height = 24,
                Stretch =
                    Stretch.Uniform,
                VerticalAlignment =
                    VerticalAlignment.Center
            };

        _connectionDot =
            new TextBlock
            {
                Text = "\u25CF",
                FontSize = 9,
                VerticalAlignment =
                    VerticalAlignment.Center
            };

        _connectionText =
            new TextBlock
            {
                Text = "CHECKING",
                FontSize = 12,
                FontWeight =
                    FontWeight.SemiBold
            };

        _connectionDetail =
            new TextBlock
            {
                Text =
                    "Provider state is loading",
                FontSize = 9,
                Margin =
                    new Thickness(
                        0,
                        3,
                        0,
                        0),
                TextTrimming =
                    TextTrimming.CharacterEllipsis,
                Classes =
                {
                    "dim"
                }
            };

        _targetHost =
            new ContentControl
            {
                MinHeight = 50,
                HorizontalAlignment =
                    HorizontalAlignment.Stretch
            };

        _navigationPanel =
            new StackPanel
            {
                Margin =
                    new Thickness(
                        0,
                        0,
                        10,
                        0)
            };

        _pageTitle =
            new TextBlock
            {
                Text = "Dashboard",
                FontSize = 22,
                FontWeight =
                    FontWeight.SemiBold
            };

        _pageSubtitle =
            new TextBlock
            {
                Text =
                    "Interactive environment health, ownership and active-host operations",
                Margin =
                    new Thickness(
                        0,
                        3,
                        0,
                        0),
                Classes =
                {
                    "pageSubtitle"
                }
            };

        _pageHost =
            new ContentControl
            {
                HorizontalContentAlignment =
                    HorizontalAlignment.Stretch,
                VerticalContentAlignment =
                    VerticalAlignment.Stretch
            };

        _overlayHost =
            new Grid
            {
                RowDefinitions =
                    new RowDefinitions(
                        "72,42,*,26"),
                IsHitTestVisible =
                    true
            };

        _footerLeft =
            new TextBlock
            {
                Text =
                    "Waiting for target",
                FontSize = 9.5,
                VerticalAlignment =
                    VerticalAlignment.Center,
                TextTrimming =
                    TextTrimming.CharacterEllipsis,
                Classes =
                {
                    "dim"
                }
            };

        _footerRight =
            new TextBlock
            {
                Text =
                    "READ-ONLY",
                FontSize = 9,
                FontWeight =
                    FontWeight.SemiBold,
                VerticalAlignment =
                    VerticalAlignment.Center
            };

        var frame =
            BuildFrame();

        Content =
            frame;

        UpdateConnectionBrush(
            _connectionText.Text);
    }

    public event EventHandler<UnifiedShellNavigationRequestedEventArgs>?
        NavigationRequested;

    public event EventHandler<UnifiedShellCommandRequestedEventArgs>?
        CommandRequested;

    public void SetBrandImage(
        IImage? image) =>
        _brandImage.Source =
            image;

    public void AttachTargetSelector(
        Control targetSelector)
    {
        Detach(
            targetSelector);

        _targetHost.Content =
            targetSelector;
    }

    public void AttachPageContent(
        Control pageContent)
    {
        Detach(
            pageContent);

        _pageHost.Content =
            pageContent;
    }

    public void AttachOverlays(
        IEnumerable<Control> overlays)
    {
        foreach (var overlay in
                 overlays.ToArray())
        {
            Detach(
                overlay);

            _overlayHost.Children.Add(
                overlay);
        }
    }

    public void BindPageHeader(
        TextBlock title,
        TextBlock subtitle)
    {
        _bindings.Add(
            _pageTitle.Bind(
                TextBlock.TextProperty,
                title.GetObservable(
                    TextBlock.TextProperty)));

        _bindings.Add(
            _pageSubtitle.Bind(
                TextBlock.TextProperty,
                subtitle.GetObservable(
                    TextBlock.TextProperty)));
    }

    public void BindConnection(
        TextBlock status,
        TextBlock detail)
    {
        _bindings.Add(
            _connectionText.Bind(
                TextBlock.TextProperty,
                status.GetObservable(
                    TextBlock.TextProperty)));

        _bindings.Add(
            _connectionDetail.Bind(
                TextBlock.TextProperty,
                detail.GetObservable(
                    TextBlock.TextProperty)));

        _bindings.Add(
            status.GetObservable(
                    TextBlock.TextProperty)
                .Subscribe(
                    new DelegateObserver<string?>(
                        UpdateConnectionBrush)));

        UpdateConnectionBrush(
            status.Text);
    }

    public void BindFooter(
        TextBlock leftSource,
        string rightText)
    {
        _bindings.Add(
            _footerLeft.Bind(
                TextBlock.TextProperty,
                leftSource.GetObservable(
                    TextBlock.TextProperty)));

        _footerRight.Text =
            rightText;
    }

    public void BindFooter(
        Func<UnifiedShellFooterState> provider,
        params TextBlock[] sources)
    {
        void Update()
        {
            var state =
                provider();

            _footerLeft.Text =
                state.Left;

            _footerRight.Text =
                state.Right;
        }

        foreach (var source in sources)
        {
            _bindings.Add(
                source.GetObservable(
                        TextBlock.TextProperty)
                    .Subscribe(
                        new DelegateObserver<string?>(
                            _ =>
                                Update())));
        }

        Update();
    }

    public void BindNavigation(
        ScrollViewer legacyNavigation)
    {
        _navigationPanel.Children.Clear();
        _navigationButtons.Clear();
        _navigationGroups.Clear();
        _navigationGroupGlyphs.Clear();

        var nodes =
            LegacyNavigationProjection.Project(
                legacyNavigation);

        string? initiallySelected =
            null;

        foreach (var node in nodes)
        {
            switch (node.Kind)
            {
                case LegacyNavigationNodeKind.Section:
                    _navigationPanel.Children.Add(
                        BuildSection(
                            node.Label));
                    break;

                case LegacyNavigationNodeKind.Group:
                    BuildGroup(
                        node);
                    break;

                case LegacyNavigationNodeKind.Item:
                    var button =
                        BuildNavigationButton(
                            node);

                    if (node.GroupKey is not null &&
                        _navigationGroups.TryGetValue(
                            node.GroupKey,
                            out var group))
                    {
                        group.Children.Add(
                            button);
                    }
                    else
                    {
                        _navigationPanel.Children.Add(
                            button);
                    }

                    if (node.SourceButton?.Classes.Contains(
                            "selected") ==
                        true)
                    {
                        initiallySelected =
                            node.Key;
                    }

                    break;
            }
        }

        if (!string.IsNullOrWhiteSpace(
                initiallySelected))
        {
            SelectNavigation(
                initiallySelected);
        }
    }

    public void SelectNavigation(
        string navigationKey)
    {
        foreach (var item in
                 _navigationButtons)
        {
            item.Value.Classes.Set(
                "selected",
                item.Key.Equals(
                    navigationKey,
                    StringComparison.Ordinal));
        }
    }

    private Border BuildFrame()
    {
        var root =
            new Grid
            {
                RowDefinitions =
                    new RowDefinitions(
                        "38,*")
            };

        root.Children.Add(
            BuildTitleBar());

        var body =
            new Grid
            {
                RowDefinitions =
                    new RowDefinitions(
                        "*"),
                ColumnDefinitions =
                    new ColumnDefinitions(
                        "260,*")
            };

        Grid.SetRow(
            body,
            1);

        body.Children.Add(
            BuildSidebar());

        var main =
            BuildMainWorkspace();

        Grid.SetColumn(
            main,
            1);

        body.Children.Add(
            main);

        root.Children.Add(
            body);

        var frame =
            new Border
            {
                BorderThickness =
                    new Thickness(1)
            };

        frame.Bind(
            Border.BackgroundProperty,
            this.GetResourceObservable(
                "BackgroundBrush"));

        frame.Bind(
            Border.BorderBrushProperty,
            this.GetResourceObservable(
                "BorderBrush"));

        frame.Child =
            root;

        return frame;
    }

    private Border BuildTitleBar()
    {
        var brand =
            new StackPanel
            {
                Orientation =
                    Orientation.Horizontal,
                VerticalAlignment =
                    VerticalAlignment.Center,
                Margin =
                    new Thickness(
                        14,
                        0,
                        0,
                        0)
            };

        brand.Children.Add(
            _brandImage);

        brand.Children.Add(
            new TextBlock
            {
                Text = "GraveOps",
                FontWeight =
                    FontWeight.SemiBold,
                FontSize = 12,
                Margin =
                    new Thickness(
                        9,
                        0,
                        0,
                        0),
                VerticalAlignment =
                    VerticalAlignment.Center
            });

        brand.Children.Add(
            new TextBlock
            {
                Text =
                    "Control Center",
                FontSize = 11,
                Margin =
                    new Thickness(
                        7,
                        0,
                        0,
                        0),
                VerticalAlignment =
                    VerticalAlignment.Center,
                Classes =
                {
                    "dim"
                }
            });

        var dragRegion =
            new Border
            {
                Background =
                    Brushes.Transparent
            };

        dragRegion.PointerPressed +=
            TitleDragRegionOnPointerPressed;

        dragRegion.DoubleTapped +=
            (_, _) =>
                ToggleMaximized();

        Grid.SetColumn(
            dragRegion,
            1);

        var commands =
            new StackPanel
            {
                Orientation =
                    Orientation.Horizontal
            };

        commands.Children.Add(
            BuildTitleButton(
                "Minimize",
                "M2,7 L12,7",
                _ =>
                {
                    if (OwnerWindow() is { } window)
                    {
                        window.WindowState =
                            WindowState.Minimized;
                    }
                }));

        commands.Children.Add(
            BuildTitleButton(
                "Maximize or restore",
                "M2.5,2.5 L11.5,2.5 L11.5,11.5 L2.5,11.5 Z",
                _ =>
                    ToggleMaximized()));

        var close =
            BuildTitleButton(
                "Close",
                "M3,3 L11,11 M11,3 L3,11",
                _ =>
                    OwnerWindow()?.Close());

        close.Classes.Add(
            "close");

        commands.Children.Add(
            close);

        Grid.SetColumn(
            commands,
            2);

        var content =
            new Grid
            {
                ColumnDefinitions =
                    new ColumnDefinitions(
                        "Auto,*,Auto")
            };

        content.Children.Add(
            brand);

        content.Children.Add(
            dragRegion);

        content.Children.Add(
            commands);

        var titleBar =
            new Border
            {
                BorderThickness =
                    new Thickness(
                        0,
                        0,
                        0,
                        1),
                Child =
                    content
            };

        titleBar.Bind(
            Border.BackgroundProperty,
            this.GetResourceObservable(
                "HeaderBrush"));

        titleBar.Bind(
            Border.BorderBrushProperty,
            this.GetResourceObservable(
                "BorderBrush"));

        return titleBar;
    }

    private Border BuildSidebar()
    {
        var connection =
            new StackPanel
            {
                Orientation =
                    Orientation.Horizontal,
                Margin =
                    new Thickness(
                        0,
                        5,
                        0,
                        0),
                Spacing = 7
            };

        connection.Children.Add(
            _connectionDot);

        connection.Children.Add(
            _connectionText);

        var identity =
            new StackPanel
            {
                Margin =
                    new Thickness(
                        11,
                        0,
                        0,
                        0),
                VerticalAlignment =
                    VerticalAlignment.Center
            };

        identity.Children.Add(
            new TextBlock
            {
                Text =
                    "CONTROL PLANE",
                Classes =
                {
                    "eyebrow"
                }
            });

        identity.Children.Add(
            connection);

        identity.Children.Add(
            _connectionDetail);

        var identityGrid =
            new Grid
            {
                ColumnDefinitions =
                    new ColumnDefinitions(
                        "Auto,*")
            };

        var icon =
            new Border
            {
                Width = 34,
                Height = 34,
                CornerRadius =
                    new CornerRadius(9)
            };

        icon.Bind(
            Border.BackgroundProperty,
            this.GetResourceObservable(
                "AccentTintBrush"));

        var iconPath =
            new Path
            {
                Data =
                    Geometry.Parse(
                        "M7,8 L19,8 M7,12 L19,12 M7,16 L15,16"),
                StrokeThickness = 1.6,
                StrokeLineCap =
                    PenLineCap.Round,
                Stretch =
                    Stretch.None,
                HorizontalAlignment =
                    HorizontalAlignment.Center,
                VerticalAlignment =
                    VerticalAlignment.Center
            };

        iconPath.Bind(
            Shape.StrokeProperty,
            this.GetResourceObservable(
                "AccentBrush"));

        icon.Child =
            iconPath;

        identityGrid.Children.Add(
            icon);

        Grid.SetColumn(
            identity,
            1);

        identityGrid.Children.Add(
            identity);

        var targetCardContent =
            new StackPanel();

        targetCardContent.Children.Add(
            identityGrid);

        targetCardContent.Children.Add(
            new TextBlock
            {
                Text =
                    "ACTIVE SERVER",
                Margin =
                    new Thickness(
                        0,
                        13,
                        0,
                        5),
                Classes =
                {
                    "eyebrow"
                }
            });

        targetCardContent.Children.Add(
            _targetHost);

        var targetCard =
            new Border
            {
                Padding =
                    new Thickness(12),
                Margin =
                    new Thickness(
                        0,
                        0,
                        0,
                        17),
                Child =
                    targetCardContent,
                Classes =
                {
                    "flatCard"
                }
            };

        var navigation =
            new ScrollViewer
            {
                ClipToBounds = true,
                VerticalScrollBarVisibility =
                    global::Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility =
                    global::Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled,
                Content =
                    _navigationPanel
            };

        Grid.SetRow(
            navigation,
            1);

        var product =
            new StackPanel
            {
                Margin =
                    new Thickness(
                        10,
                        12,
                        0,
                        0)
            };

        product.Children.Add(
            new TextBlock
            {
                Text = "GRAVEOPS",
                Classes =
                {
                    "eyebrow"
                }
            });

        product.Children.Add(
            new TextBlock
            {
                Text =
                    "Native + remote providers",
                FontSize = 10,
                Margin =
                    new Thickness(
                        0,
                        4,
                        0,
                        0),
                Classes =
                {
                    "dim"
                }
            });

        Grid.SetRow(
            product,
            2);

        var sidebarGrid =
            new Grid
            {
                RowDefinitions =
                    new RowDefinitions(
                        "Auto,*,Auto"),
                Margin =
                    new Thickness(
                        14,
                        15,
                        14,
                        14)
            };

        sidebarGrid.Children.Add(
            targetCard);

        sidebarGrid.Children.Add(
            navigation);

        sidebarGrid.Children.Add(
            product);

        var sidebar =
            new Border
            {
                BorderThickness =
                    new Thickness(
                        0,
                        0,
                        1,
                        0),
                Child =
                    sidebarGrid
            };

        sidebar.Bind(
            Border.BackgroundProperty,
            this.GetResourceObservable(
                "SidebarBrush"));

        sidebar.Bind(
            Border.BorderBrushProperty,
            this.GetResourceObservable(
                "BorderBrush"));

        return sidebar;
    }

    private Grid BuildMainWorkspace()
    {
        var main =
            new Grid
            {
                RowDefinitions =
                    new RowDefinitions(
                        "72,42,*,26")
            };

        main.Bind(
            Panel.BackgroundProperty,
            this.GetResourceObservable(
                "BackgroundBrush"));

        main.Children.Add(
            BuildPageHeader());

        var quick =
            BuildQuickHeader();

        Grid.SetRow(
            quick,
            1);

        main.Children.Add(
            quick);

        Grid.SetRow(
            _pageHost,
            2);

        main.Children.Add(
            _pageHost);

        var footer =
            BuildFooter();

        Grid.SetRow(
            footer,
            3);

        main.Children.Add(
            footer);

        Grid.SetRowSpan(
            _overlayHost,
            4);

        _overlayHost.ZIndex =
            500;

        main.Children.Add(
            _overlayHost);

        return main;
    }

    private Border BuildPageHeader()
    {
        var titles =
            new StackPanel
            {
                VerticalAlignment =
                    VerticalAlignment.Center
            };

        titles.Children.Add(
            _pageTitle);

        titles.Children.Add(
            _pageSubtitle);

        var commands =
            new StackPanel
            {
                Orientation =
                    Orientation.Horizontal,
                VerticalAlignment =
                    VerticalAlignment.Center,
                Spacing = 7
            };

        commands.Children.Add(
            BuildCommandButton(
                "Overview",
                "Overview",
                "commandOverview",
                68));

        commands.Children.Add(
            BuildCommandButton(
                "Jobs",
                "Jobs",
                "commandJobs",
                52));

        commands.Children.Add(
            BuildCommandButton(
                "Findings",
                "Findings",
                "commandIntelligence",
                84));

        commands.Children.Add(
            BuildCommandButton(
                "Activity",
                "Activity",
                "commandActivity",
                72));

        commands.Children.Add(
            BuildCommandButton(
                "Terminal",
                "Terminal",
                "commandTerminal",
                88));

        Grid.SetColumn(
            commands,
            1);

        var grid =
            new Grid
            {
                ColumnDefinitions =
                    new ColumnDefinitions(
                        "*,Auto"),
                Margin =
                    new Thickness(
                        28,
                        0)
            };

        grid.Children.Add(
            titles);

        grid.Children.Add(
            commands);

        var header =
            new Border
            {
                BorderThickness =
                    new Thickness(
                        0,
                        0,
                        0,
                        1),
                Child =
                    grid
            };

        header.Bind(
            Border.BackgroundProperty,
            this.GetResourceObservable(
                "HeaderBrush"));

        header.Bind(
            Border.BorderBrushProperty,
            this.GetResourceObservable(
                "BorderBrush"));

        return header;
    }

    private Border BuildQuickHeader()
    {
        var quickLabel =
            new TextBlock
            {
                Text = "QUICK",
                Margin =
                    new Thickness(
                        0,
                        0,
                        12,
                        0),
                VerticalAlignment =
                    VerticalAlignment.Center,
                Classes =
                {
                    "eyebrow"
                }
            };

        var maintenance =
            BuildQuickButton(
                "Maintenance",
                "Maintenance");

        Grid.SetColumn(
            maintenance,
            1);

        var shortcut =
            new TextBlock
            {
                Text =
                    "Ctrl+K search",
                FontSize = 10,
                Margin =
                    new Thickness(
                        0,
                        0,
                        8,
                        0),
                VerticalAlignment =
                    VerticalAlignment.Center,
                Classes =
                {
                    "dim"
                }
            };

        Grid.SetColumn(
            shortcut,
            3);

        var search =
            BuildQuickButton(
                "Search",
                "Search");

        Grid.SetColumn(
            search,
            4);

        var customize =
            BuildQuickButton(
                "Customize",
                "Customize");

        Grid.SetColumn(
            customize,
            5);

        var grid =
            new Grid
            {
                ColumnDefinitions =
                    new ColumnDefinitions(
                        "Auto,Auto,*,Auto,Auto,Auto"),
                Margin =
                    new Thickness(
                        28,
                        0)
            };

        grid.Children.Add(
            quickLabel);

        grid.Children.Add(
            maintenance);

        grid.Children.Add(
            shortcut);

        grid.Children.Add(
            search);

        grid.Children.Add(
            customize);

        var header =
            new Border
            {
                BorderThickness =
                    new Thickness(
                        0,
                        0,
                        0,
                        1),
                Child =
                    grid
            };

        header.Bind(
            Border.BackgroundProperty,
            this.GetResourceObservable(
                "HeaderBrush"));

        header.Bind(
            Border.BorderBrushProperty,
            this.GetResourceObservable(
                "BorderBrush"));

        return header;
    }

    private Border BuildFooter()
    {
        var grid =
            new Grid
            {
                ColumnDefinitions =
                    new ColumnDefinitions(
                        "*,Auto"),
                Margin =
                    new Thickness(
                        28,
                        0)
            };

        grid.Children.Add(
            _footerLeft);

        Grid.SetColumn(
            _footerRight,
            1);

        grid.Children.Add(
            _footerRight);

        var footer =
            new Border
            {
                BorderThickness =
                    new Thickness(
                        0,
                        1,
                        0,
                        0),
                Child =
                    grid
            };

        footer.Bind(
            Border.BackgroundProperty,
            this.GetResourceObservable(
                "HeaderBrush"));

        footer.Bind(
            Border.BorderBrushProperty,
            this.GetResourceObservable(
                "BorderBrush"));

        _footerRight.Bind(
            TextBlock.ForegroundProperty,
            this.GetResourceObservable(
                "SuccessBrush"));

        return footer;
    }

    private TextBlock BuildSection(
        string label) =>
        new()
        {
            Text = label,
            Margin =
                new Thickness(
                    10,
                    _navigationPanel.Children.Count == 0
                        ? 0
                        : 15,
                    0,
                    6),
            Classes =
            {
                "eyebrow"
            }
        };

    private void BuildGroup(
        LegacyNavigationNode node)
    {
        var groupPanel =
            new StackPanel
            {
                IsVisible =
                    node.SourceGroupPanel?.IsVisible ??
                    true
            };

        var glyph =
            new Path
            {
                Width = 12,
                Height = 12,
                Data =
                    Geometry.Parse(
                        groupPanel.IsVisible
                            ? ExpandedGlyph
                            : CollapsedGlyph),
                StrokeThickness = 1.45,
                StrokeLineCap =
                    PenLineCap.Round,
                StrokeJoin =
                    PenLineJoin.Round,
                Stretch =
                    Stretch.Uniform
            };

        var label =
            new TextBlock
            {
                Text =
                    node.Label,
                VerticalAlignment =
                    VerticalAlignment.Center
            };

        var content =
            new Grid
            {
                ColumnDefinitions =
                    new ColumnDefinitions(
                        "18,*")
            };

        content.Children.Add(
            glyph);

        Grid.SetColumn(
            label,
            1);

        content.Children.Add(
            label);

        var button =
            new Button
            {
                Content =
                    content,
                Classes =
                {
                    "navGroup"
                }
            };

        glyph.Bind(
            Shape.StrokeProperty,
            button.GetObservable(
                Button.ForegroundProperty));

        label.Bind(
            TextBlock.ForegroundProperty,
            button.GetObservable(
                Button.ForegroundProperty));

        if (node.SourceButton is not null)
        {
            _bindings.Add(
                button.Bind(
                    Visual.IsVisibleProperty,
                    node.SourceButton.GetObservable(
                        Visual.IsVisibleProperty)));
        }

        button.Click +=
            (_, _) =>
            {
                groupPanel.IsVisible =
                    !groupPanel.IsVisible;

                glyph.Data =
                    Geometry.Parse(
                        groupPanel.IsVisible
                            ? ExpandedGlyph
                            : CollapsedGlyph);
            };

        _navigationGroups[node.Key] =
            groupPanel;

        _navigationGroupGlyphs[node.Key] =
            glyph;

        _navigationPanel.Children.Add(
            button);

        _navigationPanel.Children.Add(
            groupPanel);
    }

    private Button BuildNavigationButton(
        LegacyNavigationNode node)
    {
        var icon =
            new Path
            {
                Width = 16,
                Height = 16,
                Stretch =
                    Stretch.Uniform,
                Data =
                    node.IconGeometry ??
                    Geometry.Parse(
                        FallbackIcon),
                StrokeThickness =
                    node.FillIcon
                        ? 0
                        : 1.45,
                StrokeLineCap =
                    PenLineCap.Round,
                StrokeJoin =
                    PenLineJoin.Round
            };

        var label =
            new TextBlock
            {
                Text =
                    node.Label,
                VerticalAlignment =
                    VerticalAlignment.Center
            };

        var content =
            new Grid
            {
                ColumnDefinitions =
                    new ColumnDefinitions(
                        "24,*")
            };

        content.Children.Add(
            icon);

        Grid.SetColumn(
            label,
            1);

        content.Children.Add(
            label);

        var button =
            new Button
            {
                Content =
                    content,
                Classes =
                {
                    "nav"
                }
            };

        if (node.GroupKey is not null)
            button.Classes.Add("sub");

        if (node.FillIcon)
        {
            icon.Bind(
                Shape.FillProperty,
                button.GetObservable(
                    Button.ForegroundProperty));
        }
        else
        {
            icon.Bind(
                Shape.StrokeProperty,
                button.GetObservable(
                    Button.ForegroundProperty));
        }

        label.Bind(
            TextBlock.ForegroundProperty,
            button.GetObservable(
                Button.ForegroundProperty));

        if (node.SourceButton is not null)
        {
            _bindings.Add(
                button.Bind(
                    Visual.IsVisibleProperty,
                    node.SourceButton.GetObservable(
                        Visual.IsVisibleProperty)));
        }

        button.Click +=
            (_, _) =>
                NavigationRequested?.Invoke(
                    this,
                    new UnifiedShellNavigationRequestedEventArgs(
                        node.Key));

        _navigationButtons[node.Key] =
            button;

        return button;
    }

    private Button BuildCommandButton(
        string label,
        string commandKey,
        string className,
        double minWidth)
    {
        var button =
            new Button
            {
                Content =
                    label,
                MinWidth =
                    minWidth,
                Classes =
                {
                    "compact",
                    "headerCommand",
                    className
                }
            };

        button.Click +=
            (_, _) =>
                CommandRequested?.Invoke(
                    this,
                    new UnifiedShellCommandRequestedEventArgs(
                        commandKey));

        return button;
    }

    private Button BuildQuickButton(
        string label,
        string commandKey)
    {
        var button =
            new Button
            {
                Content =
                    label,
                Padding =
                    new Thickness(
                        8,
                        4),
                Classes =
                {
                    "ghost"
                }
            };

        button.Click +=
            (_, _) =>
                CommandRequested?.Invoke(
                    this,
                    new UnifiedShellCommandRequestedEventArgs(
                        commandKey));

        return button;
    }

    private Button BuildTitleButton(
        string tooltip,
        string pathData,
        Action<RoutedEventArgs> action)
    {
        var button =
            new Button
            {
                Classes =
                {
                    "titlebar"
                }
            };

        ToolTip.SetTip(
            button,
            tooltip);

        var icon =
            new Path
            {
                Data =
                    Geometry.Parse(
                        pathData),
                StrokeThickness = 1.2,
                StrokeLineCap =
                    PenLineCap.Round,
                Stretch =
                    Stretch.None
            };

        icon.Bind(
            Shape.StrokeProperty,
            button.GetObservable(
                Button.ForegroundProperty));

        button.Content =
            icon;

        button.Click +=
            (_, e) =>
                action(
                    e);

        return button;
    }

    private void TitleDragRegionOnPointerPressed(
        object? sender,
        PointerPressedEventArgs e)
    {
        if (OwnerWindow() is { } window &&
            e.GetCurrentPoint(
                    window)
                .Properties
                .IsLeftButtonPressed)
        {
            window.BeginMoveDrag(
                e);
        }
    }

    private void ToggleMaximized()
    {
        if (OwnerWindow() is not { } window)
            return;

        window.WindowState =
            window.WindowState ==
                WindowState.Maximized
                ? WindowState.Normal
                : WindowState.Maximized;
    }

    private Window? OwnerWindow() =>
        TopLevel.GetTopLevel(
            this) as Window;

    private void UpdateConnectionBrush(
        string? value)
    {
        var healthy =
            value?.Contains(
                "READY",
                StringComparison.OrdinalIgnoreCase) ==
                true ||
            value?.Contains(
                "HEALTHY",
                StringComparison.OrdinalIgnoreCase) ==
                true ||
            value?.Contains(
                "CONNECTED",
                StringComparison.OrdinalIgnoreCase) ==
                true;

        var key =
            healthy
                ? "SuccessBrush"
                : "WarnBrush";

        var fallback =
            healthy
                ? Brushes.LimeGreen
                : Brushes.Goldenrod;

        var brush =
            this.TryFindResource(
                key,
                ActualThemeVariant,
                out var resource) &&
            resource is IBrush found
                ? found
                : fallback;

        _connectionDot.Foreground =
            brush;

        _connectionText.Foreground =
            brush;
    }

    private static void Detach(
        Control control)
    {
        switch (control.Parent)
        {
            case Panel panel:
                panel.Children.Remove(
                    control);
                break;

            case Decorator decorator
                when ReferenceEquals(
                    decorator.Child,
                    control):
                decorator.Child =
                    null;
                break;

            case ContentControl content
                when ReferenceEquals(
                    content.Content,
                    control):
                content.Content =
                    null;
                break;
        }
    }
}