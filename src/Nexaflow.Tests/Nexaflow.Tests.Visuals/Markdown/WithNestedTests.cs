using System.Linq;

using Nexaflow.Markdown.Ast;
using Nexaflow.Markdown.Prose;
using Nexaflow.Tests.Fixtures;
using Nexaflow.Visuals.Text.Editing;
using Nexaflow.Visuals.Text.Markdown;
using Nexaflow.Visuals.Text.Markdown.Prose;
using Nexaflow.Visuals.Text.Markdown.Stages;

namespace Nexaflow.Tests.Visuals.Markdown;

/// <summary>
/// Saying which language reads what is written inside a piece of content.
///
/// <para>
/// The point of doing it here is what it takes away from the builder: by the time a builder sees the node the
/// answer is on it, so the builder is handed a tree and asks nobody anything. What it does <em>not</em> take
/// away is how much room the nested content gets, which is settled when the builder asks — and these say so
/// both ways.
/// </para>
/// </summary>
[TestClass]
[CoversNode("diagram-nesting")]
public class WithNestedTests
{
    private const string Document = "A diagram:\n\n```mermaid\npie\n  \"a\" : 1\n```\n";

    [TestMethod]
    public void TheCharactersComingOutAreTheOnesThatWentIn()
    {
        // The one rule every stage lives under. What it hangs is derived: no width, printed as nothing.
        var read = MarkdownParser.Reader.Run(MarkdownParser.Read(Document));

        Assert.AreEqual(read.Print(), Stage().Run(read).Print());
        Assert.AreEqual(Document, Stage().Run(read).Print());
    }

    [TestMethod]
    public void AFenceIsToldWhatReadsIt()
    {
        var fence = Fence(Stage().Run(MarkdownParser.Reader.Run(MarkdownParser.Read(Document))));

        Assert.IsNotNull(ContentNesting.Of(fence));
    }

    [TestMethod]
    public void AFenceInALanguageNothingReadsIsToldNothing()
    {
        var read = MarkdownParser.Reader.Run(MarkdownParser.Read("```nothing-reads-this\nx\n```\n"));

        Assert.IsNull(ContentNesting.Of(Fence(Stage().Run(read))));
    }

    [TestMethod]
    public void RunningItTwiceIsTheSameAsRunningItOnce()
    {
        var once = Stage().Run(MarkdownParser.Reader.Run(MarkdownParser.Read(Document)));

        Assert.IsTrue(once.Same(Stage().Run(once)));
    }

    [TestMethod]
    public void TheBuilderChoosesTheRoomAndTheStageDoesNot()
    {
        // The whole of why the stage hangs a language rather than a finished tree: a fence inside a quote or a
        // table cell has less room than one at the margin, and only the builder knows that.
        var fence = Fence(Stage().Run(MarkdownParser.Reader.Run(MarkdownParser.Read(Document))));
        var nesting = ContentNesting.Of(fence)!;
        var body = fence.Part(Roles.Body);

        var narrow = nesting.At(body, 120);
        var wide = nesting.At(body, 600);

        Assert.IsNotNull(narrow);
        Assert.IsNotNull(wide);
        Assert.AreNotEqual(narrow!.Width, wide!.Width, "it was laid twice, at the two sizes it was asked for");
    }

    [TestMethod]
    public void ADocumentStillDrawsWhatItsFenceNames()
    {
        var laid = MarkdownBuilder.Lay(Document, StyleFormat.Dark, 480);

        Assert.AreEqual(0, laid.Root.SelfAndDescendants().Count(piece => piece.Kind == MarkdownPieces.Verbatim),
            "the diagram was laid by its own language, not shown as characters");
        Assert.IsTrue(laid.Size.Height > 0);
    }

    // ── Reading the answers ─────────────────────────────────────────────────

    private static WithNested Stage() => new(StyleFormat.Dark);

    private static ContentPart Fence(ContentNode tree) =>
        ContentPart.Of(tree).SelfAndDescendants().First(part => part.Kind == MarkdownKinds.Fence);
}
