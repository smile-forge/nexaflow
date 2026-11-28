using System;
using System.Collections.Generic;
using System.Linq;

using Nexaflow.Markdown.Editing;
using Nexaflow.Markdown.Layout;
using Nexaflow.Markdown.Layout.Prose;

namespace Nexaflow.Visuals.Text.Markdown;

/// <summary>
/// What the page shows over the content, decided here and drawn by whatever shows it: where the carets stand, which marks and waves fall
/// near the screen and on which rows, what the pointer is over and what it says there. Each is asked once a paint, or once the pointer
/// moves, for what is near the screen alone — so a long document costs a screen of answers however long it is.
/// </summary>
public sealed partial class ContentEngine
{
    /// <summary>How far past a written run the pointer still counts as inside it — just over half the widest gap on a formula's line, so moving along one never flickers to an arrow between glyphs.</summary>
    private const double PointerReach = 4.0;

    /// <summary>The carets of the last paint, kept so asking for them makes nothing.</summary>
    private readonly List<LayoutRect> _carets = [];

    /// <summary>The rows of the last lines asked for, kept so asking for them makes nothing.</summary>
    private readonly List<(double Top, double Tall)> _rows = [];

    /// <summary>The waves of the last paint, kept so asking for them makes nothing.</summary>
    private readonly List<ShownWave> _waves = [];

    /// <summary>
    /// Whether there is a caret to draw: where something is being carried, the place it would land; otherwise the caret this content
    /// holds, where it is written in — and not while whole things are chosen, a slice or a node, which is no place to write.
    /// </summary>
    public bool ShowsCaret => !_readOnly && (Dropping is not null || (HasCaret && !ChoseWhole));

    /// <summary>
    /// Where the carets stand: the one where typing goes — while something is being carried, where it would land, not where it was picked
    /// up from, that being the one thing the reader needs to see before letting go — and, for a block of columns, one on every line of it.
    /// </summary>
    internal IReadOnlyList<LayoutRect> CaretsShown()
    {
        _carets.Clear();

        var state = this.State;
        var dropping = Dropping;

        _carets.Add(dropping is not null || At < 0 ? _laid.Root.CaretRect(dropping ?? state.Caret) : _laid.Places[At].CaretRect());

        if (state.Selected is ColumnRanges block)
            foreach (var row in block)
            {
                var at = _laid.Root.CaretRect(block.Caret.Column == block.Low ? row.Start : row.End);
                if (!at.IsEmpty) _carets.Add(at);
            }

        return _carets;
    }

    /// <summary>
    /// The stretch of the document drawn round the screen — <paramref name="painted"/>, what is painted round it — which is all a mark is
    /// worked out for: from the first place laid on it to the last place laid on it. Null where nothing has said where the screen is, or where
    /// nothing laid stands on it yet, so nothing is marked until something does. Never the whole document, nor on past the band to whatever is
    /// laid next of one read a stretch at a time — after a jump that can be the end of the file, and a long stretch costs a mark for every line
    /// it holds, at every paint.
    /// </summary>
    internal (int Start, int End)? NearScreen(LayoutRect? painted)
    {
        if (painted is not { } near) return null;

        var from = _laid.Root.StopBelow(near.Y);
        var to = _laid.Root.StopAbove(near.Y + near.Height);

        // Nothing laid stands near the screen — what is there is still being read — so nothing near it is asked about.
        return from < 0 || to < 0 || from > to ? null : (from, to);
    }

