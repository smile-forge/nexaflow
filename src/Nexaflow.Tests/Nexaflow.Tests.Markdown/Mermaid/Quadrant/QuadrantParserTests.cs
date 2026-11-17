using Nexaflow.Markdown.Ast;
using Nexaflow.Markdown.Mermaid;
using Nexaflow.Markdown.Mermaid.Quadrant;
using Nexaflow.Markdown.Mermaid.Quadrant.Stages;
using Nexaflow.Markdown.Pipeline;
using Nexaflow.Markdown.Pipeline.Stages;
using Nexaflow.Tests.Fixtures;
using Nexaflow.Tests.Markdown.Ast;

namespace Nexaflow.Tests.Markdown.Mermaid.Quadrant;

/// <summary>
/// What <see cref="QuadrantParser"/> reads a quadrant chart into: its axes' ends, its quadrants' captions, its points with their
/// classes, positions and styles, and its classDefs — and what is written instead of any of that, held with the reason. What is
/// in quotes runs to its closing quote wherever that stands, and the header is optional, since a <c>quadrant</c> fence already
/// says what the block is.
/// </summary>
[TestClass]
[CoversNode("quadrant-graph-ast")]
public class QuadrantParserTests : ContentParserContract
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

    /// <inheritdoc/>
    protected override ContentNode Parse(string? source) => QuadrantParser.Parse(source);

    /// <inheritdoc/>
    protected override IEnumerable<IAstStage> Stages(ContentNode tree, bool holes) => Offered(tree, holes);

    protected override IEnumerable<string> DocumentedBlocks =>
    [
        "quadrantChart\n  x-axis Low --> High;\n  quadrant-1 Expand; quadrant-2 Promote\n  A: [0.3, 0.6];\n",
        "quadrantChart title Reach\n  A: [0.3, 0.6]",
        "quadrantChart\n  x-axis Low ---> High\n  y-axis Down --> Up",
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
        ("a caption in quotes across a line ending", "quadrantChart\n  quadrant-1 \"We should\nexpand\"\n  A: [0.1, 0.2]"),
        ("a point named in quotes across a line ending, on Windows", "quadrantChart\r\n  \"Campaign\r\nA\": [0.1, 0.2]\r\n"),
        ("a comment's marks in quotes", "quadrantChart\n  quadrant-1 \"100%% sure\""),
        ("a quote in a title", "quadrantChart\n  title Say \"hi\n  A: [0.1, 0.2]"),
        ("words after the keyword", "quadrantChart please\n  A: [0.1, 0.2]"),
        ("no header, as a quadrant fence holds it", "x-axis Low --> High\nA: [0.1, 0.2]"),
        ("a binding supplying points", "quadrantChart\n  A: [0.1, 0.2]\n  {{Points}}\n"),
        // Half written.
        ("an axis word alone", "quadrantChart\n  x-axis "),
        ("an axis still to name its high end", "quadrantChart\n  x-axis Low --> "),
        ("a point with its position still to come", "quadrantChart\n  A: "),
        ("a point with no name yet", "quadrantChart\n  : [0.5, 0.5]"),
        ("a position never closed", "quadrantChart\n  A: [0.1, "),
        ("a classDef with no style yet", "quadrantChart\n  classDef hot"),
        ("an empty caption in quotes", "quadrantChart\n  quadrant-2 \"\""),
        ("a title with nothing after it yet", "quadrantChart\n  title"),
        // What nobody means to write.
        ("a position past 1", "quadrantChart\n  A: [1.5, 0.2]"),
        ("a position of one number", "quadrantChart\n  A: [0.1]"),
        ("a class nobody writes", "quadrantChart\n  A:::cold: [0.1, 0.2]"),
        ("a style nobody knows", "quadrantChart\n  A: [0.1, 0.2] size: 4"),
        ("a line with no colon", "quadrantChart\n  Campaign A"),
        ("a caption never closed", "quadrantChart\n  quadrant-1 \"Plan"),
        ("a caption never closed, with points under it", "quadrantChart\n  quadrant-1 \"Plan\n  A: [0.1, 0.2]\n  B: [0.3, 0.4]"),
        ("nothing but the keyword", "quadrantChart"),
    ];

    /// <summary>
    /// The stages a quadrant chart's language offers the engine (<c>Shipped.Quadrant</c>), named again here — what the engine adds
    /// for every language, what a host binds and what runs of words are made of, is held where the engine reads a block.
    /// </summary>
    internal static IReadOnlyList<IAstStage> Offered(ContentNode tree, bool holes) =>
    [
        new ResolvePoints(QuadrantConfig.Read(MermaidBlock.Of(tree).Config)),
        .. holes ? [new WithHoles(QuadrantParser.Holds)] : Array.Empty<IAstStage>(),
    ];

    /// <summary>A block parsed and worked over by its stages, holes and all where somebody is writing in it.</summary>
    internal static ContentNode Staged(string source, bool holes = false)
    {
        var tree = QuadrantParser.Parse(source);
        return new AstPipeline(Offered(tree, holes)).Run(tree);
    }

    [TestMethod]
    public void TheDocumentedChartsLinesAreEachRead()
    {
        var tree = Staged(Campaigns);

        Assert.AreEqual(2, Nodes(tree, QuadrantKinds.Axis).Count);
        Assert.AreEqual(4, Nodes(tree, QuadrantKinds.Region).Count);
        Assert.AreEqual(6, Nodes(tree, QuadrantKinds.Point).Count);
    }

    [TestMethod]
    public void WhatIsInQuotesRunsToItsClosingQuote_AcrossALineEnding()
    {
        var tree = QuadrantParser.Parse("quadrantChart\n  quadrant-1 \"We should\nexpand\"\n  A: [0.1, 0.2]");

        Assert.AreEqual("We should\nexpand", Nodes(tree, QuadrantKinds.Region).Single().SelfAndDescendants().First(node => node.Kind == Kinds.Words).Text);
        Assert.AreEqual(1, Nodes(tree, QuadrantKinds.Point).Count, "the line ending is inside the caption, so it ends no line");
        Assert.AreEqual(0, tree.SelfAndDescendants().Count(node => node.Trouble is not null));
    }

    [TestMethod]
    public void AQuoteNothingClosesCostsOnlyTheLineItIsOn()
    {
        Assert.AreEqual(2, Nodes(QuadrantParser.Parse("quadrantChart\n  quadrant-1 \"Plan\n  A: [0.1, 0.2]\n  B: [0.3, 0.4]"), QuadrantKinds.Point).Count,
                        "the points under it are points as written");
    }

    [TestMethod]
    public void TheHeaderIsOptional_SinceAQuadrantFenceSaysWhatTheBlockIs()
    {
        var tree = QuadrantParser.Parse("x-axis Low --> High\nA: [0.1, 0.2]");

        Assert.AreEqual(QuadrantParser.Language, ((BlockNode)tree).Language);
        Assert.AreEqual(1, Nodes(tree, QuadrantKinds.Axis).Count, "the first line is an axis, not a header gone wrong");
        Assert.AreEqual(0, tree.SelfAndDescendants().Count(node => node.Trouble is not null));
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
                     
                     ("quadrantChart\n  A: [0.1, 0.2] size: 4", "radius"),
                 })
            Assert.IsTrue(Trouble(source).Any(said => said.Contains(reason, StringComparison.Ordinal)), $"{source}: {string.Join(" | ", Trouble(source))}");
    }

    [TestMethod]
    public void ALineThatIsNoQuadrantLineIsHeldWithTheReason()
    {
        foreach (var (source, reason) in new[] { ("quadrantChart\n  Campaign A", "A point is"), ("quadrantChart\n  quadrant-1 \"Plan", "never closed") })
        {
            var held = QuadrantParser.Parse(source).SelfAndDescendants().Single(node => node.Trouble is not null);

            Assert.AreEqual(Kinds.Verbatim, held.Kind, source);
            StringAssert.Contains(held.Trouble, reason, source);
        }
    }

    private static List<ContentNode> Nodes(ContentNode tree, string kind) => [.. tree.SelfAndDescendants().Where(node => node.Kind == kind)];

    private static List<string> Trouble(string source) =>
        [.. Staged(source).SelfAndDescendants().Select(node => node.Trouble).OfType<string>()];
}
