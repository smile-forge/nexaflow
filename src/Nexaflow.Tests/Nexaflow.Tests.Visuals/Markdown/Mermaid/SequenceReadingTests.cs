using System.Linq;
using Nexaflow.Markdown.Mermaid.Sequence;
using Nexaflow.Tests.Fixtures;
using Nexaflow.Visuals.Text.Editing;
using Nexaflow.Visuals.Text.Markdown;
using Nexaflow.Visuals.Text.Markdown.Mermaid.Sequence;

namespace Nexaflow.Tests.Visuals.Markdown.Mermaid;

/// <summary>
/// What a sequence diagram's builder reads off its tree: the participants in the order they stand, everything on the timeline
/// in the order written, and the boxes grouping them — each asked of the builder's own reader.
/// </summary>
[TestClass]
[CoversNode("sequence-diagram")]
public class SequenceReadingTests
{
    /// <summary>A sequence diagram's own builder, asked what its reader makes of a block.</summary>
    private sealed class Reads(string source, bool writing = false)
        : SequenceBuilder(Laying.Read("mermaid", source, writing), EditState.For(source), StyleFormat.Dark, isReadOnly: true, Laying.NestingNothing)
    {
        public Diagram Diagram => Read(Reading.Root, Configured(SequenceConfig.Default));
    }

    private static SequenceBuilder.Diagram Read(string source, bool writing = false) => new Reads(source, writing).Diagram;

    private static string Said(string source, Nexaflow.Markdown.Ast.ISourcePart part) => source.Substring(part.Start, part.Length);

    [TestMethod]
    public void AParticipantStandsWhereItIsFirstWritten()
    {
        CollectionAssert.AreEqual(new[] { "Bob", "Alice", "Carol" },
                                  Read("sequenceDiagram\n  Bob->>Alice: hi\n  Alice->>Carol: hi").Participants.Select(one => one.Id).ToList());
    }

    [TestMethod]
    public void DeclaringOneFirstIsHowTheOrderIsChosen()
    {
        CollectionAssert.AreEqual(new[] { "Alice", "Bob" },
                                  Read("sequenceDiagram\n  participant Alice\n  participant Bob\n  Bob->>Alice: hi").Participants.Select(one => one.Id).ToList());
    }

    [TestMethod]
    public void OneWrittenAgainIsTheSameParticipant()
    {
        var diagram = Read("sequenceDiagram\n  participant A as Alice\n  A->>B: hi\n  B->>A: hi");

        Assert.AreEqual(2, diagram.Participants.Count);
        Assert.AreEqual("Alice", diagram.Find("A")?.Said?.Text);
    }

    [TestMethod]
    public void AnActorIsDrawnAsAFigure()
    {
        var diagram = Read("sequenceDiagram\n  actor Alice\n  participant Bob");

        Assert.AreEqual(SequenceBuilder.Kind.Actor, diagram.Find("Alice")?.Kind);
        Assert.AreEqual(SequenceBuilder.Kind.Participant, diagram.Find("Bob")?.Kind);
    }

    [TestMethod]
    public void AParticipantWrittenAsItselfIsANameInABoxAndNothingMore()
    {
        var diagram = Read("sequenceDiagram\n  participant Alice\n  Alice->>Bob: hi");

        Assert.IsTrue(diagram.Participants.All(one => one.Card is null), "only a diagram that writes a card has one");
        Assert.IsTrue(diagram.Messages.All(message => message.Under.Count == 0 && message.Ink is null));
        Assert.AreEqual(0, diagram.Legend.Count);
    }

    [TestMethod]
    public void MetadataSaysWhatOneIsDrawnAs()
    {
        Assert.AreEqual(SequenceBuilder.Kind.Database, Read("sequenceDiagram\n  participant DB@{ \"type\": \"database\" }\n  DB->>DB: x").Find("DB")?.Kind);
    }

    [TestMethod]
    public void AnAliasInTheMetadataIsDrawnInsteadOfTheName()
    {
        Assert.AreEqual("Public API", Read("sequenceDiagram\n  participant API@{ \"alias\": \"Public API\" }\n  API->>API: x").Find("API")?.Said?.Text);
    }

