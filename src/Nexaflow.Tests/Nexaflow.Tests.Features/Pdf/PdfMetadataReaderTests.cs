using System.Linq;
using System.Threading;
using Nexaflow.Features.Pdf.Reading;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.Features.Pdf;

/// <summary>
/// What a PDF says about itself: the information dictionary and the table of contents. This is what the
/// reader's side panel shows the moment a document opens, and what the AI's <c>pdf_get_info</c> /
/// <c>pdf_outline</c> tools return — all of it read from the document catalogue, so none of it waits on a
/// content stream.
/// </summary>
[TestClass]
[CoversNode("pdf-page-reading")]
public class PdfMetadataReaderTests
{
    private static string Sample(string name) => TestSampleData.Path("pdf", name);

    private static PdfDocumentInfo? Read(string name)
    {
        using var scope = PdfDocumentScope.TryOpen(Sample(name), CancellationToken.None);
        return scope is null ? null : PdfMetadataReader.Read(scope.Document, CancellationToken.None);
    }

    // ── The information dictionary ────────────────────────────────────────────

    [TestMethod]
    public void Read_ReportsTheInformationDictionary()
    {
        var info = Read("text.pdf");

        Assert.IsNotNull(info);
        Assert.AreEqual(PdfSamples.MetadataTitle, info.Title);
        Assert.AreEqual("Jane Smith", info.Author);
        Assert.AreEqual("Raptor performance", info.Subject);
        StringAssert.Contains(info.Keywords, "falcons");
        Assert.AreEqual(1, info.PageCount);
    }

    [TestMethod]
    public void Read_ReportsTheFieldsTheSearchPathIgnores()
    {
        // Producer/creator/dates are read by nothing on the search side — they make a document findable by
        // nothing anyone would type. The panel shows them because "who made this, and when" is exactly what
        // someone looking at a document's properties came to find out.
        var info = Read("outline.pdf");

        Assert.IsNotNull(info);
        Assert.AreEqual(PdfSamples.OutlineProducer, info.Producer);
        Assert.AreEqual("Fixture Generator", info.Creator);
        Assert.IsNotNull(info.CreationDate);
        Assert.IsNotNull(info.ModifiedDate);
    }

    [TestMethod]
    public void Read_ReportsAFormAndItsFieldCount()
    {
        var info = Read("text.pdf");

        Assert.IsNotNull(info);
        Assert.IsTrue(info.HasForm, "text.pdf carries an AcroForm");
        Assert.IsTrue(info.FormFieldCount >= 1);
    }

    [TestMethod]
    public void Read_BlankFieldsComeBackNull_NotEmpty()
    {
        // The panel omits a row rather than printing a dash, so "absent" has to be distinguishable from
        // "present and empty" without the caller inspecting whitespace.
        var info = Read("image-only.pdf");

        Assert.IsNotNull(info);
        Assert.IsNull(info.Title);
        Assert.IsNull(info.Author);
    }

    // ── The outline ───────────────────────────────────────────────────────────

    [TestMethod]
    public void Outline_CarriesTitlesNestingAndPageNumbers()
    {
        // The page number is the load-bearing part: without it a table of contents can't be jumped to, by
        // the user or by the model.
        var info = Read("outline.pdf");

        Assert.IsNotNull(info);
        Assert.AreEqual(5, info.Outline.Count);

        var first = info.Outline[0];
        Assert.AreEqual(PdfSamples.OutlineRootTitle, first.Title);
        Assert.AreEqual(0, first.Level);
        Assert.AreEqual(1, first.PageNumber);

        var child = info.Outline[1];
        Assert.AreEqual(PdfSamples.OutlineChildTitle, child.Title);
        Assert.AreEqual(1, child.Level, "the nested bookmark sits one level in");
        Assert.AreEqual(2, child.PageNumber);

        var second = info.Outline[2];
        Assert.AreEqual(PdfSamples.OutlineSecondTitle, second.Title);
        Assert.AreEqual(0, second.Level);
        Assert.AreEqual(3, second.PageNumber);
    }

    [TestMethod]
    public void Outline_KeepsAGroupingBookmark_RatherThanHoistingItsChildren()
    {
        // The regression this exists for. PdfPig defaults to discarding a bookmark that has children but no
        // destination of its own - the "Part II over its chapters" shape that most real tables of contents
        // are built from - and silently promotes its children in its place. The outline then looks plausible
        // and is missing exactly its section headings, which is far worse than looking broken.
        var info = Read("outline.pdf");

        Assert.IsNotNull(info);

        var group = info.Outline.SingleOrDefault(e => e.Title == PdfSamples.OutlineGroupTitle);
        Assert.IsNotNull(group, "the grouping bookmark itself must appear, not just its children");
        Assert.AreEqual(0, group.Level);
        Assert.IsNull(group.PageNumber, "it has no destination, so there is no page to jump to");

        var child = info.Outline.SingleOrDefault(e => e.Title == PdfSamples.OutlineGroupChildTitle);
        Assert.IsNotNull(child);
        Assert.AreEqual(1, child.Level, "and its child stays nested under it rather than being promoted");
        Assert.AreEqual(3, child.PageNumber);
    }

