using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Nexaflow.Features.Common;
using Nexaflow.Features.Common.ClientTools;
using Nexaflow.Features.Common.Search;
using Nexaflow.Features.Logs.ViewModels;
using Nexaflow.Search;
using Nexaflow.Tests.Features.Infrastructure;
using Nexaflow.Tests.Fixtures;
using NSubstitute;

namespace Nexaflow.Tests.Features.Search;

/// <summary>
/// The log tab answering <c>?</c>. Beyond the shared conformance contract, these pin the two things that
/// make a LOG different from a static document: it is a third search layer that must leave the regex
/// fade-filter and the user's custom highlight term untouched, and its content arrives in two pieces (a
/// tail now, the head later) plus a live tail that keeps appending.
/// </summary>
[TestClass]
[CoversNode("log-viewer-search")]
public class LogSearchableTests : SearchableContentConformanceTests
{
    protected override string LiteralTermInContent => "alpha42";
    protected override string RegexOnlyPattern => @"alpha\d+";

    // Two lines carry alpha42; the third holds "alpha42x" so a whole-word search must NOT count it.
    private const string Sample =
        "2024-01-01 00:00:00 INFO alpha42 service starting\n" +
        "2024-01-01 00:00:01 WARN nothing to see\n" +
        "2024-01-01 00:00:02 ERROR alpha42 connection refused\n" +
        "2024-01-01 00:00:03 INFO alpha42x is a longer word\n";

