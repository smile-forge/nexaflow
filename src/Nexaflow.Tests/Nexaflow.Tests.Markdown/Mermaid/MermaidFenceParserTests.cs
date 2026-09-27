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
    private static BlockNode Read(string source) => (BlockNode)MermaidFenceParser.Parse(source);

    [TestMethod]
    public void TheWholeBlockIsHeldInTheLanguageItsHeaderNames()
    {
        const string source = "---\ntitle: T\n---\n%% note\ngraph TD\n  a --> b";
        var block = Read(source);

        Assert.AreEqual(source, block.Print(), "every character kept");
        Assert.AreEqual("graph", block.Language, "named as its header names it");
        Assert.AreEqual(source, block.Children.Single(child => child.Kind == Kinds.Nested).Text, "the whole of it, unread");
    }

    [TestMethod]
    public void EveryKeywordOfADiagramNamesIt()
    {
        foreach (var keyword in new[] { "flowchart", "sequenceDiagram", "stateDiagram-v2", "pie", "C4Context", "xychart-beta" })
            Assert.AreEqual(keyword, Read(keyword + "\n").Language, keyword);
    }

    [TestMethod]
    public void ABlockWithNoHeaderYetIsAFlowchart()
    {
        Assert.AreEqual("flowchart", Read("").Language);
        Assert.AreEqual("flowchart", Read("%% nothing yet\n").Language);
    }

    [TestMethod]
    public void AHeaderNamingNoDiagramIsAParseError_MarkedOnItsKeyword()
    {
        const string source = "---\ntitle: T\n---\nwibble TD\n  a --> b";
        var block = Read(source);

        Assert.AreEqual(source, block.Print());
        Assert.IsNull(ContentNested.Language(block), "nothing is held in a language nothing names");

        var wrong = block.SelfAndDescendants().Single(node => node.Trouble is not null);
        Assert.AreEqual("wibble", wrong.Text);
        StringAssert.Contains(wrong.Trouble, "is not a Mermaid diagram type");
    }
}
