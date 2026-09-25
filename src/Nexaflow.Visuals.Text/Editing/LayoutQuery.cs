using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using Nexaflow.Markdown.Ast;
using System.Windows.Media;

namespace Nexaflow.Visuals.Text.Editing;

/// <summary>
/// What a pointer, a caret and a selection mean, answered by descending the layout tree rather than
/// inferring it from a flattened list of rectangles and source ranges (the old source of a caret that
/// jumped rows, a selection that closed a brace it never opened). Pure arithmetic — <see cref="Rect"/>
/// and <see cref="Point"/> need no STA thread, fonts or desktop, so this is testable against built trees
/// alone. Anything visiting every piece descends with <see cref="Piece.Placed"/> rather than reading
/// <see cref="Piece.Bounds"/> in a loop, since geometry is relative and a climb-to-root per piece is free
/// on the way down but costly done after the fact.
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
    /// Whether a press at <paramref name="point"/> would be writing — on something a caret stands in, or in the gap between
    /// two letters of a line — rather than on drawing nobody types into, or on empty space. Answers for what is under the
    /// point, not what a press would reach for: empty space still finds the nearest thing, but a pointer there should not
    /// say the space itself is writable. The gap tolerance keeps a pointer moving along tightly-set formula letters from
    /// flickering to an arrow between every pair.
    /// </summary>
    /// <param name="reach">The least distance round something written that still counts as writing.</param>
    public static bool Writable(this Piece root, Point point, double reach)
    {
        var on = false;
        var near = false;

        // Inside a card another language drew on, only what is on the card says whether the pointer is over writing: the page
        // round it has nothing to say about a diagram's empty corner.
        var card = Card(root, point);

        foreach (var (piece, where) in root.Placed())
        {
            if (where.Width <= 0 || where.Height <= 0) continue;

            // Inside a construct that is itself somewhere to write — a formula, a fraction between its bar and what is over it —
            // is inside the writing. A diagram is not one: its lines are, and the card they are drawn on takes no caret.
            if (!piece.IsLeaf)
            {
                near |= piece.Part is { Length: > 0 } && piece.Stops != Stops.None && (!card.Exists || Within(piece, card))
                        && point.X >= where.Left - reach && point.X <= where.Right + reach
                        && point.Y >= where.Top && point.Y <= where.Bottom;
                continue;
            }

            // Written in when it takes a caret itself and so does what it stands for: a slice's swatch stands for its row, which
            // takes none, and a hole stands for nothing and is exactly where the caret goes.
            var resolved = piece.Stands() ? piece : piece.Selectable();
            var writes = piece.Stops != Stops.None && resolved is { Exists: true, Stops: not Stops.None };

            if (Contains(where, point) && Inside(piece, point))
            {
                if (writes) return true;
                on = true;
            }
            else if (writes)
            {
                // Half a letter's height round it, which is the line it stands on: the letters of a formula are boxes of
                // different heights set a hair apart, and the space over a short one is still the line.
                var around = Math.Max(reach, where.Height / 2);
                near |= point.X >= where.Left - around && point.X <= where.Right + around
                        && point.Y >= where.Top - around && point.Y <= where.Bottom + around;
            }
        }

        return near && !on;
    }

    /// <summary>The innermost card under a point: a piece standing for something written that takes no caret itself.</summary>
    private static Piece Card(Piece root, Point point)
    {
        var card = default(Piece);

        foreach (var (piece, where) in root.Placed())
            if (!piece.IsLeaf && piece.Stops == Stops.None && piece.Part is { Length: > 0 } && where.Contains(point))
                card = piece;

        return card;
    }

    /// <summary>Whether a piece is the card or inside it.</summary>
    private static bool Within(Piece piece, Piece card)
    {
        for (var at = piece; at.Exists; at = at.Parent)
            if (at == card) return true;

        return false;
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

        // Inside a run of text the press means the letter it landed on rather than the nearer end of the whole run.
        if (piece is { Words: { Maps: true } words, Part: { } inside })
            return inside.Start + words.IndexAt(Local(piece, point));

        var at = piece.Sits();
        if (at.Length <= 0) return at.Start;

        var where = piece.Bounds;
        return point.X <= where.X + (where.Width / 2) ? at.Start : at.End;
    }

    /// <summary>
    /// The drawn thing under the point. Only leaves are candidates, since blank space inside a container
    /// belongs to nobody — a press in the gap between two terms should not fall back to the start of the
    /// whole line. Containers are never used to narrow the search: a container's extent is a typesetting
    /// measurement (the height/depth it reserves on its line), not a bounding box of what it draws, so a
    /// subscript below its operator can sit outside the very piece that holds it.
    /// </summary>
    private static Piece Deepest(Piece root, Point point)
    {
        var best = default(Piece);
        var bestRank = (Named: -1, Depth: -1);

        foreach (var (piece, where) in root.Placed())
        {
            if (piece.Children.Count > 0) continue;              // draws nothing itself
            if (where.Width <= 0 || where.Height <= 0) continue; // spacing
            if (!Contains(where, point) || !Inside(piece, point)) continue;

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
    /// The first thing a selection could hold, climbing from here: this piece if the source named it,
    /// otherwise the nearest ancestor it did. What a reader points at (a glyph, a stem) usually names
    /// nothing on its own; what they mean is the smallest written thing it is part of.
    /// </summary>
    public static Piece Selectable(this Piece piece) =>
        piece.Part is { Length: > 0 } ? piece : NamedAncestor(piece);

    /// <summary>
    /// The run of text a source offset falls in, or nothing — what makes a caret between two letters of a label
    /// possible without a piece per letter. See <see cref="LayoutWords"/>.
    /// </summary>
    public static Piece WordsAt(this Piece root, int offset)
    {
        foreach (var piece in root.SelfAndDescendants())
            if (piece is { Words.Maps: true } && piece.Part is { Length: > 0 } part
                && offset >= part.Start && offset <= part.End())
                return piece;

        return default;
    }

    /// <summary>A rectangle in a piece's own frame, moved to where that piece sits on the page.</summary>
    private static Rect Shift(Rect rect, Vector by) =>
        rect.IsEmpty ? rect : new Rect(rect.X + by.X, rect.Y + by.Y, rect.Width, rect.Height);

    /// <summary>
    /// A point of the content as a whole, in one piece's own frame: past its anchor, and back through however it is turned.
    /// </summary>
    private static Point Local(Piece piece, Point point)
    {
        var at = new Point(point.X - piece.Anchor.X, point.Y - piece.Anchor.Y);
        return piece.Turned?.Inverse is { } back ? back.Transform(at) : at;
    }

    /// <summary>A rectangle of a piece's own frame, where it is drawn: turned as the piece is, then past its anchor.</summary>
    private static Rect Page(Piece piece, Rect rect) =>
        Shift(piece.Turned is { } turn && !rect.IsEmpty ? turn.TransformBounds(rect) : rect, piece.Anchor);

    /// <summary>
    /// One step from here along an axis — the next thing to select when a selection grows that way, or
    /// nothing when there is nothing that way. Taken on the run that orders this piece (<see cref="Piece.Along"/>)
    /// when it's on one; a piece on no run steps on the tree that draws it instead (sideways to the next
    /// sibling, vertically to the nearest thing on the row above/below) — a run only overrides what the
    /// drawing would otherwise say, e.g. that a syllable belongs with its verse rather than the note above it.
    /// Deliberately one step and no more, so nothing has to hold what a run <em>is</em> — a lyric can carry
    /// on across systems, a maths block stop at its own edge, a diagram stay inside its subtree.
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
    /// The ink nearest the point, for a press that landed on nothing: between two note heads, in the air over
    /// a bar, past the last note on a line. Something holding selectable things of its own (a beamed group) is
    /// the last resort, not the first — its rectangle covers the notes it joins, so a press between two heads
    /// would otherwise tie with the group and dragging along a run flickered between note and group at every
    /// gap. A group is gotten by covering it, not by aiming between its members. Ties among equals go to the
    /// deeper piece, so a press inside a bar means the note it landed on rather than the bar around it.
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

            var distance = piece.Region is null ? DistanceTo(where, point) : DistanceTo(piece, point);
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

    /// <summary>Every piece of ink the rectangle touches — the shape it stands in, for a piece that stands in one.</summary>
    public static IReadOnlyList<Piece> PiecesIn(this Piece root, Rect area) =>
        [.. root.Placed().Where(p => p.Piece.IsLeaf && p.Where.IntersectsWith(area) && Touches(p.Piece, area)).Select(p => p.Piece)];

    // ── Selection ───────────────────────────────────────────────────────────

    /// <summary>
    /// Grows a set of pieces to the largest whole constructs it covers: wherever every piece of ink under one
    /// is selected, that one is selected instead. This is where well-formedness comes from — a piece's source
    /// range is what the parser built it from, so a selection can be a fraction or a matrix row but never a
    /// numerator plus a stray closing brace.
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
    /// Works out everywhere a caret may rest, in the order the right arrow visits them. Asked once per tree
    /// (<see cref="LayoutTree.Places"/> keeps the answer). Read off what the builders declared and nothing
    /// else — a piece says which of its edges a caret may stand against (<see cref="Piece.Stops"/>), so a
    /// script finishing where its term finishes gets both a raised half-height place and a back-on-the-line
    /// one; that's why a place is a piece rather than a bare offset. Ordered innermost-first at each offset,
    /// trailing edges before leading ones, with places that would draw as the same mark collapsed to one —
    /// so the reader never presses an arrow twice for a caret that doesn't appear to move.
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
    /// Two rules for two different causes of two places at one offset: trailing edges nest (a script inside
    /// a term inside a row all finish at the same character, and height is what tells them apart, so it must
    /// match exactly), while a leading edge is a boundary between siblings where only the column matters —
    /// without the split, <c>12g</c> would offer two overlapping places between the 2 and the g, since the g
    /// has a descender.
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
        // A caret inside a run of text stands between two of its letters, which only the run can say.
        if (root.WordsAt(offset) is { Words: { } words, Part: { } written } run)
            return Page(run, words.Caret(offset - written.Start));

        if (root.StopAt(offset) is >= 0 and var stop) return Index(root)[stop].CaretRect();

        // Only what a caret may rest against. A wedge of a pie is drawn from a whole line of source and takes no
        // caret, so a bar drawn against one would be as tall as the chart.
        Piece before = default, after = default;
        foreach (var piece in root.Leaves())
        {
            if (piece.Stops == Stops.None) continue;

            var at = piece.Sits();
            if (at.End <= offset && (!before.Exists || at.End > before.Sits().End)) before = piece;
            if (at.Start >= offset && (!after.Exists || at.Start < after.Sits().Start)) after = piece;
        }

        if (before.Exists) return Bar(before, trailing: true);
        if (after.Exists) return Bar(after, trailing: false);

        // Nothing in it can be written in, so there is no caret to draw rather than one as tall as whatever is there.
        return Rect.Empty;
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
    /// The first stop past an offset, or -1 at the edge — the host's cue to move the caret out of this content
    /// and into whatever surrounds it. For a caret standing where no stop is (left behind by a stretch shown as
    /// its own characters): it arrows through a character at a time and then has to rejoin the declared places.
    /// A caret already at one steps by index instead, and needs none of this.
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
    /// The caret stop on the row above or below — how it crosses a fraction bar or leaves a script. Structural,
    /// not geometric: by pixels alone a <c>+</c> beside a fraction starts fractionally lower than the numerator
    /// and would beat the denominator the reader meant, so this walks up to the nearest ancestor with rows and
    /// steps within it instead.
    /// </summary>
    public static int? StepVertical(this Piece root, int offset, bool up)
    {
        // Whatever the caret is actually standing against here, innermost — which is the first place at the offset, the
        // order the index is already in. Between two letters of a run there is no place, and the run is what it stands in.
        var stop = root.StopAt(offset);
        var from = stop < 0 ? root.WordsAt(offset) : Index(root)[stop].Against;
        if (!from.Exists) return null;
        var fromX = root.CaretRect(offset).X;

        foreach (var ancestor in from.Ancestors())
        {
            var rows = ancestor.Rows();
            if (rows.Count < 2) continue;

            var mine = rows.FindIndex(r => r.Any(p => p.SelfAndDescendants().Contains(from)));
            if (mine < 0) continue;

            // Walk outwards past any row with nowhere in it to stand. A fraction lays out as numerator, bar, denominator —
            // three rows — and the bar is not somewhere a caret can stand, so down from the numerator has to mean the
            // denominator; a pie's wedges are a row between its title and a legend set under them.
            var step = up ? -1 : 1;
            for (var target = mine + step; target >= 0 && target < rows.Count; target += step)
                if (Landing(rows[target], ancestor, fromX, up) is { } landing) return landing;
        }

        return null;
    }

    /// <summary>
    /// Where a caret moving up or down into <paramref name="row"/> lands: on the line of it nearest the one it left, at the
    /// place on that line nearest the column it was in — between two letters, where that is inside a run of text. Null
    /// where the row has nowhere to stand.
    /// </summary>
    /// <param name="within">What the row is a row of. Something standing for the whole of it is no place inside it.</param>
    private static int? Landing(List<Piece> row, Piece within, double column, bool up)
    {
        var standing = row
            .SelectMany(piece => piece.Leaves())
            .Where(piece => piece.Stands() && piece.Stops != Stops.None)
            .Where(piece => within.Sits().Length == 0 || piece.Sits().Length < within.Sits().Length)
            .ToList();

        if (standing.Count == 0) return null;

        // A row can hold several lines — a legend beside a chart is one row of the diagram — and moving down means the
        // first of them, not whichever comes nearest the column.
        var nearest = up ? standing.MaxBy(piece => piece.Bounds.Bottom) : standing.MinBy(piece => piece.Bounds.Top);
        var line = standing.Where(piece => piece.Bounds.Top < nearest.Bounds.Bottom - Hair
                                           && nearest.Bounds.Top < piece.Bounds.Bottom - Hair);

        // Both ends of each landing candidate are on offer, and the nearest to where the caret already stands wins — moving
        // down a line keeps your place across it, so a caret after the numerator arrives after the denominator rather than
        // jumping in front of it. A run offers the place between its letters under the column.
        return line
            .SelectMany(piece => piece is { Words: { Maps: true } words, Part: { } part }
                ? new[] { (Offset: part.Start + words.IndexAt(column - piece.Anchor.X),
                           X: Math.Clamp(column, piece.Bounds.Left, piece.Bounds.Right)) }
                : new[] { (Offset: piece.Sits().Start, X: piece.Bounds.X), (Offset: piece.Sits().End, X: piece.Bounds.Right) })
            .OrderBy(place => Math.Abs(place.X - column))
            .ThenBy(place => place.Offset)
            .Select(place => (int?)place.Offset)
            .FirstOrDefault();
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

    /// <summary>
    /// Whether a press lands in what a piece stands in, once its box has said it might: the shape it occupies, for a piece
    /// that occupies one, and otherwise the box that already said yes.
    /// </summary>
    private static bool Inside(Piece piece, Point point) =>
        piece.Region is not { } region || region.FillContains(Local(piece, point));

    /// <summary>Whether a rectangle reaches what a piece stands in — see <see cref="Inside"/>.</summary>
    private static bool Touches(Piece piece, Rect area) =>
        OnPage(piece) is not { } region || region.FillContainsWithDetail(new RectangleGeometry(area)) != IntersectionDetail.Empty;

    /// <summary>The shape a piece stands in, where it sits on the page, or null for a piece that stands in its box.</summary>
    private static Geometry? OnPage(Piece piece)
    {
        if (piece.Region is not { } region) return null;

        var anchor = piece.Anchor;
        if (anchor.X == 0 && anchor.Y == 0) return region;

        // Grouped rather than given a transform of its own, so a transform the shape already carries still applies.
        var placed = new GeometryGroup { Transform = new TranslateTransform(anchor.X, anchor.Y) };
        placed.Children.Add(region);
        placed.Freeze();
        return placed;
    }

    /// <summary>
    /// How far a point is from the shape a piece stands in, squared like the distance to a box: nought inside it, and
    /// otherwise the way to the nearest edge of its outline.
    /// </summary>
    private static double DistanceTo(Piece piece, Point point)
    {
        var region = piece.Region!;
        var local = point - piece.Anchor;
        if (region.FillContains(local)) return 0;

        var nearest = double.MaxValue;
        foreach (var figure in region.GetFlattenedPathGeometry().Figures)
        {
            var from = figure.StartPoint;
            foreach (var segment in figure.Segments)
            {
                IEnumerable<Point> corners = segment switch
                {
                    LineSegment line => [line.Point],
                    PolyLineSegment poly => poly.Points,
                    _ => [],
                };

                foreach (var to in corners)
                {
                    nearest = Math.Min(nearest, ToSegment(from, to, local));
                    from = to;
                }
            }

            if (figure.IsClosed) nearest = Math.Min(nearest, ToSegment(from, figure.StartPoint, local));
        }

        return nearest;
    }

    /// <summary>How far <paramref name="point"/> is from the segment between two points, squared.</summary>
    private static double ToSegment(Point from, Point to, Point point)
    {
        var along = to - from;
        var length = along.LengthSquared;
        var t = length == 0 ? 0 : Math.Clamp(Vector.Multiply(point - from, along) / length, 0, 1);
        return (point - (from + (t * along))).LengthSquared;
    }

    private static double DistanceTo(Rect rect, Point point)
    {
        var dx = Math.Max(Math.Max(rect.X - point.X, point.X - rect.Right), 0);
        var dy = Math.Max(Math.Max(rect.Y - point.Y, point.Y - rect.Bottom), 0);
        return dx * dx + dy * dy;
    }

    // ── What a source range covers ──────────────────────────────────────────

    /// <summary>
    /// What washing a stretch of source covers: what each whole thing inside it drew (its ink plus the room it
    /// stands in, e.g. a note plus the staff line under it — ink alone would leave the staff showing over a low
    /// note), one rectangle per thing; <see cref="Wash"/> joins them across the gaps. Only the ink of what was
    /// chosen: a leaf washes only when what it belongs to is inside the stretch, so words hanging off a bar
    /// don't wash just because the bar's notes are chosen. Asks whether a piece <em>stands</em> for a place
    /// rather than whether it covers characters, since a hole covers none by definition — otherwise a selection
    /// across a half-written fraction would wash everything except the part still missing.
    /// </summary>
    public static IReadOnlyList<Rect> RangeRects(this Piece root, int start, int length)
    {
        if (length <= 0 || !root.Exists) return [];
        var covered = Covered(root, start, length);

        var whole = covered.Where(piece => !piece.Ancestors().Any(covered.Contains))
                           .Select(piece => InkFor(piece, covered))
                           .Where(ink => !ink.IsEmpty);

        // A run of text is one piece, so a stretch that takes only part of one is measured inside it.
        var part = root.SelfAndDescendants()
            .Where(piece => piece is { Words.Maps: true } && piece.Part is { Length: > 0 } written
                            && written.Start < start + length && written.End() > start
                            && (written.Start < start || written.End() > start + length))
            .Select(piece => Page(piece, piece.Words!.Covers(start - piece.Part!.Start, start + length - piece.Part!.Start)))
            .Where(rect => !rect.IsEmpty);

        return [.. whole.Concat(part)];
    }

    /// <summary>
    /// The shapes a stretch of source washes: every leaf standing in a shape of its own (see
    /// <see cref="LayoutBuilder.Occupies"/>) that belongs to something inside the stretch, where it sits on the page. What
    /// <see cref="RangeRects"/> leaves out, so the two together are the whole of it.
    /// </summary>
    public static IReadOnlyList<Geometry> RangeRegions(this Piece root, int start, int length)
    {
        if (length <= 0 || !root.Exists) return [];

        var covered = Covered(root, start, length);

        return [.. root.Leaves()
                       .Where(leaf => leaf.Region is not null && covered.Contains(leaf.Selectable()))
                       .Select(leaf => OnPage(leaf)!)];
    }

    /// <summary>Every piece standing for a place wholly inside a stretch of source.</summary>
    private static HashSet<Piece> Covered(Piece root, int start, int length)
    {
        var end = start + length;
        return root.SelfAndDescendants()
            .Where(piece => piece.Stands() && piece.Sits().Start >= start && piece.Sits().End <= end)
            .ToHashSet();
    }

    /// <summary>
    /// What <paramref name="piece"/> drew for the things in <paramref name="chosen"/>, and the room it stands in —
    /// its own box when it drew nothing at all. See <see cref="RangeRects"/>.
    /// </summary>
    private static Rect InkFor(Piece piece, HashSet<Piece> chosen)
    {
        var ink = Rect.Empty;
        var drew = false;

        foreach (var leaf in piece.Leaves())
        {
            var where = leaf.Bounds;
            if (where.IsEmpty || where.Width <= 0 || where.Height <= 0) continue;

            drew = true;

            // A shape is washed as itself — see RangeRegions — so its box stays out of the rectangle.
            if (leaf.Region is not null) continue;
            if (chosen.Contains(leaf.Selectable())) ink.Union(where);
        }

        if (!drew) return piece.Bounds;
        if (ink.IsEmpty) return ink;

        ink.Union(piece.Bounds);
        return ink;
    }

    /// <summary>
    /// What a piece drew, as one rectangle: its leaves together, whatever room it reserves. A note's stem and a
    /// fraction's bar are in it; the height of the staff the note stands on is not.
    /// </summary>
    public static Rect Ink(this Piece piece)
    {
        var ink = Rect.Empty;
        foreach (var leaf in piece.Leaves())
        {
            var where = leaf.Bounds;
            if (!where.IsEmpty && where.Width > 0 && where.Height > 0) ink.Union(where);
        }

        return ink.IsEmpty ? piece.Bounds : ink;
    }

    /// <summary>
    /// Rectangles that touch once each has grown by <paramref name="reach"/>, joined into one and returned
    /// grown — so letters set against each other become a single run and two notes with air between them stay
    /// two washes, rather than joining by shared vertical band (which washed a whole staff top to bottom).
    /// </summary>
    public static List<Rect> Clusters(IEnumerable<Rect> rects, double reach)
    {
        var grown = new List<Rect>();
        foreach (var rect in rects)
        {
            if (rect.IsEmpty || rect.Width <= 0 || rect.Height <= 0) continue;

            var wider = rect;
            wider.Inflate(reach, reach);
            grown.Add(wider);
        }

        var merged = true;
        while (merged)
        {
            merged = false;
            for (var i = 0; i < grown.Count && !merged; i++)
                for (var j = i + 1; j < grown.Count && !merged; j++)
                {
                    if (!grown[i].IntersectsWith(grown[j])) continue;

                    var joined = grown[i];
                    joined.Union(grown[j]);
                    grown[i] = joined;
                    grown.RemoveAt(j);
                    merged = true;
                }
        }

        return grown;
    }

    /// <summary>
    /// The wash over a selection, as one shape: what each stretch drew, gathered where it touches and joined
    /// across the gaps between (along each row, and down between rows), so a selection reads as one continuous
    /// shape rather than a scatter of boxes while still following what it holds rather than boxing the lot. A
    /// gap is spanned only when nothing a reader could pick lies in it unchosen — otherwise, e.g., words sung
    /// under a line of music would be claimed by washing across to a neighbouring row nobody selected. Joins
    /// are made between things that face each other with nothing else of the wash between and are square, so a
    /// taller side stands up out of the band the way a capital does from a line of text.
    /// </summary>
    public static Geometry Wash(this Piece root, IReadOnlyList<EditRange> selection, double pad)
    {
        var patches = Clusters(selection.SelectMany(range => root.RangeRects(range.Start, range.Length)), pad);

        // Along each row: every patch joins each one on its right that it faces, and what that links up is a row.
        var row = Enumerable.Range(0, patches.Count).ToArray();
        var pieces = new List<Rect>(patches);
        var owner = new List<int>(Enumerable.Range(0, patches.Count));

        foreach (var (a, b, join) in Joins(patches, down: false))
        {
            if (!Clear(root, selection, Narrowed(join, pad, down: false))) continue;

            pieces.Add(join);
            owner.Add(a);
            row[Row(row, a)] = Row(row, b);
        }

        // Down the page, between everything the rows now hold — their joins as well, so the space between two rows
        // is filled wherever both have wash rather than only under each thing in them. Two different rows are
        // joined wholly or not at all: a word left unchosen between them stops every span, or a strip would be
        // washed down through the gap between two of the words.
        var spans = Joins(pieces, down: true)
            .Select(span => (Rows: (Upper: Row(row, owner[span.A]), Lower: Row(row, owner[span.B])), span.Join,
                             Clear: Clear(root, selection, Narrowed(span.Join, pad, down: true))))
            .ToList();
        var blocked = spans.Where(span => !span.Clear).Select(span => span.Rows).ToHashSet();

        var joined = spans
            .Where(span => span.Clear && (span.Rows.Upper == span.Rows.Lower || !blocked.Contains(span.Rows)))
            .Select(span => span.Join);

        var shape = new StreamGeometry { FillRule = FillRule.Nonzero };
        using (var pen = shape.Open())
            foreach (var rect in pieces.Concat(joined))
            {
                pen.BeginFigure(rect.TopLeft, isFilled: true, isClosed: true);
                pen.PolyLineTo([rect.TopRight, rect.BottomRight, rect.BottomLeft], isStroked: false, isSmoothJoin: false);
            }

        shape.Freeze();

        // What stands in a shape of its own is washed as that shape, grown by the pad the way a rectangle is.
        var regions = selection.SelectMany(range => root.RangeRegions(range.Start, range.Length)).ToList();
        if (regions.Count == 0) return shape;

        var grow = new Pen(Brushes.Black, 2 * pad);
        Geometry washed = shape;
        foreach (var region in regions)
        {
            var grown = Geometry.Combine(region, region.GetWidenedPathGeometry(grow), GeometryCombineMode.Union, null);
            washed = Geometry.Combine(washed, grown, GeometryCombineMode.Union, null);
        }

        washed.Freeze();
        return washed;
    }

    /// <summary>
    /// Every join between two of <paramref name="rects"/> that face each other with nothing else of the wash
    /// between: side by side and sharing at least half the height of the shorter — so a chord name that only
    /// grazes the top of a stem is not on the note's row — or one above the other and sharing any width.
    /// </summary>
    private static IEnumerable<(int A, int B, Rect Join)> Joins(List<Rect> rects, bool down)
    {
        for (var a = 0; a < rects.Count; a++)
            for (var b = 0; b < rects.Count; b++)
            {
                var (one, other) = (rects[a], rects[b]);
                var gap = down ? other.Top - one.Bottom : other.Left - one.Right;
                if (gap <= 0) continue;

                var from = down ? Math.Max(one.Left, other.Left) : Math.Max(one.Top, other.Top);
                var to = down ? Math.Min(one.Right, other.Right) : Math.Min(one.Bottom, other.Bottom);
                if (to <= from) continue;
                if (!down && to - from < Math.Min(one.Height, other.Height) / 2) continue;

                var join = down ? new Rect(from, one.Bottom, to - from, gap) : new Rect(one.Right, from, gap, to - from);
                if (rects.Any(rect => Overlaps(rect, join))) continue;

                yield return (a, b, join);
            }
    }

    /// <summary>Whether two rectangles share some area — touching along an edge is not overlapping.</summary>
    private static bool Overlaps(Rect one, Rect other) =>
        one.Left < other.Right && one.Right > other.Left && one.Top < other.Bottom && one.Bottom > other.Top;

    /// <summary>
    /// A join without the pad along its two long sides: what lies between the things it joins rather than beside
    /// them. Verses of words are set as tightly as their letters allow, so the pad reaches into the verse below,
    /// and a join that kept it read a word of that verse as lying between two words of this one.
    /// </summary>
    private static Rect Narrowed(Rect join, double pad, bool down) =>
        down
            ? new Rect(join.X + pad, join.Y, Math.Max(0, join.Width - (2 * pad)), join.Height)
            : new Rect(join.X, join.Y + pad, join.Width, Math.Max(0, join.Height - (2 * pad)));

    /// <summary>Which row a patch has been joined into: the one its joins lead back to.</summary>
    private static int Row(int[] row, int at)
    {
        while (row[at] != at) at = row[at] = row[row[at]];
        return at;
    }

    /// <summary>Whether nothing a reader could pick lies in <paramref name="gap"/> without being chosen.</summary>
    private static bool Clear(Piece root, IReadOnlyList<EditRange> selection, Rect gap)
    {
        if (gap.Width <= 0 || gap.Height <= 0) return true;

        foreach (var (piece, where) in root.Placed())
        {
            if (!piece.IsLeaf || where.Width <= 0 || where.Height <= 0) continue;
            if (where.Right <= gap.Left || where.Left >= gap.Right || where.Bottom <= gap.Top || where.Top >= gap.Bottom) continue;

            var named = piece.Selectable();
            if (!named.Exists) continue;

            // Chosen, or holding something that is: a bar partly taken, whose slur arcs across the gap between two of
            // its notes, is around the selection rather than lying between its parts.
            var at = named.Sits();
            if (!selection.Any(range => at.Start < range.End && at.End > range.Start)) return false;
        }

        return true;
    }

    /// <summary>
    /// The whole things a raw range covers, as a source range: dragging across <c>x^2</c> selects the script
    /// rather than stopping mid-command at <c>x^{2</c>. Falls out of promotion — the answer is made of whole
    /// pieces, so it can never be a half-open brace, and the same call snaps a drag over half a beam group to
    /// the group without knowing what a brace or a beam is.
    /// </summary>
    public static (int Start, int Length) Snap(this Piece root, int start, int length)
    {
        var whole = root.Sits();
        var from = Math.Clamp(Math.Min(start, start + length), whole.Start, whole.End);
        var to = Math.Clamp(Math.Max(start, start + length), whole.Start, whole.End);
        if (from == to) return (from, 0);

        // A stretch inside one run of text is characters, and characters are what a run is made of: promoting it to
        // whole pieces would make every drag through a label pick out the whole label.
        if (root.WordsAt(from) is { Part: { } written } && from >= written.Start && to <= written.End())
            return (from, to - from);

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
