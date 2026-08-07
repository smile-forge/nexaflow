using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Nexaflow.Features.Common;
using Nexaflow.Features.Common.Search;
using Nexaflow.Features.Json.Models;
using Nexaflow.Features.Json.Services;
using Nexaflow.Features.Json.ViewModels;
using Nexaflow.Search;
using Nexaflow.Tests.Fixtures;
using NSubstitute;

namespace Nexaflow.Tests.Features.Search;

/// <summary>
/// The JSON tab answering <c>?</c>. Beyond the shared conformance contract, these pin the two decisions
/// that make JSON the hard case: a hit is the TOP-LEVEL item whose subtree contains the match (the one
/// identity that survives the display window evicting a node), and the search streams the whole FILE
/// rather than the few hundred realised nodes — which only means anything if a match outside the window
/// can also be revealed.
/// </summary>
[TestClass]
[CoversNode("json-search")]
public class JsonSearchableTests : SearchableContentConformanceTests
{
    protected override string LiteralTermInContent => "alpha42";
    protected override string RegexOnlyPattern => @"alpha\d+";

    // "a" and "c" match ("c" only deep inside its subtree, so a nested hit reports on its depth-1
    // ancestor). "d" holds alpha42x — a longer word a whole-word search must not count.
    private const string Doc = """
        {"a":"alpha42 one","b":"nothing here","c":{"nested":{"deep":"alpha42 two"}},"d":"alpha42x"}
        """;

    private static readonly List<string> s_temp = [];

    [ClassCleanup]
    public static void Cleanup()
    {
        foreach (var p in s_temp) { try { File.Delete(p); } catch { } }
    }

