using Nexaflow.Markdown.Mermaid;
using Nexaflow.Markdown.Mermaid.Flowchart;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.Markdown.Mermaid.Flowchart;

/// <summary>
/// What a flowchart's lines add up to: the nodes written in it, the links between them, the subgraphs they are gathered into,
/// the way it is laid out and what each node is styled with.
/// </summary>
[TestClass]
[CoversNode("flowchart-ast")]
public class FlowchartDiagramTests
{
    [TestMethod]
    public void TheWayItIsLaidOutIsWhatTheHeaderSays()
    {
        Assert.AreEqual(FlowchartWay.Down, FlowchartDiagram.Read("flowchart TD\n  a --> b").Way);
        Assert.AreEqual(FlowchartWay.Down, FlowchartDiagram.Read("flowchart TB\n  a --> b").Way);
        Assert.AreEqual(FlowchartWay.Up, FlowchartDiagram.Read("flowchart BT\n  a --> b").Way);
        Assert.AreEqual(FlowchartWay.Right, FlowchartDiagram.Read("graph LR\n  a --> b").Way);
        Assert.AreEqual(FlowchartWay.Left, FlowchartDiagram.Read("flowchart RL\n  a --> b").Way);
        Assert.AreEqual(FlowchartWay.Down, FlowchartDiagram.Read("flowchart\n  a --> b").Way, "down, where the header says nothing");
    }

    [TestMethod]
    public void TheNodesAreTheOnesWrittenInTheOrderTheyAreFirstWritten()
    {
        var nodes = FlowchartDiagram.Read("flowchart LR\n  a --> b\n  c --> a").Nodes;

        CollectionAssert.AreEqual(new[] { "a", "b", "c" }, nodes.Select(node => node.Id).ToArray());
    }

    [TestMethod]
    public void ANodeWrittenAgainSaysMoreAboutTheSameNode()
    {
        var diagram = FlowchartDiagram.Read("flowchart LR\n  a --> b\n  a[\"Said later\"]\n  b((\"Round later\"))");

        Assert.AreEqual(2, diagram.Nodes.Count, "a node written twice is one node");
        Assert.AreEqual("Said later", diagram.Find("a")!.Said?.Text);
        Assert.AreEqual(MermaidShape.Circle, diagram.Find("b")!.Shape);
    }

    [TestMethod]
    public void ANodeSaysItsIdWhereNothingElseIsWrittenOnIt()
    {
        var diagram = FlowchartDiagram.Read("flowchart LR\n  alpha --> beta[\"The end\"]");

        Assert.AreEqual("alpha", diagram.Find("alpha")!.Said?.Text);
        Assert.AreEqual("The end", diagram.Find("beta")!.Said?.Text);
    }

    [TestMethod]
    public void ALinkJoinsTheNodesEitherSideOfIt()
    {
        var links = FlowchartDiagram.Read("flowchart LR\n  a --> b\n  b -.-> c").Links;

        CollectionAssert.AreEqual(new[] { "a", "b" }, links.Select(link => link.From).ToArray());
        CollectionAssert.AreEqual(new[] { "b", "c" }, links.Select(link => link.To).ToArray());
        Assert.AreEqual(MermaidLineStyle.Dotted, links[1].Style);
    }

    [TestMethod]
    public void AChainOfLinksCarriesOnFromWhatTheOneBeforeItReached()
    {
        var links = FlowchartDiagram.Read("flowchart LR\n  a --> b --> c --> d").Links;

        CollectionAssert.AreEqual(new[] { "a", "b", "c" }, links.Select(link => link.From).ToArray());
        CollectionAssert.AreEqual(new[] { "b", "c", "d" }, links.Select(link => link.To).ToArray());
    }

    [TestMethod]
    public void AnAmpersandJoinsEveryNodeOnOneSideToEveryNodeOnTheOther()
    {
        var links = FlowchartDiagram.Read("flowchart TB\n  a & b --> c & d").Links;

        Assert.AreEqual(4, links.Count);
        CollectionAssert.AreEqual(new[] { "a-c", "a-d", "b-c", "b-d" },
                                  links.Select(link => $"{link.From}-{link.To}").ToArray());
    }

    [TestMethod]
    public void AFanOutCarriesOnAsTheChainsLeftSide()
    {
        var links = FlowchartDiagram.Read("flowchart LR\n  a --> b & c --> d").Links;

        CollectionAssert.AreEqual(new[] { "a-b", "a-c", "b-d", "c-d" },
                                  links.Select(link => $"{link.From}-{link.To}").ToArray());
    }

