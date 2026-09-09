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
/// no STA thread, no fonts and no desktop, so all of this is exercised against built trees.
/// </para>
/// <para>
/// Anything asking about <em>every</em> piece descends with <see cref="Piece.Placed"/> rather than
/// reading <see cref="Piece.Bounds"/> in a loop. Geometry is relative, so a piece's place on the page is
/// a climb to the root; taken once per piece that is the one way to make relative geometry cost
/// something, and taken on the way down it is free.
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
    public static Piece PieceAt(this Piece root, Point point)
    {
        var deepest = Deepest(root, point);
        return deepest.Exists ? deepest : Nearest(root, point);
    }

    /// <summary>
    /// The source offset a press at <paramref name="point"/> means: the near end of whatever it landed on,
    /// so pressing the left half of a note puts the caret before it and the right half after it.
    /// </summary>
    /// <remarks>
    /// The near end rather than the start, because a caret is a place between things and a press is a
    /// reader saying which side of one they want to be on. Always the start would make it impossible to
    /// get past the last note of a tune by clicking.
    /// </remarks>
    public static int OffsetAt(this Piece root, Point point)
    {
        if (root.PieceAt(point) is not { Exists: true } piece) return root.Sits().Start;

        var at = piece.Sits();
        if (at.Length <= 0) return at.Start;

        var where = piece.Bounds;
        return point.X <= where.X + (where.Width / 2) ? at.Start : at.End;
    }

    /// <summary>
    /// Where the caret goes for a press: the offset, and which of the bars drawn at that offset.
    /// <para>
    /// One offset can be two places on the page — after a note and before the next are the same character
    /// boundary drawn a hand's width apart — so the press also has to say which of them it meant, and the
    /// nearest by column is what it meant.
    /// </para>
    /// </summary>
    public static CaretPlace PlaceAt(this Piece root, Point point)
    {
        var offset = root.OffsetAt(point);
        var bars = root.CaretBars(offset);

        var level = 0;
        for (var at = 1; at < bars.Count; at++)
            if (Math.Abs(bars[at].X - point.X) < Math.Abs(bars[level].X - point.X) - Hair) level = at;

        return new CaretPlace(offset, level);
    }

    /// <summary>
    /// The drawn thing under the point. Only leaves are candidates — they are what actually put ink on the
    /// page — and blank space inside a container belongs to nobody, which is what stops a press in the gap
    /// between two terms coming back with the start of the whole line.
    /// <para>
    /// Containers are not used to narrow the search, and must not be. A container's extent is a
    /// typesetting measurement — the height and depth it reserves on its line — not a bounding box of what
    /// it draws, so a subscript hanging below its operator or an accent riding above its letter sits
    /// outside the very piece that holds it, and gating descent on the parent lost every one of those.
    /// </para>
    /// </summary>
    private static Piece Deepest(Piece root, Point point)
    {
        var best = default(Piece);
        var bestRank = (Named: -1, Depth: -1);

        foreach (var (piece, where) in root.Placed())
        {
            if (piece.Children.Count > 0) continue;              // draws nothing itself
            if (where.Width <= 0 || where.Height <= 0) continue; // spacing
            if (!Contains(where, point)) continue;

            // Named by the source, or else part of the drawing of whatever encloses it — a fraction's bar,
            // a radical's sign, the letters a macro expands to — in which case the press means that. A
            // hole names a place of its own without covering any characters, and pointing at one means
            // it rather than the construct around it.
            var resolved = piece.Stands() ? piece : NamedAncestor(piece);
            if (!resolved.Exists) continue;

            // Where several overlap, one that holds a place beats one that does not, because it is the
            // more specific answer; between equals, the deeper. Neither is a matter of which is smaller
            // on the page — that only ever settled it by luck.
            var rank = (Named: piece.Stands() ? 1 : 0, Depth: piece.Depth);
            if (rank.CompareTo(bestRank) <= 0) continue;

            bestRank = rank;
            best = resolved;
        }

        return best;
    }

    /// <summary>
    /// The first thing a selection could hold, climbing from here: this piece if the source named it, and
    /// otherwise the nearest thing above it that it did.
    ///
    /// <para>
    /// Where selection starts, every time. What a reader points at is a glyph, a stem, a syllable — most
    /// of which name nothing on their own — and what they mean by pointing at it is the smallest written
    /// thing it is part of. Climbing is the only way to get from one to the other, and it is why nothing
    /// here ever needs to know what a piece contains.
    /// </para>
    /// </summary>
    public static Piece Selectable(this Piece piece) =>
        piece.Part is { Length: > 0 } ? piece : NamedAncestor(piece);

    /// <summary>
    /// One step from here along an axis — the next thing to select when a selection grows that way, or
    /// nothing when there is nothing that way.
    ///
    /// <para>
    /// The step is taken on the run that orders this piece (see <see cref="Piece.Along"/>), and what comes
    /// back is climbed to the first thing the source named — usually itself, and not always.
    /// </para>
    /// <para>
    /// <strong>A piece on no run still steps</strong>, on the tree that draws it: sideways to the next
    /// thing its parent holds, and vertically to the nearest thing on the row above or below. A run is how
    /// a builder says something the drawing does not — that a syllable belongs with the other syllables of
    /// its verse rather than with the note above it — and where there is nothing to correct, where a thing
    /// is drawn is a perfectly good account of what is beside it. So this is not a feature only declared
    /// content gets; it is the ordinary behaviour, which a run overrides.
    /// </para>
    /// <para>
    /// Deliberately one step and no more. A run is walked by taking them, so nothing has to hold what the
    /// run <em>is</em> — which is what lets a lyric carry on across systems, a maths block stop at its own
    /// edge, and a diagram stay inside its subtree, all from the same code and without any of them being
    /// asked to pretend it is a row.
    /// </para>
    /// </summary>
    public static Piece Step(this Piece piece, bool vertical, bool forward)
    {
        if (piece.RunOn(vertical) >= 0) return piece.Along(vertical, forward).Selectable();

        return vertical ? Stacked(piece, forward) : Beside(piece, forward);
    }

    /// <summary>The sibling one place along, climbed to the first thing the source named.</summary>
    private static Piece Beside(Piece piece, bool forward)
    {
        var previous = default(Piece);
        var take = false;

        foreach (var sibling in piece.Parent.Children)
        {
            if (take) return sibling.Selectable();
            if (sibling == piece)
            {
                if (!forward) return previous.Exists ? previous.Selectable() : default;
                take = true;
            }

            previous = sibling;
        }

        return default;
    }

    /// <summary>
    /// A step up or down taken on the tree that draws things, for content with no run of its own: the row
    /// above or below within whatever holds this, and the thing on it nearest to where this one starts.
    /// <para>
    /// Nearest by column rather than first on the row, because moving down a line is meant to keep your
    /// place across it — the same rule the caret follows through a fraction.
    /// </para>
    /// </summary>
    private static Piece Stacked(Piece piece, bool forward)
    {
        if (!piece.Parent.Exists) return default;

        var rows = piece.Parent.Rows();
        var mine = rows.FindIndex(row => row.Any(p => p == piece));
        if (mine < 0) return default;

        var to = mine + (forward ? 1 : -1);
        if (to < 0 || to >= rows.Count) return default;

        var from = piece.Bounds.X;
        foreach (var candidate in rows[to].OrderBy(p => Math.Abs(p.Bounds.X - from)))
            if (candidate.Selectable() is { Exists: true } named) return named;

        return default;
    }

    /// <summary>The nearest thing containing this piece that the source actually named.</summary>
    private static Piece NamedAncestor(Piece piece)
    {
        foreach (var up in piece.Ancestors())
            if (up.Part is { Length: > 0 }) return up;
        return default;
    }

    /// <summary>
    /// What a piece draws that no part of the source named: a fraction's bar, a radical's sign, a beam
    /// between two notes. The walk stops at anything with a name of its own, because that piece's innards
    /// are its own business rather than this one's decoration.
    /// </summary>
    private static IEnumerable<Piece> Decoration(Piece piece)
    {
        foreach (var child in piece.Children)
        {
            if (child.Part is { Length: > 0 }) continue;

            if (child.Children.Count > 0)
                foreach (var deeper in Decoration(child)) yield return deeper;
            else if (child.Bounds.Width > 0 && child.Bounds.Height > 0)
                yield return child;
        }
    }

    /// <summary>
    /// The ink nearest the point, for a press that landed on nothing: between two note heads, in the air
    /// over a bar, past the last note on a line.
    ///
    /// <para>
    /// <strong>Something that holds selectable things of its own is the last resort, not the first.</strong>
    /// A beamed group's rectangle covers the notes it joins, so a press in the gap between two heads is
    /// nought away from the group and a little way from either note — and the group won. Dragging along a
    /// run of notes flickered between two notes and the whole group, every time the pointer crossed a gap.
    /// A group is something a reader gets by covering it, not by aiming between its members.
    /// </para>
    /// <para>
    /// This is the fallback only. A press that actually lands on a construct's own drawing — a fraction's
    /// bar, a radical's sign — still means that construct, because there the reader really did point at it.
    /// </para>
    /// <para>
    /// The deeper wins a tie among equals, which is what makes a press inside a bar mean the note it landed
    /// on rather than the bar: both contain it, so both are nought away, and document order was deciding.
    /// </para>
    /// </summary>
    private static Piece Nearest(Piece root, Point point)
    {
        var alone = Nearest(root, point, alone: true);
        return alone.Exists ? alone : Nearest(root, point, alone: false);
    }

    /// <param name="alone">
    /// Whether to consider only pieces holding no other selectable piece — the things a reader points at,
    /// as opposed to the things they cover.
    /// </param>
    private static Piece Nearest(Piece root, Point point, bool alone)
    {
        var best = default(Piece);
        var bestDistance = double.MaxValue;
        var bestDepth = -1;

        foreach (var (piece, where) in root.Placed())
        {
            if (!piece.IsLeaf) continue;
            if (alone && Holds(piece)) continue;

            var distance = DistanceTo(where, point);
            if (distance > bestDistance) continue;

            var depth = piece.Depth;
            if (distance == bestDistance && depth <= bestDepth) continue;

            bestDistance = distance;
            bestDepth = depth;
            best = piece;
        }

        return best;
    }

    /// <summary>Whether this piece holds a selectable piece of its own — whether it is a group.</summary>
    private static bool Holds(Piece piece)
    {
        var self = true;
        foreach (var inside in piece.SelfAndDescendants())
        {
            if (self) { self = false; continue; }
            if (inside.Part is { Length: > 0 }) return true;
        }

        return false;
    }

    /// <summary>Every piece of ink the rectangle touches.</summary>
    public static IReadOnlyList<Piece> PiecesIn(this Piece root, Rect area) =>
        [.. root.Placed().Where(p => p.Piece.IsLeaf && p.Where.IntersectsWith(area)).Select(p => p.Piece)];

    // ── Selection ───────────────────────────────────────────────────────────

    /// <summary>
    /// Grows a set of pieces to the largest whole constructs it covers: wherever every piece of ink under
    /// one is selected, that one is selected instead.
    /// <para>
    /// This is where well-formedness comes from. The result is a set of pieces, and a piece's source range
    /// is what the parser built it from — so a selection can be a fraction or a matrix row, but never a
    /// numerator plus a stray closing brace.
    /// </para>
    /// </summary>
    public static IReadOnlyList<Piece> Promote(IEnumerable<Piece> pieces)
    {
        var chosen = new HashSet<Piece>(pieces.Where(p => p.Part is { Length: > 0 }));
        if (chosen.Count == 0) return [];

        bool grew;
        do
        {
            grew = false;
            // The nearest ancestor the source named, not merely the next one up. A typesetter wraps things
            // in boxes of its own — a script's row, a denominator's row — and promoting into one of those
            // would yield a selection standing for no part of the text at all.
            foreach (var parent in chosen.Select(NamedAncestor).Where(p => p.Exists).Distinct().ToList())
            {
                // Covered, not merely present: a child promoted into a piece of its own on an earlier pass
                // is still covered by it, and requiring literal membership would stop promotion one level
                // short — a fraction's numerator would become a piece and the fraction never would.
                var ink = parent.Leaves().ToList();
                if (ink.Count == 0 || !ink.All(p => chosen.Contains(p) || p.Ancestors().Any(chosen.Contains)))
                    continue;

                // What the piece draws for itself has to be covered too, and since nothing names it that
                // can only be asked of the page: a fraction's bar sits among its numerator and
                // denominator, so a selection of both passes over it, while a radical's sign sits before
                // its contents and a selection inside them does not. Without this, selecting a radicand
                // would grow into the whole root — the piece of the root that is not the radicand having
                // never been asked about.
                if (!Decoration(parent).All(d => Among(d.Bounds, ink))) continue;

                foreach (var covered in parent.SelfAndDescendants()) chosen.Remove(covered);
                chosen.Add(parent);
                grew = true;
            }
        }
        while (grew);

        // Drop anything already inside something else in the set, so a range is never counted twice.
        return [.. chosen.Where(p => !p.Ancestors().Any(chosen.Contains)).OrderBy(p => p.Sits().Start)];
    }

    /// <summary>
    /// The source ranges a set of pieces stands for, merged and in order. More than one, because a matrix
    /// column is a real selection and is not contiguous in the source.
    /// </summary>
    public static IReadOnlyList<(int Start, int Length)> Ranges(IEnumerable<Piece> pieces) =>
        Merge(pieces.Select(p => p.Part).OfType<ISourcePart>().Where(p => p.Length > 0).Select(p => (p.Start, p.Length)));

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
    /// Every bar that may be drawn at one offset, innermost first.
    ///
    /// <para>
    /// One offset can be two places on the page: just inside a trailing exponent, and past it. Both are the
    /// same character position and a reader means different things by them, so they are both here and
    /// <see cref="CaretPlace"/> says which one the caret is at.
    /// </para>
    /// <para>
    /// Read off what the builders declared. A piece says where a caret may rest against it, and the tree is
    /// honoured — a script inside a row ends where the row ends, so both declare a stop there and the reader
    /// gets two bars. This used to be worked out from wherever a piece happened to name characters, with a
    /// walk out through enclosures bolted on to recover the cases that missed.
    /// </para>
    /// </summary>
    public static IReadOnlyList<Rect> CaretBars(this Piece root, int offset)
    {
        var bars = new List<Rect>();

        // Innermost first, which is where a reader who has just typed means to be, and then out through the
        // enclosures that finish here — a construct a caret can be inside and then outside of is two places
        // at one offset, and the builder is what says which of them are. A run that merely ends where its
        // last thing ends is not one, or the arrow key would walk between two identical positions instead of
        // leaving the content.
        foreach (var piece in Declaring(root, offset, Stops.After).OrderByDescending(p => p.Depth))
        {
            if (bars.Count > 0 && !piece.IsEnclosure) continue;

            var bar = Bar(piece, trailing: true);
            if (bars.Count == 0 || !Coincide(bars[^1], bar)) bars.Add(bar);
        }

        foreach (var piece in Declaring(root, offset, Stops.Before).OrderByDescending(p => p.Depth))
        {
            var bar = Bar(piece, trailing: false);
            if (bars.Count == 0 || Math.Abs(bars[^1].X - bar.X) > Hair) bars.Add(bar);
        }

        if (bars.Count > 0) return bars;

        // Nothing declares a stop exactly here, which is the normal case straight after an edit: the caret
        // lands wherever the text was cut. Stand it beside the nearest drawing rather than at the origin.
        Piece before = default, after = default;
        foreach (var piece in root.Leaves())
        {
            var at = piece.Sits();
            if (at.End <= offset && (!before.Exists || at.End > before.Sits().End)) before = piece;
            if (at.Start >= offset && (!after.Exists || at.Start < after.Sits().Start)) after = piece;
        }

        if (before.Exists) return [Bar(before, trailing: true)];
        if (after.Exists) return [Bar(after, trailing: false)];

        var whole = root.Bounds;
        return whole.IsEmpty
            ? [new Rect(0, 0, 0, 1)]
            : [new Rect(whole.X, whole.Y, 0, Math.Max(whole.Height, 1))];
    }

    /// <summary>Every piece declaring a stop of this kind at this offset.</summary>
    private static IEnumerable<Piece> Declaring(Piece root, int offset, Stops edge)
    {
        foreach (var piece in root.SelfAndDescendants())
        {
            if (!piece.Stands() || !piece.Stops.HasFlag(edge)) continue;

            var at = piece.Sits();
            if (edge == Stops.After ? at.End == offset : at.Start == offset) yield return piece;
        }
    }

    /// <summary>Whether two bars would be drawn as the same mark, and so are one place.</summary>
    private static bool Coincide(Rect a, Rect b) =>
        Math.Abs(a.X - b.X) <= Hair && Math.Abs(a.Y - b.Y) <= Hair && Math.Abs(a.Height - b.Height) <= Hair;

    /// <summary>
    /// Where and how tall to draw the caret — the ink it abuts decides, which is what makes it shrink and
    /// rise inside an exponent and take the numerator's height in a fraction.
    /// </summary>
    public static Rect CaretRect(this Piece root, CaretPlace place)
    {
        var bars = root.CaretBars(place.Offset);
        return bars[Math.Clamp(place.Level, 0, bars.Count - 1)];
    }

    /// <summary>Where and how tall to draw the caret at an offset, read as innermost.</summary>
    public static Rect CaretRect(this Piece root, int offset) => root.CaretRect(CaretPlace.At(offset));

    /// <summary>Smaller in source, and among equals the outer one — a fraction rather than its bar.</summary>
    private static bool Tighter(Piece candidate, Piece best)
    {
        var (mine, theirs) = (candidate.Sits().Length, best.Sits().Length);
        return mine < theirs || (mine == theirs && candidate.Depth < best.Depth);
    }

    private static bool Wider(Piece candidate, Piece best)
    {
        var (mine, theirs) = (candidate.Sits().Length, best.Sits().Length);
        return mine > theirs || (mine == theirs && candidate.Depth < best.Depth);
    }

    private static Rect Bar(Piece against, bool trailing)
    {
        var where = against.Bounds;
        if (where.IsEmpty) return new Rect(0, 0, 0, 1);

        return new Rect(trailing ? where.Right : where.X, where.Y, 0, Math.Max(where.Height, 1));
    }

    /// <summary>
    /// Where a caret may rest, ascending — every stop the builders declared.
    ///
    /// <para>
    /// Not "wherever a piece names characters", which is what this used to be. A builder knows whether what
    /// it has made can be written before and after; the layout only knew where the source happened to fall.
    /// </para>
    /// </summary>
    public static IReadOnlyList<int> CaretStops(this Piece root)
    {
        var stops = new SortedSet<int>();

        foreach (var piece in root.SelfAndDescendants())
        {
            if (!piece.Stands()) continue;

            var at = piece.Sits();
            if (piece.Stops.HasFlag(Stops.Before)) stops.Add(at.Start);
            if (piece.Stops.HasFlag(Stops.After)) stops.Add(at.End);
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
    public static CaretPlace? Step(this Piece root, CaretPlace place, bool forward)
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
    public static int? Step(this Piece root, int offset, bool forward)
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
    public static int? StepVertical(this Piece root, int offset, bool up)
    {
        // Whatever declares a stop here, innermost first — what the caret is actually standing against.
        var from = Declaring(root, offset, Stops.After)
            .Concat(Declaring(root, offset, Stops.Before))
            .OrderByDescending(piece => piece.Depth)
            .FirstOrDefault();

        if (!from.Exists) return null;
        var fromX = root.CaretRect(offset).X;

        foreach (var ancestor in from.Ancestors())
        {
            var rows = ancestor.Rows();
            if (rows.Count < 2) continue;

            var mine = rows.FindIndex(r => r.Any(p => p.SelfAndDescendants().Contains(from)));
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
                    .SelectMany(p => p.Leaves())
                    .Where(p => p.Sits().Length < ancestor.Sits().Length)
                    .SelectMany(p => new[]
                    {
                        (Offset: p.Sits().Start, X: p.Bounds.X),
                        (Offset: p.Sits().End, X: p.Bounds.Right),
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
    /// This piece's children grouped into visual rows, top to bottom. A fraction has two, a matrix has
    /// one per line, and an ordinary run of terms has one.
    /// </summary>
    public static List<List<Piece>> Rows(this Piece piece)
    {
        var rows = new List<List<Piece>>();

        foreach (var child in piece.Children.Where(c => c.Bounds.Height > 0).OrderBy(c => c.Bounds.Y))
        {
            var where = child.Bounds;
            var row = rows.FirstOrDefault(r =>
                r.Any(p => p.Bounds.Top < where.Bottom - Hair && where.Top < p.Bounds.Bottom - Hair));

            if (row is null) rows.Add([child]);
            else row.Add(child);
        }

        foreach (var row in rows) row.Sort((a, b) => a.Bounds.X.CompareTo(b.Bounds.X));
        return rows;
    }

    // ── Geometry helpers ────────────────────────────────────────────────────

    /// <summary>
    /// Whether something sits amongst a set of pieces rather than beside them. Its centre decides, not its
    /// edges: a fraction's bar overhangs the numerator and denominator it separates, and is still between
    /// them.
    /// </summary>
    private static bool Among(Rect bounds, IEnumerable<Piece> pieces)
    {
        var hull = Rect.Empty;
        foreach (var piece in pieces) hull.Union(piece.Bounds);
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

    // ── What a source range covers ──────────────────────────────────────────

    /// <summary>
    /// The rectangles to wash for a stretch of source — one per run, already merged, so a translucent
    /// selection never paints the same pixel twice and darkens where two of them met.
    ///
    /// <para>
    /// Whole pieces, not the glyphs inside them. A piece's bounds cover the parts of it no character
    /// produced — a fraction's bar, a radical's hook — which washing the leaves alone would leave clear, so
    /// a fully selected fraction would read as two selected numbers with a gap between them.
    /// </para>
    /// <para>
    /// Which is why this asks whether a piece <em>stands</em> for a place rather than whether it covers any
    /// characters. A hole covers none by definition, so a selection sweeping across a half-written fraction
    /// would otherwise wash everything except the part still missing — the one piece the reader most needs
    /// to see they have picked up.
    /// </para>
    /// </summary>
    public static IReadOnlyList<Rect> RangeRects(this Piece root, int start, int length)
    {
        if (length <= 0 || !root.Exists) return [];
        var end = start + length;

        var covered = root.SelfAndDescendants()
            .Where(piece => piece.Stands() && piece.Sits().Start >= start && piece.Sits().End <= end)
            .ToHashSet();

        return Bands([.. covered.Where(piece => !piece.Ancestors().Any(covered.Contains)).Select(piece => piece.Bounds)]);
    }

    /// <summary>
    /// Collapses the rectangles of one contiguous source range into as few as possible: anything sharing a
    /// vertical band becomes one run.
    ///
    /// <para>
    /// Horizontal gaps are closed deliberately rather than preserved. The selection is a contiguous run of
    /// source, so whatever sits in the gap is inside it — the glue a typesetter puts around a binary
    /// operator, the space between two words — and leaving those unpainted would break one selection into a
    /// row of disconnected blocks. Stacked bands (a sum's limits above and below it, a verse under its
    /// music) stay separate unless something spans them.
    /// </para>
    /// </summary>
    public static List<Rect> Bands(List<Rect> rects)
    {
        var merged = true;
        while (merged)
        {
            merged = false;
            for (var i = 0; i < rects.Count && !merged; i++)
                for (var j = i + 1; j < rects.Count && !merged; j++)
                {
                    var a = rects[i];
                    var b = rects[j];
                    if (a.Top >= b.Bottom - Hair || b.Top >= a.Bottom - Hair) continue;   // different bands

                    a.Union(b);
                    rects[i] = a;
                    rects.RemoveAt(j);
                    merged = true;
                }
        }

        return rects;
    }

    /// <summary>
    /// The whole things a raw range covers, as a source range. Dragging across <c>x^2</c> selects the script
    /// rather than stopping mid-command at <c>x^{2</c>, and dragging from a fraction's numerator to its
    /// denominator selects the fraction rather than the <c>1}{x</c> the offsets alone would give.
    ///
    /// <para>
    /// Both fall out of promotion: the answer is made of whole pieces, and a piece's source range is what it
    /// was built from, so it cannot be a half-open brace. Nothing here knows what a brace is, which is why
    /// the same call snaps a drag over half a beam group to the group.
    /// </para>
    /// </summary>
    public static (int Start, int Length) Snap(this Piece root, int start, int length)
    {
        var whole = root.Sits();
        var from = Math.Clamp(Math.Min(start, start + length), whole.Start, whole.End);
        var to = Math.Clamp(Math.Max(start, start + length), whole.Start, whole.End);
        if (from == to) return (from, 0);

        // Whatever lies wholly inside the range was dragged over — every piece, not only the ink, so a drag
        // from before a root's sign to past its contents takes the root itself and not merely what is under
        // the bar. Anything only half inside is left to promotion, which is what stops a range that clipped a
        // brace of `^{n}` from coming back as `{n`.
        var touched = root.SelfAndDescendants()
            .Where(piece => piece.Sits() is { Length: > 0 } at && at.Start >= from && at.End <= to)
            .ToList();

        if (touched.Count == 0)
        {
            // The range covers only characters nothing was drawn for — a lone brace, say, or half of a
            // command's name. Snap to whatever is nearest, so a selection is always of something visible.
            var nearest = root.Leaves()
                .OrderBy(piece => Math.Min(Math.Abs(piece.Sits().Start - from), Math.Abs(piece.Sits().End - to)))
                .FirstOrDefault();
            if (!nearest.Exists) return (from, to - from);
            touched.Add(nearest);
        }

        var promoted = Promote(touched);
        if (promoted.Count == 0) return (from, to - from);

        var (first, last) = (promoted.Min(piece => piece.Sits().Start), promoted.Max(piece => piece.Sits().End));
        return (first, last - first);
    }
}
