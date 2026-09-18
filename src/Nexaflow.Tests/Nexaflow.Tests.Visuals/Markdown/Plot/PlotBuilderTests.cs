using System.Linq;
using System.Windows.Media;
using Nexaflow.Markdown.Plot;
using Nexaflow.Tests.Fixtures;
using Nexaflow.Visuals.Text.Editing;
using Nexaflow.Visuals.Text.Markdown;
using Nexaflow.Visuals.Text.Markdown.Plot;

namespace Nexaflow.Tests.Visuals.Markdown.Plot;

/// <summary>
/// The picture a plot block comes to: a mark per row, where its values put it, standing for the row it was
/// drawn from.
///
/// <para>
/// Nothing here is plot machinery. The shared queries answer where a press landed and where a caret may
/// stand for a formula, a word cloud and this alike; what these assert is that the tree handed to them
/// says the right things — that every mark points back at what was written, that a value twice as far up
/// the range is drawn twice as far up the panel, and that a block which is not a plot still shows its
/// lines.
/// </para>
/// </summary>
[TestClass]
[TestCategory("UI")]
[CoversNode("correlation-plots-render")]
[CoversNode("correlation-plots-editing")]
public class PlotBuilderTests
{
    private const string Cars = "weight  mpg\n3504  18\n2372  24\n1613  35";

    private static Laid Lay(string source, PlotFence fence = PlotFence.Scatter, double room = 560) =>
        PlotBuilder.Build(source, fence, MarkdownPalette.Dark, room, 1.0);

    private static Piece[] Marks(Laid laid) =>
        [.. laid.Root.SelfAndDescendants().Where(piece => piece.Kind == PlotPiece.Mark)];

    private static Piece[] Of(Laid laid, string kind) =>
        [.. laid.Root.SelfAndDescendants().Where(piece => piece.Kind == kind)];

    // ── The marks ───────────────────────────────────────────────────────────

    [TestMethod]
    public void EveryRowOfTheTableIsAMark() => UiThread.Run(() =>
    {
        Assert.AreEqual(3, Marks(Lay(Cars)).Length);
    });

    [TestMethod]
    public void AHeaderIsNotAMark() => UiThread.Run(() =>
    {
        // It names the columns; drawing it would put a point at the words.
        Assert.AreEqual(3, Marks(Lay(Cars)).Length);
        Assert.AreEqual(2, Marks(Lay("1 2\n3 4")).Length);
    });

    [TestMethod]
    public void AMarkStandsForTheRowItWasDrawnFrom() => UiThread.Run(() =>
    {
        // What makes the picture editable: a press on a point means the row it came from, so the numbers
        // behind it can be typed into.
        foreach (var mark in Marks(Lay(Cars)))
        {
            Assert.IsNotNull(mark.Part, "a mark drawn from nothing could not be pressed");

            var written = Cars.Substring(mark.Part!.Start, mark.Part.Length);
            StringAssert.Matches(written, new System.Text.RegularExpressions.Regex(@"^\d+\s+\d+$"));
        }
    });

    [TestMethod]
    public void AMarkSitsWhereItsValuesAreAcrossAndUpTheRange() => UiThread.Run(() =>
    {
        // The smallest x is left of the largest, and the smallest y is below the largest. Which way up
        // the panel runs is the whole of what an axis is for.
        var marks = Marks(Lay(Cars));

        var heaviest = marks[0].Bounds;   // 3504, 18 — the widest car and the thirstiest
        var lightest = marks[2].Bounds;   // 1613, 35

        Assert.IsTrue(heaviest.Left > lightest.Left, "a bigger x is further right");
        Assert.IsTrue(heaviest.Top > lightest.Top, "a bigger y is further up, which is a smaller Top");
    });

    [TestMethod]
    public void ABiggerValueIsABiggerMarkWhereAColumnFeedsSize() => UiThread.Run(() =>
    {
        var marks = Marks(Lay("weight  mpg  pop\n3504  18  10\n2372  24  400", PlotFence.Bubble));

        Assert.IsTrue(marks[1].Bounds.Width > marks[0].Bounds.Width);
    });

    [TestMethod]
    public void WithNoColumnFeedingSizeEveryMarkIsTheSame() => UiThread.Run(() =>
    {
        var marks = Marks(Lay(Cars));

        Assert.AreEqual(marks[0].Bounds.Width, marks[2].Bounds.Width, 0.001);
    });

    // ── The axes ────────────────────────────────────────────────────────────

    [TestMethod]
    public void BothAxesAreDrawn() => UiThread.Run(() =>
    {
        var laid = Lay(Cars);

        Assert.AreEqual(1, Of(laid, PlotPiece.XAxis).Length);
        Assert.AreEqual(1, Of(laid, PlotPiece.YAxis).Length);
    });

    [TestMethod]
    public void AnAxisIsNumberedWithRoundNumbers() => UiThread.Run(() =>
    {
        Assert.IsTrue(Of(Lay(Cars), PlotPiece.Tick).Length > 1, "an axis nobody can read is not an axis");
    });

    [TestMethod]
    public void AnAxisTakesItsTitleFromTheColumnFeedingIt() => UiThread.Run(() =>
    {
        var titles = Of(Lay(Cars), PlotPiece.AxisTitle).Select(piece => piece.Words?.Glyphs.Text).ToArray();

        CollectionAssert.Contains(titles, "weight");
        CollectionAssert.Contains(titles, "mpg");
    });

    [TestMethod]
    public void WhatTheBlockCallsAnAxisWinsOverTheColumnsName() => UiThread.Run(() =>
    {
        var titles = Of(Lay("xTitle: Kerb weight\n\n" + Cars), PlotPiece.AxisTitle)
                     .Select(piece => piece.Words?.Glyphs.Text).ToArray();

        CollectionAssert.Contains(titles, "Kerb weight");
    });

    [TestMethod]
    public void ATableWithNoHeaderHasNoAxisTitles() => UiThread.Run(() =>
    {
        Assert.AreEqual(0, Of(Lay("1 2\n3 4"), PlotPiece.AxisTitle).Length);
    });

    // ── The key ─────────────────────────────────────────────────────────────

    [TestMethod]
    public void OnlyAColumnFeedingColourMakesAKey() => UiThread.Run(() =>
    {
        Assert.AreEqual(0, Of(Lay(Cars), PlotPiece.Name).Length);

        var grouped = Lay("colour: origin\n\nweight  mpg  origin\n3504  18  USA\n2372  24  Japan");

        Assert.AreEqual(2, Of(grouped, PlotPiece.Name).Length);
    });

