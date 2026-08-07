using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Win32;
using Nexaflow.Features.Common;
using Nexaflow.Features.Common.Search;
using Nexaflow.Features.WindowsRegistry.ViewModels;
using Nexaflow.Search;
using Nexaflow.Tests.Fixtures;
using NSubstitute;

namespace Nexaflow.Tests.Features.Search;

/// <summary>
/// The registry browser answering <c>?</c>, against a private, per-test
/// <c>HKCU\Software\Nexaflow.Tests\{guid}</c> subtree — no elevation, no HKLM, and the subtree is deleted in
/// cleanup. The tab is rooted at that key, which is also what makes the test honest: rooting is the
/// mechanism the search scopes itself by, so the fixture exercises it rather than working around it.
/// <para>
/// What is worth pinning beyond the shared contract: a hit is a KEY (the only thing the tree can navigate
/// to) even when a value's data is what matched, and searching moves the tree rather than filtering it.
/// </para>
/// </summary>
[TestClass]
[CoversNode("win-registry-search")]
public class RegistrySearchableTests : SearchableContentConformanceTests
{
    protected override string LiteralTermInContent => "alpha42";
    protected override string RegexOnlyPattern     => @"alpha\d+";

    // Per-instance, not static: MSTest runs test methods in parallel on separate instances, and a shared
    // subtree path would have each test's Setup clobbering the one another test was mid-search over.
    private string _sub = "";

    // The seed term sits at a word boundary in every fixture name. A bare literal is the word it spells
    // everywhere in this app, so "alpha42Key" would NOT be found by "alpha42" — that is the search's rule,
    // not something for this fixture to work around by matching loosely.
    private const string KeyByName  = "alpha42-key";
    private const string KeyByValue = "Named";
    private const string KeyByData  = "Data";
    private const string ValueName  = "alpha42-flag";

    [TestInitialize]
    public void Setup()
    {
        _sub = $@"Software\Nexaflow.Tests\{Guid.NewGuid():N}";

        //  <root>
        //    ├─ alpha42-key   ← matches on the KEY NAME
        //    ├─ Named         ← matches on a VALUE NAME
        //    ├─ Data          ← matches on VALUE DATA
        //    └─ Quiet         ← matches nothing
        using var root = Registry.CurrentUser.CreateSubKey(_sub)!;
        root.CreateSubKey(KeyByName)!.Dispose();

        using (var named = root.CreateSubKey(KeyByValue)!)
            named.SetValue(ValueName, 1, RegistryValueKind.DWord);

        using (var data = root.CreateSubKey(KeyByData)!)
            data.SetValue("Version", "built from alpha42", RegistryValueKind.String);

        root.CreateSubKey("Quiet")!.Dispose();
    }

    [TestCleanup]
    public void Teardown()
    {
        try { Registry.CurrentUser.DeleteSubKeyTree(_sub, throwOnMissingSubKey: false); } catch { }
    }

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

    private string Base => $@"HKCU\{_sub}";

    private RegistryViewModel Build()
    {
        var vm = new RegistryViewModel(RunningShell());
        vm.RootAt(Base);       // the tab is rooted at the fixture subtree — search never leaves it
        return vm;
    }

    protected override Task<ISearchable> CreateAsync() => Task.FromResult<ISearchable>(Build());

    protected override string Snapshot(ISearchable page)
    {
        var vm = (RegistryViewModel)page;
        return $"{vm.IsSearchActive}|{vm.SearchMatchCount}|{vm.CurrentSearchTerm}|" +
               $"{vm.CurrentKeyPath}|{vm.SelectedValue?.RawName}";
    }

    private static SearchRequest Query(string text) => SearchSyntax.ParseRequest(text);

    private static string Leaf(string path) => path[(path.LastIndexOf('\\') + 1)..];

    private static string[] Leaves(SearchOutcome outcome) =>
        outcome.Hits.Select(h => Leaf(h.Id)).OrderBy(s => s, StringComparer.Ordinal).ToArray();

    // ── Registry-specific behaviour beyond the shared contract ────────────────

    [TestMethod]
    public void AKeyMatchesOnItsName_OnAValueName_AndOnValueData() => WithPage(async page =>
    {
        var outcome = await page.SearchAsync(Query("alpha42"), display: false, default);

        CollectionAssert.AreEqual(new[] { KeyByData, KeyByValue, KeyByName }, Leaves(outcome),
            "all three routes into a key count, and the key that carries the term nowhere does not");
    });

