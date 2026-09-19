// LEGACY — frozen. Diagrams move off this code onto the shared layout tree one at a time, built from the Mermaid kit
// (docs/mermaid-diagrams.md); this file goes when the last diagram using it has moved. Read it for what a diagram draws —
// never copy from it, add to it, or use it from code on the shared tree (MermaidDiagramRulesTests).
using Nexaflow.Visuals.Text.Markdown.Graphs.Charts;

namespace Nexaflow.Visuals.Text.Markdown.Graphs;

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
    Document,          // @{ shape: doc }  — rectangle with a wavy bottom
    Card,              // @{ shape: card } — rectangle with a folded top-left corner

    // ── State-diagram pseudostates ───────────────────────────────────────────
    StateStart,        // [*] as a transition source — small filled "initial" dot
    StateEnd,          // [*] as a transition target — ringed "final" dot
    ForkJoin,          // <<fork>> / <<join>> — a solid synchronisation bar
    Note,              // state-diagram / class-diagram note — dashed callout

    // ── Class-diagram ─────────────────────────────────────────────────────────
    ClassBox,          // a UML class: «stereotype» + name + attribute/method compartments (see Node.Class)

    // ── C4 ────────────────────────────────────────────────────────────────────
    C4Element,         // a C4 element card: title + [Kind: technology] + description (see Node.C4)
}

/// <summary>Optional styling for a <see cref="Subgraph"/> box. Null draws the default accent-tinted
/// dashed box; C4 boundaries set it for their own colours and a <c>[type]</c> sub-label.</summary>
public sealed class SubgraphStyle
{
    /// <summary>A smaller second line under the box's title, e.g. <c>[Container]</c>.</summary>
    public string? SubLabel { get; set; }

    public string? FillColor { get; set; }
    public string? StrokeColor { get; set; }
    public string? TextColor { get; set; }
    public EdgeStyle BorderStyle { get; set; } = EdgeStyle.Dashed;

    public SubgraphStyle Copy() => new()
    {
        SubLabel = SubLabel, FillColor = FillColor, StrokeColor = StrokeColor,
        TextColor = TextColor, BorderStyle = BorderStyle,
    };
}

/// <summary>One row of a diagram legend. <see cref="Kind"/> and <see cref="External"/> travel
/// alongside <see cref="FillColor"/> so the swatch matches the colour the cards were drawn in.</summary>
public sealed record GraphLegendEntry(
    string Label,
    string? FillColor,
    string? StrokeColor,
    C4ElementShape? Shape,
    C4ElementKind? Kind = null,
    bool External = false);

public enum EdgeStyle { Solid, Dashed, Dotted, Thick }

/// <summary>Whether a node hides a subtree, and whether that subtree is shown. Modelled on the node
/// rather than its label so every renderer draws the same affordance for it.</summary>
public enum NodeExpansion
{
    /// <summary>Nothing is hidden behind this node — no chip is drawn.</summary>
    Leaf,
    /// <summary>A hidden subtree — drawn with a <c>[+]</c> chip.</summary>
    Collapsed,
    /// <summary>Its subtree is shown — drawn with a <c>[−]</c> chip that closes it again.</summary>
    Expanded,
}

/// <summary>The marker drawn at an edge end. <c>TriangleHollow</c>/<c>DiamondFilled</c>/<c>DiamondHollow</c>
/// are UML class-diagram heads (inheritance / composition / aggregation); <c>CrossCircle</c> is the
/// SysML composite-containment crosshair (requirement diagrams); the <c>Er*</c> markers are ER crow's-foot
/// cardinality (a min indicator — bar for one, circle for zero — plus a max indicator — bar for one, fork
/// for many).</summary>
public enum EdgeArrow
{
    Normal, Open, None, Circle, Cross, TriangleHollow, DiamondFilled, DiamondHollow, CrossCircle,
    ErZeroOne,     // |o  — zero or one  (circle + bar)
    ErExactlyOne,  // ||  — exactly one  (double bar)
    ErZeroMany,    // }o  — zero or more (circle + crow's foot)
    ErOneMany,     // }|  — one or more  (bar + crow's foot)
}
public enum GraphDirection { TopDown, LeftRight, BottomUp, RightLeft }

