using System;
using System.Collections.Generic;
using System.Linq;
using Nexaflow.Markdown.Plot;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.Markdown.Plot;

/// <summary>
/// The arithmetic behind a correlation, held against R's own answers for <c>mtcars</c> — the data set
/// every statistics text works the same examples on, so the numbers here are checkable by anybody rather
/// than being whatever this happened to produce.
/// </summary>
[TestClass]
[CoversNode("correlation-plots-statistics")]
public class PlotFitsTests
{
    /// <summary>mtcars, weight against fuel economy: the 32 cars, as (wt, mpg).</summary>
    private static readonly (double X, double Y)[] Cars =
    [
        (2.620, 21.0), (2.875, 21.0), (2.320, 22.8), (3.215, 21.4), (3.440, 18.7), (3.460, 18.1),
        (3.570, 14.3), (3.190, 24.4), (3.150, 22.8), (3.440, 19.2), (3.440, 17.8), (4.070, 16.4),
        (3.730, 17.3), (3.780, 15.2), (5.250, 10.4), (5.424, 10.4), (5.345, 14.7), (2.200, 32.4),
        (1.615, 30.4), (1.835, 33.9), (2.465, 21.5), (3.520, 15.5), (3.435, 15.2), (3.840, 13.3),
        (3.845, 19.2), (1.935, 27.3), (2.140, 26.0), (1.513, 30.4), (3.170, 15.8), (2.770, 19.7),
        (3.570, 15.0), (2.780, 21.4),
    ];

    // ── Against R ───────────────────────────────────────────────────────────

    [TestMethod]
    public void PearsonsRIsWhatRReportsForMtcars()
    {
        // R: cor(mtcars$wt, mtcars$mpg) is -0.8676594.
        var said = PlotFits.Pearson(Cars)!;

        Assert.AreEqual(-0.8676594, said.R, 1e-6);
        Assert.AreEqual(0.7528328, said.RSquared, 1e-6);
        Assert.AreEqual(32, said.N);
    }

    [TestMethod]
    public void TheStraightLineIsWhatRsLmReports()
    {
        // R: coef(lm(mpg ~ wt, mtcars)) is (Intercept) 37.28513, wt -5.344472.
        var line = PlotFits.Straight(Cars, band: false)!;

        var first = line.Along[0];
        var last = line.Along[^1];

        var slope = (last.Y - first.Y) / (last.X - first.X);
        var intercept = first.Y - (slope * first.X);

        Assert.AreEqual(-5.344472, slope, 1e-5);
        Assert.AreEqual(37.28513, intercept, 1e-4);
    }

    [TestMethod]
    public void SpearmansRhoIsWhatRReportsForMtcars()
    {
        // R: cor(mtcars$wt, mtcars$mpg, method = "spearman") is -0.886422.
        Assert.AreEqual(-0.886422, PlotFits.Spearman(Cars)!.R, 1e-5);
    }

    [TestMethod]
    public void KendallsTauIsWhatRReportsForMtcars()
    {
        // R: cor(mtcars$wt, mtcars$mpg, method = "kendall") is -0.7278321.
        Assert.AreEqual(-0.7278321, PlotFits.Kendall(Cars)!.R, 1e-5);
    }

    [TestMethod]
    public void ChanceWouldNotHaveDoneAsWell()
    {
        // R: cor.test(mtcars$wt, mtcars$mpg)$p.value is 1.294e-10.
        Assert.IsTrue(PlotFits.Pearson(Cars)!.P < 1e-8);
    }

    // ── What has to hold whatever the numbers ───────────────────────────────

    [TestMethod]
    public void APerfectLineIsAPerfectCorrelation()
    {
        (double, double)[] straight = [(1, 2), (2, 4), (3, 6), (4, 8), (5, 10)];

        Assert.AreEqual(1, PlotFits.Pearson(straight)!.R, 1e-12);
        Assert.AreEqual(1, PlotFits.Spearman(straight)!.R, 1e-12);
        Assert.AreEqual(1, PlotFits.Kendall(straight)!.R, 1e-12);
    }

