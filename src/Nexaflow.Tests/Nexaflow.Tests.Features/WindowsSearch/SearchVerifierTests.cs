using System.IO;
using System.Text;
using Nexaflow.Features.Common.Search;
using Nexaflow.Search;
using Nexaflow.Features.WindowsSearch.Services;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.Features.WindowsSearch;

/// <summary>
/// The second stage of a content regex search. The index can narrow by name but can't evaluate a pattern
/// against file contents, so it returns "might match" rows and this settles them.
/// </summary>
[TestClass]
[CoversNode("search-verify")]
public class SearchVerifierTests
{
    private string _dir = string.Empty;

    [TestInitialize]
    public void Setup()
    {
        _dir = Path.Combine(Path.GetTempPath(), "nexaverify_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
    }

    [TestCleanup]
    public void Cleanup()
    {
        if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true);
    }

    private string Write(string name, string content, Encoding? encoding = null)
    {
        var path = Path.Combine(_dir, name);
        File.WriteAllText(path, content, encoding ?? new UTF8Encoding(false));
        return path;
    }

    private static SearchHit Hit(string path) =>
        new(path, Path.GetFileName(path)) { Source = path, State = SearchHitState.Candidate };

    // ── Stage one: names settle for free ──────────────────────────────────────

    [TestMethod]
    public void NameMatch_IsProvenWithoutReadingTheFile()
    {
        var hit = new SearchHit(@"C:\x\report2024.pdf", "report2024.pdf");

        // The whole point of classifying first: a name match needs no file read, so the verify sweep only
        // ever touches rows that might match on content.
        Assert.AreEqual(SearchHitState.Verified,
            SearchVerifier.ClassifyByName(hit, new SearchRequest(@"report\d+", IsRegex: true)));
    }

    [TestMethod]
    public void NameThatDoesNotMatch_IsACandidateNotARejection()
    {
        var hit = new SearchHit(@"C:\x\notes.md", "notes.md");

        // It may still match on content — calling it rejected here is what made content patterns look
        // like empty folders.
        Assert.AreEqual(SearchHitState.Candidate,
            SearchVerifier.ClassifyByName(hit, new SearchRequest(@"TODO:\s*fix", IsRegex: true)));
    }

    // ── Stage two: contents decide ────────────────────────────────────────────

    [TestMethod]
    public async Task ContentMatch_VerifiesTheCandidate()
    {
        var path = Write("notes.md", "some text\nTODO:  fix the parser\nmore text\n");

        var state = await new SearchVerifier().VerifyAsync(
            Hit(path), new SearchRequest(@"TODO:\s*fix", IsRegex: true), default);

        Assert.AreEqual(SearchHitState.Verified, state, "the pattern is in the file's contents");
    }

    [TestMethod]
    public async Task ContentMiss_RejectsTheCandidate()
    {
        var path = Write("notes.md", "nothing of interest here\n");

        var state = await new SearchVerifier().VerifyAsync(
            Hit(path), new SearchRequest(@"TODO:\s*fix", IsRegex: true), default);

        Assert.AreEqual(SearchHitState.Rejected, state);
    }

    [TestMethod]
    public async Task UnreadableFile_IsMarkedUnreadable_NotRejectedAndNotStillPending()
    {
        // Three-way distinction that matters: "doesn.t match" would hide it, and "still a candidate"
        // would keep offering a re-check that can never resolve it.
        var state = await new SearchVerifier().VerifyAsync(
            Hit(Path.Combine(_dir, "does-not-exist.txt")),
            new SearchRequest("anything", IsRegex: true), default);

        Assert.AreEqual(SearchHitState.Unreadable, state);
    }

    [TestMethod]
    public async Task BinaryFile_IsStillScanned_AndAHitIsFlaggedUncertain()
    {
        var path = Path.Combine(_dir, "image.bin");
        File.WriteAllBytes(path, [0x89, .."PNG"u8.ToArray(), 0x00, 0x01, 0x02, 0x00, 0x03]);

        // Not skipping binaries is the point: a plain-text scan of unknown bytes finds real things, the way
        // Notepad++ does. What it can't promise is that the hit is meaningful text rather than a header
        // string — hence Uncertain rather than Verified.
        var state = await new SearchVerifier().VerifyAsync(
            Hit(path), new SearchRequest("PNG", IsRegex: true), default);

        Assert.AreEqual(SearchHitState.Uncertain, state);
    }

    [TestMethod]
    public async Task BinaryFile_WithNoHit_IsInconclusive_NotRejected()
    {
        // A .docx is a ZIP: its real words are compressed, so their absence from the raw bytes proves
        // nothing. Calling this a miss would confidently hide a file that does contain the text.
        var path = Path.Combine(_dir, "report.docx");
        File.WriteAllBytes(path, [0x50, 0x4B, 0x03, 0x04, 0x00, 0x00, 0x08, 0x00]);

        var state = await new SearchVerifier().VerifyAsync(
            Hit(path), new SearchRequest("magic", IsRegex: true), default);

        Assert.AreEqual(SearchHitState.Unreadable, state);
        Assert.AreNotEqual(SearchHitState.Rejected, state,
            "absence from compressed bytes is not evidence of absence from the document");
        Assert.AreNotEqual(SearchHitState.Candidate, state,
            "a candidate is something a re-check could settle; this isn't");
    }

