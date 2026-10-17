using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Media;

namespace Nexaflow.Features.Pdf.Document;

/// <summary>
/// An outline as a PDF draws it — moves, lines, curves and closes — held as two flat arrays rather than a WPF
/// <see cref="Geometry"/>.
/// <para>
/// A page holds thousands of these (every glyph outline, every rule, every clip), and most are drawn through a
/// transform the shape does not know: a glyph's outline is shared by every place the glyph is set, and the builder
/// decides the scale a page is drawn at. Flat arrays are a fraction of a <see cref="PathGeometry"/>'s size, are
/// immutable without freezing, and stream into a <see cref="StreamGeometry"/> through any matrix in one pass.
/// </para>
/// </summary>
internal sealed class PdfShape
{
    /// <summary>What one step of an outline does, and so how many coordinates it takes.</summary>
    public enum Step : byte
    {
        /// <summary>Starts a new figure at one point.</summary>
        Move,
        /// <summary>A straight line to one point.</summary>
        Line,
        /// <summary>A cubic Bézier through two control points to a third.</summary>
        Cubic,
        /// <summary>A quadratic Bézier through one control point to a second.</summary>
        Quadratic,
        /// <summary>Closes the figure back to where it started.</summary>
        Close,
    }

    /// <summary>The outline with nothing in it.</summary>
    public static PdfShape Empty { get; } = new([], []);

    private readonly Step[] _steps;
    private readonly double[] _coordinates;

    private PdfShape(Step[] steps, double[] coordinates)
    {
        _steps       = steps;
        _coordinates = coordinates;
    }

    /// <summary>The steps, in order.</summary>
    public ReadOnlySpan<Step> Steps => _steps;

    /// <summary>The coordinates every step consumes, x then y, in step order.</summary>
    public ReadOnlySpan<double> Coordinates => _coordinates;

    /// <summary>Whether there is nothing to draw.</summary>
    public bool IsEmpty => _steps.Length == 0;

    /// <summary>How many coordinates (not points) a step consumes.</summary>
    public static int Arity(Step step) => step switch
    {
        Step.Move or Step.Line => 2,
        Step.Cubic             => 6,
        Step.Quadratic         => 4,
        _                      => 0,
    };

    /// <summary>
    /// The smallest rectangle holding every point of the outline once <paramref name="through"/> is applied —
    /// control points included, so it can be larger than the drawn ink but never smaller.
    /// </summary>
    public Rect Bounds(Matrix through)
    {
        if (_coordinates.Length == 0) return Rect.Empty;

        double left = double.PositiveInfinity, top = double.PositiveInfinity;
        double right = double.NegativeInfinity, bottom = double.NegativeInfinity;
        for (var i = 0; i < _coordinates.Length; i += 2)
        {
            var p = through.Transform(new Point(_coordinates[i], _coordinates[i + 1]));
            if (p.X < left) left = p.X;
            if (p.X > right) right = p.X;
            if (p.Y < top) top = p.Y;
            if (p.Y > bottom) bottom = p.Y;
        }

        return new Rect(new Point(left, top), new Point(right, bottom));
    }

    /// <summary>
    /// Writes the outline into <paramref name="into"/> through <paramref name="through"/>. Each figure is declared
    /// filled, and closed when it ends on <see cref="Step.Close"/>, which is what PDF filling and stroking both mean.
    /// </summary>
    public void StreamTo(StreamGeometryContext into, Matrix through)
    {
        var at = 0;
        var figureOpen = false;
        Point start = default;

        for (var i = 0; i < _steps.Length; i++)
        {
            var step = _steps[i];
            switch (step)
            {
                case Step.Move:
                    start = Point(at, through);
                    into.BeginFigure(start, isFilled: true, isClosed: ClosesFigure(i));
                    figureOpen = true;
                    break;
                case Step.Line:
                    if (!figureOpen) { into.BeginFigure(start, true, ClosesFigure(i)); figureOpen = true; }
                    into.LineTo(Point(at, through), isStroked: true, isSmoothJoin: false);
                    break;
                case Step.Cubic:
                    if (!figureOpen) { into.BeginFigure(start, true, ClosesFigure(i)); figureOpen = true; }
                    into.BezierTo(Point(at, through), Point(at + 2, through), Point(at + 4, through), true, false);
                    break;
                case Step.Quadratic:
                    if (!figureOpen) { into.BeginFigure(start, true, ClosesFigure(i)); figureOpen = true; }
                    into.QuadraticBezierTo(Point(at, through), Point(at + 2, through), true, false);
                    break;
                case Step.Close:
                    // A figure that goes on after its close starts again from where it began, as in PostScript.
                    figureOpen = false;
                    break;
            }

            at += Arity(step);
        }
    }

