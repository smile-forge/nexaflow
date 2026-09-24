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
    public void ACurvedLineRunsStraightIntoItsHead_AndDoesNotBowPastItsCorners() => UiThread.Run(() =>
    {
        // The route a layered layout hands over: down, across, and down again, squared off so it leaves and arrives head on.
        var along = Flattened(Curved(DiagramHead.Arrow, new Point(0, 0), new Point(0, 50), new Point(100, 50), new Point(100, 100)));

        var strayed = along.Where(at => at.Y > 60).Select(at => Math.Abs(at.X - 100)).DefaultIfEmpty(0).Max();
        Assert.IsTrue(strayed < 0.5, $"the run into the head is straight, and this one bows {strayed:0.0} to the side of it");

        var left = along.Where(at => at.Y < 40).Select(at => Math.Abs(at.X)).DefaultIfEmpty(0).Max();
        Assert.IsTrue(left < 0.5, $"the run out of what it leaves is straight too, and this one bows {left:0.0}");

        Assert.IsTrue(along.All(at => at.X >= -0.5 && at.X <= 100.5),
                      "and it never bows past a corner, which is what holding each handle to its own run is for");
    });

    /// <summary>Every point a curved line comes to, the curve flattened to the lines it is drawn as.</summary>
    private static Point[] Flattened(Piece edge) =>
        [.. PathGeometry.CreateFromGeometry(edge.Marks.ToArray().OfType<GeometryMark>().First().Shape)
                        .GetFlattenedPathGeometry()
                        .Figures
                        .SelectMany(figure => figure.Segments.OfType<PolyLineSegment>().SelectMany(segment => segment.Points))];

    private static Piece Curved(DiagramHead end, params Point[] route)
    {
        var build = new LayoutBuilder();
        build.Open("page");
        DiagramConnector.Draw(build, "Edge", new TestPart(0, 3), route, new DiagramStroke(Brushes.Black), end: end, curved: true);
        build.Close();

        return build.Seal().Root.SelfAndDescendants().Single(piece => piece.Kind == "Edge");
    }

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
    public void ALineGoesIntoItsHeadTheWayItComesIn_WithNoHookBeforeIt() => UiThread.Run(() =>
    {
        // A curve made of its points, whose last short step turns off it — as the end of one brought in to the edge of a shape does.
        var tip = new Point(100, 30);
        var edge = Curved(DiagramHead.Arrow, new Point(0, 0), new Point(40, 2), new Point(70, 8), new Point(90, 16), new Point(96, 22), tip);
        var line = edge.Marks.ToArray().OfType<GeometryMark>().First().Shape.Bounds;

        // The head points the way the line comes in over the head's own length — from (90, 16) — and its foot is a head's length back.
        var coming = tip - new Point(90, 16);
        coming.Normalize();
        var foot = tip - (coming * (DiagramConnector.HeadLength + 1));

        Assert.AreEqual(foot.X, line.Right, 0.05, $"the line ends at the foot of a head set along the way it comes in: {line} for {foot}");
        Assert.AreEqual(foot.Y, line.Bottom, 0.05, "rather than along the short last step it was brought in by");
        Assert.IsTrue(line.Right <= foot.X + 0.05 && line.Bottom <= foot.Y + 0.05,
                      "and nothing of it runs on past the foot and comes back to it");
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
