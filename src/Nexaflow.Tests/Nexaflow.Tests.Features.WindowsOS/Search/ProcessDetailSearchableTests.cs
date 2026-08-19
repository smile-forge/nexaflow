using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Nexaflow.Features.Common;
using Nexaflow.Features.Common.Search;
using Nexaflow.Features.Processes.Models;
using Nexaflow.Features.Processes.Services;
using Nexaflow.Features.Processes.ViewModels;
using Nexaflow.Search;
using Nexaflow.Tests.Features.Processes;
using Nexaflow.Tests.Fixtures;
using NSubstitute;

namespace Nexaflow.Tests.Features.Search;

/// <summary>
/// The per-process inspector answering <c>?</c>. This is the only searchable page whose target changes
/// while you look at it, so what these tests pin is the scoping rule: a query runs against the sub-tab on
/// screen, and switching tab drops it rather than re-applying a TID pattern to module paths.
/// <para>
/// Runs pumped (the default): the three section lists are <c>ICollectionView</c>s, which are affine to the
/// thread that created them, and the shell substitute runs every marshalled body inline on that thread.
/// </para>
/// </summary>
[TestClass]
[CoversNode("processes-detail-search")]
public class ProcessDetailSearchableTests : SearchableContentConformanceTests
{
    protected override string LiteralTermInContent => "alpha42";
    protected override string RegexOnlyPattern     => @"alpha\d+";

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

    private static readonly ProcessDetail Detail = new()
    {
        Pid = 4242,
        Name = "host.exe",
        Threads =
        [
            new ThreadInfo { Tid = 100 },
            new ThreadInfo { Tid = 4242 },
            new ThreadInfo { Tid = 42425 },
        ],
        Modules =
        [
            new ModuleInfo { Name = "alpha42.dll", Path = @"C:\windows\system32\alpha42.dll" },
            new ModuleInfo { Name = "ntdll.dll",   Path = @"C:\windows\system32\ntdll.dll" },
            new ModuleInfo { Name = "beta.dll",    Path = @"C:\tools\alpha42\beta.dll" },
        ],
    };

    private static readonly IReadOnlyList<HandleInfo> Handles =
    [
        new HandleInfo { Type = "File",  Name = @"\Device\HarddiskVolume2\alpha42.log", HandleValue = "0x10" },
        new HandleInfo { Type = "Key",   Name = @"\REGISTRY\MACHINE\SOFTWARE",          HandleValue = "0x14" },
        new HandleInfo { Type = "Event", Name = @"\Sessions\1\alpha42-ready",           HandleValue = "0x18" },
    ];

    /// <summary>A detail page with all three lists loaded, parked on the section under test. Modules is the
    /// default so the inherited content conformance tests have a list to find the seed term in.</summary>
    private static ProcessDetailViewModel Build(string section = "Modules")
    {
        var vm = new ProcessDetailViewModel(RunningShell(), 4242, new FakeProcessSource());
        vm.ApplyDetail(Detail);
        vm.ApplyInspect(new ProcessInspect.InspectResult(true, false, "", Handles, null, null));
        vm.SelectedSection = section;
        return vm;
    }

    protected override Task<ISearchable> CreateAsync() => Task.FromResult<ISearchable>(Build());

    protected override string Snapshot(ISearchable page)
    {
        var vm = (ProcessDetailViewModel)page;
        return $"{vm.IsSearchActive}|{vm.SearchMatchCount}|{vm.CurrentSearchTerm}|{vm.SelectedSection}|" +
               $"{Visible(vm.ThreadsView)}|{Visible(vm.ModulesView)}|{Visible(vm.HandlesView)}";
    }

    private static string Visible(System.ComponentModel.ICollectionView view) =>
        string.Join(",", view.Cast<object>().Select(o => o switch
        {
            ThreadInfo t => t.Tid.ToString(),
            ModuleInfo m => m.Name,
            HandleInfo h => h.HandleValue,
            _            => "?",
        }));

    private static SearchRequest Query(string text) => SearchSyntax.ParseRequest(text);

    // ── Per-section scoping ───────────────────────────────────────────────────

    [TestMethod]
    public void OnModules_MatchesNameOrPath_AndFiltersTheList() => WithPage(async page =>
    {
        var vm = (ProcessDetailViewModel)page;

        await vm.SearchAsync(Query("alpha42"), display: true, default);

        // alpha42.dll by name, beta.dll by path.
        Assert.AreEqual(2, vm.SearchMatchCount);
        CollectionAssert.AreEqual(new[] { "alpha42.dll", "beta.dll" },
                                  vm.ModulesView.Cast<ModuleInfo>().Select(m => m.Name).ToArray());
    });

    [TestMethod]
    public void OnThreads_MatchesTheTid_AsAWholeNumber_NotASubstringOfALongerOne() => RunUnpumped(async () =>
    {
        var vm = Build("Threads");

        var outcome = await vm.SearchAsync(Query("4242"), display: true, default);

        // A bare literal is the word it spells, everywhere in the app — so searching a thread id can't drag
        // in every longer id that happens to start with it. 42425 stays out.
        CollectionAssert.AreEqual(new[] { "4242" }, outcome.Hits.Select(h => h.Id).ToArray());
        CollectionAssert.AreEqual(new[] { 4242 },
                                  vm.ThreadsView.Cast<ThreadInfo>().Select(t => t.Tid).ToArray());

        // …and the prefix form still reaches it, so the narrow default is not a dead end.
        var prefixed = await vm.SearchAsync(Query("4242*"), display: false, default);
        CollectionAssert.AreEquivalent(new[] { "4242", "42425" }, prefixed.Hits.Select(h => h.Id).ToArray());
    });

