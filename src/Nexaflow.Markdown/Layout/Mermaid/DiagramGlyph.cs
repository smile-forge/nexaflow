using Nexaflow.Markdown.Mermaid;

namespace Nexaflow.Markdown.Layout.Mermaid;

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
    public static LayoutShape Outline(ILayoutMedium medium, DiagramGlyph glyph, LayoutRect bounds)
    {
        // The three a node already draws are drawn by the node, so there is one circle in this assembly.
        switch (glyph)
        {
            case DiagramGlyph.Circle: return DiagramShapes.Outline(medium, DiagramShape.Circle, bounds);
            case DiagramGlyph.Square: return DiagramShapes.Outline(medium, DiagramShape.Rectangle, bounds);
            case DiagramGlyph.Diamond: return DiagramShapes.Outline(medium, DiagramShape.Diamond, bounds);
        }

        return Polygon(medium, Corners(glyph, bounds));
    }

    private static IReadOnlyList<LayoutPoint> Corners(DiagramGlyph glyph, LayoutRect r) => glyph switch
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
    private static readonly IReadOnlyList<LayoutPoint> PlusPoints =
    [
        new(0.5 - Arm, 0), new(0.5 + Arm, 0), new(0.5 + Arm, 0.5 - Arm),
        new(1, 0.5 - Arm), new(1, 0.5 + Arm), new(0.5 + Arm, 0.5 + Arm),
        new(0.5 + Arm, 1), new(0.5 - Arm, 1), new(0.5 - Arm, 0.5 + Arm),
        new(0, 0.5 + Arm), new(0, 0.5 - Arm), new(0.5 - Arm, 0.5 - Arm),
    ];

    /// <summary>A saltire filling the unit square, its points the square's own corners.</summary>
    private static readonly IReadOnlyList<LayoutPoint> CrossPoints =
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
    private static readonly IReadOnlyList<LayoutPoint> HexagonPoints =
    [
        new(0.5, 0), new(1, 0.25), new(1, 0.75), new(0.5, 1), new(0, 0.75), new(0, 0.25),
    ];

    /// <summary>A shape drawn in the unit square, stretched onto the bounds it was asked for.</summary>
    private static IReadOnlyList<LayoutPoint> Unit(LayoutRect r, IReadOnlyList<LayoutPoint> points) =>
        [.. points.Select(point => new LayoutPoint(r.X + (point.X * r.Width), r.Y + (point.Y * r.Height)))];

    private static LayoutShape Polygon(ILayoutMedium medium, IReadOnlyList<LayoutPoint> points)
    {
        var pen = medium.Outline();
        pen.Begin(points[0], filled: true, closed: true);

        for (var at = 1; at < points.Count; at++)
            pen.LineTo(points[at]);

        return pen.Close();
    }
}
