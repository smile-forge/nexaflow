using Nexaflow.Markdown.Ast;
using Nexaflow.Markdown.Mermaid;
using Nexaflow.Markdown.Mermaid.Flowchart;
using Nexaflow.Markdown.Mermaid.Swimlane;
using Nexaflow.Tests.Fixtures;
using Nexaflow.Tests.Markdown.Mermaid;

namespace Nexaflow.Tests.Markdown.Mermaid.Flowchart;

/// <summary>
/// What a flowchart's stages say its lines mean where no one line says it: which subgraph each line is in, what each link joins
/// and is styled with, which names are subgraphs, what each <c>id@{ … }</c> line is about and says, and what styles each node.
/// </summary>
[TestClass]
[CoversNode("flowchart-ast")]
public class FlowchartStagesTests
{
    private static IReadOnlyList<FlowchartJoin> Joins(string source) =>
        [.. MermaidStaged.Read(source).SelfAndDescendants().OfType<FlowchartLinkNode>().SelectMany(link => link.Joins)];

    private static string[] Pairs(string source) => [.. Joins(source).Select(join => $"{join.From}-{join.To}")];

    private static FlowchartMetadataNode Metadata(ContentNode tree, string id) =>
        tree.SelfAndDescendants().OfType<FlowchartMetadataNode>().Single(said => said.Words()?.Text == id);

    /// <summary>What the name first writing an id is styled with, as the stages said on it.</summary>
    private static MermaidStyle Style(ContentNode tree, string id) =>
        tree.SelfAndDescendants().OfType<StyledNode>().Single(name => name.Words()?.Text == id).Style;

    [TestMethod]
    public void ALinkJoinsTheNodesEitherSideOfIt()
    {
        CollectionAssert.AreEqual(new[] { "a-b", "b-c" }, Pairs("flowchart LR\n  a --> b\n  b -.-> c"));
    }

    [TestMethod]
    public void AChainOfLinksCarriesOnFromWhatTheOneBeforeItReached()
    {
        CollectionAssert.AreEqual(new[] { "a-b", "b-c", "c-d" }, Pairs("flowchart LR\n  a --> b --> c --> d"));
    }

    [TestMethod]
    public void AnAmpersandJoinsEveryNodeOnOneSideToEveryNodeOnTheOther()
    {
        CollectionAssert.AreEqual(new[] { "a-c", "a-d", "b-c", "b-d" }, Pairs("flowchart TB\n  a & b --> c & d"));
        Assert.AreEqual(1, MermaidStaged.Read("flowchart TB\n  a & b --> c & d").SelfAndDescendants().OfType<FlowchartLinkNode>().Count(),
                        "one link written, making four joins");
    }

    [TestMethod]
    public void AFanOutCarriesOnAsTheChainsLeftSide()
    {
        CollectionAssert.AreEqual(new[] { "a-b", "a-c", "b-d", "c-d" }, Pairs("flowchart LR\n  a --> b & c --> d"));
    }

    [TestMethod]
    public void ASemicolonStartsWhatFollowsItAfresh()
    {
        CollectionAssert.AreEqual(new[] { "a-b", "c-d" }, Pairs("flowchart LR\n  a --> b;c --> d"));
    }

    [TestMethod]
    public void ALinkWithNothingOnOneSideJoinsNothing_AndSaysSo()
    {
        var tree = MermaidStaged.Read("flowchart LR\n  a -->");

        Assert.AreEqual(0, tree.SelfAndDescendants().OfType<FlowchartLinkNode>().Count());
        Assert.IsTrue(tree.SelfAndDescendants().Any(node => node.Kind == FlowchartKinds.Link && node.Trouble is not null));
    }

    [TestMethod]
    public void ALinkStyleStylesTheJoinsItNumbers_CountedInTheOrderTheyAreWritten()
    {
        var joins = Joins("flowchart LR\n  a --> b\n  b --> c & d\n  linkStyle 0,2 stroke:#f00");

        CollectionAssert.AreEqual(new[] { "#f00", null, "#f00" }, joins.Select(join => join.Style.Stroke).ToArray(),
                                  "each join of b --> c & d is numbered on its own");
    }

    [TestMethod]
    public void ALinkStyleWrittenAboveTheLinksItNumbersStylesThemToo()
    {
        var joins = Joins("flowchart LR\n  linkStyle 1 stroke:#0f0\n  a --> b\n  b --> c");

        Assert.AreEqual("#0f0", joins[1].Style.Stroke);
    }

