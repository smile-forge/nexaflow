using System.IO;
using Nexaflow.Features.WindowsSearch;
using Nexaflow.Features.WindowsSearch.Services;
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

    [TestMethod]
    public async Task Glob_FindsMatchesRecursively_UnderNonIndexedRoot()
    {
        var hits = await WindowsSearchService.SearchAsync(
            SearchQueryParser.Parse("*.json"), _root, CancellationToken.None);

        var names = hits.Select(h => h.FileName).OrderBy(n => n).ToArray();
        CollectionAssert.AreEqual(new[] { "alpha.json", "beta.json" }, names);
    }

    [TestMethod]
    public async Task Glob_ExcludesNonMatchingFiles()
    {
        var hits = await WindowsSearchService.SearchAsync(
            SearchQueryParser.Parse("*.json"), _root, CancellationToken.None);

        Assert.IsFalse(hits.Any(h => h.FileName == "notes.txt"));
    }

    [TestMethod]
    public async Task Glob_ReportsDirectoryRelativeToRoot()
    {
        var hits = await WindowsSearchService.SearchAsync(
            SearchQueryParser.Parse("beta.*"), _root, CancellationToken.None);

        var beta = hits.Single();
        Assert.AreEqual("beta.json", beta.FileName);
        Assert.AreEqual("sub", beta.Directory);
    }

    [TestMethod]
    public async Task PlainTerm_FallsBackToWalk_WhenRootNotIndexed()
    {
        // Not a glob → hits the index first; the temp dir isn't indexed (or the Search
        // service may be absent), so the off-index fallback walk must still find it.
        var hits = await WindowsSearchService.SearchAsync(
            SearchQueryParser.Parse("readme"), _root, CancellationToken.None);

        Assert.IsTrue(hits.Any(h => h.FileName == "readme.md"),
            "off-index fallback should surface readme.md");
    }

    [TestMethod]
    public async Task Glob_EmptyWhenNothingMatches()
    {
        var hits = await WindowsSearchService.SearchAsync(
            SearchQueryParser.Parse("*.zip"), _root, CancellationToken.None);

        Assert.AreEqual(0, hits.Count);
    }
}
