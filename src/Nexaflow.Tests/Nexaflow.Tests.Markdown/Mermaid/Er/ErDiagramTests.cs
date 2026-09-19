using Nexaflow.Markdown.Mermaid.Er;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.Markdown.Mermaid.Er;

/// <summary>
/// What an <c>erDiagram</c> block says once its lines are read together: the entities and their attributes, how many of each
/// entity the other has at either end of a relationship, the subgraphs they are boxed into, and what styles them.
/// </summary>
[TestClass]
[CoversNode("er-diagram")]
public class ErDiagramTests
{
    [TestMethod, TestCategory("Unit")]
    public void AnEntityKeepsItsAttributesInTheOrderTheyAreWritten()
    {
        var entity = ErDiagram
            .Read("erDiagram\n  CAR {\n    string make\n    string plate PK \"What it is known by\"\n    int age\n  }")
            .Find("CAR")!;

        CollectionAssert.AreEqual(new[] { "string", "string", "int" },
                                  entity.Attributes.Select(attribute => attribute.Type!.Text).ToArray());
        CollectionAssert.AreEqual(new[] { "make", "plate", "age" },
                                  entity.Attributes.Select(attribute => attribute.Field!.Text).ToArray());

        Assert.AreEqual("PK", entity.Attributes[1].Keyed);
        Assert.AreEqual("What it is known by", entity.Attributes[1].Comment!.Text);
        Assert.IsNull(entity.Attributes[0].Comment, "one written without a comment says nothing");
    }

    [TestMethod, TestCategory("Unit")]
    public void AnAttributeIsEveryKeyItIsWrittenWith()
    {
        var entity = ErDiagram.Read("erDiagram\n  NAMED-DRIVER {\n    string plate PK, FK\n    string[] parts\n  }")
            .Find("NAMED-DRIVER")!;

        Assert.AreEqual("PK, FK", entity.Attributes[0].Keyed);
        Assert.AreEqual("string[]", entity.Attributes[1].Type!.Text, "a type says what brackets it was written with");
    }

    [TestMethod, TestCategory("Unit")]
    public void AnAliasIsDrawnInsteadOfTheNameWhereverItIsWritten()
    {
        var diagram = ErDiagram.Read("erDiagram\n  p[Person] {\n    string firstName\n  }\n  p ||--o| a[\"The account\"] : has");

        Assert.AreEqual("Person", diagram.Find("p")!.Said!.Text);
        Assert.AreEqual("The account", diagram.Find("a")!.Said!.Text);
    }

    [TestMethod, TestCategory("Unit")]
    public void EveryCardinalityIsReadAtEitherEnd()
    {
        var diagram = ErDiagram.Read("erDiagram\n  A |o--o| B : x\n  C ||--|| D : x\n  E }o--o{ F : x\n  G }|--|{ H : x");

        CollectionAssert.AreEqual(new[] { ErEnd.ZeroOne, ErEnd.One, ErEnd.ZeroMany, ErEnd.OneMany },
                                  diagram.Relations.Select(relation => relation.Near).ToArray());
        CollectionAssert.AreEqual(new[] { ErEnd.ZeroOne, ErEnd.One, ErEnd.ZeroMany, ErEnd.OneMany },
                                  diagram.Relations.Select(relation => relation.Far).ToArray());

        var tight = ErDiagram.Read("erDiagram\n  id1||--o| id2 : label").Relations.Single();
        Assert.AreEqual(("id1", "id2", ErEnd.One, ErEnd.ZeroOne), (tight.From, tight.To, tight.Near, tight.Far),
                        "one written with no space in it says the same");
    }

    [TestMethod, TestCategory("Unit")]
    public void OneWrittenInWordsSaysWhatTheSymbolsSay()
    {
        var diagram = ErDiagram.Read(
            "erDiagram\n  CAR 1 to zero or more NAMED-DRIVER : allows\n  PERSON many(0) optionally to 0+ NAMED-DRIVER : is");

        var allows = diagram.Relations[0];
        Assert.AreEqual(("CAR", "NAMED-DRIVER"), (allows.From, allows.To));
        Assert.AreEqual((ErEnd.One, ErEnd.ZeroMany, false), (allows.Near, allows.Far, allows.Dotted));

        var holds = diagram.Relations[1];
        Assert.AreEqual((ErEnd.ZeroMany, ErEnd.ZeroMany, true), (holds.Near, holds.Far, holds.Dotted));
    }

    [TestMethod, TestCategory("Unit")]
    public void ADottedLineIsOneThatDoesNotIdentifyWhatItReaches()
    {
        var diagram = ErDiagram.Read("erDiagram\n  A ||--|| B : x\n  C ||..|| D : x\n  E one optionally to one F : x");

        CollectionAssert.AreEqual(new[] { false, true, true }, diagram.Relations.Select(relation => relation.Dotted).ToArray());
    }

    [TestMethod, TestCategory("Unit")]
    public void AnEntityARelationshipNamesIsMadeForIt()
    {
        var diagram = ErDiagram.Read("erDiagram\n  CAR {\n    string make\n  }\n  CAR ||--o{ DRIVER : allows");

        CollectionAssert.AreEqual(new[] { "CAR", "DRIVER" }, diagram.Entities.Select(entity => entity.Id).ToArray());
        Assert.AreEqual(0, diagram.Find("DRIVER")!.Attributes.Count);
    }

