namespace Nexaflow.Features.Markdown.Graphs;

// ── Enumerations ──────────────────────────────────────────────────────────────

public enum NodeShape
{
    Rectangle,         // [label]
    RoundedRect,       // (label)
    Stadium,           // ([label])  — pill / long oval
    Subroutine,        // [[label]]  — double vertical border
    Cylinder,          // [(label)]  — database drum
    Circle,            // ((label))
    DoubleCircle,      // (((label)))
    Asymmetric,        // >label]    — flag / banner
    Diamond,           // {label}
    Hexagon,           // {{label}}
    Parallelogram,     // [/label/]  — lean right
    ParallelogramAlt,  // [\label\]  — lean left
    Trapezoid,         // [/label\]  — wide top
    TrapezoidAlt,      // [\label/]  — wide bottom
}

public enum EdgeStyle { Solid, Dashed, Dotted, Thick }
public enum EdgeArrow { Normal, Open, None, Circle, Cross }
public enum GraphDirection { TopDown, LeftRight, BottomUp, RightLeft }

// ── Core domain types ─────────────────────────────────────────────────────────

/// <summary>A named group of nodes declared by a <c>subgraph … end</c> block.</summary>
public sealed class Subgraph
{
    public string       Id      { get; init; } = string.Empty;
    public string       Label   { get; set; } = string.Empty;
    public List<string> NodeIds { get; } = [];
}

/// <summary>A node in the graph.  All rendering properties are optional hints.</summary>
public sealed class Node
{
    public required string Id  { get; init; }
    public string Label        { get; set; } = string.Empty;
    public NodeShape Shape     { get; set; } = NodeShape.Rectangle;
    public string? FillColor   { get; set; }
    public string? StrokeColor { get; set; }
    public string? TextColor   { get; set; }
    public string? Classifier  { get; set; }  // nomnoml: abstract, note, etc.
}

/// <summary>A directed edge between two nodes.</summary>
public sealed class Edge
{
    public required string SourceId { get; init; }
    public required string TargetId { get; init; }
    public string Label    { get; set; } = string.Empty;
    public EdgeStyle Style  { get; set; } = EdgeStyle.Solid;
    public EdgeArrow Arrow  { get; set; } = EdgeArrow.Normal;
    /// <summary>Set true when cycle removal reverses this edge; renderers should draw the arrowhead reversed.</summary>
    public bool IsReversed  { get; set; }
}

/// <summary>
/// A directed graph.  This is the single canonical intermediate representation
/// shared between all parsers, layout engines, and renderers.
/// </summary>
public sealed class Graph
{
    public string Title              { get; set; } = string.Empty;
    public GraphDirection Direction  { get; set; } = GraphDirection.TopDown;
    public List<Node>     Nodes     { get; } = [];
    public List<Edge>     Edges     { get; } = [];
    public List<Subgraph> Subgraphs { get; } = [];

    public Node? FindNode(string id) =>
        Nodes.FirstOrDefault(n => string.Equals(n.Id, id, StringComparison.Ordinal));

    /// <summary>Returns the node with <paramref name="id"/>, creating it if absent.</summary>
    public Node GetOrAdd(string id, string? label = null)
    {
        var n = FindNode(id);
        if (n is null)
        {
            n = new Node { Id = id, Label = label ?? id };
            Nodes.Add(n);
        }
        else if (label is not null)
        {
            n.Label = label;
        }
        return n;
    }

    public Edge AddEdge(string srcId, string dstId,
        string label = "",
        EdgeStyle style = EdgeStyle.Solid,
        EdgeArrow arrow = EdgeArrow.Normal)
    {
        GetOrAdd(srcId);
        GetOrAdd(dstId);
        var e = new Edge { SourceId = srcId, TargetId = dstId, Label = label, Style = style, Arrow = arrow };
        Edges.Add(e);
        return e;
    }
}

// ── Parser contract ───────────────────────────────────────────────────────────

/// <summary>
/// Implemented by each diagram-language parser.  Parsers are stateless and
/// produce a <see cref="Graph"/> from a raw source string.
/// </summary>
public interface IGraphParser
{
    /// <summary>Returns true when this parser handles <paramref name="language"/>.</summary>
    bool CanParse(string language);

    /// <summary>Parse <paramref name="source"/> and return the graph.  Never throws; returns an empty graph on failure.</summary>
    Graph Parse(string source);
}
