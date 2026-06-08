namespace Nexaflow.Visuals.Text.Markdown.Graphs.Charts;

/// <summary>
/// Optional visual overrides for a quadrant point — used both for inline point styling
/// (<c>radius: 12, color: #f00</c>) and reusable <c>classDef</c> definitions.  Every field is
/// nullable so a missing value falls through to the class style, then the renderer default.
/// </summary>
public sealed class QuadrantStyle
{
    public double? Radius      { get; set; }
    public string? FillColor   { get; set; }
    public string? StrokeColor { get; set; }
    public double? StrokeWidth { get; set; }

    public bool IsEmpty => Radius is null && FillColor is null && StrokeColor is null && StrokeWidth is null;

    /// <summary>Returns a copy with every non-null value of <paramref name="over"/> layered on top of this.</summary>
    public QuadrantStyle With(QuadrantStyle over) => new()
    {
        Radius      = over.Radius      ?? Radius,
        FillColor   = over.FillColor   ?? FillColor,
        StrokeColor = over.StrokeColor ?? StrokeColor,
        StrokeWidth = over.StrokeWidth ?? StrokeWidth,
    };
}

/// <summary>One plotted item in a quadrant chart, positioned in the unit square [0,1]×[0,1]
/// (x: 0 = left … 1 = right, y: 0 = bottom … 1 = top).</summary>
public sealed class QuadrantPoint
{
    public required string Label { get; init; }
    public double X { get; init; }
    public double Y { get; init; }

    /// <summary>Resolved visual overrides (inline merged over any referenced class), or null for defaults.</summary>
    public QuadrantStyle? Style { get; set; }
}

/// <summary>
/// Data model for a Mermaid <c>quadrantChart</c>.  Axis labels mark the low/high ends of
/// each axis; the four quadrant labels follow Mermaid's numbering — 1 = top-right, then
/// counter-clockwise: 2 = top-left, 3 = bottom-left, 4 = bottom-right.  Independent of the
/// graph/Sugiyama pipeline (a quadrant chart needs no layout algorithm).
/// </summary>
public sealed class QuadrantChart
{
    public string Title       { get; set; } = string.Empty;

    public string XAxisLeft   { get; set; } = string.Empty;
    public string XAxisRight  { get; set; } = string.Empty;
    public string YAxisBottom { get; set; } = string.Empty;
    public string YAxisTop    { get; set; } = string.Empty;

    public string Quadrant1 { get; set; } = string.Empty; // top-right
    public string Quadrant2 { get; set; } = string.Empty; // top-left
    public string Quadrant3 { get; set; } = string.Empty; // bottom-left
    public string Quadrant4 { get; set; } = string.Empty; // bottom-right

    public List<QuadrantPoint> Points { get; } = [];
}
