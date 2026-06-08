using Nexaflow.Visuals.Text.Markdown.Graphs.Charts;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;

namespace Nexaflow.Visuals.Text.Markdown.Graphs.Rendering;

/// <summary>
/// Renders a <see cref="QuadrantChart"/> as a WPF <see cref="FrameworkElement"/>, themed from a
/// <see cref="MarkdownPalette"/> (surface/text/border follow the active theme; the four quadrant
/// tints and the point dots come from the palette's <see cref="MarkdownPalette.Series"/> bank).
///
/// Layout: optional title above a square plot; axis labels along the bottom (x) and left (y);
/// quadrant captions centred in each cell; points as labelled dots.
/// </summary>
public static class WpfQuadrantChartRenderer
{
    private static readonly FontFamily BodyFont = new("Segoe UI");

    // ── Geometry constants ─────────────────────────────────────────────────

    private const double Outer     = 16;   // outer canvas margin
    private const double TitleH    = 28;
    private const double YAxisColW = 22;    // rotated y-axis label column on the left
    private const double XAxisRowH = 26;    // x-axis label row beneath the plot
    private const double PlotSize  = 380;
    private const double DotR      = 5;

    // ── Public API ─────────────────────────────────────────────────────────

    public static FrameworkElement Render(QuadrantChart chart, MarkdownPalette palette)
    {
        Brush bgBrush     = palette.CodeBg;
        Brush borderBrush = palette.CodeBorder;
        Brush titleBrush  = palette.Heading;
        Brush textBrush   = palette.Text;
        Brush mutedBrush  = palette.TextMuted;
        Brush gridBrush   = palette.CodeBorder;
        var series        = palette.Series;

        bool hasTitle  = !string.IsNullOrWhiteSpace(chart.Title);
        double plotLeft = Outer + YAxisColW;
        double plotTop  = Outer + (hasTitle ? TitleH : 0);
        double canvasW  = plotLeft + PlotSize + Outer;
        double canvasH  = plotTop + PlotSize + XAxisRowH + Outer;

        var canvas = new Canvas { Width = canvasW, Height = canvasH, Background = bgBrush };

        // Title
        if (hasTitle)
        {
            var tb = new TextBlock
            {
                Text       = chart.Title,
                Foreground = titleBrush,
                FontFamily = BodyFont,
                FontSize   = 15,
                FontWeight = FontWeights.SemiBold,
            };
            Canvas.SetLeft(tb, plotLeft);
            Canvas.SetTop(tb, Outer - 2);
            canvas.Children.Add(tb);
        }

        // Quadrant background tints (Mermaid numbering → screen cells)
        AddQuadrantTint(canvas, series, 1, plotLeft + PlotSize / 2, plotTop,                 PlotSize / 2); // top-right
        AddQuadrantTint(canvas, series, 0, plotLeft,                plotTop,                 PlotSize / 2); // top-left  (q2)
        AddQuadrantTint(canvas, series, 2, plotLeft,                plotTop + PlotSize / 2,  PlotSize / 2); // bottom-left (q3)
        AddQuadrantTint(canvas, series, 3, plotLeft + PlotSize / 2, plotTop + PlotSize / 2,  PlotSize / 2); // bottom-right (q4)

        // Plot border + centre dividers
        var plotBorder = new Rectangle
        {
            Width = PlotSize, Height = PlotSize,
            Stroke = gridBrush, StrokeThickness = 1, Fill = Brushes.Transparent,
        };
        Canvas.SetLeft(plotBorder, plotLeft);
        Canvas.SetTop(plotBorder, plotTop);
        canvas.Children.Add(plotBorder);

        AddLine(canvas, plotLeft + PlotSize / 2, plotTop, plotLeft + PlotSize / 2, plotTop + PlotSize, gridBrush);
        AddLine(canvas, plotLeft, plotTop + PlotSize / 2, plotLeft + PlotSize, plotTop + PlotSize / 2, gridBrush);

        // Quadrant captions (centred in each cell)
        AddQuadrantCaption(canvas, chart.Quadrant1, mutedBrush, plotLeft + PlotSize * 0.75, plotTop + PlotSize * 0.25);
        AddQuadrantCaption(canvas, chart.Quadrant2, mutedBrush, plotLeft + PlotSize * 0.25, plotTop + PlotSize * 0.25);
        AddQuadrantCaption(canvas, chart.Quadrant3, mutedBrush, plotLeft + PlotSize * 0.25, plotTop + PlotSize * 0.75);
        AddQuadrantCaption(canvas, chart.Quadrant4, mutedBrush, plotLeft + PlotSize * 0.75, plotTop + PlotSize * 0.75);

        // Points
        for (int i = 0; i < chart.Points.Count; i++)
        {
            var p   = chart.Points[i];
            var st  = p.Style;
            Brush fill = st?.FillColor is string fc ? ParseBrush(fc, series[i % series.Count]) : series[i % series.Count];
            double r   = st?.Radius is double rr ? Math.Clamp(rr, 1.5, 60) : DotR;
            double px = plotLeft + p.X * PlotSize;
            double py = plotTop + (1 - p.Y) * PlotSize;   // invert: y=1 is the top

            var dot = new Ellipse { Width = r * 2, Height = r * 2, Fill = fill };
            if (st?.StrokeColor is string sc)
            {
                dot.Stroke          = ParseBrush(sc, fill);
                dot.StrokeThickness = Math.Clamp(st.StrokeWidth ?? 1.5, 0.5, 12);
            }
            Canvas.SetLeft(dot, px - r);
            Canvas.SetTop(dot, py - r);
            canvas.Children.Add(dot);

            // Label — to the right of the dot, flipped left if it would overflow the plot.
            double labelW = MeasureText(p.Label, 11);
            bool flip = px + r + 4 + labelW > plotLeft + PlotSize;
            var lbl = new TextBlock
            {
                Text       = p.Label,
                Foreground = textBrush,
                FontFamily = BodyFont,
                FontSize   = 11,
            };
            Canvas.SetLeft(lbl, flip ? px - r - 4 - labelW : px + r + 4);
            Canvas.SetTop(lbl, py - 8);
            canvas.Children.Add(lbl);
        }

        // X-axis labels (low at left, high at right, beneath the plot)
        double xRowY = plotTop + PlotSize + 5;
        AddText(canvas, chart.XAxisLeft, mutedBrush, plotLeft, xRowY, false);
        if (!string.IsNullOrWhiteSpace(chart.XAxisRight))
        {
            double w = MeasureText(chart.XAxisRight, 11);
            AddText(canvas, chart.XAxisRight, mutedBrush, plotLeft + PlotSize - w, xRowY, false);
        }

        // Y-axis labels (rotated; low at bottom, high at top, left of the plot)
        AddRotatedText(canvas, chart.YAxisBottom, mutedBrush, Outer + 4, plotTop + PlotSize);
        if (!string.IsNullOrWhiteSpace(chart.YAxisTop))
        {
            double w = MeasureText(chart.YAxisTop, 11);
            AddRotatedText(canvas, chart.YAxisTop, mutedBrush, Outer + 4, plotTop + w);
        }

        return new Border
        {
            Background      = bgBrush,
            BorderBrush     = borderBrush,
            BorderThickness = new Thickness(1),
            CornerRadius    = new CornerRadius(6),
            Margin          = new Thickness(0, 8, 0, 12),
            Child           = canvas,
        };
    }

