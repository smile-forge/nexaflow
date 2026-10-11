using System;
using System.Windows;

namespace Nexaflow.Visuals.Text.Markdown;

/// <summary>
/// What happened to the content, said to the page it is on: what was written, what was picked out, that it was laid out, that
/// the page moved under the reader, and a link out of it being followed.
///
/// <para>
/// <strong>Routed, so a page says what it wants once.</strong> Every one of them bubbles, and is attached in the page's XAML
/// or handled by anything holding the control — a list of documents answers a link out of any of them in one place. They
/// are the engine's, said again here: the engine works out what happened, and this control only passes it up the page.
/// </para>
/// </summary>
public sealed partial class MarkdownSurface
{
    /// <summary>Raised when the source changed — written in, taken back, written again, or put there on the reader's behalf.</summary>
    public static readonly RoutedEvent SourceChangedEvent = EventManager.RegisterRoutedEvent(
        nameof(SourceChanged), RoutingStrategy.Bubble, typeof(EventHandler<ContentSourceChangedEventArgs>), typeof(MarkdownSurface));

    public event EventHandler<ContentSourceChangedEventArgs> SourceChanged
    {
        add => AddHandler(SourceChangedEvent, value);
        remove => RemoveHandler(SourceChangedEvent, value);
    }

    /// <summary>
    /// Raised when what is picked out changed, whatever it is: words, a note in a tune, a box in a flowchart — each picked
    /// thing says what it stands for and, where the drawing names it, its id.
    /// </summary>
    public static readonly RoutedEvent SelectedEvent = EventManager.RegisterRoutedEvent(
        nameof(Selected), RoutingStrategy.Bubble, typeof(EventHandler<ContentSelectedEventArgs>), typeof(MarkdownSurface));

    public event EventHandler<ContentSelectedEventArgs> Selected
    {
        add => AddHandler(SelectedEvent, value);
        remove => RemoveHandler(SelectedEvent, value);
    }

    /// <summary>
    /// Raised when two presses land on the content, carrying what they landed on exactly as <see cref="Selected"/>
    /// carries what one press picked out. Handled where the page took them.
    ///
    /// <para>
    /// Two presses are a thing that happened, and what they come to is the page's: one press on a module of an import
    /// tree picks it out and two open it in a tab of its own. A link is a different thing and goes on being one
    /// (<see cref="LinkNavigate"/>) — one press follows a link, here as in a help page.
    /// </para>
    /// </summary>
    public static readonly RoutedEvent DoubleClickedEvent = EventManager.RegisterRoutedEvent(
        nameof(DoubleClicked), RoutingStrategy.Bubble, typeof(EventHandler<ContentSelectedEventArgs>), typeof(MarkdownSurface));

    public event EventHandler<ContentSelectedEventArgs> DoubleClicked
    {
        add => AddHandler(DoubleClickedEvent, value);
        remove => RemoveHandler(DoubleClickedEvent, value);
    }

    /// <summary>Raised once the content has been laid out, before it is shown — for a page reading what was laid.</summary>
    public static readonly RoutedEvent PreRenderEvent = EventManager.RegisterRoutedEvent(
        nameof(PreRender), RoutingStrategy.Bubble, typeof(RoutedEventHandler), typeof(MarkdownSurface));

    public event RoutedEventHandler PreRender
    {
        add => AddHandler(PreRenderEvent, value);
        remove => RemoveHandler(PreRenderEvent, value);
    }

    /// <summary>
    /// Raised when a slower reading of something on the page has landed — code read by its grammar, a spelling
    /// checked — and the content has been laid again for it. <see cref="Rereading"/> says whether any more are still
    /// to come, which one of these on its own cannot.
    /// </summary>
    public static readonly RoutedEvent RereadEvent = EventManager.RegisterRoutedEvent(
        nameof(Reread), RoutingStrategy.Bubble, typeof(RoutedEventHandler), typeof(MarkdownSurface));

    public event RoutedEventHandler Reread
    {
        add => AddHandler(RereadEvent, value);
        remove => RemoveHandler(RereadEvent, value);
    }

