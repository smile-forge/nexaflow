using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Documents;
using Nexaflow.Markdown.Ast;
using Nexaflow.Markdown.Music.Abc;
using Nexaflow.Markdown.Prose;
using Nexaflow.Tests.Fixtures;
using Nexaflow.Visuals.Text.Editing;
using Nexaflow.Visuals.Text.Markdown;
using Nexaflow.Visuals.Text.Markdown.Languages;
using Nexaflow.Visuals.Text.Markdown.Music;
using Nexaflow.Visuals.Text.Markdown.Music.Abc;
using Nexaflow.Visuals.Text.Markdown.Prose;


namespace Nexaflow.Tests.Visuals.Markdown.Music.Abc;

/// <summary>
/// The ```abc fence, from markdown to an engraved tune in a document.
///
/// <para>
/// The offset is the other half. The engraver is handed the fence's <em>body</em>; an edit lands in the whole
/// document, fence lines and all. Without the bias every edit would land a couple of lines early, which is a fault
/// nothing but arithmetic can catch.
/// </para>
/// </summary>
[TestClass]
[TestCategory("UI")]
[CoversNode("abc-layout")]
[DoNotParallelize]
public class AbcFenceTests
{
    private const string Document = "Before it.\n\n```abc\nX:1\nK:C\nCDEF|\n```\n\nAfter it.\n";

    [TestMethod]
    public void TheFenceIsALanguageTheRendererKnows() =>
        Assert.IsTrue(ContentLanguages.Reads("abc"));

    [TestMethod]
    public void AndItEngravesInADocument() => UiThread.Run(() =>
    {
        var laid = Laying.Lay(null, Document, 700, StyleFormat.Dark);

        Assert.IsTrue(laid.Root.SelfAndDescendants().Any(piece => piece.Kind == MusicPiece.Page), "no score came out of the fence");
        Assert.IsFalse(laid.Root.SelfAndDescendants().Any(piece => piece.Kind == MarkdownPieces.Verbatim),
                       "and the fence is not shown as the characters it was written as");
    });

    [TestMethod]
    public void EveryNoteStandsWhereItWasWrittenInTheDocument() => UiThread.Run(() =>
    {
        // The engraver reads the fence's body, and every piece it draws has to name the characters in the whole
        // document: laid at the offset the body starts at, not at the top of it.
        var laid = Laying.Lay(null, Document, 700, StyleFormat.Dark);
        var notes = laid.Root.SelfAndDescendants().Where(piece => piece.Kind == "note").ToList();

        Assert.AreEqual(4, notes.Count);

        foreach (var note in notes)
        {
            var at = note.Sits();
            StringAssert.Contains("CDEF", Document.Substring(at.Start, at.Length),
                                  $"a note names {at.Start}+{at.Length}, which is not where a note was written");
        }
    });

    [TestMethod]
    public void TheProseAroundATuneIsSelectableLikeTheMusic() => UiThread.Run(() =>
    {
        // A title is words a reader wants to copy, and the words around a tune are exactly the ones they
        // reach for. They used to be TextBlocks stacked around the drawing on that reasoning — but a flow
        // document selects an embedded element whole or not at all, so the title was as unreachable as if it
        // had been painted. Engraved into the tree it names the field it came from, like a note head does.
        var tune = "X:1\nT:Speed the Plough\nT:a second title\nR:reel\nC:Trad.\nO:England\n"
                 + "S:Sussex\nK:G\nGABc dedB|\nW:a verse printed under the score\n";

        var layout = Laying.Engraved("abc", tune, 700, StyleFormat.Light);

        foreach (var (kind, text) in new[]
        {
            ("title", "Speed the Plough"),
            ("subtitle", "a second title"),
            ("rhythm", "reel"),
            ("credit", "Trad."),
            ("verse", "a verse printed under the score"),
        })
        {
            var piece = layout.Root.SelfAndDescendants()
                .FirstOrDefault(n => n.Kind == kind);

            Assert.IsTrue(piece.Exists, $"nothing was engraved for the {kind}");

            var at = piece.Sits();
            StringAssert.Contains(tune.Substring(at.Start, at.Length), text,
                $"the {kind} names {at.Start}+{at.Length}, which is not where it was written");

            // On the page rather than off the left of it, which is the failure a centred or right-aligned
            // piece has: the text engine is told the column, and where the letters land inside it is worked
            // out separately for the bounds a reader drags across.
            Assert.IsTrue(piece.Bounds.X >= 0 && piece.Bounds.Right <= 700 + 1,
                $"the {kind} was drawn at {piece.Bounds.X:F0}..{piece.Bounds.Right:F0} of a 700-wide page");
        }

        // Where a tune was collected is a fact ABOUT the tune rather than part of it, so it is read and
        // kept — a details panel is the place for it — and it is not drawn.
        Assert.IsFalse(layout.Root.SelfAndDescendants().Any(n => n.Kind == "source"));
        Assert.AreEqual("Sussex", AbcHeader.Of(ContentReading.Of(AbcPipeline.Read(tune))).Source);
    });

    [TestMethod]
    public void AndADragRunsFromTheHeadingIntoTheMusic() => UiThread.Run(() =>
    {
        // The point of engraving the words rather than stacking text around the drawing: one selection
        // model, so the two ends of a drag are the same kind of thing.
        var tune = "X:1\nT:Speed the Plough\nK:G\nGABc dedB|\n";
        var layout = Laying.Engraved("abc", tune, 700, StyleFormat.Light);

        var title = layout.Root.SelfAndDescendants().First(n => n.Kind == "title");
        var note = layout.Root.SelfAndDescendants().First(n => n.Kind == "note");

        var swept = ContentSelection.Between(layout.Root, title, note);

        Assert.IsFalse(swept.IsEmpty, "a drag from the title to a note selected nothing");
        Assert.IsTrue(swept.Pieces.Contains(title), "…and it did not include the title it started on");
    });
}
