using Nexaflow.Visuals.Text.Markdown.Graphs.Charts;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;

namespace Nexaflow.Visuals.Text.Markdown.Graphs.Rendering;

/// <summary>
/// Renders a <see cref="SequenceDiagram"/> as a WPF <see cref="FrameworkElement"/>, themed from a
/// <see cref="MarkdownPalette"/>.  Participants become boxes (or actor glyphs) joined by dashed
/// lifelines; the timeline of messages, notes, activations and fragment blocks is laid out
/// top-to-bottom in source order.
/// </summary>
public static class WpfSequenceDiagramRenderer
{
    private static readonly FontFamily BodyFont = DiagramText.BodyFont;

    // ── Geometry constants ─────────────────────────────────────────────────

    private const double Outer       = 14;
    private const double TitleH      = 26;
    private const double BoxH        = 32;
    private const double BoxPadX     = 14;
    private const double BoxMinW     = 70;
    private const double BoxMaxW     = 220;
    private const double ActorH      = 48;
    private const double MinColGap   = 150;
    private const double MinEdgeGap  = 36;
    private const double FirstGap    = 28;
    private const double MsgGap      = 40;
    private const double SelfGap     = 50;
    private const double LoopW       = 40;
    private const double LoopH       = 20;
    private const double NotePad     = 8;
    private const double FragHeadH   = 26;
    private const double FragSecH    = 22;
    private const double FragEndH    = 14;
    private const double FragPad     = 14;   // frame inset past the outermost involved lifeline
    private const double EndGap      = 20;
    private const double ActW        = 10;
    private const double ArrowLen    = 9;
    private const double FontSize    = 12;
    private const double MsgFontSize = 11;
    private const double LineH       = 14;

    /// <summary>Band reserved above the participant heads for a <c>box</c> grouping's label.</summary>
    private const double BoxLabelH   = 18;

    // ── Theme (set per render; markdown renders synchronously on the UI thread) ──

    private static Brush Bg = Brushes.Black, Border = Brushes.Gray, Title = Brushes.White,
        BoxBg = Brushes.DimGray, BoxBorder = Brushes.SteelBlue, Text = Brushes.White,
        Muted = Brushes.Gray, Line = Brushes.SteelBlue, NoteBg = Brushes.DimGray,
        FrameStroke = Brushes.Gray, FrameTabBg = Brushes.DimGray, ActBg = Brushes.DimGray, OnAccent = Brushes.White;
    private static Color AccentColor = Colors.SteelBlue;

    /// <summary>Element-card colours, used only by participants carrying a <see cref="SequenceParticipant.Card"/>.</summary>
    private static C4Palette C4 = C4Palette.Resolve(MarkdownPalette.Dark);

    private static void SetTheme(MarkdownPalette p)
    {
        Bg = p.CodeBg; Border = p.CodeBorder; Title = p.Heading;
        BoxBg = p.TableHeaderBg; BoxBorder = p.Accent; Text = p.Text; Muted = p.TextMuted;
        Line = p.Accent; NoteBg = p.QuoteBg; FrameStroke = p.TextMuted;
        FrameTabBg = p.TableHeaderBg; ActBg = p.QuoteBg;
        AccentColor = (p.Accent as SolidColorBrush)?.Color ?? Colors.SteelBlue;
        OnAccent = DiagramBrushes.Luminance(AccentColor) > 140 ? Brushes.Black : Brushes.White;
        C4 = C4Palette.Resolve(p);
    }

    private sealed class FragPlace
    {
        public FragmentKind Kind; public string Label = ""; public string? Color;
        public double Top, Bottom, MinX = double.MaxValue, MaxX = double.MinValue;
        public List<(double y, string label)> Sections = [];
        public void TouchX(double lo, double hi) { if (lo < MinX) MinX = lo; if (hi > MaxX) MaxX = hi; }
    }

    // ── Public API ─────────────────────────────────────────────────────────

