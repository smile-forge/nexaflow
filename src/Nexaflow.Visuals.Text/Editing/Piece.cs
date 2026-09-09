using System;
using System.Collections;
using System.Collections.Generic;
using System.Windows;
using Nexaflow.Markdown.Ast;

namespace Nexaflow.Visuals.Text.Editing;

/// <summary>
/// One piece of laid-out content, seen: where it was drawn, what it was drawn from, and what it belongs
/// with.
///
/// <para>
/// A handle — the tree it is in and its place in that tree — rather than an object of its own. Identity
/// is the index, which is what lets a piece be a value: two handles are the same piece when they name the
/// same place in the same tree, and a piece left over from a layout that has since been rebuilt can say
/// so instead of quietly pointing at something abandoned.
/// </para>
/// <para>
/// This is the shape every kind of embedded, rendered, editable content shares. Each keeps its own model
/// of what it is — a score knows about clefs and lyric rows, and should — and builds <em>into</em> a
/// <see cref="LayoutTree"/>, so the machinery that decides what a click means, what a drag selected and
/// where the caret goes is written once and never asks what it is looking at.
/// </para>
/// </summary>
public readonly record struct Piece
{
    private readonly LayoutTree? _tree;
    private readonly int _at;

    internal Piece(LayoutTree tree, int at)
    {
        _tree = at < 0 ? null : tree;
        _at = at < 0 ? -1 : at;
    }

    /// <summary>Whether this names a piece at all. A step off the end of a run does not.</summary>
    public bool Exists => _tree is not null;

    /// <summary>The tree it belongs to — what says whether it is still the one in hand.</summary>
    public LayoutTree? Tree => _tree;

    /// <summary>Where its own frame begins, relative to its parent's — see <see cref="Stored.Offset"/>.</summary>
    public Vector Offset => _tree is null ? default : _tree.Piece(_at).Offset;

    /// <summary>How far it reaches, relative to its own anchor. May begin at a negative offset.</summary>
    public Rect Box => _tree is null ? Rect.Empty : _tree.Piece(_at).Box;

    /// <summary>Where it sits in the content as a whole. See <see cref="LayoutTree.WhereIs"/>.</summary>
    public Rect Bounds => _tree is null ? Rect.Empty : _tree.WhereIs(_at);

    /// <summary>What it was drawn from, or null where nobody wrote it — a stem, a beam, a ledger line.</summary>
    public ISourcePart? Part => _tree?.PartOf(_at);

    /// <summary>What kind of thing it is, in the content's own vocabulary.</summary>
    public string Kind => _tree is null ? "" : _tree.KindOf(_at);

    /// <summary>Where it is in its tree — its identity, and what a run is a list of.</summary>
    public int At => _at;

    /// <summary>
    /// The part that places this piece in the source: its own, or — where it was drawn from nothing
    /// anybody wrote — the one belonging to whatever it was drawn inside.
    ///
    /// <para>
    /// The only route from a piece of layout to a position, and deliberately indirect. The layout is
    /// geometry: where a thing was drawn and what it drew. Where it was <em>written</em> is a fact about
    /// the parse tree, so it is asked of the parse tree every time rather than copied onto the picture,
    /// where it would go stale the moment anything is edited.
    /// </para>
    /// </summary>
    public ISourcePart? Naming()
    {
        if (Part is { } mine) return mine;
        foreach (var up in Ancestors())
            if (up.Part is { } theirs) return theirs;
        return null;
    }

    /// <summary>
    /// Whether this piece holds a place of its own in the source: a stretch of it, or a hole in it. What a
    /// caret can rest at and a query can land on.
    /// </summary>
    public bool Stands => Part is { Length: > 0 } || IsInk;

    /// <summary>
    /// Where this piece sits in the source.
    ///
    /// <para>
    /// A piece drawn from a part is that part's stretch of it. A piece drawn from nothing anybody wrote — a
    /// fraction's bar, a barcode's guard pattern, a hole waiting to be typed into — is a <em>point</em>, at
    /// the start of whatever it was drawn inside: it stands somewhere without standing for anything, and
    /// that distinction is what the caret turns on.
    /// </para>
    /// </summary>
    public SourcePlace Sits() =>
        Part is { } part ? new SourcePlace(part.Start, part.Length)
                         : new SourcePlace(Naming()?.Start ?? 0, 0);

    /// <summary>Whether a reader can point at it, as opposed to it being spacing or a container.</summary>
    public bool IsInk => _tree is not null && _tree.Piece(_at).IsInk;

    /// <summary>Whether a caret inside it is somewhere other than beside it — a script, a fraction.</summary>
    public bool IsEnclosure => _tree is not null && _tree.Piece(_at).IsEnclosure;

    /// <summary>What it drew, in the order it drew it, in its own frame.</summary>
    public ReadOnlySpan<LayoutMark> Marks => _tree is null ? default : _tree.MarksOf(_at);

    /// <summary>What holds it, or nothing at the root.</summary>
    public Piece Parent => _tree is null ? default : _tree.At(_tree.Piece(_at).Parent);

    /// <summary>What it holds, in reading order.</summary>
    public PieceChildren Children => new(_tree, _at);

    /// <summary>It and everything inside it, parents first — a slice of the tree, not a walk of it.</summary>
    public PieceSubtree SelfAndDescendants => new(_tree, _at);

    /// <summary>Its parent, grandparent and so on, nearest first.</summary>
    public IEnumerable<Piece> Ancestors()
    {
        for (var up = Parent; up.Exists; up = up.Parent) yield return up;
    }

    /// <summary>
    /// The ink inside it — what a reader can actually point at.
    ///
    /// <para>
    /// Whether a piece qualifies was decided when the tree was built. It used to also require the piece to
    /// cover some source, which is true of every piece except the one that matters most: a hole covers
    /// nothing by definition, and is the one place the reader has been told to write.
    /// </para>
    /// </summary>
    public IEnumerable<Piece> Ink()
    {
        foreach (var piece in SelfAndDescendants)
            if (piece.IsInk) yield return piece;
    }

    /// <summary>How many pieces hold it. Nought at the root.</summary>
    public int Depth => _tree is null ? 0 : _tree.DepthOf(_at);

    /// <summary>
    /// It and everything inside it, each with where it sits — the walk to use whenever the question is
    /// about more than one piece.
    ///
    /// <para>
    /// <see cref="Bounds"/> climbs to the root, so asking it of every piece in turn is the one way to make
    /// relative geometry cost something. This descends instead, carrying the anchor down with it, and pays
    /// nothing per piece.
    /// </para>
    /// </summary>
    public IEnumerable<(Piece Piece, Rect Where)> Placed()
    {
        if (_tree is null) yield break;
        foreach (var (at, where) in _tree.Within(_at)) yield return (new Piece(_tree, at), where);
    }

    /// <summary>
    /// The pieces that read together with this one, in order — the notes of a tune, a verse of lyrics, the
    /// things stacked at one moment. Itself alone when it is on no run, which is a run of one and the right
    /// answer for a note with nothing named over it and nothing sung under it.
    /// </summary>
    public IReadOnlyList<Piece> Sharing(bool vertical)
    {
        if (_tree is null) return [];

        var run = _tree.Run(_tree.RunOf(_at, vertical));
        if (run.Length == 0) return [this];

        var members = new Piece[run.Length];
        for (var at = 0; at < run.Length; at++) members[at] = new Piece(_tree, run[at]);
        return members;
    }

    /// <summary>
    /// One step along a run, or nothing at either end and for a piece on no run — see
    /// <see cref="LayoutTree.Along"/>.
    /// </summary>
    public Piece Along(bool vertical, bool forward) =>
        _tree is null ? default : _tree.At(_tree.Along(_at, vertical, forward));

    /// <summary>Which run it reads along, or stacks in — an identity to compare, not a thing.</summary>
    public int RunOn(bool vertical) => _tree is null ? -1 : _tree.RunOf(_at, vertical);

    public override string ToString() =>
        _tree is null ? "nothing" : $"{Kind}{(Part is { } p ? $"[{p.Start},{p.Length}]" : "")} {Bounds}";

    /// <summary>The pieces one piece holds. A struct, and walked without allocating.</summary>
    public readonly struct PieceChildren(LayoutTree? tree, int at) : IEnumerable<Piece>
    {
        public Enumerator GetEnumerator() => new(tree, at);
        IEnumerator<Piece> IEnumerable<Piece>.GetEnumerator() => GetEnumerator();
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

        /// <summary>How many, for the rare caller that wants the number rather than the pieces.</summary>
        public int Count
        {
            get
            {
                var count = 0;
                foreach (var _ in this) count++;
                return count;
            }
        }

        /// <summary>
        /// The first child is the next piece along; the one after it is that child's whole subtree away.
        /// Which is the whole of why the pieces are kept in pre-order.
        /// </summary>
        public struct Enumerator(LayoutTree? tree, int at) : IEnumerator<Piece>
        {
            private readonly int _last = tree is null ? 0 : at + tree.Piece(at).Extent;
            private int _next = tree is null ? 0 : at + 1;
            private int _now = -1;

            public readonly Piece Current => tree!.At(_now);
            readonly object IEnumerator.Current => Current;

            public bool MoveNext()
            {
                if (tree is null || _next >= _last) return false;

                _now = _next;
                _next += tree.Piece(_now).Extent;
                return true;
            }

            public void Reset() => throw new NotSupportedException();
            public readonly void Dispose() { }
        }
    }

    /// <summary>A piece and everything inside it — a contiguous run of the tree.</summary>
    public readonly struct PieceSubtree(LayoutTree? tree, int at) : IEnumerable<Piece>
    {
        public Enumerator GetEnumerator() => new(tree, at);
        IEnumerator<Piece> IEnumerable<Piece>.GetEnumerator() => GetEnumerator();
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

        public struct Enumerator(LayoutTree? tree, int at) : IEnumerator<Piece>
        {
            private readonly int _last = tree is null ? 0 : at + tree.Piece(at).Extent;
            private int _now = at - 1;

            public readonly Piece Current => tree!.At(_now);
            readonly object IEnumerator.Current => Current;

            public bool MoveNext() => tree is not null && ++_now < _last;

            public void Reset() => throw new NotSupportedException();
            public readonly void Dispose() { }
        }
    }
}
