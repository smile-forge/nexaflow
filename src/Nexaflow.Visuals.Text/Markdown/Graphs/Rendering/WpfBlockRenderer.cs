using Nexaflow.Visuals.Text.Markdown.Graphs.Charts;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;

namespace Nexaflow.Visuals.Text.Markdown.Graphs.Rendering;

/// <summary>
/// Renders a <see cref="BlockDiagram"/> as a WPF <see cref="FrameworkElement"/>, themed from a
/// <see cref="MarkdownPalette"/>.  Each group is a grid: items wrap into rows by the group's column
/// count, a column is as wide as its widest item, a spanning item widens the columns it covers, and a
/// nested group is measured first and then stretched to the cell it lands in — so the author's
/// placement is what renders, exactly as Mermaid does.  Nodes take the flowchart bracket shapes,
/// block arrows are fat arrow glyphs, edges run centre to centre between any two items by id.
/// Colours come from the palette unless a <c>style</c>/<c>classDef</c> names one.
/// </summary>
public static class WpfBlockRenderer
{
    private static readonly FontFamily BodyFont = new("Segoe UI");

    private const double Outer     = 16;
    private const double TitleH    = 28;
    private const double Gap       = 10;    // between cells
    private const double GroupPad  = 10;    // inside a composite block
    private const double MinCellW  = 64;
    private const double MinCellH  = 40;
    private const double MaxLabelW = 200;
    private const double FontSize  = 12;

    public static FrameworkElement Render(BlockDiagram diagram, MarkdownPalette palette)
    {
        if (diagram.ItemCount == 0)
            return new TextBlock { Text = "(empty block diagram)", Foreground = palette.TextMuted, FontSize = 12 };

        var ctx = new Ctx(diagram.Config, palette);
        var root = ctx.Measure(diagram.Root);
        bool hasTitle = !string.IsNullOrWhiteSpace(diagram.Title);
        double top = Outer + (hasTitle ? TitleH : 0);

        double canvasW = root.W + 2 * Outer, canvasH = top + root.H + Outer;
        var canvas = new Canvas { Width = canvasW, Height = canvasH, Background = palette.CodeBg };

        var rects = new Dictionary<string, Rect>(StringComparer.Ordinal);
        ctx.Arrange(canvas, root, new Rect(Outer, top, root.W, root.H), rects, isRoot: true);

        foreach (var edge in diagram.Edges)
            if (rects.TryGetValue(edge.From, out var ra) && rects.TryGetValue(edge.To, out var rb))
                ctx.DrawEdge(canvas, edge, ra, rb);

        if (hasTitle)
        {
            double tw = MeasureText(diagram.Title, 15);
            canvas.Children.Add(new TextBlock { Text = diagram.Title, Foreground = palette.Heading, FontFamily = BodyFont, FontSize = 15, FontWeight = FontWeights.SemiBold }.At((canvasW - tw) / 2, Outer - 2));
        }

        return new Border
        {
            Background = palette.CodeBg, BorderBrush = palette.CodeBorder, BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(6), Margin = new Thickness(0, 8, 0, 12),
            Child = new ScrollViewer
            {
                Content = canvas,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
                VerticalScrollBarVisibility   = ScrollBarVisibility.Auto,
                MaxHeight = 700,
            },
        };
    }

    // ── Layout ───────────────────────────────────────────────────────────────

    private sealed class Cell
    {
        public required BlockItem Item;
        public int Row, Col, Span;
        public double MinW, MinH;
        public GroupLayout? Child;     // when Item is a BlockGroup
        public TextBlock? Label;       // pre-measured label for nodes/arrows
    }

    private sealed class GroupLayout
    {
        public required BlockGroup Group;
        public List<Cell> Cells = [];
        public double[] ColW = [];
        public double[] RowH = [];
        public double W, H;
    }

    private sealed class Ctx(BlockConfig cfg, MarkdownPalette palette)
    {
        private readonly double _pad = Math.Clamp(cfg.Padding, 0, 40);
        private readonly Color _accent = (palette.Accent    as SolidColorBrush)?.Color ?? Colors.SteelBlue;
        private readonly Color _muted  = (palette.TextMuted as SolidColorBrush)?.Color ?? Colors.Gray;
        private readonly Color _text   = (palette.Text      as SolidColorBrush)?.Color ?? Colors.White;

