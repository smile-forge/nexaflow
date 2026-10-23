using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;

using Nexaflow.Markdown.Ast;
using Nexaflow.Markdown.Prose;
using Nexaflow.Visuals.Text.Editing;
using Nexaflow.Visuals.Text.Markdown.Export;

namespace Nexaflow.Visuals.Text.Markdown.Printing;

/// <summary>
/// A stretch of a document laid out as one long page, from <see cref="Top"/> to <see cref="Bottom"/>, drawn <see cref="Scale"/>
/// times its laid size on the sheet it is on — less than one only where a block in it is wider than the sheet, or one thing
/// is taller than it.
/// </summary>
public readonly record struct PaperBand(double Top, double Bottom, double Scale)
{
    /// <summary>How much of the laid page it holds, top to bottom, at its laid size.</summary>
    public double Height => Bottom - Top;

    /// <summary>How much of the sheet it takes, top to bottom.</summary>
    public double Drawn => Height * Scale;
}

/// <summary>One sheet: the bands of the document it holds, one under the other, top to bottom — one, unless something on it is drawn smaller.</summary>
public sealed record PaperPage(IReadOnlyList<PaperBand> Bands)
{
    /// <summary>Where on the laid page the sheet starts.</summary>
    public double Top => Bands[0].Top;

    /// <summary>Where on the laid page the sheet ends, and the next one starts.</summary>
    public double Bottom => Bands[^1].Bottom;

    /// <summary>How much of the sheet its bands take, top to bottom.</summary>
    public double Drawn => Bands.Sum(band => band.Drawn);
}

/// <summary>
/// Where a document laid out as one long page is cut into sheets, and how large each part of it is drawn on them.
///
/// <para>
/// A sheet ends only where nothing is cut through: between two lines — of prose, of code, of a table's rows — and never
/// through what another language drew, which goes onto the next sheet whole. A heading goes with the line after it, so no
/// sheet ends on one. A ground under a stretch of the page and a line drawn down it are cut, because each reads on as one
/// across the cut; a line drawn across the page is not, because cut it would be two.
/// </para>
/// <para>
/// A block is drawn at one size: its own, unless it is wider than a sheet, when the whole of it is drawn smaller to fit —
/// its columns stay under each other — and the blocks round it keep their size. One thing taller than a sheet is drawn
/// smaller to fill a sheet of its own. The sheets meet: each starts where the one before it ends, so nothing drawn between
/// two of them is lost.
/// </para>
/// </summary>
public static class PaperPages
{
    /// <summary><paramref name="laid"/> cut into sheets of <paramref name="room"/>, in device-independent pixels, in reading order — one at least.</summary>
    public static IReadOnlyList<PaperPage> Cut(Laid laid, Size room)
    {
        if (!(room.Width > 0) || !(room.Height > 0)) throw new ArgumentOutOfRangeException(nameof(room), room, "A sheet has room on it.");

        var stretches = Stretches(Wholes(laid, room.Width));
        var end = Math.Max(laid.Size.Height, stretches.Count > 0 ? stretches[^1].Bottom : 0);
        var pages = new List<PaperPage>();
        var bands = new List<PaperBand>();
        var used = 0.0;
        var from = 0.0;

        for (var at = 0; at < stretches.Count; at++)
        {
            var stretch = stretches[at];
            var drawn = stretch.Drawn(from);

            if (bands.Count > 0 && used + drawn > room.Height + Hair)
            {
                pages.Add(new PaperPage(bands));
                bands = [];
                used = 0;
            }

            // Too tall for a sheet even on one of its own: drawn smaller, to fill one.
            var shrink = drawn > room.Height + Hair ? room.Height / drawn : 1;

            for (var run = 0; run < stretch.Runs.Count; run++)
            {
                var top = run == 0 ? from : stretch.Runs[run].Top;
                var bottom = run + 1 < stretch.Runs.Count ? stretch.Runs[run + 1].Top : stretch.Bottom;

                Band(bands, new PaperBand(top, bottom, stretch.Runs[run].Scale * shrink));
            }

            used += drawn * shrink;

            // The space after it, up to the next, goes with it as far as the sheet has room for.
            var scale = bands[^1].Scale;
            var next = at + 1 < stretches.Count ? stretches[at + 1].Top : end;
            var until = Math.Max(stretch.Bottom, Math.Min(next, stretch.Bottom + ((room.Height - used) / scale)));

            used += (until - stretch.Bottom) * scale;
            Band(bands, new PaperBand(stretch.Bottom, until, scale));
            from = until;
        }

        if (bands.Count == 0) bands.Add(new PaperBand(0, Math.Min(end, room.Height), 1));
        pages.Add(new PaperPage(bands));

        return pages;
    }

    /// <summary>Closer than this and two edges on the page are the one edge.</summary>
    private const double Hair = 0.5;

    /// <summary>Puts <paramref name="band"/> on a sheet: onto the end of the last one where it goes on from it at its size.</summary>
    private static void Band(List<PaperBand> bands, PaperBand band)
    {
        if (band.Height <= 0) return;

        if (bands.Count > 0 && bands[^1] is var last && last.Scale == band.Scale && last.Bottom == band.Top)
            bands[^1] = last with { Bottom = band.Bottom };
        else
            bands.Add(band);
    }

    /// <summary>
    /// A stretch of the page that a sheet holds all of or none of, the size it is drawn at, and whether it goes
    /// <see cref="WithNext"/> — holds the one after it as well: a heading, and the line it heads.
    /// </summary>
    private readonly record struct Whole(double Top, double Bottom, double Scale, bool WithNext);

