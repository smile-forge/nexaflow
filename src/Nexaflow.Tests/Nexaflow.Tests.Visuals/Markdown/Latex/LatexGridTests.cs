using System.Linq;
using Nexaflow.Tests.Fixtures;
using Nexaflow.Visuals.Text.Markdown.Latex;

namespace Nexaflow.Tests.Visuals.Markdown.Latex;

/// <summary>
/// Moving cells about in a matrix — the edits that are only meaningful because the tree knows a matrix is
/// a table.
///
/// <para>
/// The distinction being pinned here is between a splice and a structural move. A column occupies three
/// stretches of source with the matrix's own punctuation between them; taking those three stretches out
/// and putting them back somewhere else gives three terms in a row, not a column in a new place. Every
/// assertion below is about the matrix reading correctly afterwards, which is the only thing that says
/// the difference was understood.
/// </para>
///
/// Needs an STA thread for WPF's font machinery — the grid is read from a typeset formula. It opens no
/// window and takes no focus.
/// </summary>
[TestClass]
[TestCategory("UI")]
[CoversNode("latex-grid-move")]
public class LatexGridTests
{
    private const double Scale = 16;

    private const string Matrix = @"\begin{pmatrix} 1 & 2 & 3 \\ 4 & 5 & 6 \end{pmatrix}";

    private static LatexTree Tree(string latex)
    {
        var layout = LatexLayout.Build(latex, Scale);
        Assert.IsNotNull(layout, latex);
        return layout.Tree;
    }

    private static LatexGrid Grid(string latex)
    {
        var grid = Tree(latex).GridAt(latex.IndexOf('1'));
        Assert.IsNotNull(grid, "the matrix is a grid the tree can answer for");
        return grid;
    }

    // ── What the tree knows ─────────────────────────────────────────────────

    [TestMethod]
    [CoversNode("latex-grid-cells")]
    public void TheTreeKnowsWhichRowAndColumnEachCellIsIn() => UiThread.Run(() =>
    {
        // The fact everything else rests on. The parse tree used to hand back a matrix's cells as a flat
        // run of "cell", so a consumer wanting the second column had to work it out from where the cells
        // had been drawn — geometry standing in for structure the parser already had.
        var grid = Grid(Matrix);

        Assert.AreEqual(2, grid.RowCount);
        Assert.AreEqual(3, grid.ColumnCount);
        Assert.AreEqual("1", grid.CellText(0, 0));
        Assert.AreEqual("6", grid.CellText(1, 2));
    });

    [TestMethod]
    [CoversNode("latex-grid-cells")]
    public void AnEmptyCellIsStillACellWithAPlace() => UiThread.Run(() =>
    {
        // A cell with nothing written in it used to be a NullAtom: no box, and no source position. So it
        // was absent from the tree, the grid had a hole in it that nothing could name, and a matrix whose
        // first cell was empty answered as though its columns were one short.
        var grid = Grid(@"\begin{pmatrix} 1 & {} \\ {} & 4 \end{pmatrix}");

        Assert.AreEqual(2, grid.RowCount);
        Assert.AreEqual(2, grid.ColumnCount, "the empty cells are cells");
        Assert.AreEqual("1", grid.CellText(0, 0));
        Assert.AreEqual("4", grid.CellText(1, 1));
    });

    [TestMethod]
    [CoversNode("latex-placeholders")]
    public void HolesAreForWritingIn_ARenderedFormulaHasNone() => UiThread.Run(() =>
    {
        // A placeholder is an editing affordance: it says there is something still to write, and gives
        // the reader something to aim at. A formula being set to be read has no such reader, and a
        // hollow box in the middle of a finished equation is simply wrong — so the parse a renderer asks
        // for does not make them, from the same source.
        const string latex = @"\frac{}{2}";

        var editing = LatexLayout.Build(latex, Scale, placeholders: true);
        var reading = LatexLayout.Build(latex, Scale, placeholders: false);
        Assert.IsNotNull(editing);
        Assert.IsNotNull(reading);

        Assert.AreEqual(1, editing.Tree.Placeholders.Count, "the empty numerator is a hole to fill in");
        Assert.AreEqual(0, reading.Tree.Placeholders.Count, "and is nothing at all when it is only being read");
    });

    // ── Within the matrix ───────────────────────────────────────────────────

