using Nexaflow.Markdown.Ast;
using Nexaflow.Markdown.Mermaid;
using Nexaflow.Markdown.Mermaid.Sequence;
using Nexaflow.Markdown.Pipeline;
using Nexaflow.Tests.Fixtures;
using Nexaflow.Tests.Markdown.Mermaid;

namespace Nexaflow.Tests.Markdown.Mermaid.Sequence;

/// <summary>
/// What a <c>sequenceDiagram</c> block's stages say its lines mean where no one line says it: which box or frame each line is
/// written in, what number each message takes, and what the front matter asks for.
/// </summary>
[TestClass]
[CoversNode("sequence-diagram")]
public class SequenceStagesTests
{
    /// <summary>The number each message was given, in the order they are written.</summary>
    private static string?[] Numbers(string source) =>
        [.. MermaidStaged.Read(source).SelfAndDescendants().Where(node => node.Kind == SequenceKinds.Message).Select(message => message.Said(SequenceRoles.Number))];

    [TestMethod]
    public void EachBoxAndFrameHoldsTheLinesWrittenInIt_AndStillPrintsAsWritten()
    {
        const string source = "sequenceDiagram\n  box Purple Team\n  participant A\n  end\n  loop a\n    alt b\n      A->>B: x\n    else c\n      A->>B: y\n    end\n  end\n  A->>B: z";
        var tree = MermaidStaged.Read(source);
        var groups = tree.SelfAndDescendants().Where(node => node.Kind == MermaidKinds.Group).ToList();

        Assert.AreEqual(3, groups.Count);
        Assert.IsTrue(groups[1].SelfAndDescendants().Contains(groups[2]), "a frame nests in the one it is written in");
        Assert.AreEqual(2, groups[2].SelfAndDescendants().Count(node => node.Kind == SequenceKinds.Message), "and holds what is written in it");
        Assert.AreEqual(source, tree.Print());
    }

    [TestMethod]
    public void AnEndClosesWhicheverWasOpenedLast_AndOneNothingOpensIsSaidToBe()
    {
        var stray = MermaidStaged.Read("sequenceDiagram\n  A->>B: x\n  end");
        Assert.IsTrue(stray.SelfAndDescendants().Any(node => node.Kind == SequenceKinds.Ends && node.Trouble is not null));

        var unclosed = MermaidStaged.Read("sequenceDiagram\n  loop a\n    A->>B: x");
        Assert.IsTrue(unclosed.SelfAndDescendants().Any(node => node.Kind == SequenceKinds.Frame && node.Trouble is not null));
    }

    [TestMethod]
    public void AutonumberNumbersTheMessagesUnderIt()
    {
        CollectionAssert.AreEqual(new[] { null, "1", "2" }, Numbers("sequenceDiagram\n  A->>B: x\n  autonumber\n  A->>B: y\n  A->>B: z"));
    }

    [TestMethod]
    public void ItStartsAndStepsWhereItSays()
    {
        CollectionAssert.AreEqual(new[] { "10", "15" }, Numbers("sequenceDiagram\n  autonumber 10 5\n  A->>B: x\n  A->>B: y"));
    }

    [TestMethod]
    public void ItStopsWhereItSaysOff()
    {
        CollectionAssert.AreEqual(new[] { "1", null }, Numbers("sequenceDiagram\n  autonumber\n  A->>B: x\n  autonumber off\n  A->>B: y"));
    }

    [TestMethod]
    public void AMessageInsideAFrameIsNumberedAlongWithTheRest()
    {
        CollectionAssert.AreEqual(new[] { "1", "2", "3" }, Numbers("sequenceDiagram\n  autonumber\n  A->>B: x\n  loop a\n    A->>B: y\n  end\n  A->>B: z"));
    }

    [TestMethod]
    public void TheFrontMatterNumbersThemWithoutALineSayingSo()
    {
        CollectionAssert.AreEqual(new[] { "1" }, Numbers("---\nconfig:\n  sequence:\n    showSequenceNumbers: true\n---\nsequenceDiagram\n  A->>B: x"));
    }

    [TestMethod]
    public void TheFrontMatterIsHungOnTheBlock()
    {
        var config = (MermaidStaged.Read("---\nconfig:\n  sequence:\n    actorMargin: 80\n    hideUnusedParticipants: true\n---\nsequenceDiagram\n  A->>B: x")
                      as ConfiguredNode<SequenceConfig>)!.Config;

        Assert.AreEqual(80, config.Between);
        Assert.IsTrue(config.HideUnused);
    }

    [TestMethod]
    public void TheFrontMatterSetsEverySizeMermaidNames()
    {
        var config = SequenceConfig.Read(
            "config:\n  sequence:\n    actorMargin: 80\n    messageMargin: 20\n    activationWidth: 14\n"
            + "    mirrorActors: false\n    rightAngles: true\n    noteAlign: left\n    actorFontSize: 18");

        Assert.AreEqual(80, config.Between);
        Assert.AreEqual(20, config.Apartness);
        Assert.AreEqual(14, config.Bar);
        Assert.IsFalse(config.Mirrored);
        Assert.IsTrue(config.Square);
        Assert.AreEqual("left", config.NoteAligned);
        Assert.AreEqual(18, config.NameText);
    }

    [TestMethod]
    public void WithNothingSetItIsMermaidsOwnDefaults()
    {
        var config = SequenceConfig.Default;

        Assert.AreEqual(50, config.Between);
        Assert.AreEqual(35, config.Apartness);
        Assert.AreEqual(10, config.Bar);
        Assert.IsTrue(config.Mirrored);
        Assert.AreEqual(150, config.Widest);
        Assert.AreEqual(14, config.NameText);
        Assert.AreEqual(16, config.SaidText);
    }
}
