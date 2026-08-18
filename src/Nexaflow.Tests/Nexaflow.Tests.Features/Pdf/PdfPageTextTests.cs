using System.Linq;
using System.Threading;
using Nexaflow.Features.Pdf.Reading;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.Features.Pdf;

/// <summary>
/// Reading a document page by page — the AI's route into a PDF's text. The whole-document
/// <see cref="PdfTextReader.Read"/> flattens everything because search only needs a bag of words; a reader
/// needs to say "page 12", request the next ten pages, and know when it has been cut short.
/// </summary>
[TestClass]
[CoversNode("pdf-page-reading")]
public class PdfPageTextTests
{
    private const long Budget = 4 * 1024 * 1024;

    private static string Sample(string name) => TestSampleData.Path("pdf", name);

    private static List<PdfPageText> ReadPages(string name, int from, int to, long budget = Budget)
    {
        using var scope = PdfDocumentScope.TryOpen(Sample(name), CancellationToken.None);
        Assert.IsNotNull(scope, $"{name} should open");
        return PdfTextReader.ReadPages(scope.Document, from, to, budget, null, CancellationToken.None).ToList();
    }

    // ── Page identity ─────────────────────────────────────────────────────────

    [TestMethod]
    public void ReadPages_KeepsPageNumbers()
    {
        var pages = ReadPages("outline.pdf", 1, 3);

        CollectionAssert.AreEqual(new[] { 1, 2, 3 }, pages.Select(p => p.PageNumber).ToArray());
        StringAssert.Contains(pages[0].Text, "first");
        StringAssert.Contains(pages[1].Text, "second");
        StringAssert.Contains(pages[2].Text, "third");
    }

    [TestMethod]
    public void ReadPages_ReturnsOnlyTheRangeAsked()
    {
        var pages = ReadPages("outline.pdf", 2, 2);

        Assert.AreEqual(1, pages.Count);
        Assert.AreEqual(2, pages[0].PageNumber);
        StringAssert.Contains(pages[0].Text, "second");
    }

    [TestMethod]
    public void ReadPages_ClampsAnEndPastTheLastPage()
    {
        // Over-reaching the end is the normal shape of "read from here on" and costs nothing to absorb.
        // Over-reaching the *start* is a different matter and is rejected by the tool, not here.
        var pages = ReadPages("outline.pdf", 2, 99);

        CollectionAssert.AreEqual(new[] { 2, 3 }, pages.Select(p => p.PageNumber).ToArray());
    }

    [TestMethod]
    public void ReadPages_YieldsNothing_WhenTheRangeStartsPastTheEnd()
    {
        Assert.AreEqual(0, ReadPages("outline.pdf", 10, 20).Count);
    }

    [TestMethod]
    public void ScannedPage_ComesBackEmpty_NotMissing()
    {
        // An image-only page must still be reported, with empty text: "page 4 has no text" is what tells the
        // model to look at the picture instead. Skipping the page would look like the document ended.
        var pages = ReadPages("image-only.pdf", 1, 1);

        Assert.AreEqual(1, pages.Count);
        Assert.AreEqual(1, pages[0].PageNumber);
        Assert.IsTrue(string.IsNullOrWhiteSpace(pages[0].Text));
        Assert.IsFalse(pages[0].Truncated, "empty because there is nothing there, not because we ran out");
    }

    // ── The budget ────────────────────────────────────────────────────────────

    [TestMethod]
    public void ReadPages_FlagsThePageItRanOutOn()
    {
        // A tiny budget: the first page can't be finished, and the caller has to be able to tell that from a
        // document that simply ended.
        var pages = ReadPages("outline.pdf", 1, 3, budget: 8);

        Assert.IsTrue(pages.Count >= 1);
        Assert.IsTrue(pages.Last().Truncated, "the last page returned was cut short");
        Assert.IsTrue(pages.Count < 3, "it stops rather than reading on past the budget");
    }

    [TestMethod]
    public void ZeroBudget_StillNamesThePageItCouldNotRead()
    {
        var pages = ReadPages("outline.pdf", 1, 3, budget: 0);

        Assert.AreEqual(1, pages.Count);
        Assert.AreEqual(1, pages[0].PageNumber);
        Assert.IsTrue(pages[0].Truncated);
    }

    // ── The search path must not have moved ───────────────────────────────────

    [TestMethod]
    public void WholeDocumentRead_StillProducesTheSearchText()
    {
        // ReadPages and Read now share one word-appending implementation. This is the guard that the sharing
        // didn't quietly change what search sees: body text, title, and the filled-in form value, all of
        // which the search index depends on.
        using var scope = PdfDocumentScope.TryOpen(Sample("text.pdf"), CancellationToken.None);
        Assert.IsNotNull(scope);

        var result = PdfTextReader.Read(scope.Document, Budget, includeMetadata: true, CancellationToken.None);

        Assert.IsFalse(result.Truncated);
        StringAssert.Contains(result.Text, PdfSamples.BodyNeedle);
        StringAssert.Contains(result.Text, PdfSamples.MetadataTitle);
        StringAssert.Contains(result.Text, PdfSamples.FormFieldValue);
    }
}
