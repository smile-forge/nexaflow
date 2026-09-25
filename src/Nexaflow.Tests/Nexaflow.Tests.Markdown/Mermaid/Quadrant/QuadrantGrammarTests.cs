using Nexaflow.Markdown.Ast;
using Nexaflow.Markdown.Mermaid;
using Nexaflow.Markdown.Mermaid.Quadrant;
using Nexaflow.Tests.Fixtures;
using Nexaflow.Tests.Markdown.Mermaid;

namespace Nexaflow.Tests.Markdown.Mermaid.Quadrant;

/// <summary>
/// What a <c>quadrantChart</c> block is read into: its axes' ends, its quadrants' captions, its points with their classes,
/// positions and styles, and its classDefs — and what is written instead of any of that, held with the reason.
/// </summary>
[TestClass]
[CoversNode("quadrant-graph-ast")]
public class QuadrantGrammarTests : MermaidGrammarContract
{
    /// <summary>The chart the Mermaid documentation opens with.</summary>
    public const string Campaigns =
        """
        quadrantChart
            title Reach and engagement of campaigns
            x-axis Low Reach --> High Reach
            y-axis Low Engagement --> High Engagement
            quadrant-1 We should expand
            quadrant-2 Need to promote
            quadrant-3 Re-evaluate
            quadrant-4 May be improved
            Campaign A: [0.3, 0.6]
            Campaign B: [0.45, 0.23]
            Campaign C: [0.57, 0.69]
            Campaign D: [0.78, 0.34]
            Campaign E: [0.40, 0.34]
            Campaign F: [0.35, 0.78]
        """;

    /// <summary>The documentation's points styled inline and by class, the classes written under them.</summary>
    public const string Styled =
        """
        quadrantChart
          title Reach and engagement of campaigns
          x-axis Low Reach --> High Reach
          y-axis Low Engagement --> High Engagement
          quadrant-1 We should expand
          quadrant-2 Need to promote
          quadrant-3 Re-evaluate
          quadrant-4 May be improved
          Campaign A: [0.9, 0.0] radius: 12
          Campaign B:::class1: [0.8, 0.1] color: #ff3300, radius: 10
          Campaign C: [0.7, 0.2] radius: 25, color: #00ff33, stroke-color: #10f0f0
          Campaign D: [0.6, 0.3] radius: 15, stroke-color: #00ff0f, stroke-width: 5px ,color: #ff33f0
          Campaign E:::class2: [0.5, 0.4]
          Campaign F:::class3: [0.4, 0.5] color: #0000ff
          classDef class1 color: #109060
          classDef class2 color: #908342, radius : 10, stroke-color: #310085, stroke-width: 10px
          classDef class3 color: #f00fff, radius : 10
        """;

    public override MermaidDiagram Diagram => MermaidDiagram.Quadrant;

    protected override IEnumerable<string> DocumentedBlocks =>
    [
        Campaigns,
        Styled,
        "---\nconfig:\n  quadrantChart:\n    chartWidth: 400\n    chartHeight: 400\n  themeVariables:\n    quadrant1TextFill: \"ff0000\"\n---\nquadrantChart\n  x-axis Urgent --> Not Urgent\n  y-axis Not Important --> \"Important ❤\"\n  quadrant-1 Plan\n  quadrant-2 Do\n  quadrant-3 Delegate\n  quadrant-4 Delete",
    ];

    protected override IEnumerable<(string What, string Source)> Blocks { get; } =
    [
        ("an axis with only its low end", "quadrantChart\n  x-axis Low Reach\n  A: [0.1, 0.2]"),
        ("axis ends in quotes", "quadrantChart\n  x-axis \"Low: reach\" --> \"High\""),
        ("a point named in quotes", "quadrantChart\n  \"A: the first\": [0.1, 0.2]"),
        ("no space anywhere", "quadrantChart\n  x-axis Low-->High\n  A:[0.1,0.2] radius:5"),
        ("a comment and a blank line", "quadrantChart\n\n  %% the points\n  A: [0.1, 0.2] %% one"),
        ("accessibility lines", "quadrantChart\n  accTitle: Reach\n  accDescr: By campaign\n  A: [0.1, 0.2]"),
        ("written on Windows", "quadrantChart\r\n  quadrant-1 Plan  \r\n  A: [0.1, 0.2]\r\n"),
        // Half written.
        ("an axis word alone", "quadrantChart\n  x-axis "),
        ("an axis still to name its high end", "quadrantChart\n  x-axis Low --> "),
        ("a point with its position still to come", "quadrantChart\n  A: "),
        ("a point with no name yet", "quadrantChart\n  : [0.5, 0.5]"),
        ("a position never closed", "quadrantChart\n  A: [0.1, "),
        ("a classDef with no style yet", "quadrantChart\n  classDef hot"),
        ("an empty caption in quotes", "quadrantChart\n  quadrant-2 \"\""),
        // What nobody means to write.
        ("a position past 1", "quadrantChart\n  A: [1.5, 0.2]"),
        ("a position of one number", "quadrantChart\n  A: [0.1]"),
        ("a class nobody writes", "quadrantChart\n  A:::cold: [0.1, 0.2]"),
        ("a style nobody knows", "quadrantChart\n  A: [0.1, 0.2] size: 4"),
        ("a line with no colon", "quadrantChart\n  Campaign A"),
        ("a caption never closed", "quadrantChart\n  quadrant-1 \"Plan"),
        ("nothing but the keyword", "quadrantChart"),
    ];

