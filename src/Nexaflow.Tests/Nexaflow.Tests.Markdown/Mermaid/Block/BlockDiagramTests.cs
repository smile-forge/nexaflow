using Nexaflow.Markdown.Mermaid;
using Nexaflow.Markdown.Mermaid.Block;
using Nexaflow.Tests.Fixtures;
using Nexaflow.Tests.Markdown.Mermaid;

namespace Nexaflow.Tests.Markdown.Mermaid.Block;

/// <summary>
/// What a <c>block-beta</c> block's tree is read back into: the grid its author laid out, the composites nested in it, the
/// links between the blocks, and what each block is styled with.
/// </summary>
[TestClass]
[CoversNode("block-ast")]
public class BlockDiagramTests
{
    [TestMethod]
    public void TheGridIsWhatItsAuthorLaidOut_ColumnsSpansAndAll()
    {
        var diagram = BlockDiagram.Of(MermaidStaged.Read("block-beta\n  columns 3\n  a[\"A label\"] b:2 c:2 d"));

        Assert.AreEqual(3, diagram.Columns);
        CollectionAssert.AreEqual(new[] { "a", "b", "c", "d" }, diagram.Items.Select(item => item.Id).ToArray());
        CollectionAssert.AreEqual(new[] { 1, 2, 2, 1 }, diagram.Items.Select(item => item.Span).ToArray());
        Assert.AreEqual("A label", diagram.Items[0].Said!.Text);
    }

    [TestMethod]
    public void AGridWithNoColumnCountIsAsWideAsItHolds()
    {
        Assert.IsNull(BlockDiagram.Of(MermaidStaged.Read("block-beta\n  a b c")).Columns, "nothing written lays them all on one row");
        Assert.IsNull(BlockDiagram.Of(MermaidStaged.Read("block-beta\n  columns auto\n  a b c")).Columns, "and so does auto");
        Assert.AreEqual(1, BlockDiagram.Of(MermaidStaged.Read("block-beta\n  columns 1\n  a b c")).Columns);
    }

    [TestMethod]
    public void ABlockSaysWhatIsWrittenOnIt_OrWhatItIsCalledWhereNothingIs()
    {
        var diagram = BlockDiagram.Of(MermaidStaged.Read("block-beta\n  db((\"DB\")) plain"));

        Assert.AreEqual("DB", diagram.Items[0].Said!.Text);
        Assert.AreEqual("plain", diagram.Items[1].Said!.Text, "which is the id itself, and is typed into where it is drawn");
    }

    [TestMethod]
    public void EveryBracketSaysTheShapeItIsDrawnAs()
    {
        Assert.AreEqual(MermaidShape.Circle, Shaped("block-beta\n  a((\"Round\"))"));
        Assert.AreEqual(MermaidShape.DoubleCircle, Shaped("block-beta\n  a(((\"Round\")))"));
        Assert.AreEqual(MermaidShape.Cylinder, Shaped("block-beta\n  a[(\"Store\")]"));
        Assert.AreEqual(MermaidShape.Hexagon, Shaped("block-beta\n  a{{\"Six\"}}"));
        Assert.AreEqual(MermaidShape.Asymmetric, Shaped("block-beta\n  a>\"Odd\"]"));
        Assert.AreEqual(MermaidShape.None, Shaped("block-beta\n  a"), "a block with no brackets is a plain box");
    }

    [TestMethod]
    public void CellsAreLeftEmptyWhereSpaceSaysSo_AsManyAsItAsksFor()
    {
        var diagram = BlockDiagram.Of(MermaidStaged.Read("block-beta\n  ida space:3 idb"));

        Assert.AreEqual(BlockKind.Space, diagram.Items[1].Kind);
        Assert.AreEqual(3, diagram.Items[1].Span);
        Assert.IsTrue(diagram.Items[1].Empty);
        Assert.IsNull(diagram.Items[1].Said, "and nothing is written in an empty cell");
    }