        /// <summary>Places a group's items on its grid and sizes columns/rows from their content.</summary>
        public GroupLayout Measure(BlockGroup group)
        {
            var layout = new GroupLayout { Group = group };
            int cols = group.Columns ?? Math.Max(1, group.Items.Sum(i => Math.Max(1, i.Width)));

            int row = 0, col = 0;
            foreach (var item in group.Items)
            {
                int span = Math.Clamp(item.Width, 1, cols);
                if (col + span > cols) { row++; col = 0; }
                var cell = new Cell { Item = item, Row = row, Col = col, Span = span };
                MeasureCell(cell);
                layout.Cells.Add(cell);
                col += span;
                if (col >= cols) { row++; col = 0; }
            }

            int rows = layout.Cells.Count == 0 ? 0 : layout.Cells.Max(c => c.Row) + 1;
            layout.ColW = Enumerable.Repeat(MinCellW, cols).ToArray();
            layout.RowH = Enumerable.Repeat(MinCellH, rows).ToArray();

            foreach (var c in layout.Cells)
            {
                layout.RowH[c.Row] = Math.Max(layout.RowH[c.Row], c.MinH);
                if (c.Span == 1) layout.ColW[c.Col] = Math.Max(layout.ColW[c.Col], c.MinW);
            }
            foreach (var c in layout.Cells.Where(c => c.Span > 1))
            {
                double have = 0;
                for (int k = c.Col; k < c.Col + c.Span; k++) have += layout.ColW[k];
                have += (c.Span - 1) * Gap;
                if (c.MinW > have)
                {
                    double extra = (c.MinW - have) / c.Span;
                    for (int k = c.Col; k < c.Col + c.Span; k++) layout.ColW[k] += extra;
                }
            }

            layout.W = layout.ColW.Sum() + Math.Max(0, cols - 1) * Gap;
            layout.H = layout.RowH.Sum() + Math.Max(0, rows - 1) * Gap;
            return layout;
        }

        private void MeasureCell(Cell cell)
        {
            switch (cell.Item)
            {
                case BlockGroup g:
                    cell.Child = Measure(g);
                    cell.MinW = cell.Child.W + 2 * GroupPad;
                    cell.MinH = cell.Child.H + 2 * GroupPad;
                    break;

                case BlockNode n:
                {
                    var label = MakeLabel(n.Label, MaxLabelW, FontSize, FontWeights.Normal, palette.Text);
                    cell.Label = label;
                    double tw = label.DesiredSize.Width, th = label.DesiredSize.Height;
                    double w = tw + 2 * _pad + 12, h = th + 2 * _pad;
                    switch (n.Shape)
                    {
                        case NodeShape.Diamond:                          w = tw * 2 + 2 * _pad;  h = th * 2 + 2 * _pad; break;
                        case NodeShape.Hexagon:                          w += h;                                       break;
                        case NodeShape.Parallelogram or NodeShape.ParallelogramAlt
                          or NodeShape.Trapezoid or NodeShape.TrapezoidAlt: w += h * 0.6;                              break;
                        case NodeShape.Circle or NodeShape.DoubleCircle: w = h = Math.Max(w, h) + (n.Shape == NodeShape.DoubleCircle ? 10 : 0); break;
                        case NodeShape.Cylinder:                         h += 16;                                      break;
                        case NodeShape.Asymmetric:                       w += 12;                                      break;
                    }
                    cell.MinW = Math.Max(MinCellW, w);
                    cell.MinH = Math.Max(MinCellH, h);
                    break;
                }

                case BlockArrow a:
                {
                    var label = MakeLabel(a.Label, MaxLabelW, FontSize, FontWeights.Normal, palette.Text);
                    cell.Label = label;
                    bool vertical = (a.Directions & (BlockArrowDirections.Up | BlockArrowDirections.Down)) != 0
                                 && (a.Directions & (BlockArrowDirections.Left | BlockArrowDirections.Right)) == 0;
                    cell.MinW = Math.Max(MinCellW, label.DesiredSize.Width + (vertical ? 2 * _pad + 12 : 2 * _pad + 40));
                    cell.MinH = Math.Max(MinCellH, label.DesiredSize.Height + (vertical ? 2 * _pad + 40 : 2 * _pad));
                    break;
                }

                default:   // space
                    cell.MinW = MinCellW;
                    cell.MinH = MinCellH;
                    break;
            }
        }

