using Nexaflow.Markdown.Ast;
using Nexaflow.Markdown.Mermaid;
using Nexaflow.Markdown.Pipeline;
using Nexaflow.Markdown.Treemap;
using Nexaflow.Tests.Fixtures;
using Nexaflow.Tests.Markdown.Ast;

namespace Nexaflow.Tests.Markdown.Treemap;

/// <summary>
/// What a <c>treemap</c> block is read into: its items, each a label in quotes and a leaf's value, every character kept — and, once
/// its stages have run, each item gathered with the items indented under it, saying what it and they come to.
/// </summary>
[TestClass]
[CoversNode("treemap-ast")]
public class TreemapParserTests
{
    /// <summary>The blocks Mermaid's documentation shows, which read with nothing wrong with them.</summary>
    public static readonly string[] Documented =
    [
        "treemap-beta\n\"Category A\"\n    \"Item A1\": 10\n    \"Item A2\": 20\n\"Category B\"\n    \"Item B1\": 15\n    \"Item B2\": 25",
        "treemap-beta\n\"Products\"\n    \"Electronics\"\n        \"Phones\": 50\n        \"Computers\": 30\n        \"Accessories\": 20\n    \"Clothing\"\n        \"Men's\": 40\n        \"Women's\": 40",
        "treemap-beta\n\"Section 1\"\n    \"Leaf 1.1\": 12\n    \"Section 1.2\":::class1\n      \"Leaf 1.2.1\": 12\n\"Section 2\"\n    \"Leaf 2.1\": 20:::class1\n    \"Leaf 2.2\": 25\n    \"Leaf 2.3\": 12\n\nclassDef class1 fill:red,color:blue,stroke:#FFD600;",
        "treemap-beta\n\"Main\"\n    \"A\": 20\n    \"B\":::important\n        \"B1\": 10\n        \"B2\": 15\n    \"C\": 5\n\nclassDef important fill:#f96,stroke:#333,stroke-width:2px;",
        "---\nconfig:\n    theme: 'forest'\n---\ntreemap-beta\n\"Category A\"\n    \"Item A1\": 10\n    \"Item A2\": 20",
        "---\nconfig:\n  treemap:\n    valueFormat: '$0,0'\n---\ntreemap-beta\n\"Budget\"\n    \"Operations\"\n        \"Salaries\": 700000\n        \"Equipment\": 200000",
    ];

    /// <summary>Every construct read, and what nobody means to write.</summary>
    public static readonly (string What, string Source)[] Blocks =
    [
        ("a header and nothing else", "treemap-beta"),
        ("the shorter header", "treemap\n\"A\": 1"),
        ("no header, as a treemap fence needs none", "\"A\": 1\n\"B\": 2"),
        ("a title on a line of its own", "treemap-beta\ntitle Spending\n\"A\": 1"),
        ("a comment after the header", "treemap-beta %% the map\n\"A\": 1"),
        ("a leaf", "treemap-beta\n\"A\": 1"),
        ("a section and its leaves", "treemap-beta\n\"S\"\n  \"A\": 1\n  \"B\": 2"),
        ("a comma before a value", "treemap-beta\n\"A\", 1"),
        ("a value grouped and with a point", "treemap-beta\n\"A\": 1,500.25\n\"B\": 1_000"),
        ("a label in single quotes, an apostrophe in double", "treemap-beta\n'A': 1\n\"Men's\": 2"),
        ("a class on a section and a leaf", "treemap-beta\n\"S\":::big\n  \"A\": 1:::big\nclassDef big fill:#f00"),
        ("space everywhere", "treemap-beta\n  \"S\"   :::big  \n    \"A\"  :  1  \nclassDef big fill:#f00;"),
        ("tabs for indentation", "treemap-beta\n\"S\"\n\t\"A\": 1\n\t\"B\": 2"),
        ("a comment, and one after an item", "treemap-beta\n%% the items\n\"A\": 1 %% one\n\"B\": 2"),
        ("blank lines between items", "treemap-beta\n\"S\"\n\n  \"A\": 1\n\n\"B\": 2"),
        ("accessibility lines", "treemap-beta\naccTitle: A map\naccDescr: Its items\n\"A\": 1"),
        ("a directive", "%%{init: {'theme': 'dark'}}%%\ntreemap-beta\n\"A\": 1"),
        ("front matter with its config", "---\nconfig:\n  treemap:\n    padding: 4\n    showValues: false\n---\ntreemap-beta\n\"A\": 1"),
        ("written on Windows", "treemap-beta\r\n\"S\"\r\n  \"A\": 1  \r\n  \"B\": 2\r\n"),
        // Half written.
        ("a label with nothing in it", "treemap-beta\n\"\": 1"),
        ("a colon and nothing after it", "treemap-beta\n\"A\":"),
        ("a colon and the space after it", "treemap-beta\n\"A\": "),
        ("a class mark and nothing after it", "treemap-beta\n\"A\":::"),
        ("a section with nothing in it yet", "treemap-beta\n\"S\""),
        // What nobody means to write.
        ("a label never closed", "treemap-beta\n\"A: 1\n\"B\": 2"),
        ("a label not in quotes", "treemap-beta\nA: 1"),
        ("a value that is no number", "treemap-beta\n\"A\": many"),
        ("something after an item", "treemap-beta\n\"A\": 1 and more"),
        ("a class no classDef writes", "treemap-beta\n\"A\": 1:::missing"),
        ("a header taking more than its word", "treemap-beta title\n\"A\": 1"),
        ("nothing at all", ""),
    ];

