using System.IO;
using System.Threading;
using System.Windows.Media.Imaging;
using Nexaflow.Features.Pdf.Reading;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.Features.Pdf;

/// <summary>A document's scanned pages found — no text on them, one picture over each — and each one's picture as a
/// page reader takes it.</summary>
[TestClass]
[CoversNode("pdf-scan-reading")]
public class PdfScansTests
{
    private static PdfDocumentScope Open(string name)
    {
        var scope = PdfDocumentScope.TryOpen(TestSampleData.Path("pdf", name), CancellationToken.None);
        Assert.IsNotNull(scope, $"{name} should open");
        return scope;
    }

    [TestMethod]
    public void APageThatIsOnePictureAndNoText_IsAScan()
    {
        using var scope = Open("image-only.pdf");

        CollectionAssert.AreEqual(new[] { 1 }, PdfScans.Find(scope.Document, 0.9, CancellationToken.None).ToArray());
    }

    [TestMethod]
    public void PagesWithText_AreNotScans()
    {
        using var text = Open("text.pdf");
        using var outline = Open("outline.pdf");

        Assert.AreEqual(0, PdfScans.Find(text.Document, 0.9, CancellationToken.None).Count);
        Assert.AreEqual(0, PdfScans.Find(outline.Document, 0.9, CancellationToken.None).Count);
    }

    [TestMethod]
    public void AScansPicture_IsOneAReaderDecodes()
    {
        using var scope = Open("image-only.pdf");

        var picture = PdfScans.Picture(scope.Document, 1);

        Assert.IsNotNull(picture);
        var frame = BitmapDecoder.Create(new MemoryStream(picture), BitmapCreateOptions.None, BitmapCacheOption.OnLoad).Frames[0];
        Assert.IsTrue(frame.PixelWidth > 0 && frame.PixelHeight > 0);
        Assert.IsTrue(picture[0] == 0x89 || picture[0] == 0xFF, "PNG or JPEG, which the reader reads");
    }
}