    [TestMethod]
    public void ALinkStyleSayingDefaultStylesEveryLink_AndOneNumberingItOverThat()
    {
        var joins = Joins("flowchart LR\n  a --> b\n  b --> c\n  linkStyle default stroke:#333\n  linkStyle 1 stroke:#f00");

        CollectionAssert.AreEqual(new[] { "#333", "#f00" }, joins.Select(join => join.Style.Stroke).ToArray());
    }

    [TestMethod]
    public void MetadataSaysHowALinkOfItsOwnIsCurved_WrittenAboveItOrBelow()
    {
        var links = MermaidStaged.Read("flowchart LR\n  e2@{ curve: basis }\n  A e1@--> B\n  B e2@--> C\n  e1@{ curve: linear }")
                                 .SelfAndDescendants().OfType<FlowchartLinkNode>().ToList();

        CollectionAssert.AreEqual(new[] { "linear", "basis" }, links.Select(link => link.Curve).ToArray());
        Assert.IsNull(MermaidStaged.Read("flowchart LR\n  A --> B").SelfAndDescendants().OfType<FlowchartLinkNode>().Single().Curve);
    }

    [TestMethod]
    public void MetadataSaysTheShapeAndTheLabelANodeIsDrawnWith()
    {
        var said = Metadata(MermaidStaged.Read("flowchart TD\n  a\n  a@{ shape: cyl, label: \"The store\" }"), "a");

        Assert.AreEqual(FlowchartSaid.Node, said.About);
        Assert.AreEqual(MermaidShape.Cylinder, said.Shape);
        Assert.AreEqual("The store", said.Label, "its quotes belong to the metadata");
        Assert.AreEqual(MermaidShape.Rectangle, Metadata(MermaidStaged.Read("flowchart TD\n  a@{ shape: nothing }"), "a").Shape,
                        "a shape Mermaid has none by is drawn as a rectangle, and said to be wrong");
    }

    [TestMethod]
    public void MetadataSaysThePictureOrIconANodeIsDrawnAs()
    {
        var tree = MermaidStaged.Read("flowchart TD\n  A@{ icon: \"fa:user\", form: \"square\", pos: \"t\", h: 60 }\n  B@{ img: \"a.png\", w: 40, constraint: \"on\" }\n  C@{ shape: rect }");

        var icon = Metadata(tree, "A").Picture!;
        Assert.AreEqual(("fa:user", "square", true, 60d), (icon.Icon, icon.Form, icon.Above, icon.Height!.Value));

        var image = Metadata(tree, "B").Picture!;
        Assert.IsNull(image.Icon, "a picture names no icon");
        Assert.AreEqual(40, image.Width);
        Assert.IsNull(image.Height);
        Assert.IsTrue(image.Keeps);
        Assert.IsFalse(image.Above, "its label below it where nothing says");

        Assert.IsNull(Metadata(tree, "C").Picture, "and a node naming neither is drawn as its shape");
    }

    [TestMethod]
    public void MetadataIsAboutWhatItNames_WrittenAboveItOrBelow_OrMakesANodeOfNothingWritten()
    {
        var tree = MermaidStaged.Read("flowchart TD\n  a@{ shape: circle }\n  n@{ shape: hex }\n  e1@{ curve: linear }\n  s@{ shape: rect }\n"
                                      + "  a e1@--> b\n  subgraph s\n    b\n  end");

        Assert.AreEqual(FlowchartSaid.Node, Metadata(tree, "a").About, "a node written below it");
        Assert.AreEqual(FlowchartSaid.New, Metadata(tree, "n").About, "nothing written, which it makes a node of");
        Assert.AreEqual(FlowchartSaid.Link, Metadata(tree, "e1").About);
        Assert.AreEqual(FlowchartSaid.Group, Metadata(tree, "s").About, "a subgraph called that, which is the subgraph");
    }

    [TestMethod]
    public void ANodeNamingASubgraphIsThatSubgraph_WrittenAboveItOrBelow()
    {
        var tree = MermaidStaged.Read("flowchart TB\n  one --> two\n  subgraph one\n    a\n  end\n  subgraph two\n    b\n  end\n  two --> c");
        var named = tree.SelfAndDescendants().OfType<GroupReferenceNode>().ToList();

        CollectionAssert.AreEqual(new[] { 0, 1, 1 }, named.Select(reference => reference.Group).ToArray(),
                                  "each by where its subgraph stands among those written");
        Assert.IsFalse(tree.SelfAndDescendants().OfType<GroupReferenceNode>().Any(reference => reference.Words()?.Text is "a" or "b" or "c"));
    }

