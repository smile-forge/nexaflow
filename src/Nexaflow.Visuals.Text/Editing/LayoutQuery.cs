using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using Nexaflow.Markdown.Ast;

namespace Nexaflow.Visuals.Text.Editing;

/// <summary>
/// What a pointer, a caret and a selection mean, answered by descending a layout tree.
/// <para>
/// Every one of these was previously inferred from a flattened list of rectangles and source ranges —
/// "is this a container", "what encloses this", "are these cells a row", "is this selection well
/// formed" — and each inference was a fact the tree already had. Asking the tree is both simpler and
/// right; the guessing is what produced a caret that jumped rows and a selection that closed a brace it
/// never opened.
/// </para>
/// <para>
/// Pure arithmetic over a tree. <see cref="Rect"/> and <see cref="Point"/> come from WindowsBase and need
/// no STA thread, no fonts and no desktop, so all of this is exercised against hand-built trees.
/// </para>
/// </summary>
public static class LayoutQuery
{
    /// <summary>Rounding slack — two edges within this of each other are treated as touching.</summary>
    private const double Hair = 0.5;

    // ── Pointer ─────────────────────────────────────────────────────────────

    /// <summary>
    /// The ink under <paramref name="point"/>, found by descending. Where nothing is under it — the blank
    /// margin above a formula, the gap between two terms — the nearest ink wins, because a press has to
    /// mean something.
    /// </summary>
    public static ILayoutNode? NodeAt(this ILayoutNode root, Point point) =>
        Deepest(root, point) ?? Nearest(root, point);

    /// <summary>
    /// The drawn thing under the point. Only leaves are candidates — they are what actually put ink on the
    /// page — and blank space inside a container belongs to nobody, which is what stops a press in the gap
    /// between two terms coming back with the start of the whole line.
    /// <para>
    /// Containers are not used to narrow the search, and must not be. A container's bounds are a
    /// typesetting measurement — the height and depth it reserves on its line — not a bounding box of what
    /// it draws, so a subscript hanging below its operator or an accent riding above its letter sits
    /// outside the very node that holds it, and gating descent on the parent lost every one of those.
    /// </para>
    /// </summary>
    private static ILayoutNode? Deepest(ILayoutNode root, Point point)
    {
        ILayoutNode? best = null;
        var bestRank = (Named: -1, Depth: -1);

        foreach (var node in root.SelfAndDescendants())
        {
            if (node.Children.Count > 0) continue;                                // draws nothing itself
            if (node.Bounds.Width <= 0 || node.Bounds.Height <= 0) continue;      // spacing
            if (!Contains(node.Bounds, point)) continue;

            // Named by the source, or else part of the drawing of whatever encloses it — a fraction's bar,
            // a radical's sign, the letters a macro expands to — in which case the press means that. A
            // hole names a place of its own without covering any characters, and pointing at one means
            // it rather than the construct around it.
            var resolved = node.Stands() ? node : NamedAncestor(node);
            if (resolved is null) continue;

            // Where several overlap, one that holds a place beats one that does not, because it is the
            // more specific answer; between equals, the deeper. Neither is a matter of which is smaller
            // on the page — that only ever settled it by luck.
            var rank = (Named: node.Stands() ? 1 : 0, Depth: node.Ancestors().Count());
            if (rank.CompareTo(bestRank) <= 0) continue;

            bestRank = rank;
            best = resolved;
        }

        return best;
    }

    /// <summary>
    /// The first thing a selection could hold, climbing from here: this node if the source named it, and
    /// otherwise the nearest thing above it that it did.
    ///
    /// <para>
    /// Where selection starts, every time. What a reader points at is a glyph, a stem, a syllable — most
    /// of which name nothing on their own — and what they mean by pointing at it is the smallest written
    /// thing it is part of. Climbing is the only way to get from one to the other, and it is why nothing
    /// here ever needs to know what a node contains.
    /// </para>
    /// </summary>
    public static ILayoutNode? Selectable(this ILayoutNode node) =>
        node.Part is { Length: > 0 } ? node : NamedAncestor(node);