    [TestMethod]
    public void LegendNoneDrawsNoKeyAtAll() => UiThread.Run(() =>
    {
        var laid = Lay("colour: origin\nlegend: none\n\nweight  mpg  origin\n3504  18  USA\n2372  24  Japan");

        Assert.AreEqual(0, Of(laid, PlotPiece.Name).Length);
    });

    // ── The room it is given ────────────────────────────────────────────────

    [TestMethod]
    public void ThePlotIsFittedIntoTheRoomItIsGiven() => UiThread.Run(() =>
    {
        Assert.IsTrue(Lay(Cars, room: 300).Size.Width <= 300);
    });

    [TestMethod]
    public void AWidthWrittenInTheBlockWinsOverTheRoom() => UiThread.Run(() =>
    {
        Assert.AreEqual(420, Lay("width: 420\n\n" + Cars, room: 900).Size.Width, 0.001);
    });

    // ── What cannot be drawn ────────────────────────────────────────────────

    [TestMethod]
    public void ABlockThatIsNotAPlotShowsItsOwnLines() => UiThread.Run(() =>
    {
        var laid = Lay("width: wide\n1 2");

        Assert.AreEqual(0, Marks(laid).Length);
        Assert.IsTrue(laid.Trouble.Count > 0, "a block that will not read says why");
    });

    [TestMethod]
    public void AnEmptyBlockSaysWhatItNeeds() => UiThread.Run(() =>
    {
        var laid = Lay("");

        Assert.AreEqual(0, Marks(laid).Length);
        StringAssert.Contains(laid.Trouble[0].Message, "row of values");
    });

    [TestMethod]
    public void ARowWithNoPlaceIsWavedAndTheRestStillDraw() => UiThread.Run(() =>
    {
        // It is the row somebody is editing, and it is wrong every time they are halfway through it.
        var laid = Lay("weight  mpg\n3504  18\n2372  lots\n1613  35");

        Assert.AreEqual(2, Marks(laid).Length);
        Assert.AreEqual(1, laid.Trouble.Count);
    });

    // ── The way in ──────────────────────────────────────────────────────────

    [TestMethod]
    public void TheCorrelationFencesAreDiagramLanguages()
    {
        Assert.IsTrue(DiagramRenderer.IsDiagramLanguage("scatter"));
        Assert.IsTrue(DiagramRenderer.IsDiagramLanguage("bubble"));
    }

    [TestMethod]
    public void AScatterBlockDispatchesThroughTheDiagramRenderer() => UiThread.Run(() =>
    {
        var element = DiagramRenderer.Render("scatter", Cars, MarkdownPalette.Dark);

        Assert.IsInstanceOfType<ContentElement>(element,
            "a plot is rendered content, not the source text it was written as");
    });

    // ── Scales ──────────────────────────────────────────────────────────────

    /// <summary>The words along one axis, in the order they are drawn.</summary>
    private static string[] Marks(Laid laid, string axis) =>
        [.. laid.Root.SelfAndDescendants()
                 .First(piece => piece.Kind == axis)
                 .SelfAndDescendants()
                 .Where(piece => piece.Kind == PlotPiece.Tick)
                 .Select(piece => piece.Words?.Glyphs.Text ?? string.Empty)];

    /// <summary>Where each of them is across the page.</summary>
    private static double[] Along(Laid laid, string axis) =>
        [.. laid.Root.SelfAndDescendants()
                 .First(piece => piece.Kind == axis)
                 .SelfAndDescendants()
                 .Where(piece => piece.Kind == PlotPiece.Tick)
                 .Select(piece => piece.Bounds.Left + (piece.Bounds.Width / 2))];

    [TestMethod]
    public void ALogAxisIsNumberedInPowers() => UiThread.Run(() =>
    {
        var laid = Lay("xScale: log\n\ngdp  life\n430  54\n54225  83");

        CollectionAssert.AreEqual(new[] { "100", "1000", "10000", "100000" }, Marks(laid, PlotPiece.XAxis));
    });

    [TestMethod]
    public void ALogAxisPutsEachPowerTheSameDistanceApart() => UiThread.Run(() =>
    {
        var at = Along(Lay("xScale: log\n\ngdp  life\n430  54\n54225  83"), PlotPiece.XAxis);

        // Which is the whole of what a log axis is for.
        var first = at[1] - at[0];

        for (var which = 2; which < at.Length; which++)
            Assert.AreEqual(first, at[which] - at[which - 1], 1.0);
    });

    [TestMethod]
    public void EndsWrittenInTheBlockAreTheEndsOfTheAxis() => UiThread.Run(() =>
    {
        // Rather than the round numbers either side of the values, which is what nobody wrote.
        var marks = Marks(Lay("xLimits: 0 10\n\nx  y\n3  1\n4  2"), PlotPiece.XAxis);

        Assert.AreEqual("0", marks.First());
        Assert.AreEqual("10", marks.Last());
    });

    [TestMethod]
    public void BreaksWrittenInTheBlockAreWhereItIsMarked() => UiThread.Run(() =>
    {
        var marks = Marks(Lay("xBreaks: 0 50 100\nxLimits: 0 100\n\nx  y\n30  1\n40  2"), PlotPiece.XAxis);

        CollectionAssert.AreEqual(new[] { "0", "50", "100" }, marks);
    });

    [TestMethod]
    public void AValueWithNoPlaceOnALogAxisIsWavedRatherThanDrawnAtNought() => UiThread.Run(() =>
    {
        // Nought is a real place, and a log axis has nothing to say about a value of nought or less.
        var laid = Lay("xScale: log\n\ngdp  life\n0  54\n100  60\n1000  70");

        Assert.AreEqual(2, Marks(laid).Length);
        Assert.AreEqual(1, laid.Trouble.Count);
    });

    // ── Gridlines ───────────────────────────────────────────────────────────

    [TestMethod]
    public void GridlinesAreDrawnBehindTheMarksUnlessTheBlockSaysNot() => UiThread.Run(() =>
    {
        Assert.IsTrue(Of(Lay(Cars), PlotPiece.Grid).Length > 0);
        Assert.AreEqual(0, Of(Lay("grid: none\n\n" + Cars), PlotPiece.Grid).Length);
        Assert.AreEqual(1, Of(Lay("grid: y\n\n" + Cars), PlotPiece.Grid).Length);
    });