    [TestMethod]
    public void Outline_ReportsTheDestinationsPosition_MeasuredDownFromTheTopOfThePage()
    {
        // Two things at once, and the second is the one that bit.
        //
        // A bookmark points at a heading part-way down a page, not at the page's top corner — drop that and a
        // click lands on the right page and leaves the reader to find the section themselves.
        //
        // And the axis has to be flipped. PDF user space has its origin at the BOTTOM-left, so a heading near
        // the top of the page carries a large y; the viewer's view=FitH parameter wants a distance from the
        // TOP. Passing the raw coordinate through sends every jump to the mirror image of where it belongs —
        // top-of-page headings land near the bottom and vice versa — which looks like a working feature until
        // someone actually reads the result.
        var info = Read("outline.pdf");

        Assert.IsNotNull(info);

        var child = info.Outline.Single(e => e.Title == PdfSamples.OutlineChildTitle);
        Assert.AreEqual(PdfSamples.OutlineChildOffsetFromTop, child.OffsetFromTop);
        Assert.AreNotEqual(PdfSamples.OutlineChildDestinationY, child.OffsetFromTop,
            "the raw PDF y coordinate must NOT survive to the caller — that is the inverted-jump bug");
        Assert.IsTrue(child.OffsetFromTop < PdfSamples.OutlinePageHeight / 2,
            "the destination is near the top of the page, so its offset from the top must be small");

        // A /Fit destination describes a whole page rather than a point on it, so it legitimately has none
        // and the caller falls back to the top of the page.
        var wholePage = info.Outline.Single(e => e.Title == PdfSamples.OutlineSecondTitle);
        Assert.IsNull(wholePage.OffsetFromTop);
    }

    [TestMethod]
    public void Outline_ResolvesTheDestinationFormsRealDocumentsActuallyUse()
    {
        // outline.pdf uses plain explicit destinations, which is the form a hand-written fixture reaches for
        // and very nearly the only form real tools don't emit. hyperref names every destination; word
        // processors wrap them in a GoTo action. A reader that resolves only explicit destinations shows a
        // table of contents with every page number missing — which looks like a styling bug, not a parsing one.
        var info = Read("outline-named.pdf");

        Assert.IsNotNull(info);
        Assert.AreEqual(3, info.Outline.Count);

        var named = info.Outline.Single(e => e.Title == PdfSamples.NamedDestTitle);
        Assert.AreEqual(1, named.PageNumber, "a /Dest (name) must resolve through the /Names /Dests tree");

        var action = info.Outline.Single(e => e.Title == PdfSamples.ActionDestTitle);
        Assert.AreEqual(2, action.PageNumber, "a /A /GoTo action carries the destination just as /Dest does");

        var both = info.Outline.Single(e => e.Title == PdfSamples.ActionNamedDestTitle);
        Assert.AreEqual(3, both.PageNumber, "an action naming a destination has to be followed twice");
    }

    [TestMethod]
    public void Outline_IsDepthFirst_SoItReadsInDocumentOrder()
    {
        var info = Read("outline.pdf");

        Assert.IsNotNull(info);
        CollectionAssert.AreEqual(
            new[]
            {
                PdfSamples.OutlineRootTitle, PdfSamples.OutlineChildTitle, PdfSamples.OutlineSecondTitle,
                PdfSamples.OutlineGroupTitle, PdfSamples.OutlineGroupChildTitle,
            },
            info.Outline.Select(e => e.Title).ToArray());
    }

    [TestMethod]
    public void Outline_IsEmpty_WhenTheDocumentHasNoBookmarks()
    {
        // Empty is a real answer here, not a failure — most PDFs have no outline at all, and the panel says
        // so rather than showing a blank box.
        var info = Read("text.pdf");

        Assert.IsNotNull(info);
        Assert.AreEqual(0, info.Outline.Count);
    }

    // ── Unreadable documents ──────────────────────────────────────────────────

    [TestMethod]
    public void CorruptPdf_NeverReachesTheReader()
    {
        // The scope declines first, so the panel's "couldn't read this" message comes from a document that
        // genuinely wouldn't open — never from a reader silently returning empty facts about one that did.
        using var scope = PdfDocumentScope.TryOpen(Sample("corrupt.pdf"), CancellationToken.None);
        Assert.IsNull(scope);
    }
}
