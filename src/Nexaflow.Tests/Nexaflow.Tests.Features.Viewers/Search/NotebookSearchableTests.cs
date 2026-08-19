using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Nexaflow.Features.Common;
using Nexaflow.Features.Common.Search;
using Nexaflow.Features.Notebook.ViewModels;
using Nexaflow.Search;
using Nexaflow.Tests.Fixtures;
using NSubstitute;

namespace Nexaflow.Tests.Features.Search;

/// <summary>
/// A notebook answering <c>?</c> over a real <c>.ipynb</c>.
/// <para>
/// What is worth pinning beyond the shared contract: markdown and code cells are both searched, a hit is a
/// <em>cell</em> (the only thing the page can scroll to, and the id <c>read_cell</c> takes), nothing is
/// filtered — a notebook is read in order — and a code cell carries the matched spans so the words
/// themselves can be painted.
/// </para>
/// </summary>
[TestClass]
[CoversNode("notebook-search")]
public class NotebookSearchableTests : SearchableContentConformanceTests
{
    protected override string LiteralTermInContent => "alpha42";
    protected override string RegexOnlyPattern     => @"alpha\d+";

    // Per-instance: MSTest runs test methods in parallel on separate instances.
    private string _dir = "";
    private string _path = "";

    //  1  markdown : "# alpha42 notes"                    ← hit, on a markdown cell
    //  2  code     : "x = 1"                              ← no hit
    //  3  code     : "load(alpha42)\nprint(alpha42)"      ← hit, twice, on a code cell
    private const string Notebook = """
        {
          "metadata": { "kernelspec": { "language": "python" } },
          "cells": [
            { "cell_type": "markdown", "source": ["# alpha42 notes"] },
            { "cell_type": "code", "execution_count": 1, "source": ["x = 1"] },
            { "cell_type": "code", "execution_count": 2, "source": ["load(alpha42)\n", "print(alpha42)"] }
          ]
        }
        """;

