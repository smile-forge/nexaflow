using System.Linq;

using Nexaflow.Markdown.Editing;
using Nexaflow.Tests.Fixtures;
using Nexaflow.Visuals.Text.Markdown;
using Nexaflow.Visuals.Text.Painting;

namespace Nexaflow.Tests.Visuals.Markdown;

/// <summary>
/// A page and the surface showing its document hold the one object: what the page loads or writes the surface shows, what the
/// reader writes the page hears as a change, and nothing of the text is handed back for it.
/// <para>Interactive desktop only — the editor builds its document during a render pass.</para>
/// </summary>
[TestClass]
[TestCategory("Desktop")]
[DoNotParallelize]
[CoversNode("markdown-long-writing")]
public class SurfaceDocumentTests
{
    [TestMethod]
    public void WhatThePageLoads_IsShown_AndWhatTheReaderWrites_IsThePagesToHear() => UiThread.Run(() =>
        MarkdownEditorHarness.Run(string.Empty, editor =>
        {
            var page = new EditedDocument("Some words.");
            editor.Document = page;
            editor.UpdateLayout();

            Assert.AreSame(page, editor.Document);
            Assert.AreEqual("Some words.", MarkdownEditorHarness.Showing(editor).Trim());

            var heard = 0;
            page.Changed += (_, _) => heard++;

            MarkdownEditorHarness.PlaceCaret(editor, "Some ".Length);
            MarkdownEditorHarness.Type(editor, "new ");

            Assert.AreEqual("Some new words.", page.ToString());
            Assert.AreEqual(4, heard, "a change for every letter, and the text never handed over to say so");
            Assert.IsTrue(page.IsDirty);
        }));

    [TestMethod]
    public void WhatThePageWrites_IsTakenUp_WithTheCaretCarriedThroughIt() => UiThread.Run(() =>
        MarkdownEditorHarness.Run("one two", editor =>
        {
            MarkdownEditorHarness.PlaceCaret(editor, "one two".Length);

            editor.Document.Write(ContentChange.Write(0, 0, "zero "));
            MarkdownEditorHarness.Type(editor, "!");

            Assert.AreEqual("zero one two!", editor.Document.ToString(), "the caret stayed after what it was after");
        }));

    [TestMethod]
    public void ANewPalette_KeepsWhatCanBeTakenBack_AndWhetherItWasSaved() => UiThread.Run(() =>
        MarkdownEditorHarness.Run("Some words.", editor =>
        {
            MarkdownEditorHarness.PlaceCaret(editor, "Some".Length);
            MarkdownEditorHarness.Type(editor, " more");

            editor.Palette = WpfStyles.Light;
            editor.UpdateLayout();

            Assert.IsTrue(editor.Document.IsDirty, "made again to be drawn in other colours, it is the same document");
            Assert.IsTrue(editor.CanUndo);

            editor.Undo();
            Assert.AreEqual("Some words.", editor.Document.ToString());
            Assert.IsFalse(editor.Document.IsDirty);
        }));
}
