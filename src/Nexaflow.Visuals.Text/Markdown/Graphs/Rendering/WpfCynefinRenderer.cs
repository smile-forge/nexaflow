using Nexaflow.Visuals.Text.Markdown.Graphs.Charts;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;

namespace Nexaflow.Visuals.Text.Markdown.Graphs.Rendering;

/// <summary>
/// Renders a <see cref="CynefinDiagram"/> as a WPF <see cref="FrameworkElement"/>, themed from a
/// <see cref="MarkdownPalette"/>.  Layout is a fixed 2×2 grid — Complex (top-left), Complicated
/// (top-right), Chaotic (bottom-left), Clear (bottom-right) — with the Confusion domain as a central
/// ellipse.  Each domain shows its items as badges (the confusion ellipse caps at
/// <see cref="CynefinDiagram.ConfusionMaxBadges"/> with a <c>+N more</c> overflow badge); transitions
/// draw as labelled arrows between domain centres.  Domain tints come from the config theme variables
/// when present, else the palette's <see cref="MarkdownPalette.Series"/> bank.
/// </summary>
public static class WpfCynefinRenderer
{
    private static readonly FontFamily BodyFont = DiagramText.BodyFont;

    private const double Outer    = 16;
    private const double TitleH   = 28;
    private const double PlotSize = 460;
    private const double BadgeH   = 20;
    private const double BadgeGap = 4;
    private const double ConfusionW = 138;
    private const double ConfusionH = 84;

    // Per-domain palette-series indices (a distinct hue each; overridable via config).
    private const int SeriesComplex     = 4; // purple
    private const int SeriesComplicated = 0; // blue
    private const int SeriesClear       = 2; // green
    private const int SeriesChaotic     = 1; // red
    private const int SeriesConfusion   = 7; // pink

    public static FrameworkElement Render(CynefinDiagram diagram, MarkdownPalette palette)
    {
        Brush bgBrush     = palette.CodeBg;
        Brush borderBrush = palette.CodeBorder;
        Brush titleBrush  = palette.Heading;
        Brush textBrush   = palette.Text;
        Brush mutedBrush  = palette.TextMuted;
        Brush gridBrush   = palette.CodeBorder;
        var   series      = palette.Series;

        bool   hasTitle = !string.IsNullOrWhiteSpace(diagram.Title);
        double plotLeft = Outer;
        double plotTop  = Outer + (hasTitle ? TitleH : 0);
        double canvasW  = plotLeft + PlotSize + Outer;
        double canvasH  = plotTop + PlotSize + Outer;
        double half     = PlotSize / 2;

        var canvas = new Canvas { Width = canvasW, Height = canvasH, Background = bgBrush };

        if (hasTitle)
        {
            var tb = new TextBlock
            {
                Text = diagram.Title, Foreground = titleBrush, FontFamily = BodyFont,
                FontSize = 15, FontWeight = FontWeights.SemiBold,
            };
            Canvas.SetLeft(tb, plotLeft);
            Canvas.SetTop(tb, Outer - 2);
            canvas.Children.Add(tb);
        }

        // Domain cells: (left, top) of each quadrant.  Bottom domains anchor their content to the cell
        // bottom so it stays in the outer corner, clear of the central confusion ellipse.
        bool descr = diagram.Config.ShowDomainDescriptions;
        AddDomain(canvas, diagram, CynefinDomain.Complex,     diagram.Config.ComplexBg     ?? Tint(series, SeriesComplex),
                  "Complex",     "probe · sense · respond", plotLeft,        plotTop,        half, bottomAnchored: false, textBrush, mutedBrush, descr);
        AddDomain(canvas, diagram, CynefinDomain.Complicated, diagram.Config.ComplicatedBg ?? Tint(series, SeriesComplicated),
                  "Complicated", "sense · analyse · respond", plotLeft + half, plotTop,        half, bottomAnchored: false, textBrush, mutedBrush, descr);
        AddDomain(canvas, diagram, CynefinDomain.Chaotic,     diagram.Config.ChaoticBg     ?? Tint(series, SeriesChaotic),
                  "Chaotic",     "act · sense · respond", plotLeft,        plotTop + half, half, bottomAnchored: true, textBrush, mutedBrush, descr);
        AddDomain(canvas, diagram, CynefinDomain.Clear,       diagram.Config.ClearBg       ?? Tint(series, SeriesClear),
                  "Clear",       "sense · categorise · respond", plotLeft + half, plotTop + half, half, bottomAnchored: true, textBrush, mutedBrush, descr);

        // Grid border + centre dividers.
        var plotBorder = new Rectangle
        {
            Width = PlotSize, Height = PlotSize,
            Stroke = diagram.Config.BoundaryColor ?? gridBrush, StrokeThickness = 1, Fill = Brushes.Transparent,
        };
        Canvas.SetLeft(plotBorder, plotLeft);
        Canvas.SetTop(plotBorder, plotTop);
        canvas.Children.Add(plotBorder);

        // Curved domain dividers — the signature Cynefin swirl: each half-divider curves from an edge
        // midpoint into the central disorder blob (ends hidden behind the confusion ellipse drawn later).
        AddCurvedDividers(canvas, plotLeft, plotTop, PlotSize, diagram.Config.BoundaryColor ?? gridBrush);

        // Transitions (drawn beneath the confusion ellipse so the centre node sits on top).
        foreach (var t in diagram.Transitions)
            AddTransition(canvas, t, mutedBrush, textBrush, plotLeft, plotTop, half);

        // Confusion — central ellipse with up to N badges + a "+N more" overflow badge.
        AddConfusion(canvas, diagram, diagram.Config.ConfusionBg ?? Tint(series, SeriesConfusion),
                     bgBrush, gridBrush, textBrush, mutedBrush, plotLeft + half, plotTop + half);

        return new Border
        {
            Background = bgBrush, BorderBrush = borderBrush, BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(6), Margin = new Thickness(0, 8, 0, 12), Child = canvas,
        };
    }

