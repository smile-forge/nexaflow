using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using Nexaflow.Markdown.Mermaid;
using Nexaflow.Tests.Fixtures;
using Nexaflow.Visuals.Text.Editing;

using Nexaflow.Visuals.Text.Markdown;
using Nexaflow.Visuals.Text.Markdown.Mermaid;
using Nexaflow.Visuals.Text.Markdown.Mermaid.Er;

namespace Nexaflow.Tests.Visuals.Markdown.Mermaid;

/// <summary>
/// An <c>erDiagram</c> block drawn on the shared layout tree: each entity as its name over its attributes, the entities in the
/// ranks the relationships put them in, the subgraphs holding what is written inside them, and the crow's feet at either end.
/// </summary>
[TestClass]
[TestCategory("UI")]
[CoversNode("er-diagram")]
public class ErBuilderTests : MermaidBuilderContract
{
    /// <summary>The diagram the documentation opens with.</summary>
    private const string Intro =
        "erDiagram\n    CUSTOMER }|..|{ DELIVERY-ADDRESS : has\n    CUSTOMER ||--o{ ORDER : places\n"
        + "    CUSTOMER ||--o{ INVOICE : \"liable for\"\n    DELIVERY-ADDRESS ||--o{ ORDER : receives\n"
        + "    INVOICE ||--|{ ORDER : covers\n    ORDER ||--|{ ORDER-ITEM : includes";

    /// <summary>The documentation's attributes, with their keys and their comments.</summary>
    private const string Attributed =
        "erDiagram\n    CAR ||--o{ NAMED-DRIVER : allows\n    CAR {\n        string registrationNumber PK\n"
        + "        string make\n        string[] parts\n    }\n    PERSON {\n"
        + "        string driversLicense PK \"The license #\"\n        string(99) firstName \"Only 99 characters\"\n"
        + "        int age\n    }\n    PERSON ||--o{ NAMED-DRIVER : is";

    public override MermaidDiagram Diagram => MermaidDiagram.Er;

    protected override IEnumerable<(string What, string Source)> Drawn =>
    [
        ("the documentation's own", Intro),
        ("attributes, keys and comments", Attributed),
        ("every cardinality there is", "erDiagram\n  A |o--o| B : x\n  C ||--|| D : x\n  E }o--o{ F : x\n  G }|--|{ H : x"),
        ("one written in words", "erDiagram\n  A only one to zero or more B : makes"),
        ("one that does not identify what it reaches", "erDiagram\n  A ||..|| B : x"),
        ("laid out left to right", "erDiagram\n  direction LR\n  A ||--o{ B : x\n  B ||--o{ C : y"),
        ("laid out bottom up", "erDiagram\n  direction BT\n  A ||--o{ B : x"),
        ("laid out right to left", "erDiagram\n  direction RL\n  A ||--o{ B : x"),
        ("an entity on its own", "erDiagram\n  CUSTOMER"),
        ("an entity with nothing in it", "erDiagram\n  CUSTOMER {\n  }"),
        ("an entity with a label", "erDiagram\n  p[\"The person\"] {\n    string name\n  }"),
        ("a name written with a star", "erDiagram\n  A {\n    string *id\n    string make\n  }"),
        ("an entity named in quotes", "erDiagram\n  \"Two words\" ||--|| ORDER : places"),
        ("subgraphs", "erDiagram\n  subgraph Sales\n    A ||--o{ B : x\n  end\n  subgraph Stock\n    C\n  end\n  B ||--|| C : y"),
        ("subgraphs nested", "erDiagram\n  subgraph Outer\n    subgraph Inner\n      A\n    end\n    B\n  end"),
        ("a relationship naming a subgraph",
         "erDiagram\n  subgraph stock\n    PRODUCT\n    WAREHOUSE\n  end\n  SUPPLIER ||--o{ stock : supplies"),
        ("a ring of them", "erDiagram\n  A ||--|| B : x\n  B ||--|| C : y\n  C ||--|| A : z"),
        ("one joined to itself", "erDiagram\n  A ||--|| A : x\n  A ||--|| B : y"),
        ("classes and styles",
         "erDiagram\n  A:::blue ||--|| B : x\n  classDef blue fill:#6e6ce6,color:white\n  style B stroke:#f66,stroke-width:3px"),
        ("the front matter's own colours and sizes",
         "---\nconfig:\n  er:\n    minEntityWidth: 160\n    fontSize: 14\n    fill: \"#223\"\n    stroke: \"#88a\"\n---\n"
         + "erDiagram\n  A {\n    string name\n  }\n  A ||--|| B : x"),
        ("still being written", "erDiagram\n  A {\n    string \n  }\n  \"\" ||--o{ \"\" : \"\""),
        ("what nobody means to write", "erDiagram\n  A ||--?? B\n  end\n  style nowhere fill:#969"),
        ("nothing to draw", "erDiagram"),
    ];

