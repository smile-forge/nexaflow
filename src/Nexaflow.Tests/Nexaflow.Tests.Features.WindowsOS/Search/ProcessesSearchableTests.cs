using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Nexaflow.Features.Common;
using Nexaflow.Features.Common.Search;
using Nexaflow.Features.Processes.Models;
using Nexaflow.Features.Processes.ViewModels;
using Nexaflow.Search;
using Nexaflow.Tests.Features.Processes;
using Nexaflow.Tests.Fixtures;
using NSubstitute;

namespace Nexaflow.Tests.Features.Search;

/// <summary>
/// The process list answering <c>?</c>. The point of interest here is that the page already had a search
/// box, so the contract these tests protect is that there is still exactly ONE search: a "?" query drives
/// the same box, over the same five fields, and typing in the box takes the list back off the AI.
/// </summary>
[TestClass]
[CoversNode("processes-search")]
public class ProcessesSearchableTests : SearchableContentConformanceTests
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

    /// <summary>
    /// Four processes, each carrying the seed term in a different column, so a test that only ever matched
    /// on Name would show up as a missing hit rather than passing by accident.
    /// </summary>
    private static ProcessesViewModel Build()
    {
        var vm = new ProcessesViewModel(RunningShell(), new FakeProcessSource());
        vm.ApplySnapshot(new ProcessSnapshot
        {
            ProcessorCount = 4,
            Processes =
            [
                new ProcessSample { Pid = 10, ParentPid = 0, Name = "alpha42.exe",  Company = "Acme",    Description = "Runner" },
                new ProcessSample { Pid = 11, ParentPid = 0, Name = "beta.exe",     Company = "alpha42 Ltd", Description = "Helper" },
                new ProcessSample { Pid = 12, ParentPid = 0, Name = "gamma.exe",    Company = "Acme",    Description = "alpha42 daemon" },
                new ProcessSample { Pid = 13, ParentPid = 0, Name = "delta.exe",    Company = "Acme",    Description = "Nothing",
                                    Path = @"C:\tools\alpha42\delta.exe" },
                new ProcessSample { Pid = 14, ParentPid = 0, Name = "quiet.exe",    Company = "Acme",    Description = "Nothing" },
            ],
        });
        // Tree mode is on by default and only expanded rows are listed; these are all roots, so all five show.
        Assert.AreEqual(5, vm.Rows.Count, "precondition: every seeded process is listed");
        return vm;
    }

    protected override Task<ISearchable> CreateAsync() => Task.FromResult<ISearchable>(Build());

    protected override string Snapshot(ISearchable page)
    {
        var vm = (ProcessesViewModel)page;
        return $"{vm.FilterText}|{string.Join(",", vm.Rows.Select(r => r.Pid))}";
    }

    private static SearchRequest Query(string text) => SearchSyntax.ParseRequest(text);

    // ── Processes-specific behaviour beyond the shared contract ───────────────

    [TestMethod]
    public void AMatchInAnyOfTheFiveColumns_Counts() => WithPage(async page =>
    {
        var outcome = await page.SearchAsync(Query("alpha42"), display: false, default);

        // name, company, description, path — one each. "quiet.exe" carries the term nowhere.
        CollectionAssert.AreEquivalent(
            new[] { "10", "11", "12", "13" },
            outcome.Hits.Select(h => h.Id).ToArray());
    });

    [TestMethod]
    public void PidIsSearchable_BecauseTheBoxAlwaysSearchedIt() => WithPage(async page =>
    {
        var outcome = await page.SearchAsync(Query("13"), display: false, default);

        CollectionAssert.AreEqual(new[] { "13" }, outcome.Hits.Select(h => h.Id).ToArray());
    });

    [TestMethod]
    public void DisplayingSearch_PutsTheSyntaxInTheBox_AndNarrowsTheRows() => WithPage(async page =>
    {
        var vm = (ProcessesViewModel)page;

        await vm.SearchAsync(Query(@"/alpha\d+/"), display: true, default);

        Assert.AreEqual(@"/alpha\d+/", vm.FilterText, "the box shows what was searched for, round-tripped");
        CollectionAssert.AreEquivalent(new[] { 10, 11, 12, 13 }, vm.Rows.Select(r => r.Pid).ToArray());
    });

    [TestMethod]
    public void TypingInTheBox_DropsAnAiInstalledPattern() => WithPage(async page =>
    {
        var vm = (ProcessesViewModel)page;
        await vm.SearchAsync(Query(@"/alpha\d+/"), display: true, default);

        vm.FilterText = "quiet";   // the user takes over

        // The stale compiled pattern must not keep filtering underneath a literal box the user now drives.
        CollectionAssert.AreEqual(new[] { 14 }, vm.Rows.Select(r => r.Pid).ToArray());
    });

    [TestMethod]
    public void ClearingTheBox_RestoresEveryRow() => WithPage(async page =>
    {
        var vm = (ProcessesViewModel)page;
        await vm.SearchAsync(Query("alpha42"), display: true, default);
        Assert.AreEqual(4, vm.Rows.Count);

        vm.ClearFilterCommand.Execute(null);

        Assert.AreEqual(5, vm.Rows.Count, "clearing the box must also drop the pattern behind it");
    });

    [TestMethod]
    public void ShowResults_PinsTheListToTheChosenPids() => WithPage(async page =>
    {
        var vm = (ProcessesViewModel)page;
        var found = await vm.SearchAsync(Query("alpha42"), display: false, default);

        var narrowed = await vm.ShowResultsAsync(found.Hits.Where(h => h.Id is "11" or "13").ToList(), default);

        Assert.IsTrue(narrowed);
        CollectionAssert.AreEquivalent(new[] { 11, 13 }, vm.Rows.Select(r => r.Pid).ToArray());
        Assert.AreEqual("2 selected", vm.FilterText);
    });

    [TestMethod]
    public void APinnedListSurvivesTheNextSnapshot_AndDropsAProcessThatExited() => WithPage(async page =>
    {
        var vm = (ProcessesViewModel)page;
        var found = await vm.SearchAsync(Query("alpha42"), display: false, default);
        await vm.ShowResultsAsync(found.Hits.Where(h => h.Id is "11" or "13").ToList(), default);

        // The 1s tick folds in a fresh sample; PID 13 is gone from it.
        vm.ApplySnapshot(new ProcessSnapshot
        {
            ProcessorCount = 4,
            Processes =
            [
                new ProcessSample { Pid = 10, ParentPid = 0, Name = "alpha42.exe" },
                new ProcessSample { Pid = 11, ParentPid = 0, Name = "beta.exe", Company = "alpha42 Ltd" },
                new ProcessSample { Pid = 14, ParentPid = 0, Name = "quiet.exe" },
            ],
        });

        CollectionAssert.AreEqual(new[] { 11 }, vm.Rows.Select(r => r.Pid).ToArray(),
            "the pin still holds, and a pinned process that exited simply isn't there any more");
    });

    [TestMethod]
    public void AFilenameGlob_IsRefusedRatherThanAppliedToTheImagePath() => WithPage(async page =>
    {
        var outcome = await page.SearchAsync(
            new SearchRequest("*.exe")
            {
                Terms = [new SearchTerm(SearchTermKind.Regex, [@"^.*\.exe$"], NameOnly: true, Display: "*.exe")],
            },
            display: false, default);

        Assert.IsTrue(outcome.Failed, "a glob is a file-browser constraint; this list has no filenames to judge");
        StringAssert.Contains(outcome.Message ?? "", "process list");
    });
}