    [TestMethod, TestCategory("Unit")]
    public void ANameWrittenTwiceIsOneEntity()
    {
        var diagram = ErDiagram.Read("erDiagram\n  A ||--|| B : x\n  A {\n    string name\n  }\n  A ||--|| C : y");

        CollectionAssert.AreEqual(new[] { "A", "B", "C" }, diagram.Entities.Select(entity => entity.Id).ToArray());
        Assert.AreEqual("name", diagram.Find("A")!.Attributes.Single().Field!.Text);
    }

    [TestMethod, TestCategory("Unit")]
    public void AnEntityIsBoxedIntoTheSubgraphItIsWrittenIn()
    {
        var diagram = ErDiagram.Read(
            "erDiagram\n  subgraph Outer\n    subgraph Inner\n      A\n    end\n    B\n  end\n  C");

        var outer = diagram.Within(null).Single();
        var inner = diagram.Within(outer.Key).Single();

        Assert.AreEqual("Outer", outer.Said!.Text);
        Assert.AreEqual("Inner", inner.Said!.Text);

        CollectionAssert.AreEqual(new[] { "A" }, diagram.Inside(inner.Key).Select(entity => entity.Id).ToArray());
        CollectionAssert.AreEqual(new[] { "B" }, diagram.Inside(outer.Key).Select(entity => entity.Id).ToArray());
        CollectionAssert.AreEqual(new[] { "C" }, diagram.Inside(null).Select(entity => entity.Id).ToArray());
    }

    [TestMethod, TestCategory("Unit")]
    public void ASubgraphStandsForTheWholeOfWhatWasWrittenInIt()
    {
        const string source = "erDiagram\n  subgraph Sales\n    A\n  end";
        var group = ErDiagram.Read(source).Groups.Single();

        Assert.AreEqual(source.IndexOf("subgraph", StringComparison.Ordinal), group.Whole.Start);
        Assert.AreEqual(source.Length, group.Whole.Start + group.Whole.Length);
    }

    [TestMethod, TestCategory("Unit")]
    public void AStyleLineAndAClassLineBothReachTheEntityTheyName()
    {
        var diagram = ErDiagram.Read(
            "erDiagram\n  A ||--|| B : x\n  classDef blue fill:#00f\n  classDef bold stroke-width:3px\n"
            + "  class A,B blue,bold\n  style B fill:#f00");

        Assert.AreEqual("#00f", diagram.Find("A")!.Style.Fill);
        Assert.AreEqual(3, diagram.Find("A")!.Style.StrokeWidth, "a class line gives every class it names");
        Assert.AreEqual("#f00", diagram.Find("B")!.Style.Fill, "what is written for one on its own is laid over its classes");
    }

    [TestMethod, TestCategory("Unit")]
    public void SeveralClassesGivenAtOnceAreAllLaidOn()
    {
        var entity = ErDiagram
            .Read("erDiagram\n  A:::blue,bold ||--|| B : x\n  classDef blue fill:#00f\n  classDef bold stroke-width:3px")
            .Find("A")!;

        Assert.AreEqual("#00f", entity.Style.Fill);
        Assert.AreEqual(3, entity.Style.StrokeWidth);
    }

    [TestMethod, TestCategory("Unit")]
    public void TheWayItIsLaidOutIsTheDirectionLineOrElseTheFrontMatter()
    {
        Assert.AreEqual(ErWay.Down, ErDiagram.Read("erDiagram\n  A ||--|| B : x").Way);
        Assert.AreEqual(ErWay.Right, ErDiagram.Read("erDiagram\n  direction LR\n  A ||--|| B : x").Way);

        Assert.AreEqual(ErWay.Left,
                        ErDiagram.Read("---\nconfig:\n  er:\n    layoutDirection: RL\n---\nerDiagram\n  A ||--|| B : x").Way);
        Assert.AreEqual(ErWay.Up,
                        ErDiagram.Read("---\nconfig:\n  er:\n    layoutDirection: RL\n---\nerDiagram\n  direction BT\n  A ||--|| B : x").Way,
                        "a direction line wins over the front matter");
    }

    [TestMethod, TestCategory("Unit")]
    public void TheFrontMatterSaysHowSmallAnEntityMayBe()
    {
        var config = ErDiagram
            .Read("---\nconfig:\n  er:\n    minEntityWidth: 140\n    entityPadding: 6\n    fontSize: 16\n---\nerDiagram\n  A")
            .Config;

        Assert.AreEqual(140, config.MinWidth);
        Assert.AreEqual(6, config.Padding);
        Assert.AreEqual(24, config.RowHeight, "a row follows how big the words are");
        Assert.AreEqual(ErConfig.Shortest, config.MinHeight, "and the rest are left as they are");

        var painted = ErDiagram.Read("---\nconfig:\n  er:\n    fill: honeydew\n    stroke: gray\n---\nerDiagram\n  A").Config;
        Assert.AreEqual(("honeydew", "gray"), (painted.Fill, painted.Stroke));
    }
}
