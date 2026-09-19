using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using Nexaflow.Markdown.Ast;
using Nexaflow.Visuals.Text.Editing;
using Nexaflow.Markdown.Mermaid;

namespace Nexaflow.Visuals.Text.Markdown.Mermaid;

/// <summary>How a line is drawn: its ink, how thick it is, and the dashes it is broken into — none, for a solid line.</summary>
/// <param name="Dashes">Dash and gap lengths in multiples of the thickness, as a pen takes them — <see cref="Dashed"/>, <see cref="Dotted"/>.</param>
internal sealed record DiagramStroke(Brush Ink, double Thickness = 1, DoubleCollection? Dashes = null)
{
    /// <summary>A line of dashes: Mermaid's <c>-.-></c> and a sequence diagram's reply.</summary>
    public static DoubleCollection Dashed { get; } = Frozen(4, 3);

    /// <summary>A line of dots.</summary>
    public static DoubleCollection Dotted { get; } = Frozen(1, 2);

    private static DoubleCollection Frozen(params double[] lengths)
    {
        var dashes = new DoubleCollection(lengths);
        dashes.Freeze();
        return dashes;
    }
}

/// <summary>What a connector ends in: Mermaid's arrowheads, a class diagram's relations, and an ER diagram's cardinalities.</summary>
internal enum DiagramHead
{
    None,

    /// <summary>A filled triangle: a flowchart's <c>--&gt;</c>, a sequence diagram's <c>-&gt;&gt;</c>.</summary>
    Arrow,

    /// <summary>Two strokes meeting at the tip: a sequence diagram's <c>-)</c>, a class diagram's dependency.</summary>
    Open,

    /// <summary>A hollow triangle: a class diagram's inheritance, <c>&lt;|--</c>.</summary>
    Triangle,

    /// <summary>A filled diamond: a class diagram's composition, <c>*--</c>.</summary>
    Diamond,

    /// <summary>A hollow diamond: a class diagram's aggregation, <c>o--</c>.</summary>
    HollowDiamond,

    /// <summary>A hollow circle: a flowchart's <c>--o</c>.</summary>
    Circle,

    /// <summary>A cross: a flowchart's <c>--x</c>, a sequence diagram's <c>-x</c>.</summary>
    Cross,

    /// <summary>An ER diagram's zero or one: <c>|o</c>.</summary>
    ZeroOrOne,

    /// <summary>An ER diagram's exactly one: <c>||</c>.</summary>
    ExactlyOne,

    /// <summary>An ER diagram's zero or more: <c>}o</c>.</summary>
    ZeroOrMore,

    /// <summary>An ER diagram's one or more: <c>}|</c>.</summary>
    OneOrMore,
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
internal static class DiagramConnector
{
    /// <summary>How long an arrowhead is, and how wide.</summary>
    public const double HeadLength = 9;
    public const double HeadWidth = 8;

    /// <summary>How near a press has to be to a connector's line to mean it.</summary>
    public const double Reach = 8;

    /// <summary>The air round what is written on a connector, between its words and the line they are drawn over.</summary>
    private const double Air = 2;

    /// <summary>
    /// Draws a connector through <paramref name="route"/>, as a piece of <paramref name="kind"/> standing for
    /// <paramref name="part"/>: straight from point to point, or <paramref name="curved"/> through them, ending in
    /// <paramref name="start"/> at its first point and <paramref name="end"/> at its last.
    /// </summary>
    public static void Draw(LayoutBuilder build, string kind, ISourcePart? part, IReadOnlyList<Point> route, DiagramStroke stroke,
                            DiagramHead start = DiagramHead.None, DiagramHead end = DiagramHead.Arrow, bool curved = false)
    {
        if (route.Count < 2) throw new ArgumentException("A connector runs between two points at least.", nameof(route));

        var points = route.ToArray();
        var heads = new GeometryGroup();
        var filled = new GeometryGroup();

        points[^1] = Head(points[^1], Direction(points[^2], points[^1]), end, stroke.Thickness, heads, filled);
        points[0] = Head(points[0], Direction(points[1], points[0]), start, stroke.Thickness, heads, filled);

        var line = Line(points, curved);

        build.Open(kind, part, stops: Stops.None);
        build.Draw(new GeometryMark(line, null, stroke.Ink, stroke.Thickness) { Dashes = stroke.Dashes });
        if (filled.Children.Count > 0) build.Draw(new GeometryMark(Frozen(filled), stroke.Ink, stroke.Ink, stroke.Thickness));
        if (heads.Children.Count > 0) build.Draw(new GeometryMark(Frozen(heads), null, stroke.Ink, stroke.Thickness));

        var band = new GeometryGroup { Children = { line, heads, filled } }
            .GetWidenedPathGeometry(new Pen(Brushes.Black, Math.Max(Reach, stroke.Thickness)));
        build.Occupies(band);
        build.Close();
    }

