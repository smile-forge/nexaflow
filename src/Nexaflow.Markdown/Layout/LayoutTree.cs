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
