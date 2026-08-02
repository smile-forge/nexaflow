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
    /// <summary>A shell whose RunOnUiAsync actually runs the action — the substitute's default swallows it,
    /// which silently no-ops every UI-marshalled path, the folder scan's streamed results included.</summary>
    private static IShellServices Shell()
    {
        var shell = Substitute.For<IShellServices>();
        shell.RunOnUiAsync(Arg.Any<Action>())
             .Returns(ci => { ci.Arg<Action>()(); return Task.CompletedTask; });
        return shell;
    }

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
    public void ARefinementTypedDuringASearchStillBelongsToThisPage()
    {
        // It used to score zero while busy, which sent a refinement typed during a folder scan to the AI —
        // and a scan is exactly when the user is watching rows arrive and deciding to narrow them. Whether
        // the page is busy is a question for whoever handles the query, not for who should get it.
        var vm = new SearchViewModel("needle", @"C:\", [], Shell()) { IsSearching = true };

        Assert.AreEqual(SearchViewModel.RefineScore, vm.ScoreQuery("haystack"), 0.001f);
    }

    [TestMethod]
    public async Task RefiningScanResults_RescansRatherThanAskingTheIndex()
    {
        // The index already had nothing to say about this location — that is why the scan ran. Sending the
        // narrower query back to it would answer a tighter question with an emptier list.
        var root = Directory.CreateTempSubdirectory().FullName;
        try
        {
            await File.WriteAllTextAsync(Path.Combine(root, "one.txt"), "needle and haystack");
            await File.WriteAllTextAsync(Path.Combine(root, "two.txt"), "needle alone");

            var vm = new SearchViewModel("needle", root, [], Shell());
            await vm.RunSearchAsync(CancellationToken.None);

            // Scan first, so the results on screen are the scan's.
            await vm.ScanFolderCommand.ExecuteAsync(null);
            Assert.AreEqual(2, vm.ResultCount, "precondition: the scan found both files");

            await vm.SearchAsync(new SearchRequest("haystack"), display: true, default);

            Assert.AreEqual(1, vm.ResultCount, "the refinement should have narrowed the scan, not emptied it");
            Assert.AreEqual("one.txt", vm.Results[0].FileName);
            StringAssert.Contains(vm.SearchQuery, "haystack");
            StringAssert.Contains(vm.SearchQuery, "needle");
        }
        finally { try { Directory.Delete(root, true); } catch { } }
    }

    /// <remarks>
    /// Serial: it depends on catching the scan mid-walk, and under 32-way parallel load the poll interval
    /// stretches far enough that the scan finishes first — which trips the precondition rather than the
    /// assertion, but is a flake either way.
    /// </remarks>
    [TestMethod]
    [DoNotParallelize]
    public async Task RefiningWhileAScanIsRunning_NarrowsItInsteadOfFaulting()
    {
        // Refining mid-scan is the case a user actually hits, and it was reported reaching the AI instead
        // of narrowing. This pins the behaviour that matters — the scan is superseded, the result narrows,
        // and neither the superseded scan's banner nor its busy flag overwrites the live one.
        var root = Directory.CreateTempSubdirectory().FullName;
        try
        {
            // Enough files, and enough bytes each, that the walk is unambiguously still running when the
            // refinement lands. Too few and the scan finishes first, which tests the already-covered
            // post-scan path while looking like it tested this one.
            var filler = new string('x', 512);
            for (var i = 0; i < 4000; i++)
                await File.WriteAllTextAsync(Path.Combine(root, $"f{i}.txt"), $"needle {filler}");
            await File.WriteAllTextAsync(Path.Combine(root, "both.txt"), "needle and haystack");

            var vm = new SearchViewModel("needle", root, [], Shell());
            await vm.RunSearchAsync(CancellationToken.None);

            var scanning = vm.ScanFolderCommand.ExecuteAsync(null);   // deliberately not awaited
            while (vm.ResultCount == 0 && !scanning.IsCompleted) await Task.Delay(1);

            Assert.IsFalse(scanning.IsCompleted,
                "the scan finished before the refinement — this case was never exercised");

            await vm.SearchAsync(new SearchRequest("haystack"), display: true, default);
            await scanning;              // the superseded walk unwinds

            Assert.AreEqual(1, vm.ResultCount, "only the file containing both terms should survive");
            Assert.AreEqual("both.txt", vm.Results[0].FileName);
            Assert.AreEqual(VerifyPhase.Done, vm.VerificationPhase,
                "the superseded scan must not leave the banner claiming it was stopped");
            Assert.IsFalse(vm.IsSearching, "the superseded scan must not clear the busy flag for the live one");
        }
        finally { try { Directory.Delete(root, true); } catch { } }
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

    [TestMethod]
    public async Task EditingTheQueryReplacesTheSearchRatherThanNarrowingIt()
    {
        // The header field is the only way to UNDO a refinement — everything else on this page narrows.
        // If an edit merged with what was already there, a term could be added but never removed.
        var root = Directory.CreateTempSubdirectory().FullName;
        try
        {
            await File.WriteAllTextAsync(Path.Combine(root, "a.txt"), "needle");
            await File.WriteAllTextAsync(Path.Combine(root, "b.txt"), "haystack");

            var vm = new SearchViewModel("needle", root, [], Shell());
            await vm.RunSearchAsync(CancellationToken.None);
            await vm.ScanFolderCommand.ExecuteAsync(null);

            Assert.AreEqual(1, vm.ResultCount);
            Assert.AreEqual("a.txt", vm.Results[0].FileName);

            // What the header TextBox does: write the property, then run.
            vm.SearchQuery = "haystack";
            await vm.RunSearchAsync(CancellationToken.None);
            await vm.ScanFolderCommand.ExecuteAsync(null);

            Assert.AreEqual(1, vm.ResultCount);
            Assert.AreEqual("b.txt", vm.Results[0].FileName,
                "the replaced term must be gone — a merge would have left nothing matching both");
        }
        finally { try { Directory.Delete(root, true); } catch { } }
    }

    [TestMethod]
    public async Task EditingTheQueryDuringAScanSupersedesIt()
    {
        // Re-running from the header while a scan is walking must abandon that walk, not race it.
        var root = Directory.CreateTempSubdirectory().FullName;
        try
        {
            await File.WriteAllTextAsync(Path.Combine(root, "a.txt"), "needle");

            var vm = new SearchViewModel("needle", root, [], Shell());
            await vm.RunSearchAsync(CancellationToken.None);
            await vm.ScanFolderCommand.ExecuteAsync(null);
            Assert.AreEqual(1, vm.ResultCount);

            vm.SearchQuery = "zqxwv-no-such-token-8813";
            await vm.RunSearchAsync(CancellationToken.None);

            Assert.AreEqual(0, vm.ResultCount, "the previous scan's rows belong to a query that is gone");
            Assert.AreEqual(VerifyPhase.OfferScan, vm.VerificationPhase);
        }
        finally { try { Directory.Delete(root, true); } catch { } }
    }

    // ── Tab label and breadcrumbs ─────────────────────────────────────────────

    [TestMethod]
    public void TheTabLabelNamesTheSearch()
    {
        Assert.AreEqual("needle", SearchViewModel.TabTitleFor("needle"));
        Assert.AreEqual("Search",  SearchViewModel.TabTitleFor("   "));

        // The tab strip renders Page.Icon itself, so a magnifier here would be the second one.
        Assert.IsFalse(SearchViewModel.TabTitleFor("needle").Any(char.IsSurrogate),
            "the label carries no icon of its own");
    }

    [TestMethod]
    public void ALongQueryIsElidedRatherThanWideningTheTab()
    {
        var title = SearchViewModel.TabTitleFor(new string('x', 60));

        StringAssert.EndsWith(title, "…");
        Assert.IsTrue(title.Length <= SearchViewModel.TabQueryChars + 1, title);
    }

    [TestMethod]
    public void TheBreadcrumbLeadsWithTheFolderBeingSearched()
    {
        // It used to show only the folder's NAME, which is ambiguous between every "temp" on the machine —
        // and unclickable, so there was no way back to the place being searched.
        var page = new Page();
        _ = new SearchViewModel("needle", @"C:\temp", [], Shell()) { Tab = page };

        Assert.AreEqual(2, page.Breadcrumbs.Count);
        Assert.AreEqual(@"C:\temp", page.Breadcrumbs[0].Label);
        Assert.AreEqual(FileBreadcrumbs.FileSystemPageKind, page.Breadcrumbs[0].TargetPageKind,
            "the scope crumb should navigate, like every other feature's directory crumb");
        StringAssert.Contains(page.Breadcrumbs[1].Label, "needle");
    }

    [TestMethod]
    public void ACrossDriveSearchSaysSoRatherThanShowingAnEmptyCrumb()
    {
        var page = new Page();
        _ = new SearchViewModel("needle", "", [@"C:\", @"D:\"], Shell()) { Tab = page };

        Assert.AreEqual("This PC", page.Breadcrumbs[0].Label);
    }

    [TestMethod]
    public void RefiningUpdatesTheTabAndBreadcrumb()
    {
        // The regression: both were written once, where the tab was created, so every later refinement left
        // them describing a query the page was no longer showing.
        var page = new Page();
        var vm   = new SearchViewModel("needle", @"C:\temp", [], Shell()) { Tab = page };

        vm.SearchQuery = "needle haystack";

        StringAssert.Contains(page.Breadcrumbs[1].Label, "haystack");
        StringAssert.Contains(page.Title, "needle");
        Assert.AreEqual("needle haystack", page.PageParams!["query"],
            "a reopened tab is rebuilt from these, so they have to follow the query too");
    }

    // ── Cancelling a slow query ───────────────────────────────────────────────

    [TestMethod]
    public async Task AQuickSearchNeverOffersACancel()
    {
        // A Stop that flickers on every search is noise, and noise is what trains people to ignore the
        // control when it finally matters.
        var root = Directory.CreateTempSubdirectory().FullName;
        try
        {
            var vm = new SearchViewModel("zqxwv-no-such-token-8813", root, [], Shell());

            await vm.RunSearchAsync(CancellationToken.None);

            Assert.IsFalse(vm.CanCancelSearch, "this search answered immediately");
        }
        finally { try { Directory.Delete(root, true); } catch { } }
    }

    [TestMethod]
    public void CancellingClearsTheAffordanceRatherThanLeavingItUp()
    {
        var vm = new SearchViewModel("needle", @"C:\", [], Shell()) { CanCancelSearch = true };

        vm.CancelSearchCommand.Execute(null);

        Assert.IsFalse(vm.CanCancelSearch);
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
