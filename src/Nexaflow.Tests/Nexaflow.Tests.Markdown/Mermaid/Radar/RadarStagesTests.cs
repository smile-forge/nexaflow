using Nexaflow.Markdown.Ast;
using Nexaflow.Markdown.Mermaid;
using Nexaflow.Markdown.Mermaid.Radar;
using Nexaflow.Tests.Fixtures;
using Nexaflow.Tests.Markdown.Mermaid;

namespace Nexaflow.Tests.Markdown.Mermaid.Radar;

/// <summary>
/// What a <c>radar-beta</c> block's stages write into its tree: which axes are spokes, how far each curve reaches along each of
/// them and the colour it takes, which curves the legend lists, and the scale, rings and legend its options set — everything a
/// builder needs, so it only lays the chart out.
/// </summary>
[TestClass]
[CoversNode("radar-ast")]
public class RadarStagesTests
{
    [TestMethod]
    public void TheDocumentedChartIsReadWhole()
    {
        var tree = MermaidStaged.Read(RadarGrammarTests.Restaurants);
        var chart = (RadarBlockNode)tree;

        Assert.AreEqual("Restaurant Comparison", MermaidBlock.Of(tree).TitleText);
        CollectionAssert.AreEqual(new[] { "Food Quality", "Service", "Price", "Ambiance" }, Spokes(tree).Select(Says).ToArray());
        CollectionAssert.AreEqual(new[] { "Restaurant A", "Restaurant B", "Restaurant C", "Restaurant D" }, Curves(tree).Select(Says).ToArray());
        CollectionAssert.AreEqual(new double?[] { 4, 3, 2, 4 }, Curves(tree)[0].Points.ToArray());

        Assert.AreEqual(RadarGraticule.Polygon, chart.Graticule);
        Assert.AreEqual(5, chart.Max);
        Assert.AreEqual(0, chart.Min);
        Assert.AreEqual(5, chart.Ticks, "five rings where ticks says nothing");
        Assert.IsTrue(chart.ShowsLegend);
    }

    [TestMethod]
    public void ACurveNamingItsAxesReachesThemInTheOrderTheAxesAreWritten()
    {
        var curves = Curves(MermaidStaged.Read(RadarGrammarTests.Details));

        CollectionAssert.AreEqual(new double?[] { 20, 10, 30 }, curves.Single(curve => Name(curve) == "id4").Points.ToArray());
        CollectionAssert.AreEqual(new double?[] { 7, 8, 9 }, curves.Single(curve => Name(curve) == "id3").Points.ToArray(),
                                  "and a curve sharing its line with another is a curve of its own");
    }

    [TestMethod]
    public void AnAxisOrACurveWithNoLabelIsCalledByItsName()
    {
        var tree = MermaidStaged.Read("radar-beta\n  axis A, B\n  curve c1{1, 2}");

        CollectionAssert.AreEqual(new[] { "A", "B" }, Spokes(tree).Select(Says).ToArray());
        Assert.AreEqual("c1", Says(Curves(tree).Single()));
    }

    [TestMethod]
    public void ACurveGivingAnAxisNothingHasNoPointOnIt()
    {
        var curves = Curves(MermaidStaged.Read("radar-beta\n  axis a, b, c\n  curve x{ a: 1, c: 3 }\n  curve y{ a: 1, z: 2 }"));

        CollectionAssert.AreEqual(new double?[] { 1, null, 3 }, curves[0].Points.ToArray());
        CollectionAssert.AreEqual(new double?[] { 1, null, null }, curves[1].Points.ToArray(), "and a value naming no axis is for none");
    }

    [TestMethod]
    public void WithNoMaxTheRimIsTheGreatestValue_AndMaxAndMinAreWhatIsWritten()
    {
        var chart = (RadarBlockNode)MermaidStaged.Read("radar-beta\n  axis a, b\n  curve x{2, 8}\n  curve y{4, 1}");

        Assert.AreEqual(8, chart.Max);
        Assert.AreEqual(0, chart.Min);

        var capped = (RadarBlockNode)MermaidStaged.Read("radar-beta\n  axis a, b\n  curve x{2, 8}\n  max 4\n  min 2");

        Assert.AreEqual(4, capped.Max, "a value past max does not move the rim");
        Assert.AreEqual(2, capped.Min);
    }