    [TestMethod]
    public void OnHandles_MatchesTheNameField() => RunUnpumped(async () =>
    {
        var vm = Build("Handles");

        var outcome = await vm.SearchAsync(Query("alpha42"), display: true, default);

        CollectionAssert.AreEqual(new[] { "0x10|File", "0x18|Event" }, outcome.Hits.Select(h => h.Id).ToArray());
        Assert.AreEqual(2, vm.HandlesView.Cast<HandleInfo>().Count());
    });

    [TestMethod]
    public void OnHandles_BeforeTheyAreLoaded_SaysSo_RatherThanReportingNoMatches() => RunUnpumped(async () =>
    {
        var vm = new ProcessDetailViewModel(RunningShell(), 4242, new FakeProcessSource());
        vm.ApplyDetail(Detail);          // threads + modules arrive on the refresh tick…
        vm.SelectedSection = "Handles";  // …but handles need an approved elevated read first

        var outcome = await vm.SearchAsync(Query("alpha42"), display: true, default);

        Assert.AreEqual(0, outcome.MatchCount);
        Assert.IsFalse(outcome.Failed, "the search is fine; the list simply isn't there yet");
        StringAssert.Contains(outcome.Message ?? "", "Load handles");
    });

    [TestMethod]
    public void OnATabWithNoList_SaysWhereToSearch_InsteadOfFailing() => RunUnpumped(async () =>
    {
        var vm = Build("General");

        var outcome = await vm.SearchAsync(Query("alpha42"), display: true, default);

        Assert.AreEqual(0, outcome.MatchCount);
        // Not Failed: reporting it as a failure would read as "this page can't do patterns", which is untrue.
        Assert.IsFalse(outcome.Failed);
        StringAssert.Contains(outcome.Message ?? "", "Threads, Modules or Handles");
    });

    [TestMethod]
    public void SearchingOneSection_LeavesTheOtherListsWhole() => WithPage(async page =>
    {
        var vm = (ProcessDetailViewModel)page;

        await vm.SearchAsync(Query("alpha42"), display: true, default);

        Assert.AreEqual(3, vm.ThreadsView.Cast<ThreadInfo>().Count(), "Threads was not the section searched");
        Assert.AreEqual(3, vm.HandlesView.Cast<HandleInfo>().Count(), "nor was Handles");
    });

    [TestMethod]
    public void SwitchingSection_DropsTheSearch_RatherThanReapplyingItToADifferentList() => WithPage(async page =>
    {
        var vm = (ProcessDetailViewModel)page;
        await vm.SearchAsync(Query("alpha42"), display: true, default);
        Assert.IsTrue(vm.IsSearchActive);

        vm.SelectedSection = "Threads";

        Assert.IsFalse(vm.IsSearchActive, "a module query means nothing against a thread id");
        Assert.AreEqual(3, vm.ModulesView.Cast<ModuleInfo>().Count(), "and the list it filtered is whole again");
    });

    [TestMethod]
    public void ShowResults_NarrowsToTheModulesTheAgentChose() => WithPage(async page =>
    {
        var vm = (ProcessDetailViewModel)page;
        var found = await vm.SearchAsync(Query("alpha42"), display: false, default);
        Assert.AreEqual(2, found.Hits.Count);

        var narrowed = await vm.ShowResultsAsync([found.Hits[1]], default);

        Assert.IsTrue(narrowed);
        CollectionAssert.AreEqual(new[] { "beta.dll" },
                                  vm.ModulesView.Cast<ModuleInfo>().Select(m => m.Name).ToArray());
    });

    [TestMethod]
    public void AFilteredListSurvivesTheRefreshTick() => WithPage(async page =>
    {
        var vm = (ProcessDetailViewModel)page;
        await vm.SearchAsync(Query("alpha42"), display: true, default);
        Assert.AreEqual(2, vm.ModulesView.Cast<ModuleInfo>().Count());

        // The 1s tick reconciles Modules in place; the view's filter must still be the one searching.
        vm.ApplyDetail(Detail);

        CollectionAssert.AreEqual(new[] { "alpha42.dll", "beta.dll" },
                                  vm.ModulesView.Cast<ModuleInfo>().Select(m => m.Name).ToArray());
    });

    [TestMethod]
    public void ClearSearch_RestoresTheWholeList() => WithPage(async page =>
    {
        var vm = (ProcessDetailViewModel)page;
        await vm.SearchAsync(Query("alpha42"), display: true, default);

        vm.ClearSearchCommand.Execute(null);

        Assert.IsFalse(vm.IsSearchActive);
        Assert.AreEqual(0, vm.SearchMatchCount);
        Assert.AreEqual(3, vm.ModulesView.Cast<ModuleInfo>().Count());
    });
}