    [TestMethod]
    public void ASemicolonStartsWhatFollowsItAfresh()
    {
        var links = FlowchartDiagram.Read("flowchart LR\n  a --> b;c --> d").Links;

        CollectionAssert.AreEqual(new[] { "a-b", "c-d" }, links.Select(link => $"{link.From}-{link.To}").ToArray());
    }

    [TestMethod]
    public void ALinkSaysWhatIsWrittenOnIt_HoweverItIsWritten()
    {
        var links = FlowchartDiagram.Read("flowchart LR\n  a -->|bars| b\n  b -- bare --> c\n  c -- \"quoted\" --> d\n"
                                          + "  d -. dotted .-> e\n  e == thick ==> f").Links;

        CollectionAssert.AreEqual(new[] { "bars", "bare", "quoted", "dotted", "thick" },
                                  links.Select(link => link.Said?.Text).ToArray());
    }

    [TestMethod]
    public void ALinkReachesAsFarAsItIsWrittenLong()
    {
        var links = FlowchartDiagram.Read("flowchart LR\n  a --> b\n  b ---> c\n  c ----> d\n  d -..-> e").Links;

        CollectionAssert.AreEqual(new[] { 1, 2, 3, 2 }, links.Select(link => link.Span).ToArray());
    }

    [TestMethod]
    public void ALinkOfTildesIsNotDrawn()
    {
        var links = FlowchartDiagram.Read("flowchart LR\n  a ~~~ b\n  b --> c").Links;

        Assert.IsFalse(links[0].Drawn, "a link of tildes only holds what it joins apart");
        Assert.IsTrue(links[1].Drawn);
    }

    [TestMethod]
    public void ANodeBelongsToTheSubgraphItWasFirstWrittenIn()
    {
        var diagram = FlowchartDiagram.Read("flowchart TB\n  c1 --> a2\n  subgraph one\n    a1 --> a2\n  end\n"
                                            + "  subgraph two\n    b1 --> b2\n  end");

        Assert.AreEqual(2, diagram.Groups.Count);
        Assert.IsNull(diagram.Find("a2")!.Group, "a2 was written outside them both first");
        Assert.AreEqual(diagram.Groups[0].Key, diagram.Find("a1")!.Group);
        Assert.AreEqual(diagram.Groups[1].Key, diagram.Find("b1")!.Group);
    }

    [TestMethod]
    public void ASubgraphIsCalledWhatItSays_AndTitledByItsLabelWhereItHasOne()
    {
        var titled = FlowchartDiagram.Read("flowchart TB\n  subgraph ide1 [The title]\n    a\n  end").Groups.Single();
        Assert.AreEqual("ide1", titled.Id);
        Assert.AreEqual("The title", titled.Said?.Text);

        var plain = FlowchartDiagram.Read("flowchart TB\n  subgraph one\n    a\n  end").Groups.Single();
        Assert.AreEqual("one", plain.Id);
        Assert.AreEqual("one", plain.Said?.Text, "a subgraph given only a title is called by it");
    }

    [TestMethod]
    public void ASubgraphNestsInTheOneItIsWrittenIn()
    {
        var diagram = FlowchartDiagram.Read("flowchart TB\n  subgraph one\n    subgraph two\n      a\n    end\n  end");

        Assert.AreEqual(2, diagram.Groups.Count);
        Assert.IsNull(diagram.Groups[0].Parent);
        Assert.AreEqual(diagram.Groups[0].Key, diagram.Groups[1].Parent);
        Assert.AreEqual(diagram.Groups[1].Key, diagram.Find("a")!.Group);
    }

    [TestMethod]
    public void ADirectionLineLaysOutTheSubgraphItIsWrittenIn()
    {
        var diagram = FlowchartDiagram.Read("flowchart LR\n  subgraph one\n    direction TB\n    a --> b\n  end\n"
                                            + "  subgraph two\n    c --> d\n  end");

        Assert.AreEqual(FlowchartWay.Down, diagram.Groups[0].Way);
        Assert.IsNull(diagram.Groups[1].Way, "a subgraph with no direction of its own is laid out the chart's way");
        Assert.AreEqual(FlowchartWay.Right, diagram.Way);
    }

