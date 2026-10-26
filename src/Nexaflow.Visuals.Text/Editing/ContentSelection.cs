using System.Collections.Generic;
using System.Linq;
using Nexaflow.Markdown.Ast;
using Nexaflow.Markdown.Layout;
using Nexaflow.Markdown.Layout.Prose;
using Nexaflow.Visuals.Text.Painting;

namespace Nexaflow.Visuals.Text.Editing;

/// <summary>
/// What a drag from one piece of content to another selected: a set of whole pieces, and the source ranges
/// they stand for. A set with possibly several ranges, since a selection is not always a run of text — a
/// column of a matrix is three cells nowhere near each other in the source. Whole pieces rather than
/// offsets, since a piece's range is what the parser built it from, so what you copy/replace/drag away is
/// well formed by construction rather than by counting braces afterwards.
/// </summary>
public sealed class ContentSelection
{
    private ContentSelection(IReadOnlyList<LayoutPiece> pieces, IReadOnlyList<(int Start, int Length)>? ranges = null)
    {
        Pieces = pieces;
        Ranges = ranges ?? LayoutQuery.Ranges(pieces);
    }

    /// <summary>The selected pieces, outermost, in reading order.</summary>
    public IReadOnlyList<LayoutPiece> Pieces { get; }

    /// <summary>The stretches of source they stand for, in order and never overlapping.</summary>
    public IReadOnlyList<(int Start, int Length)> Ranges { get; }

    public bool IsEmpty => Pieces.Count == 0;

    /// <summary>Nothing selected.</summary>
    public static ContentSelection None { get; } = new([]);

    /// <summary>
    /// What was selected by dragging from <paramref name="anchor"/> to <paramref name="focus"/>. Along one run
    /// the answer is the things between the two; between two runs it is the block they span (down a column
    /// gives the column, across gives the row, corner to corner gives everything between, as if the pieces
    /// were cells of a sheet); failing both, it's the plain stretch of source grown out to whole constructs.
    /// </summary>
    public static ContentSelection Between(LayoutPiece root, LayoutPiece anchor, LayoutPiece focus)
    {
        if (!anchor.Exists || !focus.Exists) return None;

        if (Along(root, anchor, focus) is { } run) return run;
        if (Stacked(root, anchor, focus) is { } stack) return stack;

        var (one, other) = (anchor.Sits(), focus.Sits());
        var from = System.Math.Min(one.Start, other.Start);
        var to = System.Math.Max(one.End, other.End);

        // What a leaf means is named, and holds it: so the leaves meaning anything between the two are inside the named pieces between
        // them, and only those are read.
        var named = Named(root, from, to, at => at.Start >= from && at.End <= to);

        var touched = LayoutQuery.LeavesUnder(named)
                    .Select(piece => piece.Selectable())
                    .Where(named.Contains)
                    .Distinct()
                    .ToList();
        return touched.Count == 0 ? None : new ContentSelection(LayoutQuery.Promote(touched));
    }

    /// <summary>
    /// What a drag from <paramref name="anchor"/> to <paramref name="focus"/> picks out where each is in a run the builder declared
    /// — a cell of a table or of a matrix — as <see cref="Between"/> answers it there: the things between them along one run, or the
    /// block they span between two. Null where either is on no declared run, which is where <see cref="Between"/> would step through
    /// what is merely drawn beside it, and words beside words are a stretch of text rather than a row of things.
    /// </summary>
    public static ContentSelection? Gridded(LayoutPiece root, LayoutPiece anchor, LayoutPiece focus) =>
        OnDeclaredRun(anchor) && OnDeclaredRun(focus) ? Along(root, anchor, focus) ?? Stacked(root, anchor, focus) : null;

    /// <summary>Whether <paramref name="piece"/>, or something holding it, is on a run the builder declared.</summary>
    private static bool OnDeclaredRun(LayoutPiece piece)
    {
        for (var at = piece; at.Exists; at = at.Parent)
            if (at.RunOn(vertical: false) >= 0 || at.RunOn(vertical: true) >= 0) return true;

        return false;
    }

