using Nexaflow.Markdown.Ast;
using Nexaflow.Markdown.Mermaid;
using Nexaflow.Markdown.Mermaid.Xy;
using Nexaflow.Tests.Fixtures;
using Nexaflow.Tests.Markdown.Mermaid;

namespace Nexaflow.Tests.Markdown.Mermaid.Xy;

/// <summary>
/// What an <c>xychart</c> block is read into: which way it runs, its title, its axes' titles, categories and ranges, and its
/// series' names, values and labels — and what is written instead of any of that, held with the reason.
/// </summary>
[TestClass]
[CoversNode("xy-chart-ast")]
public class XyGrammarTests : MermaidGrammarContract
{
    /// <summary>The chart the Mermaid documentation opens with.</summary>
    public const string Revenue =
        """
        xychart
            title "Sales Revenue"
            x-axis [jan, feb, mar, apr, may, jun, jul, aug, sep, oct, nov, dec]
            y-axis "Revenue (in $)" 4000 --> 11000
            bar [5000, 6000, 7500, 8200, 9500, 10500, 11000, 10200, 9200, 8500, 7000, 6000]
            line [5000, 6000, 7500, 8200, 9500, 10500, 11000, 10200, 9200, 8500, 7000, 6000]
        """;

    /// <summary>The documentation's chart with its config and theme.</summary>
    public const string Themed =
        """
        ---
        config:
            xyChart:
                width: 900
                height: 600
            themeVariables:
                xyChart:
                    titleColor: "#ff0000"
        ---
        xychart
            title "Sales Revenue"
            x-axis [jan, feb, mar, apr, may, jun, jul, aug, sep, oct, nov, dec]
            y-axis "Revenue (in $)" 4000 --> 11000
            bar [5000, 6000, 7500, 8200, 9500, 10500, 11000, 10200, 9200, 8500, 7000, 6000]
            line [5000, 6000, 7500, 8200, 9500, 10500, 11000, 10200, 9200, 8500, 7000, 6000]
        """;

    public override MermaidDiagram Diagram => MermaidDiagram.XyChart;

    protected override IEnumerable<string> DocumentedBlocks =>
    [
        Revenue,
        Themed,
        "xychart\n    line [+1.3, .6, 2.4, -.34]",
        "xychart horizontal\n    x-axis [cat1, \"cat2 with space\", cat3]\n    bar [1, 2, 3]",
        "xychart\n    x-axis title 0 --> 100\n    y-axis title\n    line \"series name\" [2.3, 45, .98, -3.4]",
        "xychart\n    title \"LLM parameters\"\n    x-axis [PaLM, LLaMA, Mistral]\n    line [540 \"PaLM\", 65 \"LLaMA-65B\", 7 \"Mistral 7B\"]",
    ];

    protected override IEnumerable<(string What, string Source)> Blocks { get; } =
    [
        ("xychart-beta and vertical", "xychart-beta vertical\n  x-axis [a, b]\n  bar [1, 2]"),
        ("a title of one word", "xychart\n  title Sales\n  bar [1]"),
        ("an axis titled in a word, over a range", "xychart\n  x-axis Month 1 --> 12\n  line [1, 2, 3]"),
        ("an axis with only a range", "xychart\n  y-axis -10 --> 10\n  bar [-5, 5]"),
        ("an axis with only a title", "xychart\n  y-axis \"Revenue\"\n  bar [1, 2]"),
        ("a named bar and a named line", "xychart\n  x-axis [a, b]\n  bar \"Sold\" [1, 2]\n  line Target [2, 2]"),
        ("space everywhere", "xychart\n  x-axis  \"M\"  [ a ,  b ]  \n  y-axis  0  -->  5\n  bar  [ 1 ,  2 ]  "),
        ("no space anywhere", "xychart\n  x-axis [a,b]\n  y-axis 0-->5\n  bar [1,2]"),
        ("a comment and a blank line", "xychart\n\n  %% the months\n  x-axis [a, b] %% two\n  bar [1, 2]"),
        ("accessibility lines", "xychart\n  accTitle: Sales\n  accDescr: By month\n  bar [1, 2]"),
        ("written on Windows", "xychart\r\n  x-axis [a, b]  \r\n  bar [1, 2]\r\n"),
        // Half written.
        ("an axis word alone", "xychart\n  x-axis "),
        ("categories still being written", "xychart\n  x-axis [a, "),
        ("a range with its end still to come", "xychart\n  y-axis 0 --> "),
        ("a series with no values yet", "xychart\n  bar []"),
        ("a value still to come", "xychart\n  bar [1, ]"),
        ("values never closed", "xychart\n  bar [1, 2"),
        ("an empty label", "xychart\n  line [1 \"\", 2]"),
        ("an empty title in quotes", "xychart\n  x-axis \"\" [a]"),
        // What nobody means to write.
        ("a value that is not a number", "xychart\n  bar [lots, 2]"),
        ("a range end that is not a number", "xychart\n  y-axis 0 --> lots"),
        ("categories on the y-axis", "xychart\n  y-axis [a, b]"),
        ("a series with no brackets", "xychart\n  bar 1, 2"),
        ("a label never closed", "xychart\n  line [1 \"one, 2]"),
        ("a title word and then nonsense", "xychart\n  x-axis Month 5"),
        ("a line nobody knows", "xychart\n  pie [1]"),
        ("words after the header nobody knows", "xychart sideways\n  bar [1]"),
        ("nothing but the keyword", "xychart"),
    ];

