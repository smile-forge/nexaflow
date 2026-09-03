using Nexaflow.Visuals.Text.Markdown.Graphs.Layout;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;

namespace Nexaflow.Visuals.Text.Markdown.Graphs.Rendering;

/// <summary>
/// Converts a <see cref="LayoutedGraph"/> to a WPF <see cref="Canvas"/>.
///
/// Designed to be stateless: call <see cref="Render"/> and get a self-contained
/// <see cref="FrameworkElement"/> back.  No WebView dependency.
/// </summary>
public static class WpfGraphRenderer
{
    // ── Theme ───────────────────────────────────────────────────────────────
    // Set once per render from the MarkdownPalette. Markdown renders synchronously on the UI thread,
    // so these shared statics are never touched concurrently.

    private static Brush BgBrush        = DiagramBrushes.Frozen(Color.FromRgb(0x0D, 0x10, 0x1A));
    private static Brush NodeBg         = DiagramBrushes.Frozen(Color.FromRgb(0x1E, 0x24, 0x38));
    private static Brush NodeBorder     = DiagramBrushes.Frozen(Color.FromRgb(0x4F, 0x8E, 0xF7));
    private static Brush NodeText       = DiagramBrushes.Frozen(Color.FromRgb(0xE8, 0xEA, 0xF2));
    private static Brush DiamondBg      = DiagramBrushes.Frozen(Color.FromRgb(0x2A, 0x1A, 0x3A));
    private static Brush DiamondBorder  = DiagramBrushes.Frozen(Color.FromRgb(0xA0, 0x60, 0xFF));
    private static Brush EdgeBrush      = DiagramBrushes.Frozen(Color.FromRgb(0x4F, 0x8E, 0xF7));
    private static Brush EdgeDashedBrush= DiagramBrushes.Frozen(Color.FromRgb(0x78, 0x80, 0xA0));
    private static Brush EdgeThickBrush = DiagramBrushes.Frozen(Color.FromRgb(0xFF, 0xD0, 0x60));
    private static Brush LabelBg        = DiagramBrushes.Frozen(Color.FromRgb(0x12, 0x16, 0x24));
    private static Brush LabelText      = DiagramBrushes.Frozen(Color.FromRgb(0x78, 0x80, 0xA0));
    private static Brush TitleBrush     = DiagramBrushes.Frozen(Color.FromRgb(0xA8, 0xD4, 0xFF));
    private static Color AccentColor    = Color.FromRgb(0x4F, 0x8E, 0xF7);
    // State-diagram extras: solid pseudostate fill (start/end/fork) + dashed note callout.
    private static Brush StateFill      = DiagramBrushes.Frozen(Color.FromRgb(0xE8, 0xEA, 0xF2));
    private static Brush NoteBg         = DiagramBrushes.Frozen(Color.FromArgb(0x33, 0xF5, 0x9E, 0x0B));
    private static Brush NoteBorder     = DiagramBrushes.Frozen(Color.FromRgb(0xF5, 0x9E, 0x0B));
    private static Brush LinkBrush      = DiagramBrushes.Frozen(Color.FromRgb(0x4F, 0x8E, 0xF7));   // clickable class-member rows

    // The interaction hooks are NOT statics like the theme above: every element that gets a click
    // captures the callback from the options it was drawn with, so a click arriving after a later
    // render still runs the handler that element was built for.

    // Expand chip. Its colours come from the palette like every other surface here, so a theme
    // retunes it rather than the chip staying blue on a parchment background.
    private static Brush ChipBg     = DiagramBrushes.Frozen(Color.FromRgb(0x12, 0x16, 0x24));
    private static Brush ChipBorder = DiagramBrushes.Frozen(Color.FromRgb(0x4F, 0x8E, 0xF7));
    private static Brush ChipGlyph  = DiagramBrushes.Frozen(Color.FromRgb(0xE8, 0xEA, 0xF2));

    // Selection. Deliberately not the accent — the accent is already what every ordinary edge and
    // border is drawn in, so picking a node out with it would pick out nothing.
    private static Brush SelectBrush = DiagramBrushes.Frozen(Color.FromRgb(0xF5, 0x9E, 0x0B));

    private static readonly FontFamily BodyFont = DiagramText.BodyFont;
    private const double FontSize = 12.0;


    private static void SetTheme(MarkdownPalette p)
    {
        BgBrush         = p.CodeBg;
        NodeBg          = p.TableHeaderBg;
        NodeBorder      = p.Accent;
        NodeText        = p.Text;
        DiamondBg       = p.QuoteBg;
        DiamondBorder   = p.Citation;
        EdgeBrush       = p.Accent;
        EdgeDashedBrush = p.TextMuted;
        EdgeThickBrush  = p.Series.Count > 3 ? p.Series[3] : p.Accent;   // an amber for "thick" edges
        LabelBg         = p.CodeBg;
        LabelText       = p.TextMuted;
        TitleBrush      = p.Heading;
        AccentColor     = (p.Accent as SolidColorBrush)?.Color ?? Color.FromRgb(0x4F, 0x8E, 0xF7);

        StateFill   = p.Text;
        NoteBorder  = p.Warning;
        var nc      = (p.Warning as SolidColorBrush)?.Color ?? Color.FromRgb(0xF5, 0x9E, 0x0B);
        NoteBg      = DiagramBrushes.Frozen(Color.FromArgb(0x33, nc.R, nc.G, nc.B));
        LinkBrush   = p.Accent;

        ChipBg      = p.CodeBg;
        ChipBorder  = p.Accent;
        ChipGlyph   = p.Text;
        SelectBrush = p.Warning;
    }

    // ── Public API ─────────────────────────────────────────────────────────

    public static FrameworkElement Render(LayoutedGraph lg, MarkdownPalette palette, Func<string, bool>? onNavigate = null)
        => Render(lg, palette, new GraphRenderOptions { OnNavigate = onNavigate });

    /// <summary>The canvas wrapped in the renderer's own bordered scroller — the shape every diagram
    /// type has always returned. A host that supplies its own viewport calls <see cref="RenderCanvas"/>.</summary>
    public static FrameworkElement Render(LayoutedGraph lg, MarkdownPalette palette, GraphRenderOptions options)
    {
        var canvas = RenderCanvas(lg, palette, options);

        // Wrap in a ScrollViewer-friendly Border
        return new Border
        {
            Background      = BgBrush,
            BorderBrush     = NodeBorder,
            BorderThickness = new Thickness(1),
            CornerRadius    = new CornerRadius(6),
            Margin          = new Thickness(0, 8, 0, 12),
            Padding         = new Thickness(0),
            Child           = new ScrollViewer
            {
                Content                       = canvas,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
                VerticalScrollBarVisibility   = ScrollBarVisibility.Auto,
                MaxHeight                     = options.MaxHeight,
            }
        };
    }

