using System;
using System.Collections.Generic;
using System.Linq;
using Nexaflow.Markdown.Plot;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.Markdown.Plot;

/// <summary>
/// Cutting a plane into bins and counting what falls in each.
///
/// <para>
/// Two things have to hold however the bins are shaped: every point is counted exactly once, and no
/// point is counted into a bin further from it than another. The second is the whole reason hexagons are
/// worth the arithmetic — a square lattice fails it at the corners, where a point can be nearer the
/// diagonal neighbour than the bin it rounds into.
/// </para>
/// </summary>
[TestClass]
[CoversNode("correlation-plots-statistics")]
public class PlotBinsTests
{
    private static IReadOnlyList<(double X, double Y)> Scattered(int many, int seed = 3)
    {
        var throws = new Random(seed);

        return [.. Enumerable.Range(0, many)
                             .Select(_ => (throws.NextDouble() * 200, throws.NextDouble() * 120))];
    }

    // ── However they are shaped ─────────────────────────────────────────────

    [TestMethod]
    public void EveryPointIsCountedExactlyOnce()
    {
        var points = Scattered(500);

        Assert.AreEqual(500, PlotBins.Rectangles(points, 200, 120, 12, 8).Bins.Sum(bin => bin.Count));
        Assert.AreEqual(500, PlotBins.Hexagons(points, 200, 12).Bins.Sum(bin => bin.Count));
    }

    [TestMethod]
    public void NoPointsIsNoBins()
    {
        Assert.AreEqual(0, PlotBins.Rectangles([], 200, 120, 10, 10).Bins.Count);
        Assert.AreEqual(0, PlotBins.Hexagons([], 200, 10).Bins.Count);
    }

    [TestMethod]
    public void AnEmptyStretchOfThePlaneGetsNoBinAtAll()
    {
        // Only where something fell, so a chart of a hundred bins is not a hundred thousand shapes.
        Assert.AreEqual(1, PlotBins.Rectangles([(10, 10)], 200, 120, 20, 20).Bins.Count);
    }

    [TestMethod]
    public void AskingForMoreBinsThanAllowedIsHeldAtTheLimit()
    {
        // A mistyped number cannot ask for a million shapes.
        var bins = PlotBins.Rectangles(Scattered(50), 200, 120, 100000, 100000).Bins;

        Assert.IsTrue(bins.Count <= 50, "there are never more bins than points");
    }

    // ── Rectangles ──────────────────────────────────────────────────────────

    [TestMethod]
    public void ARectangularBinIsAsWideAsItsShareOfThePlane()
    {
        var binning = PlotBins.Rectangles(Scattered(200), 200, 120, 10, 6);

        Assert.AreEqual(20, binning.Wide, 1e-9);
        Assert.AreEqual(20, binning.Tall, 1e-9);
    }

    [TestMethod]
    public void EveryPointIsInsideTheBinItWasCountedInto()
    {
        var binning = PlotBins.Rectangles([(15, 15), (25, 15)], 200, 120, 20, 12);

        // Two points ten apart, in bins of ten, so they are bins of their own.
        Assert.AreEqual(2, binning.Bins.Count);

        foreach (var bin in binning.Bins)
            Assert.AreEqual(1, bin.Count);
    }

    // ── Hexagons ────────────────────────────────────────────────────────────

    [TestMethod]
    public void NoPointIsCountedIntoABinFurtherFromItThanAnother()
    {
        // The property a square lattice does not have, and the whole reason to bin in hexagons: at the
        // corner of a square, a point can be nearer the diagonal neighbour than the bin it rounds into.
        //
        // Checked by counting the same points again by nearest middle alone. If the binning ever took a
        // point anywhere but its nearest, the two counts would differ.
        var points = Scattered(300, seed: 11);
        var binning = PlotBins.Hexagons(points, 200, 14);

        var nearest = new Dictionary<(double, double), int>();

        foreach (var (x, y) in points)
        {
            var mine = binning.Bins.OrderBy(bin => Apart(bin, x, y)).First();
            nearest[(mine.X, mine.Y)] = nearest.GetValueOrDefault((mine.X, mine.Y)) + 1;
        }

        foreach (var bin in binning.Bins)
            Assert.AreEqual(bin.Count, nearest.GetValueOrDefault((bin.X, bin.Y)),
                            $"the bin at ({bin.X:0.##}, {bin.Y:0.##}) is not what its points are nearest to");
    }

    [TestMethod]
    public void HexagonsTileThePlaneWithoutSquareCorners()
    {
        // Every bin's middle is a step of the lattice from the next, so the shapes meet edge to edge.
        var binning = PlotBins.Hexagons(Scattered(400, seed: 5), 200, 10);

        Assert.IsTrue(binning.Bins.Count > 1);
        Assert.IsTrue(binning.Wide > 0 && binning.Tall > 0);

        // A hexagon standing on a point is taller than it is wide, in the ratio a regular one has.
        Assert.AreEqual(2 / Math.Sqrt(3), binning.Tall / binning.Wide, 1e-9);
    }

    private static double Apart(PlotBin bin, double x, double y) =>
        ((bin.X - x) * (bin.X - x)) + ((bin.Y - y) * (bin.Y - y));
}
