using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Media;
using Nexaflow.Markdown.Ast;

namespace Nexaflow.Visuals.Text.Editing;

/// <summary>
/// A laid-out piece of content, as it is stored: where it is anchored inside whatever holds it, how big
/// it turned out, how far its subtree runs, and where its drawing is. Nothing else. Two pieces of geometry,
/// both needed: <see cref="Offset"/> is the anchor (fixed when opened, never moves) that children and marks
/// are measured from, while <see cref="Box"/> is how far the piece actually reaches relative to that anchor
/// and may begin at a negative offset (an accidental drawn before its note head). One rectangle can't be
/// both without moving the anchor once contents are known, which would shift every child and mark already
/// recorded. No absolute rectangle is kept — it would be a second copy of what the tree already says,
/// rewritten on every move or reuse — since absolute position is free to accumulate while descending.
/// Everything editing needs (what a piece was drawn from, its kind, what it reads along) lives beside the
/// pieces in <see cref="LayoutTree"/> instead, since most pieces are a stem or letter nobody will ever
/// select, and carrying two object references per piece through the painter's loop would be waste.
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



    /// <summary>Where a caret may rest against it — see <see cref="Editing.Stops"/>.</summary>
    public required Stops Stops { get; init; }
}

/// <summary>
/// A whole laid-out thing: its pieces in one array, its drawing in another, and the few facts about
/// pieces that only selection cares about beside them. Pieces are pre-order, so a piece's subtree is the
/// slice starting at it running for its extent — descending is arithmetic, taking a subtree is a range not
/// a walk, and lifting one out to reuse elsewhere is a block copy with one index changed. Nothing in here
/// is content-specific and nothing ever will be: a bar of music, a formula and a barcode are all just
/// pieces that drew marks.
/// </summary>
public sealed class LayoutTree
{
    private readonly Stored[] _pieces;
    private readonly LayoutMark[] _marks;

    // Beside the pieces rather than in them — see Stored.
    private readonly ISourcePart?[] _parts;
    private readonly string[] _kinds;
    private readonly LayoutPaint?[] _paints;

    private readonly Geometry?[] _regions;

    private readonly LayoutWords?[] _words;

    /// <summary>What the few pieces that answer to a gesture answer to, by index. Null where none does.</summary>
    private readonly IReadOnlyDictionary<int, LayoutActions>? _acts;

    /// <summary>Which run a piece reads along and where in it, and the same downward. -1 for neither.</summary>
    private readonly int[] _across;
    private readonly int[] _acrossAt;
    private readonly int[] _down;
    private readonly int[] _downAt;

    private readonly List<int[]> _runs;

    /// <summary>Worked out on the first ask and kept — see <see cref="Places"/>.</summary>
    private IReadOnlyList<CaretPlace>? _places;

    internal LayoutTree(Stored[] pieces, LayoutMark[] marks, ISourcePart?[] parts, string[] kinds,
                        LayoutPaint?[] paints, Geometry?[] regions, LayoutWords?[] words,
                        IReadOnlyDictionary<int, LayoutActions>? acts, int[] across, int[] acrossAt, int[] down, int[] downAt, int[][] runs)
    {
        _pieces = pieces;
        _marks = marks;
        _parts = parts;
        _kinds = kinds;
        _paints = paints;
        _regions = regions;
        _words = words;
        _acts = acts;
        _across = across;
        _acrossAt = acrossAt;
        _down = down;
        _downAt = downAt;
        _runs = [.. runs];
    }

    /// <summary>The same pieces, marks and runs, standing for other parts — see <see cref="Parted"/>.</summary>
    private LayoutTree(LayoutTree tree, ISourcePart?[] parts)
    {
        _pieces = tree._pieces;
        _marks = tree._marks;
        _parts = parts;
        _kinds = tree._kinds;
        _paints = tree._paints;
        _regions = tree._regions;
        _words = tree._words;
        _acts = tree._acts;
        _across = tree._across;
        _acrossAt = tree._acrossAt;
        _down = tree._down;
        _downAt = tree._downAt;
        _runs = tree._runs;
    }

    /// <summary>
    /// This tree with each piece standing for the part at its index in <paramref name="parts"/> — the same drawing, of the same
    /// content read again. Shares everything else, so it costs one array.
    /// </summary>
    internal LayoutTree Parted(ISourcePart?[] parts) =>
        parts.Length == _parts.Length ? new LayoutTree(this, parts) : throw new ArgumentException("a part for every piece", nameof(parts));

    /// <summary>How many pieces there are.</summary>
    public int Count => _pieces.Length;

    /// <summary>The whole of it. Nothing laid out at all has no pieces and no root.</summary>
    public Piece Root => new(this, _pieces.Length == 0 ? -1 : 0);

