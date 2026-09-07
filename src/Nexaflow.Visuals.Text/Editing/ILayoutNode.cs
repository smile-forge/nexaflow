using System.Collections.Generic;
using System.Linq;
using System.Windows;
using Nexaflow.Markdown.Ast;

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

    /// <summary>
    /// Its parent for <em>drawing</em> — what contains it, and what its position is measured inside. Null
    /// at the root.
    /// </summary>
    /// <remarks>
    /// One of three. Containment answers where a thing sits and how much room it takes; it is the wrong
    /// question for a selection, which is about what a thing belongs <em>with</em>. See
    /// <see cref="Across"/> and <see cref="Down"/>.
    /// </remarks>
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
    /// The parent that orders this node among its neighbours <em>sideways</em> — what a selection steps
    /// through when it grows left or right. Null when nothing is beside it, which is most nodes.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <strong>A second parent, and a third in <see cref="Down"/>.</strong> <see cref="Parent"/> answers
    /// what draws this and how much room it takes. That is not what a selection follows: a syllable is
    /// drawn under the note it is sung on and belongs with the <em>other syllables of its verse</em>. One
    /// tree cannot answer both, which is why a note and its lyric could not be picked apart.
    /// </para>
    /// <para>
    /// <strong>It is only ever walked upward.</strong> Selection starts at the node under the pointer,
    /// climbs to the first thing that names a piece of the source, and then asks this parent for the one
    /// after or before it. Nothing starts at the top and comes down, and nothing enumerates a whole run —
    /// so this is a way to take a step, not a group anybody belongs to. That distinction is what keeps it
    /// honest outside a grid: a lyric runs from the start of a tune to the end and across every system,
    /// while a maths block ends at its own edge and a diagram's selection is only ever within one subtree.
    /// None of those is a lane, and imposing one would be a lie about all three.
    /// </para>
    /// <para>
    /// The members are the parent's <see cref="Children"/>, but a member's <see cref="Parent"/> still
    /// points at what draws it. That asymmetry is load-bearing: nothing that walks containment to paint
    /// or to measure can reach one of these, so no node is drawn or descended twice.
    /// </para>
    /// <para>
    /// <strong>Null is a working answer, not a gap.</strong> A node with neither of these selects exactly
    /// as it does today, off the containment tree — which is why LaTeX and the barcodes carried on
    /// unchanged while this arrived.
    /// </para>
    /// </remarks>
    ILayoutNode? Across => null;

    /// <summary>
    /// The parent that orders this node among its neighbours <em>vertically</em> — what a selection steps
    /// through when it grows up or down. A note and the syllables sung on it; a matrix cell and its
    /// column.
    /// </summary>
    /// <remarks>See <see cref="Across"/>: the same idea on the other axis, and the same rules.</remarks>
    ILayoutNode? Down => null;

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
    /// How far this piece has been moved from where it was laid out, taking everything drawn inside it
    /// along. Zero for anything nobody has moved, which is nearly everything.
    ///
    /// <para>
    /// This is why the tree is built as a tree of <em>things</em> rather than a list of marks. A note is
    /// a node holding its head, its stem and its dots; a moment is a node holding the note, the chord
    /// named over it and the words sung under it. Each of those is something a reader might take hold
    /// of, and moving one is a number written here rather than the content laid out again - which is
    /// the difference between dragging a note and re-engraving a page per mouse move.
    /// </para>
    /// <para>
    /// <see cref="Bounds"/> is always where the piece is <em>now</em>, moved or not, so nothing that
    /// hit-tests, selects or measures has to know about this at all. Only a painter does, because the
    /// marks under a moved node were recorded where it used to be.
    /// </para>
    /// </summary>
    Vector Offset => default;

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

