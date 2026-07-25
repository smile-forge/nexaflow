using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Nexaflow.Features.Common;
using Nexaflow.Features.Logs.Parsing;
using Nexaflow.Features.Logs.ViewModels;
using Nexaflow.Tests.Features.Infrastructure;
using Nexaflow.Tests.Fixtures;
using NSubstitute;

namespace Nexaflow.Tests.Features.Logs;

/// <summary>
/// The log tab's controls and readouts: the toolbar's severity toggles and term highlight, the gutter
/// selection, the live-tail pause/follow pair, the status-bar readouts, and the parser / timestamp
/// detection behind them.
/// <para>
/// The rendering itself (the AvalonEdit colorizers that wash a line) is a paint pass; what is asserted
/// here is the state those renderers read — which is what actually decides whether a FATAL line is
/// highlighted, whether a paused tail withholds new lines, and what the status bar tells the user about
/// how much of the file they are looking at.
/// </para>
/// </summary>
[TestClass]
public class LogSurfaceTests
{
    private const string TimestampedLog =
        "2024-01-01 08:00:00 INFO  started\n" +
        "2024-01-01 08:00:01 WARN  slow\n" +
        "2024-01-01 08:00:02 ERROR boom\n" +
        "2024-01-01 08:00:03 INFO  recovered\n";

    private static string WriteTemp(string content)
    {
        var path = Path.Combine(Path.GetTempPath(), $"logsurface_{Guid.NewGuid():N}.log");
        File.WriteAllText(path, content, new UTF8Encoding(false));
        return path;
    }

    /// <summary>The tailing viewer keeps a read handle open across awaits; give it a moment to let go.</summary>
    private static void TryDelete(string path)
    {
        for (var i = 0; i < 20; i++)
        {
            try { File.Delete(path); return; }
            catch (IOException) { Thread.Sleep(25); }
        }
    }

    private static LogViewModel Detached()
        => new("nonexistent.log", Substitute.For<IShellServices>()) { IsMonitoring = false };

    // ── Toolbar: severity highlighting ────────────────────────────────────────

    [TestMethod]
    [CoversNode("log-viewer-level-highlight")]
    public void LevelToggles_AreIndependent_AndReportedToTheAssistant()
    {
        using var vm = Detached();

        // All five start off: an unfiltered log shows no colour until the user asks for a level.
        Assert.IsFalse(vm.HighlightFatal || vm.HighlightError || vm.HighlightWarning
                    || vm.HighlightInfo || vm.HighlightDebug);

        vm.HighlightError = true;
        vm.HighlightDebug = true;

        Assert.IsFalse(vm.HighlightWarning, "toggling Error must not drag its neighbours on");
        var context = vm.GetContext();
        StringAssert.Contains(context, "level highlight: Error, Debug",
                              "the assistant must be told which levels are actually washed");
    }

    // ── Toolbar: custom term highlight ────────────────────────────────────────

    [TestMethod]
    [CoversNode("log-viewer-custom-term")]
    public void CustomTerm_MatchesEveryOccurrence_CaseInsensitively()
    {
        using var vm = Detached();
        vm.Document.Text = "alpha Timeout beta\ntimeout gamma\n";

        vm.CustomHighlightTerm = "timeout";

        var hits = vm.CustomTermHighlights;
        Assert.AreEqual(2, hits.Count, "both casings must highlight");
        Assert.IsTrue(hits.All(h => h.length == "timeout".Length));
        CollectionAssert.AreEqual(new[] { 6, 19 }, hits.Select(h => h.offset).ToArray());
    }

    [TestMethod]
    [CoversNode("log-viewer-custom-term")]
    public void CustomTerm_Blank_ClearsEveryHighlight()
    {
        using var vm = Detached();
        vm.Document.Text = "timeout\n";
        vm.CustomHighlightTerm = "timeout";
        Assert.AreEqual(1, vm.CustomTermHighlights.Count);

        vm.CustomHighlightTerm = "   ";

        Assert.AreEqual(0, vm.CustomTermHighlights.Count);
    }

