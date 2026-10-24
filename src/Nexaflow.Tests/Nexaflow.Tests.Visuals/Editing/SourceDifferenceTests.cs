using Nexaflow.Tests.Fixtures;
using Nexaflow.Visuals.Text.Editing;

namespace Nexaflow.Tests.Visuals.Editing;

/// <summary>
/// The one stretch two writings of a document differ by, and where a place in the first stands in the second.
/// </summary>
[TestClass]
[CoversNode("markdown-block-undo")]
public class SourceDifferenceTests
{
    [TestMethod]
    public void TheStretchIsWhatLiesBetweenWhatBothStartAndEndWith()
    {
        Assert.AreEqual(new SourceDifference(6, 4, 5), SourceDifference.Between("alpha beta gamma", "alpha BETAS gamma"));
        Assert.AreEqual(new SourceDifference(6, 0, 5), SourceDifference.Between("alpha gamma", "alpha beta gamma"), "only written in");
        Assert.AreEqual(new SourceDifference(6, 5, 0), SourceDifference.Between("alpha beta gamma", "alpha gamma"), "only taken out");
        Assert.IsTrue(SourceDifference.Between("same", "same").None);
    }

    [TestMethod]
    public void AStretchThatRepeatsIsNotCountedTwice()
    {
        // "aa" to "aaa": what both start with and what both end with overlap, and the stretch is the one character between.
        Assert.AreEqual(new SourceDifference(2, 0, 1), SourceDifference.Between("aa", "aaa"));
    }

    [TestMethod]
    public void APlaceStaysBeforeTheStretch_MovesWithItAfter_AndLandsAtItsStartInside()
    {
        var change = SourceDifference.Between("alpha beta gamma", "alpha BETAS gamma");

        Assert.AreEqual(0, change.Moved(0), "before it, where it was");
        Assert.AreEqual(12, change.Moved(11), "after it, moved by what it grew by");
        Assert.AreEqual(6, change.Moved(8), "written over, at its start");
    }
}
