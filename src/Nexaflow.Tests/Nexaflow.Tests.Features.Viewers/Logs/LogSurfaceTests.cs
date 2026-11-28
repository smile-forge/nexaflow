using Nexaflow.IO.Common;
using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Nexaflow.Features.Common;
using Nexaflow.Features.Logs.Parsing;
using Nexaflow.Features.Logs.ViewModels;
using Nexaflow.Markdown.Editing;
using Nexaflow.Tests.Features.Infrastructure;
using Nexaflow.Tests.Fixtures;
using NSubstitute;

namespace Nexaflow.Tests.Features.Logs;

/// <summary>
/// The log tab's controls and readouts: the toolbar's severity toggles and term highlight, the gutter selection, the live-tail pause/follow
/// pair, the status-bar readouts, and the parser / timestamp detection behind them.
/// <para>
/// What a line is marked with is the page's: its editor's mark layers answer it for the lines asked, as they do for the lines drawn — which
/// is what decides whether a FATAL line is washed, a filtered-out one veiled, or a term picked out. Where the page is put is asked of whatever
/// shows the editor, which <see cref="ScrollRecorder"/> stands in for.
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
        var path = Path.Combine(TestTemp.Root, $"logsurface_{Guid.NewGuid():N}.log");
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

    /// <summary>A page over no file, showing <paramref name="text"/>.</summary>
    private static LogViewModel Detached(string text = "")
    {
        var vm = new LogViewModel("nonexistent.log", Substitute.For<IShellServices>(), VirtualFileSystem.Instance) { IsMonitoring = false };
        vm.Document.Load(text);
        return vm;
    }

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

    [TestMethod]
    [CoversNode("log-viewer-level-highlight")]
    public void ALevelAskedFor_WashesItsLines_AndOnlyThose()
    {
        using var vm = Detached(TimestampedLog);
        Assert.AreEqual(0, EditorMarks.MarkedBy(vm.Editor, "Log.Error").Count, "nothing is washed until a level is asked for");

        vm.HighlightError = true;

        var line = vm.Document.Current.OffsetOf(2);
        CollectionAssert.AreEqual(new[] { line }, EditorMarks.MarkedBy(vm.Editor, "Log.Error").Select(mark => mark.Start).ToArray(), "the ERROR line, washed");
        Assert.AreEqual(0, EditorMarks.MarkedBy(vm.Editor, "Log.Warning").Count, "and no other level's");

        vm.HighlightError = false;
        Assert.AreEqual(0, EditorMarks.MarkedBy(vm.Editor, "Log.Error").Count);
    }

    // ── Toolbar: custom term highlight ────────────────────────────────────────

    [TestMethod]
    [CoversNode("log-viewer-custom-term")]
    public void CustomTerm_MatchesEveryOccurrence_CaseInsensitively()
    {
        using var vm = Detached("alpha Timeout beta\ntimeout gamma\n");

        vm.CustomHighlightTerm = "timeout";

        var hits = EditorMarks.MarkedBy(vm.Editor, "Log.Term");
        Assert.AreEqual(2, hits.Count, "both casings must highlight");
        Assert.IsTrue(hits.All(h => h.Length == "timeout".Length));
        CollectionAssert.AreEqual(new[] { 6, 19 }, hits.Select(h => h.Start).ToArray());
    }

    [TestMethod]
    [CoversNode("log-viewer-custom-term")]
    public void CustomTerm_Blank_ClearsEveryHighlight()
    {
        using var vm = Detached("timeout\n");
        vm.CustomHighlightTerm = "timeout";
        Assert.AreEqual(1, EditorMarks.MarkedBy(vm.Editor, "Log.Term").Count);

        vm.CustomHighlightTerm = "   ";

        Assert.AreEqual(0, EditorMarks.MarkedBy(vm.Editor, "Log.Term").Count);
    }

    // ── Side panel: the filters fade, never remove ────────────────────────────

    [TestMethod]
    [CoversNode("log-viewer-filter")]
    public void TheRegexFilter_VeilsTheLinesItLeavesOut_AndTheTimeFilterThoseOutsideItsRange()
    {
        using var vm = Detached(TimestampedLog);
        var lines = Enumerable.Range(0, 4).Select(line => vm.Document.Current.OffsetOf(line)).ToArray();

        vm.FilterRegex = "ERROR|WARN";
        CollectionAssert.AreEqual(new[] { lines[0], lines[3] }, EditorMarks.MarkedBy(vm.Editor, "Log.Filter").Select(mark => mark.Start).ToArray(), "the INFO lines, veiled");

        vm.ClearFilterCommand.Execute(null);
        Assert.AreEqual(0, EditorMarks.MarkedBy(vm.Editor, "Log.Filter").Count);

        vm.FilterStart = new DateTime(2024, 1, 1, 8, 0, 1);
        vm.FilterEnd   = new DateTime(2024, 1, 1, 8, 0, 2);
        CollectionAssert.AreEqual(new[] { lines[0], lines[3] }, EditorMarks.MarkedBy(vm.Editor, "Log.Filter").Select(mark => mark.Start).ToArray(), "the lines stamped outside the range");
        Assert.AreEqual(TimestampedLog, vm.Document.ToString(), "faded, never removed");
    }

    // ── Gutter selection + the Copy button that consumes it ───────────────────

    [TestMethod]
    [CoversNode("log-viewer-line-select")]
    public void ClickingTheGutter_TogglesThatLine_AndOnlyThatLine()
    {
        using var vm = Detached(TimestampedLog);

        vm.ToggleLineSelection(2);
        vm.ToggleLineSelection(4);

        CollectionAssert.AreEquivalent(new long[] { 2, 4 }, vm.SelectedLineNumbers.ToArray());
        Assert.AreEqual(2, vm.SelectedLineCount);
        CollectionAssert.AreEqual(new[] { vm.Document.Current.OffsetOf(1), vm.Document.Current.OffsetOf(3) },
                                  EditorMarks.MarkedBy(vm.Editor, "Log.SelectionLine").Select(mark => mark.Start).ToArray(), "each line picked out, washed");

        vm.ToggleLineSelection(2);   // clicking the same line again de-selects it

        CollectionAssert.AreEquivalent(new long[] { 4 }, vm.SelectedLineNumbers.ToArray());
        Assert.AreEqual(1, vm.SelectedLineCount);
    }

    [TestMethod]
    [CoversNode("log-viewer-copy-selected")]
    public void CopyButton_EnablesOnlyWhileLinesAreSelected()
    {
        using var vm = Detached(TimestampedLog);
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

    [TestMethod]
    [CoversNode("log-viewer-encoding")]
    public void ChoosingAnEncoding_ReadsTheLogAgainInIt() => AsyncPump.Run(async () =>
    {
        var path = Path.Combine(TestTemp.Root, $"logsurface_{Guid.NewGuid():N}.log");
        File.WriteAllBytes(path, Encoding.Latin1.GetBytes("INFO café\n"));
        try
        {
            using var vm = new LogViewModel(path, Substitute.For<IShellServices>(), VirtualFileSystem.Instance) { IsMonitoring = false };
            await vm.LoadAsync(CancellationToken.None);

            vm.SelectedEncoding = vm.AvailableEncodings.First(e => e.Name == "UTF-8");
            await Settled(() => vm.Document.ToString().Contains('�'), "the log read again as UTF-8, where 0xE9 is no character");

            vm.SelectedEncoding = vm.AvailableEncodings.First(e => e.Name == "Latin-1");
            await Settled(() => vm.Document.ToString().Contains("café"), "and again as Latin-1");
        }
        finally { TryDelete(path); }
    });

    // ── Live tail: watch, pause, follow ───────────────────────────────────────

    /// <summary>
    /// Loads a file, captures the change callback the shell wired up, appends to the file and fires it — the same sequence the shell's
    /// watcher drives, without a real <c>FileSystemWatcher</c>.
    /// </summary>
    private static async Task<(LogViewModel Vm, Action Notify, string Path)> TailingAsync(string initial)
    {
        var path = WriteTemp(initial);
        var shell = Substitute.For<IShellServices>();
        Action? onChanged = null;
        shell.WatchFile(Arg.Any<string>(), Arg.Do<Action>(a => onChanged = a))
             .Returns(Substitute.For<IFileWatch>());

        var vm = new LogViewModel(path, shell, VirtualFileSystem.Instance);
        await vm.LoadAsync(CancellationToken.None);

        Assert.IsNotNull(onChanged, "the viewer must ask the shell to watch the file it loaded");
        return (vm, onChanged!, path);
    }

    /// <summary>
    /// The change handler reads the appended bytes off the page, so a notification is observed a moment after it is raised. Pumps until
    /// the effect lands rather than assuming one turn is enough.
    /// </summary>
    private static async Task Settled(Func<bool> until, string what)
    {
        for (var i = 0; i < 300 && !until(); i++) await Task.Delay(10);
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
            await Settled(() => vm.Document.ToString().Contains("second"), "the appended line to arrive");

            Assert.AreEqual("first\nsecond\n", vm.Document.ToString(), "only the newly-written bytes are added, so nothing is read twice");
            Assert.IsFalse(vm.Document.CanUndo, "what a writer adds is no step to take back");
        }
        finally { vm.Dispose(); TryDelete(path); }
    });

    [TestMethod]
    [CoversNode("log-viewer-watch")]
    public void ALogCutShorter_IsReadAgainFromTheStart() => AsyncPump.Run(async () =>
    {
        var (vm, notify, path) = await TailingAsync("first line of the old log\nsecond line of the old log\n");
        try
        {
            File.WriteAllText(path, "rotated\n", new UTF8Encoding(false));
            notify();
            await Settled(() => vm.Document.ToString() == "rotated\n", "the rotated log to be read again");
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
            // The size readout still tracks the growing file while paused — that's the signal the notification has been handled, so the
            // assertion below isn't just winning a race.
            await Settled(() => vm.FileSizeText != sizeBefore, "the paused tail to notice the file grew");

            Assert.IsFalse(vm.Document.ToString().Contains("while-paused"), "a paused tail must not move the view under the user");

            vm.IsPaused = false;   // resuming takes up what arrived meanwhile, still on disk

            await Settled(() => vm.Document.ToString().Contains("while-paused"), "the held lines to arrive on resuming");
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
            var page = new ScrollRecorder(vm.Editor);

            Assert.IsTrue(vm.IsAutoScrolling, "follow is on by default — a log tab opens at the live end");
            File.AppendAllText(path, "second\n");
            notify();
            await Settled(() => page.Shown.Contains((2, ShownPlace.Nearest)), "the view to follow the new tail");

            var asked = page.Shown.Count;
            vm.IsAutoScrolling = false;
            File.AppendAllText(path, "third\n");
            notify();
            await Settled(() => vm.Document.ToString().Contains("third"), "the next line to arrive");

            Assert.AreEqual(asked, page.Shown.Count, "with follow off the new line arrives but the view stays put");
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
            using var vm = new LogViewModel(path, Substitute.For<IShellServices>(), VirtualFileSystem.Instance) { IsMonitoring = false };
            await vm.LoadAsync(CancellationToken.None);

            Assert.AreEqual(vm.Document.Current.LineCount, vm.LineCount);
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
            using var vm = new LogViewModel(path, Substitute.For<IShellServices>(), VirtualFileSystem.Instance, ContentFileWindow.DefaultSize, tailBytes: 4096) { IsMonitoring = false };
            await vm.LoadAsync(CancellationToken.None);

            // Tail first, and the indicator says so until the history lands.
            Assert.IsTrue(vm.IsLoadingHead, "only the end of the log is shown at first");
            Assert.IsFalse(vm.Document.ToString().Contains("INFO line 0\n"));

            await Settled(() => !vm.IsLoadingHead, "the history to land");
            StringAssert.StartsWith(vm.Document.ToString(), "2024-01-01 00:00:00 INFO line 0\n", "the whole file is present");
            Assert.AreEqual(sb.ToString(), vm.Document.ToString());
        }
        finally { File.Delete(path); }
    });

    // ── Format auto-detect ────────────────────────────────────────────────────

    [TestMethod]
    [CoversNode("log-viewer-parser")]
    [CoversNode("log-filetype-json")]
    public void AStructuredLogIsReadAsJson_LevelsAndAll() => AsyncPump.Run(async () =>
    {
        var path = Path.Combine(TestSampleData.Path("logs"), "app_structured.log");
        using var vm = new LogViewModel(path, Substitute.For<IShellServices>(), VirtualFileSystem.Instance) { IsMonitoring = false };
        await vm.LoadAsync(CancellationToken.None);

        Assert.AreEqual("JSON", vm.ActiveParser.FormatName, "the status bar names the structured format");

        // The whole point: the severity toggles and the time filter have something to act on. A JSON log carries its timestamp in a field,
        // so a text-shaped probe would find none and hide the filter.
        Assert.IsTrue(vm.HasTimestamps, "the time-range fields must appear for a structured log too");
        StringAssert.Contains(vm.GetContext(), "Timestamps span");

        var levels = vm.Document.ToString().Split('\n')
            .Select(text => text.TrimEnd('\r'))
            .Where(text => text.Length > 0)          // the file's trailing newline leaves one empty line
            .Select(text => vm.ActiveParser.ParseLine(text).Level)
            .ToArray();

        CollectionAssert.Contains(levels, LogLevel.Error);
        CollectionAssert.Contains(levels, LogLevel.Warning);
        CollectionAssert.Contains(levels, LogLevel.Fatal);
        CollectionAssert.Contains(levels, LogLevel.Info, "Serilog omits the level for Information lines");
        CollectionAssert.DoesNotContain(levels, LogLevel.Unknown, "every written line should be readable");
    });

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
            using var vm = new LogViewModel(path, Substitute.For<IShellServices>(), VirtualFileSystem.Instance) { IsMonitoring = false };
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
    [CoversNode("log-viewer-time-filter")]
    public void ApplyingATimeRange_GoesToTheFirstLineInsideIt() => AsyncPump.Run(async () =>
    {
        var path = WriteTemp(TimestampedLog);
        try
        {
            using var vm = new LogViewModel(path, Substitute.For<IShellServices>(), VirtualFileSystem.Instance) { IsMonitoring = false };
            await vm.LoadAsync(CancellationToken.None);
            var page = new ScrollRecorder(vm.Editor);

            vm.FilterStartTime = "08:00:02";
            await vm.ApplyTimeFilterCommand.ExecuteAsync(null);

            Assert.AreEqual(2, vm.Document.Current.LineOf(vm.Editor.Caret), "the caret on the ERROR line, the first stamped 08:00:02 or later");
            Assert.AreEqual((2, ShownPlace.Top), page.Shown[^1]);
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
            using var vm = new LogViewModel(path, Substitute.For<IShellServices>(), VirtualFileSystem.Instance) { IsMonitoring = false };
            await vm.LoadAsync(CancellationToken.None);

            Assert.IsFalse(vm.HasTimestamps);
            StringAssert.Contains(vm.GetContext(), "No timestamps detected");
        }
        finally { File.Delete(path); }
    });
}