    /// <summary>The bare drawing surface, at the layout's natural size and with no chrome.</summary>
    public static Canvas RenderCanvas(LayoutedGraph lg, MarkdownPalette palette, GraphRenderOptions? options = null)
    {
        options ??= GraphRenderOptions.None;
        SetTheme(palette);

        var canvas = new Canvas
        {
            Width      = lg.Width,
            Height     = lg.Height,
            Background = BgBrush,
        };

        bool horizontal = lg.Source.Direction is GraphDirection.LeftRight or GraphDirection.RightLeft;

        // Subgraph shaded boxes (drawn first, beneath everything)
        foreach (var (label, bounds) in lg.SubgraphBoxes)
            DrawSubgraphBox(canvas, label, bounds);

        // Edges (below nodes). The selected node's own edges go last so they lie over the rest —
        // picking a line out of a bundle is the whole point of selecting the node.
        string? selected = options.SelectedNodeId;
        bool Touches(LayoutEdge e) =>
            selected is not null &&
            (e.From.Source?.Id == selected || e.To.Source?.Id == selected);

        foreach (var le in lg.Edges.Where(e => !Touches(e))) DrawEdge(canvas, le, horizontal, false);
        foreach (var le in lg.Edges.Where(Touches))          DrawEdge(canvas, le, horizontal, true);

        // Nodes
        foreach (var ln in lg.AllNodes.Where(n => !n.IsDummy))
            DrawNode(canvas, ln, options);

        // Title (if any)
        if (!string.IsNullOrWhiteSpace(lg.Source.Title))
        {
            var tb = new TextBlock
            {
                Text       = lg.Source.Title,
                Foreground = TitleBrush,
                FontFamily = BodyFont,
                FontSize   = 14,
                FontWeight = FontWeights.SemiBold,
            };
            double titleW = DiagramText.Measure(lg.Source.Title, 14);
            Canvas.SetLeft(tb, Math.Max(SugiyamaLayout.MarginX, (lg.Width - titleW) / 2.0));   // centred across the image
            Canvas.SetTop(tb, 6);
            canvas.Children.Add(tb);
        }

        return canvas;
    }

    // ── Node drawing ───────────────────────────────────────────────────────

    private static void DrawNode(Canvas canvas, LayoutNode ln, GraphRenderOptions options)
    {
        UIElement shape = ln.Source?.Shape switch
        {
            NodeShape.Diamond          => DrawDiamond(ln),
            NodeShape.Circle or
            NodeShape.DoubleCircle     => DrawEllipse(ln),
            NodeShape.Stadium          => DrawRoundedRect(ln, ln.Height / 2.0),
            NodeShape.RoundedRect      => DrawRoundedRect(ln, 8),
            NodeShape.Asymmetric       => DrawAsymmetric(ln),
            NodeShape.Hexagon          => DrawHexagon(ln),
            NodeShape.Subroutine       => DrawSubroutine(ln),
            NodeShape.Cylinder         => DrawCylinder(ln),
            NodeShape.Parallelogram or
            NodeShape.ParallelogramAlt => DrawParallelogram(ln, ln.Source!.Shape == NodeShape.ParallelogramAlt),
            NodeShape.Trapezoid or
            NodeShape.TrapezoidAlt     => DrawTrapezoid(ln, ln.Source!.Shape == NodeShape.TrapezoidAlt),
            NodeShape.Document         => DrawDocument(ln),
            NodeShape.Card             => DrawCard(ln),
            NodeShape.StateStart       => DrawStateStart(ln),
            NodeShape.StateEnd         => DrawStateEnd(ln),
            NodeShape.ForkJoin         => DrawForkJoin(ln),
            NodeShape.Note             => DrawNote(ln),
            NodeShape.ClassBox         => DrawClassBox(ln, options),
            _                          => DrawRectShape(ln),
        };

        if (ln.Source?.Id is { } nodeId && nodeId == options.SelectedNodeId) Highlight(shape);
        canvas.Children.Add(shape);

        // A class box draws its own multi-compartment text; everything else gets the centred label.
        if (ln.Source is { Shape: not NodeShape.ClassBox } && ln.Source.Label.Length > 0)
        {
            string label = ln.Source.Label;
            bool   isNote = ln.Source.Shape == NodeShape.Note;
            double maxW = ln.Source.Shape == NodeShape.Diamond ? ln.Width * 0.7
                        : isNote ? ln.Width - 16 : ln.Width - 12;

            // The same wrap the layout reserved room for, so a wrapped label stays centred in the
            // box rather than hanging out of the bottom of it.
            int lines = NodeLabelMetrics.Measure(label, maxW + NodeLabelMetrics.PadX).Lines;

            var lbl = new TextBlock
            {
                Text          = label,
                Foreground    = isNote ? NodeText : GetTextBrush(ln),
                FontFamily    = BodyFont,
                FontSize      = isNote ? 11 : FontSize,
                Width         = maxW,   // fixed width so TextAlignment.Center works
                TextWrapping  = TextWrapping.Wrap,
                TextAlignment = isNote ? TextAlignment.Left : TextAlignment.Center,
            };

            double approxH = lines * (FontSize * 1.35);
            Canvas.SetLeft(lbl, ln.X - maxW / 2.0);
            Canvas.SetTop(lbl,  ln.Y - approxH / 2.0);
            canvas.Children.Add(lbl);

            // The label sits over the shape, so it has to carry the gesture too — otherwise clicking
            // the node's text (the obvious target) would miss.
            DiagramInteraction.Attach(lbl, BodyTarget(ln, options));
        }

        DiagramInteraction.Attach(shape, BodyTarget(ln, options));

        // Last, so it sits above both the shape and the label — the chip is a hit region of its own.
        DrawExpandChip(canvas, ln, options);
    }

    /// <summary>
    /// What clicking the body of a node does. With an <see cref="GraphRenderOptions.OnNodeClick"/>
    /// hook every node is clickable and the host decides what a click means (select it, open it, or
    /// both); without one it falls back to following the node's <c>href</c>, which is what a diagram
    /// rendered outside an interactive view has always done.
    /// </summary>
    private static DiagramTarget? BodyTarget(LayoutNode ln, GraphRenderOptions options)
    {
        if (ln.Source is not { } node) return null;

        if (options.OnNodeClick is { } click)
            return new DiagramTarget(() => click(node.Id), node.Tooltip ?? node.Href,
                                     DiagramTargetKind.Activate, node.Id);

        if (options.OnNavigate is not { } navigate || node.Href is not { Length: > 0 } href) return null;
        return new DiagramTarget(() => navigate(href),
                                 string.IsNullOrWhiteSpace(node.Tooltip) ? href : node.Tooltip,
                                 DiagramTargetKind.Activate, node.Id);
    }

    /// <summary>Picks the selected node's outline out of the diagram.</summary>
    private static void Highlight(UIElement shape)
    {
        switch (shape)
        {
            case Shape s:            s.Stroke = SelectBrush;      s.StrokeThickness = 3;                     break;
            case Border b:           b.BorderBrush = SelectBrush; b.BorderThickness = new Thickness(3);      break;
            case Panel p when p.Children.Count > 0 && p.Children[0] is Shape first:
                                     first.Stroke = SelectBrush;  first.StrokeThickness = 3;                 break;
        }
    }

    // ── Expand chip ────────────────────────────────────────────────────────

    private const double ChipSize = 15;

