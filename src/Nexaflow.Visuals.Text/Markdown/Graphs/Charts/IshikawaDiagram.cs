namespace Nexaflow.Visuals.Text.Markdown.Graphs.Charts;

/// <summary>One bone of an Ishikawa diagram: a category, cause or sub-cause, with arbitrarily nested
/// <see cref="Children"/>.  Structure comes purely from source indentation (see
/// <see cref="Parsers.MermaidIshikawaParser"/>).</summary>
public sealed class IshikawaNode
{
    public required string Text { get; init; }
    public List<IshikawaNode> Children { get; } = [];
}

/// <summary>
/// Data model for a Mermaid <c>ishikawa-beta</c> (fishbone / cause-and-effect) diagram: an
/// <see cref="Head"/> (the effect/problem — the first line) and a set of <see cref="Categories"/>
/// (the main bones off the spine), each holding a nested tree of causes.  Indentation-structured like
/// a mindmap, not an explicit node/edge graph.
/// </summary>
public sealed class IshikawaDiagram
{
    /// <summary>Optional front-matter title rendered above the diagram (Ishikawa has no inline title keyword).</summary>
    public string Title { get; set; } = string.Empty;

    /// <summary>The effect / problem under analysis — the fish head.</summary>
    public string Head { get; set; } = string.Empty;

    /// <summary>Top-level bones (the cause categories).</summary>
    public List<IshikawaNode> Categories { get; } = [];

    public IshikawaConfig Config { get; set; } = new();
}