    [TestMethod]
    public void WhatIsWrittenAfterAsWinsOverTheAlias()
    {
        Assert.AreEqual("External", Read("sequenceDiagram\n  participant API@{ \"alias\": \"Internal\" } as External\n  API->>API: x").Find("API")?.Said?.Text);
    }

    [TestMethod]
    public void AMessageSaysWhatItJoinsAndWhatItIsDrawnWith()
    {
        var message = Read("sequenceDiagram\n  Alice-->>John: Great!").Messages.Single();

        Assert.AreEqual("Alice", message.From);
        Assert.AreEqual("John", message.To);
        Assert.AreEqual("Great!", message.Said?.Text);
        Assert.IsTrue(message.Dotted);
        Assert.AreEqual(SequenceBuilder.Tip.Arrow, message.Far);
        Assert.AreEqual(SequenceBuilder.Tip.None, message.Near);
    }

    [TestMethod]
    public void EveryArrowSaysItsOwnHeads()
    {
        foreach (var (arrow, dotted, near, far) in new (string, bool, SequenceBuilder.Tip, SequenceBuilder.Tip)[]
                 {
                     ("->", false, SequenceBuilder.Tip.None, SequenceBuilder.Tip.None),
                     ("-->>", true, SequenceBuilder.Tip.None, SequenceBuilder.Tip.Arrow),
                     ("-x", false, SequenceBuilder.Tip.None, SequenceBuilder.Tip.Cross),
                     ("--)", true, SequenceBuilder.Tip.None, SequenceBuilder.Tip.Open),
                     ("<<->>", false, SequenceBuilder.Tip.Arrow, SequenceBuilder.Tip.Arrow),
                     ("-|\\", false, SequenceBuilder.Tip.None, SequenceBuilder.Tip.HalfTop),
                     ("--|/", true, SequenceBuilder.Tip.None, SequenceBuilder.Tip.HalfBottom),
                     ("/|-", false, SequenceBuilder.Tip.HalfTop, SequenceBuilder.Tip.None),
                     ("-\\\\", false, SequenceBuilder.Tip.None, SequenceBuilder.Tip.StickTop),
                     ("\\\\-", false, SequenceBuilder.Tip.StickBottom, SequenceBuilder.Tip.None),
                 })
        {
            var message = Read($"sequenceDiagram\n  A{arrow}B: x").Messages.Single();
            Assert.AreEqual((dotted, near, far), (message.Dotted, message.Near, message.Far), arrow);
        }
    }

    [TestMethod]
    public void AMessageToItselfSaysSo()
    {
        Assert.IsTrue(Read("sequenceDiagram\n  A->>A: thinking").Messages.Single().Self);
        Assert.IsFalse(Read("sequenceDiagram\n  A->>B: asking").Messages.Single().Self);
    }

    [TestMethod]
    public void ThePlusAndMinusAgainstAMessageTurnABarOnAndOff()
    {
        var messages = Read("sequenceDiagram\n  A->>+B: x\n  B-->>-A: y").Messages.ToList();

        Assert.IsTrue(messages[0].Starts && !messages[0].Stops);
        Assert.IsTrue(messages[1].Stops && !messages[1].Starts);
    }

    [TestMethod]
    public void ThePairOfBracketsRunsAnEndToTheMiddleOfItsLifeline()
    {
        var messages = Read("sequenceDiagram\n  A->>()B: x\n  A()->>B: y").Messages.ToList();

        Assert.IsTrue(messages[0].ToCentre && !messages[0].FromCentre);
        Assert.IsTrue(messages[1].FromCentre && !messages[1].ToCentre);
    }

    [TestMethod]
    public void ANoteSaysWhereItSitsAndWhatItIsOver()
    {
        var note = Read("sequenceDiagram\n  Note over Alice,John: A typical interaction").Items.OfType<SequenceBuilder.Note>().Single();

        Assert.AreEqual(SequenceBuilder.Place.Over, note.Place);
        CollectionAssert.AreEqual(new[] { "Alice", "John" }, note.Over.ToList());
        Assert.AreEqual("A typical interaction", note.Said?.Text);

        Assert.AreEqual(SequenceBuilder.Place.LeftOf, Read("sequenceDiagram\n  Note left of A: x").Items.OfType<SequenceBuilder.Note>().Single().Place);
        Assert.AreEqual(SequenceBuilder.Place.RightOf, Read("sequenceDiagram\n  Note right of A: x").Items.OfType<SequenceBuilder.Note>().Single().Place);
    }

