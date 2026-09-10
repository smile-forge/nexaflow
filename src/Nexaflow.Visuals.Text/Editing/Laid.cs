using System.Collections.Generic;
using System.Windows;
using System;
using System.Linq;

namespace Nexaflow.Visuals.Text.Editing;

/// <summary>
/// What a builder makes: a tree of pieces, how much room it wants, and whatever could not be read.
///
/// <para>
/// <strong>There is one of these, not one per kind of content.</strong> A builder is the last place
/// anything knows what it is drawing — it reads a parse tree and lays pieces out — and everything after
/// it is the same code whatever was built: the painter, the hit test, the caret, the selection, the
/// element that hosts it. That is what makes a thing nobody has thought of yet cost a builder and
/// nothing else, and it is why a score, a formula and a barcode no longer have a layout type each.
/// </para>
/// <para>
/// The size is stated rather than taken from the root, because how much room content wants is a fact
/// about the content: a typeset formula leaves its spacing out, since a strut is as tall as the line it
/// reserves and counting it would pad the element with margin nothing is drawn in. Something built from
/// several of these takes the union.
/// </para>
/// </summary>
/// <param name="Tree">Every piece that was laid out, and what each drew.</param>
/// <param name="Size">How much room it wants, in element pixels.</param>
/// <param name="Trouble">
/// Whatever could not be read — what the host draws a wave under. Empty for content that parsed
/// cleanly, which is most of it and none of it while somebody is typing.
/// </param>
public sealed record Laid(LayoutTree Tree, Size Size, IReadOnlyList<Diagnostic> Trouble)
{
    /// <summary>Nothing laid out at all — what a builder handed content it could make nothing of returns.</summary>
    public static Laid Nothing { get; } = new(new LayoutBuilder().Seal(), new Size(0, 0), []);

    /// <summary>The whole of it, as a piece — where every query starts.</summary>
    public Piece Root => Tree.Root;

    /// <summary>Whether anything was laid out.</summary>
    public bool Exists => Tree.Count > 0;

    // ── What a pointer means ────────────────────────────────────────────────
    //
    // The three questions that need more than the tree: where the content ends, which is what makes a press
    // past the right-hand edge mean the end rather than whatever happens to be drawn nearest to it. Every
    // other question about a pointer is asked of the root, through LayoutQuery.

    /// <summary>Where a caret is allowed to rest, ascending. Worked out once.</summary>
    public IReadOnlyList<int> Stops => _stops ??= Root.CaretStops();

    private IReadOnlyList<int>? _stops;

    /// <summary>Moves <paramref name="offset"/> to the nearest place a caret may rest.</summary>
    public int NearestStop(int offset)
    {
        var stops = Stops;
        var best = stops.Count > 0 ? stops[0] : 0;
        var bestDistance = int.MaxValue;

        foreach (var stop in stops)
        {
            var distance = Math.Abs(stop - offset);
            if (distance >= bestDistance) continue;
            bestDistance = distance;
            best = stop;
        }

        return best;
    }

    /// <summary>
    /// The piece <paramref name="point"/> is on — what a drag is really between. Past either side it is the
    /// thing at that end, as it is in text: the first or last thing a press could mean, not the first or last
    /// piece of drawing, which in a score is a staff line nobody can pick.
    /// </summary>
    public Piece PieceAt(Point point)
    {
        if (point.X > Size.Width) return Selectables().LastOrDefault();
        if (point.X < 0) return Selectables().FirstOrDefault();
        return Root.PieceAt(point);
    }

    /// <summary>Everything a press could mean, in drawing order.</summary>
    private IEnumerable<Piece> Selectables() =>
        Root.Leaves().Select(leaf => leaf.Selectable()).Where(piece => piece.Exists);

    /// <summary>
    /// The caret stop <paramref name="point"/> means: which piece is under it, and which half of that piece
    /// was hit.
    ///
    /// <para>
    /// Clicking past either side means the end, as it does in text. Letting the nearest piece answer would
    /// instead land just inside whatever construct happens to finish last — after the y of <c>\sqrt{y}</c>
    /// rather than after the radical — which is the same pixel but a different place to type.
    /// </para>
    /// </summary>
    public int OffsetAt(Point point)
    {
        if (point.X > Size.Width) return Stops.Count > 0 ? Stops[^1] : 0;
        if (point.X < 0) return Stops.Count > 0 ? Stops[0] : 0;

        var hit = Root.PieceAt(point);
        if (!hit.Exists) return 0;

        var offset = point.X < hit.Bounds.X + (hit.Bounds.Width / 2) ? hit.Sits().Start : hit.Sits().End;
        return NearestStop(offset);
    }

    /// <summary>
    /// Everywhere a caret may rest, in the order the right arrow visits them. A caret is an index into this,
    /// so stepping is arithmetic and there is nothing to search.
    /// </summary>
    public IReadOnlyList<CaretPlace> Places => Tree.Places;

    /// <summary>Which stop a press means — see <see cref="LayoutQuery.StopNear"/>. -1 for nowhere to stand.</summary>
    public int StopNear(Point point) => Root.StopNear(point);

    /// <summary>One step along the places, or null at either end.</summary>
    public int? Step(int at, bool forward)
    {
        var to = at + (forward ? 1 : -1);
        return to >= 0 && to < Places.Count ? to : null;
    }



    /// <summary>
    /// Whether this piece was shown rather than understood — inside a stretch the reader gave up on, so it
    /// stands for characters rather than for meaning.
    /// </summary>
    public bool IsGuesswork(Piece piece) => Trouble.Any(trouble => trouble.Covers(piece));

    /// <summary>
    /// Whether this is the source shown as its own characters rather than the content read — see
    /// <see cref="LayoutText.Shown"/>. True for source a builder could not make sense of, and for source
    /// there is none of yet.
    /// </summary>
    public bool ShowsSource => Root.Kind == LayoutText.SourceKind;

    /// <summary>
    /// The places still waiting to be written in, in reading order — an argument left empty that the builder
    /// drew a box for. Empty for content with no notion of an unfilled argument, which is most of it.
    /// </summary>
    public IReadOnlyList<Piece> Holes =>
        _holes ??= [.. Root.SelfAndDescendants().Where(piece => piece.Part is { Length: 0 }).OrderBy(piece => piece.Sits().Start)];

    private IReadOnlyList<Piece>? _holes;
}
