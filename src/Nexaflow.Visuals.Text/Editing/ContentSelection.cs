using System.Collections.Generic;
using System.Linq;
using Nexaflow.Markdown.Ast;

namespace Nexaflow.Visuals.Text.Editing;

/// <summary>
/// What a drag from one piece of content to another selected: a set of whole nodes, and the source ranges
/// they stand for.
/// <para>
/// A set rather than a range, and more than one range, because a selection is not always a run of text. A
/// column of a matrix is three cells that are nowhere near each other in the source, and it is a perfectly
/// ordinary thing to want. Whole nodes rather than offsets, because a node's range is what the parser
/// built it from — so what you copy, replace or drag away is well formed by construction rather than by
/// counting braces afterwards.
/// </para>
/// </summary>
public sealed class ContentSelection
{
    private ContentSelection(IReadOnlyList<ILayoutNode> nodes, IReadOnlyList<(int Start, int Length)>? ranges = null)
    {
        Nodes = nodes;
        Ranges = ranges ?? LayoutQuery.Ranges(nodes);
    }

    /// <summary>The selected pieces, outermost, in reading order.</summary>
    public IReadOnlyList<ILayoutNode> Nodes { get; }

    /// <summary>The stretches of source they stand for, in order and never overlapping.</summary>
    public IReadOnlyList<(int Start, int Length)> Ranges { get; }

    public bool IsEmpty => Nodes.Count == 0;

    /// <summary>Nothing selected.</summary>
    public static ContentSelection None { get; } = new([]);

    /// <summary>
    /// What was selected by dragging from <paramref name="anchor"/> to <paramref name="focus"/>.
    /// <para>
    /// Inside a grid the answer is a block of cells — down a column gives the column, across gives the
    /// row, corner to corner gives everything between, exactly as it would if the cells were a sheet.
    /// Anywhere else it is the run from one to the other, grown out to whole constructs.
    /// </para>
    /// </summary>
    public static ContentSelection Between(ILayoutNode root, ILayoutNode? anchor, ILayoutNode? focus)
    {
        if (anchor is null || focus is null) return None;

        // Stepped along an axis the builder ordered, if the two ends are on one; then the block between
        // them if they are on different ones; then the grid a matrix still has recognised for it; and
        // failing all three the plain run of source, which is what most things are.
        if (Along(root, anchor, focus) is { } run) return run;
        if (Stacked(root, anchor, focus) is { } stack) return stack;
        

        var (one, other) = (anchor.Sits(), focus.Sits());
        var from = System.Math.Min(one.Start, other.Start);
        var to = System.Math.Max(one.End, other.End);

        var touched = root.Ink().Where(n => n.Sits() is var at && at.Start >= from && at.End <= to).ToList();
        return touched.Count == 0 ? None : new ContentSelection(LayoutQuery.Promote(touched));
    }

    /// <summary>One whole node, as a selection.</summary>
    public static ContentSelection Of(ILayoutNode? node) => node is null ? None : new([node]);

    /// <summary>
    /// The piece a selection steps from: the first thing on a run of its own, and failing that the first
    /// thing the source named.
    ///
    /// <para>
    /// Two climbs, because a run is not always declared on the thing that names source. A note declares its
    /// own — it is a note that belongs with the other notes — while a matrix cell is a box the typesetter
    /// made around whatever was written in it, so the cell carries the run and the letters inside it carry
    /// the source. Climbing to the run first is what lets one drag mean "the next cell" and a shorter one
    /// inside a cell mean "the next term".
    /// </para>
    /// <para>
    /// Where nothing declares a run at all this is exactly <see cref="LayoutQuery.Selectable"/>, which is
    /// what everything selected by before any of this existed.
    /// </para>
    /// </summary>
    private static ILayoutNode? Stepping(ILayoutNode node) =>
        new[] { node }.Concat(node.Ancestors())
            .FirstOrDefault(at => at.Across is not null || at.Down is not null)
        ?? node.Selectable();

