using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;

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

    /// <summary>The nearest thing containing this node that the source actually named.</summary>
    private static ILayoutNode? NamedAncestor(ILayoutNode node) =>
        node.Ancestors().FirstOrDefault(a => a.SourceLength > 0);

    /// <summary>
    /// What a node draws that no part of the source named: a fraction's bar, a radical's sign, a beam
    /// between two notes. The walk stops at anything with a name of its own, because that node's innards
    /// are its own business rather than this one's decoration.
    /// </summary>
    private static IEnumerable<ILayoutNode> Decoration(ILayoutNode node)
    {
        foreach (var child in node.Children)
        {
            if (child.SourceLength > 0) continue;

            if (child.Children.Count > 0)
                foreach (var deeper in Decoration(child)) yield return deeper;
            else if (child.Bounds.Width > 0 && child.Bounds.Height > 0)
                yield return child;
        }
    }

    private static ILayoutNode? Nearest(ILayoutNode root, Point point)
    {
        ILayoutNode? best = null;
        var bestDistance = double.MaxValue;

        foreach (var node in root.Ink())
        {
            var distance = DistanceTo(node.Bounds, point);
            if (distance >= bestDistance) continue;
            bestDistance = distance;
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
        var chosen = new HashSet<ILayoutNode>(nodes.Where(n => n.SourceLength > 0));
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
        return [.. chosen.Where(n => !n.Ancestors().Any(chosen.Contains)).OrderBy(n => n.SourceStart)];
    }

    /// <summary>
    /// The source ranges a set of nodes stands for, merged and in order. More than one, because a matrix
    /// column is a real selection and is not contiguous in the source.
    /// </summary>
    public static IReadOnlyList<(int Start, int Length)> Ranges(IEnumerable<ILayoutNode> nodes) =>
        Merge(nodes.Where(n => n.SourceLength > 0).Select(n => (n.SourceStart, n.SourceLength)));

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
    /// Where and how tall to draw the caret at <paramref name="offset"/> — the ink it abuts decides, which
    /// is what makes it shrink and rise inside an exponent and take the numerator's height in a fraction.
    /// </summary>
    public static Rect CaretRect(this ILayoutNode root, int offset)
    {
        if (Abutting(root, offset, out var trailing) is { } against) return Bar(against, trailing);

        // Nothing begins or ends exactly here, which is the normal case straight after an edit: the caret
        // lands wherever the text was cut. Stand it beside the nearest ink rather than at the origin.
        ILayoutNode? before = null, after = null;
        foreach (var node in root.Ink())
        {
            if (node.SourceEnd() <= offset && (before is null || node.SourceEnd() > before.SourceEnd())) before = node;
            if (node.SourceStart >= offset && (after is null || node.SourceStart < after.SourceStart)) after = node;
        }

        if (before is not null) return Bar(before, trailing: true);
        if (after is not null) return Bar(after, trailing: false);
        return new Rect(root.Bounds.X, root.Bounds.Y, 0, Math.Max(root.Bounds.Height, 1));
    }

    /// <summary>
    /// The deepest node the caret at <paramref name="offset"/> stands against. One that <em>ends</em>
    /// there wins: as in text, the caret belongs to what precedes it, so typing continues the exponent
    /// you just finished rather than the base you left. Containers are considered only when no ink abuts,
    /// so a caret before a fraction is as tall as the fraction rather than as its first digit.
    /// </summary>
    private static ILayoutNode? Abutting(ILayoutNode root, int offset, out bool trailing)
    {
        ILayoutNode? ends = null, starts = null;

        foreach (var node in root.SelfAndDescendants().Where(n => n.Stands()))
        {
            // Ending here: the smallest such thing, because the caret belongs to what was just finished —
            // after `x^2` it is the exponent's, not the whole script's.
            if (node.SourceEnd() == offset && (ends is null || Tighter(node, ends))) ends = node;

            // Starting here: the largest, because the caret precedes all of them. Taking the smallest
            // would stand a caret before a fraction against its bar, which is two pixels tall.
            if (node.SourceStart == offset && (starts is null || Wider(node, starts))) starts = node;
        }

        trailing = ends is not null;
        return ends ?? starts;
    }

    /// <summary>Smaller in source, and among equals the outer one — a fraction rather than its bar.</summary>
    private static bool Tighter(ILayoutNode candidate, ILayoutNode best) =>
        candidate.SourceLength < best.SourceLength
        || (candidate.SourceLength == best.SourceLength && Shallower(candidate, best));

    private static bool Wider(ILayoutNode candidate, ILayoutNode best) =>
        candidate.SourceLength > best.SourceLength
        || (candidate.SourceLength == best.SourceLength && Shallower(candidate, best));

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
        var stops = new SortedSet<int> { root.SourceStart, root.SourceEnd() };
        foreach (var node in root.SelfAndDescendants().Where(n => n.Stands()))
        {
            stops.Add(node.SourceStart);
            stops.Add(node.SourceEnd());
        }
        return [.. stops];
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
        var from = Abutting(root, offset, out _);
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
                    .Where(n => n.SourceLength < ancestor.SourceLength)
                    .SelectMany(n => new[]
                    {
                        (Offset: n.SourceStart, X: n.Bounds.X),
                        (Offset: n.SourceEnd(), X: n.Bounds.Right),
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
