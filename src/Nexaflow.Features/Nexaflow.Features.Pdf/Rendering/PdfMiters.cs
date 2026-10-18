using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Media;
using Nexaflow.Features.Pdf.Document;

namespace Nexaflow.Features.Pdf.Rendering;

/// <summary>
/// PDF's mitred joins as WPF can draw them. A join whose miter would reach past the miter limit PDF bevels (8.4.3.5);
/// WPF instead cuts the miter off at the limit, so a stroke that doubles back on itself — a rule drawn as a short line
/// there and back, thick enough to be a bar — grows a spike the limit's length that can black out everything around it.
/// Both measure the limit alike, the miter's length over the line's width, so a stroke none of whose joins reach past
/// it draws as PDF says with WPF's own miter; one that has any is drawn bevelled, and its joins within the limit given
/// their miters back.
/// </summary>
internal static class PdfMiters
{
    // A turn this slight is a straight line on: there is no corner to fill.
    private const double Straight = 1e-9;

    /// <summary>
    /// The miters <paramref name="shape"/>'s bevelled joins lack, through <paramref name="through"/> and for a line
    /// <paramref name="width"/> wide there: <see langword="null"/> where no join reaches past <paramref name="pen"/>'s
    /// limit, and WPF's miter draws the stroke as it is; otherwise each join within the limit, and inside a dash, as the
    /// triangle between its bevel and its miter point.
    /// </summary>
    /// <param name="dashes">The dash lengths in the same space, or empty for a solid line.</param>
    /// <param name="phase">How far into the dashes each figure starts, in the same space.</param>
    public static Geometry? Restored(PdfShape shape, Matrix through, double width, PdfPen pen, IReadOnlyList<double> dashes,
        double phase)
    {
        var half = width / 2;
        var wedges = new StreamGeometry { FillRule = FillRule.Nonzero };
        var beyond = false;

        using (var context = wedges.Open())
        {
            foreach (var figure in Figures(shape, through))
            {
                var along = 0.0;
                var count = figure.Closed ? figure.Segments.Count : figure.Segments.Count - 1;
                for (var i = 0; i < count; i++)
                {
                    var into = figure.Segments[i];
                    var outOf = figure.Segments[(i + 1) % figure.Segments.Count];
                    along += into.Length;

                    var turn = Cross(into.Leaving, outOf.Entering);
                    var on = Dot(into.Leaving, outOf.Entering);
                    if (Math.Abs(turn) < Straight && on > 0) continue;

                    // The miter's length over the line's width: 1 / sin(φ / 2), φ the angle between the two segments.
                    var sine = Math.Sqrt(Math.Max(0, (1 + on) / 2));
                    if (sine * pen.MiterLimit < 1)
                    {
                        beyond = true;
                        continue;
                    }

                    if (!InDash(along, dashes, phase)) continue;

                    var outer = turn > 0 ? 1 : -1;
                    var first = new Vector(into.Leaving.Y, -into.Leaving.X) * outer;
                    var second = new Vector(outOf.Entering.Y, -outOf.Entering.X) * outer;
                    var bisector = first + second;
                    bisector.Normalize();

                    var corner = into.End;
                    context.BeginFigure(corner + first * half, isFilled: true, isClosed: true);
                    context.LineTo(corner + bisector * (half / sine), false, false);
                    context.LineTo(corner + second * half, false, false);
                }
            }
        }

        if (!beyond) return null;

        wedges.Freeze();
        return wedges;
    }

    /// <summary>Whether a point <paramref name="along"/> a figure falls in a dash rather than a gap.</summary>
    private static bool InDash(double along, IReadOnlyList<double> dashes, double phase)
    {
        if (dashes.Count == 0) return true;

        // An odd number of lengths is repeated to make the pattern, as PDF repeats it (8.4.3.6).
        var cycle = 0.0;
        var repeat = dashes.Count % 2 == 0 ? 1 : 2;
        for (var r = 0; r < repeat; r++) foreach (var length in dashes) cycle += length;
        if (cycle <= 0) return true;

        var at = (along + phase) % cycle;
        if (at < 0) at += cycle;
        for (var i = 0; ; i++)
        {
            var length = dashes[i % dashes.Count];
            if (at < length) return i % 2 == 0;
            at -= length;
        }
    }

