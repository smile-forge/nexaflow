using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Media;
using Nexaflow.Markdown.Ast;

namespace Nexaflow.Visuals.Text.Editing;

/// <summary>
/// A part of one tree grafted into another, saying where it stands in the source that holds it.
///
/// <para>
/// Nested content is laid out from a slice, so its parts count from the start of that slice. Rather than rewrite a
/// parse tree nobody else's copy of would agree with, each part is wrapped as it is copied — which is exactly what
/// <see cref="ISourcePart"/> says an adapter is for.
/// </para>
/// </summary>
internal readonly record struct GraftedPart(ISourcePart Part, int By) : ISourcePart
{
    /// <inheritdoc/>
    public int Start => Part.Start + By;

    /// <inheritdoc/>
    public int Length => Part.Length;
}

/// <summary>
/// How a <see cref="LayoutTree"/> is made: open a piece, draw into it, put pieces inside it, close it.
/// A piece is finished when closed and never touched again — its anchor is fixed on open (everything
/// inside measures from it) and its reach is worked out on close, from what it turned out to hold. That
/// is what makes a subtree relocatable: nothing in it refers to where it used to be. Pieces come out in
/// pre-order with each knowing how far its subtree runs (so descent is arithmetic and reuse is a block
/// copy), achieved by claiming the piece's slot as it opens and filling it in as it closes — its children
/// land in between, immediately after it.
/// </summary>
public sealed class LayoutBuilder
{
    private readonly List<Stored> _pieces = [];
    private readonly List<ISourcePart?> _parts = [];
    private readonly List<string> _kinds = [];
    private readonly List<LayoutPaint?> _paints = [];

    /// <summary>The shape each piece stands in, where it said — see <see cref="Occupies"/>. Null for nearly everything.</summary>
    private readonly List<Geometry?> _regions = [];

    /// <summary>The run of text each piece is, where it is one — see <see cref="Words"/>. Null for nearly everything.</summary>
    private readonly List<LayoutWords?> _words = [];

    /// <summary>What a piece answers to, for the few that answer to anything. Kept beside the pieces, as their words are.</summary>
    private readonly Dictionary<int, LayoutActions> _acts = [];

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
    /// A piece being built: where it is, what it has drawn so far, and how far that reaches. Marks are held
    /// here rather than appended straight to the tree because a piece's drawing must end up contiguous — a
    /// piece that draws, nests a child, then draws again would otherwise have its marks split around the
    /// child's. Frames are pooled, so a page of ten thousand pieces makes about ten of these.
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

    /// <summary>
    /// Opens a piece anchored at <paramref name="at"/> inside whatever is already open, and gives back
    /// where it will live. Everything drawn or opened until the matching <see cref="Close"/> belongs to
    /// it and is measured from its anchor.
    /// </summary>
    /// <param name="part">What it was drawn from; a piece with none (a stem, a beam, a ledger line) can't be pointed at or selected.</param>
    /// <param name="gathers">
    /// Whether this piece's extent is what it turned out to hold — true for containers that are bounding
    /// boxes, false where the rectangle means something else (a typeset box reserves height/depth on its
    /// line, so a subscript can hang below it without growing it). A piece that gathers nothing states its
    /// extent with <see cref="Covers"/> instead.
    /// </param>
    /// <param name="paints">How it is drawn beyond its marks — turned, or snapped to a pixel grid. Null for nearly everything.</param>
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
        _regions.Add(null);
        _words.Add(null);

