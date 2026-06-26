using Nexaflow.Visuals.Text.Markdown.Graphs.Charts;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;

namespace Nexaflow.Visuals.Text.Markdown.Graphs.Rendering;

/// <summary>
/// Renders a Mermaid <see cref="RadarChart"/> (radar / spider / Kiviat chart) as a WPF
/// <see cref="FrameworkElement"/>, themed from a <see cref="MarkdownPalette"/> and overridden by the
/// chart's parsed <see cref="RadarConfig"/> (geometry, the <c>themeVariables.radar</c> styling, and the
/// <c>cScale</c> curve palette).
///
/// Axes radiate from the centre (first axis at the top, clockwise); a graticule of concentric
/// rings/polygons marks the scale; each curve is a closed cardinal spline (rounded by
/// <see cref="RadarConfig.CurveTension"/>) filled at <see cref="RadarConfig.CurveOpacity"/>.
/// </summary>
public static class WpfRadarRenderer
{
    private static readonly FontFamily BodyFont = new("Segoe UI");
    private const double Outer = 4;

    public static FrameworkElement Render(RadarChart chart, MarkdownPalette palette)
    {
        var cfg = chart.Config;

        Brush titleBrush   = cfg.TitleColor ?? palette.Heading;
        Brush axisBrush    = cfg.AxisColor ?? palette.TextMuted;
        Brush gratiBrush   = cfg.GraticuleColor ?? palette.CodeBorder;
        Brush labelBrush   = palette.Text;
        Brush legendBrush  = palette.Text;
        Brush bg           = palette.CodeBg;

        Brush CurveBrush(int i) =>
            i < cfg.CurvePalette.Count ? cfg.CurvePalette[i] : palette.Series[i % palette.Series.Count];

        int n = chart.Axes.Count;
        bool hasTitle  = !string.IsNullOrWhiteSpace(chart.Title);
        bool hasLegend = chart.ShowLegend && chart.Curves.Count > 0;

        double canvasW = Math.Max(cfg.Width, 200);
        double canvasH = Math.Max(cfg.Height, 200);

        double titleH  = hasTitle ? cfg.TitleFontSize + 12 : 0;
        // Lay the legend out first (it may wrap to several rows), so its height is known before sizing the plot.
        var legendRows = hasLegend
            ? LayoutLegendRows(chart.Curves, cfg.LegendFontSize, cfg.LegendBoxSize, canvasW - 2 * Outer)
            : [];
        double legendH = hasLegend ? legendRows.Count * (cfg.LegendFontSize + 6) + 8 : 0;

        double top    = cfg.MarginTop + titleH;
        double bottom = canvasH - cfg.MarginBottom - legendH;
        double left   = cfg.MarginLeft;
        double right  = canvasW - cfg.MarginRight;

        double cx = (left + right) / 2;
        double cy = (top + bottom) / 2;
        double baseR = Math.Max(Math.Min(right - left, bottom - top) / 2, 20);

        double maxLabelW = n > 0 ? chart.Axes.Max(a => Measure(a.Display, cfg.AxisLabelFontSize).Width) : 0;
        double labelReserve = maxLabelW * 0.55 + 12;
        double outerR = Math.Max((baseR - labelReserve) * Math.Clamp(cfg.AxisScaleFactor, 0.05, 2), 16);

        var canvas = new Canvas { Width = canvasW, Height = canvasH, Background = bg };

        // Title
        if (hasTitle)
        {
            var sz = Measure(chart.Title, cfg.TitleFontSize);
            AddText(canvas, chart.Title, titleBrush, cx - sz.Width / 2, Outer + (titleH - cfg.TitleFontSize) / 2, cfg.TitleFontSize, FontWeights.SemiBold);
        }

        if (n == 0)
            return Frame(canvas, bg, palette.CodeBorder);

        // Axis angles: first at top (-90°), clockwise.
        double Angle(int i) => -Math.PI / 2 + i * 2 * Math.PI / n;
        Point Polar(double r, int i) => new(cx + r * Math.Cos(Angle(i)), cy + r * Math.Sin(Angle(i)));

        // ── Graticule ───────────────────────────────────────────────────────
        for (int k = 1; k <= chart.Ticks; k++)
        {
            double r = outerR * k / chart.Ticks;
            if (chart.Graticule == RadarGraticule.Circle)
            {
                var ring = new Ellipse
                {
                    Width = r * 2, Height = r * 2,
                    Stroke = gratiBrush, StrokeThickness = cfg.GraticuleStrokeWidth, Opacity = cfg.GraticuleOpacity,
                    Fill = Brushes.Transparent,
                };
                Canvas.SetLeft(ring, cx - r);
                Canvas.SetTop(ring, cy - r);
                canvas.Children.Add(ring);
            }
            else
            {
                var poly = new Polygon { Stroke = gratiBrush, StrokeThickness = cfg.GraticuleStrokeWidth, Opacity = cfg.GraticuleOpacity };
                for (int i = 0; i < n; i++) poly.Points.Add(Polar(r, i));
                canvas.Children.Add(poly);
            }
        }

        // ── Axis spokes + labels ────────────────────────────────────────────
        for (int i = 0; i < n; i++)
        {
            var p = Polar(outerR, i);
            canvas.Children.Add(new Line { X1 = cx, Y1 = cy, X2 = p.X, Y2 = p.Y, Stroke = axisBrush, StrokeThickness = cfg.AxisStrokeWidth, Opacity = 0.85 });

            var lp = Polar(outerR * cfg.AxisLabelFactor + 4, i);
            var sz = Measure(chart.Axes[i].Display, cfg.AxisLabelFontSize);
            double ca = Math.Cos(Angle(i));
            double lx = ca > 0.3 ? lp.X : ca < -0.3 ? lp.X - sz.Width : lp.X - sz.Width / 2;
            AddText(canvas, chart.Axes[i].Display, labelBrush, lx, lp.Y - sz.Height / 2, cfg.AxisLabelFontSize);
        }

        // ── Curves ──────────────────────────────────────────────────────────
        double min = chart.Min;
        double max = chart.Max ?? AutoMax(chart, min);
        double span = max > min ? max - min : 1;
        double Frac(double v) => Math.Clamp((v - min) / span, 0, 1);

        for (int c = 0; c < chart.Curves.Count; c++)
        {
            var curve = chart.Curves[c];
            var pts = new List<Point>(n);
            for (int i = 0; i < n; i++)
            {
                double v = (i < curve.Values.Count ? curve.Values[i] : null) ?? min;
                pts.Add(Polar(Frac(v) * outerR, i));
            }

            Brush stroke = CurveBrush(c);
            var geom = ClosedSpline(pts, Math.Clamp(cfg.CurveTension, 0, 1));
            canvas.Children.Add(new Path
            {
                Data = geom,
                Stroke = stroke,
                StrokeThickness = cfg.CurveStrokeWidth,
                StrokeLineJoin = PenLineJoin.Round,
                Fill = WithOpacity(stroke, cfg.CurveOpacity),
            });
        }

        // ── Legend ──────────────────────────────────────────────────────────
        if (hasLegend)
            DrawLegend(canvas, chart.Curves, legendRows, CurveBrush, legendBrush, cfg.LegendFontSize, cfg.LegendBoxSize,
                canvasW, canvasH - Outer - legendH + 6);

        return Frame(canvas, bg, palette.CodeBorder);
    }

