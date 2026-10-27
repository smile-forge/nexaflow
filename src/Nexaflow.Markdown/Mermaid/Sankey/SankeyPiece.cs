namespace Nexaflow.Markdown.Mermaid.Sankey;

/// <summary>The pieces a sankey diagram's layout is made of — its layers, and what is in them.</summary>
public static class SankeyPiece
{
    /// <summary>The ribbons, which are the flows themselves.</summary>
    public const string Flows = "Flows";

    /// <summary>One ribbon. It stands for the row it was written on, so pressing it means that flow.</summary>
    public const string Flow = "Flow";

    /// <summary>The bars, one for each node the flows are written between.</summary>
    public const string Nodes = "Nodes";

    /// <inheritdoc cref="Nodes"/>
    public const string Node = "Node";

    /// <summary>What a node is called, and what it is worth, beside its bar.</summary>
    public const string Label = "Label";
}