    /// <summary>A piece by index — how a handle is made, and the only thing an index means.</summary>
    public Piece At(int index) => new(this, index >= 0 && index < _pieces.Length ? index : -1);

    // ── What a piece is ─────────────────────────────────────────────────────

    internal ref readonly Stored Piece(int at) => ref _pieces[at];

    internal ISourcePart? PartOf(int at) => _parts[at];

    /// <summary>
    /// Everywhere a caret may rest in this tree, in the order the right arrow visits them. Worked out on the
    /// first ask rather than at seal time, since a place is a mark on the page and a tree is settled onto the
    /// origin only after it's sealed. Cached, not recomputed per question — stepping the caret, snapping an
    /// offset to a stop, and deciding what a press means all read this same list, and rebuilding it per
    /// keystroke was a walk of every piece plus a sort.
    /// </summary>
    public IReadOnlyList<CaretPlace> Places => _places ??= LayoutQuery.PlacesIn(Root);

    /// <summary>
    /// What every piece and what it holds reaches, on the page and in the source — what lets a question about one point or one
    /// stretch pass over everything that cannot answer it. Made on the first question and kept; see <see cref="LayoutIndex"/>.
    /// </summary>
    internal LayoutIndex Indexed => _indexed ??= new LayoutIndex(this);

    private LayoutIndex? _indexed;

    internal string KindOf(int at) => _kinds[at];

    /// <summary>How it is painted beyond its marks, or nothing — which is nearly always.</summary>
    internal LayoutPaint? PaintOf(int at) => _paints[at];

    /// <summary>The shape it stands in, in its own frame, or nothing — which is nearly always. See <see cref="LayoutBuilder.Occupies"/>.</summary>
    internal Geometry? RegionOf(int at) => _regions[at];

    /// <summary>The run of text it is, or nothing — which is nearly always. See <see cref="LayoutBuilder.Words"/>.</summary>
    internal LayoutWords? WordsOf(int at) => _words[at];

    /// <summary>What the piece at <paramref name="at"/> answers to, or null where it answers to nothing.</summary>
    internal LayoutActions? ActsOf(int at) => _acts is not null && _acts.TryGetValue(at, out var acts) ? acts : null;

    internal ReadOnlySpan<LayoutMark> MarksOf(int at) =>
        _marks.AsSpan(_pieces[at].Marks, _pieces[at].MarkCount);

    /// <summary>
    /// Where a piece's frame begins in the content as a whole — its anchor with every parent's added, and carried through
    /// whatever turns or scales what holds it. What a painter pushes, and where its box is measured from.
    /// </summary>
    internal Vector AnchorOf(int at) => (Vector)PlacingOf(at).Out(default(Point));

    /// <summary>
    /// The frame a piece's box is measured in, on the page: its parent's own frame, moved to the piece's anchor — before anything
    /// the piece applies of its own.
    /// </summary>
    internal LayoutFrame PlacingOf(int at)
    {
        // Nearly always nothing above it turns or scales it, and then it is the anchors added up — the climb it always was.
        var anchor = _pieces[at].Offset;
        var applied = false;

        for (var up = _pieces[at].Parent; up >= 0; up = _pieces[up].Parent)
        {
            anchor += _pieces[up].Offset;
            applied |= _paints[up] is { Applies: true };
        }

        return applied ? FrameOf(_pieces[at].Parent).Placing(_pieces[at].Offset) : new LayoutFrame(anchor, null);
    }

    /// <summary>
    /// The frame a piece's marks, words, shape and children are measured in, on the page: where its box is measured, with
    /// whatever it turns or scales applied inside that (see <see cref="LayoutPaint.Applied"/>). The same as
    /// <see cref="PlacingOf"/> for nearly every piece, which is drawn in no frame of its own.
    /// </summary>
    internal LayoutFrame FrameOf(int at) => PlacingOf(at).Inside(_paints[at]?.Applied);

    /// <summary>
    /// How many pieces hold this one. What settles a tie between two pieces that both answer a question —
    /// the deeper is the more specific answer.
    /// </summary>
    internal int DepthOf(int at)
    {
        var depth = 0;
        for (var up = _pieces[at].Parent; up >= 0; up = _pieces[up].Parent) depth++;
        return depth;
    }

    /// <summary>
    /// Where a piece sits in the content as a whole. Worked out rather than stored, which is what keeps a
    /// subtree meaningful wherever it's put down — a short climb up an array (a page is about ten deep).
    /// Anything asking about many pieces at once should use <see cref="Within"/> instead, which carries the
    /// frame down for free.
    /// </summary>
    internal Rect WhereIs(int at)
    {
        var box = _pieces[at].Box;
        return box.IsEmpty ? box : PlacingOf(at).Out(box);
    }

