using System.IO;
using NSubstitute;
using Nexaflow.Features.Common;
using Nexaflow.Features.Common.Search;
using Nexaflow.Features.Markdown.ViewModels;
using Nexaflow.Search;
using Nexaflow.Tests.Fixtures;
using Nexaflow.Visuals.Text.Markdown;

namespace Nexaflow.Tests.Features.Search;

/// <summary>
/// The markdown editor as a searchable page. It searches whatever surface is showing and never switches the
/// view: source-box search selects in the source; rendered search is delegated to the view. The agent's
/// non-display search reads the document source.
/// </summary>
[TestClass]
[CoversNode("markdown-find")]
public sealed class MarkdownSearchableTests : SearchableContentConformanceTests
{
    // "alpha42" appears only in lower case; "alpha\d+" matches it but never appears verbatim.
    private const string Document = """
        # Release notes

        The alpha42 build fixes the importer.

        ## Known issues

        Nothing to report.

        Tracking alpha42 until the next drop.
        """;

    private string? _path;

    protected override string LiteralTermInContent => "alpha42";
    protected override string RegexOnlyPattern     => @"alpha\d+";

    /// <summary>A shell whose RunOnUiAsync runs the action inline — the display path marshals through it.</summary>
    private static IShellServices Shell()
    {
        var shell = Substitute.For<IShellServices>();
        shell.RunOnUiAsync(Arg.Any<Func<Task<SearchOutcome>>>())
             .Returns(ci => ci.Arg<Func<Task<SearchOutcome>>>()());
        return shell;
    }

    private MarkdownViewModel Open() => new(_path!, Shell());

    protected override Task<ISearchable> CreateAsync()
    {
        _path = Path.Combine(TestTemp.Root, "nexasearch_" + Guid.NewGuid().ToString("N") + ".md");
        File.WriteAllText(_path, Document);
        return Task.FromResult<ISearchable>(Open());
    }

    [TestCleanup]
    public void Cleanup()
    {
        if (_path is not null && File.Exists(_path)) File.Delete(_path);
    }

    protected override string Snapshot(ISearchable page)
    {
        var vm = (MarkdownViewModel)page;
        return $"{vm.SearchMatchCount}|{vm.IsSearchActive}|{vm.CurrentSearchTerm}|{vm.ViewMode}";
    }

    // ── Agent (non-display) reads the source, never touches the view ──────────

    [TestMethod]
    public void AgentSearch_ReadsSource_RegardlessOfViewMode() => WithPage(async page =>
    {
        var vm = (MarkdownViewModel)page;
        vm.ViewMode = MarkdownViewMode.Rendered;   // rendered view is showing

        var outcome = await vm.SearchAsync(new SearchRequest(LiteralTermInContent), display: false, default);

        Assert.AreEqual(2, outcome.MatchCount, "the document source has two 'alpha42' lines");
        Assert.IsFalse(vm.IsSearchActive, "a non-display search changes nothing on screen");
        Assert.IsTrue(outcome.Hits.All(h => int.TryParse(h.Id, out _)), "ids are source line numbers");
    });

    // ── Source mode: match and select in the source box ───────────────────────

    [TestMethod]
    public void DisplayingInSourceMode_SelectsInTheSourceBox() => WithPage(async page =>
    {
        var vm = (MarkdownViewModel)page;
        vm.ViewMode = MarkdownViewMode.Source;

        (int Offset, int Length)? span = null;
        vm.SourceSelectionRequested += (o, l) => span ??= (o, l);

        await vm.SearchAsync(new SearchRequest(LiteralTermInContent), display: true, default);

        Assert.AreEqual(2, vm.SearchMatchCount);
        Assert.IsTrue(vm.IsSearchActive);
        Assert.AreEqual(MarkdownViewMode.Source, vm.ViewMode, "the search never switches the view");
        Assert.IsNotNull(span, "the first match is selected in the source box");
        Assert.AreEqual(LiteralTermInContent, vm.Markdown.Substring(span!.Value.Offset, span.Value.Length),
            "the box is told to select the match itself");
    });

