using System.IO;
using NSubstitute;
using Nexaflow.Features.Code.ViewModels;
using Nexaflow.Features.Common;
using Nexaflow.Features.Common.Search;
using Nexaflow.Search;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.Features.Search;

/// <summary>
/// The "As Code" editor as a searchable page. The implementation lives on the shared
/// <c>FileTextEditorViewModel</c>, so these run the contract against the subclass that ships it — the plain
/// editor answers identically by construction.
/// </summary>
[TestClass]
[CoversNode("code-find")]
public sealed class CodeSearchableTests : SearchableContentConformanceTests
{
    // "alpha42" appears only in lower case; "alpha\d+" matches it but never appears verbatim.
    private const string Source = """
        namespace Demo;

        public class Calculator
        {
            // seed alpha42 for the search
            public int Add(int a, int b) => a + b;
            public int AlsoAlpha() => Lookup("alpha42");
        }
        """;

    private string? _path;

    protected override string LiteralTermInContent => "alpha42";
    protected override string RegexOnlyPattern     => @"alpha\d+";

    protected override async Task<ISearchable> CreateAsync()
    {
        _path = Path.Combine(Path.GetTempPath(), "nexasearch_" + Guid.NewGuid().ToString("N") + ".cs");
        File.WriteAllText(_path, Source);

        var vm = new CodeViewModel(_path, Substitute.For<IShellServices>(), maxEditableBytes: long.MaxValue);
        await vm.LoadAsync();
        return vm;
    }

    [TestCleanup]
    public void Cleanup()
    {
        if (_path is not null && File.Exists(_path)) File.Delete(_path);
    }

    protected override string Snapshot(ISearchable page)
    {
        var vm = (CodeViewModel)page;
        return $"{vm.SearchMatchCount}|{vm.IsSearchActive}|{vm.CurrentSearchTerm}|{vm.SearchHighlights.Count}";
    }

    // ── Code-specific behaviour beyond the shared contract ────────────────────

    [TestMethod]
    public void SearchTarget_NamesTheFileAsCode() => WithPage(page =>
    {
        // The agent's tool catalogue should say "code file", not the base editor's generic phrasing.
        StringAssert.Contains(page.SearchTargetDescription, "code file");
        StringAssert.Contains(page.SearchTargetDescription, Path.GetFileName(_path!));
        return Task.CompletedTask;
    });

    [TestMethod]
    public void DisplayingSearch_PaintsHighlightsAndCountsMatches() => WithPage(async page =>
    {
        var vm = (CodeViewModel)page;

        await vm.SearchAsync(new SearchRequest(LiteralTermInContent), display: true, default);

        Assert.AreEqual(2, vm.SearchMatchCount, "two lines mention alpha42");
        Assert.IsTrue(vm.IsSearchActive);
        Assert.AreEqual(2, vm.SearchHighlights.Count, "each occurrence is painted, not just counted");
        Assert.AreEqual(LiteralTermInContent, vm.CurrentSearchTerm);
    });

    [TestMethod]
    public void HighlightOffsets_LandOnTheMatchedText() => WithPage(async page =>
    {
        var vm = (CodeViewModel)page;

        await vm.SearchAsync(new SearchRequest(LiteralTermInContent), display: true, default);

        // An offset that isn't the match is the failure this catches: the highlight would sit beside the
        // word, which looks like a rendering bug rather than a search one.
        foreach (var (offset, length) in vm.SearchHighlights)
            Assert.AreEqual(LiteralTermInContent, vm.Document.GetText(offset, length));
    });

    [TestMethod]
    public void Hits_CarryLineNumbersAndPreviews() => WithPage(async page =>
    {
        var outcome = await page.SearchAsync(new SearchRequest(LiteralTermInContent), display: false, default);

        Assert.AreEqual(2, outcome.MatchCount);
        // Ids are 0-based line numbers; ShowResultsAsync parses them back.
        Assert.IsTrue(outcome.Hits.All(h => int.TryParse(h.Id, out _)));
        Assert.IsTrue(outcome.Hits.All(h => !string.IsNullOrWhiteSpace(h.Preview)),
            "previews let the agent judge relevance without a second call");
    });

    [TestMethod]
    public void ShowResults_NarrowsTheMatchSetAndItsHighlights() => WithPage(async page =>
    {
        var vm      = (CodeViewModel)page;
        var outcome = await vm.SearchAsync(new SearchRequest(LiteralTermInContent), display: true, default);
        Assert.AreEqual(2, vm.SearchMatchCount);

        var rendered = await vm.ShowResultsAsync([outcome.Hits[1]], default);

        Assert.IsTrue(rendered);
        Assert.AreEqual(1, vm.SearchMatchCount, "only the agent's chosen line stays in the match set");
        Assert.AreEqual(1, vm.SearchHighlights.Count,
            "the highlights must follow the narrowed set, not keep painting the discarded match");
    });

    [TestMethod]
    public void FilenameFilters_AreRefused_NotIgnored() => WithPage(async page =>
    {
        // A viewer of one file has no filename to judge. Dropping the term would answer a different,
        // broader question and look like a working search.
        var request = new SearchRequest("alpha42")
        {
            Terms =
            [
                new SearchTerm(SearchTermKind.Regex, [@"^.*\.md$"], NameOnly: true, Display: "*.md"),
                new SearchTerm(SearchTermKind.Text,  ["alpha42"]),
            ],
        };

        var outcome = await page.SearchAsync(request, display: false, default);

        Assert.IsTrue(outcome.Failed);
        Assert.IsFalse(string.IsNullOrWhiteSpace(outcome.Message));
    });
}
