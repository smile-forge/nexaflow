namespace Nexaflow.Markdown.Mermaid.Pie;

/// <summary>The pieces a pie chart's layout is made of — its layers, and what is in them. Its legend is the kit's legend's.</summary>
public static class PiePiece
{
    /// <summary>The wedges, which is the chart itself.</summary>
    public const string Slices = "Slices";

    /// <summary>One wedge. It stands for the whole slice as it was written, so pressing it means that line.</summary>
    public const string Wedge = "Wedge";

    /// <summary>What is written on the wedges — each slice's share.</summary>
    public const string Shares = "Shares";

    /// <inheritdoc cref="Shares"/>
    public const string Share = "Share";

    /// <summary>A slice's label, in its legend row.</summary>
    public const string Label = "Label";

    /// <summary>A slice's value, as it was written — which is what typing in the legend changes.</summary>
    public const string Value = "Value";
}
