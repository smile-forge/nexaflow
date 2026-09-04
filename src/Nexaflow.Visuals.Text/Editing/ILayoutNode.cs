using System.Collections.Generic;
using System.Linq;
using System.Windows;

namespace Nexaflow.Visuals.Text.Editing;

/// <summary>
/// One piece of laid-out content: where it was drawn, and the part of its content's parse tree it was
/// drawn from.
/// <para>
/// This is the shape every kind of embedded, rendered, editable content shares — a formula, a bar of
/// music, a diagram node, a run of markdown. Each keeps its own layout model (a score's knows about
/// clefs and lyric rows, and should) and implements this <em>over</em> it, so the machinery that decides
/// what a click means, what a drag selected and where the caret goes can be written once.
/// </para>
/// <para>
/// The link back to source is the whole point, and it is a link to a <em>part</em> rather than to a pair
/// of numbers. Selection promotes to whole nodes and takes its range from the parts they were drawn
/// from, so what you copy or replace is what the parser produced — well-formed because it could not be
/// otherwise. Content that cannot say what a node came from cannot join in; that is the one prerequisite
/// for adopting this.
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

    /// <summary>
    /// The part of the content's parse tree this piece was drawn from, or null where it was drawn from
    /// nothing anybody wrote — a fraction's bar, a barcode's guard pattern, spacing, a decoration.
    /// <para>
    /// Populated by the builder for that surface, which is the only thing holding both trees. A piece is
    /// never asked to work its own out: the layout is built <em>from</em> the parse tree, so being told
    /// is the only answer that cannot be wrong. Where a piece has none, where it sits in the source is
    /// the nearest thing above it that has one — see <see cref="LayoutNodeExtensions.Named"/>.
    /// </para>
    /// </summary>
    ISourcePart? Part { get; }

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
    /// <summary>
    /// The part that places this piece in the source: its own, or — where it was drawn from nothing
    /// anybody wrote — the one belonging to whatever it was drawn inside.
    /// <para>
    /// This is the only route from a piece of layout to a position, and it is deliberately indirect. The
    /// layout is geometry: where a thing was drawn and what it drew. Where it was <em>written</em> is a
    /// fact about the parse tree, so it is asked of the parse tree, every time, rather than copied onto
    /// the picture where it would go stale the moment anything is edited.
    /// </para>
    /// </summary>
    public static ISourcePart? Naming(this ILayoutNode node) =>
        node.Part ?? node.Ancestors().FirstOrDefault(a => a.Part is not null)?.Part;

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
    public static bool Stands(this ILayoutNode node) => node.Part is { Length: > 0 } || node.IsInk;

    /// <summary>
    /// Where this piece sits in the source.
    /// <para>
    /// A piece drawn from a part is that part's stretch of it. A piece drawn from nothing anybody wrote — a
    /// fraction's bar, a barcode's guard pattern, a hole waiting to be typed into — is a <em>point</em>, at
    /// the start of whatever it was drawn inside: it stands somewhere without standing for anything, and
    /// that distinction is what the caret turns on. Worked out from the parts on every call; the layout
    /// holds neither number.
    /// </para>
    /// </summary>
    public static SourcePlace Sits(this ILayoutNode node) =>
        node.Part is { } part ? new SourcePlace(part.Start, part.Length)
                              : new SourcePlace(node.Naming()?.Start ?? 0, 0);
}


/// <summary>Convenience over <see cref="ISourcePart"/>, so the arithmetic is written once.</summary>
public static class SourcePartExtensions
{
    /// <summary>One past the last source character this part is named by.</summary>
    public static int End(this ISourcePart part) => part.Start + part.Length;

    /// <summary>Whether this part's stretch of source wholly contains another's.</summary>
    public static bool Covers(this ISourcePart part, ISourcePart other) =>
        other.Start >= part.Start && other.End() <= part.End();
}
