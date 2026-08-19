using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Media;
using Nexaflow.Features.Common;
using Nexaflow.Features.Common.Search;
using Nexaflow.Features.Font.ViewModels;
using Nexaflow.Search;
using Nexaflow.Tests.Fixtures;
using NSubstitute;

namespace Nexaflow.Tests.Features.Search;

/// <summary>
/// The font comparison list answering <c>?</c>. Beyond the shared contract, the thing worth pinning here is
/// that this page is the one searchable list that must NOT filter: the list is the comparison, so a hit is
/// marked and selected while the rows that missed stay on screen.
/// </summary>
[TestClass]
[CoversNode("font-search")]
public class FontSearchableTests : SearchableContentConformanceTests
{
    protected override string LiteralTermInContent => "alpha42";
    protected override string RegexOnlyPattern     => @"alpha\d+";

    /// <summary>A shell whose RunOnUiAsync overloads actually run the delegate — the default substitute
    /// returns a null Task and swallows the body, no-opping every assertion below.</summary>
    private static IShellServices RunningShell()
    {
        var shell = Substitute.For<IShellServices>();
        shell.RunOnUiAsync(Arg.Any<Action>())
             .Returns(ci => { ci.Arg<Action>()(); return Task.CompletedTask; });
        shell.RunOnUiAsync(Arg.Any<Func<Task<SearchOutcome>>>())
             .Returns(ci => ci.Arg<Func<Task<SearchOutcome>>>()());
        shell.RunOnUiAsync(Arg.Any<Func<Task<bool>>>())
             .Returns(ci => ci.Arg<Func<Task<bool>>>()());
        return shell;
    }

    private const string Alpha1 = "alpha42-sans.ttf";
    private const string Alpha2 = "alpha42-mono.otf";

    /// <summary>
    /// Four fonts whose names the test controls. They are built through the failed-load route on purpose:
    /// every other route takes its display name from the <see cref="FontFamily"/>, and WPF resolves a family
    /// name it does not have installed to a fallback — seed "alpha42 Sans" and four rows come back called
    /// "Arial". A file that couldn't be read keeps its filename, which is the only name a unit test can
    /// choose. Search reads <c>DisplayName</c> and nothing else, so the load state doesn't affect what is
    /// under test.
    /// </summary>
    private static FontViewModel Build()
    {
        var vm = new FontViewModel(null, RunningShell());
        foreach (var name in new[] { Alpha1, "beta-serif.ttf", Alpha2, "gamma-display.woff" })
            vm.Fonts.Add(FontItemViewModel.Failed($@"C:\fonts\{name}", "not a real font file"));

        // Guard the fixture itself: a display name that stopped round-tripping would otherwise fail the
        // suite below in a dozen confusing places instead of one clear one.
        CollectionAssert.AreEqual(
            new[] { Alpha1, "beta-serif.ttf", Alpha2, "gamma-display.woff" },
            vm.Fonts.Select(f => f.DisplayName).ToArray(),
            "the seeded font names must round-trip to DisplayName");
        return vm;
    }

    protected override Task<ISearchable> CreateAsync() => Task.FromResult<ISearchable>(Build());

    protected override string Snapshot(ISearchable page)
    {
        var vm = (FontViewModel)page;
        return $"{vm.IsSearchActive}|{vm.SearchMatchCount}|{vm.CurrentSearchTerm}|" +
               $"{vm.SelectedFont?.DisplayName}|{string.Join(",", vm.Fonts.Where(f => f.IsSearchHit).Select(f => f.DisplayName))}";
    }

    private static SearchRequest Query(string text) => SearchSyntax.ParseRequest(text);

    // ── Font-specific behaviour beyond the shared contract ────────────────────

    [TestMethod]
    public void DisplayingSearch_MarksTheHits_AndLeavesEveryOtherRowInPlace() => WithPage(async page =>
    {
        var vm = (FontViewModel)page;

        await vm.SearchAsync(Query("alpha42"), display: true, default);

        Assert.AreEqual(4, vm.Fonts.Count, "a search must never remove a font from the comparison");
        CollectionAssert.AreEqual(
            new[] { true, false, true, false },
            vm.Fonts.Select(f => f.IsSearchHit).ToArray());
        Assert.AreEqual(2, vm.SearchMatchCount);
        Assert.AreEqual(Alpha1, vm.SelectedFont?.DisplayName, "the first hit is selected");
    });

