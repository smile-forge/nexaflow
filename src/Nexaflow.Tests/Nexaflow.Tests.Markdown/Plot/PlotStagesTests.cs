using Nexaflow.Markdown.Ast;
using Nexaflow.Markdown.Pipeline;
using Nexaflow.Markdown.Plot;
using Nexaflow.Markdown.Plot.Stages;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.Markdown.Plot;

/// <summary>
/// What the lines of a plot block mean together, worked out by the stages and hung under the pieces they
/// are about: which row names the columns, what shape the table is, which column each cell stands in,
/// what it reads as, and which channel it feeds.
///
/// <para>
/// None of it is in the characters of one line, which is why none of it is the parser's — and every one
/// of these answers changes as the next line is typed.
/// </para>
/// </summary>
[TestClass]
[CoversNode("correlation-plots-reading")]
public class PlotStagesTests
{
    private static ContentNode Read(string source, PlotFence fence = PlotFence.Scatter) =>
        PlotPipeline.Read(source, fence);

    private static IReadOnlyList<ContentNode> Rows(string source, PlotFence fence = PlotFence.Scatter) =>
        Read(source, fence).Rows();

    // ── The shape of the table ──────────────────────────────────────────────

    [TestMethod]
    public void TheFirstRowIsTheHeaderWhenNoCellOfItIsANumber()
    {
        var rows = Rows("weight  mpg\n3504  18.0");

        Assert.IsTrue(rows[0].IsHeader());
        Assert.IsFalse(rows[1].IsHeader());
    }

    [TestMethod]
    public void ATableOfNumbersHasNoHeaderAtAll()
    {
        // The smallest plot there is: two columns of numbers and nothing written.
        Assert.IsFalse(Rows("1.2  3.4\n2.5  5.1")[0].IsHeader());
    }

    [TestMethod]
    public void HeaderSettlesTheCaseTheShapeCannotReach()
    {
        // A table of categories whose first row is words like the rest.
        Assert.IsFalse(Rows("header: false\nNorth  East\nSouth  West")[0].IsHeader());

        // And columns a reader wants named even though the first row is numbers.
        Assert.IsTrue(Rows("header: true\n1  2\n3  4")[0].IsHeader());
    }

    [TestMethod]
    public void AHeaderWithEveryRowOneCellWiderIsAMatrix()
    {
        var tree = Read("      mpg   hp\nmpg   1.00  -0.78\nhp   -0.78   1.00");

        Assert.AreEqual(ResolveShape.Matrix, tree.Said(PlotRoles.Form));
    }

    [TestMethod]
    public void RowsTheSameWidthAsTheirHeaderAreALongList()
    {
        var tree = Read("weight  mpg\n3504  18.0");

        Assert.AreEqual(ResolveShape.Long, tree.Said(PlotRoles.Form));
    }

    [TestMethod]
    public void DownAMatrixTheLeadingCellNamesItsRow()
    {
        var rows = Rows("      mpg   hp\nmpg   1.00  -0.78\nhp   -0.78   1.00");

        Assert.AreEqual("mpg", rows[1].Said(PlotRoles.Names));
        Assert.AreEqual("hp", rows[2].Said(PlotRoles.Names));
        Assert.AreEqual("mpg", rows[1].Cells()[0].Said(PlotRoles.Names));
    }

    // ── Columns ─────────────────────────────────────────────────────────────

    [TestMethod]
    public void EveryCellKnowsWhichColumnItStandsIn()
    {
        var cells = Rows("weight  mpg  origin\n3504  18.0  USA")[1].Cells();

        CollectionAssert.AreEqual(new[] { "0", "1", "2" },
                                  cells.Select(cell => cell.Said(PlotRoles.Index)).ToArray());
    }

    [TestMethod]
    public void ACellKnowsWhatItsColumnIsCalled()
    {
        var cells = Rows("weight  mpg\n3504  18.0")[1].Cells();

        CollectionAssert.AreEqual(new[] { "weight", "mpg" },
                                  cells.Select(cell => cell.Said(PlotRoles.Column)).ToArray());
    }

    [TestMethod]
    public void AColumnWithNoHeaderIsStillAColumn()
    {
        // It has a place and no name, which is enough to plot it.
        var cells = Rows("1.2  3.4")[0].Cells();

        CollectionAssert.AreEqual(new[] { "0", "1" },
                                  cells.Select(cell => cell.Said(PlotRoles.Index)).ToArray());

        Assert.IsTrue(cells.All(cell => cell.Said(PlotRoles.Column) is null));
    }

    [TestMethod]
    public void AHeadersOwnCellsStandInNoColumn()
    {
        // A name is not a value, and a header cell that said it stood in the column it names would be
        // drawn as a point.
        Assert.IsTrue(Rows("weight  mpg\n3504  18.0")[0].Cells()
                          .All(cell => cell.Said(PlotRoles.Index) is null));
    }