    // ── Helpers ─────────────────────────────────────────────────────────────

    private static void AddQuadrantTint(Canvas canvas, IReadOnlyList<Brush> series, int seriesIdx,
        double left, double top, double size)
    {
        Color c = (series[seriesIdx % series.Count] as SolidColorBrush)?.Color ?? Colors.Gray;
        var fill = new SolidColorBrush(Color.FromArgb(0x1E, c.R, c.G, c.B));
        fill.Freeze();

        var rect = new Rectangle { Width = size, Height = size, Fill = fill };
        Canvas.SetLeft(rect, left);
        Canvas.SetTop(rect, top);
        canvas.Children.Add(rect);
    }

    private static void AddQuadrantCaption(Canvas canvas, string text, Brush brush, double cx, double cy)
    {
        if (string.IsNullOrWhiteSpace(text)) return;
        double w = MeasureText(text, 12);
        var tb = new TextBlock
        {
            Text          = text,
            Foreground    = brush,
            FontFamily    = BodyFont,
            FontSize      = 12,
            FontWeight    = FontWeights.SemiBold,
            TextAlignment = TextAlignment.Center,
        };
        Canvas.SetLeft(tb, cx - w / 2);
        Canvas.SetTop(tb, cy - 9);
        canvas.Children.Add(tb);
    }

    private static void AddText(Canvas canvas, string text, Brush brush, double left, double top, bool semibold)
    {
        if (string.IsNullOrWhiteSpace(text)) return;
        var tb = new TextBlock
        {
            Text       = text,
            Foreground = brush,
            FontFamily = BodyFont,
            FontSize   = 11,
            FontWeight = semibold ? FontWeights.SemiBold : FontWeights.Normal,
        };
        Canvas.SetLeft(tb, left);
        Canvas.SetTop(tb, top);
        canvas.Children.Add(tb);
    }

    private static void AddRotatedText(Canvas canvas, string text, Brush brush, double left, double bottom)
    {
        if (string.IsNullOrWhiteSpace(text)) return;
        var tb = new TextBlock
        {
            Text         = text,
            Foreground   = brush,
            FontFamily   = BodyFont,
            FontSize     = 11,
            RenderTransform       = new RotateTransform(-90),
            RenderTransformOrigin = new Point(0, 0),
        };
        // Origin (0,0) rotates about the top-left; place so text reads upward ending near `bottom`.
        Canvas.SetLeft(tb, left);
        Canvas.SetTop(tb, bottom);
        canvas.Children.Add(tb);
    }

    private static void AddLine(Canvas canvas, double x1, double y1, double x2, double y2, Brush brush)
    {
        canvas.Children.Add(new Line
        {
            X1 = x1, Y1 = y1, X2 = x2, Y2 = y2,
            Stroke = brush, StrokeThickness = 1,
        });
    }

    private static double MeasureText(string text, double fontSize)
    {
        var ft = new FormattedText(
            text, CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
            new Typeface(BodyFont, FontStyles.Normal, FontWeights.Normal, FontStretches.Normal),
            fontSize, Brushes.Black, 1.0);
        return ft.Width;
    }

    /// <summary>Parses a CSS/hex colour to a frozen brush, falling back to <paramref name="fallback"/> on failure.</summary>
    private static Brush ParseBrush(string color, Brush fallback)
    {
        try
        {
            var c = (Color)ColorConverter.ConvertFromString(color)!;
            var b = new SolidColorBrush(c);
            b.Freeze();
            return b;
        }
        catch { return fallback; }
    }
}
