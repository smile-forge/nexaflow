using System;
using System.Windows;

using Nexaflow.Visuals.Text.Editing;

namespace Nexaflow.Visuals.Text.Markdown;

/// <summary>
/// What was written here, so it can be taken back; and the clipboard, which is the application's.
///
/// <para>
/// <strong>The history is this control's.</strong> An edit is whatever the element and the language the caret was in
/// made of a key, and what it came to is a document — so a step back is that document as it was, put back and read
/// again (<see cref="EditHistory"/>). An edit to a formula and an edit to a paragraph are the same thing to take back.
/// </para>
/// <para>
/// <strong>The clipboard is not.</strong> Copying says what would go on a clipboard and asks for it to be put there
/// (<see cref="CopyingEvent"/>); pasting asks for what is on one (<see cref="PastingEvent"/>). Both bubble, so the
/// application answers them once for every document in the window — and a host that has something to say about what
/// may leave it, or arrive, answers first.
/// </para>
/// </summary>
public sealed partial class MarkdownSurface
{
    private readonly EditHistory _history = new();

    /// <summary>The document as it stood after the last thing written, which is where the next step starts from.</summary>
    private EditState _last = EditState.For(string.Empty);

    /// <summary>Raised whenever something was written here, after the host has been told what.</summary>
    public event EventHandler? Edited;

    /// <summary>What was written here, which a host with buttons for undo and redo asks whether either can be done.</summary>
    public EditHistory History => _history;

    /// <summary>Whether there is anything to take back.</summary>
    public bool CanUndo => _history.CanUndo;

    /// <summary>Whether anything taken back could be written again.</summary>
    public bool CanRedo => _history.CanRedo;

    /// <summary>Takes back the last thing written.</summary>
    public void Undo() => Back(_history.Undo(_shown.Current));

    /// <summary>Writes again what was last taken back.</summary>
    public void Redo() => Back(_history.Redo(_shown.Current));

    private void Back(EditState? state)
    {
        if (state is null) return;

        _shown.Restore(state);
        if (!IsReadOnly && IsKeyboardFocusWithin) _shown.ShowCaret();

        _last = _shown.Current;
        Told();
    }

    /// <summary>Something was written in the element: remembered, held as source if it is being, and told to the host.</summary>
    private void Written()
    {
        HoldAsWritten();

        var now = _shown.Current;
        _history.Record(_last, now);
        _last = now;

        Told();
    }

    /// <summary>Tells a binding what the document now says, without it coming back as a new one; and anybody listening.</summary>
    private void Told()
    {
        _telling = true;
        try { SetCurrentValue(MarkdownProperty, Unframed(_shown.Markdown)); }
        finally { _telling = false; }

        Prompted();
        Edited?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Writes <paramref name="next"/> as though it had been typed — one step to take back, the host told — for an edit made
    /// here on somebody's behalf: something dropped or pasted, a block put in by the host.
    /// </summary>
    private void Write(EditState next)
    {
        if (IsReadOnly || string.Equals(next.Source, _shown.Markdown, StringComparison.Ordinal)) return;

        _history.Break();
        _shown.Restore(next);
        if (IsKeyboardFocusWithin) _shown.ShowCaret();

        Written();
        _history.Break();
    }

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

    /// <summary>Asks for <paramref name="copy"/> to be put on the clipboard. True where somebody did.</summary>
    public bool Copy(MarkdownClipboard.ContentCopy copy)
    {
        var asked = new ContentCopyingEventArgs(CopyingEvent, copy);
        RaiseEvent(asked);

        return asked.Handled;
    }

    /// <summary>
    /// Asks for what is chosen to be put on the clipboard — the whole of it, where nothing is — and says whether it was.
    /// </summary>
    public bool CopySelection() => Copy(MarkdownClipboard.Copied(_shown.Markdown, Chosen()));

    /// <summary>Copies what is chosen, then takes it away — but only once it has been put somewhere, so nothing is lost.</summary>
    public bool Cut()
    {
        if (IsReadOnly || Chosen() is not { } chosen) return false;
        if (!Copy(MarkdownClipboard.Copied(_shown.Markdown, chosen))) return false;

        Write(_shown.Current.Write(string.Empty));

        return true;
    }

    /// <summary>
    /// Asks for what is on the clipboard and writes it at the caret: the host first, for a picture or a file it would
    /// rather handle, then as markdown — or, into a formula, as the formula it is meant to be.
    /// </summary>
    public bool Paste()
    {
        if (IsReadOnly) return false;

        var asked = new ContentPastingEventArgs(PastingEvent);
        RaiseEvent(asked);

        return asked.Data is { } data && Pasted(data);
    }

    private bool Pasted(IDataObject data)
    {
        if (ContentPasted?.Invoke(data) == true) return true;

        if (InFormula())
            return PasteIntoFormula(MarkdownClipboard.ReadPlainText(data));

        if (MarkdownClipboard.ReadBestMarkdown(data) is not { Length: > 0 } markdown) return false;

        Write(_shown.Current.Insert(markdown.ReplaceLineEndings("\n")));

        return true;
    }

    /// <summary>The stretch chosen, from the first thing picked out to the last — null where nothing is.</summary>
    private (int Start, int Length)? Chosen()
    {
        var state = _shown.Current;
        return state.HasSelection ? (state.SelectionStart, state.SelectionLength) : null;
    }

    /// <summary>
    /// The caret was put somewhere, or something was picked out, with nothing written: whatever is written next is a step of
    /// its own, and starts from here — so taking it back puts the caret back where the reader had put it.
    /// </summary>
    private void Moved()
    {
        var now = _shown.Current;
        if (ReferenceEquals(now, _last) || !string.Equals(now.Source, _last.Source, StringComparison.Ordinal)) return;

        _last = now;
        _history.Break();
    }
}

/// <summary>Something asked to be put on the clipboard, in every way a reader might want it pasted.</summary>
public sealed class ContentCopyingEventArgs(RoutedEvent routed, MarkdownClipboard.ContentCopy copy) : RoutedEventArgs(routed)
{
    /// <summary>What would go on the clipboard.</summary>
    public MarkdownClipboard.ContentCopy Copy { get; } = copy;

    /// <summary>The same, as a clipboard holds it — markdown, plain words and marked-up text in one.</summary>
    public IDataObject Data => MarkdownClipboard.Data(Copy);
}

/// <summary>Asked for what is on the clipboard, to paste it.</summary>
public sealed class ContentPastingEventArgs(RoutedEvent routed) : RoutedEventArgs(routed)
{
    /// <summary>What was handed over to paste, set by whoever answered.</summary>
    public IDataObject? Data { get; set; }
}
