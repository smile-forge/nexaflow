using System.Linq;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using Nexaflow.Visuals.Text.Markdown;

namespace Nexaflow.Tests.Core.Visuals.Markdown;

/// <summary>
/// Regression coverage for typing into a <em>rendered</em> block of the
/// <see cref="InlineMarkdownEditor"/> in <see cref="InlineMarkdownEditor.EditOnDoubleClick"/> mode
/// (how the Markdown file editor hosts it): a single click positions the caret without activating a
/// block, so the editor must enter edit mode on the first keystroke and apply every edit through the
/// authoritative block model.
///
/// The historical bug: the editor let WPF edit the rendered run natively and shadowed the change back
/// into the model using a source offset derived from the rendered caret. WPF collapses/normalises
/// whitespace in rendered runs, so after the first typed space that mapping drifted — characters landed
/// in the middle of the next word and spaces were dropped (e.g. "A font viewer. " became "AAfontnviewer.").
///
/// Interactive desktop only — the editor must be in a shown window for its render pass to run.
/// Run with --filter "TestCategory=UI".
/// </summary>
[TestClass]
[TestCategory("UI")]
public class InlineMarkdownEditorEditingTests
{
    private static readonly FieldInfo RtbField =
        typeof(InlineMarkdownEditor).GetField("_rtb", BindingFlags.NonPublic | BindingFlags.Instance)!;

    [TestMethod]
    public void TypingMidParagraph_InRenderedBlock_InsertsExactlyAtCaret() => UiThread.Run(() =>
    {
        const string original = "A live Process Explorer. An installed-apps manager.";
        const string typed    = "A font viewer. ";
        const string expected = "A live Process Explorer. A font viewer. An installed-apps manager.";

        var editor = new InlineMarkdownEditor { EditOnDoubleClick = true, Width = 600, Height = 400 };
        var window = new Window { Width = 640, Height = 480, Content = editor,
                                  WindowStartupLocation = WindowStartupLocation.Manual, Left = -2000, Top = -2000 };
        try
        {
            window.Show();
            editor.UpdateLayout();               // flip IsVisible → the render pass builds source-tagged runs
            editor.Markdown = original;
            editor.UpdateLayout();

            var rtb  = (RichTextBox)RtbField.GetValue(editor)!;
            var para = rtb.Document.Blocks.OfType<Paragraph>().First();

            // Place the caret just before "An" — the boundary the user was editing at.
            int caret = original.IndexOf("An installed", System.StringComparison.Ordinal);
            rtb.CaretPosition = PointerAtTextOffset(para, caret);

            // Type the string one character at a time through the real PreviewTextInput path.
            foreach (var ch in typed) RaiseTextInput(rtb, ch.ToString());

            Assert.AreEqual(expected, editor.Markdown);
        }
        finally { window.Close(); }
    });

    /// <summary>Raises a genuine <see cref="TextCompositionManager.PreviewTextInputEvent"/> for
    /// <paramref name="text"/> on <paramref name="rtb"/> — the same event WPF fires for a keystroke, so the
    /// editor's handler runs exactly as in the app.</summary>
    private static void RaiseTextInput(RichTextBox rtb, string text)
    {
        var composition = new TextComposition(InputManager.Current, rtb, text);
        rtb.RaiseEvent(new TextCompositionEventArgs(InputManager.Current.PrimaryKeyboardDevice, composition)
        {
            RoutedEvent = TextCompositionManager.PreviewTextInputEvent,
        });
    }

    /// <summary>A <see cref="TextPointer"/> <paramref name="textOffset"/> text characters into
    /// <paramref name="para"/>, counting Run text only (skipping element edges) so it maps to the block's
    /// source offset regardless of how many runs the paragraph rendered as.</summary>
    private static TextPointer PointerAtTextOffset(Paragraph para, int textOffset)
    {
        var tp = para.ContentStart;
        int remaining = textOffset;
        while (tp is not null && remaining > 0)
        {
            if (tp.GetPointerContext(LogicalDirection.Forward) == TextPointerContext.Text)
            {
                int len = tp.GetTextRunLength(LogicalDirection.Forward);
                if (len >= remaining) return tp.GetPositionAtOffset(remaining, LogicalDirection.Forward)!;
                remaining -= len;
                tp = tp.GetPositionAtOffset(len, LogicalDirection.Forward);
            }
            else tp = tp.GetNextContextPosition(LogicalDirection.Forward);
        }
        return tp ?? para.ContentEnd;
    }
}
