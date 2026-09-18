using Nexaflow.Markdown.Plot;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.Markdown.Plot;

/// <summary>
/// What a plot block's settings come to: every key read as what it takes, the fence deciding what was
/// not written, and a value the key cannot take stopping the block rather than being guessed at.
/// </summary>
[TestClass]
[CoversNode("correlation-plots-block-syntax")]
public class PlotReaderTests
{
    private static PlotSettings Read(string source, PlotFence fence = PlotFence.Scatter)
    {
        Assert.IsTrue(PlotReader.TrySettings(PlotParser.Parse(source), fence, out var settings, out var error),
                      error);

        return settings!;
    }

    private static string Refused(string source, PlotFence fence = PlotFence.Scatter)
    {
        Assert.IsFalse(PlotReader.TrySettings(PlotParser.Parse(source), fence, out _, out var error),
                       $"`{source}` should not read.");

        Assert.IsNotNull(error);
        return error!;
    }

    [TestMethod]
    public void ABlockWithNoSettingsTakesTheDefaults()
    {
        var read = Read("1 2\n3 4");

        Assert.AreEqual(PlotSettings.Default with { Geom = PlotGeom.Point }, read);
    }

    [TestMethod]
    public void EverySettingIsRead()
    {
        var read = Read("""
                        title: Fuel economy
                        subtitle: 1974 cars
                        caption: from mtcars
                        xTitle: Weight
                        yTitle: MPG
                        legendTitle: Origin
                        x: weight
                        y: mpg
                        colour: origin
                        fill: count
                        size: pop
                        shape: kind
                        alpha: share
                        label: name
                        group: maker
                        geom: tile
                        legend: bottom
                        sizeRange: 6 30
                        palette: #e06c75 #61afef
                        header: false
                        width: 600
                        height: 400

                        1 2
                        """);

        Assert.AreEqual("Fuel economy", read.Title);
        Assert.AreEqual("1974 cars", read.Subtitle);
        Assert.AreEqual("from mtcars", read.Caption);
        Assert.AreEqual("Weight", read.XTitle);
        Assert.AreEqual("MPG", read.YTitle);
        Assert.AreEqual("Origin", read.LegendTitle);

        Assert.AreEqual("weight", read.X);
        Assert.AreEqual("mpg", read.Y);
        Assert.AreEqual("origin", read.Colour);
        Assert.AreEqual("count", read.Fill);
        Assert.AreEqual("pop", read.Size);
        Assert.AreEqual("kind", read.Shape);
        Assert.AreEqual("share", read.Alpha);
        Assert.AreEqual("name", read.Label);
        Assert.AreEqual("maker", read.Group);

        Assert.AreEqual(PlotGeom.Tile, read.Geom);
        Assert.AreEqual(PlotLegend.Bottom, read.Legend);
        Assert.AreEqual(6, read.MinSize);
        Assert.AreEqual(30, read.MaxSize);
        CollectionAssert.AreEqual(new[] { "#e06c75", "#61afef" }, read.Palette?.ToArray());
        Assert.AreEqual(false, read.Header);
        Assert.AreEqual(600, read.Width);
        Assert.AreEqual(400, read.Height);
    }

    [TestMethod]
    public void ColourIsSpeltEitherWay()
    {
        Assert.AreEqual("origin", Read("color: origin\n1 2").Colour);
        Assert.AreEqual("origin", Read("colour: origin\n1 2").Colour);
    }

    [TestMethod]
    public void AKeyMayBeWrittenWithHyphensOrInAnyCase()
    {
        Assert.AreEqual("Weight", Read("x-title: Weight\n1 2").XTitle);
        Assert.AreEqual("Weight", Read("XTITLE: Weight\n1 2").XTitle);
    }