    /// <summary>
    /// The block of cells a drag covers, or null when it is not a drag across cells at all — which is the
    /// caller's cue to read it as an ordinary run of source.
    ///
    /// <para>
    /// Read off the lanes the builder declared rather than off a grid recognised in the geometry. A
    /// node's place is its index along <see cref="ILayoutNode.Across"/> and its index down
    /// <see cref="ILayoutNode.Down"/>, so the block between two of them is two spans and a walk — and a
    /// thing behaves like a grid because it was built as one, not because its rows happened to come out
    /// the same length.
    /// </para>
    /// <para>
    /// Which is also how this stopped being about matrices. Anything laid out in lanes selects this way:
    /// a verse of lyrics reads along its own line, a note and the syllables sung on it stack, and
    /// dragging between them means the block — the same behaviour, from the same code, because the score
    /// says what lines up instead of leaving it to be guessed.
    /// </para>
    /// </summary>
    /// <summary>
    /// What a drag from one node to another means when the builder ordered them along an axis — the run
    /// of things between the two, taken a step at a time.
    ///
    /// <para>
    /// It walks rather than indexes, which is the whole of why this works outside a grid. Nothing has to
    /// hold what the run <em>is</em>: a lyric carries on across every system of a tune, a maths block
    /// stops at its own edge, and a diagram stays inside its subtree, and all three come out of the same
    /// four lines because each only ever answers "what is next to me".
    /// </para>
    /// <para>
    /// Both axes and both directions are tried, and the first that reaches the other end wins. Nothing
    /// reaching it means the two are not on a common run at all, which is the caller's cue to read the
    /// drag as an ordinary stretch of source.
    /// </para>
    /// </summary>
    private static ContentSelection? Along(ILayoutNode root, ILayoutNode anchor, ILayoutNode focus)
    {
        if (Stepping(anchor) is not { } from || Stepping(focus) is not { } to) return null;
        if (ReferenceEquals(from, to)) return null;

        foreach (var vertical in new[] { false, true })
            foreach (var forward in new[] { true, false })
            {
                var run = new List<ILayoutNode> { from };

                for (var at = from.Step(vertical, forward); at is not null; at = at.Step(vertical, forward))
                {
                    run.Add(at);
                    if (ReferenceEquals(at, to)) return Gathered(root, run);
                }
            }

        return null;
    }

    /// <summary>A run of chosen nodes as a selection: their ink, and the source they cover.</summary>
    private static ContentSelection? Gathered(ILayoutNode root, IReadOnlyList<ILayoutNode> run)
    {
        var ink = run.SelectMany(n => n.Ink()).ToList();
        if (ink.Count == 0) return null;

        var chosen = LayoutQuery.Promote(ink);
        return new ContentSelection([.. chosen], Joined(root, chosen));
    }

    /// <summary>
    /// What a set of chosen things covers in the source: their own stretches, with a gap closed only
    /// where nothing else on the page was written in it.
    ///
    /// <para>
    /// One span from the first to the last was the old answer, and it is right exactly half the time.
    /// Two syllables of a verse are separated by a space that belongs to neither and to both, and a
    /// selection that leaves it out reads as three selected words with the gaps between them
    /// conspicuously unselected. Two notes may have a chord symbol, a bar line or an entire line of
    /// words between them, and a selection that swallows those has taken things the reader never
    /// pointed at - which is what lit up the rows above and below a drag along the notes.
    /// </para>
    /// <para>
    /// So the gap decides, not the axis: it closes when nothing was drawn from it. That is a question
    /// about the page, and the page is what is being dragged over.
    /// </para>
    /// </summary>
    private static IReadOnlyList<(int Start, int Length)> Joined(
        ILayoutNode root, IReadOnlyList<ILayoutNode> chosen)
    {
        var ranges = LayoutQuery.Ranges(chosen);
        if (ranges.Count < 2) return ranges;

        var inside = new HashSet<ILayoutNode>(chosen.SelectMany(n => n.SelfAndDescendants()));
        var others = root.Ink()
            .Where(n => !inside.Contains(n))
            .Select(n => n.Sits())
            .Where(at => at.Length > 0)
            .ToList();

        var joined = new List<(int Start, int Length)> { ranges[0] };

        foreach (var next in ranges.Skip(1))
        {
            var last = joined[^1];
            var (from, to) = (last.Start + last.Length, next.Start);

            if (others.Any(at => at.Start < to && at.End > from))
                joined.Add(next);
            else
                joined[^1] = (last.Start, next.Start + next.Length - last.Start);
        }

        return joined;
    }

