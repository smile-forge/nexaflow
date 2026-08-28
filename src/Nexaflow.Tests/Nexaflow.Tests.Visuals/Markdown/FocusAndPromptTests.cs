using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Nexaflow.Tests.Fixtures;
using Nexaflow.Visuals.Text.Markdown;

namespace Nexaflow.Tests.Visuals.Markdown;

/// <summary>
/// Asking the editor for the keyboard, and what the prompt does about it.
///
/// <para>
/// Both of these failed silently, which is why they kept coming back. <c>Focus()</c> on the control
/// returned false and did nothing, because the control was not focusable and the text inside it is
/// private — so a host asking for the caret got no caret, no error, and no way to tell which of the
/// several plausible causes it was. And the prompt was only reconsidered when the document was rebuilt
/// or the model pushed, never when focus changed, so once hidden it stayed hidden.
/// </para>
///
/// Shows a real (off-screen, unactivated) window, because focus is only real in a shown one.
/// </summary>
[TestClass]
[TestCategory("Desktop")]
[DoNotParallelize]
[CoversNode("markdown-inline-editor")]
public class FocusAndPromptTests
{
    [TestMethod]
    public void AskingTheEditorForTheKeyboardWorks()
    {
        UiThread.Run(() => MarkdownEditorHarness.Run("some prose", (editor, rtb) =>
        {
            Keyboard.ClearFocus();
            Assert.IsFalse(rtb.IsKeyboardFocusWithin, "nothing has it to begin with");

            // Focus() comes back false, and that is right: focus went past this control to the text
            // inside it, so the control itself does not hold it. Where it ended up is the question.
            editor.Focus();
            Assert.IsTrue(rtb.IsKeyboardFocusWithin,
                "it passes focus through to the text — a host should not have to reach past the "
                + "control to something private to put a caret in it");
        }));
    }

    [TestMethod]
    public void ThePromptStaysUntilSomethingIsWritten()
    {
        UiThread.Run(() => MarkdownEditorHarness.Run(string.Empty, (editor, rtb) =>
        {
            editor.Placeholder = "Type a formula…";

            // Focus does not silence it. It used to, which went unnoticed for as long as nothing
            // focused the editor on the way in — and once something did, the prompt was never seen at
            // all. The field you have just been put into is exactly the one that needs to say what
            // goes in it.
            editor.Focus();
            Assert.AreEqual(Visibility.Visible, PromptIn(editor).Visibility, "still nothing written");

            // Text pushed in from outside is ignored while the editor has the keyboard — rebuilding
            // its document mid-word would destroy what is being typed into — so give it up first.
            Keyboard.ClearFocus();
            editor.Markdown = "something";

            Assert.AreEqual(Visibility.Collapsed, PromptIn(editor).Visibility,
                "content silences it, which is the only thing it was ever standing in for");
        }));
    }

    [TestMethod]
    public void ThePromptStartsWhereTheTextWould()
    {
        // It sits over the document rather than in it, so the page padding does not reach it. Left
        // alone it began hard against the corner while the text it stands in for began inset.
        UiThread.Run(() => MarkdownEditorHarness.Run(string.Empty, (editor, _) =>
        {
            editor.Placeholder = "Type a formula…";
            editor.ContentPadding = new Thickness(10, 8, 0, 0);

            Assert.AreEqual(new Thickness(10, 8, 0, 0), PromptIn(editor).Margin);
        }));
    }

    private static TextBlock PromptIn(InlineMarkdownEditor editor) =>
        ((Grid)editor.Content).Children.OfType<TextBlock>().Single();
}
