using System.Linq;
using System.Windows;
using Nexaflow.Tests.Fixtures;
using Nexaflow.Visuals.Text.Editing;

namespace Nexaflow.Tests.Core.Visuals.Editing;

/// <summary>
/// Coverage for <see cref="ContentSelection"/> — what a drag from one piece to another means.
///
/// <para>
/// The behaviour a grid is owed was reported plainly: drag down and it should select down, across and it
/// should select across, and corner to corner it should take the block between. That is how a sheet
/// behaves and it is what a matrix looks like, so it is what a matrix should do. None of it is a question
/// about pixels, so none of it is asked with pixels: hand-built trees, no typesetter, no window.
/// </para>
/// </summary>
[TestClass]
[CoversNode("latex-selection")]
public class ContentSelectionTests
{
    /// <summary>A 3x3 grid: rows of cells, each cell wrapping its content the way a typesetter does.</summary>
    private static LayoutNode Matrix()
    {
        var root = new LayoutNode(new Rect(0, 0, 70, 60), 0, 9, "grid", isInk: false);
        for (var r = 0; r < 3; r++)
        {
            var row = root.Add(new LayoutNode(new Rect(0, r * 20, 70, 13), r * 3, 3, "row", isInk: false));
            for (var c = 0; c < 3; c++)
            {
                var cell = row.Add(new LayoutNode(new Rect(c * 25, r * 20, 10, 13), r * 3 + c, 1, "cell", isInk: false));
                cell.Add(new LayoutNode(new Rect(c * 25, r * 20, 10, 13), r * 3 + c, 1, "char", isInk: true));
            }
        }
        return root;
    }

    /// <summary><c>\frac{x^2}{2}+y</c> — rows without columns, which is not a grid.</summary>
    private static LayoutNode Fraction()
    {
        var root = new LayoutNode(new Rect(0, 0, 100, 44), 0, 15, "row", isInk: false);

        var frac = root.Add(new LayoutNode(new Rect(0, 0, 26, 44), 0, 13, "fraction", isInk: false));
        var numerator = frac.Add(new LayoutNode(new Rect(2, 0, 20, 17), 6, 3, "script", isInk: false));
        numerator.Add(new LayoutNode(new Rect(2, 8, 11, 9), 6, 1, "char", isInk: true));
        numerator.Add(new LayoutNode(new Rect(14, 0, 7, 9), 8, 1, "char", isInk: true));
        frac.Add(new LayoutNode(new Rect(2, 20, 22, 2), 0, 0, "rule", isInk: false));
        frac.Add(new LayoutNode(new Rect(7, 30, 10, 13), 11, 1, "char", isInk: true));

        root.Add(new LayoutNode(new Rect(28, 18, 16, 13), 13, 1, "char", isInk: true));
        root.Add(new LayoutNode(new Rect(46, 18, 11, 13), 14, 1, "char", isInk: true));
        return root;
    }

    private static ILayoutNode Cell(LayoutNode grid, int offset) =>
        grid.Ink().Single(n => n.SourceStart == offset);

    // ── A grid selects like a sheet ─────────────────────────────────────────

    [TestMethod]
    public void DraggingDownAColumnSelectsTheColumn()
    {
        var grid = Matrix();

        var selection = ContentSelection.Between(grid, Cell(grid, 1), Cell(grid, 7));

        CollectionAssert.AreEqual(new[] { (1, 1), (4, 1), (7, 1) }, selection.Ranges.ToArray(),
            "a column is three cells and three ranges — it is nowhere near contiguous in the source");
    }

    [TestMethod]
    public void DraggingAcrossARowSelectsTheRow()
    {
        var grid = Matrix();

        var selection = ContentSelection.Between(grid, Cell(grid, 3), Cell(grid, 5));

        Assert.AreEqual(1, selection.Ranges.Count, "a row is contiguous, so it is one range");
        Assert.AreEqual((3, 3), selection.Ranges[0]);
    }

