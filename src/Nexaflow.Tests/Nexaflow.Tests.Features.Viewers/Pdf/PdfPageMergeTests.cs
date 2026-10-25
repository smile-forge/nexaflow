using Nexaflow.IO.Common;
using System.IO;
using System.Windows;
using System.Windows.Media;
using Nexaflow.Features.Pdf.Document;
using Nexaflow.Features.Pdf.Editing;
using Nexaflow.Features.Pdf.Rendering;
using Nexaflow.Tests.Fixtures;
using Nexaflow.Features.Pdf.Reading;

namespace Nexaflow.Tests.Features.Pdf;

/// <summary>
/// Pages as sheets: pages put several to a sheet, a booklet folded from its pages in reading order, a page set on paper of
/// another size — every page a sheet of pieces, each a part of a source page placed by a transform, and pieces of a page
/// that lie on the sheet as they lie on the page joined back into it. The sample is a four-page booklet on two landscape
/// sheets — 4|1 and 2|3.
/// </summary>
[TestClass]
public class PdfPageMergeTests
{
    private static readonly PdfSourceId Source = new(0);

    /// <summary>The sample's sheets, 792 by 612 points; any other page here is an upright one, 306 by 792.</summary>
    private static readonly PdfShownSize Sizes = (_, number) => number <= 2 ? new Size(792, 612) : new Size(306, 792);

    [TestMethod]
    [TestCategory("Unit")]
    [CoversNode("pdf-page-transform")]
    public void APageTransform_IsUndoneByItsInverse_AndKeepsAPageSquareToItsSheet()
    {
        var turned = PdfPageTransform.Turned(1, new Size(100, 50));
        Assert.AreEqual(new Point(50, 0), turned.Apply(new Point(0, 0)), "turned clockwise, the top left goes to the top right");
        Assert.AreEqual(new Rect(0, 0, 50, 100), turned.Apply(new Rect(0, 0, 100, 50)));

        var placed = PdfPageTransform.Scaled(0.5, 0.5).Then(turned).Then(PdfPageTransform.Translate(10, 20));
        Assert.IsTrue(placed.Then(placed.Inverse).IsIdentity);
        Assert.AreEqual(PdfPageTransform.Identity, PdfPageTransform.Translate(0.1, 0).Then(PdfPageTransform.Translate(0.2, 0)).Then(PdfPageTransform.Translate(-0.3, 0)),
            "two ways to the same place are the one transform");

        Assert.ThrowsExactly<ArgumentException>(() => PdfPageTransform.Of(new Matrix(1, 0.2, 0, 1, 0, 0)), "skewed");
        Assert.ThrowsExactly<ArgumentException>(() => PdfPageTransform.Of(new Matrix(-1, 0, 0, 1, 0, 0)), "mirrored");
    }

    [TestMethod]
    [TestCategory("Unit")]
    [CoversNode("pdf-page-transform")]
    public void PartsOfAPage_PutBackAsThePageLies_AreThatPage_AndPutBackOtherwiseAreASheetOfThem()
    {
        var halves = PdfSheet.Page(Source, 1).Split(2, across: true, Sizes);
        var cell = new Size(396, 612);

        Assert.AreEqual(PdfSheet.Page(Source, 1, PdfPagePart.Split(2, across: true)[1]), halves[1], "a plain page split is a part, as it was");
        Assert.AreEqual(PdfSheet.Page(Source, 1), PdfSheet.Merged([halves[0], halves[1]], across: true, cell, Sizes), "the halves side by side are the sheet they were cut from");

        var swapped = PdfSheet.Merged([halves[1], halves[0]], across: true, cell, Sizes);
        Assert.IsFalse(swapped.IsPlain, "the right half on the left is not the page");
        Assert.AreEqual(2, swapped.Pieces.Length);
        Assert.AreEqual(new Size(792, 612), swapped.ShownSize(Sizes));
    }

    [TestMethod]
    [TestCategory("Unit")]
    [CoversNode("pdf-page-transform")]
    public void ASheetOfPages_SplitsBackIntoThem_AndSplitAcrossThemIntoPiecesOfEach()
    {
        var sheet = PdfSheet.Merged([PdfSheet.Page(Source, 3), PdfSheet.Page(Source, 4)], across: true, new Size(306, 792), Sizes);

        var back = sheet.Split(2, across: true, Sizes);
        Assert.AreEqual(PdfSheet.Page(Source, 3), back[0]);
        Assert.AreEqual(PdfSheet.Page(Source, 4), back[1]);

        var tops = sheet.Split(2, across: false, Sizes)[0]!;
        Assert.AreEqual(2, tops.Pieces.Length, "the top of the sheet is the top of each page");
        Assert.AreEqual(new PdfPagePart(0, 0, 1, 0.5), tops.Pieces[1].Part);
        Assert.AreEqual(new Rect(306, 0, 306, 396), tops.Pieces[1].Bounds(Sizes));

        var turned = PdfSheet.Page(Source, 3).Transformed(PdfPageTransform.Turned(1, new Size(306, 792)), new Size(792, 306), Sizes);
        Assert.AreEqual(new Size(792, 306), turned.ShownSize(Sizes));
        Assert.AreEqual(new PdfPagePart(0, 0, 1, 0.5), turned.Split(2, across: true, Sizes)[1]!.Pieces[0].Part,
            "the right of a page turned clockwise is its top");
    }