    /// <summary>
    /// One step from here along an axis — the next thing to select when a selection grows that way, or
    /// null when there is nothing that way.
    ///
    /// <para>
    /// The step is taken on the parent that orders this node (<see cref="ILayoutNode.Across"/> or
    /// <see cref="ILayoutNode.Down"/>), and what comes back is climbed to the first thing the source
    /// named — usually itself, and not always.
    /// </para>
    /// <para>
    /// <strong>A node that declares neither still steps</strong>, on the tree that draws it: sideways to
    /// the next thing its parent holds, and vertically to the nearest thing on the row above or below.
    /// Declaring an axis is how a builder says something the drawing does not — that a syllable belongs
    /// with the other syllables of its verse rather than with the note above it — and where there is
    /// nothing to correct, where a thing is drawn is a perfectly good account of what is beside it. So
    /// this is not a feature only declared content gets; it is the ordinary behaviour, which declaring
    /// an axis overrides.
    /// </para>
    /// <para>
    /// Deliberately one step and no more. A run is walked by taking them, so nothing has to hold what the
    /// run <em>is</em> — which is what lets a lyric carry on across systems, a maths block stop at its own
    /// edge, and a diagram stay inside its subtree, all from the same code and without any of them being
    /// asked to pretend it is a row.
    /// </para>
    /// </summary>
    public static ILayoutNode? Step(this ILayoutNode node, bool vertical, bool forward)
    {
        if ((vertical ? node.Down : node.Across) is { } ordering)
            return Beside(ordering.Children, node, forward);

        return vertical ? Stacked(node, forward) : Beside(node.Parent?.Children, node, forward);
    }

    /// <summary>The member one place along from this one, climbed to the first thing the source named.</summary>
    private static ILayoutNode? Beside(IReadOnlyList<ILayoutNode>? members, ILayoutNode node, bool forward)
    {
        if (members is null) return null;

        var at = -1;
        for (var i = 0; i < members.Count; i++)
            if (ReferenceEquals(members[i], node)) { at = i; break; }

        var to = at + (forward ? 1 : -1);
        return at < 0 || to < 0 || to >= members.Count ? null : members[to].Selectable();
    }

    /// <summary>
    /// A step up or down taken on the tree that draws things, for content that declared no stack of its
    /// own: the row above or below within whatever holds this, and the thing on it nearest to where this
    /// one starts.
    /// <para>
    /// Nearest by column rather than first on the row, because moving down a line is meant to keep your
    /// place across it — the same rule the caret follows through a fraction.
    /// </para>
    /// </summary>
    private static ILayoutNode? Stacked(ILayoutNode node, bool forward)
    {
        if (node.Parent is not { } parent) return null;

        var rows = parent.Rows();
        var mine = rows.FindIndex(row => row.Any(n => ReferenceEquals(n, node)));
        if (mine < 0) return null;

        var to = mine + (forward ? 1 : -1);
        if (to < 0 || to >= rows.Count) return null;

        return rows[to]
            .OrderBy(n => Math.Abs(n.Bounds.X - node.Bounds.X))
            .Select(n => n.Selectable())
            .FirstOrDefault(n => n is not null);
    }

    /// <summary>The nearest thing containing this node that the source actually named.</summary>
    private static ILayoutNode? NamedAncestor(ILayoutNode node) =>
        node.Ancestors().FirstOrDefault(a => a.Part is { Length: > 0 });

