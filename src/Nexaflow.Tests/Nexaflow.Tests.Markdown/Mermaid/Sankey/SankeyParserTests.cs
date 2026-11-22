using Nexaflow.Markdown.Ast;
using Nexaflow.Markdown.Mermaid;
using Nexaflow.Markdown.Mermaid.Sankey;
using Nexaflow.Markdown.Pipeline;
using Nexaflow.Markdown.Pipeline.Stages;
using Nexaflow.Tests.Fixtures;
using Nexaflow.Tests.Markdown.Ast;

namespace Nexaflow.Tests.Markdown.Mermaid.Sankey;

/// <summary>
/// What <see cref="SankeyParser"/> reads a sankey into: a flow to a row, written as the three columns of a CSV record — where it
/// comes from, where it goes, and what it is worth — a field in quotes running to its closing quote wherever that stands, and
/// the header optional, since a <c>sankey</c> fence already says what the block is.
/// </summary>
[TestClass]
[CoversNode("sankey-ast")]
public class SankeyParserTests : ContentParserContract
{
    /// <summary>The flows the documentation opens with, cut to the ones that reach each other.</summary>
    public const string Energy =
        """
        sankey-beta

        Agricultural 'waste',Bio-conversion,124.729
        Bio-conversion,Liquid,0.597
        Bio-conversion,Losses,26.862
        Bio-conversion,Solid,280.322
        Bio-conversion,Gas,81.144
        """;

    /// <inheritdoc/>
    protected override ContentNode Parse(string? source) => SankeyParser.Parse(source);

    protected override IEnumerable<string> DocumentedBlocks =>
    [
        "sankey\n\"A\",\"B\",\"10\"\nA,C,12 TWh\nC,D,10%\n",
        Energy,
        "sankey-beta\n\nElectricity grid,Over generation / exports,104.453\nElectricity grid,Heating and cooling - homes,113.726",
        "---\nconfig:\n  sankey:\n    showValues: false\n---\nsankey-beta\n\na,b,10\nb,c,10",
        "---\nconfig:\n  sankey:\n    width: 800\n    height: 400\n    linkColor: source\n    nodeAlignment: justify\n---\nsankey-beta\n\na,b,10",
        "---\nconfig:\n  sankey:\n    prefix: $\n    suffix: B\n---\nsankey-beta\n\na,b,10",
        "sankey-beta\na,b,10\nb,c,-4",
    ];

    protected override IEnumerable<(string What, string Source)> Blocks { get; } =
    [
        ("a front matter and a title", "---\ntitle: Where it goes\nconfig:\n  sankey:\n    linkColor: \"#7f7f7f\"\n---\nsankey-beta\n\na,b,10"),
        ("a name in quotes", "sankey-beta\n\"Agricultural waste\",Bio-conversion,124.729"),
        ("a name with a comma in it", "sankey-beta\n\"Waste, agricultural\",Bio-conversion,124.729"),
        ("a name with a quote in it, written twice", "sankey-beta\n\"Agricultural \"\"waste\"\"\",Bio-conversion,124.729"),
        ("space round the fields", "sankey-beta\n  a , b , 10"),
        ("a whole number and nought", "sankey-beta\na,b,10\nb,c,0"),
        ("the header written without its beta", "sankey\na,b,10"),
        ("no header, as a sankey fence holds it", "a,b,10\nb,c,5"),
        ("a comment after the keyword", "sankey-beta %% where it goes\na,b,10"),
        ("a blank line between the flows", "sankey-beta\n\na,b,10\n\nb,c,10"),
        ("written on Windows", "sankey-beta\r\n\r\na,b,10\r\n"),
        ("a name in quotes across a line ending", "sankey-beta\n\"Agricultural\nwaste\",Bio-conversion,124.729\nBio-conversion,Losses,26.862"),
        ("a name in quotes across a line ending, on Windows", "sankey-beta\r\n\"Agricultural\r\nwaste\",b,10\r\nb,c,5\r\n"),
        ("a quote written twice at a line ending", "sankey-beta\n\"Agricultural \"\"\nwaste\"\"\",b,10"),
        ("a comma and a comment's marks in quotes", "sankey-beta\n\"a, %% b\",c,10"),
        ("a quote inside a name written bare", "sankey-beta\nab\"c,d,1\ne,f,2"),
        // Half written.
        ("a flow still to say what it is worth", "sankey-beta\na,b,"),
        ("a flow still to say where it goes", "sankey-beta\na,"),
        ("a flow still to say anything at all", "sankey-beta\n,,"),
        ("a name still to be written between its quotes", "sankey-beta\n\"\",b,10"),
        ("nothing but the keyword", "sankey-beta"),
        // What nobody means to write.
        ("something after the keyword", "sankey-beta where it goes\na,b,10"),
        ("a name never closed", "sankey-beta\n\"Agricultural waste,b,10"),
        ("a name never closed, with flows under it", "sankey-beta\n\"Agricultural waste,b,10\nc,d,2\ne,f,3"),
        ("a value that is no number", "sankey-beta\na,b,lots"),
        ("a value worth less than nothing", "sankey-beta\na,b,-4"),
        ("a row of two columns", "sankey-beta\na,10"),
        ("a row of four columns", "sankey-beta\na,b,10,20"),
        ("something no row of a sankey is", "sankey-beta\nwhat?"),
    ];