    [TestMethod]
    public void EachSubgraphHoldsTheLinesWrittenInIt_AndStillPrintsAsWritten()
    {
        const string source = "flowchart TB\n  subgraph one\n    subgraph two\n      a\n    end\n    direction LR\n  end\n  b";
        var tree = MermaidStaged.Read(source);
        var groups = tree.SelfAndDescendants().Where(node => node.Kind == MermaidKinds.Group).ToList();

        Assert.AreEqual(2, groups.Count);
        Assert.IsTrue(groups[0].SelfAndDescendants().Contains(groups[1]), "a subgraph nests in the one it is written in");
        Assert.IsTrue(groups[0].SelfAndDescendants().Any(node => node.Kind == FlowchartKinds.Direction), "and a direction line in the one it lays out");
        Assert.AreEqual(source, tree.Print());
    }

    [TestMethod]
    public void ASubgraphNothingEndsIsSaidToBe()
    {
        var tree = MermaidStaged.Read("flowchart TB\n  subgraph one\n    a");

        Assert.IsTrue(tree.SelfAndDescendants().Any(node => node.Kind == FlowchartKinds.Opens && node.Trouble is not null));
    }

    [TestMethod]
    public void AStyleIsWhatTheClassesAndTheStyleLinesAddUpTo_TheNearestWinning()
    {
        var tree = MermaidStaged.Read("flowchart LR\n  a --> b\n  c:::hot\n  classDef default fill:#eee,stroke:#111\n"
            + "  classDef hot fill:#f00\n  class a hot\n  style a stroke:#00f");

        Assert.AreEqual("#f00", Style(tree, "a").Fill, "the class it is given wins over the default");
        Assert.AreEqual("#00f", Style(tree, "a").Stroke, "and the style line over the class");
        Assert.AreEqual("#eee", Style(tree, "b").Fill, "a node given nothing takes the default");
        Assert.AreEqual("#f00", Style(tree, "c").Fill, "a class given where the node is written counts too");
    }

    [TestMethod]
    public void AClassDefNamingSeveralClassesWritesTheStyleForEveryOneOfThem()
    {
        var tree = MermaidStaged.Read("flowchart LR\n  a --> b\n  classDef one,two fill:#f96\n  class a one\n  class b two");

        Assert.AreEqual("#f96", Style(tree, "a").Fill);
        Assert.AreEqual("#f96", Style(tree, "b").Fill, "both classes the line names take the style");
    }

    [TestMethod]
    public void AStyleIsSaidOnTheNameFirstWritingWhatItStyles_ASubgraphsToo()
    {
        var tree = MermaidStaged.Read("flowchart LR\n  a --> b\n  a --> c\n  subgraph s\n    b\n  end\n  style s fill:#abc\n  style a fill:#123");

        Assert.AreEqual(1, tree.SelfAndDescendants().OfType<StyledNode>().Count(name => name.Words()?.Text == "a"), "once, where a is first written");
        Assert.AreEqual("#abc", Style(tree, "s").Fill);
    }

    [TestMethod]
    public void TheFrontMatterIsHungOnTheBlock_ASwimlanesLanesWithItsChart()
    {
        var chart = (MermaidStaged.Read("---\nconfig:\n  flowchart:\n    nodeSpacing: 70\n    rankSpacing: 90\n    curve: linear\n---\nflowchart LR\n  a --> b")
                     as ConfiguredNode<FlowchartConfig>)!.Config;

        Assert.AreEqual(70, chart.NodeSpacing);
        Assert.AreEqual(90, chart.RankSpacing);
        Assert.IsFalse(chart.Curved, "a linear curve is a straight line");
        Assert.IsFalse(FlowchartConfig.Curving("linear"));

        var lanes = (MermaidStaged.Read("---\nconfig:\n  swimlane:\n    automaticLaneOrdering: true\n  flowchart:\n    nodeSpacing: 40\n---\nswimlane-beta TB\n  subgraph A\n    one\n  end")
                     as ConfiguredNode<SwimlaneConfig>)!.Config;

        Assert.IsTrue(lanes.Ordered);
        Assert.AreEqual(40, lanes.Chart.NodeSpacing, 0.01);
    }
}