    // ── Domain cell ──────────────────────────────────────────────────────────

    private static void AddDomain(Canvas canvas, CynefinDiagram diagram, CynefinDomain domain, Brush fill,
        string name, string description, double left, double top, double size, bool bottomAnchored,
        Brush textBrush, Brush mutedBrush, bool showDescr)
    {
        var rect = new Rectangle { Width = size, Height = size, Fill = fill };
        Canvas.SetLeft(rect, left);
        Canvas.SetTop(rect, top);
        canvas.Children.Add(rect);

        var items = diagram.ItemsIn(domain);

        if (!bottomAnchored)
        {
            // Top domains: name at the top, items stacked downward.
            double y = top + 8;
            AddText(canvas, name, textBrush, left + 10, y, semibold: true, size: 13);
            y += 20;
            if (showDescr) { AddText(canvas, description, mutedBrush, left + 10, y, semibold: false, size: 10); y += 16; }

            double maxY = top + size - 8;
            foreach (var item in items)
            {
                if (y + BadgeH > maxY) break;
                AddBadge(canvas, item.Text, textBrush, mutedBrush, left + 10, y, size - 20);
                y += BadgeH + BadgeGap;
            }
        }
        else
        {
            // Bottom domains: name at the bottom, items stacked upward above it.
            double nameY = top + size - 26;
            AddText(canvas, name, textBrush, left + 10, nameY, semibold: true, size: 13);
            if (showDescr) AddText(canvas, description, mutedBrush, left + 10, nameY - 15, semibold: false, size: 10);

            double y = nameY - (showDescr ? 15 : 0) - (BadgeH + BadgeGap);
            double minY = top + 8;
            foreach (var item in items)
            {
                if (y < minY) break;
                AddBadge(canvas, item.Text, textBrush, mutedBrush, left + 10, y, size - 20);
                y -= BadgeH + BadgeGap;
            }
        }
    }

    // ── Confusion centre ─────────────────────────────────────────────────────

