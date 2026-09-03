using Nexaflow.Visuals.Text.Markdown.Graphs.Charts;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;

namespace Nexaflow.Visuals.Text.Markdown.Graphs.Rendering;

/// <summary>
/// Renders a <see cref="GanttChart"/> as a WPF <see cref="FrameworkElement"/>, themed from a
/// <see cref="MarkdownPalette"/>.  Tasks become horizontal bars on a shared date axis, grouped
/// into tinted section bands; milestones render as diamonds and a "today" marker is drawn when
/// the current date falls inside the chart's range.
/// </summary>
public static class WpfGanttRenderer
{
    private static readonly FontFamily BodyFont = new("Segoe UI");

    private const double Outer      = 16;
    private const double TitleH     = 28;
    private const double AxisH      = 24;
    private const double RowH       = 26;
    private const double BarH       = 16;
    private const double Gutter     = 96;
    private const double SectionGap = 4;
    private const double MsR        = 8;     // milestone half-size

    public static FrameworkElement Render(GanttChart chart, MarkdownPalette palette)
    {
        Brush bgBrush     = palette.CodeBg;
        Brush borderBrush = palette.CodeBorder;
        Brush titleBrush  = palette.Heading;
        Brush textBrush   = palette.Text;
        Brush mutedBrush  = palette.TextMuted;
        Brush gridBrush   = palette.CodeBorder;
        var   series      = palette.Series;

        Color accentC = (palette.Accent    as SolidColorBrush)?.Color ?? Colors.SteelBlue;
        Color mutedC  = (palette.TextMuted as SolidColorBrush)?.Color ?? Colors.Gray;
        Color critC   = (series.Count > 1 ? series[1] as SolidColorBrush : null)?.Color ?? Color.FromRgb(0xFF, 0x6B, 0x6B);
        Color msC     = (series.Count > 4 ? series[4] as SolidColorBrush : palette.Accent as SolidColorBrush)?.Color ?? accentC;

        if (chart.TaskCount == 0)
            return new TextBlock { Text = "(empty gantt chart)", Foreground = mutedBrush, FontSize = 12 };

        bool hasTitle = !string.IsNullOrWhiteSpace(chart.Title);

        // ── Date range → x mapping (with a little padding around the data) ──
        DateTime min = chart.Min, max = chart.Max;
        double dataDays = Math.Max(0.5, (max - min).TotalDays);
        var pad = TimeSpan.FromDays(Math.Max(0.5, dataDays * 0.04));
        DateTime axMin = min - pad, axMax = max + pad;
        double totalDays = (axMax - axMin).TotalDays;

        double chartW   = Math.Clamp(dataDays * 14, 380, 1000);
        double chartLeft = Outer + Gutter;
        double X(DateTime d) => chartLeft + (d - axMin).TotalDays / totalDays * chartW;

        // ── Vertical layout: section bands + task rows ──
        double titleBottom = Outer + (hasTitle ? TitleH : 0);
        double rowsTop = titleBottom + AxisH;
        double y = rowsTop;

        var rows  = new List<(GanttTask task, double cy)>();
        var bands = new List<(string name, double top, double bottom, int index)>();
        for (int si = 0; si < chart.Sections.Count; si++)
        {
            var sec = chart.Sections[si];
            if (sec.Tasks.Count == 0) continue;
            double top = y;
            foreach (var t in sec.Tasks) { rows.Add((t, y + RowH / 2)); y += RowH; }
            bands.Add((sec.Name, top, y, si));
            y += SectionGap;
        }
        double rowsBottom = y;

        // ── Canvas sizing (extend for task labels that sit right of their bars) ──
        double maxRight = chartLeft + chartW;
        foreach (var (task, cy) in rows)
        {
            double bx = task.IsMilestone ? X(task.Start) + MsR : X(task.End);
            maxRight = Math.Max(maxRight, bx + 8 + MeasureText(task.Name, 11));
        }
        double canvasW = maxRight + Outer;
        double canvasH = rowsBottom + Outer;
        var canvas = new Canvas { Width = canvasW, Height = canvasH, Background = bgBrush };

        // 1. section bands
        foreach (var (name, top, bottom, index) in bands)
        {
            var c = (series[index % series.Count] as SolidColorBrush)?.Color ?? accentC;
            canvas.Children.Add(new Rectangle { Width = canvasW - 2 * Outer, Height = bottom - top, Fill = Tint(c, 0x14) }.At(Outer, top));
        }

        // 2. axis gridlines + date labels
        foreach (var tick in Ticks(min, max, dataDays, chartW))
        {
            double tx = X(tick);
            canvas.Children.Add(new Line { X1 = tx, Y1 = rowsTop, X2 = tx, Y2 = rowsBottom, Stroke = gridBrush, StrokeThickness = 1, StrokeDashArray = new DoubleCollection([2, 4]) });
            string label = tick.ToString(AxisFormat(chart.AxisFormat), CultureInfo.InvariantCulture);
            double lw = MeasureText(label, 10);
            canvas.Children.Add(new TextBlock { Text = label, Foreground = mutedBrush, FontFamily = BodyFont, FontSize = 10 }.At(tx - lw / 2, titleBottom + 4));
        }

        // 3. today marker
        var today = DateTime.Today;
        if (today >= min && today <= max)
        {
            double tx = X(today);
            canvas.Children.Add(new Line { X1 = tx, Y1 = rowsTop, X2 = tx, Y2 = rowsBottom, Stroke = Solid(critC), StrokeThickness = 1.4, StrokeDashArray = new DoubleCollection([3, 2]) });
        }

        // 4. bars + milestones + labels
        foreach (var (task, cy) in rows)
        {
            if (task.IsMilestone) DrawMilestone(canvas, task, X(task.Start), cy, msC, textBrush);
            else                  DrawBar(canvas, task, X(task.Start), X(task.End), cy, accentC, mutedC, critC, textBrush);
        }

        // 5. section names (left gutter)
        foreach (var (name, top, bottom, _) in bands)
        {
            if (string.IsNullOrWhiteSpace(name)) continue;
            var tb = new TextBlock { Text = name, Foreground = textBrush, FontFamily = BodyFont, FontSize = 11, FontWeight = FontWeights.SemiBold, MaxWidth = Gutter - 6, TextTrimming = TextTrimming.CharacterEllipsis };
            tb.Measure(new Size(Gutter, RowH));
            canvas.Children.Add(tb.At(Outer + 2, (top + bottom) / 2 - tb.DesiredSize.Height / 2));
        }

        // 6. title
        if (hasTitle)
        {
            double tw = MeasureText(chart.Title, 15);
            canvas.Children.Add(new TextBlock { Text = chart.Title, Foreground = titleBrush, FontFamily = BodyFont, FontSize = 15, FontWeight = FontWeights.SemiBold }.At((canvasW - tw) / 2, Outer - 2));
        }

        return new Border
        {
            Background = bgBrush, BorderBrush = borderBrush, BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(6), Margin = new Thickness(0, 8, 0, 12),
            Child = new ScrollViewer
            {
                Content = canvas,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
                VerticalScrollBarVisibility   = ScrollBarVisibility.Auto,
                MaxHeight = 600,
            },
        };
    }