    /// <summary>
    /// What a node draws that no part of the source named: a fraction's bar, a radical's sign, a beam
    /// between two notes. The walk stops at anything with a name of its own, because that node's innards
    /// are its own business rather than this one's decoration.
    /// </summary>
    private static IEnumerable<ILayoutNode> Decoration(ILayoutNode node)
    {
        foreach (var child in node.Children)
        {
            if (child.Part is { Length: > 0 }) continue;

            if (child.Children.Count > 0)
                foreach (var deeper in Decoration(child)) yield return deeper;
            else if (child.Bounds.Width > 0 && child.Bounds.Height > 0)
                yield return child;
        }
    }

    /// <summary>
    /// The ink nearest the point, for a press that landed on nothing: between a head and the end of its
    /// stem, in the air over a bar, past the last note on a line.
    ///
    /// <para>
    /// <strong>The deeper wins a tie, and that is the whole of it.</strong> A press inside a bar is nought
    /// away from the note, from the bar, from the line and from the tune, because each of them contains
    /// it - so with distance alone the winner was whichever came first in the walk, which is the
    /// outermost. Pressing beside a note selected the entire bar, and the two ends of a drag disagreed
    /// about which layer they were on depending on whether each happened to land on a glyph.
    /// </para>
    /// </summary>
    private static ILayoutNode? Nearest(ILayoutNode root, Point point)
    {
        ILayoutNode? best = null;
        var bestDistance = double.MaxValue;
        var bestDepth = -1;

        foreach (var node in root.Ink())
        {
            var distance = DistanceTo(node.Bounds, point);
            if (distance > bestDistance) continue;

            var depth = node.Ancestors().Count();
            if (distance == bestDistance && depth <= bestDepth) continue;

            bestDistance = distance;
            bestDepth = depth;
            best = node;
        }

        return best;
    }

    /// <summary>Every piece of ink the rectangle touches.</summary>
    public static IReadOnlyList<ILayoutNode> NodesIn(this ILayoutNode root, Rect area) =>
        [.. root.Ink().Where(n => n.Bounds.IntersectsWith(area))];

    // ── Selection ───────────────────────────────────────────────────────────

    /// <summary>
    /// Grows a set of nodes to the largest whole constructs it covers: wherever every piece of ink under
    /// a node is selected, the node itself is selected instead.
    /// <para>
    /// This is where well-formedness comes from. The result is a set of nodes, and a node's source range
    /// is what the parser built it from — so a selection can be a fraction or a matrix row, but never a
    /// numerator plus a stray closing brace.
    /// </para>
    /// </summary>
    public static IReadOnlyList<ILayoutNode> Promote(IEnumerable<ILayoutNode> nodes)
    {
        var chosen = new HashSet<ILayoutNode>(nodes.Where(n => n.Part is { Length: > 0 }));
        if (chosen.Count == 0) return [];

        bool grew;
        do
        {
            grew = false;
            // The nearest ancestor the source named, not merely the next one up. A typesetter wraps things
            // in boxes of its own — a script's row, a denominator's row — and promoting into one of those
            // would yield a selection standing for no part of the text at all.
            foreach (var parent in chosen.Select(NamedAncestor).Where(p => p is not null).Distinct().ToList())
            {
                // Covered, not merely present: a child promoted into a node of its own on an earlier pass
                // is still covered by it, and requiring literal membership would stop promotion one level
                // short — a fraction's numerator would become a node and the fraction never would.
                var ink = parent!.Ink().ToList();
                if (ink.Count == 0 || !ink.All(n => chosen.Contains(n) || n.Ancestors().Any(chosen.Contains)))
                    continue;

                // What the node draws for itself has to be covered too, and since nothing names it that
                // can only be asked of the page: a fraction's bar sits among its numerator and
                // denominator, so a selection of both passes over it, while a radical's sign sits before
                // its contents and a selection inside them does not. Without this, selecting a radicand
                // would grow into the whole root — the piece of the root that is not the radicand having
                // never been asked about.
                if (!Decoration(parent!).All(d => Among(d.Bounds, ink))) continue;

                foreach (var covered in parent!.SelfAndDescendants()) chosen.Remove(covered);
                chosen.Add(parent!);
                grew = true;
            }
        }
        while (grew);

        // Drop anything already inside something else in the set, so a range is never counted twice.
        return [.. chosen.Where(n => !n.Ancestors().Any(chosen.Contains)).OrderBy(n => n.Sits().Start)];
    }

