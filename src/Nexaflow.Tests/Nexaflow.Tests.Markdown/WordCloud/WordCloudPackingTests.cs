using Nexaflow.Markdown.WordCloud;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.Markdown.WordCloud;

/// <summary>
/// The packing: the shape of a word on the grid, the ring of places tried at each radius, and the promise
/// that two words never share a cell.
///
/// <para>
/// None of it needs a desktop. What a word looks like is the builder's — only a type engine knows that — but
/// once its outline is a set of filled cells, where it goes is arithmetic, and this is where that arithmetic
/// is held to its word.
/// </para>
/// </summary>
[TestClass]
[CoversNode("wordcloud-packing")]
[CoversNode("wordcloud-stencil")]
public class WordCloudPackingTests
{
    /// <summary>A square of ink, which is the one outline whose cells can be counted by hand.</summary>
    private static IReadOnlyList<IReadOnlyList<(double X, double Y)>> Square(double side, double at = 0) =>
        [[(at, at), (at + side, at), (at + side, at + side), (at, at + side)]];

    // ── The shape of a word ────────────────────────────────────────────────

    [TestMethod]
    public void AWordCoversTheCellsItsOutlineFills()
    {
        var mask = WordMask.Of(Square(12), grid: 4, gap: 0)!;

        Assert.AreEqual(3, mask.Across);
        Assert.AreEqual(3, mask.Down);
        Assert.AreEqual(9, mask.Ink.Count, "a solid square leaves no cell of itself empty");
    }

    [TestMethod]
    public void AWordKnowsWhereItsShapeSitsFromWhereItIsSet()
    {
        // The outline is in pixels from where the word is set, and the grid is laid over it — so a word
        // whose letters start ten pixels along has a mask that starts there too, and the builder puts the
        // word back by that much.
        var mask = WordMask.Of(Square(12, at: 10), grid: 4, gap: 0)!;

        Assert.AreEqual(10, mask.Left, 1e-9);
        Assert.AreEqual(10, mask.Top, 1e-9);
    }

    [TestMethod]
    public void TheGapIsPartOfTheWordsShape()
    {
        var tight = WordMask.Of(Square(12), grid: 4, gap: 0)!;
        var spaced = WordMask.Of(Square(12), grid: 4, gap: 4)!;

        Assert.IsTrue(spaced.Across > tight.Across && spaced.Ink.Count > tight.Ink.Count,
                      "clear air is kept by making the word bigger than its letters");
    }

    [TestMethod]
    public void AnOutlineThatEnclosesNothingIsNoWord()
    {
        Assert.IsNull(WordMask.Of([], grid: 4, gap: 0));
    }

    // ── Where the words go ─────────────────────────────────────────────────

    [TestMethod]
    public void TheFirstWordTakesTheMiddle()
    {
        var board = Board(out var settings);
        var mask = WordMask.Of(Square(40), settings.GridSize, settings.Gap)!;

        Assert.IsTrue(board.TryPlace(mask, out var spot));

        Assert.AreEqual(200, spot.X + mask.Across * settings.GridSize / 2, settings.GridSize * 2);
        Assert.AreEqual(150, spot.Y + mask.Down * settings.GridSize / 2, settings.GridSize * 2);
    }

    [TestMethod]
    public void NoTwoWordsShareACell()
    {
        var board = Board(out var settings);
        var taken = new HashSet<(int X, int Y)>();

        for (var word = 0; word < 40; word++)
        {
            var mask = WordMask.Of(Square(10 + word % 7 * 6), settings.GridSize, settings.Gap)!;
            if (!board.TryPlace(mask, out var spot)) continue;

            foreach (var cell in Cells(mask, spot, settings.GridSize))
                Assert.IsTrue(taken.Add(cell), $"word {word} was put on top of one already down at {cell}");
        }

        Assert.IsTrue(taken.Count > 0, "nothing was placed at all");
    }

    [TestMethod]
    public void EveryWordStaysInsideThePicture()
    {
        var board = Board(out var settings);

        for (var word = 0; word < 30; word++)
        {
            var mask = WordMask.Of(Square(14 + word * 2), settings.GridSize, settings.Gap)!;
            if (!board.TryPlace(mask, out var spot)) continue;

            foreach (var (x, y) in Cells(mask, spot, settings.GridSize))
            {
                Assert.IsTrue(x >= 0 && x < board.Across, $"word {word} runs off the side at {x}");
                Assert.IsTrue(y >= 0 && y < board.Down, $"word {word} runs off the end at {y}");
            }
        }
    }

    [TestMethod]
    public void AWordTooBigForThePictureIsRefused()
    {
        var board = Board(out var settings);
        var mask = WordMask.Of(Square(900), settings.GridSize, settings.Gap)!;

        Assert.IsFalse(board.TryPlace(mask, out _));
    }

    // ── The shape a cloud is packed into ───────────────────────────────────

    [TestMethod]
    public void AStencilHoldsWhatItsOutlineEncloses()
    {
        // Asked in shares of itself rather than in cells, so whoever builds one chooses its resolution and the
        // board samples it against its own grid without either having to agree with the other.
        var stencil = WordCloudStencil.Of(Square(100, at: 0), grid: 2)!;

        Assert.IsTrue(stencil.Holds(0.5, 0.5), "the middle of a filled square is inside it");
        Assert.IsTrue(stencil.Room > 0);
        Assert.IsFalse(stencil.Holds(1.4, 0.5), "and a point off its edge is not");
    }

