using Nexaflow.Markdown.Ast;
using Nexaflow.Markdown.Mermaid;
using Nexaflow.Markdown.Mermaid.Venn;
using Nexaflow.Markdown.Pipeline;
using Nexaflow.Tests.Fixtures;
using Nexaflow.Tests.Markdown.Mermaid;

namespace Nexaflow.Tests.Markdown.Mermaid.Venn;

/// <summary>
/// What a <c>venn-beta</c> block's stages write into its tree: each set and union gathered with the items written in it,
/// which region every part stands for, what styles each, and what the front matter asks for — none of which is in any one
/// line, and so is worked out over the whole block and hung underneath.
/// </summary>
[TestClass]
[CoversNode("venn-ast")]
public class VennStagesTests
{
    /// <summary>What styles each region and item, by what it is known by.</summary>
    private static Dictionary<string, MermaidStyle> Styles(string source) =>
        MermaidStaged.Read(source).SelfAndDescendants().OfType<StyledNode>()
                     .ToDictionary(node => node.Kind == VennKinds.Region ? node.Said(VennRoles.Key)! : node.Children.First(child => child.Kind == MermaidKinds.Name).Inner(MermaidKinds.Words)!.Text,
                                   node => node.Style);

    [TestMethod]
    public void ASetAndTheItemsIndentedUnderItAreOneRegionOfTheTree()
    {
        var tree = MermaidStaged.Read(VennGrammarTests.Styled);
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
        var region = MermaidStaged.Read(source).Children.Single(child => child.Kind == VennKinds.Region);

        Assert.AreEqual(4, Lines(region).Count(), "the set, both items and the comment between them");
        StringAssert.EndsWith(region.Print(), "text A2\n");
    }

    [TestMethod]
    public void AnItemWithNoRegionToSitInSaysSo()
    {
        foreach (var source in new[] { "venn-beta\n  text A1", "venn-beta\nset A\ntext A,C X" })
        {
            var trouble = MermaidStaged.Read(source).SelfAndDescendants().Single(node => node.Trouble is not null);
            StringAssert.Matches(trouble.Trouble, new System.Text.RegularExpressions.Regex("sits in|No set or union"), source);
        }
    }

    [TestMethod]
    public void AUnionOfOneSetSaysSo()
    {
        var trouble = MermaidStaged.Read("venn-beta\n  set A\n  union A,A").SelfAndDescendants().Single(node => node.Trouble is not null);
        StringAssert.Contains(trouble.Trouble, "two sets or more");
    }

    [TestMethod]
    public void AStyleOfSomethingNotWrittenSaysSo()
    {
        var trouble = MermaidStaged.Read("venn-beta\n  set A\n  style Z fill:red").SelfAndDescendants().Single(node => node.Trouble is not null);
        StringAssert.Contains(trouble.Trouble, "Nothing called Z");
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
    public void AnItemWrittenAgainstTheIndentationRuleSaysSo()
    {
        foreach (var (source, reason) in new[]
                 {
                     ("venn-beta\n  set A\ntext A1", "names its region first"),
                     ("venn-beta\n  set A\n  set B\n    text A A1", "Indented under a set"),
                 })
        {
            var trouble = MermaidStaged.Read(source).SelfAndDescendants().Single(node => node.Trouble is not null);
            StringAssert.Contains(trouble.Trouble, reason, source);
        }
    }

    [TestMethod]
    public void AUnionOfASetNotWrittenAboveItSaysSoOnTheName()
    {
        const string source = "venn-beta\n  set A\n  union A,B\n  set B";
        var trouble = MermaidStaged.Read(source).SelfAndDescendants().Single(node => node.Trouble is not null);

        Assert.AreEqual("B", trouble.Text);
        StringAssert.Contains(trouble.Trouble, "not a set written above");
    }

    [TestMethod]
    public void AnItemAtTheStartOfALineSitsInTheRegionItNames()
    {
        var tree = MermaidStaged.Read("venn-beta\nset A\nset B\nunion B,A\ntext A,B AB1[\"OpenAPI\"]\ntext A A1");
        var items = tree.SelfAndDescendants().Where(node => node.Kind == VennKinds.Text).Select(item => item.Said(VennRoles.Key)).ToArray();

        CollectionAssert.AreEqual(new[] { "A,B", "A" }, items, "named in another order, the same overlap");
        Assert.IsFalse(tree.SelfAndDescendants().Any(node => node.Trouble is not null));
    }

    [TestMethod]
    public void AStyleStylesTheSetTheUnionOrTheItemItNames_EachLaidOverTheLast()
    {
        var styles = Styles("venn-beta\n  set A\n    text A1\n  set B\n  union A,B\n"
                            + "  style A fill:#ff6b6b, stroke:#000, stroke-width:4px, fill-opacity:0.5\n  style B,A color:#333\n  style A1 color:red\n  style A stroke:#111");

        Assert.AreEqual(("#ff6b6b", "#111", 4.0, 0.5), (styles["A"].Fill, styles["A"].Stroke, styles["A"].StrokeWidth, styles["A"].FillOpacity));
        Assert.AreEqual("#333", styles["A,B"].Colour, "the overlap, whichever order its sets are named in");
        Assert.AreEqual("red", styles["A1"].Colour);
        Assert.IsFalse(styles.ContainsKey("B"), "and what nothing styles is said nothing about");
    }

    [TestMethod]
    public void TheFrontMatterIsHungOnTheBlock_AndWithNoneItIsMermaids()
    {
        static VennConfig Config(string source) => ((ConfiguredNode<VennConfig>)MermaidStaged.Read(source)).Config;

        var config = Config("venn-beta\n  set A");
        Assert.IsNull(config.Width);
        Assert.IsNull(config.Height);
        Assert.AreEqual(15, config.Padding);
        Assert.IsTrue(config.UseMaxWidth);
        Assert.IsFalse(config.UseDebugLayout);
        Assert.AreEqual(0, config.Swatches.Count);

        Assert.AreEqual(600, Config("---\nconfig:\n  venn:\n    width: 600\n---\nvenn-beta\n  set A").Width);
    }

    /// <summary>The lines a region was gathered from — not the key the pipeline hangs under it.</summary>
    private static IEnumerable<ContentNode> Lines(ContentNode region) =>
        region.Children.Where(child => child.Kind == MermaidKinds.Line);
}