    // ── Curve geometry ───────────────────────────────────────────────────────

    /// <summary>Builds a closed path through <paramref name="pts"/>; a cardinal spline rounded by
    /// <paramref name="tension"/> (0 = round, 1 = straight polygon). Falls back to straight segments
    /// for fewer than three points.</summary>
    private static Geometry ClosedSpline(IReadOnlyList<Point> pts, double tension)
    {
        int n = pts.Count;
        var fig = new PathFigure { StartPoint = pts[0], IsClosed = true, IsFilled = true };

        if (n < 3)
        {
            for (int i = 1; i < n; i++) fig.Segments.Add(new LineSegment(pts[i], true));
        }
        else
        {
            double k = (1 - tension) / 6;
            for (int i = 0; i < n; i++)
            {
                Point p0 = pts[(i - 1 + n) % n], p1 = pts[i], p2 = pts[(i + 1) % n], p3 = pts[(i + 2) % n];
                var cp1 = new Point(p1.X + (p2.X - p0.X) * k, p1.Y + (p2.Y - p0.Y) * k);
                var cp2 = new Point(p2.X - (p3.X - p1.X) * k, p2.Y - (p3.Y - p1.Y) * k);
                fig.Segments.Add(new BezierSegment(cp1, cp2, p2, true));
            }
        }

        var geo = new PathGeometry();
        geo.Figures.Add(fig);
        geo.Freeze();
        return geo;
    }

