using System;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using Nexaflow.Markdown.Mermaid;
using Nexaflow.Tests.Fixtures;
using Nexaflow.Tests.Visuals.Editing;
using Nexaflow.Visuals.Text.Editing;
using Nexaflow.Visuals.Text.Markdown.Mermaid;
using Nexaflow.Markdown.Layout;
using Nexaflow.Markdown.Layout.Prose;
using Nexaflow.Visuals.Text.Painting;
using Nexaflow.Markdown.Layout.Mermaid;

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
    private static PortablePiece Drawn(DiagramHead end, PortableDiagramStroke? stroke = null, params LayoutPoint[] route)
    {
        var build = new PortableBuilder(WpfMedium.OnThisThread);
        build.Open("page");
        PortableDiagramConnector.Draw(build, "Edge", new TestPart(0, 3), route.Length > 0 ? route : [new LayoutPoint(0, 50), new LayoutPoint(100, 50)],
                              stroke ?? new PortableDiagramStroke(Brushes.Black.Ink()), end: end);
        build.Close();

        return build.Seal().Root.SelfAndDescendants().Single(piece => piece.Kind == "Edge");
    }

    [TestMethod]
    public void APressNearTheLineMeansIt_AndOneAwayFromItDoesNot() => UiThread.Run(() =>
    {
        var edge = Drawn(DiagramHead.Arrow);

        Assert.IsTrue(edge.Region!.Contains(new LayoutPoint(50, 53)), "three pixels off the line is on it");
        Assert.IsFalse(edge.Region!.Contains(new LayoutPoint(50, 70)), "twenty is not");
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
        var along = Flattened(Curved(DiagramHead.Arrow, new LayoutPoint(0, 0), new LayoutPoint(0, 50), new LayoutPoint(100, 50), new LayoutPoint(100, 100)));

        var strayed = along.Where(at => at.Y > 60).Select(at => Math.Abs(at.X - 100)).DefaultIfEmpty(0).Max();
        Assert.IsTrue(strayed < 0.5, $"the run into the head is straight, and this one bows {strayed:0.0} to the side of it");

        var left = along.Where(at => at.Y < 40).Select(at => Math.Abs(at.X)).DefaultIfEmpty(0).Max();
        Assert.IsTrue(left < 0.5, $"the run out of what it leaves is straight too, and this one bows {left:0.0}");

        Assert.IsTrue(along.All(at => at.X >= -0.5 && at.X <= 100.5),
                      "and it never bows past a corner, which is what holding each handle to its own run is for");
    });

    /// <summary>Every point a curved line comes to, the curve flattened to the lines it is drawn as.</summary>
    private static Point[] Flattened(PortablePiece edge) =>
        [.. PathGeometry.CreateFromGeometry(edge.Marks.ToArray().OfType<PortableGeometryMark>().First().Shape.Geometry())
                        .GetFlattenedPathGeometry()
                        .Figures
                        .SelectMany(figure => figure.Segments.OfType<PolyLineSegment>().SelectMany(segment => segment.Points))];

    private static PortablePiece Curved(DiagramHead end, params LayoutPoint[] route)
    {
        var build = new PortableBuilder(WpfMedium.OnThisThread);
        build.Open("page");
        PortableDiagramConnector.Draw(build, "Edge", new TestPart(0, 3), route, new PortableDiagramStroke(Brushes.Black.Ink()), end: end, curved: true);
        build.Close();

        return build.Seal().Root.SelfAndDescendants().Single(piece => piece.Kind == "Edge");
    }

    [TestMethod]
    public void TheLineStopsWhereAHollowHeadStarts() => UiThread.Run(() =>
    {
        foreach (var head in new[] { DiagramHead.Triangle, DiagramHead.HollowDiamond, DiagramHead.Circle })
        {
            var line = (PortableGeometryMark)Drawn(head).Marks[0];
            Assert.IsTrue(line.Shape.Bounds.Right < 100 - 4, $"{head}: the line ends at {line.Shape.Bounds.Right}, short of the head");
        }
    });

    [TestMethod]
    public void AHeadIsDrawnAsBigAsItIsAskedToBe() => UiThread.Run(() =>
    {
        LayoutRect Head(double heads)
        {
            var build = new PortableBuilder(WpfMedium.OnThisThread);
            build.Open("page");
            PortableDiagramConnector.Draw(build, "Edge", new TestPart(0, 3), [new LayoutPoint(0, 50), new LayoutPoint(100, 50)], new PortableDiagramStroke(Brushes.Black.Ink()), heads: heads);
            build.Close();

            return build.Seal().Root.SelfAndDescendants().Single(piece => piece.Kind == "Edge").Marks.ToArray().OfType<PortableGeometryMark>().Last().Shape.Bounds;
        }

        var (full, half) = (Head(1), Head(0.5));

        Assert.AreEqual(full.Width / 2, half.Width, 0.05, "half as long");
        Assert.AreEqual(full.Height / 2, half.Height, 0.05, "and half as wide");
    });

    [TestMethod]
    public void ALineGoesIntoItsHeadTheWayItComesIn_WithNoHookBeforeIt() => UiThread.Run(() =>
    {
        // A curve made of its points, whose last short step turns off it — as the end of one brought in to the edge of a shape does.
        var tip = new LayoutPoint(100, 30);
        var edge = Curved(DiagramHead.Arrow, new LayoutPoint(0, 0), new LayoutPoint(40, 2), new LayoutPoint(70, 8), new LayoutPoint(90, 16), new LayoutPoint(96, 22), tip);
        var line = edge.Marks.ToArray().OfType<PortableGeometryMark>().First().Shape.Bounds;

        // The head points the way the line comes in over the head's own length — from (90, 16) — and its foot is a head's length back.
        var coming = (tip - new LayoutPoint(90, 16)).Normalized();
        var foot = tip - (coming * (PortableDiagramConnector.HeadLength + 1));

        Assert.AreEqual(foot.X, line.Right, 0.05, $"the line ends at the foot of a head set along the way it comes in: {line} for {foot}");
        Assert.AreEqual(foot.Y, line.Bottom, 0.05, "rather than along the short last step it was brought in by");
        Assert.IsTrue(line.Right <= foot.X + 0.05 && line.Bottom <= foot.Y + 0.05,
                      "and nothing of it runs on past the foot and comes back to it");
    });

    [TestMethod]
    public void ADashedLineIsDrawnDashed() => UiThread.Run(() =>
    {
        var line = (PortableGeometryMark)Drawn(DiagramHead.Arrow, new PortableDiagramStroke(Brushes.Black.Ink(), 1, PortableDiagramStroke.Dashed)).Marks[0];
        CollectionAssert.AreEqual(PortableDiagramStroke.Dashed, ((WpfStroke)line.Stroke!).Dashes?.ToArray());
    });

    [TestMethod]
    public void TheMiddleOfARouteIsHalfwayAlongIt()
    {
        Assert.AreEqual(new LayoutPoint(10, 0), PortableDiagramConnector.Middle([new LayoutPoint(0, 0), new LayoutPoint(10, 0), new LayoutPoint(10, 10)]));
        Assert.AreEqual(new LayoutPoint(5, 0), PortableDiagramConnector.Middle([new LayoutPoint(0, 0), new LayoutPoint(10, 0)]));
    }

    [TestMethod]
    public void ACurvedLinePassesThroughEveryPointOfItsRoute() => UiThread.Run(() =>
    {
        var line = (PortableGeometryMark)Drawn(DiagramHead.None, null, new LayoutPoint(0, 0), new LayoutPoint(50, 40), new LayoutPoint(100, 0)).Marks[0];
        Assert.IsTrue(line.Shape.Geometry().StrokeContains(new Pen(Brushes.Black, 2), new Point(50, 40)));
    });

    [TestMethod]
    public void TheBandARouteStandsInReachesEitherSideOfIt() => UiThread.Run(() =>
    {
        var band = PortableDiagramConnector.Band(WpfHandles.Medium, [new LayoutPoint(0, 0), new LayoutPoint(100, 0)]);

        Assert.IsTrue(band.Contains(new LayoutPoint(50, 3)), "within reach of the line");
        Assert.IsFalse(band.Contains(new LayoutPoint(50, 20)), "and no further from it");
    });
}
