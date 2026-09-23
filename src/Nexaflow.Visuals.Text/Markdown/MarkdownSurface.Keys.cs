using System;
using System.Windows;
using System.Windows.Input;

using Nexaflow.Visuals.Text.Editing;

namespace Nexaflow.Visuals.Text.Markdown;

/// <summary>
/// The keys: each one handed to the element as the one thing it means, which the element and the language the caret is
/// in then make whatever they make of it.
///
/// <para>
/// <strong>Nothing here knows what a key does to the document.</strong> Enter is a settle, Backspace is a backspace;
/// whether that starts a list item, a pie slice or nothing at all is the content's to say, which is why a formula and a
/// sentence written on the same page answer the same key differently without this code having a case for either.
/// </para>
/// </summary>
public sealed partial class MarkdownSurface
{
    /// <summary>Whether the last key was a space already written, so the text it also arrives as is passed over.</summary>
    private bool _spaced;

    /// <inheritdoc/>
    protected override void OnGotKeyboardFocus(KeyboardFocusChangedEventArgs e)
    {
        base.OnGotKeyboardFocus(e);

        // Where it was: the caret is shown, not put anywhere, so gaining the keyboard moves nothing.
        if (!IsReadOnly && !_shown.HasCaret) _shown.ShowCaret();
    }

    /// <inheritdoc/>
    protected override void OnLostKeyboardFocus(KeyboardFocusChangedEventArgs e)
    {
        base.OnLostKeyboardFocus(e);

        if (!IsKeyboardFocusWithin) _shown.ReleaseCaret();
    }

    /// <inheritdoc/>
    protected override void OnTextInput(TextCompositionEventArgs e)
    {
        base.OnTextInput(e);
        if (e.Handled || IsReadOnly || string.IsNullOrEmpty(e.Text)) return;

        // A space was already written by the key that made it, as a settle — the character it also arrives as is not a second one.
        if (_spaced && e.Text == " ") { _spaced = false; e.Handled = true; return; }

        foreach (var character in e.Text)
        {
            // Control characters arrive as text too — a backspace, an escape — and are keys, handled as keys.
            if (char.IsControl(character)) continue;

            _shown.Type(character);
        }

        e.Handled = true;
    }

    /// <inheritdoc/>
    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        base.OnPreviewKeyDown(e);
        if (e.Handled) return;

        var key = e.Key == Key.System ? e.SystemKey : e.Key;

        e.Handled = Pressed(key, Keyboard.Modifiers);
    }

    /// <summary>What a key means here, done. False leaves it to go on to whatever else wants it — Tab to the next field, say.</summary>
    internal bool Pressed(Key key, ModifierKeys modifiers)
    {
        _spaced = false;

        var shift = (modifiers & ModifierKeys.Shift) != 0;
        var ctrl = (modifiers & ModifierKeys.Control) != 0;

        if (ctrl) return Commanded(key, shift);

        switch (key)
        {
            case Key.Left or Key.Right:
                _shown.MoveCaret(forward: key == Key.Right, extend: shift);
                return true;

            case Key.Up or Key.Down:
                _shown.MoveCaretVertically(up: key == Key.Up, extend: shift);
                return true;

            case Key.Home or Key.End:
                Along(end: key == Key.End, extend: shift);
                return true;

            case Key.PageUp or Key.PageDown:
                if (key == Key.PageUp) _scroller.PageUp();
                else _scroller.PageDown();
                return true;

            case Key.Escape:
                if (!_shown.Current.HasSelection) return false;
                _shown.ClearSelection();
                return true;
        }

        if (IsReadOnly) return false;

        switch (key)
        {
            case Key.Back:
                _shown.Backspace();
                return true;

            case Key.Delete:
                _shown.Delete();
                return true;

            // Both end whatever is half-written, which is the content's to say; a line break inside a paragraph is written
            // as one, since Enter already means the next paragraph.
            case Key.Enter when shift:
                _shown.Insert("\\\n");
                return true;

            case Key.Enter or Key.Space:
                _shown.Settle(key == Key.Enter ? "\n" : " ");
                _spaced = key == Key.Space;
                return true;

            // Through the holes a construct left while any remain; otherwise Tab is the window's, to move on with.
            case Key.Tab:
                return _shown.SelectNextPlaceholder(forward: !shift);

            default:
                return false;
        }
    }

    /// <summary>What a key held with Ctrl means.</summary>
    private bool Commanded(Key key, bool shift)
    {
        switch (key)
        {
            case Key.A:
                SelectAll();
                return true;

            case Key.C or Key.Insert:
                return CopySelection();

            case Key.Home or Key.End:
                _shown.MoveCaretTo(key == Key.End ? _shown.Markdown.Length : 0, extend: shift);
                return true;
        }

        if (IsReadOnly) return false;

        switch (key)
        {
            case Key.X:
                return Cut();

            case Key.V:
                return Paste();

            case Key.Z when shift:
            case Key.Y:
                Redo();
                return true;

            case Key.Z:
                Undo();
                return true;

            case Key.B:
                _shown.Wrap("**", "**");
                return true;

            case Key.I:
                _shown.Wrap("*", "*");
                return true;

            default:
                return false;
        }
    }

    /// <summary>
    /// Picks out everything written — which in one block of a language is the language's own text, not the delimiters
    /// round it that the host never wrote. Exactly that, rather than grown out to the constructs it covers, which here would
    /// be those delimiters too.
    /// </summary>
    public void SelectAll()
    {
        var (start, length) = Inner;
        _shown.Restore(_shown.Current.Select(start, length));
    }

    /// <summary>
    /// The caret to the start or the end of the line it is on, as a reader sees the line: whatever the page drew beside
    /// it, not wherever the source happens to break.
    /// </summary>
    private void Along(bool end, bool extend)
    {
        var root = _shown.Laid.Root;
        var at = root.CaretRect(_shown.Caret);
        if (at.IsEmpty) return;

        var edge = new Point(end ? Math.Max(_shown.Laid.Size.Width, at.Right) + 1 : -1, at.Y + (at.Height / 2));

        _shown.MoveCaretTo(root.OffsetAt(edge), extend);
    }

    /// <summary>Brings the caret onto the page, wherever an edit or a key left it.</summary>
    private void Reveal()
    {
        if (!_shown.HasCaret) return;

        var at = _shown.Laid.Root.CaretRect(_shown.Caret);
        if (at.IsEmpty) return;

        var zoom = _shown.Zoom;
        _shown.BringIntoView(new Rect(at.X * zoom, at.Y * zoom, Math.Max(at.Width * zoom, 1), Math.Max(at.Height * zoom, 1)));
    }
}
