namespace Nexaflow.Visuals.Text.Markdown.Graphs.Rendering;

/// <summary>Per-render hooks for <see cref="WpfGraphRenderer"/>.</summary>
public sealed class GraphRenderOptions
{
    /// <summary>Click hook for nodes / class-member rows carrying an <c>href</c>.</summary>
    public Func<string, bool>? OnNavigate { get; init; }

    /// <summary>
    /// Click hook for a node's expand chip, given the node id. Null → no chip is drawn: an
    /// affordance that promises to open something and then cannot is worse than none.
    /// </summary>
    public Func<string, bool>? OnToggleExpand { get; init; }

    /// <summary>
    /// Click hook for the body of a node, given its id. When set it replaces following the node's
    /// <c>href</c> — the host decides what a click means (select, open, both) and every node becomes
    /// clickable, not just the ones carrying a link.
    /// </summary>
    public Func<string, bool>? OnNodeClick { get; init; }

    /// <summary>The node drawn as selected: it and the edges touching it are picked out, so a line
    /// can be followed across a diagram too dense to trace by eye.</summary>
    public string? SelectedNodeId { get; init; }

    /// <summary>Cap on the scroller wrapped around the canvas by <see cref="WpfGraphRenderer.Render"/>.</summary>
    public double MaxHeight { get; init; } = 600;

    public static readonly GraphRenderOptions None = new();
}