        /// <summary>Draws a measured group into <paramref name="bounds"/>, stretching its grid to fill them.</summary>
        public void Arrange(Canvas canvas, GroupLayout layout, Rect bounds, Dictionary<string, Rect> rects, bool isRoot)
        {
            var colW = (double[])layout.ColW.Clone();
            var rowH = (double[])layout.RowH.Clone();
            if (colW.Length > 0 && bounds.Width  > layout.W) { double e = (bounds.Width  - layout.W) / colW.Length; for (int k = 0; k < colW.Length; k++) colW[k] += e; }
            if (rowH.Length > 0 && bounds.Height > layout.H) { double e = (bounds.Height - layout.H) / rowH.Length; for (int k = 0; k < rowH.Length; k++) rowH[k] += e; }

            var colX = new double[colW.Length + 1];
            for (int k = 0; k < colW.Length; k++) colX[k + 1] = colX[k] + colW[k] + Gap;
            var rowY = new double[rowH.Length + 1];
            for (int k = 0; k < rowH.Length; k++) rowY[k + 1] = rowY[k] + rowH[k] + Gap;

            foreach (var cell in layout.Cells)
            {
                double x = bounds.X + colX[cell.Col];
                double w = colX[cell.Col + cell.Span] - colX[cell.Col] - Gap;
                double y = bounds.Y + rowY[cell.Row];
                double h = rowH[cell.Row];
                var r = new Rect(x, y, w, h);

                switch (cell.Item)
                {
                    case BlockGroup g:
                        DrawGroupBox(canvas, g, r);
                        rects[g.Id] = r;
                        Arrange(canvas, cell.Child!, new Rect(r.X + GroupPad, r.Y + GroupPad, Math.Max(0, r.Width - 2 * GroupPad), Math.Max(0, r.Height - 2 * GroupPad)), rects, isRoot: false);
                        break;
                    case BlockNode n:
                        DrawNode(canvas, n, r, cell.Label!);
                        rects[n.Id] = r;
                        break;
                    case BlockArrow a:
                        DrawBlockArrow(canvas, a, r, cell.Label!);
                        rects[a.Id] = r;
                        break;
                    // a space draws nothing
                }
            }
        }

        // ── Drawing ─────────────────────────────────────────────────────────

        private void DrawGroupBox(Canvas canvas, BlockGroup g, Rect r)
        {
            var (fill, stroke, thickness, dash) = Paint(g.Style, Tint(_muted, 0x16), Solid(Blend(_muted, 0x90)), 1);
            canvas.Children.Add(new Rectangle { Width = r.Width, Height = r.Height, RadiusX = 6, RadiusY = 6, Fill = fill, Stroke = stroke, StrokeThickness = thickness, StrokeDashArray = dash }.At(r.X, r.Y));
        }

        private void DrawNode(Canvas canvas, BlockNode n, Rect r, TextBlock label)
        {
            var (fill, stroke, thickness, dash) = Paint(n.Style, Tint(_accent, 0x40), Solid(_accent), 1.2);

            var shapeRect = r;
            if (n.Shape is NodeShape.Circle or NodeShape.DoubleCircle)
            {
                double side = Math.Min(r.Width, r.Height);
                shapeRect = new Rect(r.X + (r.Width - side) / 2, r.Y + (r.Height - side) / 2, side, side);
            }

            canvas.Children.Add(new Path { Data = ShapeGeometry(n.Shape, shapeRect), Fill = fill, Stroke = stroke, StrokeThickness = thickness, StrokeDashArray = dash });
            foreach (var extra in ShapeDecorations(n.Shape, shapeRect))
                canvas.Children.Add(new Path { Data = extra, Stroke = stroke, StrokeThickness = thickness, StrokeDashArray = dash });

            if (n.Style?.TextColor is string tc && ParseColor(tc) is Color c) label.Foreground = Solid(c);
            double inset = n.Shape switch
            {
                NodeShape.Diamond => r.Width * 0.25,
                NodeShape.Hexagon or NodeShape.Parallelogram or NodeShape.ParallelogramAlt
                  or NodeShape.Trapezoid or NodeShape.TrapezoidAlt => Math.Min(r.Height / 3, r.Width / 4),
                NodeShape.Circle or NodeShape.DoubleCircle => (r.Width - shapeRect.Width) / 2 + shapeRect.Width * 0.12,
                _ => _pad,
            };
            PlaceLabel(canvas, label, new Rect(r.X + inset, r.Y, Math.Max(10, r.Width - 2 * inset), r.Height));
        }

