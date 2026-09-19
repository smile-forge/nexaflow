using Nexaflow.Markdown.Mermaid.Sequence;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.Markdown.Mermaid.Sequence;

/// <summary>
/// What a <c>sequenceDiagram</c> block comes to: the participants in the order they stand, everything on the timeline in the
/// order written, and the boxes grouping them.
/// </summary>
[TestClass]
[CoversNode("sequence-diagram")]
public class SequenceDiagramTests
{
    [TestMethod]
    public void AParticipantStandsWhereItIsFirstWritten()
    {
        var diagram = SequenceDiagram.Read("sequenceDiagram\n  Bob->>Alice: hi\n  Alice->>Carol: hi");

        CollectionAssert.AreEqual(new[] { "Bob", "Alice", "Carol" }, diagram.Participants.Select(one => one.Id).ToList());
    }

    [TestMethod]
    public void DeclaringOneFirstIsHowTheOrderIsChosen()
    {
        var diagram = SequenceDiagram.Read("sequenceDiagram\n  participant Alice\n  participant Bob\n  Bob->>Alice: hi");

        CollectionAssert.AreEqual(new[] { "Alice", "Bob" }, diagram.Participants.Select(one => one.Id).ToList());
    }

    [TestMethod]
    public void OneWrittenAgainIsTheSameParticipant()
    {
        var diagram = SequenceDiagram.Read("sequenceDiagram\n  participant A as Alice\n  A->>B: hi\n  B->>A: hi");

        Assert.AreEqual(2, diagram.Participants.Count);
        Assert.AreEqual("Alice", diagram.Find("A")?.Said?.Text);
    }

    [TestMethod]
    public void AnActorIsDrawnAsAFigure()
    {
        var diagram = SequenceDiagram.Read("sequenceDiagram\n  actor Alice\n  participant Bob");

        Assert.AreEqual(SequenceKind.Actor, diagram.Find("Alice")?.Kind);
        Assert.AreEqual(SequenceKind.Participant, diagram.Find("Bob")?.Kind);
    }

    [TestMethod]
    public void AParticipantWrittenAsItselfIsANameInABoxAndNothingMore()
    {
        var diagram = SequenceDiagram.Read("sequenceDiagram\n  participant Alice\n  Alice->>Bob: hi");

        Assert.IsTrue(diagram.Participants.All(one => one.Card is null), "only a diagram that writes a card has one");
        Assert.IsTrue(diagram.Messages.All(message => message.Under.Count == 0 && message.Ink is null));
        Assert.AreEqual(0, diagram.Legend.Count);
    }

    [TestMethod]
    public void MetadataSaysWhatOneIsDrawnAs()
    {
        var diagram = SequenceDiagram.Read("sequenceDiagram\n  participant DB@{ \"type\": \"database\" }\n  DB->>DB: x");

        Assert.AreEqual(SequenceKind.Database, diagram.Find("DB")?.Kind);
    }

    [TestMethod]
    public void AnAliasInTheMetadataIsDrawnInsteadOfTheName()
    {
        var diagram = SequenceDiagram.Read("sequenceDiagram\n  participant API@{ \"alias\": \"Public API\" }\n  API->>API: x");

        Assert.AreEqual("Public API", diagram.Find("API")?.Said?.Text);
    }

    [TestMethod]
    public void WhatIsWrittenAfterAsWinsOverTheAlias()
    {
        var diagram = SequenceDiagram.Read(
            "sequenceDiagram\n  participant API@{ \"alias\": \"Internal\" } as External\n  API->>API: x");

        Assert.AreEqual("External", diagram.Find("API")?.Said?.Text);
    }

    [TestMethod]
    public void AMessageSaysWhatItJoinsAndWhatItIsDrawnWith()
    {
        var message = SequenceDiagram.Read("sequenceDiagram\n  Alice-->>John: Great!").Messages.Single();

        Assert.AreEqual("Alice", message.From);
        Assert.AreEqual("John", message.To);
        Assert.AreEqual("Great!", message.Said?.Text);
        Assert.IsTrue(message.Dotted);
        Assert.AreEqual(SequenceHead.Arrow, message.Far);
        Assert.AreEqual(SequenceHead.None, message.Near);
    }

