using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Text.Json.Nodes;
using Nexaflow.Features.Common;
using Nexaflow.Features.Text.ViewModels;
using Nexaflow.IO.Common;
using Nexaflow.Tests.Features.Infrastructure;
using Nexaflow.Tests.Fixtures;
using NSubstitute;

namespace Nexaflow.Tests.Features.Text;

/// <summary>
/// Covers the windowed text reader/editor: the small-file fast path and the large-file path (sparse line
/// index + two-sided placeholder padding + a sliding window of real content), plus edit dirty-tracking.
/// Runs under <see cref="AsyncPump"/> because loading mutates a thread-affine AvalonEdit
/// <c>TextDocument</c> across <c>await</c> points.
/// </summary>
[TestClass]
[CoversNode("text-viewer")]
public class TextViewModelTests
{
    private static string WriteTemp(string content)
    {
        var path = Path.Combine(Path.GetTempPath(), $"textvm_{Guid.NewGuid():N}.txt");
        File.WriteAllText(path, content, new UTF8Encoding(false));
        return path;
    }

    private static string WriteManyLines(out int lineCount)
    {
        lineCount = 10_000;
        var lines = new string[lineCount];
        for (var i = 0; i < lines.Length; i++)
            lines[i] = $"Line {i:D5}: the quick brown fox jumps over the lazy dog";
        return WriteTemp(string.Join("\n", lines)); // no trailing newline → exactly 10,000 lines
    }

    [TestMethod]
    [CoversNode("text-viewer-status-filesize")]
    [CoversNode("text-viewer-status-linecount")]
    public void LoadAsync_SmallFile_LoadsWholeContent() => AsyncPump.Run(async () =>
    {
        var path = WriteTemp("line one\nline two\nline three");
        try
        {
            using var vm = new TextViewModel(path, Substitute.For<IShellServices>()) { IsMonitoring = false };
            await vm.LoadAsync(CancellationToken.None);

            Assert.IsFalse(vm.IsLargeFile);
            Assert.AreEqual("line one\nline two\nline three", vm.Document.Text);
            Assert.AreEqual(3, vm.LineCount);                                    // status-bar Line Count
            Assert.IsFalse(string.IsNullOrWhiteSpace(vm.FileSizeText), "status bar shows the file size");
            StringAssert.Contains(vm.FileSizeText, "B");                         // e.g. "28 B"
        }
        finally { File.Delete(path); }
    });

    [TestMethod]
    [CoversNode("text-viewer-ai-preview")]
    public void SecurityContext_IsTheFilePath_AndOffersAContextPreview() => AsyncPump.Run(async () =>
    {
        var path = WriteTemp("alpha\nbeta\ngamma\n");
        try
        {
            using var vm = new TextViewModel(path, Substitute.For<IShellServices>()) { IsMonitoring = false };
            await vm.LoadAsync(CancellationToken.None);

            // Aspect 4: two Text tabs on different files must be distinguishable when pinned together, so
            // their identically-named tools don't collapse first-wins in MultiContextClientTool.
            Assert.AreEqual(path, vm.GetSecurityContext());

            // The page advertises a read-only context preview for the conversation panel.
            Assert.IsInstanceOfType(vm, typeof(IContextPreview));
        }
        finally { File.Delete(path); }
    });

    /// <summary>A shell whose RunOnUiAsync actually runs the delegate — the substitute's default swallows it,
    /// silently no-opping every UI-marshalled tool path (read/replace/save all funnel through it).</summary>
    private static IShellServices RunningShell()
    {
        var shell = Substitute.For<IShellServices>();
        shell.RunOnUiAsync(Arg.Any<Action>())
             .Returns(ci => { ci.Arg<Action>()(); return Task.CompletedTask; });
        shell.RunOnUiAsync(Arg.Any<Func<Task<string>>>())
             .Returns(ci => ci.Arg<Func<Task<string>>>()());
        return shell;
    }