    [TestMethod]
    public void TurningOneOfThemAboutTurnsTheCorrelationAbout()
    {
        var flipped = Cars.Select(point => (point.X, Y: -point.Y)).ToArray();

        Assert.AreEqual(-PlotFits.Pearson(Cars)!.R, PlotFits.Pearson(flipped)!.R, 1e-12);
        Assert.AreEqual(-PlotFits.Kendall(Cars)!.R, PlotFits.Kendall(flipped)!.R, 1e-12);
    }

    [TestMethod]
    public void RanksSeeAMonotoneCurveThatAStraightLineDoesNot()
    {
        // The whole reason to have Spearman as well: y climbs with x every time, and only the rank
        // correlation says so outright.
        (double, double)[] curved = [.. Enumerable.Range(1, 12).Select(x => ((double)x, Math.Pow(x, 4)))];

        Assert.AreEqual(1, PlotFits.Spearman(curved)!.R, 1e-12);
        Assert.IsTrue(PlotFits.Pearson(curved)!.R < 0.95);
    }

    [TestMethod]
    public void TooFewPointsSayNothingRatherThanSomethingWrong()
    {
        Assert.IsNull(PlotFits.Pearson([(1, 1), (2, 2)]));
        Assert.IsNull(PlotFits.Straight([(1, 1)], band: false));
    }

    [TestMethod]
    public void PointsAllInALineUpAndDownHaveNoLineThroughThem()
    {
        // Every x the same, so there is no slope to find rather than an infinite one.
        Assert.IsNull(PlotFits.Straight([(2, 1), (2, 5), (2, 9)], band: false));
        Assert.IsNull(PlotFits.Pearson([(2, 1), (2, 5), (2, 9)]));
    }

    // ── The band ────────────────────────────────────────────────────────────

    [TestMethod]
    public void TheBandIsNarrowestInTheMiddleOfThePoints()
    {
        // Which is why it is a waist rather than a ribbon: the fit is surest where the points are.
        var line = PlotFits.Straight(Cars, band: true)!;

        var middle = Apart(line, line.Along.Count / 2);

        Assert.IsTrue(middle < Apart(line, 0), "the band is no narrower in the middle than at the start");
        Assert.IsTrue(middle < Apart(line, line.Along.Count - 1), "nor than at the end");
    }

    [TestMethod]
    public void NoBandIsAskedForAndNoneIsGiven()
    {
        var line = PlotFits.Straight(Cars, band: false)!;

        Assert.IsNull(line.Below);
        Assert.IsNull(line.Above);
    }

    [TestMethod]
    public void MoreConfidenceIsAWiderBand()
    {
        var usual = PlotFits.Straight(Cars, band: true, level: 0.95)!;
        var surer = PlotFits.Straight(Cars, band: true, level: 0.99)!;

        Assert.IsTrue(Apart(surer, 0) > Apart(usual, 0));
    }

    // ── Loess ───────────────────────────────────────────────────────────────

    [TestMethod]
    public void ACurveFollowsWhatAStraightLineCannot()
    {
        // A hill: a straight line through it is flat, and a curve is not.
        (double, double)[] hill =
            [.. Enumerable.Range(0, 40).Select(at => (at / 4.0, -Math.Pow((at / 4.0) - 5, 2)))];

        var straight = PlotFits.Straight(hill, band: false)!;
        var curved = PlotFits.Curved(hill, band: false)!;

        Assert.IsTrue(Reach(curved) > Reach(straight) * 2,
                      "the curve does not follow the hill any better than the line does");
    }

    [TestMethod]
    public void TooFewPointsToCurveThroughAreFittedStraight()
    {
        var line = PlotFits.Curved([(1, 1), (2, 3), (3, 2)], band: false);

        Assert.IsNotNull(line);
    }

    private static double Apart(PlotLine line, int at) => line.Above![at].Y - line.Below![at].Y;

    private static double Reach(PlotLine line) =>
        line.Along.Max(point => point.Y) - line.Along.Min(point => point.Y);
}
