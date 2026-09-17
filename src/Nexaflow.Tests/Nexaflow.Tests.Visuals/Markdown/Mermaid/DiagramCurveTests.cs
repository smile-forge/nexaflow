using System.Windows;
using System.Windows.Media;
using Nexaflow.Tests.Fixtures;
using Nexaflow.Visuals.Text.Markdown.Mermaid;

namespace Nexaflow.Tests.Visuals.Markdown.Mermaid;

/// <summary>A closed shape through points: straight, or rounded as Mermaid rounds a radar's curves — through every point either way.</summary>
[TestClass]
[TestCategory("UI")]
[CoversNode("mermaid-diagram-kit")]
public class DiagramCurveTests
{
    /// <summary>A diamond: up, right, down and left of the origin.</summary>
    private static readonly Point[] Diamond = [new(0, -50), new(50, 0), new(0, 50), new(-50, 0)];

    [TestMethod]
    public void StraightItIsThePolygonThroughItsPoints() => UiThread.Run(() =>
    {
        var shape = DiagramCurve.Closed(Diamond);

        Assert.AreEqual(new Rect(-50, -50, 100, 100), shape.Bounds);
        Assert.IsTrue(shape.FillContains(new Point(0, 0)));
        Assert.IsFalse(shape.FillContains(new Point(30, 30)), "nothing past the straight line between two corners");
    });

    [TestMethod]
    public void RoundedItStillMeetsEveryPoint_AndBulgesPastTheStraightLinesBetweenThem() => UiThread.Run(() =>
    {
        var shape = DiagramCurve.Closed(Diamond, tension: 0.17);
        var hairline = new Pen(Brushes.Black, 1);

        foreach (var point in Diamond)
            Assert.IsTrue(shape.StrokeContains(hairline, point), $"through {point}");

        Assert.IsTrue(shape.FillContains(new Point(26, 26)), "rounded out past where the straight line runs");
    });

    [TestMethod]
    public void FewerThanThreePointsAreJoinedStraight_AndNoneAreNoShape() => UiThread.Run(() =>
    {
        Assert.AreEqual(new Rect(0, 0, 40, 30), DiagramCurve.Closed([new Point(0, 0), new Point(40, 30)], tension: 0.17).Bounds);
        Assert.IsTrue(DiagramCurve.Closed([]).IsEmpty());
    });
}
