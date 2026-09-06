using Nexaflow.Tests.Fixtures;
using Nexaflow.Visuals.Common.Behaviors;

namespace Nexaflow.Tests.Visuals.Behaviors;

/// <summary>
/// The arithmetic a rubber-band selection is made of. It is separated from the gesture precisely so
/// it can be checked like this — the interesting cases are all off-screen ones (a band reaching into
/// rows virtualisation never realised, or below the last row entirely), which a rendered list would
/// make far harder to pose than to reason about.
/// </summary>
[TestClass]
[CoversNode("winfs-marquee-select")]
public class MarqueeRangeTests
{
    private const double RowHeight = 20;

    [TestMethod]
    public void APointInsideTheViewportIsTheRowItLandsOn()
    {
        Assert.AreEqual(0, MarqueeRange.IndexAt(y: 0,  RowHeight, firstVisibleIndex: 0, itemCount: 10));
        Assert.AreEqual(0, MarqueeRange.IndexAt(y: 19, RowHeight, firstVisibleIndex: 0, itemCount: 10));
        Assert.AreEqual(1, MarqueeRange.IndexAt(y: 20, RowHeight, firstVisibleIndex: 0, itemCount: 10));
        Assert.AreEqual(3, MarqueeRange.IndexAt(y: 70, RowHeight, firstVisibleIndex: 0, itemCount: 10));
    }

    [TestMethod]
    public void TheScrollOffsetIsAnIndex_SoAScrolledListCountsFromThere()
    {
        // The lists scroll by item, so "first visible" is row 40, not pixel 800.
        Assert.AreEqual(40, MarqueeRange.IndexAt(y: 0,  RowHeight, firstVisibleIndex: 40, itemCount: 500));
        Assert.AreEqual(42, MarqueeRange.IndexAt(y: 50, RowHeight, firstVisibleIndex: 40, itemCount: 500));
    }

    [TestMethod]
    public void APointAboveTheViewportResolvesToTheRowsBeforeIt()
    {
        // Dragging upward faster than the auto-scroll catches up: y goes negative, and the row it
        // names must be the one above — truncation toward zero would round it back into view.
        Assert.AreEqual(39, MarqueeRange.IndexAt(y: -1,  RowHeight, firstVisibleIndex: 40, itemCount: 500));
        Assert.AreEqual(38, MarqueeRange.IndexAt(y: -21, RowHeight, firstVisibleIndex: 40, itemCount: 500));
    }

    [TestMethod]
    public void APointBelowTheLastRowIsOnePastTheEnd_NotTheLastRow()
    {
        // The ordinary case, not an edge one: the only place a press can land without hitting a row.
        Assert.AreEqual(10, MarqueeRange.IndexAt(y: 200, RowHeight, firstVisibleIndex: 0, itemCount: 10));
        Assert.AreEqual(10, MarqueeRange.IndexAt(y: 999, RowHeight, firstVisibleIndex: 0, itemCount: 10));
    }

    [TestMethod]
    public void NeitherEndRunsOffTheList()
    {
        Assert.AreEqual(0, MarqueeRange.IndexAt(y: -10_000, RowHeight, firstVisibleIndex: 0, itemCount: 10));
        Assert.AreEqual(0, MarqueeRange.IndexAt(y: 0, rowHeight: 0, firstVisibleIndex: 0, itemCount: 0),
                        "an empty list has no row anywhere");
    }

    [TestMethod]
    public void ABandCoversTheRunBetweenItsEnds_WhicheverWayItWasDragged()
    {
        Assert.AreEqual((2, 5), MarqueeRange.Resolve(anchorIndex: 2, currentIndex: 5, itemCount: 10));
        Assert.AreEqual((2, 5), MarqueeRange.Resolve(anchorIndex: 5, currentIndex: 2, itemCount: 10));
        Assert.AreEqual((4, 4), MarqueeRange.Resolve(anchorIndex: 4, currentIndex: 4, itemCount: 10));
    }

    [TestMethod]
    public void StartingBelowTheRowsAndDraggingUpCoversTheTail()
    {
        // anchor == itemCount is the "started in the empty space" sentinel; the run it makes with a
        // row must stop at the last one rather than reaching for a row that is not there.
        Assert.AreEqual((7, 9), MarqueeRange.Resolve(anchorIndex: 10, currentIndex: 7, itemCount: 10));
    }

    [TestMethod]
    public void ABandDrawnEntirelyBelowTheRowsCoversNothing()
    {
        // Which is how "drag a box over empty space" ends up clearing the selection, rather than
        // being a special case spelled out anywhere else.
        Assert.IsNull(MarqueeRange.Resolve(anchorIndex: 10, currentIndex: 10, itemCount: 10));
        Assert.IsNull(MarqueeRange.Resolve(anchorIndex: 0, currentIndex: 0, itemCount: 0));
    }
}
