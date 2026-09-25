using Nexaflow.Markdown.Mermaid.State;
using Nexaflow.Tests.Fixtures;
using Nexaflow.Tests.Markdown.Mermaid;

namespace Nexaflow.Tests.Markdown.Mermaid.State;

/// <summary>
/// What a <c>stateDiagram</c> block is read back into: the states, what is drawn on them and what they are drawn as, the
/// transitions between them, the composite states they are gathered into, the notes beside them, and what the front matter asks.
/// </summary>
[TestClass]
[CoversNode("state-diagram")]
public class StateDiagramTests
{
    [TestMethod]
    public void AStateIsWrittenWhereItIsFirstNamed_AndSaidAgainAfter()
    {
        var read = StateDiagram.Of(MermaidStaged.Read("stateDiagram-v2\n  one --> two\n  one : The first\n  two : The second"));

        CollectionAssert.AreEqual(new[] { "one", "two" }, read.Nodes.Select(node => node.Id).ToArray(),
                                  "a state written twice is the one state, said again");
        CollectionAssert.AreEqual(new[] { "The first", "The second" }, read.Nodes.Select(node => node.Said?.Text).ToArray());
    }

    [TestMethod]
    public void WhatIsWrittenOnAStateComesBeforeItsNameOrAfterIt()
    {
        var read = StateDiagram.Of(MermaidStaged.Read("stateDiagram-v2\n  state \"Standing still\" as one\n  two : Moving along"));

        Assert.AreEqual("Standing still", read.Find("one")?.Said?.Text);
        Assert.AreEqual("Moving along", read.Find("two")?.Said?.Text);
    }

    [TestMethod]
    public void EveryEdgeInOneScopeIsTheSameDot()
    {
        var read = StateDiagram.Of(MermaidStaged.Read("stateDiagram-v2\n  [*] --> one\n  one --> two\n  two --> [*]\n  one --> [*]"));

        Assert.AreEqual(1, read.Nodes.Count(node => node.Shape == StateShape.Start), "one dot to start at");
        Assert.AreEqual(1, read.Nodes.Count(node => node.Shape == StateShape.Stop), "and one to stop at");
        Assert.AreEqual("start", read.Nodes.Single(node => node.Shape == StateShape.Start).Id,
                        "called what a class line styles it by");
        Assert.AreEqual("end", read.Nodes.Single(node => node.Shape == StateShape.Stop).Id);
    }

    [TestMethod]
    public void AComposteStateHasDotsOfItsOwn()
    {
        var read = StateDiagram.Of(MermaidStaged.Read("stateDiagram-v2\n  [*] --> A\n  state A {\n    [*] --> one\n    one --> [*]\n  }"));

        Assert.AreEqual(2, read.Nodes.Count(node => node.Shape == StateShape.Start), "the diagram's, and the composite state's");
        Assert.AreEqual(1, read.Nodes.Count(node => node.Shape == StateShape.Stop));
        Assert.AreEqual(1, read.Groups.Count);
        Assert.AreEqual("A", read.Groups[0].Id);
    }

    [TestMethod]
    public void AStateBelongsToTheCompositeStateItIsFirstWrittenIn()
    {
        var read = StateDiagram.Of(MermaidStaged.Read("stateDiagram-v2\n  outside\n  state A {\n    inner\n    state B {\n      deeper\n    }\n  }"));

        Assert.IsNull(read.Find("outside")?.Group);
        Assert.AreEqual(read.Groups[0].Key, read.Find("inner")?.Group);
        Assert.AreEqual(read.Groups[1].Key, read.Find("deeper")?.Group);
        Assert.AreEqual(read.Groups[0].Key, read.Groups[1].Parent, "and a composite state knows the one it is inside");
    }

    [TestMethod]
    public void ACompositeStateStandsForTheWholeOfWhatItWasWrittenAs()
    {
        const string source = "stateDiagram-v2\n  state A {\n    one\n  }";
        var read = StateDiagram.Of(MermaidStaged.Read(source));

        Assert.AreEqual("state A {\n    one\n  }",
                        source.Substring(read.Groups[0].Whole.Start, read.Groups[0].Whole.Length));
    }

    [TestMethod]
    public void ACompositeStateRunsTheWayItsOwnDirectionSays()
    {
        var read = StateDiagram.Of(MermaidStaged.Read("stateDiagram-v2\n  direction LR\n  state A {\n    direction TB\n    one --> two\n  }"));

        Assert.AreEqual(StateWay.Right, read.Way);
        Assert.AreEqual(StateWay.Down, read.Groups[0].Way);
    }

    [TestMethod]
    public void AStateIsDrawnAsWhatItsAnglesSay()
    {
        var read = StateDiagram.Of(MermaidStaged.Read("stateDiagram-v2\n  state f <<fork>>\n  state j <<join>>\n  state c <<choice>>\n  state p [[fork]]"));

        Assert.AreEqual(StateShape.Fork, read.Find("f")?.Shape);
        Assert.AreEqual(StateShape.Join, read.Find("j")?.Shape);
        Assert.AreEqual(StateShape.Choice, read.Find("c")?.Shape);
        Assert.AreEqual(StateShape.Fork, read.Find("p")?.Shape, "Mermaid reads the brackets too");
    }

