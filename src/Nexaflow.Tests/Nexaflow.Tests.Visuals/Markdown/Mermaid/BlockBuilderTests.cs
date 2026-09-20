using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using Nexaflow.Markdown.Mermaid;
using Nexaflow.Tests.Fixtures;
using Nexaflow.Visuals.Text.Editing;
using Nexaflow.Visuals.Text.Markdown;
using Nexaflow.Visuals.Text.Markdown.Mermaid;
using Nexaflow.Visuals.Text.Markdown.Mermaid.Block;

namespace Nexaflow.Tests.Visuals.Markdown.Mermaid;

/// <summary>
/// A <c>block-beta</c> block drawn on the shared layout tree: the grid its author laid out, the composites holding grids of
/// their own, the block arrows, and the links over all of it — each block standing for what was written for it.
/// </summary>
[TestClass]
[TestCategory("UI")]
[CoversNode("block")]
public class BlockBuilderTests : MermaidBuilderContract
{
    private const string Parts =
        "block-beta\n  columns 1\n  db((\"DB\"))\n  arrow<[\"&nbsp;\"]>(down)\n  block:ID\n    A\n    B[\"A wide one in the middle\"]\n    C\n"
        + "  end\n  space\n  D\n  ID --> D\n  C --> D\n  style B fill:#969,stroke:#333,stroke-width:4px";

    private const string Shapes =
        "block-beta\n  columns 4\n  id1[\"Square\"] id2(\"Round\") id3([\"Stadium\"]) id4[[\"Subroutine\"]]\n"
        + "  id5[(\"Database\")] id6((\"Circle\")) id7>\"Asymmetric\"] id8{\"Rhombus\"}\n"
        + "  id9{{\"Hexagon\"}} id10[/\"Lean right\"/] id11[\\\"Lean left\"\\] id12[/\"Christmas\"\\]\n"
        + "  id13[\\\"Go shopping\"/] id14(((\"Double circle\")))";

    private const string Nested = "block-beta\n  block:one[\"Outer\"]\n    columns 1\n    a b\n  end\n  c";

    public override MermaidDiagram Diagram => MermaidDiagram.Block;

    protected override IEnumerable<(string What, string Source)> Drawn =>
    [
        ("the parts of a system", Parts),
        ("a grid of columns", "block-beta\n  columns 3\n  a[\"A label\"] b:2 c:2 d"),
        ("every shape there is", Shapes),
        ("arrows every way they point",
            "block-beta\n  columns 4\n  a<[\"R\"]>(right) b<[\"L\"]>(left) c<[\"U\"]>(up) d<[\"D\"]>(down)\n"
            + "  e<[\"X\"]>(x) f<[\"Y\"]>(y) g<[\"XD\"]>(x, down) h"),
        ("composites nested in composites", Nested),
        ("cells left empty", "block-beta\n  columns 3\n  a space b\n  c   d   e"),
        ("links of every thickness and a label", "block-beta\n  a b c d e f\n  a --- b\n  c -- \"why\" --> d\n  e ==> f"),
        ("classes and styles",
            "block-beta\n  a b\n  a-->b\n  classDef blue fill:#6e6ce6,stroke:#333,stroke-width:4px;\n  class a blue\n"
            + "  style b fill:#bbf,stroke:#f66,color:#fff,stroke-dasharray: 5 5"),
        ("a label long enough to wrap", "block-beta\n  columns 1\n  wide[\"A label with a good deal more in it than a word or two\"]\n  short"),
        ("a padding of its own", "---\nconfig:\n  block:\n    padding: 16\n---\nblock-beta\n  columns 2\n  a b c d"),
        ("still being written", "block-beta\n  columns \n  a[\"\"]\n  b --> "),
        ("what nobody means to write", "block-beta\n  a\n  end\n  style nowhere fill:#969"),
        ("nothing to draw", "block-beta"),
    ];

    [TestMethod]
    public void TheGridIsLaidOutInTheColumnsItSays() => UiThread.Run(() =>
    {
        var cells = Pieces(Build("block-beta\n  columns 3\n  a b c d"), BlockPiece.Block).Select(piece => piece.Bounds).ToList();

        Assert.AreEqual(4, cells.Count);
        Assert.AreEqual(cells[0].Top, cells[1].Top, 0.01, "the first three share a row");
        Assert.AreEqual(cells[0].Top, cells[2].Top, 0.01);
        Assert.IsTrue(cells[3].Top > cells[0].Bottom, "and the fourth wraps to the next");
        Assert.AreEqual(cells[0].Left, cells[3].Left, 0.01, "under the first of them");
        Assert.AreEqual(cells[0].Width, cells[1].Width, 0.01, "every cell is the same size");
        Assert.IsTrue(cells[1].Left > cells[0].Right, "and they stand apart");
    });