    [TestMethod]
    [CoversNode("text-viewer-ai-act")]
    [CoversNode("text-viewer-ai-context")]
    public void AiTools_ReadReplaceSave_ThroughClientToolSurface() => AsyncPump.Run(async () =>
    {
        var path = WriteTemp("alpha\nbeta\ngamma\n");
        try
        {
            using var vm = new TextViewModel(path, RunningShell()) { IsMonitoring = false };
            await vm.LoadAsync(CancellationToken.None);

            // Context is honest about the file the tools act on.
            StringAssert.Contains(vm.GetContext(), vm.FileName);

            // Exercise the AI act surface exactly as the conversation hub does — via GetClientTools(),
            // not the VM's internal methods — so the tools' arg-parsing and wiring are covered too.
            var tools = vm.GetClientTools();
            CollectionAssert.AreEquivalent(
                new[] { "copy_visible_text", "read_lines", "find_text", "edit_lines", "replace_in_range", "replace_all", "save_file" },
                tools.Select(t => t.Name).ToArray(),
                "the Text AI act tool surface changed — update the tree's text-viewer-ai-act leaves to match");

            // read_lines: numbered, reflects current content
            var read = tools.Single(t => t.Name == "read_lines");
            var r = await read.InvokeAsync(new JsonObject { ["start_line"] = 1, ["count"] = 3 }, CancellationToken.None);
            Assert.IsFalse(r.IsError);
            StringAssert.Contains(r.ModelText, "alpha");
            StringAssert.Contains(r.ModelText, "gamma");

            // replace_all: whole-file find/replace (case-insensitive default), marks the document dirty
            var replace = tools.Single(t => t.Name == "replace_all");
            var rep = await replace.InvokeAsync(new JsonObject { ["find"] = "beta", ["replace"] = "BETA" }, CancellationToken.None);
            Assert.IsFalse(rep.IsError);
            StringAssert.Contains(vm.Document.Text, "BETA");
            Assert.IsTrue(vm.IsDirty);

            // edit_lines: replace a line range in place (new_text keeps its trailing newline → lines stay split)
            var edit = tools.Single(t => t.Name == "edit_lines");
            await edit.InvokeAsync(new JsonObject { ["start_line"] = 1, ["end_line"] = 1, ["new_text"] = "ALPHA\n" }, CancellationToken.None);
            StringAssert.Contains(vm.Document.Text, "ALPHA\nBETA");   // still three lines, line 1 replaced

            // replace_in_range: find/replace confined to a line range
            var range = tools.Single(t => t.Name == "replace_in_range");
            var rr = await range.InvokeAsync(new JsonObject { ["start_line"] = 3, ["end_line"] = 3, ["find"] = "gamma", ["replace"] = "GAMMA" }, CancellationToken.None);
            Assert.IsFalse(rr.IsError);
            StringAssert.Contains(vm.Document.Text, "GAMMA");

            // save_file: takes the save branch on a dirty document (persistence itself is covered by the
            // direct-save tests above; the tool's write is fire-and-forget through the dispatcher).
            var save = tools.Single(t => t.Name == "save_file");
            var s = await save.InvokeAsync(new JsonObject(), CancellationToken.None);
            Assert.IsFalse(s.IsError);
            Assert.AreEqual("saved", s.Summary);
        }
        finally { File.Delete(path); }
    });

    [TestMethod]
    [CoversNode("text-viewer-streaming-indicator")]
    public void LoadAsync_LargeFile_IndexesLineCountAndWindowsFromTop() => AsyncPump.Run(async () =>
    {
        var path = WriteManyLines(out var lineCount);
        try
        {
            using var vm = new TextViewModel(path, Substitute.For<IShellServices>()) { IsMonitoring = false };
            await vm.LoadAsync(CancellationToken.None);

            Assert.IsTrue(vm.IsLargeFile);   // drives the status-bar 'Streaming' badge
            Assert.AreEqual(lineCount, vm.LineCount, "the index counts every line up front");
            Assert.AreEqual(lineCount, vm.Document.LineCount, "placeholder padding preserves the scrollbar coordinate space");

            StringAssert.Contains(vm.Document.Text, "Line 00000");                  // first window is real
            Assert.IsFalse(vm.Document.Text.Contains("Line 09999"), "the tail is still placeholder");
        }
        finally { File.Delete(path); }
    });

