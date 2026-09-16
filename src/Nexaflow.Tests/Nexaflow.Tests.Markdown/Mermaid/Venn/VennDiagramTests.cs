using Nexaflow.Markdown.Ast;
using Nexaflow.Markdown.Mermaid;
using Nexaflow.Markdown.Mermaid.Venn;
using Nexaflow.Markdown.Pipeline;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.Markdown.Mermaid.Venn;

/// <summary>
/// A <c>venn-beta</c> block read back into the diagram it describes: its sets and the unions where they overlap, each a
/// region holding the items written in it, their styles, and what the front matter asks for — much of which is not in
/// any one line, and so is worked out by the pipeline and hung underneath.
/// </summary>
[TestClass]
[CoversNode("venn-ast")]
public class VennDiagramTests
{
    private static VennDiagram Read(string source) => VennDiagram.Read(source);

    [TestMethod]
    public void TheDocumentedFeaturesReadAsTheyAreWritten()
    {
        var diagram = Read(VennGrammarTests.Features);

        Assert.AreEqual("What makes a good feature", diagram.TitleText);
        CollectionAssert.AreEqual(new[] { "Desirable", "Feasible", "Viable" }, diagram.Sets.Select(set => set.Id).ToArray());
        CollectionAssert.AreEqual(new[] { "Desirable,Feasible", "Feasible,Viable", "Desirable,Viable", "Desirable,Feasible,Viable" },
                                  diagram.Unions.Select(union => union.Key).ToArray());
        CollectionAssert.AreEqual(new[] { "Buildable", "Sustainable", "Marketable", "Ship it" },
                                  diagram.Unions.Select(union => union.Label!.Text).ToArray());
    }

    [TestMethod]
    public void WhereNoSizeIsWrittenTheSizesAreMermaids()
    {
        var diagram = Read(VennGrammarTests.Features);

        Assert.IsTrue(diagram.Sets.All(set => set.Weight == 10), "a set is ten");
        Assert.AreEqual(2.5, diagram.Unions[0].Weight, "two sets share ten over four");
        Assert.AreEqual(10.0 / 9, diagram.Unions[3].Weight, 1e-9, "and three, ten over nine");
    }

    [TestMethod]
    public void AndWhereOneIsWrittenItIsTheOneUsed()
    {
        var diagram = Read(VennGrammarTests.Styled);

        CollectionAssert.AreEqual(new[] { 20.0, 12.0 }, diagram.Sets.Select(set => set.Weight).ToArray());
        Assert.AreEqual(3, diagram.Unions.Single().Weight);
    }

    [TestMethod]
    public void ASetAndTheItemsIndentedUnderItAreOneRegionOfTheTree()
    {
        var tree = VennPipeline.Read(VennGrammarTests.Styled);
        var regions = tree.Children.Where(child => child.Kind == VennKinds.Region).ToList();

        Assert.AreEqual(3, regions.Count, "set A with its items, set B, and the union");
        CollectionAssert.AreEqual(new[] { "set A[\"Alpha\"]:20", "text A1[\"React\"]", "text A2[\"Design Systems\"]" },
                                  Lines(regions[0]).Select(line => line.Print().Trim()).ToArray());
        Assert.AreEqual("A", regions[0].Said(VennRoles.Key), "known by its set's name");
        Assert.AreEqual("A,B", regions[2].Said(VennRoles.Key));

        Assert.AreEqual(VennGrammarTests.Styled, tree.Print(), "and gathering them changes nothing written");
    }

    [TestMethod]
    public void ACommentBetweenItemsGoesWithThem_AndOneAfterTheLastDoesNot()
    {
        const string source = "venn-beta\n  set A\n    text A1\n    %% more\n    text A2\n  %% the styles\n  style A fill:red";
        var region = VennPipeline.Read(source).Children.Single(child => child.Kind == VennKinds.Region);

        Assert.AreEqual(4, Lines(region).Count(), "the set, both items and the comment between them");
        StringAssert.EndsWith(region.Print(), "text A2\n");
    }

    [TestMethod]
    public void EachItemIsInTheRegionItIsWrittenIn()
    {
        var diagram = Read("venn-beta\n  set A[\"Frontend\"]\n    text A1[\"React\"]\n  set B\n  union A,B[\"Shared\"]\n    text AB1[\"OpenAPI\"]");

        CollectionAssert.AreEqual(new[] { "React" }, diagram.Sets[0].Items.Select(item => item.Label!.Text).ToArray());
        Assert.AreEqual(0, diagram.Sets[1].Items.Count);
        CollectionAssert.AreEqual(new[] { "AB1" }, diagram.Unions.Single().Items.Select(item => item.Id).ToArray());
    }

