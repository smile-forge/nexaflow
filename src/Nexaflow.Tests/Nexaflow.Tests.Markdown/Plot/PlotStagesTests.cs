using Nexaflow.Markdown.Ast;
using Nexaflow.Markdown.Pipeline;
using Nexaflow.Markdown.Plot;
using Nexaflow.Markdown.Plot.Stages;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.Markdown.Plot;

/// <summary>
/// What the lines of a plot block mean together, worked out by the stages and said in the block's own nodes: which row names
/// the columns, what shape the table is, which column each cell stands in, what it reads as, which channel it feeds, and what
/// a <c>stats:</c> line reports.
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
        PlotPipeline.Of(fence).Run(PlotParser.Parse(source));

    private static IReadOnlyList<ContentNode> Rows(string source, PlotFence fence = PlotFence.Scatter) =>
        Read(source, fence).Rows();

    private static PlotCellNode? Cell(ContentNode cell) => cell as PlotCellNode;

    /// <summary>The first channel a cell feeds, or null where it feeds none.</summary>
    private static PlotAesthetic? Fed(ContentNode cell) => cell is PlotCellNode { Feeds: [var first, ..] } ? first : null;

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
        Assert.IsTrue(((PlotBlockNode)Read("      mpg   hp\nmpg   1.00  -0.78\nhp   -0.78   1.00")).Matrix);
    }

    [TestMethod]
    public void RowsTheSameWidthAsTheirHeaderAreALongList()
    {
        Assert.IsFalse(((PlotBlockNode)Read("weight  mpg\n3504  18.0")).Matrix);
    }

    [TestMethod]
    public void DownAMatrixTheLeadingCellNamesItsRow()
    {
        var rows = Rows("      mpg   hp\nmpg   1.00  -0.78\nhp   -0.78   1.00");

        Assert.AreEqual("mpg", ((PlotRowNode)rows[1]).Names);
        Assert.AreEqual("hp", ((PlotRowNode)rows[2]).Names);
        Assert.AreEqual("mpg", Cell(rows[1].Cells()[0])?.Names);
    }

    [TestMethod]
    public void ABlockWhoseSettingsWillNotReadIsLeftAsWrittenWithTheSettingMarked()
    {
        var tree = Read("geom: sideways\n\n1.2  3.4");

        Assert.IsNotInstanceOfType<PlotBlockNode>(tree, "nothing is worked out without the settings");
        Assert.IsTrue(tree.SelfAndDescendants().Any(node => node.Trouble is not null && node.Print() == "sideways"));
    }

    // ── Columns ─────────────────────────────────────────────────────────────

    [TestMethod]
    public void EveryCellKnowsWhichColumnItStandsIn()
    {
        var cells = Rows("weight  mpg  origin\n3504  18.0  USA")[1].Cells();

        CollectionAssert.AreEqual(new int?[] { 0, 1, 2 }, cells.Select(cell => Cell(cell)?.Index).ToArray());
    }

    [TestMethod]
    public void ACellKnowsWhatItsColumnIsCalled()
    {
        var cells = Rows("weight  mpg\n3504  18.0")[1].Cells();

        CollectionAssert.AreEqual(new[] { "weight", "mpg" }, cells.Select(cell => Cell(cell)?.Column).ToArray());
    }

    [TestMethod]
    public void AColumnWithNoHeaderIsStillAColumn()
    {
        // It has a place and no name, which is enough to plot it.
        var cells = Rows("1.2  3.4")[0].Cells();

        CollectionAssert.AreEqual(new int?[] { 0, 1 }, cells.Select(cell => Cell(cell)?.Index).ToArray());
        Assert.IsTrue(cells.All(cell => Cell(cell)?.Column is null));
    }

    [TestMethod]
    public void AHeadersOwnCellsStandInNoColumn()
    {
        // A name is not a value, and a header cell that said it stood in the column it names would be
        // drawn as a point.
        Assert.IsTrue(Rows("weight  mpg\n3504  18.0")[0].Cells().All(cell => Cell(cell)?.Index is null));
    }

    [TestMethod]
    public void DownAMatrixTheColumnsStartAfterTheNameCell()
    {
        var cells = Rows("      mpg   hp\nmpg   1.00  -0.78")[1].Cells();

        Assert.IsNull(Cell(cells[0])?.Index);
        Assert.AreEqual(0, Cell(cells[1])?.Index);
        Assert.AreEqual("mpg", Cell(cells[1])?.Column);
        Assert.AreEqual("hp", Cell(cells[2])?.Column);
    }

    // ── Values ──────────────────────────────────────────────────────────────

    [TestMethod]
    public void ACellThatReadsAsANumberSaysWhatItIs()
    {
        var cells = Rows("-1.5  3e-4  .5")[0].Cells();

        CollectionAssert.AreEqual(new double?[] { -1.5, 0.0003, 0.5 }, cells.Select(cell => Cell(cell)?.Number).ToArray());
    }

    [TestMethod]
    public void ACellThatIsACategorySaysNothing()
    {
        // A name is what the cell already is, so there would be nothing to record.
        var cells = Rows("region  count\nNorth  12")[1].Cells();

        Assert.IsNull(Cell(cells[0])?.Number);
        Assert.AreEqual(12, Cell(cells[1])?.Number);
    }

    [TestMethod]
    public void ACellSaidToBeSomethingPrintsAsItWasWritten()
    {
        // A bare cell stays the one run of characters it was, and a quoted one keeps its quotes.
        var cells = Rows("region  count\n\"North East\"  12")[1].Cells();

        Assert.AreEqual("\"North East\"", cells[0].Print());
        Assert.IsTrue(cells[1].IsLeaf);
        Assert.AreEqual("12", cells[1].Print());
    }

    // ── Channels ────────────────────────────────────────────────────────────

    [TestMethod]
    public void TheFirstTwoColumnsAreXAndYWithNothingWritten()
    {
        var cells = Rows("1.2  3.4")[0].Cells();

        Assert.AreEqual(PlotAesthetic.X, Fed(cells[0]));
        Assert.AreEqual(PlotAesthetic.Y, Fed(cells[1]));
    }

    [TestMethod]
    public void ASettingThatNamesAColumnMapsIt()
    {
        var cells = Rows("x: mpg\ny: weight\n\nweight  mpg\n3504  18.0")[1].Cells();

        Assert.AreEqual(PlotAesthetic.Y, Fed(cells[0]));
        Assert.AreEqual(PlotAesthetic.X, Fed(cells[1]));
    }

    [TestMethod]
    public void ASettingThatNamesNoColumnIsAConstantAndMapsNothing()
    {
        // size: 4 is the size of every mark; size: pop is the pop column. Nothing here can tell them
        // apart from the characters, which is exactly why it is settled once the columns are known.
        var cells = Rows("size: 4\n\nweight  mpg  pop\n3504  18.0  120")[1].Cells();

        Assert.AreEqual(PlotAesthetic.X, Fed(cells[0]));
        Assert.AreEqual(PlotAesthetic.Y, Fed(cells[1]));
        Assert.IsNull(Fed(cells[2]));
    }

    [TestMethod]
    public void AColumnNamedByItsPlaceIsMappedToo()
    {
        // Which is how a table with no header is spoken about.
        var cells = Rows("x: 2\ny: 1\n\n1.2  3.4")[0].Cells();

        Assert.AreEqual(PlotAesthetic.Y, Fed(cells[0]));
        Assert.AreEqual(PlotAesthetic.X, Fed(cells[1]));
    }

    [TestMethod]
    public void AColumnMayFeedSeveralChannels()
    {
        var cells = Rows("colour: region\nshape: region\n\nweight  mpg  region\n3504  18.0  North")[1].Cells();

        CollectionAssert.AreEquivalent(new[] { PlotAesthetic.Colour, PlotAesthetic.Shape }, Cell(cells[2])!.Feeds.ToArray());
    }

    [TestMethod]
    public void ABubbleFencesThirdColumnFeedsSizeAndAScattersFeedsNothing()
    {
        const string Source = "weight  mpg  pop\n3504  18.0  120";

        Assert.AreEqual(PlotAesthetic.Size, Fed(Rows(Source, PlotFence.Bubble)[1].Cells()[2]));
        Assert.IsNull(Fed(Rows(Source, PlotFence.Scatter)[1].Cells()[2]));
    }

    [TestMethod]
    public void AHeatMapsThirdColumnFeedsFill()
    {
        var cells = Rows("region  year  sales\nNorth  2024  120", PlotFence.Heatmap)[1].Cells();

        Assert.AreEqual(PlotAesthetic.Fill, Fed(cells[2]));
    }

    [TestMethod]
    public void EveryValueOfAMatrixFeedsFill()
    {
        // Across a matrix is x and down it is y, so the cell itself is the only value there is.
        var cells = Rows("      mpg   hp\nmpg   1.00  -0.78")[1].Cells();

        Assert.IsNull(Fed(cells[0]));
        Assert.AreEqual(PlotAesthetic.Fill, Fed(cells[1]));
        Assert.AreEqual(PlotAesthetic.Fill, Fed(cells[2]));
    }

    // ── What a stats line reports ──────────────────────────────────────────

    [TestMethod]
    public void WhatAStatsLineReportsIsWorkedOutFromTheNumbersWritten()
    {
        var block = (PlotBlockNode)Read("stats: r n\n\nx  y\n1  2\n2  4\n3  6\n4  8");

        Assert.IsNotNull(block.Statistic);
        Assert.AreEqual(1.0, block.Statistic.R, 1e-12);
        Assert.AreEqual(4, block.Statistic.N);
    }

    [TestMethod]
    public void EachPanelOfADividedPlotReportsItsOwn()
    {
        var block = (PlotBlockNode)Read("stats: r n\nfacet: side\n\nx  y  side\n1  2  a\n2  4  a\n3  6  a\n1  9  b\n2  5  b\n3  1  b");

        Assert.AreEqual(6, block.Statistic?.N, "every row");
        Assert.AreEqual(1.0, block.Statistics["a"].R, 1e-12);
        Assert.IsTrue(block.Statistics["b"].R < 0, "and the rows of each level on their own");
    }

    [TestMethod]
    public void NothingIsReportedUnlessTheBlockAsksForIt()
    {
        var block = (PlotBlockNode)Read("x  y\n1  2\n2  4\n3  6");

        Assert.IsNull(block.Statistic);
        Assert.AreEqual(0, block.Statistics.Count);
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
            "stats: r\nfacet: side\n\nx  y  side\n1  2  a\n2  4  b",
            "just some prose",
            "",
        ];

        foreach (var fence in Enum.GetValues<PlotFence>())
            foreach (var source in blocks)
            {
                var tree = PlotParser.Parse(source);

                foreach (var stage in PlotPipeline.Of(fence).Stages)
                {
                    tree = stage.Run(tree);
                    Assert.AreEqual(source, tree.Print(), $"{fence}: {stage.Name} changed {source}");
                }
            }
    }
}
