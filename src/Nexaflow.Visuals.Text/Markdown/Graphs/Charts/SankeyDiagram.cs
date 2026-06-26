namespace Nexaflow.Visuals.Text.Markdown.Graphs.Charts;

/// <summary>A node in a Sankey diagram — inferred from the source/target columns (no explicit declaration).</summary>
public sealed class SankeyNode
{
    public required string Name { get; init; }
}

/// <summary>One flow (link) between two nodes, carrying a value.  Endpoints are indices into
/// <see cref="SankeyDiagram.Nodes"/>.</summary>
public sealed class SankeyLink
{
    public required int Source { get; init; }
    public required int Target { get; init; }
    public required double Value { get; init; }
}

/// <summary>
/// Data model for a Mermaid <c>sankey</c> diagram: a flow between two sets of values.  Nodes are inferred
/// from the CSV's source/target columns (first-appearance order); each CSV row is one link.  Front-matter
/// <c>config.sankey</c> options live on <see cref="Config"/> (injected by the handler).
/// </summary>
public sealed class SankeyDiagram
{
    /// <summary>Optional front-matter title (Sankey has no inline title keyword).</summary>
    public string Title { get; set; } = string.Empty;

    public List<SankeyNode> Nodes { get; } = [];
    public List<SankeyLink> Links { get; } = [];

    public SankeyConfig Config { get; set; } = new();
}
