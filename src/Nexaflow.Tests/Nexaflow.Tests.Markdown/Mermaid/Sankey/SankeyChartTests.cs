using Nexaflow.Markdown.Mermaid.Sankey;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.Markdown.Mermaid.Sankey;

/// <summary>
/// What a <c>sankey-beta</c> block's tree is read back into: the flows written in it, the nodes they are written between,
/// and what each of those is worth.
/// </summary>
[TestClass]
[CoversNode("sankey-ast")]
public class SankeyChartTests
{
    [TestMethod]
    public void TheNodesAreTheNamesTheFlowsAreWrittenBetween_InTheOrderTheyAreFirstWritten()
    {
        var chart = SankeyChart.Read(SankeyGrammarTests.Energy);

        CollectionAssert.AreEqual(new[] { "Agricultural 'waste'", "Bio-conversion", "Liquid", "Losses", "Solid", "Gas" },
                                  chart.Nodes.Select(node => node.Name).ToArray());
        Assert.AreEqual(5, chart.Flows.Count);
        CollectionAssert.AreEqual(new[] { 0, 1, 2, 3, 4, 5 }, chart.Nodes.Select(node => node.Order).ToArray());
    }

    [TestMethod]
    public void ANodeIsWorthWhateverFlowsIntoItOrOutOfIt_WhicheverIsTheMore()
    {
        var chart = SankeyChart.Read(SankeyGrammarTests.Energy);
        var conversion = chart.Node("Bio-conversion")!;

        Assert.AreEqual(124.729, chart.Into(conversion), 1e-9);
        Assert.AreEqual(388.925, chart.OutOf(conversion), 1e-9);
        Assert.AreEqual(388.925, chart.Worth(conversion), 1e-9);
        Assert.AreEqual(26.862, chart.Worth(chart.Node("Losses")!), 1e-9);
    }

    [TestMethod]
    public void ANameInQuotesSaysWhatIsBetweenThem_AQuoteWrittenTwiceStandingForOne()
    {
        Assert.AreEqual("Waste, agricultural", SankeyChart.Read("sankey-beta\n\"Waste, agricultural\",b,10").Nodes[0].Name);
        Assert.AreEqual("Agricultural \"waste\"", SankeyChart.Read("sankey-beta\n\"Agricultural \"\"waste\"\"\",b,10").Nodes[0].Name);
        Assert.AreEqual("a", SankeyChart.Read("sankey-beta\n  a , b , 10").Nodes[0].Name, "and the space round a field is not part of it");
    }

    [TestMethod]
    public void AFlowWorthNothingIsReadButNotDrawn()
    {
        var chart = SankeyChart.Read("sankey-beta\na,b,10\nb,c,0\nc,d,lots");

        Assert.AreEqual(3, chart.Flows.Count, "every row written is a flow, whatever is wrong with it");
        CollectionAssert.AreEqual(new[] { true, false, false }, chart.Flows.Select(flow => flow.Drawn).ToArray());
        Assert.IsNotNull(chart.Flows[2].Trouble);
        Assert.AreEqual(0, chart.Worth(chart.Node("d")!), "so nothing it was worth reaches the node it names");
    }

    [TestMethod]
    public void TheFrontMattersOptionsAreRead()
    {
        Assert.AreEqual(SankeyLinkColour.Gradient, SankeyChart.Read("sankey-beta\na,b,10").Config.LinkColour);

        var config = SankeyChart.Read(
            "---\nconfig:\n  sankey:\n    width: 800\n    height: 400\n    linkColor: source\n    nodeAlignment: left\n"
            + "    showValues: false\n    prefix: \"$\"\n    suffix: B\n    nodeWidth: 14\n    nodePadding: 20\n    labelStyle: outlined\n"
            + "    nodeColors:\n      a: \"#ff0000\"\n---\nsankey-beta\n\na,b,10").Config;

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
        var config = SankeyChart.Read("---\nconfig:\n  sankey:\n    linkColor: \"#7f7f7f\"\n---\nsankey-beta\n\na,b,10").Config;

        Assert.AreEqual(SankeyLinkColour.Written, config.LinkColour);
        Assert.AreEqual("#7f7f7f", config.LinkWritten);
    }

    [TestMethod]
    public void ATitleIsTheFrontMatters_SankeyHavingNoTitleLineOfItsOwn() =>
        Assert.AreEqual("Where it goes", SankeyChart.Read("---\ntitle: Where it goes\n---\nsankey-beta\n\na,b,10").Block.Title?.Text);
}
