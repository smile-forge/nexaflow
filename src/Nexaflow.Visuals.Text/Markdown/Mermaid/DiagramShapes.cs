using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using Nexaflow.Markdown.Ast;
using Nexaflow.Visuals.Text.Editing;
using Nexaflow.Markdown.Mermaid;

namespace Nexaflow.Visuals.Text.Markdown.Mermaid;

/// <summary>The shapes a diagram draws a node as — Mermaid's, named for what they look like, with the brackets a flowchart writes each with.</summary>
internal enum DiagramShape
{
    /// <summary><c>[text]</c></summary>
    Rectangle,

    /// <summary><c>(text)</c></summary>
    Rounded,

    /// <summary><c>([text])</c> — a pill.</summary>
    Stadium,

    /// <summary><c>[[text]]</c> — a rectangle with a second line down each side.</summary>
    Subroutine,

    /// <summary><c>[(text)]</c> — a drum, as a database is drawn.</summary>
    Cylinder,

    /// <summary><c>((text))</c></summary>
    Circle,

    /// <summary><c>(((text)))</c></summary>
    DoubleCircle,

    /// <summary><c>&gt;text]</c> — a flag, notched on its left.</summary>
    Asymmetric,

    /// <summary><c>{text}</c> — a rhombus, as a decision is drawn.</summary>
    Diamond,

    /// <summary><c>{{text}}</c></summary>
    Hexagon,

    /// <summary><c>[/text/]</c> — leaning right.</summary>
    Parallelogram,

    /// <summary><c>[\text\]</c> — leaning left.</summary>
    ParallelogramAlt,

    /// <summary><c>[/text\]</c> — wider at the bottom.</summary>
    Trapezoid,

    /// <summary><c>[\text/]</c> — wider at the top.</summary>
    TrapezoidAlt,

    /// <summary>A page with a wavy foot.</summary>
    Document,

    /// <summary>A rectangle with its top left corner folded down.</summary>
    Card,

    /// <summary><c>)text(</c> — a cloud, as a mindmap draws one.</summary>
    Cloud,

    /// <summary><c>))text((</c> — a starburst, as a mindmap draws a bang.</summary>
    Bang,
}

/// <summary>
/// The shapes nodes are drawn as, as geometry: each shape's outline and the lines inside it, where words fit in it, how big it
/// has to be to hold words, and where a line from its middle leaves it — which is where a connector meets it.
///
/// <para>
/// <strong>A shape is the same in every diagram.</strong> A flowchart's decision, a block diagram's block and a mind map's node
/// are drawn here, once, so a rhombus holds its words the same way wherever one is drawn and a connector meets it at its edge
/// rather than at its bounding box. <see cref="Draw"/> puts one on the layout tree, standing in its own outline.
/// </para>
/// </summary>
internal static class DiagramShapes
{
    /// <summary>How round a <see cref="DiagramShape.Rounded"/> corner is.</summary>
    private const double Corner = 6;

    /// <summary>How far in a <see cref="DiagramShape.Subroutine"/>'s inner lines and a <see cref="DiagramShape.DoubleCircle"/>'s inner ring are.</summary>
    private const double Inner = 5;

    /// <summary>The shape Mermaid's brackets say, as one to draw — a plain box for brackets that say none.</summary>
    public static DiagramShape For(MermaidShape shape) => shape switch
    {
        MermaidShape.Rounded => DiagramShape.Rounded,
        MermaidShape.Stadium => DiagramShape.Stadium,
        MermaidShape.Subroutine => DiagramShape.Subroutine,
        MermaidShape.Cylinder => DiagramShape.Cylinder,
        MermaidShape.Circle => DiagramShape.Circle,
        MermaidShape.DoubleCircle => DiagramShape.DoubleCircle,
        MermaidShape.Asymmetric => DiagramShape.Asymmetric,
        MermaidShape.Diamond => DiagramShape.Diamond,
        MermaidShape.Hexagon => DiagramShape.Hexagon,
        MermaidShape.Parallelogram => DiagramShape.Parallelogram,
        MermaidShape.ParallelogramAlt => DiagramShape.ParallelogramAlt,
        MermaidShape.Trapezoid => DiagramShape.Trapezoid,
        MermaidShape.TrapezoidAlt => DiagramShape.TrapezoidAlt,
        MermaidShape.Document => DiagramShape.Document,
        MermaidShape.Card => DiagramShape.Card,
        MermaidShape.Cloud => DiagramShape.Cloud,
        MermaidShape.Bang => DiagramShape.Bang,
        _ => DiagramShape.Rectangle,
    };

