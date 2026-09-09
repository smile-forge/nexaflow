using System;
using System.Collections.Generic;
using System.Windows;
using Nexaflow.Markdown.Ast;

namespace Nexaflow.Visuals.Text.Editing;

/// <summary>
/// How a <see cref="LayoutTree"/> is made: open a piece, draw into it, put pieces inside it, close it.
///
/// <para>
/// <strong>A piece is finished when it is closed, and never touched again.</strong> Its anchor is fixed
/// when it opens, because everything inside is measured from that; how far it reaches is worked out when
/// it closes, from what it turned out to hold. That is the whole difference from what came before, where
/// a bar existed before its notes and grew as they arrived — and it is what makes a subtree a thing that
/// can be lifted out and put down somewhere else, because nothing in it refers to where it used to be.
/// </para>
/// <para>
/// The pieces come out in pre-order with each one knowing how far its subtree runs, which is the shape
/// that makes descending arithmetic and reuse a block copy. Getting that from a builder that meets
/// parents first is a matter of claiming the piece's slot as it opens and filling it in as it closes —
/// the children have landed in between, immediately after it, exactly where they belong.
/// </para>
/// </summary>
public sealed class LayoutBuilder
{
    private readonly List<Stored> _pieces = [];
    private readonly List<ISourcePart?> _parts = [];
    private readonly List<string> _kinds = [];

    private readonly List<LayoutMark> _marks = [];
    private readonly List<(int[] Members, bool Vertical)> _runs = [];

    private readonly Stack<Frame> _open = new();
    private readonly Stack<Frame> _spare = new();

    /// <summary>
    /// A piece being built: where it is, what it has drawn so far, and how far that reaches.
    ///
    /// <para>
    /// The marks are held here rather than appended straight to the tree because a piece's drawing has to
    /// end up contiguous, and a piece that draws, puts something inside itself, then draws again would
    /// otherwise have its own marks split around its child's. Frames are pooled, so a page of ten
    /// thousand pieces makes about ten of these.
    /// </para>
    /// </summary>
    private sealed class Frame
    {
        public int At;
        public Vector Offset;
        public Rect Box;
        public bool IsInk;
        public bool IsEnclosure;
        public readonly List<LayoutMark> Marks = [];

        public void Covers(Rect what)
        {
            if (what.IsEmpty || (what.Width <= 0 && what.Height <= 0)) return;
            Box = Box.IsEmpty ? what : Rect.Union(Box, what);
        }
    }

    /// <param name="isInk">
    /// Whether a reader can point at it. Unstated it follows the part, which is the ordinary convention —
    /// something a reader typed is something they can point at, and a beam or a guard pattern the drawing
    /// invented is not. Stated, the content knows better: a hole waiting to be typed into is pointable and
    /// covers nothing, and a printed check digit nobody wrote is still a digit on the page.
    /// </param>

    /// <summary>
    /// Opens a piece anchored at <paramref name="at"/> inside whatever is already open, and gives back
    /// where it will live. Everything drawn or opened until the matching <see cref="Close"/> belongs to
    /// it and is measured from its anchor.
    /// </summary>
    /// <param name="part">
    /// What it was drawn from. A piece with one can be pointed at and selected; a stem, a beam or a
    /// ledger line has none, because nobody wrote it.
    /// </param>
    public int Open(string kind, ISourcePart? part = null, Point at = default,
                    bool? isInk = null, bool isEnclosure = false)
    {
        var frame = _spare.Count > 0 ? _spare.Pop() : new Frame();

        frame.At = _pieces.Count;
        frame.Offset = new Vector(at.X, at.Y);
        frame.Box = Rect.Empty;
        frame.IsInk = isInk ?? part is { Length: > 0 };
        frame.IsEnclosure = isEnclosure;
        frame.Marks.Clear();

        // The slot is claimed now and written at Close, so everything opened inside it lands immediately
        // after it and the subtree comes out contiguous.
        _pieces.Add(default);
        _parts.Add(part);
        _kinds.Add(kind);

        _open.Push(frame);
        return frame.At;
    }