    [TestMethod]
    public void AColumnDraggedWithinTheMatrixShiftsTheOthersOver() => UiThread.Run(() =>
    {
        // Reported from the app: the columns did not shift over. They could not — the selection is three
        // stretches of source and the move was a splice, so the column arrived as its cells run together
        // wherever they were dropped.
        var latex = Matrix;
        var column = Tree(latex).GridAt(latex.IndexOf('3'));
        Assert.IsNotNull(column);

        var block = new GridBlock(0, 2, 1, 2);           // the third column, both rows
        var moved = column.WithColumnsMoved(block, before: 0);

        Assert.AreEqual("3", moved.Grid.CellText(0, 0), "it went to the front");
        Assert.AreEqual("1", moved.Grid.CellText(0, 1), "and the rest shifted over");
        Assert.AreEqual("2", moved.Grid.CellText(0, 2));
        Assert.AreEqual("6", moved.Grid.CellText(1, 0));
        Assert.AreEqual(new GridBlock(0, 0, 1, 0), moved.Landed);
    });

    [TestMethod]
    public void ARowDraggedWithinTheMatrixShiftsTheOthersDown() => UiThread.Run(() =>
    {
        var grid = Grid(Matrix);
        var moved = grid.WithRowsMoved(new GridBlock(1, 0, 1, 2), before: 0);

        Assert.AreEqual("4", moved.Grid.CellText(0, 0), "the second row is now the first");
        Assert.AreEqual("1", moved.Grid.CellText(1, 0));
    });

    [TestMethod]
    public void APartialBlockMovesItsContentsAndLeavesHolesBehind() => UiThread.Run(() =>
    {
        // Not a whole line, so there is no column or row to reorder: the contents go where they were
        // dropped, as they would on a sheet, and what they came from is left to be written in again.
        var grid = Grid(Matrix);
        var moved = grid.WithBlockMoved(new GridBlock(0, 0, 0, 1), toRow: 1, toColumn: 1);

        Assert.AreEqual("1", moved.Grid.CellText(1, 1), "the block landed where it was dropped");
        Assert.AreEqual("2", moved.Grid.CellText(1, 2));
        Assert.AreEqual("", moved.Grid.CellText(0, 0), "and the cells it came from are empty");
        Assert.AreEqual("", moved.Grid.CellText(0, 1));
        Assert.AreEqual("3", moved.Grid.CellText(0, 2), "everything else is untouched");
    });

    [TestMethod]
    public void AnEmptiedCellIsWrittenAsAHoleSoItCanBeFilledIn() => UiThread.Run(() =>
    {
        // Written as nothing, an emptied cell would be invisible and unclickable — the matrix would look
        // as though it had lost a column. Braces are what make the parser put a placeholder there.
        var grid = Grid(Matrix);
        var (latex, _) = grid.WithBlockMoved(new GridBlock(0, 0, 0, 0), toRow: 1, toColumn: 0).Grid.Render();

        StringAssert.Contains(latex, "{}", "the cell it came from is a hole in the source");
        Assert.IsNotNull(LatexLayout.Build(latex, Scale), "and what comes out still typesets");
    });

    // ── Out of the matrix ───────────────────────────────────────────────────

    [TestMethod]
    public void ABlockDraggedOutOfTheMatrixBecomesAMatrixOfItsOwn() => UiThread.Run(() =>
    {
        var grid = Grid(Matrix);
        var taken = grid.Extracted(new GridBlock(0, 0, 1, 1));

        Assert.AreEqual(@"\begin{pmatrix} 1 & 2 \\ 4 & 5 \end{pmatrix}", taken,
            "the same kind of matrix, at the size that was selected");
    });

    [TestMethod]
    public void ASingleCellDraggedOutIsJustItsContents() => UiThread.Run(() =>
    {
        // A matrix around one term is punctuation nobody asked for.
        Assert.AreEqual("5", Grid(Matrix).Extracted(new GridBlock(1, 1, 1, 1)));
    });

    [TestMethod]
    public void AColumnDraggedOutNarrowsTheMatrixItLeft() => UiThread.Run(() =>
    {
        var grid = Grid(Matrix);
        var left = grid.WithBlockTaken(new GridBlock(0, 0, 1, 0));

        Assert.AreEqual(2, left.ColumnCount, "the matrix closed up rather than keeping an empty column");
        Assert.AreEqual("2", left.CellText(0, 0));
    });

    [TestMethod]
    public void OnlyAColumnEmptiedRightThroughIsTakenAway() => UiThread.Run(() =>
    {
        // The rule is about what is left, not about what was selected: a vacated cell is a hole to write
        // in, and a whole column of holes is a column the matrix no longer has. So a block that empties
        // part of a column leaves holes there, and one that empties all of it closes the matrix up.
        var grid = Grid(Matrix);
        var left = grid.WithBlockTaken(new GridBlock(0, 0, 0, 1));   // the top two cells only

        Assert.AreEqual(3, left.ColumnCount, "neither column was emptied right through, so both stay");
        Assert.AreEqual("", left.CellText(0, 0), "and what was taken is a hole to write in");
        Assert.AreEqual("4", left.CellText(1, 0), "with the rest of the column untouched");
    });