    /// <summary>
    /// The <c>[+]</c> / <c>[−]</c> chip on a node that hides (or has opened) a subtree.
    /// <para>
    /// It straddles the node's top-right corner — the one place clear of both the incoming face
    /// (top / left) and the outgoing one (bottom / right) whichever way the graph flows, so it never
    /// collides with an edge. Drawn as its own element with its own action, which is the whole point:
    /// the body of the node keeps its own click, and expansion is a second target rather than a
    /// second meaning smuggled into the first.
    /// </para>
    /// </summary>
    private static void DrawExpandChip(Canvas canvas, LayoutNode ln, GraphRenderOptions options)
    {
        if (options.OnToggleExpand is null) return;
        if (ln.Source is not { } node || node.Expansion == NodeExpansion.Leaf) return;

        bool   collapsed = node.Expansion == NodeExpansion.Collapsed;
        var    toggle    = options.OnToggleExpand;   // into the closure, not read back off a static
        string id        = node.Id;

        double cx = ln.X + ln.Width / 2.0;
        double cy = ln.Y - ln.Height / 2.0;

        var chip = new Grid
        {
            Width      = ChipSize,
            Height     = ChipSize,
            Background = Brushes.Transparent,   // the whole square is grabbable, not just the glyph
        };
        chip.Children.Add(new Rectangle
        {
            Width           = ChipSize,
            Height          = ChipSize,
            RadiusX         = 3,
            RadiusY         = 3,
            Fill            = ChipBg,
            Stroke          = ChipBorder,
            StrokeThickness = 1.2,
        });

        var glyph = new Line
        {
            X1 = 4, Y1 = ChipSize / 2.0, X2 = ChipSize - 4, Y2 = ChipSize / 2.0,
            Stroke = ChipGlyph, StrokeThickness = 1.6, SnapsToDevicePixels = true,
        };
        chip.Children.Add(glyph);
        if (collapsed)
            chip.Children.Add(new Line
            {
                X1 = ChipSize / 2.0, Y1 = 4, X2 = ChipSize / 2.0, Y2 = ChipSize - 4,
                Stroke = ChipGlyph, StrokeThickness = 1.6, SnapsToDevicePixels = true,
            });

        Canvas.SetLeft(chip, cx - ChipSize / 2.0);
        Canvas.SetTop(chip,  cy - ChipSize / 2.0);
        canvas.Children.Add(chip);

        string hidden = node.HiddenCount > 0 ? $" ({node.HiddenCount} hidden)" : string.Empty;
        DiagramInteraction.Attach(chip, new DiagramTarget(
            () => toggle(id),
            collapsed ? $"Expand{hidden}" : "Collapse",
            DiagramTargetKind.Expand, id));
    }

    private static Brush NodeFill(LayoutNode ln, Brush def) =>
        ln.Source?.FillColor is string fc ? DiagramBrushes.Frozen(ParseColor(fc)) : def;
    private static Brush NodeStroke(LayoutNode ln, Brush def) =>
        ln.Source?.StrokeColor is string sc ? DiagramBrushes.Frozen(ParseColor(sc)) : def;

    private static Brush GetTextBrush(LayoutNode ln)
    {
        if (ln.Source?.FillColor is not string fc) return NodeText;
        return DiagramBrushes.ParseCss(fc) is Color c
            ? DiagramBrushes.OnColor(c, DiagramBrushes.Frozen(Colors.Black), NodeText)
            : NodeText;
    }

    /// <summary>
    /// A colour from a mermaid <c>style</c>/<c>classDef</c> declaration.  Unlike
    /// <see cref="DiagramBrushes.ParseCss"/> this never fails: an unparseable colour falls back to
    /// the graph's own accent, because a node with a broken fill should still be drawn.
    /// </summary>
    private static Color ParseColor(string hex) =>
        DiagramBrushes.ParseCss(hex) ?? Color.FromRgb(0x4F, 0x8E, 0xF7);

    private static UIElement DrawRectShape(LayoutNode ln)
    {
        var r = new Rectangle
        {
            Width           = ln.Width,
            Height          = ln.Height,
            Fill            = NodeFill(ln, NodeBg),
            Stroke          = NodeStroke(ln, NodeBorder),
            StrokeThickness = 1.5,
        };
        Canvas.SetLeft(r, ln.X - ln.Width  / 2.0);
        Canvas.SetTop(r,  ln.Y - ln.Height / 2.0);
        return r;
    }

    private static UIElement DrawRoundedRect(LayoutNode ln, double radius)
    {
        var r = new Rectangle
        {
            Width           = ln.Width,
            Height          = ln.Height,
            RadiusX         = radius,
            RadiusY         = radius,
            Fill            = NodeFill(ln, NodeBg),
            Stroke          = NodeStroke(ln, NodeBorder),
            StrokeThickness = 1.5,
        };
        Canvas.SetLeft(r, ln.X - ln.Width  / 2.0);
        Canvas.SetTop(r,  ln.Y - ln.Height / 2.0);
        return r;
    }

    private static UIElement DrawEllipse(LayoutNode ln)
    {
        var e = new Ellipse
        {
            Width           = ln.Width,
            Height          = ln.Height,
            Fill            = NodeFill(ln, NodeBg),
            Stroke          = NodeStroke(ln, NodeBorder),
            StrokeThickness = 1.5,
        };
        Canvas.SetLeft(e, ln.X - ln.Width  / 2.0);
        Canvas.SetTop(e,  ln.Y - ln.Height / 2.0);
        return e;
    }

    private static UIElement DrawDiamond(LayoutNode ln)
    {
        double hw = ln.Width  / 2.0;
        double hh = ln.Height / 2.0;
        return new Polygon
        {
            Points = new PointCollection
            {
                new(ln.X,      ln.Y - hh),
                new(ln.X + hw, ln.Y),
                new(ln.X,      ln.Y + hh),
                new(ln.X - hw, ln.Y),
            },
            Fill            = NodeFill(ln, DiamondBg),
            Stroke          = NodeStroke(ln, DiamondBorder),
            StrokeThickness = 1.5,
        };
    }

    private static UIElement DrawAsymmetric(LayoutNode ln)
    {
        // >label] — flag/banner shape: rectangle with a triangular notch CUT INTO the left side.
        // All four corners are at full extent; the 5th point is a concave indent at centre-left.
        double hw    = ln.Width  / 2.0;
        double hh    = ln.Height / 2.0;
        double notch = hh * 0.7;   // how far the notch reaches into the shape from the left edge
        return new Polygon
        {
            Points = new PointCollection
            {
                new(ln.X - hw,         ln.Y - hh),   // top-left
                new(ln.X + hw,         ln.Y - hh),   // top-right
                new(ln.X + hw,         ln.Y + hh),   // bottom-right
                new(ln.X - hw,         ln.Y + hh),   // bottom-left
                new(ln.X - hw + notch, ln.Y),         // centre-left inward notch
            },
            Fill            = NodeFill(ln, NodeBg),
            Stroke          = NodeStroke(ln, NodeBorder),
            StrokeThickness = 1.5,
        };
    }

    private static UIElement DrawSubroutine(LayoutNode ln)
    {
        // [[label]] — rectangle with double vertical borders (inner lines near left & right edges).
        // Rendered as a Path with three vertical-line segments to keep it a single UIElement.
        double hw    = ln.Width  / 2.0;
        double hh    = ln.Height / 2.0;
        const double inset = 6;
        var stroke   = NodeStroke(ln, NodeBorder);

        // Main rectangle
        var rect = new Rectangle
        {
            Width = ln.Width, Height = ln.Height,
            Fill = NodeFill(ln, NodeBg), Stroke = stroke, StrokeThickness = 1.5,
        };
        Canvas.SetLeft(rect, ln.X - hw);
        Canvas.SetTop(rect,  ln.Y - hh);

        // Inner lines are drawn by the caller via a separate pass.
        // We return a Grid that contains all three as a single element.
        var grid = new Grid { Width = ln.Width, Height = ln.Height };
        grid.Children.Add(rect);

        // Use a Path for the two inner vertical lines
        var lf = new PathFigure { StartPoint = new Point(inset, 0) };
        lf.Segments.Add(new LineSegment(new Point(inset, ln.Height), true));
        var rf = new PathFigure { StartPoint = new Point(ln.Width - inset, 0) };
        rf.Segments.Add(new LineSegment(new Point(ln.Width - inset, ln.Height), true));
        var lines = new Path
        {
            Data = new PathGeometry([lf, rf]),
            Stroke = stroke, StrokeThickness = 1,
        };
        grid.Children.Add(lines);

        Canvas.SetLeft(grid, ln.X - hw);
        Canvas.SetTop(grid,  ln.Y - hh);
        return grid;
    }

