using System.Collections.Generic;
using System.Linq;

using Nexaflow.Markdown.Editing;
using Nexaflow.Tests.Fixtures;
using Nexaflow.Visuals.Text.Markdown;

namespace Nexaflow.Tests.Visuals.Markdown;

/// <summary>
/// What a page is told when what is written changes: the change itself, both when it is written and when it is taken back —
/// read off the version the edit made, so nothing is compared to say it.
/// </summary>
[TestClass]
[TestCategory("Desktop")]
[DoNotParallelize]
[CoversNode("markdown-block-undo")]
public class SourceChangeTests
{
    [TestMethod]
    public void WritingAndTakingBackAreToldAsTheChangesTheyMade() => UiThread.Run(() =>
        MarkdownEditorHarness.Run("Some words.", editor =>
        {
            var told = new List<ContentSourceChange>();
            editor.SourceChanged += (_, args) => told.Add(args.Change);

            MarkdownEditorHarness.PlaceCaret(editor, "Some ".Length);
            MarkdownEditorHarness.Type(editor, "new ");
            editor.Undo();

            Assert.AreEqual("Some words.", editor.Markdown);

            var written = told.Where(change => change.Kind == ContentChangeKind.Written).ToList();
            Assert.AreEqual(4, written.Count, "a letter at a time");
            Assert.AreEqual(new ContentWrite("Some ".Length, 0, "n"), written[0].Change.Writes.Single());
            Assert.IsTrue(written[0].After.Document.MadeFrom(written[0].Before.Document), "the version the letter made, not one compared to find it");

            var undone = told.Single(change => change.Kind == ContentChangeKind.Undone);
            Assert.AreEqual(new ContentWrite("Some ".Length, 4, string.Empty), undone.Change.Writes.Single(), "the whole stretch typed, taken back");
            Assert.AreEqual(("Some ".Length, 4, 0), undone.Span);
        }));
}