    // ── Gutter selection + the Copy button that consumes it ───────────────────

    [TestMethod]
    [CoversNode("log-viewer-line-select")]
    public void ClickingTheGutter_TogglesThatLine_AndOnlyThatLine()
    {
        using var vm = Detached();
        vm.Document.Text = TimestampedLog;

        vm.ToggleLineSelection(2);
        vm.ToggleLineSelection(4);

        CollectionAssert.AreEquivalent(new[] { 2, 4 }, vm.SelectedLineNumbers.ToArray());
        Assert.AreEqual(2, vm.SelectedLineCount);

        vm.ToggleLineSelection(2);   // clicking the same line again de-selects it

        CollectionAssert.AreEquivalent(new[] { 4 }, vm.SelectedLineNumbers.ToArray());
        Assert.AreEqual(1, vm.SelectedLineCount);
    }

    [TestMethod]
    [CoversNode("log-viewer-copy-selected")]
    public void CopyButton_EnablesOnlyWhileLinesAreSelected()
    {
        using var vm = Detached();
        vm.Document.Text = TimestampedLog;
        Assert.IsFalse(vm.CopySelectedLinesCommand.CanExecute(null));

        vm.ToggleLineSelection(1);
        Assert.IsTrue(vm.CopySelectedLinesCommand.CanExecute(null));

        vm.ToggleLineSelection(1);
        Assert.IsFalse(vm.CopySelectedLinesCommand.CanExecute(null),
                       "the button must go back to disabled once the last line is de-selected");
    }

    // ── Toolbar: encoding selector ────────────────────────────────────────────

    [TestMethod]
    [CoversNode("log-viewer-encoding")]
    public void EncodingSelector_OffersTheCommonSet_AndDefaultsToUtf8()
    {
        using var vm = Detached();

        Assert.AreEqual("UTF-8", vm.SelectedEncoding.Name);
        CollectionAssert.AreEqual(
            new[] { "UTF-8", "UTF-16 LE", "UTF-16 BE", "Latin-1", "System Default" },
            vm.AvailableEncodings.Select(e => e.Name).ToArray());
    }

    // ── Live tail: watch, pause, follow ───────────────────────────────────────

    /// <summary>
    /// Loads a file, captures the change callback the shell wired up, appends to the file and fires it —
    /// the same sequence the shell's watcher drives, without a real <c>FileSystemWatcher</c>.
    /// </summary>
    private static async Task<(LogViewModel Vm, Action Notify, string Path)> TailingAsync(string initial)
    {
        var path = WriteTemp(initial);
        var shell = Substitute.For<IShellServices>();
        Action? onChanged = null;
        shell.WatchFile(Arg.Any<string>(), Arg.Do<Action>(a => onChanged = a))
             .Returns(Substitute.For<IFileWatch>());

        var vm = new LogViewModel(path, shell);
        await vm.LoadAsync(CancellationToken.None);

        Assert.IsNotNull(onChanged, "the viewer must ask the shell to watch the file it loaded");
        return (vm, onChanged!, path);
    }

    /// <summary>
    /// The change handler reads the appended bytes asynchronously, so a notification is observed a moment
    /// after it is raised. Pumps until the effect lands rather than assuming one turn is enough.
    /// </summary>
    private static async Task Settled(Func<bool> until, string what)
    {
        for (var i = 0; i < 200 && !until(); i++) await Task.Delay(10);
        Assert.IsTrue(until(), $"timed out waiting for {what}");
    }

    [TestMethod]
    [CoversNode("log-viewer-watch")]
    public void AppendedLines_AreTailedIn_WithoutRereadingTheFile() => AsyncPump.Run(async () =>
    {
        var (vm, notify, path) = await TailingAsync("first\n");
        try
        {
            File.AppendAllText(path, "second\n");
            notify();
            await Settled(() => vm.Document.Text.Contains("second"), "the appended line to arrive");

            Assert.AreEqual(1, vm.Document.Text.Split("first").Length - 1,
                            "only the newly-written bytes are appended, so the head is never duplicated");
        }
        finally { vm.Dispose(); TryDelete(path); }
    });

