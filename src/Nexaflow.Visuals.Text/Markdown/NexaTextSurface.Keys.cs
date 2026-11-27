using System;
using System.Windows;
using System.Windows.Input;

using Nexaflow.Markdown.Editing;
using Nexaflow.Markdown.Layout;
using Nexaflow.Visuals.Text.Editing;
using Nexaflow.Visuals.Text.Painting;

namespace Nexaflow.Visuals.Text.Markdown;

/// <summary>
/// The keys, none of which mean anything here: each one is handed to the engine as what was pressed
/// (<see cref="ContentKey"/>), and what it does about it is the engine's and the language's.
///
/// <para>
/// Not even the clipboard's or the page's. The engine is the only thing that knows what is picked out and what language it
/// was written in, so it is the only thing that can say what a copy holds; and where a page key means nothing to the
/// content it asks back for a page, which is the one part of it only this control can answer.
/// </para>
/// </summary>
public sealed partial class NexaTextSurface
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

    /// <summary>
    /// A key, handed to the engine. False leaves it to go on to whatever else wants it — Tab to the next field, say.
    ///
    /// <para>
    /// Taken separately from <see cref="OnPreviewKeyDown"/> because the modifiers a key was held with are the keyboard's own
    /// state, which nothing but a keyboard can set: a host or a test saying a key was pressed says which.
    /// </para>
    /// </summary>
    internal bool Pressed(Key key, ModifierKeys modifiers) =>
        WpfInput.Editing(key) is var pressed and not EditingKey.None && _engine.Input(new ContentKey(pressed, WpfInput.Held(modifiers)));

    /// <summary>
    /// Picks out everything written. Exactly that, rather than grown out to the constructs it covers, which in content shown
    /// inside another would reach its delimiters too.
    /// </summary>
    public void SelectAll() => _engine.SelectAll();

    /// <summary>
    /// Page Up or Page Down the engine left to this: content shown whole is scrolled by whatever holds it, so the key is taken and nothing more
    /// is done (<see cref="ContentEngine.PagedView"/> pages content that scrolls itself).
    /// </summary>
    private static bool Paged(bool up) => true;

    /// <summary>Brings the caret onto the page, wherever an edit or a key left it (<see cref="ContentEngine.RevealCaret"/>).</summary>
    private void Reveal()
    {
        if (!_shown.HasCaret || _writingGiven) return;

        // Content shown whole asks whatever holds it to.
        if (_engine.RevealCaret() is { } at) _shown.BringIntoView(_shown.FromContent(at));
    }
}
