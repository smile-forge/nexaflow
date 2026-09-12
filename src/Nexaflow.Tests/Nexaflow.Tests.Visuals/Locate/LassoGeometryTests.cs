using System.Windows;
using Nexaflow.Tests.Fixtures;
using Nexaflow.Visuals.Common.Locate;

namespace Nexaflow.Tests.Visuals.Locate;

/// <summary>
/// The loop a lasso is drawn as (<see cref="LassoGeometry"/>). What it has to promise for every shape of control — a
/// search box 300 px wide, a 16 px icon, a whole pane — is that it goes round the thing it points at without ever
/// crossing it, and that it crosses <i>itself</i>, since that is the difference between a thrown loop and a frame.
/// Geometry, so it is checked for every shape rather than eyeballed for one.
/// </summary>
[TestClass]
[CoversNode("help-locate-lasso")]
public class LassoGeometryTests
{
    private static readonly Rect[] Shapes =
    [
        new(0, 0, 300, 24),    // a search box
        new(0, 0, 24, 300),    // a scrollbar
        new(0, 0, 16, 16),     // an icon button
        new(0, 0, 800, 600),   // a whole pane
        new(40, 12, 90, 28),   // a button, away from the origin
        new(0, 0, 0, 0),       // nothing at all
    ];

    [TestMethod]
    public void TheLoopNeverCrossesWhatItPointsAt()
    {
        foreach (var target in Shapes)
        {
            var clear = Padding(target) * 0.3;
            foreach (var point in LassoGeometry.Loop(target))
                Assert.IsTrue(DistanceTo(target, point) >= clear,
                              $"{target}: the loop came within {DistanceTo(target, point):0.00} px of the control");
        }
    }

    [TestMethod]
    public void EveryCornerOfTheControlIsInsideTheLoop()
    {
        foreach (var target in Shapes)
        {
            var lap = FirstLap(LassoGeometry.Loop(target));
            foreach (var corner in new[] { target.TopLeft, target.TopRight, target.BottomLeft, target.BottomRight })
                Assert.IsTrue(Encloses(lap, corner), $"{target}: {corner} is outside the loop");
        }
    }

    [TestMethod]
    public void ItCrossesItself_AsAThrownLoopDoes()
    {
        foreach (var target in Shapes)
        {
            var loop = LassoGeometry.Loop(target);

            Assert.IsTrue(SelfCrossing(loop), $"{target}: the tail has to cut across the line the loop began on");
            Assert.IsTrue(loop.Max(p => DistanceTo(target, p)) > LassoGeometry.PaddingFor(new Size(target.Width, target.Height)) * 1.3,
                          $"{target}: …after swinging wide of it");
        }
    }

    [TestMethod]
    public void ThereIsEnoughOfItToDraw_AndAllOfItIsReal()
    {
        foreach (var target in Shapes)
        {
            var loop = LassoGeometry.Loop(target);

            Assert.IsTrue(loop.Count >= 72, $"{target}: smooth enough to read as a curve");
            Assert.IsTrue(loop.All(p => double.IsFinite(p.X) && double.IsFinite(p.Y)), $"{target}: every point is a number");
            Assert.IsTrue(LassoGeometry.Length(loop) > 2 * (target.Width + target.Height),
                          $"{target}: a loop round a box is longer than the box");
            Assert.IsTrue(loop.Contains(LassoGeometry.BadgeAnchor(loop)), $"{target}: the step number rides on the loop");
        }
    }

    [TestMethod]
    public void ThePaddingSuitsTheControl()
    {
        Assert.AreEqual(6, LassoGeometry.PaddingFor(new Size(8, 8)), "a tiny control still gets a loop you can see");
        Assert.AreEqual(14, LassoGeometry.PaddingFor(new Size(900, 700)), "a huge one does not get a huge halo");
        Assert.AreEqual(10, LassoGeometry.PaddingFor(new Size(200, 40)), "in between, a quarter of the short side");
    }

    // ── Geometry the assertions are made of ───────────────────────────────

    private static double Padding(Rect target) => LassoGeometry.PaddingFor(new Size(target.Width, target.Height));

    private static double DistanceTo(Rect rect, Point point)
    {
        var dx = Math.Max(Math.Max(rect.Left - point.X, point.X - rect.Right), 0);
        var dy = Math.Max(Math.Max(rect.Top - point.Y, point.Y - rect.Bottom), 0);
        return Math.Sqrt(dx * dx + dy * dy);
    }

    // The run overshoots its start, so the closed shape to reason about is the first time round: the point past halfway
    // that comes back nearest to where it began.
    private static IReadOnlyList<Point> FirstLap(IReadOnlyList<Point> loop)
    {
        var end = loop.Count - 1;
        for (var i = loop.Count / 2; i < loop.Count; i++)
            if ((loop[i] - loop[0]).Length < (loop[end] - loop[0]).Length) end = i;
        return loop.Take(end + 1).ToList();
    }

    // Ray casting: a point is inside when a ray from it crosses the loop an odd number of times.
    private static bool Encloses(IReadOnlyList<Point> loop, Point point)
    {
        var inside = false;
        for (int i = 0, j = loop.Count - 1; i < loop.Count; j = i++)
        {
            if (loop[i].Y > point.Y != loop[j].Y > point.Y &&
                point.X < (loop[j].X - loop[i].X) * (point.Y - loop[i].Y) / (loop[j].Y - loop[i].Y) + loop[i].X)
                inside = !inside;
        }
        return inside;
    }

    private static bool SelfCrossing(IReadOnlyList<Point> loop)
    {
        for (var i = 0; i < loop.Count - 1; i++)
            for (var j = i + 2; j < loop.Count - 1; j++)
                if (Crosses(loop[i], loop[i + 1], loop[j], loop[j + 1]))
                    return true;
        return false;
    }

    private static bool Crosses(Point a, Point b, Point c, Point d)
        => Side(a, b, c) * Side(a, b, d) < 0 && Side(c, d, a) * Side(c, d, b) < 0;

    private static double Side(Point a, Point b, Point p)
        => Math.Sign((b.X - a.X) * (p.Y - a.Y) - (b.Y - a.Y) * (p.X - a.X));
}
