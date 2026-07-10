using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Nexaflow.Features.Common;
using Nexaflow.Features.Logs.ViewModels;
using Nexaflow.Tests.Features.Infrastructure;
using Nexaflow.Tests.Fixtures;
using NSubstitute;

namespace Nexaflow.Tests.Features.Logs;

/// <summary>
/// Covers the tail-first log reader. Runs under <see cref="AsyncPump"/> because the view-model loads
/// into an AvalonEdit <c>TextDocument</c> (thread-affine) across <c>await</c> points. The background
/// head-load needs a UI <c>Dispatcher</c> (<c>Application.Current</c>), absent in a headless test, so
/// only the synchronous tail, the small-file path and encoding detection are asserted here.
/// </summary>
[TestClass]
[CoversNode("log-viewer-tail-streaming")]
public class LogViewModelTests
{
    private static string WriteTemp(string content, Encoding encoding)
    {
        var path = Path.Combine(Path.GetTempPath(), $"logvm_{Guid.NewGuid():N}.log");
        File.WriteAllText(path, content, encoding);
        return path;
    }

    [TestMethod]
    public void LoadAsync_SmallFile_LoadsWholeContent() => AsyncPump.Run(async () =>
    {
        var path = WriteTemp(
            "2024-01-01 00:00:00 INFO first line\n2024-01-01 00:00:01 WARN second line\n",
            new UTF8Encoding(false));
        try
        {
            using var vm = new LogViewModel(path, Substitute.For<IShellServices>()) { IsMonitoring = false };
            await vm.LoadAsync(CancellationToken.None);

            StringAssert.Contains(vm.Document.Text, "INFO first line");
            StringAssert.Contains(vm.Document.Text, "WARN second line");
            Assert.IsTrue(vm.LineCount >= 2);
            Assert.IsFalse(string.IsNullOrEmpty(vm.FileSizeText));
        }
        finally { File.Delete(path); }
    });

    [TestMethod]
    public void LoadAsync_Utf16LeBom_DetectsEncodingAndDecodes() => AsyncPump.Run(async () =>
    {
        // Non-ASCII char proves the BOM-driven UTF-16 LE path decoded correctly (UTF-8 would garble it).
        var path = WriteTemp("INFO Grüße\nWARN café\n", new UnicodeEncoding(bigEndian: false, byteOrderMark: true));
        try
        {
            using var vm = new LogViewModel(path, Substitute.For<IShellServices>()) { IsMonitoring = false };
            await vm.LoadAsync(CancellationToken.None);

            StringAssert.Contains(vm.Document.Text, "Grüße");
            StringAssert.Contains(vm.Document.Text, "café");
        }
        finally { File.Delete(path); }
    });

    [TestMethod]
    public void LoadAsync_LargeFile_LoadsTailThenHead() => AsyncPump.Run(async () =>
    {
        var sb = new StringBuilder();
        for (var i = 0; i < 2_000; i++)                 // ~72 KB, well past the 4 KB small-file cutoff
            sb.Append("2024-01-01 00:00:00 INFO line ").Append(i.ToString("D5")).Append('\n');
        var path = WriteTemp(sb.ToString(), new UTF8Encoding(false));
        try
        {
            using var vm = new LogViewModel(path, Substitute.For<IShellServices>()) { IsMonitoring = false };
            await vm.LoadAsync(CancellationToken.None);

            // The tail loads synchronously; the head streams in on a background read and is applied on
            // the UI thread (the pump thread here). Let it settle, then the whole file should be present.
            for (var i = 0; i < 200 && vm.IsLoadingHead; i++) await Task.Delay(10);

            Assert.IsFalse(vm.IsLoadingHead, "head load should have completed");
            StringAssert.Contains(vm.Document.Text, "line 00000");     // head
            StringAssert.Contains(vm.Document.Text, "line 01999");     // tail
        }
        finally { File.Delete(path); }
    });
}