    /// <summary>
    /// Every piece of a subtree, with where each one sits — the walk to use when the question is about
    /// more than one piece, since the frame comes down with it rather than being climbed to each time.
    /// Pre-order means a piece is always reached after the one holding it, so the running frame is only
    /// pushed and popped, never searched for or climbed.
    /// </summary>
    internal IEnumerable<(int At, Rect Where)> Within(int from)
    {
        if (from < 0 || from >= _pieces.Length) yield break;

        // The frame each piece is measured in, and where the subtree using it ends: an anchor, and — only inside something
        // turned or scaled — which of the matrices carried down with it applies, so the walk over everything else is the
        // addition it always was. The first frame is the parent's own, which is the page itself above the root.
        var frames = new (int End, Vector Anchor, int Shaped)[16];
        List<Matrix>? shapes = null;

        var up = _pieces[from].Parent;
        var start = up < 0 ? default : FrameOf(up);
        if (start.Shaped is { } first) (shapes = []).Add(first);

        frames[0] = (from + _pieces[from].Extent, start.Anchor, shapes is null ? -1 : 0);
        var top = 0;

        for (var at = from; at < frames[0].End; at++)
        {
            while (frames[top].End <= at) top--;

            var piece = _pieces[at];
            var shaped = frames[top].Shaped;
            var box = piece.Box;
            Vector anchor;
            Rect where;

            if (shaped < 0)
            {
                anchor = frames[top].Anchor + piece.Offset;
                where = box.IsEmpty ? box : new Rect(box.X + anchor.X, box.Y + anchor.Y, box.Width, box.Height);
            }
            else
            {
                var placing = new LayoutFrame(frames[top].Anchor, shapes![shaped]).Placing(piece.Offset);
                anchor = placing.Anchor;
                where = placing.Out(box);
            }

            yield return (at, where);

            if (piece.Extent <= 1) continue;

            if (_paints[at] is { Applies: true } paint)
            {
                var applied = paint.Applied!.Value;
                (shapes ??= []).Add(shaped < 0 ? applied : applied * shapes[shaped]);
                shaped = shapes.Count - 1;
            }

            if (++top >= frames.Length) Array.Resize(ref frames, frames.Length * 2);
            frames[top] = (at + piece.Extent, anchor, shaped);
        }
    }

    // ── What it belongs with ────────────────────────────────────────────────

    /// <summary>
    /// The piece one step along a run from this one, or -1 at either end and for a piece on no run. A run
    /// (a verse of lyrics, the notes of a tune, a row of a matrix) is deliberately only ever asked "what is
    /// next to this" — it's not a group anybody belongs to and nothing draws it, which is why it's a list of
    /// indices rather than a piece pretending to be a parent.
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
    internal ReadOnlySpan<int> Run(int id) => id < 0 || id >= _runs.Count ? default : _runs[id];

    /// <summary>How many runs have been declared — what a tree grafted into another brings with it.</summary>
    internal int RunCount => _runs.Count;

    /// <summary>
    /// Declares that these pieces read together, in this order — a row of a matrix, a column of one. Callable
    /// after the tree is sealed, which a parent cannot be: a typeset formula only learns its tables once the
    /// parse tree's grids are matched against finished ink, so which piece stands for a cell can't be asked
    /// while drawing is still going on. A run is just a list of indices beside the pieces, so declaring one
    /// moves nothing. Two members minimum: one thing on a run has nothing to step to.
    /// </summary>
    public void Runs(IReadOnlyList<Piece> members, bool vertical)
    {
        if (members.Count < 2) return;

        var run = _runs.Count;
        var indices = new int[members.Count];

        for (var member = 0; member < members.Count; member++)
        {
            var at = indices[member] = members[member].At;
            if (at < 0 || at >= _pieces.Length) continue;

            if (vertical) { _down[at] = run; _downAt[at] = member; }
            else { _across[at] = run; _acrossAt[at] = member; }
        }

        _runs.Add(indices);
    }

    /// <summary>
    /// Moves the whole tree, by moving the one anchor nothing is measured from — shifting the root's anchor
    /// shifts every piece under it and touches nothing else, which relative geometry makes free. Content that
    /// only learns where it sits once laid out uses this: a typeset formula can be built above and left of
    /// where the pen started, and a tree with negative coordinates would put the caret outside the control
    /// drawing it.
    /// </summary>
    public void Settle(Vector by)
    {
        if (_pieces.Length == 0 || (by.X == 0 && by.Y == 0)) return;

        _pieces[0] = _pieces[0] with { Offset = _pieces[0].Offset + by };

        // Where everything reaches on the page has moved with it.
        _indexed = null;
    }
}
