using Nexaflow.Visuals.Text.Markdown.Graphs.Charts;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;

namespace Nexaflow.Visuals.Text.Markdown.Graphs.Rendering;

/// <summary>
/// Renders a Mermaid <see cref="XyChart"/> as a WPF <see cref="FrameworkElement"/>, themed from a
/// <see cref="MarkdownPalette"/> and overridden by the chart's parsed <see cref="XyChartConfig"/>
/// (sizes, flags, axis options, and the <c>themeVariables</c> colours / <c>plotColorPalette</c>).
///
/// One category axis (the <c>x-axis</c>) and one value axis (the <c>y-axis</c>) are projected onto the
/// plot through a single orientation-aware mapping, so the vertical and horizontal layouts share all
/// the bar/line/axis drawing.  Bars (grouped per category when there are several), lines (with optional
/// per-point labels), data labels and a legend for named series are all drawn natively on a
/// <see cref="Canvas"/>.
/// </summary>
public static class WpfXyChartRenderer
{
    private static readonly FontFamily BodyFont = new("Segoe UI");
    private const double Outer = 12;          // canvas margin
    private const double PointLabelFontSize = 12;   // Mermaid fixes line point labels at 12px

    public static FrameworkElement Render(XyChart chart, MarkdownPalette palette)
    {
        var cfg = chart.Config;
        bool vertical = chart.Orientation == XyOrientation.Vertical;

        // ── Colours (config overrides the palette) ──────────────────────────
        Brush bg          = cfg.BackgroundColor ?? palette.CodeBg;
        Brush border      = palette.CodeBorder;
        Brush titleBrush  = cfg.TitleColor ?? palette.Heading;
        Brush dataLblCol  = cfg.DataLabelColor ?? palette.Text;
        Brush legendCol   = cfg.LegendTextColor ?? palette.Text;

        // The x-axis is always the category axis, the y-axis the value axis — orientation only
        // changes which screen edge each is drawn on, so their colours stay bound to x*/y* config.
        Brush catLabelCol = cfg.XAxisLabelColor ?? palette.TextMuted;
        Brush catTitleCol = cfg.XAxisTitleColor ?? palette.Text;
        Brush catTickCol  = cfg.XAxisTickColor  ?? palette.CodeBorder;
        Brush catLineCol  = cfg.XAxisLineColor  ?? palette.CodeBorder;
        Brush valLabelCol = cfg.YAxisLabelColor ?? palette.TextMuted;
        Brush valTitleCol = cfg.YAxisTitleColor ?? palette.Text;
        Brush valTickCol  = cfg.YAxisTickColor  ?? palette.CodeBorder;
        Brush valLineCol  = cfg.YAxisLineColor  ?? palette.CodeBorder;

        Brush SeriesBrush(int i) =>
            i < cfg.PlotPalette.Count ? cfg.PlotPalette[i] : palette.Series[i % palette.Series.Count];

        // ── Category slots + value range ────────────────────────────────────
        int n = chart.XAxis.IsCategorical
            ? chart.XAxis.Categories.Count
            : chart.Series.Count == 0 ? 0 : chart.Series.Max(s => s.Points.Count);
        n = Math.Max(n, 1);
        string CatLabel(int i) => i < chart.XAxis.Categories.Count ? chart.XAxis.Categories[i] : string.Empty;

        var (vmin, vmax) = ValueRange(chart, cfg);
        var ticks = NiceTicks(vmin, vmax);

        var catCfg = chart.XAxis;     // for titles
        var valCfg = chart.YAxis;
        var catAxisCfg = cfg.XAxis;   // for layout options
        var valAxisCfg = cfg.YAxis;

        bool hasTitle = cfg.ShowTitle && !string.IsNullOrWhiteSpace(chart.Title);
        var namedSeries = chart.Series.Select((s, i) => (s, i)).Where(t => !string.IsNullOrWhiteSpace(t.s.Name)).ToList();
        bool hasLegend = cfg.ShowLegend && namedSeries.Count > 0;

        // ── Region sizes ────────────────────────────────────────────────────
        double titleH = hasTitle ? cfg.TitleFontSize + cfg.TitlePadding * 2 : 0;
        double legendH = hasLegend ? cfg.LegendFontSize + cfg.LegendPadding * 2 : 0;

        // Left axis = value axis when vertical, category axis when horizontal; bottom is the other.
        var leftAxisCfg  = vertical ? valAxisCfg : catAxisCfg;
        var bottomAxisCfg = vertical ? catAxisCfg : valAxisCfg;
        string leftTitle   = vertical ? valCfg.Title : catCfg.Title;
        string bottomTitle = vertical ? catCfg.Title : valCfg.Title;

        IEnumerable<string> leftLabels   = vertical ? ticks.Select(Fmt) : Enumerable.Range(0, n).Select(CatLabel);
        IEnumerable<string> bottomLabels = vertical ? Enumerable.Range(0, n).Select(CatLabel) : ticks.Select(Fmt);

        double leftLabelW = leftAxisCfg.ShowLabel
            ? leftLabels.DefaultIfEmpty(string.Empty).Max(s => Measure(s, leftAxisCfg.LabelFontSize).Width) + leftAxisCfg.LabelPadding
            : 0;
        double leftColW = (leftAxisCfg.ShowTick ? leftAxisCfg.TickLength : 0)
                        + leftLabelW
                        + (leftAxisCfg.ShowTitle && leftTitle.Length > 0 ? leftAxisCfg.TitleFontSize + leftAxisCfg.TitlePadding : 0)
                        + 4;

        double bottomRot = vertical ? catAxisCfg.LabelRotation : 0;   // rotation applies to the bottom x-axis
        double bottomLabelH = bottomAxisCfg.ShowLabel
            ? LabelExtentH(bottomLabels, bottomAxisCfg.LabelFontSize, bottomRot) + bottomAxisCfg.LabelPadding
            : 0;
        double bottomRowH = (bottomAxisCfg.ShowTick ? bottomAxisCfg.TickLength : 0)
                          + bottomLabelH
                          + (bottomAxisCfg.ShowTitle && bottomTitle.Length > 0 ? bottomAxisCfg.TitleFontSize + bottomAxisCfg.TitlePadding : 0)
                          + 4;

        double canvasW = Math.Max(cfg.Width, 160);
        double canvasH = Math.Max(cfg.Height, 120);

        double plotLeft   = Outer + leftColW;
        double plotTop    = Outer + titleH;
        double plotRight  = canvasW - Outer;
        double plotBottom = canvasH - Outer - legendH - bottomRowH;
        double plotW = Math.Max(plotRight - plotLeft, 20);
        double plotH = Math.Max(plotBottom - plotTop, 20);
        plotRight  = plotLeft + plotW;
        plotBottom = plotTop + plotH;

        var canvas = new Canvas { Width = canvasW, Height = canvasH, Background = bg };

        // ── Projection ──────────────────────────────────────────────────────
        double catOrigin = vertical ? plotLeft : plotTop;
        double catLen    = vertical ? plotW : plotH;
        double slot      = catLen / n;

        double VFrac(double v) => vmax <= vmin ? 0 : Math.Clamp((v - vmin) / (vmax - vmin), 0, 1);
        // Value-axis screen coordinate (y when vertical, x when horizontal).
        double ValPos(double v) => vertical ? plotBottom - VFrac(v) * plotH : plotLeft + VFrac(v) * plotW;
        double CatCenter(double i) => catOrigin + (i + 0.5) * slot;
        Point At(double catCoord, double valCoord) => vertical ? new Point(catCoord, valCoord) : new Point(valCoord, catCoord);

        // ── Title ───────────────────────────────────────────────────────────
        if (hasTitle)
            AddText(canvas, chart.Title, titleBrush, plotLeft, Outer + cfg.TitlePadding, cfg.TitleFontSize, FontWeights.SemiBold);

        // ── Plot border ─────────────────────────────────────────────────────
        var frame = new Rectangle { Width = plotW, Height = plotH, Stroke = palette.CodeBorder, StrokeThickness = 1, Fill = Brushes.Transparent };
        Canvas.SetLeft(frame, plotLeft);
        Canvas.SetTop(frame, plotTop);
        canvas.Children.Add(frame);

        // ── Axes ────────────────────────────────────────────────────────────
        DrawValueAxis(canvas, vertical, ticks, ValPos, plotLeft, plotTop, plotRight, plotBottom,
            valAxisCfg, valCfg.Title, valLabelCol, valTitleCol, valTickCol, valLineCol);
        DrawCategoryAxis(canvas, vertical, n, CatLabel, CatCenter, plotLeft, plotTop, plotRight, plotBottom,
            catAxisCfg, catCfg.Title, catLabelCol, catTitleCol, catTickCol, catLineCol, bottomRot);

        // ── Series ──────────────────────────────────────────────────────────
        double baseVal = Math.Clamp(0, vmin, vmax);
        double basePos = ValPos(baseVal);

        var barSeries = chart.Series.Where(s => s.Kind == XySeriesKind.Bar).ToList();
        int barCount = barSeries.Count;
        double subSlot = slot / Math.Max(barCount, 1);
        double barW = subSlot * 0.72;

        // Bars first (so lines sit on top).
        for (int bi = 0; bi < barSeries.Count; bi++)
        {
            var s = barSeries[bi];
            Brush fill = SeriesBrush(chart.Series.IndexOf(s));
            for (int i = 0; i < s.Points.Count && i < n; i++)
            {
                double v = s.Points[i].Value;
                double catC = catOrigin + i * slot + (bi + 0.5) * subSlot;
                double valC = ValPos(v);
                DrawBar(canvas, vertical, catC, barW, basePos, valC, fill);

                if (cfg.ShowDataLabel)
                    DrawDataLabel(canvas, vertical, catC, valC, basePos, Fmt(v), dataLblCol, cfg.ShowDataLabelOutsideBar, v >= baseVal);
            }
        }

        // Lines on top.
        foreach (var s in chart.Series.Where(s => s.Kind == XySeriesKind.Line))
        {
            Brush stroke = SeriesBrush(chart.Series.IndexOf(s));
            var pts = new PointCollection();
            for (int i = 0; i < s.Points.Count && i < n; i++)
                pts.Add(At(CatCenter(i), ValPos(s.Points[i].Value)));

            if (pts.Count >= 2)
                canvas.Children.Add(new Polyline { Points = pts, Stroke = stroke, StrokeThickness = 2,
                    StrokeLineJoin = PenLineJoin.Round });

            for (int i = 0; i < s.Points.Count && i < n; i++)
            {
                var p = At(CatCenter(i), ValPos(s.Points[i].Value));
                var dot = new Ellipse { Width = 6, Height = 6, Fill = stroke };
                Canvas.SetLeft(dot, p.X - 3);
                Canvas.SetTop(dot, p.Y - 3);
                canvas.Children.Add(dot);

                if (!string.IsNullOrWhiteSpace(s.Points[i].Label))
                    DrawPointLabel(canvas, vertical, p, s.Points[i].Label!, stroke);
            }
        }

        // ── Legend ──────────────────────────────────────────────────────────
        if (hasLegend)
            DrawLegend(canvas, namedSeries, SeriesBrush, legendCol, cfg.LegendFontSize,
                plotLeft, plotRight, canvasH - Outer - legendH + cfg.LegendPadding);

        return new Border
        {
            Background      = bg,
            BorderBrush     = border,
            BorderThickness = new Thickness(1),
            CornerRadius    = new CornerRadius(6),
            Margin          = new Thickness(0, 8, 0, 12),
            Child           = canvas,
        };
    }

