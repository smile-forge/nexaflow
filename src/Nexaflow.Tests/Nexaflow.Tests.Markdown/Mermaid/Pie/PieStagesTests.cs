using System.Collections.Generic;
using System.Linq;

using Nexaflow.Markdown.Ast;
using Nexaflow.Markdown.Mermaid;
using Nexaflow.Markdown.Mermaid.Pie;
using Nexaflow.Markdown.Pipeline;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.Markdown.Mermaid.Pie;

/// <summary>
/// What a <c>pie</c> block's stages write into its tree: what the front matter asks for, and for each slice the colour it
/// takes, whether it is picked out, its share of the whole, where it comes among the wedges, whether it is listed and
/// whether its row shows its value — everything a builder needs, so it only lays the chart out.
/// </summary>
[TestClass]
[CoversNode("pie-ast")]
public class PieStagesTests
{
    [TestMethod]
    public void EverySliceSaysItsShareOfTheWhole()
    {
        var slices = Slices(MermaidStaged.Read(PieGrammarTests.Documented));

        CollectionAssert.AreEqual(new[] { "Calcium", "Potassium", "Magnesium", "Iron" }, slices.Select(Name).ToArray());
        Assert.AreEqual(0.4633, Share(slices[1]), 0.001, "Potassium's share of the whole");
        Assert.AreEqual(1, slices.Sum(Share), 0.0001, "and the shares make the whole");
    }

    [TestMethod]
    public void TheBlockSaysWhatItsFrontMatterAsksFor()
    {
        var config = Config(MermaidStaged.Read(PieGrammarTests.Documented));

        Assert.AreEqual(0.5, config.TextPosition);
        Assert.AreEqual(0.2, config.DonutHole);
        Assert.AreEqual("Potassium", config.Highlight);
        Assert.AreEqual(5, config.OuterStrokeWidth, "written as \"5px\"");
        Assert.AreEqual(PieLegend.Right, config.Legend, "which nothing asked to move");
    }

