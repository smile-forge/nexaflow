using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using Nexaflow.Markdown.Ast;
using Nexaflow.Visuals.Text.Editing;
using Nexaflow.Markdown.Mermaid;

namespace Nexaflow.Visuals.Text.Markdown.Mermaid;

/// <summary>The shapes a diagram draws a node as — Mermaid's, named for what they look like, with the brackets or the name a flowchart writes each with.</summary>
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

    /// <summary><c>lin-doc</c> — a document with a line down its left side.</summary>
    LinedDocument,

    /// <summary><c>docs</c> — documents stacked one behind another.</summary>
    StackedDocument,

    /// <summary><c>tag-doc</c> — a document with its lower right corner turned.</summary>
    TaggedDocument,

    /// <summary>A rectangle with its top left corner folded down.</summary>
    Card,

    /// <summary><c>notch-pent</c> — a rectangle with both top corners cut off.</summary>
    NotchedPentagon,

    /// <summary><c>lin-rect</c> — a rectangle with a second line down its left side.</summary>
    LinedRectangle,

    /// <summary><c>div-rect</c> — a rectangle with a line across its top.</summary>
    DividedRectangle,

    /// <summary><c>win-pane</c> — a rectangle with a line across its top and down its left.</summary>
    WindowPane,

    /// <summary><c>tag-rect</c> — a rectangle with its lower right corner turned.</summary>
    TaggedRectangle,

    /// <summary><c>st-rect</c> — rectangles stacked one behind another.</summary>
    StackedRectangle,

    /// <summary><c>sl-rect</c> — a rectangle whose top slopes up to the right.</summary>
    SlopedRectangle,

    /// <summary><c>delay</c> — a rectangle rounded off at its right end.</summary>
    Delay,

    /// <summary><c>curv-trap</c> — pointed at its left end and rounded at its right.</summary>
    CurvedTrapezoid,

    /// <summary><c>bow-rect</c> — both ends bowing to the left.</summary>
    BowTie,

    /// <summary><c>flag</c> — a band whose top and foot wave together.</summary>
    Flag,

    /// <summary><c>tri</c> — a triangle on its base, its words in the foot of it.</summary>
    Triangle,

    /// <summary><c>flip-tri</c> — a triangle on its point, its words in the top of it.</summary>
    FlippedTriangle,

    /// <summary><c>hourglass</c> — two triangles point to point.</summary>
    Hourglass,

    /// <summary><c>bolt</c> — a lightning bolt.</summary>
    Bolt,

    /// <summary><c>fork</c> — a solid bar.</summary>
    Fork,

    /// <summary><c>sm-circ</c> — a small circle.</summary>
    SmallCircle,

    /// <summary><c>fr-circ</c> — a small ring round a dot.</summary>
    FramedCircle,

    /// <summary><c>f-circ</c> — a small solid circle.</summary>
    FilledCircle,

    /// <summary><c>cross-circ</c> — a circle crossed through.</summary>
    CrossedCircle,

    /// <summary><c>h-cyl</c> — a cylinder lying on its side.</summary>
    HorizontalCylinder,

    /// <summary><c>lin-cyl</c> — a cylinder with a second rim under its lid.</summary>
    LinedCylinder,

    /// <summary><c>datastore</c> — a band ruled along its top and its foot.</summary>
    DataStore,

    /// <summary><c>bucket</c> — a pail.</summary>
    Bucket,

    /// <summary><c>brace</c> — a curly brace left of the words.</summary>
    Brace,

    /// <summary><c>brace-r</c> — a curly brace right of the words.</summary>
    BraceRight,

    /// <summary><c>braces</c> — curly braces either side of the words.</summary>
    Braces,

    /// <summary><c>browser</c> — a browser window.</summary>
    Browser,

    /// <summary><c>console</c> — a terminal window.</summary>
    Console,

    /// <summary><c>folder</c> — a folder with its tab on top.</summary>
    Folder,

    /// <summary><c>person</c> — a head over a body.</summary>
    Person,

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
internal static partial class DiagramShapes
{
    /// <summary>How round a <see cref="DiagramShape.Rounded"/> corner is.</summary>
    private const double Corner = 6;