    [TestMethod]
    public void ABlockDraggedPastTheEndOfTheMatrixLeavesIt() => UiThread.Run(() =>
    {
        // Reported from the app, and the case the first attempt got exactly backwards. Dropping to the
        // right of a matrix gives an offset past its closing brace — and when the formula *is* the
        // matrix, which is the ordinary case, that offset is still inside the matrix's span. Asking the
        // span therefore called every such drop a move within, found no cell to land in, and gave up: the
        // block arrived outside as its stretches run together, and the matrix kept its shape with the
        // cells cut out of it. Landing in a cell is what inside means.
        const string latex = @"\begin{Bmatrix} 1 & 2 & 3 \\ a & b & c \end{Bmatrix}";
        var tree = Tree(latex);

        // The first two columns of both rows — one stretch per row, which is how a grid selection comes.
        // Every offset is looked for inside the body: "Bmatrix" has an `a` in it, and a search from the
        // front finds that one.
        var body = latex.IndexOf('}') + 1;
        int At(char c) => latex.IndexOf(c, body);

        (int Start, int Length)[] block =
        [
            (At('1'), At('2') + 1 - At('1')),
            (At('a'), At('b') + 1 - At('a')),
        ];

        var moved = tree.Move(block, to: latex.Length);
        Assert.IsNotNull(moved, "dropping past the end of the matrix is a move out of it");

        StringAssert.Contains(moved.Value.Latex, @"\begin{Bmatrix} 1 & 2 \\ a & b \end{Bmatrix}",
            "the block became a matrix of its own, of the same kind and the size selected");

        var left = LatexLayout.Build(moved.Value.Latex, Scale, placeholders: true)?
            .Tree.GridAt(moved.Value.Latex.IndexOf('3'));
        Assert.IsNotNull(left, "and what it came from is still a matrix");
        Assert.AreEqual(2, left.RowCount);
        Assert.AreEqual(1, left.ColumnCount, "narrowed to the column that was left, not left holding holes");
        Assert.AreEqual("3", left.CellText(0, 0));
        Assert.AreEqual("c", left.CellText(1, 0));
    });

    [TestMethod]
    public void AWholeMatrixCarriedNowhereLeavesNothingBehind() => UiThread.Run(() =>
    {
        // Reported from the app: selecting all of a matrix and letting go where it already was produced
        // a second, empty one. Every cell had been emptied, which left the matrix standing there with a
        // single hole in it — because a grid whose cells are all gone was still being treated as a grid.
        // The whole of a matrix selected is the matrix itself being carried, and it moves as what it is.
        const string latex = @"\begin{Bmatrix} 1 & 2 \\ a & b \end{Bmatrix}";
        var tree = Tree(latex);

        var body = latex.IndexOf('}') + 1;
        int At(char c) => latex.IndexOf(c, body);

        (int Start, int Length)[] everything =
        [
            (At('1'), At('2') + 1 - At('1')),
            (At('a'), At('b') + 1 - At('a')),
        ];

        var moved = tree.Move(everything, to: latex.Length);
        var after = moved?.Latex ?? latex;

        Assert.AreEqual(1, Occurrences(after, @"\begin{Bmatrix}"),
            "there is one matrix, not the original left blank beside a copy of itself");
        Assert.IsFalse(after.Contains("{}"), "and nothing was left standing as a hole");
    });

    private static int Occurrences(string text, string what)
    {
        var count = 0;
        for (var at = text.IndexOf(what, System.StringComparison.Ordinal); at >= 0;
             at = text.IndexOf(what, at + what.Length, System.StringComparison.Ordinal))
            count++;

        return count;
    }

    // ── Merging ─────────────────────────────────────────────────────────────

    [TestMethod]
    public void ColumnsDroppedBesideAnotherMatrixJoinItAsColumns() => UiThread.Run(() =>
    {
        // Dropped *on* a cell, a block becomes that cell's contents. Dropped in the margin past the last
        // column — inside the brackets, but beyond anything drawn — it is being offered to the matrix
        // rather than to one of its cells, and joins as new columns.
        //
        // That distinction cannot be made from an offset: every position in a matrix belongs to some
        // cell as far as the source is concerned. It is a fact about where the columns were drawn.
        const string latex =
            @"\begin{pmatrix} 1 & 2 \\ 3 & 4 \end{pmatrix} + \begin{pmatrix} a \\ b \end{pmatrix}";

        var layout = LatexLayout.Build(latex, Scale);
        Assert.IsNotNull(layout);

        var target = layout.Tree.GridAt(latex.LastIndexOf('a'));
        Assert.IsNotNull(target, "the second matrix is the one being joined");

        // Just past the right edge of everything the second matrix drew, on its own baseline.
        var cells = target.SpanOf(new GridBlock(0, 0, target.RowCount - 1, target.ColumnCount - 1));
        Assert.IsNotNull(cells);
        var drawn = layout.Tree.RangeRects(cells.Value.Start, cells.Value.Length);
        var beside = new System.Windows.Point(drawn.Max(r => r.Right) + 3, drawn.Average(r => (r.Top + r.Bottom) / 2));

        var drop = layout.Tree.GridDropAt(beside);
        Assert.IsNotNull(drop, "the pointer is inside the matrix");
        Assert.IsNull(drop.Value.Cell, "but not on a cell");
        Assert.AreEqual(target.ColumnCount, drop.Value.InsertColumn, "so it is offering a column after the last");
    });