    private static string WriteTemp(string content, string suffix = "")
    {
        var path = Path.Combine(Path.GetTempPath(), $"logsearch_{Guid.NewGuid():N}{suffix}.log");
        File.WriteAllText(path, content, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        return path;
    }

    /// <summary>A shell whose RunOnUiAsync overloads actually run the delegate. The default substitute
    /// returns a null Task and swallows the body, which would silently no-op every display assertion —
    /// the search path marshals BOTH display and non-display through it.
    /// <para><paramref name="capturedWatch"/> receives the file-changed callback the ViewModel registers,
    /// so a test can drive the live-tail path deterministically instead of racing a real watcher.</para></summary>
    private static IShellServices RunningShell(Action<Action>? capturedWatch = null)
    {
        var shell = Substitute.For<IShellServices>();
        shell.RunOnUiAsync(Arg.Any<Action>())
             .Returns(ci => { ci.Arg<Action>()(); return Task.CompletedTask; });
        shell.RunOnUiAsync(Arg.Any<Func<Task<ToolResult>>>())
             .Returns(ci => ci.Arg<Func<Task<ToolResult>>>()());
        shell.RunOnUiAsync(Arg.Any<Func<Task<SearchOutcome>>>())
             .Returns(ci => ci.Arg<Func<Task<SearchOutcome>>>()());
        shell.RunOnUiAsync(Arg.Any<Func<Task<bool>>>())
             .Returns(ci => ci.Arg<Func<Task<bool>>>()());
        shell.WatchFile(Arg.Any<string>(), Arg.Any<Action>())
             .Returns(ci => { capturedWatch?.Invoke(ci.Arg<Action>()); return Substitute.For<IFileWatch>(); });
        return shell;
    }

    // IsMonitoring off before LoadAsync: otherwise a real FileSystemWatcher outlives the test.
    private static async Task<LogViewModel> LoadAsync(string path)
    {
        var vm = new LogViewModel(path, RunningShell()) { IsMonitoring = false };
        await vm.LoadAsync(CancellationToken.None);
        return vm;
    }

    protected override async Task<ISearchable> CreateAsync() => await LoadAsync(WriteTemp(Sample));

    /// <summary>FilterRegex and CustomHighlightTerm are in here deliberately: they prove a "?" search
    /// leaves the log's other two layers alone.</summary>
    protected override string Snapshot(ISearchable page)
    {
        var vm = (LogViewModel)page;
        return $"{vm.IsSearchActive}|{vm.SearchMatchCount}|{vm.CurrentSearchTerm}|" +
               $"{vm.SearchHighlights.Count}|{vm.ScrollToOffset}|{vm.FilterRegex}|{vm.CustomHighlightTerm}";
    }

    private static SearchRequest Query(string text) => SearchSyntax.ParseRequest(text);

    [TestMethod]
    public void DisplayingSearch_LeavesTheRegexFilterAndTheCustomTermAlone() => WithPage(async page =>
    {
        var vm = (LogViewModel)page;
        vm.FilterRegex = "WARN";
        vm.CustomHighlightTerm = "service";
        var termHighlightsBefore = vm.CustomTermHighlights.Count;

        await vm.SearchAsync(Query("alpha42"), display: true, default);

        Assert.AreEqual("WARN", vm.FilterRegex, "a search must not rewrite the user's fade filter");
        Assert.IsTrue(vm.IsFilterActive);
        Assert.AreEqual("service", vm.CustomHighlightTerm, "a search must not rewrite the user's marker");
        Assert.AreEqual(termHighlightsBefore, vm.CustomTermHighlights.Count);
        Assert.IsTrue(vm.SearchHighlights.Count > 0, "…while still painting its own matches");
    });

    [TestMethod]
    public void SearchIgnoresTheFadeFilter_BecauseFadingIsCosmetic() => WithPage(async page =>
    {
        var vm = (LogViewModel)page;
        // The filter dims non-matching lines rather than removing them, so scoping the search to it would
        // make the same query mean different things depending on an unrelated toolbar box.
        vm.FilterRegex = "WARN";                       // matches only the line WITHOUT alpha42
        var outcome = await vm.SearchAsync(Query("alpha42"), display: true, default);

        Assert.AreEqual(2, outcome.MatchCount, "the whole document is searched, not the filter-passing lines");
    });

    [TestMethod]
    public void ALiteralTerm_MeansTheWordItSpells() => WithPage(async page =>
    {
        // "alpha42x" is a longer word and must not count; the two exact occurrences must.
        var outcome = await page.SearchAsync(Query("alpha42"), display: false, default);
        Assert.AreEqual(2, outcome.MatchCount);
    });

    [TestMethod]
    public void Hits_CarryLineNumbersAndTheLineText() => WithPage(async page =>
    {
        var outcome = await page.SearchAsync(Query("alpha42"), display: false, default);

        CollectionAssert.AreEqual(new[] { "0", "2" }, outcome.Hits.Select(h => h.Id).ToArray());
        StringAssert.Contains(outcome.Hits[0].Label, "line 1");
        StringAssert.Contains(outcome.Hits[0].Preview!, "service starting");
    });

    [TestMethod]
    public void DisplayingSearch_ScrollsToTheFirstMatch() => WithPage(async page =>
    {
        var vm = (LogViewModel)page;
        await vm.SearchAsync(Query("alpha42"), display: true, default);

        var firstMatchOffset = vm.Document.GetLineByNumber(1).Offset;
        Assert.AreEqual(firstMatchOffset, vm.ScrollToOffset);
    });

    [TestMethod]
    public void FindNext_CyclesEveryMatch_AndWraps() => WithPage(async page =>
    {
        var vm = (LogViewModel)page;
        await vm.SearchAsync(Query("alpha42"), display: true, default);

        var line1 = vm.Document.GetLineByNumber(1).Offset;
        var line3 = vm.Document.GetLineByNumber(3).Offset;
        Assert.AreEqual(line1, vm.ScrollToOffset);

        vm.FindNextMatchCommand.Execute(null);
        Assert.AreEqual(line3, vm.ScrollToOffset);

        // Wrapping lands back on line 1 — and the reset-through--1 is what makes a repeat target register.
        vm.FindNextMatchCommand.Execute(null);
        Assert.AreEqual(line1, vm.ScrollToOffset);

        vm.FindPreviousMatchCommand.Execute(null);
        Assert.AreEqual(line3, vm.ScrollToOffset);
    });

    [TestMethod]
    public void SteppingOntoTheSameMatchTwice_StillMoves() => WithPage(async page =>
    {
        // ScrollToOffset is an observable int: re-assigning the same value raises nothing, so a
        // single-match search would leave the view stuck without the deliberate -1 reset.
        var vm = (LogViewModel)page;
        await vm.SearchAsync(Query("refused"), display: true, default);
        Assert.AreEqual(1, vm.SearchMatchCount);

        var seen = 0;
        vm.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(LogViewModel.ScrollToOffset)) seen++; };

        vm.FindNextMatchCommand.Execute(null);
        Assert.IsTrue(seen >= 2, "stepping onto the same offset must pass through -1 so the view re-scrolls");
    });

    [TestMethod]
    public void ClearSearch_DropsTheHighlightsCountAndTerm() => WithPage(async page =>
    {
        var vm = (LogViewModel)page;
        await vm.SearchAsync(Query("alpha42"), display: true, default);
        Assert.IsTrue(vm.IsSearchActive);

        vm.ClearSearchCommand.Execute(null);

        Assert.IsFalse(vm.IsSearchActive);
        Assert.AreEqual(0, vm.SearchMatchCount);
        Assert.AreEqual(0, vm.SearchHighlights.Count);
        Assert.AreEqual(string.Empty, vm.CurrentSearchTerm);
    });

    [TestMethod]
    public void SearchThatMatchesNothing_IsAResultNotAFailure() => WithPage(async page =>
    {
        var vm = (LogViewModel)page;
        var outcome = await vm.SearchAsync(Query("nosuchword"), display: true, default);

        Assert.IsFalse(outcome.Failed);
        Assert.AreEqual(0, outcome.MatchCount);
        Assert.IsTrue(vm.IsSearchActive, "\"no matches for X\" is still worth showing the user");
        Assert.IsFalse(vm.HasSearchMatches, "…but there is nothing to step through");
    });

    // ── The two-part load: a tail now, the head later ─────────────────────────

    [TestMethod]
    public void WhileTheHeadIsStillLoading_TheOutcomeSaysSo() => WithPage(async page =>
    {
        // LoadAsync fires the head load and returns, so catching the window by timing would be a race.
        // The flag IS the condition, and LogViewModelTests already covers LoadAsync setting it.
        var vm = (LogViewModel)page;
        vm.IsLoadingHead = true;

        var outcome = await vm.SearchAsync(Query("alpha42"), display: true, default);

        Assert.IsFalse(outcome.Failed, "a partial answer is still an answer");
        Assert.IsNotNull(outcome.Message);
        StringAssert.Contains(outcome.Message!, "still loading");
        Assert.AreEqual(2, outcome.MatchCount, "…and the matches it DID find are still reported");
        StringAssert.Contains(vm.SearchTargetDescription, "still loading");
    });

    [TestMethod]
    public void WhenTheHeadArrives_TheCountCompletesItself() => WithPage(async page =>
    {
        var vm = (LogViewModel)page;
        vm.IsLoadingHead = true;
        await vm.SearchAsync(Query("alpha42"), display: true, default);
        Assert.AreEqual(2, vm.SearchMatchCount);

        // What LoadHeadAsync does when the earlier history lands: insert at 0, then rescan. Inserting at
        // the front shifts EVERY offset, so this also proves the spans are re-derived rather than reused.
        vm.Document.Insert(0, "2024-01-01 00:00:00 INFO alpha42 in the oldest entry\n");
        vm.IsLoadingHead = false;
        vm.RescanSearch();

        Assert.AreEqual(3, vm.SearchMatchCount, "the head is part of the log now");
        foreach (var (offset, length) in vm.SearchHighlights)
            Assert.AreEqual("alpha42", vm.Document.GetText(offset, length),
                "a shifted offset would land this span on the wrong text");
    });

    // ── The live tail ─────────────────────────────────────────────────────────

    [TestMethod]
    public void AppendedLines_AreSearchedToo_WithoutLosingYourPlace() => AsyncPump.Run(async () =>
    {
        // Pumped: the TextDocument is thread-affine, and OnFileChanged is async void — its continuation
        // has to land back on the thread that owns the document.
        // Drives the REAL append path: capture the callback the ViewModel hands to WatchFile, append to
        // the file, then fire it — no timing race, no test-only hook on the ViewModel.
        Action? onChanged = null;
        var path = WriteTemp(Sample);
        var vm = new LogViewModel(path, RunningShell(cb => onChanged = cb));
        try
        {
            await vm.LoadAsync(CancellationToken.None);
            Assert.IsNotNull(onChanged, "the log registers a file watch when monitoring");

            await vm.SearchAsync(Query("alpha42"), display: true, default);
            Assert.AreEqual(2, vm.SearchMatchCount);

            // Park on the second match — a growing log must not drag the user back to the first.
            vm.FindNextMatchCommand.Execute(null);
            var parkedOn = vm.ScrollToOffset;

            File.AppendAllText(path, "2024-01-01 00:00:04 INFO alpha42 arrived later\n");
            onChanged!();
            await WaitFor(() => vm.SearchMatchCount == 3);

            Assert.AreEqual(3, vm.SearchMatchCount, "the appended line is part of the log now");
            Assert.AreEqual(parkedOn, vm.ScrollToOffset, "…and the user is still parked where they were");
        }
        finally { vm.Dispose(); File.Delete(path); }
    });

    /// <summary>OnFileChanged is async void, so the append lands a continuation after the callback returns.</summary>
    private static async Task WaitFor(Func<bool> condition)
    {
        for (var i = 0; i < 200 && !condition(); i++) await Task.Delay(10);
    }

    [TestMethod]
    public void ShowResults_NarrowsToTheChosenLines() => WithPage(async page =>
    {
        var vm = (LogViewModel)page;
        var found = await vm.SearchAsync(Query("alpha42"), display: true, default);
        Assert.AreEqual(2, vm.SearchMatchCount);

        var narrowed = await vm.ShowResultsAsync([found.Hits[1]], default);

        Assert.IsTrue(narrowed);
        Assert.AreEqual(1, vm.SearchMatchCount);
        Assert.AreEqual(1, vm.SearchHighlights.Count, "the highlights follow the narrowed set");
    });
}
