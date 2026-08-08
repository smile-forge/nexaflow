using System.IO;
using Nexaflow.Features.Pdf;
using Nexaflow.Features.Pdf.Search;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.Features.Pdf;

/// <summary>
/// Makes a PDF searchable by content. The contract's whole subtlety is a three-way answer — text, "read it
/// and there is genuinely none", "couldn't read it" — because the search verifier treats any non-null result
/// as authoritative and turns a missing term into a confident rejection.
/// </summary>
[TestClass]
[CoversNode("pdf-search")]
public class PdfTextExtractorTests
{
    private const long Budget = 4 * 1024 * 1024;

    private static PdfTextExtractor NewExtractor(PdfConfig? config = null) => new(config ?? new PdfConfig());

    private static string Sample(string name) => TestSampleData.Path("pdf", name);

    // ── Claiming files ────────────────────────────────────────────────────────

    [TestMethod]
    public void CanExtract_ClaimsPdfsOnly()
    {
        var extractor = NewExtractor();

        Assert.IsTrue(extractor.CanExtract(@"C:\x\report.pdf"));
        Assert.IsTrue(extractor.CanExtract(@"C:\x\REPORT.PDF"), "extension matching is case-insensitive");
        Assert.IsFalse(extractor.CanExtract(@"C:\x\report.txt"));
        Assert.IsFalse(extractor.CanExtract(@"C:\x\pdf"), "a file merely named 'pdf' is not one");
        Assert.IsFalse(extractor.CanExtract(string.Empty));
    }

    [TestMethod]
    public void CanExtract_DoesNotTouchTheDisk()
    {
        // Asked for every candidate file in a sweep, so it has to answer from the name alone.
        Assert.IsTrue(NewExtractor().CanExtract(@"Z:\nonexistent\ghost.pdf"));
    }

    // ── Text ─────────────────────────────────────────────────────────────────

    [TestMethod]
    public async Task Extract_FindsWordsFromThePageContent()
    {
        var text = await NewExtractor().ExtractAsync(Sample("text.pdf"), Budget, default);

        Assert.IsNotNull(text);
        StringAssert.Contains(text, PdfSamples.BodyNeedle);
    }

    [TestMethod]
    public async Task Extract_SeparatesWords_SoWholeWordSearchCanMatch()
    {
        // Search matches whole words, and PdfPig's raw Page.Text concatenates glyphs with no spacing —
        // "theperegrinestoops" would satisfy no query anyone would type. This is the assertion that pins the
        // word extractor being used instead.
        var text = await NewExtractor().ExtractAsync(Sample("text.pdf"), Budget, default);

        Assert.IsNotNull(text);
        StringAssert.Contains(text, $"{PdfSamples.BodyNeedle} stoops",
            "adjacent words must be space-separated, not run together");
    }

    [TestMethod]
    public async Task Extract_IncludesTitleAndFormValues_WhichThePageTextLacks()
    {
        var text = await NewExtractor().ExtractAsync(Sample("text.pdf"), Budget, default);

        Assert.IsNotNull(text);
        StringAssert.Contains(text, PdfSamples.MetadataTitle,
            "a document should be findable by its title even when the body never states its subject");
        StringAssert.Contains(text, PdfSamples.FormFieldValue,
            "a filled-in form's answers exist nowhere but its field values");
    }

    [TestMethod]
    public async Task Extract_OmitsMetadata_WhenTheConfigSaysSo()
    {
        var text = await NewExtractor(new PdfConfig { IncludeMetadata = false })
            .ExtractAsync(Sample("text.pdf"), Budget, default);

        Assert.IsNotNull(text);
        StringAssert.Contains(text, PdfSamples.BodyNeedle, "the page text is unaffected");
        Assert.IsFalse(text.Contains(PdfSamples.MetadataTitle, StringComparison.Ordinal));
    }

    // ── The three-way answer ─────────────────────────────────────────────────

