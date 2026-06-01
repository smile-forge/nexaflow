using Nexaflow.Visuals.Text.Markdown.Graphs.Charts;
using System.Collections.Generic;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;

namespace Nexaflow.Visuals.Text.Markdown.Graphs.Rendering;

/// <summary>
/// Renders a <see cref="PieChart"/> as a WPF <see cref="FrameworkElement"/>, themed from a
/// <see cref="MarkdownPalette"/> (surface, text and border follow the active theme; slice colours come
/// from the palette's <see cref="MarkdownPalette.Series"/> mini-palette).
///
/// Layout: pie on the left, legend on the right, optional title above.
/// </summary>
public static class WpfPieChartRenderer
{
    private static readonly FontFamily BodyFont = new("Segoe UI");

    // ── Geometry constants ─────────────────────────────────────────────────

    private const double TitleH   = 28;
    private const double PieR     = 130;
    private const double PieCX    = 28 + PieR;          // left margin + radius
    private const double LegendX  = PieCX + PieR + 36;  // start of legend column
    private const double LegendSw = 14;                  // legend swatch size
    private const double LegendGap = 6;                  // gap between swatch and text
    private const double RowH     = 22;                  // legend row height
    private const double MarginY  = 20;

    // ── Public API ─────────────────────────────────────────────────────────

