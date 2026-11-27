using Nexaflow.IO.Common;
using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Nexaflow.Features.Common;
using Nexaflow.Features.Logs.ViewModels;
using Nexaflow.Markdown.Editing;
using Nexaflow.Tests.Features.Infrastructure;
using Nexaflow.Tests.Fixtures;
using NSubstitute;

namespace Nexaflow.Tests.Features.Logs;

/// <summary>
/// The log read over the file on disk: a short one whole, a long one end first with its history read in behind and put before it, and one
/// too long for a document shown through a window onto it — the sizes made small here, so a short file is read the way a long one is.
/// Runs under <see cref="AsyncPump"/>, so what the page awaits comes back to one thread, as it does to the page's own.
/// </summary>
[TestClass]
public class LogViewModelTests
{
    private static string WriteTemp(string content, Encoding encoding)
    {
        var path = Path.Combine(TestTemp.Root, $"logvm_{Guid.NewGuid():N}.log");
        File.WriteAllText(path, content, encoding);
        return path;
    }

    private static string Lines(int count) =>
        string.Concat(Enumerable.Range(0, count).Select(i => $"2024-01-01 00:00:00 INFO line {i:D5}\n"));

    private static async Task Settled(Func<bool> until, string what)
    {
        for (var i = 0; i < 300 && !until(); i++) await Task.Delay(10);
        Assert.IsTrue(until(), $"timed out waiting for {what}");
    }

    [TestMethod]
    [CoversNode("log-viewer-tail-streaming")]
    public void LoadAsync_SmallFile_LoadsWholeContent() => AsyncPump.Run(async () =>
    {
        var path = WriteTemp("2024-01-01 00:00:00 INFO first line\n2024-01-01 00:00:01 WARN second line\n", new UTF8Encoding(false));
        try
        {
            using var vm = new LogViewModel(path, Substitute.For<IShellServices>(), VirtualFileSystem.Instance) { IsMonitoring = false };
            await vm.LoadAsync(CancellationToken.None);

            Assert.AreEqual("2024-01-01 00:00:00 INFO first line\n2024-01-01 00:00:01 WARN second line\n", vm.Document.ToString());
            Assert.IsFalse(vm.IsLoadingHead, "a short log is read whole");
            Assert.AreEqual(3, vm.LineCount);
            
            Assert.IsFalse(string.IsNullOrEmpty(vm.FileSizeText));
        }
        finally { File.Delete(path); }
    });

    [TestMethod]
    [CoversNode("log-viewer-encoding-detect")]
    public void LoadAsync_Utf16LeBom_DetectsEncodingAndDecodes() => AsyncPump.Run(async () =>
    {
        // Non-ASCII proves the BOM-driven UTF-16 LE path decoded correctly (UTF-8 would garble it).
        var path = WriteTemp("INFO Grüße\nWARN café\n", new UnicodeEncoding(bigEndian: false, byteOrderMark: true));
        try
        {
            using var vm = new LogViewModel(path, Substitute.For<IShellServices>(), VirtualFileSystem.Instance) { IsMonitoring = false };
            await vm.LoadAsync(CancellationToken.None);

            Assert.AreEqual("INFO Grüße\nWARN café\n", vm.Document.ToString(), "the mark is not a character of the log");
            Assert.AreEqual("UTF-16 LE", vm.SelectedEncoding.Name, "the selector shows what was found");
        }
        finally { File.Delete(path); }
    });

