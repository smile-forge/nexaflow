using System.Collections.Generic;
using System.IO;
using System.Linq;
using Nexaflow.Features.Pdf.Reading;
using Nexaflow.Tests.Fixtures;
using UglyToad.PdfPig;
using UglyToad.PdfPig.Content;
using UglyToad.PdfPig.Tokens;

namespace Nexaflow.Tests.Features.Pdf;

/// <summary>
/// <c>JPXDecode</c> through Nexaflow.Media's JPEG 2000 decoder, over the JPEG 2000 images in PdfPig's own test
/// documents — which stay with the PdfPig submodule.
/// </summary>
[TestClass]
[CoversNode("pdf-image-filters")]
public class PdfJpxFilterTests
{
    private const string IndexedWithPalette = "Documents/Indexed-DeviceRGB-JPXDecode-0-.0.0.255.0.-Font-F1_1_missing_char_255-1.pdf";

    [TestMethod]
    public void AnIndexedImageKeepsItsPaletteIndices()
    {
        // A JP2 file with a palette of its own (a pclr box), under the PDF's own /Indexed colour space: the PDF's
        // lookup table reads the samples, so they stay indices — one a pixel, each row whole bytes — rather than the
        // codestream's palette applied, three colours a pixel.
        using var document = Open(IndexedWithPalette);
        var images = JpxImages(document);

        Assert.IsTrue(images.Count > 0, "the document holds a JPEG 2000 image");
        foreach (var image in images)
        {
            Assert.IsTrue(image.TryGetBytesAsMemory(out var samples));
            var row = (image.WidthInSamples * image.BitsPerComponent + 7) / 8;
            Assert.AreEqual(row * image.HeightInSamples, samples.Length);
            Assert.IsTrue(image.TryGetPng(out _));
        }
    }

    [TestMethod]
    [DataRow("SpecificTestDocuments/GHOSTSCRIPT-688999-2.pdf", DisplayName = "a bare codestream")]
    [DataRow("Documents/68-1990-01_A.pdf", DisplayName = "JP2 files")]
    [DataRow("Documents/MOZILLA-3136-0.pdf", DisplayName = "a palette applied, no /ColorSpace declared")]
    public void EveryJpegTwoThousandImage_DecodesToAPicture(string document)
    {
        using var pdf = Open(document);
        var images = JpxImages(pdf);

        Assert.IsTrue(images.Count > 0, "the document holds a JPEG 2000 image");
        foreach (var image in images)
            Assert.IsTrue(image.TryGetPng(out var png) && png.Length > 0, $"{image.WidthInSamples}x{image.HeightInSamples} decoded");
    }

    private static PdfDocument Open(string document) =>
        PdfDocument.Open(
            Path.Combine(TestSampleData.RepoRoot, "external", "PdfPig", "src", "UglyToad.PdfPig.Tests", "Integration", document),
            new ParsingOptions { UseLenientParsing = true, SkipMissingFonts = true, FilterProvider = PdfFilterProvider.Instance });

    private static List<IPdfImage> JpxImages(PdfDocument document) =>
        document.GetPages().SelectMany(page => page.GetImages()).Where(IsJpx).ToList();

    private static bool IsJpx(IPdfImage image) =>
        image.ImageDictionary.TryGet(NameToken.Filter, out NameToken filter) && filter.Equals(NameToken.JpxDecode)
        || image.ImageDictionary.TryGet(NameToken.Filter, out ArrayToken filters) && filters.Data.OfType<NameToken>().Any(f => f.Equals(NameToken.JpxDecode));
}
