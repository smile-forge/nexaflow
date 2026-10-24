using System.Collections;
using Nexaflow.Markdown.Ast;

namespace Nexaflow.Markdown.Layout;

/// <summary>Where a caret may rest against a piece.</summary>
[Flags]
public enum PortableStops
{
    None = 0,
    Before = 1,
    After = 2,
    Both = Before | After,
}

/// <summary>Which side of the block a piece stands against.</summary>
public enum PortableSide
{
    Left,
    Centre,
    Right,
}

/// <summary>A laid-out piece as it is stored: its anchor in its parent, its reach about that anchor, its subtree and its drawing.</summary>
internal readonly record struct PortableStored
{
    public required LayoutVector Offset { get; init; }

    public required LayoutRect Box { get; init; }

    public required int Parent { get; init; }

    public required int Extent { get; init; }

    public required int Marks { get; init; }

    public required int MarkCount { get; init; }

    public required PortableStops Stops { get; init; }
}

/// <summary>Where a frame of a layout stands on the page, and how what is measured in it is turned or scaled on the way.</summary>
internal readonly record struct PortableFrame(LayoutVector Anchor, LayoutMatrix? Shaped)
{
    public PortableFrame Placing(LayoutVector offset) =>
        new(Anchor + (Shaped is { } shaped ? shaped.Transform(offset) : offset), Shaped);

    public PortableFrame Inside(LayoutMatrix? applied) =>
        applied is not { } own ? this : new(Anchor, Shaped is { } shaped ? own * shaped : own);

    public LayoutPoint Out(LayoutPoint local) => (Shaped is { } shaped ? shaped.Transform(local) : local) + Anchor;

    public LayoutRect Out(LayoutRect local)
    {
        if (local.IsEmpty) return local;
        if (Shaped is { } shaped) local = LayoutRect.Transform(local, shaped);

        return new LayoutRect(local.X + Anchor.X, local.Y + Anchor.Y, local.Width, local.Height);
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
public sealed class PortableTree
{
    private readonly PortableStored[] _pieces;
    private readonly PortableMark[] _marks;
    private readonly ISourcePart?[] _parts;
    private readonly string[] _kinds;
    private readonly PortablePaint?[] _paints;
    private readonly LayoutShape?[] _regions;
    private readonly PortableWords?[] _words;
    private readonly IReadOnlyDictionary<int, PortableActions>? _acts;
    private readonly int[] _across;
    private readonly int[] _acrossAt;
    private readonly int[] _down;
    private readonly int[] _downAt;
    private readonly List<int[]> _runs;

    internal PortableTree(PortableStored[] pieces, PortableMark[] marks, ISourcePart?[] parts, string[] kinds,
                          PortablePaint?[] paints, LayoutShape?[] regions, PortableWords?[] words,
                          IReadOnlyDictionary<int, PortableActions>? acts, int[] across, int[] acrossAt, int[] down, int[] downAt,
                          int[][] runs)
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

    public int Count => _pieces.Length;

    public PortablePiece Root => new(this, _pieces.Length == 0 ? -1 : 0);

    public PortablePiece At(int index) => new(this, index >= 0 && index < _pieces.Length ? index : -1);

    internal ref readonly PortableStored Piece(int at) => ref _pieces[at];

    internal ISourcePart? PartOf(int at) => _parts[at];

    internal string KindOf(int at) => _kinds[at];

    internal PortablePaint? PaintOf(int at) => _paints[at];

    internal LayoutShape? RegionOf(int at) => _regions[at];

    internal PortableWords? WordsOf(int at) => _words[at];

    /// <summary>What the piece at <paramref name="at"/> answers to, or null where it answers to nothing.</summary>
    internal PortableActions? ActsOf(int at) => _acts is not null && _acts.TryGetValue(at, out var acts) ? acts : null;

    internal ReadOnlySpan<PortableMark> MarksOf(int at) => _marks.AsSpan(_pieces[at].Marks, _pieces[at].MarkCount);

    internal PortableFrame PlacingOf(int at)
    {
        var anchor = _pieces[at].Offset;
        var applied = false;

        for (var up = _pieces[at].Parent; up >= 0; up = _pieces[up].Parent)
        {
            anchor += _pieces[up].Offset;
            applied |= _paints[up] is { Applies: true };
        }

        return applied ? FrameOf(_pieces[at].Parent).Placing(_pieces[at].Offset) : new PortableFrame(anchor, null);
    }

    internal PortableFrame FrameOf(int at) => PlacingOf(at).Inside(_paints[at]?.Applied);

    internal LayoutRect WhereIs(int at)
    {
        var box = _pieces[at].Box;
        return box.IsEmpty ? box : PlacingOf(at).Out(box);
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
                var placing = new PortableFrame(frames[top].Anchor, shapes![shaped]).Placing(piece.Offset);
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

    /// <summary>Moves the whole tree by moving the one anchor nothing is measured from.</summary>
    public void Settle(LayoutVector by)
    {
        if (_pieces.Length == 0 || (by.X == 0 && by.Y == 0)) return;

        _pieces[0] = _pieces[0] with { Offset = _pieces[0].Offset + by };
    }
}

/// <summary>One piece of a <see cref="PortableTree"/>, seen: a handle, the tree and an index.</summary>
public readonly record struct PortablePiece
{
    private readonly PortableTree? _tree;
    private readonly int _at;

    internal PortablePiece(PortableTree tree, int at)
    {
        _tree = at < 0 ? null : tree;
        _at = at < 0 ? -1 : at;
    }

    public bool Exists => _tree is not null;

    public int At => _at;

    public LayoutVector Offset => _tree is null ? default : _tree.Piece(_at).Offset;

    public LayoutRect Box => _tree is null ? LayoutRect.Empty : _tree.Piece(_at).Box;

    public LayoutRect Bounds => _tree is null ? LayoutRect.Empty : _tree.WhereIs(_at);

    public ISourcePart? Part => _tree?.PartOf(_at);

    public string Kind => _tree is null ? "" : _tree.KindOf(_at);

    public PortableStops Stops => _tree is null ? PortableStops.None : _tree.Piece(_at).Stops;

    public ReadOnlySpan<PortableMark> Marks => _tree is null ? default : _tree.MarksOf(_at);

    public PortablePaint? Painting => _tree?.PaintOf(_at);

    public LayoutShape? Region => _tree?.RegionOf(_at);

    public PortableWords? Words => _tree?.WordsOf(_at);

    public PortablePiece Parent => _tree is null ? default : _tree.At(_tree.Piece(_at).Parent);

    public PieceChildren Children => new(_tree, _at);

    /// <summary>
    /// Where a point of the page is in this piece's own frame — what a press is asked in against its shape or its words.
    /// </summary>
    public LayoutPoint Local(LayoutPoint page) => _tree is null ? page : _tree.FrameOf(_at).Back(page);

    /// <summary>It and everything inside it, each with where it sits on the page.</summary>
    public IEnumerable<(PortablePiece Piece, LayoutRect Where)> Placed()
    {
        if (_tree is null) yield break;
        foreach (var (at, where) in _tree.Within(_at)) yield return (new PortablePiece(_tree, at), where);
    }

    /// <summary>The pieces one piece holds, walked without allocating.</summary>
    public readonly struct PieceChildren(PortableTree? tree, int at) : IEnumerable<PortablePiece>
    {
        public Enumerator GetEnumerator() => new(tree, at);
        IEnumerator<PortablePiece> IEnumerable<PortablePiece>.GetEnumerator() => GetEnumerator();
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

        public struct Enumerator(PortableTree? tree, int at) : IEnumerator<PortablePiece>
        {
            private readonly int _last = tree is null ? 0 : at + tree.Piece(at).Extent;
            private int _next = tree is null ? 0 : at + 1;
            private int _now = -1;

            public readonly PortablePiece Current => tree!.At(_now);
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
}