    /// <summary>The outline of a shape filling <paramref name="bounds"/> — what is filled, stroked, and stood in. Frozen.</summary>
    public static Geometry Outline(DiagramShape shape, Rect bounds)
    {
        Geometry outline = shape switch
        {
            DiagramShape.Rounded => new RectangleGeometry(bounds, Corner, Corner),
            DiagramShape.Stadium => new RectangleGeometry(bounds, bounds.Height / 2, bounds.Height / 2),
            DiagramShape.Circle or DiagramShape.DoubleCircle => new EllipseGeometry(bounds),
            DiagramShape.Cylinder => Cylinder(bounds),
            DiagramShape.Document => Document(bounds),
            DiagramShape.Cloud => Cloud(bounds),
            _ when Corners(shape, bounds) is { } points => Polygon(points),
            _ => new RectangleGeometry(bounds),
        };

        outline.Freeze();
        return outline;
    }

    /// <summary>The lines a shape draws inside its outline — a subroutine's inner sides, a cylinder's rim, the inner ring of a double circle — or null.</summary>
    public static Geometry? Details(DiagramShape shape, Rect bounds)
    {
        Geometry? details = shape switch
        {
            DiagramShape.Subroutine => new GeometryGroup
            {
                Children =
                {
                    new LineGeometry(new Point(bounds.Left + Inner, bounds.Top), new Point(bounds.Left + Inner, bounds.Bottom)),
                    new LineGeometry(new Point(bounds.Right - Inner, bounds.Top), new Point(bounds.Right - Inner, bounds.Bottom)),
                },
            },
            DiagramShape.DoubleCircle when bounds.Width > Inner * 2 && bounds.Height > Inner * 2 =>
                new EllipseGeometry(new Rect(bounds.X + Inner, bounds.Y + Inner, bounds.Width - (Inner * 2), bounds.Height - (Inner * 2))),
            DiagramShape.Cylinder => Rim(bounds),
            DiagramShape.Card => new LineGeometry(new Point(bounds.Left, bounds.Top + Fold(bounds)), new Point(bounds.Left + Fold(bounds), bounds.Top)),
            _ => null,
        };

        details?.Freeze();
        return details;
    }

