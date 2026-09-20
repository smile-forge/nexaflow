using Nexaflow.Markdown.Mermaid.C4;
using Nexaflow.Markdown.Mermaid.Sequence;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.Markdown.Mermaid.C4;

/// <summary>
/// What a <c>C4Sequence</c> block comes to: the very same timeline a <c>sequenceDiagram</c> comes to, with the participants
/// carrying cards and the relationships carrying what they are done with.
/// </summary>
[TestClass]
[CoversNode("c4-sequence")]
public class C4SequenceTests
{
    [TestMethod]
    public void AnElementIsAParticipantWhoseBoxIsACard()
    {
        var diagram = C4Sequence.Read("C4Sequence\nContainer(spa, \"Single-Page App\", \"Angular\", \"Delivers it\")\n"
                                      + "SHOW_ELEMENT_DESCRIPTIONS()\nRel(spa, spa, \"Runs\")");

        var one = diagram.Find("spa");

        Assert.AreEqual("Single-Page App", one?.Said?.Text);
        Assert.AreEqual("[Container: Angular]", one?.Card?.Stereotype);
        Assert.AreEqual("Delivers it", one?.Card?.Said?.Text);
        Assert.AreEqual(SequenceCardShape.Box, one?.Card?.Shape);
    }

    [TestMethod]
    public void EveryKindSaysWhatItIsAndHowItIsDrawn()
    {
        var diagram = C4Sequence.Read(
            "C4Sequence\nPerson(p, \"P\")\nPerson_Ext(q, \"Q\")\nSystem(s, \"S\")\nSystemDb(d, \"D\")\nSystemQueue(u, \"U\")\n"
            + "Component(c, \"C\", \"Spring\")\nRel(p, s, \"x\")");

        Assert.AreEqual("[Person]", diagram.Find("p")?.Card?.Stereotype);
        Assert.AreEqual("[Person (external)]", diagram.Find("q")?.Card?.Stereotype);
        Assert.AreEqual("[Software System]", diagram.Find("s")?.Card?.Stereotype);
        Assert.AreEqual("[Component: Spring]", diagram.Find("c")?.Card?.Stereotype);

        Assert.AreEqual(SequenceCardShape.Person, diagram.Find("p")?.Card?.Shape);
        Assert.AreEqual(SequenceCardShape.Database, diagram.Find("d")?.Card?.Shape);
        Assert.AreEqual(SequenceCardShape.Queue, diagram.Find("u")?.Card?.Shape);
        Assert.AreEqual(SequenceCardShape.Box, diagram.Find("s")?.Card?.Shape);
    }

    [TestMethod]
    public void HidingTheStereotypeLeavesTheCardItsNameAndItsDescription()
    {
        var diagram = C4Sequence.Read("C4Sequence\nHIDE_STEREOTYPE()\nPerson(p, \"P\")\nRel(p, p, \"x\")");

        Assert.AreEqual(string.Empty, diagram.Find("p")?.Card?.Stereotype);
    }

    [TestMethod]
    public void ADescriptionIsDrawnOnlyWhereItIsAskedFor()
    {
        const string source = "C4Sequence\nPerson(p, \"P\", \"Somebody\")\nRel(p, p, \"x\")";

        Assert.IsNull(C4Sequence.Read(source).Find("p")?.Card?.Said);
        Assert.AreEqual("Somebody",
                        C4Sequence.Read("C4Sequence\nSHOW_ELEMENT_DESCRIPTIONS()\nPerson(p, \"P\", \"Somebody\")\nRel(p, p, \"x\")")
                                  .Find("p")?.Card?.Said?.Text);
    }

    [TestMethod]
    public void ARelationshipIsAMessageCarryingWhatItIsDoneWith()
    {
        var message = C4Sequence.Read("C4Sequence\nPerson(a, \"A\")\nSystem(b, \"B\")\nRel(a, b, \"Visits\", \"HTTPS\", \"Every day\")")
                                .Messages.Single();

        Assert.AreEqual("a", message.From);
        Assert.AreEqual("b", message.To);
        Assert.AreEqual("Visits", message.Said?.Text);
        CollectionAssert.AreEqual(new[] { "HTTPS", "Every day" }, message.Under.Select(part => part.Text).ToArray());
        Assert.AreEqual(SequenceHead.Arrow, message.Far);
        Assert.AreEqual(SequenceHead.None, message.Near);
    }

    [TestMethod]
    public void ARelBackPointsTheOtherWayAndABiRelPointsBothWays()
    {
        var back = C4Sequence.Read("C4Sequence\nPerson(a, \"A\")\nSystem(b, \"B\")\nRel_Back(a, b, \"Answers\")").Messages.Single();

        Assert.AreEqual("b", back.From);
        Assert.AreEqual("a", back.To);

        var both = C4Sequence.Read("C4Sequence\nPerson(a, \"A\")\nSystem(b, \"B\")\nBiRel(a, b, \"Talks to\")").Messages.Single();

        Assert.AreEqual(SequenceHead.Arrow, both.Near);
        Assert.AreEqual(SequenceHead.Arrow, both.Far);
    }

