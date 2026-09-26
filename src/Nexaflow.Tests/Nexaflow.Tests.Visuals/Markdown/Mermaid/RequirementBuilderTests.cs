using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using Nexaflow.Markdown.Mermaid;
using Nexaflow.Tests.Fixtures;
using Nexaflow.Visuals.Text.Editing;
using Nexaflow.Visuals.Text.Markdown;
using Nexaflow.Visuals.Text.Markdown.Mermaid;
using Nexaflow.Visuals.Text.Markdown.Mermaid.Requirement;

namespace Nexaflow.Tests.Visuals.Markdown.Mermaid;

/// <summary>
/// A <c>requirementDiagram</c> block drawn on the shared layout tree: each requirement as its name and its fields, the boxes in
/// the ranks the relations put them in, and what holds between them written over the lines joining them.
/// </summary>
[TestClass]
[TestCategory("UI")]
[CoversNode("requirement-diagram")]
public class RequirementBuilderTests : MermaidBuilderContract
{
    /// <summary>The diagram the documentation opens with, cut to what draws differently.</summary>
    private const string Intro =
        "requirementDiagram\n\nrequirement test_req {\nid: 1\ntext: the test text.\nrisk: high\nverifymethod: test\n}\n\n"
        + "functionalRequirement test_req2 {\nid: 1.1\ntext: the second test text.\nrisk: low\nverifymethod: inspection\n}\n\n"
        + "element test_entity {\ntype: simulation\n}\n\n"
        + "test_entity - satisfies -> test_req2\ntest_req - contains -> test_req2";

    /// <summary>One relation of each kind there is.</summary>
    private const string Related =
        "requirementDiagram\na - contains -> b\na - copies -> c\na - derives -> d\na - satisfies -> e\n"
        + "a - verifies -> f\na - refines -> g\na - traces -> h\ni <- contains - a";

    public override MermaidDiagram Diagram => MermaidDiagram.Requirement;

    protected override IEnumerable<(string What, string Source)> Drawn =>
    [
        ("the documentation's own", Intro),
        ("one relation of each kind", Related),
        ("laid out left to right", "requirementDiagram\n  direction LR\n  a - satisfies -> b\n  b - traces -> c"),
        ("laid out bottom up", "requirementDiagram\n  direction BT\n  a - satisfies -> b"),
        ("laid out right to left", "requirementDiagram\n  direction RL\n  a - satisfies -> b"),
        ("a requirement on its own", "requirementDiagram\n  requirement A {\n    id: 1\n    text: what it asks for\n  }"),
        ("a requirement with no fields", "requirementDiagram\n  requirement A {\n  }"),
        ("a box only a relation names", "requirementDiagram\n  requirement A {\n    id: 1\n  }\n  A - traces -> B"),
        ("an element", "requirementDiagram\n  element E {\n    type: \"test suite\"\n    docref: reqs/e\n  }"),
        ("a name in quotes", "requirementDiagram\n  requirement \"two words\" {\n    id: 1\n  }"),
        ("what holds the other", "requirementDiagram\n  a - contains -> b"),
        ("a relation the other way round", "requirementDiagram\n  b <- satisfies - a"),
        ("a ring of them", "requirementDiagram\n  a - traces -> b\n  b - traces -> c\n  c - traces -> a"),
        ("one joined to itself", "requirementDiagram\n  a - traces -> a\n  a - traces -> b"),
        ("classes and styles",
         "requirementDiagram\n  a:::blue\n  a - traces -> b\n  classDef blue fill:#6e6ce6,color:white\n"
         + "  style b stroke:#f66,stroke-width:3px"),
        ("a box of its own size",
         "---\nconfig:\n  requirement:\n    rect_min_width: 200\n    line_height: 24\n---\n"
         + "requirementDiagram\n  requirement A {\n    id: 1\n  }"),
        ("still being written", "requirementDiagram\n  requirement A {\n    id: \n  }\n  \"\" - satisfies -> \"\""),
        ("what nobody means to write",
         "requirementDiagram\n  requirement A {\n    risk: enormous\n  }\n  }\n  style nowhere fill:#969"),
        ("nothing to draw", "requirementDiagram"),
    ];

