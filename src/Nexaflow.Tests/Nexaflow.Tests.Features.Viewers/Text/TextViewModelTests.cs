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
using Nexaflow.Markdown.Editing;
using Nexaflow.Tests.Features.Infrastructure;
using Nexaflow.Tests.Fixtures;
using NSubstitute;

namespace Nexaflow.Tests.Features.Text;

/// <summary>
/// The Text viewer over a file on disk: read as one document over its blocks, or — past the window it is given, kept small here so a test
/// file is too long for one — through a window onto it that moves where the reader goes, its lines numbered as lines of the whole file;
/// what is written held over the file until it is saved, and the file held against other writers while it is edited. Runs under
/// <see cref="AsyncPump"/>, so what the page awaits comes back to one thread, as it does to the page's own.
/// </summary>
[TestClass]
[CoversNode("text-viewer")]
public class TextViewModelTests
{
    /// <summary>A window far shorter than <see cref="WriteManyLines"/>'s file, so that file is shown a stretch at a time.</summary>
    private const int SmallWindow = 200_000;

    private static string WriteTemp(string content)
    {
        var path = Path.Combine(TestTemp.Root, $"textvm_{Guid.NewGuid():N}.txt");
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

    /// <summary>Which line of the whole file the caret is on, counting from nought.</summary>
    private static long CaretLine(TextViewModel vm) => vm.FirstLineNumber - 1 + vm.Document.Current.LineOf(vm.Editor.Caret);

    /// <summary>A shell whose go-to-line prompt answers with what the test gives the function it hands back.</summary>
    private static (IShellServices Shell, Action<string> GoTo) PromptingShell()
    {
        var shell = Substitute.For<IShellServices>();
        Action<string>? confirm = null;
        shell.When(s => s.ShowPrompt(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<Action<string>>(), Arg.Any<Action>()))
             .Do(ci => confirm = ci.Arg<Action<string>>());

        return (shell, line => confirm!(line));
    }

    [TestMethod]
    [CoversNode("text-viewer-status-filesize")]
    [CoversNode("text-viewer-status-linecount")]
    public void LoadAsync_SmallFile_LoadsWholeContent() => AsyncPump.Run(async () =>
    {
        var path = WriteTemp("line one\nline two\nline three");
        try
        {
            using var vm = new TextViewModel(path, Substitute.For<IShellServices>(), VirtualFileSystem.Instance) { IsMonitoring = false };
            await vm.LoadAsync(CancellationToken.None);

            Assert.IsFalse(vm.IsLargeFile);
            Assert.AreEqual("line one\nline two\nline three", vm.Document.ToString());
            Assert.AreEqual(3, vm.LineCount);                                    // status-bar Line Count
            Assert.AreEqual(1, vm.FirstLineNumber);
            Assert.IsFalse(vm.Document.IsDirty, "what was read is what is saved");
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
            using var vm = new TextViewModel(path, Substitute.For<IShellServices>(), VirtualFileSystem.Instance) { IsMonitoring = false };
            await vm.LoadAsync(CancellationToken.None);

            // Two Text tabs on different files must be distinguishable when pinned together, so their identically-named tools don't
            // collapse first-wins in MultiContextClientTool.
            Assert.AreEqual(path, vm.GetSecurityContext());
            Assert.IsInstanceOfType(vm, typeof(IContextPreview));
        }
        finally { File.Delete(path); }
    });

    [TestMethod]
    [CoversNode("text-viewer-ai-act")]
    [CoversNode("text-viewer-ai-context")]
    public void AiTools_ReadReplaceSave_ThroughClientToolSurface() => AsyncPump.Run(async () =>
    {
        var path = WriteTemp("alpha\nbeta\ngamma\n");
        try
        {
            using var vm = new TextViewModel(path, UiRunningShell.Over(Substitute.For<IShellServices>()), VirtualFileSystem.Instance) { IsMonitoring = false };
            await vm.LoadAsync(CancellationToken.None);

            StringAssert.Contains(vm.GetContext(), vm.FileName);

            // Exercised exactly as the conversation hub does — through GetClientTools(), not the view-model's own methods — so the
            // tools' argument parsing and wiring are covered too.
            var tools = vm.GetClientTools();
            CollectionAssert.AreEquivalent(
                new[] { "copy_visible_text", "read_lines", "find_text", "edit_lines", "replace_in_range", "replace_all", "save_file" },
                tools.Select(t => t.Name).ToArray(),
                "the Text AI act tool surface changed — update the tree's text-viewer-ai-act leaves to match");

            var read = await tools.Single(t => t.Name == "read_lines").InvokeAsync(new JsonObject { ["start_line"] = 1, ["count"] = 3 }, CancellationToken.None);
            Assert.IsFalse(read.IsError);
            StringAssert.Contains(read.ModelText, "1\talpha");
            StringAssert.Contains(read.ModelText, "3\tgamma");

            var found = await tools.Single(t => t.Name == "find_text").InvokeAsync(new JsonObject { ["query"] = "gamma" }, CancellationToken.None);
            Assert.IsFalse(found.IsError);
            StringAssert.Contains(found.ModelText, "line 3");

            var replaced = await tools.Single(t => t.Name == "replace_all").InvokeAsync(new JsonObject { ["find"] = "beta", ["replace"] = "BETA" }, CancellationToken.None);
            Assert.IsFalse(replaced.IsError);
            Assert.IsTrue(vm.IsEditing, "a tool that writes starts editing");
            StringAssert.Contains(vm.Document.ToString(), "BETA");
            Assert.IsTrue(vm.IsDirty);

            await tools.Single(t => t.Name == "edit_lines").InvokeAsync(new JsonObject { ["start_line"] = 1, ["end_line"] = 1, ["new_text"] = "ALPHA\n" }, CancellationToken.None);
            Assert.AreEqual("ALPHA\nBETA\ngamma\n", vm.Document.ToString(), "line 1 replaced, the lines still apart");

            var ranged = await tools.Single(t => t.Name == "replace_in_range").InvokeAsync(new JsonObject { ["start_line"] = 3, ["end_line"] = 3, ["find"] = "gamma", ["replace"] = "GAMMA" }, CancellationToken.None);
            Assert.IsFalse(ranged.IsError);
            Assert.AreEqual("ALPHA\nBETA\nGAMMA\n", vm.Document.ToString());

            // Every write the tools made is the reader's to take back.
            Assert.IsTrue(vm.Document.CanUndo);

            var save = await tools.Single(t => t.Name == "save_file").InvokeAsync(new JsonObject(), CancellationToken.None);
            Assert.IsFalse(save.IsError);
            Assert.AreEqual("saved", save.Summary);
        }
        finally { File.Delete(path); }
    });

    [TestMethod]
    [CoversNode("text-viewer-streaming-indicator")]
    public void LoadAsync_FileLongerThanItsWindow_CountsEveryLine_AndShowsTheTop() => AsyncPump.Run(async () =>
    {
        var path = WriteManyLines(out var lineCount);
        try
        {
            using var vm = new TextViewModel(path, Substitute.For<IShellServices>(), VirtualFileSystem.Instance, SmallWindow) { IsMonitoring = false };
            await vm.LoadAsync(CancellationToken.None);

            Assert.IsTrue(vm.IsLargeFile);   // drives the status-bar 'Streaming' badge
            Assert.AreEqual(lineCount, vm.LineCount, "the whole file's lines are counted, not the window's");
            Assert.AreEqual(1, vm.FirstLineNumber);
            Assert.IsTrue(vm.Document.Current.LineCount < lineCount / 2, "the document holds its window, not the file");

            StringAssert.Contains(vm.Document.ToString(), "Line 00000");
            Assert.IsFalse(vm.Document.ToString().Contains("Line 09999"), "the end of the file is not in the window yet");
        }
        finally { File.Delete(path); }
    });

    [TestMethod]
    [CoversNode("text-viewer-windowing")]
    [CoversNode("text-viewer-goto-line")]
    public void GoToLine_DeepInALongFile_MovesTheWindowThere_NumberingLinesAsTheWholeFiles() => AsyncPump.Run(async () =>
    {
        var path = WriteManyLines(out _);
        try
        {
            var (shell, goTo) = PromptingShell();
            using var vm = new TextViewModel(path, shell, VirtualFileSystem.Instance, SmallWindow) { IsMonitoring = false };
            await vm.LoadAsync(CancellationToken.None);
            Assert.IsFalse(vm.Document.ToString().Contains("Line 08000"));

            vm.GoToLineCommand.Execute(null);
            goTo("8001");

            Assert.AreEqual(8000, CaretLine(vm), "the caret stands on line 8001 of the file");
            Assert.IsTrue(vm.FirstLineNumber > 1, "the window moved, and the line numbers moved with it");
            var line = vm.Document.Current.LineOf(vm.Editor.Caret);
            var (start, end) = vm.Document.Current.LineAt(vm.Document.Current.OffsetOf(line));
            StringAssert.StartsWith(vm.Document.Current.Slice(start, end - start), "Line 08000");
            Assert.AreEqual(10_000, vm.LineCount, "moving the window does not change the file's line count");
        }
        finally { File.Delete(path); }
    });

    [TestMethod]
    [CoversNode("text-viewer-find")]
    [CoversNode("text-viewer-status-matchcount")]
    [CoversNode("text-viewer-search-indicator")]
    public void Search_LongFile_FindsMatchesAcrossTheWholeFile() => AsyncPump.Run(async () =>
    {
        var path = WriteManyLines(out _);
        try
        {
            using var vm = new TextViewModel(path, Substitute.For<IShellServices>(), VirtualFileSystem.Instance, SmallWindow) { IsMonitoring = false };
            await vm.LoadAsync(CancellationToken.None);

            await vm.SearchConventionalAsync("Line 07777");

            Assert.IsTrue(vm.IsSearchActive);
            Assert.AreEqual(1, vm.SearchMatchCount);                             // status-bar Match Count
            Assert.AreEqual("Line 07777", vm.CurrentSearchTerm);                 // search-bar Search Indicator
            Assert.AreEqual(7777, CaretLine(vm), "the search went to the match, the window brought there");
        }
        finally { File.Delete(path); }
    });

    [TestMethod]
    [CoversNode("text-viewer-func-editing")]
    public void LongFile_EditsFarApart_AreBothSaved_AndCanStillBeTakenBack() => AsyncPump.Run(async () =>
    {
        var path = WriteManyLines(out _);
        try
        {
            var (shell, goTo) = PromptingShell();
            using (var vm = new TextViewModel(path, shell, VirtualFileSystem.Instance, SmallWindow) { IsMonitoring = false })
            {
                await vm.LoadAsync(CancellationToken.None);
                await vm.ToggleEditingCommand.ExecuteAsync(null);

                vm.Document.Write(ContentChange.Write(0, 0, "ZZZ"));
                Assert.IsTrue(vm.IsDirty);

                // Far enough that the window leaves the first edit behind.
                vm.GoToLineCommand.Execute(null);
                goTo("8001");
                Assert.IsFalse(vm.Document.ToString().Contains("ZZZ"), "the first edit is outside the window now");
                vm.Document.Write(ContentChange.Write(vm.Editor.Caret, 0, "YYY"));

                await vm.SaveCommand.ExecuteAsync(null);

                Assert.IsFalse(vm.IsDirty, "saving leaves nothing to save");
                Assert.IsTrue(vm.Document.CanUndo, "saving keeps what can be taken back");
                Assert.AreEqual(10_000, vm.LineCount);
            }

            var lines = File.ReadAllLines(path);
            StringAssert.StartsWith(lines[0], "ZZZLine 00000", "the edit the window left behind was saved");
            StringAssert.StartsWith(lines[8000], "YYYLine 08000", "the edit in the window was saved");
            Assert.AreEqual(10_000, lines.Length);
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
            using (var vm = new TextViewModel(path, Substitute.For<IShellServices>(), VirtualFileSystem.Instance) { IsMonitoring = false })
            {
                await vm.LoadAsync(CancellationToken.None);

                var dirtyRaised = false;
                vm.DirtyChanged += dirty => dirtyRaised = dirty;

                await vm.ToggleEditingCommand.ExecuteAsync(null);
                vm.Document.Write(ContentChange.Write(0, 0, "X"));

                Assert.IsTrue(vm.IsDirty);
                Assert.IsTrue(dirtyRaised);
                Assert.IsTrue(vm.SaveCommand.CanExecute(null), "Save is enabled once the document is dirty");

                await vm.SaveCommand.ExecuteAsync(null);
                Assert.IsFalse(vm.IsDirty, "saving clears the dirty flag");
            }

            Assert.AreEqual("Xalpha\nbeta\ngamma", File.ReadAllText(path), "the edit was saved, and nothing else changed");
        }
        finally { File.Delete(path); }
    });

    [TestMethod]
    [CoversNode("text-viewer-find-nav")]
    [CoversNode("text-viewer-highlights")]
    [CoversNode("text-viewer-minimap")]
    public void FindNextPrevious_MovesTheCaret_AndFeedsTheMarksAndTheMinimap() => AsyncPump.Run(async () =>
    {
        var path = WriteTemp("foo a\nbar b\nfoo c\nbar d\nfoo e"); // "foo" on lines 1, 3, 5
        try
        {
            using var vm = new TextViewModel(path, Substitute.For<IShellServices>(), VirtualFileSystem.Instance) { IsMonitoring = false };
            await vm.LoadAsync(CancellationToken.None);

            await vm.SearchConventionalAsync("foo");
            Assert.IsTrue(vm.IsSearchActive);
            Assert.AreEqual(3, vm.SearchMatchCount);
            Assert.AreEqual(3, vm.MiniMapMarks.Count, "one minimap mark per matching line across the file");
            Assert.AreEqual(0, CaretLine(vm), "the search goes to the first match");

            var document = vm.Document.Current;
            var marks = vm.Editor.Marks.Mark(new ContentMarkRequest(document, 0, document.Length));
            CollectionAssert.AreEqual(new[] { 0, 12, 24 }, marks.Where(m => m.Kind == ContentMarkKind.Wash).Select(m => m.Start).ToArray(), "every match is marked");
            Assert.AreEqual(0, marks.Single(m => m.Kind == ContentMarkKind.Outline).Start, "the match gone to is outlined");

            vm.FindNextCommand.Execute(null);
            Assert.AreEqual(2, CaretLine(vm), "Find Next advances to the next matching line");
            Assert.AreEqual(12, vm.Editor.Marks.Mark(new ContentMarkRequest(document, 0, document.Length)).Single(m => m.Kind == ContentMarkKind.Outline).Start);

            vm.FindPreviousCommand.Execute(null);
            Assert.AreEqual(0, CaretLine(vm), "Find Previous steps back to the earlier matching line");
        }
        finally { File.Delete(path); }
    });

    [TestMethod]
    [CoversNode("text-viewer-func-encoding-detect")]
    public void LoadAsync_DetectsEncodingFromBom() => AsyncPump.Run(async () =>
    {
        // A UTF-16 LE file (with BOM) whose bytes are garbage under the default UTF-8 selection — the loader must honour the byte-order
        // mark and decode it correctly regardless.
        var path = Path.Combine(TestTemp.Root, $"textvm_{Guid.NewGuid():N}.txt");
        File.WriteAllText(path, "héllo\nwörld", Encoding.Unicode);
        try
        {
            using var vm = new TextViewModel(path, Substitute.For<IShellServices>(), VirtualFileSystem.Instance) { IsMonitoring = false };
            Assert.AreEqual("UTF-8", vm.SelectedEncoding.Name); // selection is the default; the BOM overrides it
            await vm.LoadAsync(CancellationToken.None);

            Assert.AreEqual("héllo\nwörld", vm.Document.ToString(), "the UTF-16 BOM was auto-detected on load");
        }
        finally { File.Delete(path); }
    });

    [TestMethod]
    [CoversNode("text-viewer-encoding")]
    public void SelectedEncoding_Change_ReDecodesFile() => AsyncPump.Run(async () =>
    {
        // Byte 0xE9 is 'é' in Latin-1 but an invalid lead byte in UTF-8 (→ replacement char).
        var path = Path.Combine(TestTemp.Root, $"textvm_{Guid.NewGuid():N}.txt");
        File.WriteAllBytes(path, Encoding.Latin1.GetBytes("café\nline two"));
        try
        {
            using var vm = new TextViewModel(path, Substitute.For<IShellServices>(), VirtualFileSystem.Instance) { IsMonitoring = false };
            await vm.LoadAsync(CancellationToken.None);
            StringAssert.Contains(vm.Document.ToString(), "café", "bytes that are not UTF-8 are read as the single-byte text they are");

            // Choosing an encoding reads the file again in it (fire-and-forget); poll until it lands.
            vm.SelectedEncoding = vm.AvailableEncodings.First(e => e.Name == "UTF-8");
            await WaitUntilAsync(() => vm.Document.ToString().Contains('�'), "read as UTF-8, 0xE9 is no character");

            vm.SelectedEncoding = vm.AvailableEncodings.First(e => e.Name == "Latin-1");
            await WaitUntilAsync(() => vm.Document.ToString().Contains("café"), "read as Latin-1, it is é again");
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

            using var vm = new TextViewModel(path, shell, VirtualFileSystem.Instance) { IsMonitoring = true };
            await vm.LoadAsync(CancellationToken.None); // monitoring on → StartMonitoring registers the watch

            Assert.IsNotNull(onChanged, "monitoring registered a file watch");
            Assert.IsFalse(vm.FileChangedBannerVisible);

            File.WriteAllText(path, "updated\ncontent\nthird"); // the file changes on disk, which the viewer shares with writers while unedited
            onChanged!();                                       // the shell's debounced watcher fires

            Assert.IsTrue(vm.FileChangedBannerVisible, "a disk change raises the file-changed banner");

            // Let the change → reload → auto-hide cycle finish before the pump completes, so no continuation is posted after it closes.
            for (var i = 0; i < 500 && vm.FileChangedBannerVisible; i++) await Task.Delay(20);
            Assert.IsFalse(vm.FileChangedBannerVisible, "the banner auto-hides after the reload settles");
            Assert.AreEqual("updated\ncontent\nthird", vm.Document.ToString(), "the reload read what is on disk now");
        }
        finally { File.Delete(path); }
    });

    [TestMethod]
    [CoversNode("text-viewer-editing-toggle")]
    public void Editing_HoldsTheFileAgainstOtherWriters_UntilEditingStops() => AsyncPump.Run(async () =>
    {
        var path = WriteTemp("alpha\nbeta");
        try
        {
            using var vm = new TextViewModel(path, Substitute.For<IShellServices>(), VirtualFileSystem.Instance) { IsMonitoring = false };
            await vm.LoadAsync(CancellationToken.None);

            using (new FileStream(path, FileMode.Open, FileAccess.Write, FileShare.ReadWrite | FileShare.Delete)) { }

            await vm.ToggleEditingCommand.ExecuteAsync(null);
            Assert.IsTrue(vm.IsEditing);
            Assert.IsFalse(vm.IsReadOnly);
            Assert.ThrowsExactly<IOException>(() => new FileStream(path, FileMode.Open, FileAccess.Write, FileShare.ReadWrite | FileShare.Delete).Dispose(),
                "no other program writes the file while it is edited");
            using (new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete)) { }

            await vm.ToggleEditingCommand.ExecuteAsync(null);   // nothing written: no question asked
            Assert.IsFalse(vm.IsEditing);
            Assert.IsTrue(vm.IsReadOnly);
            using (new FileStream(path, FileMode.Open, FileAccess.Write, FileShare.ReadWrite | FileShare.Delete)) { }
        }
        finally { File.Delete(path); }
    });