    [TestMethod]
    public void TheHitPreviewNamesWhatActuallyMatched() => WithPage(async page =>
    {
        var outcome = await page.SearchAsync(Query("alpha42"), display: false, default);

        var byLeaf = outcome.Hits.ToDictionary(h => Leaf(h.Id), h => h.Preview ?? "");
        StringAssert.Contains(byLeaf[KeyByName],  "key name");
        StringAssert.Contains(byLeaf[KeyByValue], $"value name '{ValueName}'");
        StringAssert.Contains(byLeaf[KeyByData],  "built from alpha42");
    });

    [TestMethod]
    public void HitIdsAreFullPaths_SoTheyNavigate() => WithPage(async page =>
    {
        var outcome = await page.SearchAsync(Query("alpha42"), display: false, default);

        Assert.IsTrue(outcome.Hits.All(h => h.Id.StartsWith(Base + @"\", StringComparison.OrdinalIgnoreCase)),
            "every id is an absolute path under the tab's root");
    });

    [TestMethod]
    public void DisplayingSearch_NavigatesToTheFirstHit_AndSelectsTheValueThatMatched() => WithPage(async page =>
    {
        var vm = (RegistryViewModel)page;

        // Narrowed to the one key whose DATA matched, so the value selection is unambiguous.
        await vm.SearchAsync(Query("built"), display: true, default);

        Assert.AreEqual(1, vm.SearchMatchCount);
        Assert.AreEqual($@"{Base}\{KeyByData}", vm.CurrentKeyPath, "the tree moved to the hit");
        Assert.AreEqual("Version", vm.SelectedValue?.RawName,
            "…and to the value row that matched, not just the key holding it");
    });

    [TestMethod]
    public void FindNext_WalksEveryHit_AndWraps() => WithPage(async page =>
    {
        var vm = (RegistryViewModel)page;
        await vm.SearchAsync(Query("alpha42"), display: true, default);
        Assert.AreEqual(3, vm.SearchMatchCount);

        var visited = new string[4];
        visited[0] = vm.CurrentKeyPath;
        for (int i = 1; i < 4; i++)
        {
            vm.FindNextMatchCommand.Execute(null);
            visited[i] = vm.CurrentKeyPath;
        }

        CollectionAssert.AreEquivalent(
            new[] { KeyByName, KeyByValue, KeyByData },
            visited.Take(3).Select(Leaf).ToArray(),
            "stepping reaches each of the three hits exactly once");
        Assert.AreEqual(visited[0], visited[3], "then wraps back to the first");
    });

    [TestMethod]
    public void SearchIsScopedToTheTabsRoot_NotTheWholeHive() => WithPage(async page =>
    {
        // "Software" is all over HKCU; under this tab's root it appears nowhere.
        var outcome = await page.SearchAsync(Query("Software"), display: false, default);

        Assert.AreEqual(0, outcome.MatchCount,
            "a rooted tab searches its own subtree — otherwise pinning a key would silently widen the search");
        StringAssert.Contains(page.SearchTargetDescription, _sub);
    });

    [TestMethod]
    public void ShowResults_NarrowsTheWalkToTheChosenKeys() => WithPage(async page =>
    {
        var vm = (RegistryViewModel)page;
        var found = await vm.SearchAsync(Query("alpha42"), display: false, default);
        var chosen = found.Hits.Single(h => Leaf(h.Id) == KeyByData);

        var narrowed = await vm.ShowResultsAsync([chosen], default);

        Assert.IsTrue(narrowed);
        Assert.AreEqual(1, vm.SearchMatchCount);
        Assert.AreEqual($@"{Base}\{KeyByData}", vm.CurrentKeyPath);
    });

    [TestMethod]
    public void ClearSearch_DropsTheChip_ButLeavesTheUserWhereTheyLanded() => WithPage(async page =>
    {
        var vm = (RegistryViewModel)page;
        await vm.SearchAsync(Query("built"), display: true, default);
        var landed = vm.CurrentKeyPath;

        vm.ClearSearchCommand.Execute(null);

        Assert.IsFalse(vm.IsSearchActive);
        Assert.AreEqual(0, vm.SearchMatchCount);
        Assert.AreEqual(string.Empty, vm.CurrentSearchTerm);
        Assert.AreEqual(landed, vm.CurrentKeyPath,
            "dismissing the search is not an undo — the user navigated here");
    });

    [TestMethod]
    public void ACancelledScan_Throws_RatherThanReportingAPartialSetAsComplete() => WithPage(async page =>
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsExactlyAsync<TaskCanceledException>(
            () => page.SearchAsync(Query("alpha42"), display: false, cts.Token));
    });
}