    [TestMethod]
    public void DisplayingSearch_NeverSwitchesTheView() => WithPage(async page =>
    {
        var vm = (MarkdownViewModel)page;
        vm.ViewMode = MarkdownViewMode.Rendered;      // rendered
        vm.FindInRendered = (_, _) => [new RenderedMatch(0, "The alpha42 build fixes the importer.")];

        await vm.SearchAsync(new SearchRequest(LiteralTermInContent), display: true, default);

        Assert.AreEqual(MarkdownViewMode.Rendered, vm.ViewMode, "a rendered-mode search stays in the rendered view");
        Assert.AreEqual(1, vm.SearchMatchCount, "the rendered surface's own match count is used");
    });

    // ── Rendered mode: delegated to the view ──────────────────────────────────

    [TestMethod]
    public void DisplayingInRenderedMode_DelegatesToTheRenderedSurface() => WithPage(async page =>
    {
        var vm = (MarkdownViewModel)page;
        vm.ViewMode = MarkdownViewMode.Rendered;

        TextSearchMatcher? handed = null;
        vm.FindInRendered = (m, _) => { handed = m; return [new RenderedMatch(0, "preview")]; };

        var outcome = await vm.SearchAsync(new SearchRequest(LiteralTermInContent), display: true, default);

        Assert.IsNotNull(handed, "the rendered surface is asked to run the search");
        Assert.AreEqual(1, outcome.MatchCount);
    });

    [TestMethod]
    public void SwitchingToRendered_CarriesTheSearchOn_FromTheMatchTheReaderWasOn() => WithPage(async page =>
    {
        var vm = (MarkdownViewModel)page;
        vm.ViewMode = MarkdownViewMode.Source;

        await vm.SearchAsync(new SearchRequest(LiteralTermInContent), display: true, default);
        vm.FindNextMatchCommand.Execute(null);   // on to the second of them

        int? from = null;
        vm.FindInRendered = (_, at) => { from = at; return [new RenderedMatch(0, "a"), new RenderedMatch(1, "b")]; };

        vm.ViewMode = MarkdownViewMode.Rendered;   // slid back to the rendered view
        Assert.IsTrue(vm.IsSearchActive, "the search is the reader's, not the surface's");

        vm.CarrySearchToShownSurface();             // what the view does once the rendered surface is laid out

        Assert.AreEqual(vm.Markdown.LastIndexOf(LiteralTermInContent, StringComparison.Ordinal), from, "taken up at the second match");
        Assert.AreEqual(2, vm.SearchMatchCount, "and counted on the surface now showing");
    });

    [TestMethod]
    public void SwitchingToSource_CarriesTheSearchOn_FromTheMatchTheReaderWasOn() => WithPage(async page =>
    {
        var vm = (MarkdownViewModel)page;
        vm.ViewMode = MarkdownViewMode.Rendered;

        var second = vm.Markdown.LastIndexOf(LiteralTermInContent, StringComparison.Ordinal);
        vm.FindInRendered = (_, _) => [new RenderedMatch(0, "a"), new RenderedMatch(1, "b")];
        vm.RenderedFound = () => ([(second, LiteralTermInContent.Length)], 0);   // the reader stepped on to the second

        await vm.SearchAsync(new SearchRequest(LiteralTermInContent), display: true, default);

        (int Offset, int Length)? selected = null;
        vm.SourceSelectionRequested += (offset, length) => selected = (offset, length);

        vm.ViewMode = MarkdownViewMode.Source;
        vm.CarrySearchToShownSurface();

        Assert.IsTrue(vm.IsSearchActive);
        Assert.AreEqual((second, LiteralTermInContent.Length), selected, "the raw box selects the match the reader was on");
    });

    [TestMethod]
    public void ASearchClearedBeforeTheNewSurfaceIsLaidOut_IsNotTakenUpOnIt() => WithPage(async page =>
    {
        var vm = (MarkdownViewModel)page;
        vm.ViewMode = MarkdownViewMode.Source;
        await vm.SearchAsync(new SearchRequest(LiteralTermInContent), display: true, default);

        vm.ViewMode = MarkdownViewMode.Rendered;
        vm.ClearSearchCommand.Execute(null);

        var asked = false;
        vm.FindInRendered = (_, _) => { asked = true; return []; };
        vm.CarrySearchToShownSurface();

        Assert.IsFalse(asked, "nothing is searched for");
        Assert.IsFalse(vm.IsSearchActive);
    });