    private static IEnumerable<(string What, string Source)> All => Blocks.Concat(Documented.Select(source => ("documented", source)));

    /// <summary>A block parsed and worked over by its stages, holes and all where somebody is writing in it.</summary>
    internal static ContentNode Staged(string source, bool holes = false)
    {
        var tree = TreemapParser.Parse(source);
        return new AstPipeline(TreemapPipeline.Of(tree, holes)).Run(tree);
    }

    [TestMethod, TestCategory("Unit")]
    public void EveryBlockReadsBackAsItWasWritten()
    {
        foreach (var (what, source) in All)
        {
            Assert.AreEqual(source, TreemapParser.Parse(source).Print(), what);
            Assert.AreEqual(source, Staged(source).Print(), $"{what}, after the stages");
            Assert.AreEqual(source, Staged(source, holes: true).Print(), $"{what}, with holes");
        }
    }

    [TestMethod, TestCategory("Unit")]
    public void AndSoDoesEveryStageOnTheWayThere()
    {
        foreach (var (what, source) in All)
            foreach (var holes in new[] { false, true })
            {
                var tree = TreemapParser.Parse(source);

                foreach (var stage in TreemapPipeline.Of(tree, holes))
                {
                    tree = stage.Run(tree);
                    Assert.AreEqual(source, tree.Print(), $"{what}: after {stage.Name}{(holes ? ", with holes" : "")}");
                }
            }
    }

    [TestMethod, TestCategory("Unit")]
    public void EveryPrefixOfEveryBlockReadsBackToo()
    {
        foreach (var (what, source) in All)
            for (var length = 0; length <= source.Length; length++)
                Assert.AreEqual(source[..length], Staged(source[..length], holes: true).Print(), $"{what}: after {length} character(s)");
    }

    [TestMethod, TestCategory("Unit")]
    public void TheParserOnlyEverCopies()
    {
        foreach (var (what, source) in All)
            foreach (var place in TreemapParser.Parse(source).Placed())
                if (place.Node.IsLeaf)
                    Assert.AreEqual(source.Substring(place.Start, place.Node.Width), place.Node.Text, $"{what}: {place.Node.Kind} at {place.Start}");
    }

    [TestMethod]
    public void EveryPieceSaysWhereItWasRead()
    {
        foreach (var (what, source) in All)
            foreach (var (stage, tree) in new[] { ("read", TreemapParser.Parse(source)), ("after the stages", Staged(source)) })
            {
                var faults = AstOracle.Faults(source, tree).ToList();
                Assert.AreEqual(0, faults.Count, $"{what}, {stage}\n{string.Join("\n", faults)}");
            }
    }