    [TestMethod]
    public void FindNext_CyclesEveryHit_AndWraps() => WithPage(async page =>
    {
        var vm = (FontViewModel)page;
        await vm.SearchAsync(Query("alpha42"), display: true, default);

        Assert.AreEqual(Alpha1, vm.SelectedFont?.DisplayName);
        vm.FindNextMatchCommand.Execute(null);
        Assert.AreEqual(Alpha2, vm.SelectedFont?.DisplayName);
        vm.FindNextMatchCommand.Execute(null);
        Assert.AreEqual(Alpha1, vm.SelectedFont?.DisplayName, "next wraps back to the first hit");

        vm.FindPreviousMatchCommand.Execute(null);
        Assert.AreEqual(Alpha2, vm.SelectedFont?.DisplayName, "previous wraps the other way");
    });

    [TestMethod]
    public void ZeroMatches_StillShowsTheChip_SoNoMatchesIsAVisibleAnswer() => WithPage(async page =>
    {
        var vm = (FontViewModel)page;

        var outcome = await vm.SearchAsync(Query("nothinghere"), display: true, default);

        Assert.AreEqual(0, outcome.MatchCount);
        Assert.IsTrue(vm.IsSearchActive, "'no matches for X' is a result the user must be able to see and dismiss");
        Assert.IsFalse(vm.HasSearchMatches, "…but there is nothing to step through");
    });

    [TestMethod]
    public void ClearSearch_DropsTheMarksAndTheChip() => WithPage(async page =>
    {
        var vm = (FontViewModel)page;
        await vm.SearchAsync(Query("alpha42"), display: true, default);

        vm.ClearSearchCommand.Execute(null);

        Assert.IsFalse(vm.IsSearchActive);
        Assert.AreEqual(0, vm.SearchMatchCount);
        Assert.AreEqual(string.Empty, vm.CurrentSearchTerm);
        Assert.IsFalse(vm.Fonts.Any(f => f.IsSearchHit));
    });

    [TestMethod]
    public void ShowResults_MarksOnlyTheFontsTheAgentChose() => WithPage(async page =>
    {
        var vm = (FontViewModel)page;
        var found = await vm.SearchAsync(Query("alpha42"), display: false, default);
        Assert.AreEqual(2, found.Hits.Count);

        var narrowed = await vm.ShowResultsAsync([found.Hits[1]], default);

        Assert.IsTrue(narrowed);
        CollectionAssert.AreEqual(
            new[] { false, false, true, false },
            vm.Fonts.Select(f => f.IsSearchHit).ToArray());
        Assert.AreEqual(Alpha2, vm.SelectedFont?.DisplayName);
    });

    [TestMethod]
    public void HitIdsAreTheOneBasedIndex_TheSameHandleTheAiToolsUse() => WithPage(async page =>
    {
        var vm = (FontViewModel)page;

        var found = await vm.SearchAsync(Query("alpha42"), display: false, default);

        CollectionAssert.AreEqual(new[] { "1", "3" }, found.Hits.Select(h => h.Id).ToArray());
        // The contract behind that choice: the id round-trips through the page's own font resolver.
        Assert.AreEqual(Alpha1, vm.ResolveFont(found.Hits[0].Id)?.DisplayName);
        Assert.AreEqual(Alpha2, vm.ResolveFont(found.Hits[1].Id)?.DisplayName);
    });

    [TestMethod]
    public void AddingOrRemovingAFont_DropsTheSearch_RatherThanRenumberingItSilently() => WithPage(async page =>
    {
        var vm = (FontViewModel)page;
        await vm.SearchAsync(Query("alpha42"), display: true, default);
        Assert.AreEqual(2, vm.SearchMatchCount);

        // Hits are list positions; removing row 0 shifts every one of them.
        vm.RemoveFontCommand.Execute(vm.Fonts[0]);

        Assert.IsFalse(vm.IsSearchActive, "a stale hit list must not survive the list changing under it");
        Assert.AreEqual(0, vm.SearchMatchCount);
        Assert.IsFalse(vm.Fonts.Any(f => f.IsSearchHit));
    });
}