    [TestMethod]
    [TestCategory("Unit")]
    [CoversNode("pdf-page-merge")]
    public void ABooklet_IsFoldedFromItsPagesInReadingOrder_OntoWholeSheets()
    {
        var pages = Enumerable.Range(1, 12).Select(number => number.ToString()).ToList();
        CollectionAssert.AreEqual(new[] { "12", "1", "2", "11", "10", "3", "4", "9", "8", "5", "6", "7" }, PdfBooklet.Fold(pages).ToArray());
        CollectionAssert.AreEqual(pages, PdfBooklet.Unfold(PdfBooklet.Fold(pages)).ToArray());

        var six = PdfBooklet.Fold(pages.Take(6).ToList());
        CollectionAssert.AreEqual(new[] { null, "1", "2", null, "6", "3", "4", "5" }, six.ToArray(), "padded to two sheets, blank at the back");
    }

    [TestMethod]
    [TestCategory("Unit")]
    [CoversNode("pdf-page-size")]
    public void APageSetOnPaper_IsScaledToFitAndCentred_TheWayUpItFitsLarger()
    {
        var a3Landscape = new Size(PdfPaper.A3.Size.Height, PdfPaper.A3.Size.Width);

        var (place, paper) = new PdfPageSizing(PdfPaper.A4.Size, PdfOrientation.Auto, PdfContentFit.Scale).SetOnPaper(a3Landscape);
        Assert.AreEqual(PdfPaper.A4.Size.Height, paper.Width, 0.01, "a landscape page goes on landscape paper");
        var placed = place.Apply(new Rect(a3Landscape));
        Assert.AreEqual(paper.Height, placed.Height, 0.01, "scaled down till it fits — by about the square root of two");
        Assert.IsTrue(placed.Width <= paper.Width + 0.01);
        Assert.AreEqual((paper.Width - placed.Width) / 2, placed.X, 0.01, "and centred");

        var (kept, portrait) = new PdfPageSizing(PdfPaper.A4.Size, PdfOrientation.Portrait, PdfContentFit.Keep).SetOnPaper(new Size(100, 100));
        Assert.AreEqual(PdfPaper.A4.Size, portrait);
        var centred = kept.Apply(new Rect(0, 0, 100, 100));
        Assert.AreEqual((portrait.Width - 100) / 2, centred.X, 1e-3, "kept its size, centred");
        Assert.AreEqual((portrait.Height - 100) / 2, centred.Y, 1e-3);
        Assert.AreEqual(100, centred.Width, 1e-3);
    }

    [TestMethod]
    [TestCategory("Unit")]
    [CoversNode("pdf-page-merge")]
    public async Task ABookletUnfolded_FoldsBack_IntoTheSheetsItWasPrintedOn()
    {
        using var session = await Opened();
        session.Split([0, 1], 2, PdfSplitDirection.Auto, booklet: true);
        Assert.AreEqual("1 2 2 1", Numbers(session.Arrangement));

        session.Merge([0, 1, 2, 3], 2, PdfSplitDirection.Auto, booklet: true, PdfMergePaper.None);

        Assert.AreEqual("1 2", Numbers(session.Arrangement));
        Assert.IsTrue(session.Arrangement.Pages.All(page => page.Sheet.Equals(PdfSheet.Page(page.Sheet.Pieces[0].Source, page.Sheet.Pieces[0].Number))),
            "each side is its sheet again, nothing imposed");

        session.Undo();
        Assert.AreEqual("1 2 2 1", Numbers(session.Arrangement), "one edit");
    }

    [TestMethod]
    [TestCategory("Unit")]
    [CoversNode("pdf-page-merge")]
    public async Task PagesMerged_AreCentredInCellsOfTheLargest_ALastShortOfPagesLeavingCellsEmpty()
    {
        using var session = await Opened();
        session.Split([0, 1], 2, PdfSplitDirection.Auto, booklet: false);

        session.Merge([1, 2, 3], 2, PdfSplitDirection.Across, booklet: false, PdfMergePaper.None);

        Assert.AreEqual("1 1|2 2", Numbers(session.Arrangement), "page one beside page two, then page three alone");
        Assert.AreEqual(new Size(792, 612), session.PageSize(1));
        Assert.AreEqual(new Size(792, 612), session.PageSize(2), "the last sheet keeps its empty cell");
        CollectionAssert.AreEqual(new[] { "page four", "page one page two", "page three" }, await Texts(session));
    }

    [TestMethod]
    [TestCategory("Unit")]
    [CoversNode("pdf-page-merge")]
    public async Task PagesMerged_OntoTheirOwnPaper_AreScaledDownToShareIt()
    {
        using var session = await Opened();
        session.Split([0, 1], 2, PdfSplitDirection.Auto, booklet: false);

        session.Merge([1, 2], 2, PdfSplitDirection.Auto, booklet: false, PdfMergePaper.ThePagesOwn);

        Assert.AreEqual(new Size(612, 396), Rounded(session.PageSize(1)), "two upright halves, side by side, on one half turned");
        CollectionAssert.AreEqual(new[] { "page four", "page one page two", "page three" }, await Texts(session));
    }