    [TestMethod]
    public void AReversedTreeStillPrintsWhatWasWritten()
    {
        foreach (var (what, source) in All)
            Assert.AreEqual(source, AstOracle.Reversed(TreemapParser.Parse(source)).Print(), what);
    }

    [TestMethod, TestCategory("Unit")]
    public void TheDocumentedBlocksHaveNothingWrongWithThem()
    {
        foreach (var source in Documented)
        {
            var trouble = Staged(source).SelfAndDescendants().Select(node => node.Trouble).OfType<string>().ToList();
            Assert.AreEqual(0, trouble.Count, $"{source}\n{string.Join("\n", trouble)}");
        }
    }

    [TestMethod, TestCategory("Unit")]
    public void WhatWillNotReadSaysWhy()
    {
        foreach (var source in new[]
                 {
                     "treemap-beta\n\"A: 1", "treemap-beta\nA: 1", "treemap-beta\n\"A\": many", "treemap-beta\n\"A\": 1 and more", "treemap-beta title",
                 })
            Assert.IsTrue(TreemapParser.Parse(source).SelfAndDescendants().Any(node => node.Trouble is not null), source);

        Assert.IsTrue(Staged("treemap-beta\n\"A\": 1:::missing").SelfAndDescendants().Any(node => node.Trouble?.Contains("missing", StringComparison.Ordinal) == true),
                      "a class nobody defines is the stages' to see");
    }

    [TestMethod, TestCategory("Unit")]
    public void NothingStillToWriteIsWrong()
    {
        foreach (var source in new[] { "treemap-beta\n\"\": 1", "treemap-beta\n\"A\":", "treemap-beta\n\"A\": ", "treemap-beta\n\"S\"", "treemap-beta\ntitle" })
            Assert.IsFalse(Staged(source, holes: true).SelfAndDescendants().Any(node => node.Trouble is not null && !node.IsDerived), source);
    }

    [TestMethod, TestCategory("Unit")]
    public void AHoleStandsInALabelAndAValueStillToWrite()
    {
        var holes = Staged("treemap-beta\n\"\": 1\n\"B\":", holes: true).SelfAndDescendants().Count(node => node.Kind == Kinds.Hole);
        Assert.AreEqual(2, holes);
    }

    [TestMethod, TestCategory("Unit")]
    public void EachItemIsGatheredWithTheItemsIndentedUnderIt()
    {
        var tree = ContentPart.Of(Staged("treemap-beta\n\"S\"\n  \"A\": 1\n  \"T\"\n    \"B\": 2\n\"C\": 3"));
        var top = tree.Children.Where(part => part.Kind == TreemapKinds.Node).ToList();

        CollectionAssert.AreEqual(new[] { "S", "C" }, top.Select(Label).ToArray(), "any number of items at the top");
        CollectionAssert.AreEqual(new[] { "A", "T" }, Under(top[0]).Select(Label).ToArray());
        CollectionAssert.AreEqual(new[] { "B" }, Under(Under(top[0])[1]).Select(Label).ToArray());
    }

    [TestMethod, TestCategory("Unit")]
    public void ALineIndentedLessThanItsSiblingButMoreThanItsUncleHangsOffTheNearestLineIndentedLess()
    {
        var tree = ContentPart.Of(Staged("treemap-beta\n\"S\"\n    \"A\": 1\n  \"B\": 2"));
        var section = tree.Children.Single(part => part.Kind == TreemapKinds.Node);

        CollectionAssert.AreEqual(new[] { "A", "B" }, Under(section).Select(Label).ToArray(), "as Mermaid nests a line nobody lined up");
    }

    [TestMethod, TestCategory("Unit")]
    public void ACommentBetweenTwoLeavesIsInTheirSection_AndABlankLineAfterItIsNot()
    {
        const string source = "treemap-beta\n\"S\"\n  \"A\": 1\n  %% note\n  \"B\": 2\n\n\"C\": 3";
        var section = ContentPart.Of(Staged(source)).Children.First(part => part.Kind == TreemapKinds.Node);

        Assert.AreEqual("\"S\"\n  \"A\": 1\n  %% note\n  \"B\": 2\n", section.Print());
    }

