using Nexaflow.Markdown.Ast;

namespace Nexaflow.Markdown.Layout;

/// <summary>
/// How a <see cref="LayoutTree"/> is made: open a piece, draw into it, put pieces inside it, close it. The anchor is fixed on
/// open and the reach worked out on close, so a subtree means the same wherever it is put down.
/// </summary>
public sealed class LayoutBuilder
{
    private readonly List<LayoutStored> _pieces;
    private readonly List<ISourcePart?> _parts;
    private readonly List<string> _kinds;
    private readonly List<LayoutPaint?> _paints;
    private readonly List<LayoutShape?> _regions;
    private readonly List<LayoutWords?> _words;
    private readonly Dictionary<int, LayoutActions> _acts = [];
    private readonly List<LayoutMark> _marks;
    private readonly List<(int[] Members, bool Vertical)> _runs = [];
    private readonly List<(int At, LayoutSide Side, double Clear)> _sides = [];
    private readonly Stack<Frame> _open = new();
    private readonly Stack<Frame> _spare = new();
    private readonly ILayoutMedium _medium;

    /// <param name="medium">What the tree is laid out for — what its shapes are resolved by.</param>
    /// <param name="pieces">How many pieces this will hold, where that is known.</param>
    public LayoutBuilder(ILayoutMedium medium, int pieces = 0)
    {
        _medium = medium;

        var room = Math.Max(pieces, Fewest);

        _pieces = new(room);
        _parts = new(room);
        _kinds = new(room);
        _paints = new(room);
        _regions = new(room);
        _words = new(room);
        _marks = new(room);
    }

    private const int Fewest = 32;

    /// <summary>What the layout is made in — what anything placing a piece of its own asks for a turn or an outline.</summary>
    public ILayoutMedium Medium => _medium;

    private sealed class Frame
    {
        public int At;
        public LayoutVector Offset;
        public LayoutRect Box;
        public LayoutStops Stops;
        public (double Top, double Height)? Room;
        public bool Gathers;
        public LayoutPaint? Paints;
        public readonly List<LayoutMark> Marks = [];

        public void Covers(LayoutRect what)
        {
            if (what.IsEmpty) return;
            Box = Box.IsEmpty ? what : LayoutRect.Union(Box, what);
        }

        public void Gathered(LayoutRect what)
        {
            if (Gathers) Covers(what);
        }
    }

    public int Open(string kind, ISourcePart? part = null, LayoutPoint at = default, LayoutStops stops = LayoutStops.Both,
                    bool gathers = true, LayoutPaint? paints = null)
    {
        var frame = _spare.Count > 0 ? _spare.Pop() : new Frame();

        frame.At = _pieces.Count;
        frame.Offset = new LayoutVector(at.X, at.Y);
        frame.Box = LayoutRect.Empty;
        frame.Room = null;
        frame.Stops = stops;
        frame.Gathers = gathers;
        frame.Paints = paints is { Matters: true } ? paints : null;
        frame.Marks.Clear();

        _pieces.Add(default);
        _parts.Add(part);
        _kinds.Add(kind);
        _paints.Add(frame.Paints);
        _regions.Add(null);
        _words.Add(null);

        _open.Push(frame);
        return frame.At;
    }

    public LayoutVector Anchor
    {
        get
        {
            var anchor = default(LayoutVector);
            foreach (var frame in _open) anchor += frame.Offset;
            return anchor;
        }
    }

    public LayoutRect Reached => _open.Count == 0 ? LayoutRect.Empty : _open.Peek().Box;

    public void Draw(LayoutMark mark)
    {
        var frame = _open.Peek();
        frame.Marks.Add(mark);
        frame.Gathered(mark.Covers);
    }

    public void Covers(LayoutRect what) => _open.Peek().Covers(what);

    /// <summary>Says the piece being built stands in exactly this shape, resolved once so a press is cheap to answer.</summary>
    public void Occupies(LayoutShape region) => _regions[_open.Peek().At] = _medium.Resolved(region);

    public void Words(LayoutWords words) => _words[_open.Peek().At] = words;

    /// <summary>Says what the open piece answers to.</summary>
    public void Acts(LayoutActions actions) => _acts[_open.Peek().At] = actions;

    /// <summary>Says the open piece leads somewhere: one press on it follows it.</summary>
    public void Links(string href, string? tip = null) =>
        Acts(new LayoutActions { Click = new LayoutAsk(LayoutVerbs.Navigate, href, tip) });

    public void Reserves(double top, double height) => _open.Peek().Room = (top, height);

    public void Against(LayoutSide side, double clear = 0) => _sides.Add((_open.Peek().At, side, clear));

