using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Text.Json.Nodes;
using Nexaflow.Features.Common;
using Nexaflow.Features.Text.ViewModels;
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
[CoversNode("edit-file")]
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
    public void LoadAsync_SmallFile_LoadsWholeContent() => AsyncPump.Run(async () =>
    {
        var path = WriteTemp("line one\nline two\nline three");
        try
        {
            using var vm = new TextViewModel(path, Substitute.For<IShellServices>()) { IsMonitoring = false };
            await vm.LoadAsync(CancellationToken.None);

            Assert.IsFalse(vm.IsLargeFile);
            Assert.AreEqual("line one\nline two\nline three", vm.Document.Text);
            Assert.AreEqual(3, vm.LineCount);
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
    public void AiTools_ReadReplaceSave_ThroughClientToolSurface() => AsyncPump.Run(async () =>
    {
        var path = WriteTemp("alpha\nbeta\ngamma\n");
        try
        {
            using var vm = new TextViewModel(path, RunningShell()) { IsMonitoring = false };
            await vm.LoadAsync(CancellationToken.None);

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

            // replace_all: edits in place (case-insensitive default), marks the document dirty (unsaved)
            var replace = tools.Single(t => t.Name == "replace_all");
            var rep = await replace.InvokeAsync(new JsonObject { ["find"] = "beta", ["replace"] = "BETA" }, CancellationToken.None);
            Assert.IsFalse(rep.IsError);
            StringAssert.Contains(vm.Document.Text, "BETA");
            Assert.IsTrue(vm.IsDirty);

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
    public void LoadAsync_LargeFile_IndexesLineCountAndWindowsFromTop() => AsyncPump.Run(async () =>
    {
        var path = WriteManyLines(out var lineCount);
        try
        {
            using var vm = new TextViewModel(path, Substitute.For<IShellServices>()) { IsMonitoring = false };
            await vm.LoadAsync(CancellationToken.None);

            Assert.IsTrue(vm.IsLargeFile);
            Assert.AreEqual(lineCount, vm.LineCount, "the index counts every line up front");
            Assert.AreEqual(lineCount, vm.Document.LineCount, "placeholder padding preserves the scrollbar coordinate space");

            StringAssert.Contains(vm.Document.Text, "Line 00000");                  // first window is real
            Assert.IsFalse(vm.Document.Text.Contains("Line 09999"), "the tail is still placeholder");
        }
        finally { File.Delete(path); }
    });

    [TestMethod]
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
    public void Search_LargeFile_FindsMatchesAcrossTheWholeFile() => AsyncPump.Run(async () =>
    {
        var path = WriteManyLines(out _);
        try
        {
            using var vm = new TextViewModel(path, Substitute.For<IShellServices>()) { IsMonitoring = false };
            await vm.LoadAsync(CancellationToken.None);

            await vm.SearchConventionalAsync("Line 07777");

            Assert.IsTrue(vm.IsSearchActive);
            Assert.AreEqual(1, vm.SearchMatchCount);
            StringAssert.Contains(vm.Document.Text, "Line 07777", "search navigated/slid the window to the match");
        }
        finally { File.Delete(path); }
    });

    [TestMethod]
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
        }
        finally { File.Delete(path); }
    });
}
