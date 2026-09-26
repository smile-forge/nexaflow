using System.Windows;
using Nexaflow.Markdown.Mermaid;
using Nexaflow.Tests.Fixtures;
using Nexaflow.Visuals.Text.Editing;
using Nexaflow.Visuals.Text.Markdown.Mermaid;
using Nexaflow.Visuals.Text.Markdown.Mermaid.Sequence;

namespace Nexaflow.Tests.Visuals.Markdown.Mermaid;

/// <summary>
/// What a <c>C4Sequence</c> draws: the sequence diagram's own picture, with the participants' boxes drawn as cards and the
/// relationships carrying what they are done with.
/// </summary>
[TestClass]
[TestCategory("UI")]
[CoversNode("c4-sequence")]
public class C4SequenceBuilderTests : MermaidBuilderContract
{
    /// <summary>The sign-in sequence our own documentation shows.</summary>
    private const string Intro =
        "C4Sequence\ntitle Sign-in sequence\n\nSHOW_INDEX()\nSHOW_FOOT_BOXES(false)\n\n"
        + "Person(customer, \"Banking Customer\")\nContainer(spa, \"Single-Page App\", \"Angular\")\n"
        + "Boundary(b, \"API Application\", \"Container\")\nComponent(signin, \"Sign In Controller\", \"Spring MVC\")\n"
        + "ComponentDb(users, \"User Store\", \"Spring Bean\")\nBoundary_End()\n\n"
        + "Rel(customer, spa, \"Submits credentials\", \"HTTPS\")\nRel(spa, signin, \"POST /signin\", \"JSON/HTTPS\")\n"
        + "alt credentials valid\nRel(signin, users, \"Looks the user up\", \"JDBC\")\n"
        + "Rel_Back(signin, users, \"Returns the record\")\nelse rejected\nRel_Back(spa, signin, \"401 Unauthorized\")\nend\n"
        + "Rel_Back(customer, spa, \"Shows the dashboard\")";

    public override MermaidDiagram Diagram => MermaidDiagram.C4Sequence;

    protected override IEnumerable<(string What, string Source)> Drawn =>
    [
        ("the documentation's own", Intro),
        ("every kind of element",
         "C4Sequence\nPerson(p, \"A person\")\nPerson_Ext(q, \"Somebody else's\")\nSystem(s, \"A system\")\n"
         + "SystemDb(d, \"A store\")\nSystemQueue(u, \"A queue\")\nContainer(c, \"A container\", \"Java\")\n"
         + "Component(m, \"A component\", \"Spring\")\nRel(p, s, \"Uses\", \"HTTPS\")"),
        ("descriptions on the cards",
         "C4Sequence\nSHOW_ELEMENT_DESCRIPTIONS()\nPerson(p, \"A person\", \"Somebody outside the bank\")\n"
         + "System(s, \"A system\", \"What the bank runs on\")\nRel(p, s, \"Uses\")"),
        ("a key under it",
         "C4Sequence\nSHOW_LEGEND()\nAddElementTag(\"v1\", $bgColor=\"#1168bd\", $legendText=\"Version one\")\n"
         + "Person(a, \"A\")\nSystem(b, \"B\")\nSystemDb(c, \"C\")\nRel(a, b, \"Uses\")"),
        ("colours of its own",
         "C4Sequence\nPerson(a, \"A\")\nSystem(b, \"B\")\nUpdateElementStyle(\"person\", $bgColor=\"#08427b\", $fontColor=\"#ffffff\")\n"
         + "Rel(a, b, \"Uses\")\nUpdateRelStyle(a, b, $textColor=\"#ff6b6b\", $lineColor=\"#ff6b6b\", $lineStyle=DashedLine())"),
        ("boundaries nested",
         "C4Sequence\nBoundary(o, \"The bank\") {\n  Boundary(i, \"The API\") {\n    System(s, \"Core\")\n  }\n}\n"
         + "Person(a, \"A\")\nRel(a, s, \"Banks with\")"),
        ("a sequence diagram's own lines among the macros",
         "C4Sequence\nPerson(a, \"A\")\nSystem(b, \"B\")\nloop every day\nRel(a, b, \"Asks\")\nend\n"
         + "Note over a,b: they meet\nactivate b\nRel_Back(a, b, \"Answers\")\ndeactivate b"),
        ("numbering", "C4Sequence\nSHOW_INDEX()\nPerson(a, \"A\")\nRel(a, a, \"One\")\nRel(a, a, \"Two\")"),
        ("the wrappers a pasted diagram brings",
         "C4Sequence\n@startuml\n!include C4_Sequence.puml\n' a note\nPerson(a, \"A\")\nRel(a, a, \"x\")\n@enduml"),
        ("still being written", "C4Sequence\nRel(, , \"\")"),
        ("what nobody means to write", "C4Sequence\n??? !!!\n}"),
        ("nothing to draw", "C4Sequence"),
    ];