    /// <summary>Puts a finished tree down inside whatever is open — a block copy, every index moved by the same amount.</summary>
    public int Graft(LayoutTree tree, LayoutPoint at = default, LayoutSide? against = null, double clear = 0,
                     IReadOnlyList<ISourcePart?>? parts = null)
    {
        if (tree.Count == 0) return -1;

        var first = _pieces.Count;
        var parent = _open.Count == 0 ? -1 : _open.Peek().At;

        for (var piece = 0; piece < tree.Count; piece++)
        {
            var stored = tree.Piece(piece);
            var marks = _marks.Count;
            foreach (var mark in tree.MarksOf(piece)) _marks.Add(mark);

            var root = stored.Parent < 0;

            _pieces.Add(stored with
            {
                Offset = root ? stored.Offset + new LayoutVector(at.X, at.Y) : stored.Offset,
                Parent = root ? parent : stored.Parent + first,
                Marks = marks,
            });

            _parts.Add(parts is null ? tree.PartOf(piece) : parts[piece]);
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

        if (_open.Count > 0)
            for (var piece = first; piece < _pieces.Count; piece++)
            {
                var top = _pieces[piece];
                if (top.Parent != parent || top.Box.IsEmpty) continue;

                _open.Peek().Gathered(LayoutRect.Offset(top.Box, top.Offset));
            }

        if (against is { } side) _sides.Add((first, side, clear));
        return first;
    }

    public int Close()
    {
        var frame = _open.Pop();
        var at = frame.At;
        var marks = _marks.Count;

        _marks.AddRange(frame.Marks);

        if (frame.Gathers && frame.Paints?.Applied is { } applied && !frame.Box.IsEmpty)
            frame.Box = LayoutRect.Transform(frame.Box, applied);

        var reach = frame.Box;
        if (frame.Room is { } room && !frame.Box.IsEmpty)
            frame.Box = new LayoutRect(frame.Box.X, room.Top, frame.Box.Width, room.Height);

        _pieces[at] = new LayoutStored
        {
            Offset = frame.Offset,
            Box = frame.Box,
            Parent = _open.Count == 0 ? -1 : _open.Peek().At,
            Extent = _pieces.Count - at,
            Marks = marks,
            MarkCount = frame.Marks.Count,
            Stops = frame.Stops,
        };

        if (_open.Count > 0 && !reach.IsEmpty)
            _open.Peek().Gathered(LayoutRect.Offset(reach, frame.Offset));

        _spare.Push(frame);
        return at;
    }

    public void Runs(IReadOnlyList<int> members, bool vertical)
    {
        if (members.Count > 1) _runs.Add(([.. members], vertical));
    }

    public LayoutTree Seal(double block = 0)
    {
        if (_open.Count > 0)
            throw new InvalidOperationException($"{_open.Count} piece(s) were opened and never closed");

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

                if (vertical) { down[at] = run; downAt[at] = member; }
                else { across[at] = run; acrossAt[at] = member; }
            }
        }

        return new LayoutTree(_medium, [.. _pieces], [.. _marks], [.. _parts], [.. _kinds], [.. _paints], [.. _regions], [.. _words],
                                _acts.Count == 0 ? null : new Dictionary<int, LayoutActions>(_acts), across, acrossAt, down, downAt,
                                runs);
    }

    private void Align(double block)
    {
        foreach (var right in new[] { false, true })
            foreach (var (at, side, clear) in _sides)
            {
                if ((side == LayoutSide.Right) != right) continue;

                var piece = _pieces[at];
                if (piece.Box.IsEmpty) continue;

                var box = LayoutRect.Offset(piece.Box, piece.Offset);
                var beside = Beside(at);
                var (x, y) = (box.X, box.Y);

                switch (side)
                {
                    case LayoutSide.Left:
                        x = 0;
                        break;

                    case LayoutSide.Centre when block > 0:
                        x = Math.Max(0, (block - box.Width) / 2);
                        break;

                    case LayoutSide.Right when block <= 0:
                        if (!beside.IsEmpty) x = beside.Right + clear;
                        break;

                    case LayoutSide.Right:
                        x = Math.Max(0, block - box.Width);
                        if (!beside.IsEmpty && x < beside.Right + clear) y = beside.Bottom;
                        break;
                }

                _pieces[at] = piece with { Offset = piece.Offset + new LayoutVector(x - box.X, y - box.Y) };
                Regather(piece.Parent);
            }
    }

    private LayoutRect Beside(int at)
    {
        var holder = _pieces[at].Parent;
        var union = LayoutRect.Empty;

        for (var other = 0; other < _pieces.Count; other++)
            if (other != at && _pieces[other].Parent == holder && !_pieces[other].Box.IsEmpty)
                union = LayoutRect.Union(union, LayoutRect.Offset(_pieces[other].Box, _pieces[other].Offset));

        return union;
    }

    private void Regather(int holder)
    {
        if (holder < 0) return;

        var union = LayoutRect.Empty;
        for (var child = 0; child < _pieces.Count; child++)
            if (_pieces[child].Parent == holder && !_pieces[child].Box.IsEmpty)
                union = LayoutRect.Union(union, LayoutRect.Offset(_pieces[child].Box, _pieces[child].Offset));

        if (!union.IsEmpty) _pieces[holder] = _pieces[holder] with { Box = union };
    }

    public void Clear()
    {
        if (_open.Count > 0) throw new InvalidOperationException($"{_open.Count} piece(s) were opened and never closed");

        _pieces.Clear();
        _parts.Clear();
        _kinds.Clear();
        _paints.Clear();
        _regions.Clear();
        _words.Clear();
        _acts.Clear();
        _marks.Clear();
        _runs.Clear();
        _sides.Clear();
    }
}