    /// <summary>Where words fit inside a shape filling <paramref name="bounds"/>.</summary>
    public static Rect Inside(DiagramShape shape, Rect bounds)
    {
        var (x, y, w, h) = (bounds.X, bounds.Y, bounds.Width, bounds.Height);

        return shape switch
        {
            DiagramShape.Stadium => Inset(bounds, h / 2, 0),
            DiagramShape.Subroutine => Inset(bounds, Inner * 2, 0),
            DiagramShape.Circle => Inset(bounds, w * (1 - Math.Sqrt(0.5)) / 2, h * (1 - Math.Sqrt(0.5)) / 2),
            DiagramShape.DoubleCircle => Inset(bounds, Inner + ((w - (Inner * 2)) * (1 - Math.Sqrt(0.5)) / 2), Inner + ((h - (Inner * 2)) * (1 - Math.Sqrt(0.5)) / 2)),
            DiagramShape.Diamond => Inset(bounds, w / 4, h / 4),
            DiagramShape.Hexagon or DiagramShape.Parallelogram or DiagramShape.ParallelogramAlt
                or DiagramShape.Trapezoid or DiagramShape.TrapezoidAlt => Inset(bounds, Slant(bounds), 0),
            DiagramShape.Asymmetric => new Rect(x + Notch(bounds), y, Math.Max(0, w - Notch(bounds)), h),
            DiagramShape.Cylinder => new Rect(x, y + (Lid(bounds) * 2), w, Math.Max(0, h - (Lid(bounds) * 3))),
            DiagramShape.Document => new Rect(x, y, w, Math.Max(0, h - (Wave(bounds) * 2))),
            DiagramShape.Card => new Rect(x + (Fold(bounds) / 2), y, Math.Max(0, w - (Fold(bounds) / 2)), h),
            DiagramShape.Cloud => Inset(bounds, w * 0.14, h * 0.2),
            DiagramShape.Bang => Inset(bounds, w * 0.22, h * 0.24),
            _ => bounds,
        };
    }

    /// <summary>
    /// How big a shape has to be for <paramref name="words"/> to fit inside it with <paramref name="pad"/> of clear air round
    /// them — the size <see cref="Inside"/> gives that room back for.
    /// </summary>
    public static Size Around(DiagramShape shape, Size words, double pad)
    {
        var (w, h) = (words.Width + (pad * 2), words.Height + (pad * 2));

        switch (shape)
        {
            case DiagramShape.Stadium: return new Size(w + h, h);
            case DiagramShape.Subroutine: return new Size(w + (Inner * 4), h);
            case DiagramShape.Circle: return Square(Math.Max(w, h) * Math.Sqrt(2));
            case DiagramShape.DoubleCircle: return Square((Math.Max(w, h) * Math.Sqrt(2)) + (Inner * 2));
            case DiagramShape.Diamond: return new Size(w * 2, h * 2);
            case DiagramShape.Asymmetric: return new Size(Math.Max(w + 12, w * 4 / 3), h);
            case DiagramShape.Cylinder: return new Size(w, h / 0.55 * 0.15 < 10 ? h / 0.55 : h + 30);
            case DiagramShape.Document: return new Size(w, h / 0.85);
            case DiagramShape.Card: return new Size(w + (Math.Min(12, h / 3) / 2), h);
            case DiagramShape.Cloud: return new Size(w / 0.72, h / 0.6);
            case DiagramShape.Bang: return new Size(w / 0.56, h / 0.52);
            case DiagramShape.Hexagon or DiagramShape.Parallelogram or DiagramShape.ParallelogramAlt
                or DiagramShape.Trapezoid or DiagramShape.TrapezoidAlt:
                return new Size(w + (h / 3 * 2), h);
            default: return new Size(w, h);
        }

        static Size Square(double side) => new(side, side);
    }

    /// <summary>
    /// Where a line meets a shape: the point its ray leaves the outline by.
    /// </summary>
    /// <param name="from">
    /// Where the ray is cast from, for a line given its own place along the edge rather than the shape's middle. Cast from
    /// the middle, every line meeting a shape leaves it at much the same point however far apart their ends were set.
    /// </param>
    public static Point Edge(DiagramShape shape, Rect bounds, Point toward, Point? from = null)
    {
        var middle = new Point(bounds.X + (bounds.Width / 2), bounds.Y + (bounds.Height / 2));

        if (shape is DiagramShape.Circle or DiagramShape.DoubleCircle)
        {
            var round = toward - middle;
            if (round.Length < 1e-9) return middle;

            var (a, b) = (bounds.Width / 2, bounds.Height / 2);
            var scale = 1 / Math.Sqrt(((round.X * round.X) / (a * a)) + ((round.Y * round.Y) / (b * b)));

            return middle + (round * scale);
        }

        var start = from ?? middle;
        var along = toward - start;
        if (along.Length < 1e-9) return start;

        var points = Corners(shape, bounds) ?? [bounds.TopLeft, bounds.TopRight, bounds.BottomRight, bounds.BottomLeft];
        var nearest = double.PositiveInfinity;

        for (var at = 0; at < points.Count; at++)
        {
            var (one, other) = (points[at], points[(at + 1) % points.Count]);
            if (Crossing(start, along, one, other) is { } distance && distance < nearest) nearest = distance;
        }

        return double.IsInfinity(nearest) ? start : start + (along * nearest);
    }