    [TestMethod]
    public void ABarIsStartedAndEndedInOrder()
    {
        var turns = Read("sequenceDiagram\n  activate A\n  A->>B: x\n  deactivate A").Items.OfType<SequenceBuilder.Turn>().ToList();

        Assert.AreEqual(2, turns.Count);
        Assert.IsTrue(turns[0].On);
        Assert.IsFalse(turns[1].On);
    }

    [TestMethod]
    public void AParticipantIsMadeAndEndedPartwayDown()
    {
        var diagram = Read("sequenceDiagram\n  A->>B: x\n  create participant C\n  A->>C: hi\n  destroy C\n  A-xC: bye");

        Assert.IsTrue(diagram.Find("C")?.Created);
        Assert.IsTrue(diagram.Find("C")?.Destroyed);
        Assert.AreEqual("C", diagram.Items.OfType<SequenceBuilder.Gone>().Single().Id);
    }

    [TestMethod]
    public void AFrameOpensDividesAndCloses()
    {
        var diagram = Read("sequenceDiagram\n  alt is sick\n    A->>B: x\n  else is well\n    A->>B: y\n  end");

        var opening = diagram.Items.OfType<SequenceBuilder.Opening>().Single();
        var divider = diagram.Items.OfType<SequenceBuilder.Divider>().Single();
        var closing = diagram.Items.OfType<SequenceBuilder.Closing>().Single();

        Assert.AreEqual(SequenceBuilder.Frame.Alt, opening.Kind);
        Assert.AreEqual("is sick", opening.Said?.Text);
        Assert.AreEqual("is well", divider.Said?.Text);
        Assert.AreEqual(opening.Key, divider.Key);
        Assert.AreEqual(opening.Key, closing.Key);
        Assert.IsTrue(opening.Order < closing.Order, "and everything in it comes between");
    }

    [TestMethod]
    public void AFrameStandsForEverythingWrittenInIt()
    {
        const string source = "sequenceDiagram\n  loop every day\n    A->>B: x\n  end";

        Assert.AreEqual("loop every day\n    A->>B: x\n  end", Said(source, Read(source).Items.OfType<SequenceBuilder.Opening>().Single().Whole));
    }

    [TestMethod]
    public void AFrameNothingClosesStandsForItsOwnLine()
    {
        const string source = "sequenceDiagram\n  loop every day\n    A->>B: x";
        var diagram = Read(source);

        Assert.AreEqual("loop every day", Said(source, diagram.Items.OfType<SequenceBuilder.Opening>().Single().Whole));
        Assert.AreEqual(0, diagram.Items.OfType<SequenceBuilder.Closing>().Count());
    }

    [TestMethod]
    public void EveryWordOpensItsOwnKindOfFrame()
    {
        var diagram = Read("sequenceDiagram\n  alt a\n  end\n  opt b\n  end\n  loop c\n  end\n  par d\n  end\n  critical e\n  end\n"
                           + "  break f\n  end\n  rect red\n  end");

        CollectionAssert.AreEqual(
            new[]
            {
                SequenceBuilder.Frame.Alt, SequenceBuilder.Frame.Opt, SequenceBuilder.Frame.Loop, SequenceBuilder.Frame.Par,
                SequenceBuilder.Frame.Critical, SequenceBuilder.Frame.Break, SequenceBuilder.Frame.Rect,
            },
            diagram.Items.OfType<SequenceBuilder.Opening>().Select(frame => frame.Kind).ToList());
    }

    [TestMethod]
    public void AFrameInsideOneSaysWhichItIsIn()
    {
        var frames = Read("sequenceDiagram\n  loop a\n    alt b\n      A->>B: x\n    end\n  end").Items.OfType<SequenceBuilder.Opening>().ToList();

        Assert.IsNull(frames[0].Parent);
        Assert.AreEqual(frames[0].Key, frames[1].Parent);
    }

