using Nexaflow.Features.Pdf.Editing;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.Features.Pdf;

/// <summary>
/// The order of an edited document's pages. Every page edit is one of its three operations, and a gap — the place
/// between pages a drop or an insert names — is counted before the edit, as the reader points at it.
/// </summary>
[TestClass]
public class PdfPageArrangementTests
{
    private static readonly PdfSourceId Base = new(0);

    [TestMethod]
    [TestCategory("Unit")]
    [CoversNode("pdf-page-reorder")]
    public void MovingPages_KeepsTheirOrder_AndCountsTheGapBeforeTheMove()
    {
        var pages = Arrangement(5);

        Assert.AreEqual("1 2 4 3 5", Numbers(pages.Move([2], 4)));
        Assert.AreEqual("4 1 2 3 5", Numbers(pages.Move([3], 0)));
        Assert.AreEqual("1 3 5 2 4", Numbers(pages.Move([1, 3], 5)));
        Assert.AreEqual("2 4 1 3 5", Numbers(pages.Move([3, 1], 0)), "the pages move in the order they had, not the order picked");
        Assert.AreEqual("1 2 3 4 5", Numbers(pages.Move([2], 3)), "a page dropped beside itself stays where it was");
    }

    [TestMethod]
    [TestCategory("Unit")]
    [CoversNode("pdf-page-delete")]
    public void DeletingPages_KeepsTheRest_ButNeverTheLastOne()
    {
        var pages = Arrangement(3);

        Assert.AreEqual("2", Numbers(pages.Remove([0, 2])));
        Assert.ThrowsExactly<InvalidOperationException>(() => pages.Remove([0, 1, 2]));
    }

    [TestMethod]
    [TestCategory("Unit")]
    [CoversNode("pdf-page-insert-pdf")]
    public void InsertingPages_PutsThemInTheGap_InTheirOrder()
    {
        var pages = Arrangement(2);
        var inserted = new[] { new PdfArrangedPage(new PdfPageId(10), new PdfSourceId(1), 1), new PdfArrangedPage(new PdfPageId(11), new PdfSourceId(1), 2) };

        var result = pages.Insert(1, inserted);

        Assert.AreEqual("1 1 2 2", Numbers(result));
        Assert.AreEqual(new PdfSourceId(1), result[1].Sheet.Pieces[0].Source);
        Assert.AreEqual(2, result.IndexOf(new PdfSourceId(1), 2));
        Assert.AreEqual(-1, result.IndexOf(new PdfSourceId(1), 3));
    }

    [TestMethod]
    [TestCategory("Unit")]
    [CoversNode("pdf-page-reorder")]
    public void WhereEachPageWas_IsKnownByThePage_NewPagesHavingNoPlace()
    {
        var pages = Arrangement(3);
        var next = pages.Remove([0]).Insert(0, [new PdfArrangedPage(new PdfPageId(9), Base, 9)]);

        CollectionAssert.AreEqual(new int?[] { null, 1, 2 }, pages.Before(next).ToArray());
    }

    [TestMethod]
    [TestCategory("Unit")]
    [CoversNode("pdf-page-edit-history")]
    public void TwoArrangements_OfTheSamePages_AreEqual()
    {
        var pages = Arrangement(3);

        Assert.AreEqual(pages, pages.Move([0], 3).Move([2], 0));
        Assert.AreNotEqual(pages, pages.Move([0], 3));
    }

    [TestMethod]
    [TestCategory("Unit")]
    [CoversNode("pdf-page-reorder")]
    public void APlaceOrPageThatIsNotTheDocuments_IsRefused()
    {
        var pages = Arrangement(2);

        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => pages.Move([0], 3));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => pages.Remove([2]));
        Assert.ThrowsExactly<ArgumentException>(() => pages.Move([], 0));
    }

    private static PdfPageArrangement Arrangement(int count) =>
        PdfPageArrangement.Of(Enumerable.Range(1, count).Select(number => new PdfArrangedPage(new PdfPageId(number), Base, number)));

    private static string Numbers(PdfPageArrangement pages) =>
        string.Join(" ", pages.Pages.Select(page => string.Join("|", page.Sheet.Pieces.Select(piece => piece.Number))));
}
