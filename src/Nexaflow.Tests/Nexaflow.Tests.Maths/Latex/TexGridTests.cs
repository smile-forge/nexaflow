using Nexaflow.Maths.Latex;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.Maths.Latex;

/// <summary>
/// A matrix read as a table.
///
/// <para>
/// The separators are in the tree where they were written, so the shape is read rather than worked out:
/// nothing here counts <c>&amp;</c>s or clusters rectangles. That is what makes "move this column" a
/// question with an answer, and it is why every cell knows its own row and column rather than its
/// position in a flat run.
/// </para>
/// </summary>
[TestClass]
[CoversNode("maths-latex-grid")]
public class TexGridTests
{
    [TestMethod]
    public void AMatrixIsAsWideAndAsTallAsItWasWritten()
    {
        var grid = Only(@"\begin{matrix} 1 & 2 & 3 \\ a & b & c \end{matrix}");

        Assert.AreEqual("matrix", grid.Name);
        Assert.AreEqual(2, grid.RowCount);
        Assert.AreEqual(3, grid.ColumnCount);
    }

    [TestMethod]
    public void EveryCellNamesTheCharactersWrittenInIt()
    {
        const string latex = @"\begin{matrix} 1 & 2 \\ ab & cd \end{matrix}";
        var grid = Only(latex);

        Assert.AreEqual("1", Text(latex, grid[0, 0]));
        Assert.AreEqual("2", Text(latex, grid[0, 1]));
        Assert.AreEqual("ab", Text(latex, grid[1, 0]));
        Assert.AreEqual("cd", Text(latex, grid[1, 1]));
    }

    [TestMethod]
    public void WithoutTheSeparatorOrTheSpaceAroundIt()
    {
        // The cell is what is written in it. An ampersand belongs to the table rather than to either of
        // the cells it stands between, and a run carrying one would arrive somewhere else still holding
        // a piece of the matrix it came from.
        const string latex = @"\begin{matrix}   x   &   y   \end{matrix}";
        var grid = Only(latex);

        Assert.AreEqual("x", Text(latex, grid[0, 0]));
        Assert.AreEqual("y", Text(latex, grid[0, 1]));
    }

    [TestMethod]
    public void ACellWithNothingInItStandsWhereItsContentsWouldBegin()
    {
        // Not at the separator, and not nowhere. It is where a caret goes and where anything typed into
        // the cell would be typed, which is the only position that is any use to an editor.
        const string latex = @"\begin{matrix} a & & c \end{matrix}";
        var grid = Only(latex);

        var empty = grid[0, 1];
        Assert.IsTrue(empty.IsEmpty);
        Assert.AreEqual('&', latex[empty.Start], "it sits right where the next separator starts");
        Assert.IsTrue(empty.Start > grid[0, 0].End, "and after the cell before it");
    }

    [TestMethod]
    public void ARaggedMatrixIsSquaredOff()
    {
        // Which column is this, what is above it, where would a new one go — none of those mean anything
        // on a ragged grid, and the alternative is every caller handling it and most of them forgetting.
        const string latex = @"\begin{matrix} a & b & c \\ d \end{matrix}";
        var grid = Only(latex);

        Assert.AreEqual(2, grid.RowCount);
        Assert.AreEqual(3, grid.ColumnCount);

        Assert.AreEqual("d", Text(latex, grid[1, 0]));
        Assert.IsTrue(grid[1, 1].IsEmpty);
        Assert.IsTrue(grid[1, 2].IsEmpty);
    }

    [TestMethod]
    public void AndTheCellsItGainsStandAtTheEndOfTheirRow()
    {
        const string latex = @"\begin{matrix} a & b \\ c \end{matrix}";
        var grid = Only(latex);

        Assert.AreEqual(grid[1, 0].End, grid[1, 1].Start, "just past the last cell that was written");
    }

    [TestMethod]
    public void TheWholeOfItStartsAtTheBackslash()
    {
        // Not at "begin". A span that started a character in would carry `begin{matrix}…` away and leave
        // a lone backslash where the matrix had been.
        const string latex = @"x + \begin{matrix} a \end{matrix}";
        var grid = Only(latex);

        Assert.AreEqual('\\', latex[grid.Start]);
        Assert.AreEqual(latex.Length, grid.End);
    }

    [TestMethod]
    public void AMatrixInsideAMatrixAnswersAsTheOneBeingPointedInto()
    {
        const string latex = @"\begin{matrix} \begin{matrix} p \end{matrix} & q \end{matrix}";
        var root = TexParser.Parse(latex);

        var inner = TexGrid.At(root, latex.IndexOf('p'));
        Assert.IsNotNull(inner);
        Assert.AreEqual(1, inner.ColumnCount, "the inner one has a single cell");

        var outer = TexGrid.At(root, latex.IndexOf('q'));
        Assert.IsNotNull(outer);
        Assert.AreEqual(2, outer.ColumnCount, "the outer one has two");
    }

    [TestMethod]
    public void PointingSomewhereThatIsNotATableAnswersNothing()
    {
        const string latex = @"\frac{a}{b}";
        Assert.IsNull(TexGrid.At(TexParser.Parse(latex), latex.IndexOf('a')));
    }

    [TestMethod]
    public void AnArraysColumnSpecIsNotACell()
    {
        const string latex = @"\begin{array}{cc} a & b \end{array}";
        var grid = Only(latex);

        Assert.AreEqual(2, grid.ColumnCount);
        Assert.AreEqual("a", Text(latex, grid[0, 0]));
        Assert.IsTrue(grid.HasColumnSpec, "and it says so, because moving a column has to move it too");
    }

    [TestMethod]
    public void AMatrixWithoutOneSaysSo()
    {
        Assert.IsFalse(Only(@"\begin{matrix} a & b \end{matrix}").HasColumnSpec);
    }

    [TestMethod]
    public void ATrailingLineBreakAddsNoRow()
    {
        Assert.AreEqual(1, Only(@"\begin{matrix} a & b \\ \end{matrix}").RowCount);
    }

    [TestMethod]
    public void AnEmptyMatrixIsNoTableAtAll()
    {
        Assert.IsNull(TexGrid.At(TexParser.Parse(@"\begin{matrix}\end{matrix}"), 5));
    }

    [TestMethod]
    public void CasesAndAlignedBlocksAreTablesToo()
    {
        // They are laid out differently and read identically: rows of cells, separated the same way.
        Assert.AreEqual(2, Only(@"\begin{cases} u & x > 0 \\ v & x \le 0 \end{cases}").RowCount);
        Assert.AreEqual(2, Only(@"\begin{align} a &= b \\ c &= d \end{align}").RowCount);
    }

    [TestMethod]
    public void EveryCellCanBeFoundFromASpotInsideIt()
    {
        const string latex = @"\begin{matrix} 1 & 2 \\ 3 & 4 \end{matrix}";
        var grid = Only(latex);

        foreach (var cell in grid.Cells)
        {
            var found = grid.CellAt(cell.Start);
            Assert.IsNotNull(found, $"nothing at {cell.Start}");
            Assert.AreEqual((cell.Row, cell.Column), (found.Value.Row, found.Value.Column));
        }
    }

    private static TexGrid Only(string latex)
    {
        var grid = TexGrid.In(TexParser.Parse(latex)).FirstOrDefault();
        Assert.IsNotNull(grid, $"no table in {latex}");
        return grid;
    }

    private static string Text(string latex, TexCell cell) => latex.Substring(cell.Start, cell.Length);
}