    [TestMethod]
    public void TheFenceDecidesTheGeomAndTheThirdChannel()
    {
        Assert.AreEqual(PlotGeom.Point, Read("1 2", PlotFence.Scatter).Geom);
        Assert.IsNull(Read("1 2", PlotFence.Scatter).Third);

        Assert.AreEqual(PlotGeom.Point, Read("1 2", PlotFence.Bubble).Geom);
        Assert.AreEqual(PlotAesthetic.Size, Read("1 2", PlotFence.Bubble).Third);

        Assert.AreEqual(PlotGeom.Tile, Read("1 2", PlotFence.Heatmap).Geom);
        Assert.AreEqual(PlotAesthetic.Fill, Read("1 2", PlotFence.Heatmap).Third);

        Assert.AreEqual(PlotGeom.Density2d, Read("1 2", PlotFence.Density2d).Geom);
        Assert.IsNull(Read("1 2", PlotFence.Density2d).Third);
    }

    [TestMethod]
    public void GeomOverridesTheFence_AndTheThirdChannelFollowsTheGeom()
    {
        // A heat map fence asked to draw points is a scatter plot, third column and all.
        Assert.AreEqual(PlotGeom.Point, Read("geom: point\n1 2", PlotFence.Heatmap).Geom);
        Assert.IsNull(Read("geom: point\n1 2", PlotFence.Heatmap).Third);

        // And a scatter fence asked for tiles needs a fill, whatever the fence was called.
        Assert.AreEqual(PlotAesthetic.Fill, Read("geom: tile\n1 2", PlotFence.Scatter).Third);
    }

    [TestMethod]
    public void SizeRangeIsTwoNumbersAndTakesThemInEitherOrder()
    {
        var read = Read("sizeRange: 30 6\n1 2");

        Assert.AreEqual(6, read.MinSize);
        Assert.AreEqual(30, read.MaxSize);
    }

    [TestMethod]
    public void APaletteIsSplitOnSpaceOrCommasAlike()
    {
        CollectionAssert.AreEqual(Read("palette: red green blue\n1 2").Palette?.ToArray(),
                                  Read("palette: red, green, blue\n1 2").Palette?.ToArray());
    }

    [TestMethod]
    public void ASettingLeftBlankIsNotWrittenAtAll()
    {
        // Which is what a reader has while they are still typing it.
        Assert.IsNull(Read("title:\n1 2").Title);
        Assert.AreEqual(PlotSettings.Default.Legend, Read("legend:\n1 2").Legend);
    }

    // ── What stops the block ────────────────────────────────────────────────

    [TestMethod]
    public void AValueThatIsNotANumberStopsTheBlock()
    {
        StringAssert.Contains(Refused("width: wide\n1 2"), "is not a number");
    }

    [TestMethod]
    public void ANumberOutsideWhatItsSettingTakesStopsTheBlock()
    {
        StringAssert.Contains(Refused("width: 99999\n1 2"), "is outside");
    }

    [TestMethod]
    public void AValueThatIsNoneOfTheFewASettingTakesSaysWhichTheyAre()
    {
        var error = Refused("geom: sunburst\n1 2");

        StringAssert.Contains(error, "sunburst");
        StringAssert.Contains(error, "density2d");
    }

    [TestMethod]
    public void AFlagThatIsNotTrueOrFalseStopsTheBlock()
    {
        StringAssert.Contains(Refused("header: maybe\n1 2"), "is not true or false");
    }

    [TestMethod]
    public void ARangeThatIsNotTwoNumbersStopsTheBlock()
    {
        StringAssert.Contains(Refused("sizeRange: 6\n1 2"), "is not two numbers");
    }

    /// <summary>
    /// A setting nobody can read is a question about the whole picture, where a cell that will not read
    /// is a question about one mark — so one stops the block and the other loses a mark. The line a word
    /// cloud draws, drawn in the same place.
    /// </summary>
    [TestMethod]
    public void ACellThatWillNotReadDoesNotStopTheBlock()
    {
        var read = Read("x: weight\n\nweight  mpg\n3504  lots");

        Assert.AreEqual("weight", read.X);
    }

    // ── How a value becomes a place ─────────────────────────────────────────

