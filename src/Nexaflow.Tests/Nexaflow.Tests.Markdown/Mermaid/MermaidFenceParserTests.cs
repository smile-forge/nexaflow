using System.Linq;

using Nexaflow.Markdown.Ast;
using Nexaflow.Markdown.Mermaid;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.Markdown.Mermaid;

/// <summary>
/// A <c>mermaid</c> block is a diagram written in the language its header names: read no further than the header, and held whole
/// as written in that language, for the engine to have it read there. A header naming no diagram is a parse error.
/// </summary>
[TestClass]
[CoversNode("mermaid")]
public class MermaidFenceParserTests
{
    [TestMethod]
    public void TheWholeBlockIsHeldInTheLanguageItsHeaderNames()
    {
        const string source = "---\ntitle: T\n---\n%% note\ngraph TD\n  a --> b";
        var parse = ContentParse.Of(MermaidFenceParser.Parse(source));

        Assert.AreEqual(source, parse.Tree.Print(), "every character kept");
        Assert.AreEqual(("graph", source, 0), (parse.Nested.Single().Language, parse.Nested.Single().Written, parse.Nested.Single().At),
                        "the whole of it, named as its header names it");
    }

    [TestMethod]
    public void EveryKeywordOfADiagramNamesIt()
    {
        foreach (var keyword in new[] { "flowchart", "sequenceDiagram", "stateDiagram-v2", "pie", "C4Context", "xychart-beta" })
            Assert.AreEqual(keyword, ContentParse.Of(MermaidFenceParser.Parse(keyword + "\n")).Nested.Single().Language, keyword);
    }

    [TestMethod]
    public void ABlockWithNoHeaderYetIsAFlowchart()
    {
        Assert.AreEqual("flowchart", ContentParse.Of(MermaidFenceParser.Parse("")).Nested.Single().Language);
        Assert.AreEqual("flowchart", ContentParse.Of(MermaidFenceParser.Parse("%% nothing yet\n")).Nested.Single().Language);
    }

    [TestMethod]
    public void AHeaderNamingNoDiagramIsAParseError_MarkedOnItsKeyword()
    {
        const string source = "---\ntitle: T\n---\nwibble TD\n  a --> b";
        var tree = MermaidFenceParser.Parse(source);

        Assert.AreEqual(source, tree.Print());
        Assert.AreEqual(0, ContentParse.Of(tree).Nested.Count, "nothing is held in a language nothing names");

        var wrong = tree.SelfAndDescendants().Single(node => node.Trouble is not null);
        Assert.AreEqual("wibble", wrong.Text);
        StringAssert.Contains(wrong.Trouble, "is not a Mermaid diagram type");
    }
}
