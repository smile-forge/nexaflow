using Nexaflow.Markdown.Mermaid.Quadrant;
using Nexaflow.Tests.Fixtures;
using Nexaflow.Tests.Markdown.Mermaid;

namespace Nexaflow.Tests.Markdown.Mermaid.Quadrant;

/// <summary>A <c>quadrantChart</c> block read back into its axes' ends, its captions, its points and their styles, and its front matter.</summary>
[TestClass]
[CoversNode("quadrant-graph-ast")]
public class QuadrantChartTests
{
    [TestMethod]
    public void TheDocumentedChartIsReadWhole()
    {
        var chart = QuadrantChart.Of(MermaidStaged.Read(QuadrantGrammarTests.Campaigns));

        Assert.AreEqual("Reach and engagement of campaigns", chart.Block.TitleText);
        Assert.AreEqual("Low Reach", chart.Left!.Says.Text);
        Assert.AreEqual("High Engagement", chart.Top!.Says.Text);
        CollectionAssert.AreEqual(new[] { "We should expand", "Need to promote", "Re-evaluate", "May be improved" },
                                  chart.Regions.Select(region => region!.Says.Text).ToArray());
        Assert.AreEqual(6, chart.Points.Count);
        Assert.AreEqual("Campaign B", chart.Points[1].Name.Says.Text);
        Assert.AreEqual(0.45, chart.Points[1].X);
        Assert.AreEqual(0.23, chart.Points[1].Y);
    }

    [TestMethod]
    public void APointsOwnStyleIsLaidOverItsClass_WrittenAboveItOrBelow()
    {
        var points = QuadrantChart.Of(MermaidStaged.Read(QuadrantGrammarTests.Styled)).Points;

        Assert.AreEqual(new QuadrantStyle(10, "#ff3300", null, null), points[1].Style, "its own colour over class1's");
        Assert.AreEqual(new QuadrantStyle(10, "#908342", "#310085", 10), points[4].Style, "class2 whole");
        Assert.AreEqual(new QuadrantStyle(15, "#ff33f0", "#00ff0f", 5), points[3].Style, "its own, whatever order written in");
    }

    [TestMethod]
    public void TheXAxissWordsGoOverTheChartOnlyWhereThereAreNoPoints_UnlessTheFrontMatterSays()
    {
        Assert.IsTrue(QuadrantChart.Of(MermaidStaged.Read("quadrantChart\n  x-axis Low --> High")).XAxisOnTop);
        Assert.IsFalse(QuadrantChart.Of(MermaidStaged.Read("quadrantChart\n  A: [0.1, 0.2]")).XAxisOnTop);
        Assert.IsTrue(QuadrantChart.Of(MermaidStaged.Read("---\nconfig:\n  quadrantChart:\n    xAxisPosition: top\n---\nquadrantChart\n  A: [0.1, 0.2]")).XAxisOnTop);
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
}
