using System.IO;
using Nexaflow.Features.Office.Search;
using Nexaflow.Search;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.Features.Office;

/// <summary>
/// Makes Word documents searchable by content. Two things carry the weight: word boundaries — search matches
/// whole words, and WordprocessingML both splits words across runs and puts no whitespace between paragraphs —
/// and the contract's three-way answer (text / "read it, there is none" / "couldn't read it"), because the
/// search verifier trusts any non-null result and turns a missing term into a confident rejection.
/// </summary>
[TestClass]
[CoversNode("office-word-search")]
public class OfficeTextExtractorTests
{
    private const long Budget = 4 * 1024 * 1024;

    private static OfficeTextExtractor NewExtractor() => new();

    private static string Sample(string name) => TestSampleData.Path("office", name);

    private static async Task<string> Read(string name)
    {
        var text = await NewExtractor().ExtractAsync(Sample(name), Budget, default);
        Assert.IsNotNull(text, $"{name} is a readable Word document");
        return text;
    }

    private static int Occurrences(string text, string word)
    {
        var count = 0;
        for (var i = text.IndexOf(word, StringComparison.Ordinal); i >= 0; i = text.IndexOf(word, i + word.Length, StringComparison.Ordinal))
            count++;
        return count;
    }

    // ── Claiming files ────────────────────────────────────────────────────────

    [TestMethod]
    public void CanExtract_ClaimsTheWordFormatsOnly()
    {
        var extractor = NewExtractor();

        foreach (var path in new[] { @"C:\x\a.docx", @"C:\x\A.DOCX", @"C:\x\a.docm", @"C:\x\a.dotx", @"C:\x\a.dotm" })
            Assert.IsTrue(extractor.CanExtract(path), path);

        Assert.IsFalse(extractor.CanExtract(@"C:\x\a.doc"), "binary Word 97-2003 is a different format entirely");
        Assert.IsFalse(extractor.CanExtract(@"C:\x\a.xlsx"), "not yet — a workbook has no w:t to read");
        Assert.IsFalse(extractor.CanExtract(@"C:\x\a.txt"));
        Assert.IsFalse(extractor.CanExtract(@"C:\x\docx"), "a file merely named 'docx' is not one");
        Assert.IsFalse(extractor.CanExtract(string.Empty));
    }

    [TestMethod]
    public void CanExtract_DoesNotTouchTheDisk()
    {
        // Asked for every candidate file in a sweep, so it has to answer from the name alone.
        Assert.IsTrue(NewExtractor().CanExtract(@"Z:\nonexistent\ghost.docx"));
    }

    // ── Word boundaries ──────────────────────────────────────────────────────

    [TestMethod]
    public async Task Extract_JoinsAWordSplitAcrossRuns_AndKeepsTabStopsOut()
    {
        // "Geor" (bold) + "giana" are two runs; the paragraph also defines a tab stop in its properties, which
        // is formatting, not a tab character. Either mistake breaks this exact line.
        var text = await Read("text.docx");

        StringAssert.Contains(text, $"\nThe survey of {OfficeSamples.SplitWord} began.");
    }

    [TestMethod]
    public async Task Extract_KeepsASpaceOnlyRun()
    {
        // <w:t xml:space="preserve"> </w:t> is the only thing between the two words.
        StringAssert.Contains(await Read("text.docx"), OfficeSamples.SpacedPhrase);
    }

    [TestMethod]
    public async Task Extract_KeepsTableCellsAndTabbedWordsApart()
    {
        var text = await Read("text.docx");

        Assert.IsFalse(text.Contains(OfficeSamples.LeftCell + OfficeSamples.RightCell, StringComparison.Ordinal),
            "adjacent cells carry no whitespace in the markup; fused, neither word is findable");
        StringAssert.Contains(text, OfficeSamples.LeftCell);
        StringAssert.Contains(text, OfficeSamples.RightCell);
        StringAssert.Contains(text, $"{OfficeSamples.TabbedLeft}\t{OfficeSamples.TabbedRight}");
    }

    // ── Everything the document says ─────────────────────────────────────────

    [TestMethod]
    public async Task Extract_IncludesEveryPartThatHoldsTheDocumentsWords()
    {
        var text = await Read("text.docx");

        foreach (var word in new[]
                 {
                     OfficeSamples.TitleNeedle, OfficeSamples.HeaderNeedle, OfficeSamples.FooterNeedle,
                     OfficeSamples.FootnoteNeedle, OfficeSamples.EndnoteNeedle, OfficeSamples.CommentNeedle,
                     OfficeSamples.MathNeedle, OfficeSamples.FieldResult,
                 })
            StringAssert.Contains(text, word);
    }

    [TestMethod]
    public async Task Extract_ReadsADoubledTextBoxAndAMovedWordOnce()
    {
        var text = await Read("text.docx");

        Assert.AreEqual(1, Occurrences(text, OfficeSamples.TextBoxNeedle),
            "a text box is written as mc:Choice and again as a VML mc:Fallback — only one is the document");
        Assert.AreEqual(1, Occurrences(text, OfficeSamples.MovedWord),
            "a tracked move keeps the old copy under w:moveFrom; the document says it once");
    }

