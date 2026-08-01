using System.IO;
using System.Text.Json.Nodes;
using NSubstitute;
using Nexaflow.Features.Common;
using Nexaflow.Features.Common.ClientTools;
using Nexaflow.Features.WindowsSearch;
using Nexaflow.Features.WindowsSearch.Services;
using Nexaflow.Features.WindowsSearch.ViewModels;
using Nexaflow.Search;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.Features.WindowsSearch;

[TestClass]
[CoversNode("win-search-ai-act-search")]
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

    // ── The folder-scan offer ─────────────────────────────────────────────────

    [TestMethod]
    public async Task NoIndexResults_OffersAScanRatherThanAnEmptyList()
    {
        // A term no index will match, under a real folder. Whether the index answers "nothing" or isn't
        // running at all, the user must be offered the scan — an empty list on its own claims the file
        // isn't there, which an unindexed folder cannot support.
        var root = Directory.CreateTempSubdirectory().FullName;
        try
        {
            var vm = new SearchViewModel("zqxwv-no-such-token-8813", root, [], Shell());

            await vm.RunSearchAsync(CancellationToken.None);

            Assert.AreEqual(0, vm.ResultCount);
            Assert.AreEqual(VerifyPhase.OfferScan, vm.VerificationPhase);
            StringAssert.Contains(vm.VerificationBanner, "scan");
        }
        finally { try { Directory.Delete(root, true); } catch { } }
    }

    [TestMethod]
    public async Task WithResults_NoScanIsOffered()
    {
        // The offer is for an empty result. Showing it alongside hits would invite a slow walk over a
        // question that was already answered.
        var root = Directory.CreateTempSubdirectory().FullName;
        try
        {
            var vm = new SearchViewModel("zqxwv-no-such-token-8813", root, [], Shell());
            await vm.RunSearchAsync(CancellationToken.None);
            vm.Results.Add(new SearchResultEntry { FilePath = "x", FileName = "x", Directory = "" });

            Assert.AreNotEqual(VerifyPhase.Scanning, vm.VerificationPhase);
        }
        finally { try { Directory.Delete(root, true); } catch { } }
    }

    // ── Refining a search that is already on screen ───────────────────────────

    [TestMethod]
    public void ASearchShapedQueryClaimsTheInput()
    {
        // Someone looking at results who types a search almost always means "narrow this". Leaving it to
        // the default score made it compete with the agent for the input.
        var vm = new SearchViewModel("needle", @"C:\", [], Shell());

        Assert.AreEqual(SearchViewModel.RefineScore, vm.ScoreQuery("haystack"), 0.001f);
        Assert.AreEqual(SearchViewModel.RefineScore, vm.ScoreQuery("*.txt"), 0.001f);
        Assert.AreEqual(SearchViewModel.RefineScore, vm.ScoreQuery("/ma(ths|gic)/"), 0.001f);
        Assert.AreEqual(SearchViewModel.RefineScore, vm.ScoreQuery("\"the lost dog\""), 0.001f);
    }

    [TestMethod]
    public void AQuotedPhraseIsOneTerm_NotSeveralWords()
    {
        // The decay counts TERMS. Scoring a quoted phrase by its word count would push a perfectly ordinary
        // refinement down towards prose.
        var vm = new SearchViewModel("needle", @"C:\", [], Shell());

        Assert.AreEqual(vm.ScoreQuery("dog"), vm.ScoreQuery("\"the lost dog\""), 0.001f);
    }

    [TestMethod]
    public void EachExtraTermLooksALittleLessLikeAFilter()
    {
        Assert.AreEqual(0.9f, SearchViewModel.ScoreRefinement(1), 0.001f);
        Assert.AreEqual(0.8f, SearchViewModel.ScoreRefinement(2), 0.001f);
        Assert.AreEqual(0.7f, SearchViewModel.ScoreRefinement(3), 0.001f);

        // Floored rather than cut off — a wordy refinement here still beats the same words on a page with
        // nothing to refine, and a hard threshold would snap between the two.
        Assert.AreEqual(0.5f, SearchViewModel.ScoreRefinement(20), 0.001f);
    }

    [TestMethod]
    public async Task RefiningAnEmptyResultSet_OffersANewSearchInstead()
    {
        // Narrowing nothing yields nothing, so taking the query at face value answers with the same empty
        // list — which reads as having been ignored.
        var root = Directory.CreateTempSubdirectory().FullName;
        try
        {
            var vm = new SearchViewModel("zqxwv-no-such-token-8813", root, [], Shell());
            await vm.RunSearchAsync(CancellationToken.None);
            Assert.AreEqual(0, vm.ResultCount, "precondition: nothing to refine");

            var outcome = await vm.SearchAsync(new SearchRequest("needle"), display: true, default);

            Assert.AreEqual(VerifyPhase.OfferNewSearch, vm.VerificationPhase);
            StringAssert.Contains(vm.VerificationBanner, "no results here to search within");
            StringAssert.Contains(vm.VerificationBanner, "needle");
            Assert.IsFalse(outcome.Failed, "a question is not a failure");
        }
        finally { try { Directory.Delete(root, true); } catch { } }
    }

    [TestMethod]
    public async Task DecliningTheNewSearch_LeavesTheResultsAlone()
    {
        var root = Directory.CreateTempSubdirectory().FullName;
        try
        {
            var vm = new SearchViewModel("zqxwv-no-such-token-8813", root, [], Shell());
            await vm.RunSearchAsync(CancellationToken.None);
            await vm.SearchAsync(new SearchRequest("needle"), display: true, default);

            vm.DeclineNewSearchCommand.Execute(null);

            Assert.AreEqual(VerifyPhase.None, vm.VerificationPhase);
            Assert.AreEqual(string.Empty, vm.VerificationBanner);
            Assert.AreEqual("zqxwv-no-such-token-8813", vm.SearchQuery, "the original query is untouched");
        }
        finally { try { Directory.Delete(root, true); } catch { } }
    }

    [TestMethod]
    public async Task AcceptingTheNewSearch_ReplacesTheQueryRatherThanNarrowingIt()
    {
        // Merging would carry forward a query that already found nothing, guaranteeing the new one finds
        // nothing too — the exact outcome the offer exists to avoid.
        var root = Directory.CreateTempSubdirectory().FullName;
        try
        {
            var vm = new SearchViewModel("zqxwv-no-such-token-8813", root, [], Shell());
            await vm.RunSearchAsync(CancellationToken.None);
            await vm.SearchAsync(new SearchRequest("needle"), display: true, default);

            await vm.RunAsNewSearchCommand.ExecuteAsync(null);

            Assert.AreEqual("needle", vm.SearchQuery);
            Assert.IsFalse(vm.SearchQuery.Contains("zqxwv"), "the failed query must not be carried forward");
        }
        finally { try { Directory.Delete(root, true); } catch { } }
    }

    // ── Teardown ──────────────────────────────────────────────────────────────

    [TestMethod]
    public void DisposingStopsWhatTheTabStarted()
    {
        // The shell disposes a page ViewModel when its tab closes. Without this a folder scan keeps walking
        // the disk and reading files for a tab nobody can see, with nothing left to stop it.
        var vm = new SearchViewModel("needle", @"C:\", [], Shell());

        vm.Dispose();
        vm.Dispose();   // idempotent — the shell may dispose a view and its ViewModel both
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
    public void GetContext_ThisPcSearch_ReportsQueryAndScope_NotNoSearch()
    {
        // A This-PC / cross-drive search has an empty SearchRoot but a populated drive list. Regression:
        // the old guard keyed "performed" off SearchRoot and mis-reported this as "no search performed".
        var vm = new SearchViewModel("budget", "", [@"C:\", @"D:\"], Shell());

        var ctx = vm.GetContext();

        StringAssert.Contains(ctx, "budget");
        StringAssert.Contains(ctx, "This PC");
        Assert.IsFalse(ctx.Contains("no search performed"), ctx);
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