    /// <summary>
    /// The source ranges a set of nodes stands for, merged and in order. More than one, because a matrix
    /// column is a real selection and is not contiguous in the source.
    /// </summary>
    public static IReadOnlyList<(int Start, int Length)> Ranges(IEnumerable<ILayoutNode> nodes) =>
        Merge(nodes.Select(n => n.Part).OfType<ISourcePart>().Where(p => p.Length > 0).Select(p => (p.Start, p.Length)));

    /// <summary>Ranges in order, with touching and overlapping ones folded together.</summary>
    public static IReadOnlyList<(int Start, int Length)> Merge(IEnumerable<(int Start, int Length)> ranges)
    {
        var ordered = ranges.Where(r => r.Length > 0)
            .Select(r => (Start: r.Start, End: r.Start + r.Length))
            .OrderBy(r => r.Start)
            .ToList();
        if (ordered.Count == 0) return [];

        var merged = new List<(int Start, int End)> { ordered[0] };
        foreach (var (start, end) in ordered.Skip(1))
        {
            var last = merged[^1];
            if (start <= last.End) merged[^1] = (last.Start, Math.Max(last.End, end));
            else merged.Add((start, end));
        }

        return [.. merged.Select(r => (r.Start, r.End - r.Start))];
    }

    // ── Caret ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Every bar a caret could be drawn as at <paramref name="offset"/>, in the order an arrow key
    /// visits them: against what ends there, then out through anything that ends where its contents do,
    /// then against what starts there.
    /// <para>
    /// More than one, because a source offset is not a place on the page. The typesetter puts space
    /// around a binary operator, so "after the 6" and "before the +" of <c>6+5</c> are the same character
    /// boundary drawn a hand's width apart, and a reader arrowing along expects to visit both. And LaTeX
    /// lets a one-token argument go unbraced, so the exponent of <c>x^2</c> and the script holding it
    /// finish at the same character: without a second bar there is nowhere to stand past the script, and
    /// the caret can never come back down from the exponent's height.
    /// </para>
    /// <para>
    /// The two reasons for a second bar collapse differently, because they are different reasons. A bar
    /// for the thing starting here is worth having only when there is space to put it in: where nothing
    /// separates the things either side — the <c>2</c> of <c>123</c> — "after this" and "before the next"
    /// are the one position a reader sees, and offering both would take two presses to get past a point
    /// the caret never appeared to leave. A bar for stepping out of an enclosure is often in the same
    /// column as the one inside it and is still a different place: it is a different height, on a
    /// different line, and typing at it means something else.
    /// </para>
    /// </summary>
    public static IReadOnlyList<Rect> CaretBars(this ILayoutNode root, int offset)
    {
        var bars = new List<Rect>();
        var (ends, starts) = Abutting(root, offset);

        if (ends is not null)
        {
            bars.Add(Bar(ends, trailing: true));

            // Out through every enclosure finishing where what is inside it finishes. That is the case
            // the source cannot express: `x^{2}` closes the exponent with a brace and so has an offset
            // of its own for "past the script", while `x^2` has none.
            foreach (var enclosure in ends.Ancestors().Where(a => a.Sits().End == offset && a.IsEnclosure))
            {
                var stepped = Bar(enclosure, trailing: true);
                if (!Coincide(bars[^1], stepped)) bars.Add(stepped);
            }
        }

        if (starts is not null)
        {
            var leading = Bar(starts, trailing: false);
            if (bars.Count == 0 || Math.Abs(bars[^1].X - leading.X) > Hair) bars.Add(leading);
        }

        if (bars.Count > 0) return bars;

        // Nothing begins or ends exactly here, which is the normal case straight after an edit: the caret
        // lands wherever the text was cut. Stand it beside the nearest ink rather than at the origin.
        ILayoutNode? before = null, after = null;
        foreach (var node in root.Ink())
        {
            var at = node.Sits();
            if (at.End <= offset && (before is null || at.End > before.Sits().End)) before = node;
            if (at.Start >= offset && (after is null || at.Start < after.Sits().Start)) after = node;
        }

        if (before is not null) return [Bar(before, trailing: true)];
        if (after is not null) return [Bar(after, trailing: false)];
        return [new Rect(root.Bounds.X, root.Bounds.Y, 0, Math.Max(root.Bounds.Height, 1))];
    }

