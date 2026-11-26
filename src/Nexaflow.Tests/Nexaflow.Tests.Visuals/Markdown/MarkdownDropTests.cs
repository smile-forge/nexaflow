using System;
using System.Linq;
using System.Windows;

using Nexaflow.Interaction;
using Nexaflow.Tests.Fixtures;
using Nexaflow.Visuals.Text.Editing;
using Nexaflow.Visuals.Text.Markdown;

namespace Nexaflow.Tests.Visuals.Markdown;

/// <summary>
/// Something dragged onto the editor from another window. A drop carries whatever the thing dragged put in it, and
/// turning that into something a document can be written from is the host's: the surface asks, then writes what it is
/// told where the pointer let it go — or writes nothing, where the host dealt with the drop itself.
///
/// <para>
/// These show a real window, because a drop is a gesture a window receives. That is what keeps them out of
/// <see cref="NexaTextSurfaceTests"/> and in a file of their own.
/// </para>
/// </summary>
[TestClass]
[TestCategory("Desktop")]
[DoNotParallelize]
[CoversNode("markdown-drop-text")]
public class MarkdownDropTests
{
    private const string Doc = "# Getting Started\n\nSome words about chrome.\n\n## Notes\n\n1. one\n1. two\n";

    [TestMethod]
    public void TextDraggedInFromAnotherWindowLandsWhereItWasLetGo() => UiThread.Run(() => MarkdownEditorHarness.Run(Doc, editor =>
    {
        editor.Dropping += (_, e) =>
        {
            e.Words = e.Data.Text();
            e.Markdown = e.Words;
            e.Handled = true;
        };

        editor.DropContent(new DragPayload((DragKinds.Text, "opera")), Over(editor, "chrome"));

        var sentence = Doc.IndexOf("Some words about", StringComparison.Ordinal);

        StringAssert.Contains(MarkdownEditorHarness.Showing(editor), "opera", "written in, not dropped on the floor");
        Assert.IsTrue(editor.Shown.Caret > sentence && editor.Shown.Caret <= sentence + "Some words about chrome.opera".Length,
                      $"in the sentence it was let go in, which left the caret at {editor.Shown.Caret}");
    }));

    [TestMethod]
    public void AHostThatClaimsADropKeepsIt() => UiThread.Run(() => MarkdownEditorHarness.Run(Doc, editor =>
    {
        // An image or a file is the host's to deal with: it says so by marking the drop handled having named neither
        // words nor markdown, and nothing of it is written here.
        var before = MarkdownEditorHarness.Showing(editor);
        editor.Dropping += (_, e) => e.Handled = true;

        editor.DropContent(new DragPayload((DragKinds.Files, new[] { @"C:\pets.png" })), Over(editor, "chrome"));

        Assert.AreEqual(before, MarkdownEditorHarness.Showing(editor), "the host kept it, so the document reads as it did");
    }));

    [TestMethod]
    public void TextNoHostClaims_IsWrittenWhereItWasLetGo() => UiThread.Run(() => MarkdownEditorHarness.Run(Doc, editor =>
    {
        editor.DropContent(new DragPayload((DragKinds.Text, "opera")), Over(editor, "chrome"));

        StringAssert.Contains(MarkdownEditorHarness.Showing(editor), "opera", "an editor with no host of its own still takes words");
    }));

    [TestMethod]
    public void TextLetGoBelowTheLastLine_IsWrittenAtTheEnd() => UiThread.Run(() => MarkdownEditorHarness.Run(Doc, editor =>
    {
        editor.DropContent(new DragPayload((DragKinds.Text, "opera")), new Point(5, 100_000));

        StringAssert.Contains(MarkdownEditorHarness.Showing(editor), "opera", "a short note is mostly the room below its words");
    }));

    [TestMethod]
    public void FilesNoHostClaims_AreNotWrittenAndNotOffered() => UiThread.Run(() => MarkdownEditorHarness.Run(Doc, editor =>
    {
        var before = MarkdownEditorHarness.Showing(editor);
        var files = new DragPayload((DragKinds.Files, new[] { @"C:\pets.png" }), (DragKinds.Text, @"C:\pets.png"));

        Assert.IsNull(editor.OfferDrop(new DropRequest(files, default)), "left to the surface around it");
        editor.DropContent(files, Over(editor, "chrome"));

        Assert.AreEqual(before, MarkdownEditorHarness.Showing(editor));
    }));

    /// <summary>A point on the editor over the piece <paramref name="word"/> is drawn as.</summary>
    private static Point Over(NexaTextSurface editor, string word)
    {
        var at = Doc.IndexOf(word, StringComparison.Ordinal);
        var piece = editor.Shown.Laid.Root.SelfAndDescendants()
            .First(one => one.IsLeaf && one.Sits().Start <= at && one.Sits().Start + one.Sits().Length > at);

        var box = piece.Bounds;

        return editor.Shown.TranslatePoint(new Point(box.Left + (box.Width / 2), box.Top + (box.Height / 2)), editor);
    }
}