    private static UIElement DrawCylinder(LayoutNode ln)
    {
        // [(label)] — database drum: rectangle body with ellipse caps on top and bottom
        double hw   = ln.Width  / 2.0;
        double hh   = ln.Height / 2.0;
        double capH = Math.Min(10.0, hh * 0.4);

        var fill   = NodeFill(ln, NodeBg);
        var stroke = NodeStroke(ln, NodeBorder);

        // Build as a PathGeometry: two vertical lines + two arc caps
        // Left side
        var figure = new PathFigure
        {
            StartPoint  = new Point(ln.X - hw, ln.Y - hh + capH),
            IsClosed    = false,
        };
        figure.Segments.Add(new LineSegment(new Point(ln.X - hw, ln.Y + hh - capH), true));
        // Bottom arc (left → right)
        figure.Segments.Add(new ArcSegment(
            new Point(ln.X + hw, ln.Y + hh - capH),
            new Size(hw, capH), 0, false, SweepDirection.Clockwise, true));
        // Right side
        figure.Segments.Add(new LineSegment(new Point(ln.X + hw, ln.Y - hh + capH), true));
        // Top arc right → left (back)
        figure.Segments.Add(new ArcSegment(
            new Point(ln.X - hw, ln.Y - hh + capH),
            new Size(hw, capH), 0, false, SweepDirection.Counterclockwise, true));

        // Top ellipse (full, drawn filled to close the lid)
        var topFig = new PathFigure { StartPoint = new Point(ln.X - hw, ln.Y - hh + capH), IsClosed = true };
        topFig.Segments.Add(new ArcSegment(
            new Point(ln.X + hw, ln.Y - hh + capH),
            new Size(hw, capH), 0, false, SweepDirection.Clockwise, true));
        topFig.Segments.Add(new ArcSegment(
            new Point(ln.X - hw, ln.Y - hh + capH),
            new Size(hw, capH), 0, false, SweepDirection.Counterclockwise, true));

        return new Path
        {
            Data            = new PathGeometry([figure, topFig]),
            Fill            = fill,
            Stroke          = stroke,
            StrokeThickness = 1.5,
        };
    }

    private static UIElement DrawParallelogram(LayoutNode ln, bool mirrorX)
    {
        // [/text/] lean-right: left edges offset up-right; [\text\] lean-left: mirror
        double hw   = ln.Width  / 2.0;
        double hh   = ln.Height / 2.0;
        double skew = hh * 0.7;  // horizontal skew amount
        if (mirrorX) skew = -skew;
        return new Polygon
        {
            Points = new PointCollection
            {
                new(ln.X - hw + skew, ln.Y - hh),
                new(ln.X + hw + skew, ln.Y - hh),
                new(ln.X + hw - skew, ln.Y + hh),
                new(ln.X - hw - skew, ln.Y + hh),
            },
            Fill            = NodeFill(ln, NodeBg),
            Stroke          = NodeStroke(ln, NodeBorder),
            StrokeThickness = 1.5,
        };
    }

    private static UIElement DrawTrapezoid(LayoutNode ln, bool invertY)
    {
        // [/text\] wide-top trapezoid; [\text/] wide-bottom (inverted)
        double hw   = ln.Width  / 2.0;
        double hh   = ln.Height / 2.0;
        double inset = hw * 0.25;  // narrow side inset
        double topW  = invertY ? inset : 0;
        double botW  = invertY ? 0     : inset;
        return new Polygon
        {
            Points = new PointCollection
            {
                new(ln.X - hw + topW, ln.Y - hh),
                new(ln.X + hw - topW, ln.Y - hh),
                new(ln.X + hw - botW, ln.Y + hh),
                new(ln.X - hw + botW, ln.Y + hh),
            },
            Fill            = NodeFill(ln, NodeBg),
            Stroke          = NodeStroke(ln, NodeBorder),
            StrokeThickness = 1.5,
        };
    }

    private static UIElement DrawHexagon(LayoutNode ln)
    {
        double hw = ln.Width  / 2.0;
        double hh = ln.Height / 2.0;
        double qw = hw * 0.35;
        return new Polygon
        {
            Points = new PointCollection
            {
                new(ln.X - hw,      ln.Y),
                new(ln.X - hw + qw, ln.Y - hh),
                new(ln.X + hw - qw, ln.Y - hh),
                new(ln.X + hw,      ln.Y),
                new(ln.X + hw - qw, ln.Y + hh),
                new(ln.X - hw + qw, ln.Y + hh),
            },
            Fill            = NodeFill(ln, NodeBg),
            Stroke          = NodeStroke(ln, NodeBorder),
            StrokeThickness = 1.5,
        };
    }

    private static UIElement DrawDocument(LayoutNode ln)
    {
        // Rectangle with a single wave along the bottom edge — the classic "document" symbol.
        double hw = ln.Width / 2.0, hh = ln.Height / 2.0;
        double left = ln.X - hw, right = ln.X + hw, top = ln.Y - hh;
        double baseY = ln.Y + hh - 4;          // mean line of the wave
        double amp  = 5;

        var fig = new PathFigure { StartPoint = new Point(left, top), IsClosed = true };
        fig.Segments.Add(new LineSegment(new Point(right, top), true));        // top edge
        fig.Segments.Add(new LineSegment(new Point(right, baseY), true));      // right edge
        // wavy bottom: right → middle (dip), middle → left (rise)
        fig.Segments.Add(new QuadraticBezierSegment(new Point(ln.X + hw / 2, baseY + amp * 2), new Point(ln.X, baseY), true));
        fig.Segments.Add(new QuadraticBezierSegment(new Point(ln.X - hw / 2, baseY - amp * 2), new Point(left, baseY), true));
        // left edge closes back to the start point

        return new Path
        {
            Data            = new PathGeometry([fig]),
            Fill            = NodeFill(ln, NodeBg),
            Stroke          = NodeStroke(ln, NodeBorder),
            StrokeThickness = 1.5,
        };
    }

    private static UIElement DrawCard(LayoutNode ln)
    {
        // Rectangle with the top-left corner folded in — the "card" / notched-rectangle symbol.
        double hw = ln.Width / 2.0, hh = ln.Height / 2.0;
        double fold = Math.Min(16, Math.Min(hw, hh) * 0.6);
        return new Polygon
        {
            Points = new PointCollection
            {
                new(ln.X - hw + fold, ln.Y - hh),     // top edge starts past the fold
                new(ln.X + hw,        ln.Y - hh),
                new(ln.X + hw,        ln.Y + hh),
                new(ln.X - hw,        ln.Y + hh),
                new(ln.X - hw,        ln.Y - hh + fold), // left edge stops below the fold
            },
            Fill            = NodeFill(ln, NodeBg),
            Stroke          = NodeStroke(ln, NodeBorder),
            StrokeThickness = 1.5,
        };
    }

