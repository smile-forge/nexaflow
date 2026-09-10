using System.IO;
using Nexaflow.Features.Common.Search;
using Nexaflow.Features.WindowsSearch;
using Nexaflow.Features.WindowsSearch.Services;
using Nexaflow.IO.Common;
using Nexaflow.Search;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.Features.WindowsSearch;

/// <summary>
/// Exercises the live-filesystem walk directly — no Windows Search index required, so
/// these run the same on a dev box and in headless CI. A temp tree is never indexed,
/// which is exactly the "search under a non-indexed folder" case that was returning
/// nothing (globs went to the index only).
/// </summary>
[TestClass]
[CoversNode("search-index-query")]
public class WindowsSearchServiceTests
{
    private string _root = string.Empty;

    [TestInitialize]
    public void Setup()
    {
        _root = Path.Combine(Path.GetTempPath(), "nexa-search-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(_root, "sub"));
        File.WriteAllText(Path.Combine(_root, "alpha.json"), "{}");
        File.WriteAllText(Path.Combine(_root, "notes.txt"),  "hi");
        File.WriteAllText(Path.Combine(_root, "sub", "beta.json"),  "{}");
        File.WriteAllText(Path.Combine(_root, "sub", "readme.md"),  "readme");
    }

    [TestCleanup]
    public void Teardown()
    {
        try { Directory.Delete(_root, recursive: true); } catch { /* best effort */ }
    }

    // A folder scan is no longer entered automatically — it reads every file in the tree, so the user is
    // asked first. These exercise it directly, the way the banner's "scan" button does.

    private static async Task<List<SearchResultEntry>> Scan(string query, string root, FileContentReader? reader = null)
    {
        var hits = new List<SearchResultEntry>();
        await WindowsSearchService.WalkAsync(
            SearchSyntax.ParseRequest(query, [new GlobTermRecognizer()]),
            root, 500, h => { lock (hits) hits.Add(h); }, reader ?? new FileContentReader(), CancellationToken.None);
        return hits;
    }

    [TestMethod]
    public async Task Scan_FindsMatchesRecursively()
    {
        var names = (await Scan("*.json", _root)).Select(h => h.FileName).OrderBy(n => n).ToArray();

        CollectionAssert.AreEqual(new[] { "alpha.json", "beta.json" }, names);
    }

    [TestMethod]
    public async Task Scan_ExcludesNonMatchingFiles()
    {
        var hits = await Scan("*.json", _root);

        Assert.IsFalse(hits.Any(h => h.FileName == "notes.txt"));
    }

    [TestMethod]
    public async Task Scan_ReportsDirectoryRelativeToRoot()
    {
        var beta = (await Scan("beta.*", _root)).Single();

        Assert.AreEqual("beta.json", beta.FileName);
        Assert.AreEqual("sub", beta.Directory);
    }

    [TestMethod]
    public async Task Scan_ReadsFileContents()
    {
        // The reason the scan exists: a term no filename can answer. The old walk saw names only and
        // quietly reported nothing here.
        await File.WriteAllTextAsync(Path.Combine(_root, "poem.txt"), "the bookcase in the corner");

        var hits = await Scan("bookcase", _root);

        Assert.IsTrue(hits.Any(h => h.FileName == "poem.txt"),
            "a scan must match on what is inside the file, not just its name");
    }

    [TestMethod]
    public async Task Scan_StreamsEachHitAsItIsFound()
    {
        // Streaming is the difference between a progress bar and a frozen tab, so it is a behaviour worth
        // asserting rather than an implementation detail.
        var seen = 0;
        await WindowsSearchService.WalkAsync(
            SearchSyntax.ParseRequest("*.json", [new GlobTermRecognizer()]),
            _root, 500, _ => Interlocked.Increment(ref seen), new FileContentReader(), CancellationToken.None);

        Assert.AreEqual(2, seen, "each match should have been reported through the callback");
    }

    [TestMethod]
    public async Task Scan_EmptyWhenNothingMatches()
    {
        Assert.AreEqual(0, (await Scan("*.zip", _root)).Count);
    }

    [TestMethod]
    public async Task Index_DoesNotSilentlyWalk()
    {
        // The index is asked, and only the index. A scan that started itself off a keystroke is what the
        // banner replaced.
        var hits = await WindowsSearchService.SearchAsync(
            SearchQueryParser.Parse("readme"), _root, CancellationToken.None);

        Assert.AreEqual(0, hits.Count, "an unindexed temp folder yields nothing until the user scans");
    }

    // ── Format-aware reading ─────────────────────────────────────────────────
    //
    // A PDF or a Word document keeps its words in compressed streams. The scan used to read every file as
    // plain text, so those words were findable through the index but never by walking a folder it doesn't
    // cover. It now reads through the same FileContentReader as the index's sweep; fakes stand in for the
    // real extractors so this suite needs no feature that owns a format.

    [TestMethod]
    public async Task Scan_ReadsThroughTheExtractorThatClaimsTheFile()
    {
        await File.WriteAllTextAsync(Path.Combine(_root, "report.fake"), "deflated gibberish");

        var hits = await Scan("heron", _root, Reader(new FixedExtractor("a survey of the heron")));

        CollectionAssert.AreEqual(new[] { "report.fake" }, hits.Select(h => h.FileName).ToArray(),
            "only the extractor's text holds the term — the raw bytes never did");
    }

    [TestMethod]
    public async Task Scan_TrustsAnExtractorsEmptyAnswer()
    {
        // "Read it, there is no text" is a real miss — the raw bytes are not scanned behind its back.
        await File.WriteAllTextAsync(Path.Combine(_root, "scan.fake"), "the bookcase in the corner");

        Assert.AreEqual(0, (await Scan("bookcase", _root, Reader(new FixedExtractor(string.Empty)))).Count);
    }

    [TestMethod]
    public async Task Scan_ClaimantReturningNull_StillGetsThePlainTextRead()
    {
        // Null is "couldn't tell", so the plain-text read gets its turn.
        await File.WriteAllTextAsync(Path.Combine(_root, "notes.fake"), "the bookcase in the corner");

        var hits = await Scan("bookcase", _root, Reader(new FixedExtractor(null)));

        Assert.IsTrue(hits.Any(h => h.FileName == "notes.fake"));
    }

    [TestMethod]
    public async Task Scan_ABrokenExtractor_DoesNotStopTheWalk()
    {
        await File.WriteAllTextAsync(Path.Combine(_root, "a.fake"), "the bookcase");
        await File.WriteAllTextAsync(Path.Combine(_root, "poem.txt"), "the bookcase in the corner");

        var hits = await Scan("bookcase", _root, Reader(new ExplodingExtractor()));

        CollectionAssert.AreEquivalent(new[] { "a.fake", "poem.txt" }, hits.Select(h => h.FileName).ToArray(),
            "the throwing claimant's file falls back to plain text, and the walk carries on past it");
    }

    /// <summary>Mirrors <c>IShellServices.GetFileTextExtractor</c> for one format: the extractor claims
    /// <c>.fake</c> files and nothing else.</summary>
    private static FileContentReader Reader(IFileTextExtractor forFakeFiles) =>
        new(path => path.EndsWith(".fake", StringComparison.OrdinalIgnoreCase) ? forFakeFiles : null);

    private sealed class FixedExtractor(string? text) : IFileTextExtractor
    {
        public bool CanExtract(string path) => true;
        public Task<string?> ExtractAsync(string path, long maxBytes, CancellationToken ct) => Task.FromResult(text);
    }

    private sealed class ExplodingExtractor : IFileTextExtractor
    {
        public bool CanExtract(string path) => true;
        public Task<string?> ExtractAsync(string path, long maxBytes, CancellationToken ct)
            => throw new InvalidOperationException("boom");
    }
}