    // ── Marks ───────────────────────────────────────────────────────────────

    [TestMethod]
    public void AColumnCanFeedColourAndShapeAtOnce() => UiThread.Run(() =>
    {
        // aes(colour = region, shape = region) — how a chart tells its groups apart twice over, so they
        // read in colour and in print alike.
        var laid = Lay("colour: region\nshape: region\n\nx  y  region\n1  1  North\n2  2  South");

        Assert.AreEqual(2, Of(laid, PlotPiece.Name).Length);

        var marks = Marks(laid);
        Assert.AreNotEqual(Fill(marks[0]), Fill(marks[1]), "the two groups are drawn in different colours");
    });

    /// <summary>What a piece was drawn in, which is how two groups are told apart.</summary>
    private static Color? Fill(Piece piece) =>
        piece.SelfAndDescendants().SelectMany(inner => inner.Marks.ToArray()).OfType<GeometryMark>()
             .Select(mark => mark.Fill).OfType<SolidColorBrush>().Select(brush => (Color?)brush.Color)
             .FirstOrDefault();

    [TestMethod]
    public void AShapeNamingNeitherAColumnNorAMarkSaysSo() => UiThread.Run(() =>
    {
        var laid = Lay("shape: sunburst\n\n" + Cars);

        Assert.AreEqual(3, Marks(laid).Length, "and the plot still draws");
        StringAssert.Contains(laid.Trouble[0].Message, "neither a column nor a mark");
    });

    // ── Heat maps ───────────────────────────────────────────────────────────

    private const string Sightings = "month  year  count\nJan  2023  12\nFeb  2023  28\nJan  2024  19\nFeb  2024  36";

    private const string Matrix = "     mpg    hp\nmpg   1.00  -0.78\nhp   -0.78   1.00";

    [TestMethod]
    public void AHeatMapDrawsATilePerValue() => UiThread.Run(() =>
    {
        Assert.AreEqual(4, Marks(Lay(Sightings, PlotFence.Heatmap)).Length);
    });

    [TestMethod]
    public void AMatrixDrawsATilePerValueOfIt() => UiThread.Run(() =>
    {
        // Two rows of two, which is four marks rather than two.
        Assert.AreEqual(4, Marks(Lay(Matrix, PlotFence.Heatmap)).Length);
    });

    [TestMethod]
    public void TilesFillThePanelOnceAndNoMore() => UiThread.Run(() =>
    {
        // A tile stands for one value rather than a stretch of them. Reading the years as a plain range
        // left every tile a third of the panel wide with three of them to fit, and the heat map was drawn
        // over its own title.
        var marks = Marks(Lay(Sightings, PlotFence.Heatmap));

        var wide = marks[0].Bounds.Width;
        foreach (var mark in marks) Assert.AreEqual(wide, mark.Bounds.Width, 0.5, "every tile is the same size");

        var left = marks.Min(mark => mark.Bounds.Left);
        var right = marks.Max(mark => mark.Bounds.Right);

        Assert.AreEqual(2 * wide, right - left, 0.5, "two columns of tiles take exactly two tiles' width");
    });

    [TestMethod]
    public void ATileAxisOfNumbersIsStillReadInOrder() => UiThread.Run(() =>
    {
        CollectionAssert.AreEqual(new[] { "2023", "2024" }, Marks(Lay(Sightings, PlotFence.Heatmap), PlotPiece.YAxis));
    });



    [TestMethod]
    public void AMatrixIsTitledByNeitherOfItsAxes() => UiThread.Run(() =>
    {
        // Its columns are its values, so an axis titled with one of them would be naming its own tick.
        Assert.AreEqual(0, Of(Lay(Matrix, PlotFence.Heatmap), PlotPiece.AxisTitle).Length);
    });

    // ── Colour read as a quantity ───────────────────────────────────────────

    [TestMethod]
    public void AColourThatIsAQuantityGetsABarRatherThanRows() => UiThread.Run(() =>
    {
        var laid = Lay(Sightings, PlotFence.Heatmap);

        Assert.AreEqual(0, Of(laid, PlotPiece.Name).Length, "a run of colours is not a list of names");
        Assert.IsTrue(Of(laid, "Key").Length >= 3, "and it is explained by the numbers along it");
    });

    [TestMethod]
    public void ABiggerValueIsFurtherAlongTheRunOfColours() => UiThread.Run(() =>
    {
        var marks = Marks(Lay("gradient: blues\n\n" + Sightings, PlotFence.Heatmap));

        // Jan 2023 counts 12 and Feb 2024 counts 36, so the second is the darker blue.
        Assert.IsTrue(Grey(Fill(marks[0])) > Grey(Fill(marks[3])));
    });

    [TestMethod]
    public void AMidpointPutsTheMiddleColourOnTheMiddleValue() => UiThread.Run(() =>
    {
        // Which is what makes the colour of a correlation mean its sign.
        var marks = Marks(Lay("gradient: rdbu\nmidpoint: 0\nfillLimits: -1 1\n\n" + Matrix, PlotFence.Heatmap));

        var ones = marks.Where(mark => mark.Part is not null).ToArray();

        Assert.IsTrue(ones.Any(mark => Fill(mark)!.Value.R > Fill(mark)!.Value.B), "a positive one reads red");
        Assert.IsTrue(ones.Any(mark => Fill(mark)!.Value.B > Fill(mark)!.Value.R), "and a negative one blue");
    });

    [TestMethod]
    public void LabelsWriteEachValueOnItsOwnTile() => UiThread.Run(() =>
    {
        Assert.AreEqual(0, Of(Lay(Matrix, PlotFence.Heatmap), PlotPiece.Label).Length);
        Assert.AreEqual(4, Of(Lay("labels: true\n\n" + Matrix, PlotFence.Heatmap), PlotPiece.Label).Length);
    });

    [TestMethod]
    public void AGradientNamingNoRunOfColoursSaysSo() => UiThread.Run(() =>
    {
        var laid = Lay("gradient: sunburst\n\n" + Sightings, PlotFence.Heatmap);

        Assert.AreEqual(4, Marks(laid).Length, "and the heat map still draws");
        StringAssert.Contains(laid.Trouble[0].Message, "not a colour");
    });