    // ── Axis drawing ───────────────────────────────────────────────────────

    private static void DrawValueAxis(Canvas canvas, bool vertical, IReadOnlyList<double> ticks, Func<double, double> valPos,
        double plotLeft, double plotTop, double plotRight, double plotBottom,
        XyAxisConfig cfg, string title, Brush label, Brush titleCol, Brush tick, Brush line)
    {
        if (vertical)
        {
            if (cfg.ShowAxisLine) AddLine(canvas, plotLeft, plotTop, plotLeft, plotBottom, line, cfg.AxisLineWidth);
            foreach (var t in ticks)
            {
                double y = valPos(t);
                if (cfg.ShowTick) AddLine(canvas, plotLeft - cfg.TickLength, y, plotLeft, y, tick, cfg.TickWidth);
                if (cfg.ShowLabel)
                {
                    var sz = Measure(Fmt(t), cfg.LabelFontSize);
                    AddText(canvas, Fmt(t), label, plotLeft - cfg.TickLength - cfg.LabelPadding - sz.Width, y - sz.Height / 2, cfg.LabelFontSize);
                }
            }
            if (cfg.ShowTitle && title.Length > 0)
            {
                var tsz = Measure(title, cfg.TitleFontSize);
                AddRotatedText(canvas, title, titleCol, Outer, (plotTop + plotBottom) / 2 + tsz.Width / 2, cfg.TitleFontSize);
            }
        }
        else
        {
            if (cfg.ShowAxisLine) AddLine(canvas, plotLeft, plotBottom, plotRight, plotBottom, line, cfg.AxisLineWidth);
            foreach (var t in ticks)
            {
                double x = valPos(t);
                if (cfg.ShowTick) AddLine(canvas, x, plotBottom, x, plotBottom + cfg.TickLength, tick, cfg.TickWidth);
                if (cfg.ShowLabel)
                {
                    var sz = Measure(Fmt(t), cfg.LabelFontSize);
                    AddText(canvas, Fmt(t), label, x - sz.Width / 2, plotBottom + cfg.TickLength + cfg.LabelPadding, cfg.LabelFontSize);
                }
            }
            if (cfg.ShowTitle && title.Length > 0)
            {
                var sz = Measure(title, cfg.TitleFontSize);
                AddText(canvas, title, titleCol, (plotLeft + plotRight) / 2 - sz.Width / 2, canvas.Height - Outer - cfg.TitleFontSize, cfg.TitleFontSize);
            }
        }
    }

