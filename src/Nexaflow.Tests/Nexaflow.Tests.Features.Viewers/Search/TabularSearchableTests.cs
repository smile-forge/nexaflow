using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Nexaflow.Features.Common;
using Nexaflow.Features.Common.Search;
using Nexaflow.Features.Tabular.Templates;
using Nexaflow.Features.Tabular.ViewModels;
using Nexaflow.Search;
using Nexaflow.Tests.Fixtures;
using NSubstitute;

namespace Nexaflow.Tests.Features.Search;

/// <summary>
/// The grid answering <c>?</c>. Beyond the shared conformance contract, these pin what makes a GRID
/// different from a document: a hit is a row (not a cell), the search composes with the typed per-column
/// filters instead of hijacking them, it reaches rows far outside the loaded 150-row window, and a scan
/// that stopped at the cap says so rather than reporting a floor as a total.
/// </summary>
[TestClass]
[CoversNode("tabular-search")]
public class TabularSearchableTests : SearchableContentConformanceTests
{
    protected override string LiteralTermInContent => "alpha42";
    protected override string RegexOnlyPattern => @"alpha\d+";

    private static IShellServices RunningShell()
    {
        var shell = Substitute.For<IShellServices>();
        shell.RunOnUiAsync(Arg.Any<Action>())
             .Returns(ci => { ci.Arg<Action>()(); return Task.CompletedTask; });
        shell.RunOnUiAsync(Arg.Any<Func<Task<bool>>>())
             .Returns(ci => ci.Arg<Func<Task<bool>>>()());
        return shell;
    }

