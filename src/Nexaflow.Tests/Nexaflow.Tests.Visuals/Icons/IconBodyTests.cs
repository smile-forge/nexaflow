using Nexaflow.Icons;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.Visuals.Icons;

/// <summary>
/// A pack's icon read from its SVG into what it draws (<see cref="IconBody"/>): every outline carried into the icon's own
/// coordinates, arcs drawn as the curves making them, and each fill and stroke painted as SVG paints it — inherited, faded,
/// graded, clipped and ordered as written.
/// </summary>
[TestClass]
[CoversNode("icons-packs")]
public class IconBodyTests
{
    private static IReadOnlyList<VectorLayer> Read(string body) => IconBody.Read(body, VectorMatrix.Identity, 24, 24);

    private static VectorPoint P(double x, double y) => new(x, y);

    private static void Near(VectorPoint expected, VectorPoint actual) =>
        Assert.IsTrue(Math.Abs(expected.X - actual.X) < 1e-9 && Math.Abs(expected.Y - actual.Y) < 1e-9, $"expected {expected}, was {actual}");

    [TestMethod, TestCategory("Unit")]
    public void PathDataIsReadAbsolute_EachCommandWhereSvgPutsIt()
    {
        var figures = Read("<path d=\"M2 2h4v4H2zm10 0l2 2-2 2\"/>").Single().Shape.Figures;

        Assert.AreEqual(2, figures.Count);
        Assert.IsTrue(figures[0].Closed);
        CollectionAssert.AreEqual(new[] { P(6, 2), P(6, 6), P(2, 6) }, figures[0].Segments.Select(segment => segment.To).ToArray());

        Assert.AreEqual(P(12, 2), figures[1].Start, "after a close the pen is back where its figure began");
        CollectionAssert.AreEqual(new[] { P(14, 4), P(12, 6) }, figures[1].Segments.Select(segment => segment.To).ToArray());
        Assert.IsFalse(figures[1].Closed);
    }

    [TestMethod, TestCategory("Unit")]
    public void ASmoothCurveTakesItsFirstControlFromTheCurveBefore()
    {
        var cubic = (VectorCubic)Read("<path d=\"M0 0C0 10 10 10 10 0S20-10 20 0\"/>").Single().Shape.Figures[0].Segments[1];
        Assert.AreEqual(P(10, -10), cubic.First);

        var quadratic = (VectorQuadratic)Read("<path d=\"M0 0Q5 10 10 0T20 0\"/>").Single().Shape.Figures[0].Segments[1];
        Assert.AreEqual(P(15, -10), quadratic.Control);
    }

    [TestMethod, TestCategory("Unit")]
    public void AnArcIsTheCurvesMakingIt_AQuarterTurnEach_ItsFlagsWrittenTogetherOrApart()
    {
        foreach (var data in (string[])["M0 10A10 10 0 0 1 20 10", "M0 10a10 10 0 0120 0"])
        {
            var figure = Read($"<path d=\"{data}\"/>").Single().Shape.Figures.Single();

            Assert.AreEqual(2, figure.Segments.Count, data);
            Assert.IsTrue(figure.Segments.All(segment => segment is VectorCubic), data);
            Near(P(10, 0), figure.Segments[0].To);
            Assert.AreEqual(P(20, 10), figure.Segments[1].To, data);
        }
    }

    [TestMethod, TestCategory("Unit")]
    public void ShapesAreTheOutlinesTheyDraw()
    {
        var circle = Read("<circle cx=\"12\" cy=\"12\" r=\"10\"/>").Single().Shape.Figures.Single();
        Assert.IsTrue(circle.Closed);
        Assert.AreEqual(4, circle.Segments.Count);
        Assert.AreEqual(P(22, 12), circle.Start);

        var square = Read("<rect x=\"1\" y=\"2\" width=\"3\" height=\"4\"/>").Single().Shape.Figures.Single();
        CollectionAssert.AreEqual(new[] { P(4, 2), P(4, 6), P(1, 6) }, square.Segments.Select(segment => segment.To).ToArray());

        var rounded = Read("<rect width=\"10\" height=\"10\" rx=\"2\"/>").Single().Shape.Figures.Single();
        Assert.AreEqual(4, rounded.Segments.OfType<VectorCubic>().Count(), "a corner each, ry taking rx where it is not written");

        Assert.AreEqual(0, Read("<rect width=\"0\" height=\"4\"/>").Count, "a rectangle with no width draws nothing");
    }

    [TestMethod, TestCategory("Unit")]
    public void TransformsNest_TheOuterWrittenFirst_AndAStrokeWidensAsTheyScale()
    {
        var layer = Read("<g transform=\"translate(10 0)\"><path transform=\"scale(2)\" d=\"M1 1L2 2\" fill=\"none\" stroke=\"#000\" stroke-width=\"1.5\"/></g>").Single();

        Assert.AreEqual(P(12, 2), layer.Shape.Figures[0].Start);
        Assert.AreEqual(P(14, 4), layer.Shape.Figures[0].Segments[0].To);
        Assert.IsNull(layer.Fill);
        Assert.AreEqual(3, layer.StrokeWidth, 1e-9);
    }