    [TestMethod]
    public void ARelationshipPutsWhatItReachesInTheRankBeyondWhatItLeaves() => UiThread.Run(() =>
    {
        const string down = "erDiagram\n  A ||--o{ B : x";
        var stacked = Boxes(down, Lay(down));
        Assert.IsTrue(stacked["B"].Top > stacked["A"].Bottom, $"B is below A: {stacked["A"]} then {stacked["B"]}");

        const string across = "erDiagram\n  direction LR\n  A ||--o{ B : x";
        var beside = Boxes(across, Lay(across));
        Assert.IsTrue(beside["B"].Left > beside["A"].Right, $"B is right of A: {beside["A"]} then {beside["B"]}");
    });

    [TestMethod]
    public void AnEntityDrawsItsAttributesUnderItsNameInTheOrderTheyAreWritten() => UiThread.Run(() =>
    {
        var laid = Lay("erDiagram\n  CAR {\n    string make\n    int age\n  }");
        var name = Words(Pieces(laid, ErPiece.Entity).Single(), "CAR");
        var rows = Pieces(laid, ErPiece.Attribute);

        Assert.AreEqual(2, rows.Count);
        Assert.IsTrue(rows[0].Bounds.Top >= name.Bottom, $"the first attribute is under the name: {name} then {rows[0].Bounds}");
        Assert.IsTrue(rows[1].Bounds.Top >= rows[0].Bounds.Bottom, "and the second under the first");
    });

    [TestMethod]
    public void TheAttributesAreSetInColumnsSoTheyReadDownAsWellAsAcross() => UiThread.Run(() =>
    {
        var laid = Lay("erDiagram\n  CAR {\n    string registrationNumber PK\n    int age\n  }");
        var rows = Pieces(laid, ErPiece.Attribute);

        var names = rows.Select(row => Said(row).ElementAt(1).Bounds.Left).ToList();
        Assert.AreEqual(names[0], names[1], 0.5, "what each attribute is called starts in the same place");
    });

    [TestMethod]
    public void WhatAnAttributeSaysIsTheCharactersWrittenInIt() => UiThread.Run(() =>
    {
        var row = Pieces(Lay("erDiagram\n  CAR {\n    string plate PK, FK \"What it is known by\"\n  }"), ErPiece.Attribute)
            .Single();

        CollectionAssert.AreEqual(new[] { "string", "plate", "PK, FK", "What it is known by" },
                                  Said(row).Select(words => words.Words!.Glyphs.Text).ToArray());
    });

    [TestMethod]
    public void ASubgraphsBoxHoldsTheEntitiesWrittenInsideIt() => UiThread.Run(() =>
    {
        const string source = "erDiagram\n  subgraph Sales\n    A ||--|| B : x\n  end";
        var laid = Lay(source);
        var box = Pieces(laid, ErPiece.Group).Single().Bounds;

        foreach (var (name, bounds) in Boxes(source, laid))
            Assert.IsTrue(Holds(box, bounds), $"{name} is inside the subgraph: {bounds} in {box}");
    });

    [TestMethod]
    public void ARelationshipNamingASubgraphRunsToTheBoxItself() => UiThread.Run(() =>
    {
        const string source = "erDiagram\n  subgraph stock\n    PRODUCT\n  end\n  SUPPLIER ||--o{ stock : supplies";
        var laid = Lay(source);

        var boxes = Boxes(source, laid);
        CollectionAssert.AreEqual(new[] { "PRODUCT", "SUPPLIER" }, boxes.Keys.OrderBy(name => name).ToArray(),
                                  "nothing is drawn for the subgraph as though it were an entity");

        var group = Pieces(laid, ErPiece.Group).Single().Bounds;
        var line = Pieces(laid, ErPiece.Relation).Single().Bounds;

        Assert.IsTrue(line.Bottom >= group.Top - 1 && line.Top <= group.Top + 1,
                      $"the line reaches the edge of the box: {line} against {group}");
    });

    [TestMethod]
    public void AnEntityWithNoAttributesIsShallowerThanOneWithThem() => UiThread.Run(() =>
    {
        const string source = "erDiagram\n  A {\n    string name\n    string other\n  }\n  A ||--|| B : x";
        var boxes = Boxes(source, Lay(source));

        Assert.IsTrue(boxes["B"].Height <= boxes["A"].Height, $"B has no attributes to draw: {boxes["B"]} against {boxes["A"]}");
    });

