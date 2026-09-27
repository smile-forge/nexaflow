using System;
using System.Windows;
using System.Windows.Input;

using Nexaflow.Visuals.Text.Editing;

namespace Nexaflow.Visuals.Text.Markdown;

/// <summary>
/// The keys: each one handed to the engine as what was pressed (<see cref="ContentKey"/>), which takes it as the one thing
/// it means — except the clipboard's and the page's, which are this control's.
/// </summary>
public sealed partial class MarkdownSurface
{
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
        if (e.Handled) return;

        e.Handled = _engine.Input(new ContentText(e.Text));
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
        var ctrl = (modifiers & ModifierKeys.Control) != 0;

        // What only this control can do: the clipboard is the application's, and a page is as tall as what shows it. Every
        // other key is about the content, and the engine's.
        if (ctrl && key is Key.C or Key.Insert) return CopySelection();
        if (ctrl && key == Key.X) return !IsReadOnly && Cut();
        if (ctrl && key == Key.V) return !IsReadOnly && Paste();

        if (!ctrl && key is Key.PageUp or Key.PageDown)
        {
            if (key == Key.PageUp) _scroller.PageUp();
            else _scroller.PageDown();

            return true;
        }

        return _engine.Input(new ContentKey(key, modifiers));
    }

    /// <summary>
    /// Picks out everything written. Exactly that, rather than grown out to the constructs it covers, which in content shown
    /// inside another would reach its delimiters too.
    /// </summary>
    public void SelectAll() => _engine.SelectAll();

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