    [TestMethod]
    [TestCategory("Unit")]
    [CoversNode("pdf-page-size")]
    public async Task APage_IsSetOnPaperOfAnotherSize_AsOneEdit()
    {
        using var session = await Opened();
        var before = await Texts(session);

        session.Resize([0, 1], new PdfPageSizing(PdfPaper.A5.Size, PdfOrientation.Auto, PdfContentFit.Scale));

        Assert.AreEqual(Rounded(new Size(PdfPaper.A5.Size.Height, PdfPaper.A5.Size.Width)), Rounded(session.PageSize(0)));
        CollectionAssert.AreEqual(before, await Texts(session), "nothing on the pages lost");
        session.Undo();
        Assert.AreEqual(new Size(792, 612), session.PageSize(0));
    }

    [TestMethod]
    [TestCategory("Unit")]
    [CoversNode("pdf-page-merge")]
    public async Task ALinkOnAMergedSheet_SaysWhichPieceItIsOn_AndATargetOnOne_IsFoundOnIt()
    {
        using var session = await Opened();
        session.Split([0, 1], 2, PdfSplitDirection.Auto, booklet: false);
        session.Merge([1, 2], 2, PdfSplitDirection.Across, booklet: false, PdfMergePaper.None);
        var pages = new PdfPageSource(session.Arrangement, session.Opened(), Brushes.White);
        var source = session.Arrangement[1].Sheet.Pieces[1].Source;

        Assert.AreEqual(1, pages.Locate(source, new PdfPageTarget(2, Top: 300, Left: 100))?.Index, "page two's left half is on the merged sheet");
        Assert.AreEqual(2, pages.Locate(source, new PdfPageTarget(2, Top: 300, Left: 500))?.Index);

        var onPiece = new PdfPageTarget(2, 300, 100, Piece: 1);
        Assert.IsTrue(PdfLinks.TryRead(PdfLinks.ToPage(onPiece), out var back));
        Assert.AreEqual(onPiece, back, "the piece a link is on round-trips through its target");
    }

    [TestMethod]
    [TestCategory("Unit")]
    [CoversNode("pdf-document-assembly")]
    public async Task SheetsSaved_ShowTheirPages_AndTheirTextIsOnlyWhatTheyShow()
    {
        var path = Copy();
        using (var session = await Opened(path))
        {
            // Page two before page one, side by side: each sheet is placed whole, so the rest of each lies under the
            // other's half — drawn over, and its text no part of the page.
            session.Split([0, 1], 2, PdfSplitDirection.Auto, booklet: false);
            session.Move([2], 1);
            session.Merge([1, 2], 2, PdfSplitDirection.Across, booklet: false, PdfMergePaper.None);
            session.Resize([0], new PdfPageSizing(PdfPaper.A5.Size, PdfOrientation.Auto, PdfContentFit.Scale));
            await session.SaveAsync(null, CancellationToken.None);
        }

        using (var scope = PdfDocumentScope.TryOpen(File.OpenRead(path), CancellationToken.None))
        {
            Assert.IsNotNull(scope);
            Assert.AreEqual(3, scope.Document.NumberOfPages);
    Assert.AreEqual(PdfPaper.A5.Size.Width, scope.Document.GetPage(1).Width, 0.01, "page four, an upright half, on upright A5");
            Assert.AreEqual(PdfPaper.A5.Size.Height, scope.Document.GetPage(1).Height, 0.01);
            Assert.AreEqual(792, scope.Document.GetPage(2).Width, 0.01);
            Assert.AreEqual("pagetwopageone", Letters(scope.Document.GetPage(2).Text), "what the assistant reads is what the sheet shows");
            Assert.AreEqual("pagethree", Letters(scope.Document.GetPage(3).Text), "and of a part, only the part");
        }

        using var reopened = await Opened(path);
        CollectionAssert.AreEqual(new[] { "page four", "page two page one", "page three" }, await Texts(reopened));
    }

    private static string Letters(string text) => string.Concat(text.Where(char.IsLetter));

    private static Size Rounded(Size size) => new(Math.Round(size.Width, 2), Math.Round(size.Height, 2));

    /// <summary>Each page's words, in order, a space between each.</summary>
    private static async Task<List<string>> Texts(PdfDocumentSession session)
    {
        var pages = new PdfPageSource(session.Arrangement, session.Opened(), Brushes.White);
        var texts = new List<string>();
        for (var index = 0; index < pages.PageCount; index++)
            texts.Add(string.Join(" ", (await pages.TextAsync(index, CancellationToken.None)).Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)));
        return texts;
    }

    private static async Task<PdfDocumentSession> Opened(string? path = null)
    {
        var session = new PdfDocumentSession(path ?? Copy(), VirtualFileSystem.Instance);
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