    [TestMethod]
    public void ABoxHoldsTheParticipantsWrittenInIt()
    {
        var diagram = Read("sequenceDiagram\n  box Purple Alice & John\n  participant A\n  participant J\n  end\n  participant B\n  A->>J: x");
        var box = diagram.Boxes.Single();

        Assert.AreEqual("Alice & John", box.Said?.Text);
        Assert.AreEqual("Purple", box.Colour);
        Assert.AreEqual(box.Key, diagram.Find("A")?.Box);
        Assert.AreEqual(box.Key, diagram.Find("J")?.Box);
        Assert.IsNull(diagram.Find("B")?.Box);
        Assert.AreEqual(0, diagram.Items.OfType<SequenceBuilder.Closing>().Count(), "a box closes nothing on the timeline");
    }

    [TestMethod]
    public void ABoxWithNoColourIsAllName()
    {
        var box = Read("sequenceDiagram\n  box Another Group\n  participant A\n  end").Boxes.Single();

        Assert.IsNull(box.Colour);
        Assert.AreEqual("Another Group", box.Said?.Text);
    }

    [TestMethod]
    public void AWashIsTheColourWrittenAfterRect()
    {
        var opening = Read("sequenceDiagram\n  rect rgb(191, 223, 255)\n  A->>B: x\n  end").Items.OfType<SequenceBuilder.Opening>().Single();

        Assert.AreEqual(SequenceBuilder.Frame.Rect, opening.Kind);
        Assert.AreEqual("rgb(191, 223, 255)", opening.Colour);
    }

    [TestMethod]
    public void AMessageCarriesTheNumberItsStagesGaveIt()
    {
        var messages = Read("sequenceDiagram\n  A->>B: x\n  autonumber 10 5\n  A->>B: y\n  A->>B: z\n  autonumber off\n  A->>B: w").Messages.ToList();

        CollectionAssert.AreEqual(new[] { null, "10", "15", null }, messages.Select(message => message.Number).ToArray());
    }

    [TestMethod]
    public void ALinkIsSomewhereAParticipantLeads()
    {
        var links = Read("sequenceDiagram\n  participant A\n  link A: Dashboard @ https://example.com/a\n  link A: Wiki @ https://example.com/w").Find("A")!.Links;

        Assert.AreEqual(2, links.Count);
        Assert.AreEqual("Dashboard", links[0].Said?.Text);
        Assert.AreEqual("https://example.com/a", links[0].Url);
        Assert.AreEqual("Wiki", links[1].Said?.Text);
    }

    [TestMethod]
    public void SeveralAtOnceAreReadFromTheBracesToo()
    {
        var links = Read("sequenceDiagram\n  participant A\n  links A: {\"One\": \"https://one\", \"Two\": \"https://two\"}").Find("A")!.Links;

        Assert.AreEqual(2, links.Count);
        Assert.AreEqual("One", links[0].Said?.Text);
        Assert.AreEqual("https://two", links[1].Url);
    }

    [TestMethod]
    public void WhatAParticipantIsSaidToBeIsNotSomewhereItLeads()
    {
        Assert.AreEqual(0, Read("sequenceDiagram\n  participant A\n  properties A: {\"class\": \"internal\"}\n  details A: {\"Comment\": \"x\"}").Find("A")!.Links.Count);
    }

    [TestMethod]
    public void AParticipantNothingReachesIsLeftOutWhereTheFrontMatterAsks()
    {
        const string source = "sequenceDiagram\n  participant A\n  participant B\n  participant C\n  A->>B: x";

        CollectionAssert.AreEqual(new[] { "A", "B", "C" }, Read(source).Drawn.Select(one => one.Id).ToList());
        CollectionAssert.AreEqual(new[] { "A", "B" },
                                  Read("---\nconfig:\n  sequence:\n    hideUnusedParticipants: true\n---\n" + source).Drawn.Select(one => one.Id).ToList());
    }

    [TestMethod]
    public void ANameWithNothingInItYetIsAParticipantOfItsOwn()
    {
        var diagram = Read("sequenceDiagram\n  ->>: ", writing: true);

        Assert.AreEqual(2, diagram.Participants.Count);
        Assert.IsTrue(diagram.Participants.All(one => one.SaidHole is not null));
    }
}