    private static string Write(string content)
    {
        var path = Path.Combine(Path.GetTempPath(), $"jsonsearch_{Guid.NewGuid():N}.json");
        File.WriteAllText(path, content, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        lock (s_temp) s_temp.Add(path);
        return path;
    }

    private static IShellServices RunningShell()
    {
        var shell = Substitute.For<IShellServices>();
        shell.RunOnUiAsync(Arg.Any<Action>())
             .Returns(ci => { ci.Arg<Action>()(); return Task.CompletedTask; });
        shell.RunOnUiAsync(Arg.Any<Func<Task<bool>>>())
             .Returns(ci => ci.Arg<Func<Task<bool>>>()());
        return shell;
    }

    private static async Task<JsonViewModel> LoadAsync(string path)
    {
        var vm = new JsonViewModel(path, new JsonFileLoader(), new JsonPathEvaluator(), RunningShell());
        await vm.LoadAsync(CancellationToken.None);
        return vm;
    }

    protected override async Task<ISearchable> CreateAsync() => await LoadAsync(Write(Doc));

    /// <summary>The streamed path does real async file IO whose continuations outlive a pumped block.</summary>
    protected override void WithPage(Func<ISearchable, Task> body) => WithPageUnpumped(body);

    protected override string Snapshot(ISearchable page)
    {
        var vm = (JsonViewModel)page;
        return $"{vm.IsSearchActive}|{vm.SearchMatchCount}|{vm.CurrentSearchTerm}|" +
               $"{(vm.SelectedDisplayItem as JsonTreeDisplayItem)?.KeyLabel}|" +
               $"{vm.DisplayItems.Count(i => i.IsSearchHit)}";
    }

    private static SearchRequest Query(string text) => SearchSyntax.ParseRequest(text);

    /// <summary>The selected row's key label. KeyLabel carries its own trailing ": " separator.</summary>
    private static string Key(JsonViewModel vm) =>
        (vm.SelectedDisplayItem as JsonTreeDisplayItem)?.KeyLabel ?? "(nothing selected)";

    [TestMethod]
    public void AHitIsATopLevelItem_EvenWhenTheMatchIsNested() => WithPage(async page =>
    {
        var outcome = await page.SearchAsync(Query("alpha42"), display: false, default);

        // "a" (index 0) matches directly; "c" (index 2) matches three levels down. Both report as the
        // depth-1 item, because that is the only thing the windowed loader can address and reveal.
        CollectionAssert.AreEqual(new[] { "0", "2" }, outcome.Hits.Select(h => h.Id).ToArray());
        Assert.AreEqual("\"a\"", outcome.Hits[0].Label);
        Assert.AreEqual("\"c\"", outcome.Hits[1].Label);
        StringAssert.Contains(outcome.Hits[1].Preview!, "deep");
    });

    [TestMethod]
    public void ALiteralTerm_MeansTheWordItSpells() => WithPage(async page =>
    {
        // "d" holds alpha42x — a different word.
        var outcome = await page.SearchAsync(Query("alpha42"), display: false, default);
        Assert.IsFalse(outcome.Hits.Any(h => h.Id == "3"));
    });

    [TestMethod]
    public void KeysAreSearchedAsWellAsValues() => WithPage(async page =>
    {
        // "nested" is a key inside item "c", never a value.
        var outcome = await page.SearchAsync(Query("nested"), display: false, default);
        Assert.AreEqual(1, outcome.MatchCount);
        Assert.AreEqual("2", outcome.Hits[0].Id);
    });

    [TestMethod]
    public void DisplayingSearch_MarksAndSelectsTheFirstHit() => WithPage(async page =>
    {
        var vm = (JsonViewModel)page;
        await vm.SearchAsync(Query("alpha42"), display: true, default);

        Assert.AreEqual(2, vm.SearchMatchCount);
        Assert.AreEqual(2, vm.DisplayItems.Count(i => i.IsSearchHit), "both matching items wash");
        StringAssert.StartsWith(Key(vm), "\"a\"");
    });

    [TestMethod]
    public void FindNext_StepsBetweenMatchingItems() => WithPage(async page =>
    {
        var vm = (JsonViewModel)page;
        await vm.SearchAsync(Query("alpha42"), display: true, default);
        StringAssert.StartsWith(Key(vm), "\"a\"");

        await vm.FindNextMatchCommand.ExecuteAsync(null);
        StringAssert.StartsWith(Key(vm), "\"c\"");

        await vm.FindNextMatchCommand.ExecuteAsync(null);
        StringAssert.StartsWith(Key(vm), "\"a\"", "and wraps");
    });

    [TestMethod]
    public void ClearSearch_DropsEveryMark() => WithPage(async page =>
    {
        var vm = (JsonViewModel)page;
        await vm.SearchAsync(Query("alpha42"), display: true, default);
        Assert.IsTrue(vm.IsSearchActive);

        vm.ClearSearchCommand.Execute(null);

        Assert.IsFalse(vm.IsSearchActive);
        Assert.AreEqual(0, vm.SearchMatchCount);
        Assert.AreEqual(0, vm.DisplayItems.Count(i => i.IsSearchHit));
    });

    [TestMethod]
    public void SearchThatMatchesNothing_IsAResultNotAFailure() => WithPage(async page =>
    {
        var vm = (JsonViewModel)page;
        var outcome = await vm.SearchAsync(Query("nosuchword"), display: true, default);

        Assert.IsFalse(outcome.Failed);
        Assert.AreEqual(0, outcome.MatchCount);
        Assert.IsTrue(vm.IsSearchActive);
        Assert.IsFalse(vm.HasSearchMatches);
    });

    [TestMethod]
    public void ShowResults_NarrowsToTheChosenItems() => WithPage(async page =>
    {
        var vm = (JsonViewModel)page;
        var found = await vm.SearchAsync(Query("alpha42"), display: false, default);

        var shown = await vm.ShowResultsAsync([found.Hits[1]], default);

        Assert.IsTrue(shown);
        Assert.AreEqual(1, vm.SearchMatchCount);
        Assert.AreEqual(1, vm.DisplayItems.Count(i => i.IsSearchHit));
        StringAssert.StartsWith(Key(vm), "\"c\"");
    });

    // ── The windowed file: the whole point of the streamed scan ───────────────

    /// <summary>&gt;1 MB so the viewer takes the windowed path, with the seeded term in an element far
    /// past the few hundred nodes the window ever holds.</summary>
    private static string WriteLargeArray(int matchAt = 4000, int count = 8000)
    {
        var sb = new StringBuilder("[\n");
        for (var i = 0; i < count; i++)
        {
            sb.Append(i == matchAt
                ? "{\"id\":" + i + ",\"note\":\"alpha42 is buried right here\",\"pad\":\"" + new string('x', 120) + "\"}"
                : "{\"id\":" + i + ",\"note\":\"ordinary entry number " + i + "\",\"pad\":\"" + new string('x', 120) + "\"}");
            if (i < count - 1) sb.Append(',');
            sb.Append('\n');
        }
        sb.Append(']');
        return Write(sb.ToString());
    }

    [TestMethod]
    public void LargeFile_SearchesPastTheLoadedWindow_AndCanRevealTheHit() => RunUnpumped(async () =>
    {
        var vm = await LoadAsync(WriteLargeArray());
        try
        {
            Assert.IsTrue(vm.IsLargeFile, "this file must take the windowed path for the test to mean anything");
            Assert.IsTrue(vm.DisplayItems.Count(i => i is JsonTreeDisplayItem) < 1000,
                "only a fraction of the file is realised");

            var outcome = await vm.SearchAsync(Query("alpha42"), display: true, default);

            Assert.AreEqual(1, outcome.MatchCount, "the scan reached element 4000, far outside the window");
            Assert.AreEqual("4000", outcome.Hits[0].Id);

            // Counting a match it cannot SHOW would be the dishonest version of this feature. Marking is
            // not enough to prove that — a virtual placeholder row carries the wash just as happily — so
            // assert the item was actually realised and selected.
            var selected = vm.SelectedDisplayItem;
            Assert.IsNotNull(selected, "the matching item must be revealed, not just counted");
            Assert.IsInstanceOfType<JsonTreeDisplayItem>(selected,
                "a placeholder row means the batch was never loaded");
            Assert.AreEqual(4000, selected.Node.Index, "…and it must be the item that actually matched");
        }
        finally { vm.Dispose(); }
    });

    [TestMethod]
    public void LargeFile_ARefusalIsAboutSize_AndNamesTheWayRound() => RunUnpumped(async () =>
    {
        // Nothing here exceeds MaxScanBytes, so this file must NOT be refused — the guard is about size
        // alone and must not become a blanket "large files can't be searched".
        var vm = await LoadAsync(WriteLargeArray());
        try
        {
            var outcome = await vm.SearchAsync(Query("alpha42"), display: false, default);
            Assert.IsFalse(outcome.Failed, outcome.Message);
        }
        finally { vm.Dispose(); }
    });

    [TestMethod]
    public void LargeFile_SearchHonoursCancellation() => RunUnpumped(async () =>
    {
        var vm = await LoadAsync(WriteLargeArray());
        try
        {
            using var cts = new CancellationTokenSource();
            cts.Cancel();

            var before = ((ISearchable)vm).SearchTargetDescription;
            await Assert.ThrowsExactlyAsync<OperationCanceledException>(
                async () => await vm.SearchAsync(Query("alpha42"), display: true, cts.Token));

            Assert.IsFalse(vm.IsSearchActive, "a cancelled search must not half-apply to the view");
            Assert.AreEqual(before, ((ISearchable)vm).SearchTargetDescription);
        }
        finally { vm.Dispose(); }
    });

    [TestMethod]
    public void JsonPathHandler_StillClaimsADollarQuery() => WithPage(page =>
    {
        // "?foo" and "$.a" answer different questions — text search vs structural address — so the
        // JSONPath handler keeps its symbol rather than being folded into page search.
        var handler = new JsonPathQueryHandler();
        Assert.IsTrue(handler.CanProcess("$.a", prefixed: false, (JsonViewModel)page) > 0f);
        return Task.CompletedTask;
    });
}
