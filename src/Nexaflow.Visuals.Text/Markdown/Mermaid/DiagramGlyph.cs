using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Media;

namespace Nexaflow.Visuals.Text.Markdown.Mermaid;

/// <summary>
/// A mark drawn at a point: what a scatter plot tells its groups apart by.
///
/// <para>
/// Deliberately not a <see cref="DiagramShape"/>, though three of them draw as one. A node shape is
/// <em>sized around words</em> — <see cref="DiagramShapes.Around"/> and <see cref="DiagramShapes.Inside"/>
/// exist to make one big enough to hold a label, and every shape is held to it. A glyph is the opposite
/// thing: a mark a few pixels across that holds nothing at all, and whose whole job is to stay legible
/// at that size. Putting the two in one enum makes every glyph owe an answer to a question it has no
/// business being asked.
/// </para>
/// <para>
/// Where a glyph and a node shape are the same outline, the outline is the node shape's — there is no
/// second circle here.
/// </para>
/// </summary>
internal enum DiagramGlyph
{
    Circle,
    Square,
    Diamond,
    Triangle,
    TriangleDown,
    Plus,
    Cross,

    /// <summary>A regular hexagon standing on a point, which is what a hexagonal bin is drawn as.</summary>
    Hexagon,
}

/// <summary>The outlines <see cref="DiagramGlyph"/> draws, and the order a chart takes them in.</summary>
internal static class DiagramGlyphs
{
    /// <summary>
    /// The glyphs in the order a chart hands them out, so the first group is a circle wherever it
    /// appears — the order ggplot2 uses, which puts the two most distinct marks first.
    /// </summary>
    public static readonly IReadOnlyList<DiagramGlyph> Order =
    [
        DiagramGlyph.Circle, DiagramGlyph.Triangle, DiagramGlyph.Square, DiagramGlyph.Plus,
        DiagramGlyph.Cross, DiagramGlyph.Diamond, DiagramGlyph.TriangleDown, DiagramGlyph.Hexagon,
    ];

    /// <summary>The glyph a place in the order takes. The order repeats rather than running out.</summary>
    public static DiagramGlyph At(int order) => Order[((order % Order.Count) + Order.Count) % Order.Count];

    /// <summary>The glyph a name stands for, or null where it names none.</summary>
    public static DiagramGlyph? Named(string? name) => name?.Trim().ToLowerInvariant() switch
    {
        "circle" or "o" => DiagramGlyph.Circle,
        "square" => DiagramGlyph.Square,
        "diamond" => DiagramGlyph.Diamond,
        "triangle" or "triangleup" or "triangle-up" => DiagramGlyph.Triangle,
        "triangledown" or "triangle-down" => DiagramGlyph.TriangleDown,
        "plus" or "+" => DiagramGlyph.Plus,
        "cross" or "x" => DiagramGlyph.Cross,
        "hexagon" or "hex" => DiagramGlyph.Hexagon,
        _ => null,
    };

    /// <summary>What a glyph's names are, for saying what a block could have written instead.</summary>
    public static readonly string Names = "circle, square, diamond, triangle, triangle-down, plus, cross, hexagon";

    /// <summary>The outline of a glyph filling <paramref name="bounds"/>.</summary>
    public static Geometry Outline(DiagramGlyph glyph, Rect bounds)
    {
        // The three a node already draws are drawn by the node, so there is one circle in this assembly.
        switch (glyph)
        {
            case DiagramGlyph.Circle: return DiagramShapes.Outline(DiagramShape.Circle, bounds);
            case DiagramGlyph.Square: return DiagramShapes.Outline(DiagramShape.Rectangle, bounds);
            case DiagramGlyph.Diamond: return DiagramShapes.Outline(DiagramShape.Diamond, bounds);
        }

        var outline = Polygon(Corners(glyph, bounds));
        outline.Freeze();
        return outline;
    }

    private static IReadOnlyList<Point> Corners(DiagramGlyph glyph, Rect r) => glyph switch
    {
        DiagramGlyph.Triangle => [new(r.X + (r.Width / 2), r.Y), new(r.Right, r.Bottom), new(r.X, r.Bottom)],
        DiagramGlyph.TriangleDown => [new(r.X, r.Y), new(r.Right, r.Y), new(r.X + (r.Width / 2), r.Bottom)],
        DiagramGlyph.Plus => Unit(r, PlusPoints),
        DiagramGlyph.Hexagon => Unit(r, HexagonPoints),
        _ => Unit(r, CrossPoints),
    };

    /// <summary>
    /// How thick the arms of a plus or a saltire are, as a share of the whole — chosen so the mark still
    /// reads as itself at the few pixels a scatter plot draws one at.
    /// </summary>
    private const double Arm = 0.19;

    /// <summary>A plus filling the unit square, clockwise from the top left of its upright arm.</summary>
    private static readonly IReadOnlyList<Point> PlusPoints =
    [
        new(0.5 - Arm, 0), new(0.5 + Arm, 0), new(0.5 + Arm, 0.5 - Arm),
        new(1, 0.5 - Arm), new(1, 0.5 + Arm), new(0.5 + Arm, 0.5 + Arm),
        new(0.5 + Arm, 1), new(0.5 - Arm, 1), new(0.5 - Arm, 0.5 + Arm),
        new(0, 0.5 + Arm), new(0, 0.5 - Arm), new(0.5 - Arm, 0.5 - Arm),
    ];

    /// <summary>A saltire filling the unit square, its points the square's own corners.</summary>
    private static readonly IReadOnlyList<Point> CrossPoints =
    [
        new(0, 0), new(Arm, 0), new(0.5, 0.5 - Arm), new(1 - Arm, 0),
        new(1, 0), new(1, Arm), new(0.5 + Arm, 0.5), new(1, 1 - Arm),
        new(1, 1), new(1 - Arm, 1), new(0.5, 0.5 + Arm), new(Arm, 1),
        new(0, 1), new(0, 1 - Arm), new(0.5 - Arm, 0.5), new(0, Arm),
    ];

    /// <summary>
    /// A hexagon standing on a point, filling the unit square. Regular where the box it is given is as
    /// wide as a hexagon of that height — which is what the hexagonal binning hands it.
    /// </summary>
    private static readonly IReadOnlyList<Point> HexagonPoints =
    [
        new(0.5, 0), new(1, 0.25), new(1, 0.75), new(0.5, 1), new(0, 0.75), new(0, 0.25),
    ];

    /// <summary>A shape drawn in the unit square, stretched onto the bounds it was asked for.</summary>
    private static IReadOnlyList<Point> Unit(Rect r, IReadOnlyList<Point> points) =>
        [.. points.Select(point => new Point(r.X + (point.X * r.Width), r.Y + (point.Y * r.Height)))];

    private static Geometry Polygon(IReadOnlyList<Point> points)
    {
        var figure = new PathFigure { StartPoint = points[0], IsClosed = true, IsFilled = true };

        for (var at = 1; at < points.Count; at++)
            figure.Segments.Add(new LineSegment(points[at], true));

        var path = new PathGeometry();
        path.Figures.Add(figure);

        return path;
    }
}
