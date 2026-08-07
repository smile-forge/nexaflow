using System;
using System.Linq;
using System.Threading.Tasks;
using Nexaflow.Features.Common;
using Nexaflow.Features.Common.Search;
using Nexaflow.Features.SystemInfo.Models;
using Nexaflow.Features.SystemInfo.Services;
using Nexaflow.Features.SystemInfo.ViewModels;
using Nexaflow.Search;
using Nexaflow.Tests.Fixtures;
using NSubstitute;

namespace Nexaflow.Tests.Features.Search;

/// <summary>
/// The device dashboard answering <c>?</c>. The cards are seeded directly rather than gathered from this
/// machine — what is under test is what a search does to the page, and a fixture whose facts depend on
/// which PC runs it cannot state that.
/// <para>
/// The one behaviour worth pinning beyond the shared contract: this page <em>marks</em> and never filters.
/// The dashboard is a fixed set of cards the user is reading, not a list they are looking through, so
/// hiding the rows that missed would take away the card they were shown.
/// </para>
/// </summary>
[TestClass]
[CoversNode("sysinfo-search")]
public class SystemInfoSearchableTests : SearchableContentConformanceTests
{
    protected override string LiteralTermInContent => "alpha42";
    protected override string RegexOnlyPattern     => @"alpha\d+";

    private const string HitByValue = "Name";            // Operating System / Name
    private const string HitByLabel = "alpha42 chipset"; // Hardware / alpha42 chipset

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

    private static SystemInfoViewModel Build()
    {
        // The substitute never runs the queued gather, so nothing from this machine reaches the page and
        // the cards below are the whole of what is on screen.
        var vm = new SystemInfoViewModel(RunningShell(), new SystemInfoCollector());

        vm.Sections.Add(new SystemInfoSection("Operating System", "🪟")
            .Add(HitByValue, "Windows alpha42 Edition")     // matches on the VALUE
            .Add("Architecture", "64-bit"));

        vm.Sections.Add(new SystemInfoSection("Hardware", "🧩")
            .Add(HitByLabel, "Contoso")                     // matches on the LABEL
            .Add("Memory", "16 GB"));

        return vm;
    }

    protected override Task<ISearchable> CreateAsync() => Task.FromResult<ISearchable>(Build());

    protected override string Snapshot(ISearchable page)
    {
        var vm = (SystemInfoViewModel)page;
        return $"{vm.IsSearchActive}|{vm.SearchMatchCount}|{vm.CurrentSearchTerm}|" +
               string.Join(",", Marked(vm));
    }

    private static string[] Marked(SystemInfoViewModel vm) =>
        vm.Sections.SelectMany(s => s.Items).Where(i => i.IsSearchHit).Select(i => i.Label).ToArray();

    private static SearchRequest Query(string text) => SearchSyntax.ParseRequest(text);

    private static string[] Ids(SearchOutcome outcome) =>
        outcome.Hits.Select(h => h.Id).OrderBy(s => s, StringComparer.Ordinal).ToArray();

    // ── Dashboard-specific behaviour beyond the shared contract ───────────────

    [TestMethod]
    public void AFactMatchesOnItsLabel_OrOnItsValue() => WithPage(async page =>
    {
        var outcome = await page.SearchAsync(Query("alpha42"), display: false, default);

        CollectionAssert.AreEqual(new[] { $"Hardware/{HitByLabel}", $"Operating System/{HitByValue}" },
            Ids(outcome), "a fact is findable by what it is called and by what it says");
    });

    [TestMethod]
    public void DisplayingSearch_MarksTheHits_AndLeavesEveryOtherFactOnScreen() => WithPage(async page =>
    {
        var vm = (SystemInfoViewModel)page;
        var factsBefore = vm.Sections.Sum(s => s.Items.Count);

        await vm.SearchAsync(Query("alpha42"), display: true, default);

        CollectionAssert.AreEquivalent(new[] { HitByValue, HitByLabel }, Marked(vm));
        Assert.AreEqual(2, vm.SearchMatchCount);
        Assert.AreEqual(factsBefore, vm.Sections.Sum(s => s.Items.Count),
            "the dashboard is what the user is reading — a search marks it, it never takes cards away");
        Assert.IsFalse(vm.HasSearchMatches,
            "and there is nothing to step: every hit is already on screen, with no selection to move");
    });

    [TestMethod]
    public void ZeroMatches_StillShowsTheChip() => WithPage(async page =>
    {
        var vm = (SystemInfoViewModel)page;

        var outcome = await vm.SearchAsync(Query("nothinghere"), display: true, default);

        Assert.AreEqual(0, outcome.MatchCount);
        Assert.IsFalse(outcome.Failed, "running and finding nothing is not a failure");
        Assert.IsTrue(vm.IsSearchActive, "\"no matches for X\" is a result the user has to be able to see");
        Assert.AreEqual(0, Marked(vm).Length);
    });

    [TestMethod]
    public void ClearSearch_DropsEveryMark() => WithPage(async page =>
    {
        var vm = (SystemInfoViewModel)page;
        await vm.SearchAsync(Query("alpha42"), display: true, default);

        vm.ClearSearchCommand.Execute(null);

        Assert.IsFalse(vm.IsSearchActive);
        Assert.AreEqual(0, vm.SearchMatchCount);
        Assert.AreEqual(string.Empty, vm.CurrentSearchTerm);
        Assert.AreEqual(0, Marked(vm).Length);
    });

    [TestMethod]
    public void ShowResults_MarksOnlyTheFactsTheAgentChose() => WithPage(async page =>
    {
        var vm = (SystemInfoViewModel)page;
        var found = await vm.SearchAsync(Query("alpha42"), display: false, default);
        var chosen = found.Hits.Single(h => h.Id.EndsWith(HitByLabel, StringComparison.Ordinal));

        var marked = await vm.ShowResultsAsync([chosen], default);

        Assert.IsTrue(marked);
        CollectionAssert.AreEqual(new[] { HitByLabel }, Marked(vm));
        Assert.AreEqual(1, vm.SearchMatchCount);
    });

    [TestMethod]
    public void ShowResults_WithIdsThisPageNeverGave_Declines() => WithPage(async page =>
    {
        var vm = (SystemInfoViewModel)page;

        var marked = await vm.ShowResultsAsync([new SearchHit("Nowhere/Nothing", "Nothing")], default);

        Assert.IsFalse(marked, "the agent needs to know it must describe the matches instead");
        Assert.AreEqual(0, Marked(vm).Length);
    });

    [TestMethod]
    public void AnEmptyDashboard_SaysThereIsNothingToSearch_NotThatSearchFailed() => WithPage(async page =>
    {
        var vm = (SystemInfoViewModel)page;
        vm.Sections.Clear();

        var outcome = await vm.SearchAsync(Query("alpha42"), display: false, default);

        Assert.AreEqual(0, outcome.MatchCount);
        Assert.IsFalse(outcome.Failed,
            "the page understood the query — it has no facts yet, which is a different thing");
        Assert.IsFalse(string.IsNullOrWhiteSpace(outcome.Message));
    });
}