    [TestMethod]
    public void AnAxisIsItsTitleAndItsCategoriesOrItsRange()
    {
        var axes = Nodes(MermaidStaged.Read(Revenue), XyKinds.Axis);
        var categories = axes[0].Inner(XyKinds.Categories)!.SelfAndDescendants().Where(node => node.Kind == MermaidKinds.Words).ToList();

        Assert.AreEqual(12, categories.Count);
        Assert.AreEqual("jan", categories[0].Text);
        Assert.AreEqual("Revenue (in $)", Title(axes[1]));
        CollectionAssert.AreEqual(new double?[] { 4000, 11000 }, Range(axes[1]));
    }

    [TestMethod]
    public void AWordAheadOfTheArrowIsWhereTheRangeStarts_AndOneAheadOfThatIsTheTitle()
    {
        var bare = Nodes(MermaidStaged.Read("xychart\n  x-axis 1 --> 12"), XyKinds.Axis).Single();
        var titled = Nodes(MermaidStaged.Read("xychart\n  x-axis Month 1 --> 12"), XyKinds.Axis).Single();

        Assert.IsNull(Title(bare));
        CollectionAssert.AreEqual(new double?[] { 1, 12 }, Range(bare));
        Assert.AreEqual("Month", Title(titled));
        CollectionAssert.AreEqual(new double?[] { 1, 12 }, Range(titled));
    }

    /// <summary>What an axis's or a series' title says, in quotes or a word — or null where it has none.</summary>
    private static string? Title(ContentNode line) =>
        line.Children.FirstOrDefault(child => child.Kind is MermaidKinds.Quoted or MermaidKinds.Name).Words()?.Text;

    /// <summary>What the two ends of an axis's range come to.</summary>
    private static double?[] Range(ContentNode axis) =>
        [.. axis.Inner(XyKinds.Range)!.Children.Where(child => child.Kind == MermaidKinds.Amount).Select(end => end.Number())];

    private static List<ContentNode> Nodes(ContentNode tree, string kind) => [.. tree.SelfAndDescendants().Where(node => node.Kind == kind)];

    [TestMethod]
    public void AValueOfALineMayCarryALabel_SignedOrStartingAtItsPoint()
    {
        var points = Nodes(MermaidStaged.Read("xychart\n  line [540 \"PaLM\", -.34, +1.3]"), XyKinds.Point);

        CollectionAssert.AreEqual(new double?[] { 540, -0.34, 1.3 }, points.Select(point => point.Number()).ToArray());
        Assert.AreEqual("PaLM", points[0].Children.FirstOrDefault(child => child.Kind == MermaidKinds.Quoted).Words()?.Text);
        Assert.IsNull(points[1].Children.FirstOrDefault(child => child.Kind == MermaidKinds.Quoted));
    }

    [TestMethod]
    public void WhatIsStillBeingWrittenIsNoComplaint()
    {
        foreach (var source in new[] { "xychart\n  x-axis ", "xychart\n  x-axis [a, ", "xychart\n  y-axis 0 --> ", "xychart\n  bar []", "xychart\n  bar [1, ]" })
        {
            var trouble = Trouble(source);
            Assert.AreEqual(0, trouble.Count(said => !said.Contains("never closed", StringComparison.Ordinal)), $"{source}: {string.Join(" | ", trouble)}");
        }
    }

    [TestMethod]
    public void ALineThatIsNoXyChartLineIsHeldWithTheReason()
    {
        foreach (var (source, reason) in new[]
                 {
                     ("xychart\n  pie [1]", "An xychart line is"),
                     ("xychart\n  y-axis [a, b]", "not categories"),
                     ("xychart\n  bar 1, 2", "A series is"),
                     ("xychart\n  line [1 \"one, 2]", "never closed"),
                     ("xychart sideways", "horizontal or vertical"),
                 })
        {
            var held = MermaidParser.Parse(source).SelfAndDescendants().Single(node => node.Trouble is not null);

            Assert.AreEqual(Kinds.Verbatim, held.Kind, source);
            StringAssert.Contains(held.Trouble, reason, source);
        }
    }

    [TestMethod]
    public void ANewLineUnderASeriesIsAnotherOfItsKind_AndElsewhereABar()
    {
        var grammar = new XyGrammar();
        var line = MermaidParser.Parse("xychart\n  line [1]").SelfAndDescendants().Single(node => node.Kind == XyKinds.Series);

        Assert.AreEqual(("line []", 6), grammar.Blank(line));
        Assert.AreEqual(("bar []", 5), grammar.Blank(null));
    }

    [TestMethod]
    public void ACategoryGivenASpaceIsPutInQuotes()
    {
        const string source = "xychart\n  x-axis [jan, feb]";
        var jan = ContentReading.Of(MermaidStaged.Read(source)).Root.SelfAndDescendants().First(part => part.Kind == MermaidKinds.Words && part.Text == "jan");
        var writing = new XyGrammar().Escaping(jan, jan.End, " ")!.Value;

        Assert.AreEqual("xychart\n  x-axis [\"jan \", feb]", source[..writing.Start] + writing.Text + source[writing.End..]);
    }

    private static List<string> Trouble(string source) =>
        [.. MermaidStaged.Read(source).SelfAndDescendants().Select(node => node.Trouble).OfType<string>()];
}