    private static double AutoMax(RadarChart chart, double min)
    {
        double m = min;
        foreach (var curve in chart.Curves)
            foreach (var v in curve.Values)
                if (v is double d && d > m) m = d;
        return m > min ? m : min + 1;
    }

    // ── Legend ───────────────────────────────────────────────────────────────

    private const double LegendGap = 5, LegendItemGap = 16;

    private static double ItemWidth(RadarCurve curve, double fontSize, double box) =>
        box + LegendGap + Measure(curve.Display, fontSize).Width;

    /// <summary>Greedily packs the curve legend into rows no wider than <paramref name="maxWidth"/>.</summary>
    private static List<List<int>> LayoutLegendRows(List<RadarCurve> curves, double fontSize, double box, double maxWidth)
    {
        var rows = new List<List<int>>();
        var row = new List<int>();
        double w = 0;
        for (int i = 0; i < curves.Count; i++)
        {
            double iw = ItemWidth(curves[i], fontSize, box);
            double add = (row.Count == 0 ? 0 : LegendItemGap) + iw;
            if (row.Count > 0 && w + add > maxWidth)
            {
                rows.Add(row);
                row = [];
                w = 0;
                add = iw;
            }
            row.Add(i);
            w += add;
        }
        if (row.Count > 0) rows.Add(row);
        return rows;
    }

    private static void DrawLegend(Canvas canvas, List<RadarCurve> curves, List<List<int>> rows, Func<int, Brush> brushOf,
        Brush textCol, double fontSize, double box, double canvasW, double top)
    {
        double lineH = fontSize + 6;
        for (int r = 0; r < rows.Count; r++)
        {
            var row = rows[r];
            double rowW = row.Sum(i => ItemWidth(curves[i], fontSize, box)) + (row.Count - 1) * LegendItemGap;
            double x = Math.Max(4, (canvasW - rowW) / 2);
            double y = top + r * lineH;

            foreach (int i in row)
            {
                var chip = new Rectangle { Width = box, Height = box, Fill = brushOf(i) };
                Canvas.SetLeft(chip, x);
                Canvas.SetTop(chip, y + (fontSize - box) / 2 + 2);
                canvas.Children.Add(chip);
                x += box + LegendGap;

                AddText(canvas, curves[i].Display, textCol, x, y, fontSize);
                x += Measure(curves[i].Display, fontSize).Width + LegendItemGap;
            }
        }
    }

    // ── Low-level helpers ─────────────────────────────────────────────────────

    private static Border Frame(Canvas canvas, Brush bg, Brush border) => new()
    {
        Background = bg, BorderBrush = border, BorderThickness = new Thickness(1),
        CornerRadius = new CornerRadius(6), Margin = new Thickness(0, 8, 0, 12), Child = canvas,
    };

    private static Brush WithOpacity(Brush b, double opacity)
    {
        Color c = (b as SolidColorBrush)?.Color ?? Colors.Gray;
        var brush = new SolidColorBrush(c) { Opacity = Math.Clamp(opacity, 0, 1) };
        brush.Freeze();
        return brush;
    }

    private static void AddText(Canvas canvas, string text, Brush brush, double left, double top, double fontSize, FontWeight? weight = null)
    {
        var tb = new TextBlock
        {
            Text = text, Foreground = brush, FontFamily = BodyFont, FontSize = fontSize,
            FontWeight = weight ?? FontWeights.Normal,
        };
        Canvas.SetLeft(tb, left);
        Canvas.SetTop(tb, top);
        canvas.Children.Add(tb);
    }

    private static Size Measure(string text, double fontSize)
    {
        var ft = new FormattedText(
            text, CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
            new Typeface(BodyFont, FontStyles.Normal, FontWeights.Normal, FontStretches.Normal),
            fontSize, Brushes.Black, 1.0);
        return new Size(ft.Width, ft.Height);
    }
}