    /// <summary>
    /// Where the rows of every line from the one holding <paramref name="from"/> to the one holding <paramref name="to"/> stand — only those
    /// <paramref name="near"/> the screen: each line's top and how tall its rows come to, a line with nothing on it as tall as a caret standing
    /// in it. Read before it is asked again.
    /// </summary>
    internal IReadOnlyList<(double Top, double Tall)> RowsOfLines(int from, int to, (int Start, int End) near)
    {
        _rows.Clear();

        var document = this.State.Document;
        var (low, high) = near;
        if (Math.Max(to, from) < low || from > high) return _rows;

        var (first, last) = (document.LineOf(Math.Max(from, low)), document.LineOf(Math.Min(Math.Max(to, from), high)));

        for (var line = first; line <= last; line++)
        {
            var (start, end) = document.LineAt(document.OffsetOf(line));
            var rects = end > start ? _laid.Root.RangeRects(start, end - start) : [];

            if (rects.Count == 0)
            {
                var caret = _laid.Root.CaretRect(start);
                if (!caret.IsEmpty) _rows.Add((caret.Y, caret.Height));
                continue;
            }

            var (top, bottom) = (double.PositiveInfinity, double.NegativeInfinity);
            foreach (var rect in rects) (top, bottom) = (Math.Min(top, rect.Y), Math.Max(bottom, rect.Y + rect.Height));

            _rows.Add((top, bottom - top));
        }

        return _rows;
    }

    /// <summary>
    /// The marks laid over the view of the document (<see cref="ContentEditor.Marks"/>) for the stretch <paramref name="near"/> the screen, in
    /// the order they are drawn — or null where no layer has anything to mark.
    /// </summary>
    internal IReadOnlyList<ContentMark>? MarksNear((int Start, int End) near)
    {
        if (EditorIfAny?.Marks is not { Count: > 0 } layers) return null;

        var any = false;
        foreach (var layer in layers) any |= !layer.IsEmpty;
        if (!any) return null;

        return layers.Mark(new ContentMarkRequest(this.State.Document, near.Start, near.End) { Syntax = ShownSyntax });
    }

    /// <summary>
    /// The waves under whatever could not be read that stand <paramref name="near"/> the screen — all of them where nothing says what is on
    /// screen — each with the runs it is drawn under. What a wave is under is worked out the first time it comes near the screen, and kept for
    /// the layout it was laid in: a long document is painted again every time it is scrolled, and that is the same until it is laid again.
    /// </summary>
    internal IReadOnlyList<ShownWave> WavesNear(LayoutRect? near)
    {
        _waves.Clear();

        var laid = _laid;
        if (!ReferenceEquals(_squiggledIn, laid)) (_squiggles, _squiggledIn) = (new Squiggled?[laid.Trouble.Count], laid);

        for (var at = 0; at < laid.Trouble.Count; at++)
        {
            var trouble = laid.Trouble[at];

            if (_squiggles[at] is not { } squiggled)
            {
                if (near is { } area && !LayoutQuery.SittingIn(laid.Root, trouble.Start, trouble.Start + trouble.Length, area).Any()) continue;

                _squiggles[at] = squiggled = WaveUnder(laid, trouble);
            }

            if (squiggled.Runs.Count > 0 && (near is not { } painted || painted.IntersectsWith(squiggled.Under)))
                _waves.Add(new ShownWave(trouble.Severity, squiggled.Runs));
        }

        return _waves;
    }

    /// <summary>What <paramref name="trouble"/> blames is drawn as, and the stretch of the page that covers — nothing, where that drew nothing.</summary>
    private static Squiggled WaveUnder(Laid laid, LayoutDiagnostic trouble)
    {
        var runs = LayoutQuery.Clusters(laid.Root.RangeRects(trouble.Start, trouble.Length), 0);
        if (runs.Count == 0) return new Squiggled(LayoutRect.Empty, []);

        var under = runs[0];
        foreach (var run in runs) under = LayoutRect.Union(under, run);

        return new Squiggled(LayoutRect.Inflate(under, 0, 4), runs);
    }

    /// <summary>What each wave the layout in <see cref="_squiggledIn"/> draws is drawn under, by where it stands in its trouble — null for one never yet near the screen.</summary>
    private Squiggled?[] _squiggles = [];
    private Laid? _squiggledIn;

