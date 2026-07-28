using System.IO;
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

    private static async Task<List<SearchResultEntry>> Scan(string query, string root)
    {
        var hits = new List<SearchResultEntry>();
        await WindowsSearchService.WalkAsync(
            SearchSyntax.ParseRequest(query, [new GlobTermRecognizer()]),
            root, 500, h => { lock (hits) hits.Add(h); }, CancellationToken.None);
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
            _root, 500, _ => Interlocked.Increment(ref seen), CancellationToken.None);

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
}