    [TestMethod]
    public void EveryArrowSaysItsOwnHeads()
    {
        Assert.AreEqual((false, SequenceHead.None, SequenceHead.None), SequenceDiagram.Ended("->"));
        Assert.AreEqual((true, SequenceHead.None, SequenceHead.Arrow), SequenceDiagram.Ended("-->>"));
        Assert.AreEqual((false, SequenceHead.None, SequenceHead.Cross), SequenceDiagram.Ended("-x"));
        Assert.AreEqual((true, SequenceHead.None, SequenceHead.Open), SequenceDiagram.Ended("--)"));
        Assert.AreEqual((false, SequenceHead.Arrow, SequenceHead.Arrow), SequenceDiagram.Ended("<<->>"));
        Assert.AreEqual((false, SequenceHead.None, SequenceHead.HalfTop), SequenceDiagram.Ended("-|\\"));
        Assert.AreEqual((true, SequenceHead.None, SequenceHead.HalfBottom), SequenceDiagram.Ended("--|/"));
        Assert.AreEqual((false, SequenceHead.HalfTop, SequenceHead.None), SequenceDiagram.Ended("/|-"));
        Assert.AreEqual((false, SequenceHead.None, SequenceHead.StickTop), SequenceDiagram.Ended("-\\\\"));
        Assert.AreEqual((false, SequenceHead.StickBottom, SequenceHead.None), SequenceDiagram.Ended("\\\\-"));
    }

    [TestMethod]
    public void AMessageToItselfSaysSo()
    {
        Assert.IsTrue(SequenceDiagram.Read("sequenceDiagram\n  A->>A: thinking").Messages.Single().Self);
        Assert.IsFalse(SequenceDiagram.Read("sequenceDiagram\n  A->>B: asking").Messages.Single().Self);
    }

    [TestMethod]
    public void ThePlusAndMinusAgainstAMessageTurnABarOnAndOff()
    {
        var messages = SequenceDiagram.Read("sequenceDiagram\n  A->>+B: x\n  B-->>-A: y").Messages.ToList();

        Assert.IsTrue(messages[0].Starts);
        Assert.IsFalse(messages[0].Stops);
        Assert.IsTrue(messages[1].Stops);
        Assert.IsFalse(messages[1].Starts);
    }

    [TestMethod]
    public void ThePairOfBracketsRunsAnEndToTheMiddleOfItsLifeline()
    {
        var messages = SequenceDiagram.Read("sequenceDiagram\n  A->>()B: x\n  A()->>B: y").Messages.ToList();

        Assert.IsTrue(messages[0].ToCentre);
        Assert.IsFalse(messages[0].FromCentre);
        Assert.IsTrue(messages[1].FromCentre);
        Assert.IsFalse(messages[1].ToCentre);
    }

    [TestMethod]
    public void ANoteSaysWhereItSitsAndWhatItIsOver()
    {
        var note = SequenceDiagram.Read("sequenceDiagram\n  Note over Alice,John: A typical interaction")
                                  .Items.OfType<SequenceNote>().Single();

        Assert.AreEqual(SequencePlace.Over, note.Place);
        CollectionAssert.AreEqual(new[] { "Alice", "John" }, note.Over.ToList());
        Assert.AreEqual("A typical interaction", note.Said?.Text);
    }

    [TestMethod]
    public void ANoteSitsToEitherSideToo()
    {
        Assert.AreEqual(SequencePlace.LeftOf,
                        SequenceDiagram.Read("sequenceDiagram\n  Note left of A: x").Items.OfType<SequenceNote>().Single().Place);
        Assert.AreEqual(SequencePlace.RightOf,
                        SequenceDiagram.Read("sequenceDiagram\n  Note right of A: x").Items.OfType<SequenceNote>().Single().Place);
    }

    [TestMethod]
    public void ABarIsStartedAndEndedInOrder()
    {
        var turns = SequenceDiagram.Read("sequenceDiagram\n  activate A\n  A->>B: x\n  deactivate A")
                                   .Items.OfType<SequenceTurn>().ToList();

        Assert.AreEqual(2, turns.Count);
        Assert.IsTrue(turns[0].On);
        Assert.IsFalse(turns[1].On);
    }

