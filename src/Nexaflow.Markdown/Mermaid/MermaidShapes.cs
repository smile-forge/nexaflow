using Nexaflow.Markdown.Ast;

namespace Nexaflow.Markdown.Mermaid;

/// <summary>
/// The shape a node is drawn as, which is what the brackets its label is written in say. Mermaid's set, shared by every
/// diagram whose nodes are written that way — a block diagram's blocks, a flowchart's nodes.
/// </summary>
public enum MermaidShape
{
    /// <summary>No brackets at all, or brackets that say no shape: a plain box.</summary>
    None,

    /// <summary><c>[…]</c></summary>
    Rectangle,

    /// <summary><c>(…)</c></summary>
    Rounded,

    /// <summary><c>([…])</c></summary>
    Stadium,

    /// <summary><c>[[…]]</c></summary>
    Subroutine,

    /// <summary><c>[(…)]</c></summary>
    Cylinder,

    /// <summary><c>((…))</c></summary>
    Circle,

    /// <summary><c>(((…)))</c></summary>
    DoubleCircle,

    /// <summary><c>&gt;…]</c></summary>
    Asymmetric,

    /// <summary><c>{…}</c></summary>
    Diamond,

    /// <summary><c>{{…}}</c></summary>
    Hexagon,

    /// <summary><c>[/…/]</c></summary>
    Parallelogram,

    /// <summary><c>[\…\]</c></summary>
    ParallelogramAlt,

    /// <summary><c>[/…\]</c></summary>
    Trapezoid,

    /// <summary><c>[\…/]</c></summary>
    TrapezoidAlt,

    /// <summary>A page with a wavy foot, which only <c>@{ shape: doc }</c> names.</summary>
    Document,

    /// <summary>A rectangle with a corner folded down, which only <c>@{ shape: card }</c> names.</summary>
    Card,

    /// <summary>A cloud, which only <c>@{ shape: cloud }</c> names.</summary>
    Cloud,

    /// <summary>A starburst, which only <c>@{ shape: bang }</c> names.</summary>
    Bang,

    /// <summary>Words with nothing drawn round them, which only <c>@{ shape: text }</c> names.</summary>
    Text,
}

/// <summary>
/// The brackets a node's label is written in, and the shape each pair says — what <see cref="MermaidOutline.Node"/> reads a
/// label in for a diagram drawing Mermaid's shapes, and what the tree is asked afterwards to learn which one was written.
/// </summary>
public static class MermaidShapes
{
    /// <summary>
    /// Every pair, the longest opening first so the longest one written is the one read. An opening several closings may end
    /// — <c>[/…/]</c> and <c>[/…\]</c> — is written once for each, together.
    /// </summary>
    public static readonly IReadOnlyList<(string Open, string Close, MermaidShape Shape)> Nodes =
    [
        ("(((", ")))", MermaidShape.DoubleCircle),
        ("((", "))", MermaidShape.Circle),
        ("([", "])", MermaidShape.Stadium),
        ("[[", "]]", MermaidShape.Subroutine),
        ("[(", ")]", MermaidShape.Cylinder),
        ("[/", "/]", MermaidShape.Parallelogram),
        ("[/", "\\]", MermaidShape.Trapezoid),
        ("[\\", "\\]", MermaidShape.ParallelogramAlt),
        ("[\\", "/]", MermaidShape.TrapezoidAlt),
        ("{{", "}}", MermaidShape.Hexagon),
        ("[", "]", MermaidShape.Rectangle),
        ("(", ")", MermaidShape.Rounded),
        ("{", "}", MermaidShape.Diamond),
        (">", "]", MermaidShape.Asymmetric),
    ];

    /// <summary>The pairs alone, which is what <see cref="MermaidOutline.Node"/> takes.</summary>
    public static IReadOnlyList<(string Open, string Close)> Brackets { get; } = [.. Nodes.Select(node => (node.Open, node.Close))];

    /// <summary>The shape a node read into the tree is drawn as — <see cref="MermaidShape.None"/> where nothing says one.</summary>
    public static MermaidShape Of(ContentPart? node)
    {
        if (node?.Children.FirstOrDefault(child => child.Kind == MermaidKinds.Label) is not { } label) return MermaidShape.None;

        return Of(label.Children.FirstOrDefault(child => child.Role == Roles.Open)?.Text,
                  label.Children.LastOrDefault(child => child.Role == Roles.Close)?.Text);
    }

    /// <summary>The shape a pair of brackets says — <see cref="MermaidShape.None"/> for a pair that says none.</summary>
    public static MermaidShape Of(string? open, string? close) =>
        Nodes.FirstOrDefault(node => node.Open == open && node.Close == close).Shape;

    /// <summary>
    /// The shape a name written in <c>@{ shape: … }</c> says, with every name Mermaid gives it. Mermaid names some fifty shapes
    /// there, many of them a rectangle or a circle with a detail of its own; a name this has no drawing of its own for comes to the
    /// nearest shape it has, so the node is still drawn as the kind of thing it was asked to be.
    /// </summary>
    public static MermaidShape Named(string? said) => (said ?? string.Empty).Trim().ToLowerInvariant() switch
    {
        "rounded" or "round-rect" or "event" or "curv-trap" or "curved-trapezoid" or "brace" or "brace-l" or "brace-r"
            or "braces" or "comment" => MermaidShape.Rounded,

        "stadium" or "pill" or "terminal" => MermaidShape.Stadium,

        "subroutine" or "subprocess" or "subproc" or "framed-rectangle" or "fr-rect" or "procs" or "processes"
            or "st-rect" or "stacked-rectangle" => MermaidShape.Subroutine,

        "cyl" or "cylinder" or "database" or "db" or "disk" or "disk-storage" or "das" or "h-cyl" or "horizontal-cylinder"
            or "lin-cyl" or "lined-cylinder" or "datastore" or "bucket" => MermaidShape.Cylinder,

        "circle" or "circ" or "sm-circ" or "small-circle" or "start" or "f-circ" or "filled-circle" or "junction"
            => MermaidShape.Circle,

        "dbl-circ" or "double-circle" or "stop" or "fr-circ" or "framed-circle" or "cross-circ" or "crossed-circle"
            or "summary" => MermaidShape.DoubleCircle,

        "diam" or "diamond" or "decision" or "question" => MermaidShape.Diamond,

        "hex" or "hexagon" or "prepare" => MermaidShape.Hexagon,

        "lean-r" or "lean-right" or "in-out" => MermaidShape.Parallelogram,

        "lean-l" or "lean-left" or "out-in" => MermaidShape.ParallelogramAlt,

        "trap-b" or "trapezoid-bottom" or "trapezoid" or "priority" => MermaidShape.Trapezoid,

        "trap-t" or "trapezoid-top" or "inv-trapezoid" or "manual" or "manual-operation" => MermaidShape.TrapezoidAlt,

        "doc" or "document" or "docs" or "documents" or "lin-doc" or "lined-document" or "paper-tape" or "tag-doc"
            or "tagged-document" or "delay" => MermaidShape.Document,

        "card" or "notch-rect" or "notched-rectangle" or "notch-pent" or "loop-limit" or "flip-tri" or "manual-file"
            => MermaidShape.Card,

        "flag" or "tag-rect" or "tagged-process" or "odd" => MermaidShape.Asymmetric,

        "cloud" => MermaidShape.Cloud,

        "bang" => MermaidShape.Bang,

        "text" => MermaidShape.Text,

        _ => MermaidShape.Rectangle,
    };
}