    private static void DrawCategoryAxis(Canvas canvas, bool vertical, int n, Func<int, string> labelOf, Func<double, double> catCenter,
        double plotLeft, double plotTop, double plotRight, double plotBottom,
        XyAxisConfig cfg, string title, Brush label, Brush titleCol, Brush tick, Brush line, double rotation)
    {
        if (vertical)
        {
            if (cfg.ShowAxisLine) AddLine(canvas, plotLeft, plotBottom, plotRight, plotBottom, line, cfg.AxisLineWidth);
            for (int i = 0; i < n; i++)
            {
                double x = catCenter(i);
                if (cfg.ShowTick) AddLine(canvas, x, plotBottom, x, plotBottom + cfg.TickLength, tick, cfg.TickWidth);
                string text = labelOf(i);
                if (cfg.ShowLabel && text.Length > 0)
                {
                    var sz = Measure(text, cfg.LabelFontSize);
                    double y = plotBottom + cfg.TickLength + cfg.LabelPadding;
                    if (Math.Abs(rotation) > 0.5)
                        AddRotatedText(canvas, text, label, x, y + sz.Width, cfg.LabelFontSize, -rotation);
                    else
                        AddText(canvas, text, label, x - sz.Width / 2, y, cfg.LabelFontSize);
                }
            }
            if (cfg.ShowTitle && title.Length > 0)
            {
                var sz = Measure(title, cfg.TitleFontSize);
                AddText(canvas, title, titleCol, (plotLeft + plotRight) / 2 - sz.Width / 2, canvas.Height - Outer - cfg.TitleFontSize, cfg.TitleFontSize);
            }
        }
        else
        {
            if (cfg.ShowAxisLine) AddLine(canvas, plotLeft, plotTop, plotLeft, plotBottom, line, cfg.AxisLineWidth);
            for (int i = 0; i < n; i++)
            {
                double y = catCenter(i);
                if (cfg.ShowTick) AddLine(canvas, plotLeft - cfg.TickLength, y, plotLeft, y, tick, cfg.TickWidth);
                string text = labelOf(i);
                if (cfg.ShowLabel && text.Length > 0)
                {
                    var sz = Measure(text, cfg.LabelFontSize);
                    AddText(canvas, text, label, plotLeft - cfg.TickLength - cfg.LabelPadding - sz.Width, y - sz.Height / 2, cfg.LabelFontSize);
                }
            }
            if (cfg.ShowTitle && title.Length > 0)
            {
                var tsz = Measure(title, cfg.TitleFontSize);
                AddRotatedText(canvas, title, titleCol, Outer, (plotTop + plotBottom) / 2 + tsz.Width / 2, cfg.TitleFontSize);
            }
        }
    }