    private static void AddConfusion(Canvas canvas, CynefinDiagram diagram, Brush fill,
        Brush bgBrush, Brush strokeBrush, Brush textBrush, Brush mutedBrush, double cx, double cy)
    {
        var items = diagram.ItemsIn(CynefinDomain.Confusion);
        double ew = ConfusionW, eh = ConfusionH;

        var ellipse = new Ellipse
        {
            Width = ew, Height = eh, Fill = Blend(bgBrush, fill), Stroke = strokeBrush, StrokeThickness = 1,
        };
        Canvas.SetLeft(ellipse, cx - ew / 2);
        Canvas.SetTop(ellipse, cy - eh / 2);
        canvas.Children.Add(ellipse);

        double y = cy - eh / 2 + 8;
        AddCentredText(canvas, "Confusion", textBrush, cx, y, semibold: true, size: 12);
        y += 18;

        int shown = Math.Min(items.Count, CynefinDiagram.ConfusionMaxBadges);
        for (int i = 0; i < shown; i++)
        {
            AddCentredText(canvas, items[i].Text, textBrush, cx, y, semibold: false, size: 10);
            y += 14;
        }
        if (diagram.ConfusionOverflow > 0)
            AddCentredText(canvas, $"+{diagram.ConfusionOverflow} more", textBrush, cx, y, semibold: true, size: 10);
    }

    // ── Transitions ──────────────────────────────────────────────────────────

    private static void AddTransition(Canvas canvas, CynefinTransition t, Brush lineBrush, Brush textBrush,
        double plotLeft, double plotTop, double half)
    {
        var (fx, fy) = Centre(t.From, plotLeft, plotTop, half);
        var (tx, ty) = Centre(t.To,   plotLeft, plotTop, half);
        if (fx == tx && fy == ty) return;

        canvas.Children.Add(new Line
        {
            X1 = fx, Y1 = fy, X2 = tx, Y2 = ty,
            Stroke = lineBrush, StrokeThickness = 1.5, StrokeDashArray = [3, 2],
        });
        AddArrowHead(canvas, fx, fy, tx, ty, lineBrush);

        if (!string.IsNullOrWhiteSpace(t.Label))
        {
            double w = DiagramText.Measure(t.Label, 10);
            AddText(canvas, t.Label, textBrush, (fx + tx) / 2 - w / 2, (fy + ty) / 2 - 7, semibold: false, size: 10);
        }
    }

    private static (double x, double y) Centre(CynefinDomain d, double plotLeft, double plotTop, double half)
    {
        double q = half / 2;
        return d switch
        {
            CynefinDomain.Complex     => (plotLeft + q,        plotTop + q),
            CynefinDomain.Complicated => (plotLeft + half + q, plotTop + q),
            CynefinDomain.Chaotic     => (plotLeft + q,        plotTop + half + q),
            CynefinDomain.Clear       => (plotLeft + half + q, plotTop + half + q),
            _                         => (plotLeft + half,     plotTop + half),   // Confusion → centre
        };
    }

    private static void AddArrowHead(Canvas canvas, double x1, double y1, double x2, double y2, Brush brush)
    {
        double angle = Math.Atan2(y2 - y1, x2 - x1);
        const double len = 8, spread = 0.5;
        var p1 = new Point(x2 - len * Math.Cos(angle - spread), y2 - len * Math.Sin(angle - spread));
        var p2 = new Point(x2 - len * Math.Cos(angle + spread), y2 - len * Math.Sin(angle + spread));
        var head = new Polygon { Fill = brush, Points = [new Point(x2, y2), p1, p2] };
        canvas.Children.Add(head);
    }

    // ── Badges & text helpers ─────────────────────────────────────────────────

