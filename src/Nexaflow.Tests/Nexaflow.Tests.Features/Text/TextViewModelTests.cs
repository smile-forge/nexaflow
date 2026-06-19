using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Nexaflow.Features.Text.ViewModels;
using Nexaflow.Tests.Features.Infrastructure;

namespace Nexaflow.Tests.Features.Text;

/// <summary>
/// Covers the head-first windowed text reader: the small-file fast path and the large-file path
/// (pre-scanned line index + placeholder padding + a sliding window of real content that the viewport
/// advances). Runs under <see cref="AsyncPump"/> because loading mutates a thread-affine AvalonEdit
/// <c>TextDocument</c> across <c>await</c> points.
/// </summary>
[TestClass]
public class TextViewModelTests
{
    private static string WriteTemp(string content)
    {
        var path = Path.Combine(Path.GetTempPath(), $"textvm_{Guid.NewGuid():N}.txt");
        File.WriteAllText(path, content, new UTF8Encoding(false));
        return path;
    }

    [TestMethod]
    public void LoadAsync_SmallFile_LoadsWholeContent() => AsyncPump.Run(async () =>
    {
        var path = WriteTemp("line one\nline two\nline three");
        try
        {
            using var vm = new TextViewModel(path) { IsMonitoring = false };
            await vm.LoadAsync(CancellationToken.None);

            Assert.IsFalse(vm.IsLargeFile);
            Assert.AreEqual("line one\nline two\nline three", vm.Document.Text);
            Assert.AreEqual(3, vm.LineCount);
        }
        finally { File.Delete(path); }
    });

    [TestMethod]
    public void LoadAsync_LargeFile_PreScansLineCountAndWindowsContent() => AsyncPump.Run(async () =>
    {
        var lines = new string[10_000];                         // ~600 KB, well past the 100 KB cutoff
        for (var i = 0; i < lines.Length; i++)
            lines[i] = $"Line {i:D5}: the quick brown fox jumps over the lazy dog";
        var path = WriteTemp(string.Join("\n", lines));         // no trailing newline → exactly 10,000 lines
        try
        {
            using var vm = new TextViewModel(path) { IsMonitoring = false };
            await vm.LoadAsync(CancellationToken.None);

            Assert.IsTrue(vm.IsLargeFile);
            Assert.AreEqual(10_000, vm.LineCount, "pre-scan counts every line up front");
            Assert.AreEqual(10_000, vm.Document.LineCount, "placeholder padding preserves the scrollbar coordinate space");
            Assert.IsTrue(vm.LoadedLineCount is > 0 and < 10_000, "only a window is backed by real content");

            StringAssert.Contains(vm.Document.Text, "Line 00000");           // first chunk is real
            Assert.IsFalse(vm.Document.Text.Contains("Line 09999"), "the tail is still placeholder");
        }
        finally { File.Delete(path); }
    });

    [TestMethod]
    public void EnsureContentLoadedUpToLine_AdvancesTheWindow() => AsyncPump.Run(async () =>
    {
        var lines = new string[10_000];
        for (var i = 0; i < lines.Length; i++)
            lines[i] = $"Line {i:D5}: the quick brown fox jumps over the lazy dog";
        var path = WriteTemp(string.Join("\n", lines));
        try
        {
            using var vm = new TextViewModel(path) { IsMonitoring = false };
            await vm.LoadAsync(CancellationToken.None);
            var loadedAfterFirstChunk = vm.LoadedLineCount;

            await vm.EnsureContentLoadedUpToLineAsync(8_000);

            Assert.IsTrue(vm.LoadedLineCount > loadedAfterFirstChunk, "the window grew");
            Assert.IsTrue(vm.LoadedLineCount > 8_000);
            StringAssert.Contains(vm.Document.Text, "Line 08000");           // now real content
        }
        finally { File.Delete(path); }
    });
}