    [TestMethod]
    public void AnOutlineThatEnclosesNothingIsNoStencil()
    {
        Assert.IsNull(WordCloudStencil.Of([], grid: 2));
        Assert.IsNull(WordCloudStencil.Of((_, _) => false, 20, 20));
    }

    [TestMethod]
    public void NoWordIsPlacedOutsideTheStencil()
    {
        // The whole of how a cloud comes out letter-shaped: every cell outside the shape is taken before a
        // word is placed, and the search finds them occupied without knowing why.
        var settings = WordCloudSettings.Default;

        // The left half of the picture, and nothing else.
        var stencil = WordCloudStencil.Of((x, _) => x < 50, 100, 100)!;
        var board = new WordCloudBoard(400, 300, settings, new WordCloudRandom(settings.Seed), stencil);

        var placed = 0;

        for (var word = 0; word < 30; word++)
        {
            var mask = WordMask.Of(Square(12 + word % 5 * 8), settings.GridSize, settings.Gap)!;
            if (!board.TryPlace(mask, out var spot)) continue;

            placed++;

            foreach (var (x, _) in Cells(mask, spot, settings.GridSize))
                Assert.IsTrue(x < board.Across / 2 + 1, $"word {word} strayed out of the shape at column {x}");
        }

        Assert.IsTrue(placed > 0, "nothing was placed at all");
    }

    [TestMethod]
    public void TheSameBlockAlwaysGivesTheSameCloud()
    {
        // The whole reason the throw is the block's rather than the clock's: a cloud is laid out again on
        // every keystroke, and a picture that moved each time would be unusable to type into.
        CollectionAssert.AreEqual(Spots(seed: 1), Spots(seed: 1));
    }

    [TestMethod]
    public void ADifferentSeedGivesADifferentCloud()
    {
        CollectionAssert.AreNotEqual(Spots(seed: 1), Spots(seed: 2));
    }

    // ── The outline the ring is pulled into ────────────────────────────────

    [TestMethod]
    public void ACircleReachesTheSameDistanceEveryWay()
    {
        foreach (var angle in Angles())
            Assert.AreEqual(1, WordCloudShapes.Reach(WordCloudShape.Circle, angle), 1e-9);
    }

    [TestMethod]
    public void ADiamondReachesFurthestAlongItsAxesAndLeastAtItsSides()
    {
        Assert.AreEqual(1, WordCloudShapes.Reach(WordCloudShape.Diamond, 0), 1e-9);
        Assert.AreEqual(1, WordCloudShapes.Reach(WordCloudShape.Diamond, Math.PI / 2), 1e-9);
        Assert.AreEqual(Math.Sqrt(0.5), WordCloudShapes.Reach(WordCloudShape.Diamond, Math.PI / 4), 1e-9);
    }

    [TestMethod]
    public void EveryShapeReachesSomewhereAtEveryAngle()
    {
        // Including the angles either side of where each formula wraps, which is where a reach worked out
        // from a negative remainder would come back as an infinity or a nothing. Nought is a reach like any
        // other — it is the cardioid's cusp, where the ring closes on the middle.
        foreach (var shape in Enum.GetValues<WordCloudShape>())
            foreach (var angle in Angles())
            {
                var reach = WordCloudShapes.Reach(shape, angle);

                Assert.IsTrue(reach >= 0 && reach < 100 && !double.IsNaN(reach),
                              $"{shape} reaches {reach} at {angle} radians");
            }
    }

    [TestMethod]
    public void EveryShapeNamedIsAShape()
    {
        foreach (var name in WordCloudShapes.Names.Split(", "))
            Assert.IsNotNull(WordCloudShapes.Of(name), name);

        Assert.IsNull(WordCloudShapes.Of("blob"));
    }

    // ── Reading the board ──────────────────────────────────────────────────

    private static WordCloudBoard Board(out WordCloudSettings settings)
    {
        settings = WordCloudSettings.Default;
        return new WordCloudBoard(400, 300, settings, new WordCloudRandom(settings.Seed));
    }

    private static IEnumerable<(int X, int Y)> Cells(WordMask mask, WordSpot spot, double grid)
    {
        var left = (int)Math.Round(spot.X / grid);
        var top = (int)Math.Round(spot.Y / grid);

        foreach (var cell in mask.Ink)
            yield return (left + cell % mask.Across, top + cell / mask.Across);
    }

    private static double[] Spots(int seed)
    {
        var settings = WordCloudSettings.Default with { Seed = seed };
        var board = new WordCloudBoard(400, 300, settings, new WordCloudRandom(seed + 1));
        var spots = new List<double>();

        for (var word = 0; word < 12; word++)
        {
            var mask = WordMask.Of(Square(20 + word * 4), settings.GridSize, settings.Gap)!;
            if (board.TryPlace(mask, out var spot)) spots.AddRange([spot.X, spot.Y]);
        }

        return [.. spots];
    }

    private static IEnumerable<double> Angles()
    {
        for (var step = 0; step < 720; step++) yield return step / 720.0 * Math.PI * 2;
    }
}
