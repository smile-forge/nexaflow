using Nexaflow.Visuals.Common.Behaviors;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.Visuals.Behaviors;

/// <summary>
/// Which gap a pointer lands in: the nearest item boundary along the axis, so the near half of an item is the gap
/// before it, the far half and the margin past its middle the gap after, and the space past the last item the end.
/// </summary>
[TestClass]
[CoversNode("vcommon-drag-reorder")]
public class ItemGapsTests
{
    // Three 20-unit items with 10-unit margins between them: 0-20, 30-50, 60-80.
    static readonly (int Index, double Start, double End)[] Three = [(0, 0, 20), (1, 30, 50), (2, 60, 80)];

    [TestMethod]
    public void InsideAnItem_TheNearHalfIsBefore_TheFarHalfAfter()
    {
        Assert.AreEqual(0, ItemGaps.GapAt(Three, 4, 3));
        Assert.AreEqual(1, ItemGaps.GapAt(Three, 16, 3));
        Assert.AreEqual(1, ItemGaps.GapAt(Three, 35, 3));
        Assert.AreEqual(2, ItemGaps.GapAt(Three, 45, 3));
    }

    [TestMethod]
    public void TheMarginBetweenTwoItems_IsTheGapBetweenThem()
    {
        Assert.AreEqual(1, ItemGaps.GapAt(Three, 22, 3));
        Assert.AreEqual(1, ItemGaps.GapAt(Three, 28, 3));
        Assert.AreEqual(2, ItemGaps.GapAt(Three, 55, 3));
    }

    [TestMethod]
    public void BeforeTheFirst_IsZero_AndPastTheLast_IsTheCount()
    {
        Assert.AreEqual(0, ItemGaps.GapAt(Three, -15, 3));
        Assert.AreEqual(3, ItemGaps.GapAt(Three, 75, 3));
        Assert.AreEqual(3, ItemGaps.GapAt(Three, 400, 3));
    }

    [TestMethod]
    public void AnEmptyList_HasOneGap()
    {
        Assert.AreEqual(0, ItemGaps.GapAt([], 10, 0));
        Assert.AreEqual(0, ItemGaps.GapAt(Three, 10, 0), "no items, whatever was realized");
    }

    [TestMethod]
    public void OnlyPartRealized_AnswersFromWhatIsThere()
    {
        // A virtualized window onto items 40..42 of 100, given out of order.
        (int, double, double)[] window = [(42, 60, 80), (40, 0, 20), (41, 30, 50)];

        Assert.AreEqual(40, ItemGaps.GapAt(window, -5, 100));
        Assert.AreEqual(41, ItemGaps.GapAt(window, 25, 100));
        Assert.AreEqual(43, ItemGaps.GapAt(window, 90, 100), "past the last realized, the gap after it");
        Assert.AreEqual(100, ItemGaps.GapAt([], 0, 100), "nothing realized, the end");
    }

    [TestMethod]
    public void TheAnswer_StaysWithinTheList()
    {
        (int, double, double)[] stale = [(5, 0, 20)];   // realized before the list shrank to three
        Assert.AreEqual(3, ItemGaps.GapAt(stale, 50, 3));
    }
}