    // ── Bars ───────────────────────────────────────────────────────────────

    private static void DrawBar(Canvas canvas, GanttTask task, double x0, double x1, double cy,
        Color accentC, Color mutedC, Color critC, Brush textBrush)
    {
        Color c = task.Critical ? critC : task.State == GanttTaskState.Done ? mutedC : accentC;
        byte fillA = task.State == GanttTaskState.Done ? (byte)0x66
                   : task.Critical                     ? (byte)0xB0
                   : task.State == GanttTaskState.Active ? (byte)0xDD
                   :                                       (byte)0x99;
        double w = Math.Max(3, x1 - x0);
        double top = cy - BarH / 2;

        bool emphasised = task.State == GanttTaskState.Active || task.Critical;
        canvas.Children.Add(new Rectangle
        {
            Width = w, Height = BarH, RadiusX = 4, RadiusY = 4,
            Fill = Tint(c, fillA), Stroke = Solid(c), StrokeThickness = emphasised ? 1.8 : 1,
        }.At(x0, top));

        // Label: inside the bar when it fits, otherwise to the right.
        double nameW = MeasureText(task.Name, 11);
        if (w >= nameW + 14)
            canvas.Children.Add(new TextBlock { Text = task.Name, Foreground = OnColor(c, fillA), FontFamily = BodyFont, FontSize = 11 }.At(x0 + 6, cy - 8));
        else
            canvas.Children.Add(new TextBlock { Text = task.Name, Foreground = textBrush, FontFamily = BodyFont, FontSize = 11 }.At(x1 + 6, cy - 8));
    }