    [TestMethod]
    public void AnElementIsDrawnAsACardSayingWhatItIsUnderItsName() => UiThread.Run(() =>
    {
        var laid = Lay("C4Sequence\nContainer(spa, \"Single-Page App\", \"Angular\")\nRel(spa, spa, \"Runs\")");
        var head = Pieces(laid, SequencePiece.Head)[0];

        var name = Said(head).First(words => words.Words!.Glyphs.Text == "Single-Page App").Bounds;
        var says = Said(head).First(words => words.Words!.Glyphs.Text == "[Container: Angular]").Bounds;

        Assert.IsTrue(says.Top >= name.Bottom - 1, $"what it is goes under its name: {name} then {says}");
    });

    [TestMethod]
    public void ABoundaryIsTheBoxRoundTheLifelinesInsideIt() => UiThread.Run(() =>
    {
        var laid = Lay("C4Sequence\nPerson(a, \"A\")\nBoundary(b, \"The API\")\nSystem(s, \"Core\")\nBoundary_End()\n"
                       + "Rel(a, s, \"Uses\")");

        var box = Pieces(laid, SequencePiece.Box).Single().Bounds;
        var heads = Pieces(laid, SequencePiece.Lifeline)
            .Select(piece => piece.SelfAndDescendants().First(part => part.Kind == SequencePiece.Head).Bounds)
            .OrderBy(bounds => bounds.Left).ToList();

        Assert.IsFalse(Holds(box, heads[0]), "the one written outside it is outside the box");
        Assert.IsTrue(Holds(box, heads[1]), "and the one written inside it is inside");
    });

    [TestMethod]
    public void WhatARelationshipIsDoneWithIsDrawnUnderWhatItSays() => UiThread.Run(() =>
    {
        var laid = Lay("C4Sequence\nPerson(a, \"A\")\nSystem(b, \"B\")\nRel(a, b, \"Visits\", \"HTTPS\")");
        var message = Pieces(laid, SequencePiece.Message).Single();

        var says = Said(message).First(words => words.Words!.Glyphs.Text == "Visits").Bounds;
        var with = Said(message).First(words => words.Words!.Glyphs.Text == "HTTPS").Bounds;

        Assert.IsTrue(with.Top >= says.Bottom - 1, $"what it is done with goes under what it says: {says} then {with}");
    });

    [TestMethod]
    public void AKeyIsDrawnUnderTheDiagramWhereItAsksForOne() => UiThread.Run(() =>
    {
        var laid = Lay("C4Sequence\nSHOW_LEGEND()\nPerson(a, \"A\")\nSystem(b, \"B\")\nRel(a, b, \"Uses\")");
        var key = Pieces(laid, MermaidPiece.Legend).Single();

        var rows = key.SelfAndDescendants()
                      .Where(part => part.Kind == MermaidPiece.Words && part.Words is not null)
                      .Select(part => part.Words!.Glyphs.Text)
                      .ToList();

        CollectionAssert.AreEqual(new[] { "Person", "Software System" }, rows);
        Assert.IsTrue(key.Bounds.Top > Pieces(laid, SequencePiece.Message).Single().Bounds.Bottom, "and it is under the diagram");
    });

    [TestMethod]
    public void TheKeyRunsAcrossTheFootOfTheDiagram_EachRowInTheColourOfWhatItNames() => UiThread.Run(() =>
    {
        var laid = Lay("C4Sequence\nSHOW_LEGEND()\nPerson(a, \"A\")\nContainer(b, \"B\", \"Java\")\nRel(a, b, \"Uses\")");
        var key = Pieces(laid, MermaidPiece.Legend).Single();
        var rows = key.SelfAndDescendants().Where(part => part.Kind == MermaidPiece.Words && part.Words is not null).Select(part => part.Bounds).ToList();

        Assert.AreEqual(2, rows.Count);
        Assert.AreEqual(rows[0].Top, rows[1].Top, 1, "the rows stand side by side");
        Assert.IsTrue(key.SelfAndDescendants().Where(part => part.Kind == MermaidPiece.Swatch).All(swatch => swatch.Marks.ToArray().OfType<RuleMark>().Any()),
                      "and every swatch is filled with the colour the cards it names are painted, though nothing wrote one");
    });

    [TestMethod]
    public void ItIsDrawnByTheSequenceDiagramsOwnBuilder() => UiThread.Run(() =>
    {
        var laid = Lay("C4Sequence\nPerson(a, \"A\")\nSystem(b, \"B\")\nalt it works\nRel(a, b, \"x\")\nend");

        Assert.AreEqual(1, Pieces(laid, SequencePiece.Frame).Count, "a C4 sequence draws a frame as a sequence diagram does");
        Assert.AreEqual(2, Pieces(laid, SequencePiece.Lifeline).Count);
        Assert.AreEqual(1, Pieces(laid, SequencePiece.Line).Count);
    });

