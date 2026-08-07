using System;
using System.Linq;
using System.Threading.Tasks;
using Nexaflow.Features.Common;
using Nexaflow.Features.Common.Search;
using Nexaflow.Features.SystemInfo.Models;
using Nexaflow.Features.SystemInfo.ViewModels;
using Nexaflow.Search;
using Nexaflow.Tests.Fixtures;
using NSubstitute;

namespace Nexaflow.Tests.Features.Search;

/// <summary>
/// The Environment Variables page answering <c>?</c> over a seeded name list.
/// <para>
/// What is worth pinning beyond the shared contract: "?" drives the filter box the page already had, over
/// the one field that box filters by — the <em>name</em>. A variable whose value carries the term is not a
/// hit, and that is the point: one box filtering by two different rules depending on who filled it in is
/// how a page comes to give two answers to one query.
/// </para>
/// </summary>
[TestClass]
[CoversNode("sysinfo-envvars-search")]
public class EnvVarsSearchableTests : SearchableContentConformanceTests
{
    protected override string LiteralTermInContent => "alpha42";
    protected override string RegexOnlyPattern     => @"alpha\d+";

    private const string ByName    = "alpha42_home";   // matches on the variable name
    private const string ValueOnly = "PATH";           // carries the term in its VALUE only
    private const string Quiet     = "TEMP";

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

    private static EnvironmentVariablesViewModel Build()
    {
        // The substitute never runs the queued gather, so this machine's real environment never reaches
        // the page and the three rows below are the whole list.
        var vm = new EnvironmentVariablesViewModel(RunningShell());
        vm.Variables.Add(new EnvVarRow(ByName,    @"C:\tools",              EnvScope.User));
        vm.Variables.Add(new EnvVarRow(ValueOnly, @"C:\alpha42\bin;C:\bin", EnvScope.User));
        vm.Variables.Add(new EnvVarRow(Quiet,     @"C:\temp",               EnvScope.User));
        return vm;
    }

    protected override Task<ISearchable> CreateAsync() => Task.FromResult<ISearchable>(Build());

    protected override string Snapshot(ISearchable page)
    {
        var vm = (EnvironmentVariablesViewModel)page;
        return $"{vm.IsSearchActive}|{vm.SearchMatchCount}|{vm.CurrentSearchTerm}|{vm.FilterText}|" +
               string.Join(",", Visible(vm));
    }

    private static string[] Visible(EnvironmentVariablesViewModel vm) =>
        vm.VariablesView.Cast<EnvVarRow>().Select(v => v.Name).ToArray();

    private static SearchRequest Query(string text) => SearchSyntax.ParseRequest(text);

    // ── Env-vars-specific behaviour beyond the shared contract ────────────────

    [TestMethod]
    public void AVariableMatchesOnItsName_AndNotOnItsValue() => WithPage(async page =>
    {
        var outcome = await page.SearchAsync(Query("alpha42"), display: false, default);

        CollectionAssert.AreEqual(new[] { ByName }, outcome.Hits.Select(h => h.Id).ToArray(),
            "the box this drives is the name list; 'which value mentions alpha42' is a different question, " +
            "and get_environment_variable is what answers it");
    });

    [TestMethod]
    public void TheHitPreviewCarriesTheValue_SoTheAgentNeedNotAskAgain() => WithPage(async page =>
    {
        var outcome = await page.SearchAsync(Query("alpha42"), display: false, default);

        StringAssert.Contains(outcome.Hits[0].Preview ?? "", @"C:\tools");
    });

    [TestMethod]
    public void DisplayingSearch_DrivesTheBoxThePageAlreadyHad() => WithPage(async page =>
    {
        var vm = (EnvironmentVariablesViewModel)page;

        await vm.SearchAsync(Query("alpha42"), display: true, default);

        CollectionAssert.AreEqual(new[] { ByName }, Visible(vm));
        Assert.AreEqual("alpha42", vm.FilterText,
            "the query is shown in the box it filtered by, so an AI-run search is visible and dismissible");
        Assert.AreEqual(1, vm.SearchMatchCount);
    });

    [TestMethod]
    public void TypingInTheBox_DropsThePatternBehindIt() => WithPage(async page =>
    {
        var vm = (EnvironmentVariablesViewModel)page;
        await vm.SearchAsync(new SearchRequest(@"alpha\d+", IsRegex: true), display: true, default);
        Assert.IsTrue(vm.IsSearchActive);

        vm.FilterText = "temp";

        Assert.IsFalse(vm.IsSearchActive, "the chip goes with it — the box says what is being filtered by");
        CollectionAssert.AreEqual(new[] { Quiet }, Visible(vm));
    });

    [TestMethod]
    public void SwitchingScope_DropsTheSearch_RatherThanDescribingRowsThatAreGone() => WithPage(async page =>
    {
        var vm = (EnvironmentVariablesViewModel)page;
        await vm.SearchAsync(Query("alpha42"), display: true, default);
        Assert.IsTrue(vm.IsSearchActive);

        vm.SelectedScope = EnvScope.Machine;

        Assert.IsFalse(vm.IsSearchActive, "the page shows one scope at a time");
        Assert.AreEqual(string.Empty, vm.FilterText);
    });

    [TestMethod]
    public void ClearSearch_EmptiesTheBox_AndPutsEveryVariableBack() => WithPage(async page =>
    {
        var vm = (EnvironmentVariablesViewModel)page;
        await vm.SearchAsync(Query("alpha42"), display: true, default);

        vm.ClearSearchCommand.Execute(null);

        Assert.IsFalse(vm.IsSearchActive);
        Assert.AreEqual(string.Empty, vm.FilterText);
        Assert.AreEqual(3, Visible(vm).Length);
    });

    [TestMethod]
    public void ShowResults_PinsTheListToTheChosenVariables() => WithPage(async page =>
    {
        var vm = (EnvironmentVariablesViewModel)page;

        var narrowed = await vm.ShowResultsAsync([new SearchHit(Quiet, Quiet)], default);

        Assert.IsTrue(narrowed);
        CollectionAssert.AreEqual(new[] { Quiet }, Visible(vm));
        Assert.AreEqual(1, vm.SearchMatchCount);
    });
}
