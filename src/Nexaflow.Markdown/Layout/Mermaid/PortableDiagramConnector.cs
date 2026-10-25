using Nexaflow.Markdown.Ast;
using Nexaflow.Markdown.Mermaid;

namespace Nexaflow.Markdown.Layout.Mermaid;

/// <summary>How a line is drawn: its ink, how thick it is, and the dashes it is broken into — none, for a solid line.</summary>
/// <param name="Dashes">Dash and gap lengths in multiples of the thickness, as a pen takes them — <see cref="Dashed"/>, <see cref="Dotted"/>.</param>
internal sealed record PortableDiagramStroke(LayoutInk Ink, double Thickness = 1, double[]? Dashes = null)
{
    /// <summary>A line of dashes: Mermaid's <c>-.-></c> and a sequence diagram's reply.</summary>
    public static double[] Dashed { get; } = [4, 3];

    /// <summary>A line of dots.</summary>
    public static double[] Dotted { get; } = [1, 2];
}

/// <summary>
/// A line from one thing a diagram draws to another — a flowchart's edge, a sequence diagram's message, a class diagram's
/// relation — with what it ends in at either end.
///
/// <para>
/// <strong>A connector stands in a band round its line</strong>, <see cref="Reach"/> wide, so a press near a line means it
/// rather than whatever is behind it — a line a pixel wide is nothing anybody can press. Its heads are part of the same
/// piece, so pressing an arrowhead means the edge too. The line stops where a head starts, so a hollow head is hollow.
/// </para>
/// </summary>
internal static class PortableDiagramConnector
{
    /// <summary>How long an arrowhead is, and how wide.</summary>
    public const double HeadLength = 9;
    public const double HeadWidth = 8;

    /// <summary>How near a press has to be to a connector's line to mean it.</summary>
    public const double Reach = 8;

    /// <summary>
    /// How far what is written on a line is kept from what the line meets, so the head at that end always shows: the head
    /// itself, and enough line past it to read as a line.
    /// </summary>
    public const double Clearance = HeadLength + 6;

    /// <summary>And the air between two lots of words written one under the other over the same stretch.</summary>
    public const double Stacking = 12;

    /// <summary>The air round what is written on a connector, between its words and the line they are drawn over.</summary>
    private const double Air = 2;

    /// <summary>How many points a cubic is made of — enough that the corners between them do not read as corners.</summary>
    private const int Steps = 16;

    /// <summary>
    /// Draws a connector through <paramref name="route"/>, as a piece of <paramref name="kind"/> standing for
    /// <paramref name="part"/>: straight from point to point, or <paramref name="curved"/> through them, ending in
    /// <paramref name="start"/> at its first point and <paramref name="end"/> at its last — each head <paramref name="heads"/>
    /// times its usual size.
    /// </summary>
    public static void Draw(PortableBuilder build, string kind, ISourcePart? part, IReadOnlyList<LayoutPoint> route, PortableDiagramStroke stroke,
                            DiagramHead start = DiagramHead.None, DiagramHead end = DiagramHead.Arrow, bool curved = false, double heads = 1)
    {
        if (route.Count < 2) throw new ArgumentException("A connector runs between two points at least.", nameof(route));

        var points = route.ToList();
        var headed = new List<LayoutShape>();
        var filled = new List<LayoutShape>();

        Capped(build.Medium, points, end, stroke.Thickness, heads, lines: headed, filled);
        points.Reverse();
        Capped(build.Medium, points, start, stroke.Thickness, heads, lines: headed, filled);
        points.Reverse();

        var line = Line(build.Medium, points, curved);

        build.Open(kind, part, stops: PortableStops.None);
        build.Draw(PortableDiagramMarks.Outlined(build.Medium, line, null, stroke.Ink, stroke.Thickness, dashes: stroke.Dashes));
        var solid = build.Medium.Group(filled);
        var hollow = build.Medium.Group(headed);
        if (filled.Count > 0) build.Draw(PortableDiagramMarks.Outlined(build.Medium, solid, stroke.Ink, stroke.Ink, stroke.Thickness));
        if (headed.Count > 0) build.Draw(PortableDiagramMarks.Outlined(build.Medium, hollow, null, stroke.Ink, stroke.Thickness));

        build.Occupies(build.Medium.Widen(build.Medium.Group([line, hollow, solid]), Math.Max(Reach, stroke.Thickness)));
        build.Close();
    }

