using Nexaflow.Markdown.Mermaid;
using Nexaflow.Markdown.Mermaid.Xy;
using Nexaflow.Tests.Fixtures;
using Nexaflow.Tests.Markdown.Mermaid;

namespace Nexaflow.Tests.Markdown.Mermaid.Xy;

/// <summary>What an <c>xychart</c> block's front matter asks for, as its stage hangs it on the block.</summary>
[TestClass]
[CoversNode("xy-chart-ast")]
public class XyConfigTests
{
    [TestMethod]
    public void TheFrontMatterIsReadIntoTheConfig()
    {
        var config = ((ConfiguredNode<XyConfig>)MermaidStaged.Read(
            """
            ---
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
            ---
            xychart
              bar [1]
            """)).Config;

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