    /// <summary>How far in a <see cref="DiagramShape.Subroutine"/>'s inner lines and a <see cref="DiagramShape.DoubleCircle"/>'s inner ring are.</summary>
    private const double Inner = 5;

    /// <summary>The shape Mermaid's brackets or name say, as one to draw — a plain box for brackets that say none.</summary>
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
        MermaidShape.LinedDocument => DiagramShape.LinedDocument,
        MermaidShape.StackedDocument => DiagramShape.StackedDocument,
        MermaidShape.TaggedDocument => DiagramShape.TaggedDocument,
        MermaidShape.Card => DiagramShape.Card,
        MermaidShape.NotchedPentagon => DiagramShape.NotchedPentagon,
        MermaidShape.LinedRectangle => DiagramShape.LinedRectangle,
        MermaidShape.DividedRectangle => DiagramShape.DividedRectangle,
        MermaidShape.WindowPane => DiagramShape.WindowPane,
        MermaidShape.TaggedRectangle => DiagramShape.TaggedRectangle,
        MermaidShape.StackedRectangle => DiagramShape.StackedRectangle,
        MermaidShape.SlopedRectangle => DiagramShape.SlopedRectangle,
        MermaidShape.Delay => DiagramShape.Delay,
        MermaidShape.CurvedTrapezoid => DiagramShape.CurvedTrapezoid,
        MermaidShape.BowTie => DiagramShape.BowTie,
        MermaidShape.Flag => DiagramShape.Flag,
        MermaidShape.Triangle => DiagramShape.Triangle,
        MermaidShape.FlippedTriangle => DiagramShape.FlippedTriangle,
        MermaidShape.Hourglass => DiagramShape.Hourglass,
        MermaidShape.Bolt => DiagramShape.Bolt,
        MermaidShape.Fork => DiagramShape.Fork,
        MermaidShape.SmallCircle => DiagramShape.SmallCircle,
        MermaidShape.FramedCircle => DiagramShape.FramedCircle,
        MermaidShape.FilledCircle => DiagramShape.FilledCircle,
        MermaidShape.CrossedCircle => DiagramShape.CrossedCircle,
        MermaidShape.HorizontalCylinder => DiagramShape.HorizontalCylinder,
        MermaidShape.LinedCylinder => DiagramShape.LinedCylinder,
        MermaidShape.DataStore => DiagramShape.DataStore,
        MermaidShape.Bucket => DiagramShape.Bucket,
        MermaidShape.Brace => DiagramShape.Brace,
        MermaidShape.BraceRight => DiagramShape.BraceRight,
        MermaidShape.Braces => DiagramShape.Braces,
        MermaidShape.Browser => DiagramShape.Browser,
        MermaidShape.Console => DiagramShape.Console,
        MermaidShape.Folder => DiagramShape.Folder,
        MermaidShape.Person => DiagramShape.Person,
        MermaidShape.Cloud => DiagramShape.Cloud,
        MermaidShape.Bang => DiagramShape.Bang,
        _ => DiagramShape.Rectangle,
    };

    /// <summary>The outline of a shape filling <paramref name="bounds"/> — what is filled, stroked, and stood in. Frozen.</summary>
    public static Geometry Outline(DiagramShape shape, Rect bounds)
    {
        Geometry outline = shape switch
        {
            DiagramShape.Rounded or DiagramShape.Browser or DiagramShape.Console => new RectangleGeometry(bounds, Corner, Corner),
            DiagramShape.Stadium => new RectangleGeometry(bounds, bounds.Height / 2, bounds.Height / 2),
            _ when Round(shape) => new EllipseGeometry(bounds),
            DiagramShape.Cylinder or DiagramShape.LinedCylinder => Cylinder(bounds),
            DiagramShape.Document or DiagramShape.LinedDocument or DiagramShape.TaggedDocument => Document(bounds),
            DiagramShape.StackedDocument => United(Stacked(bounds).Select(Document)),
            DiagramShape.StackedRectangle => United(Stacked(bounds).Select(sheet => (Geometry)new RectangleGeometry(sheet))),
            DiagramShape.Cloud => Cloud(bounds),
            DiagramShape.Delay => Delay(bounds),
            DiagramShape.CurvedTrapezoid => CurvedTrapezoid(bounds),
            DiagramShape.BowTie => BowTie(bounds),
            DiagramShape.Flag => Flag(bounds),
            DiagramShape.Hourglass => Polygon([bounds.TopLeft, bounds.TopRight, bounds.BottomLeft, bounds.BottomRight]),
            DiagramShape.HorizontalCylinder => HorizontalCylinder(bounds),
            DiagramShape.DataStore => DataStore(bounds),
            DiagramShape.Bucket => Bucket(bounds),
            DiagramShape.Brace or DiagramShape.BraceRight or DiagramShape.Braces => Braced(shape, bounds),
            DiagramShape.Person => Person(bounds),
            _ when Corners(shape, bounds) is { } points => Polygon(points),
            _ => new RectangleGeometry(bounds),
        };

        outline.Freeze();
        return outline;
    }

    /// <summary>
    /// The lines a shape draws inside its outline — a subroutine's inner sides, a cylinder's rim, the inner ring of a double circle,
    /// the sheets showing behind a stack's front one — or null.
    /// </summary>
    public static Geometry? Details(DiagramShape shape, Rect bounds)
    {
        var (x, y, w, h) = (bounds.X, bounds.Y, bounds.Width, bounds.Height);

        Geometry? details = shape switch
        {
            DiagramShape.Subroutine => Group(Segment(x + Inner, y, x + Inner, y + h), Segment(x + w - Inner, y, x + w - Inner, y + h)),
            DiagramShape.DoubleCircle when w > Inner * 2 && h > Inner * 2 =>
                new EllipseGeometry(new Rect(x + Inner, y + Inner, w - (Inner * 2), h - (Inner * 2))),
            DiagramShape.FramedCircle => new EllipseGeometry(new Rect(x + (w / 4), y + (h / 4), w / 2, h / 2)),
            DiagramShape.CrossedCircle => Crossed(bounds),
            DiagramShape.Cylinder or DiagramShape.Bucket => Rim(bounds),
            DiagramShape.LinedCylinder => Group(Rim(bounds), Rim(new Rect(x, y + Ring, w, h))),
            DiagramShape.HorizontalCylinder => Face(bounds),
            DiagramShape.Card => Segment(x, y + Fold(bounds), x + Fold(bounds), y),
            DiagramShape.LinedRectangle => Segment(x + Lining, y, x + Lining, y + h),
            DiagramShape.DividedRectangle => Segment(x, y + Divide(bounds), x + w, y + Divide(bounds)),
            DiagramShape.WindowPane => Group(Segment(x, y + Pane, x + w, y + Pane), Segment(x + Pane, y, x + Pane, y + h)),
            DiagramShape.TaggedRectangle => Segment(x + w - Tag(bounds), y + h, x + w, y + h - Tag(bounds)),
            DiagramShape.LinedDocument => Segment(x + Lining, y, x + Lining, WaveAt(bounds, x + Lining)),
            DiagramShape.TaggedDocument => Segment(x + w, y + h - Wave(bounds) - Tag(bounds), x + w - Tag(bounds), WaveAt(bounds, x + w - Tag(bounds))),
            DiagramShape.StackedRectangle => Behind([.. Stacked(bounds).Select(sheet => (Geometry)new RectangleGeometry(sheet))]),
            DiagramShape.StackedDocument => Behind([.. Stacked(bounds).Select(Document)]),
            DiagramShape.Browser => Browser(bounds),
            DiagramShape.Console => Prompt(bounds),
            DiagramShape.Person => new EllipseGeometry(Crown(bounds)),
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
            DiagramShape.Asymmetric => Box(x + Notch(bounds), y, w - Notch(bounds), h),
            DiagramShape.Cylinder => Box(x, y + Lid(bounds), w, h - (Lid(bounds) * 2)),
            DiagramShape.LinedCylinder => Box(x, y + Lid(bounds) + Ring, w, h - (Lid(bounds) * 2) - Ring),
            DiagramShape.Bucket => Box(x + Lip(bounds), y + (Lid(bounds) * 2), w - (Lip(bounds) * 2), h - (Lid(bounds) * 2)),
            DiagramShape.HorizontalCylinder => Box(x + Cap(bounds), y, w - (Cap(bounds) * 3), h),
            DiagramShape.Document or DiagramShape.TaggedDocument => Box(x, y, w, h - (Wave(bounds) * 2)),
            DiagramShape.LinedDocument => Box(x + Lining, y, w - Lining, h - (Wave(bounds) * 2)),
            DiagramShape.StackedDocument => Inside(DiagramShape.Document, Stacked(bounds)[^1]),
            DiagramShape.StackedRectangle => Stacked(bounds)[^1],
            DiagramShape.Card => Box(x + (Fold(bounds) / 2), y, w - (Fold(bounds) / 2), h),
            DiagramShape.NotchedPentagon => Box(x + (Fold(bounds) / 2), y + (Fold(bounds) / 2), w - Fold(bounds), h - (Fold(bounds) / 2)),
            DiagramShape.LinedRectangle => Box(x + Lining, y, w - Lining, h),
            DiagramShape.DividedRectangle => Box(x, y + Divide(bounds), w, h - Divide(bounds)),
            DiagramShape.WindowPane => Box(x + Pane, y + Pane, w - Pane, h - Pane),
            DiagramShape.SlopedRectangle => Box(x, y + Slope(bounds), w, h - Slope(bounds)),
            DiagramShape.Delay => Box(x, y, w - Math.Min(h / 2, w), h),
            DiagramShape.CurvedTrapezoid => Inset(bounds, Pointed(bounds), 0),
            DiagramShape.BowTie => Inset(bounds, Bowed(bounds), 0),
            DiagramShape.Flag => Inset(bounds, 0, Wave(bounds) * 2),
            DiagramShape.Triangle => Box(x + (w * 0.2), y + (h * 0.6), w * 0.6, h * 0.4),
            DiagramShape.FlippedTriangle => Box(x + (w * 0.2), y, w * 0.6, h * 0.4),
            DiagramShape.Brace => Box(x + Curl, y, w - Curl, h),
            DiagramShape.BraceRight => Box(x, y, w - Curl, h),
            DiagramShape.Braces => Inset(bounds, Curl, 0),
            DiagramShape.Browser or DiagramShape.Console => Box(x, y + Bar, w, h - Bar),
            DiagramShape.Folder => Box(x, y + Tab(bounds), w, h - Tab(bounds)),
            DiagramShape.Person => Inset(Body(bounds), Rounding(Body(bounds)) / 2, 0),
            DiagramShape.Cloud => Inset(bounds, w * 0.14, h * 0.2),
            DiagramShape.Bang => Inset(bounds, w * 0.22, h * 0.24),
            _ => bounds,
        };
    }

    /// <summary>
    /// How big a shape has to be for <paramref name="words"/> to fit inside it with <paramref name="pad"/> of clear air round
    /// them — the size <see cref="Inside"/> gives that room back for. A shape drawn without words is the size it always is.
    /// </summary>
    public static Size Around(DiagramShape shape, Size words, double pad)
    {
        if (Fixed(shape) is { } size) return size;

        var (w, h) = (words.Width + (pad * 2), words.Height + (pad * 2));

        switch (shape)
        {
            case DiagramShape.Stadium: return new Size(w + h, h);
            case DiagramShape.Subroutine: return new Size(w + (Inner * 4), h);
            case DiagramShape.Circle: return Square(Math.Max(w, h) * Math.Sqrt(2));
            case DiagramShape.DoubleCircle: return Square((Math.Max(w, h) * Math.Sqrt(2)) + (Inner * 2));
            case DiagramShape.Diamond: return new Size(w * 2, h * 2);
            case DiagramShape.Asymmetric: return new Size(Math.Max(w + 12, w * 4 / 3), h);
            case DiagramShape.Cylinder: return new Size(w, Drum(h));
            case DiagramShape.LinedCylinder: return new Size(w, Drum(h + Ring));
            case DiagramShape.Bucket: return new Size(w / 0.76, Drum(h));
            case DiagramShape.HorizontalCylinder: return new Size(w + (h * 0.75), h);
            case DiagramShape.Document or DiagramShape.TaggedDocument: return new Size(w, h / 0.85);
            case DiagramShape.LinedDocument: return new Size(w + Lining, h / 0.85);
            case DiagramShape.StackedDocument: return new Size(w + (Stack * 2), (h / 0.85) + (Stack * 2));
            case DiagramShape.StackedRectangle: return new Size(w + (Stack * 2), h + (Stack * 2));
            case DiagramShape.Card: return new Size(w + (Math.Min(12, h / 3) / 2), h);
            case DiagramShape.NotchedPentagon: return new Size(w + 12, h + 6);
            case DiagramShape.LinedRectangle: return new Size(w + Lining, h);
            case DiagramShape.DividedRectangle: return new Size(w, h / 0.8);
            case DiagramShape.WindowPane: return new Size(w + Pane, h + Pane);
            case DiagramShape.SlopedRectangle or DiagramShape.Flag: return new Size(w, h / 0.7);
            case DiagramShape.Delay or DiagramShape.BowTie: return new Size(w + (h / 2), h);
            case DiagramShape.CurvedTrapezoid: return new Size(w + (h / 3 * 2), h);
            case DiagramShape.Triangle or DiagramShape.FlippedTriangle:
            {
                var across = Math.Max(w / 0.6, h / 0.4);
                return new Size(across, Math.Max(h / 0.4, across * 0.8));
            }
            case DiagramShape.Brace or DiagramShape.BraceRight: return new Size(w + Curl, h);
            case DiagramShape.Braces: return new Size(w + (Curl * 2), h);
            case DiagramShape.Browser or DiagramShape.Console: return new Size(Math.Max(w, 72), h + Bar);
            case DiagramShape.Folder: return new Size(w, h + 10);
            case DiagramShape.Person: return new Size(w + 10, h + (Head * 0.8));
            case DiagramShape.Cloud: return new Size(w / 0.72, h / 0.6);
            case DiagramShape.Bang: return new Size(w / 0.56, h / 0.52);
            case DiagramShape.Hexagon or DiagramShape.Parallelogram or DiagramShape.ParallelogramAlt
                or DiagramShape.Trapezoid or DiagramShape.TrapezoidAlt:
                return new Size(w + (h / 3 * 2), h);
            default: return new Size(w, h);
        }

        static Size Square(double side) => new(side, side);

        // As tall as a drum has to be for this much room between its lid and its foot, each of which takes 15% of it up to 10.
        static double Drum(double room) => room / 0.7 * 0.15 < 10 ? room / 0.7 : room + 20;
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

        if (Round(shape))
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
                            Brush? fill, DiagramStroke? stroke, DiagramWords? words = null, string wordsKind = MermaidPiece.Words,
                                                    LayoutActions? acts = null)
    {
        if (!Worded(shape)) words = null;

        var outline = Outline(shape, bounds);
        var room = Inside(shape, bounds);
        var at = words is null ? default : new Point(room.X + ((room.Width - words.Width) / 2), room.Y + ((room.Height - words.Height) / 2));

        build.Open(kind, part, stops: Stops.None);
        if (acts is not null) build.Acts(acts);

        // The drawing is a piece of its own, and a leaf: only what draws is pressed, so a shape with words in it stands in its
        // outline through this — less where its words are, which stand in front.
        build.Open(MermaidPiece.Shape, part, stops: Stops.None);
        // A shape given neither a fill nor an outline — words alone — draws nothing, though it still stands where it is.
        if (Filled(shape, fill, stroke) is not null || stroke is not null)
            build.Draw(new GeometryMark(outline, Filled(shape, fill, stroke), stroke?.Ink, stroke?.Thickness ?? 0) { Dashes = stroke?.Dashes });
        if (Details(shape, bounds) is { } details && stroke is not null)
            build.Draw(new GeometryMark(details, Dotted(shape) ? stroke.Ink : null, stroke.Ink, stroke.Thickness));
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
                            double degrees = 0, LayoutActions? acts = null, Brush? band = null)
    {
        if (!Worded(shape)) words = [];
        fill = Filled(shape, fill, stroke);

        var outline = Outline(shape, bounds);
        var over = new GeometryGroup();
        if (covered is not null) over.Children.Add(covered);
        foreach (var (said, at, _) in words) over.Children.Add(new RectangleGeometry(Taken(said, at, degrees)));

        build.Open(kind, part, stops: Stops.None);
        if (acts is not null) build.Acts(acts);

        build.Open(MermaidPiece.Shape, part, stops: Stops.None);
        if (band is not null && Banded(outline, bounds, words, degrees) is { } heading)
        {
            // The band goes over the fill and under the outline, with a rule under it in the outline's ink: the name reads as the
            // top of the box rather than something set on it.
            build.Draw(new GeometryMark(outline, fill, null, 0));
            build.Draw(new GeometryMark(heading.Band, band, null, 0));
            if (stroke is not null) build.Draw(new GeometryMark(heading.Rule, null, stroke.Ink, stroke.Thickness));
            build.Draw(new GeometryMark(outline, null, stroke?.Ink, stroke?.Thickness ?? 0) { Dashes = stroke?.Dashes });
        }
        else if (fill is not null || stroke is not null)
            build.Draw(new GeometryMark(outline, fill, stroke?.Ink, stroke?.Thickness ?? 0) { Dashes = stroke?.Dashes });
        if (Details(shape, bounds) is { } details && stroke is not null)
            build.Draw(new GeometryMark(details, Dotted(shape) ? stroke.Ink : null, stroke.Ink, stroke.Thickness));
        var stands = new CombinedGeometry(GeometryCombineMode.Exclude, outline, over);
        stands.Freeze();
        build.Occupies(stands);
        build.Close();

        foreach (var (said, at, wordsKind) in words) said.Set(build, at, wordsKind, degrees);

        build.Close();
    }

    /// <summary>
    /// The band across the top of a shape its words are set in: from the top down past the words by as much air again as there is
    /// over them, cut to the outline — and the rule under it. Null where nothing is written to set a band behind.
    /// </summary>
    private static (Geometry Band, Geometry Rule)? Banded(Geometry outline, Rect bounds,
                                                          IReadOnlyList<(DiagramWords Words, Point At, string Kind)> words, double degrees)
    {
        if (words.Count == 0) return null;

        var taken = Rect.Empty;
        foreach (var (said, at, _) in words) taken.Union(Taken(said, at, degrees));

        var depth = Math.Min(bounds.Height, taken.Bottom - bounds.Top + Math.Max(0, taken.Top - bounds.Top));
        if (depth <= 0) return null;

        var band = new CombinedGeometry(GeometryCombineMode.Intersect, outline,
                                        new RectangleGeometry(new Rect(bounds.X, bounds.Y, bounds.Width, depth)));
        band.Freeze();

        var rule = new LineGeometry(new Point(bounds.Left, bounds.Y + depth), new Point(bounds.Right, bounds.Y + depth));
        rule.Freeze();

        return (band, rule);
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
            DiagramShape.NotchedPentagon => [new(x + Fold(r), y), new(x + w - Fold(r), y), new(x + w, y + Fold(r)), new(x + w, y + h), new(x, y + h), new(x, y + Fold(r))],
            DiagramShape.SlopedRectangle => [new(x, y + Slope(r)), new(x + w, y), new(x + w, y + h), new(x, y + h)],
            DiagramShape.Triangle => [new(cx, y), new(x + w, y + h), new(x, y + h)],
            DiagramShape.FlippedTriangle => [new(x, y), new(x + w, y), new(cx, y + h)],
            DiagramShape.Bolt => Bolt(r),
            DiagramShape.Folder => Folder(r),
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