    /// <summary>The point halfway along a route — where the words on a connector go.</summary>
    public static Point Middle(IReadOnlyList<Point> route)
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
    /// The room what is written on a connector takes, over the middle of it and with a little air round it — or nothing, where
    /// there is nothing to say. Whatever is drawn under it does not stand there, so it is worked out before anything is drawn.
    /// </summary>
    public static Rect Room(IReadOnlyList<Point> route, IReadOnlyList<DiagramWords> said)
    {
        var taken = DiagramWords.Taken(said);
        if (taken.Width <= 0 || taken.Height <= 0) return Rect.Empty;

        var at = Middle(route);
        return Rect.Inflate(new Rect(at.X - (taken.Width / 2), at.Y - (taken.Height / 2), taken.Width, taken.Height), Air, Air / 2);
    }

    /// <summary>
    /// What is written on a connector, in the room <see cref="Room"/> gave it: the words on a patch of <paramref name="backing"/>,
    /// so the line does not run through them and a press there means the connector rather than whatever is under it.
    /// </summary>
    public static void Says(LayoutBuilder build, string kind, ISourcePart? part, Rect room, IReadOnlyList<DiagramWords> said,
                            Brush backing, string wordsKind = MermaidPiece.Words)
    {
        if (room.IsEmpty) return;

        var patch = new RectangleGeometry(room);
        patch.Freeze();

        build.Open(kind, part, stops: Stops.None);
        build.Draw(new GeometryMark(patch, backing, null, 0));
        build.Occupies(patch);

        foreach (var (words, at, said_kind) in DiagramWords.Placed(said, Rect.Inflate(room, -Air, -Air / 2), wordsKind))
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
    public static DiagramStroke Stroked(Brush ink, MermaidLineStyle style, double thin = 1, double thick = 2.5,
                                        DoubleCollection? dashes = null) =>
        new(ink, style == MermaidLineStyle.Thick ? thick : thin,
            dashes ?? (style == MermaidLineStyle.Dotted ? DiagramStroke.Dotted : null));

    /// <summary>
    /// The band a route stands in — how near a press must be to mean it (<see cref="Reach"/>) — for a shape drawn under one to
    /// leave it out of what it stands in itself.
    /// </summary>
    public static Geometry Band(IReadOnlyList<Point> route, double thickness = 1, bool curved = false) =>
        Frozen(Line(route, curved).GetWidenedPathGeometry(new Pen(Brushes.Black, Math.Max(Reach, thickness))));

    // ── Heads ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Draws what a connector ends in at <paramref name="tip"/>, pointing along <paramref name="along"/>, and hands back where
    /// the line itself stops — short of a head it would otherwise run through.
    /// </summary>
    private static Point Head(Point tip, Vector along, DiagramHead head, double thickness, GeometryGroup lines, GeometryGroup solid)
    {
        var across = new Vector(-along.Y, along.X);
        var (length, half) = (HeadLength + thickness, (HeadWidth + thickness) / 2);
        var back = tip - (along * length);

        switch (head)
        {
            case DiagramHead.Arrow:
                solid.Children.Add(Polygon(tip, back + (across * half), back - (across * half)));
                return back;

            case DiagramHead.Open:
                lines.Children.Add(Polyline(back + (across * half), tip, back - (across * half)));
                return tip;

            case DiagramHead.Triangle:
                lines.Children.Add(Polygon(tip, back + (across * half), back - (across * half)));
                return back;

            case DiagramHead.Diamond or DiagramHead.HollowDiamond:
                var far = tip - (along * length * 2);
                var diamond = Polygon(tip, back + (across * half), far, back - (across * half));
                (head == DiagramHead.Diamond ? solid : lines).Children.Add(diamond);
                return far;

            case DiagramHead.Circle:
                lines.Children.Add(new EllipseGeometry(tip - (along * half), half, half));
                return tip - (along * half * 2);

            case DiagramHead.Cross:
                var centre = tip - (along * half);
                lines.Children.Add(new LineGeometry(centre + ((along + across) * half), centre - ((along + across) * half)));
                lines.Children.Add(new LineGeometry(centre + ((along - across) * half), centre - ((along - across) * half)));
                return tip;

            case DiagramHead.ZeroOrOne:
                Bar(tip - (along * 6), across, half, lines);
                lines.Children.Add(new EllipseGeometry(tip - (along * 14), 4, 4));
                return tip;

            case DiagramHead.ExactlyOne:
                Bar(tip - (along * 6), across, half, lines);
                Bar(tip - (along * 11), across, half, lines);
                return tip;

            case DiagramHead.ZeroOrMore:
                Foot(tip, along, across, half, lines);
                lines.Children.Add(new EllipseGeometry(tip - (along * (length + 6)), 4, 4));
                return tip;

            case DiagramHead.OneOrMore:
                Foot(tip, along, across, half, lines);
                Bar(tip - (along * (length + 4)), across, half, lines);
                return tip;

            default:
                return tip;
        }
    }

    private static void Bar(Point at, Vector across, double half, GeometryGroup lines) =>
        lines.Children.Add(new LineGeometry(at + (across * half), at - (across * half)));

    /// <summary>A crow's foot: three strokes spreading from a point on the line out to the tip.</summary>
    private static void Foot(Point tip, Vector along, Vector across, double half, GeometryGroup lines)
    {
        var heel = tip - (along * HeadLength);
        lines.Children.Add(Polyline(tip + (across * half), heel, tip - (across * half)));
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
    private static Geometry Line(IReadOnlyList<Point> points, bool curved)
    {
        var line = new StreamGeometry();
        using (var pen = line.Open())
        {
            pen.BeginFigure(points[0], isFilled: false, isClosed: false);

            if (!curved || points.Count < 3)
            {
                pen.PolyLineTo([.. points.Skip(1)], isStroked: true, isSmoothJoin: true);
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

                    pen.BezierTo(from + lead, to - trail, to, isStroked: true, isSmoothJoin: true);
                }
            }
        }

        line.Freeze();
        return line;
    }

    /// <summary>A handle held to how far it may reach, keeping its direction — what stops a curve overshooting its own corner.</summary>
    private static Vector Held(Vector handle, double most)
    {
        var reach = handle.Length;

        return reach <= most || reach < 1e-9 ? handle : handle * (most / reach);
    }

    private static Vector Direction(Point from, Point to)
    {
        var along = to - from;
        return along.Length < 1e-9 ? new Vector(1, 0) : along / along.Length;
    }

    private static Geometry Polygon(params Point[] points) => Figure(points, closed: true);

    private static Geometry Polyline(params Point[] points) => Figure(points, closed: false);

    private static Geometry Figure(Point[] points, bool closed)
    {
        var shape = new StreamGeometry();
        using (var pen = shape.Open())
        {
            pen.BeginFigure(points[0], isFilled: closed, isClosed: closed);
            pen.PolyLineTo([.. points.Skip(1)], isStroked: true, isSmoothJoin: false);
        }

        shape.Freeze();
        return shape;
    }

    private static Geometry Frozen(Geometry geometry)
    {
        geometry.Freeze();
        return geometry;
    }
}
