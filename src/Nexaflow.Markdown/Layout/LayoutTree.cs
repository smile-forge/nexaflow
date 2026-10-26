using System.Collections;
using Nexaflow.Markdown.Ast;

namespace Nexaflow.Markdown.Layout;

/// <summary>Where a caret may rest against a piece.</summary>
[Flags]
public enum LayoutStops
{
    None = 0,
    Before = 1,
    After = 2,
    Both = Before | After,
}

/// <summary>Which side of the block a piece stands against.</summary>
public enum LayoutSide
{
    Left,
    Centre,
    Right,
}

/// <summary>A laid-out piece as it is stored: its anchor in its parent, its reach about that anchor, its subtree and its drawing.</summary>
internal readonly record struct LayoutStored
{
    public required LayoutVector Offset { get; init; }

    public required LayoutRect Box { get; init; }

    public required int Parent { get; init; }

    public required int Extent { get; init; }

    public required int Marks { get; init; }

    public required int MarkCount { get; init; }

    public required LayoutStops Stops { get; init; }
}

/// <summary>Where a frame of a layout stands on the page, and how what is measured in it is turned or scaled on the way.</summary>
internal readonly record struct LayoutFrame(LayoutVector Anchor, LayoutMatrix? Shaped)
{
    public LayoutFrame Placing(LayoutVector offset) =>
        new(Anchor + (Shaped is { } shaped ? shaped.Transform(offset) : offset), Shaped);

    public LayoutFrame Inside(LayoutMatrix? applied) =>
        applied is not { } own ? this : new(Anchor, Shaped is { } shaped ? own * shaped : own);

    public LayoutPoint Out(LayoutPoint local) => (Shaped is { } shaped ? shaped.Transform(local) : local) + Anchor;

    public LayoutRect Out(LayoutRect local)
    {
        if (local.IsEmpty) return local;
        if (Shaped is { } shaped) local = LayoutRect.Transform(local, shaped);

        return new LayoutRect(local.X + Anchor.X, local.Y + Anchor.Y, local.Width, local.Height);
    }

    /// <summary>An outline said in this frame, where it stands on the page — made by <paramref name="medium"/>, which holds what it is.</summary>
    public LayoutShape Out(LayoutShape local, ILayoutMedium medium)
    {
        if (Shaped is null && Anchor is { X: 0, Y: 0 }) return local;

        var placing = Shaped is { } shaped ? shaped * LayoutMatrix.Translation(Anchor.X, Anchor.Y) : LayoutMatrix.Translation(Anchor.X, Anchor.Y);
        // A group round it carrying the placing, as the shape it is placed from stays untouched: what a press is asked of and what a wash
        // is drawn over is the shape the piece stands in, moved, not a copy of it.
        return medium.Transformed(medium.Group([local]), placing);
    }

    public LayoutPoint Back(LayoutPoint page)
    {
        var at = page - Anchor;
        if (Shaped is not { HasInverse: true } shaped) return new LayoutPoint(at.X, at.Y);

        return shaped.Inverted().Transform(new LayoutPoint(at.X, at.Y));
    }
}

/// <summary>
/// A whole laid-out thing, renderer-free: its pieces in one array in pre-order, its drawing in another, and the few facts only
/// selection asks beside them.
/// </summary>
public sealed class LayoutTree
{
    private readonly LayoutStored[] _pieces;
    private readonly LayoutMark[] _marks;
    private readonly ISourcePart?[] _parts;
    private readonly string[] _kinds;
    private readonly LayoutPaint?[] _paints;
    private readonly LayoutShape?[] _regions;
    private readonly LayoutWords?[] _words;
    private readonly IReadOnlyDictionary<int, LayoutActions>? _acts;
    private readonly int[] _across;
    private readonly int[] _acrossAt;
    private readonly int[] _down;
    private readonly int[] _downAt;
    private readonly List<int[]> _runs;

