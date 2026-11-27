using Nexaflow.IO.Common;
using System.IO;
using NSubstitute;
using Nexaflow.Features.Code.ViewModels;
using Nexaflow.Features.Common;
using Nexaflow.Features.Common.Search;
using Nexaflow.Markdown.Editing;
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
        _path = Path.Combine(TestTemp.Root, "nexasearch_" + Guid.NewGuid().ToString("N") + ".cs");
        File.WriteAllText(_path, Source);

        var vm = new CodeViewModel(_path, Substitute.For<IShellServices>(), VirtualFileSystem.Instance, maxEditableBytes: long.MaxValue);
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
        return $"{vm.SearchMatchCount}|{vm.IsSearchActive}|{vm.CurrentSearchTerm}|{vm.Editor.Found.Places.Count}";
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
        Assert.AreEqual(2, vm.Editor.Found.Places.Count, "each occurrence is marked, not just counted");
        Assert.AreEqual(0, vm.Editor.Found.At, "the first is the one looked at");
        Assert.AreEqual(LiteralTermInContent, vm.CurrentSearchTerm);
    });

    [TestMethod]
    public void HighlightOffsets_LandOnTheMatchedText() => WithPage(async page =>
    {
        var vm = (CodeViewModel)page;

        await vm.SearchAsync(new SearchRequest(LiteralTermInContent), display: true, default);

        // An offset that isn't the match is the failure this catches: the highlight would sit beside the
        // word, which looks like a rendering bug rather than a search one.
        foreach (var (offset, length) in vm.Editor.Found.Places)
            Assert.AreEqual(LiteralTermInContent, vm.Document.Current.Slice(offset, length));
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
        Assert.AreEqual(1, vm.Editor.Found.Places.Count,
            "the highlights must follow the narrowed set, not keep painting the discarded match");
    });

    [TestMethod]
    public void SteppingThroughMatches_MovesTheCaretAndWhichIsLookedAt() => WithPage(async page =>
    {
        var vm = (CodeViewModel)page;
        await vm.SearchAsync(new SearchRequest(LiteralTermInContent), display: true, default);
        var lines = vm.Editor.Found.Places.Select(place => vm.Document.Current.LineOf(place.Start)).ToArray();

        Assert.AreEqual(lines[0], vm.Document.Current.LineOf(vm.Editor.Caret), "the caret goes to the first match's line");

        vm.FindNextMatchCommand.Execute(null);

        Assert.AreEqual(1, vm.Editor.Found.At);
        Assert.AreEqual(lines[1], vm.Document.Current.LineOf(vm.Editor.Caret));

        vm.ClearSearchCommand.Execute(null);
        Assert.IsTrue(vm.Editor.Found.IsEmpty, "clearing a search takes its marks away");
    });

    [TestMethod]
    public void WritingAboveAMatch_KeepsItsMarkOnIt_AndTheSearchRunsAgain() => WithPage(async page =>
    {
        var vm = (CodeViewModel)page;
        await vm.SearchAsync(new SearchRequest(LiteralTermInContent), display: true, default);
        Assert.AreEqual(2, vm.SearchMatchCount);

        vm.Document.Write(ContentChange.Write(0, 0, "// alpha42 written above\n"));

        // At once, the marks are on what they found, carried with it.
        var document = vm.Document.Current;
        Assert.IsTrue(vm.Editor.Found.Places.All(place => document.Slice(place.Start, place.Length) == LiteralTermInContent));

        // Once writing pauses, the search is run again over what is written now.
        for (var wait = 0; wait < 200 && vm.SearchMatchCount != 3; wait++) await Task.Delay(10);
        Assert.AreEqual(3, vm.SearchMatchCount, "the line written above is counted");
        Assert.AreEqual(3, vm.Editor.Found.Places.Count);
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
