using System.Linq;

using Nexaflow.Markdown.Ast;
using Nexaflow.Markdown.Mermaid;
using Nexaflow.Markdown.Prose;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.Markdown.Ast;

/// <summary>
/// A parse says where every piece written in another language is: its body's own characters, where they start, and what
/// they are written in — so nothing has to walk the tree to find them.
/// </summary>
[TestClass]
[CoversNode("markdown-text")]
public class ContentNestedTests
{
    [TestMethod]
    public void AParseSaysWhereEveryPieceInAnotherLanguageIs()
    {
        const string source = "Words $x^2$ here.\n\n```mermaid\npie\n  \"a\" : 1\n```\n\n$$\na+b\n$$\n\n```\nplain\n```\n";

        var parse = MarkdownParser.Parsing()(source);

        CollectionAssert.AreEqual(
            new[] { ("latex", "x^2"), ("mermaid", "pie\n  \"a\" : 1"), ("latex", "a+b") },
            parse.Nested.Select(piece => (piece.Language, piece.Written)).ToArray(),
            "in the order written; a fence naming no language holds none");

        foreach (var piece in parse.Nested)
            Assert.AreEqual(piece.Written, source.Substring(piece.At, piece.Written.Length), $"{piece.Language} starts where its characters are");
    }

    [TestMethod]
    public void EveryNodeCountsThePiecesItHolds()
    {
        var tree = MarkdownParser.Parsing()("One $a$ and $b$.\n\nNone here.\n").Tree;

        Assert.AreEqual(2, tree.Nests);
        Assert.AreEqual(2, tree.Children[0].Nests, "the paragraph holding both");
        Assert.AreEqual(0, tree.Children.Last(child => child.Role != Roles.Trivia && !child.IsDerived).Nests, "and the one holding none");
    }

    [TestMethod]
    public void ADiagramSaysWhereAnotherLanguageIsWrittenInItsLabels()
    {
        const string source = "graph TD\n  a[\"```latex x^2 + y^2\"] --> b[\"Next\"]\n";

        var pieces = ContentNested.Pieces(MermaidParser.Parse(source));

        Assert.AreEqual(1, pieces.Count);
        Assert.AreEqual("latex", pieces[0].Language);
        Assert.AreEqual(pieces[0].Written, source.Substring(pieces[0].At, pieces[0].Written.Length));
    }

    [TestMethod]
    public void ContentWithNothingInAnotherLanguageSaysSoAtOnce() =>
        Assert.AreEqual(0, ContentParse.Of(MermaidParser.Parse("pie\n  \"a\" : 1\n")).Nested.Count);
}