    [TestMethod, TestCategory("Unit")]
    public void PaintsAreInherited_FadedByTheirOpacity_AndWrittenAsAttributesOrStyle()
    {
        Assert.AreEqual(new VectorSolid(new VectorColor(128, 255, 0, 0)), Read("<g fill=\"#f00\" fill-opacity=\".5\"><path d=\"M0 0h1v1z\"/></g>").Single().Fill);
        Assert.AreEqual(new VectorSolid(new VectorColor(255, 0, 255, 0)), Read("<path style=\"fill:#00ff00;stroke:none\" d=\"M0 0h1v1z\"/>").Single().Fill);
        Assert.AreEqual(new VectorSolid(new VectorColor(255, 0, 0, 0)), Read("<path d=\"M0 0h1v1z\"/>").Single().Fill, "black where nothing says otherwise");

        Assert.AreEqual(new VectorCurrent(1), Read("<path fill=\"currentColor\" d=\"M0 0h1v1z\"/>").Single().Fill, "the colour the icon is drawn in");
        Assert.AreEqual(new VectorSolid(new VectorColor(255, 0, 0, 255)), Read("<g color=\"#00f\"><path fill=\"currentColor\" d=\"M0 0h1v1z\"/></g>").Single().Fill);

        Assert.AreEqual(0, Read("<path fill=\"none\" d=\"M0 0h1v1z\"/>").Count, "neither filled nor stroked, it draws nothing");
        Assert.IsTrue(Read("<path paint-order=\"stroke\" stroke=\"#000\" fill=\"#fff\" d=\"M0 0h1v1z\"/>").Single().StrokeFirst);
        Assert.IsTrue(Read("<path fill-rule=\"evenodd\" d=\"M0 0h1v1z\"/>").Single().Shape.EvenOdd);
    }

    [TestMethod, TestCategory("Unit")]
    public void AGradientInTheOutlinesCoordinatesIsCarriedByItsOwnTransformAndTheOutlines()
    {
        const string body = "<defs><linearGradient id=\"g\" x1=\"0\" y1=\"0\" x2=\"10\" y2=\"0\" gradientUnits=\"userSpaceOnUse\" gradientTransform=\"translate(5 0)\">"
                            + "<stop offset=\"0\" stop-color=\"#fff\"/><stop offset=\"1\" stop-color=\"#000\" stop-opacity=\".5\"/></linearGradient></defs>"
                            + "<g transform=\"scale(2)\"><path fill=\"url(#g)\" d=\"M0 0h10v10H0z\"/></g>";

        var linear = (VectorLinear)Read(body).Single().Fill!;

        Assert.AreEqual(P(10, 0), linear.Transform.Transform(linear.From));
        Assert.AreEqual(P(30, 0), linear.Transform.Transform(linear.To));
        CollectionAssert.AreEqual(new[] { new VectorStop(new VectorColor(255, 255, 255, 255), 0), new VectorStop(new VectorColor(128, 0, 0, 0), 1) }, linear.Stops.ToArray());
    }

    [TestMethod, TestCategory("Unit")]
    public void AGradientAcrossItsOutlineIsMeasuredInItsBounds_TakingWhatItDoesNotSayFromTheOneItNames()
    {
        const string body = "<defs><linearGradient id=\"a\"><stop offset=\"0\" stop-color=\"#fff\"/><stop offset=\"100%\" stop-color=\"#000\"/></linearGradient>"
                            + "<linearGradient id=\"b\" href=\"#a\" x2=\"0\" y2=\"1\"/></defs><rect x=\"2\" y=\"2\" width=\"10\" height=\"20\" fill=\"url(#b)\"/>";

        var linear = (VectorLinear)Read(body).Single().Fill!;

        Assert.AreEqual(2, linear.Stops.Count);
        Assert.AreEqual(P(2, 2), linear.Transform.Transform(linear.From));
        Assert.AreEqual(P(2, 22), linear.Transform.Transform(linear.To));
    }

    [TestMethod, TestCategory("Unit")]
    public void ARadialGradientRunsOutFromItsFocus_ItsCentreUnlessItSaysOtherwise()
    {
        const string body = "<defs><radialGradient id=\"r\" cx=\"5\" cy=\"6\" r=\"4\" gradientUnits=\"userSpaceOnUse\"><stop offset=\"0\" stop-color=\"#ff0\"/>"
                            + "<stop offset=\"1\" stop-color=\"#f00\"/></radialGradient></defs><path fill=\"url(#r)\" d=\"M0 0h10v10H0z\"/>";

        var radial = (VectorRadial)Read(body).Single().Fill!;

        Assert.AreEqual((P(5, 6), 4.0, P(5, 6)), (radial.Centre, radial.Radius, radial.Focus));
    }

    [TestMethod, TestCategory("Unit")]
    public void AUseSetsWhatItNamesWhereItSays_InWhatItPaintsWith()
    {
        foreach (var href in (string[])["href", "xlink:href"])
        {
            var layer = Read($"<defs><path id=\"p\" d=\"M0 0h1v1z\"/></defs><use {href}=\"#p\" x=\"3\" y=\"4\" fill=\"#0f0\"/>").Single();

            Assert.AreEqual(P(3, 4), layer.Shape.Figures[0].Start, href);
            Assert.AreEqual(new VectorSolid(new VectorColor(255, 0, 255, 0)), layer.Fill, href);
        }
    }

    [TestMethod, TestCategory("Unit")]
    public void AClipPathIsInTheCoordinatesOfWhatNamesIt()
    {
        var layer = Read("<clipPath id=\"c\"><path d=\"M0 0h5v5H0z\"/></clipPath><g clip-path=\"url(#c)\" transform=\"translate(10 0)\"><path d=\"M0 0h10v10H0z\"/></g>").Single();

        Assert.AreEqual(P(10, 0), layer.Clips.Single().Shapes.Single().Figures[0].Start);
    }

    [TestMethod, TestCategory("Unit")]
    public void PathDataThatGoesWrongDrawsWhatCameBefore() =>
        Assert.AreEqual(1, Read("<path d=\"M0 0L5 5L\"/>").Single().Shape.Figures.Single().Segments.Count);
}