    /// <summary>
    /// Where a line meets a shape that is read as being met at its points rather than wherever the line happens to cross
    /// its outline — a flowchart's diamond, met at its top, its foot, or one of its sides.
    ///
    /// <para>
    /// Which point it takes follows from where the layout set this line's end along the shape: the ends set out to either
    /// side take the side points, and one left in the middle takes the point ahead of it. That is the classic drawing of a
    /// decision — what comes in arrives at the top, and each way out leaves by a side or the foot — and it holds however
    /// the layout happened to place what is at the other end of each line.
    /// </para>
    /// </summary>
    /// <param name="end">Where the layout put this line's end, which is on the shape's own middle line.</param>
    /// <param name="toward">The way the line goes from there.</param>
    public static Point Cornered(DiagramShape shape, Rect bounds, Point end, Point toward)
    {
        var points = Corners(shape, bounds);
        if (points is null || points.Count == 0) return Edge(shape, bounds, toward, end);

        var middle = new Point(bounds.X + (bounds.Width / 2), bounds.Y + (bounds.Height / 2));

        var along = toward - middle;
        if (along.Length < 1e-9) return middle;

        var upright = Math.Abs(along.Y) >= Math.Abs(along.X);
        var aside = upright ? end.X - middle.X : end.Y - middle.Y;

        // Off the middle at all. Which point a line takes is which of the ends it is, not how far from the middle the layout
        // happened to set it: measured as a distance, a wide shape swallows the whole spread and every line takes the point
        // ahead, so the same diagram drawn with longer words in its decisions loses the sides it had with shorter ones.
        var out0 = Math.Abs(aside) > 1e-9;

        var want = (upright, out0) switch
        {
            (true, true) => new Vector(Math.Sign(aside), 0),
            (true, false) => new Vector(0, Math.Sign(along.Y)),
            (false, true) => new Vector(0, Math.Sign(aside)),
            (false, false) => new Vector(Math.Sign(along.X), 0),
        };

        var (best, most) = (points[0], double.NegativeInfinity);

        foreach (var corner in points)
        {
            var side = corner - middle;
            if (side.Length < 1e-9) continue;

            var how = ((side.X * want.X) + (side.Y * want.Y)) / side.Length;
            if (how > most) (most, best) = (how, corner);
        }

        return best;
    }

    /// <summary>
    /// Draws a shape filling <paramref name="bounds"/> as a piece of <paramref name="kind"/> standing for <paramref name="part"/>:
    /// filled, outlined, and standing in its own outline (<see cref="MermaidPiece.Shape"/>), so a press anywhere inside it means
    /// what it stands for — with <paramref name="words"/>, where it has any, in the middle of the room inside it as a piece of
    /// <paramref name="wordsKind"/>.
    /// </summary>
    public static void Draw(LayoutBuilder build, string kind, ISourcePart? part, DiagramShape shape, Rect bounds,
                            Brush? fill, DiagramStroke? stroke, DiagramWords? words = null, string wordsKind = MermaidPiece.Words)
    {
        var outline = Outline(shape, bounds);
        var room = Inside(shape, bounds);
        var at = words is null ? default : new Point(room.X + ((room.Width - words.Width) / 2), room.Y + ((room.Height - words.Height) / 2));

        build.Open(kind, part, stops: Stops.None);

        // The drawing is a piece of its own, and a leaf: only what draws is pressed, so a shape with words in it stands in its
        // outline through this — less where its words are, which stand in front.
        build.Open(MermaidPiece.Shape, part, stops: Stops.None);
        build.Draw(new GeometryMark(outline, fill, stroke?.Ink, stroke?.Thickness ?? 0) { Dashes = stroke?.Dashes });
        if (Details(shape, bounds) is { } details && stroke is not null)
            build.Draw(new GeometryMark(details, null, stroke.Ink, stroke.Thickness));
        build.Occupies(words is null ? outline : Clear(outline, new Rect(at, new Size(words.Width, words.Height))));
        build.Close();

        words?.Set(build, at, wordsKind);

        build.Close();
    }