    // ── State-diagram shapes ─────────────────────────────────────────────────

    private static UIElement DrawStateStart(LayoutNode ln)
    {
        // Initial pseudostate — a solid filled dot.
        double d = Math.Min(ln.Width, ln.Height);
        var e = new Ellipse { Width = d, Height = d, Fill = StateFill, Stroke = StateFill, StrokeThickness = 1 };
        Canvas.SetLeft(e, ln.X - d / 2.0);
        Canvas.SetTop(e,  ln.Y - d / 2.0);
        return e;
    }

    private static UIElement DrawStateEnd(LayoutNode ln)
    {
        // Final pseudostate — a ringed dot: outer hollow ring + inner filled dot.
        double d = Math.Min(ln.Width, ln.Height);
        var grid = new Grid { Width = d, Height = d };
        grid.Children.Add(new Ellipse { Width = d, Height = d, Fill = BgBrush, Stroke = StateFill, StrokeThickness = 1.5 });
        double inner = d * 0.55;
        grid.Children.Add(new Ellipse
        {
            Width = inner, Height = inner, Fill = StateFill,
            HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
        });
        Canvas.SetLeft(grid, ln.X - d / 2.0);
        Canvas.SetTop(grid,  ln.Y - d / 2.0);
        return grid;
    }

    private static UIElement DrawForkJoin(LayoutNode ln)
    {
        // Fork / join — a solid synchronisation bar.
        var r = new Rectangle
        {
            Width = ln.Width, Height = ln.Height,
            Fill = StateFill, Stroke = StateFill, StrokeThickness = 1,
            RadiusX = 2, RadiusY = 2,
        };
        Canvas.SetLeft(r, ln.X - ln.Width  / 2.0);
        Canvas.SetTop(r,  ln.Y - ln.Height / 2.0);
        return r;
    }

    private static UIElement DrawNote(LayoutNode ln)
    {
        // Note callout — a dashed rectangle with a tinted fill.
        var r = new Rectangle
        {
            Width = ln.Width, Height = ln.Height,
            Fill = NoteBg, Stroke = NoteBorder, StrokeThickness = 1,
            StrokeDashArray = new DoubleCollection([4, 2]),
            RadiusX = 2, RadiusY = 2,
        };
        Canvas.SetLeft(r, ln.X - ln.Width  / 2.0);
        Canvas.SetTop(r,  ln.Y - ln.Height / 2.0);
        return r;
    }

    // ── Class-diagram box ─────────────────────────────────────────────────

    private static UIElement DrawClassBox(LayoutNode ln, GraphRenderOptions options)
    {
        var info   = ln.Source!.Class!;
        var stroke = NodeStroke(ln, NodeBorder);

        var (bw, bh) = ClassBoxMetrics.MeasureBox(ln.Source.Label, info);
        bool hasAbove = info.Lollipops.Any(l => !l.Below);
        double band   = ClassBoxMetrics.LollipopBand;

        // A cell the size of the reserved footprint; the box sits inside it, offset past any top band.
        var cell = new Canvas { Width = ln.Width, Height = ln.Height };
        double boxLeft = (ln.Width - bw) / 2.0;
        double boxTop  = hasAbove ? band : 0;

        var border = BuildClassBorder(ln, info, bw, bh, options);
        Canvas.SetLeft(border, boxLeft);
        Canvas.SetTop(border,  boxTop);
        cell.Children.Add(border);

        // Lollipop interfaces — straight stubs off the top / bottom edge.
        DrawLollipops(cell, info.Lollipops.Where(l => !l.Below).ToList(), boxLeft, bw, boxTop,      above: true,  stroke);
        DrawLollipops(cell, info.Lollipops.Where(l =>  l.Below).ToList(), boxLeft, bw, boxTop + bh, above: false, stroke);

        Canvas.SetLeft(cell, ln.X - ln.Width  / 2.0);
        Canvas.SetTop(cell,  ln.Y - ln.Height / 2.0);
        return cell;
    }

    private static Border BuildClassBorder(LayoutNode ln, ClassInfo info, double bw, double bh, GraphRenderOptions options)
    {
        var fill   = NodeFill(ln, NodeBg);
        var stroke = NodeStroke(ln, NodeBorder);
        var text   = GetTextBrush(ln);

        const double rowH = ClassBoxMetrics.RowH;
        const double padV = ClassBoxMetrics.PadV;
        const double padX = ClassBoxMetrics.PadX;

        var panel = new StackPanel { Width = bw };

        // Header compartment: «stereotype» (optional) over the bold class name.
        var header = new StackPanel { Margin = new Thickness(0, padV, 0, padV) };
        if (info.Stereotype is { Length: > 0 } st)
            header.Children.Add(ClassRow($"«{st}»", text, rowH, padX, center: true, italic: true, size: 10.5));
        header.Children.Add(ClassRow(ln.Source!.Label, text, rowH, padX, center: true, bold: true));
        panel.Children.Add(header);

        // The member compartments are drawn only when the box has members. Requirement boxes
        // (SingleCompartment) show one list of fields; class boxes show attributes then methods.
        if (info.HasMembers)
        {
            panel.Children.Add(ClassDivider(stroke));
            panel.Children.Add(ClassCompartment(info.Attributes, text, rowH, padX, padV, options));
            if (!info.SingleCompartment)
            {
                panel.Children.Add(ClassDivider(stroke));
                panel.Children.Add(ClassCompartment(info.Methods, text, rowH, padX, padV, options));
            }
        }

        return new Border
        {
            Width           = bw,
            Height          = bh,
            Background      = fill,
            BorderBrush     = stroke,
            BorderThickness = new Thickness(1.5),
            CornerRadius    = new CornerRadius(3),
            ClipToBounds    = true,
            Child           = panel,
        };
    }

    /// <summary>Draws lollipop interfaces hanging off one edge of a class box: a short straight stub, a
    /// small hollow circle at its end, and the interface name just beyond the circle. Coordinates are local
    /// to the node's cell canvas. <paramref name="edgeY"/> is the box edge the stubs attach to.</summary>
    private static void DrawLollipops(Canvas cell, IReadOnlyList<Lollipop> lollipops,
        double boxLeft, double bw, double edgeY, bool above, Brush stroke)
    {
        if (lollipops.Count == 0) return;

        double stub = ClassBoxMetrics.LollipopStub, r = ClassBoxMetrics.LollipopR, gap = ClassBoxMetrics.LollipopGapX;
        int    n   = lollipops.Count;
        double cx0 = boxLeft + bw / 2.0;
        double dir = above ? -1 : 1;

        for (int i = 0; i < n; i++)
        {
            double cx     = cx0 + (i - (n - 1) / 2.0) * gap;
            double stubEnd = edgeY + dir * stub;
            double cy      = stubEnd + dir * r;           // circle centre just past the stub end

            cell.Children.Add(new Line
            {
                X1 = cx, Y1 = edgeY, X2 = cx, Y2 = stubEnd,
                Stroke = stroke, StrokeThickness = 1.5,
            });

            var circle = new Ellipse { Width = 2 * r, Height = 2 * r, Stroke = stroke, StrokeThickness = 1.5, Fill = BgBrush };
            Canvas.SetLeft(circle, cx - r);
            Canvas.SetTop(circle,  cy - r);
            cell.Children.Add(circle);

            var lbl = new TextBlock
            {
                Text = lollipops[i].Name, Foreground = NodeText, FontFamily = BodyFont, FontSize = 11,
                TextAlignment = TextAlignment.Center,
            };
            double lw = DiagramText.Measure(lollipops[i].Name, 11);
            Canvas.SetLeft(lbl, cx - lw / 2.0);
            Canvas.SetTop(lbl, above ? cy - r - 2 - 14 : cy + r + 2);
            cell.Children.Add(lbl);
        }
    }