    [TestMethod]
    public void AStyleIsWhatTheClassesAndTheStyleLinesAddUpTo_TheNearestWinning()
    {
        var diagram = FlowchartDiagram.Read(
            "flowchart LR\n  a --> b\n  c:::hot\n  classDef default fill:#eee,stroke:#111\n"
            + "  classDef hot fill:#f00\n  class a hot\n  style a stroke:#00f");

        Assert.AreEqual("#f00", diagram.Find("a")!.Style.Fill, "the class it is given wins over the default");
        Assert.AreEqual("#00f", diagram.Find("a")!.Style.Stroke, "and the style line over the class");
        Assert.AreEqual("#eee", diagram.Find("b")!.Style.Fill, "a node given nothing takes the default");
        Assert.AreEqual("#f00", diagram.Find("c")!.Style.Fill, "a class given where the node is written counts too");
    }

    [TestMethod]
    public void AClassDefNamingSeveralClassesWritesTheStyleForEveryOneOfThem()
    {
        var diagram = FlowchartDiagram.Read("flowchart LR\n  a --> b\n  classDef one,two fill:#f96\n"
                                            + "  class a one\n  class b two");

        Assert.AreEqual("#f96", diagram.Find("a")!.Style.Fill);
        Assert.AreEqual("#f96", diagram.Find("b")!.Style.Fill, "both classes the line names take the style");
    }

    [TestMethod]
    public void ALinkStyleStylesTheLinksItNumbers()
    {
        var links = FlowchartDiagram.Read("flowchart LR\n  a --> b\n  b --> c\n  c --> d\n"
                                          + "  linkStyle 0,2 stroke:#f00").Links;

        Assert.AreEqual("#f00", links[0].Written.Stroke);
        Assert.IsNull(links[1].Written.Stroke);
        Assert.AreEqual("#f00", links[2].Written.Stroke);
    }

    [TestMethod]
    public void ALinkStyleSayingDefaultStylesEveryLink()
    {
        var links = FlowchartDiagram.Read("flowchart LR\n  a --> b\n  b --> c\n  linkStyle default stroke:#333").Links;

        Assert.IsTrue(links.All(link => link.Written.Stroke == "#333"));
    }

    [TestMethod]
    public void AClickLineSaysWhereANodeLeadsAndWhatItSaysWhilePointedAt()
    {
        var node = FlowchartDiagram.Read("flowchart LR\n  a --> b\n  click a \"https://example.com\" \"Go there\"").Find("a")!;

        Assert.AreEqual("https://example.com", node.Href);
        Assert.AreEqual("Go there", node.Tip);
    }

    [TestMethod]
    public void MetadataSaysTheShapeAndTheLabelANodeIsDrawnWith()
    {
        var node = FlowchartDiagram.Read("flowchart TD\n  a\n  a@{ shape: cyl, label: \"The store\" }").Find("a")!;

        Assert.AreEqual(MermaidShape.Cylinder, node.Shape);
        Assert.AreEqual("The store", node.Worked);
    }

    [TestMethod]
    public void MetadataSaysWhatItSaysAboutANodeWrittenBelowIt()
    {
        var node = FlowchartDiagram.Read("flowchart TD\n  a@{ shape: circle }\n  a --> b").Find("a")!;

        Assert.AreEqual(MermaidShape.Circle, node.Shape);
    }

    [TestMethod]
    public void MetadataSaysHowALinkOfItsOwnIsCurved()
    {
        var links = FlowchartDiagram.Read("flowchart LR\n  A e1@--> B\n  B e2@--> C\n  e1@{ curve: linear }").Links;

        Assert.AreEqual("linear", links[0].Curve);
        Assert.IsFalse(FlowchartConfig.Curving(links[0].Curve), "a linear curve is the straight line between the points");
        Assert.IsNull(links[1].Curve, "and a link whose metadata says nothing is curved the way the front matter asks");
    }

    [TestMethod]
    public void TheFrontMatterIsApplied()
    {
        var diagram = FlowchartDiagram.Read("---\nconfig:\n  flowchart:\n    nodeSpacing: 70\n    rankSpacing: 90\n"
                                            + "    curve: linear\n---\nflowchart LR\n  a --> b");

        Assert.AreEqual(70, diagram.Config.NodeSpacing);
        Assert.AreEqual(90, diagram.Config.RankSpacing);
        Assert.IsFalse(diagram.Config.Curved, "a linear curve is a straight line");
    }
}