    // ── Both halves of the split, and the marks beside them ──────────────────

    [TestMethod]
    public void InTheSourceView_EveryMatchIsWashed_AndSteppingMovesTheOneLookedAt() => WithPage(async page =>
    {
        var vm = (MarkdownViewModel)page;
        vm.ViewMode = MarkdownViewMode.Source;

        await vm.SearchAsync(new SearchRequest(LiteralTermInContent), display: true, default);

        Assert.AreEqual(2, vm.SourceSearch.Places.Count, "every match, not only the one selected");
        Assert.AreEqual(0, vm.SourceSearch.At);

        vm.FindNextMatchCommand.Execute(null);
        Assert.AreEqual(1, vm.SourceSearch.At, "the next one is the one looked at");
    });

    [TestMethod]
    public void InTheSplit_TheRawHalfWashesWhatTheRenderedHalfFound_AndFollowsItsSteps() => WithPage(async page =>
    {
        var vm = (MarkdownViewModel)page;
        vm.ViewMode = MarkdownViewMode.Split;

        IReadOnlyList<(int Start, int Length)> places = [(10, 7), (90, 7)];
        var at = 0;
        vm.FindInRendered = (_, _) => [new RenderedMatch(0, "a"), new RenderedMatch(1, "b")];
        vm.RenderedFound = () => (places, at);
        vm.StepRendered = delta => at = (at + delta + places.Count) % places.Count;

        await vm.SearchAsync(new SearchRequest(LiteralTermInContent), display: true, default);

        CollectionAssert.AreEqual(places.ToArray(), vm.SourceSearch.Places.ToArray(), "the one search, shown in both halves");
        Assert.AreEqual(0, vm.SourceSearch.At);

        vm.FindNextMatchCommand.Execute(null);
        Assert.AreEqual(1, vm.SourceSearch.At, "a step in the rendered half moves the one looked at in the raw half too");
    });

    [TestMethod]
    public void WithTheRenderedViewAlone_TheRawHalfWashesNothing() => WithPage(async page =>
    {
        var vm = (MarkdownViewModel)page;
        vm.ViewMode = MarkdownViewMode.Rendered;
        vm.FindInRendered = (_, _) => [new RenderedMatch(0, "a")];
        vm.RenderedFound = () => ([(10, 7)], 0);

        await vm.SearchAsync(new SearchRequest(LiteralTermInContent), display: true, default);

        Assert.AreEqual(0, vm.SourceSearch.Places.Count, "it is not showing");
    });

    [TestMethod]
    public void TheMarks_AreReadAgain_WhenTheRenderedSurfaceIsLaidOutAgain() => WithPage(async page =>
    {
        var vm = (MarkdownViewModel)page;
        vm.ViewMode = MarkdownViewMode.Rendered;

        IReadOnlyList<double> marks = [0.1];
        vm.FindInRendered = (_, _) => [new RenderedMatch(0, "a"), new RenderedMatch(1, "b")];
        vm.RenderedMarkPositions = () => marks;

        await vm.SearchAsync(new SearchRequest(LiteralTermInContent), display: true, default);
        CollectionAssert.AreEqual(new[] { 0.1 }, vm.MiniMapMarks.ToArray());

        marks = [0.1, 0.6];   // a section opened, and the second match is on the page now
        vm.RenderedLaidOut();

        CollectionAssert.AreEqual(new[] { 0.1, 0.6 }, vm.MiniMapMarks.ToArray(), "the strip follows the page");
    });

    [TestMethod]
    public void ClearingSearch_LeavesTheViewModeUntouched() => WithPage(async page =>
    {
        var vm = (MarkdownViewModel)page;
        vm.ViewMode = MarkdownViewMode.Rendered;
        vm.FindInRendered = (_, _) => [new RenderedMatch(0, "x")];

        await vm.SearchAsync(new SearchRequest(LiteralTermInContent), display: true, default);
        vm.ClearSearchCommand.Execute(null);

        Assert.IsFalse(vm.IsSearchActive);
        Assert.AreEqual(MarkdownViewMode.Rendered, vm.ViewMode, "clearing a search does not change the view mode");
    });
}
