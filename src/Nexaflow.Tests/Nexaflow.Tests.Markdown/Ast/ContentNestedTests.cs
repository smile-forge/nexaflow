using System.Linq;

using Nexaflow.Markdown.Ast;
using Nexaflow.Markdown.Mermaid;
using Nexaflow.Markdown.Prose;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.Markdown.Ast;

/// <summary>
/// Content written in another language is a block in that language — its delimiters, and its characters held as written, unread —
/// and every parse is a block in its own language at its root. Nothing is read in another language until a builder asks for it.
/// </summary>
[TestClass]
[CoversNode("markdown-text")]
public class ContentNestedTests
{
    private static ContentNode[] Holders(ContentNode tree) =>
        [.. tree.SelfAndDescendants().Where(node => ContentNested.Language(node) is not null)];

    [TestMethod]
    public void AFenceIsABlockInItsLanguage_HoldingItsCharactersUnread()
    {
        const string source = "```mermaid\npie\n  \"a\" : 1\n```\n";
        var fence = Holders(MarkdownParser.Parsing()(source).Tree).Single();

        Assert.IsInstanceOfType<BlockNode>(fence);
        Assert.AreEqual(("mermaid", Kinds.Block), (ContentNested.Language(fence), fence.Kind));
        CollectionAssert.AreEqual(new[] { "```mermaid\n", "pie\n  \"a\" : 1\n", "```\n" }, fence.Children.Select(child => child.Print()).ToArray(),
                                  "the fence line, the characters in the language it names, and the closing fence");

        var body = fence.Children.Single(child => child.Kind == Kinds.Nested);
        Assert.IsTrue(body.IsLeaf, "held as written: nothing is read in another language until a builder asks for it");
    }

    [TestMethod]
    public void EveryPieceInAnotherLanguageIsABlockNamingIt_WhereverItIsWritten()
    {
        const string source = "Words $x^2$ here.\n\n```mermaid\npie\n  \"a\" : 1\n```\n\n$$\na+b\n$$\n\n```\nplain\n```\n";

        CollectionAssert.AreEqual(new[] { ("latex", "x^2"), ("mermaid", "pie\n  \"a\" : 1\n"), ("latex", "a+b\n") },
                                  Holders(MarkdownParser.Parsing()(source).Tree)
                                      .Select(holder => (ContentNested.Language(holder)!, holder.Part(Roles.Body)!.Text)).ToArray(),
                                  "in a sentence, in a fence, between dollars; a fence naming no language holds none");
    }

    [TestMethod]
    public void EveryParseIsABlockInItsOwnLanguageAtItsRoot()
    {
        Assert.AreEqual(MarkdownParser.Language, ((BlockNode)MarkdownParser.Parsing()("One line.\n").Tree).Language);
        Assert.AreEqual("pie", ((BlockNode)MermaidParser.Parse("pie\n  \"a\" : 1\n")).Language);
        Assert.AreEqual("graph", ((BlockNode)MermaidParser.Parse("graph TD\n  a --> b\n")).Language);
    }

    [TestMethod]
    public void ADiagramLabelInAnotherLanguageIsABlockInIt()
    {
        const string source = "graph TD\n  a[\"```latex x^2 + y^2\"] --> b[\"Next\"]\n";

        var label = Holders(MermaidParser.Parse(source)).Single();

        Assert.AreEqual("latex", ContentNested.Language(label));
        Assert.AreEqual("x^2 + y^2", label.Part(Roles.Body)!.Text);
    }
}
