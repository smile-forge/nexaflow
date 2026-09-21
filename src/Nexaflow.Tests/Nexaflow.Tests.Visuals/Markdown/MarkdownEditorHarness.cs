using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Reflection;
using Nexaflow.Visuals.Text.Markdown;
using System.Linq;

namespace Nexaflow.Tests.Visuals.Markdown;

/// <summary>
/// Shared harness for driving a real <see cref="InlineMarkdownEditor"/> in a shown (off-screen) window —
/// the editor only builds its document during a render pass, so its editing behaviour can't be exercised
/// on a control that was never displayed. Interactive desktop only.
/// </summary>
internal static class MarkdownEditorHarness
{
    private static readonly FieldInfo RtbField =
        typeof(InlineMarkdownEditor).GetField("_rtb", BindingFlags.NonPublic | BindingFlags.Instance)!;

    /// <summary>
    /// Runs whatever the editor has queued. A paste and a drop both finish on the dispatcher rather than
    /// in the handler that started them — the paste because it fires inside the RichTextBox's change
    /// block, where rebuilding the document throws — so nothing has happened yet when the call returns.
    /// Waiting at a <em>lower</em> priority than the work is what makes this a wait rather than a race.
    /// </summary>
    public static void Pump() =>
        System.Windows.Threading.Dispatcher.CurrentDispatcher.Invoke(
            () => { }, System.Windows.Threading.DispatcherPriority.ContextIdle);

    /// <summary>Shows an editor loaded with <paramref name="markdown"/>, runs <paramref name="test"/>, closes it.</summary>
    /// <param name="configure">
    /// Applied before the text is loaded, for the properties that change what the document even is —
    /// <see cref="InlineMarkdownEditor.SingleBlock"/> decides whether the text is fenced as maths, so
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

    // ── Driving the editor, rather than the surface it happens to be drawn on ────────────────────────
    //
    // The editor is a RichTextBox today and will be one element tomorrow. Everything below is written in
    // terms of the editor and a source offset, so a test says what it means — put the caret here, type
    // that — and nothing has to be rewritten when the surface underneath it changes.

    /// <summary>Shows an editor loaded with <paramref name="markdown"/>, runs <paramref name="test"/>, closes it.</summary>
    public static void Run(string markdown, Action<InlineMarkdownEditor> test,
                           Action<InlineMarkdownEditor>? configure = null) =>
        Run(markdown, (editor, _) => test(editor), configure);

    /// <summary>Raises a genuine keystroke's worth of text on the editor.</summary>
    public static void RaiseTextInput(InlineMarkdownEditor editor, string text) =>
        RaiseTextInput(RichTextBoxOf(editor), text);

    /// <summary>Types <paramref name="text"/> into the editor one character at a time.</summary>
    public static void Type(InlineMarkdownEditor editor, string text)
    {
        foreach (var character in text) RaiseTextInput(editor, character.ToString());
    }

    /// <summary>Presses <paramref name="key"/> on the editor.</summary>
    public static void RaiseKey(InlineMarkdownEditor editor, Key key) => RaiseKey(RichTextBoxOf(editor), key);

    /// <summary>
    /// Puts the caret <paramref name="at"/> characters into the markdown the editor is showing.
    ///
    /// <para>
    /// A source offset, because that is the thing a test knows — it wrote the markdown. Resolved here by
    /// finding the block the offset falls in and counting to its paragraph, which holds while a block draws
    /// as one paragraph, and is exact for the single-block documents these tests use. Once the editor is one
    /// element over one document this is a caret move and nothing else.
    /// </para>
    /// </summary>
    public static void PlaceCaret(InlineMarkdownEditor editor, int at)
    {
        var source = editor.Markdown ?? string.Empty;
        var (block, within) = Blocked(source, Math.Clamp(at, 0, source.Length));

        var rtb = RichTextBoxOf(editor);
        var paragraphs = rtb.Document.Blocks.OfType<Paragraph>().ToList();
        if (paragraphs.Count == 0) return;

        PlaceCaret(rtb, paragraphs[Math.Clamp(block, 0, paragraphs.Count - 1)], within);
    }

    /// <summary>Which block of <paramref name="source"/> an offset falls in, and how far into it.</summary>
    private static (int Block, int Within) Blocked(string source, int at)
    {
        var blocks = MarkdownBlocks.Split(source);
        var start = 0;

        for (var index = 0; index < blocks.Count; index++)
        {
            var found = source.IndexOf(blocks[index], start, StringComparison.Ordinal);
            if (found < 0) break;

            if (at <= found + blocks[index].Length) return (index, Math.Max(at - found, 0));

            start = found + blocks[index].Length;
        }

        return (Math.Max(blocks.Count - 1, 0), 0);
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
