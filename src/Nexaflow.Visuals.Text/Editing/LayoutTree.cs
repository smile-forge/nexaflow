using System;
using System.Collections.Generic;
using System.Windows;
using Nexaflow.Markdown.Ast;

namespace Nexaflow.Visuals.Text.Editing;

/// <summary>
/// A laid-out piece of content, as it is stored: where it is anchored inside whatever holds it, how big
/// it turned out, how far its subtree runs, and where its drawing is. Nothing else.
///
/// <para>
/// <strong>Two pieces of geometry, and both are needed.</strong> <see cref="Offset"/> is the anchor —
/// where this piece's own frame begins, relative to its parent's — and it is what children and marks are
/// measured from. <see cref="Box"/> is how far the piece actually reaches, relative to that same anchor,
/// and it may begin at a negative offset: an accidental is drawn before the note head it belongs to, and
/// a beam above the stems it joins. One rectangle cannot be both, and making it try means moving the
/// anchor once the contents are known — which would shift every child and every mark already recorded.
/// The anchor is fixed when the piece is opened and never moves; only the box is learnt at the end.
/// </para>
/// <para>
/// No absolute rectangle is kept. It would be a second copy of what the tree already says, and it would
/// have to be rewritten every time a subtree moved or was reused — which is the per-piece work this whole
/// shape exists to avoid. Absolute position is accumulated by whatever is walking, which is already
/// descending and so gets it for nothing. A piece never knows the size of the scene it lands in.
/// </para>
/// <para>
/// Everything <em>editing</em> needs — what a piece was drawn from, what kind of thing it is, what it
/// reads along and stacks with — is kept beside the pieces rather than in them, in
/// <see cref="LayoutTree"/>. Most pieces are a stem or a letter that nobody will ever select, and
/// carrying two object references through the painter's loop for all of them is waste.
/// </para>
/// </summary>
internal readonly record struct Stored
{
    /// <summary>Where this piece's frame begins, relative to its parent's. Fixed when it is opened.</summary>
    public required Vector Offset { get; init; }

    /// <summary>How far it reaches, relative to its own anchor. Learnt when it is closed.</summary>
    public required Rect Box { get; init; }

    /// <summary>The piece holding this one, or -1 at the root.</summary>
    public required int Parent { get; init; }

    /// <summary>
    /// How many pieces this one and everything inside it occupy, so the subtree is the run
    /// <c>[at, at + Extent)</c>. Walking into one is arithmetic rather than a chase through memory, and
    /// copying one is a copy of a block.
    /// </summary>
    public required int Extent { get; init; }

    /// <summary>Where this piece's drawing begins in the tree's marks, and how much of it there is.</summary>
    public required int Marks { get; init; }

    /// <inheritdoc cref="Marks"/>
    public required int MarkCount { get; init; }

    /// <summary>Whether a reader can point at it, as opposed to it being spacing or a container.</summary>
    public required bool IsInk { get; init; }

    /// <summary>Whether a caret inside it is somewhere other than beside it — a script, a fraction.</summary>
    public required bool IsEnclosure { get; init; }
}

/// <summary>
/// A whole laid-out thing: its pieces in one array, its drawing in another, and the few facts about
/// pieces that only selection cares about beside them.
///
/// <para>
/// The pieces are in <strong>pre-order</strong>, so a piece's subtree is the slice starting at it and
/// running for its extent. Descending is arithmetic, taking a piece and everything in it is a range
/// rather than a walk, and lifting a subtree out to reuse it somewhere else is a block copy with one
/// index changed.
/// </para>
/// <para>
/// Nothing in here is content-specific and nothing ever will be: a bar of music, a formula and a barcode
/// are all pieces that drew marks, and everything that hit-tests, selects, measures or paints one has
/// never needed to know which it was looking at.
/// </para>
/// </summary>
public sealed class LayoutTree
{
    private readonly Stored[] _pieces;
    private readonly LayoutMark[] _marks;

    // Beside the pieces rather than in them — see Stored.
    private readonly ISourcePart?[] _parts;
    private readonly string[] _kinds;

    /// <summary>Which run a piece reads along and where in it, and the same downward. -1 for neither.</summary>
    private readonly int[] _across;
    private readonly int[] _acrossAt;
    private readonly int[] _down;
    private readonly int[] _downAt;

    private readonly int[][] _runs;

    internal LayoutTree(Stored[] pieces, LayoutMark[] marks, ISourcePart?[] parts, string[] kinds,
                        int[] across, int[] acrossAt, int[] down, int[] downAt, int[][] runs)
    {
        _pieces = pieces;
        _marks = marks;
        _parts = parts;
        _kinds = kinds;
        _across = across;
        _acrossAt = acrossAt;
        _down = down;
        _downAt = downAt;
        _runs = runs;
    }

