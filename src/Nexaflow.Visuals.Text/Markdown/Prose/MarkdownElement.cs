using System.Windows;
using System.Windows.Input;

using System.Windows.Media;
using Nexaflow.Markdown.Ast;
using Nexaflow.Markdown.Editing;
using Nexaflow.Markdown.Prose;
using Nexaflow.Visuals.Text.Editing;
using Nexaflow.Markdown.Layout;
using Nexaflow.Markdown.Layout.Prose;
using Nexaflow.Visuals.Text.Painting;

namespace Nexaflow.Visuals.Text.Markdown.Prose;

/// <summary>
/// A markdown document, drawn where it was written and written in where it is drawn.
///
/// <para>
/// <strong>Almost nothing is here.</strong> Laying out, painting, the caret, the selection, the pointer, the arrow
/// keys, backspace, dragging a stretch somewhere else, the wash and the wave — all of it is
/// <see cref="ContentElement"/>, and it is the same code a formula, a tune and twenty-six diagrams run. What is left
/// is the one thing only a markdown document knows: what a press on a tick means.
/// </para>
/// <para>
/// <strong>The block answers its own verbs first, and the host is offered what it did not claim.</strong> That is the
/// way round that matters. Ticking an item off is this document's business and needs nobody's help, so a host that
/// wants none writes none; a host with a reason to take it on — a list that lives somewhere other than in the file —
/// says so and is asked.
/// </para>
/// </summary>
/// <param name="host">What the host answers, for the verbs this document does not answer itself.</param>
public sealed class MarkdownElement : LinkedElement
{
    /// <param name="host">What the host answers, for the verbs this document does not answer itself.</param>
    /// <param name="engine">What lays it out — the host's, where the host keeps one for longer than an element lasts.</param>
    /// <param name="language">What it is written in, where that is one language rather than a markdown document.</param>
    public MarkdownElement(string source, LayoutStyle palette, ILayoutActions? host = null, ContentEngine? engine = null,
                           string? language = null)
        : this(engine ?? new ContentEngine(), source, palette, host, language)
    {
    }

    private MarkdownElement(ContentEngine engine, string source, LayoutStyle palette, ILayoutActions? host, string? language)
        : base(source ?? string.Empty, palette, engine, language, host)
    {
    }

    /// <summary>
    /// The markdown this is showing. Setting it re-reads and re-lays, without telling anybody the document
    /// changed — because it did not: this is a host showing something else, not somebody writing.
    /// </summary>
    public string Markdown
    {
        get => Source;
        set
        {
            if (string.Equals(Source, value, StringComparison.Ordinal)) return;

            Apply(EditState.For(value ?? string.Empty), notify: false);
        }
    }

    /// <summary>The document as it is being written: its source, the caret, what is picked out, and what is shown as typed.</summary>
    public EditState Current => State;

    /// <summary>
    /// Puts the document back as <paramref name="state"/> had it — an undo, a redo, a host holding it open as source — without
    /// telling anybody it was written in, because whoever asked for it already knows.
    /// </summary>
    public void Restore(EditState state) => Apply(state, notify: false);

    /// <summary>
    /// The places a search turned up, and which of them is the one being looked at.
    ///
    /// <para>
    /// <strong>Drawn over, never written into.</strong> A found word is washed at paint time from the offsets
    /// it was found at; nothing in the tree is changed to mark it, so clearing a search costs a repaint and
    /// there is no state to get out of step with the document. It is the same bargain the selection makes.
    /// </para>
    /// </summary>
    public (IReadOnlyList<(int Start, int Length)> Places, int At) Showing
    {
        get => _showing;
        set
        {
            _showing = (value.Places ?? [], value.At);

            InvalidateVisual();
        }
    }

    private (IReadOnlyList<(int Start, int Length)> Places, int At) _showing = ([], -1);

    /// <inheritdoc/>
    protected override void PaintOver(DrawingContext dc)
    {
        base.PaintOver(dc);

        for (var at = 0; at < _showing.Places.Count; at++)
        {
            var (start, length) = _showing.Places[at];
            var runs = LayoutQuery.Clusters(Laid.Root.RangeRects(start, length), 0);

            foreach (var run in runs)
                dc.DrawRectangle(Palette.Marked.Brush(), Edge(at == _showing.At), run.ToWpf());
        }
    }

    /// <summary>
    /// What is drawn round a found word. The one being looked at is outlined as well as washed, because a
    /// wash alone cannot say which of several is the one — and the glyphs are already down by the time this
    /// paints, so their colour is not ours to change.
    /// </summary>
    private Pen? Edge(bool looking)
    {
        if (!looking) return null;

        var pen = new Pen(Palette.Accent.Brush(), Math.Max(1, Palette.TextSize / 13.5));
        pen.Freeze();

        return pen;
    }

    /// <summary>
    /// Brings a stretch of the source into view and picks it out.
    ///
    /// <para>
    /// Where it lands is worked out from the offsets, which every piece carries whatever language drew it — so
    /// a hit inside a diagram is found the same way a hit in a paragraph is, and neither had to be searched for
    /// on the page.
    /// </para>
    /// <para>
    /// In a long document shown a stretch at a time, a place not laid yet is laid to first, and comes onto the page once it is.
    /// </para>
    /// <para>
    /// <strong>What is scrolled to is not always what was found.</strong> A word inside a diagram is a few
    /// pixels of a picture, and putting those few pixels at the top of the view shows a reader the middle of a
    /// chart with no idea what they are looking at. So where the hit sits inside content another language laid
    /// out, the whole of that is brought into view instead, with the hit still picked out inside it.
    /// </para>
    /// </summary>
    /// <returns>Whether the document had it to show.</returns>
    public bool Show((int Start, int Length) what, bool choose = true)
    {
        var found = Laid.Root.RangeRects(what.Start, what.Length);

        // A place inside a section the reader folded away is opened before it is shown.
        if (found.Count == 0 && Unfold(what.Start)) found = Laid.Root.RangeRects(what.Start, what.Length);

        // One a long document has not laid yet is laid to, and brought onto the page once it is; it is picked out now, so the
        // caret is there when it arrives.
        if (found.Count == 0)
        {
            if (!Uncover(what.Start)) return false;

            if (choose && what.Length > 0) Apply(State.Select(what.Start, what.Length), notify: false);

            return true;
        }

        var shown = Whole(what);

        BringIntoView(new Rect(shown.X * Zoom, shown.Y * Zoom,
                               Math.Max(shown.Width * Zoom, 1), Math.Max(shown.Height * Zoom, 1)));

        if (choose && what.Length > 0) Apply(State.Select(what.Start, what.Length), notify: false);

        return true;
    }

    /// <summary>
    /// What a reader has to see for a place to mean anything: the place itself, unless it sits inside content
    /// another language drew, in which case the whole of that.
    /// </summary>
    private Rect Whole((int Start, int Length) what)
    {
        var found = Laid.Root.RangeRects(what.Start, what.Length);
        var place = found[0];

        foreach (var rect in found) place = LayoutRect.Union(place, rect);

        for (var piece = Laid.Root.PieceAt(new LayoutPoint(place.X + (place.Width / 2), place.Y + (place.Height / 2)));
             piece.Exists;
             piece = piece.Parent)
        {
            if (piece.Part.TreePart() is not { } part) continue;
            if (ContentLanguages.Held(part) is null) continue;

            return piece.Bounds.ToWpf();
        }

        return place.ToWpf();
    }
}
