using System.IO;
using System.Windows;
using System.Windows.Media;
using Nexaflow.Features.Pdf.Document;
using Nexaflow.Features.Pdf.Editing;
using Nexaflow.Features.Pdf.Rendering;
using Nexaflow.Tests.Fixtures;
using UglyToad.PdfPig;
using UglyToad.PdfPig.Core;
using System.Windows.Media.Imaging;

namespace Nexaflow.Tests.Features.Pdf;

/// <summary>
/// Pages printed several to a sheet, split into pages of their own. A part is the sheet seen through a narrower crop
/// box: measured on the page as shown, read as that part alone, written with that crop box, and a link into the sheet
/// goes to the part its point lies in. The sample is a four-page booklet on two landscape sheets — 4|1 and 2|3.
/// </summary>
[TestClass]
public class PdfPageSplitTests
{
    private static readonly PdfRectangle Letter = new(0, 0, 612, 792);

    [TestMethod]
    [TestCategory("Unit")]
    [CoversNode("pdf-page-split")]
    public void APart_IsMeasuredOnThePageAsShown_HoweverThePageIsTurned()
    {
        var left = new PdfPagePart(0, 0, 0.5, 1);

        Assert.AreEqual(new PdfRectangle(0, 0, 306, 792), left.InUserSpace(Letter, 0));
        Assert.AreEqual(new PdfRectangle(0, 0, 612, 396), left.InUserSpace(Letter, 90), "turned a quarter clockwise, the bottom of the page is shown at the left");
        Assert.AreEqual(new PdfRectangle(306, 0, 612, 792), left.InUserSpace(Letter, 180));
        Assert.AreEqual(new PdfRectangle(0, 396, 612, 792), left.InUserSpace(Letter, -90));
        Assert.AreEqual(new PdfRectangle(10, 170, 110, 220), new PdfPagePart(0, 0, 1, 0.25).InUserSpace(new PdfRectangle(10, 20, 110, 220), 0),
            "a part is measured from the crop box, not the paper");
    }

    [TestMethod]
    [TestCategory("Unit")]
    [CoversNode("pdf-page-split")]
    public void APage_IsCutIntoEqualParts_AndAPartIntoPartsOfItself()
    {
        CollectionAssert.AreEqual(
            new[] { new PdfPagePart(0, 0, 0.25, 1), new PdfPagePart(0.25, 0, 0.25, 1), new PdfPagePart(0.5, 0, 0.25, 1), new PdfPagePart(0.75, 0, 0.25, 1) },
            PdfPagePart.Split(4, across: true).ToArray());
        CollectionAssert.AreEqual(new[] { new PdfPagePart(0, 0, 1, 0.5), new PdfPagePart(0, 0.5, 1, 0.5) }, PdfPagePart.Split(2, across: false).ToArray());
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => PdfPagePart.Split(1, across: true));