    [TestMethod]
    public void WhatARelationshipIsCalledIsWrittenOverTheLineJoiningThem() => UiThread.Run(() =>
    {
        var label = Pieces(Lay("erDiagram\n  A ||--o{ B : places"), ErPiece.Label).Single();

        Assert.AreEqual("places", Said(label).Single().Words!.Glyphs.Text);
    });

    [TestMethod]
    public void TheFrontMatterSaysHowSmallAnEntityMayBe() => UiThread.Run(() =>
    {
        const string source = "erDiagram\n  A ||--|| B : x";
        const string wider = "---\nconfig:\n  er:\n    minEntityWidth: 260\n---\n" + source;

        Assert.IsTrue(Boxes(wider, Lay(wider))["A"].Width >= 260);
        Assert.IsTrue(Boxes(source, Lay(source))["A"].Width < 260);
    });

    // ── What it works with ──────────────────────────────────────────────────

    /// <summary>Every entity drawn, by the name it was written with.</summary>
    private static Dictionary<string, Rect> Boxes(string source, Laid laid)
    {
        var boxes = new Dictionary<string, Rect>();

        foreach (var piece in Pieces(laid, ErPiece.Entity))
            if (piece.SelfAndDescendants().FirstOrDefault(part => part.Kind == MermaidPiece.Shape) is { } shape)
                boxes[Written(source, shape.Part).Trim('"')] = piece.Bounds;

        return boxes;
    }

    /// <summary>Where the words saying this were drawn inside a piece.</summary>
    private static Rect Words(Piece piece, string said) => Said(piece).First(words => words.Words!.Glyphs.Text == said).Bounds;

    /// <summary>The words drawn under a piece, in the order they were drawn.</summary>
    private static IEnumerable<Piece> Said(Piece piece) =>
        piece.SelfAndDescendants().Where(part => part.Kind == MermaidPiece.Words && part.Words is not null);

    private static bool Holds(Rect over, Rect inner) =>
        inner.Left >= over.Left - 1 && inner.Right <= over.Right + 1 && inner.Top >= over.Top - 1 && inner.Bottom <= over.Bottom + 1;

    // ── What is read from the lines as written ──────────────────────────────

    /// <summary>What each attribute row says, in the order drawn.</summary>
    private string[][] Rows(string source) =>
        [.. Pieces(Lay(source), ErPiece.Attribute).Select(row => Said(row).Select(words => words.Words!.Glyphs.Text).ToArray())];

    /// <summary>What one relationship draws: its line and the ends at either side of it.</summary>
    private (string[] Shapes, bool Dotted) Line(string relation)
    {
        var marks = Pieces(Lay($"erDiagram\n  {relation}"), ErPiece.Relation).Single()
            .SelfAndDescendants().SelectMany(piece => piece.Marks.ToArray()).OfType<GeometryMark>().ToList();

        return ([.. marks.Select(mark => PathGeometry.CreateFromGeometry(mark.Shape).ToString())], marks.Any(mark => mark.Dashes is not null));
    }

    [TestMethod]
    public void AnEntityKeepsItsAttributesInTheOrderTheyAreWritten() => UiThread.Run(() =>
    {
        var rows = Rows("erDiagram\n  CAR {\n    string make\n    string plate PK \"What it is known by\"\n    int age\n  }");

        CollectionAssert.AreEqual(new[] { "string", "make" }, rows[0], "one written without a key or a comment says nothing more");
        CollectionAssert.AreEqual(new[] { "string", "plate", "PK", "What it is known by" }, rows[1]);
        CollectionAssert.AreEqual(new[] { "int", "age" }, rows[2]);
    });

    [TestMethod]
    public void ANameWrittenWithAStarIsAPrimaryKeyToo() => UiThread.Run(() =>
    {
        var rows = Rows("erDiagram\n  CAR {\n    string *plate\n    string *vin PK\n    string *owner FK\n    string[] parts\n  }");

        CollectionAssert.AreEqual(new[] { "string", "*plate", "PK" }, rows[0], "the star says it without writing it, and the name is drawn as written");
        CollectionAssert.AreEqual(new[] { "string", "*vin", "PK" }, rows[1], "one that says it both ways says it once");
        CollectionAssert.AreEqual(new[] { "string", "*owner", "PK, FK" }, rows[2], "and the star comes before what else was written");
        CollectionAssert.AreEqual(new[] { "string[]", "parts" }, rows[3], "a type says what brackets it was written with");
    });

