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
    private readonly List<LayoutPaint?> _paints = [];

    private readonly List<LayoutMark> _marks = [];
    private readonly List<(int[] Members, bool Vertical)> _runs = [];

    /// <summary>
    /// Which pieces stand against a side of the block, and how far each keeps from what is beside it — see
    /// <see cref="Against"/>.
    /// </summary>
    private readonly List<(int At, Side Side, double Clear)> _sides = [];

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
        
        public Stops Stops;
        /// <summary>The vertical room it said it reserves, if it said — see <see cref="LayoutBuilder.Reserves"/>.</summary>
        public (double Top, double Height)? Room;
        public bool Gathers;
        public LayoutPaint? Paints;
        public readonly List<LayoutMark> Marks = [];

        /// <summary>Says the piece reaches at least this far, whatever it holds.</summary>
        public void Covers(Rect what)
        {
            // A rectangle with no size is still a place. A typesetter makes plenty of them — a strut that
            // reserves nothing, a kern of zero — and a piece that says it is at a point is not the same as a
            // piece that never said where it was, which is what Rect.Empty means.
            if (what.IsEmpty) return;
            Box = Box.IsEmpty ? what : Rect.Union(Box, what);
        }

        /// <summary>
        /// …and the same for what it turned out to hold, which only counts where the content says a piece
        /// is its contents. See the `gathers` argument to <see cref="Open"/>.
        /// </summary>
        public void Gathered(Rect what)
        {
            if (Gathers) Covers(what);
        }
    }

    /// <param name="isInk">
    /// Whether a reader can point at it. Unstated it follows the part, which is the ordinary convention —
    /// something a reader typed is something they can point at, and a beam or a guard pattern the drawing
    /// invented is not. Stated, the content knows better: a hole waiting to be typed into is pointable and
    /// covers nothing, and a printed check digit nobody wrote is still a digit on the page.
    /// </param>

    /// <param name="gathers">
    /// Whether this piece's extent is what it turned out to hold. True for content whose containers are
    /// bounding boxes — a bar of music is as tall as what is in it — and false where a container's rectangle
    /// means something else: a typeset box is the height and depth it reserves on its line, so a subscript
    /// hangs below the very piece that holds it and growing to fit would be wrong. A piece that gathers
    /// nothing states its extent with <see cref="Covers"/> instead.
    /// </param>
    /// <param name="paints">
    /// How it is drawn beyond its marks — turned, or snapped to a pixel grid. Null for nearly everything.
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
                Stops stops = Stops.Both,
                bool gathers = true,
                    LayoutPaint? paints = null)
    {
        var frame = _spare.Count > 0 ? _spare.Pop() : new Frame();

        frame.At = _pieces.Count;
        frame.Offset = new Vector(at.X, at.Y);
        frame.Box = Rect.Empty;

                
                
        frame.Room = null;
        frame.Stops = stops;
        frame.Gathers = gathers;
        frame.Paints = paints is { Matters: true } ? paints : null;
        frame.Marks.Clear();

        // The slot is claimed now and written at Close, so everything opened inside it lands immediately
        // after it and the subtree comes out contiguous.
        _pieces.Add(default);
        _parts.Add(part);
        _kinds.Add(kind);
        _paints.Add(frame.Paints);

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
    /// How far the piece being built has reached so far, in its own frame — what its extent would be if it
    /// were closed now.
    ///
    /// <para>
    /// For the one thing a builder legitimately has to ask part-way through: where to put what comes next.
    /// Verses go under the music, and how far down that is depends on what the music turned out to be.
    /// </para>
    /// </summary>
    public Rect Reached => _open.Count == 0 ? Rect.Empty : _open.Peek().Box;

    /// <summary>
    /// Records a mark against the piece being built, in that piece's own frame. The piece grows to hold
    /// it — a mark says how far it reaches, so nothing has to be told twice.
    /// </summary>
    public void Draw(LayoutMark mark)
    {
        var frame = _open.Peek();
        frame.Marks.Add(mark);
        frame.Gathered(mark.Covers);
    }

    /// <summary>
    /// Says the piece reaches at least this far, for content whose extent is not what it drew — a bar as
    /// tall as its staff whether or not anything was written in it.
    /// </summary>
    public void Covers(Rect what) => _open.Peek().Covers(what);

    /// <summary>
    /// Says the piece being built reserves exactly this much of its line, vertically, whatever it draws above
    /// or below — the staff a note stands on, the height of a word's letters on its line. Its width is still
    /// whatever it drew.
    ///
    /// <para>
    /// The piece's own rectangle takes the room, and it is what a caret is drawn from: a caret stands in the
    /// room a thing reserves on its line. Whatever holds the piece still grows to cover everything it drew, so
    /// a measure stays as tall as its lyrics while every note in it is exactly the staff — and the wash, which
    /// covers what was drawn, never uses the room at all.
    /// </para>
    /// </summary>
    public void Reserves(double top, double height) => _open.Peek().Room = (top, height);

    /// <summary>
    /// Says which side of the block the piece being built stands against — see <see cref="Side"/>. Resolved when
    /// the tree is sealed, against the width it is sealed for.
    /// </summary>
    /// <param name="clear">How much room it keeps from anything else it sits beside.</param>
    public void Against(Side side, double clear = 0) => _sides.Add((_open.Peek().At, side, clear));

    /// <summary>
    /// Puts a finished tree down inside whatever is open, anchored at <paramref name="at"/>, and gives back where
    /// its root now lives — one layout set into another, the way a formula is set into a block beside its number.
    ///
    /// <para>
    /// A copy of a block, which is what the shape was made for: the pieces are in pre-order with their subtrees
    /// contiguous, so every index moves by the same amount and nothing inside is measured from anywhere but its
    /// own anchor. The runs it declared come with it, renumbered.
    /// </para>
    /// </summary>
    /// <param name="against">Which side of the block it stands against, if it stands against one.</param>
    /// <param name="clear">How much room it keeps from anything else it sits beside.</param>
    public int Graft(LayoutTree tree, Point at = default, Side? against = null, double clear = 0)
    {
        if (tree.Count == 0) return -1;

        var first = _pieces.Count;
        var parent = _open.Count == 0 ? -1 : _open.Peek().At;

        for (var piece = 0; piece < tree.Count; piece++)
        {
            var stored = tree.Piece(piece);
            var marks = _marks.Count;
            foreach (var mark in tree.MarksOf(piece)) _marks.Add(mark);

            _pieces.Add(stored with
            {
                Offset = piece == 0 ? stored.Offset + new Vector(at.X, at.Y) : stored.Offset,
                Parent = piece == 0 ? parent : stored.Parent + first,
                Marks = marks,
            });

            _parts.Add(tree.PartOf(piece));
            _kinds.Add(tree.KindOf(piece));
            _paints.Add(tree.PaintOf(piece));
        }

        for (var run = 0; run < tree.RunCount; run++)
        {
            var members = tree.Run(run);
            if (members.IsEmpty) continue;

            var moved = new int[members.Length];
            for (var member = 0; member < members.Length; member++) moved[member] = members[member] + first;
            _runs.Add((moved, tree.RunOf(members[0], vertical: true) == run));
        }

        // What it covers, whatever holds it holds too — as for any piece closed inside it.
        var root = _pieces[first];
        if (_open.Count > 0 && !root.Box.IsEmpty) _open.Peek().Gathered(Rect.Offset(root.Box, root.Offset));

        if (against is { } side) _sides.Add((first, side, clear));
        return first;
    }

    /// <summary>Finishes the piece being built, and gives back where it went.</summary>
    public int Close()
    {
        var frame = _open.Pop();
        var at = frame.At;
        var marks = _marks.Count;

        _marks.AddRange(frame.Marks);

        // The room it stated, if it stated one. What it reaches is kept apart, for whatever holds it.
        var reach = frame.Box;
        if (frame.Room is { } room && !frame.Box.IsEmpty)
            frame.Box = new Rect(frame.Box.X, room.Top, frame.Box.Width, room.Height);

        _pieces[at] = new Stored
        {
            Offset = frame.Offset,
            Box = frame.Box,
            Parent = _open.Count == 0 ? -1 : _open.Peek().At,
            Extent = _pieces.Count - at,
            Marks = marks,
            MarkCount = frame.Marks.Count,



                        
                        
            Stops = frame.Stops,
        };

        // What a piece holds, whatever holds it holds too — measured in the parent's frame, which
        // differs from the child's by exactly the child's anchor. Nothing is passed up for a piece that
        // drew nothing: Rect.Offset throws on an empty rectangle rather than leaving it empty.
        if (_open.Count > 0 && !reach.IsEmpty)
            _open.Peek().Gathered(Rect.Offset(reach, frame.Offset));

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
    public LayoutTree Seal(double block = 0)
    {
        if (_open.Count > 0)
            throw new InvalidOperationException($"{_open.Count} piece(s) were opened and never closed");

        // Whatever stands against a side of the block goes there now, when the block's width is known.
        Align(block);

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

        return new LayoutTree([.. _pieces], [.. _marks], [.. _parts], [.. _kinds], [.. _paints],
                              across, acrossAt, down, downAt, runs);
    }

    /// <summary>
    /// Puts every piece that said which side of the block it stands against onto that side. The block is the frame
    /// of whatever holds the piece, from its left edge to <paramref name="block"/>; with no width to go by, a piece
    /// against the right follows everything else and one in the centre stays where it is.
    ///
    /// <para>
    /// The left and the centre first, so the right knows where they ended up. And a piece against the right keeps
    /// clear of everything beside it: where the block is too narrow for both on one line it goes under them, still
    /// against the right — which is what LaTeX does with an equation's number.
    /// </para>
    /// </summary>
    private void Align(double block)
    {
        foreach (var right in new[] { false, true })
            foreach (var (at, side, clear) in _sides)
            {
                if ((side == Side.Right) != right) continue;

                var piece = _pieces[at];
                if (piece.Box.IsEmpty) continue;

                var box = Rect.Offset(piece.Box, piece.Offset);
                var beside = Beside(at);
                var (x, y) = (box.X, box.Y);

                switch (side)
                {
                    case Side.Left:
                        x = 0;
                        break;

                    case Side.Centre when block > 0:
                        x = Math.Max(0, (block - box.Width) / 2);
                        break;

                    case Side.Right when block <= 0:
                        if (!beside.IsEmpty) x = beside.Right + clear;
                        break;

                    case Side.Right:
                        x = Math.Max(0, block - box.Width);
                        if (!beside.IsEmpty && x < beside.Right + clear) y = beside.Bottom;
                        break;
                }

                _pieces[at] = piece with { Offset = piece.Offset + new Vector(x - box.X, y - box.Y) };
                Regather(piece.Parent);
            }
    }

    /// <summary>Everything else the piece's holder holds, as it now stands, in the holder's frame.</summary>
    private Rect Beside(int at)
    {
        var holder = _pieces[at].Parent;
        var union = Rect.Empty;

        for (var other = 0; other < _pieces.Count; other++)
            if (other != at && _pieces[other].Parent == holder && !_pieces[other].Box.IsEmpty)
                union.Union(Rect.Offset(_pieces[other].Box, _pieces[other].Offset));

        return union;
    }

    /// <summary>
    /// A holder's reach, gathered again from what it holds once one of them has moved. A holder with a piece
    /// against a side of it is a block, and a block reaches exactly as far as what it holds.
    /// </summary>
    private void Regather(int holder)
    {
        if (holder < 0) return;

        var union = Rect.Empty;
        for (var child = 0; child < _pieces.Count; child++)
            if (_pieces[child].Parent == holder && !_pieces[child].Box.IsEmpty)
                union.Union(Rect.Offset(_pieces[child].Box, _pieces[child].Offset));

        if (!union.IsEmpty) _pieces[holder] = _pieces[holder] with { Box = union };
    }
}
