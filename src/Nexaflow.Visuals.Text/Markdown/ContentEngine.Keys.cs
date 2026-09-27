using System;
using System.Windows;
using System.Windows.Input;

using Nexaflow.Visuals.Text.Editing;

namespace Nexaflow.Visuals.Text.Markdown;

/// <summary>
/// The keys: each one taken as the one thing it means, which the language the caret is in then makes whatever it makes of —
/// and what was written, so it can be taken back.
///
/// <para>
/// <strong>Nothing here knows what a key does to the document.</strong> Enter is a settle, Backspace is a backspace; whether
/// that starts a list item, a pie slice or nothing at all is the language's to say, which is why a formula and a sentence
/// written on the same page answer the same key differently without this code having a case for either.
/// </para>
/// <para>
/// <strong>The history is the engine's.</strong> An edit is whatever a key came to in the language it landed in, and what it
/// came to is the whole content — so a step back is that content as it was, put back and read again
/// (<see cref="EditHistory"/>). An edit to a formula and an edit to a paragraph are the same thing to take back.
/// </para>
/// </summary>
public sealed partial class ContentEngine
{
    private readonly EditHistory _history = new();

    /// <summary>The content as it stood after the last thing written, which is where the next step starts from.</summary>
    private EditState _last = EditState.For(string.Empty);

    /// <summary>Whether the last key was a space already written, so the text it also arrives as is passed over.</summary>
    private bool _spaced;

    private bool _holdsWritten;

    /// <summary>
    /// What the reader did, done. True where it meant something here; false leaves it to whatever else wants it — Tab to the
    /// next field, say.
    /// </summary>
    public bool Input(ContentInput input) => input switch
    {
        ContentKey key => Pressed(key.Key, key.Modifiers),
        ContentText text => Typed(text.Text),
        _ => Pointed(input),
    };

    /// <summary>Raised when the source changed — written, taken back, written again, or put there by whatever shows it.</summary>
    public event EventHandler<ContentSourceChange>? SourceChanged;

    /// <summary>What a key means here, done.</summary>
    private bool Pressed(Key key, ModifierKeys modifiers)
    {
        _spaced = false;

        var shift = (modifiers & ModifierKeys.Shift) != 0;
        var ctrl = (modifiers & ModifierKeys.Control) != 0;

        if (ctrl) return Commanded(key, shift);

        switch (key)
        {
            case Key.Left or Key.Right:
                MoveCaret(forward: key == Key.Right, extend: shift);
                return true;

            case Key.Up or Key.Down:
                MoveCaretVertically(up: key == Key.Up, extend: shift);
                return true;

            case Key.Home or Key.End:
                Along(end: key == Key.End, extend: shift);
                return true;

            // What is picked out goes first; then a block shown as written is drawn again, keeping what was written in it.
            case Key.Escape:
                if (!_state.HasSelection) return LetGo();
                ClearSelection();
                return true;
        }

        if (_readOnly) return false;

        switch (key)
        {
            case Key.Back:
                Backspace();
                return true;

            case Key.Delete:
                Delete();
                return true;

            // Both end whatever is half-written, which is the language's to say; a line break inside a paragraph is written
            // as one, since Enter already means the next paragraph.
            case Key.Enter when shift:
                Insert("\\\n");
                return true;

            case Key.Enter or Key.Space:
                Settle(key == Key.Enter ? "\n" : " ");
                _spaced = key == Key.Space;
                return true;

            // Through the holes a construct left while any remain; otherwise Tab is the window's, to move on with.
            case Key.Tab:
                return SelectNextPlaceholder(forward: !shift);

            default:
                return false;
        }
    }

    /// <summary>What a key held with Ctrl means — the ones about the content; the clipboard's are whatever shows it.</summary>
    private bool Commanded(Key key, bool shift)
    {
        switch (key)
        {
            case Key.A:
                SelectAll();
                return true;

            case Key.Home or Key.End:
                MoveCaretTo(key == Key.End ? _state.Source.Length : 0, extend: shift);
                return true;
        }

        if (_readOnly) return false;

        switch (key)
        {
            case Key.Z when shift:
            case Key.Y:
                Redo();
                return true;

            case Key.Z:
                Undo();
                return true;

            case Key.B:
                Wrap("**", "**");
                return true;

            case Key.I:
                Wrap("*", "*");
                return true;

            default:
                return false;
        }
    }