    /// <summary>Whether two bars would be drawn as the same mark, and so are one place.</summary>
    private static bool Coincide(Rect a, Rect b) =>
        Math.Abs(a.X - b.X) <= Hair && Math.Abs(a.Y - b.Y) <= Hair && Math.Abs(a.Height - b.Height) <= Hair;

    /// <summary>
    /// Where and how tall to draw the caret — the ink it abuts decides, which is what makes it shrink and
    /// rise inside an exponent and take the numerator's height in a fraction.
    /// </summary>
    public static Rect CaretRect(this ILayoutNode root, CaretPlace place)
    {
        var bars = root.CaretBars(place.Offset);
        return bars[Math.Clamp(place.Level, 0, bars.Count - 1)];
    }

    /// <summary>Where and how tall to draw the caret at an offset, read as innermost.</summary>
    public static Rect CaretRect(this ILayoutNode root, int offset) => root.CaretRect(CaretPlace.At(offset));

    /// <summary>
    /// The nodes the caret at <paramref name="offset"/> stands between: the one ending there and the one
    /// starting there, either of which may be absent at an end of the content.
    /// <para>
    /// Ending: the smallest such thing, because the caret belongs to what was just finished — after
    /// <c>x^2</c> it is the exponent's, not the whole script's. Starting: the largest, because the caret
    /// precedes all of them, and taking the smallest would stand a caret before a fraction against its
    /// bar, which is two pixels tall.
    /// </para>
    /// </summary>
    private static (ILayoutNode? Ends, ILayoutNode? Starts) Abutting(ILayoutNode root, int offset)
    {
        ILayoutNode? ends = null, starts = null;

        foreach (var node in root.SelfAndDescendants().Where(n => n.Stands()))
        {
            var at = node.Sits();
            if (at.End == offset && (ends is null || Tighter(node, ends))) ends = node;
            if (at.Start == offset && (starts is null || Wider(node, starts))) starts = node;
        }

        return (ends, starts);
    }

    /// <summary>Smaller in source, and among equals the outer one — a fraction rather than its bar.</summary>
    private static bool Tighter(ILayoutNode candidate, ILayoutNode best)
    {
        var (mine, theirs) = (candidate.Sits().Length, best.Sits().Length);
        return mine < theirs || (mine == theirs && Shallower(candidate, best));
    }

    private static bool Wider(ILayoutNode candidate, ILayoutNode best)
    {
        var (mine, theirs) = (candidate.Sits().Length, best.Sits().Length);
        return mine > theirs || (mine == theirs && Shallower(candidate, best));
    }

    private static bool Shallower(ILayoutNode candidate, ILayoutNode best) =>
        candidate.Ancestors().Count() < best.Ancestors().Count();

    private static Rect Bar(ILayoutNode against, bool trailing) =>
        new(trailing ? against.Bounds.Right : against.Bounds.X,
            against.Bounds.Y,
            0,
            Math.Max(against.Bounds.Height, 1));