    /// <summary>
    /// Draws a shape with several sets of words placed where the diagram puts them, each standing for what it was drawn from — a node's
    /// label and its value, a lane's own name turned a quarter turn to read up its band.
    /// </summary>
    /// <param name="degrees">How far the words are turned, a quarter turn being <c>-90</c>, which reads them up the page.</param>
    public static void Draw(LayoutBuilder build, string kind, ISourcePart? part, DiagramShape shape, Rect bounds, Brush? fill, DiagramStroke? stroke,
                            IReadOnlyList<(DiagramWords Words, Point At, string Kind)> words, Geometry? covered = null,
                            double degrees = 0)
    {
        var outline = Outline(shape, bounds);
        var over = new GeometryGroup();
        if (covered is not null) over.Children.Add(covered);
        foreach (var (said, at, _) in words) over.Children.Add(new RectangleGeometry(Taken(said, at, degrees)));

        build.Open(kind, part, stops: Stops.None);

        build.Open(MermaidPiece.Shape, part, stops: Stops.None);
        build.Draw(new GeometryMark(outline, fill, stroke?.Ink, stroke?.Thickness ?? 0) { Dashes = stroke?.Dashes });
        if (Details(shape, bounds) is { } details && stroke is not null)
            build.Draw(new GeometryMark(details, null, stroke.Ink, stroke.Thickness));
        var stands = new CombinedGeometry(GeometryCombineMode.Exclude, outline, over);
        stands.Freeze();
        build.Occupies(stands);
        build.Close();

        foreach (var (said, at, wordsKind) in words) said.Set(build, at, wordsKind, degrees);

        build.Close();
    }

    /// <summary>
    /// The room words take where they are set: their own box, turned about where they are anchored — a quarter turn leaves them reaching
    /// up from their foot by however wide they are.
    /// </summary>
    private static Rect Taken(DiagramWords said, Point at, double degrees)
    {
        var room = new Rect(at, new Size(said.Width, said.Height));

        return degrees == 0 ? room : new RotateTransform(degrees, at.X, at.Y).TransformBounds(room);
    }

    /// <summary>
    /// Several shapes as one. A group of them would draw the same and stand wrong: a line's band is wound the other way round
    /// from a rectangle, and either fill rule takes the overlap of the two back out again.
    /// </summary>
    public static Geometry United(IEnumerable<Geometry> shapes)
    {
        Geometry all = new RectangleGeometry(Rect.Empty);
        foreach (var shape in shapes) all = new CombinedGeometry(GeometryCombineMode.Union, all, shape);

        all.Freeze();
        return all;
    }

    /// <summary>Where a shape stands with words drawn over it: its outline, less where the words are, so a press on them means them.</summary>
    public static Geometry Clear(Geometry outline, Rect words)
    {
        var stands = new CombinedGeometry(GeometryCombineMode.Exclude, outline, new RectangleGeometry(words));
        stands.Freeze();
        return stands;
    }

    // ── Outlines ────────────────────────────────────────────────────────────