        private void DrawBlockArrow(Canvas canvas, BlockArrow a, Rect r, TextBlock label)
        {
            var (fill, stroke, thickness, dash) = Paint(a.Style, Tint(_muted, 0x40), Solid(Blend(_muted, 0xB0)), 1);
            Geometry? geo = null;
            foreach (var dir in new[] { BlockArrowDirections.Right, BlockArrowDirections.Left, BlockArrowDirections.Up, BlockArrowDirections.Down })
            {
                if ((a.Directions & dir) == 0) continue;
                var g = ArrowGeometry(dir, a.Directions, r);
                geo = geo is null ? g : Geometry.Combine(geo, g, GeometryCombineMode.Union, null);
            }
            if (geo is not null)
                canvas.Children.Add(new Path { Data = geo, Fill = fill, Stroke = stroke, StrokeThickness = thickness, StrokeDashArray = dash, StrokeLineJoin = PenLineJoin.Round });

            if (a.Style?.TextColor is string tc && ParseColor(tc) is Color c) label.Foreground = Solid(c);
            PlaceLabel(canvas, label, new Rect(r.X + 8, r.Y, Math.Max(10, r.Width - 16), r.Height));
        }

        /// <summary>One direction's fat arrow: a body across the cell with a head on that side; the shaft
        /// is shared so that combined directions union into one glyph.</summary>
        private static Geometry ArrowGeometry(BlockArrowDirections dir, BlockArrowDirections all, Rect r)
        {
            double x = r.X + 2, y = r.Y + 2, w = r.Width - 4, h = r.Height - 4;
            double cx = x + w / 2, cy = y + h / 2;
            bool horizontal = dir is BlockArrowDirections.Left or BlockArrowDirections.Right;
            double body = horizontal ? h * 0.5 : w * 0.5;                        // shaft thickness
            double head = horizontal ? Math.Min(w * 0.35, h * 0.6) : Math.Min(h * 0.35, w * 0.6);
            bool opposite = (all & Opposite(dir)) != 0;                         // leave room for the other head
            double tailInset = opposite ? head : 0;

            Point[] pts = dir switch
            {
                BlockArrowDirections.Right => [new(x + tailInset, cy - body / 2), new(x + w - head, cy - body / 2), new(x + w - head, y), new(x + w, cy), new(x + w - head, y + h), new(x + w - head, cy + body / 2), new(x + tailInset, cy + body / 2)],
                BlockArrowDirections.Left  => [new(x + w - tailInset, cy - body / 2), new(x + head, cy - body / 2), new(x + head, y), new(x, cy), new(x + head, y + h), new(x + head, cy + body / 2), new(x + w - tailInset, cy + body / 2)],
                BlockArrowDirections.Down  => [new(cx - body / 2, y + tailInset), new(cx - body / 2, y + h - head), new(x, y + h - head), new(cx, y + h), new(x + w, y + h - head), new(cx + body / 2, y + h - head), new(cx + body / 2, y + tailInset)],
                _                          => [new(cx - body / 2, y + h - tailInset), new(cx - body / 2, y + head), new(x, y + head), new(cx, y), new(x + w, y + head), new(cx + body / 2, y + head), new(cx + body / 2, y + h - tailInset)],
            };
            return Polygon(pts);
        }

        private static BlockArrowDirections Opposite(BlockArrowDirections d) => d switch
        {
            BlockArrowDirections.Right => BlockArrowDirections.Left,
            BlockArrowDirections.Left  => BlockArrowDirections.Right,
            BlockArrowDirections.Up    => BlockArrowDirections.Down,
            _                          => BlockArrowDirections.Up,
        };

