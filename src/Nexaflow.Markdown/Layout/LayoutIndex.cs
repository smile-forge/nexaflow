namespace Nexaflow.Markdown.Layout;

/// <summary>
/// What every piece of a laid-out tree reaches — on the page and in the source — kept so that a question about one point or
/// one stretch of source descends only into what could answer it: the few blocks round a press, not every glyph of a long
/// document.
///
/// <para>
/// <strong>What a subtree reaches, not what its box says.</strong> A container's box is a typesetting measurement — the height
/// and depth it reserves on its line — and a subscript below its operator can sit outside the very piece that holds it, which
/// is why no question is narrowed by one (<see cref="LayoutQuery"/>). What is kept here is every box and every shape inside a
/// subtree, gathered: a point or a rectangle clear of that is clear of everything the subtree drew, so passing the subtree
/// over leaves out nothing a walk of all of it would have found, and everything found is found in the order that walk finds it.
/// </para>
/// <para>
/// <strong>And the same for the source:</strong> the stretch every piece of a subtree sits in (<see cref="LayoutPiece.Sits"/>), so a
/// question about a range of it passes over every block the range does not touch.
/// </para>
/// <para>
/// <strong>And for a run of siblings.</strong> A document holds its blocks side by side — thousands of them under one piece —
/// so asking each in turn is still a walk of the document. Every <see cref="Run"/> of them is gathered the same way, and a run
/// that cannot answer is passed over whole: a question asks a few hundred runs and the blocks of the one or two that can.
/// </para>
/// <para>
/// Made on the first question and kept with the tree, which does not change once sealed — save for being settled onto the
/// page, which lets this go (<see cref="LayoutTree.Settle"/>).
/// </para>
/// </summary>
internal sealed class LayoutIndex
{
    /// <summary>How many siblings are gathered into one run, and how many a piece has to hold before its are.</summary>
    private const int Run = 32;

    private readonly LayoutTree _tree;

    /// <summary>Where each piece sits on the page — what <see cref="LayoutTree.Within"/> says of it.</summary>
    private readonly LayoutRect[] _where;

    /// <summary>Everything each piece and what it holds stands in on the page: every box and every shape, gathered.</summary>
    private readonly LayoutRect[] _reach;

    /// <summary>The stretch of source each piece and what it holds sits in — from the first character to the last.</summary>
    private readonly int[] _first;
    private readonly int[] _last;

    /// <summary>
    /// For a piece that begins a run of siblings, which run — and -1 for every other piece. A run is what its pieces and all
    /// they hold reach and sit in, where the first of them begins and where the last of them ends.
    /// </summary>
    private readonly int[] _runOf;
    private readonly List<(LayoutRect Reach, int First, int Last, int Begin, int End)> _runs = [];

    internal LayoutIndex(LayoutTree tree)
    {
        _tree = tree;

        var count = tree.Count;
        _where = new LayoutRect[count];
        _reach = new LayoutRect[count];
        _first = new int[count];
        _last = new int[count];
        _runOf = new int[count];
        Array.Fill(_runOf, -1);

        // Where something drawn from nothing anybody wrote sits: at the start of whatever it was drawn inside (LayoutPiece.Sits).
        var named = new int[count];

        // A tree may hold several pieces at its top level — a diagram built as its layers — each the root of its own run.
        for (var top = 0; top < count; top += tree.Piece(top).Extent)
            foreach (var (at, where) in tree.Within(top))
            {
                _where[at] = where;

                var reach = where;
                if (tree.RegionOf(at) is { } region) reach = LayoutRect.Union(reach, tree.FrameOf(at).Out(region.Bounds));
                _reach[at] = reach;

                var up = tree.Piece(at).Parent;

                if (tree.PartOf(at) is { } part)
                {
                    named[at] = part.Start;
                    (_first[at], _last[at]) = (part.Start, part.Start + part.Length);
                }
                else
                {
                    named[at] = up < 0 ? 0 : named[up];
                    _first[at] = _last[at] = named[at];
                }
            }

        // Pre-order puts every piece after what holds it, so walking back gathers each subtree into its root.
        for (var at = count - 1; at > 0; at--)
        {
            var up = tree.Piece(at).Parent;
            if (up < 0) continue;

            _reach[up] = LayoutRect.Union(_reach[up], _reach[at]);
            _first[up] = Math.Min(_first[up], _first[at]);
            _last[up] = Math.Max(_last[up], _last[at]);
        }

        for (var at = 0; at < count; at++)
            if (tree.Piece(at).Extent > Run) Gather(at);
    }