    /// <summary>Where a caret may rest: wherever a piece of ink begins or ends, plus either end.</summary>
    public static IReadOnlyList<int> CaretStops(this ILayoutNode root)
    {
        var whole = root.Sits();
        var stops = new SortedSet<int> { whole.Start, whole.End };

        foreach (var node in root.SelfAndDescendants().Where(n => n.Stands()))
        {
            var at = node.Sits();
            stops.Add(at.Start);
            stops.Add(at.End);
        }

        return [.. stops];
    }

    /// <summary>
    /// The next place in <paramref name="forward"/>'s direction, or null at the edge — which is the
    /// host's cue to move the caret out of this content and into whatever surrounds it.
    /// <para>
    /// Through the bars at the offset first, and only then on to the next one. That is what makes the
    /// arrow key walk out of an exponent instead of leaving the formula from inside it, and what puts a
    /// stop on each side of the space around an operator. Backwards it arrives at the far end of the
    /// previous offset's bars, so the two directions retrace one another exactly.
    /// </para>
    /// </summary>
    public static CaretPlace? Step(this ILayoutNode root, CaretPlace place, bool forward)
    {
        if (forward && place.Level + 1 < root.CaretBars(place.Offset).Count)
            return place with { Level = place.Level + 1 };

        if (!forward && place.Level > 0)
            return place with { Level = place.Level - 1 };

        if (root.Step(place.Offset, forward) is not { } next) return null;

        return new CaretPlace(next, forward ? 0 : root.CaretBars(next).Count - 1);
    }

    /// <summary>
    /// The next caret stop in <paramref name="forward"/>'s direction, or null at the edge — which is the
    /// host's cue to move the caret out of this content and into whatever surrounds it.
    /// </summary>
    public static int? Step(this ILayoutNode root, int offset, bool forward)
    {
        var stops = root.CaretStops();
        if (forward)
        {
            foreach (var stop in stops)
                if (stop > offset) return stop;
            return null;
        }

        for (var i = stops.Count - 1; i >= 0; i--)
            if (stops[i] < offset) return stops[i];
        return null;
    }

    /// <summary>
    /// The caret stop on the row above or below — how it crosses a fraction bar or leaves a script.
    /// <para>
    /// Structural, not geometric. By pixels alone a <c>+</c> beside a fraction starts fractionally lower
    /// than the numerator and so beats the denominator the reader meant; asking which of my ancestors has
    /// rows, and stepping within it, gives the answer the reader expects.
    /// </para>
    /// </summary>
    public static int? StepVertical(this ILayoutNode root, int offset, bool up)
    {
        var (ends, starts) = Abutting(root, offset);
        var from = ends ?? starts;
        if (from is null) return null;
        var fromX = root.CaretRect(offset).X;

        foreach (var ancestor in from.Ancestors())
        {
            var rows = ancestor.Rows();
            if (rows.Count < 2) continue;

            var mine = rows.FindIndex(r => r.Any(n => n.SelfAndDescendants().Contains(from)));
            if (mine < 0) continue;

            // Walk outwards past any row that is only decoration. A fraction lays out as numerator, bar,
            // denominator — three rows — and the bar is not somewhere a caret can stand, so down from the
            // numerator has to mean the denominator.
            var step = up ? -1 : 1;
            for (var target = mine + step; target >= 0 && target < rows.Count; target += step)
            {
                // Both ends of each landing candidate are on offer, and the nearest to where the caret
                // already stands wins — moving down a line keeps your place across it, so a caret after
                // the numerator arrives after the denominator rather than jumping in front of it.
                var landing = rows[target]
                    .SelectMany(n => n.Ink())
                    .Where(n => n.Sits().Length < ancestor.Sits().Length)
                    .SelectMany(n => new[]
                    {
                        (Offset: n.Sits().Start, X: n.Bounds.X),
                        (Offset: n.Sits().End, X: n.Bounds.Right),
                    })
                    .OrderBy(stop => Math.Abs(stop.X - fromX))
                    .ThenBy(stop => stop.Offset)
                    .Select(stop => (int?)stop.Offset)
                    .FirstOrDefault();

                if (landing is not null) return landing;
            }
        }

        return null;
    }

