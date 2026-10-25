using Nexaflow.Markdown.Mermaid;

namespace Nexaflow.Markdown.Layout.Mermaid;

/// <summary>
/// The shapes Mermaid names only in <c>@{ shape: … }</c>: the flowchart symbols each drawn as its own outline, what it draws
/// inside that outline, and how it is painted.
/// </summary>
internal static partial class PortableDiagramShapes
{
    /// <summary>How far in a lined shape's second line is, down the left of a lined rectangle or a lined document.</summary>
    private const double Lining = 8;

    /// <summary>How far in a window pane's lines are.</summary>
    private const double Pane = 5;

    /// <summary>How far each sheet of a stack stands up and to the right of the one in front of it.</summary>
    private const double Stack = 5;

    /// <summary>How deep a window's bar across its top is — a browser's, a console's.</summary>
    private const double Bar = 16;

    /// <summary>How wide a curly brace is.</summary>
    private const double Curl = 10;

    /// <summary>How far under a lined cylinder's lid its second rim is.</summary>
    private const double Ring = 6;

    /// <summary>How big a person's head is, at most.</summary>
    private const double Head = 26;

    /// <summary>
    /// Whether a shape holds its words. Mermaid draws its small markers — a start, a stop, a junction, a fork, a collate, a
    /// communication link, a summary — without them, at a size of their own, and so does this.
    /// </summary>
    public static bool Worded(DiagramShape shape) => Fixed(shape) is null;

    /// <summary>The size a shape drawn without words always is, or null for one made to the size of its words.</summary>
    private static LayoutSize? Fixed(DiagramShape shape) => shape switch
    {
        DiagramShape.SmallCircle or DiagramShape.FramedCircle or DiagramShape.FilledCircle => new LayoutSize(14, 14),
        DiagramShape.CrossedCircle => new LayoutSize(36, 36),
        DiagramShape.Hourglass => new LayoutSize(26, 26),
        DiagramShape.Bolt => new LayoutSize(24, 48),
        DiagramShape.Fork => new LayoutSize(60, 8),
        _ => null,
    };

    /// <summary>Whether a shape's outline is an ellipse, which a line meets wherever it reaches it.</summary>
    private static bool Round(DiagramShape shape) =>
        shape is DiagramShape.Circle or DiagramShape.DoubleCircle or DiagramShape.SmallCircle or DiagramShape.FramedCircle
            or DiagramShape.FilledCircle or DiagramShape.CrossedCircle;

    /// <summary>
    /// What a shape is filled with: its ink for the solid ones — a junction, a fork — nothing for a brace, which only marks
    /// the words beside it, and the fill it was given for everything else.
    /// </summary>
    private static LayoutInk? Filled(DiagramShape shape, LayoutInk? fill, PortableDiagramStroke? stroke) => shape switch
    {
        _ when DrawnSolid(shape) => stroke?.Ink ?? fill,
        DiagramShape.Brace or DiagramShape.BraceRight or DiagramShape.Braces => null,
        _ => fill,
    };

    /// <summary>
    /// Whether a shape is drawn solid — filled in the ink its outline is drawn in rather than in whatever fills a node. A junction
    /// and a fork are marks rather than boxes: Mermaid draws them as a dot and a bar, and neither has anything written on it, so
    /// the only thing telling one from a hollow mark the same size is that it is filled in.
    /// </summary>
    public static bool DrawnSolid(DiagramShape shape) => shape is DiagramShape.FilledCircle or DiagramShape.Fork;

    /// <summary>Whether what a shape draws inside its outline is filled in its ink — a framed circle's dot.</summary>
    private static bool Dotted(DiagramShape shape) => shape == DiagramShape.FramedCircle;

    // ── Outlines ────────────────────────────────────────────────────────────