    /// <summary>The corners of a shape whose outline is straight lines, or null for one that is not.</summary>
    private static IReadOnlyList<Point>? Corners(DiagramShape shape, Rect r)
    {
        var (x, y, w, h) = (r.X, r.Y, r.Width, r.Height);
        var (cx, cy, k) = (x + (w / 2), y + (h / 2), Slant(r));

        return shape switch
        {
        DiagramShape.Bang => Bang(r),
            DiagramShape.Diamond => [new(cx, y), new(x + w, cy), new(cx, y + h), new(x, cy)],
            DiagramShape.Hexagon => [new(x + k, y), new(x + w - k, y), new(x + w, cy), new(x + w - k, y + h), new(x + k, y + h), new(x, cy)],
            DiagramShape.Asymmetric => [new(x, y), new(x + w, y), new(x + w, y + h), new(x, y + h), new(x + Notch(r), cy)],
            DiagramShape.Parallelogram => [new(x + k, y), new(x + w, y), new(x + w - k, y + h), new(x, y + h)],
            DiagramShape.ParallelogramAlt => [new(x, y), new(x + w - k, y), new(x + w, y + h), new(x + k, y + h)],
            DiagramShape.Trapezoid => [new(x + k, y), new(x + w - k, y), new(x + w, y + h), new(x, y + h)],
            DiagramShape.TrapezoidAlt => [new(x, y), new(x + w, y), new(x + w - k, y + h), new(x + k, y + h)],
            DiagramShape.Card => [new(x + Fold(r), y), new(x + w, y), new(x + w, y + h), new(x, y + h), new(x, y + Fold(r))],
            _ => null,
        };
    }

    private static Geometry Polygon(IReadOnlyList<Point> points)
    {
        var shape = new StreamGeometry();
        using (var pen = shape.Open())
        {
            pen.BeginFigure(points[0], isFilled: true, isClosed: true);
            pen.PolyLineTo([.. points.Skip(1)], isStroked: true, isSmoothJoin: false);
        }

        return shape;
    }

    private static Geometry Cylinder(Rect r)
    {
        var lid = Lid(r);
        var shape = new StreamGeometry();
        using (var pen = shape.Open())
        {
            pen.BeginFigure(new Point(r.Left, r.Top + lid), isFilled: true, isClosed: true);
            pen.ArcTo(new Point(r.Right, r.Top + lid), new Size(r.Width / 2, lid), 0, false, SweepDirection.Clockwise, true, true);
            pen.LineTo(new Point(r.Right, r.Bottom - lid), true, true);
            pen.ArcTo(new Point(r.Left, r.Bottom - lid), new Size(r.Width / 2, lid), 0, false, SweepDirection.Clockwise, true, true);
        }

        return shape;
    }

    /// <summary>The near edge of a cylinder's lid, which its outline does not draw.</summary>
    private static Geometry Rim(Rect r)
    {
        var lid = Lid(r);
        var shape = new StreamGeometry();
        using (var pen = shape.Open())
        {
            pen.BeginFigure(new Point(r.Right, r.Top + lid), isFilled: false, isClosed: false);
            pen.ArcTo(new Point(r.Left, r.Top + lid), new Size(r.Width / 2, lid), 0, false, SweepDirection.Clockwise, true, true);
        }

        return shape;
    }

    private static Geometry Document(Rect r)
    {
        // A cubic through its ends with its handles a height k either side of them bows out 0.2887 k at most: the foot waves
        // down to the bottom of the bounds and up as far again.
        var wave = Wave(r);
        var foot = r.Bottom - wave;
        var handle = wave / 0.2887;

        var shape = new StreamGeometry();
        using (var pen = shape.Open())
        {
            pen.BeginFigure(r.TopLeft, isFilled: true, isClosed: true);
            pen.LineTo(r.TopRight, true, false);
            pen.LineTo(new Point(r.Right, foot), true, false);
            pen.BezierTo(new Point(r.Right - (r.Width / 3), foot - handle), new Point(r.Left + (r.Width / 3), foot + handle),
                         new Point(r.Left, foot), true, true);
        }

        return shape;
    }

