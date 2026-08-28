using System.Linq;
using System.Windows.Documents;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.Visuals.Markdown;

/// <summary>
/// Block-level undo in the inline markdown editor: a snapshot is taken at the <i>start</i> of each block's
/// editing session, so every edit made inside one block collapses into a single undo step (Word-style
/// per-keystroke undo would make a long paragraph unusable to step back through). Moving to another block
/// starts a new step, and undoing past the first step is a no-op rather than an exception.
///
/// Interactive desktop only — the editor builds its document in a render pass.
/// Run with --filter "TestCategory=UI".
/// </summary>
[TestClass]
[TestCategory("Desktop")]
[DoNotParallelize]   // spins an off-screen Window; concurrent WPF layout and focus make it flaky
[CoversNode("markdown-block-undo")]
public class InlineMarkdownEditorUndoTests
{
    [TestMethod]
    public void Undo_RestoresTheBlockAsItWasBeforeTheEdit()
        => UiThread.Run(() => MarkdownEditorHarness.Run("A paragraph.", (editor, rtb) =>
        {
            var para = rtb.Document.Blocks.OfType<Paragraph>().First();
            MarkdownEditorHarness.PlaceCaret(rtb, para, 0);
            MarkdownEditorHarness.Type(rtb, "New ");
            Assert.AreEqual("New A paragraph.", editor.Markdown, "Precondition: the edit landed.");

            editor.Undo();

            Assert.AreEqual("A paragraph.", editor.Markdown);
        }));

    [TestMethod]
    public void ManyEditsInOneBlock_CollapseIntoASingleUndoStep()
        => UiThread.Run(() => MarkdownEditorHarness.Run("A paragraph.", (editor, rtb) =>
        {
            var para = rtb.Document.Blocks.OfType<Paragraph>().First();
            MarkdownEditorHarness.PlaceCaret(rtb, para, 0);
            MarkdownEditorHarness.Type(rtb, "Several words typed ");

            editor.Undo();   // one step, not one per character

            Assert.AreEqual("A paragraph.", editor.Markdown);
        }));

    [TestMethod]
    public void EachBlockEdited_IsItsOwnUndoStep()
        => UiThread.Run(() => MarkdownEditorHarness.Run("First block.\n\nSecond block.", (editor, rtb) =>
        {
            var paragraphs = rtb.Document.Blocks.OfType<Paragraph>().ToList();
            Assert.IsTrue(paragraphs.Count >= 2, "Precondition: two rendered blocks.");

            MarkdownEditorHarness.PlaceCaret(rtb, paragraphs[0], 0);
            MarkdownEditorHarness.Type(rtb, "A");
            MarkdownEditorHarness.PlaceCaret(rtb, rtb.Document.Blocks.OfType<Paragraph>().ElementAt(1), 0);
            MarkdownEditorHarness.Type(rtb, "B");
            Assert.AreEqual("AFirst block.\n\nBSecond block.", editor.Markdown, "Precondition: both edits landed.");

            editor.Undo();
            Assert.AreEqual("AFirst block.\n\nSecond block.", editor.Markdown, "Undo should drop only the second block's session.");

            editor.Undo();
            Assert.AreEqual("First block.\n\nSecond block.", editor.Markdown);
        }));

    [TestMethod]
    public void Undo_WithNothingToUndo_IsANoOp()
        => UiThread.Run(() => MarkdownEditorHarness.Run("Untouched.", (editor, _) =>
        {
            editor.Undo();
            editor.Undo();

            Assert.AreEqual("Untouched.", editor.Markdown);
        }));
}
