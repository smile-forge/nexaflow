using System.Windows;

namespace Nexaflow.Visuals.Text.Markdown.Graphs.Layout;

/// <summary>
/// A node in the layout graph.  Real nodes wrap a <see cref="Node"/>; dummy nodes
/// are inserted by long-edge splitting and have <see cref="Source"/> == null.
/// </summary>
public sealed class LayoutNode
{
    public Node? Source  { get; init; }
    public int   Layer   { get; set; }
    public int   Order   { get; set; }  // position within layer (updated by crossing-min)
    public double X      { get; set; }
    public double Y      { get; set; }
    public double Width  { get; set; }
    public double Height { get; set; }
    public bool IsDummy  { get; init; }
}

/// <summary>
/// A routed edge in the layout graph.  <see cref="Waypoints"/> are screen-space
/// points from the source port through any bend points to the target port.
/// </summary>
public sealed class LayoutEdge
{
    public required LayoutNode From { get; init; }
    public required LayoutNode To   { get; init; }
    /// <summary>The original graph edge (null for intermediate dummy segments — these are merged away before the result is returned).</summary>
    public Edge? Source { get; init; }
    public List<Point> Waypoints { get; } = [];
    /// <summary>Where to anchor the edge's label. Set by parallel-edge separation to stagger the
    /// labels of grouped edges so they don't collide; null → the renderer uses the path midpoint.</summary>
    public Point? LabelAnchor { get; set; }
}

/// <summary>
/// The fully computed layout: positioned nodes grouped by layer and routed edges.
/// </summary>
public sealed class LayoutedGraph
{
    public required Graph Source              { get; init; }
    /// <summary>Layer 0 is topmost (TD) or leftmost (LR).</summary>
    public List<List<LayoutNode>> Layers     { get; } = [];
    public List<LayoutEdge> Edges            { get; } = [];
    public double Width                                  { get; set; }
    public double Height                                 { get; set; }
    public List<(string Label, Rect Bounds)> SubgraphBoxes { get; } = [];
    public IEnumerable<LayoutNode> AllNodes              => Layers.SelectMany(l => l);
}