    // ── Structure ───────────────────────────────────────────────────────────

    /// <summary>
    /// This node's children grouped into visual rows, top to bottom. A fraction has two, a matrix has
    /// one per line, and an ordinary run of terms has one.
    /// </summary>
    public static List<List<ILayoutNode>> Rows(this ILayoutNode node)
    {
        var rows = new List<List<ILayoutNode>>();
        foreach (var child in node.Children.Where(c => c.Bounds.Height > 0).OrderBy(c => c.Bounds.Y))
        {
            var row = rows.FirstOrDefault(r =>
                r.Any(n => n.Bounds.Top < child.Bounds.Bottom - Hair && child.Bounds.Top < n.Bounds.Bottom - Hair));

            if (row is null) rows.Add([child]);
            else row.Add(child);
        }

        foreach (var row in rows) row.Sort((a, b) => a.Bounds.X.CompareTo(b.Bounds.X));
        return rows;
    }

    /// <summary>
    /// This node's cells as rows and columns, or nothing when it is not a grid.
    /// <para>
    /// A grid is what makes selection behave like a canvas rather than like a line of text: drag down a
    /// column and you get the column, across and you get the row, and corner to corner you get the block
    /// between them. The rows come from the tree, so this is not a matter of clustering rectangles into
    /// bands and hoping.
    /// </para>
    /// <para>
    /// Two rows of two is the least that counts. A fraction stacks a numerator, a rule and a denominator,
    /// which is rows without columns — it is not a grid, and dragging from a numerator to a denominator
    /// must mean the fraction rather than a column of it.
    /// </para>
    /// </summary>
    public static IReadOnlyList<IReadOnlyList<ILayoutNode>> Grid(this ILayoutNode node)
    {
        var rows = node.Rows();
        if (rows.Count < 2) return [];

        var grid = new List<IReadOnlyList<ILayoutNode>>();
        foreach (var row in rows)
        {
            var cells = row.SelectMany(Cells).ToList();
            if (cells.Count < 2) return [];
            if (grid.Count > 0 && cells.Count != grid[0].Count) return [];
            grid.Add(cells);
        }

        return grid;
    }

    /// <summary>What a row is made of: the things inside it that hold ink, left to right.</summary>
    private static IEnumerable<ILayoutNode> Cells(ILayoutNode row)
    {
        var inside = row.Children.Where(c => c.Ink().Any()).OrderBy(c => c.Bounds.X).ToList();
        return inside.Count > 0 ? inside : row.Ink().Any() ? [row] : [];
    }

    // ── Geometry helpers ────────────────────────────────────────────────────

    /// <summary>
    /// Whether something sits amongst a set of nodes rather than beside them. Its centre decides, not its
    /// edges: a fraction's bar overhangs the numerator and denominator it separates, and is still between
    /// them.
    /// </summary>
    private static bool Among(Rect bounds, IEnumerable<ILayoutNode> nodes)
    {
        var hull = Rect.Empty;
        foreach (var node in nodes) hull.Union(node.Bounds);
        if (hull.IsEmpty) return false;

        var centre = new Point(bounds.X + bounds.Width / 2, bounds.Y + bounds.Height / 2);
        return Contains(hull, centre);
    }

    private static bool Contains(Rect bounds, Point point) =>
        point.X >= bounds.X - Hair && point.X <= bounds.Right + Hair
        && point.Y >= bounds.Y - Hair && point.Y <= bounds.Bottom + Hair;

    private static double DistanceTo(Rect rect, Point point)
    {
        var dx = Math.Max(Math.Max(rect.X - point.X, point.X - rect.Right), 0);
        var dy = Math.Max(Math.Max(rect.Y - point.Y, point.Y - rect.Bottom), 0);
        return dx * dx + dy * dy;
    }
}