    public static FrameworkElement Render(SequenceDiagram diagram, MarkdownPalette palette)
    {
        SetTheme(palette);

        int n = diagram.Participants.Count;
        if (n == 0)
            return new TextBlock { Text = "(empty sequence diagram)", Foreground = Muted, FontSize = 12 };

        bool hasTitle = !string.IsNullOrWhiteSpace(diagram.Title);

        var index   = new Dictionary<string, int>(StringComparer.Ordinal);
        var widths  = new double[n];
        var headHs  = new double[n];
        var centers = new double[n];
        double headerH = BoxH;
        for (int i = 0; i < n; i++)
        {
            var pt = diagram.Participants[i];
            index[pt.Id] = i;
            (widths[i], headHs[i]) = MeasureHead(pt);
            if (!pt.Created) headerH = Math.Max(headerH, headHs[i]);   // created heads appear mid-diagram
        }
        centers[0] = Outer + widths[0] / 2;
        for (int i = 1; i < n; i++)
            centers[i] = centers[i - 1] + Math.Max(MinColGap, widths[i - 1] / 2 + widths[i] / 2 + MinEdgeGap);

        // A box grouping writes its label above the participant heads, so it needs a band of its own
        // — without one the label sits a few pixels above the tallest head and any head taller than
        // the rest (a card, a database glyph) is drawn straight through it.
        double boxBandH = diagram.Boxes.Any(b => !string.IsNullOrWhiteSpace(b.Label)) ? BoxLabelH : 0;
        double topBoxY = Outer + (hasTitle ? TitleH : 0) + boxBandH;
        double lifeTop = topBoxY + headerH;

        double Cx(string id) => index.TryGetValue(id, out int k) ? centers[k] : centers[0];
        int    Ix(string id) => index.TryGetValue(id, out int k) ? k : 0;

        // ── Layout pass ──
        var draw  = new List<object>();
        var yOf   = new Dictionary<SequenceItem, double>();
        var frags = new List<FragPlace>();
        var open  = new Stack<FragPlace>();
        var creationY = new Dictionary<string, double>(StringComparer.Ordinal);
        var destroyY  = new Dictionary<string, double>(StringComparer.Ordinal);
        var pendingDestroy = new Dictionary<string, double>(StringComparer.Ordinal);
        double maxRight = centers[n - 1] + widths[n - 1] / 2;
        double? lastMsgY = null;
        double y = lifeTop + FirstGap;

        void TouchOpen(double lo, double hi) { foreach (var f in open) f.TouchX(lo, hi); }

        foreach (var item in diagram.Items)
        {
            switch (item)
            {
                case SequenceMessage m:
                {
                    bool self = string.Equals(m.FromId, m.ToId, StringComparison.Ordinal);
                    int lines = LabelLines(m);
                    double arrowY = y + Math.Max(0, lines) * LineH + 6;
                    double fromX = Cx(m.FromId), toX = Cx(m.ToId);

                    foreach (var ep in new[] { m.FromId, m.ToId })
                    {
                        var p = diagram.Find(ep);
                        if (p is { Created: true } && !creationY.ContainsKey(ep)) creationY[ep] = arrowY;
                        if (pendingDestroy.ContainsKey(ep)) { destroyY[ep] = arrowY; pendingDestroy.Remove(ep); }
                    }

                    // arrow lands on the near edge of a participant's box at its creation point
                    double headX = toX;
                    if (diagram.Find(m.ToId) is { Created: true } && creationY.TryGetValue(m.ToId, out double cyT) && cyT == arrowY)
                        headX = toX + (toX >= fromX ? -widths[Ix(m.ToId)] / 2 : widths[Ix(m.ToId)] / 2);

                    yOf[m] = arrowY; lastMsgY = arrowY;
                    draw.Add(new MsgPlace(m, fromX, toX, headX, arrowY, self));

                    double labelW = Math.Max(
                        DiagramText.MeasureBlock(m.Text, MsgFontSize).w,
                        HasTech(m) ? DiagramText.Measure($"[{m.Technology!.Trim()}]", MsgFontSize - 1) : 0);
                    double selfRight = fromX + LoopW + 6 + labelW;
                    if (self) { TouchOpen(fromX, selfRight); maxRight = Math.Max(maxRight, selfRight + 12); }
                    else TouchOpen(Math.Min(fromX, toX), Math.Max(fromX, toX));
                    y = arrowY + (self ? SelfGap : MsgGap) + Math.Max(0, lines - 1) * LineH;
                    break;
                }
                case SequenceNote nt:
                {
                    var (tw, lines) = DiagramText.MeasureBlock(nt.Text, MsgFontSize);
                    double noteH = lines * LineH + 2 * NotePad;
                    var ids = nt.ParticipantIds.Select(Ix).ToList();
                    double left, right;
                    if (nt.Placement == NotePlacement.Over && ids.Count >= 2)
                    {
                        left = centers[ids.Min()] - 12; right = centers[ids.Max()] + 12;
                        if (right - left < tw + 2 * NotePad) { double c = (left + right) / 2; left = c - (tw / 2 + NotePad); right = c + (tw / 2 + NotePad); }
                    }
                    else
                    {
                        double w = tw + 2 * NotePad, cx = centers[ids[0]];
                        (left, right) = nt.Placement switch
                        {
                            NotePlacement.RightOf => (cx + 10, cx + 10 + w),
                            NotePlacement.LeftOf  => (Math.Max(Outer, cx - 10 - w), Math.Max(Outer, cx - 10 - w) + w),
                            _                     => (cx - w / 2, cx + w / 2),
                        };
                    }
                    yOf[nt] = y;
                    draw.Add(new NotePlace(nt, y, noteH, left, right));
                    TouchOpen(left, right);
                    maxRight = Math.Max(maxRight, right);
                    y += noteH + 14;
                    break;
                }
                case SequenceActivation a: yOf[a] = lastMsgY ?? y; break;
                case SequenceDestroy d:    pendingDestroy[d.ParticipantId] = lastMsgY ?? y; break;
                case SequenceFragment f:
                    switch (f.Boundary)
                    {
                        case FragmentBoundary.Begin:
                            var fp = new FragPlace { Kind = f.Kind, Label = f.Label, Color = f.Color, Top = y };
                            open.Push(fp); frags.Add(fp); y += FragHeadH;
                            break;
                        case FragmentBoundary.Section:
                            if (open.Count > 0) open.Peek().Sections.Add((y, f.Label));
                            y += FragSecH;
                            break;
                        case FragmentBoundary.End:
                            if (open.Count > 0) open.Pop().Bottom = y;
                            y += FragEndH;
                            break;
                    }
                    break;
            }
        }
        foreach (var f in open) f.Bottom = y;
        foreach (var (id, fy) in pendingDestroy) destroyY[id] = fy;   // destroy with no following message

        double botBoxY = y + EndGap;
        double lifeBot = botBoxY;

        var bars = ComputeActivationBars(diagram, yOf, lifeBot);

        // Tallest head — the bottom band must fit it. Zero when the foot boxes are off, which is
        // the only thing that changes: nothing above this line consults it.
        double bandH   = diagram.ShowFootBoxes ? headHs.Max() : 0;
        double canvasW = maxRight + Outer;
        double canvasH = botBoxY + bandH + Outer;
        var canvas = new Canvas { Width = canvasW, Height = canvasH, Background = Bg };

        // 1. box groupings
        foreach (var box in diagram.Boxes)
            DrawBoxGroup(canvas, box, centers, widths, index, topBoxY - boxBandH - 6, botBoxY + bandH + 6);

        // 2. rect highlights
        foreach (var f in frags.Where(f => f.Kind == FragmentKind.Rect))
            DrawRect(canvas, f, centers, widths, n);

        // 3. lifelines (created start at their creation point; destroyed stop at the ✗)
        for (int i = 0; i < n; i++)
        {
            var pt = diagram.Participants[i];
            double top = pt.Created   && creationY.TryGetValue(pt.Id, out double cy) ? cy + headHs[i] / 2 : lifeTop;
            double bot = pt.Destroyed && destroyY.TryGetValue(pt.Id, out double dy)  ? dy : lifeBot;
            canvas.Children.Add(new Line { X1 = centers[i], Y1 = top, X2 = centers[i], Y2 = bot, Stroke = Muted, StrokeThickness = 1, StrokeDashArray = new DoubleCollection([3, 3]) });
        }

        // 4. participant heads
        for (int i = 0; i < n; i++)
        {
            var pt = diagram.Participants[i];
            if (pt.Created && creationY.TryGetValue(pt.Id, out double cy))
                DrawHead(canvas, pt, centers[i], cy - headHs[i] / 2, widths[i], headHs[i]);
            else
                DrawHead(canvas, pt, centers[i], lifeTop - headHs[i], widths[i], headHs[i]);

            if (pt.Destroyed && destroyY.TryGetValue(pt.Id, out double dy))
                DrawDestroyMark(canvas, centers[i], dy);
            else if (diagram.ShowFootBoxes)
                DrawHead(canvas, pt, centers[i], botBoxY, widths[i], headHs[i]);
        }

        // 5. fragment frames
        foreach (var f in frags.Where(f => f.Kind != FragmentKind.Rect))
            DrawFragment(canvas, f, centers, widths, n);

        // 6. activation bars
        foreach (var (id, top, bottom, depth) in bars)
        {
            var bar = new Rectangle { Width = ActW, Height = Math.Max(6, bottom - top), Fill = ActBg, Stroke = Line, StrokeThickness = 1 };
            Canvas.SetLeft(bar, Cx(id) - ActW / 2 + depth * 6);
            Canvas.SetTop(bar, top);
            canvas.Children.Add(bar);
        }

        // 7. messages + notes (timeline order)
        foreach (var d in draw)
        {
            switch (d)
            {
                case MsgPlace mp when mp.Self: DrawSelfMessage(canvas, mp); break;
                case MsgPlace mp:              DrawMessage(canvas, mp); break;
                case NotePlace np:             DrawNote(canvas, np); break;
            }
        }

        // 8. title
        if (hasTitle)
        {
            var tb = new TextBlock { Text = diagram.Title, Foreground = Title, FontFamily = BodyFont, FontSize = 14, FontWeight = FontWeights.SemiBold };
            Canvas.SetLeft(tb, Outer); Canvas.SetTop(tb, Outer - 2);
            canvas.Children.Add(tb);
        }

        return new Border
        {
            Background = Bg, BorderBrush = Border, BorderThickness = new Thickness(1),
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

    private sealed record MsgPlace(SequenceMessage M, double FromX, double ToX, double HeadX, double ArrowY, bool Self);
    private sealed record NotePlace(SequenceNote N, double Top, double H, double Left, double Right);

    // ── Activation bars ────────────────────────────────────────────────────────

    private static List<(string id, double top, double bottom, int depth)> ComputeActivationBars(
        SequenceDiagram diagram, Dictionary<SequenceItem, double> yOf, double lifeBot)
    {
        var bars   = new List<(string, double, double, int)>();
        var stacks = new Dictionary<string, Stack<(double y, int depth)>>(StringComparer.Ordinal);
        Stack<(double, int)> St(string id) => stacks.TryGetValue(id, out var s) ? s : (stacks[id] = new());
        void Push(string id, double y) { var s = St(id); s.Push((y, s.Count)); }
        void Pop(string id, double y)  { var s = St(id); if (s.Count > 0) { var (sy, d) = s.Pop(); bars.Add((id, sy, y, d)); } }

        foreach (var item in diagram.Items)
        {
            switch (item)
            {
                case SequenceMessage m when yOf.TryGetValue(m, out double my):
                    if (m.ActivateTarget)   Push(m.ToId,  my);
                    if (m.DeactivateSource) Pop(m.FromId, my);
                    break;
                case SequenceActivation a when yOf.TryGetValue(a, out double ay):
                    if (a.Activate) Push(a.ParticipantId, ay); else Pop(a.ParticipantId, ay);
                    break;
            }
        }
        foreach (var (id, s) in stacks)
            while (s.Count > 0) { var (sy, d) = s.Pop(); bars.Add((id, sy, lifeBot, d)); }
        return bars;
    }

    // ── Participant heads ──────────────────────────────────────────────────────

    /// <summary>
    /// The footprint one lifeline's head needs. The whole timeline below is laid out from these two
    /// numbers and nothing else, which is why a C4 element card can stand in for a box or an actor
    /// without the rest of the renderer knowing: measure differently, draw differently, lay out the
    /// same.
    /// </summary>
    private static (double w, double h) MeasureHead(SequenceParticipant p)
    {
        if (p.Card is C4ElementInfo card)
        {
            var (cw, ch) = C4ElementMetrics.Measure(p.Label, card);
            return (Math.Clamp(cw, BoxMinW, C4ElementMetrics.MaxW), Math.Max(BoxH, ch));
        }

        var (tw, lines) = DiagramText.MeasureBlock(p.Label, FontSize);
        double glyphAllow = p.Kind is ParticipantKind.Participant or ParticipantKind.Actor ? 0 : 24;
        return (
            Math.Clamp(tw + BoxPadX * 2 + glyphAllow, BoxMinW, BoxMaxW),
            p.Kind == ParticipantKind.Actor ? ActorH : Math.Max(BoxH, lines * LineH + 14));
    }

    private static void DrawHead(Canvas canvas, SequenceParticipant p, double cx, double topY, double w, double headH)
    {
        if (p.Card is C4ElementInfo card)
        {
            canvas.Children.Add(C4ElementPainter.Build(p.Label, card, w, headH, C4).Place(cx - w / 2, topY));
            return;
        }

        if (p.Kind == ParticipantKind.Actor) { DrawActor(canvas, p.Label, cx, topY, headH); return; }

        double boxH = Math.Min(headH, Math.Max(BoxH, p.Label.Split('\n').Length * LineH + 14));
        double boxTop = topY + (headH - boxH);
        canvas.Children.Add(new Rectangle { Width = w, Height = boxH, RadiusX = 5, RadiusY = 5, Fill = BoxBg, Stroke = BoxBorder, StrokeThickness = 1.5 }.Place(cx - w / 2, boxTop));

        double glyphW = p.Kind == ParticipantKind.Participant ? 0 : DrawTypeGlyph(canvas, p.Kind, cx - w / 2 + 9, boxTop + boxH / 2);

        var (_, lines) = DiagramText.MeasureBlock(p.Label, FontSize);
        var tb = new TextBlock
        {
            Text = p.Label, Foreground = Text, FontFamily = BodyFont, FontSize = FontSize,
            Width = w - 12 - glyphW, TextAlignment = TextAlignment.Center, TextWrapping = TextWrapping.NoWrap,
        };
        Canvas.SetLeft(tb, cx - (w - 12 - glyphW) / 2 + glyphW / 2);
        Canvas.SetTop(tb, boxTop + (boxH - lines * LineH) / 2);
        canvas.Children.Add(tb);
    }

    private static void DrawActor(Canvas canvas, string label, double cx, double topY, double headH)
    {
        double figTop = topY + 2, r = 6, bodyTop = figTop + 2 * r, bodyBot = bodyTop + 12;
        void Ln(double x1, double y1, double x2, double y2) =>
            canvas.Children.Add(new Line { X1 = x1, Y1 = y1, X2 = x2, Y2 = y2, Stroke = BoxBorder, StrokeThickness = 1.5 });

        canvas.Children.Add(new Ellipse { Width = r * 2, Height = r * 2, Stroke = BoxBorder, StrokeThickness = 1.5, Fill = Bg }.Place(cx - r, figTop));
        Ln(cx, bodyTop, cx, bodyBot);
        Ln(cx - 8, bodyTop + 4, cx + 8, bodyTop + 4);
        Ln(cx, bodyBot, cx - 7, bodyBot + 9);
        Ln(cx, bodyBot, cx + 7, bodyBot + 9);

        var tb = new TextBlock { Text = label, Foreground = Text, FontFamily = BodyFont, FontSize = FontSize, TextAlignment = TextAlignment.Center, Width = 130 };
        Canvas.SetLeft(tb, cx - 65); Canvas.SetTop(tb, topY + headH - LineH - 1);
        canvas.Children.Add(tb);
    }

    /// <summary>Draws a UML role glyph (ICONIX-style) at the left of a typed participant's box; returns the width it uses.</summary>
    private static double DrawTypeGlyph(Canvas canvas, ParticipantKind kind, double x, double cy)
    {
        Brush s = BoxBorder; const double R = 6;
        Ellipse Circle(double ox, double oy, double w, double h) { var e = new Ellipse { Width = w, Height = h, Stroke = s, StrokeThickness = 1.4, Fill = Brushes.Transparent }; e.Place(ox, oy); canvas.Children.Add(e); return e; }
        void Ln(double x1, double y1, double x2, double y2) => canvas.Children.Add(new Line { X1 = x1, Y1 = y1, X2 = x2, Y2 = y2, Stroke = s, StrokeThickness = 1.4 });

        switch (kind)
        {
            case ParticipantKind.Boundary:                              // |—○
                Ln(x, cy - 7, x, cy + 7);
                Ln(x, cy, x + 5, cy);
                Circle(x + 5, cy - R, R * 2, R * 2);
                return 24;
            case ParticipantKind.Control:                              // ○ with an arrow tick at top
                Circle(x, cy - R, R * 2, R * 2);
                canvas.Children.Add(new Polyline { Points = new PointCollection([new(x + 2, cy - R - 3), new(x + 5, cy - R + 0.5), new(x + 8, cy - R - 3)]), Stroke = s, StrokeThickness = 1.4 });
                return 20;
            case ParticipantKind.Entity:                               // ○ above a line
                Circle(x, cy - 8, R * 2, R * 2);
                Ln(x - 1, cy + 5, x + 2 * R + 1, cy + 5);
                return 20;
            case ParticipantKind.Database:                            // cylinder
                Circle(x, cy - 7, 13, 5);
                Ln(x, cy - 5, x, cy + 4); Ln(x + 13, cy - 5, x + 13, cy + 4);
                Circle(x, cy + 2, 13, 5);
                return 21;
            case ParticipantKind.Queue:                              // horizontal pill
                canvas.Children.Add(new Rectangle { Width = 17, Height = 11, RadiusX = 5.5, RadiusY = 5.5, Stroke = s, StrokeThickness = 1.4, Fill = Brushes.Transparent }.Place(x, cy - 5.5));
                return 21;
            case ParticipantKind.Collections:                        // two stacked rectangles
                canvas.Children.Add(new Rectangle { Width = 10, Height = 10, Stroke = s, StrokeThickness = 1.3, Fill = Brushes.Transparent }.Place(x + 3, cy - 6));
                canvas.Children.Add(new Rectangle { Width = 10, Height = 10, Stroke = s, StrokeThickness = 1.3, Fill = Bg }.Place(x, cy - 2));
                return 19;
            default: return 0;
        }
    }

    // ── Fragments & boxes ──────────────────────────────────────────────────────

    private static (double l, double r) FragSpan(FragPlace f, double[] centers, double[] widths, int n)
    {
        if (f.MinX <= f.MaxX) return (f.MinX - FragPad, f.MaxX + FragPad);
        return (centers[0] - widths[0] / 2 - 10, centers[n - 1] + widths[n - 1] / 2 + 10);   // empty fragment → full width
    }

    private static void DrawFragment(Canvas canvas, FragPlace f, double[] centers, double[] widths, int n)
    {
        var (l, r) = FragSpan(f, centers, widths, n);
        canvas.Children.Add(new Rectangle { Width = Math.Max(40, r - l), Height = Math.Max(20, f.Bottom - f.Top), Stroke = FrameStroke, StrokeThickness = 1, Fill = Brushes.Transparent }.Place(l, f.Top));

        string kind = f.Kind.ToString().ToLowerInvariant();
        double tabW = DiagramText.Measure(kind, 10) + 12;
        canvas.Children.Add(new Polygon
        {
            Fill = FrameTabBg, Stroke = FrameStroke, StrokeThickness = 1,
            Points = new PointCollection { new(l, f.Top), new(l + tabW, f.Top), new(l + tabW - 6, f.Top + 11), new(l, f.Top + 11) },
        });
        canvas.Children.Add(new TextBlock { Text = kind, Foreground = Muted, FontFamily = BodyFont, FontSize = 10, FontWeight = FontWeights.SemiBold }.Place(l + 5, f.Top + 0.5));

        if (!string.IsNullOrWhiteSpace(f.Label))
        {
            double w = DiagramText.Measure($"[{f.Label}]", 10);
            double lx = Math.Max((l + r) / 2 - w / 2, l + tabW + 4);
            canvas.Children.Add(new TextBlock { Text = $"[{f.Label}]", Foreground = Muted, FontFamily = BodyFont, FontSize = 10 }.Place(lx, f.Top + 1));
        }

        foreach (var (sy, label) in f.Sections)
        {
            canvas.Children.Add(new Line { X1 = l, Y1 = sy, X2 = r, Y2 = sy, Stroke = FrameStroke, StrokeThickness = 1, StrokeDashArray = new DoubleCollection([4, 3]) });
            if (!string.IsNullOrWhiteSpace(label))
            {
                double w = DiagramText.Measure($"[{label}]", 10);
                canvas.Children.Add(new TextBlock { Text = $"[{label}]", Foreground = Muted, FontFamily = BodyFont, FontSize = 10, Background = Bg, Padding = new Thickness(3, 0, 3, 0) }.Place((l + r) / 2 - w / 2, sy + 1));
            }
        }
    }

    private static void DrawRect(Canvas canvas, FragPlace f, double[] centers, double[] widths, int n)
    {
        var (l, r) = FragSpan(f, centers, widths, n);
        canvas.Children.Add(new Rectangle { Width = Math.Max(40, r - l), Height = Math.Max(20, f.Bottom - f.Top), Fill = DiagramBrushes.Tint(DiagramBrushes.ParseCss(f.Color) ?? AccentColor, 0x26) }.Place(l, f.Top));
    }

    private static void DrawBoxGroup(Canvas canvas, SequenceBox box, double[] centers, double[] widths, Dictionary<string, int> index, double top, double bottom)
    {
        var ids = box.ParticipantIds.Where(index.ContainsKey).Select(id => index[id]).ToList();
        if (ids.Count == 0) return;
        int lo = ids.Min(), hi = ids.Max();
        double l = centers[lo] - widths[lo] / 2 - 10, r = centers[hi] + widths[hi] / 2 + 10;

        canvas.Children.Add(new Rectangle { Width = Math.Max(40, r - l), Height = Math.Max(20, bottom - top), Fill = DiagramBrushes.Tint(DiagramBrushes.ParseCss(box.Color) ?? AccentColor, 0x1A), Stroke = DiagramBrushes.Tint(AccentColor, 0x55), StrokeThickness = 1, RadiusX = 6, RadiusY = 6 }.Place(l, top));
        if (!string.IsNullOrWhiteSpace(box.Label))
        {
            double w = DiagramText.Measure(box.Label, 11);
            canvas.Children.Add(new TextBlock { Text = box.Label, Foreground = Muted, FontFamily = BodyFont, FontSize = 11, FontStyle = FontStyles.Italic }.Place((l + r) / 2 - w / 2, top + 3));
        }
    }

    // ── Messages ──────────────────────────────────────────────────────────────

    /// <summary>A message's own line colour, or the theme's when it has none.</summary>
    private static Brush InkOf(SequenceMessage m) =>
        DiagramBrushes.ParseCss(m.LineColor) is System.Windows.Media.Color c ? DiagramBrushes.Frozen(c) : Line;

    private static void DrawMessage(Canvas canvas, MsgPlace mp)
    {
        var m = mp.M;
        var ink = InkOf(m);
        bool right = mp.HeadX >= mp.FromX;
        double tipX = mp.HeadX + (right ? -1 : 1) * 0.5;
        double startX = mp.FromX + (m.Bidirectional ? (right ? ArrowLen : -ArrowLen) : 0);

        var seg = new Line { X1 = startX, Y1 = mp.ArrowY, X2 = tipX, Y2 = mp.ArrowY, Stroke = ink, StrokeThickness = 1.4, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round };
        if (m.Line == SequenceLineStyle.Dashed) seg.StrokeDashArray = new DoubleCollection([5, 3]);
        canvas.Children.Add(seg);

        DrawArrowHead(canvas, mp.HeadX, mp.ArrowY, right, m.Head, ink);
        if (m.Bidirectional) DrawArrowHead(canvas, mp.FromX, mp.ArrowY, !right, m.Head, ink);
        if (m.DotSource) DrawDot(canvas, mp.FromX, mp.ArrowY, ink);
        if (m.DotTarget) DrawDot(canvas, mp.HeadX, mp.ArrowY, ink);
        if (m.Number is int num) DrawNumberBullet(canvas, mp.FromX, mp.ArrowY, num);

        if (m.Text.Length > 0)
            AddLabel(canvas, m.Text, m.Technology, m.TextColor, (mp.FromX + mp.HeadX) / 2, mp.ArrowY - 4, center: true);
    }

    private static void DrawSelfMessage(Canvas canvas, MsgPlace mp)
    {
        var m = mp.M; double x = mp.FromX, y = mp.ArrowY;
        var ink = InkOf(m);
        var fig = new PathFigure { StartPoint = new Point(x, y), IsFilled = false };
        fig.Segments.Add(new LineSegment(new Point(x + LoopW, y), true));
        fig.Segments.Add(new LineSegment(new Point(x + LoopW, y + LoopH), true));
        fig.Segments.Add(new LineSegment(new Point(x, y + LoopH), true));
        var path = new Path { Data = new PathGeometry([fig]), Stroke = ink, StrokeThickness = 1.4, StrokeLineJoin = PenLineJoin.Round };
        if (m.Line == SequenceLineStyle.Dashed) path.StrokeDashArray = new DoubleCollection([5, 3]);
        canvas.Children.Add(path);
        DrawArrowHead(canvas, x, y + LoopH, right: false, m.Head, ink);
        if (m.Number is int num) DrawNumberBullet(canvas, x, y, num);

        if (m.Text.Length > 0 || HasTech(m)) AddLabel(canvas, m.Text, m.Technology, m.TextColor, x + LoopW + 6, y - 1, center: false);
    }

    private static void DrawArrowHead(Canvas canvas, double tipX, double tipY, bool right, SequenceArrowHead head, Brush ink)
    {
        if (head == SequenceArrowHead.None) return;
        double dx = right ? -ArrowLen : ArrowLen;
        var tip = new Point(tipX, tipY); var top = new Point(tipX + dx, tipY - 4.5); var bot = new Point(tipX + dx, tipY + 4.5);
        switch (head)
        {
            case SequenceArrowHead.Filled:
                canvas.Children.Add(new Polygon { Points = new PointCollection([tip, top, bot]), Fill = ink, Stroke = ink, StrokeThickness = 0.5 });
                break;
            case SequenceArrowHead.Open:
                canvas.Children.Add(new Polyline { Points = new PointCollection([top, tip, bot]), Stroke = ink, StrokeThickness = 1.4, StrokeLineJoin = PenLineJoin.Round });
                break;
            case SequenceArrowHead.Cross:
                const double cr = 4.5;
                canvas.Children.Add(new Line { X1 = tipX - cr, Y1 = tipY - cr, X2 = tipX + cr, Y2 = tipY + cr, Stroke = ink, StrokeThickness = 1.4 });
                canvas.Children.Add(new Line { X1 = tipX - cr, Y1 = tipY + cr, X2 = tipX + cr, Y2 = tipY - cr, Stroke = ink, StrokeThickness = 1.4 });
                break;
        }
    }

    private static void DrawDot(Canvas canvas, double x, double y, Brush ink)
    {
        const double r = 3.6;
        canvas.Children.Add(new Ellipse { Width = r * 2, Height = r * 2, Fill = ink }.Place(x - r, y - r));
    }

    private static void DrawNumberBullet(Canvas canvas, double x, double y, int number)
    {
        const double r = 8.5;
        canvas.Children.Add(new Ellipse { Width = r * 2, Height = r * 2, Fill = Line }.Place(x - r, y - r));
        double w = DiagramText.Measure(number.ToString(), 9);
        canvas.Children.Add(new TextBlock { Text = number.ToString(), Foreground = OnAccent, FontFamily = BodyFont, FontSize = 9, FontWeight = FontWeights.SemiBold }.Place(x - w / 2, y - 7.5));
    }

    private static void DrawNote(Canvas canvas, NotePlace np)
    {
        double w = np.Right - np.Left;
        canvas.Children.Add(new Rectangle { Width = Math.Max(20, w), Height = np.H, Fill = NoteBg, Stroke = FrameStroke, StrokeThickness = 1, RadiusX = 2, RadiusY = 2 }.Place(np.Left, np.Top));
        canvas.Children.Add(new TextBlock
        {
            Text = np.N.Text, Foreground = Text, FontFamily = BodyFont, FontSize = MsgFontSize,
            Width = Math.Max(20, w - 2 * NotePad), TextAlignment = TextAlignment.Center, TextWrapping = TextWrapping.NoWrap,
        }.Place(np.Left + NotePad, np.Top + NotePad));
    }

    private static void DrawDestroyMark(Canvas canvas, double cx, double y)
    {
        const double r = 6;
        canvas.Children.Add(new Line { X1 = cx - r, Y1 = y - r, X2 = cx + r, Y2 = y + r, Stroke = Line, StrokeThickness = 2 });
        canvas.Children.Add(new Line { X1 = cx - r, Y1 = y + r, X2 = cx + r, Y2 = y - r, Stroke = Line, StrokeThickness = 2 });
    }

    /// <summary>Rendered lines a message's label occupies — its text plus the technology line, if any.</summary>
    private static int LabelLines(SequenceMessage m) =>
        (m.Text.Length == 0 ? 0 : DiagramText.LineCount(m.Text)) + (HasTech(m) ? 1 : 0);

    private static bool HasTech(SequenceMessage m) => !string.IsNullOrWhiteSpace(m.Technology);

    /// <summary>
    /// Draws a message label so its block sits directly above <paramref name="bottomY"/>. With no
    /// technology this is a single TextBlock at exactly the position it always had; a C4 relationship
    /// adds a smaller muted <c>[HTTPS]</c> line beneath it.
    /// </summary>
    private static void AddLabel(Canvas canvas, string text, string? technology, string? textColor, double x, double bottomY, bool center)
    {
        var (w, lines) = DiagramText.MeasureBlock(text, MsgFontSize);
        bool hasTech = !string.IsNullOrWhiteSpace(technology);
        Brush ink = DiagramBrushes.ParseCss(textColor) is System.Windows.Media.Color tc ? DiagramBrushes.Frozen(tc) : Text;
        int rows = lines + (hasTech ? 1 : 0);

        var tb = new TextBlock
        {
            Text = text, Foreground = ink, FontFamily = BodyFont, FontSize = MsgFontSize,
            Background = Bg, Padding = new Thickness(3, 0, 3, 0), TextAlignment = center ? TextAlignment.Center : TextAlignment.Left,
        };
        Canvas.SetLeft(tb, center ? x - (w + 6) / 2 : x);
        Canvas.SetTop(tb, bottomY - rows * LineH);
        canvas.Children.Add(tb);

        if (!hasTech) return;

        string tech = $"[{technology!.Trim()}]";
        double techW = DiagramText.Measure(tech, MsgFontSize - 1);
        var tt = new TextBlock
        {
            Text = tech, Foreground = Muted, FontFamily = BodyFont, FontSize = MsgFontSize - 1,
            Background = Bg, Padding = new Thickness(3, 0, 3, 0), TextAlignment = center ? TextAlignment.Center : TextAlignment.Left,
        };
        Canvas.SetLeft(tt, center ? x - (techW + 6) / 2 : x);
        Canvas.SetTop(tt, bottomY - LineH);
        canvas.Children.Add(tt);
    }

}
