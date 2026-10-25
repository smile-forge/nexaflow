namespace Nexaflow.Markdown.Mermaid.Xy;

/// <summary>The pieces an xychart's layout is made of — its layers, and what is in them. Its legend is <see cref="DiagramLegend"/>'s.</summary>
public static class XyPiece
{
    /// <summary>The plot's background, where the front matter colours it.</summary>
    public const string Plot = "Plot";

    /// <summary>The bars, and one bar — standing for its value as written, so pressing it means that value.</summary>
    public const string Bars = "Bars";
    public const string Bar = "Bar";

    /// <summary>The lines, and one line — standing for its series.</summary>
    public const string Lines = "Lines";
    public const string Trace = "Trace";

    /// <summary>A dot on a line where one of its values is — standing for that value as written, so pressing it means that value.</summary>
    public const string Dot = "Dot";

    /// <summary>The x-axis — the categories, or the numbers the values stand over — and the y-axis, the numbers they reach.</summary>
    public const string XAxis = "XAxis";
    public const string YAxis = "YAxis";

    /// <summary>What is written at a tick: a category, typed into, or a number worked out.</summary>
    public const string Tick = "Tick";

    /// <summary>An axis's title.</summary>
    public const string AxisTitle = "AxisTitle";

    /// <summary>What is written on the plot: each line's point labels, and each bar's value where the front matter asks.</summary>
    public const string Labels = "Labels";
    public const string Label = "Label";
    public const string Value = "Value";

    /// <summary>A series' name, in its legend row.</summary>
    public const string Name = "Name";
}