    private static double Grey(Color? colour) =>
        colour is null ? 0 : ((0.2126 * colour.Value.R) + (0.7152 * colour.Value.G) + (0.0722 * colour.Value.B)) / 255;

    // ── Counted rather than drawn ───────────────────────────────────────────

    /// <summary>Enough points that drawing them one by one says less than counting them.</summary>
    private static string Crowded(string geom)
    {
        var lines = new System.Text.StringBuilder();
        lines.AppendLine($"geom: {geom}");
        lines.AppendLine("bins: 6");
        lines.AppendLine();
        lines.AppendLine("x  y");

        var throws = new System.Random(4);

        for (var which = 0; which < 400; which++)
            lines.AppendLine($"{throws.NextDouble() * 10:0.###}  {throws.NextDouble() * 10:0.###}");

        return lines.ToString();
    }

    [TestMethod]
    public void ABinnedPlotDrawsBinsRatherThanAMarkPerRow() => UiThread.Run(() =>
    {
        var laid = Lay(Crowded("hex"), PlotFence.Heatmap);

        Assert.AreEqual(0, Marks(laid).Length, "four hundred rows are not four hundred marks");
        Assert.IsTrue(Of(laid, PlotPiece.Bin).Length is > 4 and < 400, "they are counted into bins");
    });

    [TestMethod]
    public void BothBinningsDrawSomething() => UiThread.Run(() =>
    {
        Assert.IsTrue(Of(Lay(Crowded("hex"), PlotFence.Heatmap), PlotPiece.Bin).Length > 4);
        Assert.IsTrue(Of(Lay(Crowded("bin2d"), PlotFence.Heatmap), PlotPiece.Bin).Length > 4);
    });

    [TestMethod]
    public void ABinStandsForNoOneRow() => UiThread.Run(() =>
    {
        // Nobody typed a bin — it is a count of rows, not one of them — so there is nowhere in one for a
        // caret to go, exactly as with a gridline.
        foreach (var bin in Of(Lay(Crowded("hex"), PlotFence.Heatmap), PlotPiece.Bin))
            Assert.IsNull(bin.Part);
    });

    [TestMethod]
    public void ABinnedPlotsKeyCountsRowsRatherThanNamingColumns() => UiThread.Run(() =>
    {
        // There may be no third column at all, and the colours still mean something: how many fell there.
        var laid = Lay(Crowded("hex"), PlotFence.Heatmap);

        Assert.AreEqual(0, Of(laid, PlotPiece.Name).Length);
        Assert.IsTrue(Of(laid, "Key").Length >= 3, "the counts are written along the bar");
    });

    [TestMethod]
    public void MoreBinsAreSmallerBins() => UiThread.Run(() =>
    {
        var few = Of(Lay(Crowded("bin2d").Replace("bins: 6", "bins: 4"), PlotFence.Heatmap), PlotPiece.Bin);
        var many = Of(Lay(Crowded("bin2d").Replace("bins: 6", "bins: 16"), PlotFence.Heatmap), PlotPiece.Bin);

        Assert.IsTrue(many[0].Bounds.Width < few[0].Bounds.Width);
    });

    [TestMethod]
    public void BinsThatAreNotWholeNumbersStopTheBlock() => UiThread.Run(() =>
    {
        var laid = Lay("geom: hex\nbins: 2.5\n\nx  y\n1  1", PlotFence.Heatmap);

        Assert.AreEqual(0, Of(laid, PlotPiece.Bin).Length);
        StringAssert.Contains(laid.Trouble[0].Message, "whole numbers");
    });

    // ── How thickly the rows lie ────────────────────────────────────────────

    [TestMethod]
    public void ADensityDrawsContoursRatherThanAMarkPerRow() => UiThread.Run(() =>
    {
        var laid = Lay(Crowded("density2d"), PlotFence.Density2d);

        Assert.AreEqual(0, Marks(laid).Length);
        Assert.IsTrue(Of(laid, PlotPiece.Cloud).Length > 2, "and the contours are drawn");
    });

    [TestMethod]
    public void EveryWayOfDrawingADensityDrawsSomething() => UiThread.Run(() =>
    {
        foreach (var how in new[] { "bands", "lines", "raster" })
        {
            var laid = Lay($"contour: {how}\n" + Crowded("density2d"), PlotFence.Density2d);

            Assert.IsTrue(Of(laid, PlotPiece.Cloud).Length > 2, $"a {how} density drew nothing");
        }
    });

    [TestMethod]
    public void AContourStandsForNoOneRow() => UiThread.Run(() =>
    {
        foreach (var piece in Of(Lay(Crowded("density2d"), PlotFence.Density2d), PlotPiece.Cloud))
            Assert.IsNull(piece.Part);
    });

    [TestMethod]
    public void PointsDrawsTheRowsOverTheCloudAndTheyCanStillBePressed() => UiThread.Run(() =>
    {
        var over = Lay("points: true\n" + Crowded("density2d"), PlotFence.Density2d);

        Assert.AreEqual(0, Marks(Lay(Crowded("density2d"), PlotFence.Density2d)).Length);
        Assert.IsTrue(Marks(over).Length > 100, "the rows are drawn as well");
        Assert.IsTrue(Marks(over).All(mark => mark.Part is not null), "and each still means its own row");
    });

    [TestMethod]
    public void RowsShownOverACloudAreSmallerThanRowsShownAlone() => UiThread.Run(() =>
    {
        // At the number of rows that make a cloud worth drawing, marks at their usual size are a solid
        // field with the cloud nowhere to be seen under it.
        var alone = Marks(Lay(Crowded("point"), PlotFence.Scatter))[0].Bounds.Width;
        var over = Marks(Lay("points: true\n" + Crowded("density2d"), PlotFence.Density2d))[0].Bounds.Width;

        Assert.IsTrue(over < alone, $"{over} is not smaller than {alone}");
    });

    [TestMethod]
    public void TooFewRowsToSayHowThicklyTheyLieSaysSo() => UiThread.Run(() =>
    {
        var laid = Lay("x  y\n1  1\n2  2", PlotFence.Density2d);

        Assert.AreEqual(0, Of(laid, PlotPiece.Cloud).Length);
        StringAssert.Contains(laid.Trouble[0].Message, "at least three");
    });

    // ── What is worked out and drawn over them ──────────────────────────────