    [TestMethod]
    public void TheDocumentedChartsLinesAreEachRead()
    {
        var tree = MermaidStaged.Read(Campaigns);

        Assert.AreEqual(2, Nodes(tree, QuadrantKinds.Axis).Count);
        Assert.AreEqual(4, Nodes(tree, QuadrantKinds.Region).Count);
        Assert.AreEqual(6, Nodes(tree, QuadrantKinds.Point).Count);
    }

    [TestMethod]
    public void WhatIsStillBeingWrittenIsNoComplaint()
    {
        foreach (var source in new[] { "quadrantChart\n  x-axis ", "quadrantChart\n  x-axis Low --> ", "quadrantChart\n  A: ", "quadrantChart\n  : [0.5, 0.5]", "quadrantChart\n  classDef hot" })
            Assert.AreEqual(0, Trouble(source).Count, $"{source}: {string.Join(" | ", Trouble(source))}");
    }

    [TestMethod]
    public void WhatIsWrongIsSaid()
    {
        foreach (var (source, reason) in new[]
                 {
                     ("quadrantChart\n  A: [1.5, 0.2]", "from 0 to 1"),
                     ("quadrantChart\n  A: [0.1]", "two numbers"),
                     ("quadrantChart\n  A: [0.1, ", "never closed"),
                     ("quadrantChart\n  A:::cold: [0.1, 0.2]", "No classDef cold"),
                     ("quadrantChart\n  A: [0.1, 0.2] size: 4", "radius"),
                 })
            Assert.IsTrue(Trouble(source).Any(said => said.Contains(reason, StringComparison.Ordinal)), $"{source}: {string.Join(" | ", Trouble(source))}");
    }

    [TestMethod]
    public void ALineThatIsNoQuadrantLineIsHeldWithTheReason()
    {
        foreach (var (source, reason) in new[] { ("quadrantChart\n  Campaign A", "A point is"), ("quadrantChart\n  quadrant-1 \"Plan", "never closed") })
        {
            var held = MermaidParser.Parse(source).SelfAndDescendants().Single(node => node.Trouble is not null);

            Assert.AreEqual(Kinds.Verbatim, held.Kind, source);
            StringAssert.Contains(held.Trouble, reason, source);
        }
    }

    [TestMethod]
    public void ANewLineUnderACaptionIsTheNextQuadrants_AndElsewhereAPoint()
    {
        var grammar = new QuadrantGrammar();
        var region = Nodes(MermaidParser.Parse("quadrantChart\n  quadrant-1 Plan"), QuadrantKinds.Region).Single();

        Assert.AreEqual(("quadrant-2 ", 11), grammar.Blank(region));
        Assert.AreEqual((": [0.5, 0.5]", 0), grammar.Blank(null));
    }

    [TestMethod]
    public void AColonTypedIntoABareNamePutsItInQuotes()
    {
        const string source = "quadrantChart\n  Campaign A: [0.1, 0.2]";
        var name = ContentReading.Of(MermaidStaged.Read(source)).Root.SelfAndDescendants().First(part => part.Kind == MermaidKinds.Words && part.Text == "Campaign A");
        var writing = new QuadrantGrammar().Escaping(name, name.End, ":")!.Value;

        Assert.AreEqual("quadrantChart\n  \"Campaign A:\": [0.1, 0.2]", source[..writing.Start] + writing.Text + source[writing.End..]);
    }

    [TestMethod]
    public void AClassRenamedWhereItIsDeclaredIsRenamedInThePointsTakingIt()
    {
        var hot = new QuadrantGrammar().Names(ContentReading.Of(MermaidStaged.Read(Styled)).Root).Single(name => name.Name == "class1");
        Assert.AreEqual(1, hot.Uses.Count);
    }

    private static List<ContentNode> Nodes(ContentNode tree, string kind) => [.. tree.SelfAndDescendants().Where(node => node.Kind == kind)];

    private static List<string> Trouble(string source) =>
        [.. MermaidStaged.Read(source).SelfAndDescendants().Select(node => node.Trouble).OfType<string>()];
}
