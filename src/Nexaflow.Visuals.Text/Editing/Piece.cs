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
    /// One step along a run, or nothing at either end and for a piece on no run — see
    /// <see cref="LayoutTree.Along"/>.
    /// </summary>
    public Piece Step(bool vertical, bool forward) =>
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
