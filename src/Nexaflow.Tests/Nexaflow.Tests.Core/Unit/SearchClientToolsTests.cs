using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Nexaflow.Core.Services;
using Nexaflow.Features.Common.ClientTools;
using Nexaflow.Features.Common.Search;
using Nexaflow.Search;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.Core.Unit;

/// <summary>
/// The agent-side half of <see cref="ISearchable"/>. The two tools are deliberately separate: reading
/// results must not disturb the user's view, and showing a filtered subset is its own explicit act.
/// </summary>
[TestClass]
[CoversNode("ai-intent-symbol")]
public class SearchClientToolsTests
{
    private sealed class Page : ISearchable
    {
        public readonly List<(SearchRequest Request, bool Display)> Calls = [];
        public IReadOnlyList<SearchHit>? Shown;
        public bool CanShow = true;
        public SearchOutcome Result = SearchOutcome.Found(
        [
            new SearchHit("1", "line 2", "alpha42 here"),
            new SearchHit("7", "line 8", "alpha43 there"),
        ]);

        public string SearchTargetDescription => "the fake page";

        public Task<SearchOutcome> SearchAsync(SearchRequest request, bool display, CancellationToken ct)
        {
            Calls.Add((request, display));
            return Task.FromResult(Result);
        }

        public Task<bool> ShowResultsAsync(IReadOnlyList<SearchHit> hits, CancellationToken ct)
        {
            if (!CanShow) return Task.FromResult(false);
            Shown = hits;
            return Task.FromResult(true);
        }
    }

    private static IClientTool Tool(Page page, string name)
        => new SearchClientTools(page).Tools.First(t => t.Name == name);

    private static (SearchClientTools Tools, IClientTool Search, IClientTool Show) Pair(Page page)
    {
        var tools = new SearchClientTools(page);
        return (tools, tools.Tools.First(t => t.Name == "search_page"), tools.Tools.First(t => t.Name == "show_search_results"));
    }

    [TestMethod]
    public void BothToolsAreOffered()
    {
        var names = new SearchClientTools(new Page()).Tools.Select(t => t.Name).ToList();

        CollectionAssert.AreEquivalent(new[] { "search_page", "show_search_results" }, names);
    }

    [TestMethod]
    public void SearchToolDescription_NamesWhatIsSearched()
        => StringAssert.Contains(Tool(new Page(), "search_page").Description, "the fake page");

    [TestMethod]
    public void SearchToolDescription_AlwaysOffersRegex()
    {
        // Regex is universal across searchable pages, so the model is never told to avoid it — a
        // capability that varied page to page would be worse than none.
        StringAssert.Contains(Tool(new Page(), "search_page").Description, "regular expression");
    }

    [TestMethod]
    public async Task SearchTool_NeverDisplays()
    {
        var page = new Page();

        await Tool(page, "search_page").InvokeAsync(new JsonObject { ["query"] = "alpha" }, default);

        Assert.AreEqual(1, page.Calls.Count);
        Assert.IsFalse(page.Calls[0].Display,
            "the agent reading results must not change what the user is looking at");
    }

    [TestMethod]
    public async Task SearchTool_PassesRegexAndCaseThrough()
    {
        var page = new Page();

        await Tool(page, "search_page").InvokeAsync(
            new JsonObject { ["query"] = @"alpha\d+", ["regex"] = true, ["match_case"] = true }, default);

        Assert.IsTrue(page.Calls[0].Request.IsRegex);
        Assert.IsTrue(page.Calls[0].Request.MatchCase);
    }

    [TestMethod]
    public async Task SearchTool_ReturnsIdsTheModelCanReuse()
    {
        var result = await Tool(new Page(), "search_page")
            .InvokeAsync(new JsonObject { ["query"] = "alpha" }, default);

        Assert.IsTrue(result.Success);
        StringAssert.Contains(result.ModelText, "id=1");
        StringAssert.Contains(result.ModelText, "alpha42 here");
    }

    [TestMethod]
    public async Task SearchTool_ReportsAFailureAsAnError()
    {
        var page = new Page { Result = SearchOutcome.Unsupported("no regex here") };

        var result = await Tool(page, "search_page").InvokeAsync(new JsonObject { ["query"] = "x" }, default);

        Assert.IsTrue(result.IsError, "an unsupported search must not read to the model as 'no matches'");
        StringAssert.Contains(result.ModelText, "no regex here");
    }

    [TestMethod]
    public async Task ShowTool_NarrowsToTheChosenSubset()
    {
        var page = new Page();
        var (_, search, show) = Pair(page);

        await search.InvokeAsync(new JsonObject { ["query"] = "alpha" }, default);
        var result = await show.InvokeAsync(new JsonObject { ["ids"] = new JsonArray("7") }, default);

        Assert.IsTrue(result.Success);
        Assert.AreEqual(1, page.Shown!.Count);
        Assert.AreEqual("7", page.Shown[0].Id);
        Assert.AreEqual("line 8", page.Shown[0].Label, "the page gets its own hit back, not a model-authored one");
    }

    [TestMethod]
    public async Task ShowTool_AcceptsACommaSeparatedList()
    {
        var page = new Page();
        var (_, search, show) = Pair(page);

        await search.InvokeAsync(new JsonObject { ["query"] = "alpha" }, default);
        await show.InvokeAsync(new JsonObject { ["ids"] = "1, 7" }, default);

        Assert.AreEqual(2, page.Shown!.Count);
    }

    [TestMethod]
    public async Task ShowTool_RejectsIdsThatDidNotComeFromASearch()
    {
        var page = new Page();
        var (_, search, show) = Pair(page);

        await search.InvokeAsync(new JsonObject { ["query"] = "alpha" }, default);
        var result = await show.InvokeAsync(new JsonObject { ["ids"] = new JsonArray("999") }, default);

        Assert.IsTrue(result.IsError);
        Assert.IsNull(page.Shown, "an invented id must not reach the page");
    }

    [TestMethod]
    public async Task ShowTool_TellsTheModelWhenThePageCannotFilter()
    {
        var page = new Page { CanShow = false };
        var (_, search, show) = Pair(page);

        await search.InvokeAsync(new JsonObject { ["query"] = "alpha" }, default);
        var result = await show.InvokeAsync(new JsonObject { ["ids"] = new JsonArray("1") }, default);

        // Not an error — the model should describe the matches instead of retrying.
        Assert.IsFalse(result.IsError);
        StringAssert.Contains(result.ModelText, "cannot display");
    }

    [TestMethod]
    public async Task BothTools_AreSafeToAutoRun()
    {
        var page = new Page();
        var (_, search, show) = Pair(page);

        Assert.AreEqual(ToolSafety.SafeOperation, search.Safety);
        Assert.AreEqual(ToolSafety.SafeOperation, show.Safety);
        await Task.CompletedTask;
    }
}
