using System;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using Nexaflow.Tests.Fixtures;
using Nexaflow.Tests.Visuals.Editing;
using Nexaflow.Visuals.Text.Editing;
using Nexaflow.Visuals.Text.Markdown.Mermaid;

namespace Nexaflow.Tests.Visuals.Markdown.Mermaid;

/// <summary>
/// A line from one thing a diagram draws to another: it stands in a band round itself so it can be pressed, ends in what its
/// diagram says, and stops short of a head it would otherwise run through.
/// </summary>
[TestClass]
[TestCategory("UI")]
[CoversNode("mermaid-diagram-kit")]
public class DiagramConnectorTests
{
    private static Piece Drawn(DiagramHead end, DiagramStroke? stroke = null, params Point[] route)
    {
        var build = new LayoutBuilder();
        build.Open("page");
        DiagramConnector.Draw(build, "Edge", new TestPart(0, 3), route.Length > 0 ? route : [new Point(0, 50), new Point(100, 50)],
                              stroke ?? new DiagramStroke(Brushes.Black), end: end);
        build.Close();

        return build.Seal().Root.SelfAndDescendants().Single(piece => piece.Kind == "Edge");
    }

    [TestMethod]
    public void APressNearTheLineMeansIt_AndOneAwayFromItDoesNot() => UiThread.Run(() =>
    {
        var edge = Drawn(DiagramHead.Arrow);

        Assert.IsTrue(edge.Region!.FillContains(new Point(50, 53)), "three pixels off the line is on it");
        Assert.IsFalse(edge.Region!.FillContains(new Point(50, 70)), "twenty is not");
    });

    [TestMethod]
    public void EveryHeadIsDrawn() => UiThread.Run(() =>
    {
        var plain = Drawn(DiagramHead.None).Marks.Length;

        foreach (var head in Enum.GetValues<DiagramHead>().Where(head => head != DiagramHead.None))
            Assert.IsTrue(Drawn(head).Marks.Length > plain, $"{head} draws something at the end of the line");
    });

    [TestMethod]
    public void TheLineStopsWhereAHollowHeadStarts() => UiThread.Run(() =>
    {
        foreach (var head in new[] { DiagramHead.Triangle, DiagramHead.HollowDiamond, DiagramHead.Circle })
        {
            var line = (GeometryMark)Drawn(head).Marks[0];
            Assert.IsTrue(line.Shape.Bounds.Right < 100 - 4, $"{head}: the line ends at {line.Shape.Bounds.Right}, short of the head");
        }
    });

    [TestMethod]
    public void ADashedLineIsDrawnDashed() => UiThread.Run(() =>
    {
        var line = (GeometryMark)Drawn(DiagramHead.Arrow, new DiagramStroke(Brushes.Black, 1, DiagramStroke.Dashed)).Marks[0];
        Assert.AreSame(DiagramStroke.Dashed, line.Dashes);
    });

    [TestMethod]
    public void TheMiddleOfARouteIsHalfwayAlongIt()
    {
        Assert.AreEqual(new Point(10, 0), DiagramConnector.Middle([new Point(0, 0), new Point(10, 0), new Point(10, 10)]));
        Assert.AreEqual(new Point(5, 0), DiagramConnector.Middle([new Point(0, 0), new Point(10, 0)]));
    }

    [TestMethod]
    public void ACurvedLinePassesThroughEveryPointOfItsRoute() => UiThread.Run(() =>
    {
        var line = (GeometryMark)Drawn(DiagramHead.None, null, new Point(0, 0), new Point(50, 40), new Point(100, 0)).Marks[0];
        Assert.IsTrue(line.Shape.StrokeContains(new Pen(Brushes.Black, 2), new Point(50, 40)));
    });

    [TestMethod]
    public void TheBandARouteStandsInReachesEitherSideOfIt() => UiThread.Run(() =>
    {
        var band = DiagramConnector.Band([new Point(0, 0), new Point(100, 0)]);

        Assert.IsTrue(band.FillContains(new Point(50, 3)), "within reach of the line");
        Assert.IsFalse(band.FillContains(new Point(50, 20)), "and no further from it");
    });
}