    /// <summary>What a wave is drawn under, kept for the layout: the runs of what could not be read, and the stretch of the page they cover.</summary>
    private readonly record struct Squiggled(LayoutRect Under, IReadOnlyList<LayoutRect> Runs);

    /// <summary>
    /// The outermost pieces of what is being carried, as the content would read once let go — outermost, so a piece and its children are not
    /// drawn twice. What shows the preview draws these again in the accent, since once merged in nothing else would tell them apart.
    /// </summary>
    internal IEnumerable<LayoutPiece> PiecesCarried()
    {
        if (Preview is not { } preview) yield break;

        var (start, end) = (preview.Start, preview.End);
        if (end <= start) yield break;

        var taken = new List<LayoutPiece>();
        foreach (var piece in preview.Laid.Root.SelfAndDescendants())
        {
            if (piece.Sits() is not { Length: > 0 } at || at.Start < start || at.End > end) continue;
            if (taken.Any(already => piece.Ancestors().Contains(already))) continue;

            taken.Add(piece);
            yield return piece;
        }
    }

    /// <summary>
    /// What the pointer is over at <paramref name="at"/>: the shape it takes — what the language says a press there would take hold of, a bar
    /// over what can be written in and an arrow elsewhere — and what the piece there says while it is pointed at: the tip its builder put in the
    /// tree for it, or for what it is drawn in, else all of its words, where they are set cut short to fit. Read off the layout alone.
    /// </summary>
    internal (PointerShape Shape, string? Tip) PointedAt(LayoutPoint at)
    {
        var piece = _laid.Root.PieceAt(at);
        var tip = Says(piece);

        if (!_readOnly && Gripping(piece, at) is { } grip) return (grip, tip);

        // Over words a bar, whether they may be written in or only read: read, they are still picked out from where the caret stands.
        return (_laid.Root.Writable(at, PointerReach) ? PointerShape.Text : PointerShape.Arrow, tip);
    }

    /// <summary>What <paramref name="piece"/> says while pointed at (<see cref="PointedAt"/>).</summary>
    private static string? Says(LayoutPiece piece)
    {
        if (!piece.Exists) return null;

        for (var at = piece; at.Exists; at = at.Parent)
            if (at.Tip is { } tip) return tip;

        // Words set cut short to fit their room still hold all of what they say, so that is what they say when pointed at.
        return piece.Words is { Glyphs: { CutShort: true } cut } ? cut.Text : null;
    }

    /// <summary>Whether <paramref name="at"/> is over the buttons in the corner of the block the pointer is over.</summary>
    internal bool OnCorner(LayoutPoint at) => Corner?.Root.Bounds.Contains(at) == true;

    /// <summary>
    /// Whether the caret belongs in this content at all. Asked of the tree rather than declared, since a piece naming a stretch of source is one
    /// somebody typed — a barcode that only prints a worked-out number has none — and a hole standing where something is still to be written is
    /// somewhere to write too.
    /// </summary>
    internal bool AcceptsCaret =>
        this.State.Document.Length == 0
        || _laid.Root.SelfAndDescendants().Any(piece => piece.Part is { Length: > 0 } || (piece.Part is not null && piece.Kind == LayoutText.HoleKind));

    /// <summary>
    /// The pieces that answer to a gesture and are covered by what is picked out — empty where nothing is, which is what makes "for one item"
    /// and "for a selection of them" the same question asked twice.
    /// </summary>
    internal IReadOnlyList<LayoutPiece> ActsSelected() =>
        [.. _laid.Root.SelfAndDescendants().Where(piece => piece.Acts is not null && piece.Sits() is { Length: > 0 } sits && Covers(sits.Start))];
}

/// <summary>A wave to draw under what could not be read: how bad it is, and the runs on the page it is drawn under.</summary>
internal readonly record struct ShownWave(LayoutSeverity Severity, IReadOnlyList<LayoutRect> Runs);
