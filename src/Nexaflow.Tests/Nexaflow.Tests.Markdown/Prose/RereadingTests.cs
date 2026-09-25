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

        Assert.AreSame(before[0], after[0], "the heading nobody touched");
        Assert.AreNotSame(before[1], after[1], "the paragraph typed in");
        Assert.AreSame(before[2], after[2], "the list after it");
        Assert.AreSame(before[3], after[3], "the table after that");
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

    private static ContentNode[] Blocks(ContentNode document) =>
        [.. document.Children.Where(child => !child.IsDerived && child.Role != Roles.Trivia)];
}