    /// <summary>A rectangle rounded off at its right end.</summary>
    private static LayoutShape Delay(ILayoutMedium medium, LayoutRect r)
    {
        var round = Math.Min(r.Height / 2, r.Width);
        var pen = medium.Outline();
        pen.Begin(r.TopLeft, filled: true, closed: true);
        pen.LineTo(new LayoutPoint(r.Right - round, r.Top), true, false);
        pen.ArcTo(new LayoutPoint(r.Right - round, r.Bottom), round, r.Height / 2, 0, false, true, true, true);
        pen.LineTo(r.BottomLeft, true, false);

        return pen.Close();
    }

    /// <summary>Pointed at its left end and rounded at its right, as a display is drawn.</summary>
    private static LayoutShape CurvedTrapezoid(ILayoutMedium medium, LayoutRect r)
    {
        var k = Pointed(r);
        var pen = medium.Outline();
        pen.Begin(new LayoutPoint(r.Left, r.Top + (r.Height / 2)), filled: true, closed: true);
        pen.LineTo(new LayoutPoint(r.Left + k, r.Top), true, false);
        pen.LineTo(new LayoutPoint(r.Right - k, r.Top), true, false);
        pen.ArcTo(new LayoutPoint(r.Right - k, r.Bottom), k, r.Height / 2, 0, false, true, true, true);
        pen.LineTo(new LayoutPoint(r.Left + k, r.Bottom), true, false);

        return pen.Close();
    }

    /// <summary>Both ends bowing to the left, as stored data is drawn: the near end out, the far end in.</summary>
    private static LayoutShape BowTie(ILayoutMedium medium, LayoutRect r)
    {
        var d = Bowed(r);
        var reach = new LayoutSize(d, r.Height / 2);
        var pen = medium.Outline();
        pen.Begin(new LayoutPoint(r.Left + d, r.Top), filled: true, closed: true);
        pen.LineTo(r.TopRight, true, false);
        pen.ArcTo(r.BottomRight, reach.Width, reach.Height, 0, false, false, true, true);
        pen.LineTo(new LayoutPoint(r.Left + d, r.Bottom), true, false);
        pen.ArcTo(new LayoutPoint(r.Left + d, r.Top), reach.Width, reach.Height, 0, false, true, true, true);

        return pen.Close();
    }

    /// <summary>A band whose top and foot wave together, as paper tape is drawn.</summary>
    private static LayoutShape Flag(ILayoutMedium medium, LayoutRect r)
    {
        // Each wave is a cubic whose handles are a height k either side of its ends, which bows out 0.2887 k at most: the top waves
        // from the top of the bounds down twice that, and the foot the same way up from the bottom.
        var wave = Wave(r);
        var handle = wave / 0.2887;
        var (top, foot) = (r.Top + wave, r.Bottom - wave);

        var pen = medium.Outline();
        pen.Begin(new LayoutPoint(r.Left, top), filled: true, closed: true);
        pen.BezierTo(new LayoutPoint(r.Left + (r.Width / 3), top - handle), new LayoutPoint(r.Right - (r.Width / 3), top + handle),
                     new LayoutPoint(r.Right, top), true, true);
        pen.LineTo(new LayoutPoint(r.Right, foot), true, false);
        pen.BezierTo(new LayoutPoint(r.Right - (r.Width / 3), foot + handle), new LayoutPoint(r.Left + (r.Width / 3), foot - handle),
                     new LayoutPoint(r.Left, foot), true, true);

        return pen.Close();
    }

    /// <summary>A cylinder lying on its side, its near end towards the right.</summary>
    private static LayoutShape HorizontalCylinder(ILayoutMedium medium, LayoutRect r)
    {
        var c = Cap(r);
        var reach = new LayoutSize(c, r.Height / 2);
        var pen = medium.Outline();
        pen.Begin(new LayoutPoint(r.Left + c, r.Top), filled: true, closed: true);
        pen.LineTo(new LayoutPoint(r.Right - c, r.Top), true, false);
        pen.ArcTo(new LayoutPoint(r.Right - c, r.Bottom), reach.Width, reach.Height, 0, false, true, true, true);
        pen.LineTo(new LayoutPoint(r.Left + c, r.Bottom), true, false);
        pen.ArcTo(new LayoutPoint(r.Left + c, r.Top), reach.Width, reach.Height, 0, false, true, true, true);

        return pen.Close();
    }

