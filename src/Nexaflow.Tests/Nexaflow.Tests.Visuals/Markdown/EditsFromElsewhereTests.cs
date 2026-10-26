using Nexaflow.Tests.Fixtures;
using Nexaflow.Visuals.Text.Markdown;

namespace Nexaflow.Tests.Visuals.Markdown;

/// <summary>
/// The document written in somewhere else — the raw half of a page showing it twice — and taken here as an edit of the same
/// document: a step that can be taken back here, and a search that goes on finding what it found. A different document is
/// still a different document, with nothing of the last one kept.
/// <para>Interactive desktop only — the editor builds its document during a render pass.</para>
/// </summary>
[TestClass]
[TestCategory("Desktop")]
[DoNotParallelize]
[CoversNode("markdown-block-undo")]
public class EditsFromElsewhereTests
{
    private const string Doc = "A zebra up top.\n\nThen some words.\n\nA zebra further down.\n";

    [TestMethod]
    public void AnEditWrittenElsewhere_IsAStepThatCanBeTakenBackHere() =>
        UiThread.Run(() => MarkdownEditorHarness.Run(Doc, editor =>
        {
            editor.TakeEdit(Doc.Replace("some words", "some more words", StringComparison.Ordinal));

            StringAssert.Contains(editor.Markdown, "some more words");
            Assert.IsTrue(editor.CanUndo, "it is a step, not another document");

            editor.Undo();
            Assert.AreEqual(Doc, editor.Markdown);
        }));

    [TestMethod]
    public void AndKeysWrittenOneAfterAnotherThereAreOneStep() =>
        UiThread.Run(() => MarkdownEditorHarness.Run(Doc, editor =>
        {
            var at = Doc.IndexOf("words", StringComparison.Ordinal);
            var written = Doc;

            foreach (var key in "many ")
            {
                written = written.Insert(at++, key.ToString());
                editor.TakeEdit(written);
            }

            StringAssert.Contains(editor.Markdown, "some many words");

            editor.Undo();
            Assert.AreEqual(Doc, editor.Markdown, "typed in one place, taken back at once");
        }));

    [TestMethod]
    public void ASearch_GoesOnFindingWhatItFound_WithTheOneLookedAtWhereItWent() =>
        UiThread.Run(() => MarkdownEditorHarness.Run(Doc, editor =>
        {
            Assert.AreEqual(2, editor.Find("zebra"));
            editor.Next();   // the second, further down
            var second = editor.Found[editor.At].Start;

            var written = "A zebra written in above.\n\n" + Doc;
            editor.TakeEdit(written);

            Assert.AreEqual(3, editor.Found.Count, "the one written in is found too");
            Assert.AreEqual(second + "A zebra written in above.\n\n".Length, editor.Found[editor.At].Start,
                "and the one looked at is still the one it was, where it went");
            Assert.AreEqual("zebra", written.Substring(editor.Found[editor.At].Start, 5));
        }));

    [TestMethod]
    public void ADifferentDocument_KeepsNothingOfTheLast() =>
        UiThread.Run(() => MarkdownEditorHarness.Run(Doc, editor =>
        {
            editor.Find("zebra");
            editor.TakeEdit(Doc + "\nMore.\n");

            editor.Markdown = "Another document altogether, with a zebra in it.\n";
            editor.UpdateLayout();

            Assert.AreEqual(0, editor.Found.Count, "nothing found in the last one is looked at in this one");
            Assert.IsFalse(editor.CanUndo, "and nothing of it can be taken back into this one");
        }));
}
