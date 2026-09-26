using Nexaflow.Markdown.Ast;
using Nexaflow.Markdown.Nomnoml;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.Markdown.Nomnoml;

/// <summary>
/// What a <c>nomnoml</c> block is read into: its lines, every character kept, each group gathered with the lines written
/// in it, and each association's operator read as the end, the line and the other end it is built from.
/// </summary>
[TestClass]
[CoversNode("class-diagram")]
public class NomnomlParserTests
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

    /// <summary>The blocks nomnoml's own documentation shows, which read with nothing wrong with them.</summary>
    private static readonly string[] Documented =
    [
        Pirates,
        Nested,
        "#direction: right\n[A] -> [B]",
        "[Shape] <:- [Circle]\n[Shape] <:- [Square]",
    ];

    /// <summary>Every construct read, and what nobody means to write.</summary>
    private static readonly (string What, string Source)[] Blocks =
    [
        ("the documentation's own pirates", Pirates),
        ("a group written across lines", Nested),
        ("a group across lines inside one", "[Outer|\n  [Inner|\n    [A]\n  ]\n  [B]\n]"),
        ("lines ended the other way", "[A] -> [B]\r\n[C]\r\n"),

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
        ("a bracket escaped in a name", "[A \\] B]"),

        ("an association", "[A] -> [B]"),
        ("one with no space in it", "[A]->[B]"),
        ("a chain of them", "[A] -> [B] -> [C]"),
        ("counts either side", "[A] 1 -> 0..n [B]"),
        ("what one says", "[A] -> [B] : leads to"),
        ("every way two nodes are joined", string.Join("\n", NomnomlParser.Operators.Select(operation => $"[A] {operation} [B]"))),

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
        ("nothing at all", ""),
    ];

    private static IEnumerable<(string What, string Source)> All => Blocks.Concat(Documented.Select(source => ("documented", source)));

    [TestMethod, TestCategory("Unit")]
    public void EveryBlockReadsBackAsItWasWritten()
    {
        foreach (var (what, source) in All)
            Assert.AreEqual(source, NomnomlParser.Parse(source).Print(), what);
    }

    [TestMethod, TestCategory("Unit")]
    public void EveryPrefixOfEveryBlockReadsBackToo()
    {
        foreach (var (what, source) in All)
            for (var length = 0; length <= source.Length; length++)
                Assert.AreEqual(source[..length], NomnomlParser.Parse(source[..length]).Print(), $"{what}: after {length} character(s)");
    }

    [TestMethod, TestCategory("Unit")]
    public void TheParserOnlyEverCopies()
    {
        foreach (var (what, source) in All)
            foreach (var place in NomnomlParser.Parse(source).Placed())
                if (place.Node.IsLeaf)
                    Assert.AreEqual(source.Substring(place.Start, place.Node.Width), place.Node.Text, $"{what}: {place.Node.Kind} at {place.Start}");
    }

    [TestMethod, TestCategory("Unit")]
    public void TheDocumentedBlocksHaveNothingWrongWithThem()
    {
        foreach (var source in Documented)
        {
            var trouble = NomnomlParser.Parse(source).SelfAndDescendants().Select(node => node.Trouble).OfType<string>().ToList();
            Assert.AreEqual(0, trouble.Count, $"{source}\n{string.Join("\n", trouble)}");
        }
    }

    [TestMethod, TestCategory("Unit")]
    public void WhatWillNotReadSaysWhy()
    {
        foreach (var source in new[] { "[A", "]", "[Outer|\n  [A]", "[A] ->", "->", "#direction", "??? !!!" })
            Assert.IsTrue(NomnomlParser.Parse(source).SelfAndDescendants().Any(node => node.Trouble is not null), source);
    }

    [TestMethod, TestCategory("Unit")]
    public void AGroupWrittenAcrossLinesHoldsTheLinesBetweenItsBrackets_AndAGroupInsideItHoldsItsOwn()
    {
        var tree = NomnomlParser.Parse("[Outer|\n  [Inner|\n    [A]\n  ]\n  [B]\n]\n[C]");
        var outer = tree.Children.Single(child => child.Kind == NomnomlKinds.Group);

        Assert.AreEqual(4, outer.Children.Count, "the line opening it, the group inside it, [B] and the line closing it");
        Assert.AreEqual(3, outer.Children.Single(child => child.Kind == NomnomlKinds.Group).Children.Count);
        Assert.AreEqual(NomnomlKinds.Line, tree.Children.Last().Kind, "and [C] is past it");
    }

    [TestMethod, TestCategory("Unit")]
    public void AnOperatorIsReadAsTheEndTheLineAndTheOtherEndItIsBuiltFrom_NeverAsAShorterOneItStartsWith()
    {
        foreach (var operation in NomnomlParser.Operators)
        {
            var read = NomnomlParser.Parse($"[A] {operation} [B]").SelfAndDescendants().Single(node => node.Kind == NomnomlKinds.Operator);
            string Piece(string role) => read.Children.FirstOrDefault(piece => piece.Role == role)?.Text ?? string.Empty;

            Assert.AreEqual(operation, read.Print(), operation);
            Assert.AreEqual(operation, Piece(NomnomlRoles.Head) + Piece(NomnomlRoles.Drawn) + Piece(NomnomlRoles.Tail), operation);
            Assert.IsTrue(NomnomlParser.Lines.Contains(Piece(NomnomlRoles.Drawn)), $"{operation} has a line");
        }
    }
}