    [TestMethod]
    [CoversNode("text-viewer-windowing")]
    public void EnsureWindow_SlidesToViewportDeepInFile() => AsyncPump.Run(async () =>
    {
        var path = WriteManyLines(out _);
        try
        {
            using var vm = new TextViewModel(path, Substitute.For<IShellServices>()) { IsMonitoring = false };
            await vm.LoadAsync(CancellationToken.None);
            Assert.IsFalse(vm.Document.Text.Contains("Line 08000"), "line 8000 starts out as placeholder");

            await vm.EnsureWindowAsync(8000, 8050);

            StringAssert.Contains(vm.Document.Text, "Line 08000");                  // now real content
            Assert.AreEqual(10_000, vm.Document.LineCount, "total line count is unchanged by sliding");
        }
        finally { File.Delete(path); }
    });

    [TestMethod]
    [CoversNode("text-viewer-find")]
    [CoversNode("text-viewer-status-matchcount")]
    [CoversNode("text-viewer-search-indicator")]
    public void Search_LargeFile_FindsMatchesAcrossTheWholeFile() => AsyncPump.Run(async () =>
    {
        var path = WriteManyLines(out _);
        try
        {
            using var vm = new TextViewModel(path, Substitute.For<IShellServices>()) { IsMonitoring = false };
            await vm.LoadAsync(CancellationToken.None);

            await vm.SearchConventionalAsync("Line 07777");

            Assert.IsTrue(vm.IsSearchActive);
            Assert.AreEqual(1, vm.SearchMatchCount);                             // status-bar Match Count
            Assert.AreEqual("Line 07777", vm.CurrentSearchTerm);                 // search-bar Search Indicator
            StringAssert.Contains(vm.Document.Text, "Line 07777", "search navigated/slid the window to the match");
        }
        finally { File.Delete(path); }
    });

    [TestMethod]
    [CoversNode("text-viewer-func-editing")]
    public void LargeFile_EditAndSave_PersistsViaStreamingMerge() => AsyncPump.Run(async () =>
    {
        var path = WriteManyLines(out _);
        try
        {
            using var vm = new TextViewModel(path, Substitute.For<IShellServices>()) { IsMonitoring = false };
            await vm.LoadAsync(CancellationToken.None);

            vm.IsEditing = true;
            vm.OnUserEdit(0, 0, "ZZZ"); // insert at the very start of the window (the view forwards this)
            Assert.IsTrue(vm.IsDirty);

            await vm.SaveCommand.ExecuteAsync(null);

            Assert.IsFalse(vm.IsDirty, "save clears the dirty flag");
            var first = File.ReadLines(path).First();
            StringAssert.StartsWith(first, "ZZZLine 00000", "the edit merged into the saved file");
            Assert.AreEqual(10_000, vm.LineCount, "line count is intact after save + reload");
        }
        finally { File.Delete(path); }
    });

    [TestMethod]
    [CoversNode("text-viewer-save")]
    public void SmallFile_Edit_MarksDirtyAndSaves() => AsyncPump.Run(async () =>
    {
        var path = WriteTemp("alpha\nbeta\ngamma");
        try
        {
            var shell = Substitute.For<IShellServices>();
            using var vm = new TextViewModel(path, shell) { IsMonitoring = false };
            await vm.LoadAsync(CancellationToken.None);

            bool dirtyRaised = false;
            vm.DirtyChanged += d => dirtyRaised = d;

            vm.IsEditing = true;
            vm.Document.Insert(0, "X"); // a real Document edit raises Document.Changed in the view; here drive directly
            vm.OnUserEdit(0, 0, "X");

            Assert.IsTrue(vm.IsDirty);
            Assert.IsTrue(dirtyRaised);
            Assert.IsTrue(vm.SaveCommand.CanExecute(null), "Save is enabled once the document is dirty");

            await vm.SaveCommand.ExecuteAsync(null);

            Assert.IsFalse(vm.IsDirty, "saving clears the dirty flag");
            StringAssert.StartsWith(File.ReadAllText(path), "Xalpha", "the edit was persisted to disk");
        }
        finally { File.Delete(path); }
    });