    /// <summary>One whole piece, as a selection.</summary>
    public static ContentSelection Of(LayoutPiece piece) => piece.Exists ? new([piece]) : None;

    /// <summary>
    /// What <paramref name="ranges"/> of the source pick out: the stretches themselves, and the whole pieces that lie inside them
    /// — none, where they are characters inside one run of words.
    /// </summary>
    public static ContentSelection Over(LayoutPiece root, IReadOnlyList<(int Start, int Length)> ranges)
    {
        if (ranges.All(range => range.Length <= 0)) return None;

        // What a piece means is itself where the source named it, and otherwise something holding it — met before it, holding the
        // same source — so what is picked out is every named piece inside a range, in drawing order. Only what sits in source a range
        // touches is looked in.
        var inside = LayoutQuery.Sitting(root, span => ranges.Any(range => span.First <= range.Start + range.Length && span.Last >= range.Start))
            .Where(piece => piece.Part is { Length: > 0 } && piece.Sits() is var at
                            && ranges.Any(range => at.Start >= range.Start && at.End <= range.Start + range.Length))
            .ToList();

        return new ContentSelection(inside.Count == 0 ? [] : LayoutQuery.Promote(inside), ranges);
    }

    /// <summary>
    /// The piece a selection steps from: the first thing on a run of its own, and failing that the first
    /// thing the source named. Two separate climbs, since a run is not always declared on the thing that
    /// names source — a matrix cell carries the run while the letters inside it carry the source, so climbing
    /// to the run first is what lets one drag mean "the next cell" and a shorter one mean "the next term".
    /// Falls back to <see cref="LayoutQuery.Selectable"/> where nothing declares a run at all.
    /// </summary>
    private static LayoutPiece Stepping(LayoutPiece piece)
    {
        for (var at = piece; at.Exists; at = at.Parent)
            if (at.RunOn(vertical: false) >= 0 || at.RunOn(vertical: true) >= 0) return at;

        return piece.Selectable();
    }

    /// <summary>
    /// What a drag from one piece to another means when the builder put them on a run — the things between
    /// the two, taken a step at a time. Walks rather than indexes, which is why this works outside a grid:
    /// nothing has to hold what the run <em>is</em>, since each step only ever answers "what is next to me".
    /// Both axes and both directions are tried, and the first that reaches the other end wins; nothing
    /// reaching it means the two share no run, the caller's cue to read the drag as an ordinary stretch.
    /// </summary>
    private static ContentSelection? Along(LayoutPiece root, LayoutPiece anchor, LayoutPiece focus)
    {
        var from = Stepping(anchor);
        var to = Stepping(focus);
        if (!from.Exists || !to.Exists || from == to) return null;

        foreach (var vertical in new[] { false, true })
            foreach (var forward in new[] { true, false })
            {
                var run = new List<LayoutPiece> { from };

                for (var at = from.Step(vertical, forward); at.Exists; at = at.Step(vertical, forward))
                {
                    run.Add(at);
                    if (at == to) return Gathered(root, run);
                    if (!Within(at, to)) continue;

                    // The walk has reached into the group the drag ended on — a beam, pointed at by its own bar
                    // over the notes it joins. That means the group, so it carries on through what the group
                    // holds: half of one is not what anybody pointed at.
                    for (var next = at.Step(vertical, forward); next.Exists && Within(next, to); next = next.Step(vertical, forward))
                        run.Add(next);

                    return Gathered(root, run);
                }
            }

        return null;
    }

    /// <summary>Whether <paramref name="piece"/> is inside <paramref name="group"/>.</summary>
    private static bool Within(LayoutPiece piece, LayoutPiece group) => piece.Ancestors().Contains(group);

