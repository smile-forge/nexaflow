using System.Linq;
using Nexaflow.Maths.Latex;
using Nexaflow.Tests.Fixtures;
using Nexaflow.Visuals.Text.Editing;
using Nexaflow.Visuals.Text.Markdown.Latex;

namespace Nexaflow.Tests.Visuals.Markdown.Latex;

/// <summary>
/// A matrix rewritten, with something more than a single letter in its cells.
///
/// <para>
/// The editor's grid used to be read off the typesetter's atoms, whose span for a command begins at the
/// command's <em>name</em>: a cell holding <c>\alpha</c> was named as <c>alpha</c>. So every rewrite of
/// a matrix — moving a column, moving a row, dragging a block — took the backslash off every command in
/// it and handed back LaTeX that no longer parsed. Nothing caught it because every grid test written
/// until now had one letter in each cell.
/// </para>
/// <para>
/// The cells come from the parse tree now, which names the whole of what was written. These are the
/// tests that say so, and every one of them fails against the old reading.
/// </para>
/// <para>
/// Needs an STA thread: the editor's grid only exists once the formula has been typeset.
/// </para>
/// </summary>
[TestClass]
[TestCategory("UI")]
[CoversNode("maths-latex-grid")]
public class MatrixRewriteTests
{
    private const double Scale = 16;

    private const string Greek = @"\begin{matrix} \alpha & \beta \\ \gamma & \delta \end{matrix}";

    [TestMethod]
    public void ACellHoldingACommandHoldsTheBackslashToo() => UiThread.Run(() =>
    {
        var grid = Grid(Greek);

        Assert.AreEqual(@"\alpha", grid.CellText(0, 0));
        Assert.AreEqual(@"\beta", grid.CellText(0, 1));
        Assert.AreEqual(@"\gamma", grid.CellText(1, 0));
        Assert.AreEqual(@"\delta", grid.CellText(1, 1));
    });

    [TestMethod]
    public void SoDoesOneHoldingAWholeConstruct() => UiThread.Run(() =>
    {
        var grid = Grid(@"\begin{matrix} \frac{1}{2} & \sqrt{x} \end{matrix}");

        Assert.AreEqual(@"\frac{1}{2}", grid.CellText(0, 0));
        Assert.AreEqual(@"\sqrt{x}", grid.CellText(0, 1));
    });

    [TestMethod]
    public void WritingAMatrixOutAgainWithoutChangingItChangesNothingThatMatters() => UiThread.Run(() =>
    {
        // The weakest thing a rewrite can be asked to do, and it did not do it: the body came back as
        // `alpha & beta \\ gamma & delta`.
        var body = Grid(Greek).Body();

        foreach (var command in new[] { @"\alpha", @"\beta", @"\gamma", @"\delta" })
            StringAssert.Contains(body, command, $"{command} lost its backslash in: {body}");
    });

    [TestMethod]
    public void AndMovingAColumnLeavesTheFormulaSomethingThatStillReads() => UiThread.Run(() =>
    {
        // The gesture as the reader makes it, end to end. What came back before was
        // `\begin{matrix} beta & alpha \\ delta & gamma \end{matrix}` — every command in the matrix
        // broken by a drag that was only supposed to reorder them.
        var layout = LatexLayout.Build(Greek, Scale);
        Assert.IsNotNull(layout);

        var grid = layout.Tree.GridAt(Greek.IndexOf(@"\alpha", System.StringComparison.Ordinal));
        Assert.IsNotNull(grid);

        var first = Column(grid, 0);
        var moved = layout.Tree.Move(first, Greek.IndexOf(@"\beta", System.StringComparison.Ordinal) + 1);
        Assert.IsNotNull(moved, "the column would not move");

        foreach (var command in new[] { @"\alpha", @"\beta", @"\gamma", @"\delta" })
            StringAssert.Contains(moved.Value.Latex, command, $"{command} did not survive the move");

        Assert.IsNotNull(LatexLayout.Build(moved.Value.Latex, Scale), $"will not typeset: {moved.Value.Latex}");
    });

    /// <summary>The spans of one column's cells, which is what carrying it hands to the tree.</summary>
    private static (int Start, int Length)[] Column(LatexGrid grid, int column) =>
        [.. Enumerable.Range(0, grid.RowCount)
            .Select(row => Cell(grid, row, column))];

    private static (int Start, int Length) Cell(LatexGrid grid, int row, int column)
    {
        // The grid reports what is written in a cell rather than where; the parse tree, which is where
        // the grid came from, reports both.
        var cells = TexGrid.In(TexParser.Parse(grid.Latex)).First();
        var cell = cells[row, column];
        return (cell.Start, cell.Length);
    }

    private static LatexGrid Grid(string latex)
    {
        var layout = LatexLayout.Build(latex, Scale);
        Assert.IsNotNull(layout, $"{latex} no longer typesets");

        var inside = TexGrid.In(TexParser.Parse(latex)).First()[0, 0].Start;
        var grid = layout.Tree.GridAt(inside);
        Assert.IsNotNull(grid, $"the editor finds no table in {latex}");

        return grid;
    }
}
