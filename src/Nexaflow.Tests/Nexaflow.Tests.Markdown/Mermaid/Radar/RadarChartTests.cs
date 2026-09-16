using Nexaflow.Markdown.Mermaid.Radar;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.Markdown.Mermaid.Radar;

/// <summary>
/// A <c>radar-beta</c> block read back into the chart it describes: its axes and curves, how far each curve reaches along each
/// axis, the scale its options set, and what its front matter asks for.
/// </summary>
[TestClass]
[CoversNode("radar-ast")]
public class RadarChartTests
{
    [TestMethod]
    public void TheDocumentedChartIsReadWhole()
    {
        var chart = RadarChart.Read(RadarGrammarTests.Restaurants);

        Assert.AreEqual("Restaurant Comparison", chart.Block.TitleText);
        CollectionAssert.AreEqual(new[] { "Food Quality", "Service", "Price", "Ambiance" }, chart.Axes.Select(axis => axis.Says.Text).ToArray());
        CollectionAssert.AreEqual(new[] { "Restaurant A", "Restaurant B", "Restaurant C", "Restaurant D" }, chart.Curves.Select(curve => curve.Says.Text).ToArray());
        CollectionAssert.AreEqual(new double?[] { 4, 3, 2, 4 }, chart.Curves[0].Points.ToArray());

        Assert.AreEqual(RadarGraticule.Polygon, chart.Graticule);
        Assert.AreEqual(5, chart.Max);
        Assert.AreEqual(0, chart.Min);
        Assert.AreEqual(RadarChart.Rings, chart.Ticks);
        Assert.IsTrue(chart.ShowsLegend);
    }

    [TestMethod]
    public void ACurveNamingItsAxesReachesThemInTheOrderTheAxesAreWritten()
    {
        var chart = RadarChart.Read(RadarGrammarTests.Details);

        CollectionAssert.AreEqual(new double?[] { 20, 10, 30 }, chart.Curves.Single(curve => curve.Name.Text == "id4").Points.ToArray());
        CollectionAssert.AreEqual(new double?[] { 7, 8, 9 }, chart.Curves.Single(curve => curve.Name.Text == "id3").Points.ToArray(),
                                  "and a curve sharing its line with another is a curve of its own");
    }

    [TestMethod]
    public void AnAxisOrACurveWithNoLabelIsCalledByItsName()
    {
        var chart = RadarChart.Read("radar-beta\n  axis A, B\n  curve c1{1, 2}");

        CollectionAssert.AreEqual(new[] { "A", "B" }, chart.Axes.Select(axis => axis.Says.Text).ToArray());
        Assert.AreEqual("c1", chart.Curves.Single().Says.Text);
    }

    [TestMethod]
    public void ACurveGivingAnAxisNothingHasNoPointOnIt()
    {
        var chart = RadarChart.Read("radar-beta\n  axis a, b, c\n  curve x{ a: 1, c: 3 }\n  curve y{ a: 1, z: 2 }");

        CollectionAssert.AreEqual(new double?[] { 1, null, 3 }, chart.Curves[0].Points.ToArray());
        CollectionAssert.AreEqual(new double?[] { 1, null, null }, chart.Curves[1].Points.ToArray(), "and a value naming no axis is for none");
        Assert.AreEqual(1, chart.Value(chart.Curves[1], chart.Axes[0]));
    }

    [TestMethod]
    public void WithNoMaxTheRimIsTheGreatestValue_AndNoValueReachesPastIt()
    {
        var chart = RadarChart.Read("radar-beta\n  axis a, b\n  curve x{2, 8}\n  curve y{4, 1}");

        Assert.AreEqual(8, chart.Max);
        Assert.AreEqual(0.5, chart.Reach(4), 1e-9);

        var capped = RadarChart.Read("radar-beta\n  axis a, b\n  curve x{2, 8}\n  max 4\n  min 2");

        Assert.AreEqual(1, capped.Reach(8), 1e-9, "past max is at the rim");
        Assert.AreEqual(0, capped.Reach(1), 1e-9, "and short of min in the middle");
    }

    [TestMethod]
    public void AChartOfNothingButNoughtsStillHasAScale()
    {
        var chart = RadarChart.Read("radar-beta\n  axis a, b\n  curve x{0, 0}");
        Assert.IsTrue(chart.Max > chart.Min);
    }

    [TestMethod]
    public void TheOptionsSetTheRingsAndTheLegend_TheLastWrittenWinning()
    {
        const string source = "radar-beta\n  axis a, b, c\n  curve x{1, 2, 3}\n  ticks 8\n  showLegend false\n  ticks 3";
        var chart = RadarChart.Read(source);

        Assert.AreEqual(3, chart.Ticks);
        Assert.IsFalse(chart.ShowsLegend);
        Assert.AreEqual(RadarGraticule.Circle, chart.Graticule, "a circle where nothing says otherwise");
        Assert.AreEqual(source.IndexOf("ticks 3", StringComparison.Ordinal), chart.Shaped!.Start, "and a ring stands for the option shaping it");
    }

    [TestMethod]
    public void TheFrontMatterIsReadIntoTheConfig()
    {
        var config = RadarConfig.Read(
            """
            config:
              radar:
                width: 800
                height: 400
                marginTop: 20
                axisScaleFactor: 0.25
                axisLabelFactor: 5
                curveTension: 0.1
              themeVariables:
                fontSize: 20px
                titleColor: red
                cScale0: "#FF0000"
                cScale2: "#0000FF"
                radar:
                  axisColor: blue
                  curveOpacity: 0
                  axisLabelFontSize: 16px
                  legendBoxSize: 10
            """);

        Assert.AreEqual(800, config.Width);
        Assert.AreEqual(400, config.Height);
        Assert.AreEqual(20, config.MarginTop);
        Assert.IsNull(config.MarginLeft, "a margin nobody wrote is the builder's");
        Assert.AreEqual(0.25, config.AxisScaleFactor, 1e-9);
        Assert.AreEqual(2, config.AxisLabelFactor, 1e-9, "brought back inside what it allows");
        Assert.AreEqual(0.1, config.CurveTension, 1e-9);

        Assert.AreEqual(20, config.TitleTextSize);
        Assert.AreEqual("red", config.TitleTextColour);
        Assert.AreEqual("#FF0000", config.Swatches[0]);
        Assert.AreEqual("#0000FF", config.Swatches[2]);
        Assert.IsFalse(config.Swatches.ContainsKey(1));

        Assert.AreEqual("blue", config.AxisColour);
        Assert.AreEqual(0, config.CurveOpacity);
        Assert.AreEqual(16, config.AxisLabelTextSize);
        Assert.AreEqual(10, config.LegendBoxSize);
    }

    [TestMethod]
    public void WhatNoFrontMatterWritesIsMermaidsShape_AndTheThemesEverythingElse()
    {
        var config = RadarChart.Read("radar-beta\n  axis a").Config;

        Assert.AreEqual(1, config.AxisScaleFactor);
        Assert.AreEqual(1.05, config.AxisLabelFactor, 1e-9);
        Assert.AreEqual(0.17, config.CurveTension, 1e-9);
        Assert.IsNull(config.Width);
        Assert.IsNull(config.CurveOpacity);
        Assert.IsNull(config.AxisColour);
        Assert.AreEqual(0, config.Swatches.Count);
    }

    [TestMethod]
    public void ACurveTakesTheColourWrittenForItsPlace() =>
        CollectionAssert.AreEqual(new[] { "#FF0000", "#00FF00", "#0000FF" },
                                  RadarChart.Read(RadarGrammarTests.Themed).Curves.Select(curve => curve.Colour).ToArray());
}