    [TestMethod]
    public async Task PlainTextMiss_IsConclusive_UnlikeABinaryMiss()
    {
        // The distinction the fidelity flag buys: the same "not found" means different things.
        var text   = Write("readme.txt", "nothing of interest");
        var binary = Path.Combine(_dir, "blob.bin");
        File.WriteAllBytes(binary, [0x00, 0x01, 0x02, 0x00, 0x03, 0x04]);

        var request = new SearchRequest("magic", IsRegex: true);

        Assert.AreEqual(SearchHitState.Rejected,
            await new SearchVerifier().VerifyAsync(Hit(text), request, default));
        Assert.AreEqual(SearchHitState.Unreadable,
            await new SearchVerifier().VerifyAsync(Hit(binary), request, default));
    }

    [TestMethod]
    public async Task Utf16File_IsDecodedBeforeMatching()
    {
        var path = Write("wide.txt", "hello TODO: fix me\n", Encoding.Unicode);

        // Encoding detection matters: raw bytes of UTF-16 text contain NULs and would look binary, so the
        // BOM path has to win.
        var state = await new SearchVerifier().VerifyAsync(
            Hit(path), new SearchRequest(@"TODO:\s*fix", IsRegex: true), default);

        Assert.AreEqual(SearchHitState.Verified, state);
    }

    // ── The sweep ─────────────────────────────────────────────────────────────

    [TestMethod]
    public async Task VerifyAll_ReportsEachRowAsItLands()
    {
        var hits = new[]
        {
            Hit(Write("a.txt", "TODO: fix a")),
            Hit(Write("b.txt", "nothing here")),
            Hit(Write("c.txt", "TODO: fix c")),
        };

        var settled = new List<(string Name, SearchHitState State)>();
        await new SearchVerifier().VerifyAllAsync(
            hits, new SearchRequest(@"TODO:\s*fix", IsRegex: true),
            (h, s) => { settled.Add((h.Label, s)); return Task.CompletedTask; },
            default);

        // Reported one at a time so the UI settles row by row instead of freezing on the whole pass.
        Assert.AreEqual(3, settled.Count);
        Assert.AreEqual(SearchHitState.Verified, settled[0].State);
        Assert.AreEqual(SearchHitState.Rejected, settled[1].State);
        Assert.AreEqual(SearchHitState.Verified, settled[2].State);
    }

    [TestMethod]
    public async Task VerifyAll_StopsOnCancellation()
    {
        var hits = Enumerable.Range(0, 20).Select(i => Hit(Write($"f{i}.txt", "TODO: fix"))).ToList();
        using var cts = new CancellationTokenSource();
        var settled = 0;

        await Assert.ThrowsExactlyAsync<OperationCanceledException>(() =>
            new SearchVerifier().VerifyAllAsync(
                hits, new SearchRequest(@"TODO:\s*fix", IsRegex: true),
                (_, _) => { if (++settled == 3) cts.Cancel(); return Task.CompletedTask; },
                cts.Token));

        Assert.IsTrue(settled < hits.Count, "the sweep must abandon the rest, not run to completion");
    }

    // ── Format-aware extractors ───────────────────────────────────────────────

    private sealed class UpperCaseExtractor : IFileTextExtractor
    {
        public bool CanExtract(string path) => path.EndsWith(".weird", StringComparison.OrdinalIgnoreCase);
        public Task<string?> ExtractAsync(string path, long maxBytes, CancellationToken ct)
            => Task.FromResult<string?>("EXTRACTED CONTENT: needle");
    }

    private sealed class ExplodingExtractor : IFileTextExtractor
    {
        public bool CanExtract(string path) => true;
        public Task<string?> ExtractAsync(string path, long maxBytes, CancellationToken ct)
            => throw new InvalidOperationException("boom");
    }

    [TestMethod]
    public async Task FormatAwareExtractor_IsPreferredOverReadingAsText()
    {
        var path = Write("doc.weird", "the raw bytes say nothing useful");

        var state = await new SearchVerifier([new UpperCaseExtractor()]).VerifyAsync(
            Hit(path), new SearchRequest("needle", IsRegex: true), default);

        Assert.AreEqual(SearchHitState.Verified, state, "the extractor's text should be what gets matched");
    }

    [TestMethod]
    public async Task BrokenExtractor_FallsBackToPlainText()
    {
        var path = Write("notes.txt", "TODO: fix the parser");

        // One misbehaving feature must not sink the whole sweep.
        var state = await new SearchVerifier([new ExplodingExtractor()]).VerifyAsync(
            Hit(path), new SearchRequest(@"TODO:\s*fix", IsRegex: true), default);

        Assert.AreEqual(SearchHitState.Verified, state);
    }
}
