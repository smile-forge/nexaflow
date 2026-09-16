using Nexaflow.Markdown.Mermaid;
using Nexaflow.Markdown.Mermaid.Pie;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.Markdown.Mermaid.Pie;

/// <summary>
/// A <c>pie</c> block read back into the chart it describes: its title, its slices and their shares, and what its
/// front matter asks for — including the colour each slice takes, which is written by position in the front matter and
/// so has to be worked out and hung under the slice.
/// </summary>
[TestClass]
[CoversNode("pie-ast")]
public class PieChartTests
{
    private static PieChart Read(string source) => PieChart.Read(source);

    [TestMethod]
    public void TheDocumentedBlockReadsAsItIsWritten()
    {
        var chart = Read(PieGrammarTests.Documented);

        Assert.IsTrue(chart.ShowsData);
        Assert.AreEqual("Key elements in Product X", chart.Block.TitleText);
        CollectionAssert.AreEqual(new[] { "Calcium", "Potassium", "Magnesium", "Iron" },
                                  chart.Slices.Select(slice => slice.Name).ToArray());

        Assert.AreEqual(108.02, chart.Total, 0.001);
        Assert.AreEqual(0.4633, chart.Share(chart.Slices[1]), 0.001, "Potassium's share of the whole");
    }

    [TestMethod]
    public void AndSoDoesWhatItsFrontMatterAsksFor()
    {
        var config = Read(PieGrammarTests.Documented).Config;

        Assert.AreEqual(0.5, config.TextPosition);
        Assert.AreEqual(0.2, config.DonutHole);
        Assert.AreEqual("Potassium", config.Highlight);
        Assert.AreEqual(5, config.OuterStrokeWidth, "written as \"5px\"");
        Assert.AreEqual(PieLegend.Right, config.Legend, "which nothing asked to move");
    }

    [TestMethod]
    public void TheSliceTheConfigPicksOutKnowsItIsPickedOut()
    {
        var slices = Read(PieGrammarTests.Documented).Slices;

        CollectionAssert.AreEqual(new[] { false, true, false, false },
                                  slices.Select(slice => slice.Highlighted).ToArray());
    }

    [TestMethod]
    public void HoverPicksNoSliceOut()
    {
        var chart = Read("---\nconfig:\n  pie:\n    highlightSlice: hover\n---\npie\n  \"hover\" : 1\n  \"Dogs\" : 2");

        Assert.IsTrue(chart.Config.HighlightsOnHover);
        Assert.IsFalse(chart.Slices.Any(slice => slice.Highlighted), "not even the one that happens to be called hover");
    }

    [TestMethod]
    public void EachSliceTakesTheColourWrittenForItsPlaceInTheOrder()
    {
        var chart = Read(
            """
            ---
            config:
              themeVariables:
                pie1: "#ff0000"
                pie3: green
            ---
            pie
                "One" : 1
                "Two" : 2
                "Three" : 3
            """);

        CollectionAssert.AreEqual(new[] { "#ff0000", null, "green" }, chart.Slices.Select(slice => slice.Colour).ToArray());
        CollectionAssert.AreEqual(new[] { "pie1", null, "pie3" }, chart.Slices.Select(slice => slice.Swatch).ToArray());
    }

    [TestMethod]
    public void AndThePaletteStartsAgainAfterTwelve()
    {
        var slices = string.Join('\n', Enumerable.Range(1, 13).Select(at => $"  \"n{at}\" : 1"));
        var chart = Read($"---\nconfig:\n  themeVariables:\n    pie1: red\n---\npie\n{slices}");

        Assert.AreEqual("pie1", chart.Slices[0].Swatch);
        Assert.AreEqual("pie1", chart.Slices[12].Swatch, "the thirteenth takes the first colour again");
        Assert.IsNull(chart.Slices[1].Swatch);
    }

    [TestMethod]
    public void WithNoFrontMatterEverythingIsMermaidsDefault()
    {
        var config = Read("pie\n  \"Dogs\" : 386").Config;

        Assert.AreEqual(0.75, config.TextPosition);
        Assert.AreEqual(0, config.DonutHole);
        Assert.AreEqual(PieLegend.Right, config.Legend);
        Assert.AreEqual(2, config.StrokeWidth, "the gap between two slices");
        Assert.IsNull(config.Opacity, "how solid a slice is drawn is the theme's until the front matter says");
        Assert.IsNull(config.TitleTextSize);
        Assert.IsNull(config.LegendTextSize);
        Assert.IsNull(config.OuterStrokeWidth, "and no line round the chart was asked for");
        Assert.IsNull(config.Highlight);
        Assert.AreEqual(0, config.Swatches.Count);
    }

