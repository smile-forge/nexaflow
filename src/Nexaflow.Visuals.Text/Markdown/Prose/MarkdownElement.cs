using System.Windows;
using System.Windows.Input;

using System.Windows.Media;
using Nexaflow.Markdown.Ast;
using Nexaflow.Markdown.Prose;
using Nexaflow.Visuals.Text.Editing;

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
    /// <param name="options">What the host said about the content written inside the document.</param>
    public MarkdownElement(string source, StyleFormat palette, ILayoutActions? host = null,
                           DiagramRenderOptions? options = null)
        : base(source ?? string.Empty, palette, MarkdownContent.Of(palette, options), host)
    {
        // A fenced block draws uncoloured until its language has been read against it, which happens off the
        // way to drawing. When it lands, this is what shows it — the same refresh a ticked item uses.
        Loaded += (_, _) => Code.CodeSpans.Ready += Coloured;
        Unloaded += (_, _) => Code.CodeSpans.Ready -= Coloured;
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
                dc.DrawRectangle(at == _showing.At ? Palette.Marked : Palette.QuoteBg, Edge(at == _showing.At), run);
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

        var pen = new Pen(Palette.Accent, Math.Max(1, Style.TextSize / 13.5));
        pen.Freeze();

        return pen;
    }

    /// <summary>What this is drawn in, which a host may swap for another theme.</summary>
    private StyleFormat Style => Palette;

    /// <inheritdoc/>
    /// <remarks>Only a plain press: Ctrl and Shift are adding to a selection, which is not what ticking an item is.</remarks>
    protected override bool Pressed(Point at, ModifierKeys modifiers) =>
        (modifiers == ModifierKeys.None && (Ticked(at) || Anchored(at))) || base.Pressed(at, modifiers);

    /// <summary>
    /// A press on a task's box: the mark between its brackets written over as the press means — an edit like any other,
    /// through the document's own edit handling, told to whoever follows the document.
    /// </summary>
    private bool Ticked(Point at)
    {
        if (IsReadOnly) return false;
        if (Offered(at, LayoutGesture.Click) is not { Intent.Verb: MarkdownVerbs.Tick } act) return false;
        if (act.Part is not ContentPart box
            || (box.Part(MarkdownRoles.Done) ?? box.Part(MarkdownRoles.Todo)) is not { Length: 1 } mark) return false;

        WriteOver(mark.Start, mark.Length, act.Intent.Target == "on" ? "x" : " ");

        return true;
    }

    /// <summary>
    /// Goes to the heading an in-page link names, where a press landed on one.
    ///
    /// <para>
    /// <strong>A link into this document is never the host's.</strong> Nobody else can answer it: the heading
    /// is on this page, laid out by this element, and a host handed <c>#getting-started</c> has no way to know
    /// what that means or where it went. So it is answered here and not offered onwards — and a name this
    /// document has no heading for is still not the host's, because it is still a link into this document. It
    /// does nothing, which is what a reader sees when they follow a link to a section somebody deleted.
    /// </para>
    /// </summary>
    private bool Anchored(Point at)
    {
        if (Offered(at, LayoutGesture.Click) is not { Intent: { Verb: LayoutVerbs.Navigate, Target: { } where } } ) return false;
        if (!MarkdownAnchors.IsInPage(where, out var anchor)) return false;

        if (MarkdownAnchors.Sought(Laid, anchor) is { Exists: true } heading)
            BringIntoView(new Rect(heading.Bounds.X * Zoom, heading.Bounds.Y * Zoom,
                                   Math.Max(heading.Bounds.Width * Zoom, 1), Math.Max(heading.Bounds.Height * Zoom, 1)));

        return true;
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
        if (found.Count == 0) return false;

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

        foreach (var rect in found) place = Rect.Union(place, rect);

        for (var piece = Laid.Root.PieceAt(new Point(place.X + (place.Width / 2), place.Y + (place.Height / 2)));
             piece.Exists;
             piece = piece.Parent)
        {
            if (piece.Part is not ContentPart part) continue;
            if (ContentNesting.Of(part) is null) continue;

            return piece.Bounds;
        }

        return place;
    }

    /// <summary>
    /// What may be done where the gesture landed, asked of whatever language is being shown there.
    ///
    /// <para>
    /// Which language that is was settled by a stage and is hanging on the node, so nothing here looks one up:
    /// the walk goes out from the piece that was pressed until it reaches something written in another
    /// language, and asks that. A press in the prose reaches nothing and markdown answers for itself.
    /// </para>
    /// </summary>
    protected override IEnumerable<LayoutIntent> Offering(LayoutAct act)
    {
        if (act.Part is not ContentPart part) return [];

        for (var at = part; at is not null; at = at.Parent)
        {
            if (ContentNesting.Of(at) is not { } nesting) continue;

            var body = at.Part(Roles.Body);
            if (body is null) continue;

            return nesting.Language.Offers(new ContentAsk(nesting.Named, body.Text)
            {
                Part = part,
                Chosen = Chosen(body),
                IsReadOnly = IsReadOnly,
            });
        }

        return [];
    }

    /// <summary>What is picked out inside a piece of content, said as offsets into that content's own source.</summary>
    private (int Start, int Length)? Chosen(ContentPart body)
    {
        if (!State.HasSelection) return null;

        var from = Math.Max(State.SelectionStart, body.Start);
        var to = Math.Min(State.SelectionStart + State.SelectionLength, body.End);

        return to > from ? (from - body.Start, to - from) : null;
    }

    /// <summary>A reading landed somewhere. Laid again on the thread that draws, since it did not land there.</summary>
    private void Coloured(object? sender, EventArgs args) => Dispatcher.BeginInvoke(Refresh);
}