    // ── Glyph drawing ──────────────────────────────────────────────────────

    private static void DrawBar(Canvas canvas, bool vertical, double catCenter, double barW, double basePos, double valPos, Brush fill)
    {
        var rect = new Rectangle { Fill = fill };
        if (vertical)
        {
            rect.Width  = barW;
            rect.Height = Math.Abs(valPos - basePos);
            Canvas.SetLeft(rect, catCenter - barW / 2);
            Canvas.SetTop(rect, Math.Min(valPos, basePos));
        }
        else
        {
            rect.Height = barW;
            rect.Width  = Math.Abs(valPos - basePos);
            Canvas.SetLeft(rect, Math.Min(valPos, basePos));
            Canvas.SetTop(rect, catCenter - barW / 2);
        }
        canvas.Children.Add(rect);
    }

    private static void DrawDataLabel(Canvas canvas, bool vertical, double catCenter, double valPos, double basePos,
        string text, Brush col, bool outside, bool positive)
    {
        var sz = Measure(text, 11);
        double x, y;
        if (vertical)
        {
            x = catCenter - sz.Width / 2;
            y = outside ? (positive ? valPos - sz.Height - 2 : valPos + 2)
                        : (positive ? valPos + 2 : valPos - sz.Height - 2);
        }
        else
        {
            y = catCenter - sz.Height / 2;
            x = outside ? (positive ? valPos + 2 : valPos - sz.Width - 2)
                        : (positive ? valPos - sz.Width - 2 : valPos + 2);
        }
        AddText(canvas, text, col, x, y, 11);
    }