    [TestMethod]
    public void AnAliasIsDrawnInsteadOfTheNameWhereverItIsWritten() => UiThread.Run(() =>
    {
        var laid = Lay("erDiagram\n  p[Person] {\n    string firstName\n  }\n  p ||--o| a[\"The account\"] : has");
        var said = Pieces(laid, ErPiece.Entity).Select(entity => Said(entity).First().Words!.Glyphs.Text).ToArray();

        CollectionAssert.AreEqual(new[] { "Person", "The account" }, said);
    });

    [TestMethod]
    public void EachCardinalityDrawsAnEndOfItsOwn_ReadAtEitherEnd() => UiThread.Run(() =>
    {
        var ends = new[] { "A |o--o| B : x", "A ||--|| B : x", "A }o--o{ B : x", "A }|--|{ B : x" }
            .Select(relation => string.Join(" ", Line(relation).Shapes)).ToList();

        Assert.AreEqual(4, ends.Distinct().Count());
        CollectionAssert.AreEqual(Line("A ||--o| B : x").Shapes, Line("A||--o|B : x").Shapes, "one written with no space in it draws the same");
    });

    [TestMethod]
    public void OneWrittenInWordsDrawsWhatTheSymbolsDraw() => UiThread.Run(() =>
    {
        CollectionAssert.AreEqual(Line("A ||--o{ B : x").Shapes, Line("A 1 to zero or more B : x").Shapes);
        CollectionAssert.AreEqual(Line("A }o..o{ B : x").Shapes, Line("A many(0) optionally to 0+ B : x").Shapes);
    });

    [TestMethod]
    public void ADottedLineIsOneThatDoesNotIdentifyWhatItReaches() => UiThread.Run(() =>
    {
        Assert.IsFalse(Line("A ||--|| B : x").Dotted);
        Assert.IsTrue(Line("A ||..|| B : x").Dotted);
        Assert.IsTrue(Line("A one optionally to one B : x").Dotted);
    });

    [TestMethod]
    public void ANameWrittenTwiceIsOneEntity_AndOneARelationshipNamesIsMadeForIt() => UiThread.Run(() =>
    {
        const string source = "erDiagram\n  A ||--|| B : x\n  A {\n    string name\n  }\n  A ||--|| C : y";
        var laid = Lay(source);

        CollectionAssert.AreEquivalent(new[] { "A", "B", "C" }, Boxes(source, laid).Keys.ToArray());
        Assert.AreEqual(1, Pieces(laid, ErPiece.Attribute).Count, "the attributes written for the A named above");
    });

    [TestMethod]
    public void ADirectionLineWinsOverTheFrontMatter() => UiThread.Run(() =>
    {
        const string configured = "---\nconfig:\n  er:\n    layoutDirection: RL\n---\nerDiagram\n";

        var left = Boxes(configured + "  A ||--o{ B : x", Lay(configured + "  A ||--o{ B : x"));
        Assert.IsTrue(left["B"].Right < left["A"].Left, $"the front matter lays it right to left: {left["A"]} then {left["B"]}");

        const string up = configured + "  direction BT\n  A ||--o{ B : x";
        var above = Boxes(up, Lay(up));
        Assert.IsTrue(above["B"].Bottom < above["A"].Top, $"and a direction line lays it bottom up: {above["A"]} then {above["B"]}");
    });

    [TestMethod]
    public void AnEntityIsBoxedIntoTheSubgraphItIsWrittenIn() => UiThread.Run(() =>
    {
        const string source = "erDiagram\n  subgraph Outer\n    subgraph Inner\n      A\n    end\n    B\n  end\n  C";
        var laid = Lay(source);
        var boxes = Boxes(source, laid);

        var groups = Pieces(laid, ErPiece.Group).ToDictionary(group => Written(source, group.Part).Split('\n')[0], group => group.Bounds);
        var (outer, inner) = (groups["subgraph Outer"], groups["subgraph Inner"]);

        Assert.IsTrue(Holds(inner, boxes["A"]), "A is in the inner subgraph");
        Assert.IsTrue(Holds(outer, boxes["B"]) && !Holds(inner, boxes["B"]), "B in the outer one alone");
        Assert.IsFalse(Holds(outer, boxes["C"]), "and C in neither");
    });

    [TestMethod]
    public void ASubgraphStandsForTheWholeOfWhatWasWrittenInIt() => UiThread.Run(() =>
    {
        const string source = "erDiagram\n  subgraph Sales\n    A\n  end";

        Assert.AreEqual("subgraph Sales\n    A\n  end", Written(source, Pieces(Lay(source), ErPiece.Group).Single().Part));
    });
}
