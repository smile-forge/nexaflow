namespace Nexaflow.Markdown.Mermaid.Radar;

/// <summary>The pieces a radar chart's layout is made of — its layers, and what is in them. Its legend is <see cref="DiagramLegend"/>'s.</summary>
public static class RadarPiece
{
    /// <summary>The graticule: the rings behind the curves that mark the scale.</summary>
    public const string Graticule = "Graticule";

    /// <summary>One ring. It stands for the option that shapes the graticule, where one is written.</summary>
    public const string Ring = "Ring";

    /// <summary>The spokes, one per axis.</summary>
    public const string Spokes = "Spokes";

    /// <summary>One spoke. It stands for its axis as it was written, so pressing it means that axis.</summary>
    public const string Spoke = "Spoke";

    /// <summary>The curves, drawn one over another in the order they are written.</summary>
    public const string Curves = "Curves";

    /// <summary>One curve's shape, filled. It stands for the curve as it was written.</summary>
    public const string Curve = "Curve";

    /// <summary>What is written at the end of each spoke.</summary>
    public const string Labels = "Labels";

    /// <summary>An axis's label — or its name, where it has none — typed into where it is drawn.</summary>
    public const string Label = "Label";

    /// <summary>A curve's label — or its name, where it has none — in its legend row.</summary>
    public const string Name = "Name";
}