    /// <summary>How many pieces there are.</summary>
    public int Count => _pieces.Length;

    /// <summary>The whole of it. Nothing laid out at all has no pieces and no root.</summary>
    public Piece Root => new(this, _pieces.Length == 0 ? -1 : 0);

    /// <summary>A piece by index — how a handle is made, and the only thing an index means.</summary>
    public Piece At(int index) => new(this, index >= 0 && index < _pieces.Length ? index : -1);

    // ── What a piece is ─────────────────────────────────────────────────────

    internal ref readonly Stored Piece(int at) => ref _pieces[at];

    internal ISourcePart? PartOf(int at) => _parts[at];

    internal string KindOf(int at) => _kinds[at];

    internal ReadOnlySpan<LayoutMark> MarksOf(int at) =>
        _marks.AsSpan(_pieces[at].Marks, _pieces[at].MarkCount);

    /// <summary>
    /// Where a piece's frame begins in the content as a whole — its anchor with every parent's added.
    /// What a painter pushes, and what its marks and children are measured from.
    /// </summary>
    internal Vector AnchorOf(int at)
    {
        var anchor = _pieces[at].Offset;
        for (var up = _pieces[at].Parent; up >= 0; up = _pieces[up].Parent) anchor += _pieces[up].Offset;
        return anchor;
    }

    /// <summary>
    /// Where a piece sits in the content as a whole.
    ///
    /// <para>
    /// Worked out rather than stored, which is what keeps a subtree meaningful wherever it is put down.
    /// It is a short climb up an array — a page is about ten deep — and anything asking about many
    /// pieces at once should be descending with <see cref="Within"/> instead, which carries the anchor
    /// down and pays nothing at all.
    /// </para>
    /// </summary>
    internal Rect WhereIs(int at)
    {
        var box = _pieces[at].Box;
        if (box.IsEmpty) return box;

        var anchor = AnchorOf(at);
        return new Rect(box.X + anchor.X, box.Y + anchor.Y, box.Width, box.Height);
    }

    /// <summary>
    /// Every piece of a subtree, with where each one sits — the walk to use when the question is about
    /// more than one piece, because the anchor comes down with it rather than being climbed to each time.
    ///
    /// <para>
    /// Pre-order means a piece is always reached after the one holding it, so the running anchor is only
    /// ever pushed and popped: a piece with anything inside it becomes the frame those pieces are
    /// measured from, and leaving its subtree is a comparison against where that subtree ends. Nothing is
    /// searched for and nothing is climbed.
    /// </para>
    /// </summary>
    internal IEnumerable<(int At, Rect Where)> Within(int from)
    {
        if (from < 0 || from >= _pieces.Length) yield break;

        // The frame each piece is measured from, and where the subtree using it ends. The first is the
        // parent of `from`, which is wherever `from` is anchored less its own offset.
        var frames = new (int End, Vector Anchor)[16];
        frames[0] = (from + _pieces[from].Extent, AnchorOf(from) - _pieces[from].Offset);
        var top = 0;

        for (var at = from; at < frames[0].End; at++)
        {
            while (frames[top].End <= at) top--;

            var piece = _pieces[at];
            var anchor = frames[top].Anchor + piece.Offset;
            var box = piece.Box;

            yield return (at, box.IsEmpty
                ? box
                : new Rect(box.X + anchor.X, box.Y + anchor.Y, box.Width, box.Height));

            if (piece.Extent <= 1) continue;

            if (++top >= frames.Length) Array.Resize(ref frames, frames.Length * 2);
            frames[top] = (at + piece.Extent, anchor);
        }
    }

    // ── What it belongs with ────────────────────────────────────────────────

    /// <summary>
    /// The piece one step along a run from this one, or -1 at either end and for a piece on no run.
    ///
    /// <para>
    /// A run is a list of pieces that read together — a verse of lyrics, the notes of a tune, a row of a
    /// matrix — and it is deliberately only ever asked "what is next to this". It is not a group anybody
    /// belongs to and nothing draws it, which is why it is a list of indices here rather than a piece
    /// pretending to be a parent.
    /// </para>
    /// </summary>
    internal int Along(int at, bool vertical, bool forward)
    {
        var (run, index) = vertical ? (_down[at], _downAt[at]) : (_across[at], _acrossAt[at]);
        if (run < 0) return -1;

        var members = _runs[run];
        var to = index + (forward ? 1 : -1);
        return to < 0 || to >= members.Length ? -1 : members[to];
    }

    /// <summary>Which run a piece reads along, or stacks in — an identity to compare, not a thing.</summary>
    internal int RunOf(int at, bool vertical) => vertical ? _down[at] : _across[at];

    /// <summary>The members of a run, in order.</summary>
    internal ReadOnlySpan<int> Run(int id) => id < 0 || id >= _runs.Length ? default : _runs[id];
}