    [TestMethod]
    public void NoFitIsAskedForAndNoneIsDrawn() => UiThread.Run(() =>
    {
        Assert.AreEqual(0, Of(Lay(Cars), PlotPiece.Fit).Length);
        Assert.AreEqual(0, Of(Lay(Cars), PlotPiece.Stats).Length);
    });

    [TestMethod]
    public void AFitIsDrawnWithTheBandItsOwnDoubtMakes() => UiThread.Run(() =>
    {
        var laid = Lay("fit: lm\n\n" + Cars);

        Assert.AreEqual(1, Of(laid, PlotPiece.Fit).Length);
        Assert.AreEqual(1, Of(laid, PlotPiece.Band).Length);
    });

    [TestMethod]
    public void SeFalseDrawsTheLineWithoutTheBand() => UiThread.Run(() =>
    {
        var laid = Lay("fit: lm\nse: false\n\n" + Cars);

        Assert.AreEqual(1, Of(laid, PlotPiece.Fit).Length);
        Assert.AreEqual(0, Of(laid, PlotPiece.Band).Length);
    });

    [TestMethod]
    public void BothKindsOfFitDrawSomething() => UiThread.Run(() =>
    {
        Assert.AreEqual(1, Of(Lay("fit: lm\n\n" + Cars), PlotPiece.Fit).Length);
        Assert.AreEqual(1, Of(Lay("fit: loess\n\n" + Cars), PlotPiece.Fit).Length);
    });

    [TestMethod]
    public void AFitStandsForNoOneRow() => UiThread.Run(() =>
    {
        // Nobody typed a regression.
        foreach (var piece in Of(Lay("fit: lm\n\n" + Cars), PlotPiece.Fit))
            Assert.IsNull(piece.Part);
    });

    [TestMethod]
    public void TheStatsAreWrittenOnThePanel() => UiThread.Run(() =>
    {
        var said = Of(Lay("stats: r r2 n\n\n" + Cars), PlotPiece.Stats)
                   .Select(piece => piece.Words?.Glyphs.Text)
                   .FirstOrDefault(text => text is not null);

        Assert.IsNotNull(said);
        StringAssert.Contains(said!, "r = ");
        StringAssert.Contains(said!, "R² = ");
        StringAssert.Contains(said!, "n = 3");
    });

    [TestMethod]
    public void ACorrelationIsReportedFromTheValuesRatherThanFromThePage() => UiThread.Run(() =>
    {
        // Down the page is the way a screen counts and not the way a number does. Worked out on the panel,
        // every correlation would come out with its sign turned about.
        var said = Of(Lay("stats: r\n\n" + Cars), PlotPiece.Stats)
                   .Select(piece => piece.Words?.Glyphs.Text)
                   .First(text => text is not null);

        StringAssert.Contains(said!, "-", "weight against fuel economy is a negative correlation");
    });

    [TestMethod]
    public void EachMethodIsWrittenWithItsOwnLetter() => UiThread.Run(() =>
    {
        StringAssert.Contains(Said("stats: r\nmethod: pearson\n\n" + Cars), "r = ");
        StringAssert.Contains(Said("stats: r\nmethod: spearman\n\n" + Cars), "ρ = ");
        StringAssert.Contains(Said("stats: r\nmethod: kendall\n\n" + Cars), "τ = ");
    });

    private static string Said(string source) =>
        Of(Lay(source), PlotPiece.Stats).Select(piece => piece.Words?.Glyphs.Text)
                                        .First(text => text is not null)!;

    [TestMethod]
    public void AStatNobodyKnowsSaysWhatCouldHaveBeenAskedFor() => UiThread.Run(() =>
    {
        var laid = Lay("stats: sigma\n\n" + Cars);

        Assert.AreEqual(0, Marks(laid).Length, "a setting nobody can read stops the block");
        StringAssert.Contains(laid.Trouble[0].Message, "names nothing to report");
    });

    // ── Written in place ────────────────────────────────────────────────────

    [TestMethod]
    public void AValueDrawnOnItsTileIsTheCharactersItWasWrittenWith() => UiThread.Run(() =>
    {
        // What makes a heat map editable: a caret stands in the number, a drag picks out its digits, and
        // typing into the picture edits the block.
        const string Source = "labels: true\n\n     mpg    hp\nmpg   1.00  -0.78\nhp   -0.78   1.00";

        var labels = Of(Lay(Source, PlotFence.Heatmap), PlotPiece.Label);

        Assert.AreEqual(4, labels.Length);

        foreach (var label in labels)
        {
            Assert.IsNotNull(label.Part, "a value drawn from nothing could not be typed into");
            Assert.IsNotNull(label.Words, "it is a run of text rather than a picture of one");
            Assert.AreNotEqual(Stops.None, label.Stops, "and a caret can stand between any two of its digits");

            Assert.AreEqual(Source.Substring(label.Part!.Start, label.Part.Length),
                            label.Words!.Glyphs.Text,
                            "what is drawn is what was written");
        }
    });

    [TestMethod]
    public void ATicksNumberIsWorkedOutRatherThanWritten() => UiThread.Run(() =>
    {
        // Nobody typed "2000" on the axis — it is a number the plot chose — so there is nowhere in it for a
        // caret to stop.
        foreach (var tick in Of(Lay(Cars), PlotPiece.Tick))
            Assert.IsFalse(tick.Words is { Maps: true }, $"`{tick.Words?.Glyphs.Text}` is not source");
    });

    [TestMethod]
    public void ACategoryOnAnAxisIsWorkedOutToo() => UiThread.Run(() =>
    {
        // A category stands for every row carrying it rather than for one of them, so typing there would
        // have no one place to go.
        var ticks = Of(Lay("month  count\nJan  4\nFeb  9", PlotFence.Heatmap), PlotPiece.Tick);

        Assert.IsTrue(ticks.Length > 0);
        Assert.IsTrue(ticks.All(tick => tick.Words is not { Maps: true }));
    });

    // ── Settings that used to parse and do nothing ──────────────────────────

    [TestMethod]
    public void ASubtitleAndACaptionAreDrawn() => UiThread.Run(() =>
    {
        var said = Of(Lay("title: One\nsubtitle: Two\ncaption: Three\n\n" + Cars), PlotPiece.Title)
                   .Select(piece => piece.Words?.Glyphs.Text)
                   .ToArray();

        CollectionAssert.Contains(said, "One");
        CollectionAssert.Contains(said, "Two");
        CollectionAssert.Contains(said, "Three");
    });