    // ── What the document does not say ────────────────────────────────────────

    [TestMethod]
    public async Task Extract_OmitsDeletedTextFieldCodesAndBookkeeping()
    {
        var text = await Read("text.docx");

        Assert.IsFalse(text.Contains(OfficeSamples.DeletedWord, StringComparison.Ordinal), "tracked deletion");
        Assert.IsFalse(text.Contains(OfficeSamples.FieldCodeWord, StringComparison.Ordinal),
            "a field code is the HYPERLINK instruction, not the words shown");
        Assert.IsFalse(text.Contains(OfficeSamples.LastModifiedBy, StringComparison.Ordinal),
            "last-modified-by is not what anyone means by the document");
    }

    // ── Package variants ──────────────────────────────────────────────────────

    [TestMethod]
    public async Task Extract_ReadsStrictDocuments()
    {
        var text = await Read("strict.docx");

        StringAssert.Contains(text, OfficeSamples.StrictBodyNeedle);
        StringAssert.Contains(text, OfficeSamples.StrictHeaderNeedle, "Strict relationship types too");
    }

    [TestMethod]
    public async Task Extract_FollowsRelationshipsRatherThanConventionalPaths()
    {
        // An absolute, percent-encoded main-part target and a header reached by "../" — nothing at word/.
        var text = await Read("relocated.docx");

        StringAssert.Contains(text, OfficeSamples.RelocatedBodyNeedle);
        StringAssert.Contains(text, OfficeSamples.RelocatedHeaderNeedle);
    }

    // ── The three-way answer ─────────────────────────────────────────────────

    [TestMethod]
    public async Task EmptyDocument_ReturnsEmpty_NotNull()
    {
        // Read fine, genuinely no words: the verifier may then reject a term with confidence.
        Assert.AreEqual(string.Empty, await Read("empty.docx"));
    }

    [TestMethod]
    [DataRow("corrupt.docx")]
    [DataRow("encrypted.docx")]
    [DataRow("malformed.docx")]
    [DataRow("spreadsheet.docx")]
    public async Task UnreadableFile_ReturnsNull_NotEmpty(string name)
    {
        // Not a zip, a password-protected (compound) file, XML that breaks off after some text was already
        // read, and a package whose main part isn't a Word document. Anything but null would strike these
        // through as definite misses for files never actually understood.
        Assert.IsNull(await NewExtractor().ExtractAsync(Sample(name), Budget, default));
    }

    [TestMethod]
    public async Task MissingFile_ReturnsNull()
    {
        var path = Path.Combine(Path.GetTempPath(), "nexa-no-such-" + Guid.NewGuid().ToString("N") + ".docx");

        Assert.IsNull(await NewExtractor().ExtractAsync(path, Budget, default));
    }

    // ── Budgets ──────────────────────────────────────────────────────────────

    [TestMethod]
    public async Task ZeroByteBudget_ReturnsNull()
    {
        Assert.IsNull(await NewExtractor().ExtractAsync(Sample("text.docx"), 0, default));
    }

    [TestMethod]
    public async Task Extract_StopsAtTheByteBudget_AndReturnsWhatItRead()
    {
        var clipped = await NewExtractor().ExtractAsync(Sample("text.docx"), maxBytes: 16, default);

        Assert.IsNotNull(clipped, "a partial read is still a read — the contract says return it");
        Assert.IsTrue(clipped.Length <= 8, $"16 bytes buys 8 UTF-16 chars; got {clipped.Length}");
    }

    [TestMethod]
    public async Task AnOversizedRun_IsReadOnlyAsFarAsTheBudget()
    {
        // One text node of 300,000 characters: streamed in chunks, never held whole.
        var text = await NewExtractor().ExtractAsync(Sample("long-run.docx"), maxBytes: 2000, default);

        Assert.IsNotNull(text);
        Assert.IsTrue(text.Length <= 1000, $"got {text.Length} chars");
        Assert.IsTrue(text.StartsWith(OfficeSamples.LongRunWord, StringComparison.Ordinal));
    }

    // ── Cancellation ─────────────────────────────────────────────────────────

    [TestMethod]
    public async Task CancelledToken_Throws_RatherThanReportingAnEmptyDocument()
    {
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await Assert.ThrowsExactlyAsync<TaskCanceledException>(
            () => NewExtractor().ExtractAsync(Sample("text.docx"), Budget, cts.Token));
    }

    // ── What the verifier does with it ────────────────────────────────────────

    [TestMethod]
    public async Task Output_SatisfiesTheSearchMatcher()
    {
        // SearchRequest.MatchesFile is exactly what SearchVerifier and the folder scan apply to this text.
        var text = await Read("text.docx");

        Assert.IsTrue(new SearchRequest(OfficeSamples.SplitWord).MatchesFile("text.docx", text));
        Assert.IsFalse(new SearchRequest("Geor").MatchesFile("text.docx", text),
            "whole-word: the first half of a split word is not a word the document contains");
        Assert.IsTrue(new SearchRequest(OfficeSamples.RightCell).MatchesFile("text.docx", text));
        Assert.IsTrue(new SearchRequest(@"amber\s+lantern", IsRegex: true).MatchesFile("text.docx", text));
    }
}