    [TestInitialize]
    public void Setup()
    {
        _dir = Path.Combine(Path.GetTempPath(), "nexa-nbsearch-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
        _path = Path.Combine(_dir, "book.ipynb");
        File.WriteAllText(_path, Notebook);
    }

    [TestCleanup]
    public void Teardown()
    {
        try { Directory.Delete(_dir, recursive: true); } catch { }
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

    private async Task<NotebookViewModel> BuildAsync()
    {
        var vm = new NotebookViewModel(_path, RunningShell());
        await vm.LoadAsync();
        return vm;
    }

    protected override async Task<ISearchable> CreateAsync() => await BuildAsync();

    protected override string Snapshot(ISearchable page)
    {
        var vm = (NotebookViewModel)page;
        return $"{vm.IsSearchActive}|{vm.SearchMatchCount}|{vm.CurrentSearchTerm}|{vm.ScrollToCellIndex}|" +
               string.Join(",", vm.Cells.Select((c, i) => c.IsSearchHit ? i.ToString() : "").Where(s => s.Length > 0));
    }

    private static SearchRequest Query(string text) => SearchSyntax.ParseRequest(text);

    // ── Notebook-specific behaviour beyond the shared contract ────────────────

    [TestMethod]
    public void BothMarkdownAndCodeCellsAreSearched() => WithPage(async page =>
    {
        var outcome = await page.SearchAsync(Query("alpha42"), display: false, default);

        CollectionAssert.AreEqual(new[] { "1", "3" }, outcome.Hits.Select(h => h.Id).ToArray(),
            "prose and source are both what a notebook says; the ids are 1-based, as read_cell takes them");
        StringAssert.Contains(outcome.Hits[0].Label, "markdown");
        StringAssert.Contains(outcome.Hits[1].Label, "code");
    });

    [TestMethod]
    public void AHitIsACell_HoweverManyTimesItMatchesInside() => WithPage(async page =>
    {
        var outcome = await page.SearchAsync(Query("alpha42"), display: false, default);

        Assert.AreEqual(2, outcome.MatchCount,
            "the third cell says alpha42 twice — the cell is the unit, because the cell is what the page " +
            "can scroll to");
    });

    [TestMethod]
    public void TheHitPreviewIsTheLineTheMatchSitsOn() => WithPage(async page =>
    {
        var outcome = await page.SearchAsync(Query("print"), display: false, default);

        Assert.AreEqual("print(alpha42)", outcome.Hits.Single().Preview,
            "the matched line, not the cell's first line — otherwise every hit in a long cell reads the same");
    });

    [TestMethod]
    public void DisplayingSearch_MarksTheCells_AndKeepsEveryOtherCellOnThePage() => WithPage(async page =>
    {
        var vm = (NotebookViewModel)page;

        await vm.SearchAsync(Query("alpha42"), display: true, default);

        Assert.AreEqual(3, vm.Cells.Count, "a notebook is read in order — cells build on the ones above them");
        CollectionAssert.AreEqual(new[] { true, false, true }, vm.Cells.Select(c => c.IsSearchHit).ToArray());
        Assert.AreEqual(0, vm.ScrollToCellIndex, "and the page moves to the first hit");
    });

    [TestMethod]
    public void ACodeCellCarriesTheSpansSoTheWordsThemselvesArePainted() => WithPage(async page =>
    {
        var vm = (NotebookViewModel)page;

        await vm.SearchAsync(Query("alpha42"), display: true, default);

        var code = vm.Cells[2];
        Assert.AreEqual(2, code.SearchSpans.Count, "both occurrences in the cell's source");
        foreach (var (offset, length) in code.SearchSpans)
            Assert.AreEqual("alpha42", code.Source.Substring(offset, length),
                "a span that does not sit on the word would paint the wrong characters");

        Assert.AreEqual(0, vm.Cells[1].SearchSpans.Count, "a cell that missed carries nothing");
    });

    [TestMethod]
    public void FindNext_WalksEveryHitCell_AndWraps() => WithPage(async page =>
    {
        var vm = (NotebookViewModel)page;
        await vm.SearchAsync(Query("alpha42"), display: true, default);

        vm.FindNextMatchCommand.Execute(null);
        Assert.AreEqual(2, vm.ScrollToCellIndex);

        vm.FindNextMatchCommand.Execute(null);
        Assert.AreEqual(0, vm.ScrollToCellIndex, "then wraps back to the first");
    });

    [TestMethod]
    public void ClearSearch_DropsEveryMarkAndSpan() => WithPage(async page =>
    {
        var vm = (NotebookViewModel)page;
        await vm.SearchAsync(Query("alpha42"), display: true, default);

        vm.ClearSearchCommand.Execute(null);

        Assert.IsFalse(vm.IsSearchActive);
        Assert.AreEqual(0, vm.SearchMatchCount);
        Assert.IsFalse(vm.Cells.Any(c => c.IsSearchHit));
        Assert.IsFalse(vm.Cells.Any(c => c.SearchSpans.Count > 0));
        Assert.AreEqual(-1, vm.ScrollToCellIndex);
    });

    [TestMethod]
    public void ShowResults_MarksOnlyTheCellsTheAgentChose() => WithPage(async page =>
    {
        var vm = (NotebookViewModel)page;
        var found = await vm.SearchAsync(Query("alpha42"), display: false, default);
        var chosen = found.Hits.Single(h => h.Id == "3");

        var marked = await vm.ShowResultsAsync([chosen], default);

        Assert.IsTrue(marked);
        CollectionAssert.AreEqual(new[] { false, false, true }, vm.Cells.Select(c => c.IsSearchHit).ToArray());
        Assert.AreEqual(2, vm.Cells[2].SearchSpans.Count, "…and it is still painted where it matched");
    });

    [TestMethod]
    public void ShowResults_WithCellsThisNotebookDoesNotHave_Declines() => WithPage(async page =>
    {
        var vm = (NotebookViewModel)page;

        var marked = await vm.ShowResultsAsync([new SearchHit("99", "Cell 99")], default);

        Assert.IsFalse(marked, "the agent needs to know it must describe the matches instead");
        Assert.IsFalse(vm.Cells.Any(c => c.IsSearchHit));
    });
}