        public void DrawEdge(Canvas canvas, BlockEdge edge, Rect ra, Rect rb)
        {
            var ca = Centre(ra); var cb = Centre(rb);
            if (ca == cb) return;
            var p0 = Exit(ra, cb); var p1 = Exit(rb, ca);
            var ink = Solid(Blend(_text, 0xB0));

            canvas.Children.Add(new Line { X1 = p0.X, Y1 = p0.Y, X2 = p1.X, Y2 = p1.Y, Stroke = ink, StrokeThickness = 1.6 });
            if (edge.HasArrow)
            {
                var d = p1 - p0; d.Normalize();
                var n = new Vector(-d.Y, d.X);
                var tip = p1; var back = p1 - d * 10;
                canvas.Children.Add(new Path { Data = Polygon([tip, back + n * 5, back - n * 5]), Fill = ink });
            }
            if (edge.Label.Length > 0)
            {
                var tb = new TextBlock { Text = edge.Label, Foreground = palette.Text, Background = palette.CodeBg, Padding = new Thickness(4, 1, 4, 1), FontFamily = BodyFont, FontSize = 11 };
                tb.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
                var mid = new Point((p0.X + p1.X) / 2, (p0.Y + p1.Y) / 2);
                canvas.Children.Add(tb.At(mid.X - tb.DesiredSize.Width / 2, mid.Y - tb.DesiredSize.Height / 2));
            }
        }

        /// <summary>Resolves an item's paint: explicit style values over the theme defaults.</summary>
        private (Brush fill, Brush stroke, double thickness, DoubleCollection? dash) Paint(BlockStyle? s, Brush fill, Brush stroke, double thickness)
        {
            if (s is null) return (fill, stroke, thickness, null);
            if (s.Fill   is string f && ParseColor(f) is Color fc) fill   = Solid(fc);
            if (s.Stroke is string k && ParseColor(k) is Color sc) stroke = Solid(sc);
            if (s.StrokeWidth is double sw) thickness = Math.Clamp(sw, 0.5, 8);
            return (fill, stroke, thickness, s.Dashed ? new DoubleCollection([5, 4]) : null);
        }
    }

    // ── Shape geometry ────────────────────────────────────────────────────────

    /// <summary>The outline of a flowchart bracket shape fitted to <paramref name="r"/>.</summary>
    private static Geometry ShapeGeometry(NodeShape shape, Rect r)
    {
        double x = r.X, y = r.Y, w = r.Width, h = r.Height, cx = x + w / 2, cy = y + h / 2;
        double k = Math.Min(h / 3, w / 4);
        switch (shape)
        {
            case NodeShape.RoundedRect:      return new RectangleGeometry(r, 8, 8);
            case NodeShape.Stadium:          return new RectangleGeometry(r, h / 2, h / 2);
            case NodeShape.Circle:
            case NodeShape.DoubleCircle:     return new EllipseGeometry(r);
            case NodeShape.Diamond:          return Polygon([new(cx, y), new(x + w, cy), new(cx, y + h), new(x, cy)]);
            case NodeShape.Hexagon:          return Polygon([new(x + k, y), new(x + w - k, y), new(x + w, cy), new(x + w - k, y + h), new(x + k, y + h), new(x, cy)]);
            case NodeShape.Asymmetric:       return Polygon([new(x, y), new(x + w, y), new(x + w, y + h), new(x, y + h), new(x + Math.Min(12, w / 4), cy)]);
            case NodeShape.Parallelogram:    return Polygon([new(x + k, y), new(x + w, y), new(x + w - k, y + h), new(x, y + h)]);
            case NodeShape.ParallelogramAlt: return Polygon([new(x, y), new(x + w - k, y), new(x + w, y + h), new(x + k, y + h)]);
            case NodeShape.Trapezoid:        return Polygon([new(x, y), new(x + w, y), new(x + w - k, y + h), new(x + k, y + h)]);
            case NodeShape.TrapezoidAlt:     return Polygon([new(x + k, y), new(x + w - k, y), new(x + w, y + h), new(x, y + h)]);
            case NodeShape.Cylinder:
            {
                double ry = Math.Min(10, h * 0.15);
                var g = new StreamGeometry();
                using (var c = g.Open())
                {
                    c.BeginFigure(new Point(x, y + ry), true, true);
                    c.ArcTo(new Point(x + w, y + ry), new Size(w / 2, ry), 0, false, SweepDirection.Clockwise, true, true);
                    c.LineTo(new Point(x + w, y + h - ry), true, true);
                    c.ArcTo(new Point(x, y + h - ry), new Size(w / 2, ry), 0, false, SweepDirection.Clockwise, true, true);
                }
                g.Freeze();
                return g;
            }
            default:                         return new RectangleGeometry(r);
        }
    }

