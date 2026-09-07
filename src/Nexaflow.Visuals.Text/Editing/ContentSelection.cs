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
        if (Block(anchor, focus) is { } block) return block;

        var (one, other) = (anchor.Sits(), focus.Sits());
        var from = System.Math.Min(one.Start, other.Start);
        var to = System.Math.Max(one.End, other.End);

        var touched = root.Ink().Where(n => n.Sits() is var at && at.Start >= from && at.End <= to).ToList();
        return touched.Count == 0 ? None : new ContentSelection(LayoutQuery.Promote(touched));
    }

    /// <summary>One whole node, as a selection.</summary>
    public static ContentSelection Of(ILayoutNode? node) => node is null ? None : new([node]);

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
        if (anchor.Selectable() is not { } from || focus.Selectable() is not { } to) return null;
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
        if (anchor.Selectable() is not { } from || focus.Selectable() is not { } to) return null;
        if (ReferenceEquals(from, to)) return null;
        if (from.Across is not { } run || to.Across is null || to.Down is null) return null;
        if (ReferenceEquals(run, to.Across)) return null;   // one run: Along has already answered

        if (Columns(from, to.Down) is not { } columns) return null;
        if (Downward(from, to) is not { } downward) return null;

        var block = new List<ILayoutNode>();

        foreach (var column in columns)
        {
            // A note with nothing named over it and nothing sung under it is in no stack at all, and it
            // is still a column of the block - it is simply a column of one. Reading its stack as itself
            // is the whole of that case, and it is why nothing here asks whether a stack exists.
            var stack = column.Down?.Children ?? [column];

            var mine = Holding(stack, from.Across);
            if (mine < 0) continue;

            // A stack with nothing in the run the drag reaches for is not a stack to skip: the note is
            // still between the two, it simply has no word under it. It gives everything from where the
            // anchor's run sits to whichever end the reach was headed for.
            var theirs = Holding(stack, to.Across);
            if (theirs < 0) theirs = downward ? stack.Count - 1 : 0;

            var (top, bottom) = mine <= theirs ? (mine, theirs) : (theirs, mine);
            for (var row = top; row <= bottom; row++) block.AddRange(stack[row].Ink());
        }

        if (block.Count == 0) return null;

        var chosen = LayoutQuery.Promote(block);
        return new ContentSelection([.. chosen], Joined(root, chosen));
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

    private static ContentSelection? Block(ILayoutNode anchor, ILayoutNode focus)
    {
        if (Placed(anchor) is not { } from || Placed(focus) is not { } to) return null;

        // Two lanes that share nothing are two grids, and there is no block between them.
        if (!ReferenceEquals(from.Column, to.Column) && !Meets(from.Column, to.Column)) return null;

        var (top, bottom) = from.Row <= to.Row ? (from.Row, to.Row) : (to.Row, from.Row);
        var (left, right) = from.Along <= to.Along ? (from.Along, to.Along) : (to.Along, from.Along);

        // One cell is a drag inside it, not a block of cells: what was dragged over within a cell is a
        // run of terms like any other. Answering "the whole cell" is what made every term in a matrix
        // impossible to pick out on its own.
        if (top == bottom && left == right) return null;

        var block = new List<ILayoutNode>();
        var ranges = new List<(int Start, int Length)>();
        var column = from.Column.Children;

        for (var row = top; row <= bottom && row < column.Count; row++)
        {
            var along = column[row].Across?.Children ?? [column[row]];

            // A cell is a container the builder wrapped the content in, and may name nothing itself;
            // what stands for it in the source is whatever it holds.
            var ink = Enumerable.Range(left, right - left + 1)
                .Where(at => at < along.Count)
                .SelectMany(at => along[at].Ink())
                .ToList();
            if (ink.Count == 0) continue;

            block.AddRange(ink);

            // One range per row, from its first selected cell to its last — separators included. Cells
            // chosen as a block are adjacent by construction, so everything between two of them is the
            // grid's own punctuation and belongs to the selection. Taking each cell separately instead
            // would leave a selected row reading as three selected digits with the `&` between them
            // conspicuously unselected.
            var start = ink.Min(n => n.Sits().Start);
            ranges.Add((start, ink.Max(n => n.Sits().End) - start));
        }

        return block.Count == 0
            ? null
            : new ContentSelection([.. LayoutQuery.Promote(block)], LayoutQuery.Merge(ranges));
    }

    /// <summary>
    /// Where a node sits in its lanes — the cell it is, its place along its run and down its stack — or
    /// null when it is in no lanes at all.
    /// <para>
    /// The node pointed at is often inside a cell rather than being one, so this walks out to the first
    /// ancestor that is in a lane. That is the same reach the grid version had, said as a walk instead of
    /// as a search through every cell.
    /// </para>
    /// </summary>
    private static (ILayoutNode Cell, ILayoutNode Column, int Row, int Along)? Placed(ILayoutNode node) =>
        Inferred(node);

    /// <summary>
    /// The same answer for a builder that has not declared its lanes yet, worked out from the geometry.
    ///
    /// <para>
    /// <strong>Transitional, and the only thing still asking a rectangle what it belongs to.</strong> A
    /// grid is recognised here rather than declared: rows come from clustering children by vertical
    /// overlap, and a grid is a stack of rows that happen to hold the same number of cells. That is why
    /// it only ever worked for a matrix — a score's rows overlap nothing, and its lanes hold different
    /// numbers of things.
    /// </para>
    /// <para>
    /// It goes when the last builder declares <see cref="ILayoutNode.Across"/> and
    /// <see cref="ILayoutNode.Down"/>, and <c>LayoutQuery.Grid</c> goes with it. Nothing new should be
    /// written against it.
    /// </para>
    /// </summary>
    private static (ILayoutNode Cell, ILayoutNode Column, int Row, int Along)? Inferred(ILayoutNode node)
    {
        var grid = node.Ancestors().FirstOrDefault(a => a.Grid().Count > 0);
        if (grid is null) return null;

        var cells = grid.Grid();

        for (var row = 0; row < cells.Count; row++)
            for (var along = 0; along < cells[row].Count; along++)
            {
                var cell = cells[row][along];
                if (cell != node && !node.Ancestors().Contains(cell)) continue;

                // A stack the block walk can index down, built to match what the rows say.
                var column = LayoutNode.Ordering(
                    [.. cells.Where(r => along < r.Count).Select(r => r[along])], across: false, "column");

                foreach (var r in cells) LayoutNode.Ordering([.. r], across: true, "row");

                return (cell, column, row, along);
            }

        return null;
    }

    /// <summary>Where a node sits in one of its lanes.</summary>
    private static int At(ILayoutNode lane, ILayoutNode member)
    {
        for (var i = 0; i < lane.Children.Count; i++)
            if (ReferenceEquals(lane.Children[i], member)) return i;
        return -1;
    }

    /// <summary>Whether two stacks are stacks of the same thing — they hold a row in common.</summary>
    private static bool Meets(ILayoutNode one, ILayoutNode other) =>
        one.Children.Any(a => other.Children.Any(b => ReferenceEquals(a.Across, b.Across)));
}