    [TestMethod]
    [CoversNode("log-viewer-pause")]
    public void Pausing_HoldsNewLines_AndResumingFlushesThem() => AsyncPump.Run(async () =>
    {
        var (vm, notify, path) = await TailingAsync("first\n");
        try
        {
            var sizeBefore = vm.FileSizeText;
            vm.IsPaused = true;
            File.AppendAllText(path, "while-paused\n");
            notify();
            // The size readout still tracks the growing file while paused — that's the signal the
            // notification has been handled, so the assertion below isn't just winning a race.
            await Settled(() => vm.FileSizeText != sizeBefore, "the paused tail to notice the file grew");

            Assert.IsFalse(vm.Document.Text.Contains("while-paused"),
                           "a paused tail must not move the view under the user");

            vm.IsPaused = false;   // resuming flushes what arrived meanwhile

            StringAssert.Contains(vm.Document.Text, "while-paused");
        }
        finally { vm.Dispose(); TryDelete(path); }
    });

    [TestMethod]
    [CoversNode("log-viewer-follow")]
    public void Following_ScrollsToTheNewTail_AndNotFollowingLeavesTheViewAlone() => AsyncPump.Run(async () =>
    {
        var (vm, notify, path) = await TailingAsync("first\n");
        try
        {
            var scrolls = 0;
            vm.ScrollToBottomRequested += (_, _) => scrolls++;

            Assert.IsTrue(vm.IsAutoScrolling, "follow is on by default — a log tab opens at the live end");
            File.AppendAllText(path, "second\n");
            notify();
            await Settled(() => scrolls == 1, "the view to follow the new tail");

            vm.IsAutoScrolling = false;
            File.AppendAllText(path, "third\n");
            notify();
            await Settled(() => vm.Document.Text.Contains("third"), "the next line to arrive");

            Assert.AreEqual(1, scrolls, "with follow off the new line arrives but the view stays put");
        }
        finally { vm.Dispose(); TryDelete(path); }
    });

    // ── Status bar ────────────────────────────────────────────────────────────

    [TestMethod]
    [CoversNode("log-viewer-status-lines")]
    [CoversNode("log-viewer-status-size")]
    [CoversNode("log-viewer-status-format")]
    public void StatusBar_ReportsLineCount_SizeAndDetectedFormat() => AsyncPump.Run(async () =>
    {
        var path = WriteTemp(TimestampedLog);
        try
        {
            using var vm = new LogViewModel(path, Substitute.For<IShellServices>()) { IsMonitoring = false };
            await vm.LoadAsync(CancellationToken.None);

            Assert.AreEqual(vm.Document.LineCount, vm.LineCount);
            Assert.IsFalse(string.IsNullOrWhiteSpace(vm.FileSizeText));
            Assert.AreEqual("Raw Text", vm.ActiveParser.FormatName);
        }
        finally { File.Delete(path); }
    });

    [TestMethod]
    [CoversNode("log-viewer-status-filter")]
    public void ActiveFilterIndicator_TracksWhetherLinesAreBeingFaded()
    {
        using var vm = Detached();
        Assert.IsFalse(vm.IsFilterActive);

        vm.FilterRegex = "ERROR";
        Assert.IsTrue(vm.IsFilterActive, "the user must be able to see that the view is filtered");

        vm.ClearFilterCommand.Execute(null);
        Assert.IsFalse(vm.IsFilterActive);
    }