    private static IEnumerable<Piece> Said(Piece piece) =>
        piece.SelfAndDescendants().Where(part => part.Kind == MermaidPiece.Words && part.Words is not null);

    private static bool Holds(Rect over, Rect inner) =>
        inner.Left >= over.Left - 1 && inner.Right <= over.Right + 1;

    [TestMethod]
    public void ABoundaryClosedByABraceGroupsTheSameWay() => UiThread.Run(() =>
    {
        var laid = Lay("C4Sequence\nBoundary(b, \"The bank\") {\n  System(s, \"Core\")\n}\nPerson(a, \"A\")\nRel(a, s, \"x\")");
        var box = Pieces(laid, SequencePiece.Box).Single().Bounds;
        var heads = Pieces(laid, SequencePiece.Lifeline)
            .Select(piece => piece.SelfAndDescendants().First(part => part.Kind == SequencePiece.Head).Bounds)
            .OrderBy(bounds => bounds.Left).ToList();

        Assert.IsTrue(Holds(box, heads[0]), "the one written inside it is inside the box");
        Assert.IsFalse(Holds(box, heads[1]), "and the one written after the brace is not");
    });

    [TestMethod]
    public void ADescriptionIsDrawnOnACardOnlyWhereItIsAskedFor() => UiThread.Run(() =>
    {
        bool Described(string source) =>
            Pieces(Lay(source), SequencePiece.Head).SelectMany(Said).Any(words => words.Words!.Glyphs.Text == "Somebody");

        Assert.IsFalse(Described("C4Sequence\nPerson(p, \"P\", \"Somebody\")\nRel(p, p, \"x\")"));
        Assert.IsTrue(Described("C4Sequence\nSHOW_ELEMENT_DESCRIPTIONS()\nPerson(p, \"P\", \"Somebody\")\nRel(p, p, \"x\")"));
    });

    [TestMethod]
    public void ASequenceDiagramsOwnLinesAreDrawnAmongTheMacros() => UiThread.Run(() =>
    {
        var laid = Lay("C4Sequence\nPerson(a, \"A\")\nSystem(b, \"B\")\nalt it works\nRel(a, b, \"x\")\nelse it does not\n"
                       + "Rel(a, b, \"y\")\nend\nNote over a,b: they met\nactivate b\nRel(a, b, \"z\")\ndeactivate b");

        Assert.AreEqual(1, Pieces(laid, SequencePiece.Frame).Count);
        Assert.AreEqual(1, Pieces(laid, SequencePiece.Divider).Count);
        Assert.AreEqual(1, Pieces(laid, SequencePiece.Note).Count);
        Assert.AreEqual(1, Pieces(laid, SequencePiece.Bar).Count);
        Assert.AreEqual(3, Pieces(laid, SequencePiece.Message).Count);

        var frame = Pieces(laid, SequencePiece.Frame).Single().Bounds;
        var messages = Pieces(laid, SequencePiece.Message).OrderBy(message => message.Bounds.Top).ToList();
        Assert.IsTrue(Holds(frame, messages[0].Bounds) && Holds(frame, messages[1].Bounds), "the frame holds the macros written inside it");
        Assert.IsFalse(frame.IntersectsWith(messages[2].Bounds), "and not the one written after it");
    });

    [TestMethod]
    public void AParticipantWrittenAsASequenceDiagramsOwnStandsBesideTheCards() => UiThread.Run(() =>
    {
        var heads = Pieces(Lay("C4Sequence\nPerson(a, \"A\")\nparticipant plain\nRel(a, plain, \"Uses\")"), SequencePiece.Lifeline)
            .Select(piece => piece.SelfAndDescendants().First(part => part.Kind == SequencePiece.Head))
            .OrderBy(head => head.Bounds.Left).ToList();

        Assert.AreEqual(2, heads.Count);
        Assert.IsTrue(Said(heads[0]).Any(words => words.Words!.Glyphs.Text == "[Person]"), "a C4 element is a card");
        CollectionAssert.AreEqual(new[] { "plain" }, Said(heads[1]).Select(words => words.Words!.Glyphs.Text).ToArray(),
                                  "and a participant written as a sequence diagram's own is a name in a box");
    });

    [TestMethod]
    public void TheLinesAPastedDiagramBringsDrawNothing() => UiThread.Run(() =>
    {
        var laid = Lay("C4Sequence\n@startuml\n!include C4_Sequence.puml\n' a note\nPerson(a, \"A\")\nSystem(b, \"B\")\nRel_Back(a, b, \"Answers\")\n@enduml");
        var lifelines = Pieces(laid, SequencePiece.Lifeline).OrderBy(piece => piece.Bounds.Left).ToList();

        Assert.AreEqual(2, lifelines.Count, "nothing but the two written");
        Assert.IsTrue(Said(lifelines[0]).Any(words => words.Words!.Glyphs.Text == "A"), "A stands first, where it was written");
        Assert.AreEqual(1, Pieces(laid, SequencePiece.Message).Count);
    });
}