    /// <summary>
    /// A cloud filling <paramref name="bounds"/>: a band across its middle with a bump over and under each third of it, every bump
    /// inside the bounds, so a cloud takes the room it is given like any other shape.
    /// </summary>
    private static Geometry Cloud(Rect bounds)
    {
        var (w, h) = (bounds.Width, bounds.Height);
        var puffs = new GeometryGroup();
        puffs.Children.Add(new RectangleGeometry(new Rect(bounds.X + (w * 0.06), bounds.Y + (h * 0.3), w * 0.88, h * 0.45)));

        // Three bumps along the top and three along the foot, the middle one of each the tallest.
        foreach (var (at, wide, tall, top) in (( double At, double Wide, double Tall, bool Top)[])
                 [(0.0, 0.42, 0.62, true), (0.28, 0.46, 0.7, true), (0.58, 0.42, 0.62, true),
                  (0.02, 0.4, 0.55, false), (0.3, 0.44, 0.62, false), (0.58, 0.4, 0.55, false)])
            puffs.Children.Add(new EllipseGeometry(new Rect(bounds.X + (w * at), top ? bounds.Y : bounds.Bottom - (h * tall), w * wide, h * tall)));

        return United(puffs);
    }

    /// <summary>A starburst filling <paramref name="bounds"/>: sixteen points round its middle, every other one drawn in.</summary>
    private static IReadOnlyList<Point> Bang(Rect bounds)
    {
        var (cx, cy) = (bounds.X + (bounds.Width / 2), bounds.Y + (bounds.Height / 2));
        var points = new List<Point>();

        for (var spike = 0; spike < 16; spike++)
        {
            var angle = spike * Math.PI / 8;
            var reach = spike % 2 == 0 ? 1.0 : 0.82;
            points.Add(new Point(cx + (Math.Cos(angle) * bounds.Width / 2 * reach), cy + (Math.Sin(angle) * bounds.Height / 2 * reach)));
        }

        return points;
    }

    /// <summary>Several shapes as one outline, with nothing of the lines where they meet.</summary>
    private static Geometry United(GeometryGroup shapes)
    {
        Geometry united = new RectangleGeometry(Rect.Empty);
        foreach (var shape in shapes.Children) united = new CombinedGeometry(GeometryCombineMode.Union, united, shape);

        return united;
    }

    // ── Proportions ─────────────────────────────────────────────────────────

    /// <summary>How far a slanted side leans in.</summary>
    private static double Slant(Rect r) => Math.Min(r.Height / 3, r.Width / 4);

    private static double Notch(Rect r) => Math.Min(12, r.Width / 4);

    private static double Lid(Rect r) => Math.Min(10, r.Height * 0.15);

    /// <summary>How far a document's foot waves either side of where it runs.</summary>
    private static double Wave(Rect r) => r.Height * 0.075;

    private static double Fold(Rect r) => Math.Min(12, r.Height / 3);

    private static Rect Inset(Rect r, double across, double down) =>
        new(r.X + across, r.Y + down, Math.Max(0, r.Width - (across * 2)), Math.Max(0, r.Height - (down * 2)));

    /// <summary>How far along <paramref name="along"/> from <paramref name="origin"/> the side from <paramref name="from"/> to <paramref name="to"/> is crossed, or null where it is not.</summary>
    private static double? Crossing(Point origin, Vector along, Point from, Point to)
    {
        var side = to - from;
        var denominator = Vector.CrossProduct(along, side);
        if (Math.Abs(denominator) < 1e-12) return null;

        var offset = from - origin;
        var t = Vector.CrossProduct(offset, side) / denominator;
        var u = Vector.CrossProduct(offset, along) / denominator;

        return t >= 0 && u is >= -1e-9 and <= 1 + 1e-9 ? t : null;
    }
}