    /// <summary>
    /// Puts <paramref name="head"/> on the last end of a line, and draws none of the line past where the head begins.
    ///
    /// <para>
    /// The head points the way the line comes into it over the head's own length, not along whatever short last step the line
    /// ends on: a curve is the points it is made of, and the last of them, brought in to the edge of what the line meets, can
    /// turn a good way off the curve — a head set along it points somewhere the line is not going. Only as far back as the line
    /// runs on without turning a corner, so a square line's head still points along its last leg.
    /// </para>
    /// <para>
    /// And the points between the head's foot and its tip go. Left in, the line runs on past the foot to them and comes back
    /// to the foot — a hook where it goes into the head.
    /// </para>
    /// </summary>
    private static void Capped(ILayoutMedium medium, List<LayoutPoint> points, DiagramHead head, double thickness, double scale, List<LayoutShape> lines, List<LayoutShape> solid)
    {
        var tip = points[^1];
        var last = Direction(points[^2], tip);

        var back = points.Count - 2;
        while (back > 0 && (tip - points[back]).Length < HeadLength * scale && Direction(points[back - 1], points[back]) * last > Bent)
            back--;

        var foot = Head(medium, tip, Direction(points[back], tip), head, thickness, scale, lines, solid);
        var cut = (tip - foot).Length;

        if (cut > 1e-9)
            while (points.Count > 2 && (tip - points[^2]).Length < cut) points.RemoveAt(points.Count - 2);

        points[^1] = foot;
    }

    /// <summary>How far a line may turn and still be running on rather than turning a corner — the cosine of the angle.</summary>
    private const double Bent = 0.7;

    /// <summary>The point halfway along a route — where the words on a connector go.</summary>
    public static LayoutPoint Middle(IReadOnlyList<LayoutPoint> route)
    {
        if (route.Count == 0) return default;

        var total = 0.0;
        for (var at = 1; at < route.Count; at++) total += (route[at] - route[at - 1]).Length;

        var left = total / 2;
        for (var at = 1; at < route.Count; at++)
        {
            var leg = route[at] - route[at - 1];
            if (left <= leg.Length) return route[at - 1] + (leg * (leg.Length > 0 ? left / leg.Length : 0));
            left -= leg.Length;
        }

        return route[^1];
    }

    /// <summary>
    /// The room what is written on a line takes, set about <paramref name="at"/> — the middle of the line where nothing
    /// else is said.
    /// </summary>
    public static LayoutRect Room(IReadOnlyList<LayoutPoint> route, IReadOnlyList<PortableDiagramWords> said, LayoutPoint? at = null)
    {
        var taken = PortableDiagramWords.Taken(said);
        if (taken.Width <= 0 || taken.Height <= 0) return LayoutRect.Empty;

        var on = at ?? Middle(route);

        return LayoutRect.Inflate(new LayoutRect(on.X - (taken.Width / 2), on.Y - (taken.Height / 2), taken.Width, taken.Height), Air, Air / 2);
    }

    /// <summary>
    /// The middle of the longest straight run of a line, which is where what is written on it has the most room to stand in.
    /// A line that turns has its own middle on or near one of its corners as often as not, and a corner is where it is
    /// getting past whatever made it turn.
    /// </summary>
    public static LayoutPoint Longest(IReadOnlyList<LayoutPoint> route)
    {
        if (route.Count < 2) return Middle(route);

        var (best, at) = (-1.0, 1);

        for (var one = 1; one < route.Count; one++)
            if ((route[one] - route[one - 1]).Length > best) (best, at) = ((route[one] - route[one - 1]).Length, one);

        return new LayoutPoint((route[at - 1].X + route[at].X) / 2, (route[at - 1].Y + route[at].Y) / 2);
    }

    /// <summary>How round the corner of an outlined label is.</summary>
    private const double Rounded = 3;

    /// <summary>
    /// What is written on a connector, in the room <see cref="Room"/> gave it: the words on a patch of <paramref name="backing"/>,
    /// so the line does not run through them and a press there means the connector rather than whatever is under it.
    /// </summary>
    public static void Says(PortableBuilder build, string kind, ISourcePart? part, LayoutRect room, IReadOnlyList<PortableDiagramWords> said,
                            LayoutInk backing, string wordsKind = MermaidPiece.Words, LayoutInk? outline = null)
    {
        if (room.IsEmpty) return;

        // Rounded and outlined where a diagram asks for it: what is written on a line reads better held off the line than
        // sitting on it, and the edge is what says where the words stop and the drawing starts again.
        var patch = outline is null ? build.Medium.Rectangle(room) : build.Medium.Rectangle(room, Rounded, Rounded);

        build.Open(kind, part, stops: PortableStops.None);
        build.Draw(PortableDiagramMarks.Outlined(build.Medium, patch, backing, outline, outline is null ? 0 : 1));
        build.Occupies(patch);

        foreach (var (words, at, said_kind) in PortableDiagramWords.Placed(said, LayoutRect.Inflate(room, -Air, -Air / 2), wordsKind))
            words.Set(build, at, said_kind);

        build.Close();
    }