    /// <summary>A run of chosen pieces as a selection: their ink, and the source they cover.</summary>
    private static ContentSelection? Gathered(LayoutPiece root, IReadOnlyList<LayoutPiece> run)
    {
        var ink = run.SelectMany(p => p.Leaves()).Select(p => p.Selectable()).Where(p => p.Exists).Distinct().ToList();
        if (ink.Count == 0) return null;

        var chosen = LayoutQuery.Promote(ink);
        return new ContentSelection([.. chosen], Joined(root, chosen));
    }

    /// <summary>
    /// What a set of chosen things covers in the source: their own stretches, with a gap closed only where
    /// nothing else on the page was written in it. One span from first to last was the old answer, right
    /// only half the time: two notes may have a chord symbol or a whole line of words between them, and
    /// swallowing those took things the reader never pointed at. So the gap decides, not the axis — it
    /// closes only when nothing was drawn from it.
    /// </summary>
    private static IReadOnlyList<(int Start, int Length)> Joined(LayoutPiece root, IReadOnlyList<LayoutPiece> chosen)
    {
        var ranges = LayoutQuery.Ranges(chosen);
        if (ranges.Count < 2) return ranges;

        var inside = new HashSet<LayoutPiece>(chosen.SelectMany(p => p.SelfAndDescendants()));

        // Nor what holds them. A bar, or the group a slur is drawn in, names every character between its notes, and
        // the arc nobody typed is named by it — but it is around what was chosen, not written between.
        var around = new HashSet<LayoutPiece>(chosen.SelectMany(p => p.Ancestors()));

        // Only a gap between two of the ranges is asked about, so only what is written somewhere between the first and the last.
        var (opening, closing) = (ranges[0].Start, ranges[^1].Start + ranges[^1].Length);
        var between = Named(root, opening, closing, at => at.Start < closing && at.End > opening);

        var others = LayoutQuery.LeavesUnder(between)
            .Select(p => p.Selectable())
            .Where(p => between.Contains(p) && !inside.Contains(p) && !around.Contains(p))
            .Select(p => p.Sits())
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
    /// Every piece the source named — with characters of its own — that sits in source touching <c>[from, to]</c> and that
    /// <paramref name="keeps"/> keeps: what a leaf between the two can mean.
    /// </summary>
    private static HashSet<LayoutPiece> Named(LayoutPiece root, int from, int to, Func<SourcePlace, bool> keeps) =>
        [.. LayoutQuery.SittingIn(root, from, to).Where(piece => piece.Part is { Length: > 0 } && keeps(piece.Sits()))];

    /// <summary>
    /// What a drag between two different runs means: the block of things between them, one column at a time.
    /// Walked on both axes rather than indexed on either, which is what makes it work on a score: columns come
    /// from stepping along the anchor's own run until it lands in the same stack as the focus, and rows come
    /// from each stack separately by looking for the drag's two runs in it. Nothing needs a global row/column
    /// numbering or equal-length runs — just as well, since a note layer has a member every moment of a tune
    /// and a verse only where something is sung, and indexing by row position broke on exactly that mismatch.
    /// </summary>
    private static ContentSelection? Stacked(LayoutPiece root, LayoutPiece anchor, LayoutPiece focus)
    {
        var from = Stepping(anchor);
        var to = Stepping(focus);
        if (!from.Exists || !to.Exists || from == to) return null;

        var mineAcross = from.RunOn(vertical: false);
        var theirsAcross = to.RunOn(vertical: false);
        var theirsDown = to.RunOn(vertical: true);

        if (mineAcross < 0 || theirsAcross < 0 || theirsDown < 0) return null;
        if (mineAcross == theirsAcross) return null;   // one run: Along has already answered

        if (Columns(from, theirsDown) is not { } columns) return null;
        if (Downward(from, to) is not { } downward) return null;

        var block = new List<LayoutPiece>();
        var rows = new Dictionary<(int Run, int Alone), List<LayoutPiece>>();

        foreach (var column in columns)
        {
            // A piece with nothing named over it and nothing sung under it is in no stack at all, and it is
            // still a column of the block — a column of one. Reading its stack as itself is the whole of that
            // case, and it is why nothing here asks whether a stack exists.
            var stack = column.Sharing(vertical: true);

            var mine = Holding(stack, mineAcross);
            if (mine < 0) continue;

            // A stack with nothing in the run the drag reaches for is not a stack to skip: the piece is still
            // between the two, it simply has no word under it. It gives everything from where the anchor's run
            // sits to whichever end the reach was headed for.
            var theirs = Holding(stack, theirsAcross);
            if (theirs < 0) theirs = downward ? stack.Count - 1 : 0;

            var (top, bottom) = mine <= theirs ? (mine, theirs) : (theirs, mine);

            for (var at = top; at <= bottom; at++)
            {
                var ink = stack[at].Leaves().Select(p => p.Selectable()).Where(p => p.Exists).Distinct().ToList();
                if (ink.Count == 0) continue;

                block.AddRange(ink);

                // Keyed by the run it reads along, and a piece on none is a row of its own.
                var along = stack[at].RunOn(vertical: false);
                var key = along >= 0 ? (Run: along, Alone: -1) : (Run: -1, Alone: stack[at].At);

                if (!rows.TryGetValue(key, out var gathered)) rows[key] = gathered = [];
                gathered.AddRange(ink);
            }
        }

        if (block.Count == 0) return null;

        // Each row joined along itself, across whatever lies between two of its pieces and draws nothing — the
        // grid's own punctuation: a selected row reading as three selected digits with the `&` between them
        // conspicuously unselected is not what anybody dragged. Not across what is drawn: in a tune a chord symbol
        // is written between two notes of the staff's row, and a row run from its first note to its last took
        // every chord after the first.
        //
        // A row at a time, never all together: what separates two ROWS is deliberately not swallowed — selecting
        // every cell of a matrix and deleting it should leave a matrix with empty cells, not a matrix with its
        // rows run together.
        var ranges = rows.Values.SelectMany(ink => Joined(root, ink));

        return new ContentSelection([.. LayoutQuery.Promote(block)], LayoutQuery.Merge(ranges));
    }

    /// <summary>
    /// The things the block spans across: stepped along the anchor's own run, in whichever direction
    /// reaches the stack the focus is in.
    /// </summary>
    private static List<LayoutPiece>? Columns(LayoutPiece from, int stack)
    {
        if (from.RunOn(vertical: true) == stack) return [from];

        foreach (var forward in new[] { true, false })
        {
            var run = new List<LayoutPiece> { from };

            for (var at = from.Step(vertical: false, forward); at.Exists;
                 at = at.Step(vertical: false, forward))
            {
                run.Add(at);
                if (at.RunOn(vertical: true) == stack) return run;
            }
        }

        return null;
    }

    /// <summary>Whether the focus's run sits below the anchor's, asked of a stack that holds both. Null when neither end's stack holds both, meaning the two are not stacked together at all.</summary>
    private static bool? Downward(LayoutPiece from, LayoutPiece to)
    {
        foreach (var end in new[] { from, to })
        {
            if (end.RunOn(vertical: true) < 0) continue;

            var stack = end.Sharing(vertical: true);
            var mine = Holding(stack, from.RunOn(vertical: false));
            var theirs = Holding(stack, to.RunOn(vertical: false));
            if (mine >= 0 && theirs >= 0) return theirs > mine;
        }

        return null;
    }

    /// <summary>Where in a stack the member reading along a given run sits, or -1 when it has none.</summary>
    private static int Holding(IReadOnlyList<LayoutPiece> stack, int run)
    {
        for (var i = 0; i < stack.Count; i++)
            if (stack[i].RunOn(vertical: false) == run) return i;
        return -1;
    }
}