    private static void AddBadge(Canvas canvas, string text, Brush textBrush, Brush stroke, double left, double top, double maxW)
    {
        var tb = new TextBlock
        {
            Text = text, Foreground = textBrush, FontFamily = BodyFont, FontSize = 11,
            MaxWidth = maxW - 12, TextTrimming = TextTrimming.CharacterEllipsis,
        };
        var border = new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(0x22, 0x88, 0x88, 0x88)),
            BorderBrush = stroke, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(4),
            Padding = new Thickness(6, 1, 6, 1), Height = BadgeH, Child = tb,
        };
        Canvas.SetLeft(border, left);
        Canvas.SetTop(border, top);
        canvas.Children.Add(border);
    }

    private static void AddText(Canvas canvas, string text, Brush brush, double left, double top, bool semibold, double size)
    {
        if (string.IsNullOrWhiteSpace(text)) return;
        var tb = new TextBlock
        {
            Text = text, Foreground = brush, FontFamily = BodyFont, FontSize = size,
            FontWeight = semibold ? FontWeights.SemiBold : FontWeights.Normal,
        };
        Canvas.SetLeft(tb, left);
        Canvas.SetTop(tb, top);
        canvas.Children.Add(tb);
    }

    private static void AddCentredText(Canvas canvas, string text, Brush brush, double cx, double top, bool semibold, double size)
    {
        if (string.IsNullOrWhiteSpace(text)) return;
        double w = DiagramText.Measure(text, size);
        AddText(canvas, text, brush, cx - w / 2, top, semibold, size);
    }

    private static void AddLine(Canvas canvas, double x1, double y1, double x2, double y2, Brush brush)
    {
        canvas.Children.Add(new Line { X1 = x1, Y1 = y1, X2 = x2, Y2 = y2, Stroke = brush, StrokeThickness = 1 });
    }

    /// <summary>Draws the four curved domain boundaries as a gentle clockwise pinwheel: each half-divider
    /// sweeps from an outer edge midpoint to the central disorder blob, bowed tangentially.</summary>
    private static void AddCurvedDividers(Canvas canvas, double plotLeft, double plotTop, double size, Brush brush)
    {
        double cx = plotLeft + size / 2, cy = plotTop + size / 2;
        double erx = ConfusionW / 2, ery = ConfusionH / 2;
        double bow = 30;   // tangential bow that gives the swirl

        // top → blob-top (bow right); right → blob-right (bow down); bottom → blob-bottom (bow left); left → blob-left (bow up).
        AddCurve(canvas, new Point(cx, plotTop),              new Point(cx + bow, (plotTop + cy - ery) / 2),       new Point(cx, cy - ery), brush);
        AddCurve(canvas, new Point(plotLeft + size, cy),      new Point((plotLeft + size + cx + erx) / 2, cy + bow), new Point(cx + erx, cy), brush);
        AddCurve(canvas, new Point(cx, plotTop + size),       new Point(cx - bow, (plotTop + size + cy + ery) / 2), new Point(cx, cy + ery), brush);
        AddCurve(canvas, new Point(plotLeft, cy),             new Point((plotLeft + cx - erx) / 2, cy - bow),      new Point(cx - erx, cy), brush);
    }

    private static void AddCurve(Canvas canvas, Point start, Point control, Point end, Brush brush)
    {
        var fig = new PathFigure { StartPoint = start };
        fig.Segments.Add(new QuadraticBezierSegment(control, end, isStroked: true));
        var geo = new PathGeometry();
        geo.Figures.Add(fig);
        canvas.Children.Add(new Path { Data = geo, Stroke = brush, StrokeThickness = 1.3 });
    }


    /// <summary>A translucent tint of a series colour for a domain background.</summary>
    private static Brush Tint(IReadOnlyList<Brush> series, int idx)
    {
        Color c = (series[idx % series.Count] as SolidColorBrush)?.Color ?? Colors.Gray;
        var fill = new SolidColorBrush(Color.FromArgb(0x22, c.R, c.G, c.B));
        fill.Freeze();
        return fill;
    }

    /// <summary>Opaque-ish blend of the surface with a tint, used for the confusion ellipse fill.</summary>
    private static Brush Blend(Brush bg, Brush tint)
    {
        Color b = (bg as SolidColorBrush)?.Color ?? Colors.Black;
        Color t = (tint as SolidColorBrush)?.Color ?? Colors.Gray;
        var mixed = new SolidColorBrush(Color.FromArgb(0xEE, (byte)((b.R + t.R) / 2), (byte)((b.G + t.G) / 2), (byte)((b.B + t.B) / 2)));
        mixed.Freeze();
        return mixed;
    }
}