    [TestMethod]
    public void ABlockSpanningColumnsIsThatManyCellsWide() => UiThread.Run(() =>
    {
        var cells = Pieces(Build("block-beta\n  columns 4\n  a b:2 c"), BlockPiece.Block).Select(piece => piece.Bounds).ToList();
        var air = cells[1].Left - cells[0].Right;

        Assert.AreEqual((cells[0].Width * 2) + air, cells[1].Width, 0.01, "two cells and the air between them");
        Assert.AreEqual(cells[1].Right + air, cells[2].Left, 0.01, "and what follows it starts after all of that");
    });

    [TestMethod]
    public void ACompositesBlocksAreDrawnInsideIt_WithWhatIsWrittenOnItAtTheTop() => UiThread.Run(() =>
    {
        var laid = Build(Nested);
        var composite = Pieces(laid, BlockPiece.Composite).Single();
        var inside = Pieces(laid, BlockPiece.Block).Where(piece => piece.Ancestors().Any(over => over.Kind == BlockPiece.Composite)).ToList();

        CollectionAssert.AreEqual(new[] { "a", "b" }, inside.Select(piece => Written(Nested, piece.Part)).ToArray(),
                                  "the composite's own blocks hang off it, and c does not");

        foreach (var block in inside)
            Assert.IsTrue(Holds(composite.Bounds, block.Bounds), $"{block.Bounds} sits inside {composite.Bounds}");

        var said = Said(Pieces(laid, BlockPiece.Holding).Single()).Single();
        Assert.AreEqual("Outer", said.Words!.Glyphs.Text);
        Assert.IsTrue(said.Bounds.Top < inside[0].Bounds.Top, "what is written on it is above the grid inside it");
    });

    [TestMethod]
    public void ACellLeftEmptyIsDrawnAsNothing_AndStillTakesItsPlace() => UiThread.Run(() =>
    {
        var cells = Pieces(Build("block-beta\n  columns 3\n  a space b"), BlockPiece.Block).Select(piece => piece.Bounds).ToList();

        Assert.AreEqual(2, cells.Count, "nothing is drawn for the cell left empty");
        Assert.IsTrue(cells[1].Left > cells[0].Right + cells[0].Width, $"and what follows it is a cell further along: {cells[0]} then {cells[1]}");
    });

    [TestMethod]
    public void ALinkRunsFromOneBlocksEdgeToTheOthers() => UiThread.Run(() =>
    {
        const string source = "block-beta\n  columns 3\n  a space b\n  a --> b";

        var laid = Build(source);
        var cells = Pieces(laid, BlockPiece.Block).Select(piece => piece.Bounds).ToList();
        var link = Pieces(laid, BlockPiece.Link).Single();

        Assert.AreEqual("-->", Written(source, link.Part), "the line stands for the link that was written, not for what it joins");
        Assert.IsTrue(link.Bounds.Left >= cells[0].Right - 1, $"the link starts at the edge of the first: {link.Bounds.Left} over {cells[0].Right}");
        Assert.IsTrue(link.Bounds.Right <= cells[1].Left + 1, $"and stops at the edge of the second: {link.Bounds.Right} under {cells[1].Left}");
    });

    [TestMethod]
    public void WhatIsWrittenOnALinkIsDrawnOverTheMiddleOfIt() => UiThread.Run(() =>
    {
        var laid = Build("block-beta\n  columns 4\n  a space:2 b\n  a -- \"why\" --> b");
        var link = Pieces(laid, BlockPiece.Link).Single().Bounds;
        var said = Pieces(laid, BlockPiece.Label).Single();

        Assert.AreEqual("why", Said(said).Single().Words!.Glyphs.Text);
        Assert.AreEqual((link.Left + link.Right) / 2, (said.Bounds.Left + said.Bounds.Right) / 2, 1);
    });

    [TestMethod]
    public void WhatIsWrittenOnABlockIsTheCharactersWritten_OrItsIdWhereNothingIs() => UiThread.Run(() =>
    {
        const string source = "block-beta\n  db((\"DB\")) plain";

        var words = Said(Pieces(Build(source), BlockPiece.Blocks).Single()).ToList();

        CollectionAssert.AreEqual(new[] { "DB", "plain" }, words.Select(piece => piece.Words!.Glyphs.Text).ToArray());
        CollectionAssert.AreEqual(new[] { "DB", "plain" }, words.Select(piece => Written(source, piece.Part)).ToArray(),
                                  "each typed into where it is drawn");
    });