    private static string WriteCsv(string content)
    {
        var dir = Path.Combine(Path.GetTempPath(), "tabsearch_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var csv = Path.Combine(dir, "people.csv");
        File.WriteAllText(csv, content);
        return csv;
    }

    // alpha42 appears in the name column (row 0) and the city column (row 2) — so "matches any cell"
    // is genuinely exercised. "alpha42x" is a longer word a whole-word search must not count.
    private const string Sample =
        "name,age,city\n" +
        "alpha42,30,London\n" +
        "Bob,25,Paris\n" +
        "Carol,41,alpha42\n" +
        "alpha42x,19,Madrid\n";

    private static TabularViewModel Build(string csv) =>
        new(csv, RunningShell(), Substitute.For<IAIService>(), new TabularTemplatesConfig());

    protected override async Task<ISearchable> CreateAsync()
    {
        var vm = Build(WriteCsv(Sample));
        await vm.Ready;
        return vm;
    }

    /// <summary>The ViewModel starts LoadAsync from its constructor and RowWindowReader uses Task.Run,
    /// so its continuations outlive a pumped block (TabularAiTests runs unpumped for the same reason).</summary>
    protected override void WithPage(Func<ISearchable, Task> body) => WithPageUnpumped(body);

    protected override string Snapshot(ISearchable page)
    {
        var vm = (TabularViewModel)page;
        return $"{vm.IsSearchActive}|{vm.SearchMatchCount}|{vm.CurrentSearchTerm}|{vm.FocalRow}|" +
               $"{string.Join(',', vm.Window.Select(r => r.AbsoluteIndex))}|" +
               $"{string.Join(',', vm.Window.Select(r => r.IsSearchHit))}|" +
               $"{string.Join(',', vm.Columns.Select(c => c.Filter.IsActive))}";
    }

    private static SearchRequest Query(string text) => SearchSyntax.ParseRequest(text);

    [TestMethod]
    public void AHitIsARow_WithTheMatchingColumnInThePreview() => WithPage(async page =>
    {
        var outcome = await page.SearchAsync(Query("alpha42"), display: false, default);

        // Rows 0 and 2 — the id is the absolute row index, which is what ShowResultsAsync takes back.
        CollectionAssert.AreEqual(new[] { "0", "2" }, outcome.Hits.Select(h => h.Id).ToArray());
        StringAssert.Contains(outcome.Hits[0].Label, "row 1");

        // The column isn't lost just because the hit is a row.
        StringAssert.StartsWith(outcome.Hits[0].Preview!, "name:");
        StringAssert.StartsWith(outcome.Hits[1].Preview!, "city:");
    });

    [TestMethod]
    public void AMatchInAnyColumn_Counts() => WithPage(async page =>
    {
        // Row 2 matches only in its last column; a per-column filter could never express this.
        var outcome = await page.SearchAsync(Query("alpha42"), display: false, default);
        Assert.AreEqual(2, outcome.MatchCount);
    });

    [TestMethod]
    public void ALiteralTerm_MeansTheWordItSpells() => WithPage(async page =>
    {
        // "alpha42x" is a longer word and must not count.
        var outcome = await page.SearchAsync(Query("alpha42"), display: false, default);
        Assert.IsFalse(outcome.Hits.Any(h => h.Id == "3"), "alpha42x is a different word");
    });

    [TestMethod]
    public void DisplayingSearch_LeavesTheColumnFiltersAlone() => WithPage(async page =>
    {
        var vm = (TabularViewModel)page;
        var nameFilter = (StringColumnFilter)vm.Columns[0].Filter;
        nameFilter.Text = "alpha";

        await vm.SearchAsync(Query("London"), display: true, default);

        Assert.AreEqual("alpha", nameFilter.Text, "a search must not rewrite the user's column filter");
        Assert.IsTrue(nameFilter.IsActive);
    });

    [TestMethod]
    public void AColumnFilterAndASearch_Compose() => WithPage(async page =>
    {
        var vm = (TabularViewModel)page;
        // Narrow to rows whose name contains "Carol" — row 2 only. alpha42 is in that row's city.
        ((StringColumnFilter)vm.Columns[0].Filter).Text = "Carol";

        var outcome = await vm.SearchAsync(Query("alpha42"), display: false, default);

        Assert.AreEqual(1, outcome.MatchCount, "the filtered-out row 0 must not surface as a hit");
        Assert.AreEqual("2", outcome.Hits[0].Id);
    });

    [TestMethod]
    public void DisplayingSearch_RevealsAndWashesTheFirstMatch() => WithPage(async page =>
    {
        var vm = (TabularViewModel)page;
        await vm.SearchAsync(Query("alpha42"), display: true, default);

        Assert.AreEqual(2, vm.SearchMatchCount);
        Assert.AreEqual(0, vm.FocalRow, "the view moves to the first match");
        Assert.IsTrue(vm.Window.Single(r => r.AbsoluteIndex == 0).IsSearchHit);
        Assert.IsTrue(vm.Window.Single(r => r.AbsoluteIndex == 2).IsSearchHit);
        Assert.IsFalse(vm.Window.Single(r => r.AbsoluteIndex == 1).IsSearchHit, "Bob matched nothing");
    });

    [TestMethod]
    public void FindNext_StepsBetweenMatchingRows() => WithPage(async page =>
    {
        var vm = (TabularViewModel)page;
        await vm.SearchAsync(Query("alpha42"), display: true, default);
        Assert.AreEqual(0, vm.FocalRow);

        await vm.FindNextMatchCommand.ExecuteAsync(null);
        Assert.AreEqual(2, vm.FocalRow);

        await vm.FindNextMatchCommand.ExecuteAsync(null);
        Assert.AreEqual(0, vm.FocalRow, "and wraps");
    });

    [TestMethod]
    public void ShowResults_PinsTheGridToTheChosenRows() => WithPage(async page =>
    {
        var vm = (TabularViewModel)page;
        var found = await vm.SearchAsync(Query("alpha42"), display: false, default);

        var pinned = await vm.ShowResultsAsync([found.Hits[1]], default);

        Assert.IsTrue(pinned);
        CollectionAssert.AreEqual(new[] { 2 }, vm.Window.Select(r => r.AbsoluteIndex).ToArray(),
            "the grid shows only the row the agent chose");
        Assert.AreEqual(1, vm.SearchMatchCount);
    });

    [TestMethod]
    public void ClearingAPinnedSearch_MakesTheWindowContiguousAgain() => WithPage(async page =>
    {
        var vm = (TabularViewModel)page;
        var found = await vm.SearchAsync(Query("alpha42"), display: false, default);
        await vm.ShowResultsAsync([found.Hits[1]], default);   // pins to row 2 alone
        CollectionAssert.AreEqual(new[] { 2 }, vm.Window.Select(r => r.AbsoluteIndex).ToArray());

        await vm.ClearSearchCommand.ExecuteAsync(null);

        Assert.IsFalse(vm.IsSearchActive);
        // Clearing un-pins; it does NOT scroll away from where the user was left. So the window is the
        // ordinary contiguous run from the focal row — row 3 is back, which the pin had excluded.
        Assert.AreEqual(2, vm.FocalRow);
        CollectionAssert.AreEqual(new[] { 2, 3 }, vm.Window.Select(r => r.AbsoluteIndex).ToArray());
        Assert.IsTrue(vm.Window.All(r => !r.IsSearchHit));
    });

    [TestMethod]
    public void SearchThatMatchesNothing_IsAResultNotAFailure() => WithPage(async page =>
    {
        var vm = (TabularViewModel)page;
        var outcome = await vm.SearchAsync(Query("nosuchvalue"), display: true, default);

        Assert.IsFalse(outcome.Failed);
        Assert.AreEqual(0, outcome.MatchCount);
        Assert.IsTrue(vm.IsSearchActive);
        Assert.IsFalse(vm.HasSearchMatches);
    });

    // ── Whole-file reach, past the 150-row window ─────────────────────────────

    [TestMethod]
    public void SearchCoversTheWholeFile_NotJustTheLoadedWindow() => RunUnpumped(async () =>
    {
        // >100 KB so RowWindowReader (the windowed reader) is chosen over SmallFileLoader, with the
        // only match ~2000 rows in — far past the 150 rows the grid has loaded.
        var sb = new StringBuilder("name,age,city\n");
        for (var i = 0; i < 4000; i++)
            sb.Append(i == 2000
                ? "alpha42,44,Reykjavik with plenty of padding to grow the file\n"
                : $"person{i},{i % 90},somewhere with plenty of padding to grow the file\n");

        var csv = WriteCsv(sb.ToString());
        var vm  = Build(csv);
        try
        {
            await vm.Ready;
            Assert.IsFalse(vm.IsSmallMode, "this file must take the windowed path for the test to mean anything");
            Assert.IsFalse(vm.Window.Any(r => r.AbsoluteIndex == 2000), "the match is outside the loaded window");

            var outcome = await vm.SearchAsync(Query("alpha42"), display: false, default);

            Assert.AreEqual(1, outcome.MatchCount);
            Assert.AreEqual("2000", outcome.Hits[0].Id);
        }
        finally { vm.Dispose(); Directory.Delete(Path.GetDirectoryName(csv)!, true); }
    });

    [TestMethod]
    public void AScanThatStoppedAtTheCap_SaysSo() => RunUnpumped(async () =>
    {
        // Every row matches, and there are more than the 5,000-row scan cap — so the count is a floor.
        var sb = new StringBuilder("name,age,city\n");
        for (var i = 0; i < 6000; i++) sb.Append($"alpha42,{i % 90},town number {i} padded out a bit\n");

        var csv = WriteCsv(sb.ToString());
        var vm  = Build(csv);
        try
        {
            await vm.Ready;
            var outcome = await vm.SearchAsync(Query("alpha42"), display: true, default);

            Assert.IsFalse(outcome.Failed);
            Assert.AreEqual(5000, outcome.MatchCount);
            Assert.IsNotNull(outcome.Message);
            StringAssert.Contains(outcome.Message!, "there may be more");
            Assert.IsTrue(vm.IsSearchTruncated);
            Assert.AreEqual("+", vm.SearchCountSuffix, "the chip must not show a floor as an exact total");
        }
        finally { vm.Dispose(); Directory.Delete(Path.GetDirectoryName(csv)!, true); }
    });

    [TestMethod]
    public void SmallAndWindowedModes_AgreeOnTheMatchCount() => RunUnpumped(async () =>
    {
        // The same content either side of the 100 KB small-file threshold must answer "?" identically.
        static string Body(int rows)
        {
            var sb = new StringBuilder("name,age,city\n");
            for (var i = 0; i < rows; i++)
                sb.Append(i % 500 == 0
                    ? "alpha42,44,Reykjavik with plenty of padding to grow this file\n"
                    : $"person{i},{i % 90},somewhere with plenty of padding to grow this file\n");
            return sb.ToString();
        }

        var smallCsv = WriteCsv(Body(100));    // well under 100 KB → SmallFileLoader
        var largeCsv = WriteCsv(Body(2000));   // well over            → RowWindowReader
        var small = Build(smallCsv);
        var large = Build(largeCsv);
        try
        {
            await small.Ready;
            await large.Ready;
            Assert.IsTrue(small.IsSmallMode);
            Assert.IsFalse(large.IsSmallMode);

            var inSmall = await small.SearchAsync(Query("alpha42"), display: false, default);
            var inLarge = await large.SearchAsync(Query("alpha42"), display: false, default);

            Assert.AreEqual(1, inSmall.MatchCount);   // row 0 only
            Assert.AreEqual(4, inLarge.MatchCount);   // rows 0, 500, 1000, 1500
        }
        finally
        {
            small.Dispose(); large.Dispose();
            Directory.Delete(Path.GetDirectoryName(smallCsv)!, true);
            Directory.Delete(Path.GetDirectoryName(largeCsv)!, true);
        }
    });
}