    [TestMethod]
    public void ABoundaryIsTheBoxGroupingTheLifelinesInsideIt()
    {
        var diagram = C4Sequence.Read(
            "C4Sequence\nPerson(a, \"A\")\nBoundary(b, \"API Application\", \"Container\")\nSystem(s, \"Core\")\nBoundary_End()\n"
            + "Rel(a, s, \"Uses\")");

        var box = diagram.Boxes.Single();

        Assert.AreEqual("API Application", box.Said?.Text);
        Assert.AreEqual(box.Key, diagram.Find("s")?.Box);
        Assert.IsNull(diagram.Find("a")?.Box);
    }

    [TestMethod]
    public void ABoundaryClosedByABraceGroupsTheSameWay()
    {
        var diagram = C4Sequence.Read("C4Sequence\nBoundary(b, \"The bank\") {\n  System(s, \"Core\")\n}\nPerson(a, \"A\")\nRel(a, s, \"x\")");

        Assert.AreEqual(diagram.Boxes.Single().Key, diagram.Find("s")?.Box);
        Assert.IsNull(diagram.Find("a")?.Box);
    }

    [TestMethod]
    public void NumberingIsOnlyWhereShowIndexAsksForIt()
    {
        Assert.IsNull(C4Sequence.Read("C4Sequence\nPerson(a, \"A\")\nRel(a, a, \"x\")").Messages.Single().Number);

        var numbered = C4Sequence.Read("C4Sequence\nSHOW_INDEX()\nPerson(a, \"A\")\nRel(a, a, \"x\")\nRel(a, a, \"y\")")
                                 .Messages.ToList();

        Assert.AreEqual("1", numbered[0].Number);
        Assert.AreEqual("2", numbered[1].Number);
    }

    [TestMethod]
    public void TheIndexIsWhateverTheCallWorksOut()
    {
        var numbers = C4Sequence.Read(
            "C4Sequence\nSHOW_INDEX()\nPerson(a, \"A\")\nRel(a, a, \"one\", $index=Index())\n"
            + "Rel(a, a, \"again\", $index=LastIndex())\nRel(a, a, \"then\", $index=SetIndex(9))\nRel(a, a, \"after\")")
            .Messages.Select(message => message.Number).ToList();

        CollectionAssert.AreEqual(new[] { "1", "1", "9", "10" }, numbers);
    }

    [TestMethod]
    public void RelIndexTakesItsNumberFirstAndPushesTheRestAlong()
    {
        var message = C4Sequence.Read("C4Sequence\nSHOW_INDEX()\nPerson(a, \"A\")\nSystem(b, \"B\")\nRelIndex(7, a, b, \"x\", \"HTTPS\")")
                                .Messages.Single();

        Assert.AreEqual("7", message.Number);
        Assert.AreEqual("x", message.Said?.Text);
        Assert.AreEqual("HTTPS", message.Under.Single().Text);
    }

    [TestMethod]
    public void AnArgumentGivenByNameWinsOverTheOneInItsPlace()
    {
        var diagram = C4Sequence.Read("C4Sequence\nPerson(a, \"Positional\", $label=\"By name\")\nRel(a, a, \"x\")");

        Assert.AreEqual("By name", diagram.Find("a")?.Said?.Text);
    }

    [TestMethod]
    public void ALabelHoldsACommaBetweenItsQuotes()
    {
        var diagram = C4Sequence.Read("C4Sequence\nContainer(c, \"Web\", \"C#, ASP.NET Core\")\nRel(c, c, \"x\")");

        Assert.AreEqual("[Container: C#, ASP.NET Core]", diagram.Find("c")?.Card?.Stereotype);
    }

    [TestMethod]
    public void AStyleNamingAnElementsTypeColoursIt()
    {
        var diagram = C4Sequence.Read(
            "C4Sequence\nPerson(a, \"A\")\nUpdateElementStyle(\"person\", $bgColor=\"#08427b\", $fontColor=\"#ffffff\")\nRel(a, a, \"x\")");

        Assert.AreEqual("#08427b", diagram.Find("a")?.Card?.Fill);
        Assert.AreEqual("#ffffff", diagram.Find("a")?.Card?.Ink);
    }

    [TestMethod]
    public void ATagColoursWhatNamesIt()
    {
        var diagram = C4Sequence.Read(
            "C4Sequence\nAddElementTag(\"v1\", $bgColor=\"#1168bd\")\nPerson(a, \"A\", $tags=\"v1\")\nRel(a, a, \"x\")");

        Assert.AreEqual("#1168bd", diagram.Find("a")?.Card?.Fill);
    }

    [TestMethod]
    public void AStyleNamingTheTwoEndsColoursThatRelationship()
    {
        var message = C4Sequence.Read(
            "C4Sequence\nPerson(a, \"A\")\nSystem(b, \"B\")\nRel(a, b, \"x\")\n"
            + "UpdateRelStyle(a, b, $textColor=\"#ff0000\", $lineColor=\"#00ff00\", $lineStyle=DashedLine())")
            .Messages.Single();

        Assert.AreEqual("#ff0000", message.SaidInk);
        Assert.AreEqual("#00ff00", message.Ink);
        Assert.IsTrue(message.Dotted);
    }

