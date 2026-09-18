using System;
using System.Collections.Generic;
using System.Linq;
using Nexaflow.Markdown.Plot;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.Markdown.Plot;

/// <summary>
/// How thickly the points lie, and the contours of it.
///
/// <para>
/// The estimate is MASS's <c>kde2d</c> with Silverman's rule as <c>bandwidth.nrd</c> states it, which is
/// what ggplot2 calls — so the numbers are held against R's rather than against our own opinion of them.
/// The two that carry the rest are the bandwidth, which is a closed form anybody can check, and that the
/// whole estimate sums to one, which is what makes it a density at all.
/// </para>
/// </summary>
[TestClass]
[CoversNode("correlation-plots-statistics")]
public class PlotDensityTests
{
    /// <summary>A bell-shaped drift of points, so the estimate has a shape to find.</summary>
    private static IReadOnlyList<(double X, double Y)> Drift(int many, int seed = 9)
    {
        var throws = new Random(seed);

        return [.. Enumerable.Range(0, many).Select(_ => (Normal(throws) * 2, Normal(throws)))];
    }

    private static double Normal(Random throws) =>
        Math.Sqrt(-2 * Math.Log(1 - throws.NextDouble())) * Math.Cos(2 * Math.PI * throws.NextDouble());

    // ── The width of the kernel ─────────────────────────────────────────────

    [TestMethod]
    public void TheWidthIsSilvermansRuleAsMassStatesIt()
    {
        // bandwidth.nrd is 4 * 1.06 * min(sd, IQR/1.34) * n^(-1/5). Over 1..10 the spread is 3.0276504 and
        // the middle half over 1.34 is 3.3582090, so the spread is the narrower and the rule takes it:
        // 4 * 1.06 * 3.0276504 * 10^(-1/5) = 8.0997493.
        Assert.AreEqual(8.0997493, PlotDensity.Width([1, 2, 3, 4, 5, 6, 7, 8, 9, 10]), 1e-6);
    }

    [TestMethod]
    public void OneValueFarOutDoesNotWidenTheWholeEstimate()
    {
        // Which is the whole point of taking the narrower of the two: the middle half is untouched by it.
        var tight = PlotDensity.Width([1, 2, 3, 4, 5, 6, 7, 8, 9, 10]);
        var strayed = PlotDensity.Width([1, 2, 3, 4, 5, 6, 7, 8, 9, 1000]);

        Assert.IsTrue(strayed < tight * 2, $"{strayed} is not held near {tight} by the middle half");
    }

    [TestMethod]
    public void TooFewValuesToJudgeTakeAWidthOfOne()
    {
        Assert.AreEqual(1, PlotDensity.Width([]));
        Assert.AreEqual(1, PlotDensity.Width([5]));
    }

    // ── The estimate ────────────────────────────────────────────────────────

    [TestMethod]
    public void TheWholeEstimateComesToOne()
    {
        // What makes it a density rather than a heap of numbers. The grid reaches three widths past the
        // points, so all but a sliver of the curve is on it.
        var field = PlotDensity.Estimate(Drift(400));

        var cell = (field.Wide / (field.Across - 1)) * (field.Tall / (field.Up - 1));
        var whole = 0.0;

        foreach (var value in field.At) whole += value;

        Assert.AreEqual(1.0, whole * cell, 0.02);
    }

    [TestMethod]
    public void ItIsThickestWhereThePointsAre()
    {
        // Two drifts far apart, and the estimate finds both of them.
        IReadOnlyList<(double X, double Y)> points =
            [.. Drift(200).Select(point => (point.X - 20, point.Y)),
             .. Drift(200, seed: 4).Select(point => (point.X + 20, point.Y))];

        var field = PlotDensity.Estimate(points);
        var most = field.Most;

        var peaks = 0;

        for (var i = 1; i < field.Across - 1; i++)
            for (var j = 1; j < field.Up - 1; j++)
                if (field.At[i, j] > most * 0.5) peaks++;

        Assert.IsTrue(peaks > 0, "the estimate found nothing at all");

        // And nothing much in the empty stretch between them.
        var middle = field.At[field.Across / 2, field.Up / 2];

        Assert.IsTrue(middle < most * 0.5, $"the gap between the two drifts is not a gap ({middle} of {most})");
    }

    [TestMethod]
    public void AWiderKernelIsASmootherEstimate()
    {
        var tight = PlotDensity.Estimate(Drift(300), adjust: 0.5).Most;
        var loose = PlotDensity.Estimate(Drift(300), adjust: 2).Most;

        // Spread over more ground, so the highest point is lower.
        Assert.IsTrue(loose < tight, $"{loose} is not below {tight}");
    }

    [TestMethod]
    public void AWidthWrittenOutIsTheWidthUsed()
    {
        var told = PlotDensity.Estimate(Drift(200), bandwidth: (8, 8));
        var judged = PlotDensity.Estimate(Drift(200));

        Assert.AreNotEqual(judged.Most, told.Most);
    }

    [TestMethod]
    public void NoPointsIsAnEmptyField()
    {
        Assert.AreEqual(0, PlotDensity.Estimate([]).Most);
    }

    // ── The contours ────────────────────────────────────────────────────────

    [TestMethod]
    public void EveryContourIsAClosedRing()
    {
        // The grid reaches past the points into where the density has all but gone, so nothing is cut
        // off at the edge — which is what lets a level be filled as well as drawn.
        var field = PlotDensity.Estimate(Drift(400));

        foreach (var level in PlotDensity.Contours(field, 6))
            foreach (var ring in level.Rings)
            {
                Assert.IsTrue(ring.Points.Count >= 3, "a ring of two points is a line");

                var first = ring.Points[0];
                var last = ring.Points[^1];
                var apart = Math.Sqrt(((first.X - last.X) * (first.X - last.X))
                                    + ((first.Y - last.Y) * (first.Y - last.Y)));

                Assert.IsTrue(apart < field.Wide / 20, $"a ring at {level.At} does not close ({apart} apart)");
            }
    }

    [TestMethod]
    public void MoreLevelsAreMoreContours()
    {
        var field = PlotDensity.Estimate(Drift(400));

        Assert.IsTrue(PlotDensity.Contours(field, 12).Count > PlotDensity.Contours(field, 4).Count);
    }

    [TestMethod]
    public void AThickerLevelLiesInsideAThinnerOne()
    {
        // Contours nest rather than cross, which is what makes filling them from the outside in read as
        // a cloud thickening.
        var field = PlotDensity.Estimate(Drift(500));
        var levels = PlotDensity.Contours(field, 5);

        Assert.IsTrue(levels.Count >= 2);

        for (var which = 1; which < levels.Count; which++)
        {
            Assert.IsTrue(levels[which].At > levels[which - 1].At, "the levels run from thin to thick");

            Assert.IsTrue(Reach(levels[which]) <= Reach(levels[which - 1]) + 1e-6,
                          $"the contour at {levels[which].At} reaches outside the one below it");
        }
    }

    [TestMethod]
    public void AFieldOfNothingHasNoContours()
    {
        Assert.AreEqual(0, PlotDensity.Contours(PlotDensity.Estimate([])).Count);
    }

    /// <summary>How far across a level's rings reach altogether.</summary>
    private static double Reach(PlotLevel level)
    {
        var points = level.Rings.SelectMany(ring => ring.Points).ToList();

        return points.Count == 0 ? 0 : points.Max(point => point.X) - points.Min(point => point.X);
    }
}
