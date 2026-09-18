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
}