    /// <summary>The far edge of a lying cylinder's near end, which its outline does not draw.</summary>
    private static LayoutShape Face(ILayoutMedium medium, LayoutRect r)
    {
        var c = Cap(r);
        var pen = medium.Outline();
        pen.Begin(new LayoutPoint(r.Right - c, r.Top), filled: false, closed: false);
        pen.ArcTo(new LayoutPoint(r.Right - c, r.Bottom), c, r.Height / 2, 0, false, false, true, true);

        return pen.Close();
    }

    /// <summary>A band filled right across and ruled only along its top and its foot, as a data flow diagram's store is drawn.</summary>
    private static LayoutShape DataStore(ILayoutMedium medium, LayoutRect r)
    {
        var pen = medium.Outline();
        pen.Begin(r.TopLeft, filled: true, closed: true);
        pen.LineTo(r.TopRight, true, false);
        pen.LineTo(r.BottomRight, false, false);
        pen.LineTo(r.BottomLeft, true, false);
        pen.LineTo(r.TopLeft, false, false);

        return pen.Close();
    }

    /// <summary>A pail: the far rim of its open top, and its sides narrowing to its foot.</summary>
    private static LayoutShape Bucket(ILayoutMedium medium, LayoutRect r)
    {
        var (lid, lip) = (Lid(r), Lip(r));
        var pen = medium.Outline();
        pen.Begin(new LayoutPoint(r.Left, r.Top + lid), filled: true, closed: true);
        pen.ArcTo(new LayoutPoint(r.Right, r.Top + lid), r.Width / 2, lid, 0, false, true, true, true);
        pen.LineTo(new LayoutPoint(r.Right - lip, r.Bottom), true, false);
        pen.LineTo(new LayoutPoint(r.Left + lip, r.Bottom), true, false);

        return pen.Close();
    }

    /// <summary>
    /// A curly brace on either side of the words or both. Only the braces are drawn; the rest of the bounds is a figure filled and
    /// never stroked, so the shape stands in all of it, as every other shape does — and a brace is given no fill to paint it with.
    /// </summary>
    private static LayoutShape Braced(ILayoutMedium medium, DiagramShape which, LayoutRect r)
    {
        var pen = medium.Outline();
        pen.Begin(r.TopLeft, filled: true, closed: true);
        pen.PolyLineTo([r.TopRight, r.BottomRight, r.BottomLeft, r.TopLeft], stroked: false, smooth: false);

        if (which is DiagramShape.Brace or DiagramShape.Braces) Curly(pen, r, r.Left, 1);
        if (which is DiagramShape.BraceRight or DiagramShape.Braces) Curly(pen, r, r.Right, -1);

        return pen.Close();
    }

    /// <summary>One curly brace down the height of <paramref name="r"/>, its point at <paramref name="tip"/> and its arms reaching <paramref name="toward"/> the words.</summary>
    private static void Curly(ShapeBuilder pen, LayoutRect r, double tip, int toward)
    {
        var bend = Math.Min(Curl / 2, r.Height / 4);
        var (arm, spine, middle) = (tip + (toward * Curl), tip + (toward * Curl / 2), r.Top + (r.Height / 2));

        pen.Begin(new LayoutPoint(arm, r.Top), filled: false, closed: false);
        pen.QuadraticTo(new LayoutPoint(spine, r.Top), new LayoutPoint(spine, r.Top + bend), true, true);
        pen.LineTo(new LayoutPoint(spine, middle - bend), true, true);
        pen.QuadraticTo(new LayoutPoint(spine, middle), new LayoutPoint(tip, middle), true, true);
        pen.QuadraticTo(new LayoutPoint(spine, middle), new LayoutPoint(spine, middle + bend), true, true);
        pen.LineTo(new LayoutPoint(spine, r.Bottom - bend), true, true);
        pen.QuadraticTo(new LayoutPoint(spine, r.Bottom), new LayoutPoint(arm, r.Bottom), true, true);
    }

