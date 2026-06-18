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

    private static Brush BgBrush        = Frozen(Color.FromRgb(0x0D, 0x10, 0x1A));
    private static Brush NodeBg         = Frozen(Color.FromRgb(0x1E, 0x24, 0x38));
    private static Brush NodeBorder     = Frozen(Color.FromRgb(0x4F, 0x8E, 0xF7));
    private static Brush NodeText       = Frozen(Color.FromRgb(0xE8, 0xEA, 0xF2));
    private static Brush DiamondBg      = Frozen(Color.FromRgb(0x2A, 0x1A, 0x3A));
    private static Brush DiamondBorder  = Frozen(Color.FromRgb(0xA0, 0x60, 0xFF));
    private static Brush EdgeBrush      = Frozen(Color.FromRgb(0x4F, 0x8E, 0xF7));
    private static Brush EdgeDashedBrush= Frozen(Color.FromRgb(0x78, 0x80, 0xA0));
    private static Brush EdgeThickBrush = Frozen(Color.FromRgb(0xFF, 0xD0, 0x60));
    private static Brush LabelBg        = Frozen(Color.FromRgb(0x12, 0x16, 0x24));
    private static Brush LabelText      = Frozen(Color.FromRgb(0x78, 0x80, 0xA0));
    private static Brush TitleBrush     = Frozen(Color.FromRgb(0xA8, 0xD4, 0xFF));
    private static Color AccentColor    = Color.FromRgb(0x4F, 0x8E, 0xF7);
    // State-diagram extras: solid pseudostate fill (start/end/fork) + dashed note callout.
    private static Brush StateFill      = Frozen(Color.FromRgb(0xE8, 0xEA, 0xF2));
    private static Brush NoteBg         = Frozen(Color.FromArgb(0x33, 0xF5, 0x9E, 0x0B));
    private static Brush NoteBorder     = Frozen(Color.FromRgb(0xF5, 0x9E, 0x0B));

    private static readonly FontFamily BodyFont = new("Segoe UI");
    private const double FontSize = 12.0;

    private static Brush Frozen(Color c) { var b = new SolidColorBrush(c); b.Freeze(); return b; }

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
        NoteBg      = Frozen(Color.FromArgb(0x33, nc.R, nc.G, nc.B));
    }

    // ── Public API ─────────────────────────────────────────────────────────

    public static FrameworkElement Render(LayoutedGraph lg, MarkdownPalette palette)
    {
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

        // Edges (below nodes)
        foreach (var le in lg.Edges)
            DrawEdge(canvas, le, horizontal);

        // Nodes
        foreach (var ln in lg.AllNodes.Where(n => !n.IsDummy))
            DrawNode(canvas, ln);

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
            Canvas.SetLeft(tb, SugiyamaLayout.MarginX);
            Canvas.SetTop(tb, 6);
            canvas.Children.Add(tb);
        }

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
                MaxHeight                     = 600,
            }
        };
    }

    // ── Node drawing ───────────────────────────────────────────────────────

    private static void DrawNode(Canvas canvas, LayoutNode ln)
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
            _                          => DrawRectShape(ln),
        };
        canvas.Children.Add(shape);

        if (ln.Source is not null && ln.Source.Label.Length > 0)
        {
            string label = ln.Source.Label;
            bool   isNote = ln.Source.Shape == NodeShape.Note;
            int lines = label.Count(c => c == '\n') + 1;
            double maxW = ln.Source.Shape == NodeShape.Diamond ? ln.Width * 0.7
                        : isNote ? ln.Width - 16 : ln.Width - 12;

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
        }
    }

    private static Brush NodeFill(LayoutNode ln, Brush def) =>
        ln.Source?.FillColor is string fc ? Frozen(ParseColor(fc)) : def;
    private static Brush NodeStroke(LayoutNode ln, Brush def) =>
        ln.Source?.StrokeColor is string sc ? Frozen(ParseColor(sc)) : def;

    private static Brush GetTextBrush(LayoutNode ln)
    {
        if (ln.Source?.FillColor is not string fc) return NodeText;
        try
        {
            var c   = (System.Windows.Media.Color)ColorConverter.ConvertFromString(fc)!;
            double lum = 0.299 * c.R + 0.587 * c.G + 0.114 * c.B;
            return lum > 140 ? Frozen(Colors.Black) : NodeText;
        }
        catch { return NodeText; }
    }

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
        double w = MeasureText(label, 11);
        Canvas.SetLeft(tb, bounds.Left + (bounds.Width - w) / 2.0);   // centred on the header
        Canvas.SetTop(tb,  bounds.Top  + (headerH - 14) / 2.0);
        canvas.Children.Add(tb);
    }

    // ── Edge drawing ───────────────────────────────────────────────────────

    private static void DrawEdge(Canvas canvas, LayoutEdge le, bool horizontal)
    {
        var pts = le.Waypoints;
        if (pts.Count < 2) return;

        var edge      = le.Source;
        var brush     = edge?.Style switch
        {
            EdgeStyle.Thick  => EdgeThickBrush,
            EdgeStyle.Dashed => EdgeDashedBrush,
            EdgeStyle.Dotted => EdgeDashedBrush,
            _                => EdgeBrush,
        };
        double thickness = edge?.Style == EdgeStyle.Thick ? 2.5 : 1.5;

        var (path, arrowFrom) = BuildBezierPath(pts, horizontal);
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
        // arrowhead belongs at the START of the path, not the end.
        bool rev = edge?.IsReversed == true;
        Point endTip   = rev ? pts[0]     : pts[^1];
        Point endFrom  = rev ? pts[1]     : arrowFrom;
        Point startTip = rev ? pts[^1]    : pts[0];
        Point startFrom = rev ? arrowFrom : pts[1];

        if (edge?.Arrow != EdgeArrow.None)
            DrawArrowhead(canvas, endFrom, endTip, brush, edge?.Arrow ?? EdgeArrow.Normal);

        // Start head — for multidirectional links (o--o, x--x, <-->).
        if (edge is { StartArrow: not EdgeArrow.None })
            DrawArrowhead(canvas, startFrom, startTip, brush, edge.StartArrow);

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
    /// Builds a smooth cubic-bezier path through <paramref name="pts"/>.
    /// Returns the path and the last bezier control point (for arrowhead direction).
    /// </summary>
    private static (Path path, Point arrowFrom) BuildBezierPath(IList<Point> pts, bool horizontal)
    {
        var figure  = new PathFigure { StartPoint = pts[0], IsFilled = false };
        Point lastCp2 = pts[^2]; // fallback

        if (pts.Count == 2)
        {
            // Single cubic bezier: control points pulled along the primary flow axis.
            var p0 = pts[0]; var p1 = pts[1];
            double dx = p1.X - p0.X, dy = p1.Y - p0.Y;
            var cp1 = horizontal ? new Point(p0.X + dx * 0.5, p0.Y) : new Point(p0.X,           p0.Y + dy * 0.5);
            var cp2 = horizontal ? new Point(p1.X - dx * 0.5, p1.Y) : new Point(p1.X,           p1.Y - dy * 0.5);
            lastCp2 = cp2;
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
                if (i == ext.Count - 3) lastCp2 = cp2;
                figure.Segments.Add(new BezierSegment(cp1, cp2, p1, isStroked: true));
            }
        }

        return (new Path { Data = new PathGeometry([figure]) }, lastCp2);
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
        double w = MeasureText(text, labelFont) + 10;
        double h = labelFont * 1.35 + 4;
        Canvas.SetLeft(border, mid.X - w / 2.0);
        Canvas.SetTop(border,  mid.Y - h / 2.0);
        canvas.Children.Add(border);
    }

    private static void DrawArrowhead(Canvas canvas, Point from, Point tip, Brush brush, EdgeArrow arrowType)
    {
        const double arrowLen   = 10;
        const double arrowAngle = 25 * Math.PI / 180;

        double angle = Math.Atan2(tip.Y - from.Y, tip.X - from.X);

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

    // ── Text helpers ───────────────────────────────────────────────────────

    private static double MeasureText(string text, double fontSize)
    {
        var ft = new FormattedText(
            text, System.Globalization.CultureInfo.CurrentCulture,
            FlowDirection.LeftToRight,
            new Typeface(BodyFont, FontStyles.Normal, FontWeights.Normal, FontStretches.Normal),
            fontSize, Brushes.Black, 1.0);
        return ft.Width;
    }

    private static Color ParseColor(string hex)
    {
        try { return (Color)ColorConverter.ConvertFromString(hex)!; }
        catch { return Color.FromRgb(0x4F, 0x8E, 0xF7); }
    }
}
