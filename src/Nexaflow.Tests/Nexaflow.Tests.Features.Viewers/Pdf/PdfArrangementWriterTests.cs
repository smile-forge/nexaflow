using System.IO;
using Nexaflow.Features.Pdf.Editing;
using Nexaflow.Tests.Fixtures;
using UglyToad.PdfPig;
using UglyToad.PdfPig.Writer;

namespace Nexaflow.Tests.Features.Pdf;

/// <summary>
/// An arrangement written whole: the pages in their new order from each source, through the fork's assembler, with
/// the document's outline following its pages.
/// </summary>
[TestClass]
[CoversNode("pdf-document-assembly")]
public class PdfArrangementWriterTests
{
    private static readonly PdfSourceId Base = new(0);
    private static readonly PdfSourceId Inserted = new(1);

    [TestMethod]
    [TestCategory("Unit")]
    public void TheDocument_IsWrittenInTheOrderArranged_WithPagesFromAnotherDocument()
    {
        var path = TestSampleData.Path("pdf", "outline.pdf");
        using var source = PdfDocument.Open(path);
        var count = source.NumberOfPages;

        var pages = Enumerable.Range(1, count).Reverse().Select(number => new PdfArrangedPage(new PdfPageId(number), Base, number))
            .Append(new PdfArrangedPage(new PdfPageId(100), Inserted, 1))
            .ToList();
        var sources = new Dictionary<PdfSourceId, PdfSourceFile>
        {
            [Base] = new(path, null),
            [Inserted] = new(TestSampleData.Path("pdf", "text.pdf"), new AssembledContentTag("Figure", "text.pdf")),
        };

        using var output = new MemoryStream();
        PdfArrangementWriter.Write(PdfPageArrangement.Of(pages), sources, Base, output, CancellationToken.None);

        using var written = PdfDocument.Open(output.ToArray());
        Assert.AreEqual(count + 1, written.NumberOfPages);
        for (var i = 0; i < count; i++)
            Assert.AreEqual(source.GetPage(count - i).Text, written.GetPage(i + 1).Text);
        Assert.IsTrue(written.TryGetBookmarks(out var bookmarks) && bookmarks.Roots.Count > 0, "the outline is carried");
    }

    [TestMethod]
    [TestCategory("Unit")]
    public void ASourceThatCannotBeRead_SaysSo()
    {
        var missing = Path.Combine(TestTemp.CreateSubdirectory().FullName, "gone.pdf");
        var pages = PdfPageArrangement.Of([new PdfArrangedPage(new PdfPageId(1), Base, 1)]);

        Assert.ThrowsExactly<IOException>(() => PdfArrangementWriter.Write(
            pages, new Dictionary<PdfSourceId, PdfSourceFile> { [Base] = new(missing, null) }, Base, new MemoryStream(), CancellationToken.None));
    }
}