    [TestMethod]
    public void TheKeyCarriesItsOwnTitle() => UiThread.Run(() =>
    {
        var said = Of(Lay("colour: origin\nlegendTitle: Origin\n\nweight  mpg  origin\n3504  18  USA\n2372  24  Japan"),
                      PlotPiece.Name)
                   .Select(piece => piece.Words?.Glyphs.Text)
                   .ToArray();

        CollectionAssert.Contains(said, "Origin");
        CollectionAssert.Contains(said, "USA");
    });

    [TestMethod]
    public void AColumnMappedToAlphaMakesSomeMarksFainter() => UiThread.Run(() =>
    {
        var marks = Marks(Lay("alpha: share\n\nx  y  share\n1  1  1\n2  2  100"));

        Assert.AreNotEqual(Clearness(marks[0]), Clearness(marks[1]));
    });

    private static double Clearness(Piece piece) =>
        piece.SelfAndDescendants().SelectMany(inner => inner.Marks.ToArray()).OfType<GeometryMark>()
             .Select(mark => mark.Fill?.Opacity ?? 1).FirstOrDefault();

    [TestMethod]
    public void JitterMovesMarksOffTheirPlaceAndAlwaysTheSameWay() => UiThread.Run(() =>
    {
        const string Stacked = "x  y\n1  1\n1  1\n1  1";

        var still = Marks(Lay(Stacked)).Select(mark => mark.Bounds.Left).ToArray();
        var shaken = Marks(Lay("jitter: 0.5\n\n" + Stacked)).Select(mark => mark.Bounds.Left).ToArray();

        Assert.AreEqual(1, still.Distinct().Count(), "without jitter they sit on top of one another");
        Assert.IsTrue(shaken.Distinct().Count() > 1, "with it they do not");

        // Thrown from where each row was written rather than from the clock.
        CollectionAssert.AreEqual(shaken, Marks(Lay("jitter: 0.5\n\n" + Stacked))
                                          .Select(mark => mark.Bounds.Left).ToArray());
    });

    [TestMethod]
    public void AColumnMappedToLabelNamesEachMark() => UiThread.Run(() =>
    {
        var said = Of(Lay("label: name\n\nx  y  name\n1  1  Alpha\n2  2  Beta"), PlotPiece.Label)
                   .Select(piece => piece.Words?.Glyphs.Text)
                   .ToArray();

        CollectionAssert.AreEquivalent(new[] { "Alpha", "Beta" }, said);
    });

    [TestMethod]
    public void FlipSwapsTheAxes() => UiThread.Run(() =>
    {
        // The upright title is the turned one, whichever order the pieces happen to be laid in.
        Assert.AreEqual("mpg", Upright(Lay(Cars)));
    Assert.AreEqual("weight", Upright(Lay("flip: true\n\n" + Cars)));

            // A title written for a channel goes where that channel is drawn, so flipping carries it up the
            // page with its own values rather than leaving it over somebody else's.
            Assert.AreEqual("Kerb weight",
                            Upright(Lay("flip: true\nxTitle: Kerb weight\nyTitle: Economy\n\n" + Cars)));
    });

    /// <summary>What is written up the side, which is the axis title drawn turned.</summary>
    private static string? Upright(Laid laid)
    {
        foreach (var piece in Of(laid, PlotPiece.AxisTitle))
            if (piece.Turned is not null) return piece.Words?.Glyphs.Text;

        return null;
    }

    [TestMethod]
    public void AspectHoldsThePanelToTheShapeItAsksFor() => UiThread.Run(() =>
    {
        // A correlation matrix asked for square cells is not a correlation matrix drawn oblong. The room
        // left over is simply not used, so it is the panel's shape that is held, not its width.
        Assert.AreEqual(1.0, Shape(Lay("aspect: 1\nwidth: 400\nheight: 400\n\n" + Cars)), 0.02);
    Assert.AreEqual(2.0, Shape(Lay("aspect: 2\nwidth: 400\nheight: 400\n\n" + Cars)), 0.02);

            // And with no size of its own, which is how a block is usually written.
            Assert.AreEqual(1.0, Shape(Lay("aspect: 1\n\n" + Cars)), 0.02);
    });

    [TestMethod]
    public void AspectHoldsEvenWithAFitAndAFlipOverIt() => UiThread.Run(() =>
    {
        // The combination the figure is drawn from, because a shape held in isolation and lost in company
        // is a shape nobody can rely on.
        const string Source = """
            title: Fuel economy by weight
            aspect: 1
            flip: true
            fit: lm
            group: cyl
            colour: cyl

            weight  mpg   cyl
            2620    21.0  six
            2320    22.8  four
            3440    18.7  eight
            3570    14.3  eight
            3190    24.4  four
            2200    32.4  four
            1615    30.4  four
            5250    10.4  eight
            """;

        Assert.AreEqual(1.0, Shape(Lay(Source, room: 520)), 0.02);
    });

    [TestMethod]
    public void TheTitleStandsClearOfThePanel() => UiThread.Run(() =>
    {
        // A title drawn over the top gridline is a title drawn inside the plot.
        Clear(Lay("title: Fuel economy by weight\naspect: 1\n\n" + Cars, room: 520));
        Clear(Lay("title: Ratings by month\njitter: 0.6\n\nmonth  rating\nJan  3\nJan  4\nFeb  5", room: 420));
        Clear(Lay("title: One\nsubtitle: Two\n\n" + Cars, room: 640));
    });

    private static void Clear(Laid laid)
    {
        var title = Of(laid, PlotPiece.Title)[0].Bounds;
        var panel = Of(laid, PlotPiece.Grid)[0].Bounds;

        foreach (var piece in Of(laid, PlotPiece.Grid)) panel.Union(piece.Bounds);
        foreach (var piece in Of(laid, PlotPiece.Title)) title.Union(piece.Bounds);

        Assert.IsTrue(title.Top >= panel.Bottom - 0.5 || title.Bottom <= panel.Top + 0.5,
                      $"a title reaching {title.Top}–{title.Bottom} runs into a panel of {panel.Top}–{panel.Bottom}");
    }

    /// <summary>
    /// How much wider than tall the panel came out. Measured off the gridlines, which span the panel
    /// exactly — an axis piece reaches past it by however far its last number overhangs.
    /// </summary>
    private static double Shape(Laid laid)
    {
        var grid = Of(laid, PlotPiece.Grid);
        var panel = grid[0].Bounds;

        foreach (var piece in grid) panel.Union(piece.Bounds);

        return panel.Width / panel.Height;
    }