        var bottomOfRight = new PdfPagePart(0, 0.5, 1, 0.5).Of(new PdfPagePart(0.5, 0, 0.5, 1));
        Assert.AreEqual(new PdfPagePart(0.5, 0.5, 0.5, 0.5), bottomOfRight);
        Assert.AreEqual(new Size(396, 306), bottomOfRight.Of(new Size(792, 612)));
    }

    [TestMethod]
    [TestCategory("Unit")]
    [CoversNode("pdf-page-split")]
    public void ABooklet_IsUnfoldedFromItsSpreads_IntoReadingOrder()
    {
        int[] spreads = [12, 1, 2, 11, 10, 3, 4, 9, 8, 5, 6, 7];

        CollectionAssert.AreEqual(Enumerable.Range(1, 12).ToArray(), PdfBooklet.Unfold(spreads).ToArray());
        Assert.ThrowsExactly<ArgumentException>(() => PdfBooklet.Unfold([1, 2, 3]));
    }

    [TestMethod]
    [TestCategory("Unit")]
    [CoversNode("pdf-page-split")]
    public void ReplacingPages_PutsTheNewOnesWhereTheFirstWas()
    {
        var pages = PdfPageArrangement.Of(Enumerable.Range(1, 4).Select(number => new PdfArrangedPage(new PdfPageId(number), new PdfSourceId(0), number)));
        var parts = Enumerable.Range(7, 3).Select(number => new PdfArrangedPage(new PdfPageId(number), new PdfSourceId(0), number)).ToList();

        Assert.AreEqual("1 7 8 9 4", Numbers(pages.Replace([1, 2], parts)));
        Assert.AreEqual("7 8 9 2 3", Numbers(pages.Replace([3, 0], parts)));
    }

    [TestMethod]
    [TestCategory("Unit")]
    [CoversNode("pdf-page-split")]
    public void APageSplit_IsMadeFromItsSheet_AndASheetJoinedBack_FromItsParts()
    {
        var source = new PdfSourceId(0);
        var sheets = PdfPageArrangement.Of([new PdfArrangedPage(new PdfPageId(1), source, 1), new PdfArrangedPage(new PdfPageId(2), source, 2)]);
        var halves = PdfPagePart.Split(2, across: true);
        var split = sheets.Replace([0],
        [
            new PdfArrangedPage(new PdfPageId(3), source, 1, halves[0]),
            new PdfArrangedPage(new PdfPageId(4), source, 1, halves[1]),
        ]);

        var made = sheets.MadeFrom(split);
        CollectionAssert.AreEqual(new[] { 0 }, made[0].ToArray());
        CollectionAssert.AreEqual(new[] { 0 }, made[1].ToArray());
        Assert.AreEqual(0, made[2].Count, "the second sheet is the same page, not one made from it");

        var joined = split.MadeFrom(sheets);
        CollectionAssert.AreEqual(new[] { 0, 1 }, joined[0].ToArray(), "undone, the sheet is made from both its halves");
    }

    [TestMethod]
    [TestCategory("Unit")]
    [CoversNode("pdf-page-split")]
    public async Task EachPage_IsSplitInItsOwnPlace_TheWayItIsLonger_AsOneEdit()
    {
        using var session = await Opened();
        session.Split([0, 1], 2, PdfSplitDirection.Auto, booklet: false);

        Assert.AreEqual("1 1 2 2", Numbers(session.Arrangement));
        Assert.AreEqual(new Size(396, 612), session.PageSize(0));
        CollectionAssert.AreEqual(new[] { "page four", "page one", "page two", "page three" }, await Texts(session));

        session.Undo();
        Assert.AreEqual("1 2", Numbers(session.Arrangement));
        Assert.IsFalse(session.IsDirty);
    }

    [TestMethod]
    [TestCategory("Unit")]
    [CoversNode("pdf-page-split")]
    public async Task APage_IsSplitTheWayAsked_AndAPartSplitAgain()
    {
        using var session = await Opened();

        session.Split([0], 3, PdfSplitDirection.Down, booklet: false);
        Assert.AreEqual(4, session.Arrangement.Count);
        Assert.AreEqual(new Size(792, 204), session.PageSize(0));

        session.Split([1], 2, PdfSplitDirection.Auto, booklet: false);
        Assert.AreEqual(new Size(396, 204), session.PageSize(1), "a part wider than it is tall is cut side by side");
        Assert.AreEqual(new Size(792, 204), session.PageSize(3), "the parts after it are where they were");
    }

    [TestMethod]
    [TestCategory("Unit")]
    [CoversNode("pdf-page-split")]
    public async Task ABooklet_IsUnfoldedIntoReadingOrder_AndOnlyFromHalves()
    {
        using var session = await Opened();

        Assert.ThrowsExactly<ArgumentException>(() => session.Split([0, 1], 3, PdfSplitDirection.Auto, booklet: true));
        session.Split([0, 1], 2, PdfSplitDirection.Auto, booklet: true);

        CollectionAssert.AreEqual(new[] { "page one", "page two", "page three", "page four" }, await Texts(session));
    }

    [TestMethod]
    [TestCategory("Unit")]
    [CoversNode("pdf-page-split")]
    [CoversNode("pdf-document-assembly")]
    public async Task ASplitPage_IsSaved_AsPagesCroppedToTheirParts()
    {
        var path = Copy();
        using (var session = await Opened(path))
        {
            session.Split([0, 1], 2, PdfSplitDirection.Auto, booklet: true);
            await session.SaveAsync(null, CancellationToken.None);
        }

        using (var saved = PdfDocument.Open(path))
        {
            Assert.AreEqual(4, saved.NumberOfPages);
            Assert.AreEqual(new PdfRectangle(396, 0, 792, 612), saved.GetPage(1).CropBox.Bounds);
            Assert.AreEqual(new PdfRectangle(0, 0, 396, 612), saved.GetPage(4).CropBox.Bounds);
        }

        using var reopened = await Opened(path);
        CollectionAssert.AreEqual(new[] { "page one", "page two", "page three", "page four" }, await Texts(reopened));
    }

    [TestMethod]
    [TestCategory("Unit")]
    [CoversNode("pdf-page-split")]
    public async Task ALinkIntoASplitPage_GoesToThePartItsPointLiesIn()
    {
        using var session = await Opened();
        session.Split([0], 2, PdfSplitDirection.Across, booklet: false);
        var pages = new PdfPageSource(session.Arrangement, session.Opened(), Brushes.White);
        var source = session.Arrangement[0].Sheet.Pieces[0].Source;

        Assert.AreEqual(1, pages.Locate(source, new PdfPageTarget(1, Top: 300, Left: 500))?.Index);
        Assert.AreEqual(0, pages.Locate(source, new PdfPageTarget(1, Top: 300, Left: 100))?.Index);
        Assert.AreEqual(0, pages.Locate(source, new PdfPageTarget(1, Top: 300))?.Index, "a target naming only a height is on the first part");
        Assert.AreEqual(2, pages.Locate(source, new PdfPageTarget(2, Top: null))?.Index);
    }

    [TestMethod]
    [TestCategory("Unit")]
    [CoversNode("pdf-page-split")]
    public void ACut_CrossesArtwork_WhereItsLineMeetsAnotherColourThanItsOwn()
    {
        var cream = (B: (byte)200, G: (byte)230, R: (byte)245);
        var clear = PdfInk.Of(Picture(100, 60, cream, (10, 40, 10, 50), (60, 90, 10, 50)));
        var crossed = PdfInk.Of(Picture(100, 60, cream, (10, 40, 10, 50), (45, 90, 10, 50)));

        Assert.IsFalse(clear.Crosses(new PdfPageCut(0.5, Vertical: true)), "a gutter of the sheet's own colour, whatever that is");
        Assert.IsTrue(crossed.Crosses(new PdfPageCut(0.5, Vertical: true)));
        Assert.IsFalse(crossed.Crosses(new PdfPageCut(0.9, Vertical: false)), "below the artwork");
    }

    /// <summary>A sheet of one colour with dark blocks on it, each given as its columns and rows, ends excluded.</summary>
    private static BitmapSource Picture(int width, int height, (byte B, byte G, byte R) sheet, params (int Left, int Right, int Top, int Bottom)[] blocks)
    {
        var pixels = new byte[width * height * 4];
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var inked = blocks.Any(block => x >= block.Left && x < block.Right && y >= block.Top && y < block.Bottom);
                var offset = (y * width + x) * 4;
                pixels[offset] = inked ? (byte)30 : sheet.B;
                pixels[offset + 1] = inked ? (byte)30 : sheet.G;
                pixels[offset + 2] = inked ? (byte)30 : sheet.R;
                pixels[offset + 3] = 255;
            }
        }

        return BitmapSource.Create(width, height, 96, 96, PixelFormats.Bgra32, null, pixels, width * 4);
    }

    private static async Task<List<string>> Texts(PdfDocumentSession session)
    {
        var pages = new PdfPageSource(session.Arrangement, session.Opened(), Brushes.White);
        var texts = new List<string>();
        for (var index = 0; index < pages.PageCount; index++)
            texts.Add((await pages.TextAsync(index, CancellationToken.None)).Trim());
        return texts;
    }

    private static async Task<PdfDocumentSession> Opened(string? path = null)
    {
        var session = new PdfDocumentSession(path ?? Copy());
        Assert.IsTrue(await session.OpenAsync(CancellationToken.None));
        return session;
    }

    private static string Copy()
    {
        var path = Path.Combine(TestTemp.CreateSubdirectory().FullName, "two-up.pdf");
        File.Copy(TestSampleData.Path("pdf", "two-up.pdf"), path);
        return path;
    }

    private static string Numbers(PdfPageArrangement pages) =>
        string.Join(" ", pages.Pages.Select(page => string.Join("|", page.Sheet.Pieces.Select(piece => piece.Number))));
}
