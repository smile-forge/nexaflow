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
}