    [TestMethod]
    public void WithNoFrontMatterEverythingIsMermaidsDefault()
    {
        var config = Config(MermaidStaged.Read("pie\n  \"Dogs\" : 386"));

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
    public void TheSliceTheConfigPicksOutSaysItIsPickedOut() =>
        CollectionAssert.AreEqual(new[] { false, true, false, false },
                                  Slices(MermaidStaged.Read(PieGrammarTests.Documented)).Select(slice => slice.Said(PieRoles.Highlighted) is not null).ToArray());

    [TestMethod]
    public void HoverPicksNoSliceOut()
    {
        var tree = MermaidStaged.Read("---\nconfig:\n  pie:\n    highlightSlice: hover\n---\npie\n  \"hover\" : 1\n  \"Dogs\" : 2");

        Assert.IsTrue(Config(tree).HighlightsOnHover);
        Assert.IsFalse(Slices(tree).Any(slice => slice.Said(PieRoles.Highlighted) is not null), "not even the one that happens to be called hover");
    }

    [TestMethod]
    public void EachSliceTakesTheColourWrittenForItsPlaceInTheOrder()
    {
        var slices = Slices(MermaidStaged.Read(
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
            """));

        CollectionAssert.AreEqual(new[] { "#ff0000", null, "green" }, slices.Select(slice => slice.Said(PieRoles.Colour)).ToArray());
        CollectionAssert.AreEqual(new[] { "pie1", null, "pie3" }, slices.Select(slice => slice.Said(PieRoles.Swatch)).ToArray());
    }

    [TestMethod]
    public void AndThePaletteStartsAgainAfterTwelve()
    {
        var written = string.Join('\n', Enumerable.Range(1, 13).Select(at => $"  \"n{at}\" : 1"));
        var slices = Slices(MermaidStaged.Read($"---\nconfig:\n  themeVariables:\n    pie1: red\n---\npie\n{written}"));

        Assert.AreEqual("pie1", slices[0].Said(PieRoles.Swatch));
        Assert.AreEqual("pie1", slices[12].Said(PieRoles.Swatch), "the thirteenth takes the first colour again");
        Assert.IsNull(slices[1].Said(PieRoles.Swatch));
    }

    [TestMethod]
    public void ASliceWorthNothingIsNoPartOfTheWholeAndHasNoWedge()
    {
        var slices = Slices(MermaidStaged.Read("pie\n  \"Dogs\" : 3\n  \"Cats\" : -1\n  \"Fish\" : lots\n  \"Birds\" : 1"));

        CollectionAssert.AreEqual(new[] { 0.75, 0, 0, 0.25 }, slices.Select(Share).ToArray(), "only what is worth drawing shares the whole");
        CollectionAssert.AreEqual(new[] { 0, -1, -1, 1 }, slices.Select(slice => (int)slice.HeldAs(PieRoles.Order)!).ToArray(),
                                  "and a slice's place among the wedges skips those with none");
        Assert.AreEqual(4, slices.Count, "every one of them is still read, so a reader can fix it");
    }

    [TestMethod]
    public void OnlyWhatIsDrawnIsListed_UnlessSomebodyIsWritingTheChart()
    {
        const string source = "pie\n  \"Dogs\" : 3\n  \"Cats\" : \n";

        CollectionAssert.AreEqual(new[] { true, false }, Slices(MermaidStaged.Read(source)).Select(Listed).ToArray(),
                                  "a chart only being read lists what it draws");
        CollectionAssert.AreEqual(new[] { true, true }, Slices(MermaidStaged.Read(source, holes: true)).Select(Listed).ToArray(),
                                  "a chart being written lists every slice written, where the reader is typing");
    }

    [TestMethod]
    public void ARowShowsItsValueWhereTheChartAsksOrTheValueWantsWriting()
    {
        const string plain = "pie\n  \"Dogs\" : 3\n  \"Fish\" : lots\n  \"Cats\" : \n";

        CollectionAssert.AreEqual(new[] { false, true, false }, Slices(MermaidStaged.Read(plain)).Select(ValueShown).ToArray(),
                                  "a value that is wrong is shown, so it can be put right");
        CollectionAssert.AreEqual(new[] { false, true, true }, Slices(MermaidStaged.Read(plain, holes: true)).Select(ValueShown).ToArray(),
                                  "and one still to be written, while the chart is being written");
        Assert.IsTrue(Slices(MermaidStaged.Read("pie showData\n  \"Dogs\" : 3\n")).All(ValueShown), "and every one, where the chart shows its values");
    }

    [TestMethod]
    public void WhereALabelOrAValueIsStillToBeWrittenAHoleStandsInIt()
    {
        const string source = "pie\n  \"\" : ";

        var writing = ContentPart.Of(MermaidStaged.Read(source, holes: true)).SelfAndDescendants().Where(part => part.Kind == Kinds.Hole).ToList();
        Assert.AreEqual(2, writing.Count);
        Assert.AreEqual(source.IndexOf('"') + 1, writing[0].Start, "between the quotes");
        Assert.AreEqual(source.Length, writing[1].Start, "after the space left for it");

        Assert.IsFalse(MermaidStaged.Read(source).SelfAndDescendants().Any(node => node.Kind == Kinds.Hole), "a chart only being read has none");
    }

    [TestMethod]
    public void WhatTheStagesWriteIsNoPartOfTheSource()
    {
        foreach (var source in new[] { PieGrammarTests.Documented, "pie\n  \"Dogs\" : 1", "pie\n  \"Dogs\" : 3\n  \"\" : \n  \"Cats\" : 1" })
        {
            Assert.AreEqual(source, MermaidStaged.Read(source).Print(), "a stage leaves the characters alone");
            Assert.AreEqual(source, MermaidStaged.Read(source, holes: true).Print());
        }
    }

    [TestMethod]
    public void TheChartsOwnTitleIsTheOneItUses()
    {
        Assert.AreEqual("Pets", MermaidBlock.Of(MermaidStaged.Read("---\ntitle: Elements\n---\npie title Pets\n  \"Dogs\" : 1")).TitleText);
        Assert.AreEqual("Elements", MermaidBlock.Of(MermaidStaged.Read("---\ntitle: Elements\n---\npie\n  \"Dogs\" : 1")).TitleText,
                        "and the front matter's where it has none");
        Assert.IsNull(MermaidBlock.Of(MermaidStaged.Read("pie\n  \"Dogs\" : 1")).TitleText);
    }

    private static List<ContentNode> Slices(ContentNode tree) =>
        [.. tree.SelfAndDescendants().Where(node => node.Kind == PieKinds.Slice)];

    private static PieConfig Config(ContentNode tree) => (PieConfig)tree.HeldAs(PieRoles.Config)!;

    private static string Name(ContentNode slice) => slice.Inner(MermaidKinds.Words)!.Print();

    private static double Share(ContentNode slice) => (double)slice.HeldAs(PieRoles.Share)!;

    private static bool Listed(ContentNode slice) => (bool)slice.HeldAs(PieRoles.Listed)!;

    private static bool ValueShown(ContentNode slice) => (bool)slice.HeldAs(PieRoles.ValueShown)!;
}
