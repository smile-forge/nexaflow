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

            // What the press would mean, by the same rule a direct hit already uses: the first thing above the
            // ink that names source. Ink with nothing above it — a staff line, a barcode's bars — is drawing
            // nobody can pick, and letting it win put the caret wherever that drawing reported itself as
            // sitting: a press between two notes landed on a staff line and took the caret up into the title.
            var resolved = piece.Selectable();
            if (!resolved.Exists) continue;

            var distance = DistanceTo(where, point);
            if (distance > bestDistance) continue;

            var depth = piece.Depth;
            if (distance == bestDistance && depth <= bestDepth) continue;

            bestDistance = distance;
            bestDepth = depth;
            best = resolved;
        }

        return best;
    }

    /// <summary>Whether this piece holds a selectable piece of its own — whether it is a group.</summary>
    public static bool Holds(this Piece piece)
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
    /// Works out everywhere a caret may rest, in the order the right arrow visits them. Asked once per
    /// tree: <see cref="LayoutTree.Places"/> keeps the answer, beside the part links the stops ride on.
    ///
    /// <para>
    /// Read off what the builders declared, and nothing else. A piece says which of its edges a caret may
    /// stand against (<see cref="Piece.Stops"/>) and the tree is honoured: a script inside a term finishes
    /// where the term finishes, so both declare a stop at that character and the reader gets both — one
    /// raised and half height, one back on the line. That is why a place is a piece rather than an offset.
    /// </para>
    /// <para>
    /// This used to be a list of bars per offset, with a walk out through anything flagged as an enclosure
    /// bolted on to recover the places nobody had declared. The flag has gone with the walk: a run declares
    /// no stops of its own — its ends are its contents' ends, which is what makes it a run — and once that
    /// is said in the builder, every place left is one somebody meant.
    /// </para>
    /// <para>
    /// Innermost first at each offset, and trailing edges before leading ones: against the thing that ends
    /// here, out through whatever else ends here, then against the thing that starts here. Where two would
    /// be drawn as the one mark there is one place, so the reader never presses an arrow twice for a caret
    /// that does not appear to move.
    /// </para>
    /// </summary>
    internal static IReadOnlyList<CaretPlace> PlacesIn(Piece root)
    {
        var declared = new List<(int Offset, int Edge, int Depth, Piece Piece)>();

        foreach (var piece in root.SelfAndDescendants())
        {
            if (!piece.Stands()) continue;

            var at = piece.Sits();
            if (piece.Stops.HasFlag(Stops.After)) declared.Add((at.End, 0, piece.Depth, piece));
            if (piece.Stops.HasFlag(Stops.Before)) declared.Add((at.Start, 1, piece.Depth, piece));
        }

        declared.Sort((a, b) =>
            a.Offset != b.Offset ? a.Offset.CompareTo(b.Offset)
            : a.Edge != b.Edge ? a.Edge.CompareTo(b.Edge)
            : b.Depth.CompareTo(a.Depth));

        var places = new List<CaretPlace>();
        foreach (var (offset, edge, _, piece) in declared)
        {
            var place = new CaretPlace(piece, Trailing: edge == 0);
            if (places.Count > 0 && places[^1].Offset == offset && Drawn(places[^1], place)) continue;

            places.Add(place);
        }

        return places;
    }

    /// <summary>
    /// Whether a place would be drawn as the mark already standing there, and so is not a second place.
    ///
    /// <para>
    /// Two rules, because two different things put places at one offset. Trailing edges nest — a script
    /// inside a term inside a row all finish at the same character — and height is the only thing telling
    /// them apart, so every number has to agree. A leading edge is the far side of a boundary between two
    /// things set beside each other, and there the caret is the same mark whether it takes this letter's
    /// height or the last one's: the column is all that says anything. Without the split, <c>12g</c> would
    /// offer two places between the 2 and the g, drawn one on top of the other, because the g has a
    /// descender.
    /// </para>
    /// </summary>
    private static bool Drawn(CaretPlace already, CaretPlace next)
    {
        var (standing, coming) = (already.CaretRect(), next.CaretRect());

        return next.Trailing ? Coincide(standing, coming) : Math.Abs(standing.X - coming.X) <= Hair;
    }

    /// <summary>Whether two bars would be drawn as the same mark, and so are one place.</summary>
    private static bool Coincide(Rect a, Rect b) =>
        Math.Abs(a.X - b.X) <= Hair && Math.Abs(a.Y - b.Y) <= Hair && Math.Abs(a.Height - b.Height) <= Hair;

    /// <summary>
    /// Where and how tall to draw the caret: against the ink of the piece it stands on, which is what makes
    /// it shrink and rise inside an exponent and take the numerator's height in a fraction.
    /// </summary>
    public static Rect CaretRect(this CaretPlace place) => Bar(place.Against, place.Trailing);

    /// <summary>
    /// Which stop is at an offset: the innermost, or the outermost for a caret arriving from outside the
    /// content with everything here behind it. -1 when nothing declares a stop there — which is the ordinary
    /// case straight after an edit, where the caret lands wherever the text was cut.
    /// </summary>
    public static int StopAt(this Piece root, int offset, bool outermost = false)
    {
        var places = Index(root);
        var found = -1;

        for (var at = 0; at < places.Count; at++)
        {
            if (places[at].Offset != offset) continue;
            if (found < 0 || outermost) found = at;
        }

        return found;
    }

    /// <summary>
    /// Which stop a press means: the one at the offset it landed on, nearest by column. -1 when there is
    /// nowhere to stand at all.
    /// <para>
    /// One offset can be several stops on the page — after a note and before the next are the same character
    /// boundary drawn a hand's width apart — so the press also has to say which of them it meant.
    /// </para>
    /// </summary>
    public static int StopNear(this Piece root, Point point)
    {
        var places = Index(root);
        var offset = root.OffsetAt(point);
        var best = -1;

        for (var at = 0; at < places.Count; at++)
        {
            if (places[at].Offset != offset) continue;
            if (best < 0) { best = at; continue; }

            if (Math.Abs(places[at].CaretRect().X - point.X) < Math.Abs(places[best].CaretRect().X - point.X) - Hair)
                best = at;
        }

        return best;
    }

    /// <summary>Everywhere a caret may rest, from the tree that worked it out — see <see cref="LayoutTree.Places"/>.</summary>
    private static IReadOnlyList<CaretPlace> Index(Piece root) => root.Tree?.Places ?? [];

    /// <summary>
    /// Where to draw the caret at an offset, whether or not anything stands there. Beside the nearest
    /// drawing when nothing does, rather than at the origin — which is the ordinary case straight after an
    /// edit, where the caret lands wherever the text was cut.
    /// </summary>
    public static Rect CaretRect(this Piece root, int offset)
    {
        if (root.StopAt(offset) is >= 0 and var stop) return Index(root)[stop].CaretRect();

        Piece before = default, after = default;
        foreach (var piece in root.Leaves())
        {
            var at = piece.Sits();
            if (at.End <= offset && (!before.Exists || at.End > before.Sits().End)) before = piece;
            if (at.Start >= offset && (!after.Exists || at.Start < after.Sits().Start)) after = piece;
        }

        if (before.Exists) return Bar(before, trailing: true);
        if (after.Exists) return Bar(after, trailing: false);

        var whole = root.Bounds;
        return whole.IsEmpty
            ? new Rect(0, 0, 0, 1)
            : new Rect(whole.X, whole.Y, 0, Math.Max(whole.Height, 1));
    }

    private static Rect Bar(Piece against, bool trailing)
    {
        var where = against.Bounds;
        if (where.IsEmpty) return new Rect(0, 0, 0, 1);

        return new Rect(trailing ? where.Right : where.X, where.Y, 0, Math.Max(where.Height, 1));
    }

    /// <summary>
    /// Where a caret may rest, ascending — the offsets of the places, with the several at one offset counted
    /// once. What snapping an arbitrary offset onto the content works from.
    /// </summary>
    public static IReadOnlyList<int> CaretStops(this Piece root)
    {
        var stops = new List<int>();

        foreach (var place in Index(root))
            if (stops.Count == 0 || stops[^1] != place.Offset) stops.Add(place.Offset);

        return stops;
    }



    /// <summary>
    /// The first stop past an offset, or -1 at the edge — which is the host's cue to move the caret out of
    /// this content and into whatever surrounds it.
    ///
    /// <para>
    /// For a caret standing where no stop is, which a stretch shown as its own characters leaves behind: the
    /// reader arrows through it a character at a time and then has to rejoin the places the builder declared.
    /// A caret that is already at one steps by index instead, and needs none of this.
    /// </para>
    /// </summary>
    public static int StopPast(this Piece root, int offset, bool forward)
    {
        var places = Index(root);

        if (forward)
        {
            for (var at = 0; at < places.Count; at++)
                if (places[at].Offset > offset) return at;

            return -1;
        }

        for (var at = places.Count - 1; at >= 0; at--)
            if (places[at].Offset < offset) return at;

        return -1;
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
        // Whatever the caret is actually standing against here, innermost — which is the first place at the
        // offset, the order the index is already in.
        var stop = root.StopAt(offset);
                var from = stop < 0 ? default : Index(root)[stop].Against;
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
