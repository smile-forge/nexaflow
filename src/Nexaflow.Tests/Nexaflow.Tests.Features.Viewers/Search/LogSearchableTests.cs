using Nexaflow.IO.Common;
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
using Nexaflow.Markdown.Editing;
using Nexaflow.Search;
using Nexaflow.Tests.Features.Infrastructure;
using Nexaflow.Tests.Fixtures;
using NSubstitute;

namespace Nexaflow.Tests.Features.Search;

/// <summary>
/// The log tab answering <c>?</c>. Beyond the shared conformance contract, these pin the two things that make a LOG different from a
/// static document: it is a third search layer that must leave the regex fade-filter and the user's custom highlight term untouched, and
/// its content arrives in two pieces (its end now, its history later) plus a live tail that keeps appending.
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

    /// <summary>What a search marks its matches with.</summary>
    private const string Found = FoundMarks.WashToken;

    private static string WriteTemp(string content, string suffix = "")
    {
        var path = Path.Combine(TestTemp.Root, $"logsearch_{Guid.NewGuid():N}{suffix}.log");
        File.WriteAllText(path, content, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        return path;
    }

    /// <summary>
    /// A shell whose RunOnUiAsync does its work at once — a substitute alone would hand back a default task, which would silently no-op
    /// every display assertion. <paramref name="capturedWatch"/> receives the file-changed callback the page registers, so a test can drive
    /// the live-tail path deterministically instead of racing a real watcher.
    /// </summary>
    private static IShellServices RunningShell(Action<Action>? capturedWatch = null)
    {
        var shell = Substitute.For<IShellServices>();
        shell.WatchFile(Arg.Any<string>(), Arg.Any<Action>())
             .Returns(ci => { capturedWatch?.Invoke(ci.Arg<Action>()); return Substitute.For<IFileWatch>(); });
        return UiRunningShell.Over(shell);
    }

    // IsMonitoring off before LoadAsync: otherwise a real FileSystemWatcher outlives the test.
    private static async Task<LogViewModel> LoadAsync(string path)
    {
        var vm = new LogViewModel(path, RunningShell(), VirtualFileSystem.Instance) { IsMonitoring = false };
        await vm.LoadAsync(CancellationToken.None);
        return vm;
    }

    protected override async Task<ISearchable> CreateAsync() => await LoadAsync(WriteTemp(Sample));

    /// <summary>FilterRegex and CustomHighlightTerm are in here deliberately: they prove a "?" search leaves the log's other two layers alone.</summary>
    protected override string Snapshot(ISearchable page)
    {
        var vm = (LogViewModel)page;
        return $"{vm.IsSearchActive}|{vm.SearchMatchCount}|{vm.CurrentSearchTerm}|" +
               $"{EditorMarks.MarkedBy(vm.Editor, Found).Count}|{vm.CurrentMatchLine}|{vm.FilterRegex}|{vm.CustomHighlightTerm}";
    }

    private static SearchRequest Query(string text) => SearchSyntax.ParseRequest(text);

    /// <summary>The line the caret is on, counting from nought.</summary>
    private static int CaretLine(LogViewModel vm) => vm.Document.Current.LineOf(vm.Editor.Caret);

    [TestMethod]
    public void DisplayingSearch_LeavesTheRegexFilterAndTheCustomTermAlone() => WithPage(async page =>
    {
        var vm = (LogViewModel)page;
        vm.FilterRegex = "WARN";
        vm.CustomHighlightTerm = "service";
        var termMarksBefore = EditorMarks.MarkedBy(vm.Editor, "Log.Term").Count;

        await vm.SearchAsync(Query("alpha42"), display: true, default);

        Assert.AreEqual("WARN", vm.FilterRegex, "a search must not rewrite the user's fade filter");
        Assert.IsTrue(vm.IsFilterActive);
        Assert.AreEqual("service", vm.CustomHighlightTerm, "a search must not rewrite the user's marker");
        Assert.AreEqual(termMarksBefore, EditorMarks.MarkedBy(vm.Editor, "Log.Term").Count);
        Assert.AreEqual(2, EditorMarks.MarkedBy(vm.Editor, Found).Count, "…while still marking its own matches");
    });

    [TestMethod]
    public void SearchIgnoresTheFadeFilter_BecauseFadingIsCosmetic() => WithPage(async page =>
    {
        var vm = (LogViewModel)page;
        // The filter dims non-matching lines rather than removing them, so scoping the search to it would make the same query mean
        // different things depending on an unrelated toolbar box.
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
    public void DisplayingSearch_GoesToTheFirstMatch() => WithPage(async page =>
    {
        var vm = (LogViewModel)page;
        var shown = new ScrollRecorder(vm.Editor);

        await vm.SearchAsync(Query("alpha42"), display: true, default);

        Assert.AreEqual(0, vm.CurrentMatchLine);
        Assert.AreEqual(vm.Document.ToString().IndexOf("alpha42", StringComparison.Ordinal), vm.Editor.Caret, "the caret on the match itself");
        Assert.AreEqual((0, ShownPlace.Centre), shown.Shown[^1], "its line put in the middle of the page");
    });

    [TestMethod]
    public void FindNext_CyclesEveryMatch_AndWraps() => WithPage(async page =>
    {
        var vm = (LogViewModel)page;
        await vm.SearchAsync(Query("alpha42"), display: true, default);
        Assert.AreEqual(0, CaretLine(vm));

        vm.FindNextMatchCommand.Execute(null);
        Assert.AreEqual(2, vm.CurrentMatchLine);
        Assert.AreEqual(2, CaretLine(vm));

        vm.FindNextMatchCommand.Execute(null);
        Assert.AreEqual(0, CaretLine(vm), "wrapping lands back on the first");

        vm.FindPreviousMatchCommand.Execute(null);
        Assert.AreEqual(2, CaretLine(vm));
    });

    [TestMethod]
    public void SteppingOntoTheSameMatchTwice_StillMoves() => WithPage(async page =>
    {
        // A single match: stepping lands where the page already is, and must still ask for it — the reader may have scrolled away.
        var vm = (LogViewModel)page;
        await vm.SearchAsync(Query("refused"), display: true, default);
        Assert.AreEqual(1, vm.SearchMatchCount);

        var shown = new ScrollRecorder(vm.Editor);
        var asked = shown.Shown.Count;

        vm.FindNextMatchCommand.Execute(null);
        Assert.AreEqual(asked + 1, shown.Shown.Count, "stepping onto the same match asks for its line again");
    });

    [TestMethod]
    public void ClearSearch_DropsTheMarksCountAndTerm() => WithPage(async page =>
    {
        var vm = (LogViewModel)page;
        await vm.SearchAsync(Query("alpha42"), display: true, default);
        Assert.IsTrue(vm.IsSearchActive);

        vm.ClearSearchCommand.Execute(null);

        Assert.IsFalse(vm.IsSearchActive);
        Assert.AreEqual(0, vm.SearchMatchCount);
        Assert.AreEqual(0, EditorMarks.MarkedBy(vm.Editor, Found).Count);
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

    // ── The two-part load: the end now, the history later ─────────────────────

    [TestMethod]
    public void WhileTheHeadIsStillLoading_TheOutcomeSaysSo() => WithPage(async page =>
    {
        // The flag IS the condition — LogViewModelTests covers loading setting it.
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
    public void WhenTheHistoryArrives_TheCountCompletesItself() => AsyncPump.Run(async () =>
    {
        var filler = string.Concat(Enumerable.Range(0, 200).Select(i => $"2024-01-01 00:00:00 INFO filler line {i:D4}\n"));
        var path = WriteTemp("2024-01-01 00:00:00 INFO alpha42 in the oldest entry\n" + filler + Sample);
        var vm = new LogViewModel(path, RunningShell(), VirtualFileSystem.Instance, ContentFileWindow.DefaultSize, tailBytes: 300) { IsMonitoring = false };
        try
        {
            await vm.LoadAsync(CancellationToken.None);
            Assert.IsTrue(vm.IsLoadingHead, "the end is shown first");

            await vm.SearchAsync(Query("alpha42"), display: true, default);
            await WaitFor(() => !vm.IsLoadingHead && vm.SearchMatchCount == 3);

            Assert.AreEqual(3, vm.SearchMatchCount, "the history is part of the log now");
            var document = vm.Document.Current;
            foreach (var (start, length) in EditorMarks.MarkedBy(vm.Editor, Found))
                Assert.AreEqual("alpha42", document.Slice(start, length), "each mark on its match, wherever the history put it");
        }
        finally { vm.Dispose(); File.Delete(path); }
    });

    // ── The live tail ─────────────────────────────────────────────────────────

    [TestMethod]
    public void AppendedLines_AreSearchedToo_WithoutLosingYourPlace() => AsyncPump.Run(async () =>
    {
        // Drives the REAL append path: capture the callback the page hands to WatchFile, append to the file, then fire it.
        Action? onChanged = null;
        var path = WriteTemp(Sample);
        var vm = new LogViewModel(path, RunningShell(cb => onChanged = cb), VirtualFileSystem.Instance);
        try
        {
            await vm.LoadAsync(CancellationToken.None);
            Assert.IsNotNull(onChanged, "the log registers a file watch when monitoring");

            await vm.SearchAsync(Query("alpha42"), display: true, default);
            Assert.AreEqual(2, vm.SearchMatchCount);

            // Park on the second match — a growing log must not drag the user back to the first.
            vm.FindNextMatchCommand.Execute(null);
            Assert.AreEqual(2, vm.CurrentMatchLine);

            File.AppendAllText(path, "2024-01-01 00:00:04 INFO alpha42 arrived later\n");
            onChanged!();
            await WaitFor(() => vm.SearchMatchCount == 3);

            Assert.AreEqual(3, vm.SearchMatchCount, "the appended line is part of the log now");
            Assert.AreEqual(2, vm.CurrentMatchLine, "…and the user is still parked where they were");
        }
        finally { vm.Dispose(); File.Delete(path); }
    });

    /// <summary>The append is read off the page, so it lands a moment after the callback returns.</summary>
    private static async Task WaitFor(Func<bool> condition)
    {
        for (var i = 0; i < 300 && !condition(); i++) await Task.Delay(10);
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
        Assert.AreEqual(1, EditorMarks.MarkedBy(vm.Editor, Found).Count, "the marks follow the narrowed set");
    });
}