    [TestMethod]
    public void ShowLegendGivesARowForEachKindWrittenAndEachTagThatNamesOne()
    {
        var diagram = C4Sequence.Read(
            "C4Sequence\nSHOW_LEGEND()\nAddElementTag(\"v1\", $bgColor=\"#1168bd\", $legendText=\"Version one\")\n"
            + "Person(a, \"A\")\nSystem(b, \"B\")\nSystemDb(c, \"C\")\nRel(a, b, \"x\")");

        CollectionAssert.AreEqual(new[] { "Person", "Software System", "Software System (database)", "Version one" },
                                  diagram.Legend.Select(row => row.Says).ToArray(),
                                  "a store is its own row: the key describes what was written, and a cylinder is not a box");
    }

    [TestMethod]
    public void AskingForALegendHidesTheStereotypesTheLegendNowCarries()
    {
        var diagram = C4Sequence.Read("C4Sequence\nSHOW_LEGEND()\nPerson(a, \"A\")\nRel(a, a, \"x\")");

        Assert.AreEqual(string.Empty, diagram.Find("a")?.Card?.Stereotype);
    }

    [TestMethod]
    public void ShowFootBoxesTurnsTheSecondRowOfParticipantsOff()
    {
        Assert.IsTrue(C4Sequence.Read("C4Sequence\nPerson(a, \"A\")\nRel(a, a, \"x\")").Config.Mirrored);
        Assert.IsFalse(C4Sequence.Read("C4Sequence\nSHOW_FOOT_BOXES(false)\nPerson(a, \"A\")\nRel(a, a, \"x\")").Config.Mirrored);
    }

    [TestMethod]
    public void ASequenceDiagramsOwnLinesAreReadAmongTheMacros()
    {
        var diagram = C4Sequence.Read(
            "C4Sequence\nPerson(a, \"A\")\nSystem(b, \"B\")\nalt it works\nRel(a, b, \"x\")\nelse it does not\n"
            + "Rel(a, b, \"y\")\nend\nNote over a,b: they met\nactivate b\ndeactivate b");

        Assert.AreEqual(1, diagram.Items.OfType<SequenceOpening>().Count());
        Assert.AreEqual(1, diagram.Items.OfType<SequenceDivider>().Count());
        Assert.AreEqual(1, diagram.Items.OfType<SequenceNote>().Count());
        Assert.AreEqual(2, diagram.Items.OfType<SequenceTurn>().Count());
        Assert.AreEqual(2, diagram.Messages.Count());
    }

    [TestMethod]
    public void AParticipantWrittenAsASequenceDiagramsOwnStandsBesideTheCards()
    {
        var diagram = C4Sequence.Read("C4Sequence\nPerson(a, \"A\")\nparticipant plain\nRel(a, plain, \"Uses\")");

        Assert.IsNotNull(diagram.Find("a")?.Card, "a C4 element is a card");
        Assert.IsNull(diagram.Find("plain")?.Card, "and a participant written as a sequence diagram's own is not");
        CollectionAssert.AreEqual(new[] { "a", "plain" }, diagram.Participants.Select(one => one.Id).ToList());
    }

    [TestMethod]
    public void TheTitleIsTheBlocksOwn()
    {
        Assert.AreEqual("Sign-in sequence",
                        C4Sequence.Read("C4Sequence\ntitle Sign-in sequence\nPerson(a, \"A\")\nRel(a, a, \"x\")").Block.Title?.Text);
    }

    [TestMethod]
    public void AFrameHoldsTheMacrosWrittenInsideIt()
    {
        var diagram = C4Sequence.Read("C4Sequence\nPerson(a, \"A\")\nloop every day\nRel(a, a, \"x\")\nend");
        var opening = diagram.Items.OfType<SequenceOpening>().Single();
        var message = diagram.Messages.Single();

        Assert.IsTrue(opening.Order < message.Order, "the frame opens before the message it holds");
        Assert.IsTrue(message.Order < diagram.Items.OfType<SequenceClosing>().Single().Order, "and closes after it");
    }

    [TestMethod]
    public void TheLinesAPastedDiagramBringsWithItAreReadAndDrawNothing()
    {
        var diagram = C4Sequence.Read("C4Sequence\n@startuml\n!include C4_Sequence.puml\n' a note\nPerson(a, \"A\")\nRel(a, a, \"x\")\n@enduml");

        Assert.AreEqual(1, diagram.Participants.Count);
        Assert.AreEqual(1, diagram.Messages.Count());
    }

    [TestMethod]
    public void TheFrontMatterSaysWhatAC4SequenceIsDrawnAt()
    {
        var config = C4Config.Read("config:\n  c4:\n    wrap: true\n    width: 220\n  sequence:\n    actorMargin: 70");

        Assert.IsTrue(config.Wraps);
        Assert.AreEqual(220, config.Widest);
        Assert.AreEqual(70, config.Between);
    }
}
