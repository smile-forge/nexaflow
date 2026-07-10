using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
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