    [TestMethod]
    public void AnItemAtTheStartOfALineSitsInTheRegionItNames()
    {
        const string source = "venn-beta\nset A\nset B\nunion B,A\ntext A,B AB1[\"OpenAPI\"]\ntext A A1";
        var diagram = Read(source);

        Assert.AreEqual("OpenAPI", diagram.Unions.Single().Items.Single().Label!.Text, "named in another order, the same overlap");
        Assert.AreEqual("A1", diagram.Sets[0].Items.Single().Id);
        Assert.IsFalse(VennPipeline.Read(source).SelfAndDescendants().Any(node => node.Trouble is not null));
    }

    [TestMethod]
    public void AnItemWrittenAgainstTheIndentationRuleSaysSo_ButStillSitsWhereItPlainlyBelongs()
    {
        foreach (var (source, reason, region) in new[]
                 {
                     ("venn-beta\n  set A\ntext A1", "names its region first", "A"),
                     ("venn-beta\n  set A\n  set B\n    text A A1", "Indented under a set", "A"),
                 })
        {
            var trouble = VennPipeline.Read(source).SelfAndDescendants().Single(node => node.Trouble is not null);
            StringAssert.Contains(trouble.Trouble, reason, source);

            var diagram = Read(source);
            Assert.AreEqual("A1", diagram.Sets.Single(set => set.Id == region).Items.Single().Id, source);
        }
    }

    [TestMethod]
    public void AnItemWithNoRegionToSitInSaysSo()
    {
        foreach (var source in new[] { "venn-beta\n  text A1", "venn-beta\nset A\ntext A,C X" })
        {
            var trouble = VennPipeline.Read(source).SelfAndDescendants().Single(node => node.Trouble is not null);
            StringAssert.Matches(trouble.Trouble, new System.Text.RegularExpressions.Regex("sits in|No set or union"), source);
        }
    }

    [TestMethod]
    public void AUnionOfASetNotWrittenAboveItSaysSoOnTheName_AndIsNoOverlap()
    {
        const string source = "venn-beta\n  set A\n  union A,B\n  set B";
        var trouble = VennPipeline.Read(source).SelfAndDescendants().Single(node => node.Trouble is not null);

        Assert.AreEqual("B", trouble.Text);
        StringAssert.Contains(trouble.Trouble, "not a set written above");
        Assert.AreEqual(0, Read(source).Unions.Count);
    }

    [TestMethod]
    public void AUnionOfOneSetSaysSo()
    {
        var trouble = VennPipeline.Read("venn-beta\n  set A\n  union A,A").SelfAndDescendants().Single(node => node.Trouble is not null);
        StringAssert.Contains(trouble.Trouble, "two sets or more");
    }

    [TestMethod]
    public void ASetWrittenTwiceIsOneSet_ItsLaterLabelAndSizeWinning()
    {
        var diagram = Read("venn-beta\n  set A\n    text A1\n  set A[\"Alpha\"]:5\n    text A2");
        var set = diagram.Sets.Single();

        Assert.AreEqual("Alpha", set.Label!.Text);
        Assert.AreEqual(5, set.Weight);
        CollectionAssert.AreEqual(new[] { "A1", "A2" }, set.Items.Select(item => item.Id).ToArray());
    }

    [TestMethod]
    public void AStyleStylesTheSetTheUnionOrTheItemItNames()
    {
        var diagram = Read(
            "venn-beta\n  set A\n    text A1\n  set B\n  union A,B\n"
            + "  style A fill:#ff6b6b, stroke:#000, stroke-width:4px, fill-opacity:0.5\n  style B,A color:#333\n  style A1 color:red");

        var a = diagram.Sets[0].Style;
        Assert.AreEqual("#ff6b6b", a.Fill);
        Assert.AreEqual("#000", a.Stroke);
        Assert.AreEqual(4, a.StrokeWidth);
        Assert.AreEqual(0.5, a.FillOpacity);

        Assert.AreEqual("#333", diagram.Unions.Single().Style.Colour, "the overlap, whichever order its sets are named in");
        Assert.AreEqual("red", diagram.Sets[0].Items.Single().Style.Colour);
        Assert.AreEqual(VennStyle.None, diagram.Sets[1].Style);
    }

    [TestMethod]
    public void AStyleOfSomethingNotWrittenSaysSo()
    {
        var trouble = VennPipeline.Read("venn-beta\n  set A\n  style Z fill:red").SelfAndDescendants().Single(node => node.Trouble is not null);
        StringAssert.Contains(trouble.Trouble, "Nothing called Z");
    }

    [TestMethod]
    public void EachSetTakesTheColourWrittenForItsPlace_AndThePaletteStartsAgainAfterEight()
    {
        var sets = string.Join('\n', Enumerable.Range(1, 9).Select(at => $"  set S{at}"));
        var diagram = Read($"---\nconfig:\n  themeVariables:\n    venn1: \"#ff0000\"\n    venn3: green\n---\nvenn-beta\n{sets}");

        Assert.AreEqual("#ff0000", diagram.Sets[0].Colour);
        Assert.AreEqual("venn1", diagram.Sets[0].Swatch);
        Assert.IsNull(diagram.Sets[1].Colour, "nothing written for the second, which is the theme's");
        Assert.AreEqual("green", diagram.Sets[2].Colour);
        Assert.AreEqual("venn1", diagram.Sets[8].Swatch, "the ninth takes the first colour again");
    }