    [TestMethod, TestCategory("Unit")]
    public void AnItemComesToItsValueAndEverythingUnderIt()
    {
        var items = Items(Staged("treemap-beta\n\"S\"\n  \"A\": 1,000\n  \"T\"\n    \"B\": 2.5\n\"C\", 3"));

        Assert.AreEqual(1002.5, Said(items, "S").Total, 1e-9);
        Assert.AreEqual(2.5, Said(items, "T").Total, 1e-9);
        Assert.AreEqual(1000, Said(items, "A").Value);
        Assert.AreEqual(3, Said(items, "C").Value, "a comma may stand where the colon goes");
        Assert.IsTrue(Said(items, "S").Section);
        Assert.IsFalse(Said(items, "A").Section);
    }

    [TestMethod, TestCategory("Unit")]
    public void AnItemIsDrawnWithTheDefaultClassAndThenTheClassItTakes()
    {
        var items = Items(Staged("treemap-beta\n\"A\": 1:::hot\n\"B\": 2\nclassDef default stroke:#000\nclassDef hot fill:#f00,color:#fff"));

        Assert.AreEqual("#f00", Said(items, "A").Style.Fill);
        Assert.AreEqual("#000", Said(items, "A").Style.Stroke, "what the default class writes and its own does not");
        Assert.AreEqual("#000", Said(items, "B").Style.Stroke);
        Assert.IsNull(Said(items, "B").Style.Fill);
    }

    [TestMethod, TestCategory("Unit")]
    public void AValueIsSetAsItsFormatSays()
    {
        Assert.AreEqual("700,000", TreemapValues.Format(700000, null));
        Assert.AreEqual("0.35", TreemapValues.Format(0.35, ","));
        Assert.AreEqual("$700,000", TreemapValues.Format(700000, "$0,0"));
        Assert.AreEqual("$1,234.50", TreemapValues.Format(1234.5, "$,.2f"));
        Assert.AreEqual("$12.35", TreemapValues.Format(12.345, "$.2f"));
        Assert.AreEqual("35.0%", TreemapValues.Format(0.35, ".1%"));
        Assert.AreEqual("$35.0%", TreemapValues.Format(0.35, "$.1%"), "Mermaid's dollar goes in front of whatever the rest sets");
        Assert.AreEqual("12.3", TreemapValues.Format(12.34, ".1f"));
        Assert.AreEqual("-1,500", TreemapValues.Format(-1500, ","));
    }

    [TestMethod, TestCategory("Unit")]
    public void AValueReadsAsMermaidReadsIt()
    {
        Assert.AreEqual(1500, TreemapValues.Read("1,500"));
        Assert.AreEqual(1000, TreemapValues.Read("1_000"));
        Assert.AreEqual(2.5, TreemapValues.Read("2.5"));
        Assert.IsNull(TreemapValues.Read("1.2.3"));
        Assert.IsNull(TreemapValues.Read(","));
    }

    [TestMethod, TestCategory("Unit")]
    public void AMermaidBlockHeadedTreemapIsATreemap()
    {
        Assert.AreEqual(MermaidDiagram.Treemap, MermaidDiagrams.Named("treemap-beta"));
        Assert.AreEqual(MermaidDiagram.Treemap, MermaidDiagrams.Named("treemap"));
        Assert.AreEqual(MermaidDiagram.Treemap, MermaidBlock.Of(TreemapParser.Parse("treemap-beta\n\"A\": 1")).Diagram);
    }

    private static string Label(ContentPart item) => item.Children[0].Stated()!.Inner(TreemapKinds.Label)!.Words()!.Text;

    private static List<ContentPart> Under(ContentPart item) => [.. item.Children.Where(part => part.Kind == TreemapKinds.Node)];

    private static List<ContentPart> Items(ContentNode tree) => [.. ContentPart.Of(tree).SelfAndDescendants().Where(part => part.Kind == TreemapKinds.Node)];

    private static TreemapNode Said(List<ContentPart> items, string label) => (TreemapNode)items.Single(item => Label(item) == label).Node;
}
