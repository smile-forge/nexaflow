using Nexaflow.Markdown.Ast;
using Nexaflow.Markdown.Mermaid;
using Nexaflow.Tests.Fixtures;
using Nexaflow.Tests.Markdown.Mermaid;

namespace Nexaflow.Tests.Markdown.Mermaid.Sankey;

/// <summary>
/// What a <c>sankey-beta</c> block is read into: a flow to a line, written as the three columns of a CSV row — where it
/// comes from, where it goes, and what it is worth.
/// </summary>
[TestClass]
[CoversNode("sankey-ast")]
public class SankeyGrammarTests : MermaidGrammarContract
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

    public override MermaidDiagram Diagram => MermaidDiagram.Sankey;

    protected override IEnumerable<string> DocumentedBlocks =>
    [
        Energy,
        "sankey-beta\n\nElectricity grid,Over generation / exports,104.453\nElectricity grid,Heating and cooling - homes,113.726",
        "---\nconfig:\n  sankey:\n    showValues: false\n---\nsankey-beta\n\na,b,10\nb,c,10",
        "---\nconfig:\n  sankey:\n    width: 800\n    height: 400\n    linkColor: source\n    nodeAlignment: justify\n---\nsankey-beta\n\na,b,10",
        "---\nconfig:\n  sankey:\n    prefix: $\n    suffix: B\n---\nsankey-beta\n\na,b,10",
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
        ("a blank line between the flows", "sankey-beta\n\na,b,10\n\nb,c,10"),
        ("written on Windows", "sankey-beta\r\n\r\na,b,10\r\n"),
        // Half written.
        ("a flow still to say what it is worth", "sankey-beta\na,b,"),
        ("a flow still to say where it goes", "sankey-beta\na,"),
        ("a flow still to say anything at all", "sankey-beta\n,,"),
        ("a name still to be written between its quotes", "sankey-beta\n\"\",b,10"),
        ("nothing but the keyword", "sankey-beta"),
        // What nobody means to write.
        ("a name never closed", "sankey-beta\n\"Agricultural waste,b,10"),
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
                     ("sankey-beta\na,b,-4", "nought or more"),
                     ("sankey-beta\n\"Agricultural waste,b,10", "never closed with a quote"),
                     ("sankey-beta\na,10", "where it comes from, where it goes"),
                     // A fourth column is read as part of what the flow is worth, which is then no number — Mermaid reads three and no more.
                     ("sankey-beta\na,b,10,20", "is not a number"),
                 })
        {
            var trouble = MermaidStaged.Read(source).SelfAndDescendants().Select(node => node.Trouble).OfType<string>().ToList();
            Assert.IsTrue(trouble.Any(reason => reason.Contains(said, StringComparison.Ordinal)),
                          $"{source}\nsays {string.Join(" / ", trouble)}, and nothing about '{said}'");
        }
    }
}
