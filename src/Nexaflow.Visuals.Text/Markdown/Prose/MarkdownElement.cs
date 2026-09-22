using System.Windows;
using System.Windows.Input;

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
    public MarkdownElement(string source, StyleFormat palette, ILayoutActions? host = null)
        : base(source ?? string.Empty, palette, MarkdownContent.Of(palette), host)
    {
        // A fenced block draws uncoloured until its language has been read against it, which happens off the
        // way to drawing. When it lands, this is what shows it — the same refresh a ticked item uses.
        Loaded += (_, _) => Code.CodeSpans.Ready += Coloured;
        Unloaded += (_, _) => Code.CodeSpans.Ready -= Coloured;
    }

    /// <summary>The markdown this is showing.</summary>
    public string Markdown => Source;

    /// <inheritdoc/>
    /// <remarks>Only a plain press: Ctrl and Shift are adding to a selection, which is not what ticking an item is.</remarks>
    protected override bool Pressed(Point at, ModifierKeys modifiers) =>
        (modifiers == ModifierKeys.None && (Ticked(at) || Anchored(at))) || base.Pressed(at, modifiers);

    /// <summary>
    /// Ticks the item a press landed on, where it landed on one.
    ///
    /// <para>
    /// The whole of it is an edit. What is drawn is read off the tree, the tree is read off the source, and
    /// the source is what this writes — so there is no state anywhere saying which items are done, nothing to
    /// keep in step, and taking it back is the undo the reader already has.
    /// </para>
    /// </summary>
    private bool Ticked(Point at)
    {
        if (IsReadOnly) return false;
        if (Offered(at, LayoutGesture.Click) is not { Intent.Verb: MarkdownVerbs.Tick } act) return false;
        if (MarkdownContent.Ticked(State, act.Part) is not { } ticked) return false;

        Apply(ticked, notify: true);

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

    /// <summary>A reading landed somewhere. Laid again on the thread that draws, since it did not land there.</summary>
    private void Coloured(object? sender, EventArgs args) => Dispatcher.BeginInvoke(Refresh);
}