    [TestMethod]
    public void EveryScaleSettingIsRead()
    {
        var read = Read("""
                        xScale: log2
                        yScale: sqrt
                        xLimits: 0 100
                        yLimits: 10 -10
                        xBreaks: 0 25 50 75 100
                        yBreaks: -10 0 10
                        grid: y

                        1 2
                        """);

        Assert.AreEqual(PlotScale.Log2, read.XScale);
        Assert.AreEqual(PlotScale.Sqrt, read.YScale);
        Assert.AreEqual((0.0, 100.0), read.XLimits);
        Assert.AreEqual(PlotGrid.Y, read.Grid);

        // Written either way round, and kept the way an axis runs.
        Assert.AreEqual((-10.0, 10.0), read.YLimits);

        CollectionAssert.AreEqual(new[] { 0.0, 25, 50, 75, 100 }, read.XBreaks?.ToArray());
        CollectionAssert.AreEqual(new[] { -10.0, 0, 10 }, read.YBreaks?.ToArray());
    }

    [TestMethod]
    public void LogIsShortForLogTen()
    {
        // Nobody writing `log` means any other base.
        Assert.AreEqual(PlotScale.Log10, Read("xScale: log\n1 2").XScale);
    }

    [TestMethod]
    public void LimitsThatAreOneNumberTwiceStopTheBlock()
    {
        // An axis with no length has nowhere to put anything.
        StringAssert.Contains(Refused("xLimits: 5 5\n1 2"), "no length");
    }

    [TestMethod]
    public void LimitsThatAreNotTwoNumbersStopTheBlock()
    {
        StringAssert.Contains(Refused("xLimits: 0 10 20\n1 2"), "the ends of the axis");
    }

    [TestMethod]
    public void ABreakThatIsNotANumberNamesItself()
    {
        StringAssert.Contains(Refused("xBreaks: 0 ten 20\n1 2"), "`ten` is not one");
    }

    // ── How a value becomes a colour ────────────────────────────────────────

    [TestMethod]
    public void EveryColourSettingIsRead()
    {
        var read = Read("""
                        gradient: rdbu
                        midpoint: 0
                        fillLimits: -1 1
                        labels: true

                        1 2
                        """);

        Assert.AreEqual("rdbu", read.Gradient);
        Assert.AreEqual(0.0, read.Midpoint);
        Assert.AreEqual((-1.0, 1.0), read.FillLimits);
        Assert.IsTrue(read.Labels);
    }

    [TestMethod]
    public void AMidpointOfNoughtIsAMidpointRatherThanNothingWritten()
    {
        // Nought is the midpoint anybody actually writes, so it must not read as "none given".
        Assert.AreEqual(0.0, Read("midpoint: 0\n1 2").Midpoint);
        Assert.IsNull(Read("1 2").Midpoint);
    }

    [TestMethod]
    public void AGradientIsCarriedAcrossAsWritten()
    {
        // What it names — a run this knows, or colours written out — is settled where the colours are.
        Assert.AreEqual("#fff #000", Read("gradient: #fff #000\n1 2").Gradient);
    }

    [TestMethod]
    public void AMidpointThatIsNotANumberStopsTheBlock()
    {
        StringAssert.Contains(Refused("midpoint: middle\n1 2"), "is not a number");
    }

    // ── How thickly the points lie ──────────────────────────────────────────

    [TestMethod]
    public void EveryDensitySettingIsRead()
    {
        var read = Read("""
                        contour: lines
                        levels: 12
                        bandwidth: 2 3
                        adjust: 1.5
                        points: true

                        1 2
                        """, PlotFence.Density2d);

        Assert.AreEqual(PlotContour.Lines, read.Contour);
        Assert.AreEqual(12, read.Levels);
        Assert.AreEqual((2.0, 3.0), read.Bandwidth);
        Assert.AreEqual(1.5, read.Adjust);
        Assert.IsTrue(read.Points);
    }

    [TestMethod]
    public void OneBandwidthIsTheWidthOverBothAxes()
    {
        Assert.AreEqual((2.0, 2.0), Read("bandwidth: 2\n1 2").Bandwidth);
    }

    [TestMethod]
    public void ABandwidthOfNothingOrLessStopsTheBlock()
    {
        // A kernel with no width is not a kernel.
        StringAssert.Contains(Refused("bandwidth: 0\n1 2"), "greater than nothing");
    }

    [TestMethod]
    public void BinsAndLevelsAreHeldToWhatCanBeDrawn()
    {
        StringAssert.Contains(Refused("bins: 9000\n1 2"), "whole numbers");
        StringAssert.Contains(Refused("levels: 500\n1 2"), "is outside");
    }
}
