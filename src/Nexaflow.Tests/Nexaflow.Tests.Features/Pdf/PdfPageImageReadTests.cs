using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Nexaflow.Features.Pdf.Reading;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.Features.Pdf;

/// <summary>
/// Asking a PDF what is on <em>one</em> page, which is a different question from "give me every distinct
/// image in this document" and needs the opposite answer about repeats.
/// </summary>
[TestClass]
[CoversNode("pdf-page-reading")]
public class PdfPageImageReadTests
{
    private static PdfDocumentScope Open(string name)
    {
        var scope = PdfDocumentScope.TryOpen(TestSampleData.Path("pdf", name), CancellationToken.None);
        Assert.IsNotNull(scope, $"{name} should open");
        return scope;
    }

    [TestMethod]
    public void ReadPage_YieldsThePagesImage()
    {
        using var scope = Open("image-only.pdf");

        var images = PdfImageReader.ReadPage(scope.Document, 1, CancellationToken.None);

        Assert.AreEqual(1, images.Count);
        Assert.AreEqual(1, images[0].PageNumber);
        Assert.AreEqual(1, images[0].IndexOnPage);
        Assert.IsTrue(images[0].Bytes.Length > 0);
    }

    [TestMethod]
    public void ReadPage_ReportsARepeatedImage_WhereTheDocumentWideReadDropsIt()
    {
        // The dedup split, pinned. Read's document-wide hash filter is right for writing files out — one
        // header logo shouldn't land on disk forty times. Applied to "what is on page 2" it answers
        // "nothing", which is simply false, and would send a caller looking at that page down the wrong path.
        using var scope = Open("repeated-image.pdf");

        var wholeDocument = PdfImageReader.Read(scope.Document, null, CancellationToken.None).ToList();
        Assert.AreEqual(1, wholeDocument.Count, "the same image on two pages is written out once");
        Assert.AreEqual(1, wholeDocument[0].PageNumber);

        var pageTwo = PdfImageReader.ReadPage(scope.Document, 2, CancellationToken.None);
        Assert.AreEqual(1, pageTwo.Count, "page 2 draws that image too, and asking about page 2 must say so");
        Assert.AreEqual(2, pageTwo[0].PageNumber);
    }

    [TestMethod]
    public void ReadPage_IsEmptyForAPageWithNoImages()
    {
        using var scope = Open("text.pdf");

        Assert.AreEqual(0, PdfImageReader.ReadPage(scope.Document, 1, CancellationToken.None).Count);
    }

    [TestMethod]
    public void ReadPage_IsEmptyForAPageThatDoesNotExist()
    {
        using var scope = Open("text.pdf");

        Assert.AreEqual(0, PdfImageReader.ReadPage(scope.Document, 99, CancellationToken.None).Count);
        Assert.AreEqual(0, PdfImageReader.ReadPage(scope.Document, 0, CancellationToken.None).Count);
    }

    // ── Inventory ─────────────────────────────────────────────────────────────

    [TestMethod]
    public void Inventory_CountsRepeatsRatherThanHidingThem()
    {
        using var scope = Open("repeated-image.pdf");

        var rows = PdfImageReader.Inventory(scope.Document, CancellationToken.None).ToList();

        Assert.AreEqual(2, rows.Count, "two drawings of one image are two rows");
        Assert.IsFalse(rows[0].IsRepeat);
        Assert.IsTrue(rows[1].IsRepeat, "the second is flagged, not dropped — '2 images, 1 distinct' is the truth");
        CollectionAssert.AreEqual(new[] { 1, 2 }, rows.Select(r => r.PageNumber).ToArray());
    }

    [TestMethod]
    public void Inventory_ReportsPixelSizeAndFormat()
    {
        using var scope = Open("jpeg-image.pdf");

        var row = PdfImageReader.Inventory(scope.Document, CancellationToken.None).Single();

        Assert.AreEqual(2, row.WidthInSamples);
        Assert.AreEqual(2, row.HeightInSamples);
        Assert.AreEqual(".jpg", row.Extension, "a stored JPEG passes through without re-encoding");
        Assert.IsTrue(row.ByteLength > 0);
    }

    [TestMethod]
    public void Inventory_ReportsHowMuchOfThePageAnImageCovers()
    {
        // Coverage is the signal that separates "this page IS a scan" from "this page has a picture on it",
        // which decides whether a caller can read the page by looking at its embedded image or has to
        // photograph the renderer.
        using var scope = Open("image-only.pdf");
        var scanned = PdfImageReader.Inventory(scope.Document, CancellationToken.None).Single();

        using var partial = Open("repeated-image.pdf");
        var illustration = PdfImageReader.Inventory(partial.Document, CancellationToken.None).First();

        Assert.IsTrue(scanned.PageCoverage >= 0.9,
            $"a full-bleed scan covers its page (was {scanned.PageCoverage:P0})");
        Assert.IsTrue(illustration.PageCoverage < 0.9,
            $"a quarter-page logo does not (was {illustration.PageCoverage:P0})");
    }

    [TestMethod]
    public void Inventory_IsEmptyForATextOnlyDocument()
    {
        using var scope = Open("text.pdf");

        Assert.AreEqual(0, PdfImageReader.Inventory(scope.Document, CancellationToken.None).Count());
    }
}