    /// <summary>What a link written one of Mermaid's ways draws at one of its ends.</summary>
    public static DiagramHead Headed(MermaidHead head) => head switch
    {
        MermaidHead.Arrow => DiagramHead.Arrow,
        MermaidHead.Circle => DiagramHead.Circle,
        MermaidHead.Cross => DiagramHead.Cross,
        _ => DiagramHead.None,
    };

    /// <summary>
    /// How the line of a link written one of Mermaid's ways is drawn: thicker where it is written with equals signs, dotted where it
    /// is written with dots. A link drawn as nothing at all (<see cref="MermaidLineStyle.Invisible"/>) is not drawn by its caller.
    /// </summary>
    public static PortableDiagramStroke Stroked(LayoutInk ink, MermaidLineStyle style, double thin = 1, double thick = 2.5,
                                                double[]? dashes = null) =>
        new(ink, style == MermaidLineStyle.Thick ? thick : thin,
            dashes ?? (style == MermaidLineStyle.Dotted ? PortableDiagramStroke.Dotted : null));

    /// <summary>
    /// The band a route stands in — how near a press must be to mean it (<see cref="Reach"/>) — for a shape drawn under one to
    /// leave it out of what it stands in itself.
    /// </summary>
    public static LayoutShape Band(ILayoutMedium medium, IReadOnlyList<LayoutPoint> route, double thickness = 1, bool curved = false) =>
        medium.Widen(Line(medium, route, curved), Math.Max(Reach, thickness));

    /// <summary>
    /// A join's route with its ends brought in from the middles of the cells to their edges.
    ///
    /// <para>
    /// The default cast is from the line's own end rather than the cell's middle, so a line given its own place along the
    /// edge keeps it. A diagram that hands its own <paramref name="edge"/> — because its shapes are not rectangles — says
    /// where a line meets a shape its own way, and that is taken as given; it is handed the line's own end and the way the
    /// line goes from there, which is everything there is to know about how the line arrives.
    /// </para>
    /// </summary>
    public static IReadOnlyList<LayoutPoint> Trimmed(PortableDiagramJoin join, Func<PortableDiagramCell, LayoutPoint, LayoutPoint, LayoutPoint>? edge = null)
    {
        var points = join.Route.ToList();
        if (ReferenceEquals(join.From, join.To)) return points;

        Brought(points, join.To, edge);
        points.Reverse();
        Brought(points, join.From, edge);
        points.Reverse();

        return points;
    }

    /// <summary>
    /// Brings the far end of a route in to the edge of what it meets, and drops whatever of the route was within.
    ///
    /// <para>
    /// A route that swings across a gap is made of points rather than corners, and several of them at that end are inside the
    /// shape it meets. Moving only the last of them leaves the rest drawn across the shape, which reads as a line setting off
    /// from somewhere in the middle of it.
    /// </para>
    /// </summary>
    private static void Brought(List<LayoutPoint> points, PortableDiagramCell cell, Func<PortableDiagramCell, LayoutPoint, LayoutPoint, LayoutPoint>? edge)
    {
        // The last point of the route still outside the shape — never the other end, which would leave nothing to draw.
        var outside = points.Count - 2;
        while (outside > 0 && cell.Bounds.Contains(points[outside])) outside--;

        var met = edge is null
            ? PortableDiagramShapes.Edge(DiagramShape.Rectangle, cell.Bounds, points[outside], points[^1])
            : edge(cell, points[^1], points[outside]);

        points.RemoveRange(outside + 1, points.Count - outside - 1);
        points.Add(met);
    }

