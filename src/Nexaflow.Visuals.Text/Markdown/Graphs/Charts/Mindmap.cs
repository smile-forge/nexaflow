namespace Nexaflow.Visuals.Text.Markdown.Graphs.Charts;

/// <summary>Node shape in a mindmap, from the text delimiters (<c>[…] (…) ((…)) {{…}} )…( ))…((</c>).</summary>
public enum MindmapShape { Default, Square, Rounded, Circle, Bang, Cloud, Hexagon }

/// <summary>One node in a mindmap tree, with its laid-out position and branch (colour) index.</summary>
public sealed class MindmapNode
{
    public required string Text { get; init; }
    public MindmapShape Shape { get; set; } = MindmapShape.Default;
    public MindmapNode? Parent { get; set; }
    public List<MindmapNode> Children { get; } = [];

    public int Depth       { get; set; }
    public int BranchIndex { get; set; } = -1;   // top-level branch this belongs to (-1 = root)

    // Layout (filled by the renderer).
    public double X, Y, W, H;
}

/// <summary>
/// Data model for a Mermaid <c>mindmap</c>: a single-rooted tree whose hierarchy is defined by
/// source indentation.  The renderer lays it out as a left/right balanced tidy tree.
/// </summary>
public sealed class Mindmap
{
    public string Title { get; set; } = string.Empty;
    public MindmapNode? Root { get; set; }

    /// <summary>Depth-first enumeration of every node (root first).</summary>
    public IEnumerable<MindmapNode> All()
    {
        if (Root is null) yield break;
        var stack = new Stack<MindmapNode>();
        stack.Push(Root);
        while (stack.Count > 0)
        {
            var n = stack.Pop();
            yield return n;
            for (int i = n.Children.Count - 1; i >= 0; i--) stack.Push(n.Children[i]);
        }
    }
}
