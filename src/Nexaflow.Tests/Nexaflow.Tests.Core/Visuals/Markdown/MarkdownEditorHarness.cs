using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Reflection;
using Nexaflow.Visuals.Text.Markdown;

namespace Nexaflow.Tests.Core.Visuals.Markdown;

/// <summary>
/// Shared harness for driving a real <see cref="InlineMarkdownEditor"/> in a shown (off-screen) window —
/// the editor only builds its document during a render pass, so its editing behaviour can't be exercised
/// on a control that was never displayed. Interactive desktop only.
/// </summary>
internal static class MarkdownEditorHarness
{
    private static readonly FieldInfo RtbField =
        typeof(InlineMarkdownEditor).GetField("_rtb", BindingFlags.NonPublic | BindingFlags.Instance)!;

    /// <summary>Shows an editor loaded with <paramref name="markdown"/>, runs <paramref name="test"/>, closes it.</summary>
    /// <param name="configure">
    /// Applied before the text is loaded, for the properties that change what the document even is —
    /// <see cref="InlineMarkdownEditor.SingleFormula"/> decides whether the text is fenced as maths, so
    /// setting it afterwards would mean loading the text once as the wrong thing.
    /// </param>
    public static void Run(string markdown, Action<InlineMarkdownEditor, RichTextBox> test,
                           Action<InlineMarkdownEditor>? configure = null)
    {
        var editor = new InlineMarkdownEditor { EditOnDoubleClick = true, Width = 600, Height = 400 };
        configure?.Invoke(editor);
        // Shown but never activated. The editor has to be in a shown window for its render pass to
        // build the document, but taking the foreground as well means the suite snatches focus from
        // whoever is using the machine — and then loses it back the moment they click anything, which
        // reads as a different test failing on every run. Off-screen and unactivated, the render pass
        // still runs and nothing outside the test is disturbed.
        var window = new Window { Width = 640, Height = 480, Content = editor, ShowActivated = false,
                                  WindowStartupLocation = WindowStartupLocation.Manual, Left = -2000, Top = -2000 };
        try
        {
            window.Show();
            editor.UpdateLayout();               // flip IsVisible → the render pass builds the document
            editor.Markdown = markdown;
            editor.UpdateLayout();

            var rtb = (RichTextBox)RtbField.GetValue(editor)!;
            rtb.Focus();
            test(editor, rtb);
        }
        finally { window.Close(); }
    }

    /// <summary>Raises a genuine <see cref="TextCompositionManager.PreviewTextInputEvent"/> for
    /// <paramref name="text"/> on <paramref name="rtb"/> — the same event WPF fires for a keystroke, so
    /// the editor's handler runs exactly as in the app.</summary>
    public static void RaiseTextInput(RichTextBox rtb, string text)
    {
        var composition = new TextComposition(InputManager.Current, rtb, text);
        rtb.RaiseEvent(new TextCompositionEventArgs(InputManager.Current.PrimaryKeyboardDevice, composition)
        {
            RoutedEvent = TextCompositionManager.PreviewTextInputEvent,
        });
    }

    /// <summary>Types <paramref name="text"/> one character at a time.</summary>
    public static void Type(RichTextBox rtb, string text)
    {
        foreach (var ch in text) RaiseTextInput(rtb, ch.ToString());
    }

    /// <summary>
    /// Presses <paramref name="key"/> on <paramref name="rtb"/>. A real
    /// <see cref="Keyboard.PreviewKeyDownEvent"/>, because Space and Enter are decided in the editor's
    /// key handler and never reach it as typed text.
    /// </summary>
    /// <remarks>
    /// Unmodified presses only. <see cref="Keyboard.Modifiers"/> is computed from the real keyboard's
    /// state, not carried on the event, so a synthetic Shift or Ctrl cannot be pressed from in here —
    /// anything that turns on a modifier belongs in a UI journey, where the keys are real.
    /// </remarks>
    public static void RaiseKey(RichTextBox rtb, Key key)
    {
        var source = PresentationSource.FromVisual(rtb)
                     ?? throw new InvalidOperationException("the editor must be in a shown window");

        rtb.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, source, 0, key)
        {
            RoutedEvent = Keyboard.PreviewKeyDownEvent,
        });
    }

    /// <summary>The editor's own <see cref="RichTextBox"/> — the surface events are raised on.</summary>
    public static RichTextBox RichTextBoxOf(InlineMarkdownEditor editor) =>
        (RichTextBox)RtbField.GetValue(editor)!;

    /// <summary>Places the caret <paramref name="textOffset"/> text characters into
    /// <paramref name="para"/>, counting Run text only (skipping element edges).</summary>
    public static void PlaceCaret(RichTextBox rtb, Paragraph para, int textOffset)
    {
        var tp = para.ContentStart;
        int remaining = textOffset;
        while (tp is not null && remaining > 0)
        {
            if (tp.GetPointerContext(LogicalDirection.Forward) == TextPointerContext.Text)
            {
                int len = tp.GetTextRunLength(LogicalDirection.Forward);
                if (len >= remaining) { tp = tp.GetPositionAtOffset(remaining, LogicalDirection.Forward)!; break; }
                remaining -= len;
                tp = tp.GetPositionAtOffset(len, LogicalDirection.Forward);
            }
            else tp = tp.GetNextContextPosition(LogicalDirection.Forward);
        }
        rtb.CaretPosition = tp ?? para.ContentEnd;
    }
}
