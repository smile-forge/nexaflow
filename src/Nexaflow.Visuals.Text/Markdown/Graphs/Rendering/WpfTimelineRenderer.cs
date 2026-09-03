using Nexaflow.Visuals.Text.Markdown.Graphs.Charts;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;

namespace Nexaflow.Visuals.Text.Markdown.Graphs.Rendering;

/// <summary>
/// Renders a <see cref="TimelineDiagram"/> as a WPF <see cref="FrameworkElement"/>, themed from a
/// <see cref="MarkdownPalette"/>.  Periods sit on a spine — a row for <c>LR</c>, a column for
/// <c>TD</c> — with their events stacked away from it and a tinted band over each section.  Colour
/// follows Mermaid: the section's slot when sections exist, otherwise the period's own, and slot 0
/// for everything when <c>disableMulticolor</c> is set; the slots come from the config's
/// <c>cScale</c>/<c>cScaleLabel</c> bank first and the palette's series bank after that.
/// </summary>
public static class WpfTimelineRenderer
{
    private static readonly FontFamily BodyFont = DiagramText.BodyFont;

    private const double Outer      = 16;
    private const double TitleH     = 28;
    private const double SectionH   = 26;
    private const double SectionW   = 110;   // TD: the section strip on the left
    private const double SectionGap = 6;
    private const double ColW       = 150;   // period + event box width
    private const double ColGap     = 24;
    private const double PeriodMinH = 34;
    private const double EventGap   = 6;
    private const double DropGap    = 12;    // spine → first event
    private const double DotR       = 4;