    /// <summary>A head over a rounded body, the words in the body.</summary>
    private static LayoutShape Person(ILayoutMedium medium, LayoutRect r)
    {
        var body = Body(r);
        var round = Rounding(body);

        return United(medium, [medium.Ellipse(Crown(r)), medium.Rectangle(body, round, round)]);
    }

    /// <summary>Sheets stacked one behind another, the back one first: each up and to the right of the one in front of it.</summary>
    private static IReadOnlyList<LayoutRect> Stacked(LayoutRect r)
    {
        var (w, h) = (Math.Max(0, r.Width - (Stack * 2)), Math.Max(0, r.Height - (Stack * 2)));

        return [new LayoutRect(r.X + (Stack * 2), r.Y, w, h), new LayoutRect(r.X + Stack, r.Y + Stack, w, h), new LayoutRect(r.X, r.Y + (Stack * 2), w, h)];
    }

    /// <summary>
    /// The edges of a stack's sheets that show behind the one in front — each sheet's outline where nothing in front of it covers
    /// it, which stroking what is left of the sheet once those in front are taken out of it draws.
    /// </summary>
    private static LayoutShape Behind(ILayoutMedium medium, IReadOnlyList<LayoutShape> sheets)
    {
        var edges = new List<LayoutShape>();
        for (var at = 0; at < sheets.Count - 1; at++)
            edges.Add(medium.Combine(sheets[at], United(medium, sheets.Skip(at + 1)), ShapeCombine.Exclude));

        return medium.Group(edges);
    }

    /// <summary>A browser window's bar: the rule under it, three dots, and the address box.</summary>
    private static LayoutShape Browser(ILayoutMedium medium, LayoutRect r)
    {
        List<LayoutShape> chrome = [Segment(medium, r.Left, r.Top + Bar, r.Right, r.Top + Bar)];
        for (var dot = 0; dot < 3; dot++)
            chrome.Add(medium.Ellipse(new LayoutPoint(r.Left + 8 + (dot * 6), r.Top + (Bar / 2)), 1.5, 1.5));

        var address = Box(r.Left + (r.Width * 0.45), r.Top + (Bar * 0.28), (r.Width * 0.55) - 8, Bar * 0.44);
        chrome.Add(medium.Rectangle(address, 2, 2));

        return medium.Group(chrome);
    }

    /// <summary>A console's prompt in its top corner.</summary>
    private static LayoutShape Prompt(ILayoutMedium medium, LayoutRect r)
    {
        var pen = medium.Outline();
        pen.Begin(new LayoutPoint(r.Left + 8, r.Top + 5), filled: false, closed: false);
        pen.PolyLineTo([new LayoutPoint(r.Left + 12, r.Top + 8), new LayoutPoint(r.Left + 8, r.Top + 11)], stroked: true, smooth: false);

        return medium.Group([pen.Close(), Segment(medium, r.Left + 14, r.Top + 11, r.Left + 20, r.Top + 11)]);
    }

    /// <summary>A circle crossed through from corner to corner.</summary>
    private static LayoutShape Crossed(ILayoutMedium medium, LayoutRect r)
    {
        var (cx, cy) = (r.X + (r.Width / 2), r.Y + (r.Height / 2));
        var (a, b) = (r.Width / 2 * Math.Sqrt(0.5), r.Height / 2 * Math.Sqrt(0.5));

        return medium.Group([Segment(medium, cx - a, cy - b, cx + a, cy + b), Segment(medium, cx - a, cy + b, cx + a, cy - b)]);
    }