// ── Core domain types ─────────────────────────────────────────────────────────

/// <summary>A named group of nodes declared by a <c>subgraph … end</c> block (flowchart) or a
/// <c>state X { … }</c> composite (state diagram).</summary>
public sealed class Subgraph
{
    public string       Id      { get; init; } = string.Empty;
    public string       Label   { get; set; } = string.Empty;
    public List<string> NodeIds { get; } = [];
    /// <summary>Id of the enclosing subgraph for true nesting (state composites), or null at the top
    /// level. Flowchart subgraphs leave this null and are laid out as a single level.</summary>
    public string?      ParentId { get; set; }

    /// <summary>Optional per-box styling; null keeps the shared accent-tinted dashed look.</summary>
    public SubgraphStyle? Style { get; set; }

    /// <summary>Where a click on this group's title goes. See <see cref="Node.Href"/>.</summary>
    public string?      Href     { get; set; }

    /// <summary>Tooltip for the link; falls back to the href.</summary>
    public string?      Tooltip  { get; set; }

    /// <summary>A copy carrying every property, optionally with a different membership list — keeps a
    /// property added here from being dropped by some site's hand-written field list.</summary>
    public Subgraph Copy(IEnumerable<string>? nodeIds = null)
    {
        var copy = new Subgraph
        {
            Id       = Id,
            Label    = Label,
            ParentId = ParentId,
            Style    = Style?.Copy(),
            Href     = Href,
            Tooltip  = Tooltip,
        };
        copy.NodeIds.AddRange(nodeIds ?? NodeIds);
        return copy;
    }
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
    /// <summary>Set only on <see cref="NodeShape.ClassBox"/> nodes — the UML class body
    /// (stereotype + attribute/method compartments). Null for every other shape.</summary>
    public ClassInfo? Class    { get; set; }

    /// <summary>Set only on <see cref="NodeShape.C4Element"/> nodes — the C4 card's kind, shape,
    /// technology and description. Null for every other shape.</summary>
    public C4ElementInfo? C4  { get; set; }

    /// <summary>Where a click on this node goes, from a mermaid <c>click</c> directive. Lives on the
    /// graph model, not one renderer, so every diagram type gets it for free.</summary>
    public string? Href { get; set; }

    /// <summary>Tooltip for the link; falls back to the href.</summary>
    public string? Tooltip { get; set; }

    /// <summary>Whether this node hides a subtree, and whether it's shown — drawn as a corner chip,
    /// independent of <see cref="Href"/>, so a node can both navigate and expand in place.</summary>
    public NodeExpansion Expansion { get; set; } = NodeExpansion.Leaf;

    /// <summary>How many nodes sit behind a <see cref="NodeExpansion.Collapsed"/> node, when that is
    /// knowable. Zero means "unknown" — a generated diagram that simply declares a node expandable
    /// has not walked what is behind it.</summary>
    public int HiddenCount { get; set; }

    /// <summary>The producer's own name for this node, echoed back on expand/collapse so a host
    /// needs no side table of mermaid ids. Null → the request carries the id.</summary>
    public string? ExpandKey { get; set; }

    /// <summary>A copy carrying every property. Used when a view of the graph is derived (expansion
    /// pruning) so the parsed graph stays intact and can be re-derived from.</summary>
    public Node Copy() => new()
    {
        Id          = Id,
        Label       = Label,
        Shape       = Shape,
        FillColor   = FillColor,
        StrokeColor = StrokeColor,
        TextColor   = TextColor,
        Classifier  = Classifier,
        Class       = Class,
        C4          = C4,
        Href        = Href,
        Tooltip     = Tooltip,
        Expansion   = Expansion,
        HiddenCount = HiddenCount,
        ExpandKey   = ExpandKey,
    };
}