    /// <summary>
    /// What a drag between two different runs means: the block of things between them, one column at a
    /// time.
    ///
    /// <para>
    /// Walked on both axes rather than indexed on either, which is what makes it work on a score. The
    /// columns come from stepping along the anchor's own run until the step lands in the same stack as
    /// the focus; the rows come from each of those stacks separately, by looking in it for the two runs
    /// the drag is between. Nothing needs a global numbering of rows or of columns, and nothing needs the
    /// runs to be the same length - which is just as well, because they never are: the note layer has a
    /// member at every moment of a tune and a verse only at the moments something is sung.
    /// </para>
    /// <para>
    /// Indexing was the previous answer and it could not survive that. A row was a position within one
    /// stack, so "row 1" meant the note where a chord was named above it and the syllable where none was,
    /// and a drag from a note down to a word came back as a row of notes.
    /// </para>
    /// </summary>
    private static ContentSelection? Stacked(ILayoutNode root, ILayoutNode anchor, ILayoutNode focus)
    {
        if (Stepping(anchor) is not { } from || Stepping(focus) is not { } to) return null;
        if (ReferenceEquals(from, to)) return null;
        if (from.Across is not { } run || to.Across is null || to.Down is null) return null;
        if (ReferenceEquals(run, to.Across)) return null;   // one run: Along has already answered

        if (Columns(from, to.Down) is not { } columns) return null;
        if (Downward(from, to) is not { } downward) return null;

        var block = new List<ILayoutNode>();
        var rows = new Dictionary<ILayoutNode, List<ILayoutNode>>();

        foreach (var column in columns)
        {
            // A piece with nothing named over it and nothing sung under it is in no stack at all, and it is
            // still a column of the block — a column of one. Reading its stack as itself is the whole of that
            // case, and it is why nothing here asks whether a stack exists.
            var stack = column.Down?.Children ?? [column];

            var mine = Holding(stack, from.Across);
            if (mine < 0) continue;

            // A stack with nothing in the run the drag reaches for is not a stack to skip: the piece is still
            // between the two, it simply has no word under it. It gives everything from where the anchor's run
            // sits to whichever end the reach was headed for.
            var theirs = Holding(stack, to.Across);
            if (theirs < 0) theirs = downward ? stack.Count - 1 : 0;

            var (top, bottom) = mine <= theirs ? (mine, theirs) : (theirs, mine);

            for (var at = top; at <= bottom; at++)
            {
                var ink = stack[at].Ink().ToList();
                if (ink.Count == 0) continue;

                block.AddRange(ink);

                var along = stack[at].Across ?? stack[at];
                        if (!rows.TryGetValue(along, out var gathered)) rows[along] = gathered = [];
                gathered.AddRange(ink);
            }
        }

        if (block.Count == 0) return null;

        // One range per row, from its first chosen piece to its last — the separators between them included,
        // because pieces chosen as a block are adjacent by construction and what lies between two of them is
        // the grid's own punctuation. A selected row reading as three selected digits with the `&` between
        // them conspicuously unselected is not what anybody dragged.
        //
        // What separates two ROWS is deliberately not swallowed, which is why this does not go through Joined:
        // selecting every cell of a matrix and deleting it should leave a matrix with empty cells, not a
        // matrix with its rows run together.
        var ranges = rows.Values.Select(ink =>
        {
            var start = ink.Min(n => n.Sits().Start);
            return (start, ink.Max(n => n.Sits().End) - start);
        });

        return new ContentSelection([.. LayoutQuery.Promote(block)], LayoutQuery.Merge(ranges));
    }

    /// <summary>
    /// The things the block spans across: stepped along the anchor's own run, in whichever direction
    /// reaches the stack the focus is in.
    /// </summary>
    private static List<ILayoutNode>? Columns(ILayoutNode from, ILayoutNode stack)
    {
        if (ReferenceEquals(from.Down, stack)) return [from];

        foreach (var forward in new[] { true, false })
        {
            var run = new List<ILayoutNode> { from };

            for (var at = from.Step(vertical: false, forward); at is not null;
                 at = at.Step(vertical: false, forward))
            {
                run.Add(at);
                if (ReferenceEquals(at.Down, stack)) return run;
            }
        }

        return null;
    }

    /// <summary>
    /// Whether the focus's run sits below the anchor's, asked of a stack that holds both. Null when
    /// neither end's stack holds both, which means the two are not stacked together at all.
    /// <para>
    /// Only needed for the stacks that hold one of the two, to say which end of them the block reaches
    /// to. The stacks that hold both answer for themselves.
    /// </para>
    /// </summary>
    private static bool? Downward(ILayoutNode from, ILayoutNode to)
    {
        foreach (var stack in new[] { from.Down, to.Down })
        {
            if (stack is null) continue;

            var mine = Holding(stack.Children, from.Across);
            var theirs = Holding(stack.Children, to.Across);
            if (mine >= 0 && theirs >= 0) return theirs > mine;
        }

        return null;
    }

    /// <summary>Where in a stack the member belonging to a given run sits, or -1 when it has none.</summary>
    private static int Holding(IReadOnlyList<ILayoutNode> stack, ILayoutNode? run)
    {
        for (var i = 0; i < stack.Count; i++)
            if (ReferenceEquals(stack[i].Across, run)) return i;
        return -1;
    }
}