    [TestMethod]
    public void ARelationPutsWhatItReachesInTheRankBeyondWhatItLeaves() => UiThread.Run(() =>
    {
        const string down = "requirementDiagram\n  a - satisfies -> b";
        var stacked = Boxes(down, Lay(down));
        Assert.IsTrue(stacked["b"].Top > stacked["a"].Bottom, $"b is below a: {stacked["a"]} then {stacked["b"]}");

        const string across = "requirementDiagram\n  direction LR\n  a - satisfies -> b";
        var beside = Boxes(across, Lay(across));
        Assert.IsTrue(beside["b"].Left > beside["a"].Right, $"b is right of a: {beside["a"]} then {beside["b"]}");
    });

    [TestMethod]
    public void ARequirementDrawsItsFieldsUnderItsNameInTheOrderTheyAreWritten() => UiThread.Run(() =>
    {
        var laid = Lay("requirementDiagram\n  requirement A {\n    id: 1\n    text: what it asks for\n  }");
        var name = Words(Pieces(laid, RequirementPiece.Box).Single(), "A");
        var facts = Pieces(laid, RequirementPiece.Fact);

        Assert.AreEqual(2, facts.Count);
        Assert.IsTrue(facts[0].Bounds.Top >= name.Bottom, $"the first field is under the name: {name} then {facts[0].Bounds}");
        Assert.IsTrue(facts[1].Bounds.Top >= facts[0].Bounds.Bottom, "and the second under the first");
    });

    [TestMethod]
    public void AFieldIsMermaidsWordForItThenTheCharactersWritten() => UiThread.Run(() =>
    {
        var laid = Lay("requirementDiagram\n  requirement A {\n    verifymethod: test\n  }");
        var fact = Pieces(laid, RequirementPiece.Fact).Single();

        CollectionAssert.AreEqual(new[] { "Verification:", "test" }, Said(fact).Select(words => words.Words!.Glyphs.Text).ToArray());
    });

    [TestMethod]
    public void WhatKindOfThingItIsIsDrawnInGuillemetsOverItsName() => UiThread.Run(() =>
    {
        var box = Pieces(Lay("requirementDiagram\n  designConstraint A {\n    id: 1\n  }"), RequirementPiece.Box).Single();

        var kind = Words(box, "«Design Constraint»");
        var name = Words(box, "A");

        Assert.IsTrue(kind.Bottom <= name.Top + 1, $"what it is sits over its name: {kind} then {name}");
    });

    [TestMethod]
    public void ABoxNothingWritesFieldsForIsShallowerThanOneWithThem() => UiThread.Run(() =>
    {
        const string source = "requirementDiagram\n  requirement A {\n    id: 1\n  }\n  A - traces -> B";
        var boxes = Boxes(source, Lay(source));

        Assert.IsTrue(boxes["B"].Height < boxes["A"].Height, $"B has no fields to draw: {boxes["B"]} against {boxes["A"]}");
    });

    [TestMethod]
    public void WhatHoldsBetweenTwoOfThemIsWrittenOverTheLineJoiningThem() => UiThread.Run(() =>
    {
        var label = Pieces(Lay("requirementDiagram\n  a - satisfies -> b"), RequirementPiece.Label).Single();

        Assert.AreEqual("«satisfies»", Said(label).Single().Words!.Glyphs.Text);
    });

    [TestMethod]
    public void TheOneHoldingTheOtherIsASolidLineAndEveryOtherIsDashed() => UiThread.Run(() =>
    {
        var holds = Pieces(Lay("requirementDiagram\n  a - contains -> b"), RequirementPiece.Relation).Single();
        var meets = Pieces(Lay("requirementDiagram\n  a - satisfies -> b"), RequirementPiece.Relation).Single();

        Assert.IsFalse(Dashed(holds), "what holds the other is the whole and its parts, drawn solid");
        Assert.IsTrue(Dashed(meets), "and everything else is a dashed arrow");
    });

    [TestMethod]
    public void TheFrontMatterSaysHowSmallABoxMayBe() => UiThread.Run(() =>
    {
        const string source = "requirementDiagram\n  requirement A {\n    id: 1\n  }";
        const string wider = "---\nconfig:\n  requirement:\n    rect_min_width: 240\n---\n" + source;

        Assert.IsTrue(Boxes(wider, Lay(wider))["A"].Width >= 240);
        Assert.IsTrue(Boxes(source, Lay(source))["A"].Width < 240);
    });