    /// <summary>
    /// A stretch a sheet can end after and nowhere inside: wholes that overlap, or that a heading holds together. Each block in
    /// it is drawn at its own size, so it is a run of sizes, each from where it starts to where the next one does.
    /// </summary>
    private sealed record Stretch(double Top, double Bottom, bool WithNext, List<(double Top, double Scale)> Runs)
    {
        /// <summary>How much of a sheet it takes, from <paramref name="from"/> — where the sheet stands before it — to its foot.</summary>
        public double Drawn(double from)
        {
            var drawn = 0.0;

            for (var run = 0; run < Runs.Count; run++)
            {
                var top = run == 0 ? from : Runs[run].Top;
                var bottom = run + 1 < Runs.Count ? Runs[run + 1].Top : Bottom;
                drawn += (bottom - top) * Runs[run].Scale;
            }

            return drawn;
        }
    }

    /// <summary>
    /// What has to stay whole, as the layout draws it — what another language drew, each line of words, and each leaf that
    /// draws anything but rules and grounds, and of those each rule drawn across the page — each at the size its block is
    /// drawn at on a sheet <paramref name="width"/> wide.
    /// </summary>
    private static List<Whole> Wholes(Laid laid, double width)
    {
        var found = new List<(Rect Where, int Block, bool WithNext)>();
        var reach = new Dictionary<int, double>();
        var block = -1;
        var passing = int.MaxValue;

        foreach (var (piece, where) in laid.Root.Placed())
        {
            // Inside a drawing already kept whole, nothing of it has a say of its own.
            var depth = piece.Depth;
            if (depth > passing) continue;
            passing = int.MaxValue;

            // Each block the document holds is drawn at one size: as wide as the widest of it allows.
            if (depth <= 1) block = piece.At;
            if (!where.IsEmpty) reach[block] = Math.Max(reach.GetValueOrDefault(block), where.Right);

            if (where.IsEmpty || where.Height <= 0) continue;

            if (piece.Part is ContentPart part && LayoutExport.IsPicture(part, piece))
            {
                found.Add((where, block, false));
                passing = depth;
            }
            else if (piece.Words is not null)
                found.Add((where, block, Heads(piece)));
            else if (piece.IsLeaf)
                foreach (var inked in Inked(piece, where))
                    found.Add((inked, block, Heads(piece)));
        }

        return found.ConvertAll(whole =>
            new Whole(whole.Where.Top, whole.Where.Bottom, Fit(width, reach[whole.Block]), whole.WithNext));
    }

    /// <summary>How much smaller something reaching <paramref name="right"/> is drawn to fit <paramref name="width"/>.</summary>
    private static double Fit(double width, double right) => right > width ? width / right : 1;

    /// <summary>
    /// What of a leaf cannot be cut: all of it where it draws anything but rules and grounds; of a rule, one drawn across the
    /// page, which a cut would make two — not one drawn down it, which reads on as one across the cut; and of a ground,
    /// nothing, since a cut leaves it under both halves.
    /// </summary>
    private static IEnumerable<Rect> Inked(Piece leaf, Rect where)
    {
        var marks = leaf.Marks.ToArray();

        if (marks.Any(mark => mark is not (RuleMark or LineMark or WashMark)))
        {
            yield return where;
            yield break;
        }

        // A mark is drawn in its leaf's own frame, whose nought is where the leaf's box starts short of where it stands.
        var anchor = where.Location - leaf.Box.Location;

        foreach (var mark in marks)
            if (mark is RuleMark or LineMark && mark.Covers is { IsEmpty: false } covers && covers.Width >= covers.Height)
                yield return Rect.Offset(covers, anchor);
    }

    /// <summary>Whether a piece is drawn from a heading, which no sheet ends on.</summary>
    private static bool Heads(Piece piece) =>
        piece.Naming() is ContentPart part
        && (part.Kind == MarkdownKinds.Heading || part.Ancestors().Any(holder => holder.Kind == MarkdownKinds.Heading));

    /// <summary>
    /// The wholes, top to bottom, gathered into the stretches a sheet can end between: wholes that overlap — the cells of one
    /// row, a marker and its item — are one stretch, and so is a heading with whatever comes after it.
    /// </summary>
    private static List<Stretch> Stretches(List<Whole> wholes)
    {
        var stretches = new List<Stretch>();

        foreach (var whole in wholes.OrderBy(whole => whole.Top))
        {
            if (stretches.Count > 0 && stretches[^1] is var last && (last.WithNext || whole.Top < last.Bottom - Hair))
            {
                var overlaps = whole.Top < last.Bottom - Hair;
                var runs = last.Runs;

                // A whole drawn at another size than what it follows starts a run of its own — where it starts, or, laid over
                // the foot of what it follows, where that ends.
                if (runs[^1].Scale != whole.Scale) runs.Add((Math.Max(runs[^1].Top, Math.Max(whole.Top, Math.Min(last.Bottom, whole.Bottom))), whole.Scale));

                stretches[^1] = last with
                {
                    Bottom = Math.Max(last.Bottom, whole.Bottom),
                    WithNext = whole.WithNext || (overlaps && last.WithNext),
                };
            }
            else
                stretches.Add(new Stretch(whole.Top, whole.Bottom, whole.WithNext, [(whole.Top, whole.Scale)]));
        }

        return stretches;
    }
}
