using System.Collections.Generic;
using System.Linq;
using Nexaflow.Features.Pdf.Reading;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.Features.Pdf;

/// <summary>
/// Pulling the embedded images out of a PDF: which ones come out, in what format, and — the part that decides
/// whether the result is usable — which repeats don't.
/// </summary>
[TestClass]
[CoversNode("pdf-extract-images")]
public class PdfImageReaderTests
{
    private static List<PdfImageData> Read(string sampleName, out PdfImageTally tally)
    {
        using var scope = PdfDocumentScope.TryOpen(TestSampleData.Path("pdf", sampleName), default);
        Assert.IsNotNull(scope, $"{sampleName} should open");

        var captured = default(PdfImageTally);
        var images = PdfImageReader.Read(scope.Document, t => captured = t, default).ToList();
        tally = captured;
        return images;
    }

    [TestMethod]
    public void ImageOnlyPdf_YieldsItsImage_AsPng()
    {
        var images = Read("image-only.pdf", out var tally);

        Assert.AreEqual(1, images.Count);
        Assert.AreEqual(1, tally.Extracted);
        Assert.AreEqual(".png", images[0].Extension,
            "a raw DeviceRGB image has no encoded form to pass through, so it gets decoded to PNG");
        Assert.AreEqual(1, images[0].PageNumber);
        Assert.AreEqual("p001-01.png", images[0].SuggestedFileName);
        Assert.IsTrue(images[0].Bytes.Length > 0);
    }

    [TestMethod]
    public void DecodedImage_IsAValidPng()
    {
        var images = Read("image-only.pdf", out _);

        // Written straight to disk with this extension, so the bytes had better be what the name claims.
        ReadOnlySpan<byte> pngSignature = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];
        Assert.IsTrue(images[0].Bytes.Span.StartsWith(pngSignature));
    }

    [TestMethod]
    public void OneImageDrawnOnTwoPages_IsExtractedOnce()
    {
        var images = Read("repeated-image.pdf", out var tally);

        // The single most important behaviour here. A PDF references one logo XObject from every page, so
        // without deduplication a 40-page report buries the pictures someone wanted under 40 copies of its
        // letterhead.
        Assert.AreEqual(1, images.Count, "the same image on two pages is one image");
        Assert.AreEqual(1, tally.Extracted);
        Assert.AreEqual(1, tally.Duplicates, "and the skipped copy is counted, not silently dropped");
    }

    [TestMethod]
    public void TextOnlyPdf_YieldsNothing()
    {
        var images = Read("text.pdf", out var tally);

        Assert.AreEqual(0, images.Count);
        Assert.AreEqual(0, tally.Extracted);
    }

    [TestMethod]
    public void JpegImage_PassesThroughUnreencoded()
    {
        var images = Read("jpeg-image.pdf", out _);

        Assert.AreEqual(1, images.Count);
        Assert.AreEqual(".jpg", images[0].Extension);

        // The bytes are the PDF's own, copied verbatim: re-encoding a photo to PNG would inflate it for
        // nothing. Detected by signature, so a JPEG with another filter stacked on top correctly falls
        // through to full decoding instead.
        ReadOnlySpan<byte> jpegSignature = [0xFF, 0xD8, 0xFF];
        Assert.IsTrue(images[0].Bytes.Span.StartsWith(jpegSignature));
    }

    [TestMethod]
    public void SuggestedFileNames_SortIntoReadingOrder()
    {
        // Zero-padded so a directory listing doesn't put page 10 before page 2.
        var first = new PdfImageData(2, 1, ReadOnlyMemory<byte>.Empty, ".png").SuggestedFileName;
        var later = new PdfImageData(10, 3, ReadOnlyMemory<byte>.Empty, ".png").SuggestedFileName;

        Assert.AreEqual("p002-01.png", first);
        Assert.AreEqual("p010-03.png", later);
        Assert.IsTrue(string.CompareOrdinal(first, later) < 0);
    }

    [TestMethod]
    public void CorruptPdf_DoesNotOpen()
    {
        using var scope = PdfDocumentScope.TryOpen(TestSampleData.Path("pdf", "corrupt.pdf"), default);
        Assert.IsNull(scope, "null is how 'couldn't read it' is reported — never an exception at the caller");
    }

    [TestMethod]
    public void Cancellation_AbandonsTheRun()
    {
        using var scope = PdfDocumentScope.TryOpen(TestSampleData.Path("pdf", "repeated-image.pdf"), default);
        Assert.IsNotNull(scope);

        using var cts = new CancellationTokenSource();
        cts.Cancel();

        Assert.ThrowsExactly<OperationCanceledException>(
            () => PdfImageReader.Read(scope.Document, null, cts.Token).ToList());
    }
}

/// <summary>
/// PdfPig ships DCTDecode, JBIG2Decode and JPXDecode as stubs that decline, with each real decoder in its own
/// package. Nothing else in the suite would notice them going missing — a scanned document would just quietly
/// yield no images — so the substitution is asserted directly.
/// </summary>
[TestClass]
[CoversNode("pdf-image-filters")]
public class PdfFilterProviderTests
{
    [TestMethod]
    public void TheThreeStubbedImageCodecs_AreAllSupported()
    {
        foreach (var name in new[] { "DCTDecode", "JBIG2Decode", "JPXDecode" })
        {
            var filters = PdfFilterProvider.Instance.GetNamedFilters(
                [UglyToad.PdfPig.Tokens.NameToken.Create(name)]);

            Assert.AreEqual(1, filters.Count, name);
            Assert.IsTrue(filters[0].IsSupported,
                $"/{name} resolves to PdfPig's unimplemented stub unless its decoder package is substituted in");
        }
    }

    [TestMethod]
    public void FiltersTheCoreAlreadyImplements_AreLeftAlone()
    {
        // A decorator, not a rebuilt table — so a filter added to a future PdfPig keeps working rather than
        // silently vanishing from a copied dictionary.
        var filters = PdfFilterProvider.Instance.GetNamedFilters(
            [UglyToad.PdfPig.Tokens.NameToken.Create("FlateDecode")]);

        Assert.AreEqual(1, filters.Count);
        Assert.IsInstanceOfType<UglyToad.PdfPig.Filters.FlateFilter>(filters[0]);
    }
}