    /// <summary>
    /// The figures of <paramref name="shape"/> through <paramref name="through"/>, each its segments that have a
    /// direction (a segment of no length makes no join), and whether it is closed.
    /// </summary>
    private static List<Figure> Figures(PdfShape shape, Matrix through)
    {
        var figures = new List<Figure>();
        var steps = shape.Steps;
        var coordinates = shape.Coordinates;
        Figure? figure = null;
        Point start = default, now = default;
        var at = 0;

        for (var i = 0; i < steps.Length; i++)
        {
            var step = steps[i];
            switch (step)
            {
                case PdfShape.Step.Move:
                    figure = null;
                    start = now = At(coordinates, at, through);
                    break;
                case PdfShape.Step.Line:
                    Add(ref figure, figures, Segment.Straight(now, At(coordinates, at, through)));
                    now = At(coordinates, at, through);
                    break;
                case PdfShape.Step.Cubic:
                    Add(ref figure, figures, Segment.Curved(now, At(coordinates, at, through), At(coordinates, at + 2, through), At(coordinates, at + 4, through)));
                    now = At(coordinates, at + 4, through);
                    break;
                case PdfShape.Step.Quadratic:
                    var control = At(coordinates, at, through);
                    var end = At(coordinates, at + 2, through);
                    Add(ref figure, figures, Segment.Curved(now, now + (control - now) * 2 / 3, end + (control - end) * 2 / 3, end));
                    now = end;
                    break;
                case PdfShape.Step.Close:
                    if (figure is not null)
                    {
                        if (Segment.Straight(now, start) is { } closing) figure.Segments.Add(closing);
                        figure.Closed = true;
                    }

                    // A figure that goes on after its close starts again from where it began, as in PostScript.
                    figure = null;
                    now = start;
                    break;
            }

            at += PdfShape.Arity(step);
        }

        figures.RemoveAll(static f => f.Segments.Count == 0);
        return figures;
    }

    private static void Add(ref Figure? figure, List<Figure> figures, Segment? segment)
    {
        if (figure is null)
        {
            figure = new Figure();
            figures.Add(figure);
        }

        if (segment is { } made) figure.Segments.Add(made);
    }

    private static Point At(ReadOnlySpan<double> coordinates, int index, Matrix through) =>
        new Point(coordinates[index], coordinates[index + 1]) * through;

    private static double Cross(Vector a, Vector b) => a.X * b.Y - a.Y * b.X;

    private static double Dot(Vector a, Vector b) => a.X * b.X + a.Y * b.Y;

    private sealed class Figure
    {
        public List<Segment> Segments { get; } = [];

        public bool Closed { get; set; }
    }

    /// <summary>A segment with a direction: where it ends, its unit tangent entering and leaving, and its length.</summary>
    private readonly record struct Segment(Point End, Vector Entering, Vector Leaving, double Length)
    {
        public static Segment? Straight(Point from, Point to)
        {
            var direction = to - from;
            var length = direction.Length;
            if (length <= 0) return null;

            direction /= length;
            return new Segment(to, direction, direction, length);
        }

        /// <summary>
        /// A cubic, its tangents taken from the nearest control point that is not its end, and its length from a polyline
        /// through it fine enough for a dash to fall within a fraction of where it does.
        /// </summary>
        public static Segment? Curved(Point from, Point first, Point second, Point to)
        {
            var entering = Direction(from, first, second, to);
            var leaving = Direction(to, second, first, from);
            if (entering is null || leaving is null) return null;

            const int pieces = 16;
            var length = 0.0;
            var last = from;
            for (var k = 1; k <= pieces; k++)
            {
                var t = (double)k / pieces;
                var u = 1 - t;
                var point = new Point(
                    u * u * u * from.X + 3 * u * u * t * first.X + 3 * u * t * t * second.X + t * t * t * to.X,
                    u * u * u * from.Y + 3 * u * u * t * first.Y + 3 * u * t * t * second.Y + t * t * t * to.Y);
                length += (point - last).Length;
                last = point;
            }

            return new Segment(to, entering.Value, -leaving.Value, length);
        }

        private static Vector? Direction(Point end, Point near, Point far, Point other)
        {
            foreach (var toward in new[] { near, far, other })
            {
                var direction = toward - end;
                if (direction.Length > 0)
                {
                    direction.Normalize();
                    return direction;
                }
            }

            return null;
        }
    }
}