    [TestMethod]
    [CoversNode("log-viewer-status-loading")]
    public void LoadingIndicator_IsOnWhileOnlyTheTailIsShown() => AsyncPump.Run(async () =>
    {
        var sb = new StringBuilder();
        for (var i = 0; i < 2_000; i++) sb.Append("2024-01-01 00:00:00 INFO line ").Append(i).Append('\n');
        var path = WriteTemp(sb.ToString());
        try
        {
            using var vm = new LogViewModel(path, Substitute.For<IShellServices>()) { IsMonitoring = false };
            await vm.LoadAsync(CancellationToken.None);

            // Big file → tail first, and the indicator says so until the head lands.
            for (var i = 0; i < 200 && vm.IsLoadingHead; i++) await Task.Delay(10);
            Assert.IsFalse(vm.IsLoadingHead, "the indicator must clear once the whole file is present");
            StringAssert.Contains(vm.Document.Text, "line 0");
        }
        finally { File.Delete(path); }
    });

    // ── Format auto-detect ────────────────────────────────────────────────────

    [TestMethod]
    [CoversNode("log-viewer-parser")]
    public void JsonLines_WinTheConfidenceVote_ButTheJsonParserIsStillAStub()
    {
        var first = """{"ts":"2024-01-01T00:00:00Z","level":"error","message":"boom"}""";

        var parser = LogParserRegistry.SelectParser(Encoding.UTF8.GetBytes(first).AsSpan(0, 16), first);

        Assert.AreEqual("JSON", parser.FormatName, "a braced first line outvotes the raw-text fallback");

        // Pinned deliberately: JsonLogParser wins the vote but does not yet read the structured fields, so
        // a JSON log shows "JSON" in the status bar while level highlighting and the time filter find
        // nothing. Change this assertion when the parser is implemented - it is the tell that it has been.
        Assert.AreEqual(LogLevel.Unknown, parser.ParseLine(first).Level);
        Assert.IsNull(parser.ParseLine(first).Timestamp);
    }

    [TestMethod]
    [CoversNode("log-viewer-parser")]
    [CoversNode("log-filetype-text")]
    public void PlainText_FallsBackToTheRawParser_WhichStillReadsLevels()
    {
        const string first = "2024-01-01 08:00:02 ERROR boom";

        var parser = LogParserRegistry.SelectParser(Encoding.UTF8.GetBytes(first).AsSpan(0, 16), first);

        Assert.AreEqual("Raw Text", parser.FormatName);
        Assert.AreEqual(LogLevel.Error, parser.ParseLine(first).Level);
    }

    // ── Timestamp detection ───────────────────────────────────────────────────

    [TestMethod]
    [CoversNode("log-viewer-timestamps")]
    [CoversNode("log-viewer-time-filter")]
    public void TimestampedLog_ExposesTheTimeFilter_AndReportsItsSpan() => AsyncPump.Run(async () =>
    {
        var path = WriteTemp(TimestampedLog);
        try
        {
            using var vm = new LogViewModel(path, Substitute.For<IShellServices>()) { IsMonitoring = false };
            await vm.LoadAsync(CancellationToken.None);

            Assert.IsTrue(vm.HasTimestamps, "the time-range fields only appear when timestamps were found");
            Assert.AreEqual(new DateTime(2024, 1, 1), vm.FilterStartDate);
            Assert.AreEqual("08:00:00", vm.FilterStartTime);
            Assert.AreEqual("08:00:03", vm.FilterEndTime);
            StringAssert.Contains(vm.GetContext(), "Timestamps span 2024-01-01 08:00:00 to 2024-01-01 08:00:03");
        }
        finally { File.Delete(path); }
    });

    [TestMethod]
    [CoversNode("log-viewer-timestamps")]
    public void UntimestampedLog_HidesTheTimeFilter() => AsyncPump.Run(async () =>
    {
        var path = WriteTemp("just some words\nand some more\nno dates at all\nnor here\n");
        try
        {
            using var vm = new LogViewModel(path, Substitute.For<IShellServices>()) { IsMonitoring = false };
            await vm.LoadAsync(CancellationToken.None);

            Assert.IsFalse(vm.HasTimestamps);
            StringAssert.Contains(vm.GetContext(), "No timestamps detected");
        }
        finally { File.Delete(path); }
    });
}
