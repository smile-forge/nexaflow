using System.Windows;
using System.Windows.Input;
using Nexaflow.Features.WindowsFileSystem;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.Features.WindowsFileSystem;

/// <summary>
/// When a press stops being able to become a drag. This was a bare bool on the view, set on
/// mouse-down and cleared only when a drag actually fired, and the gap that left is what produced
/// copies nobody asked for.
/// </summary>
[TestClass]
[CoversNode("winfs-drag-drop")]
public class DragArmingTests
{
    /// <summary>Comfortably past <see cref="SystemParameters.MinimumHorizontalDragDistance"/>.</summary>
    private static Point FarFrom(Point origin) => new(origin.X + 200, origin.Y + 200);

    [TestMethod]
    public void APressThatTravelsFarEnoughBecomesADrag_Once()
    {
        var arming = new DragArming();
        var origin = new Point(100, 100);

        arming.Arm(origin);

        Assert.IsFalse(arming.ShouldStart(origin), "it has not moved");
        Assert.IsTrue(arming.ShouldStart(FarFrom(origin)));
        Assert.IsFalse(arming.ShouldStart(FarFrom(origin)),
                       "one press starts one drag; saying yes twice would start a second");
    }

    /// <summary>
    /// The reported bug. A menu holding mouse capture swallows the press and release both, so the
    /// list never sees the up that would have disarmed it — and the next move, from wherever the
    /// cursor now is, is instantly far enough from an origin nobody is pointing at any more.
    /// </summary>
    [TestMethod]
    public void AReleaseThisControlNeverSawStillDisarmsIt()
    {
        var arming = new DragArming();
        var origin = new Point(100, 100);

        arming.Arm(origin);
        Assert.IsTrue(arming.IsArmed);

        // No Disarm() call: nothing told it the button came up. The move itself does.
        arming.ObserveButton(MouseButtonState.Released);

        Assert.IsFalse(arming.IsArmed);
        Assert.IsFalse(arming.ShouldStart(FarFrom(origin)),
                       "a drag from a press that is long over is a drag nobody started");
    }

    [TestMethod]
    public void AButtonStillDownLeavesTheArmingAlone()
    {
        var arming = new DragArming();
        var origin = new Point(100, 100);

        arming.Arm(origin);
        arming.ObserveButton(MouseButtonState.Pressed);

        Assert.IsTrue(arming.IsArmed);
        Assert.IsTrue(arming.ShouldStart(FarFrom(origin)));
    }

    [TestMethod]
    public void NothingStartsFromAPressThatNeverHappened()
    {
        var arming = new DragArming();

        Assert.IsFalse(arming.IsArmed);
        Assert.IsFalse(arming.ShouldStart(new Point(9_999, 9_999)));
    }
}
