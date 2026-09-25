using Nexaflow.Markdown.Mermaid;
using Nexaflow.Markdown.Mermaid.Sankey;
using Nexaflow.Tests.Fixtures;
using Nexaflow.Tests.Markdown.Mermaid;

namespace Nexaflow.Tests.Markdown.Mermaid.Sankey;

/// <summary>What a <c>sankey-beta</c> block's front matter asks for, as its stages hang it on the block.</summary>
[TestClass]
[CoversNode("sankey-ast")]
public class SankeyConfigTests
{
    private static SankeyConfig Config(string source) => ((ConfiguredNode<SankeyConfig>)MermaidStaged.Read(source)).Config;

    [TestMethod]
    public void TheFrontMattersOptionsAreRead()
    {
        Assert.AreEqual(SankeyLinkColour.Gradient, Config("sankey-beta\na,b,10").LinkColour);

        var config = Config("---\nconfig:\n  sankey:\n    width: 800\n    height: 400\n    linkColor: source\n    nodeAlignment: left\n"
            + "    showValues: false\n    prefix: \"$\"\n    suffix: B\n    nodeWidth: 14\n    nodePadding: 20\n    labelStyle: outlined\n"
            + "    nodeColors:\n      a: \"#ff0000\"\n---\nsankey-beta\n\na,b,10");

        Assert.AreEqual(800, config.Width);
        Assert.AreEqual(400, config.Height);
        Assert.AreEqual(SankeyLinkColour.Source, config.LinkColour);
        Assert.AreEqual(SankeyAlignment.Left, config.Alignment);
        Assert.IsFalse(config.ShowValues);
        Assert.AreEqual("$", config.Prefix);
        Assert.AreEqual("B", config.Suffix);
        Assert.AreEqual(14, config.NodeWidth);
        Assert.AreEqual(20, config.NodePadding);
        Assert.AreEqual(SankeyLabels.Outlined, config.Labels);
        Assert.AreEqual("#ff0000", config.NodeColours["a"]);
    }

    [TestMethod]
    public void AColourWrittenForTheFlowsIsTheOneTheyAllTake()
    {
        var config = Config("---\nconfig:\n  sankey:\n    linkColor: \"#7f7f7f\"\n---\nsankey-beta\n\na,b,10");

        Assert.AreEqual(SankeyLinkColour.Written, config.LinkColour);
        Assert.AreEqual("#7f7f7f", config.LinkWritten);
    }
}
