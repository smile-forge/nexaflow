using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Nexaflow.Elevation.Contracts;
using Nexaflow.Features.Common;
using Nexaflow.Features.Common.Search;
using Nexaflow.Features.SystemInfo.Models;
using Nexaflow.Features.SystemInfo.ViewModels;
using Nexaflow.Search;
using Nexaflow.Tests.Fixtures;
using NSubstitute;

namespace Nexaflow.Tests.Features.Search;

/// <summary>
/// The Services page answering <c>?</c> over a seeded row set.
/// <para>
/// What is worth pinning beyond the shared contract: "?" drives the filter box the page already had —
/// same box, same two fields — so the tab has one search and not two, the query it ran is visible and
/// dismissible, and typing over the box drops the compiled pattern behind it.
/// </para>
/// </summary>
[TestClass]
[CoversNode("sysinfo-services-search")]
public class ServicesSearchableTests : SearchableContentConformanceTests
{
    protected override string LiteralTermInContent => "alpha42";
    protected override string RegexOnlyPattern     => @"alpha\d+";

    private const string ByName    = "alpha42-svc";     // matches on the service name
    private const string ByDisplay = "wuauserv";        // matches on the display name
    private const string Quiet     = "spooler";         // matches neither

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

    private static ServicesViewModel Build()
    {
        // The substitute never runs the queued gather, so this machine's real services never reach the
        // page and the three rows below are the whole list.
        var vm = new ServicesViewModel(RunningShell());
        vm.Services.Add(Row(ByName,    "contoso sync"));
        vm.Services.Add(Row(ByDisplay, "windows alpha42 update"));
        vm.Services.Add(Row(Quiet,     "print spooler"));
        return vm;
    }

    private static ServiceRow Row(string name, string display) =>
        new(name, display, "A service.", true, "Running", ServiceStartModes.Automatic);

    protected override Task<ISearchable> CreateAsync() => Task.FromResult<ISearchable>(Build());

    protected override string Snapshot(ISearchable page)
    {
        var vm = (ServicesViewModel)page;
        return $"{vm.IsSearchActive}|{vm.SearchMatchCount}|{vm.CurrentSearchTerm}|{vm.FilterText}|" +
               string.Join(",", Visible(vm));
    }

    private static string[] Visible(ServicesViewModel vm) =>
        vm.ServicesView.Cast<ServiceRow>().Select(r => r.Name).ToArray();

    private static SearchRequest Query(string text) => SearchSyntax.ParseRequest(text);

    private static string[] Ids(SearchOutcome outcome) =>
        outcome.Hits.Select(h => h.Id).OrderBy(s => s, StringComparer.Ordinal).ToArray();

    // ── Services-specific behaviour beyond the shared contract ────────────────

    [TestMethod]
    public void AServiceMatchesOnItsName_OrOnItsDisplayName() => WithPage(async page =>
    {
        var outcome = await page.SearchAsync(Query("alpha42"), display: false, default);

        CollectionAssert.AreEqual(new[] { ByName, ByDisplay }, Ids(outcome),
            "the same two fields the filter box already searched");
    });

    [TestMethod]
    public void DisplayingSearch_DrivesTheBoxThePageAlreadyHad() => WithPage(async page =>
    {
        var vm = (ServicesViewModel)page;

        await vm.SearchAsync(Query("alpha42"), display: true, default);

        CollectionAssert.AreEquivalent(new[] { ByName, ByDisplay }, Visible(vm));
        Assert.AreEqual("alpha42", vm.FilterText,
            "the query is shown in the box it filtered by, so an AI-run search is visible and dismissible");
        Assert.AreEqual(2, vm.SearchMatchCount);
    });

    [TestMethod]
    public void APatternRunsAsAPattern_WhereTheSameTextTypedIntoTheBoxWouldNot() => WithPage(async page =>
    {
        var vm = (ServicesViewModel)page;

        await vm.SearchAsync(new SearchRequest(@"alpha\d+", IsRegex: true), display: true, default);
        CollectionAssert.AreEquivalent(new[] { ByName, ByDisplay }, Visible(vm));

        // The box is a substring filter; the pattern only means something because "?" parsed it.
        vm.FilterText = @"alpha\d+";
        Assert.AreEqual(0, Visible(vm).Length);
    });

    [TestMethod]
    public void TypingInTheBox_DropsThePatternBehindIt() => WithPage(async page =>
    {
        var vm = (ServicesViewModel)page;
        await vm.SearchAsync(new SearchRequest(@"alpha\d+", IsRegex: true), display: true, default);
        Assert.IsTrue(vm.IsSearchActive);

        vm.FilterText = "spool";

        Assert.IsFalse(vm.IsSearchActive, "the chip goes with it — the box says what is being filtered by");
        CollectionAssert.AreEqual(new[] { Quiet }, Visible(vm), "and the typed text filters as typed text");
    });

    [TestMethod]
    public void ClearSearch_EmptiesTheBox_AndPutsEveryServiceBack() => WithPage(async page =>
    {
        var vm = (ServicesViewModel)page;
        await vm.SearchAsync(Query("alpha42"), display: true, default);

        vm.ClearSearchCommand.Execute(null);

        Assert.IsFalse(vm.IsSearchActive);
        Assert.AreEqual(string.Empty, vm.FilterText);
        Assert.AreEqual(3, Visible(vm).Length);
    });

    [TestMethod]
    public void ShowResults_PinsTheListToTheChosenServices() => WithPage(async page =>
    {
        var vm = (ServicesViewModel)page;
        var found = await vm.SearchAsync(Query("alpha42"), display: false, default);
        var chosen = found.Hits.Single(h => h.Id == ByDisplay);

        var narrowed = await vm.ShowResultsAsync([chosen], default);

        Assert.IsTrue(narrowed);
        CollectionAssert.AreEqual(new[] { ByDisplay }, Visible(vm));
        Assert.AreEqual(1, vm.SearchMatchCount);
    });

    [TestMethod]
    public void AGlobIsRefused_RatherThanQuietlyIgnored() => WithPage(async page =>
    {
        var outcome = await page.SearchAsync(
            SearchSyntax.ParseRequest("*.exe", [new Nexaflow.IO.Common.GlobTermRecognizer()]),
            display: false, default);

        Assert.IsTrue(outcome.Failed, "a service has a name, not a filename — dropping the term silently " +
                                      "would answer a narrower question than the one asked");
        StringAssert.Contains(outcome.Message ?? "", "Filename filters");
    });
}