    [TestMethod]
    [CoversNode("log-viewer-tail-streaming")]
    public void ALongLog_IsShownEndFirst_AndItsHistoryPutBeforeIt_ThePageLeftWhereItStood() => AsyncPump.Run(async () =>
    {
        var text = Lines(2000);
        var path = WriteTemp(text, new UTF8Encoding(false));
        try
        {
            using var vm = new LogViewModel(path, Substitute.For<IShellServices>(), VirtualFileSystem.Instance, ContentFileWindow.DefaultSize, tailBytes: 4096)
            { IsMonitoring = false, IsAutoScrolling = false };
            await vm.LoadAsync(CancellationToken.None);

            Assert.IsTrue(vm.IsLoadingHead);
            StringAssert.EndsWith(vm.Document.ToString(), "line 01999\n", "the end shows at once");
            StringAssert.StartsWith(vm.Document.ToString(), "2024-01-01 00:00:00 INFO line ", "from where a line starts");
            var shownFirst = vm.Document.Current.LineCount;

            var page = new ScrollRecorder(vm.Editor);
            await Settled(() => !vm.IsLoadingHead, "the history to land");

            Assert.AreEqual(text, vm.Document.ToString());
            Assert.AreEqual(2001, vm.LineCount);
            Assert.AreEqual((2001 - shownFirst, ShownPlace.Top), page.Shown[^1], "the page kept on the line it stood on, the history put before it");
        }
        finally { File.Delete(path); }
    });

    [TestMethod]
    [CoversNode("log-viewer-line-select")]
    public void LinesPickedOut_WhileTheHistoryLoads_StayOnTheirLines() => AsyncPump.Run(async () =>
    {
        var path = WriteTemp(Lines(2000), new UTF8Encoding(false));
        try
        {
            using var vm = new LogViewModel(path, Substitute.For<IShellServices>(), VirtualFileSystem.Instance, ContentFileWindow.DefaultSize, tailBytes: 4096) { IsMonitoring = false };
            await vm.LoadAsync(CancellationToken.None);
            Assert.IsTrue(vm.IsLoadingHead);

            var (start, end) = vm.Document.Current.LineAt(0);
            var picked = vm.Document.Current.Slice(start, end - start);
            vm.ToggleLineSelection(1);

            await Settled(() => !vm.IsLoadingHead, "the history to land");

            var line = vm.SelectedLineNumbers.Single();
            Assert.IsTrue(line > 1, "moved on by the lines put before it");
            var at = vm.Document.Current.OffsetOf((int)line - 1);
            Assert.AreEqual(picked, vm.Document.Current.Slice(at, picked.Length), "still the line that was picked out");
        }
        finally { File.Delete(path); }
    });

    [TestMethod]
    [CoversNode("log-viewer-tail-streaming")]
    public void ALogLongerThanADocument_IsShownThroughAWindow_AtItsLiveEnd_AndKeepsGrowing() => AsyncPump.Run(async () =>
    {
        var text = Lines(2000);
        var path = WriteTemp(text, new UTF8Encoding(false));
        var shell = Substitute.For<IShellServices>();
        Action? onChanged = null;
        shell.WatchFile(Arg.Any<string>(), Arg.Do<Action>(a => onChanged = a)).Returns(Substitute.For<IFileWatch>());

        var vm = new LogViewModel(path, shell, VirtualFileSystem.Instance, windowSize: 20_000, tailBytes: 4096);
        try
        {
            await vm.LoadAsync(CancellationToken.None);
            await Settled(() => !vm.IsLoadingHead, "the history to be read");

            Assert.IsNotNull(vm.Document.Window, "too long for a document of 20,000 characters");
            Assert.AreEqual(2001, vm.LineCount, "the whole log's lines are counted, not the window's");
            Assert.IsTrue(vm.FirstLineNumber > 1, "the window stands at the live end");
            StringAssert.EndsWith(vm.Document.ToString(), "line 01999\n");

            File.AppendAllText(path, "2024-01-01 00:00:01 ERROR arrived later\n");
            onChanged!();
            await Settled(() => vm.Document.ToString().Contains("arrived later"), "the appended line to arrive in the window");

            Assert.AreEqual(2002, vm.LineCount);
            Assert.IsTrue(vm.Document.Current.Length <= 20_000 + 100, "the window moved on rather than growing past its size");
        }
        finally { vm.Dispose(); File.Delete(path); }
    });
}
