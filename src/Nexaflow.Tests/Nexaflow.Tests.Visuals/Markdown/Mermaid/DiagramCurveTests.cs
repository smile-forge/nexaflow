using System.Windows;
using System.Windows.Media;
using Nexaflow.Tests.Fixtures;
using Nexaflow.Visuals.Text.Markdown.Mermaid;
using Nexaflow.Markdown.Layout;
using Nexaflow.Markdown.Layout.Prose;
using Nexaflow.Visuals.Text.Painting;
using Nexaflow.Markdown.Layout.Mermaid;

namespace Nexaflow.Tests.Visuals.Markdown.Mermaid;

/// <summary>A closed shape through points: straight, or rounded as Mermaid rounds a radar's curves — through every point either way.</summary>
[TestClass]
[TestCategory("UI")]
[CoversNode("mermaid-diagram-kit")]
public class DiagramCurveTests
{
    /// <summary>A diamond: up, right, down and left of the origin.</summary>
    private static readonly LayoutPoint[] Diamond = [new(0, -50), new(50, 0), new(0, 50), new(-50, 0)];

    [TestMethod]
    public void StraightItIsThePolygonThroughItsPoints() => UiThread.Run(() =>
    {
        var shape = PortableDiagramCurve.Closed(WpfHandles.Medium, Diamond);

        Assert.AreEqual(new LayoutRect(-50, -50, 100, 100), shape.Bounds);
        Assert.IsTrue(shape.Contains(new LayoutPoint(0, 0)));
        Assert.IsFalse(shape.Contains(new LayoutPoint(30, 30)), "nothing past the straight line between two corners");
    });

    [TestMethod]
    public void RoundedItStillMeetsEveryPoint_AndBulgesPastTheStraightLinesBetweenThem() => UiThread.Run(() =>
    {
        var shape = PortableDiagramCurve.Closed(WpfHandles.Medium, Diamond, tension: 0.17);
        var hairline = new Pen(Brushes.Black, 1);

        foreach (var point in Diamond)
            Assert.IsTrue(shape.Geometry().StrokeContains(hairline, point.ToWpf()), $"through {point}");

        Assert.IsTrue(shape.Contains(new LayoutPoint(26, 26)), "rounded out past where the straight line runs");
    });

    [TestMethod]
    public void FewerThanThreePointsAreJoinedStraight_AndNoneAreNoShape() => UiThread.Run(() =>
    {
        Assert.AreEqual(new LayoutRect(0, 0, 40, 30), PortableDiagramCurve.Closed(WpfHandles.Medium, [new LayoutPoint(0, 0), new LayoutPoint(40, 30)], tension: 0.17).Bounds);
        Assert.IsTrue(PortableDiagramCurve.Closed(WpfHandles.Medium, []).Geometry().IsEmpty());
    });

    [TestMethod]
    public void BowedItRunsFromOnePointToAnother_OutTowardTheOneBetween() => UiThread.Run(() =>
    {
        var shape = PortableDiagramCurve.Bowed(WpfHandles.Medium, new LayoutPoint(0, 0), new LayoutPoint(50, -40), new LayoutPoint(100, 0));
        var hairline = new Pen(Brushes.Black, 1);

        Assert.IsTrue(shape.Geometry().StrokeContains(hairline, new Point(0, 0)), "from where it starts");
        Assert.IsTrue(shape.Geometry().StrokeContains(hairline, new Point(100, 0)), "to where it ends");
        Assert.IsTrue(shape.Bounds.Top is < -15 and > -40, $"bowed out toward the point between, and no further: {shape.Bounds}");
    });
}
