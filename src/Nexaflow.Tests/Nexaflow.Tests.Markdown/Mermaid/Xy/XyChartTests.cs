using Nexaflow.Markdown.Mermaid.Xy;
using Nexaflow.Tests.Fixtures;
using Nexaflow.Tests.Markdown.Mermaid;

namespace Nexaflow.Tests.Markdown.Mermaid.Xy;

/// <summary>
/// An <c>xychart</c> block read back into the chart it describes: which way it runs, its axes, its series and their colours,
/// the range its values are drawn against, and what its front matter asks for.
/// </summary>
[TestClass]
[CoversNode("xy-chart-ast")]
public class XyChartTests
{
    [TestMethod]
    public void TheDocumentedChartIsReadWhole()
    {
        var chart = XyChart.Of(MermaidStaged.Read(XyGrammarTests.Revenue));

        Assert.AreEqual("Sales Revenue", chart.Block.TitleText);
        Assert.AreEqual(XyOrientation.Vertical, chart.Orientation);
        Assert.AreEqual(12, chart.Slots);
        CollectionAssert.AreEqual(new[] { XySeriesKind.Bar, XySeriesKind.Line }, chart.Series.Select(series => series.Kind).ToArray());
        Assert.AreEqual((4000d, 11000d), chart.Range);
    }

    [TestMethod]
    public void WhichWayItRunsIsTheHeaders_UnlessTheFrontMatterSays()
    {
        Assert.AreEqual(XyOrientation.Horizontal, XyChart.Of(MermaidStaged.Read("xychart horizontal\n  bar [1]")).Orientation);
        Assert.AreEqual(XyOrientation.Vertical,
                        XyChart.Of(MermaidStaged.Read("---\nconfig:\n  xyChart:\n    chartOrientation: vertical\n---\nxychart horizontal\n  bar [1]")).Orientation);
    }

    [TestMethod]
    public void WithNoRangeWrittenTheValuesAreTheirOwn_FromNoughtWhereThereAreBars()
    {
        Assert.AreEqual((0d, 8d), XyChart.Of(MermaidStaged.Read("xychart\n  bar [2, 8]")).Range);
        Assert.AreEqual((2d, 8d), XyChart.Of(MermaidStaged.Read("xychart\n  line [2, 8]")).Range);
        Assert.AreEqual((-5d, 3d), XyChart.Of(MermaidStaged.Read("xychart\n  bar [-5, 3]")).Range);
        Assert.AreEqual((0d, 1d), XyChart.Of(MermaidStaged.Read("xychart")).Range, "and one wide where there is nothing");
    }

    [TestMethod]
    public void WithNoCategoriesTheSlotsAreTheLongestSeries() =>
        Assert.AreEqual(4, XyChart.Of(MermaidStaged.Read("xychart\n  x-axis 0 --> 10\n  bar [1, 2]\n  line [1, 2, 3, 4]")).Slots);

    [TestMethod]
    public void AnAxisWrittenTwiceIsTheLastOneWritten() =>
        Assert.AreEqual("b", XyChart.Of(MermaidStaged.Read("xychart\n  x-axis [a]\n  x-axis [b]")).X!.Categories.Single().Name.Text);

    [TestMethod]
    public void OnlyANamedSeriesHasAName_AndEachTakesThePalettesColourForItsPlace()
    {
        var chart = XyChart.Of(MermaidStaged.Read("---\nconfig:\n  themeVariables:\n    xyChart:\n      plotColorPalette: \"#ff0000, #00ff00\"\n---\nxychart\n  bar \"Sold\" [1]\n  line [2]\n  bar [3]"));

        Assert.AreEqual("Sold", chart.Series[0].Name!.Text);
        Assert.IsNull(chart.Series[1].Name);
        CollectionAssert.AreEqual(new[] { "#ff0000", "#00ff00", "#ff0000" }, chart.Series.Select(series => series.Colour).ToArray());
    }

    [TestMethod]
    public void TheFrontMatterIsReadIntoTheConfig()
    {
        var config = XyConfig.Read(
            """
            config:
              xyChart:
                width: 900
                showLegend: false
                showDataLabel: true
                xAxis:
                  labelFontSize: 10
                  showTick: false
                yAxis:
                  axisLineWidth: 3
              themeVariables:
                xyChart:
                  titleColor: "#ff0000"
                  xAxisLabelColor: blue
                  yAxisLineColor: green
            """);

        Assert.AreEqual(900, config.Width);
        Assert.IsNull(config.Height, "a size nobody wrote is the builder's");
        Assert.IsFalse(config.ShowLegend);
        Assert.IsTrue(config.ShowDataLabel);
        Assert.AreEqual(10, config.XAxis.LabelFontSize);
        Assert.IsFalse(config.XAxis.ShowTick);
        Assert.AreEqual(3, config.YAxis.AxisLineWidth);
        Assert.AreEqual("#ff0000", config.TitleColour);
        Assert.AreEqual("blue", config.XAxis.LabelColour);
        Assert.AreEqual("green", config.YAxis.LineColour);
    }
}