    [TestMethod]
    public void TheStyleWrittenForABlockIsWhatItIsDrawnIn() => UiThread.Run(() =>
    {
        var blocks = Pieces(Build("block-beta\n  a b\n  classDef blue fill:#6e6ce6\n  class a blue\n  style b fill:#bbf,stroke:#f66,stroke-width:3"),
                            BlockPiece.Block).ToList();

        Assert.AreEqual(Color.FromRgb(0x6E, 0x6C, 0xE6), Filled(blocks[0]), "the class it is given");
        Assert.AreEqual(Color.FromRgb(0xBB, 0xBB, 0xFF), Filled(blocks[1]), "and the style written for it");

        var outline = Marks(blocks[1])[0];
        Assert.AreEqual(Color.FromRgb(0xFF, 0x66, 0x66), ((SolidColorBrush)outline.Stroke!).Color);
        Assert.AreEqual(3, outline.Thickness);
    });

    [TestMethod]
    public void ABlockArrowIsDrawnTheWayItPoints() => UiThread.Run(() =>
    {
        // The same thing written on each of them, so each is drawn in a cell of the same size and the four can be compared.
        var across = Drawing("block-beta\n  a<[\"X\"]>(right)");
        var down = Drawing("block-beta\n  a<[\"X\"]>(down)");
        var both = Drawing("block-beta\n  a<[\"X\"]>(y)");
        var three = Drawing("block-beta\n  a<[\"X\"]>(x, down)");

        Assert.IsTrue(across.Width > across.Height, $"an arrow pointing right lies across its cell: {across}");
        Assert.IsTrue(down.Height > down.Width, $"and one pointing down stands up it: {down}");
        Assert.AreEqual(down.Height, both.Height, 0.01, "one pointing up and down reaches the same two edges, with a head at each of them");
        Assert.IsTrue(three.Width >= across.Width && three.Height >= down.Height, $"and one pointing three ways reaches three of them: {three}");
    });

    [TestMethod]
    public void EveryShapeABlockIsWrittenInIsDrawnAsItsOwn() => UiThread.Run(() =>
    {
        var drawn = Pieces(Build(Shapes), BlockPiece.Block).Select(Outlined).ToList();

        Assert.AreEqual(14, drawn.Count, "the documentation shows fourteen");
        Assert.AreEqual(14, drawn.Distinct().Count(),
                        "and no two of them are drawn the same: "
                        + string.Join("\n", drawn.GroupBy(said => said).Where(same => same.Count() > 1).Select(same => same.Key)));
    });

    /// <summary>What a block's shape comes to as a path, which is the only thing that tells two shapes of the same size apart.</summary>
    private static string Outlined(Piece piece) =>
        string.Join("|", Marks(piece).Select(mark => PathGeometry.CreateFromGeometry(mark.Shape).ToString(CultureInfo.InvariantCulture)));

    private static Laid Build(string source, double room = 900) =>
        BlockBuilder.Build(EditState.For(source), new DiagramLaying(MarkdownPalette.Dark, 1.0, room));

    /// <summary>The words drawn under a piece, in the order they were drawn.</summary>
    private static IEnumerable<Piece> Said(Piece piece) =>
        piece.SelfAndDescendants().Where(part => part.Kind == MermaidPiece.Words && part.Words is not null);

    private static IReadOnlyList<GeometryMark> Marks(Piece piece) =>
        [.. piece.SelfAndDescendants().First(part => part.Kind == MermaidPiece.Shape).Marks.ToArray().OfType<GeometryMark>()];

    private static Color? Filled(Piece piece) => Fill(piece.SelfAndDescendants().First(part => part.Kind == MermaidPiece.Shape));

    private static Rect Drawing(string source) => Marks(Pieces(Build(source), BlockPiece.Arrow).Single())[0].Shape.Bounds;

    private static bool Holds(Rect over, Rect inner) =>
        inner.Left >= over.Left - 1 && inner.Right <= over.Right + 1 && inner.Top >= over.Top - 1 && inner.Bottom <= over.Bottom + 1;

    [TestMethod]
    public void ACompositeDoesNotStandWhereALinkIsDrawnOverIt() => UiThread.Run(() =>
    {
        var laid = Build("block-beta\n  block:one[\"Outer\"]\n    columns 2\n    a b\n  end\n  a -- \"why\" --> b");
        var said = Pieces(laid, BlockPiece.Label).Single();
        var holding = Pieces(laid, BlockPiece.Holding).Single();
        var at = Middle(said.Bounds);

        Assert.IsTrue(holding.Bounds.Contains(at), "what is written on the link sits over the composite");
        Assert.IsFalse(Stands(holding, at), "and the composite does not stand there, so a press there means the link");
    });

    /// <summary>Whether a piece's shape stands at a point, which is where a press on it lands rather than on what is under it.</summary>
    private static bool Stands(Piece piece, Point at)
    {
        var shift = piece.Offset;
        foreach (var over in piece.Ancestors()) shift += over.Offset;

        return piece.SelfAndDescendants().First(part => part.Kind == MermaidPiece.Shape).Region?.FillContains(at - shift) == true;
    }
}
