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
        _path = Path.Combine(Path.GetTempPath(), "nexasearch_" + Guid.NewGuid().ToString("N") + ".md");
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
        return $"{vm.SearchMatchCount}|{vm.IsSearchActive}|{vm.CurrentSearchTerm}|{vm.SourceOnly}";
    }

    // ── Agent (non-display) reads the source, never touches the view ──────────

    [TestMethod]
    public void AgentSearch_ReadsSource_RegardlessOfViewMode() => WithPage(async page =>
    {
        var vm = (MarkdownViewModel)page;
        vm.SourceOnly = false;   // rendered view is showing

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
        vm.SourceOnly = true;

        (int Offset, int Length)? span = null;
        vm.SourceSelectionRequested += (o, l) => span ??= (o, l);

        await vm.SearchAsync(new SearchRequest(LiteralTermInContent), display: true, default);

        Assert.AreEqual(2, vm.SearchMatchCount);
        Assert.IsTrue(vm.IsSearchActive);
        Assert.IsTrue(vm.SourceOnly, "the search never switches the view");
        Assert.IsNotNull(span, "the first match is selected in the source box");
        Assert.AreEqual(LiteralTermInContent, vm.Markdown.Substring(span!.Value.Offset, span.Value.Length),
            "the box is told to select the match itself");
    });

    [TestMethod]
    public void DisplayingSearch_NeverSwitchesTheView() => WithPage(async page =>
    {
        var vm = (MarkdownViewModel)page;
        vm.SourceOnly = false;                        // rendered
        vm.FindInRendered = _ => [new RenderedMatch(0, "The alpha42 build fixes the importer.")];

        await vm.SearchAsync(new SearchRequest(LiteralTermInContent), display: true, default);

        Assert.IsFalse(vm.SourceOnly, "a rendered-mode search stays in the rendered view");
        Assert.AreEqual(1, vm.SearchMatchCount, "the rendered surface's own match count is used");
    });

    // ── Rendered mode: delegated to the view ──────────────────────────────────

    [TestMethod]
    public void DisplayingInRenderedMode_DelegatesToTheRenderedSurface() => WithPage(async page =>
    {
        var vm = (MarkdownViewModel)page;
        vm.SourceOnly = false;

        TextSearchMatcher? handed = null;
        vm.FindInRendered = m => { handed = m; return [new RenderedMatch(0, "preview")]; };

        var outcome = await vm.SearchAsync(new SearchRequest(LiteralTermInContent), display: true, default);

        Assert.IsNotNull(handed, "the rendered surface is asked to run the search");
        Assert.AreEqual(1, outcome.MatchCount);
    });

    [TestMethod]
    public void SwitchingSurfaceWhileSearching_AbandonsTheSearch() => WithPage(async page =>
    {
        var vm = (MarkdownViewModel)page;
        vm.SourceOnly = true;

        await vm.SearchAsync(new SearchRequest(LiteralTermInContent), display: true, default);
        Assert.IsTrue(vm.IsSearchActive);

        vm.SourceOnly = false;   // toggled to the rendered view

        Assert.IsFalse(vm.IsSearchActive, "the search belonged to the surface that is no longer showing");
        Assert.AreEqual(0, vm.SearchMatchCount);
    });

    [TestMethod]
    public void ClearingSearch_LeavesTheViewModeUntouched() => WithPage(async page =>
    {
        var vm = (MarkdownViewModel)page;
        vm.SourceOnly = false;
        vm.FindInRendered = _ => [new RenderedMatch(0, "x")];

        await vm.SearchAsync(new SearchRequest(LiteralTermInContent), display: true, default);
        vm.ClearSearchCommand.Execute(null);

        Assert.IsFalse(vm.IsSearchActive);
        Assert.IsFalse(vm.SourceOnly, "clearing a search does not change the view mode");
    });
}
