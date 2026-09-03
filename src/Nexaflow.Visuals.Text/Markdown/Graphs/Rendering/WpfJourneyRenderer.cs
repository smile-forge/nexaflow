using Nexaflow.Visuals.Text.Markdown.Graphs.Charts;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;

namespace Nexaflow.Visuals.Text.Markdown.Graphs.Rendering;

/// <summary>
/// Renders a <see cref="JourneyDiagram"/> as a WPF <see cref="FrameworkElement"/>, themed from a
/// <see cref="MarkdownPalette"/>.  Tasks run left to right under a band per section; above each
/// task a face — drawn as vector strokes, not a glyph — floats in a score lane, higher for a better
/// score, coloured success/warning/danger by mood.  Each actor gets a colour (config
/// <c>actorColours</c>, else the palette's series bank offset from the section colours), shown in a
/// legend and as a dot on every task they take part in.
/// </summary>
public static class WpfJourneyRenderer
{
    private static readonly FontFamily BodyFont = DiagramText.BodyFont;

    private const double Outer      = 16;
    private const double TitleH     = 28;
    private const double LegendRowH = 20;
    private const double LegendR    = 6;
    private const double LegendGap  = 8;
    private const double SectionH   = 28;
    private const double SectionGap = 6;
    private const double LaneStep   = 22;                 // one score step
    private const double LaneH      = 5 * LaneStep + 8;   // score lane (faces live here)
    private const double FaceR      = 12;
    private const double ActorR     = 6;