    [TestMethod]
    public void AnOptionOutsideWhatItAllowsIsBroughtBackInside()
    {
        var config = PieConfig.Read("config:\n  pie:\n    textPosition: 5\n    donutHole: 2");

        Assert.AreEqual(1, config.TextPosition);
        Assert.AreEqual(0.9, config.DonutHole);
    }

    [TestMethod]
    public void TheLegendGoesWhereItIsAskedTo()
    {
        foreach (var (written, where) in new[]
                 {
                     ("left", PieLegend.Left), ("top", PieLegend.Top), ("bottom", PieLegend.Bottom),
                     ("center", PieLegend.Centre), ("right", PieLegend.Right), ("sideways", PieLegend.Right),
                 })
            Assert.AreEqual(where, PieConfig.Read($"config:\n  pie:\n    legendPosition: {written}").Legend, written);
    }

    [TestMethod]
    public void ASliceWorthNothingIsNoPartOfTheWhole()
    {
        var chart = Read("pie\n  \"Dogs\" : 3\n  \"Cats\" : -1\n  \"Fish\" : lots");

        Assert.AreEqual(3, chart.Total, "only what is worth drawing");
        Assert.IsFalse(chart.Slices[1].Drawn);
        Assert.IsNotNull(chart.Slices[2].Trouble);
        Assert.AreEqual(3, chart.Slices.Count, "and every one of them is still read, so a reader can fix it");
    }

    [TestMethod]
    public void WhereALabelOrAValueIsStillToBeWrittenAHoleStandsInIt()
    {
        const string source = "pie\n  \"\" : ";
        var writing = PieChart.Of(MermaidParser.Read(source, holes: true)).Slices.Single();
        var reading = PieChart.Of(MermaidParser.Read(source)).Slices.Single();

        Assert.AreEqual(source.IndexOf('"') + 1, writing.LabelHole!.Start, "between the quotes");
        Assert.AreEqual(source.Length, writing.ValueHole!.Start, "after the space left for it");
        Assert.IsFalse(writing.Drawn, "and there is nothing of it to draw yet");

        Assert.IsNull(reading.LabelHole, "a chart only being read has none");
        Assert.IsNull(reading.ValueHole);
    }

    [TestMethod]
    public void AndAHoleIsNoPartOfTheSource()
    {
        const string source = "pie\n  \"Dogs\" : 3\n  \"\" : \n  \"Cats\" : 1";
        Assert.AreEqual(source, MermaidParser.Read(source, holes: true).Print());
    }

    [TestMethod]
    public void TheChartsOwnTitleIsTheOneItUses()
    {
        Assert.AreEqual("Pets", Read("---\ntitle: Elements\n---\npie title Pets\n  \"Dogs\" : 1").Block.TitleText);
        Assert.AreEqual("Elements", Read("---\ntitle: Elements\n---\npie\n  \"Dogs\" : 1").Block.TitleText,
                        "and the front matter's where it has none");
        Assert.IsNull(Read("pie\n  \"Dogs\" : 1").Block.TitleText);
    }

    [TestMethod]
    public void WhatTheStagesHangUnderneathIsNoPartOfTheSource()
    {
        foreach (var source in new[] { PieGrammarTests.Documented, "pie\n  \"Dogs\" : 1" })
            Assert.AreEqual(source, MermaidParser.Read(source).Print(), "a stage leaves the characters alone");
    }

    [TestMethod]
    public void EverySlicePointsAtWhatWasWrittenForIt()
    {
        const string source = "pie\n  \"Calcium\" : 42.96";
        var slice = Read(source).Slices.Single();

        Assert.AreEqual("\"Calcium\" : 42.96", source.Substring(slice.Part.Start, slice.Part.Length));
        Assert.AreEqual("Calcium", source.Substring(slice.Label.Start, slice.Label.Length));
        Assert.AreEqual("42.96", source.Substring(slice.Value!.Start, slice.Value.Length));
    }
}
