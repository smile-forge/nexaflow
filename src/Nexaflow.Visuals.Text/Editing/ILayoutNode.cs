using System.Collections.Generic;
using System.Linq;
using System.Windows;

namespace Nexaflow.Visuals.Text.Editing;

/// <summary>
/// One piece of laid-out content: where it was drawn, and which part of its source produced it.
/// <para>
/// This is the shape every kind of embedded, rendered, editable content shares — a formula, a bar of
/// music, a diagram node, a run of markdown. Each keeps its own layout model (a score's knows about
/// clefs and lyric rows, and should) and implements this <em>over</em> it, so the machinery that decides
/// what a click means, what a drag selected and where the caret goes can be written once.
/// </para>
/// <para>
/// The link back to source is the whole point. Selection promotes to whole nodes and takes its range
/// from them, so what you copy or replace is what the parser produced — well-formed because it could not
/// be otherwise. Content that cannot say where a node came from cannot join in; that is the one
/// prerequisite for adopting this.
/// </para>
/// </summary>
public interface ILayoutNode
{
    /// <summary>Where it sits, in element pixels with the content's top-left at (0,0).</summary>
    Rect Bounds { get; }

    /// <summary>Its parent, or null at the root.</summary>
    ILayoutNode? Parent { get; }

    /// <summary>Its children, in reading order.</summary>
    IReadOnlyList<ILayoutNode> Children { get; }

    /// <summary>Offset of the first source character this node came from.</summary>
    int SourceStart { get; }

    /// <summary>How many source characters it covers.</summary>
    int SourceLength { get; }

    /// <summary>
    /// Whether this node draws something a reader could point at, as opposed to being spacing or a
    /// structural container. Only ink can be clicked, selected or stood beside by a caret.
    /// </summary>
    bool IsInk { get; }

    /// <summary>
    /// Whether a caret inside this is somewhere other than beside it — a script, a fraction, a root:
    /// one thing made of parts, each meaning something to it. A run of terms is not one, and neither is
    /// a box the typesetter made to hold a run.
    /// <para>
    /// It is what says there are two places at the end of <c>x^2</c>. LaTeX lets a one-token argument go
    /// unbraced, so the exponent and the script it belongs to finish at the same character — and without
    /// knowing the script is a thing to be inside of, there is nowhere to say "past it": the caret keeps
    /// the exponent's height and its raised line, and the next arrow leaves the formula (or, in a matrix,
    /// the cell) still wearing them.
    /// </para>
    /// </summary>
    bool IsEnclosure { get; }

    /// <summary>
    /// What kind of thing it is, in the content's own vocabulary — enough to recognise a row or a grid.
    /// Deliberately a string: the shared layer never switches on it, and each content type has its own
    /// set.
    /// </summary>
    string Kind { get; }
}

/// <summary>Convenience over <see cref="ILayoutNode"/> that every implementation would otherwise repeat.</summary>
public static class LayoutNodeExtensions
{
    /// <summary>One past the last source character.</summary>
    public static int SourceEnd(this ILayoutNode node) => node.SourceStart + node.SourceLength;

    /// <summary>Whether this node's source range wholly contains another's.</summary>
    public static bool Covers(this ILayoutNode node, ILayoutNode other) =>
        other.SourceStart >= node.SourceStart && other.SourceEnd() <= node.SourceEnd();

    /// <summary>This node and everything beneath it, parents first.</summary>
    public static IEnumerable<ILayoutNode> SelfAndDescendants(this ILayoutNode node)
    {
        yield return node;
        foreach (var child in node.Children)
            foreach (var descendant in child.SelfAndDescendants())
                yield return descendant;
    }

    /// <summary>Its parent, grandparent and so on, nearest first.</summary>
    public static IEnumerable<ILayoutNode> Ancestors(this ILayoutNode node)
    {
        for (var up = node.Parent; up is not null; up = up.Parent) yield return up;
    }

    /// <summary>
    /// The ink beneath this node — what a reader can actually point at inside it.
    /// <para>
    /// Whether a piece qualifies is decided when the tree is built and recorded on
    /// <see cref="ILayoutNode.IsInk"/>, not re-derived here. It used to also require the piece to cover
    /// some source, which is true of every piece except the one that matters most: a hole covers
    /// nothing by definition, and is the one place the reader has been told to write.
    /// </para>
    /// </summary>
    public static IEnumerable<ILayoutNode> Ink(this ILayoutNode node) =>
        node.SelfAndDescendants().Where(n => n.IsInk);

    /// <summary>
    /// Whether this piece holds a place of its own in the source: a stretch of it, or a hole in it.
    /// What a caret can rest at and a query can land on.
    /// </summary>
    public static bool Stands(this ILayoutNode node) => node.SourceLength > 0 || node.IsInk;
}
