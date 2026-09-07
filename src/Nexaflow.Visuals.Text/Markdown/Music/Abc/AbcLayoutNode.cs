using System.Windows;
using Nexaflow.Markdown.Ast;
using Nexaflow.Visuals.Text.Editing;

namespace Nexaflow.Visuals.Text.Markdown.Music.Abc;

/// <summary>
/// A piece of an engraved tune: where it sits, what part of the ABC it was drawn from, and what it drew.
///
/// <para>
/// Holding the drawing on the node is what lets a score be painted by walking its own tree rather than by
/// engraving it again. One pass produces structure, geometry and picture together, so they cannot
/// disagree — and a single note can be repainted, or washed for a selection, on its own.
/// </para>
/// <para>
/// <strong>Ink is what carries a part.</strong> A note head, a rest, a bar line: things somebody wrote and
/// can therefore point at, select and edit. A staff line, a stem, a beam, a ledger line and the clef carry
/// none — nobody typed them, they are how the notation is drawn — so they are drawn and are not
/// selectable, which is the honest thing to say about a rule the engraver added. Pointing at one still
/// means something: the shared queries resolve it to the nearest thing above it that <em>was</em> written.
/// </para>
/// </summary>
internal sealed class AbcLayoutNode : LayoutNode
{
    public AbcLayoutNode(Rect bounds, string kind, ISourcePart? part = null)
        : base(bounds, part, kind, isInk: part is { Length: > 0 })
    {
    }

    /// <summary>
    /// The part of the parse tree this piece was drawn from, typed — the link back that says what it
    /// <em>is</em> rather than merely where it came from.
    /// <para>
    /// The same reference <see cref="ILayoutNode.Part"/> carries. A <see cref="ContentPart"/> already
    /// answers what the editing seam asks of it, so there is no adapter between the two and nothing that
    /// could disagree. One would be worth writing the moment a construct is <em>named by</em> something
    /// other than the characters it spans — as a braced LaTeX argument is by its contents, so that
    /// replacing it does not re-brace what is already braced. ABC has no construct like that: every edit
    /// here rewrites a leaf of a note.
    /// </para>
    /// </summary>
    public ContentPart? Origin => Part as ContentPart;

    /// <summary>Adds a child and adopts it, giving back the child so a builder can go on filling it.</summary>
    public AbcLayoutNode Adding(AbcLayoutNode child)
    {
        Add(child);
        Covering(child.Bounds);
        return child;
    }

    /// <summary>
    /// Gathers pieces this one already holds under a new piece of their own — see
    /// <see cref="LayoutNode.Regroup{TGroup}"/>. A tie is the reason: it joins two notes and can only be
    /// built once both of them are on the page.
    /// </summary>
    public AbcLayoutNode Gathering(IReadOnlyList<AbcLayoutNode> members, string kind) =>
        Regroup(members, new AbcLayoutNode(Rect.Empty, kind));

    /// <summary>
    /// Grows this piece to cover something it drew, and everything holding it to cover this.
    ///
    /// <para>
    /// Upward, because a piece is made before it is filled — a note before its head, a bar before its
    /// notes — so growing only the piece itself would leave every container the size it was when it was
    /// made. Each drawing says once where it went and the whole chain above it learns; the alternative
    /// is a <c>foreach child</c> at the end of every method that builds one, and the one that gets
    /// forgotten is a container that reports a rectangle smaller than what it holds.
    /// </para>
    /// </summary>
    public void Covering(Rect what)
    {
        if (what.IsEmpty || what.Width < 0 || what.Height < 0) return;

        var union = Bounds;
        if (union.IsEmpty || (union.Width == 0 && union.Height == 0)) Bounds = what;
        else
        {
            union.Union(what);
            Bounds = union;
        }

        (Parent as AbcLayoutNode)?.Covering(what);
    }
}