    [TestMethod]
    public void RegionsRunningAtTheSameTimeAreDividedByALineOfTheirOwn()
    {
        var read = StateDiagram.Of(MermaidStaged.Read("stateDiagram-v2\n  state A {\n    one\n    --\n    two\n  }"));

        var divider = read.Nodes.Single(node => node.Shape == StateShape.Divider);
        Assert.AreEqual(read.Groups[0].Key, divider.Group, "the line belongs to the composite state it divides");
    }

    [TestMethod]
    public void EachRegionHasDotsOfItsOwn_AndKnowsWhichRegionItIs()
    {
        var read = StateDiagram.Of(MermaidStaged.Read("stateDiagram-v2\n  state A {\n    [*] --> one\n    one --> [*]\n    --\n    [*] --> two\n    two --> [*]\n  }"));

        Assert.AreEqual(2, read.Nodes.Count(node => node.Shape == StateShape.Start), "each region starts at a dot of its own, as Mermaid draws it");
        Assert.AreEqual(2, read.Nodes.Count(node => node.Shape == StateShape.Stop), "and stops at one");
        Assert.AreEqual(1, read.Find("one")!.Region);
        Assert.AreEqual(2, read.Find("two")!.Region, "what is written after a -- is in the next region");
    }

    [TestMethod]
    public void ANoteIsWrittenBesideAStateOnEitherSide()
    {
        var read = StateDiagram.Of(MermaidStaged.Read("stateDiagram-v2\n  one\n  note right of one : mind this\n  note left of one : and this"));

        Assert.AreEqual(2, read.Notes.Count);
        Assert.IsFalse(read.Notes[0].Left);
        Assert.IsTrue(read.Notes[1].Left);
        Assert.AreEqual("one", read.Notes[0].Of);
        Assert.AreEqual("mind this", read.Notes[0].Said.Single().Text);
    }

    [TestMethod]
    public void ANoteWrittenAcrossSeveralLinesSaysEveryOneOfThem()
    {
        var read = StateDiagram.Of(MermaidStaged.Read("stateDiagram-v2\n  one\n  note right of one\n    first line\n    second line\n  end note"));

        CollectionAssert.AreEqual(new[] { "first line", "second line" }, read.Notes.Single().Said.Select(said => said.Text).ToArray(),
                                  "the parser hands the grammar the whole stretch, so the note is one statement");
    }

    [TestMethod]
    public void ClassesAndStylesReachTheStatesTheyName()
    {
        var read = StateDiagram.Of(MermaidStaged.Read("stateDiagram-v2\n  one:::busy --> two\n  classDef busy fill:#fee\n  class two busy\n"
                                   + "  style one stroke:#c00"));

        Assert.AreEqual("#fee", read.Find("one")?.Style.Fill);
        Assert.AreEqual("#c00", read.Find("one")?.Style.Stroke, "a style line is laid over the class");
        Assert.AreEqual("#fee", read.Find("two")?.Style.Fill);
    }

    [TestMethod]
    public void WhereAStateLeadsAndWhatItSaysArePutOnIt()
    {
        var read = StateDiagram.Of(MermaidStaged.Read("stateDiagram-v2\n  one --> two\n  click one \"https://example.com\" \"Go there\""));

        Assert.AreEqual("https://example.com", read.Find("one")?.Href);
        Assert.AreEqual("Go there", read.Find("one")?.Tip);
    }

    [TestMethod]
    public void AStateWithNothingOnItIsDrawnPlainWhereTheDiagramSaysSo()
    {
        Assert.IsTrue(StateDiagram.Of(MermaidStaged.Read("stateDiagram-v2\n  hide empty description\n  one")).Plain);
        Assert.IsFalse(StateDiagram.Of(MermaidStaged.Read("stateDiagram-v2\n  one")).Plain);
    }

    [TestMethod]
    public void TheFrontMatterIsApplied()
    {
        var read = StateDiagram.Of(MermaidStaged.Read("---\nconfig:\n  state:\n    nodeSpacing: 40\n    wrappingWidth: 90\n    forkWidth: 60\n"
                                   + "---\nstateDiagram-v2\n  one --> two"));

        Assert.AreEqual(40, read.Config.NodeSpacing, 0.01);
        Assert.AreEqual(90, read.Config.Wrapping, 0.01);
        Assert.AreEqual(60, read.Config.ForkWidth, 0.01);
        Assert.AreEqual(StateConfig.Along, read.Config.RankSpacing, 0.01, "and what it says nothing about is Mermaid's own");
    }

    [TestMethod]
    public void ABlockWithNothingInItReadsIntoNothing()
    {
        var read = StateDiagram.Of(MermaidStaged.Read("stateDiagram-v2"));

        Assert.AreEqual(0, read.Nodes.Count);
        Assert.AreEqual(0, read.Steps.Count);
        Assert.AreEqual(0, read.Groups.Count);
    }
}
