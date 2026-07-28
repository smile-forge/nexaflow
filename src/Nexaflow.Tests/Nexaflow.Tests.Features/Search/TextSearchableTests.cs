using System.IO;
using NSubstitute;
using Nexaflow.Features.Common;
using Nexaflow.Features.Common.Search;
using Nexaflow.Search;
using Nexaflow.Features.Text.ViewModels;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.Features.Search;

/// <summary>The text viewer as a searchable page — it replaced the two hand-written Text query handlers.</summary>
[TestClass]
[CoversNode("text-viewer-find")]
public sealed class TextSearchableTests : SearchableContentConformanceTests
{
    // "alpha42" appears only in lower case; "alpha\d+" matches it but never appears verbatim.
    private const string Content =
        "first line with alpha42 in it\n" +
        "second line, nothing here\n" +
        "third line mentions beta and alpha42 again\n" +
        "fourth line is quiet\n";

    private string? _path;

    protected override string LiteralTermInContent => "alpha42";
    protected override string RegexOnlyPattern     => @"alpha\d+";

    protected override async Task<ISearchable> CreateAsync()
    {
        _path = Path.Combine(Path.GetTempPath(), "nexasearch_" + Guid.NewGuid().ToString("N") + ".txt");
        File.WriteAllText(_path, Content);

        var vm = new TextViewModel(_path, Substitute.For<IShellServices>()) { IsMonitoring = false };
        await vm.LoadAsync(CancellationToken.None);
        return vm;
    }

    [TestCleanup]
    public void Cleanup()
    {
        if (_path is not null && File.Exists(_path)) File.Delete(_path);
    }

    protected override string Snapshot(ISearchable page)
    {
        var vm = (TextViewModel)page;
        return $"{vm.SearchMatchCount}|{vm.IsSearchActive}|{vm.CurrentSearchTerm}|{vm.FindText}|{vm.MatchCase}";
    }

    // ── Text-specific behaviour beyond the shared contract ────────────────────

    [TestMethod]
    public void DisplayingSearch_DrivesTheFindBar() => WithPage(async page =>
    {
        var vm = (TextViewModel)page;

        await vm.SearchAsync(new SearchRequest(LiteralTermInContent), display: true, default);

        // An AI-bar search should light up exactly what typing in the find bar would.
        Assert.AreEqual(2, vm.SearchMatchCount);
        Assert.IsTrue(vm.IsSearchActive);
        Assert.IsTrue(vm.IsFindBarOpen, "the find bar surfaces the executed search");
        Assert.AreEqual(LiteralTermInContent, vm.FindText);
        Assert.IsFalse(vm.UseRegex);
    });

    [TestMethod]
    public void DisplayingRegexSearch_SetsTheRegexToggle() => WithPage(async page =>
    {
        var vm = (TextViewModel)page;

        await vm.SearchAsync(new SearchRequest(RegexOnlyPattern, IsRegex: true), display: true, default);

        Assert.IsTrue(vm.UseRegex, "the bar's regex toggle must reflect how the search actually ran");
        Assert.AreEqual(2, vm.SearchMatchCount);
    });

    [TestMethod]
    public void Hits_CarryLineNumbersAndPreviews() => WithPage(async page =>
    {
        var outcome = await page.SearchAsync(new SearchRequest(LiteralTermInContent), display: false, default);

        Assert.AreEqual(2, outcome.MatchCount);
        // Ids are 0-based line numbers; ShowResultsAsync parses them back.
        CollectionAssert.AreEqual(new[] { "0", "2" }, outcome.Hits.Select(h => h.Id).ToArray());
        Assert.IsTrue(outcome.Hits.All(h => !string.IsNullOrWhiteSpace(h.Preview)),
            "previews let the agent judge relevance without a second call");
    });

    [TestMethod]
    public void ShowResults_NarrowsTheMatchSetToTheChosenLines() => WithPage(async page =>
    {
        var vm      = (TextViewModel)page;
        var outcome = await vm.SearchAsync(new SearchRequest(LiteralTermInContent), display: true, default);
        Assert.AreEqual(2, vm.SearchMatchCount);

        var rendered = await vm.ShowResultsAsync([outcome.Hits[1]], default);

        Assert.IsTrue(rendered);
        Assert.AreEqual(1, vm.SearchMatchCount, "only the agent's chosen line stays in the match set");
    });
}