    private static UIElement ClassCompartment(IReadOnlyList<ClassMember> members, Brush text, double rowH, double padX, double padV, GraphRenderOptions options)
    {
        var nav = options.OnNavigate;
        var sp = new StackPanel { Margin = new Thickness(0, padV, 0, padV) };
        foreach (var m in members)
        {
            bool linked = m.Href is { Length: > 0 } && nav is not null;
            var tb = ClassRow(m.Text, linked ? LinkBrush : text, rowH, padX);
            if (m.IsAbstract) tb.FontStyle        = FontStyles.Italic;       // UML: abstract member
            if (m.IsStatic)   tb.TextDecorations  = TextDecorations.Underline; // UML: static member
            if (linked)
            {
                var href = m.Href!;
                tb.Cursor          = System.Windows.Input.Cursors.Hand;
                tb.TextDecorations = TextDecorations.Underline;
                tb.ToolTip         = new TextBlock { Text = "Go to definition", TextAlignment = TextAlignment.Left };
                tb.MouseLeftButtonDown += (_, e) => { e.Handled = true; nav!(href); };
            }
            sp.Children.Add(tb);
        }
        return sp;
    }

    private static TextBlock ClassRow(string s, Brush brush, double rowH, double padX,
        bool center = false, bool bold = false, bool italic = false, double size = FontSize) =>
        new()
        {
            Text          = s,
            Foreground    = brush,
            FontFamily    = BodyFont,
            FontSize      = size,
            Height        = rowH,
            Padding       = new Thickness(padX, 1, padX, 0),
            FontWeight    = bold   ? FontWeights.SemiBold : FontWeights.Normal,
            FontStyle     = italic ? FontStyles.Italic    : FontStyles.Normal,
            TextAlignment = center ? TextAlignment.Center : TextAlignment.Left,
            TextTrimming  = TextTrimming.CharacterEllipsis,
        };

    private static UIElement ClassDivider(Brush stroke) =>
        new Rectangle { Height = 1, Fill = stroke, HorizontalAlignment = HorizontalAlignment.Stretch };

    // ── Subgraph box ──────────────────────────────────────────────────────

    private const double SubgraphHeaderH = 22;

    private static void DrawSubgraphBox(Canvas canvas, string label, Rect bounds)
    {
        var fillBrush   = new SolidColorBrush(Color.FromArgb(0x22, AccentColor.R, AccentColor.G, AccentColor.B));
        var strokeBrush = new SolidColorBrush(Color.FromArgb(0x55, AccentColor.R, AccentColor.G, AccentColor.B));
        var headerBrush = new SolidColorBrush(Color.FromArgb(0x3C, AccentColor.R, AccentColor.G, AccentColor.B));
        fillBrush.Freeze();
        strokeBrush.Freeze();
        headerBrush.Freeze();

        var rect = new Rectangle
        {
            Width           = bounds.Width,
            Height          = bounds.Height,
            Fill            = fillBrush,
            Stroke          = strokeBrush,
            StrokeThickness = 1,
            StrokeDashArray = new DoubleCollection([5, 3]),
            RadiusX         = 6,
            RadiusY         = 6,
        };
        Canvas.SetLeft(rect, bounds.Left);
        Canvas.SetTop(rect,  bounds.Top);
        canvas.Children.Add(rect);

        if (string.IsNullOrWhiteSpace(label)) return;

        // A distinct header band (a tinted strip + a divider under it), Mermaid-style.
        double headerH = Math.Min(SubgraphHeaderH, bounds.Height);
        var header = new Border
        {
            Width        = Math.Max(0, bounds.Width - 2),
            Height       = headerH,
            Background   = headerBrush,
            BorderBrush  = strokeBrush,
            BorderThickness = new Thickness(0, 0, 0, 1),
            CornerRadius = new CornerRadius(6, 6, 0, 0),
        };
        Canvas.SetLeft(header, bounds.Left + 1);
        Canvas.SetTop(header,  bounds.Top  + 1);
        canvas.Children.Add(header);

        var tb = new TextBlock
        {
            Text       = label,
            Foreground = NodeText,
            FontFamily = BodyFont,
            FontSize   = 11,
            FontWeight = FontWeights.SemiBold,
        };
        double w = DiagramText.Measure(label, 11);
        Canvas.SetLeft(tb, bounds.Left + (bounds.Width - w) / 2.0);   // centred on the header
        Canvas.SetTop(tb,  bounds.Top  + (headerH - 14) / 2.0);
        canvas.Children.Add(tb);
    }

    // ── Edge drawing ───────────────────────────────────────────────────────

    /// <summary>Pulls a bounding-box port back onto the node's actual outline for shapes whose surface is
    /// inset from the box (diamond, circle / double-circle). Returns <paramref name="port"/> unchanged for
    /// rectangular shapes and for a port already on the boundary (e.g. a single edge hitting a vertex).</summary>
    private static Point ClipToShape(LayoutNode node, Point port)
    {
        double hw = node.Width / 2.0, hh = node.Height / 2.0;
        if (hw <= 0 || hh <= 0) return port;
        double dx = port.X - node.X, dy = port.Y - node.Y;

        double denom = node.Source?.Shape switch
        {
            NodeShape.Diamond                          => Math.Abs(dx) / hw + Math.Abs(dy) / hh,
            NodeShape.Circle or NodeShape.DoubleCircle => Math.Sqrt(dx * dx / (hw * hw) + dy * dy / (hh * hh)),
            _                                          => 0,
        };
        if (denom <= 1.0 + 1e-6) return port;                          // rect (denom 0) or already on the edge
        return new Point(node.X + dx / denom, node.Y + dy / denom);    // project onto the boundary
    }

