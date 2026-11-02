using System.Collections;
using Nexaflow.Markdown.Ast;

namespace Nexaflow.Markdown.Layout;

/// <summary>
/// One piece of a <see cref="LayoutTree"/>, seen: a handle, the tree and an index — and, for a piece of a tree put down inside another
/// (<see cref="LayoutMount"/>), the mount it is seen through. Everything a piece is lives in the tree; this is how it is asked, and through
/// the mount it is placed on the page and names its part as the tree holding it sees it.
/// </summary>
public readonly record struct LayoutPiece
{
    private readonly LayoutTree? _tree;
    private readonly int _at;
    private readonly LayoutMounted? _in;

    internal LayoutPiece(LayoutTree tree, int at, LayoutMounted? within = null)
    {
        _tree = at < 0 ? null : tree;
        _at = at < 0 ? -1 : at;
        _in = at < 0 ? null : within;
    }

    /// <summary>Whether this is a piece at all, rather than the nothing a question that found none answers with.</summary>
    public bool Exists => _tree is not null;

    /// <summary>The tree it belongs to, or nothing — for a piece seen through a mount, the mounted tree.</summary>
    public LayoutTree? Tree => _tree;

    /// <summary>Where it stands in its tree — how a handle is made, and the only thing an index means.</summary>
    public int At => _at;

    /// <summary>The mount it is seen through, or null for a piece of the tree it is asked of.</summary>
    internal LayoutMounted? Within => _in;

    /// <summary>
    /// Where its frame begins, relative to its parent's — for the top of a tree seen through its mount, relative to whatever holds the
    /// mount, since the mount is no piece of its own.
    /// </summary>
    public LayoutVector Offset =>
        _tree is null ? default : _at == 0 && _in is { } mounted ? mounted.Holder.Piece(mounted.At).Offset + _tree.Piece(0).Offset : _tree.Piece(_at).Offset;

    /// <summary>How far it reaches, relative to its own anchor.</summary>
    public LayoutRect Box => _tree is null ? LayoutRect.Empty : _tree.Piece(_at).Box;

    /// <summary>Where it sits on the page — worked out by climbing, so a question about many pieces walks <see cref="Placed"/> instead.</summary>
    public LayoutRect Bounds
    {
        get
        {
            if (_tree is null) return LayoutRect.Empty;
            if (_in is null) return _tree.WhereIs(_at);

            var box = _tree.Piece(_at).Box;
            return box.IsEmpty ? box : LayoutMounted.Composed(_in, _tree.PlacingOf(_at)).Out(box);
        }
    }

    /// <summary>What it was drawn from, or nothing for a piece drawn from nothing anybody wrote — as the tree it is asked of names it.</summary>
    public ISourcePart? Part => _tree is null ? null : _in is null ? _tree.PartOf(_at) : _in.Restated(_tree.PartOf(_at));

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
    public bool Stands() => _tree is not null && _tree.PartOf(_at) is not null;

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

    /// <summary>What it says while it is pointed at, as its builder put it in the tree's tips — or null where it says nothing.</summary>
    public string? Tip => _tree?.TipOf(_at);

    /// <summary>Where its anchor is on the page — the nought its box is measured from.</summary>
    internal LayoutVector Anchor => _tree is null ? default : (LayoutVector)LayoutMounted.Composed(_in, _tree.PlacingOf(_at)).Out(default(LayoutPoint));

    /// <summary>
    /// The frame its marks, words, shape and children are measured in, and how that stands on the page — what a press comes back
    /// through into the piece, and what a caret, a wash or its shape goes out through.
    /// </summary>
    internal LayoutFrame Frame => _tree is null ? default : LayoutMounted.Composed(_in, _tree.FrameOf(_at));

    /// <summary>What holds it, or nothing at the root — through the mount it is seen through, where it stands at the mounted tree's top.</summary>
    public LayoutPiece Parent => _tree is null ? default : Up(_tree, _tree.Piece(_at).Parent, _in);

    /// <summary>What it holds, in reading order — and what any tree put down inside it holds, as though it were its own.</summary>
    public PieceChildren Children => new(_tree, _at, _in);

    /// <summary>
    /// What it holds as it is drawn, in reading order: a tree put down among them stands as that tree's top, seen through its mount, rather
    /// than being gone through — a top is drawn as it was laid, its frame, paint and marks, though no question of the layout meets it as a
    /// piece (<see cref="Children"/>).
    /// </summary>
    internal PieceDrawn DrawnChildren => new(_tree, _at, _in);

    /// <summary>What holds it as it is drawn — <see cref="Parent"/>, except that the top of a tree put down inside another is not gone through.</summary>
    internal LayoutPiece DrawnHolder
    {
        get
        {
            if (_tree is null) return default;
            if (_tree.Piece(_at).Parent is var parent and >= 0) return new LayoutPiece(_tree, parent, _in);

            return _in is { } mounted ? new LayoutPiece(mounted.Holder, mounted.Holder.Piece(mounted.At).Parent, mounted.In) : default;
        }
    }

    /// <summary>It and everything inside it, parents first — a slice of the tree, and of every tree put down inside it.</summary>
    public PieceSubtree SelfAndDescendants() => new(_tree, _at, _in);

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
    public bool IsLeaf => _tree is null || (_tree.Piece(_at).Extent == 1 && _tree.MountOf(_at) is null);

    /// <summary>How many pieces hold it. Nought at the root.</summary>
    public int Depth => _tree is null ? 0 : LayoutMounted.Seen(_in, _tree.DepthOf(_at));

    /// <summary>
    /// It and everything inside it, each with where it sits on the page — the walk to use whenever the question is about more than one
    /// piece, since it carries the frame down rather than climbing to it for each.
    /// </summary>
    public IEnumerable<(LayoutPiece Piece, LayoutRect Where)> Placed() =>
        _tree is null ? [] : Walked(_tree, _at, _in, _in?.Frame);

    /// <summary>Every piece from <paramref name="from"/> down in <paramref name="tree"/>, and down every tree put down inside it, placed.</summary>
    private static IEnumerable<(LayoutPiece Piece, LayoutRect Where)> Walked(LayoutTree tree, int from, LayoutMounted? within, LayoutFrame? outer)
    {
        foreach (var (at, where) in tree.Within(from, outer))
        {
            if (tree.MountOf(at) is not { } mount)
            {
                yield return (new LayoutPiece(tree, at, within), where);
                continue;
            }

            // The mount and the mounted tree's top are no pieces of their own: what the top holds is walked as though laid here.
            var mounted = new LayoutMounted(tree, at, within);
            var frame = mounted.Frame;
            var inner = mount.Tree;

            for (var child = 1; child < inner.Count; child += inner.Piece(child).Extent)
                foreach (var placed in Walked(inner, child, mounted, frame))
                    yield return placed;
        }
    }

    /// <summary>
    /// Where a point of the page is in this piece's own frame — what a press is asked in against its shape or its words.
    /// </summary>
    public LayoutPoint Local(LayoutPoint page) => _tree is null ? page : this.Frame.Back(page);

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
        for (var at = 0; at < run.Length; at++) members[at] = new LayoutPiece(_tree, run[at], _in);
        return members;
    }

    /// <summary>One step along a run, or nothing at either end and for a piece on no run.</summary>
    public LayoutPiece Along(bool vertical, bool forward) =>
        _tree is null || _tree.Along(_at, vertical, forward) is var to && to < 0 ? default : new LayoutPiece(_tree, to, _in);

    /// <summary>Which run it reads along, or stacks in — an identity to compare, not a thing.</summary>
    public int RunOn(bool vertical) => _tree is null ? -1 : _tree.RunOf(_at, vertical);

    public override string ToString() =>
        _tree is null ? "nothing" : $"{Kind}{(Part is { } p ? $"[{p.Start},{p.Length}]" : "")} {Bounds}";

    /// <summary>
    /// The piece at <paramref name="parent"/> in <paramref name="tree"/>, seen through <paramref name="within"/> — or, where that is the top
    /// of a tree put down inside another, which is no piece of its own, whatever holds its mount.
    /// </summary>
    private static LayoutPiece Up(LayoutTree tree, int parent, LayoutMounted? within)
    {
        if (parent < 0) return default;
        if (parent > 0 || within is null) return new LayoutPiece(tree, parent, within);

        return Up(within.Holder, within.Holder.Piece(within.At).Parent, within.In);
    }

    /// <summary>One run of pieces a walk is going along: a tree, the next index in it, where the run ends, and the mount it is seen through.</summary>
    private struct Walk(LayoutTree tree, int next, int last, LayoutMounted? within)
    {
        public readonly LayoutTree Tree = tree;
        public int Next = next;
        public readonly int Last = last;
        public readonly LayoutMounted? Within = within;
    }

    /// <summary>The pieces one piece holds, walked without allocating unless a tree is put down among them.</summary>
    public readonly struct PieceChildren : IEnumerable<LayoutPiece>
    {
        private readonly LayoutTree? _tree;
        private readonly int _at;
        private readonly LayoutMounted? _within;

        internal PieceChildren(LayoutTree? tree, int at, LayoutMounted? within) => (_tree, _at, _within) = (tree, at, within);

        public Enumerator GetEnumerator() => new(_tree, _at, _within);
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

        /// <summary>
        /// The first child is the next piece along; the one after it is that child's whole subtree away. A mount among them is gone into:
        /// what its tree's top holds is walked in its place.
        /// </summary>
        public struct Enumerator : IEnumerator<LayoutPiece>
        {
            private Walk _walk;
            private Stack<Walk>? _out;
            private LayoutPiece _now;

            internal Enumerator(LayoutTree? tree, int at, LayoutMounted? within) =>
                _walk = tree is null ? default : new Walk(tree, at + 1, at + tree.Piece(at).Extent, within);

            public readonly LayoutPiece Current => _now;
            readonly object IEnumerator.Current => Current;

            public bool MoveNext()
            {
                while (true)
                {
                    if (_walk.Tree is null) return false;

                    if (_walk.Next >= _walk.Last)
                    {
                        if (_out is not { Count: > 0 }) return false;

                        _walk = _out.Pop();
                        continue;
                    }

                    var now = _walk.Next;
                    _walk.Next += _walk.Tree.Piece(now).Extent;

                    if (_walk.Tree.MountOf(now) is { } mount)
                    {
                        (_out ??= new()).Push(_walk);
                        _walk = new Walk(mount.Tree, 1, mount.Tree.Count, new LayoutMounted(_walk.Tree, now, _walk.Within));
                        continue;
                    }

                    _now = new LayoutPiece(_walk.Tree, now, _walk.Within);
                    return true;
                }
            }

            public void Reset() => throw new NotSupportedException();
            public readonly void Dispose() { }
        }
    }

    /// <summary>The pieces one piece holds as they are drawn (<see cref="DrawnChildren"/>), walked without allocating unless a tree is put down among them.</summary>
    internal readonly struct PieceDrawn
    {
        private readonly LayoutTree? _tree;
        private readonly int _at;
        private readonly LayoutMounted? _within;

        internal PieceDrawn(LayoutTree? tree, int at, LayoutMounted? within) => (_tree, _at, _within) = (tree, at, within);

        public Enumerator GetEnumerator() => new(_tree, _at, _within);

        /// <summary>The first child is the next piece along; the one after it is that child's whole subtree away. A mount among them is its tree's top.</summary>
        public struct Enumerator
        {
            private readonly LayoutTree? _tree;
            private readonly LayoutMounted? _within;
            private readonly int _last;
            private int _next;

            internal Enumerator(LayoutTree? tree, int at, LayoutMounted? within)
            {
                (_tree, _within) = (tree, within);
                (_next, _last) = tree is null ? (0, 0) : (at + 1, at + tree.Piece(at).Extent);
            }

            public LayoutPiece Current { get; private set; }

            public bool MoveNext()
            {
                if (_tree is null || _next >= _last) return false;

                var now = _next;
                _next += _tree.Piece(now).Extent;

                this.Current = _tree.MountOf(now) is { } mount ? new LayoutPiece(mount.Tree, 0, new LayoutMounted(_tree, now, _within)) : new LayoutPiece(_tree, now, _within);
                return true;
            }
        }
    }

    /// <summary>A piece and everything inside it — a contiguous run of the tree, and of every tree put down inside it.</summary>
    public readonly struct PieceSubtree : IEnumerable<LayoutPiece>
    {
        private readonly LayoutTree? _tree;
        private readonly int _at;
        private readonly LayoutMounted? _within;

        internal PieceSubtree(LayoutTree? tree, int at, LayoutMounted? within) => (_tree, _at, _within) = (tree, at, within);

        public Enumerator GetEnumerator() => new(_tree, _at, _within);
        IEnumerator<LayoutPiece> IEnumerable<LayoutPiece>.GetEnumerator() => GetEnumerator();
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

        public struct Enumerator : IEnumerator<LayoutPiece>
        {
            private Walk _walk;
            private Stack<Walk>? _out;
            private LayoutPiece _now;

            internal Enumerator(LayoutTree? tree, int at, LayoutMounted? within) =>
                _walk = tree is null ? default : new Walk(tree, at, at + tree.Piece(at).Extent, within);

            public readonly LayoutPiece Current => _now;
            readonly object IEnumerator.Current => Current;

            public bool MoveNext()
            {
                while (true)
                {
                    if (_walk.Tree is null) return false;

                    if (_walk.Next >= _walk.Last)
                    {
                        if (_out is not { Count: > 0 }) return false;

                        _walk = _out.Pop();
                        continue;
                    }

                    var now = _walk.Next++;

                    if (_walk.Tree.MountOf(now) is { } mount)
                    {
                        (_out ??= new()).Push(_walk);
                        _walk = new Walk(mount.Tree, 1, mount.Tree.Count, new LayoutMounted(_walk.Tree, now, _walk.Within));
                        continue;
                    }

                    _now = new LayoutPiece(_walk.Tree, now, _walk.Within);
                    return true;
                }
            }

            public void Reset() => throw new NotSupportedException();
            public readonly void Dispose() { }
        }
    }
}
