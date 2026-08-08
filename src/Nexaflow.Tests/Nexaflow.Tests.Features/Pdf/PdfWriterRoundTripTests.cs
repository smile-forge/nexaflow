using System.IO;
using System.Linq;
using Nexaflow.Features.Pdf;
using Nexaflow.Features.Pdf.Reading;
using Nexaflow.Features.Pdf.Search;
using Nexaflow.Tests.Fixtures;
using UglyToad.PdfPig.Core;
using UglyToad.PdfPig.Fonts.Standard14Fonts;
using UglyToad.PdfPig.Writer;

namespace Nexaflow.Tests.Features.Pdf;

/// <summary>
/// The same extraction, but against a PDF built by a real writer rather than by hand.
/// <para>
/// This exists because the hand-encoded fixtures in <see cref="PdfSamples"/> use uncompressed content streams
/// and plain cross-reference tables — legal PDF, but not what any tool in the world actually emits. A writer
/// produces Flate-compressed streams and a properly built object graph, so this is the test that would catch
/// an extractor that only ever worked on the fixtures.
/// </para>
/// </summary>
[TestClass]
[CoversNode("pdf-search")]
[CoversNode("pdf-extract-images")]
public class PdfWriterRoundTripTests
{
    private const string Needle = "peregrine";
    private const long Budget = 4 * 1024 * 1024;

    private string _dir = string.Empty;

    [TestInitialize]
    public void Setup()
    {
        _dir = Path.Combine(Path.GetTempPath(), "nexapdfrt_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
    }

    [TestCleanup]
    public void Cleanup()
    {
        if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true);
    }

    /// <summary>
    /// Two pages: text plus a JPEG on the first, the <em>same</em> JPEG plus a PNG on the second. Real image
    /// bytes come from the shared image fixtures rather than being invented here.
    /// </summary>
    private string WriteDocument()
    {
        var jpegBytes = File.ReadAllBytes(FirstImage(".jpg"));
        var pngBytes  = File.ReadAllBytes(FirstImage(".png"));

        using var builder = new PdfDocumentBuilder();
        var font = builder.AddStandard14Font(Standard14Font.Helvetica);

        var page1 = builder.AddPage(400, 400);
        page1.AddText($"the {Needle} stoops on prey", 12, new PdfPoint(20, 350), font);
        var jpeg = page1.AddJpeg(jpegBytes, new PdfRectangle(20, 20, 140, 140));

        var page2 = builder.AddPage(400, 400);
        page2.AddJpeg(jpeg, new PdfRectangle(20, 20, 140, 140));   // reuses the object, as a letterhead would
        page2.AddPng(pngBytes, new PdfRectangle(160, 160, 280, 280));

        var path = Path.Combine(_dir, "written.pdf");
        File.WriteAllBytes(path, builder.Build());
        return path;
    }

    private static string FirstImage(string extension) =>
        TestSampleData.Files("images")
            .First(p => p.EndsWith(extension, StringComparison.OrdinalIgnoreCase));

    [TestMethod]
    public async Task Text_IsExtractedFromACompressedContentStream()
    {
        var text = await new PdfTextExtractor(new PdfConfig()).ExtractAsync(WriteDocument(), Budget, default);

        Assert.IsNotNull(text);
        StringAssert.Contains(text, Needle);
        StringAssert.Contains(text, $"{Needle} stoops", "words must still come out separated");
    }

    [TestMethod]
    public void Images_AreExtracted_WithTheJpegPassedThroughAndTheRepeatDropped()
    {
        using var scope = PdfDocumentScope.TryOpen(WriteDocument(), default);
        Assert.IsNotNull(scope);

        var tally = default(PdfImageTally);
        var images = PdfImageReader.Read(scope.Document, t => tally = t, default).ToList();

        Assert.AreEqual(2, images.Count, "one JPEG and one PNG — the JPEG's second appearance is the same image");
        Assert.AreEqual(1, tally.Duplicates);

        var jpeg = images.Single(i => i.Extension == ".jpg");
        ReadOnlySpan<byte> jpegSignature = [0xFF, 0xD8, 0xFF];
        Assert.IsTrue(jpeg.Bytes.Span.StartsWith(jpegSignature),
            "a JPEG the PDF already holds is copied out verbatim, not re-encoded");

        Assert.IsTrue(images.Any(i => i.Extension == ".png"));
        Assert.AreEqual(0, tally.Undecodable);
    }

    [TestMethod]
    public async Task ExtractionTask_WritesBothImages()
    {
        var task = new Nexaflow.Features.Pdf.Services.PdfImageExtractionTask([WriteDocument()], _dir);
        await task.RunAsync(default);

        var written = Directory.GetFiles(Path.Combine(_dir, "written")).Select(Path.GetFileName).ToList();

        CollectionAssert.AreEquivalent(new[] { "p001-01.jpg", "p002-02.png" }, written,
            "named by the page and position they were first seen at");
    }
}