    [TestMethod]
    public void AParticipantIsMadeAndEndedPartwayDown()
    {
        var diagram = SequenceDiagram.Read(
            "sequenceDiagram\n  A->>B: x\n  create participant C\n  A->>C: hi\n  destroy C\n  A-xC: bye");

        Assert.IsTrue(diagram.Find("C")?.Created);
        Assert.IsTrue(diagram.Find("C")?.Destroyed);
        Assert.AreEqual("C", diagram.Items.OfType<SequenceGone>().Single().Id);
    }

    [TestMethod]
    public void AFrameOpensDividesAndCloses()
    {
        var diagram = SequenceDiagram.Read(
            "sequenceDiagram\n  alt is sick\n    A->>B: x\n  else is well\n    A->>B: y\n  end");

        var opening = diagram.Items.OfType<SequenceOpening>().Single();
        var divider = diagram.Items.OfType<SequenceDivider>().Single();
        var closing = diagram.Items.OfType<SequenceClosing>().Single();

        Assert.AreEqual(SequenceFrame.Alt, opening.Kind);
        Assert.AreEqual("is sick", opening.Said?.Text);
        Assert.AreEqual("is well", divider.Said?.Text);
        Assert.AreEqual(opening.Key, divider.Key);
        Assert.AreEqual(opening.Key, closing.Key);
    }

    [TestMethod]
    public void AFrameStandsForEverythingWrittenInIt()
    {
        var source = "sequenceDiagram\n  loop every day\n    A->>B: x\n  end";
        var opening = SequenceDiagram.Read(source).Items.OfType<SequenceOpening>().Single();

        Assert.AreEqual("loop every day\n    A->>B: x\n  end", source.Substring(opening.Whole.Start, opening.Whole.Length));
    }

    [TestMethod]
    public void EveryWordOpensItsOwnKindOfFrame()
    {
        var diagram = SequenceDiagram.Read(
            "sequenceDiagram\n  alt a\n  end\n  opt b\n  end\n  loop c\n  end\n  par d\n  end\n  critical e\n  end\n"
            + "  break f\n  end\n  rect red\n  end");

        CollectionAssert.AreEqual(
            new[]
            {
                SequenceFrame.Alt, SequenceFrame.Opt, SequenceFrame.Loop, SequenceFrame.Par, SequenceFrame.Critical,
                SequenceFrame.Break, SequenceFrame.Rect,
            },
            diagram.Items.OfType<SequenceOpening>().Select(frame => frame.Kind).ToList());
    }

    [TestMethod]
    public void AFrameInsideOneSaysWhichItIsIn()
    {
        var frames = SequenceDiagram.Read("sequenceDiagram\n  loop a\n    alt b\n      A->>B: x\n    end\n  end")
                                    .Items.OfType<SequenceOpening>().ToList();

        Assert.IsNull(frames[0].Parent);
        Assert.AreEqual(frames[0].Key, frames[1].Parent);
    }

    [TestMethod]
    public void ABoxHoldsTheParticipantsWrittenInIt()
    {
        var diagram = SequenceDiagram.Read(
            "sequenceDiagram\n  box Purple Alice & John\n  participant A\n  participant J\n  end\n  participant B\n  A->>J: x");

        var box = diagram.Boxes.Single();

        Assert.AreEqual("Alice & John", box.Said?.Text);
        Assert.AreEqual("Purple", box.Colour);
        Assert.AreEqual(box.Key, diagram.Find("A")?.Box);
        Assert.AreEqual(box.Key, diagram.Find("J")?.Box);
        Assert.IsNull(diagram.Find("B")?.Box);
    }

    [TestMethod]
    public void ABoxWithNoColourIsAllName()
    {
        var box = SequenceDiagram.Read("sequenceDiagram\n  box Another Group\n  participant A\n  end").Boxes.Single();

        Assert.IsNull(box.Colour);
        Assert.AreEqual("Another Group", box.Said?.Text);
    }

    [TestMethod]
    public void AWashIsTheColourWrittenAfterRect()
    {
        var opening = SequenceDiagram.Read("sequenceDiagram\n  rect rgb(191, 223, 255)\n  A->>B: x\n  end")
                                     .Items.OfType<SequenceOpening>().Single();

        Assert.AreEqual(SequenceFrame.Rect, opening.Kind);
        Assert.AreEqual("rgb(191, 223, 255)", opening.Colour);
    }