    private static void DrawMilestone(Canvas canvas, GanttTask task, double x, double cy, Color msC, Brush textBrush)
    {
        canvas.Children.Add(new Polygon
        {
            Points = new PointCollection([new(x, cy - MsR), new(x + MsR, cy), new(x, cy + MsR), new(x - MsR, cy)]),
            Fill = Solid(msC), Stroke = Solid(msC), StrokeThickness = 1,
        });
        canvas.Children.Add(new TextBlock { Text = task.Name, Foreground = textBrush, FontFamily = BodyFont, FontSize = 11 }.At(x + MsR + 6, cy - 8));
    }

    // ── Axis ticks & formats ─────────────────────────────────────────────────

    private static readonly double[] NiceSteps = [1, 2, 3, 7, 14, 30, 60, 90, 180, 365];

    private static IEnumerable<DateTime> Ticks(DateTime min, DateTime max, double dataDays, double chartW)
    {
        double target = Math.Clamp(chartW / 90.0, 4, 12);
        double raw    = dataDays / target;
        double step   = NiceSteps.FirstOrDefault(s => s >= raw, 365);

        for (var d = min.Date; d <= max; d = d.AddDays(step))
            yield return d;
    }

    /// <summary>Maps a d3 axis format (%Y-%m-%d) to a .NET one; defaults to "MMM d".</summary>
    private static string AxisFormat(string f) =>
        string.IsNullOrWhiteSpace(f)
            ? "MMM d"
            : f.Replace("%Y", "yyyy").Replace("%y", "yy").Replace("%m", "MM").Replace("%d", "dd")
               .Replace("%e", "d").Replace("%b", "MMM").Replace("%B", "MMMM").Replace("%a", "ddd")
               .Replace("%A", "dddd").Replace("%H", "HH").Replace("%M", "mm").Replace("%S", "ss")
               .Replace("%%", "%");

    // ── Colour & text helpers ─────────────────────────────────────────────────

    private static Brush Tint(Color c, byte a) { var b = new SolidColorBrush(Color.FromArgb(a, c.R, c.G, c.B)); b.Freeze(); return b; }
    private static Brush Solid(Color c)        { var b = new SolidColorBrush(c); b.Freeze(); return b; }

    /// <summary>Readable text colour over a tinted bar fill of colour <paramref name="c"/>.</summary>
    private static Brush OnColor(Color c, byte a)
    {
        double lum = (0.299 * c.R + 0.587 * c.G + 0.114 * c.B) * (a / 255.0);
        return lum > 110 ? Solid(Colors.Black) : Solid(Colors.White);
    }

    private static double MeasureText(string text, double fontSize)
    {
        var ft = new FormattedText(text, CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
            new Typeface(BodyFont, FontStyles.Normal, FontWeights.Normal, FontStretches.Normal), fontSize, Brushes.Black, 1.0);
        return ft.Width;
    }
}