    private static void DrawEdge(Canvas canvas, LayoutEdge le, bool horizontal, bool highlighted = false)
    {
        var pts = le.Waypoints;
        if (pts.Count < 2) return;

        // Ports are computed on the node's bounding box, but a diamond / ellipse surface slants inward —
        // an off-centre port (fanned out when several edges share a face) would otherwise float in the
        // empty corner. Pull each endpoint back onto the actual shape boundary.
        pts[0]  = ClipToShape(le.From, pts[0]);
        pts[^1] = ClipToShape(le.To,   pts[^1]);

        var edge      = le.Source;
        var brush     = highlighted ? SelectBrush : edge?.Style switch
        {
            EdgeStyle.Thick  => EdgeThickBrush,
            EdgeStyle.Dashed => EdgeDashedBrush,
            EdgeStyle.Dotted => EdgeDashedBrush,
            _                => EdgeBrush,
        };
        double thickness = edge?.Style == EdgeStyle.Thick ? 2.5 : 1.5;
        if (highlighted) thickness += 1.0;

        var (path, startTan, endTan) = BuildBezierPath(pts, horizontal);
        path.Stroke               = brush;
        path.StrokeThickness      = thickness;
        path.StrokeLineJoin       = PenLineJoin.Round;
        path.StrokeStartLineCap   = PenLineCap.Round;
        path.StrokeEndLineCap     = PenLineCap.Round;

        if (edge?.Style == EdgeStyle.Dashed)
            path.StrokeDashArray = new DoubleCollection([5, 3]);
        if (edge?.Style == EdgeStyle.Dotted)
            path.StrokeDashArray = new DoubleCollection([2, 3]);

        canvas.Children.Add(path);

        // A cycle-reversed edge is routed backwards (its waypoints run target → source), so its
        // arrowhead belongs at the START of the path, not the end. Both heads take the bezier tangent
        // (the control point nearest the tip) as their direction so they stay aligned with a curved edge.
        bool rev = edge?.IsReversed == true;
        Point endTip   = rev ? pts[0]   : pts[^1];
        Point endFrom  = rev ? startTan : endTan;
        Point startTip = rev ? pts[^1]  : pts[0];
        Point startFrom = rev ? endTan  : startTan;

        if (edge?.Arrow != EdgeArrow.None)
            DrawArrowhead(canvas, endFrom, endTip, brush, edge?.Arrow ?? EdgeArrow.Normal);

        // Start head — for multidirectional links (o--o, x--x, <-->) and UML relationship heads.
        if (edge is { StartArrow: not EdgeArrow.None })
            DrawArrowhead(canvas, startFrom, startTip, brush, edge.StartArrow);

        // Multiplicity / cardinality text near each end (class diagrams).
        if (edge is { StartLabel.Length: > 0 }) DrawMultiplicity(canvas, startTip, startFrom, edge.StartLabel);
        if (edge is { EndLabel.Length:   > 0 }) DrawMultiplicity(canvas, endTip,   endFrom,   edge.EndLabel);

        // Edge label as a floating styled box. A staggered anchor (set for parallel/antiparallel
        // groups) wins; otherwise centre on the path midpoint.
        if (!string.IsNullOrWhiteSpace(edge?.Label))
        {
            var mid = le.LabelAnchor
                ?? (pts.Count == 2
                    ? new Point((pts[0].X + pts[1].X) / 2.0, (pts[0].Y + pts[1].Y) / 2.0)
                    : pts[pts.Count / 2]);
            DrawEdgeLabel(canvas, edge!.Label, mid);
        }
    }

    /// <summary>
    /// Builds a smooth cubic-bezier path through <paramref name="pts"/>. Returns the path plus the
    /// bezier control points nearest the start and end tips — the tangents used to orient arrowheads
    /// so a head stays aligned with a curved edge rather than its straight chord.
    /// </summary>
    private static (Path path, Point startTangent, Point endTangent) BuildBezierPath(IList<Point> pts, bool horizontal)
    {
        var figure  = new PathFigure { StartPoint = pts[0], IsFilled = false };
        Point firstCp1 = pts[1];   // fallback
        Point lastCp2  = pts[^2];  // fallback

        if (pts.Count == 2)
        {
            // Single cubic bezier: control points pulled along the primary flow axis.
            var p0 = pts[0]; var p1 = pts[1];
            double dx = p1.X - p0.X, dy = p1.Y - p0.Y;
            var cp1 = horizontal ? new Point(p0.X + dx * 0.5, p0.Y) : new Point(p0.X,           p0.Y + dy * 0.5);
            var cp2 = horizontal ? new Point(p1.X - dx * 0.5, p1.Y) : new Point(p1.X,           p1.Y - dy * 0.5);
            firstCp1 = cp1;
            lastCp2  = cp2;
            figure.Segments.Add(new BezierSegment(cp1, cp2, p1, isStroked: true));
        }
        else
        {
            // Catmull-Rom → cubic bezier for smooth multi-point curves.
            // Pad with duplicate endpoints so end tangents are zero.
            var ext = new List<Point>(pts.Count + 2) { pts[0] };
            ext.AddRange(pts);
            ext.Add(pts[^1]);

            for (int i = 1; i < ext.Count - 2; i++)
            {
                var pm = ext[i - 1]; var p0 = ext[i]; var p1 = ext[i + 1]; var p2 = ext[i + 2];
                var cp1 = new Point(p0.X + (p1.X - pm.X) / 6.0, p0.Y + (p1.Y - pm.Y) / 6.0);
                var cp2 = new Point(p1.X - (p2.X - p0.X) / 6.0, p1.Y - (p2.Y - p0.Y) / 6.0);
                if (i == 1)              firstCp1 = cp1;
                if (i == ext.Count - 3) lastCp2  = cp2;
                figure.Segments.Add(new BezierSegment(cp1, cp2, p1, isStroked: true));
            }
        }

        return (new Path { Data = new PathGeometry([figure]) }, firstCp1, lastCp2);
    }

    private static void DrawEdgeLabel(Canvas canvas, string text, Point mid)
    {
        const double labelFont = 10.5;
        var border = new Border
        {
            Background      = LabelBg,
            BorderBrush     = EdgeBrush,
            BorderThickness = new Thickness(1),
            CornerRadius    = new CornerRadius(3),
            Padding         = new Thickness(4, 1, 4, 1),
            Child = new TextBlock
            {
                Text       = text,
                Foreground = LabelText,
                FontFamily = BodyFont,
                FontSize   = labelFont,
            },
        };
        // Centre the box on the midpoint using the measured text size (padding 4+4, border 1+1).
        double w = DiagramText.Measure(text, labelFont) + 10;
        double h = labelFont * 1.35 + 4;
        Canvas.SetLeft(border, mid.X - w / 2.0);
        Canvas.SetTop(border,  mid.Y - h / 2.0);
        canvas.Children.Add(border);
    }

    private static void DrawMultiplicity(Canvas canvas, Point tip, Point along, string text)
    {
        const double f = 10;
        // Sit the label in the gap just inside the edge (toward `along`) plus a perpendicular nudge, so
        // it clears the line/arrowhead and isn't painted over by the node box that follows.
        double dx = along.X - tip.X, dy = along.Y - tip.Y;
        double len = Math.Sqrt(dx * dx + dy * dy);
        if (len < 0.001) { dx = 0; dy = 1; len = 1; }
        double ux = dx / len, uy = dy / len, px = -uy, py = ux;

        var tb = new TextBlock { Text = text, Foreground = LabelText, FontFamily = BodyFont, FontSize = f };
        double w = DiagramText.Measure(text, f), h = f * 1.35;
        double cx = tip.X + ux * 13 + px * 8;
        double cy = tip.Y + uy * 13 + py * 8;
        Canvas.SetLeft(tb, cx - w / 2.0);
        Canvas.SetTop(tb,  cy - h / 2.0);
        canvas.Children.Add(tb);
    }