    public static FrameworkElement Render(PieChart chart, MarkdownPalette palette)
    {
        IReadOnlyList<Brush> series = palette.Series;
        Brush bgBrush     = palette.CodeBg;
        Brush titleBrush  = palette.Heading;
        Brush textBrush   = palette.Text;
        Brush mutedBrush  = palette.TextMuted;
        Brush borderBrush = palette.CodeBorder;
        Brush Slice(int i) => series[i % series.Count];

        double total = chart.Total;
        if (total <= 0)
            return new TextBlock { Text = "(empty pie chart)", Foreground = mutedBrush, FontSize = 12 };

        int n          = chart.Slices.Count;
        double pieCY   = MarginY + (string.IsNullOrEmpty(chart.Title) ? 0 : TitleH) + PieR;
        double canvasH = pieCY + PieR + MarginY;
        double legendH = n * RowH + MarginY;
        double canvasW = LegendX + 220;      // legend column width ≤ 220
        canvasH        = Math.Max(canvasH, legendH + (string.IsNullOrEmpty(chart.Title) ? MarginY : MarginY + TitleH));

        var canvas = new Canvas { Width = canvasW, Height = canvasH, Background = bgBrush };

        // Title
        if (!string.IsNullOrWhiteSpace(chart.Title))
        {
            var tb = new TextBlock
            {
                Text       = chart.Title,
                Foreground = titleBrush,
                FontFamily = BodyFont,
                FontSize   = 15,
                FontWeight = FontWeights.SemiBold,
            };
            Canvas.SetLeft(tb, PieCX - PieR);
            Canvas.SetTop(tb, MarginY - 4);
            canvas.Children.Add(tb);
        }

        // Pie slices
        double angleStart = -Math.PI / 2; // start at 12 o'clock
        for (int i = 0; i < n; i++)
        {
            var slice      = chart.Slices[i];
            double fraction = slice.Value / total;
            double sweep   = fraction * 2 * Math.PI;
            double angleEnd = angleStart + sweep;

            var path = BuildSlice(PieCX, pieCY, PieR, angleStart, angleEnd, Slice(i), bgBrush);
            canvas.Children.Add(path);

            // Percentage label inside slice (only if slice is wide enough)
            if (fraction >= 0.05)
            {
                double midAngle = angleStart + sweep / 2;
                double labelR   = PieR * 0.62;
                double lx       = PieCX + labelR * Math.Cos(midAngle);
                double ly       = pieCY + labelR * Math.Sin(midAngle);

                string pct  = $"{fraction * 100:0.#}%";
                var lbl = new TextBlock
                {
                    Text       = chart.ShowData ? $"{slice.Value:0.##}\n{pct}" : pct,
                    Foreground = Brushes.White,   // saturated slice colours read white labels best
                    FontFamily = BodyFont,
                    FontSize   = 10.5,
                    FontWeight = fraction >= 0.12 ? FontWeights.SemiBold : FontWeights.Normal,
                    TextAlignment = TextAlignment.Center,
                };
                // Measure to centre properly
                var ft = MeasureText(pct, 10.5);
                Canvas.SetLeft(lbl, lx - ft / 2);
                Canvas.SetTop(lbl, ly - 7);
                canvas.Children.Add(lbl);
            }

            angleStart = angleEnd;
        }

        // Legend
        double legendTopY = (string.IsNullOrEmpty(chart.Title) ? MarginY : MarginY + TitleH)
                          + (canvasH - (string.IsNullOrEmpty(chart.Title) ? MarginY : MarginY + TitleH) - n * RowH) / 2;
        legendTopY = Math.Max(legendTopY, MarginY);

        for (int i = 0; i < n; i++)
        {
            var slice    = chart.Slices[i];
            double rowY  = legendTopY + i * RowH;
            double pct   = slice.Value / total * 100;

            // Colour swatch
            var swatch = new Rectangle
            {
                Width  = LegendSw,
                Height = LegendSw,
                Fill   = Slice(i),
                RadiusX = 2,
                RadiusY = 2,
            };
            Canvas.SetLeft(swatch, LegendX);
            Canvas.SetTop(swatch, rowY + (RowH - LegendSw) / 2);
            canvas.Children.Add(swatch);

            // Label + value
            string legendText = chart.ShowData
                ? $"{slice.Label} ({slice.Value:0.##} — {pct:0.#}%)"
                : $"{slice.Label} — {pct:0.#}%";

            var tb = new TextBlock
            {
                Text         = legendText,
                Foreground   = textBrush,
                FontFamily   = BodyFont,
                FontSize     = 11.5,
                MaxWidth     = 200,
                TextWrapping = TextWrapping.NoWrap,
                TextTrimming = TextTrimming.CharacterEllipsis,
                ToolTip      = legendText,
            };
            Canvas.SetLeft(tb, LegendX + LegendSw + LegendGap);
            Canvas.SetTop(tb, rowY + (RowH - 14) / 2);
            canvas.Children.Add(tb);
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

    // ── Slice geometry ─────────────────────────────────────────────────────

    private static Path BuildSlice(double cx, double cy, double r,
        double startAngle, double endAngle, Brush fill, Brush gapStroke)
    {
        // Inset by 1px for a thin gap between slices
        const double gap = 0.012; // radians
        double a0 = startAngle + gap;
        double a1 = endAngle   - gap;
        if (a1 <= a0) { a0 = startAngle; a1 = endAngle; }

        var center = new Point(cx, cy);
        var pStart = new Point(cx + r * Math.Cos(a0), cy + r * Math.Sin(a0));
        var pEnd   = new Point(cx + r * Math.Cos(a1), cy + r * Math.Sin(a1));

        bool isLarge = (endAngle - startAngle) > Math.PI;

        var figure = new PathFigure { StartPoint = center, IsClosed = true };
        figure.Segments.Add(new LineSegment(pStart, isStroked: false));
        figure.Segments.Add(new ArcSegment(
            pEnd, new Size(r, r), 0,
            isLargeArc: isLarge,
            sweepDirection: SweepDirection.Clockwise,
            isStroked: true));

        var geo = new PathGeometry([figure]);
        return new Path { Data = geo, Fill = fill, Stroke = gapStroke, StrokeThickness = 1 };
    }

    // ── Text measurement ───────────────────────────────────────────────────

    private static double MeasureText(string text, double fontSize)
    {
        var ft = new FormattedText(
            text, CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
            new Typeface(BodyFont, FontStyles.Normal, FontWeights.Normal, FontStretches.Normal),
            fontSize, Brushes.Black, 1.0);
        return ft.Width;
    }
}