    [TestMethod]
    public async Task ImageOnlyPdf_ReturnsEmpty_NotNull()
    {
        var text = await NewExtractor().ExtractAsync(Sample("image-only.pdf"), Budget, default);

        // Empty is the honest answer and it matters: the verifier trusts a non-null result, so a search term
        // this document lacks is correctly rejected. Null would send it to a raw byte scan and leave every
        // scanned page permanently "possible".
        Assert.IsNotNull(text, "the file was read successfully — there simply is no text in it");
        Assert.AreEqual(string.Empty, text.Trim());
    }

    [TestMethod]
    public async Task CorruptPdf_ReturnsNull_NotEmpty()
    {
        var text = await NewExtractor().ExtractAsync(Sample("corrupt.pdf"), Budget, default);

        // The inverse of the case above. Empty here would strike the row through as a definite non-match for
        // a file we never managed to open.
        Assert.IsNull(text);
    }

    [TestMethod]
    public async Task MissingFile_ReturnsNull()
    {
        var text = await NewExtractor().ExtractAsync(
            Path.Combine(Path.GetTempPath(), "nexa-no-such-" + Guid.NewGuid().ToString("N") + ".pdf"),
            Budget, default);

        Assert.IsNull(text);
    }

    // ── Budgets ──────────────────────────────────────────────────────────────

    [TestMethod]
    public async Task OversizedFile_IsDeclinedWithoutParsing()
    {
        // Opening a PDF reads its whole cross-reference table before any page exists, and that stretch can't
        // be interrupted — so on a sequential sweep one huge file stalls every candidate behind it.
        //
        // Padded past %%EOF, which leaves the document perfectly valid (startxref still resolves). That's the
        // point: the very same file parses fine below, so a null above can only be the size ceiling talking
        // and not some incidental damage.
        var padded = Path.Combine(Path.GetTempPath(), "nexapdfbig_" + Guid.NewGuid().ToString("N") + ".pdf");
        try
        {
            File.WriteAllBytes(padded, [.. File.ReadAllBytes(Sample("text.pdf")), .. new byte[2 * 1024 * 1024]]);

            Assert.IsNull(
                await NewExtractor(new PdfConfig { SearchMaxFileSizeMb = 1 }).ExtractAsync(padded, Budget, default),
                "declined for size is 'couldn't tell', not 'no text'");

            var allowed = await NewExtractor(new PdfConfig { SearchMaxFileSizeMb = 8 })
                .ExtractAsync(padded, Budget, default);
            Assert.IsNotNull(allowed);
            StringAssert.Contains(allowed, PdfSamples.BodyNeedle, "raise the ceiling and the same file reads");
        }
        finally
        {
            if (File.Exists(padded)) File.Delete(padded);
        }
    }

    [TestMethod]
    public async Task ZeroByteBudget_ReturnsNull()
    {
        Assert.IsNull(await NewExtractor().ExtractAsync(Sample("text.pdf"), 0, default));
    }

    [TestMethod]
    public async Task Extract_StopsAtTheByteBudget()
    {
        var full = await NewExtractor().ExtractAsync(Sample("text.pdf"), Budget, default);
        var clipped = await NewExtractor().ExtractAsync(Sample("text.pdf"), maxBytes: 8, default);

        Assert.IsNotNull(full);
        Assert.IsNotNull(clipped);
        Assert.IsTrue(clipped.Length < full.Length, "a tiny budget must truncate rather than read it all");
    }

    // ── Cancellation ─────────────────────────────────────────────────────────

    [TestMethod]
    public async Task CancelledToken_Throws_RatherThanReportingAnEmptyDocument()
    {
        // The tab that started the sweep has closed. Swallowing this and returning "" would report every
        // remaining row as a confident non-match.
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await Assert.ThrowsExactlyAsync<TaskCanceledException>(
            () => NewExtractor().ExtractAsync(Sample("text.pdf"), Budget, cts.Token));
    }
}