    [TestMethod]
    [CoversNode("text-viewer-find-nav")]
    [CoversNode("text-viewer-highlights")]
    [CoversNode("text-viewer-minimap")]
    public void FindNextPrevious_MovesMatchCursor_AndFeedsHighlightsAndMinimap() => AsyncPump.Run(async () =>
    {
        var path = WriteTemp("foo a\nbar b\nfoo c\nbar d\nfoo e"); // "foo" on lines 1, 3, 5
        try
        {
            using var vm = new TextViewModel(path, Substitute.For<IShellServices>()) { IsMonitoring = false };
            await vm.LoadAsync(CancellationToken.None);

            await vm.SearchConventionalAsync("foo");
            Assert.IsTrue(vm.IsSearchActive);
            Assert.AreEqual(3, vm.SearchMatchCount);
            Assert.IsTrue(vm.SearchHighlights.Count > 0, "matches in the resident window are highlighted");
            Assert.AreEqual(3, vm.MiniMapMarks.Count, "one minimap mark per matching line across the file");

            int LineAt(int offset) => vm.Document.GetLineByOffset(offset).LineNumber;
            Assert.AreEqual(1, LineAt(vm.ScrollToOffset), "the first match centres on line 1");

            vm.CurrentCaretOffset = vm.ScrollToOffset;
            await vm.FindNextCommand.ExecuteAsync(null);
            Assert.AreEqual(3, LineAt(vm.ScrollToOffset), "Find Next advances to the next matching line");

            vm.CurrentCaretOffset = vm.ScrollToOffset;
            await vm.FindPreviousCommand.ExecuteAsync(null);
            Assert.AreEqual(1, LineAt(vm.ScrollToOffset), "Find Previous steps back to the earlier matching line");
        }
        finally { File.Delete(path); }
    });

    [TestMethod]
    [CoversNode("text-viewer-func-encoding-detect")]
    public void LoadAsync_DetectsEncodingFromBom() => AsyncPump.Run(async () =>
    {
        // A UTF-16 LE file (with BOM) whose bytes are garbage under the default UTF-8 selection —
        // the loader must honour the byte-order mark and decode it correctly regardless.
        var path = Path.Combine(Path.GetTempPath(), $"textvm_{Guid.NewGuid():N}.txt");
        File.WriteAllText(path, "héllo\nwörld", Encoding.Unicode);
        try
        {
            using var vm = new TextViewModel(path, Substitute.For<IShellServices>()) { IsMonitoring = false };
            Assert.AreEqual("UTF-8", vm.SelectedEncoding.Name); // selection is the default; the BOM overrides it
            await vm.LoadAsync(CancellationToken.None);

            Assert.AreEqual("héllo\nwörld", vm.Document.Text, "the UTF-16 BOM was auto-detected on load");
        }
        finally { File.Delete(path); }
    });

    [TestMethod]
    [CoversNode("text-viewer-encoding")]
    public void SelectedEncoding_Change_ReDecodesFile() => AsyncPump.Run(async () =>
    {
        // Byte 0xE9 is 'é' in Latin-1 but an invalid lead byte in UTF-8 (→ replacement char).
        var path = Path.Combine(Path.GetTempPath(), $"textvm_{Guid.NewGuid():N}.txt");
        File.WriteAllBytes(path, Encoding.Latin1.GetBytes("café\nline two"));
        try
        {
            using var vm = new TextViewModel(path, Substitute.For<IShellServices>()) { IsMonitoring = false };
            await vm.LoadAsync(CancellationToken.None);
            Assert.IsFalse(vm.Document.Text.Contains("café"), "under the default UTF-8, 0xE9 mangles");

            // Switching the selector re-reads the file (fire-and-forget reload); poll until it lands.
            vm.SelectedEncoding = vm.AvailableEncodings.First(e => e.Name == "Latin-1");
            await WaitUntilAsync(() => vm.Document.Text.Contains("café"),
                "changing the encoding selector re-decodes the file as Latin-1");

            Assert.IsTrue(vm.Document.Text.Contains("café"));
        }
        finally { File.Delete(path); }
    });

