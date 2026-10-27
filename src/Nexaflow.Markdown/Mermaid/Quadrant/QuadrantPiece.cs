namespace Nexaflow.Markdown.Mermaid.Quadrant;

/// <summary>The pieces a quadrant chart's layout is made of — its layers, and what is in them.</summary>
public static class QuadrantPiece
{
    /// <summary>The four quadrants, and one — standing for the line writing its caption, where one is written.</summary>
    public const string Quadrants = "Quadrants";
    public const string Quadrant = "Quadrant";

    /// <summary>The line round the chart and the lines between its quadrants.</summary>
    public const string Borders = "Borders";

    /// <summary>The points, and one — standing for the point as written.</summary>
    public const string Points = "Points";
    public const string Point = "Point";

    /// <summary>What is written on the chart: captions, the axes' ends, and the points' names.</summary>
    public const string Words = "Words";
    public const string Caption = "Caption";
    public const string AxisLabel = "AxisLabel";
    public const string Name = "Name";
}