    [TestMethod]
    public void AColumnMappedToGroupFitsALinePerGroup() => UiThread.Run(() =>
    {
        const string Two = "x  y  side\n1  1  up\n2  2  up\n3  3  up\n1  9  down\n2  8  down\n3  7  down";

        Assert.AreEqual(1, Of(Lay("fit: lm\nse: false\n\n" + Two), PlotPiece.Fit).Length);
        Assert.AreEqual(2, Of(Lay("fit: lm\nse: false\ngroup: side\n\n" + Two), PlotPiece.Fit).Length);
    });

    [TestMethod]
    public void NoMarkIsShakenPastTheAxis() => UiThread.Run(() =>
    {
        // A mark moved off its place to stop it hiding another is still a mark standing at a value, and a
        // value the axis says is not there is not one.
        var laid = Lay("jitter: 0.9\n\nmonth  rating\nJan  3\nJan  3\nJan  5\nFeb  5\nFeb  2\nMar  4");

        var panel = Of(laid, PlotPiece.Grid)[0].Bounds;
            foreach (var piece in Of(laid, PlotPiece.Grid)) panel.Union(piece.Bounds);

            // A mark held to the edge lands on it, and an edge is not a place two doubles agree about.
            panel.Inflate(0.01, 0.01);

        foreach (var mark in Marks(laid))
        {
            var middle = new System.Windows.Point(mark.Bounds.Left + (mark.Bounds.Width / 2),
                                                  mark.Bounds.Top + (mark.Bounds.Height / 2));

            Assert.IsTrue(panel.Contains(middle),
                          $"a mark at {middle} stands outside a panel of {panel}");
        }
    });

    // ── A correlation matrix, whose marks nobody wrote ──────────────────────

    private const string Observations =
        "geom: corr\n\nwt  mpg  hp\n2.620  21.0  110\n3.440  18.7  175\n1.615  30.4  52\n5.250  10.4  205";

    [TestMethod]
    public void ACorrelationMatrixDrawsATilePerPairOfColumns() => UiThread.Run(() =>
    {
        // Three columns, so nine tiles — the table's four rows are not marks at all.
        Assert.AreEqual(9, Marks(Lay(Observations, PlotFence.Heatmap)).Length);
    });

    [TestMethod]
    public void BothAxesOfACorrelationMatrixAreTheColumnNames() => UiThread.Run(() =>
    {
        var laid = Lay(Observations, PlotFence.Heatmap);

        CollectionAssert.AreEqual(new[] { "wt", "mpg", "hp" }, Marks(laid, PlotPiece.XAxis));
        CollectionAssert.AreEqual(new[] { "hp", "mpg", "wt" }, Marks(laid, PlotPiece.YAxis),
                                  "read up the page, so the diagonal falls from the top left");
    });

    [TestMethod]
    public void ACorrelationTileStandsForNothingWritten() => UiThread.Run(() =>
    {
        // No cell of the table holds the coefficient, so there is nowhere in it for a caret to go.
        foreach (var mark in Marks(Lay(Observations, PlotFence.Heatmap)))
            Assert.IsNull(mark.Part, "a worked-out tile is not typed into");
    });

    [TestMethod]
    public void ThePerfectDiagonalIsTheStrongestColourOnTheMatrix() => UiThread.Run(() =>
    {
        var marks = Marks(Lay(Observations, PlotFence.Heatmap));

        // Which column and which row each tile is in. A tile is not square, so this is counted in slots
        // rather than measured in pixels.
        var across = marks.Select(mark => Math.Round(mark.Bounds.Left)).Distinct().Order().ToList();
        var down = marks.Select(mark => Math.Round(mark.Bounds.Top)).Distinct().Order().ToList();

        var diagonal = marks.Where(mark => across.IndexOf(Math.Round(mark.Bounds.Left))
                                           == down.IndexOf(Math.Round(mark.Bounds.Top))).ToList();

        Assert.AreEqual(3, diagonal.Count, "three columns, three of them against themselves");

        // A column against itself is one, and one is the far end of the run of colours.
        var one = Fill(diagonal[0]);
        foreach (var mark in diagonal) Assert.AreEqual(one, Fill(mark), "every one of them is the same colour");
        Assert.IsTrue(marks.Any(mark => Fill(mark) != one), "and it is not the colour of everything");
    });

    [TestMethod]
    public void LabelsWritesTheCoefficientInsideEachTile() => UiThread.Run(() =>
    {
        var laid = Lay("geom: corr\nlabels: true\n\nwt  mpg  hp\n2.620  21.0  110\n3.440  18.7  175\n"
                       + "1.615  30.4  52\n5.250  10.4  205", PlotFence.Heatmap);

        var said = Of(laid, PlotPiece.Label).Select(piece => piece.Words?.Glyphs.Text ?? string.Empty).ToList();

        Assert.AreEqual(9, said.Count, "a number on every tile");
        Assert.AreEqual(3, said.Count(text => text == "1"), "a column against itself is exactly one");
    });

    [TestMethod]
    public void ATableWithOneNumericColumnIsNoMatrixAndSaysSo() => UiThread.Run(() =>
    {
        // Nothing to correlate, so nothing is drawn — and the block still stands rather than throwing.
        var laid = Lay("geom: corr\n\nname  wt\nMazda  2.620\nMerc  3.440\nFiat  2.200", PlotFence.Heatmap);

        Assert.AreEqual(0, Marks(laid).Length);
    });

    [TestMethod]
    public void NeitherAxisOfACorrelationMatrixIsTitledWithOneOfItsOwnColumns() => UiThread.Run(() =>
    {
        // Both axes are the columns, so a title taken off a column would be naming one of its own ticks.
        var laid = Lay(Observations, PlotFence.Heatmap);

        Assert.AreEqual(0, Of(laid, PlotPiece.AxisTitle).Length);
    });

    [TestMethod]
    public void ACorrelationMatrixStillTakesTheTitlesTheBlockWrites() => UiThread.Run(() =>
    {
        var laid = Lay("geom: corr\nxTitle: Across\nyTitle: Down\n\nwt  mpg  hp\n2.620  21.0  110\n"
                       + "3.440  18.7  175\n1.615  30.4  52\n5.250  10.4  205", PlotFence.Heatmap);

        var said = Of(laid, PlotPiece.AxisTitle).Select(piece => piece.Words?.Glyphs.Text).ToList();

        CollectionAssert.AreEquivalent(new[] { "Across", "Down" }, said);
    });

