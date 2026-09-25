using Nexaflow.Markdown.Ast;
using Nexaflow.Markdown.Mermaid;
using Nexaflow.Markdown.Mermaid.Quadrant;
using Nexaflow.Tests.Fixtures;
using Nexaflow.Tests.Markdown.Mermaid;

namespace Nexaflow.Tests.Markdown.Mermaid.Quadrant;

/// <summary>
/// What a <c>quadrantChart</c> block's stages write into its tree: where each point stands and how it is drawn — its class,
/// wherever that is written, with its own style over it — and whether the x-axis's words go over the chart.
/// </summary>
[TestClass]
[CoversNode("quadrant-graph-ast")]
public class QuadrantStagesTests
{
    [TestMethod]
    public void EveryPointSaysWhereItStands()
    {
        var tree = MermaidStaged.Read(QuadrantGrammarTests.Campaigns);
        var points = Points(tree);

        Assert.AreEqual("Reach and engagement of campaigns", MermaidBlock.Of(tree).TitleText);
        Assert.AreEqual(6, points.Count);
        Assert.AreEqual("Campaign B", Name(points[1]));
        Assert.AreEqual(0.45, points[1].X);
        Assert.AreEqual(0.23, points[1].Y);
        Assert.IsTrue(points.All(point => point.Located));
    }

    [TestMethod]
    public void APointWithNowhereWrittenToStandIsStillAPoint_ButNotPlaced()
    {
        var point = Points(MermaidStaged.Read("quadrantChart\n  A: [0.1, lots]")).Single();

        Assert.AreEqual(0.1, point.X);
        Assert.IsNull(point.Y);
        Assert.IsFalse(point.Located, "it is still read, so a reader can fix it");
    }

    [TestMethod]
    public void APointsOwnStyleIsLaidOverItsClass_WrittenAboveItOrBelow()
    {
        var points = Points(MermaidStaged.Read(QuadrantGrammarTests.Styled));

        Assert.AreEqual(new QuadrantStyle(10, "#ff3300", null, null), points[1].Style, "its own colour over class1's");
        Assert.AreEqual(new QuadrantStyle(10, "#908342", "#310085", 10), points[4].Style, "class2 whole");
        Assert.AreEqual(new QuadrantStyle(15, "#ff33f0", "#00ff0f", 5), points[3].Style, "its own, whatever order written in");
    }

    [TestMethod]
    public void TheXAxissWordsGoOverTheChartOnlyWhereThereAreNoPoints_UnlessTheFrontMatterSays()
    {
        Assert.IsTrue(((QuadrantBlockNode)MermaidStaged.Read("quadrantChart\n  x-axis Low --> High")).XAxisOnTop);
        Assert.IsFalse(((QuadrantBlockNode)MermaidStaged.Read("quadrantChart\n  A: [0.1, 0.2]")).XAxisOnTop);
        Assert.IsTrue(((QuadrantBlockNode)MermaidStaged.Read("---\nconfig:\n  quadrantChart:\n    xAxisPosition: top\n---\nquadrantChart\n  A: [0.1, 0.2]")).XAxisOnTop);
    }

    [TestMethod]
    public void WhatTheStagesWriteIsNoPartOfTheSource()
    {
        foreach (var source in new[] { QuadrantGrammarTests.Campaigns, QuadrantGrammarTests.Styled, "quadrantChart\n  A:::missing: [0.1, " })
        {
            Assert.AreEqual(source, MermaidStaged.Read(source).Print(), "a stage leaves the characters alone");
            Assert.AreEqual(source, MermaidStaged.Read(source, holes: true).Print());
        }
    }

    [TestMethod]
    public void TheFrontMatterIsReadIntoTheConfig()
    {
        var config = QuadrantConfig.Read("config:\n  quadrantChart:\n    chartWidth: 400\n    pointRadius: 8\n    yAxisPosition: right\n  themeVariables:\n    quadrant1Fill: \"#ff0000\"\n    quadrantPointFill: blue\n");

        Assert.AreEqual(400, config.ChartWidth);
        Assert.IsNull(config.ChartHeight);
        Assert.AreEqual(8, config.PointRadius);
        Assert.IsTrue(config.YAxisOnRight);
        Assert.AreEqual("#ff0000", config.QuadrantFills[0]);
        Assert.IsNull(config.QuadrantFills[1]);
        Assert.AreEqual("blue", config.PointFill);
    }

    private static List<QuadrantPointNode> Points(ContentNode tree) => [.. tree.SelfAndDescendants().OfType<QuadrantPointNode>()];

    private static string Name(ContentNode point) =>
        point.Children.Single(child => child.Kind == QuadrantKinds.Text && child.Role == QuadrantRoles.Name).Inner(MermaidKinds.Words)!.Text;
}
