using System.Text.Json.Nodes;
using NSubstitute;
using Nexaflow.Features.Common;
using Nexaflow.Features.Common.ClientTools;
using Nexaflow.Features.WindowsSearch;
using Nexaflow.Features.WindowsSearch.ViewModels;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.Features.WindowsSearch;

[TestClass]
[CoversNode("search-ai-tool")]
public class SearchViewModelTests
{
    private static IShellServices Shell() => Substitute.For<IShellServices>();

    // ── Construction ──────────────────────────────────────────────────────────

    [TestMethod]
    public void Constructor_SetsQueryAndRoot()
    {
        var vm = new SearchViewModel("hello", @"C:\", [], Shell());

        Assert.AreEqual("hello", vm.SearchQuery);
        Assert.AreEqual(@"C:\", vm.SearchRoot);
    }

    [TestMethod]
    public void Constructor_ResultsEmpty()
    {
        var vm = new SearchViewModel("hello", @"C:\", [], Shell());

        Assert.AreEqual(0, vm.Results.Count);
    }

    // ── RunSearchAsync – fast paths (no I/O) ─────────────────────────────────

    [TestMethod]
    public async Task RunSearchAsync_EmptyQuery_SetsStatusText()
    {
        var vm = new SearchViewModel("", @"C:\", [], Shell());

        await vm.RunSearchAsync(CancellationToken.None);

        Assert.AreEqual("Enter a search term.", vm.StatusText);
    }

    [TestMethod]
    public async Task RunSearchAsync_WhitespaceQuery_SetsStatusText()
    {
        var vm = new SearchViewModel("   ", @"C:\", [], Shell());

        await vm.RunSearchAsync(CancellationToken.None);

        Assert.AreEqual("Enter a search term.", vm.StatusText);
    }

    [TestMethod]
    public async Task RunSearchAsync_EmptyRoot_SetsStatusText()
    {
        var vm = new SearchViewModel("report", "", [], Shell());

        await vm.RunSearchAsync(CancellationToken.None);

        Assert.AreEqual("Enter a search term.", vm.StatusText);
    }

    [TestMethod]
    public async Task RunSearchAsync_EmptyQuery_IsSearchingReturnsFalse()
    {
        var vm = new SearchViewModel("", @"C:\", [], Shell());

        await vm.RunSearchAsync(CancellationToken.None);

        Assert.IsFalse(vm.IsSearching);
    }

    // ── IPageViewModel ────────────────────────────────────────────────────────

    [TestMethod]
    public void GetContext_BeforeSearch_MentionsNoSearch()
    {
        var vm = new SearchViewModel("", "", [], Shell());

        StringAssert.Contains(vm.GetContext(), "no search performed");
    }

    [TestMethod]
    public void GetContext_QueryAndRoot_ContainsBoth()
    {
        var vm = new SearchViewModel("budget", @"C:\docs", [], Shell());

        // Simulate a result count via direct property (SearchViewModel has ResultCount as observable)
        var ctx = vm.GetContext();

        StringAssert.Contains(ctx, "budget");
        StringAssert.Contains(ctx, @"C:\docs");
    }

    [TestMethod]
    public void GetClientTools_ContainsSearchTool()
    {
        var vm = new SearchViewModel("", "", [], Shell());

        var tools = vm.GetClientTools();

        Assert.IsTrue(tools.Any(t => t.Name == "search"),
            "Expected a 'search' client tool");
    }

    [TestMethod]
    public async Task SearchTool_SetsSearchQuery()
    {
        var vm   = new SearchViewModel("", @"C:\", [], Shell());
        var tool = vm.GetClientTools().Single(t => t.Name == "search");

        await tool.InvokeAsync(new JsonObject { ["query"] = "invoice" }, CancellationToken.None);

        Assert.AreEqual("invoice", vm.SearchQuery);
    }

    [TestMethod]
    public async Task SearchTool_EmptyQuery_ReturnsErrorAndLeavesQuery()
    {
        var vm   = new SearchViewModel("original", @"C:\", [], Shell());
        var tool = vm.GetClientTools().Single(t => t.Name == "search");

        var result = await tool.InvokeAsync(new JsonObject(), CancellationToken.None);

        Assert.IsTrue(result.IsError);
        Assert.AreEqual("original", vm.SearchQuery);
    }

    // ── Selection ────────────────────────────────────────────────────────────

    [TestMethod]
    public void SelectedEntry_Set_HasSelectionTrue()
    {
        var vm = new SearchViewModel("", "", [], Shell());

        vm.SelectedEntry = MakeEntry(@"C:\foo\bar.txt");

        Assert.IsTrue(vm.HasSelection);
    }

    [TestMethod]
    public void SelectedEntry_Cleared_HasSelectionFalse()
    {
        var vm = new SearchViewModel("", "", [], Shell());
        vm.SelectedEntry = MakeEntry(@"C:\foo\bar.txt");

        vm.SelectedEntry = null;

        Assert.IsFalse(vm.HasSelection);
    }

    // ── OpenLocation command ──────────────────────────────────────────────────

    [TestMethod]
    public void OpenLocation_CallsShellOpenTab_WithFileDirectory()
    {
        var shell = Shell();
        var vm = new SearchViewModel("", "", [], shell);
        vm.SelectedEntry = new SearchResultEntry
        {
            FilePath  = @"C:\foo\bar.txt",
            FileName  = "bar.txt",
            Directory = @"C:\foo"
        };

        vm.OpenLocationCommand.Execute(null);

        shell.Received(1).OpenTab(
            "FileSystem",
            Arg.Is<Dictionary<string, string>>(d => d["path"] == @"C:\foo"));
    }

    [TestMethod]
    public void OpenLocation_CanExecute_FalseWithNoSelection()
    {
        var vm = new SearchViewModel("", "", [], Shell());

        Assert.IsFalse(vm.OpenLocationCommand.CanExecute(null));
    }

    [TestMethod]
    public void OpenLocation_CanExecute_TrueAfterSelecting()
    {
        var vm = new SearchViewModel("", "", [], Shell());
        vm.SelectedEntry = MakeEntry(@"C:\foo\bar.txt");

        Assert.IsTrue(vm.OpenLocationCommand.CanExecute(null));
    }

    // ── GetContextObject ──────────────────────────────────────────────────────

    [TestMethod]
    public void GetContextObject_NoSelection_Null()
    {
        var vm = new SearchViewModel("", "", [], Shell());

        Assert.IsNull(vm.GetContextObject());
    }

    [TestMethod]
    public void GetContextObject_FolderSelected_RootIsFolder()
    {
        var vm = new SearchViewModel("", "", [], Shell());
        vm.SelectedEntry = new SearchResultEntry
        {
            FilePath  = @"C:\projects",
            FileName  = "projects",
            Directory = @"C:\",
            Kind      = "folder"
        };

        var ctx = (FileSystemContext?)vm.GetContextObject();

        Assert.IsNotNull(ctx);
        Assert.AreEqual(@"C:\projects", ctx.RootPath);
        Assert.AreEqual(@"C:\projects", ctx.CurrentPath);
    }

    [TestMethod]
    public void GetContextObject_FileSelected_SelectedItemsContainsPath()
    {
        var vm = new SearchViewModel("", "", [], Shell());
        vm.SelectedEntry = new SearchResultEntry
        {
            FilePath  = @"C:\foo\bar.txt",
            FileName  = "bar.txt",
            Directory = @"C:\foo",
            Kind      = "document"
        };

        var ctx = (FileSystemContext?)vm.GetContextObject();

        Assert.IsNotNull(ctx);
        Assert.IsTrue(ctx.SelectedItems.Contains(@"C:\foo\bar.txt"));
    }

    // ── MergeAndSearchAsync ───────────────────────────────────────────────────

    [TestMethod]
    public async Task MergeAndSearchAsync_SearchQueryContainsRefinement()
    {
        var vm = new SearchViewModel("report", @"C:\", [], Shell());

        // SearchQuery update happens synchronously before the first await
        var task = vm.MergeAndSearchAsync("pdf");
        StringAssert.Contains(vm.SearchQuery, "pdf");

        // Let the async search complete (may succeed or fail gracefully)
        await Task.WhenAny(task, Task.Delay(TimeSpan.FromSeconds(10)));
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static SearchResultEntry MakeEntry(string path) =>
        new()
        {
            FilePath  = path,
            FileName  = System.IO.Path.GetFileName(path),
            Directory = System.IO.Path.GetDirectoryName(path) ?? string.Empty
        };
}