    /// <summary>A lightning bolt's corners, as fractions of its bounds.</summary>
    private static IReadOnlyList<LayoutPoint> Bolt(LayoutRect r) =>
        [.. new (double X, double Y)[] { (0.7, 0), (0, 0.58), (0.45, 0.58), (0.3, 1), (1, 0.4), (0.55, 0.4) }
            .Select(at => new LayoutPoint(r.X + (at.X * r.Width), r.Y + (at.Y * r.Height)))];

    /// <summary>A folder's corners: its tab along the top of its left end, sloping down to the rest of it.</summary>
    private static IReadOnlyList<LayoutPoint> Folder(LayoutRect r)
    {
        var (tab, wide) = (Tab(r), Math.Min(r.Width * 0.4, 48));

        return [r.TopLeft, new(r.Left + Math.Max(0, wide - tab), r.Top), new(r.Left + wide, r.Top + tab), new(r.Right, r.Top + tab), r.BottomRight, r.BottomLeft];
    }

    /// <summary>How far down a document's foot is at <paramref name="along"/> — the wave <see cref="Document"/> draws.</summary>
    private static double WaveAt(LayoutRect r, double along)
    {
        // Its handles are spaced evenly across, so how far along the curve a point is is how far across it is.
        var t = r.Width <= 0 ? 0 : Math.Clamp((r.Right - along) / r.Width, 0, 1);

        return r.Bottom - Wave(r) + (3 * Wave(r) / 0.2887 * t * (1 - t) * ((2 * t) - 1));
    }

    private static LayoutShape Segment(ILayoutMedium medium, double x1, double y1, double x2, double y2) =>
        medium.Line(new LayoutPoint(x1, y1), new LayoutPoint(x2, y2));

    /// <summary>A rectangle, none of it less than nothing across or down.</summary>
    private static LayoutRect Box(double x, double y, double w, double h) => new(x, y, Math.Max(0, w), Math.Max(0, h));

    // ── Proportions ─────────────────────────────────────────────────────────

    /// <summary>How far down a divided rectangle's line is.</summary>
    private static double Divide(LayoutRect r) => r.Height * 0.2;

    /// <summary>How far a sloped rectangle's top falls from its right end to its left.</summary>
    private static double Slope(LayoutRect r) => r.Height * 0.3;

    /// <summary>How far a display's point reaches, and how far its round end does.</summary>
    private static double Pointed(LayoutRect r) => Math.Min(r.Height / 3, r.Width / 3);

    /// <summary>How far stored data's ends bow.</summary>
    private static double Bowed(LayoutRect r) => Math.Min(r.Height / 4, r.Width / 4);

    /// <summary>How far a lying cylinder's ends reach.</summary>
    private static double Cap(LayoutRect r) => Math.Min(r.Height * 0.25, r.Width * 0.15);

    /// <summary>How far a pail's foot is in from the ends of its rim.</summary>
    private static double Lip(LayoutRect r) => r.Width * 0.12;

    /// <summary>How big a turned corner is — a tagged rectangle's, a tagged document's.</summary>
    private static double Tag(LayoutRect r) => Math.Min(10, r.Height * 0.25);

    /// <summary>How deep a folder's tab is.</summary>
    private static double Tab(LayoutRect r) => Math.Min(10, r.Height * 0.2);

    /// <summary>Where a person's head is: in the middle across, at the top.</summary>
    private static LayoutRect Crown(LayoutRect r)
    {
        var side = Math.Min(Head, Math.Min(r.Width, r.Height) / 2);
        return new LayoutRect(r.X + ((r.Width - side) / 2), r.Y, side, side);
    }

    /// <summary>Where a person's body is: under their head, which sits a little into it.</summary>
    private static LayoutRect Body(LayoutRect r)
    {
        var shoulders = Crown(r).Height * 0.8;
        return Box(r.X, r.Y + shoulders, r.Width, r.Height - shoulders);
    }

    private static double Rounding(LayoutRect body) => Math.Min(10, body.Height / 2);
}