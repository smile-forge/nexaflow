using Nexaflow.Markdown.Mermaid;
using Nexaflow.Markdown.Nomnoml;
using Nexaflow.Tests.Fixtures;
using Nexaflow.Tests.Markdown.Mermaid;

namespace Nexaflow.Tests.Markdown.Nomnoml;

/// <summary>
/// What a nomnoml block says. Nothing in the block names its type — its fence's language does — so the grammar is
/// handed to the parser rather than found from a header, which is what <see cref="MermaidGrammarContract.Named"/>
/// says here.
/// </summary>
[TestClass]
[CoversNode("class-diagram")]
public class NomnomlGrammarTests : MermaidGrammarContract
{
    /// <summary>The diagram nomnoml's own documentation opens with.</summary>
    public const string Pirates =
        """
        [Pirate|eyeCount: Int|raid();pillage()]
        [<abstract>Marauder] <:-- [Pirate]
        [Pirate] - 0..7 [mischief]
        [jollyness] -> [Pirate]
        [Pirate] -> * [rum|tastiness: Int|swig()]
        [singing] <-> [rum]
        """;

    /// <summary>A group, written the way nomnoml's documentation writes one.</summary>
    public const string Nested =
        """
        [Decorator pattern|
          [<abstract>Component||+ operation()]
          [Client] -> [Component]
          [Component] <:- [Decorator]
        ]
        """;

    /// <summary>A nomnoml block is not a Mermaid diagram at all: its language names it, so no keyword does.</summary>
    public override MermaidDiagram Diagram => MermaidDiagram.Unknown;

    /// <inheritdoc/>
    protected override IMermaidGrammar? Named => NomnomlDiagram.Grammar;

    /// <inheritdoc/>
    protected override IEnumerable<string> DocumentedBlocks =>
    [
        Pirates,
        Nested,
        "#direction: right\n[A] -> [B]",
        "[Shape] <:- [Circle]\n[Shape] <:- [Square]",
    ];

    /// <inheritdoc/>
    protected override IEnumerable<(string What, string Source)> Blocks { get; } =
    [
        ("the documentation's own pirates", Pirates),
        ("a group written across lines", Nested),

        ("a node on its own", "[Pirate]"),
        ("a node with nothing in it", "[]"),
        ("a node with a space in its name", "[Line item]"),
        ("a node with one compartment", "[Pirate|eyeCount: Int]"),
        ("a node with two", "[Pirate|eyeCount: Int|raid()]"),
        ("two members in one compartment", "[Pirate|a: Int;b: Int]"),
        ("a compartment with nothing in it", "[Pirate||raid()]"),
        ("a node saying what it is", "[<abstract>Shape]"),
        ("a node saying it is a note", "[<note>Read me]"),
        ("a group written on one line", "[Outer|[A] -> [B]]"),
        ("a group inside a group", "[Outer|[Inner|[A]]]"),

        ("an association", "[A] -> [B]"),
        ("one with no space in it", "[A]->[B]"),
        ("a chain of them", "[A] -> [B] -> [C]"),
        ("counts either side", "[A] 1 -> 0..n [B]"),
        ("what one says", "[A] -> [B] : leads to"),
        ("every way two nodes are joined",
         "[A] - [B]\n[A] -- [C]\n[A] -/- [D]\n[A] -> [E]\n[A] <-> [F]\n[A] --> [G]\n[A] <--> [H]\n"
         + "[A] -:> [I]\n[A] <:- [J]\n[A] --:> [K]\n[A] <:-- [L]\n[A] +- [M]\n[A] +-> [N]\n[A] o- [O]\n[A] o-> [P]"),

        ("the way it is laid out", "#direction: right\n[A]"),
        ("a style directive", "#.abstract: fill=#8f8 dashed"),
        ("a directive nothing here draws", "#arrowSize: 1.5\n[A]"),
        ("a comment", "// what this is\n[A]"),
        ("a comment after a node", "[A]\n// and why"),

        ("a node never closed", "[A"),
        ("a bracket closing nothing", "]"),
        ("a group never closed", "[Outer|\n  [A]"),
        ("an association with nothing the far side", "[A] ->"),
        ("an association with nothing either side", "->"),
        ("a classifier never closed", "[<abstract Shape]"),
        ("a directive with no value", "#direction"),
        ("a directive with no name", "#: right"),
        ("nothing anybody means to write", "??? !!!"),
    ];

    [TestMethod]
    public void EveryOperatorDrawsSomethingOfItsOwn()
    {
        foreach (var operation in NomnomlGrammar.Operators)
        {
            var model = NomnomlDiagram.Read($"[A] {operation} [B]");

            Assert.AreEqual(1, model.Relations.Count, operation);
            Assert.AreEqual("A", model.Relations[0].From, operation);
            Assert.AreEqual("B", model.Relations[0].To, operation);
        }
    }

    [TestMethod]
    public void ALongerOperatorIsNeverReadAsTheShorterOneItStartsWith()
    {
        foreach (var operation in NomnomlGrammar.Operators)
        {
            var read = NomnomlDiagram.Read($"[A] {operation} [B]").Relations[0];

            Assert.AreEqual(NomnomlDiagram.Ends(operation), (read.Head, read.Tail, read.Dotted), operation);
        }
    }
}
