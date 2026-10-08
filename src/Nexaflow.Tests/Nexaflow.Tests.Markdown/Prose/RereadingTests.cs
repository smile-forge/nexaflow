using System.Linq;

using Nexaflow.Markdown.Ast;
using Nexaflow.Markdown.Prose;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.Markdown.Prose;

/// <summary>
/// A reader kept for one document hands back what it read of every block written as it was — and what it hands back is
/// always what reading the document from nothing would have made.
/// </summary>
[TestClass]
[CoversNode("markdown-laid-again")]
public class RereadingTests
{
    private const string Document = "# Title\n\nFirst *words* here.\n\n- one\n- two\n\n| a | b |\n|---|---|\n| c | d |\n";

    [TestMethod]
    public void ReadingAgainAfterAnEditIsReadingFromNothing()
    {
        var parse = MarkdownParser.Parsing();
        parse(Document);

        var edited = Document.Replace("First", "The first");
        var again = parse(edited).Tree;

        Assert.IsTrue(again.Same(MarkdownParser.Parsing()(edited).Tree));
        Assert.AreEqual(edited, again.Print());
    }

    [TestMethod]
    public void ABlockWrittenAsItWasIsTheBlockReadLastTime()
    {
        var parse = MarkdownParser.Parsing();
        var before = Blocks(parse(Document).Tree);
        var after = Blocks(parse(Document.Replace("First", "The first")).Tree);

        Assert.AreSame(before[0], after[0], "the heading nobody touched, and nothing moved");
        Assert.AreNotSame(before[1], after[1], "the paragraph typed in");

        // The list and the table stand four characters further on than they did, and where a piece was read from is one
        // of the things it says — so each is the reading from last time moved along, which is the same tree and
        // necessarily not the same object.
        Assert.IsTrue(Moved(before[2], after[2], by: 4), "the list after it");
        Assert.IsTrue(Moved(before[3], after[3], by: 4), "the table after that");
    }

    [TestMethod]
    public void AndABlockThatMovedWasMovedRatherThanReadAgain()
    {
        // Moving a reading along carries what the reader worked out about it, object for object — so one of those answers
        // tells the two apart. How a list counts itself is the one to ask: nothing can read it off the markers
        // afterwards, so a reader asked for it again hands back a new answer rather than the same one.
        const string document = "Words.\n\n3. one\n4. two\n";
        var parse = MarkdownParser.Parsing();

        var before = Blocks(parse(document).Tree);
        var after = Blocks(parse(document.Replace("Words", "More words")).Tree);

        Assert.IsNotNull(Counted(before[1]), "the list says how it counts itself");
        Assert.AreSame(Counted(before[1]), Counted(after[1]), "the very answer the reader worked out, not one worked out again");
    }

    [TestMethod]
    public void ABlockFinishedAsItWasReadIsKeptFinished()
    {
        // What a block's parts make together, and the line break closing what it holds as written, are settled as the block is
        // read — so what is kept of it is the block as it was handed out, and none of it is worked out again. Each of these
        // now stands five characters further on than it did, so each is that block moved along.
        const string document = "Words.\n\n```csharp\nvar x = 1;\n```\n\n> [!NOTE]\n> Worth knowing.\n\nTerm\n:   what it means\n\n$$ x $$\n";
        var parse = MarkdownParser.Parsing();

        var before = Blocks(parse(document).Tree);
        var after = Blocks(parse(document.Replace("Words", "More words")).Tree);

        Assert.AreEqual(5, after.Length);
        Assert.AreNotSame(before[0], after[0], "the paragraph typed in");

        for (var at = 1; at < after.Length; at++) Assert.IsTrue(Moved(before[at], after[at], by: 5), after[at].Kind);
    }

    [TestMethod]
    public void ADefinitionWrittenAgainReadsEveryBlockAgain()
    {
        // Every block's words are read beside what the document defines, so a change there is a change to every block.
        const string defined = "Go [there][a].\n\n[a]: https://one.example\n";
        var parse = MarkdownParser.Parsing();

        var before = Blocks(parse(defined).Tree);
        var after = Blocks(parse(defined.Replace("one", "two")).Tree);

        Assert.AreNotSame(before[0], after[0]);
    }

    /// <summary>Whether <paramref name="now"/> is <paramref name="was"/> moved <paramref name="by"/> characters along, and nothing else.</summary>
    private static bool Moved(ContentNode was, ContentNode now, int by) => was.FurtherOn(by).Same(now);

    /// <summary>What the reader worked out about how a list counts itself.</summary>
    private static MarkdownNumbering? Counted(ContentNode list) =>
        list.SelfAndDescendants().Select(node => node.Held).OfType<MarkdownNumbering>().FirstOrDefault();

    private static ContentNode[] Blocks(ContentNode document) =>
        [.. document.Children.Where(child => !child.IsDerived && child.Role != Roles.Trivia)];
}