    /// <summary>The outline as a frozen geometry, drawn through <paramref name="through"/>.</summary>
    public StreamGeometry ToGeometry(Matrix through, FillRule rule)
    {
        var geometry = new StreamGeometry { FillRule = rule };
        using (var context = geometry.Open())
            StreamTo(context, through);
        geometry.Freeze();
        return geometry;
    }

    /// <summary>Whether the figure the step at <paramref name="index"/> belongs to ends by closing.</summary>
    private bool ClosesFigure(int index)
    {
        for (var i = index + 1; i < _steps.Length; i++)
        {
            if (_steps[i] == Step.Close) return true;
            if (_steps[i] == Step.Move) return false;
        }

        return false;
    }

    private Point Point(int at, Matrix through) => through.Transform(new Point(_coordinates[at], _coordinates[at + 1]));

    /// <summary>
    /// Gathers an outline step by step. One writer is reused for every path on a page, so the arrays it grows are
    /// paid for once.
    /// </summary>
    public sealed class Writer
    {
        private readonly List<Step> _steps = [];
        private readonly List<double> _coordinates = [];
        private Point _figureStart;
        private Point _current;

        /// <summary>Whether nothing has been written since the last <see cref="Take"/> or <see cref="Clear"/>.</summary>
        public bool IsEmpty => _steps.Count == 0;

        /// <summary>Where the pen is: the end of the last step.</summary>
        public Point Current => _current;

        public void MoveTo(double x, double y)
        {
            _steps.Add(Step.Move);
            _coordinates.Add(x);
            _coordinates.Add(y);
            _figureStart = _current = new Point(x, y);
        }

        public void LineTo(double x, double y)
        {
            _steps.Add(Step.Line);
            _coordinates.Add(x);
            _coordinates.Add(y);
            _current = new Point(x, y);
        }

        public void CubicTo(double x1, double y1, double x2, double y2, double x3, double y3)
        {
            _steps.Add(Step.Cubic);
            _coordinates.Add(x1);
            _coordinates.Add(y1);
            _coordinates.Add(x2);
            _coordinates.Add(y2);
            _coordinates.Add(x3);
            _coordinates.Add(y3);
            _current = new Point(x3, y3);
        }

        public void QuadraticTo(double x1, double y1, double x2, double y2)
        {
            _steps.Add(Step.Quadratic);
            _coordinates.Add(x1);
            _coordinates.Add(y1);
            _coordinates.Add(x2);
            _coordinates.Add(y2);
            _current = new Point(x2, y2);
        }

        public void Close()
        {
            if (_steps.Count == 0 || _steps[^1] == Step.Close) return;
            _steps.Add(Step.Close);
            _current = _figureStart;
        }

        /// <summary>A rectangle as its own closed figure, the way <c>re</c> draws one.</summary>
        public void Rectangle(double x, double y, double width, double height)
        {
            MoveTo(x, y);
            LineTo(x + width, y);
            LineTo(x + width, y + height);
            LineTo(x, y + height);
            Close();
        }

        /// <summary>Appends <paramref name="shape"/> drawn through <paramref name="through"/>.</summary>
        public void Append(PdfShape shape, Matrix through)
        {
            var coordinates = shape.Coordinates;
            var at = 0;
            foreach (var step in shape.Steps)
            {
                switch (step)
                {
                    case Step.Move:
                        var m = through.Transform(new Point(coordinates[at], coordinates[at + 1]));
                        MoveTo(m.X, m.Y);
                        break;
                    case Step.Line:
                        var l = through.Transform(new Point(coordinates[at], coordinates[at + 1]));
                        LineTo(l.X, l.Y);
                        break;
                    case Step.Cubic:
                        var c1 = through.Transform(new Point(coordinates[at], coordinates[at + 1]));
                        var c2 = through.Transform(new Point(coordinates[at + 2], coordinates[at + 3]));
                        var c3 = through.Transform(new Point(coordinates[at + 4], coordinates[at + 5]));
                        CubicTo(c1.X, c1.Y, c2.X, c2.Y, c3.X, c3.Y);
                        break;
                    case Step.Quadratic:
                        var q1 = through.Transform(new Point(coordinates[at], coordinates[at + 1]));
                        var q2 = through.Transform(new Point(coordinates[at + 2], coordinates[at + 3]));
                        QuadraticTo(q1.X, q1.Y, q2.X, q2.Y);
                        break;
                    case Step.Close:
                        Close();
                        break;
                }

                at += Arity(step);
            }
        }

        /// <summary>The outline written so far, leaving the writer empty for the next.</summary>
        public PdfShape Take()
        {
            if (_steps.Count == 0) return Empty;
            var shape = new PdfShape([.. _steps], [.. _coordinates]);
            Clear();
            return shape;
        }

        /// <summary>Drops what was written — a path ended with <c>n</c>, or one that was only ever a clip.</summary>
        public void Clear()
        {
            _steps.Clear();
            _coordinates.Clear();
            _figureStart = _current = default;
        }
    }
}