    [TestMethod]
    public void ABlockArrowPointsEveryWayItSays_AndRightWhereItSaysNothing()
    {
        Assert.AreEqual(BlockTowards.Down, Pointing("block-beta\n  a<[\"Down\"]>(down)"));
        Assert.AreEqual(BlockTowards.Left | BlockTowards.Right, Pointing("block-beta\n  a<[\"Across\"]>(x)"));
        Assert.AreEqual(BlockTowards.Up | BlockTowards.Down, Pointing("block-beta\n  a<[\"Up and down\"]>(y)"));
        Assert.AreEqual(BlockTowards.Left | BlockTowards.Right | BlockTowards.Down, Pointing("block-beta\n  a<[\"Three\"]>(x, down)"));

        var arrow = BlockDiagram.Of(MermaidStaged.Read("block-beta\n  a<[\"Label\"]>(right)")).Items[0];
        Assert.AreEqual(BlockKind.Arrow, arrow.Kind);
        Assert.AreEqual("Label", arrow.Said!.Text);
    }

    [TestMethod]
    public void ACompositeIsACellHoldingAGridOfItsOwn()
    {
        var diagram = BlockDiagram.Of(MermaidStaged.Read("block-beta\n  columns 3\n  a:3\n  block:group1:2\n    columns 2\n    h i j k\n  end\n  g"));

        CollectionAssert.AreEqual(new[] { "a", "group1", "g" }, diagram.Items.Select(item => item.Id).ToArray());

        var group = diagram.Items[1];
        Assert.AreEqual(BlockKind.Composite, group.Kind);
        Assert.AreEqual(2, group.Span);
        Assert.AreEqual(2, group.Columns);
        CollectionAssert.AreEqual(new[] { "h", "i", "j", "k" }, group.Items.Select(item => item.Id).ToArray());
        Assert.IsNull(group.Said, "a composite nobody labelled says nothing");
    }

    [TestMethod]
    public void CompositesNestAsDeepAsTheyAreWritten_EachOpeningInItsOwnColour()
    {
        var diagram = BlockDiagram.Of(MermaidStaged.Read("block-beta\n  block:one\n    block:two\n      a\n    end\n  end\n  block:three\n    b\n  end"));

        Assert.AreEqual("two", diagram.Items[0].Items[0].Id);
        Assert.AreEqual("a", diagram.Items[0].Items[0].Items[0].Id);
        CollectionAssert.AreEqual(new[] { 0, 1, 2 }, diagram.All.Where(item => item.Kind == BlockKind.Composite).Select(item => item.Order).ToArray(),
                                  "an outer composite opens before the one inside it, so it takes the earlier colour");
    }

    [TestMethod]
    public void ABlockWrittenTwiceIsOneBlock_AndTheSecondWritingSaysMoreAboutIt()
    {
        var diagram = BlockDiagram.Of(MermaidStaged.Read("block-beta\n  A space B\n  A[\"Said later\"] --> B((\"Round later\"))"));

        Assert.AreEqual(3, diagram.Items.Count, "A, the cell between them, and B — and no second A or B");
        Assert.AreEqual("Said later", diagram.Find("A")!.Said!.Text);
        Assert.AreEqual(MermaidShape.Circle, diagram.Find("B")!.Shape);
    }

    [TestMethod]
    public void ALinkJoinsTheBlocksEitherSideOfIt_DrawnAsItSays()
    {
        var links = BlockDiagram.Of(MermaidStaged.Read("block-beta\n  a b c d e f g h\n  a --- b\n  c --> d\n  e ==> f\n  g -.-> h")).Links;

        CollectionAssert.AreEqual(new[] { "a", "c", "e", "g" }, links.Select(link => link.From).ToArray());
        CollectionAssert.AreEqual(new[] { "b", "d", "f", "h" }, links.Select(link => link.To).ToArray());
        CollectionAssert.AreEqual(new[] { MermaidHead.None, MermaidHead.Arrow, MermaidHead.Arrow, MermaidHead.Arrow },
                                  links.Select(link => link.End).ToArray());
        Assert.AreEqual(MermaidLineStyle.Thick, links[2].Style, "a link of equals signs is drawn thick");
        Assert.AreEqual(MermaidLineStyle.Dotted, links[3].Style);
        Assert.AreEqual(MermaidLineStyle.Solid, links[1].Style);
    }