    /// <summary>
    /// A cubic, as the points a line is made of: from <paramref name="from"/> to <paramref name="to"/>, leaving towards
    /// <paramref name="lead"/> and arriving from <paramref name="trail"/>.
    ///
    /// <para>
    /// This is what a line crossing a gap between two things that are not lined up is: it leaves square to the edge it
    /// leaves, swings across, and arrives square to the edge it reaches. Drawn as the two corners it would otherwise have,
    /// it is a dog-leg — and two of them out of the same shape read as one line that forks rather than as a fan.
    /// </para>
    ///
    /// <para>
    /// It is the route itself, not a way of drawing one, so everything that reads a route afterwards — where what is written
    /// on it goes, what it leaves no room under, what a shape takes out of it — sees the line that is actually drawn.
    /// </para>
    /// </summary>
    public static IReadOnlyList<LayoutPoint> Curving(LayoutPoint from, LayoutPoint lead, LayoutPoint trail, LayoutPoint to, int steps = Steps)
    {
        var along = new LayoutPoint[steps + 1];

        for (var step = 0; step <= steps; step++)
        {
            var t = step / (double)steps;
            var s = 1 - t;

            along[step] = new LayoutPoint(
                (s * s * s * from.X) + (3 * s * s * t * lead.X) + (3 * s * t * t * trail.X) + (t * t * t * to.X),
                (s * s * s * from.Y) + (3 * s * s * t * lead.Y) + (3 * s * t * t * trail.Y) + (t * t * t * to.Y));
        }

        return along;
    }

    /// <summary>
    /// What a set of lines covers, which whatever is drawn under them does not stand in: the band each runs in, and the room what
    /// is written over the middle of it takes.
    /// </summary>
    public static IReadOnlyList<LayoutShape> Covered(ILayoutMedium medium, IEnumerable<(IReadOnlyList<LayoutPoint> Along, LayoutRect Room)> routes, double thickness = 1)
    {
        var over = new List<LayoutShape>();

        foreach (var (along, room) in routes)
        {
            over.Add(Band(medium, along, thickness));
            if (!room.IsEmpty) over.Add(medium.Rectangle(room));
        }

        return over;
    }

    // ── Heads ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Draws what a connector ends in at <paramref name="tip"/>, pointing along <paramref name="along"/>, and hands back where
    /// the line itself stops — short of a head it would otherwise run through.
    /// </summary>
    private static LayoutPoint Head(ILayoutMedium medium, LayoutPoint tip, LayoutVector along, DiagramHead head, double thickness, double scale, List<LayoutShape> lines, List<LayoutShape> solid)
    {
        var across = new LayoutVector(-along.Y, along.X);
        var (length, half) = ((HeadLength + thickness) * scale, (HeadWidth + thickness) / 2 * scale);
        var back = tip - (along * length);

        switch (head)
        {
            case DiagramHead.Arrow:
                solid.Add(Polygon(medium, tip, back + (across * half), back - (across * half)));
                return back;

            case DiagramHead.Open:
                lines.Add(Polyline(medium, back + (across * half), tip, back - (across * half)));
                return tip;

            case DiagramHead.Triangle:
                lines.Add(Polygon(medium, tip, back + (across * half), back - (across * half)));
                return back;

            case DiagramHead.Diamond or DiagramHead.HollowDiamond:
                var far = tip - (along * length * 2);
                var diamond = Polygon(medium, tip, back + (across * half), far, back - (across * half));
                (head == DiagramHead.Diamond ? solid : lines).Add(diamond);
                return far;

            case DiagramHead.Circle:
                lines.Add(medium.Ellipse(tip - (along * half), half, half));
                return tip - (along * half * 2);

            case DiagramHead.Cross:
                var centre = tip - (along * half);
                lines.Add(medium.Line(centre + ((along + across) * half), centre - ((along + across) * half)));
                lines.Add(medium.Line(centre + ((along - across) * half), centre - ((along - across) * half)));
                return tip;

            case DiagramHead.CrossCircle:
                var held = tip - (along * half);
                lines.Add(medium.Ellipse(held, half, half));
                lines.Add(medium.Line(held - (along * half), held + (along * half)));
                lines.Add(medium.Line(held - (across * half), held + (across * half)));
                return tip - (along * half * 2);

            // Half a head is the half of one on the side the characters draw it: -|\ above the line, -|/ below it.
            case DiagramHead.HalfTop or DiagramHead.HalfBottom:
                var barb = head == DiagramHead.HalfTop ? -across : across;
                solid.Add(Polygon(medium, tip, back + (barb * half), back));
                return back;

            case DiagramHead.StickTop or DiagramHead.StickBottom:
                var stroke = head == DiagramHead.StickTop ? -across : across;
                lines.Add(medium.Line(tip, back + (stroke * half)));
                return tip;

            case DiagramHead.ZeroOrOne:
                Bar(medium, tip - (along * 6), across, half, lines);
                lines.Add(medium.Ellipse(tip - (along * 14), 4, 4));
                return tip;

            case DiagramHead.ExactlyOne:
                Bar(medium, tip - (along * 6), across, half, lines);
                Bar(medium, tip - (along * 11), across, half, lines);
                return tip;

            case DiagramHead.ZeroOrMore:
                Foot(medium, tip, along, across, half, lines);
                lines.Add(medium.Ellipse(tip - (along * (length + 6)), 4, 4));
                return tip;

            case DiagramHead.OneOrMore:
                Foot(medium, tip, along, across, half, lines);
                Bar(medium, tip - (along * (length + 4)), across, half, lines);
                return tip;

            default:
                return tip;
        }
    }