    [TestMethod]
    public void APointOnACellIsStillACell() => UiThread.Run(() =>
    {
        // The other half: a boundary is only a boundary. Over a cell, a drop still means that cell, and
        // dragging a term onto one goes on meaning what it always did.
        var layout = LatexLayout.Build(Matrix, Scale);
        Assert.IsNotNull(layout);

        var grid = layout.Tree.GridAt(Matrix.IndexOf('5'));
        Assert.IsNotNull(grid);

        var five = grid.SpanOf(new GridBlock(1, 1, 1, 1));
        Assert.IsNotNull(five);
        var box = layout.Tree.RangeRects(five.Value.Start, five.Value.Length).Single();

        var drop = layout.Tree.GridDropAt(new System.Windows.Point(
            (box.Left + box.Right) / 2, (box.Top + box.Bottom) / 2));

        Assert.IsNotNull(drop);
        Assert.AreEqual((1, 1), drop.Value.Cell, "the pointer is on the 5");
    });

    [TestMethod]
    public void ABlockJoiningAnotherMatrixLeavesTheOneItCameFrom() => UiThread.Run(() =>
    {
        // Both matrices are rewritten at once: the block is gone from one and in the other, and the
        // formula in between is untouched.
        var grid = Grid(Matrix);
        var joined = grid.WithColumnsInserted(1, grid.Contents(new GridBlock(0, 0, 1, 0)));

        Assert.AreEqual(4, joined.Grid.ColumnCount, "the matrix widened by the column it was given");
        Assert.AreEqual("1", joined.Grid.CellText(0, 1), "which went in where it was offered");
        Assert.AreEqual("2", joined.Grid.CellText(0, 2), "and the rest shifted over");
        Assert.AreEqual(new GridBlock(0, 1, 1, 1), joined.Landed);
    });

    [TestMethod]
    public void ARowJoiningAMatrixIsSquaredOffToIt() => UiThread.Run(() =>
    {
        // A matrix has one number of columns. A row arriving with fewer leaves holes at the end of it,
        // and one arriving with more is clipped — a merge cannot make a matrix ragged.
        var grid = Grid(Matrix);
        var joined = grid.WithRowsInserted(0, [["x"]]);

        Assert.AreEqual(3, joined.Grid.RowCount);
        Assert.AreEqual("x", joined.Grid.CellText(0, 0));
        Assert.AreEqual("", joined.Grid.CellText(0, 1), "the columns it did not fill are holes to write in");
        Assert.AreEqual("", joined.Grid.CellText(0, 2));
        Assert.AreEqual("1", joined.Grid.CellText(1, 0), "and what was there moved down");
    });

    // ── Through the editor's own entry point ────────────────────────────────

    [TestMethod]
    public void MovingAColumnThroughTheTreeRewritesTheWholeMatrix() => UiThread.Run(() =>
    {
        // The path a drag actually takes. What comes back has to be a formula that still typesets and
        // still reads as a matrix — the check a splice could never pass.
        var latex = Matrix;
        var tree = Tree(latex);

        // The third column: each cell on its own, which is how a grid selection arrives.
        var cells = new[] { latex.IndexOf('3'), latex.IndexOf('6') }
            .Select(at => (Start: at, Length: 1))
            .ToList();

        var moved = tree.Move(cells, to: latex.IndexOf('1'));
        Assert.IsNotNull(moved, "a column dragged to the first cell is a move");

        var after = LatexLayout.Build(moved.Value.Latex, Scale)?.Tree.GridAt(moved.Value.Latex.IndexOf('3'));
        Assert.IsNotNull(after, "and what comes back is still a matrix");
        Assert.AreEqual(2, after.RowCount);
        Assert.AreEqual(3, after.ColumnCount, "with its shape kept");
        Assert.AreEqual("3", after.CellText(0, 0), "and the column in its new place");
        Assert.AreEqual("6", after.CellText(1, 0));
    });
}
