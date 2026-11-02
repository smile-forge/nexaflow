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
    private readonly IReadOnlyDictionary<int, string>? _tips;
    private readonly IReadOnlyDictionary<int, LayoutMount>? _mounts;
    private readonly int[] _across;
    private readonly int[] _acrossAt;
    private readonly int[] _down;
    private readonly int[] _downAt;
    private readonly List<int[]> _runs;

    internal LayoutTree(ILayoutMedium medium, LayoutStored[] pieces, LayoutMark[] marks, ISourcePart?[] parts, string[] kinds,
                          LayoutPaint?[] paints, LayoutShape?[] regions, LayoutWords?[] words,
                          IReadOnlyDictionary<int, LayoutActions>? acts, IReadOnlyDictionary<int, string>? tips,
                          IReadOnlyDictionary<int, LayoutMount>? mounts, int[] across, int[] acrossAt, int[] down, int[] downAt, int[][] runs)
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
        _tips = tips;
        _mounts = mounts;
        _across = across;
        _acrossAt = acrossAt;
        _down = down;
        _downAt = downAt;
        _runs = [.. runs];
    }

    /// <summary>The same pieces, marks and runs, standing for other parts, and putting down other trees where <paramref name="mounts"/> says — see <see cref="Parted"/>.</summary>
    private LayoutTree(LayoutTree tree, ISourcePart?[] parts, IReadOnlyDictionary<int, LayoutMount>? mounts)
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
        _tips = tree._tips;
        _mounts = mounts is null ? tree._mounts : tree._mounts?.ToDictionary(pair => pair.Key, pair => mounts.TryGetValue(pair.Key, out var again) ? again : pair.Value);
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
    /// read again — and, where <paramref name="mounts"/> says, another tree put down for a mount. Shares everything else, so it costs one array.
    /// </summary>
    public LayoutTree Parted(ISourcePart?[] parts, IReadOnlyDictionary<int, LayoutMount>? mounts = null) =>
        parts.Length == _parts.Length ? new LayoutTree(this, parts, mounts) : throw new ArgumentException("a part for every piece", nameof(parts));

    /// <summary>
    /// Everywhere a caret may rest in this tree, in the order the right arrow visits them. Worked out on the first ask rather than at
    /// seal time, since a place is a mark on the page and a tree is settled onto the page only after it is sealed — and kept, since
    /// stepping the caret, snapping an offset to a stop and deciding what a press means all read this same list. A tree that puts trees
    /// down puts it together from theirs, each worked out once and kept with its tree (<see cref="MountedList{T}"/>) — whatever those mount
    /// in turn — with what its own top offers, or what its own pieces do, where a list gathered from every piece would put them.
    /// </summary>
    public IReadOnlyList<CaretPlace> Places => _places ??=
        this.Mounted<CaretPlace>(static tree => tree.Places,
                                 static (_, within) => place => new CaretPlace(Lifted(place.Against, within), place.Trailing),
                                 static (before, place) => before.Offset == place.Offset && LayoutQuery.Drawn(before, place),
                                 static root => root.Stands() && root.Stops.HasFlag(LayoutStops.Before)
                                     ? new(new CaretPlace(root, Trailing: false), place => place.Offset == root.Sits().Start)
                                     : null,
                                 static root => root.Stands() && root.Stops.HasFlag(LayoutStops.After) ? (true, new CaretPlace(root, Trailing: true)) : default)
        ?? this.Among<CaretPlace>(this.OwnPlaces, static tree => tree.Places,
                                  static (_, within) => place => new CaretPlace(Lifted(place.Against, within), place.Trailing),
                                  static place => place.Offset,
                                  static (before, place) => before.Offset == place.Offset && LayoutQuery.Drawn(before, place))
        ?? LayoutQuery.PlacesIn(Root);

    /// <summary>Every offset a caret may rest at, ascending, each once — the offsets of <see cref="Places"/>, put together the same way.</summary>
    public IReadOnlyList<int> Stops => _stops ??=
        this.Mounted<int>(static tree => tree.Stops, static (mount, _) => offset => offset + mount.Shift, static (before, offset) => before == offset,
                          static root => root.Stands() && root.Stops.HasFlag(LayoutStops.Before) ? new(root.Sits().Start, offset => offset == root.Sits().Start) : null,
                          static root => root.Stands() && root.Stops.HasFlag(LayoutStops.After) ? (true, root.Sits().End) : default)
        ?? this.Among<int>(Stopped(this.OwnPlaces), static tree => tree.Stops, static (mount, _) => offset => offset + mount.Shift, static offset => offset,
                           static (before, offset) => before == offset)
        ?? Stopped(this.Places);

    /// <summary>
    /// The pieces still waiting to be written in, in reading order — an argument left empty that the builder drew a box for — put
    /// together the same way.
    /// </summary>
    public IReadOnlyList<LayoutPiece> Holes => _holes ??=
        this.Mounted<LayoutPiece>(static tree => tree.Holes, static (_, within) => hole => Lifted(hole, within), static (_, _) => false,
                                  static _ => null, static _ => default)
        ?? Root.SelfAndDescendants().Where(piece => piece.Part is { Length: 0 }).OrderBy(piece => piece.Sits().Start).ToList();

    private IReadOnlyList<int>? _stops;
    private IReadOnlyList<LayoutPiece>? _holes;

    /// <summary>
    /// What this tree says of itself where it is only a piece holding the trees it mounts, put together from what each of them says —
    /// kept with it, seen where it is put down, through whatever it mounts in turn — and what this tree's own top says first and last;
    /// null where it holds anything else.
    /// </summary>
    private IReadOnlyList<T>? Mounted<T>(Func<LayoutTree, IReadOnlyList<T>> own, Func<LayoutMount, LayoutMounted, Func<T, T>> seen, Func<T, T, bool> same,
                                         Func<LayoutPiece, MountedList<T>.Leading?> first, Func<LayoutPiece, (bool Has, T Item)> last)
    {
        if (this.Count < 2) return null;

        var parts = new List<(IReadOnlyList<T> Own, Func<T, T> Seen)>(this.Count - 1);

        for (var at = 1; at < this.Count; at++)
        {
            if (this.MountOf(at) is not { } mount) return null;
            parts.Add((own(mount.Tree), seen(mount, new LayoutMounted(this, at, null))));
        }

    // Two trees put down one after the other can share a place only where the last stop of the one is the first of the other.
        var mounts = Enumerable.Range(1, this.Count - 1).Select(at => this.MountOf(at)!).ToArray();
        bool Meet(int before, int after) =>
            mounts[before].Tree.Stops is { Count: > 0 } ended && mounts[after].Tree.Stops is { Count: > 0 } begun
            && ended[^1] + mounts[before].Shift == begun[0] + mounts[after].Shift;

        return new MountedList<T>(parts, same, first(this.Root), last(this.Root), Meet);
    }

    /// <summary>Where a caret may rest against this tree's own pieces — not the trees it puts down — worked out once.</summary>
    private List<CaretPlace> OwnPlaces => _ownPlaces ??= LayoutQuery.Declared(this.OwnPieces());

    private List<CaretPlace>? _ownPlaces;

    /// <summary>This tree's own pieces, parents first: every one but those that put a tree down, and nothing of what those put down.</summary>
    private IEnumerable<LayoutPiece> OwnPieces()
    {
        for (var at = 0; at < this.Count; at++)
            if (_mounts is null || !_mounts.ContainsKey(at)) yield return new LayoutPiece(this, at);
    }

    /// <summary>
    /// What a tree that puts trees down among pieces of its own says of itself, put together from what its own pieces say (<paramref name="own"/>)
    /// and what each tree it puts down says — kept with that tree, seen where it is put down — one after another by where in the source
    /// each stands. Null where it puts none down, or where something of its own stands among what a tree it puts down stands for, so the
    /// two cannot be put one after the other.
    /// </summary>
    private IReadOnlyList<T>? Among<T>(IReadOnlyList<T> own, Func<LayoutTree, IReadOnlyList<T>> mounted, Func<LayoutMount, LayoutMounted, Func<T, T>> seen,
                                       Func<T, int> offset, Func<T, T, bool> same)
    {
        if (_mounts is not { Count: > 0 }) return null;

        var parts = new List<(IReadOnlyList<T> Own, Func<T, T> Seen)>(_mounts.Count * 2 + 1);
        var (next, reached) = (0, int.MinValue);

        void Owned(int to)
        {
            var run = new List<T>();
            while (next < own.Count && offset(own[next]) <= to) run.Add(own[next++]);
            if (run.Count > 0) parts.Add((run, static item => item));
        }

        foreach (var at in _mounts.Keys.Order())
        {
            var mount = _mounts[at];
            if (mount.Tree.Stops is not { Count: > 0 } stops) continue;

            // Where in the source the tree put down stands, from its first stop to its last: what is the holder's own before it goes first.
            var (from, to) = (stops[0] + mount.Shift, stops[^1] + mount.Shift);
            if (from < reached) return null;

            Owned(from);
            if (next < own.Count && offset(own[next]) < to) return null;

            parts.Add((mounted(mount.Tree), seen(mount, new LayoutMounted(this, at, null))));
            reached = to;
        }

        Owned(int.MaxValue);

        return new MountedList<T>(parts, same);
    }

    /// <summary><paramref name="piece"/>, of a tree put down at <paramref name="outer"/>, seen from outside it — through every mount it was already seen through, and that one.</summary>
    private static LayoutPiece Lifted(LayoutPiece piece, LayoutMounted outer) => new(piece.Tree!, piece.At, piece.Within is { } inner ? Under(inner, outer) : outer);

    /// <summary>The way out through <paramref name="inner"/>, carried on through <paramref name="outer"/> where it ends.</summary>
    private static LayoutMounted Under(LayoutMounted inner, LayoutMounted outer) => inner with { In = inner.In is { } further ? Under(further, outer) : outer };

    /// <summary>The offsets of <paramref name="places"/>, each once.</summary>
    private static List<int> Stopped(IReadOnlyList<CaretPlace> places)
    {
        var stops = new List<int>();

        foreach (var place in places)
            if (stops.Count == 0 || stops[^1] != place.Offset) stops.Add(place.Offset);

        return stops;
    }

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

    /// <summary>What the piece at <paramref name="at"/> says while it is pointed at, as its builder put it in the tips, or null where it says nothing.</summary>
    internal string? TipOf(int at) => _tips is not null && _tips.TryGetValue(at, out var tip) ? tip : null;

    /// <summary>The tree put down as the piece at <paramref name="at"/> (<see cref="LayoutMount"/>), or null for a piece of this tree's own.</summary>
    internal LayoutMount? MountOf(int at) => _mounts is not null && _mounts.TryGetValue(at, out var mount) ? mount : null;

    /// <summary>Each tree put down in this one, with the piece it is put down as — none, for a tree that puts none down.</summary>
    internal IEnumerable<(int At, LayoutMount Mount)> Mounts => _mounts is null ? [] : _mounts.Select(static pair => (pair.Key, pair.Value));

    /// <summary>Whether <paramref name="other"/> is this tree's drawing: this tree, or one parted from it (<see cref="Parted"/>), which shares its pieces.</summary>
    internal bool SameDrawing(LayoutTree other) => ReferenceEquals(this.Drawing, other.Drawing);

    /// <summary>What this tree and every tree parted from it share (<see cref="SameDrawing"/>) — something to keep what was worked out of the drawing against.</summary>
    internal object Drawing => _pieces;

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

    /// <summary>
    /// Every piece of a subtree with where it sits, the frame carried down rather than climbed to — on the page, or through
    /// <paramref name="outer"/>, where this tree is put down inside another (<see cref="LayoutMount"/>). A tree put down inside this
    /// one is its mount here, and is walked by whatever walks the mounts (<see cref="LayoutPiece.Placed"/>).
    /// </summary>
    internal IEnumerable<(int At, LayoutRect Where)> Within(int from, LayoutFrame? outer = null)
    {
        if (from < 0 || from >= _pieces.Length) yield break;

        var frames = new (int End, LayoutVector Anchor, int Shaped)[16];
        List<LayoutMatrix>? shapes = null;

        var up = _pieces[from].Parent;
        var start = up < 0 ? outer ?? default : Placed(outer, FrameOf(up));
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

    /// <summary><paramref name="local"/>, a frame measured from this tree's nought, as it stands through <paramref name="outer"/>.</summary>
    private static LayoutFrame Placed(LayoutFrame? outer, LayoutFrame local)
    {
        if (outer is not { } frame) return local;

        var anchor = (LayoutVector)frame.Out(new LayoutPoint(local.Anchor.X, local.Anchor.Y));
        return new LayoutFrame(anchor, local.Shaped is { } inner ? (frame.Shaped is { } shaped ? inner * shaped : inner) : frame.Shaped);
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