    [TestMethod]
    public void AChartOfNothingButNoughtsStillHasAScale()
    {
        var chart = (RadarBlockNode)MermaidStaged.Read("radar-beta\n  axis a, b\n  curve x{0, 0}");
        Assert.IsTrue(chart.Max > chart.Min);
    }

    [TestMethod]
    public void TheOptionsSetTheRingsAndTheLegend_TheLastWrittenWinning()
    {
        const string source = "radar-beta\n  axis a, b, c\n  curve x{1, 2, 3}\n  ticks 8\n  showLegend false\n  ticks 3";
        var tree = MermaidStaged.Read(source);
        var chart = (RadarBlockNode)tree;

        Assert.AreEqual(3, chart.Ticks);
        Assert.IsFalse(chart.ShowsLegend);
        Assert.AreEqual(RadarGraticule.Circle, chart.Graticule, "a circle where nothing says otherwise");
        Assert.AreEqual(source.IndexOf("ticks 3", StringComparison.Ordinal), tree.Placed().Single(place => place.Node is RadarShapingNode).Start,
                        "and a ring stands for the option shaping it");
    }

    [TestMethod]
    public void WhileItIsWrittenAnAxisOrACurveStillToNameHasItsPlace_AndOtherwiseNot()
    {
        const string source = "radar-beta\n  axis a, b, \n  curve c{1, 2}\n  curve d";

        Assert.AreEqual(2, Spokes(MermaidStaged.Read(source)).Count, "a chart only being read has a spoke for each axis named");
        Assert.AreEqual(3, Spokes(MermaidStaged.Read(source, holes: true)).Count, "and one being written for the axis still to name too");

        CollectionAssert.AreEqual(new[] { true, false }, Curves(MermaidStaged.Read(source)).Select(curve => curve.Listed).ToArray(),
                                  "a curve with nothing to draw is listed only while the chart is being written");
        CollectionAssert.AreEqual(new[] { true, true }, Curves(MermaidStaged.Read(source, holes: true)).Select(curve => curve.Listed).ToArray());
        CollectionAssert.AreEqual(new double?[] { 1, 2, null }, Curves(MermaidStaged.Read(source, holes: true))[0].Points.ToArray(),
                                  "and a curve gives the spoke still to name nothing");
    }

    [TestMethod]
    public void WhatTheStagesWriteIsNoPartOfTheSource()
    {
        foreach (var source in new[] { RadarGrammarTests.Restaurants, RadarGrammarTests.Details, "radar-beta\n  axis a, b, \n  curve c{1, }\n  ticks 3" })
        {
            Assert.AreEqual(source, MermaidStaged.Read(source).Print(), "a stage leaves the characters alone");
            Assert.AreEqual(source, MermaidStaged.Read(source, holes: true).Print());
        }
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
        var config = ((RadarBlockNode)MermaidStaged.Read("radar-beta\n  axis a")).Config;

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
                                  Curves(MermaidStaged.Read(RadarGrammarTests.Themed)).Select(curve => curve.Colour).ToArray());

    private static List<RadarAxisNode> Spokes(ContentNode tree) => [.. tree.SelfAndDescendants().OfType<RadarAxisNode>()];

    private static List<RadarCurveNode> Curves(ContentNode tree) => [.. tree.SelfAndDescendants().OfType<RadarCurveNode>()];

    private static string Name(ContentNode item) =>
        item.Children.Single(child => child.Kind == MermaidKinds.Name).Inner(MermaidKinds.Words)!.Text;

    /// <summary>What is written for an axis or a curve: its label, or its name where it has none.</summary>
    private static string Says(ContentNode item) =>
        (item.Children.FirstOrDefault(child => child.Kind == MermaidKinds.Label) ?? item.Children.Single(child => child.Kind == MermaidKinds.Name))
            .Inner(MermaidKinds.Words)!.Text;
}
