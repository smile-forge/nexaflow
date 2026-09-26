using Nexaflow.Markdown.Ast;
using Nexaflow.Markdown.Plot;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.Markdown.Plot;

/// <summary>
/// The correlation matrix a block asks for with <c>geom: corr</c>: a coefficient per pair of numeric
/// columns, worked out by the pipeline and hung under the block, because no cell of the table holds one.
/// </summary>
[TestClass]
[CoversNode("correlation-plots-reading")]
public class PlotCorrelationsTests
{
    /// <summary>mtcars' weight, miles per gallon and horsepower, in the order R lists them.</summary>
    private static readonly double[] Weight =
    [
        2.620, 2.875, 2.320, 3.215, 3.440, 3.460, 3.570, 3.190, 3.150, 3.440, 3.440, 4.070,
        3.730, 3.780, 5.250, 5.424, 5.345, 2.200, 1.615, 1.835, 2.465, 3.520, 3.435, 3.840,
        3.845, 1.935, 2.140, 1.513, 3.170, 2.770, 3.570, 2.780,
    ];

    private static readonly double[] Mpg =
    [
        21.0, 21.0, 22.8, 21.4, 18.7, 18.1, 14.3, 24.4, 22.8, 19.2, 17.8, 16.4,
        17.3, 15.2, 10.4, 10.4, 14.7, 32.4, 30.4, 33.9, 21.5, 15.5, 15.2, 13.3,
        19.2, 27.3, 26.0, 30.4, 15.8, 19.7, 15.0, 21.4,
    ];

    private static readonly double[] Power =
    [
        110, 110, 93, 110, 175, 105, 245, 62, 95, 123, 123, 180,
        180, 180, 205, 215, 230, 66, 52, 65, 97, 150, 150, 245,
        175, 66, 91, 113, 264, 175, 335, 109,
    ];

    private static string Cars(string settings = "geom: corr")
    {
        var lines = new List<string> { settings, string.Empty, "wt mpg hp" };

        for (var at = 0; at < Weight.Length; at++)
            lines.Add($"{Weight[at]} {Mpg[at]} {Power[at]}");

        return string.Join("\n", lines);
    }

    private static IReadOnlyList<(string Across, string Down, double R)> Read(string source) =>
        (PlotPipeline.Of(PlotFence.Heatmap).Run(PlotParser.Parse(source)) as PlotBlockNode)?.Correlations ?? [];

    private static double Between(IReadOnlyList<(string Across, string Down, double R)> pairs,
                                  string across, string down) =>
        pairs.Single(pair => pair.Across == across && pair.Down == down).R;

    [TestMethod]
    public void EveryPairOfNumericColumnsIsCorrelated()
    {
        var pairs = Read(Cars());

        Assert.AreEqual(9, pairs.Count, "three columns, every one against every one");
        CollectionAssert.AreEquivalent(new[] { "wt", "mpg", "hp" },
                                       pairs.Select(pair => pair.Across).Distinct().ToArray());
    }

    [TestMethod]
    public void TheCoefficientsAreWhatRReportsForMtcars()
    {
        var pairs = Read(Cars());

        Assert.AreEqual(-0.8676594, Between(pairs, "wt", "mpg"), 1e-6);
        Assert.AreEqual(-0.7761684, Between(pairs, "hp", "mpg"), 1e-6);
        Assert.AreEqual(0.6587479, Between(pairs, "hp", "wt"), 1e-6);
    }

    [TestMethod]
    public void AColumnAgreesPerfectlyWithItself()
    {
        var pairs = Read(Cars());

        foreach (var name in new[] { "wt", "mpg", "hp" })
            Assert.AreEqual(1.0, Between(pairs, name, name), 1e-9, $"{name} against itself");
    }

    [TestMethod]
    public void ItReadsTheSameEitherWayRound()
    {
        var pairs = Read(Cars());

        Assert.AreEqual(Between(pairs, "wt", "mpg"), Between(pairs, "mpg", "wt"), 1e-12);
    }

    [TestMethod]
    public void TheMethodWrittenIsTheCoefficientWorkedOut()
    {
        // Spearman ranks the values, so it is not the same number as Pearson's on the same columns.
        var ranked = Read(Cars("geom: corr\nmethod: spearman"));

        Assert.AreEqual(-0.886422, Between(ranked, "wt", "mpg"), 1e-5);
    }

    [TestMethod]
    public void AColumnOfNamesHasNoCoefficient()
    {
        var pairs = Read("geom: corr\n\nname weight mpg\nMazda 2.620 21.0\nDatsun 2.320 22.8\nMerc 3.440 19.2");

        Assert.IsFalse(pairs.Any(pair => pair.Across == "name" || pair.Down == "name"),
                       "a column of names moves with nothing");
        Assert.AreEqual(4, pairs.Count, "the two numeric columns, each against both");
    }

    [TestMethod]
    public void OneNumericColumnCorrelatesWithNothing()
    {
        Assert.AreEqual(0, Read("geom: corr\n\nname weight\nMazda 2.620\nDatsun 2.320").Count);
    }

    [TestMethod]
    public void NothingIsCorrelatedUnlessTheBlockAsksForIt()
    {
        Assert.AreEqual(0, Read(Cars("geom: tile")).Count);
        Assert.AreEqual(0, Read(Cars("x: wt")).Count);
    }

    [TestMethod]
    public void WorkingItOutLeavesTheBlockPrintingAsItWasWritten()
    {
        var source = Cars();

        Assert.AreEqual(source, new Nexaflow.Markdown.Plot.Stages.ResolveSettings(PlotFence.Heatmap).Run(PlotParser.Parse(source)).Print(),
                        "a coefficient is a fact about the block, and takes up none of it");
    }

    [TestMethod]
    public void ARowMissingAValueIsLeftOutOfThatPairAlone()
    {
        // The second row says nothing about b. Left out, b still runs perfectly with a; read as a nought it
        // would not, which is the mistake this is here to catch.
        var pairs = Read("geom: corr\n\na b c\n1 2 3\n2 x 5\n3 6 7\n4 8 9");

        Assert.AreEqual(1.0, Between(pairs, "a", "b"), 1e-9, "the three rows b has run perfectly with a");
        Assert.AreEqual(1.0, Between(pairs, "a", "c"), 1e-9, "and c, which is missing nothing, still sees all four");
    }

    [TestMethod]
    public void APairWithTooFewRowsInCommonSaysNothing()
    {
        // Two points are always perfectly correlated, so a coefficient from them would mean nothing.
        var pairs = Read("geom: corr\n\na b\n1 2\n2 x\n3 6");

        Assert.IsFalse(pairs.Any(pair => pair.Across != pair.Down),
                       "a and b share two rows, which is too few to say anything");
    }
}
