using System;
using System.Linq;

using Nexaflow.Markdown.Ast;
using Nexaflow.Markdown.Mermaid;
using Nexaflow.Markdown.Mermaid.Flowchart;
using Nexaflow.Markdown.Mermaid.Sankey;
using Nexaflow.Markdown.Packet;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.Markdown.Mermaid;

/// <summary>
/// A binding standing where a diagram's content would be (<c>{{Path}}</c>): the diagram's parser holds it as written, and
/// <see cref="MermaidParser.Bind"/> reads what it comes to into its place — with that same parser — as content nobody wrote
/// here, so the tree still prints as its author wrote it. A diagram is never bound whole: the header names it.
/// </summary>
[TestClass]
[CoversNode("mermaid")]
public class MermaidBindingTests
{
    /// <summary>What reads a flowchart, which the blocks here are, and so what reads what is bound into one.</summary>
    private static readonly Func<string?, ContentNode> Reads = FlowchartParser.Parse;

    private static ContentNode[] Bound(ContentNode tree) =>
        [.. tree.SelfAndDescendants().Where(node => node.Kind == Kinds.BoundContent)];

    private static ContentNode[] Supplied(ContentNode tree) =>
        [.. tree.Children.Where(line => line.Role == Roles.Supplied)];

    [TestMethod]
    public void ABindingOnALineOfItsOwnIsHeldAsWritten()
    {
        var source = "graph TD\n  a --> b\n  {{More}}\n";
        var tree = Reads(source);

        Assert.AreEqual(source, tree.Print(), "held, not read");
        Assert.AreEqual(1, Bound(tree).Length);
    }

    [TestMethod]
    public void ABindingAmongWordsIsTheWordsOwn()
    {
        var tree = Reads("graph TD\n  a[\"Owned by {{Name}}\"]\n");

        Assert.AreEqual(0, Bound(tree).Length, "a binding in a label is read where the label is drawn");
    }

    [TestMethod]
    public void ADiagramIsNeverBoundWhole_ABindingBeforeTheHeaderIsAParseError()
    {
        // The header names the diagram, and so the language reading it: a diagram bound whole would be one whose type nothing names.
        var source = "{{Graph}}\n";
        var tree = Reads(source);
        var bound = MermaidParser.Bind(tree, _ => "graph TD\n  a --> b\n", Reads);

        Assert.AreEqual(source, tree.Print());
        Assert.AreEqual(0, Bound(tree).Length, "it is not a binding at all");
        StringAssert.Contains(tree.SelfAndDescendants().Single(node => node.Trouble is not null).Trouble, "not bound whole");
        Assert.AreEqual(0, Supplied(bound).Length, "and nothing is supplied for it");
    }

    [TestMethod]
    public void AfterTheHeaderWhatIsSuppliedIsLinesOfTheDiagramItNames()
    {
        var source = "graph TD\n  a --> b\n  {{More}}\n";
        var bound = MermaidParser.Bind(Reads(source), _ => "b --> c\nc --> d\n", Reads);

        Assert.AreEqual(source, bound.Print());
        Assert.AreEqual(2, Supplied(bound).Length, "a line of the diagram for each line supplied");
        Assert.AreEqual(1, bound.SelfAndDescendants().Count(node => node.Kind == MermaidKinds.Header),
                        "the header it was read after is not supplied again");
    }

    [TestMethod]
    public void FrontMatterWhatIsSuppliedOpensWithIsFrontMatter()
    {
        var source = "graph TD\n  a --> b\n  {{More}}\n";
        var supplied = "---\nconfig:\n  nexaflow:\n    collapsed:\n      c: \"C\"\n---\nb --> c\n";
        var bound = MermaidParser.Bind(Reads(source), _ => supplied, Reads);

        var folds = MermaidBlock.Of(bound).Configs.Aggregate(NexaflowConfig.None, (all, yaml) => all.And(NexaflowConfig.Read(yaml)));

        Assert.AreEqual("C", folds.Collapsed["c"], "what was supplied says which of its nodes have more behind them");
    }

    [TestMethod]
    public void EveryFrontMatterSaysItsPart()
    {
        var own = NexaflowConfig.Read("config:\n  nexaflow:\n    collapsed:\n      a: \"A\"\n");
        var more = NexaflowConfig.Read("config:\n  nexaflow:\n    collapsed:\n      a: \"Other\"\n      b: \"B\"\n");

        var both = own.And(more);

        Assert.AreEqual("A", both.Collapsed["a"], "the first to say wins");
        Assert.AreEqual("B", both.Collapsed["b"]);
    }

    [TestMethod]
    public void ABindingNothingSuppliesIsLeftAsWrittenSayingSo()
    {
        var source = "graph TD\n  {{Graph}}\n";
        var bound = MermaidParser.Bind(Reads(source), _ => null, Reads);

        Assert.AreEqual(source, bound.Print());
        Assert.AreEqual(0, Supplied(bound).Length);
        StringAssert.Contains(Bound(bound).Single().Trouble, "Nothing is bound to 'Graph'.");
    }

    [TestMethod]
    public void ABlockWithNoBindingIsTheSameBlock()
    {
        var tree = Reads("graph TD\n  a --> b\n");

        Assert.AreSame(tree, MermaidParser.Bind(tree, _ => "never asked", Reads));
    }

    [TestMethod]
    public void WhatIsSuppliedIsReadByTheDiagramsOwnParser()
    {
        // A sankey reads its own block, and a name in quotes runs across a line ending there: the shared reader, handed a line at
        // a time, would read the two halves as two rows that are no flows.
        var source = "sankey-beta\n{{Flows}}\n";
        Func<string?, ContentNode> reads = SankeyParser.Parse;
        var bound = MermaidParser.Bind(reads(source), _ => "\"Agricultural\nwaste\",Bio-conversion,124.729\n", reads);

        Assert.AreEqual(source, bound.Print());
        var flows = Supplied(bound).SelectMany(line => line.SelfAndDescendants()).Where(node => node.Kind == SankeyKinds.Flow).ToList();
        Assert.AreEqual(1, flows.Count, "one flow, its name written across two lines");
        Assert.AreEqual(0, Supplied(bound).SelectMany(line => line.SelfAndDescendants()).Count(node => node.Trouble is not null));
    }

    [TestMethod]
    public void ABlockWithNoHeader_ItsFenceNamingIt_IsSuppliedLinesReadAsItIsRead()
    {
        // A packet fence names the language, so the block needs no header; what is bound into it is fields, read as the block is.
        var source = "0-7: \"A\"\n{{Fields}}\n";
        Func<string?, ContentNode> reads = PacketParser.Parse;
        var bound = MermaidParser.Bind(reads(source), _ => "8-15: \"B\"\n16-31: \"C\"\n", reads);

        Assert.AreEqual(source, bound.Print());
        Assert.AreEqual(2, Supplied(bound).Count(line => line.SelfAndDescendants().Any(node => node.Kind == PacketKinds.Field)),
                        "a field of the packet's for each line supplied");
        Assert.AreEqual(0, Supplied(bound).SelectMany(line => line.SelfAndDescendants()).Count(node => node.Trouble is not null));
    }
}
