using Nexaflow.Markdown.Layout.Prose;

namespace Nexaflow.Markdown.Layout;

/// <summary>
/// What a builder makes: a tree of pieces, how much room it wants, and whatever could not be read.
///
/// <para>
/// <strong>There is one of these, not one per kind of content.</strong> A builder is the last place anything knows what it is
/// drawing; everything after it is the same code whatever was built — the painter, the hit test, the caret, the selection, the host
/// showing it. That is what makes a thing nobody has thought of yet cost a builder and nothing else.
/// </para>
/// <para>
/// The size is stated rather than taken from the root, because how much room content wants is a fact about the content: a typeset
/// formula leaves its spacing out, since a strut is as tall as the line it reserves. Something built from several takes the union.
/// </para>
/// </summary>
/// <param name="Tree">Every piece that was laid out, and what each drew.</param>
/// <param name="Width">How much room across it wants.</param>
/// <param name="Height">How much room down it wants.</param>
/// <param name="Trouble">Whatever could not be read — what the host draws a wave under.</param>
public sealed record PortableLaid(PortableTree Tree, double Width, double Height, IReadOnlyList<PortableDiagnostic> Trouble)
{
    /// <summary>Nothing laid out at all, in <paramref name="medium"/> — what a builder handed content it could make nothing of returns.</summary>
    public static PortableLaid Nothing(ILayoutMedium medium) => new(new PortableBuilder(medium).Seal(), 0, 0, []);

    /// <summary>The whole of it, as a piece — where every query starts.</summary>
    public PortablePiece Root => Tree.Root;

    /// <summary>How much room it wants, across and down.</summary>
    public LayoutSize Size => new(Width, Height);

    /// <summary>Whether anything was laid out.</summary>
    public bool Exists => Tree.Count > 0;

    /// <summary>
    /// Whether anything actually reached the page: a run of words, or a mark of any kind. Not the same question as
    /// <see cref="Exists"/>: a reader handed something it could make no sense of leaves pieces standing for source with nothing set in
    /// them. <strong>Something that drew nothing must never be what a document shows</strong> — whoever asked draws the characters instead.
    /// </summary>
    public bool Draws => _draws ??= Drew();

    private bool? _draws;

    private bool Drew()
    {
        for (var at = 0; at < Tree.Count; at++)
            if (Tree.WordsOf(at) is not null || Tree.MarksOf(at).Length > 0) return true;

        return false;
    }

    /// <summary>Whether this is the source shown as it is written, rather than anything drawn from it.</summary>
    public bool ShowsSource => Root.Kind is PortableText.SourceKind or PortableSourceShown.Block;

    // ── What a pointer means ────────────────────────────────────────────────
    //
    // The questions that need more than the tree: where the content ends, which is what makes a press past the right-hand edge mean
    // the end rather than whatever happens to be drawn nearest to it. Every other question about a pointer is asked of the root.

    /// <summary>Where a caret is allowed to rest, ascending. Worked out once.</summary>
    public IReadOnlyList<int> Stops => _stops ??= Root.CaretStops();

    private IReadOnlyList<int>? _stops;

    /// <summary>Moves <paramref name="offset"/> to the nearest place a caret may rest.</summary>
    public int NearestStop(int offset)
    {
        var stops = Stops;
        if (stops.Count == 0) return 0;

        // The stops are in order, so the nearest is one of the two either side of where the offset would go — the earlier of them
        // where both are as near.
        var (low, high) = (0, stops.Count);
        while (low < high)
        {
            var middle = low + ((high - low) / 2);
            if (stops[middle] < offset) low = middle + 1;
            else high = middle;
        }

        if (low == stops.Count) return stops[^1];
        if (low == 0) return stops[0];

        return offset - stops[low - 1] <= stops[low] - offset ? stops[low - 1] : stops[low];
    }

    /// <summary>
    /// The piece <paramref name="point"/> is on — what a drag is really between. Past either side it is the thing at that end, as it
    /// is in text: the first or last thing a press could mean, not the first or last piece of drawing.
    /// </summary>
    public PortablePiece PieceAt(LayoutPoint point)
    {
        if (point.X > Width) return LastSelectable();
        if (point.X < 0) return Selectables().FirstOrDefault();
        return Root.PieceAt(point);
    }

    /// <summary>Everything a press could mean, in drawing order.</summary>
    private IEnumerable<PortablePiece> Selectables() =>
        Root.Leaves().Select(leaf => leaf.Selectable()).Where(piece => piece.Exists);

    /// <summary>The last thing a press could mean, in drawing order — found from the end.</summary>
    private PortablePiece LastSelectable()
    {
        if (!Exists) return default;

        for (var at = Tree.Piece(0).Extent - 1; at >= 0; at--)
            if (Tree.At(at) is { IsLeaf: true } leaf && leaf.Selectable() is { Exists: true } selectable)
                return selectable;

        return default;
    }

    /// <summary>
    /// The caret stop <paramref name="point"/> means: which piece is under it, and which half of that piece was hit. Clicking past
    /// either side means the end, as it does in text — letting the nearest piece answer would land just inside whatever construct
    /// happens to finish last, which is the same pixel but a different place to type.
    /// </summary>
    public int OffsetAt(LayoutPoint point)
    {
        if (point.X > Width) return Stops.Count > 0 ? Stops[^1] : 0;
        if (point.X < 0) return Stops.Count > 0 ? Stops[0] : 0;

        var hit = Root.PieceAt(point);
        if (!hit.Exists) return 0;

        // Inside a run of text the press means the letter it landed on, and the places between letters are the run's own rather than
        // stops of the layout — so snapping would put the caret back at one end of the run.
        if (hit.Words is { Maps: true }) return Root.OffsetAt(point);

        var offset = point.X < hit.Bounds.X + (hit.Bounds.Width / 2) ? hit.Sits().Start : hit.Sits().End;
        return NearestStop(offset);
    }

    /// <summary>Everywhere a caret may rest, in the order the right arrow visits them. A caret is an index into this.</summary>
    public IReadOnlyList<PortableCaretPlace> Places => Tree.Places;

    /// <summary>Which stop a press means — see <see cref="PortableQuery.StopNear"/>. -1 for nowhere to stand.</summary>
    public int StopNear(LayoutPoint point) => Root.StopNear(point);

    /// <summary>One step along the places, or null at either end.</summary>
    public int? Step(int at, bool forward)
    {
        var to = at + (forward ? 1 : -1);
        return to >= 0 && to < Places.Count ? to : null;
    }

    /// <summary>
    /// Whether this piece was shown rather than understood — inside a stretch the reader gave up on, so it stands for characters
    /// rather than for meaning.
    /// </summary>
    public bool IsGuesswork(PortablePiece piece) => Trouble.Any(trouble => trouble.Covers(piece));

    /// <summary>
    /// The places still waiting to be written in, in reading order — an argument left empty that the builder drew a box for. Empty for
    /// content with no notion of an unfilled argument, which is most of it.
    /// </summary>
    public IReadOnlyList<PortablePiece> Holes =>
        _holes ??= [.. Root.SelfAndDescendants().Where(piece => piece.Part is { Length: 0 }).OrderBy(piece => piece.Sits().Start)];

    private IReadOnlyList<PortablePiece>? _holes;
}