    public static FrameworkElement Render(JourneyDiagram diagram, MarkdownPalette palette)
    {
        if (diagram.TaskCount == 0)
            return new TextBlock { Text = "(empty user journey)", Foreground = palette.TextMuted, FontSize = 12 };

        var cfg = diagram.Config;
        double W = Math.Max(40, cfg.Width), H = Math.Max(20, cfg.Height), gap = Math.Max(0, cfg.BoxMargin);
        bool hasTitle = !string.IsNullOrWhiteSpace(diagram.Title);
        var actors = diagram.Actors;

        Color accentC = (palette.Accent as SolidColorBrush)?.Color ?? Colors.SteelBlue;
        Color textC   = (palette.Text   as SolidColorBrush)?.Color ?? Colors.White;

        Color SectionColour(int si) =>
            (si < cfg.SectionFills.Count ? cfg.SectionFills[si] as SolidColorBrush : null)?.Color
            ?? (palette.Series[si % palette.Series.Count] as SolidColorBrush)?.Color
            ?? accentC;

        Color ActorColour(int k) =>
            (k < cfg.ActorColours.Count ? cfg.ActorColours[k] as SolidColorBrush : null)?.Color
            ?? (palette.Series[(k + palette.Series.Count / 2) % palette.Series.Count] as SolidColorBrush)?.Color
            ?? accentC;

        Color MoodColour(JourneyMood mood) => (mood switch
        {
            JourneyMood.Happy   => palette.Success as SolidColorBrush,
            JourneyMood.Neutral => palette.Warning as SolidColorBrush,
            _                   => palette.Danger  as SolidColorBrush,
        })?.Color ?? accentC;

        // ── Flat task list + vertical layout ──
        var rows = new List<(JourneyTask task, int sectionIndex)>();
        for (int si = 0; si < diagram.Sections.Count; si++)
            foreach (var t in diagram.Sections[si].Tasks) rows.Add((t, si));
        int n = rows.Count;
        bool hasBands = diagram.Sections.Any(s => s.Name.Length > 0);

        double X(int i) => Outer + i * (W + gap);
        double titleBottom = Outer + (hasTitle ? TitleH : 0);
        double legendTop   = titleBottom;
        double legendH     = actors.Count * LegendRowH + (actors.Count > 0 ? LegendGap : 0);
        double sectionTop  = legendTop + legendH;
        double laneTop     = hasBands ? sectionTop + SectionH + SectionGap : sectionTop;
        double taskTop     = laneTop + LaneH;
        double bottom      = taskTop + H;

        double legendW = actors.Count == 0 ? 0 : actors.Max(a => 2 * LegendR + 6 + DiagramText.Measure(a, 11));
        double canvasW = 2 * Outer + Math.Max(legendW, n * W + (n - 1) * gap);
        var canvas = new Canvas { Width = canvasW, Height = bottom + Outer, Background = palette.CodeBg };

        // 1. actor legend
        for (int k = 0; k < actors.Count; k++)
        {
            double y = legendTop + k * LegendRowH;
            canvas.Children.Add(new Ellipse { Width = 2 * LegendR, Height = 2 * LegendR, Fill = DiagramBrushes.Frozen(ActorColour(k)), Stroke = palette.CodeBorder, StrokeThickness = 1 }.At(Outer, y + (LegendRowH - 2 * LegendR) / 2));
            canvas.Children.Add(new TextBlock { Text = actors[k], Foreground = palette.Text, FontFamily = BodyFont, FontSize = 11 }.At(Outer + 2 * LegendR + 6, y + 2));
        }

        // 2. section bands
        if (hasBands)
        {
            for (int i = 0; i < n;)
            {
                int si = rows[i].sectionIndex, j = i;
                while (j < n && rows[j].sectionIndex == si) j++;
                var sec = diagram.Sections[si];
                if (sec.Name.Length > 0)
                {
                    double left = X(i), right = X(j - 1) + W;
                    var sc = SectionColour(si);
                    canvas.Children.Add(new Rectangle { Width = right - left, Height = SectionH, RadiusX = 4, RadiusY = 4, Fill = DiagramBrushes.Tint(sc, 0x66), Stroke = DiagramBrushes.Frozen(sc), StrokeThickness = 1 }.At(left, sectionTop));
                    // Heading ink rather than a fill-derived colour: the band is a light tint over whichever theme is active.
                    var name = MakeLabel(sec.Name, right - left - 12, 12, FontWeights.SemiBold, palette.Heading, TextAlignment.Center);
                    canvas.Children.Add(name.At(left + 6, sectionTop + (SectionH - name.DesiredSize.Height) / 2));
                }
                i = j;
            }
        }

        // 3. tasks, faces, actor dots
        for (int i = 0; i < n; i++)
        {
            var (task, si) = rows[i];
            var sc = SectionColour(si);
            double x = X(i), cx = x + W / 2;

            // face in the score lane — score 5 at the top, 1 just above the task box
            double cy = laneTop + (5 - task.Score) * LaneStep + FaceR + 2;
            canvas.Children.Add(new Line { X1 = cx, Y1 = cy + FaceR, X2 = cx, Y2 = taskTop, Stroke = DiagramBrushes.Tint(textC, 0x50), StrokeThickness = 1, StrokeDashArray = new DoubleCollection([2, 3]) });
            DrawFace(canvas, cx, cy, task.Mood, MoodColour(task.Mood), textC);

            // task box
            canvas.Children.Add(new Rectangle { Width = W, Height = H, RadiusX = 3, RadiusY = 3, Fill = DiagramBrushes.Tint(sc, 0x33), Stroke = DiagramBrushes.Frozen(sc), StrokeThickness = 1 }.At(x, taskTop));
            var label = MakeLabel(task.Name, W - 12, cfg.TaskFontSize, FontWeights.Normal, palette.Text, TextAlignment.Center);
            canvas.Children.Add(label.At(x + 6, taskTop + Math.Max(2, (H - label.DesiredSize.Height) / 2)));

            // actor dots along the box's top-left edge
            for (int a = 0; a < task.Actors.Count; a++)
            {
                int k = IndexOf(actors, task.Actors[a]);
                canvas.Children.Add(new Ellipse { Width = 2 * ActorR, Height = 2 * ActorR, Fill = DiagramBrushes.Frozen(ActorColour(k)), Stroke = palette.CodeBg, StrokeThickness = 1.2 }.At(x + 4 + a * (ActorR + 3), taskTop - ActorR));
            }
        }

        // 4. title
        if (hasTitle)
        {
            double tw = DiagramText.Measure(diagram.Title, 15);
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
                MaxHeight = 600,
            },
        };
    }

    // ── Face ────────────────────────────────────────────────────────────────

    /// <summary>A face built from strokes: a tinted disc, two eyes, and a mouth that is an arc bulging
    /// down (smile), an arc bulging up (frown) or a flat line.</summary>
    private static void DrawFace(Canvas canvas, double cx, double cy, JourneyMood mood, Color moodC, Color inkC)
    {
        var ink = DiagramBrushes.Frozen(inkC);
        canvas.Children.Add(new Ellipse { Width = 2 * FaceR, Height = 2 * FaceR, Fill = DiagramBrushes.Tint(moodC, 0xB0), Stroke = ink, StrokeThickness = 1.2 }.At(cx - FaceR, cy - FaceR));
        foreach (double dx in new[] { -4.5, 4.5 })
            canvas.Children.Add(new Ellipse { Width = 3.6, Height = 3.6, Fill = ink }.At(cx + dx - 1.8, cy - 3.5 - 1.8));

        if (mood == JourneyMood.Neutral)
        {
            canvas.Children.Add(new Line { X1 = cx - 5, Y1 = cy + 4.5, X2 = cx + 5, Y2 = cy + 4.5, Stroke = ink, StrokeThickness = 1.5 });
            return;
        }

        // In screen coordinates a clockwise sweep from the left point to the right one passes over
        // the top (a frown); counter-clockwise passes underneath (a smile).
        bool smile = mood == JourneyMood.Happy;
        double my = smile ? cy + 3 : cy + 7;
        var figure = new PathFigure { StartPoint = new Point(cx - 5, my), IsClosed = false, IsFilled = false };
        figure.Segments.Add(new ArcSegment(new Point(cx + 5, my), new Size(6, 4), 0, false,
            smile ? SweepDirection.Counterclockwise : SweepDirection.Clockwise, true));
        var geometry = new PathGeometry();
        geometry.Figures.Add(figure);
        canvas.Children.Add(new Path { Data = geometry, Stroke = ink, StrokeThickness = 1.5 });
    }

    // ── Text & colour helpers ────────────────────────────────────────────────

    private static int IndexOf(IReadOnlyList<string> actors, string actor)
    {
        for (int i = 0; i < actors.Count; i++) if (string.Equals(actors[i], actor, StringComparison.Ordinal)) return i;
        return 0;
    }

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


    private static Brush OnColor(Color c, byte a)
    {
        double lum = (0.299 * c.R + 0.587 * c.G + 0.114 * c.B) * (a / 255.0);
        return lum > 110 ? DiagramBrushes.Frozen(Colors.Black) : DiagramBrushes.Frozen(Colors.White);
    }

}