    [TestMethod]
    public void AutonumberNumbersTheMessagesUnderIt()
    {
        var messages = SequenceDiagram.Read("sequenceDiagram\n  A->>B: x\n  autonumber\n  A->>B: y\n  A->>B: z").Messages.ToList();

        Assert.IsNull(messages[0].Number);
        Assert.AreEqual("1", messages[1].Number);
        Assert.AreEqual("2", messages[2].Number);
    }

    [TestMethod]
    public void ItStartsAndStepsWhereItSays()
    {
        var messages = SequenceDiagram.Read("sequenceDiagram\n  autonumber 10 5\n  A->>B: x\n  A->>B: y").Messages.ToList();

        Assert.AreEqual("10", messages[0].Number);
        Assert.AreEqual("15", messages[1].Number);
    }

    [TestMethod]
    public void ItStopsWhereItSaysOff()
    {
        var messages = SequenceDiagram.Read("sequenceDiagram\n  autonumber\n  A->>B: x\n  autonumber off\n  A->>B: y")
                                      .Messages.ToList();

        Assert.AreEqual("1", messages[0].Number);
        Assert.IsNull(messages[1].Number);
    }

    [TestMethod]
    public void TheFrontMatterNumbersThemWithoutALineSayingSo()
    {
        var source = "---\nconfig:\n  sequence:\n    showSequenceNumbers: true\n---\nsequenceDiagram\n  A->>B: x";

        Assert.AreEqual("1", SequenceDiagram.Read(source).Messages.Single().Number);
    }

    [TestMethod]
    public void ALinkIsSomewhereAParticipantLeads()
    {
        var diagram = SequenceDiagram.Read(
            "sequenceDiagram\n  participant A\n  link A: Dashboard @ https://example.com/a\n  link A: Wiki @ https://example.com/w");

        var links = diagram.Find("A")!.Links;

        Assert.AreEqual(2, links.Count);
        Assert.AreEqual("Dashboard", links[0].Said?.Text);
        Assert.AreEqual("https://example.com/a", links[0].Url);
        Assert.AreEqual("Wiki", links[1].Said?.Text);
    }

    [TestMethod]
    public void SeveralAtOnceAreReadFromTheBracesToo()
    {
        var diagram = SequenceDiagram.Read(
            "sequenceDiagram\n  participant A\n  links A: {\"One\": \"https://one\", \"Two\": \"https://two\"}");

        var links = diagram.Find("A")!.Links;

        Assert.AreEqual(2, links.Count);
        Assert.AreEqual("One", links[0].Said?.Text);
        Assert.AreEqual("https://two", links[1].Url);
    }

    [TestMethod]
    public void WhatAParticipantIsSaidToBeIsNotSomewhereItLeads()
    {
        var diagram = SequenceDiagram.Read(
            "sequenceDiagram\n  participant A\n  properties A: {\"class\": \"internal\"}\n  details A: {\"Comment\": \"x\"}");

        Assert.AreEqual(0, diagram.Find("A")!.Links.Count);
    }

    [TestMethod]
    public void AParticipantNothingReachesIsLeftOutWhereTheFrontMatterAsks()
    {
        var source = "sequenceDiagram\n  participant A\n  participant B\n  participant C\n  A->>B: x";

        CollectionAssert.AreEqual(new[] { "A", "B", "C" }, SequenceDiagram.Read(source).Drawn.Select(one => one.Id).ToList());

        var hidden = "---\nconfig:\n  sequence:\n    hideUnusedParticipants: true\n---\n" + source;

        CollectionAssert.AreEqual(new[] { "A", "B" }, SequenceDiagram.Read(hidden).Drawn.Select(one => one.Id).ToList());
    }

    [TestMethod]
    public void ANameWithNothingInItYetIsAParticipantOfItsOwn()
    {
        var diagram = SequenceDiagram.Of(Nexaflow.Markdown.Mermaid.MermaidParser.Read("sequenceDiagram\n  ->>: ", holes: true));

        Assert.AreEqual(2, diagram.Participants.Count);
        Assert.IsTrue(diagram.Participants.All(one => one.SaidHole is not null));
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