    /// <summary>Extra strokes a shape carries beyond its outline: subroutine bars, the cylinder rim, the inner circle.</summary>
    private static IEnumerable<Geometry> ShapeDecorations(NodeShape shape, Rect r)
    {
        double x = r.X, y = r.Y, w = r.Width, h = r.Height;
        switch (shape)
        {
            case NodeShape.Subroutine:
                yield return new LineGeometry(new Point(x + 6, y), new Point(x + 6, y + h));
                yield return new LineGeometry(new Point(x + w - 6, y), new Point(x + w - 6, y + h));
                break;
            case NodeShape.Cylinder:
            {
                double ry = Math.Min(10, h * 0.15);
                var g = new StreamGeometry();
                using (var c = g.Open())
                {
                    c.BeginFigure(new Point(x + w, y + ry), false, false);
                    c.ArcTo(new Point(x, y + ry), new Size(w / 2, ry), 0, false, SweepDirection.Clockwise, true, true);
                }
                g.Freeze();
                yield return g;
                break;
            }
            case NodeShape.DoubleCircle:
                yield return new EllipseGeometry(new Rect(x + 5, y + 5, Math.Max(0, w - 10), Math.Max(0, h - 10)));
                break;
        }
    }

    private static Geometry Polygon(Point[] pts)
    {
        var figure = new PathFigure { StartPoint = pts[0], IsClosed = true, IsFilled = true };
        for (int i = 1; i < pts.Length; i++) figure.Segments.Add(new LineSegment(pts[i], true));
        var g = new PathGeometry();
        g.Figures.Add(figure);
        g.Freeze();
        return g;
    }

    private static Point Centre(Rect r) => new(r.X + r.Width / 2, r.Y + r.Height / 2);

    /// <summary>Where the ray from <paramref name="r"/>'s centre towards <paramref name="toward"/> leaves the rectangle.</summary>
    private static Point Exit(Rect r, Point toward)
    {
        var c = Centre(r);
        double dx = toward.X - c.X, dy = toward.Y - c.Y;
        if (dx == 0 && dy == 0) return c;
        double sx = dx == 0 ? double.PositiveInfinity : (r.Width  / 2) / Math.Abs(dx);
        double sy = dy == 0 ? double.PositiveInfinity : (r.Height / 2) / Math.Abs(dy);
        double s = Math.Min(sx, sy);
        return new Point(c.X + dx * s, c.Y + dy * s);
    }

    // ── Text & colour helpers ─────────────────────────────────────────────────

    private static void PlaceLabel(Canvas canvas, TextBlock label, Rect r)
    {
        label.Width = r.Width;
        label.TextAlignment = TextAlignment.Center;
        label.Measure(new Size(r.Width, double.PositiveInfinity));
        canvas.Children.Add(label.At(r.X, r.Y + Math.Max(0, (r.Height - label.DesiredSize.Height) / 2)));
    }

    private static TextBlock MakeLabel(string text, double maxWidth, double fontSize, FontWeight weight, Brush brush)
    {
        var tb = new TextBlock
        {
            Text = text, MaxWidth = maxWidth, TextWrapping = TextWrapping.Wrap, TextAlignment = TextAlignment.Center,
            Foreground = brush, FontFamily = BodyFont, FontSize = fontSize, FontWeight = weight,
        };
        tb.Measure(new Size(maxWidth, double.PositiveInfinity));
        return tb;
    }

    private static Color? ParseColor(string s)
    {
        try { return (Color)ColorConverter.ConvertFromString(s.Trim())!; }
        catch { return null; }
    }

    private static Brush Tint(Color c, byte a) { var b = new SolidColorBrush(Color.FromArgb(a, c.R, c.G, c.B)); b.Freeze(); return b; }
    private static Brush Solid(Color c)        { var b = new SolidColorBrush(c); b.Freeze(); return b; }
    private static Color Blend(Color c, byte a) => Color.FromArgb(a, c.R, c.G, c.B);

    private static double MeasureText(string text, double fontSize)
    {
        var ft = new FormattedText(text, CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
            new Typeface(BodyFont, FontStyles.Normal, FontWeights.Normal, FontStretches.Normal), fontSize, Brushes.Black, 1.0);
        return ft.Width;
    }
}