    [TestMethod]
    public void TheOneIsWhereAColumnMeetsItself() => UiThread.Run(() =>
    {
        // The claim this holds: a tile reads one exactly when the name under it and the name beside it are
        // the same column. Read off the drawn page rather than off the model, so a matrix drawn transposed
        // would fail here.
        var laid = Lay("geom: corr\nlabels: true\n\nwt  mpg  hp\n2.620  21.0  110\n3.440  18.7  175\n"
                       + "1.615  30.4  52\n5.250  10.4  205\n3.190  24.4  62", PlotFence.Heatmap);

        var under = Ticked(laid, PlotPiece.XAxis, piece => piece.Bounds.Left + (piece.Bounds.Width / 2));
        var beside = Ticked(laid, PlotPiece.YAxis, piece => piece.Bounds.Top + (piece.Bounds.Height / 2));

        var seen = 0;

        foreach (var label in Of(laid, PlotPiece.Label))
        {
            var x = label.Bounds.Left + (label.Bounds.Width / 2);
            var y = label.Bounds.Top + (label.Bounds.Height / 2);

            var across = under.OrderBy(tick => Math.Abs(tick.At - x)).First().Says;
            var down = beside.OrderBy(tick => Math.Abs(tick.At - y)).First().Says;

            var says = label.Words?.Glyphs.Text ?? string.Empty;

            Assert.AreEqual(across == down, says == "1",
                            $"the tile at {across} across and {down} down says {says}");

            if (across == down) seen++;
        }

        Assert.AreEqual(3, seen, "three columns meet themselves three times");
    });

    /// <summary>Each of an axis's ticks: what it says, and where it is along the page.</summary>
    private static (string Says, double At)[] Ticked(Laid laid, string axis, Func<Piece, double> along) =>
        [.. laid.Root.SelfAndDescendants()
                 .First(piece => piece.Kind == axis)
                 .SelfAndDescendants()
                 .Where(piece => piece.Kind == PlotPiece.Tick)
                 .Select(piece => (piece.Words?.Glyphs.Text ?? string.Empty, along(piece)))];

    // ── Facets: a panel per value of a column, all on the same scales ───────

    private const string Cylinders =
        "weight  mpg   cyl\n2620  21.0  six\n2320  22.8  four\n3440  18.7  eight\n"
        + "3570  14.3  eight\n3190  24.4  four\n2200  32.4  four\n1615  30.4  four\n5250  10.4  eight\n"
        + "3170  15.8  eight\n2770  19.7  six\n3460  18.1  six\n1835  33.9  four";

    private static string ByCylinders(string settings = "facet: cyl") => settings + "\n\n" + Cylinders;

    private static string[] Strips(Laid laid) =>
        [.. Of(laid, PlotPiece.Strip).Select(piece => piece.Words?.Glyphs.Text ?? string.Empty)];

    [TestMethod]
    public void EachValueOfTheFacetColumnGetsAPanelOfItsOwn() => UiThread.Run(() =>
    {
        CollectionAssert.AreEquivalent(new[] { "six", "four", "eight" }, Strips(Lay(ByCylinders())));
    });

    [TestMethod]
    public void EveryRowIsStillAMarkOnceTheyAreSplitUp() => UiThread.Run(() =>
    {
        // Split across three panels, but not one row lost or drawn twice.
        Assert.AreEqual(12, Marks(Lay(ByCylinders())).Length);
    });

    [TestMethod]
    public void APanelHoldsOnlyItsOwnRows() => UiThread.Run(() =>
    {
        // Three panels in a row, so which one a mark is in is settled by how far across the page it is.
        var laid = Lay(ByCylinders("facet: cyl\nfacetCols: 3"));

        var strips = Of(laid, PlotPiece.Strip)
            .Select(piece => (Says: piece.Words?.Glyphs.Text ?? string.Empty,
                              At: piece.Bounds.Left + (piece.Bounds.Width / 2)))
            .ToList();

        var counted = Marks(laid)
            .GroupBy(mark => strips.OrderBy(strip =>
                Math.Abs(strip.At - (mark.Bounds.Left + (mark.Bounds.Width / 2)))).First().Says)
            .ToDictionary(group => group.Key, group => group.Count());

        Assert.AreEqual(5, counted["four"], "five four-cylinder rows");
        Assert.AreEqual(3, counted["six"], "three sixes");
        Assert.AreEqual(4, counted["eight"], "four eights");
    });

    [TestMethod]
    public void ThePanelsAreAllOnTheSameScales() => UiThread.Run(() =>
    {
        // The point of facets: read one panel against another. Each axis is numbered once for all of them.
        var laid = Lay(ByCylinders());

        var down = Marks(laid, PlotPiece.YAxis);

        Assert.IsTrue(down.Length > 0 && Marks(laid, PlotPiece.XAxis).Length > 0);
        CollectionAssert.AreEqual(down.Distinct().ToArray(), down, "the numbers up the side are written once");
    });

    [TestMethod]
    public void OneValueIsNoDivisionAtAll() => UiThread.Run(() =>
    {
        // Everything in one group is one plot, not one panel with a name over it.
        var laid = Lay("facet: cyl\n\nweight  mpg   cyl\n2620  21.0  six\n2770  19.7  six\n3460  18.1  six");

        Assert.AreEqual(0, Strips(laid).Length);
        Assert.AreEqual(3, Marks(laid).Length);
    });

    [TestMethod]
    public void FacetColsSaysHowManyStandSideBySide() => UiThread.Run(() =>
    {
        var strips = Of(Lay(ByCylinders("facet: cyl\nfacetCols: 1")), PlotPiece.Strip);

        Assert.AreEqual(3, strips.Length);
        Assert.AreEqual(3, strips.Select(piece => Math.Round(piece.Bounds.Top)).Distinct().Count(),
                        "one panel across, so the three of them stand one above another");
    });

    [TestMethod]
    public void AFacetedPlotIsNotSplitByTheColumnItAlreadyPlots() => UiThread.Run(() =>
    {
        // The facet column is not used up as a place, so x and y are still the first two columns.
        Assert.AreEqual(12, Marks(Lay(ByCylinders())).Length, "nothing was left without somewhere to go");
    });
}