    /// <summary>Characters typed, written one at a time as the language they land in takes each.</summary>
    private bool Typed(string text)
    {
        if (_readOnly || string.IsNullOrEmpty(text)) return false;

        // A space was already written by the key that made it, as a settle — the character it also arrives as is not a second one.
        if (_spaced && text == " ") { _spaced = false; return true; }

        foreach (var character in text)
        {
            // Control characters arrive as text too — a backspace, an escape — and are keys, handled as keys.
            if (char.IsControl(character)) continue;

            Write(character.ToString());
        }

        return true;
    }

    /// <summary>
    /// Picks out everything written. Exactly that, rather than grown out to the constructs it covers, which in content shown
    /// inside another would reach its delimiters too.
    /// </summary>
    internal void SelectAll() => Apply(_state.Select(0, _state.Source.Length), notify: false);

    /// <summary>
    /// The caret to the start or the end of the line it is on, as a reader sees the line: whatever the page drew beside it,
    /// not wherever the source happens to break.
    /// </summary>
    private void Along(bool end, bool extend)
    {
        var root = _laid.Root;
        var at = root.CaretRect(_state.Caret);
        if (at.IsEmpty) return;

        var edge = new Point(end ? Math.Max(_laid.Size.Width, at.Right) + 1 : -1, at.Y + (at.Height / 2));

        MoveCaretTo(root.OffsetAt(edge), extend);
    }

    /// <summary>
    /// Draws again whatever is shown as written — except where the whole of it is held so (<see cref="HoldsWritten"/>), which
    /// is not the reader's to let go of. False where nothing was shown.
    /// </summary>
    private bool LetGo()
    {
        if (_state.Raw is null || _holdsWritten) return false;

        Apply(_state.Settled(), notify: false);
        return true;
    }

    // ── Shown as written ────────────────────────────────────────────────────

    /// <summary>
    /// Whether the whole content is held shown as the characters written rather than what they draw — for when the drawing
    /// itself is the trouble, a formula that will not set. Everything else about writing in it carries on as it was.
    /// </summary>
    internal bool HoldsWritten
    {
        get => _holdsWritten;
        set
        {
            if (_holdsWritten == value) return;

            _holdsWritten = value;

            // Let go of only what this held open: a command being spelled is the writer's, and stays shown as they spell it.
            Apply(value || _state.Raw != Whole(_state) ? _state : _state with { Raw = null }, notify: false);
        }
    }

    /// <summary><paramref name="next"/>, held shown as written where the whole content is.</summary>
    private EditState Held(EditState next) => _holdsWritten ? next with { Raw = Whole(next) } : next;

    private static RawZone Whole(EditState state) => new(0, state.Source.Length);

    // ── What was written ────────────────────────────────────────────────────

    /// <summary>What was written, which a host with buttons for undo and redo asks whether either can be done.</summary>
    internal EditHistory History => _history;

    /// <summary>Takes back the last thing written.</summary>
    internal void Undo() => Back(_history.Undo(_state), ContentChangeKind.Undone);

    /// <summary>Writes again what was last taken back.</summary>
    internal void Redo() => Back(_history.Redo(_state), ContentChangeKind.Redone);

    private void Back(EditState? state, ContentChangeKind kind)
    {
        if (state is null) return;

        var before = _state;
        Apply(state, notify: false);
        _last = _state;

        SourceChanged?.Invoke(this, new ContentSourceChange(before, _state, kind));
    }

    /// <summary>
    /// Writes <paramref name="next"/> as though it had been typed — one step to take back — for an edit made on the reader's
    /// behalf: something dropped or pasted, a block put in by the host.
    /// </summary>
    internal void Replace(EditState next)
    {
        if (_readOnly || string.Equals(next.Source, _state.Source, StringComparison.Ordinal)) return;

        var before = _state;

        _history.Break();
        Apply(next, notify: false);
        Recorded();
        _history.Break();

        SourceChanged?.Invoke(this, new ContentSourceChange(before, _state, ContentChangeKind.Replaced));
    }

    /// <summary>Forgets what was written: what is here now is where taking back stops.</summary>
    internal void Begin()
    {
        _history.Clear();
        _last = _state;
    }

    /// <summary>What was just written, remembered as a step — or as more of the step it carries on from.</summary>
    private void Recorded()
    {
        _history.Record(_last, _state);
        _last = _state;
    }

    /// <summary>
    /// The caret was put somewhere, or something was picked out, with nothing written: whatever is written next is a step of
    /// its own, and starts from here — so taking it back puts the caret back where the reader had put it.
    /// </summary>
    private void Stayed()
    {
        if (ReferenceEquals(_state, _last) || !string.Equals(_state.Source, _last.Source, StringComparison.Ordinal)) return;

        _last = _state;
        _history.Break();
    }
}
