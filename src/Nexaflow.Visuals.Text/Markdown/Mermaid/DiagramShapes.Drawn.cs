using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Media;

namespace Nexaflow.Visuals.Text.Markdown.Mermaid;

/// <summary>
/// The shapes Mermaid names only in <c>@{ shape: … }</c>: the flowchart symbols each drawn as its own outline, what it draws
/// inside that outline, and how it is painted.
/// </summary>
internal static partial class DiagramShapes
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
    private static Size? Fixed(DiagramShape shape) => shape switch
    {
        DiagramShape.SmallCircle or DiagramShape.FramedCircle or DiagramShape.FilledCircle => new Size(14, 14),
        DiagramShape.CrossedCircle => new Size(36, 36),
        DiagramShape.Hourglass => new Size(26, 26),
        DiagramShape.Bolt => new Size(24, 48),
        DiagramShape.Fork => new Size(60, 8),
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
    private static Brush? Filled(DiagramShape shape, Brush? fill, DiagramStroke? stroke) => shape switch
    {
        DiagramShape.FilledCircle or DiagramShape.Fork => stroke?.Ink ?? fill,
        DiagramShape.Brace or DiagramShape.BraceRight or DiagramShape.Braces => null,
        _ => fill,
    };

    /// <summary>Whether what a shape draws inside its outline is filled in its ink — a framed circle's dot.</summary>
    private static bool Dotted(DiagramShape shape) => shape == DiagramShape.FramedCircle;

    // ── Outlines ────────────────────────────────────────────────────────────

    /// <summary>A rectangle rounded off at its right end.</summary>
    private static Geometry Delay(Rect r)
    {
        var round = Math.Min(r.Height / 2, r.Width);
        var shape = new StreamGeometry();
        using (var pen = shape.Open())
        {
            pen.BeginFigure(r.TopLeft, isFilled: true, isClosed: true);
            pen.LineTo(new Point(r.Right - round, r.Top), true, false);
            pen.ArcTo(new Point(r.Right - round, r.Bottom), new Size(round, r.Height / 2), 0, false, SweepDirection.Clockwise, true, true);
            pen.LineTo(r.BottomLeft, true, false);
        }

        return shape;
    }

    /// <summary>Pointed at its left end and rounded at its right, as a display is drawn.</summary>
    private static Geometry CurvedTrapezoid(Rect r)
    {
        var k = Pointed(r);
        var shape = new StreamGeometry();
        using (var pen = shape.Open())
        {
            pen.BeginFigure(new Point(r.Left, r.Top + (r.Height / 2)), isFilled: true, isClosed: true);
            pen.LineTo(new Point(r.Left + k, r.Top), true, false);
            pen.LineTo(new Point(r.Right - k, r.Top), true, false);
            pen.ArcTo(new Point(r.Right - k, r.Bottom), new Size(k, r.Height / 2), 0, false, SweepDirection.Clockwise, true, true);
            pen.LineTo(new Point(r.Left + k, r.Bottom), true, false);
        }

        return shape;
    }

    /// <summary>Both ends bowing to the left, as stored data is drawn: the near end out, the far end in.</summary>
    private static Geometry BowTie(Rect r)
    {
        var d = Bowed(r);
        var reach = new Size(d, r.Height / 2);
        var shape = new StreamGeometry();
        using (var pen = shape.Open())
        {
            pen.BeginFigure(new Point(r.Left + d, r.Top), isFilled: true, isClosed: true);
            pen.LineTo(r.TopRight, true, false);
            pen.ArcTo(r.BottomRight, reach, 0, false, SweepDirection.Counterclockwise, true, true);
            pen.LineTo(new Point(r.Left + d, r.Bottom), true, false);
            pen.ArcTo(new Point(r.Left + d, r.Top), reach, 0, false, SweepDirection.Clockwise, true, true);
        }

        return shape;
    }

    /// <summary>A band whose top and foot wave together, as paper tape is drawn.</summary>
    private static Geometry Flag(Rect r)
    {
        // Each wave is a cubic whose handles are a height k either side of its ends, which bows out 0.2887 k at most: the top waves
        // from the top of the bounds down twice that, and the foot the same way up from the bottom.
        var wave = Wave(r);
        var handle = wave / 0.2887;
        var (top, foot) = (r.Top + wave, r.Bottom - wave);

        var shape = new StreamGeometry();
        using (var pen = shape.Open())
        {
            pen.BeginFigure(new Point(r.Left, top), isFilled: true, isClosed: true);
            pen.BezierTo(new Point(r.Left + (r.Width / 3), top - handle), new Point(r.Right - (r.Width / 3), top + handle),
                         new Point(r.Right, top), true, true);
            pen.LineTo(new Point(r.Right, foot), true, false);
            pen.BezierTo(new Point(r.Right - (r.Width / 3), foot + handle), new Point(r.Left + (r.Width / 3), foot - handle),
                         new Point(r.Left, foot), true, true);
        }

        return shape;
    }

    /// <summary>A cylinder lying on its side, its near end towards the right.</summary>
    private static Geometry HorizontalCylinder(Rect r)
    {
        var c = Cap(r);
        var reach = new Size(c, r.Height / 2);
        var shape = new StreamGeometry();
        using (var pen = shape.Open())
        {
            pen.BeginFigure(new Point(r.Left + c, r.Top), isFilled: true, isClosed: true);
            pen.LineTo(new Point(r.Right - c, r.Top), true, false);
            pen.ArcTo(new Point(r.Right - c, r.Bottom), reach, 0, false, SweepDirection.Clockwise, true, true);
            pen.LineTo(new Point(r.Left + c, r.Bottom), true, false);
            pen.ArcTo(new Point(r.Left + c, r.Top), reach, 0, false, SweepDirection.Clockwise, true, true);
        }

        return shape;
    }

    /// <summary>The far edge of a lying cylinder's near end, which its outline does not draw.</summary>
    private static Geometry Face(Rect r)
    {
        var c = Cap(r);
        var shape = new StreamGeometry();
        using (var pen = shape.Open())
        {
            pen.BeginFigure(new Point(r.Right - c, r.Top), isFilled: false, isClosed: false);
            pen.ArcTo(new Point(r.Right - c, r.Bottom), new Size(c, r.Height / 2), 0, false, SweepDirection.Counterclockwise, true, true);
        }

        return shape;
    }

    /// <summary>A band filled right across and ruled only along its top and its foot, as a data flow diagram's store is drawn.</summary>
    private static Geometry DataStore(Rect r)
    {
        var shape = new StreamGeometry();
        using (var pen = shape.Open())
        {
            pen.BeginFigure(r.TopLeft, isFilled: true, isClosed: true);
            pen.LineTo(r.TopRight, true, false);
            pen.LineTo(r.BottomRight, false, false);
            pen.LineTo(r.BottomLeft, true, false);
            pen.LineTo(r.TopLeft, false, false);
        }

        return shape;
    }

    /// <summary>A pail: the far rim of its open top, and its sides narrowing to its foot.</summary>
    private static Geometry Bucket(Rect r)
    {
        var (lid, lip) = (Lid(r), Lip(r));
        var shape = new StreamGeometry();
        using (var pen = shape.Open())
        {
            pen.BeginFigure(new Point(r.Left, r.Top + lid), isFilled: true, isClosed: true);
            pen.ArcTo(new Point(r.Right, r.Top + lid), new Size(r.Width / 2, lid), 0, false, SweepDirection.Clockwise, true, true);
            pen.LineTo(new Point(r.Right - lip, r.Bottom), true, false);
            pen.LineTo(new Point(r.Left + lip, r.Bottom), true, false);
        }

        return shape;
    }

    /// <summary>
    /// A curly brace on either side of the words or both. Only the braces are drawn; the rest of the bounds is a figure filled and
    /// never stroked, so the shape stands in all of it, as every other shape does — and a brace is given no fill to paint it with.
    /// </summary>
    private static Geometry Braced(DiagramShape which, Rect r)
    {
        var shape = new StreamGeometry();
        using (var pen = shape.Open())
        {
            pen.BeginFigure(r.TopLeft, isFilled: true, isClosed: true);
            pen.PolyLineTo([r.TopRight, r.BottomRight, r.BottomLeft, r.TopLeft], isStroked: false, isSmoothJoin: false);

            if (which is DiagramShape.Brace or DiagramShape.Braces) Curly(pen, r, r.Left, 1);
            if (which is DiagramShape.BraceRight or DiagramShape.Braces) Curly(pen, r, r.Right, -1);
        }

        return shape;
    }

    /// <summary>One curly brace down the height of <paramref name="r"/>, its point at <paramref name="tip"/> and its arms reaching <paramref name="toward"/> the words.</summary>
    private static void Curly(StreamGeometryContext pen, Rect r, double tip, int toward)
    {
        var bend = Math.Min(Curl / 2, r.Height / 4);
        var (arm, spine, middle) = (tip + (toward * Curl), tip + (toward * Curl / 2), r.Top + (r.Height / 2));

        pen.BeginFigure(new Point(arm, r.Top), isFilled: false, isClosed: false);
        pen.QuadraticBezierTo(new Point(spine, r.Top), new Point(spine, r.Top + bend), true, true);
        pen.LineTo(new Point(spine, middle - bend), true, true);
        pen.QuadraticBezierTo(new Point(spine, middle), new Point(tip, middle), true, true);
        pen.QuadraticBezierTo(new Point(spine, middle), new Point(spine, middle + bend), true, true);
        pen.LineTo(new Point(spine, r.Bottom - bend), true, true);
        pen.QuadraticBezierTo(new Point(spine, r.Bottom), new Point(arm, r.Bottom), true, true);
    }

    /// <summary>A head over a rounded body, the words in the body.</summary>
    private static Geometry Person(Rect r)
    {
        var body = Body(r);
        var round = Rounding(body);

        return Union(new EllipseGeometry(Crown(r)), new RectangleGeometry(body, round, round));
    }

    /// <summary>Sheets stacked one behind another, the back one first: each up and to the right of the one in front of it.</summary>
    private static IReadOnlyList<Rect> Stacked(Rect r)
    {
        var (w, h) = (Math.Max(0, r.Width - (Stack * 2)), Math.Max(0, r.Height - (Stack * 2)));

        return [new Rect(r.X + (Stack * 2), r.Y, w, h), new Rect(r.X + Stack, r.Y + Stack, w, h), new Rect(r.X, r.Y + (Stack * 2), w, h)];
    }

    /// <summary>
    /// The edges of a stack's sheets that show behind the one in front — each sheet's outline where nothing in front of it covers
    /// it, which stroking what is left of the sheet once those in front are taken out of it draws.
    /// </summary>
    private static Geometry Behind(IReadOnlyList<Geometry> sheets)
    {
        var edges = new GeometryGroup();
        for (var at = 0; at < sheets.Count - 1; at++)
            edges.Children.Add(new CombinedGeometry(GeometryCombineMode.Exclude, sheets[at], Union([.. sheets.Skip(at + 1)])));

        return edges;
    }

    /// <summary>A browser window's bar: the rule under it, three dots, and the address box.</summary>
    private static Geometry Browser(Rect r)
    {
        var chrome = Group(Segment(r.Left, r.Top + Bar, r.Right, r.Top + Bar));
        for (var dot = 0; dot < 3; dot++)
            chrome.Children.Add(new EllipseGeometry(new Point(r.Left + 8 + (dot * 6), r.Top + (Bar / 2)), 1.5, 1.5));

        var address = Box(r.Left + (r.Width * 0.45), r.Top + (Bar * 0.28), (r.Width * 0.55) - 8, Bar * 0.44);
        chrome.Children.Add(new RectangleGeometry(address, 2, 2));

        return chrome;
    }

    /// <summary>A console's prompt in its top corner.</summary>
    private static Geometry Prompt(Rect r)
    {
        var prompt = new StreamGeometry();
        using (var pen = prompt.Open())
        {
            pen.BeginFigure(new Point(r.Left + 8, r.Top + 5), isFilled: false, isClosed: false);
            pen.PolyLineTo([new Point(r.Left + 12, r.Top + 8), new Point(r.Left + 8, r.Top + 11)], isStroked: true, isSmoothJoin: false);
        }

        return Group(prompt, Segment(r.Left + 14, r.Top + 11, r.Left + 20, r.Top + 11));
    }

    /// <summary>A circle crossed through from corner to corner.</summary>
    private static Geometry Crossed(Rect r)
    {
        var (cx, cy) = (r.X + (r.Width / 2), r.Y + (r.Height / 2));
        var (a, b) = (r.Width / 2 * Math.Sqrt(0.5), r.Height / 2 * Math.Sqrt(0.5));

        return Group(Segment(cx - a, cy - b, cx + a, cy + b), Segment(cx - a, cy + b, cx + a, cy - b));
    }

    /// <summary>A lightning bolt's corners, as fractions of its bounds.</summary>
    private static IReadOnlyList<Point> Bolt(Rect r) =>
        [.. new (double X, double Y)[] { (0.7, 0), (0, 0.58), (0.45, 0.58), (0.3, 1), (1, 0.4), (0.55, 0.4) }
            .Select(at => new Point(r.X + (at.X * r.Width), r.Y + (at.Y * r.Height)))];

    /// <summary>A folder's corners: its tab along the top of its left end, sloping down to the rest of it.</summary>
    private static IReadOnlyList<Point> Folder(Rect r)
    {
        var (tab, wide) = (Tab(r), Math.Min(r.Width * 0.4, 48));

        return [r.TopLeft, new(r.Left + Math.Max(0, wide - tab), r.Top), new(r.Left + wide, r.Top + tab), new(r.Right, r.Top + tab), r.BottomRight, r.BottomLeft];
    }

    /// <summary>How far down a document's foot is at <paramref name="along"/> — the wave <see cref="Document"/> draws.</summary>
    private static double WaveAt(Rect r, double along)
    {
        // Its handles are spaced evenly across, so how far along the curve a point is is how far across it is.
        var t = r.Width <= 0 ? 0 : Math.Clamp((r.Right - along) / r.Width, 0, 1);

        return r.Bottom - Wave(r) + (3 * Wave(r) / 0.2887 * t * (1 - t) * ((2 * t) - 1));
    }

    private static LineGeometry Segment(double x1, double y1, double x2, double y2) => new(new Point(x1, y1), new Point(x2, y2));

    private static GeometryGroup Group(params Geometry[] parts)
    {
        var group = new GeometryGroup();
        foreach (var part in parts) group.Children.Add(part);
        return group;
    }

    private static Geometry Union(params Geometry[] shapes) => United(Group(shapes));

    /// <summary>A rectangle, none of it less than nothing across or down.</summary>
    private static Rect Box(double x, double y, double w, double h) => new(x, y, Math.Max(0, w), Math.Max(0, h));

    // ── Proportions ─────────────────────────────────────────────────────────

    /// <summary>How far down a divided rectangle's line is.</summary>
    private static double Divide(Rect r) => r.Height * 0.2;

    /// <summary>How far a sloped rectangle's top falls from its right end to its left.</summary>
    private static double Slope(Rect r) => r.Height * 0.3;

    /// <summary>How far a display's point reaches, and how far its round end does.</summary>
    private static double Pointed(Rect r) => Math.Min(r.Height / 3, r.Width / 3);

    /// <summary>How far stored data's ends bow.</summary>
    private static double Bowed(Rect r) => Math.Min(r.Height / 4, r.Width / 4);

    /// <summary>How far a lying cylinder's ends reach.</summary>
    private static double Cap(Rect r) => Math.Min(r.Height * 0.25, r.Width * 0.15);

    /// <summary>How far a pail's foot is in from the ends of its rim.</summary>
    private static double Lip(Rect r) => r.Width * 0.12;

    /// <summary>How big a turned corner is — a tagged rectangle's, a tagged document's.</summary>
    private static double Tag(Rect r) => Math.Min(10, r.Height * 0.25);

    /// <summary>How deep a folder's tab is.</summary>
    private static double Tab(Rect r) => Math.Min(10, r.Height * 0.2);

    /// <summary>Where a person's head is: in the middle across, at the top.</summary>
    private static Rect Crown(Rect r)
    {
        var side = Math.Min(Head, Math.Min(r.Width, r.Height) / 2);
        return new Rect(r.X + ((r.Width - side) / 2), r.Y, side, side);
    }

    /// <summary>Where a person's body is: under their head, which sits a little into it.</summary>
    private static Rect Body(Rect r)
    {
        var shoulders = Crown(r).Height * 0.8;
        return Box(r.X, r.Y + shoulders, r.Width, r.Height - shoulders);
    }

    private static double Rounding(Rect body) => Math.Min(10, body.Height / 2);
}