    private static void DrawArrowhead(Canvas canvas, Point from, Point tip, Brush brush, EdgeArrow arrowType)
    {
        const double arrowLen   = 10;
        const double arrowAngle = 25 * Math.PI / 180;

        double angle = Math.Atan2(tip.Y - from.Y, tip.X - from.X);

        // UML class-diagram heads: hollow triangle (inheritance/realization), filled / hollow diamond
        // (composition / aggregation). Drawn back along the line from the tip.
        if (arrowType is EdgeArrow.TriangleHollow or EdgeArrow.DiamondFilled or EdgeArrow.DiamondHollow)
        {
            double ux = Math.Cos(angle), uy = Math.Sin(angle);   // from → tip
            double px = -uy, py = ux;                            // perpendicular

            if (arrowType == EdgeArrow.TriangleHollow)
            {
                const double len = 13, hw = 7;
                var baseC = new Point(tip.X - len * ux, tip.Y - len * uy);
                var a = new Point(baseC.X + hw * px, baseC.Y + hw * py);
                var b = new Point(baseC.X - hw * px, baseC.Y - hw * py);
                canvas.Children.Add(new Polygon
                {
                    Points = new PointCollection([tip, a, b]),
                    Fill = BgBrush, Stroke = brush, StrokeThickness = 1.5, StrokeLineJoin = PenLineJoin.Round,
                });
            }
            else
            {
                const double len = 16, hw = 6;
                var back = new Point(tip.X - len       * ux, tip.Y - len       * uy);
                var mid  = new Point(tip.X - len / 2.0 * ux, tip.Y - len / 2.0 * uy);
                var a = new Point(mid.X + hw * px, mid.Y + hw * py);
                var b = new Point(mid.X - hw * px, mid.Y - hw * py);
                canvas.Children.Add(new Polygon
                {
                    Points = new PointCollection([tip, a, back, b]),
                    Fill = arrowType == EdgeArrow.DiamondFilled ? brush : BgBrush,
                    Stroke = brush, StrokeThickness = 1.5, StrokeLineJoin = PenLineJoin.Round,
                });
            }
            return;
        }

        // Crosshair-circle terminal (SysML containment): a hollow circle with a plus, sitting in the
        // gap just off the container box, the plus aligned to the line.
        if (arrowType == EdgeArrow.CrossCircle)
        {
            const double r = 7;
            double ux = Math.Cos(angle), uy = Math.Sin(angle);   // from → tip (toward the box)
            double px = -uy, py = ux;
            double cx = tip.X - ux * r, cy = tip.Y - uy * r;     // one radius back into the gap
            var ring = new Ellipse { Width = r * 2, Height = r * 2, Fill = BgBrush, Stroke = brush, StrokeThickness = 1.5 };
            Canvas.SetLeft(ring, cx - r);
            Canvas.SetTop(ring, cy - r);
            canvas.Children.Add(ring);
            canvas.Children.Add(new Line { X1 = cx - ux * r, Y1 = cy - uy * r, X2 = cx + ux * r, Y2 = cy + uy * r, Stroke = brush, StrokeThickness = 1.5 });
            canvas.Children.Add(new Line { X1 = cx - px * r, Y1 = cy - py * r, X2 = cx + px * r, Y2 = cy + py * r, Stroke = brush, StrokeThickness = 1.5 });
            return;
        }

        // Circle terminal (--o): a hollow bulb on the line end.
        if (arrowType == EdgeArrow.Circle)
        {
            const double r = 4.5;
            double cxC = tip.X - r * Math.Cos(angle), cyC = tip.Y - r * Math.Sin(angle);
            var dot = new Ellipse { Width = r * 2, Height = r * 2, Fill = BgBrush, Stroke = brush, StrokeThickness = 1.5 };
            Canvas.SetLeft(dot, cxC - r);
            Canvas.SetTop(dot, cyC - r);
            canvas.Children.Add(dot);
            return;
        }

        // Cross terminal (--x): an × at the line end.
        if (arrowType == EdgeArrow.Cross)
        {
            const double r = 5;
            double px = Math.Cos(angle), py = Math.Sin(angle);   // along the line
            double qx = -py, qy = px;                            // perpendicular
            var c = new Point(tip.X - r * px, tip.Y - r * py);   // step back so the × sits on the line
            void Seg(double s1, double s2) => canvas.Children.Add(new Line
            {
                X1 = c.X + r * (qx * s1 + px * s2), Y1 = c.Y + r * (qy * s1 + py * s2),
                X2 = c.X - r * (qx * s1 + px * s2), Y2 = c.Y - r * (qy * s1 + py * s2),
                Stroke = brush, StrokeThickness = 1.6,
            });
            Seg(1, 1); Seg(1, -1);
            return;
        }

        // ER crow's-foot cardinality: a max indicator nearest the box (bar = one, fork = many) and a min
        // indicator just outside it (bar = one, circle = zero), drawn back along the line from the box edge.
        if (arrowType is EdgeArrow.ErZeroOne or EdgeArrow.ErExactlyOne or EdgeArrow.ErZeroMany or EdgeArrow.ErOneMany)
        {
            double ux = Math.Cos(angle), uy = Math.Sin(angle);   // from → tip (toward the box)
            double px = -uy, py = ux;                            // perpendicular

            void Bar(double d, double hw)
            {
                double bx = tip.X - ux * d, by = tip.Y - uy * d;
                canvas.Children.Add(new Line { X1 = bx + px * hw, Y1 = by + py * hw, X2 = bx - px * hw, Y2 = by - py * hw, Stroke = brush, StrokeThickness = 1.6 });
            }
            void Bulb(double d, double r)
            {
                double bx = tip.X - ux * d, by = tip.Y - uy * d;
                var e = new Ellipse { Width = r * 2, Height = r * 2, Fill = BgBrush, Stroke = brush, StrokeThickness = 1.5 };
                Canvas.SetLeft(e, bx - r);
                Canvas.SetTop(e, by - r);
                canvas.Children.Add(e);
            }
            void Foot(double apexLen, double hw)
            {
                double ax = tip.X - ux * apexLen, ay = tip.Y - uy * apexLen;
                canvas.Children.Add(new Line { X1 = ax, Y1 = ay, X2 = tip.X,          Y2 = tip.Y,          Stroke = brush, StrokeThickness = 1.5 });
                canvas.Children.Add(new Line { X1 = ax, Y1 = ay, X2 = tip.X + px * hw, Y2 = tip.Y + py * hw, Stroke = brush, StrokeThickness = 1.5 });
                canvas.Children.Add(new Line { X1 = ax, Y1 = ay, X2 = tip.X - px * hw, Y2 = tip.Y - py * hw, Stroke = brush, StrokeThickness = 1.5 });
            }

            switch (arrowType)
            {
                case EdgeArrow.ErExactlyOne: Bar(8, 6);  Bar(14, 6);  break;
                case EdgeArrow.ErZeroOne:    Bar(8, 6);  Bulb(15, 4); break;
                case EdgeArrow.ErOneMany:    Foot(12, 7); Bar(17, 6); break;
                case EdgeArrow.ErZeroMany:   Foot(12, 7); Bulb(18, 4); break;
            }
            return;
        }

        double a1 = angle + Math.PI - arrowAngle;
        double a2 = angle + Math.PI + arrowAngle;
        var p1 = new Point(tip.X + arrowLen * Math.Cos(a1), tip.Y + arrowLen * Math.Sin(a1));
        var p2 = new Point(tip.X + arrowLen * Math.Cos(a2), tip.Y + arrowLen * Math.Sin(a2));

        if (arrowType == EdgeArrow.Open)
        {
            // Open arrowhead (two lines, not filled)
            canvas.Children.Add(new Polyline
            {
                Points          = new PointCollection([p1, tip, p2]),
                Stroke          = brush,
                StrokeThickness = 1.5,
                StrokeLineJoin  = PenLineJoin.Round,
            });
        }
        else
        {
            // Filled arrowhead
            canvas.Children.Add(new Polygon
            {
                Points          = new PointCollection([tip, p1, p2]),
                Fill            = brush,
                Stroke          = brush,
                StrokeThickness = 0.5,
            });
        }
    }

}