    [TestMethod]
    public void ALinkDrawsWhatEachOfItsEndsSays()
    {
        var links = BlockDiagram.Of(MermaidStaged.Read("block-beta\n  a b c d e f\n  a <--> b\n  c --x d\n  e o--o f")).Links;

        Assert.AreEqual(MermaidHead.Arrow, links[0].Start);
        Assert.AreEqual(MermaidHead.Arrow, links[0].End);
        Assert.AreEqual(MermaidHead.Cross, links[1].End);
        Assert.AreEqual(MermaidHead.Circle, links[2].Start);
        Assert.AreEqual(MermaidHead.Circle, links[2].End);
    }

    [TestMethod]
    public void ALinkSaysWhatIsWrittenOnIt()
    {
        var link = BlockDiagram.Of(MermaidStaged.Read("block-beta\n  a space:2 b\n  a -- \"X\" --> b")).Links.Single();

        Assert.AreEqual("X", link.Said!.Text);
        Assert.AreEqual(MermaidHead.Arrow, link.End);
    }

    [TestMethod]
    public void AStyleIsWhatTheClassesAndTheStyleLinesAddUpTo_TheNearestWinning()
    {
        var diagram = BlockDiagram.Of(MermaidStaged.Read("block-beta\n  a b c\n  classDef default stroke:#111\n  classDef blue fill:#6e6ce6,stroke:#333\n"
            + "  class a,b blue\n  style b fill:#bbf,stroke-dasharray: 5 5"));

        Assert.AreEqual("#111", diagram.Find("c")!.Style.Stroke, "every block starts from the default class");
        Assert.AreEqual("#6e6ce6", diagram.Find("a")!.Style.Fill);
        Assert.AreEqual("#333", diagram.Find("a")!.Style.Stroke, "which is laid over the default");
        Assert.AreEqual("#bbf", diagram.Find("b")!.Style.Fill, "a style of its own wins over its class");
        Assert.AreEqual("5 5", diagram.Find("b")!.Style.Dashes);
        Assert.AreEqual("#333", diagram.Find("b")!.Style.Stroke, "and what it says nothing about stays what the class asked");
    }

    [TestMethod]
    public void TheFrontMattersPaddingIsRead()
    {
        Assert.AreEqual(BlockConfig.Air, BlockDiagram.Of(MermaidStaged.Read("block-beta\n  a")).Config.Padding);
        Assert.AreEqual(12, BlockDiagram.Of(MermaidStaged.Read("---\nconfig:\n  block:\n    padding: 12\n---\nblock-beta\n  a")).Config.Padding);
    }

    [TestMethod]
    public void TheDocumentationsOpeningDiagramIsReadWhole()
    {
        var diagram = BlockDiagram.Of(MermaidStaged.Read(BlockGrammarTests.Intro));

        Assert.AreEqual(1, diagram.Columns);
        CollectionAssert.AreEqual(new[] { "db", "blockArrowId6", "ID", string.Empty, "D" },
                                  diagram.Items.Select(item => item.Id).ToArray());
        CollectionAssert.AreEqual(new[] { "A", "B", "C" }, diagram.Find("ID")!.Items.Select(item => item.Id).ToArray());
        Assert.AreEqual(2, diagram.Links.Count);
        Assert.AreEqual("#969", diagram.Find("B")!.Style.Fill);
        // Mermaid's block diagram has no title line of its own, so a title is the front matter's — and title is a block like any other.
        Assert.AreEqual("Where it runs", BlockDiagram.Of(MermaidStaged.Read("---\ntitle: Where it runs\n---\nblock-beta\n  a")).Block.Title?.Text);
        Assert.AreEqual(3, BlockDiagram.Of(MermaidStaged.Read("block-beta\n  title Where it runs")).Items.Count(item => item.Id.Length > 0 && item.Id != "title"));
    }

    private static MermaidShape Shaped(string source) => BlockDiagram.Of(MermaidStaged.Read(source)).Items[0].Shape;

    private static BlockTowards Pointing(string source) => BlockDiagram.Of(MermaidStaged.Read(source)).Items[0].Towards;
}
