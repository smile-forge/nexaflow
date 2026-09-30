using System;
using System.Windows;

using Nexaflow.Markdown.Editing;
using Nexaflow.Visuals.Text.Editing;

namespace Nexaflow.Visuals.Text.Markdown;

/// <summary>
/// What was written here, so it can be taken back; and the clipboard, which is the application's.
///
/// <para>
/// <strong>The history is the engine's.</strong> An edit is whatever the language the caret was in made of a key, and what
/// it came to is a document — so a step back is that document as it was, put back and read again. This control only says
/// where it can be asked for (<see cref="EditHistory"/>).
/// </para>
/// <para>
/// <strong>The clipboard is the application's, and what goes on it is the engine's.</strong> The engine says what a copy
/// holds, because what is picked out and the language it was written in are both its; this control only asks for that copy
/// to be put somewhere (<see cref="CopyingEvent"/>), and asks for what is on a clipboard to paste (<see cref="PastingEvent"/>).
/// Both bubble, so the application answers them once for every document in the window — and a host that has something to say
/// about what may leave it, or arrive, answers first.
/// </para>
/// </summary>
public sealed partial class MarkdownSurface
{
    /// <summary>What was written here, which a host with buttons for undo and redo asks whether either can be done.</summary>
    public EditHistory History => _engine.History;

    /// <summary>Whether there is anything to take back.</summary>
    public bool CanUndo => _engine.History.CanUndo;

    /// <summary>Whether anything taken back could be written again.</summary>
    public bool CanRedo => _engine.History.CanRedo;

    /// <summary>Takes back the last thing written.</summary>
    public void Undo() => _engine.Undo();

    /// <summary>Writes again what was last taken back.</summary>
    public void Redo() => _engine.Redo();

    /// <summary>
    /// The document changed — written in, taken back, or put there here on the reader's behalf: the host is told, and where the
    /// change was not typed, the caret is shown where it now stands.
    /// </summary>
    private void Written(ContentSourceChange change)
    {
        if (change.Kind != ContentChangeKind.Written && !IsReadOnly && IsKeyboardFocusWithin) _shown.ShowCaret();

        Told();

        RaiseEvent(new ContentSourceChangedEventArgs(SourceChangedEvent, change) { Source = this });
    }

    /// <summary>Tells a binding what the document now says, without it coming back as a new one; and anybody listening.</summary>
    private void Told()
    {
        _telling = true;
        try { SetCurrentValue(MarkdownProperty, _shown.Markdown); }
        finally { _telling = false; }

        Prompted();
    }

    /// <summary>
    /// Writes <paramref name="next"/> as though it had been typed — one step to take back, the host told — for an edit made
    /// here on somebody's behalf: something dropped or pasted, a block put in by the host.
    /// </summary>
    private void Write(EditState next) => _engine.Replace(next);

    // ── The clipboard ───────────────────────────────────────────────────────

    /// <summary>
    /// Raised to put something on the clipboard: what was chosen, as markdown, as plain words and as marked-up text. Whoever
    /// answers — the application, once for the window — marks it handled; a cut only takes the words away once it has.
    /// </summary>
    public static readonly RoutedEvent CopyingEvent = EventManager.RegisterRoutedEvent(
        "Copying", RoutingStrategy.Bubble, typeof(EventHandler<ContentCopyingEventArgs>), typeof(MarkdownSurface));

    /// <summary>Raised to be handed what is on the clipboard, to paste it. Whoever answers sets what it holds.</summary>
    public static readonly RoutedEvent PastingEvent = EventManager.RegisterRoutedEvent(
        "Pasting", RoutingStrategy.Bubble, typeof(EventHandler<ContentPastingEventArgs>), typeof(MarkdownSurface));

    /// <summary>Raised when something dragged in from elsewhere is let go here, to be told what it comes to. Whoever answers says, or takes it.</summary>
    public static readonly RoutedEvent DroppingEvent = EventManager.RegisterRoutedEvent(
        "Dropping", RoutingStrategy.Bubble, typeof(EventHandler<ContentDroppingEventArgs>), typeof(MarkdownSurface));

    public event EventHandler<ContentCopyingEventArgs> Copying
    {
        add => AddHandler(CopyingEvent, value);
        remove => RemoveHandler(CopyingEvent, value);
    }

    public event EventHandler<ContentPastingEventArgs> Pasting
    {
        add => AddHandler(PastingEvent, value);
        remove => RemoveHandler(PastingEvent, value);
    }

    public event EventHandler<ContentDroppingEventArgs> Dropping
    {
        add => AddHandler(DroppingEvent, value);
        remove => RemoveHandler(DroppingEvent, value);
    }

    /// <summary>Asks for <paramref name="copy"/> to be put on the clipboard. True where somebody did.</summary>
    public bool Copy(MarkdownClipboard.ContentCopy copy)
    {
        var asked = new ContentCopyingEventArgs(CopyingEvent, copy);
        RaiseEvent(asked);

        return asked.Handled;
    }

    /// <summary>
    /// Asks for what is chosen to be put on the clipboard — the whole of it, where nothing is — and says whether it was. What
    /// a copy holds is the engine's to say, because what is chosen and the language it is written in are both its.
    /// </summary>
    public bool CopySelection() => _engine.Copied();

    /// <summary>Copies what is chosen, then takes it away — but only once it has been put somewhere, so nothing is lost.</summary>
    public bool Cut() => _engine.Cut();
}

/// <summary>Something asked to be put on the clipboard, in every way a reader might want it pasted.</summary>
public sealed class ContentCopyingEventArgs(RoutedEvent routed, MarkdownClipboard.ContentCopy copy) : RoutedEventArgs(routed)
{
    /// <summary>What would go on the clipboard.</summary>
    public MarkdownClipboard.ContentCopy Copy { get; } = copy;
}

/// <summary>
/// Asked for what is on the clipboard, to paste it. Whoever answers takes it off the clipboard and says what it comes to in
/// words and in markdown — a clipboard holds whatever a machine put there, and turning that into something a document can be
/// written from is the host's, not the content's. Answering with neither, having dealt with it another way, still marks it
/// handled.
/// </summary>
public sealed class ContentPastingEventArgs(RoutedEvent routed) : RoutedEventArgs(routed)
{
    /// <summary>What was on it as plain words.</summary>
    public string? Words { get; set; }

    /// <summary>The same as markdown, where what was on it was marked up.</summary>
    public string? Markdown { get; set; }
}

/// <summary>
/// Something dragged in from elsewhere and let go, asked what it comes to. Whoever answers says it in words and in markdown,
/// or deals with it another way and marks it handled having said neither.
/// </summary>
public sealed class ContentDroppingEventArgs(RoutedEvent routed, IDataObject data, Point at) : RoutedEventArgs(routed)
{
    /// <summary>What was dragged, as it was carried.</summary>
    public IDataObject Data { get; } = data;

    /// <summary>Where it was let go, in the control's own coordinates.</summary>
    public Point At { get; } = at;

    /// <summary>What it comes to as plain words.</summary>
    public string? Words { get; set; }

    /// <summary>The same as markdown, where what was dragged was marked up.</summary>
    public string? Markdown { get; set; }
}
