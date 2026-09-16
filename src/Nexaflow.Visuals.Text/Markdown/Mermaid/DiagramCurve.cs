using System.Collections.Generic;
using System.Windows;
using System.Windows.Media;

namespace Nexaflow.Visuals.Text.Markdown.Mermaid;

/// <summary>
/// A closed shape through points, in order: straight from each to the next, or rounded through every one — a radar's
/// curves, and its graticule drawn as polygons.
///
/// <para>
/// Rounded the way Mermaid rounds them, so a shape drawn here meets every point Mermaid's does and bulges no further: each
/// stretch between two points is a cubic Bézier whose handles lie along the line joining the points either side of it,
/// <c>tension</c> of that line's length out. A sixth is a Catmull-Rom spline; nought is the straight polygon.
/// </para>
/// </summary>
internal static class DiagramCurve
{
    /// <summary>
    /// The closed shape through <paramref name="points"/>, rounded by <paramref name="tension"/> — nought for straight lines
    /// between them. Fewer than three points are joined straight, having no corner to round; no points are no shape.
    /// </summary>
    public static Geometry Closed(IReadOnlyList<Point> points, double tension = 0)
    {
        if (points.Count == 0) return Geometry.Empty;

        var count = points.Count;
        var round = count >= 3 && tension != 0;

        var shape = new StreamGeometry();
        using (var pen = shape.Open())
        {
            pen.BeginFigure(points[0], isFilled: true, isClosed: true);

            for (var at = 0; at < count; at++)
            {
                var from = points[at];
                var to = points[(at + 1) % count];

                if (!round)
                {
                    // The figure closes itself back to the first point.
                    if (at + 1 < count) pen.LineTo(to, isStroked: true, isSmoothJoin: false);
                    continue;
                }

                var before = points[(at - 1 + count) % count];
                var after = points[(at + 2) % count];
                pen.BezierTo(from + ((to - before) * tension), to - ((after - from) * tension), to, isStroked: true, isSmoothJoin: true);
            }
        }

        shape.Freeze();
        return shape;
    }
}