        _open.Push(frame);
        return frame.At;
    }

    /// <summary>
    /// Where the piece being built is anchored, with every anchor above it added — the frame its marks are
    /// measured in. Lets content that thinks in page coordinates (an engraver deciding where a note goes, a
    /// typesetter reporting where a glyph landed) subtract this to get frame-relative marks without being
    /// rewritten: before <see cref="Open"/>, the stack top is still the parent, so <c>at - Anchor</c> is
    /// where the new piece sits inside it; after, it's the piece's own anchor. What's stored stays relative,
    /// which is what makes a subtree mean the same thing wherever it's put down.
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
    /// were closed now. For the one thing a builder legitimately asks part-way through: where to put what
    /// comes next (verses go under the music, and how far down depends on what the music turned out to be).
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
    /// Says the piece being built stands in exactly this shape, in its own frame, rather than in its box: what a press
    /// has to land inside to mean it, what a marquee has to reach, and what a selection washes. For drawing whose box
    /// badly overstates it — a wedge of a pie is a sliver of the square that holds it, and neighbouring squares overlap
    /// it, so its box alone would hand a press on one slice to the next. Said by the builder, not read off the marks,
    /// since the shape drawn isn't always the shape meant (a note head is drawn as an outline, but a press between its
    /// strokes still means the note). Asked of the piece that draws — a leaf — whose box is still what it drew.
    /// </summary>
    public void Occupies(Geometry region) =>
        _regions[_open.Peek().At] = region.IsFrozen ? region : (Geometry)region.GetAsFrozen();

    /// <summary>
    /// Says the piece being built is a run of text, with a caret position between any two of its letters — see
    /// <see cref="LayoutWords"/>. One piece for the run rather than one per letter: the type engine shapes and
    /// kerns the whole string at once, and where each letter landed is answered on demand.
    /// </summary>
    public void Words(LayoutWords words) => _words[_open.Peek().At] = words;

    /// <summary>Says what the open piece answers to: what one press means, what two mean, what a menu over it offers.</summary>
    public void Acts(LayoutActions actions) => _acts[_open.Peek().At] = actions;

    /// <summary>
    /// Says the open piece leads somewhere: one press on it meaning that rather than a place for the caret, and the
    /// pointer a hand over it. Shorthand for the one verb almost every link is.
    /// </summary>
    /// <param name="tip">What it says while pointed at, or null to say where it leads.</param>
    public void Links(string href, string? tip = null) =>
        Acts(new LayoutActions { Click = new LayoutIntent(LayoutVerbs.Navigate, href, tip) });

    /// <summary>
    /// Says the piece being built reserves exactly this much of its line, vertically, whatever it draws above
    /// or below — the staff a note stands on, the height of a word's letters on its line. Width is still whatever
    /// it drew. The piece's own rectangle takes the room, and a caret is drawn from that — but whatever holds the
    /// piece still grows to cover everything actually drawn, so a measure stays as tall as its lyrics while every
    /// note in it is exactly the staff, and the wash (which covers what was drawn) never uses the room at all.
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
    /// A copy of a block, which is what the shape was made for: pieces are pre-order with contiguous subtrees, so
    /// every index moves by the same amount and nothing inside is measured from anywhere but its own anchor. The
    /// runs it declared come with it, renumbered.
    /// </summary>
    /// <param name="against">Which side of the block it stands against, if it stands against one.</param>
    /// <param name="clear">How much room it keeps from anything else it sits beside.</param>
    public int Graft(LayoutTree tree, Point at = default, Side? against = null, double clear = 0, int shift = 0)
    {
        if (tree.Count == 0) return -1;

        var first = _pieces.Count;
        var parent = _open.Count == 0 ? -1 : _open.Peek().At;

        for (var piece = 0; piece < tree.Count; piece++)
        {
            var stored = tree.Piece(piece);
            var marks = _marks.Count;
            foreach (var mark in tree.MarksOf(piece)) _marks.Add(mark);

            // Every piece the tree holds at its top level is anchored here, not only the first of them: a tree with
            // several — a diagram built as its layers — would otherwise have one layer put where it was asked for and
            // the rest left claiming a parent they are nowhere inside, which paints them in one place and measures them
            // in another.
            var root = stored.Parent < 0;

            _pieces.Add(stored with
            {
                Offset = root ? stored.Offset + new Vector(at.X, at.Y) : stored.Offset,
                Parent = root ? parent : stored.Parent + first,
                Marks = marks,
            });

            _parts.Add(Shifted(tree.PartOf(piece), shift));
            _kinds.Add(tree.KindOf(piece));
            _paints.Add(tree.PaintOf(piece));
            _regions.Add(tree.RegionOf(piece));
            _words.Add(tree.WordsOf(piece));
            if (tree.ActsOf(piece) is { } acts) _acts[_pieces.Count - 1] = acts;
        }

        for (var run = 0; run < tree.RunCount; run++)
        {
            var members = tree.Run(run);
            if (members.IsEmpty) continue;

            var moved = new int[members.Length];
            for (var member = 0; member < members.Length; member++) moved[member] = members[member] + first;
            _runs.Add((moved, tree.RunOf(members[0], vertical: true) == run));
        }

        // What it covers, whatever holds it holds too — as for any piece closed inside it, and for every one of its
        // tops rather than the first.
        if (_open.Count > 0)
            for (var piece = first; piece < _pieces.Count; piece++)
            {
                var top = _pieces[piece];
                if (top.Parent != parent || top.Box.IsEmpty) continue;

                _open.Peek().Gathered(Rect.Offset(top.Box, top.Offset));
            }

        if (against is { } side) _sides.Add((first, side, clear));
        return first;
    }

    /// <summary>A copied part, as it stands in the source it was grafted into — itself, where the two are the same.</summary>
    private static ISourcePart? Shifted(ISourcePart? part, int by) =>
        part is null || by == 0 ? part : new GraftedPart(part, by);

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
    /// a row of a matrix. Said after they're built, since a run is only complete once its last member is
    /// and doesn't stop where a line does. Two members minimum: one thing on a run has nothing to step to.
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

        return new LayoutTree([.. _pieces], [.. _marks], [.. _parts], [.. _kinds], [.. _paints], [.. _regions], [.. _words],
                              _acts.Count == 0 ? null : new Dictionary<int, LayoutActions>(_acts), across, acrossAt, down, downAt, runs);
    }

    /// <summary>
    /// Puts every piece that said which side of the block it stands against onto that side. The block is the frame
    /// of whatever holds the piece, from its left edge to <paramref name="block"/>; with no width to go by, a piece
    /// against the right follows everything else and one in the centre stays where it is. Left and centre resolve
    /// first, so the right knows where they ended up, and a right-aligned piece keeps clear of what's beside it —
    /// dropping under it when the block is too narrow for both, as LaTeX does with an equation's number.
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