    internal LayoutTree(ILayoutMedium medium, LayoutStored[] pieces, LayoutMark[] marks, ISourcePart?[] parts, string[] kinds,
                          LayoutPaint?[] paints, LayoutShape?[] regions, LayoutWords?[] words,
                          IReadOnlyDictionary<int, LayoutActions>? acts, int[] across, int[] acrossAt, int[] down, int[] downAt,
                          int[][] runs)
    {
        Medium = medium;
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
        Medium = tree.Medium;
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

    /// <summary>What it was laid out in — what made every outline, ink and run of words it holds, and what makes any more asked of it.</summary>
    public ILayoutMedium Medium { get; }

    /// <summary>
    /// This tree with each piece standing for the part at its index in <paramref name="parts"/> — the same drawing, of the same content
    /// read again. Shares everything else, so it costs one array.
    /// </summary>
    public LayoutTree Parted(ISourcePart?[] parts) =>
        parts.Length == _parts.Length ? new LayoutTree(this, parts) : throw new ArgumentException("a part for every piece", nameof(parts));

    /// <summary>
    /// Everywhere a caret may rest in this tree, in the order the right arrow visits them. Worked out on the first ask rather than at
    /// seal time, since a place is a mark on the page and a tree is settled onto the page only after it is sealed — and kept, since
    /// stepping the caret, snapping an offset to a stop and deciding what a press means all read this same list.
    /// </summary>
    public IReadOnlyList<CaretPlace> Places => _places ??= LayoutQuery.PlacesIn(Root);

    private IReadOnlyList<CaretPlace>? _places;

    /// <summary>
    /// What every piece and what it holds reaches, on the page and in the source — what lets a question about one point or one stretch
    /// pass over everything that cannot answer it. Made on the first question and kept; see <see cref="LayoutIndex"/>.
    /// </summary>
    internal LayoutIndex Indexed => _indexed ??= new LayoutIndex(this);

    private LayoutIndex? _indexed;

    public int Count => _pieces.Length;

    public LayoutPiece Root => new(this, _pieces.Length == 0 ? -1 : 0);

    public LayoutPiece At(int index) => new(this, index >= 0 && index < _pieces.Length ? index : -1);

    internal ref readonly LayoutStored Piece(int at) => ref _pieces[at];

    internal ISourcePart? PartOf(int at) => _parts[at];

    internal string KindOf(int at) => _kinds[at];

    internal LayoutPaint? PaintOf(int at) => _paints[at];

    internal LayoutShape? RegionOf(int at) => _regions[at];

    internal LayoutWords? WordsOf(int at) => _words[at];

    /// <summary>What the piece at <paramref name="at"/> answers to, or null where it answers to nothing.</summary>
    internal LayoutActions? ActsOf(int at) => _acts is not null && _acts.TryGetValue(at, out var acts) ? acts : null;

    internal ReadOnlySpan<LayoutMark> MarksOf(int at) => _marks.AsSpan(_pieces[at].Marks, _pieces[at].MarkCount);

    internal LayoutFrame PlacingOf(int at)
    {
        var anchor = _pieces[at].Offset;
        var applied = false;

        for (var up = _pieces[at].Parent; up >= 0; up = _pieces[up].Parent)
        {
            anchor += _pieces[up].Offset;
            applied |= _paints[up] is { Applies: true };
        }

        return applied ? FrameOf(_pieces[at].Parent).Placing(_pieces[at].Offset) : new LayoutFrame(anchor, null);
    }

    internal LayoutFrame FrameOf(int at) => PlacingOf(at).Inside(_paints[at]?.Applied);

    internal LayoutRect WhereIs(int at)
    {
        var box = _pieces[at].Box;
        return box.IsEmpty ? box : PlacingOf(at).Out(box);
    }

    /// <summary>Where a piece's frame begins on the page — its anchor with every parent's added, carried through whatever turns or scales what holds it.</summary>
    internal LayoutVector AnchorOf(int at) => (LayoutVector)PlacingOf(at).Out(default(LayoutPoint));

    /// <summary>How many pieces hold this one — what settles a tie between two pieces that both answer a question, the deeper being the more specific.</summary>
    internal int DepthOf(int at)
    {
        var depth = 0;
        for (var up = _pieces[at].Parent; up >= 0; up = _pieces[up].Parent) depth++;
        return depth;
    }

    /// <summary>
    /// The piece one step along a run from this one, or -1 at either end and for a piece on no run. A run (a verse of lyrics, the notes
    /// of a tune, a row of a matrix) is only ever asked "what is next to this" — it is not a group anybody belongs to and nothing draws it.
    /// </summary>
    internal int Along(int at, bool vertical, bool forward)
    {
        var (run, index) = vertical ? (_down[at], _downAt[at]) : (_across[at], _acrossAt[at]);
        if (run < 0) return -1;

        var members = _runs[run];
        var to = index + (forward ? 1 : -1);
        return to < 0 || to >= members.Length ? -1 : members[to];
    }

    /// <summary>Every piece of a subtree with where it sits, the frame carried down rather than climbed to.</summary>
    internal IEnumerable<(int At, LayoutRect Where)> Within(int from)
    {
        if (from < 0 || from >= _pieces.Length) yield break;

        var frames = new (int End, LayoutVector Anchor, int Shaped)[16];
        List<LayoutMatrix>? shapes = null;

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
            LayoutVector anchor;
            LayoutRect where;

            if (shaped < 0)
            {
                anchor = frames[top].Anchor + piece.Offset;
                where = box.IsEmpty ? box : new LayoutRect(box.X + anchor.X, box.Y + anchor.Y, box.Width, box.Height);
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

    internal int RunOf(int at, bool vertical) => vertical ? _down[at] : _across[at];

    internal ReadOnlySpan<int> Run(int id) => id < 0 || id >= _runs.Count ? default : _runs[id];

    internal int RunCount => _runs.Count;

    /// <summary>
    /// Declares that these pieces read together, in this order — a row of a matrix, a column of one. Callable after the tree is
    /// sealed: a formula only learns which piece is a cell once its grids are matched against what was drawn.
    /// </summary>
    public void Runs(IReadOnlyList<LayoutPiece> members, bool vertical)
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

    /// <summary>Moves the whole tree by moving the one anchor nothing is measured from.</summary>
    public void Settle(LayoutVector by)
    {
        if (_pieces.Length == 0 || (by.X == 0 && by.Y == 0)) return;

        _pieces[0] = _pieces[0] with { Offset = _pieces[0].Offset + by };

        // Where everything reaches on the page has moved with it.
        _indexed = null;
    }
}

/// <summary>
/// One piece of a <see cref="LayoutTree"/>, seen: a handle, the tree and an index. Everything a piece is lives in the tree; this
/// is how it is asked.
/// </summary>
public readonly record struct LayoutPiece
{
    private readonly LayoutTree? _tree;
    private readonly int _at;

    internal LayoutPiece(LayoutTree tree, int at)
    {
        _tree = at < 0 ? null : tree;
        _at = at < 0 ? -1 : at;
    }

    /// <summary>Whether this is a piece at all, rather than the nothing a question that found none answers with.</summary>
    public bool Exists => _tree is not null;

    /// <summary>The tree it belongs to, or nothing.</summary>
    public LayoutTree? Tree => _tree;

    /// <summary>Where it stands in its tree — how a handle is made, and the only thing an index means.</summary>
    public int At => _at;

    /// <summary>Where its frame begins, relative to its parent's.</summary>
    public LayoutVector Offset => _tree is null ? default : _tree.Piece(_at).Offset;

    /// <summary>How far it reaches, relative to its own anchor.</summary>
    public LayoutRect Box => _tree is null ? LayoutRect.Empty : _tree.Piece(_at).Box;

    /// <summary>Where it sits on the page — worked out by climbing, so a question about many pieces walks <see cref="Placed"/> instead.</summary>
    public LayoutRect Bounds => _tree is null ? LayoutRect.Empty : _tree.WhereIs(_at);

    /// <summary>What it was drawn from, or nothing for a piece drawn from nothing anybody wrote.</summary>
    public ISourcePart? Part => _tree?.PartOf(_at);

    /// <summary>What it is, as its builder named it.</summary>
    public string Kind => _tree is null ? "" : _tree.KindOf(_at);

    /// <summary>
    /// The part that places this piece in the source: its own, or — where it was drawn from nothing anybody wrote — the one belonging
    /// to whatever it was drawn inside. Asked of the parse tree every time rather than copied onto the picture, where it would go stale
    /// the moment anything is edited.
    /// </summary>
    public ISourcePart? Naming()
    {
        if (Part is { } mine) return mine;
        foreach (var up in Ancestors())
            if (up.Part is { } theirs) return theirs;
        return null;
    }

    /// <summary>
    /// Whether this piece stands for a place in the source. Naming a part is enough — the part need not cover any characters, so an
    /// argument nobody has written yet is still a place with nothing in it, exactly where a caret can go and text land.
    /// </summary>
    public bool Stands() => Part is not null;

    /// <summary>
    /// Where this piece sits in the source: a piece drawn from a part is that part's stretch of it; one drawn from nothing anybody wrote
    /// is a point at the start of whatever it was drawn inside — it stands somewhere without standing for anything.
    /// </summary>
    public SourcePlace Sits() =>
        Part is { } part ? new SourcePlace(part.Start, part.Length) : new SourcePlace(Naming()?.Start ?? 0, 0);

    /// <summary>Where a caret may rest against it.</summary>
    public LayoutStops Stops => _tree is null ? LayoutStops.None : _tree.Piece(_at).Stops;

    /// <summary>What it drew, in the order it drew it, in its own frame.</summary>
    public ReadOnlySpan<LayoutMark> Marks => _tree is null ? default : _tree.MarksOf(_at);

    /// <summary>How it is drawn beyond its marks, or nothing — which is nearly always.</summary>
    public LayoutPaint? Painting => _tree?.PaintOf(_at);

    /// <summary>
    /// How the piece is turned inside whatever holds it, as one matrix about its own anchor — null for the ordinary case, which is
    /// every piece of most content.
    /// </summary>
    public LayoutMatrix? Turned
    {
        get
        {
            if (Painting?.Turn is not { Count: > 0 } turns) return null;

            var turned = LayoutMatrix.Identity;
            foreach (var turn in turns) turned *= turn.Matrix;

            return turned;
        }
    }

    /// <summary>The shape it stands in, in its own frame, where its builder said its box overstates it — or nothing, which is nearly always.</summary>
    public LayoutShape? Region => _tree?.RegionOf(_at);

    /// <summary>The run of text it is, with a caret position between any two of its letters — or nothing, which is nearly always.</summary>
    public LayoutWords? Words => _tree?.WordsOf(_at);

    /// <summary>What it answers to, or null where it answers to nothing.</summary>
    public LayoutActions? Acts => _tree?.ActsOf(_at);

    /// <summary>Where its anchor is on the page — the nought its box is measured from.</summary>
    internal LayoutVector Anchor => _tree is null ? default : _tree.AnchorOf(_at);

    /// <summary>
    /// The frame its marks, words, shape and children are measured in, and how that stands on the page — what a press comes back
    /// through into the piece, and what a caret, a wash or its shape goes out through.
    /// </summary>
    internal LayoutFrame Frame => _tree is null ? default : _tree.FrameOf(_at);

    /// <summary>What holds it, or nothing at the root.</summary>
    public LayoutPiece Parent => _tree is null ? default : _tree.At(_tree.Piece(_at).Parent);

    /// <summary>What it holds, in reading order.</summary>
    public PieceChildren Children => new(_tree, _at);

    /// <summary>It and everything inside it, parents first — a slice of the tree, not a walk of it.</summary>
    public PieceSubtree SelfAndDescendants() => new(_tree, _at);

    /// <summary>Its parent, grandparent and so on, nearest first.</summary>
    public IEnumerable<LayoutPiece> Ancestors()
    {
        for (var up = Parent; up.Exists; up = up.Parent) yield return up;
    }

    /// <summary>
    /// Everything drawn inside this piece — the leaves. A leaf <em>is</em> the drawing: a piece with nothing inside it exists because
    /// something was put on the page there. Drawn is not selectable — what a press means is the first ancestor that names a stretch of
    /// source, see <see cref="LayoutQuery.Selectable"/>.
    /// </summary>
    public IEnumerable<LayoutPiece> Leaves()
    {
        foreach (var piece in SelfAndDescendants())
            if (piece.IsLeaf) yield return piece;
    }

    /// <summary>Whether this is the drawing itself rather than a group holding it.</summary>
    public bool IsLeaf => _tree is null || _tree.Piece(_at).Extent == 1;

    /// <summary>How many pieces hold it. Nought at the root.</summary>
    public int Depth => _tree is null ? 0 : _tree.DepthOf(_at);

    /// <summary>
    /// It and everything inside it, each with where it sits on the page — the walk to use whenever the question is about more than one
    /// piece, since it carries the frame down rather than climbing to it for each.
    /// </summary>
    public IEnumerable<(LayoutPiece Piece, LayoutRect Where)> Placed()
    {
        if (_tree is null) yield break;
        foreach (var (at, where) in _tree.Within(_at)) yield return (new LayoutPiece(_tree, at), where);
    }

    /// <summary>
    /// Where a point of the page is in this piece's own frame — what a press is asked in against its shape or its words.
    /// </summary>
    public LayoutPoint Local(LayoutPoint page) => _tree is null ? page : _tree.FrameOf(_at).Back(page);

    /// <summary>
    /// The pieces that read together with this one, in order — the notes of a tune, a verse of lyrics. Itself alone when it is on no
    /// run, which is a run of one.
    /// </summary>
    public IReadOnlyList<LayoutPiece> Sharing(bool vertical)
    {
        if (_tree is null) return [];

        var run = _tree.Run(_tree.RunOf(_at, vertical));
        if (run.Length == 0) return [this];

        var members = new LayoutPiece[run.Length];
        for (var at = 0; at < run.Length; at++) members[at] = new LayoutPiece(_tree, run[at]);
        return members;
    }

    /// <summary>One step along a run, or nothing at either end and for a piece on no run.</summary>
    public LayoutPiece Along(bool vertical, bool forward) => _tree is null ? default : _tree.At(_tree.Along(_at, vertical, forward));

    /// <summary>Which run it reads along, or stacks in — an identity to compare, not a thing.</summary>
    public int RunOn(bool vertical) => _tree is null ? -1 : _tree.RunOf(_at, vertical);

    public override string ToString() =>
        _tree is null ? "nothing" : $"{Kind}{(Part is { } p ? $"[{p.Start},{p.Length}]" : "")} {Bounds}";

    /// <summary>The pieces one piece holds, walked without allocating.</summary>
    public readonly struct PieceChildren(LayoutTree? tree, int at) : IEnumerable<LayoutPiece>
    {
        public Enumerator GetEnumerator() => new(tree, at);
        IEnumerator<LayoutPiece> IEnumerable<LayoutPiece>.GetEnumerator() => GetEnumerator();
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

        /// <summary>The first child is the next piece along; the one after it is that child's whole subtree away.</summary>
        public struct Enumerator(LayoutTree? tree, int at) : IEnumerator<LayoutPiece>
        {
            private readonly int _last = tree is null ? 0 : at + tree.Piece(at).Extent;
            private int _next = tree is null ? 0 : at + 1;
            private int _now = -1;

            public readonly LayoutPiece Current => tree!.At(_now);
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
    public readonly struct PieceSubtree(LayoutTree? tree, int at) : IEnumerable<LayoutPiece>
    {
        public Enumerator GetEnumerator() => new(tree, at);
        IEnumerator<LayoutPiece> IEnumerable<LayoutPiece>.GetEnumerator() => GetEnumerator();
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

        public struct Enumerator(LayoutTree? tree, int at) : IEnumerator<LayoutPiece>
        {
            private readonly int _last = tree is null ? 0 : at + tree.Piece(at).Extent;
            private int _now = at - 1;

            public readonly LayoutPiece Current => tree!.At(_now);
            readonly object IEnumerator.Current => Current;

            public bool MoveNext() => tree is not null && ++_now < _last;

            public void Reset() => throw new NotSupportedException();
            public readonly void Dispose() { }
        }
    }
}
