using System.Linq;
using Nexaflow.Tests.Fixtures;
using Nexaflow.Visuals.Text.Editing;

namespace Nexaflow.Tests.Visuals.Editing;

/// <summary>
/// Coverage for <see cref="DocumentSelection"/> — what a drag that leaves one thing and ends in another
/// selected.
///
/// <para>
/// The rule a reader expects is that whatever the sweep <em>passes through</em> is taken whole, and only
/// the two ends are partial. A formula caught mid-sweep is selected entirely however the pointer crossed
/// it, because a selection clipped at whatever pixel the mouse was travelling through is a selection
/// nobody asked for.
/// </para>
/// <para>
/// A document, for these purposes, is an ordered run of things with lengths — so none of this needs a
/// document, a window or a caret to be asked.
/// </para>
/// </summary>
[TestClass]
[CoversNode("latex-document-seam")]
public class DocumentSelectionTests
{
    /// <summary>Prose, a formula, more prose, another formula, a last run of prose.</summary>
    private static DocumentPart[] Page() =>
    [
        new(20, IsBlock: false),
        new(13, IsBlock: true),
        new(30, IsBlock: false),
        new(9, IsBlock: true),
        new(15, IsBlock: false),
    ];

    [TestMethod]
    public void ASelectionInsideOnePartStaysThere()
    {
        var page = Page();

        var selection = DocumentSelection.Between(page, new DocumentPoint(0, 4), new DocumentPoint(0, 11));

        Assert.AreEqual(1, selection.Ranges.Count);
        Assert.AreEqual(new DocumentRange(0, 4, 7), selection.Ranges[0]);
    }

    [TestMethod]
    public void CrossingIntoABlockTakesTheEndOfOneAndTheStartOfTheOther()
    {
        var page = Page();

        var selection = DocumentSelection.Between(page, new DocumentPoint(0, 15), new DocumentPoint(1, 6));

        CollectionAssert.AreEqual(
            new[] { new DocumentRange(0, 15, 5), new DocumentRange(1, 0, 6) },
            selection.Ranges.ToArray());
    }

    [TestMethod]
    public void ABlockSweptThroughIsTakenWhole()
    {
        // The rule worth stating: the formula in the middle is selected entirely, not clipped at whatever
        // point of it the pointer happened to pass over.
        var page = Page();

        var selection = DocumentSelection.Between(page, new DocumentPoint(0, 18), new DocumentPoint(2, 4));

        CollectionAssert.AreEqual(
            new[] { new DocumentRange(0, 18, 2), new DocumentRange(1, 0, 13), new DocumentRange(2, 0, 4) },
            selection.Ranges.ToArray());
        Assert.IsTrue(selection.Wholly(page, 1), "the formula in the middle");
        Assert.IsFalse(selection.Wholly(page, 0), "but not the prose it started in");
        Assert.IsFalse(selection.Wholly(page, 2), "nor the prose it ended in");
    }

    [TestMethod]
    public void ASweepAcrossSeveralBlocksTakesEveryOneItPasses()
    {
        var page = Page();

        var selection = DocumentSelection.Between(page, new DocumentPoint(0, 19), new DocumentPoint(4, 2));

        Assert.AreEqual(5, selection.Ranges.Count);
        Assert.IsTrue(selection.Wholly(page, 1));
        Assert.IsTrue(selection.Wholly(page, 2), "the prose between them too");
        Assert.IsTrue(selection.Wholly(page, 3));
    }

    [TestMethod]
    public void ItDoesNotMatterWhichEndYouStartedFrom()
    {
        var page = Page();

        var forwards = DocumentSelection.Between(page, new DocumentPoint(0, 18), new DocumentPoint(2, 4));
        var backwards = DocumentSelection.Between(page, new DocumentPoint(2, 4), new DocumentPoint(0, 18));

        CollectionAssert.AreEqual(forwards.Ranges.ToArray(), backwards.Ranges.ToArray());
        Assert.AreEqual(forwards.From, backwards.From);
        Assert.AreEqual(forwards.To, backwards.To);
    }

    [TestMethod]
    public void AClickSelectsNothing()
    {
        var page = Page();

        Assert.IsTrue(DocumentSelection.Between(page, new DocumentPoint(1, 3), new DocumentPoint(1, 3)).IsEmpty);
        Assert.IsTrue(DocumentSelection.None.IsEmpty);
    }

    [TestMethod]
    public void APartTakingNothingIsNotInTheAnswer()
    {
        // Ending exactly on a boundary must not leave an empty range behind for the part it stopped at —
        // a block asked to select nothing would clear its selection and then be told to wash it.
        var page = Page();

        var selection = DocumentSelection.Between(page, new DocumentPoint(0, 5), new DocumentPoint(1, 0));

        Assert.AreEqual(1, selection.Ranges.Count);
        Assert.AreEqual(0, selection.Ranges[0].Part);
        Assert.IsNull(selection.Of(1), "the block it stopped at the front of takes nothing");
    }

    [TestMethod]
    public void AnEndPastThePartIsBroughtBackToIt()
    {
        // Where the pointer lands is the host's arithmetic, and hosts miscount at edges. A selection is
        // clamped to what actually exists rather than trusting it.
        var page = Page();

        var selection = DocumentSelection.Between(page, new DocumentPoint(3, 400), new DocumentPoint(4, -7));

        Assert.AreEqual(new DocumentPoint(3, 9), selection.From, "no further than the block is long");
        Assert.AreEqual(new DocumentPoint(4, 0), selection.To);
    }
}