    private static void DrawPointLabel(Canvas canvas, bool vertical, Point at, string text, Brush col)
    {
        var sz = Measure(text, PointLabelFontSize);
        double x = vertical ? at.X - sz.Width / 2 : at.X + 6;
        double y = vertical ? at.Y - sz.Height - 4 : at.Y - sz.Height / 2;
        AddText(canvas, text, col, x, y, PointLabelFontSize);
    }

    private static void DrawLegend(Canvas canvas, List<(XySeries s, int i)> named, Func<int, Brush> brushOf,
        Brush textCol, double fontSize, double plotLeft, double plotRight, double top)
    {
        const double sw = 12, gap = 6, itemGap = 16;
        double totalW = named.Sum(t => sw + gap + Measure(t.s.Name!, fontSize).Width + itemGap) - itemGap;
        double x = Math.Max(plotLeft, (plotLeft + plotRight) / 2 - totalW / 2);

        foreach (var (s, idx) in named)
        {
            var chip = new Rectangle { Width = sw, Height = sw, Fill = brushOf(idx) };
            Canvas.SetLeft(chip, x);
            Canvas.SetTop(chip, top + 1);
            canvas.Children.Add(chip);
            x += sw + gap;

            var sz = Measure(s.Name!, fontSize);
            AddText(canvas, s.Name!, textCol, x, top + (sw - sz.Height) / 2, fontSize);
            x += sz.Width + itemGap;
        }
    }

    // ── Value range + ticks ──────────────────────────────────────────────────