    private static void Bar(ILayoutMedium medium, LayoutPoint at, LayoutVector across, double half, List<LayoutShape> lines) =>
        lines.Add(medium.Line(at + (across * half), at - (across * half)));

    /// <summary>A crow's foot: three strokes spreading from a point on the line out to the tip.</summary>
    private static void Foot(ILayoutMedium medium, LayoutPoint tip, LayoutVector along, LayoutVector across, double half, List<LayoutShape> lines)
    {
        var heel = tip - (along * HeadLength);
        lines.Add(Polyline(medium, tip + (across * half), heel, tip - (across * half)));
    }

    // ── Lines ───────────────────────────────────────────────────────────────

    /// <summary>
    /// The line a route is drawn as: straight from point to point, or curved through every one of them.
    ///
    /// <para>
    /// Curved, it is a Catmull-Rom spline written as cubic Béziers, so the line passes through the route rather than near it, with each
    /// handle held to a third of the run it belongs to — the tangent at a corner points the way the route turns, and left to its own
    /// length it would carry the line past the corner and back.
    /// </para>
    /// <para>
    /// <strong>A stub at either end is straight.</strong> A line turns in the air between two ranks, so the run from that turn to what
    /// it joins is about as long as the head it carries; a corner's tangent across a run that short bows it right where the head is, and
    /// the hook shows. So a run no longer than twice a head goes straight into what it meets, and every longer run keeps its curve.
    /// </para>
    /// </summary>
    private static LayoutShape Line(ILayoutMedium medium, IReadOnlyList<LayoutPoint> points, bool curved)
    {
        var pen = medium.Outline();
        pen.Begin(points[0], filled: false, closed: false);

        if (!curved || points.Count < 3)
        {
            pen.PolyLineTo([.. points.Skip(1)], stroked: true, smooth: true);
        }
        else
        {
            for (var at = 0; at < points.Count - 1; at++)
            {
                var (before, from, to, after) = (points[Math.Max(0, at - 1)], points[at], points[at + 1], points[Math.Min(points.Count - 1, at + 2)]);
                var run = to - from;
                var most = run.Length / 3;
                var stub = run.Length <= HeadLength * 2;

                var lead = stub && at == points.Count - 2 ? run / 3 : Held((to - before) / 6, most);
                var trail = stub && at == 0 ? run / 3 : Held((after - from) / 6, most);

                pen.BezierTo(from + lead, to - trail, to, stroked: true, smooth: true);
            }
        }

        return pen.Close();
    }

    /// <summary>A handle held to how far it may reach, keeping its direction — what stops a curve overshooting its own corner.</summary>
    private static LayoutVector Held(LayoutVector handle, double most)
    {
        var reach = handle.Length;

        return reach <= most || reach < 1e-9 ? handle : handle * (most / reach);
    }

    private static LayoutVector Direction(LayoutPoint from, LayoutPoint to)
    {
        var along = to - from;
        return along.Length < 1e-9 ? new LayoutVector(1, 0) : along / along.Length;
    }

    private static LayoutShape Polygon(ILayoutMedium medium, params LayoutPoint[] points) => Figure(medium, points, closed: true);

    private static LayoutShape Polyline(ILayoutMedium medium, params LayoutPoint[] points) => Figure(medium, points, closed: false);

    private static LayoutShape Figure(ILayoutMedium medium, LayoutPoint[] points, bool closed)
    {
        var pen = medium.Outline();
        pen.Begin(points[0], filled: closed, closed: closed);
        pen.PolyLineTo([.. points.Skip(1)], stroked: true, smooth: false);

        return pen.Close();
    }
}