    // ── What it works with ──────────────────────────────────────────────────

    /// <summary>Every box drawn, by the name it was written with.</summary>
    private static Dictionary<string, Rect> Boxes(string source, Laid laid)
    {
        var boxes = new Dictionary<string, Rect>();

        foreach (var piece in Pieces(laid, RequirementPiece.Box))
            if (piece.SelfAndDescendants().FirstOrDefault(part => part.Kind == MermaidPiece.Shape) is { } shape)
                boxes[Written(source, shape.Part).Trim('"')] = piece.Bounds;

        return boxes;
    }

    /// <summary>Where the words saying this were drawn inside a piece.</summary>
    private static Rect Words(Piece piece, string said) => Said(piece).First(words => words.Words!.Glyphs.Text == said).Bounds;

    /// <summary>The words drawn under a piece, in the order they were drawn.</summary>
    private static IEnumerable<Piece> Said(Piece piece) =>
        piece.SelfAndDescendants().Where(part => part.Kind == MermaidPiece.Words && part.Words is not null);

    /// <summary>Whether anything under a piece is drawn with a broken line.</summary>
    private static bool Dashed(Piece piece) =>
        piece.SelfAndDescendants().SelectMany(inner => inner.Marks.ToArray()).OfType<GeometryMark>()
            .Any(mark => mark.Dashes is not null);

    [TestMethod]
    public void AnElementSaysWhatItIsOverItsName_AndEachFieldIsMermaidsWordForIt() => UiThread.Run(() =>
    {
        var box = Pieces(Lay("requirementDiagram\n  element E {\n    type: \"test suite\"\n    docRef: reqs/e\n  }"), RequirementPiece.Box).Single();
        var said = Said(box).Select(words => words.Words!.Glyphs.Text).ToArray();

        CollectionAssert.AreEqual(new[] { "«Element»", "E", "Type:", "test suite", "Doc Ref:", "reqs/e" }, said, "a value in quotes says what is between them");
    });

    [TestMethod]
    public void ARelationWrittenTheOtherWayRoundStillLeavesTheOneWrittenFirst() => UiThread.Run(() =>
    {
        const string source = "requirementDiagram\n  b <- satisfies - a";
        var boxes = Boxes(source, Lay(source));

        Assert.IsTrue(boxes["b"].Top > boxes["a"].Bottom, $"a leaves, so b is the rank beyond it: {boxes["a"]} then {boxes["b"]}");
    });

    [TestMethod]
    public void ANameWrittenTwiceIsOneBox_WithTheFieldsItsBlockWrites() => UiThread.Run(() =>
    {
        var laid = Lay("requirementDiagram\n  a - traces -> b\n  requirement a {\n    id: 1\n  }");

        Assert.AreEqual(2, Pieces(laid, RequirementPiece.Box).Count);
        Assert.AreEqual(1, Pieces(laid, RequirementPiece.Fact).Count);
    });

    [TestMethod]
    public void ANameWithNothingInItYetIsABoxOfItsOwn_SoWritingItIsWatched() => UiThread.Run(() =>
        Assert.AreEqual(2, Pieces(Lay("requirementDiagram\n  \"\" - satisfies -> \"\"", writing: true), RequirementPiece.Box).Count,
                        "the two ends of a relation nobody has named yet are two boxes"));

    [TestMethod]
    public void UpThePageIsWhatADirectionLineSays() => UiThread.Run(() =>
    {
        const string source = "requirementDiagram\n  direction BT\n  a - satisfies -> b";
        var boxes = Boxes(source, Lay(source));

        Assert.IsTrue(boxes["b"].Bottom < boxes["a"].Top, $"b is above a: {boxes["a"]} then {boxes["b"]}");
    });

    [TestMethod]
    public void ABoxIsFilledWithWhatStylesIt() => UiThread.Run(() =>
    {
        var box = Pieces(Lay("requirementDiagram\n  A:::blue\n  classDef blue fill:#0000ff"), RequirementPiece.Box).Single();
        var fills = box.SelfAndDescendants().SelectMany(piece => piece.Marks.ToArray()).OfType<GeometryMark>().Select(mark => mark.Fill).OfType<SolidColorBrush>();

        Assert.IsTrue(fills.Any(fill => fill.Color == Color.FromRgb(0, 0, 0xFF)), "filled as its class says");
    });
}