    public static FrameworkElement Render(TimelineDiagram diagram, MarkdownPalette palette)
    {
        if (diagram.PeriodCount == 0)
            return new TextBlock { Text = "(empty timeline)", Foreground = palette.TextMuted, FontSize = 12 };

        var canvas = diagram.Direction == TimelineDirection.TopDown
            ? LayoutTopDown(diagram, palette)
            : LayoutLeftToRight(diagram, palette);

        return new Border
        {
            Background = palette.CodeBg, BorderBrush = palette.CodeBorder, BorderThickness = new Thickness(1),
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

    // ── Shared slot resolution ─────────────────────────────────────────────

    private readonly record struct Slot(Color Colour, Brush Label);

    /// <summary>One flat entry per period with the colour slot Mermaid's rule assigns it.</summary>
    private static List<(TimelinePeriod period, int sectionIndex, Slot slot)> Flatten(TimelineDiagram diagram, MarkdownPalette palette)
    {
        var rows = new List<(TimelinePeriod, int, Slot)>();
        bool bySection = diagram.HasSections;
        int periodIndex = 0;
        for (int si = 0; si < diagram.Sections.Count; si++)
        {
            foreach (var p in diagram.Sections[si].Periods)
            {
                int ci = diagram.Config.DisableMulticolor ? 0 : bySection ? si : periodIndex;
                rows.Add((p, si, SlotFor(diagram.Config, palette, ci)));
                periodIndex++;
            }
        }
        return rows;
    }

    private static Slot SlotFor(TimelineConfig cfg, MarkdownPalette palette, int index)
    {
        Color accentC = (palette.Accent as SolidColorBrush)?.Color ?? Colors.SteelBlue;
        Color c = (cfg.ScaleAt(index) as SolidColorBrush)?.Color
               ?? (palette.Series[index % palette.Series.Count] as SolidColorBrush)?.Color
               ?? accentC;
        Brush label = cfg.ScaleLabelAt(index) ?? OnColor(c, 0xCC);
        return new Slot(c, label);
    }

    // ── LR: periods across a horizontal spine, events stacked below ─────────

    private static Canvas LayoutLeftToRight(TimelineDiagram diagram, MarkdownPalette palette)
    {
        var rows = Flatten(diagram, palette);
        double pad = diagram.Config.Padding;
        double textW = Math.Max(20, ColW - 2 * pad);   // an absurd config padding must not make a negative width
        bool hasTitle = !string.IsNullOrWhiteSpace(diagram.Title);

        // Measure first: the period row is as tall as its tallest wrapped title.
        var periodLabels = rows.Select(r => MakeLabel(r.period.Title, textW, 12, FontWeights.SemiBold, r.slot.Label, TextAlignment.Center)).ToList();
        double periodH = Math.Max(PeriodMinH, periodLabels.Max(l => l.DesiredSize.Height) + 2 * pad);

        double x0 = Outer;
        double X(int i) => x0 + i * (ColW + ColGap);
        double titleBottom = Outer + (hasTitle ? TitleH : 0);
        double sectionTop  = titleBottom;
        double periodTop   = diagram.HasSections ? sectionTop + SectionH + SectionGap : sectionTop;
        double periodCy    = periodTop + periodH / 2;
        double eventsTop   = periodTop + periodH + DropGap;

        int n = rows.Count;
        double canvasW = 2 * Outer + n * ColW + (n - 1) * ColGap;
        double bottom  = periodTop + periodH;
        var canvas = new Canvas { Width = canvasW, Background = palette.CodeBg };

        // 1. section bands
        if (diagram.HasSections)
        {
            for (int i = 0; i < n;)
            {
                int si = rows[i].sectionIndex, j = i;
                while (j < n && rows[j].sectionIndex == si) j++;
                var sec = diagram.Sections[si];
                if (sec.Name.Length > 0)
                {
                    double left = X(i), right = X(j - 1) + ColW;
                    var c = rows[i].slot.Colour;
                    canvas.Children.Add(new Rectangle { Width = right - left, Height = SectionH, RadiusX = 4, RadiusY = 4, Fill = DiagramBrushes.Tint(c, 0x2A), Stroke = DiagramBrushes.Tint(c, 0x80), StrokeThickness = 1 }.At(left, sectionTop));
                    var name = MakeLabel(sec.Name, right - left - 2 * pad, 12, FontWeights.SemiBold, palette.Heading, TextAlignment.Center);
                    canvas.Children.Add(name.At(left + pad, sectionTop + (SectionH - name.DesiredSize.Height) / 2));
                }
                i = j;
            }
        }

        // 2. spine
        canvas.Children.Add(new Line { X1 = X(0) + ColW / 2, Y1 = periodCy, X2 = X(n - 1) + ColW / 2, Y2 = periodCy, Stroke = palette.CodeBorder, StrokeThickness = 2 });

        // 3. periods + events
        for (int i = 0; i < n; i++)
        {
            var (period, _, slot) = rows[i];
            double x = X(i), cx = x + ColW / 2;
            var c = slot.Colour;

            double y = eventsTop;
            var boxes = new List<(TextBlock label, double top, double h)>();
            foreach (var ev in period.Events)
            {
                var label = MakeLabel(ev, textW, 11, FontWeights.Normal, palette.Text, TextAlignment.Left);
                double h = label.DesiredSize.Height + 2 * pad;
                boxes.Add((label, y, h));
                y += h + EventGap;
            }
            double lastBottom = boxes.Count > 0 ? boxes[^1].top + boxes[^1].h : periodTop + periodH;
            bottom = Math.Max(bottom, lastBottom);

            if (boxes.Count > 0)
                canvas.Children.Add(new Line { X1 = cx, Y1 = periodTop + periodH, X2 = cx, Y2 = boxes[^1].top, Stroke = DiagramBrushes.Tint(c, 0x80), StrokeThickness = 1.5 });

            canvas.Children.Add(new Rectangle { Width = ColW, Height = periodH, RadiusX = 5, RadiusY = 5, Fill = DiagramBrushes.Tint(c, 0xCC), Stroke = DiagramBrushes.Frozen(c), StrokeThickness = 1.2 }.At(x, periodTop));
            canvas.Children.Add(periodLabels[i].At(x + pad, periodTop + (periodH - periodLabels[i].DesiredSize.Height) / 2));
            canvas.Children.Add(new Ellipse { Width = 2 * DotR, Height = 2 * DotR, Fill = DiagramBrushes.Frozen(c), Stroke = palette.CodeBg, StrokeThickness = 1 }.At(cx - DotR, periodTop + periodH - DotR));

            foreach (var (label, top, h) in boxes)
            {
                canvas.Children.Add(new Rectangle { Width = ColW, Height = h, RadiusX = 4, RadiusY = 4, Fill = DiagramBrushes.Tint(c, 0x33), Stroke = DiagramBrushes.Frozen(c), StrokeThickness = 1 }.At(x, top));
                canvas.Children.Add(label.At(x + pad, top + pad));
            }
        }

        // 4. title
        if (hasTitle) AddTitle(canvas, diagram.Title, palette, canvasW);

        canvas.Height = bottom + Outer;
        return canvas;
    }

    // ── TD: periods down a vertical spine, events flowing to the right ──────

    private static Canvas LayoutTopDown(TimelineDiagram diagram, MarkdownPalette palette)
    {
        var rows = Flatten(diagram, palette);
        double pad = diagram.Config.Padding;
        double textW = Math.Max(20, ColW - 2 * pad);   // an absurd config padding must not make a negative width
        bool hasTitle = !string.IsNullOrWhiteSpace(diagram.Title);

        double periodX  = Outer + (diagram.HasSections ? SectionW + SectionGap : 0);
        double eventsX  = periodX + ColW + DropGap;
        double topStart = Outer + (hasTitle ? TitleH : 0);

        // Row geometry: each period row is as tall as its title or its tallest event.
        var rowTops = new List<double>();
        var rowHs   = new List<double>();
        var periodLabels = new List<TextBlock>();
        var eventLabels  = new List<List<(TextBlock label, double h)>>();
        double y = topStart, maxRight = periodX + ColW;
        for (int i = 0; i < rows.Count; i++)
        {
            var (period, _, slot) = rows[i];
            var pl = MakeLabel(period.Title, textW, 12, FontWeights.SemiBold, slot.Label, TextAlignment.Center);
            periodLabels.Add(pl);
            var evs = period.Events
                .Select(e => { var l = MakeLabel(e, textW, 11, FontWeights.Normal, palette.Text, TextAlignment.Left); return (label: l, h: l.DesiredSize.Height + 2 * pad); })
                .ToList();
            eventLabels.Add(evs);
            double h = Math.Max(PeriodMinH, pl.DesiredSize.Height + 2 * pad);
            if (evs.Count > 0)
            {
                h = Math.Max(h, evs.Max(e => e.h));
                maxRight = Math.Max(maxRight, eventsX + evs.Count * ColW + (evs.Count - 1) * EventGap);
            }
            rowTops.Add(y); rowHs.Add(h);
            y += h + ColGap;
        }
        double bottom  = y - ColGap;
        double canvasW = maxRight + Outer;
        var canvas = new Canvas { Width = canvasW, Height = bottom + Outer, Background = palette.CodeBg };

        // 1. section strips
        if (diagram.HasSections)
        {
            for (int i = 0; i < rows.Count;)
            {
                int si = rows[i].sectionIndex, j = i;
                while (j < rows.Count && rows[j].sectionIndex == si) j++;
                var sec = diagram.Sections[si];
                if (sec.Name.Length > 0)
                {
                    double top = rowTops[i], bot = rowTops[j - 1] + rowHs[j - 1];
                    var c = rows[i].slot.Colour;
                    canvas.Children.Add(new Rectangle { Width = SectionW, Height = bot - top, RadiusX = 4, RadiusY = 4, Fill = DiagramBrushes.Tint(c, 0x2A), Stroke = DiagramBrushes.Tint(c, 0x80), StrokeThickness = 1 }.At(Outer, top));
                    var name = MakeLabel(sec.Name, SectionW - 2 * pad, 12, FontWeights.SemiBold, palette.Heading, TextAlignment.Center);
                    canvas.Children.Add(name.At(Outer + pad, top + (bot - top - name.DesiredSize.Height) / 2));
                }
                i = j;
            }
        }

        // 2. spine
        double cx = periodX + ColW / 2;
        canvas.Children.Add(new Line { X1 = cx, Y1 = rowTops[0] + rowHs[0] / 2, X2 = cx, Y2 = rowTops[^1] + rowHs[^1] / 2, Stroke = palette.CodeBorder, StrokeThickness = 2 });

        // 3. periods + events
        for (int i = 0; i < rows.Count; i++)
        {
            var c = rows[i].slot.Colour;
            double top = rowTops[i], h = rowHs[i], cy = top + h / 2;
            var evs = eventLabels[i];

            if (evs.Count > 0)
            {
                double lastLeft = eventsX + (evs.Count - 1) * (ColW + EventGap);
                canvas.Children.Add(new Line { X1 = periodX + ColW, Y1 = cy, X2 = lastLeft, Y2 = cy, Stroke = DiagramBrushes.Tint(c, 0x80), StrokeThickness = 1.5 });
            }

            canvas.Children.Add(new Rectangle { Width = ColW, Height = h, RadiusX = 5, RadiusY = 5, Fill = DiagramBrushes.Tint(c, 0xCC), Stroke = DiagramBrushes.Frozen(c), StrokeThickness = 1.2 }.At(periodX, top));
            canvas.Children.Add(periodLabels[i].At(periodX + pad, top + (h - periodLabels[i].DesiredSize.Height) / 2));
            canvas.Children.Add(new Ellipse { Width = 2 * DotR, Height = 2 * DotR, Fill = DiagramBrushes.Frozen(c), Stroke = palette.CodeBg, StrokeThickness = 1 }.At(periodX + ColW - DotR, cy - DotR));

            double ex = eventsX;
            foreach (var (label, eh) in evs)
            {
                canvas.Children.Add(new Rectangle { Width = ColW, Height = eh, RadiusX = 4, RadiusY = 4, Fill = DiagramBrushes.Tint(c, 0x33), Stroke = DiagramBrushes.Frozen(c), StrokeThickness = 1 }.At(ex, cy - eh / 2));
                canvas.Children.Add(label.At(ex + pad, cy - eh / 2 + pad));
                ex += ColW + EventGap;
            }
        }

        // 4. title
        if (hasTitle) AddTitle(canvas, diagram.Title, palette, canvasW);

        return canvas;
    }

    // ── Text & colour helpers ────────────────────────────────────────────────

    private static void AddTitle(Canvas canvas, string title, MarkdownPalette palette, double canvasW)
    {
        double tw = DiagramText.Measure(title, 15);
        canvas.Children.Add(new TextBlock { Text = title, Foreground = palette.Heading, FontFamily = BodyFont, FontSize = 15, FontWeight = FontWeights.SemiBold }.At((canvasW - tw) / 2, Outer - 2));
    }

    /// <summary>A wrapped, pre-measured label of a fixed width (so its height is known before placement).</summary>
    private static TextBlock MakeLabel(string text, double width, double fontSize, FontWeight weight, Brush brush, TextAlignment align)
    {
        var tb = new TextBlock
        {
            Text = text, Width = width, TextWrapping = TextWrapping.Wrap, TextAlignment = align,
            Foreground = brush, FontFamily = BodyFont, FontSize = fontSize, FontWeight = weight,
        };
        tb.Measure(new Size(width, double.PositiveInfinity));
        return tb;
    }


    /// <summary>Readable text colour over a tinted fill of colour <paramref name="c"/>.</summary>
    private static Brush OnColor(Color c, byte a)
    {
        double lum = (0.299 * c.R + 0.587 * c.G + 0.114 * c.B) * (a / 255.0);
        return lum > 110 ? DiagramBrushes.Frozen(Colors.Black) : DiagramBrushes.Frozen(Colors.White);
    }

}