    [TestMethod]
    public void DraggingCornerToCornerSelectsTheBlockBetween()
    {
        // The middle four cells: rows 0-1, columns 1-2. Two runs, because each row's pair is contiguous
        // and the rows are not.
        var grid = Matrix();

        var selection = ContentSelection.Between(grid, Cell(grid, 1), Cell(grid, 5));

        CollectionAssert.AreEqual(new[] { (1, 2), (4, 2) }, selection.Ranges.ToArray());
    }

    [TestMethod]
    public void ABlockIsTheSameWhicheverCornerYouStartFrom()
    {
        var grid = Matrix();

        var forwards = ContentSelection.Between(grid, Cell(grid, 1), Cell(grid, 5));
        var backwards = ContentSelection.Between(grid, Cell(grid, 5), Cell(grid, 1));

        CollectionAssert.AreEqual(forwards.Ranges.ToArray(), backwards.Ranges.ToArray());
    }

    [TestMethod]
    public void SelectingOneCellSelectsThatCell()
    {
        var grid = Matrix();

        var selection = ContentSelection.Between(grid, Cell(grid, 4), Cell(grid, 4));

        Assert.AreEqual(1, selection.Ranges.Count);
        Assert.AreEqual((4, 1), selection.Ranges[0]);
    }

    // ── Everything else selects like a line of text ─────────────────────────

    [TestMethod]
    public void AFractionIsNotAGrid()
    {
        // Rows without columns. Dragging from the numerator to the denominator has to mean the fraction,
        // not a column of it — which is exactly the guarantee promotion gives.
        var root = Fraction();

        Assert.AreEqual(0, root.Children.First().Grid().Count, "a numerator, a rule and a denominator");

        var selection = ContentSelection.Between(root, Cell(root, 6), Cell(root, 11));

        Assert.AreEqual(1, selection.Ranges.Count);
        Assert.AreEqual((0, 13), selection.Ranges[0], "the whole fraction, braces and bar included");
    }

    [TestMethod]
    public void ARunOfTermsSelectsFromOneToTheOther()
    {
        var root = Fraction();

        var selection = ContentSelection.Between(root, Cell(root, 13), Cell(root, 14));

        Assert.AreEqual(1, selection.Ranges.Count);
        Assert.AreEqual((13, 2), selection.Ranges[0]);
    }

    [TestMethod]
    public void SelectingNothingIsNotASelection()
    {
        Assert.IsTrue(ContentSelection.Between(Fraction(), null, null).IsEmpty);
        Assert.IsTrue(ContentSelection.None.IsEmpty);
    }

    // ── The grid itself ─────────────────────────────────────────────────────

    [TestMethod]
    public void AGridIsRowsAndColumnsOfTheTree()
    {
        var cells = Matrix().Grid();

        Assert.AreEqual(3, cells.Count);
        Assert.IsTrue(cells.All(r => r.Count == 3));
        CollectionAssert.AreEqual(
            new[] { 6, 7, 8 },
            cells[2].Select(c => c.SourceStart).ToArray(),
            "and in reading order, left to right");
    }

    [TestMethod]
    public void ARaggedThingIsNotAGrid()
    {
        // Rows of different widths are a stack of rows, not a sheet, and block selection would have to
        // invent an answer for the cells that are not there.
        var root = new LayoutNode(new Rect(0, 0, 70, 40), 0, 5, "rows", isInk: false);

        var first = root.Add(new LayoutNode(new Rect(0, 0, 70, 13), 0, 3, "row", isInk: false));
        for (var c = 0; c < 3; c++)
            first.Add(new LayoutNode(new Rect(c * 25, 0, 10, 13), c, 1, "char", isInk: true));

        var second = root.Add(new LayoutNode(new Rect(0, 20, 70, 13), 3, 2, "row", isInk: false));
        for (var c = 0; c < 2; c++)
            second.Add(new LayoutNode(new Rect(c * 25, 20, 10, 13), 3 + c, 1, "char", isInk: true));

        Assert.AreEqual(0, root.Grid().Count);
    }
}