    private static (double min, double max) ValueRange(XyChart chart, XyChartConfig cfg)
    {
        if (chart.YAxis.HasRange) return (chart.YAxis.Min!.Value, chart.YAxis.Max!.Value);

        var values = chart.Series.SelectMany(s => s.Points.Select(p => p.Value)).ToList();
        if (values.Count == 0) return (0, 1);

        double dmin = values.Min(), dmax = values.Max();
        if (chart.Series.Any(s => s.Kind == XySeriesKind.Bar)) dmin = Math.Min(dmin, 0);
        if (dmax <= dmin) dmax = dmin + 1;

        // Honour plotReservedSpacePercent loosely: leave headroom above the data.
        double headroom = (dmax - dmin) * Math.Clamp(1 - cfg.PlotReservedSpacePercent / 100.0, 0.02, 0.5);
        return (dmin, dmax + headroom);
    }

    private static List<double> NiceTicks(double min, double max)
    {
        var ticks = new List<double>();
        if (max <= min) { ticks.Add(min); return ticks; }

        double step = NiceStep((max - min) / 5);
        double start = Math.Ceiling(min / step) * step;
        for (double t = start; t <= max + step * 1e-6 && ticks.Count < 24; t += step)
            ticks.Add(Math.Abs(t) < step * 1e-6 ? 0 : t);
        if (ticks.Count == 0) { ticks.Add(min); ticks.Add(max); }
        return ticks;
    }

    private static double NiceStep(double raw)
    {
        if (raw <= 0) return 1;
        double mag  = Math.Pow(10, Math.Floor(Math.Log10(raw)));
        double norm = raw / mag;
        double nice = norm < 1.5 ? 1 : norm < 3 ? 2 : norm < 7 ? 5 : 10;
        return nice * mag;
    }

    private static string Fmt(double v) =>
        Math.Abs(v - Math.Round(v)) < 1e-9
            ? ((long)Math.Round(v)).ToString(CultureInfo.InvariantCulture)
            : v.ToString("0.##", CultureInfo.InvariantCulture);

    // ── Low-level helpers ─────────────────────────────────────────────────────

    private static double LabelExtentH(IEnumerable<string> labels, double fontSize, double rotationDeg)
    {
        var list = labels.Where(s => s.Length > 0).ToList();
        if (list.Count == 0) return fontSize;
        if (Math.Abs(rotationDeg) < 0.5) return Measure(list[0], fontSize).Height;
        // Rotated labels stand roughly vertical — reserve their (capped) width.
        double maxW = list.Max(s => Measure(s, fontSize).Width);
        double rad = Math.Abs(rotationDeg) * Math.PI / 180;
        return Math.Min(maxW, 80) * Math.Sin(rad) + fontSize * Math.Cos(rad);
    }

    private static void AddText(Canvas canvas, string text, Brush brush, double left, double top, double fontSize,
        FontWeight? weight = null)
    {
        var tb = new TextBlock
        {
            Text       = text,
            Foreground = brush,
            FontFamily = BodyFont,
            FontSize   = fontSize,
            FontWeight = weight ?? FontWeights.Normal,
        };
        Canvas.SetLeft(tb, left);
        Canvas.SetTop(tb, top);
        canvas.Children.Add(tb);
    }

    private static void AddRotatedText(Canvas canvas, string text, Brush brush, double left, double bottom, double fontSize, double angle = -90)
    {
        var tb = new TextBlock
        {
            Text                  = text,
            Foreground            = brush,
            FontFamily            = BodyFont,
            FontSize              = fontSize,
            RenderTransform       = new RotateTransform(angle),
            RenderTransformOrigin = new Point(0, 0),
        };
        Canvas.SetLeft(tb, left);
        Canvas.SetTop(tb, bottom);
        canvas.Children.Add(tb);
    }

    private static void AddLine(Canvas canvas, double x1, double y1, double x2, double y2, Brush brush, double thickness)
    {
        canvas.Children.Add(new Line { X1 = x1, Y1 = y1, X2 = x2, Y2 = y2, Stroke = brush, StrokeThickness = thickness });
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