    [TestMethod]
    public void WhatIsWrongIsSaid()
    {
        foreach (var (source, said) in new[]
                 {
                     ("sankey-beta\na,b,lots", "is not a number"),
                     
                     ("sankey-beta\n\"Agricultural waste,b,10", "never closed with a quote"),
                     ("sankey-beta\na,10", "where it comes from, where it goes"),
                     // A fourth column is read as part of what the flow is worth, which is then no number — Mermaid reads three and no more.
                     ("sankey-beta\na,b,10,20", "is not a number"),
                     ("sankey-beta where it goes\na,b,10", "its keyword alone"),
                 })
        {
            var trouble = Parse(source).SelfAndDescendants().Select(node => node.Trouble).OfType<string>().ToList();
            Assert.IsTrue(trouble.Any(reason => reason.Contains(said, StringComparison.Ordinal)),
                          $"{source}\nsays {string.Join(" / ", trouble)}, and nothing about '{said}'");
        }
    }

    [TestMethod]
    public void TheHeaderIsOptional_SinceASankeyFenceSaysWhatTheBlockIs()
    {
        var read = Parse("a,b,10\nb,c,5");

        Assert.AreEqual(SankeyParser.Language, ((BlockNode)read).Language);
        Assert.AreEqual(2, Flows(read).Count, "the first line is a flow, not a header gone wrong");
        Assert.AreEqual(0, read.SelfAndDescendants().Count(node => node.Trouble is not null));
    }

    [TestMethod]
    public void ANameInQuotesRunsToItsClosingQuote_AcrossALineEnding()
    {
        var read = Parse("sankey-beta\n\"Agricultural\nwaste\",Bio-conversion,124.729\nBio-conversion,Losses,26.862");
        var flows = Flows(read);

        Assert.AreEqual(2, flows.Count, "the line ending is inside the name, so it ends no flow");
        Assert.AreEqual("\"Agricultural\nwaste\",Bio-conversion,124.729", flows[0].Print());
        Assert.AreEqual(0, read.SelfAndDescendants().Count(node => node.Trouble is not null));
    }

    [TestMethod]
    public void AQuoteNothingClosesCostsOnlyTheRowItIsOn()
    {
        Assert.AreEqual(2, Flows(Parse("sankey-beta\n\"Agricultural waste,b,10\nc,d,2\ne,f,3")).Count, "the rows under it are flows as written");
    }

    [TestMethod]
    public void OnlyAQuoteOpeningAFieldOpensOne()
    {
        // A quote in the middle of a name written bare is no field in quotes, so it does not run on into the rows under it.
        Assert.AreEqual("e,f,2\"", Parse("sankey-beta\nab\"c,d,1\ne,f,2\"").Children.Last().Print());
    }

    /// <summary>Every flow read, in the order written.</summary>
    private static List<ContentNode> Flows(ContentNode read) =>
        [.. read.SelfAndDescendants().Where(node => node.Kind == SankeyKinds.Flow)];

    /// <summary>The stages a sankey's language offers the engine (<c>Shipped.Sankey</c>), named again here.</summary>
    protected override IEnumerable<IAstStage> Stages(ContentNode tree, bool holes) =>
    [
        new WithConfig<SankeyConfig>(SankeyConfig.Read(MermaidBlock.Of(tree).Config)),
        .. holes ? [new WithHoles(SankeyParser.Holds)] : Array.Empty<IAstStage>(),
    ];
}