/// <summary>A directed edge between two nodes.</summary>
public sealed class Edge
{
    public required string SourceId { get; init; }
    public required string TargetId { get; init; }
    public string Label    { get; set; } = string.Empty;
    public EdgeStyle Style  { get; set; } = EdgeStyle.Solid;
    public EdgeArrow Arrow  { get; set; } = EdgeArrow.Normal;
    /// <summary>Head at the source end — set for multidirectional links (<c>o--o</c>, <c>x--x</c>, <c>&lt;--&gt;</c>)
    /// and for the UML head of a class relationship that points at the source (e.g. <c>A &lt;|-- B</c>).</summary>
    public EdgeArrow StartArrow { get; set; } = EdgeArrow.None;
    /// <summary>Multiplicity / cardinality text shown at the source end (class diagrams, e.g. <c>"1"</c>).</summary>
    public string StartLabel { get; set; } = string.Empty;
    /// <summary>Multiplicity / cardinality text shown at the target end (class diagrams, e.g. <c>"*"</c>).</summary>
    public string EndLabel   { get; set; } = string.Empty;
    /// <summary>Set true when cycle removal reverses this edge; renderers should draw the arrowhead reversed.</summary>
    public bool IsReversed  { get; set; }

    /// <summary>A smaller, muted second line under <see cref="Label"/> — a C4 relationship's
    /// <c>[technology]</c>. Null leaves the label as the single line it has always been.</summary>
    public string? SubLabel { get; set; }

    /// <summary>Explicit line colour (a C4 <c>UpdateRelStyle</c>/<c>AddRelTag</c>); null keeps the
    /// colour the edge's <see cref="Style"/> implies.</summary>
    public string? LineColor { get; set; }

    /// <summary>Explicit label colour; null keeps the theme's muted label ink.</summary>
    public string? TextColor { get; set; }

    /// <summary>Where a click on this edge's label goes. See <see cref="Node.Href"/>.</summary>
    public string? Href { get; set; }

    /// <summary>Tooltip for the link; falls back to the href.</summary>
    public string? Tooltip { get; set; }

    /// <summary>A copy carrying every property, optionally re-pointed at different endpoints — the
    /// clustered layout rebuilds each edge against whichever entity represents it at that level.</summary>
    public Edge Copy(string? sourceId = null, string? targetId = null) => new()
    {
        SourceId   = sourceId ?? SourceId,
        TargetId   = targetId ?? TargetId,
        Label      = Label,
        Style      = Style,
        Arrow      = Arrow,
        StartArrow = StartArrow,
        StartLabel = StartLabel,
        EndLabel   = EndLabel,
        SubLabel   = SubLabel,
        LineColor  = LineColor,
        TextColor  = TextColor,
        IsReversed = IsReversed,
        Href       = Href,
        Tooltip    = Tooltip,
    };
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

    /// <summary>Rows for a legend drawn below the diagram, or null for no legend (the default).</summary>
    public List<GraphLegendEntry>? Legend { get; set; }

    /// <summary>How much each legend row says — a C4 <c>SHOW_LEGEND($details)</c>.</summary>
    public C4LegendDetails LegendDetails { get; set; } = C4LegendDetails.Small;

    /// <summary>The document-level properties with none of the content — what a derived view or
    /// per-level rebuild starts from before filling in its own nodes and edges.</summary>
    public Graph CopyShell() => new()
    {
        Title         = Title,
        Direction     = Direction,
        Legend        = Legend,
        LegendDetails = LegendDetails,
    };

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

/// <summary>Implemented by each diagram-language parser. Parsers are stateless.</summary>
public interface IGraphParser
{
    /// <summary>Returns true when this parser handles <paramref name="language"/>.</summary>
    bool CanParse(string language);

    /// <summary>Parse <paramref name="source"/> and return the graph.  Never throws; returns an empty graph on failure.</summary>
    Graph Parse(string source);
}