    [TestMethod]
    [CoversNode("text-viewer-editing-toggle")]
    public void Editing_IsRefused_WhileAnotherProgramWritesTheFile() => AsyncPump.Run(async () =>
    {
        var path = WriteTemp("alpha\nbeta");
        try
        {
            var shell = Substitute.For<IShellServices>();
            using var vm = new TextViewModel(path, shell, VirtualFileSystem.Instance) { IsMonitoring = false };
            await vm.LoadAsync(CancellationToken.None);

            using (new FileStream(path, FileMode.Open, FileAccess.Write, FileShare.ReadWrite | FileShare.Delete))
            {
                await vm.ToggleEditingCommand.ExecuteAsync(null);
            }

            Assert.IsFalse(vm.IsEditing, "a file another program is writing is not edited over it");
            Assert.IsTrue(vm.IsReadOnly);
            shell.Received(1).ShowError(Arg.Is<string>(message => message.Contains(vm.FileName)));
        }
        finally { File.Delete(path); }
    });

    [TestMethod]
    [CoversNode("text-viewer-editing-toggle")]
    public void Editing_AFileChangedSinceItWasRead_ReadsItAgainFirst() => AsyncPump.Run(async () =>
    {
        var path = WriteTemp("alpha\nbeta");
        try
        {
            using var vm = new TextViewModel(path, Substitute.For<IShellServices>(), VirtualFileSystem.Instance) { IsMonitoring = false };
            await vm.LoadAsync(CancellationToken.None);

            File.WriteAllText(path, "changed\nmeanwhile\nby another program", new UTF8Encoding(false));
            await vm.ToggleEditingCommand.ExecuteAsync(null);

            Assert.IsTrue(vm.IsEditing);
            Assert.AreEqual("changed\nmeanwhile\nby another program", vm.Document.ToString(), "what is edited is what is on disk now");
            Assert.AreEqual(3, vm.LineCount);
        }
        finally { File.Delete(path); }
    });

    [TestMethod]
    [CoversNode("text-viewer-split-run")]
    [CoversNode("text-viewer-func-splitting")]
    public async Task Split_QueuesTask_ThatSplitsFileIntoSiblingParts()
    {
        var dir = Path.Combine(TestTemp.Root, $"textvm_split_{Guid.NewGuid():N}");
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

            using var vm = new TextViewModel(path, shell, VirtualFileSystem.Instance) { IsMonitoring = false };
            vm.SelectedSplitMode = vm.SplitModes.First(m => m.Mode == SplitMode.ByLineCount);
            vm.SplitValue = "5";
            vm.IsSplitPanelOpen = true;

            await vm.SplitCommand.ExecuteAsync(null);

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
        for (var i = 0; i < 300 && !condition(); i++) await Task.Delay(10);
        Assert.IsTrue(condition(), because);
    }
}