    [TestMethod]
    public void TheFrontMatterIsWhatTheDiagramAsksFor()
    {
        var config = VennConfig.Read(
            """
            config:
              venn:
                width: 600
                height: 400
                padding: 12px
                useMaxWidth: false
                useDebugLayout: true
              themeVariables:
                vennTitleTextColor: "#123456"
                vennSetTextColor: white
            """);

        Assert.AreEqual(600, config.Width);
        Assert.AreEqual(400, config.Height);
        Assert.AreEqual(12, config.Padding);
        Assert.IsFalse(config.UseMaxWidth);
        Assert.IsTrue(config.UseDebugLayout);
        Assert.AreEqual("#123456", config.TitleTextColour);
        Assert.AreEqual("white", config.SetTextColour);
    }

    [TestMethod]
    public void WithNoFrontMatterTheSizeIsTheBuildersAndTheRestMermaids()
    {
        var config = Read("venn-beta\n  set A").Config;

        Assert.IsNull(config.Width);
        Assert.IsNull(config.Height);
        Assert.AreEqual(15, config.Padding);
        Assert.IsTrue(config.UseMaxWidth);
        Assert.IsFalse(config.UseDebugLayout);
        Assert.AreEqual(0, config.Swatches.Count);
    }

    [TestMethod]
    public void TwoSetsOverlapByTheirUnion_OrAQuarterOfTheSmallerWhereOnlyALargerUnionOverlapsThem()
    {
        var written = Read("venn-beta\n  set A:20\n  set B:12\n  union A,B:3");
        Assert.AreEqual(3, written.Overlap(written.Sets[0], written.Sets[1]));

        var implied = Read("venn-beta\n  set A:20\n  set B:12\n  set C\n  union A,B,C");
        Assert.AreEqual(3, implied.Overlap(implied.Sets[0], implied.Sets[1]), "a quarter of twelve");

        var apart = Read("venn-beta\n  set A\n  set B");
        Assert.AreEqual(0, apart.Overlap(apart.Sets[0], apart.Sets[1]), "and nothing says two sets on their own overlap");
    }

    [TestMethod]
    public void WhereANameOrALabelIsStillToBeWrittenAHoleStandsInIt()
    {
        const string source = "venn-beta\n  set A[\"\"]\n    text \"\"";

        var writing = VennDiagram.Of(VennPipeline.Read(source, holes: true));
        var reading = Read(source);

        Assert.AreEqual(source.IndexOf("[\"", StringComparison.Ordinal) + 2, writing.Sets[0].LabelHole!.Start, "between the label's quotes");
        Assert.AreEqual(source.LastIndexOf('"'), writing.Sets[0].Items.Single().NameHole!.Start, "and the item's name's");

        Assert.IsNull(reading.Sets[0].LabelHole, "a diagram only being read has none");
        Assert.AreEqual(source, VennPipeline.Read(source, holes: true).Print(), "and a hole is no part of the source");
    }

    [TestMethod]
    public void TheDiagramsOwnTitleIsTheOneItUses()
    {
        Assert.AreEqual("Pets", Read("---\ntitle: Elements\n---\nvenn-beta\n  title \"Pets\"\n  set A").TitleText);
        Assert.AreEqual("Elements", Read("---\ntitle: Elements\n---\nvenn-beta\n  set A").TitleText, "and the front matter's where it has none");
        Assert.IsNull(Read("venn-beta\n  set A").TitleText);
    }

    [TestMethod]
    public void EveryPartPointsAtWhatWasWrittenForIt()
    {
        const string source = "venn-beta\n  set A[\"Alpha\"]:20\n    text A1[\"React\"]";
        var set = Read(source).Sets.Single();

        Assert.AreEqual("set A[\"Alpha\"]:20\n    text A1[\"React\"]", source.Substring(set.Part.Start, set.Part.Length).Trim(),
                        "the region is the set's line and its items");
        Assert.AreEqual("Alpha", source.Substring(set.Label!.Start, set.Label.Length));
        Assert.AreEqual("20", source.Substring(set.Size!.Start, set.Size.Length));
        Assert.AreEqual("text A1[\"React\"]", source.Substring(set.Items[0].Part.Start, set.Items[0].Part.Length));
    }

    /// <summary>The lines a region was gathered from — not the key the pipeline hangs under it.</summary>
    private static IEnumerable<ContentNode> Lines(ContentNode region) =>
        region.Children.Where(child => child.Kind == MermaidKinds.Line);
}