    /// <summary>Gathers the pieces <paramref name="holder"/> holds into runs, where it holds more than one run of them.</summary>
    private void Gather(int holder)
    {
        var end = holder + _tree.Piece(holder).Extent;
        var held = 0;

        for (var child = holder + 1; child < end; child += _tree.Piece(child).Extent) held++;
        if (held <= Run) return;

        for (var child = holder + 1; child < end;)
        {
            var run = (Reach: LayoutRect.Empty, First: int.MaxValue, Last: int.MinValue, Begin: child, End: child);

            for (var taken = 0; taken < Run && child < end; taken++, child += _tree.Piece(child).Extent)
            {
                run.Reach = LayoutRect.Union(run.Reach, _reach[child]);
                run.First = Math.Min(run.First, _first[child]);
                run.Last = Math.Max(run.Last, _last[child]);
            }

            run.End = child;
            _runOf[run.Begin] = _runs.Count;
            _runs.Add(run);
        }
    }

    /// <summary>Where the piece at <paramref name="at"/> sits on the page.</summary>
    public LayoutRect Where(int at) => _where[at];

    /// <summary>Everything the piece at <paramref name="at"/> and what it holds stand in on the page — empty where none of it takes room.</summary>
    public LayoutRect Reach(int at) => _reach[at];

    /// <summary>The stretch of source the piece at <paramref name="at"/> and what it holds sit in, from the first character to the last.</summary>
    public (int First, int Last) Span(int at) => (_first[at], _last[at]);

    /// <summary>
    /// Every piece of the subtree at <paramref name="from"/> in pre-order — passing over each subtree, and each run of siblings,
    /// that <paramref name="enters"/> turns down, asked of what it reaches on the page and the stretch of source it sits in.
    /// </summary>
    public IEnumerable<int> Descend(int from, Func<LayoutRect, (int First, int Last), bool> enters)
    {
        var end = from + _tree.Piece(from).Extent;

        for (var at = from; at < end;)
        {
            if (Passed(at, enters) is { } past)
            {
                at = past;
                continue;
            }

            if (!enters(_reach[at], (_first[at], _last[at])))
            {
                at += _tree.Piece(at).Extent;
                continue;
            }

            yield return at;
            at++;
        }
    }

    /// <summary>
    /// The pieces <paramref name="holder"/> holds, in order, that <paramref name="enters"/> takes — what
    /// <see cref="LayoutPiece.Children"/> gives, passing over a run of them at a time where none of the run can be one.
    /// </summary>
    public IEnumerable<int> Children(int holder, Func<LayoutRect, (int First, int Last), bool> enters)
    {
        var end = holder + _tree.Piece(holder).Extent;

        for (var at = holder + 1; at < end;)
        {
            if (Passed(at, enters) is { } past)
            {
                at = past;
                continue;
            }

            if (enters(_reach[at], (_first[at], _last[at]))) yield return at;
            at += _tree.Piece(at).Extent;
        }
    }

    /// <summary>
    /// What a nearest-first search opens <paramref name="item"/> into, each with what it reaches and the source it sits in: a
    /// piece's runs of siblings where it holds enough to have them, and otherwise the pieces it holds; and a run's pieces. A piece
    /// is its index and a run the complement of its number, so one queue holds both — and opening the piece holding a document's
    /// thousands of blocks puts a few hundred runs on it, not every block.
    /// </summary>
    public IEnumerable<(int Item, LayoutRect Reach, (int First, int Last) Span)> Opened(int item)
    {
        if (item < 0)
        {
            var run = _runs[~item];
            for (var at = run.Begin; at < run.End; at += _tree.Piece(at).Extent)
                yield return (at, _reach[at], (_first[at], _last[at]));

            yield break;
        }

        var end = item + _tree.Piece(item).Extent;

        for (var at = item + 1; at < end;)
        {
            if (_runOf[at] is >= 0 and var begun)
            {
                var those = _runs[begun];
                yield return (~begun, those.Reach, (those.First, those.Last));
                at = those.End;
                continue;
            }

            yield return (at, _reach[at], (_first[at], _last[at]));
            at += _tree.Piece(at).Extent;
        }
    }

    /// <summary>
    /// Where a walk goes on from when the run of siblings that <paramref name="at"/> begins cannot answer — past all of them —
    /// or null where it does not begin one, or the run may.
    /// </summary>
    private int? Passed(int at, Func<LayoutRect, (int First, int Last), bool> enters) =>
        _runOf[at] is >= 0 and var run && _runs[run] is var those && !enters(those.Reach, (those.First, those.Last))
            ? those.End
            : null;
}