    [TestMethod]
    public void DownAMatrixTheColumnsStartAfterTheNameCell()
    {
        var cells = Rows("      mpg   hp\nmpg   1.00  -0.78")[1].Cells();

        Assert.IsNull(cells[0].Said(PlotRoles.Index));
        Assert.AreEqual("0", cells[1].Said(PlotRoles.Index));
        Assert.AreEqual("mpg", cells[1].Said(PlotRoles.Column));
        Assert.AreEqual("hp", cells[2].Said(PlotRoles.Column));
    }

    // ── Values ──────────────────────────────────────────────────────────────

    [TestMethod]
    public void ACellThatReadsAsANumberSaysWhatItIs()
    {
        var cells = Rows("-1.5  3e-4  .5")[0].Cells();

        CollectionAssert.AreEqual(new[] { "-1.5", "0.0003", "0.5" },
                                  cells.Select(cell => cell.Said(PlotRoles.Number)).ToArray());
    }

    [TestMethod]
    public void ACellThatIsACategorySaysNothing()
    {
        // A name is what the cell already is, so there would be nothing to record.
        var cells = Rows("region  count\nNorth  12")[1].Cells();

        Assert.IsNull(cells[0].Said(PlotRoles.Number));
        Assert.AreEqual("12", cells[1].Said(PlotRoles.Number));
    }

    // ── Channels ────────────────────────────────────────────────────────────

    [TestMethod]
    public void TheFirstTwoColumnsAreXAndYWithNothingWritten()
    {
        var cells = Rows("1.2  3.4")[0].Cells();

        Assert.AreEqual("x", cells[0].Said(PlotRoles.Aesthetic));
        Assert.AreEqual("y", cells[1].Said(PlotRoles.Aesthetic));
    }

    [TestMethod]
    public void ASettingThatNamesAColumnMapsIt()
    {
        var cells = Rows("x: mpg\ny: weight\n\nweight  mpg\n3504  18.0")[1].Cells();

        Assert.AreEqual("y", cells[0].Said(PlotRoles.Aesthetic));
        Assert.AreEqual("x", cells[1].Said(PlotRoles.Aesthetic));
    }

    [TestMethod]
    public void ASettingThatNamesNoColumnIsAConstantAndMapsNothing()
    {
        // size: 4 is the size of every mark; size: pop is the pop column. Nothing here can tell them
        // apart from the characters, which is exactly why it is settled once the columns are known.
        var cells = Rows("size: 4\n\nweight  mpg  pop\n3504  18.0  120")[1].Cells();

        Assert.AreEqual("x", cells[0].Said(PlotRoles.Aesthetic));
        Assert.AreEqual("y", cells[1].Said(PlotRoles.Aesthetic));
        Assert.IsNull(cells[2].Said(PlotRoles.Aesthetic));
    }

    [TestMethod]
    public void AColumnNamedByItsPlaceIsMappedToo()
    {
        // Which is how a table with no header is spoken about.
        var cells = Rows("x: 2\ny: 1\n\n1.2  3.4")[0].Cells();

        Assert.AreEqual("y", cells[0].Said(PlotRoles.Aesthetic));
        Assert.AreEqual("x", cells[1].Said(PlotRoles.Aesthetic));
    }

    [TestMethod]
    public void ABubbleFencesThirdColumnFeedsSizeAndAScattersFeedsNothing()
    {
        const string Source = "weight  mpg  pop\n3504  18.0  120";

        Assert.AreEqual("size", Rows(Source, PlotFence.Bubble)[1].Cells()[2].Said(PlotRoles.Aesthetic));
        Assert.IsNull(Rows(Source, PlotFence.Scatter)[1].Cells()[2].Said(PlotRoles.Aesthetic));
    }

    [TestMethod]
    public void AHeatMapsThirdColumnFeedsFill()
    {
        var cells = Rows("region  year  sales\nNorth  2024  120", PlotFence.Heatmap)[1].Cells();

        Assert.AreEqual("fill", cells[2].Said(PlotRoles.Aesthetic));
    }

    [TestMethod]
    public void EveryValueOfAMatrixFeedsFill()
    {
        // Across a matrix is x and down it is y, so the cell itself is the only value there is.
        var cells = Rows("      mpg   hp\nmpg   1.00  -0.78")[1].Cells();

        Assert.IsNull(cells[0].Said(PlotRoles.Aesthetic));
        Assert.AreEqual("fill", cells[1].Said(PlotRoles.Aesthetic));
        Assert.AreEqual("fill", cells[2].Said(PlotRoles.Aesthetic));
    }

    // ── The pipeline's own rule ─────────────────────────────────────────────

    [TestMethod]
    public void EveryStageLeavesTheSourceAlone()
    {
        string[] blocks =
        [
            "1.2  3.4\n2.5  5.1",
            "weight  mpg\n3504  18.0\n2372  24.0",
            "x: mpg\nsize: 4\n\nweight  mpg  pop\n3504  18.0  120",
            "      mpg   hp\nmpg   1.00  -0.78\nhp   -0.78   1.00",
            "header: false\nNorth  East",
            "# a note\n\nx: weight\n\n1 2",
            "just some prose",
            "",
        ];

        foreach (var fence in Enum.GetValues<PlotFence>())
            foreach (var source in blocks)
                Assert.AreEqual(source, Read(source, fence).Print(), $"{fence}: {source}");
    }
}