    /// <summary>
    /// Where the piece being built is anchored, with every anchor above it added — the frame its marks are
    /// measured in.
    ///
    /// <para>
    /// For content that works in page coordinates, which is most of it: an engraver decides where a note
    /// goes on a line and a typesetter reports where a glyph landed, and neither is going to be rewritten
    /// to think in frames. Subtracting this turns one into the other, and it means the right thing at both
    /// moments it is asked. When a piece is opened the top of the stack is still its parent, so
    /// <c>at - Anchor</c> is where the new piece sits inside it; once it is open this is the piece's own
    /// anchor, which is what its drawing is measured from.
    /// </para>
    /// <para>
    /// So a builder converts in one place per drawing helper rather than at every call, and what it stores
    /// is relative — which is the whole point, because that is what makes a subtree mean the same thing
    /// wherever it is put down.
    /// </para>
    /// </summary>
    public Vector Anchor
    {
        get
        {
            var anchor = default(Vector);
            foreach (var frame in _open) anchor += frame.Offset;
            return anchor;
        }
    }

    /// <summary>
    /// Records a mark against the piece being built, in that piece's own frame. The piece grows to hold
    /// it — a mark says how far it reaches, so nothing has to be told twice.
    /// </summary>
    public void Draw(LayoutMark mark)
    {
        var frame = _open.Peek();
        frame.Marks.Add(mark);
        frame.Covers(mark.Covers);
    }

    /// <summary>
    /// Says the piece reaches at least this far, for content whose extent is not what it drew — a bar as
    /// tall as its staff whether or not anything was written in it.
    /// </summary>
    public void Covers(Rect what) => _open.Peek().Covers(what);

    /// <summary>Finishes the piece being built, and gives back where it went.</summary>
    public int Close()
    {
        var frame = _open.Pop();
        var at = frame.At;
        var marks = _marks.Count;

        _marks.AddRange(frame.Marks);

        _pieces[at] = new Stored
        {
            Offset = frame.Offset,
            Box = frame.Box,
            Parent = _open.Count == 0 ? -1 : _open.Peek().At,
            Extent = _pieces.Count - at,
            Marks = marks,
            MarkCount = frame.Marks.Count,

            // Ink is a promise that a reader can point at the thing, and a piece that drew nothing cannot
            // be pointed at, hit-tested, washed or stood beside. Kept here rather than checked at each of
            // the places that trust it, because there are too many of those to keep in step.
            IsInk = frame.IsInk && !frame.Box.IsEmpty,
            IsEnclosure = frame.IsEnclosure,
        };

        // What a piece holds, whatever holds it holds too — measured in the parent's frame, which
        // differs from the child's by exactly the child's anchor. Nothing is passed up for a piece that
        // drew nothing: Rect.Offset throws on an empty rectangle rather than leaving it empty.
        if (_open.Count > 0 && !frame.Box.IsEmpty)
            _open.Peek().Covers(Rect.Offset(frame.Box, frame.Offset));

        _spare.Push(frame);
        return at;
    }

    /// <summary>
    /// Declares that these pieces read together, in this order — a verse of lyrics, the notes of a tune,
    /// a row of a matrix.
    ///
    /// <para>
    /// Said after they are built, because a run is only complete when its last member is, and a run does
    /// not stop where a line does. Two members at least: one thing on a run has nothing to step to.
    /// </para>
    /// </summary>
    public void Runs(IReadOnlyList<int> members, bool vertical)
    {
        if (members.Count > 1) _runs.Add(([.. members], vertical));
    }

    /// <summary>The tree, finished. Nothing may be added to the builder afterwards.</summary>
    public LayoutTree Seal()
    {
        if (_open.Count > 0)
            throw new InvalidOperationException($"{_open.Count} piece(s) were opened and never closed");

        var count = _pieces.Count;

        var across = new int[count];
        var acrossAt = new int[count];
        var down = new int[count];
        var downAt = new int[count];

        Array.Fill(across, -1);
        Array.Fill(down, -1);

        var runs = new int[_runs.Count][];

        for (var run = 0; run < _runs.Count; run++)
        {
            var (members, vertical) = _runs[run];
            runs[run] = members;

            for (var member = 0; member < members.Length; member++)
            {
                var at = members[member];
                if (at < 0 || at >= count) continue;

                // One of each is the ordinary case: a note reads along the tune and stacks with the chord
                // named over it and the words sung under it.
                if (vertical) { down[at] = run; downAt[at] = member; }
                else { across[at] = run; acrossAt[at] = member; }
            }
        }

        return new LayoutTree([.. _pieces], [.. _marks], [.. _parts], [.. _kinds],
                              across, acrossAt, down, downAt, runs);
    }
}