    /// <summary>
    /// Whether a slower reading of something on the page is still to land. False once every reading asked for has come
    /// back, whether or not it came to anything — so a page that must act on the content as it finally reads, rather
    /// than as it first appeared, waits for <see cref="Reread"/> to leave this false instead of counting events.
    /// </summary>
    public bool Rereading => _engine.Rereading;

    /// <summary>
    /// Raised when the page has moved under the reader — scrolled, or laid out taller or shorter than it was.
    /// <see cref="ShownFrom"/> says where it now stands.
    /// </summary>
    public static readonly RoutedEvent PlaceChangedEvent = EventManager.RegisterRoutedEvent(
        nameof(PlaceChanged), RoutingStrategy.Bubble, typeof(RoutedEventHandler), typeof(MarkdownSurface));

    public event RoutedEventHandler PlaceChanged
    {
        add => AddHandler(PlaceChangedEvent, value);
        remove => RemoveHandler(PlaceChangedEvent, value);
    }

    /// <summary>
    /// Raised to follow a link out of the content. Handled where the page took it; unhandled leaves it to open as links do.
    /// A link into the content is never raised — it is answered by the content, which is the only thing that knows where it
    /// goes.
    /// </summary>
    public static readonly RoutedEvent LinkNavigateEvent = EventManager.RegisterRoutedEvent(
        nameof(LinkNavigate), RoutingStrategy.Bubble, typeof(EventHandler<ContentLinkEventArgs>), typeof(MarkdownSurface));

    public event EventHandler<ContentLinkEventArgs> LinkNavigate
    {
        add => AddHandler(LinkNavigateEvent, value);
        remove => RemoveHandler(LinkNavigateEvent, value);
    }

    /// <summary>Says to the page that the engine laid the content out.</summary>
    private void Laid(object? sender, EventArgs args) => RaiseEvent(new RoutedEventArgs(PreRenderEvent, this));

    /// <summary>
    /// Says to the page that a slower reading landed. Raised on this control's own thread, since the reading lands on
    /// whatever thread read it, and after the content has been laid again for it — the element shown here answers the
    /// same event by laying it out again, and does so first.
    /// </summary>
    private void Landed(object? sender, EventArgs args) =>
        Dispatcher.BeginInvoke(() => RaiseEvent(new RoutedEventArgs(RereadEvent, this)));

    /// <summary>Says to the page that the document has moved under it.</summary>
    private void Moved() => RaiseEvent(new RoutedEventArgs(PlaceChangedEvent, this));

    /// <summary>Says to the page what is picked out now.</summary>
    /// <inheritdoc/>
    void IContentEvents.OnSelect(ContentSelectionChange change) =>
        RaiseEvent(new ContentSelectedEventArgs(SelectedEvent, change) { Source = this });

    bool IContentEvents.OnDoubleClick(ContentSelectionChange change)
    {
        var asked = new ContentSelectedEventArgs(DoubleClickedEvent, change) { Source = this };
        RaiseEvent(asked);

        return asked.Handled;
    }

    /// <summary>A link out of the content, offered to the page. True where it took it.</summary>
    private bool OpenLink(string url)
    {
        var asked = new ContentLinkEventArgs(LinkNavigateEvent, url) { Source = this };
        RaiseEvent(asked);

        return asked.Handled;
    }
}

/// <summary>What changed in the source, and how — said to the page.</summary>
public sealed class ContentSourceChangedEventArgs(RoutedEvent routed, ContentSourceChange change) : RoutedEventArgs(routed)
{
    /// <summary>The change, with what the content was and is, and how it came about.</summary>
    public ContentSourceChange Change { get; } = change;
}

/// <summary>What is picked out now — said to the page.</summary>
public sealed class ContentSelectedEventArgs(RoutedEvent routed, ContentSelectionChange change) : RoutedEventArgs(routed)
{
    /// <summary>What is picked out, and each thing picked out.</summary>
    public ContentSelectionChange Change { get; } = change;
}

/// <summary>A link out of the content being followed.</summary>
public sealed class ContentLinkEventArgs(RoutedEvent routed, string url) : RoutedEventArgs(routed)
{
    /// <summary>Where it goes, as written.</summary>
    public string Url { get; } = url;
}