    [TestMethod]
    [CoversNode("text-viewer-func-monitoring")]
    [CoversNode("text-viewer-file-banner")]
    public void FileMonitoring_OnDiskChange_RaisesReloadBanner() => AsyncPump.Run(async () =>
    {
        var path = WriteTemp("original\ncontent");
        try
        {
            var shell = Substitute.For<IShellServices>();
            var watch = Substitute.For<IFileWatch>();
            Action? onChanged = null;
            shell.WatchFile(Arg.Any<string>(), Arg.Do<Action>(a => onChanged = a)).Returns(watch);

            using var vm = new TextViewModel(path, shell) { IsMonitoring = true };
            await vm.LoadAsync(CancellationToken.None); // monitoring on → StartMonitoring registers the watch

            Assert.IsNotNull(onChanged, "monitoring registered a file watch");
            Assert.IsFalse(vm.FileChangedBannerVisible);

            File.WriteAllText(path, "updated\ncontent\nthird"); // the file mutates on disk
            onChanged!();                                       // the shell's debounced watcher fires

            Assert.IsTrue(vm.FileChangedBannerVisible, "a disk change raises the file-changed banner");

            // Let the full change→reload→auto-hide cycle finish before the pump completes, so no
            // continuation is posted after the sync-context closes.
            for (int i = 0; i < 500 && vm.FileChangedBannerVisible; i++) await Task.Delay(20);
            Assert.IsFalse(vm.FileChangedBannerVisible, "the banner auto-hides after the reload settles");
            StringAssert.Contains(vm.Document.Text, "updated", "the reload pulled the new on-disk content");
        }
        finally { File.Delete(path); }
    });

    [TestMethod]
    [CoversNode("text-viewer-split-run")]
    [CoversNode("text-viewer-func-splitting")]
    public async Task Split_QueuesTask_ThatSplitsFileIntoSiblingParts()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"textvm_split_{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, "big.txt");
        File.WriteAllLines(path, Enumerable.Range(0, 20).Select(i => $"line {i}"));
        try
        {
            var shell = Substitute.For<IShellServices>();
            IBackgroundTask? queued = null;
            Action<bool>? onComplete = null;
            shell.When(s => s.QueueBackgroundTask(Arg.Any<IBackgroundTask>(), Arg.Any<Action<bool>>(), Arg.Any<CancellationToken>()))
                 .Do(ci => { queued = ci.Arg<IBackgroundTask>(); onComplete = ci.Arg<Action<bool>>(); });

            using var vm = new TextViewModel(path, shell) { IsMonitoring = false };
            vm.SelectedSplitMode = vm.SplitModes.First(m => m.Mode == SplitMode.ByLineCount);
            vm.SplitValue = "5";
            vm.IsSplitPanelOpen = true;

            vm.SplitCommand.Execute(null);

            Assert.IsFalse(vm.IsSplitPanelOpen, "running a split closes the panel");
            shell.Received(1).QueueBackgroundTask(Arg.Any<IBackgroundTask>(), Arg.Any<Action<bool>>(), Arg.Any<CancellationToken>());
            Assert.IsNotNull(queued);

            await queued!.RunAsync(CancellationToken.None); // the split actually runs off the UI thread
            onComplete?.Invoke(true);                        // the shell reports completion

            var parts = Directory.GetFiles(dir, "big.part*.txt");
            Assert.AreEqual(4, parts.Length, "20 lines / 5 per part → 4 sibling part files");
            shell.Received().ShowNotification(Arg.Is<string>(m => m.Contains("Split")));
        }
        finally { try { Directory.Delete(dir, recursive: true); } catch { } }
    }

    private static async Task WaitUntilAsync(Func<bool> condition, string because)
    {
        for (int i = 0; i < 300 && !condition(); i++) await Task.Delay(10);
        Assert.IsTrue(condition(), because);
    }
}
