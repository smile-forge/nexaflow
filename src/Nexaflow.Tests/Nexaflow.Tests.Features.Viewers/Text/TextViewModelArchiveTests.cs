using System;
using System.IO;
using System.IO.Compression;
using System.Text;
using System.Threading;
using Nexaflow.Features.Common;
using Nexaflow.Features.Compressed.Handlers;
using Nexaflow.Features.Text.ViewModels;
using Nexaflow.IO.Common;
using Nexaflow.Markdown.Editing;
using Nexaflow.Tests.Features.Infrastructure;
using Nexaflow.Tests.Fixtures;
using NSubstitute;

namespace Nexaflow.Tests.Features.Text;

/// <summary>
/// The text viewer opening a file that lives <i>inside</i> an archive. Such a path is not a real OS path, so the entry is copied to this disk
/// to be read there, and a save is written back into the archive through <see cref="VirtualFileSystem"/> — a file too long for one document
/// as well as a short one.
/// </summary>
[TestClass]
public class TextViewModelArchiveTests
{
    /// <summary>A window far shorter than the long entry, so it is shown a stretch at a time.</summary>
    private const int SmallWindow = 200_000;

    private string _dir = string.Empty;
    private string _zip = string.Empty;

    [TestInitialize]
    public void Setup()
    {
        // TextViewModel reads through the process-wide VFS, so the zip handler must be on the singleton.
        VirtualFileSystem.Instance.RegisterHandler(new ZipArchiveHandler());

        _dir = Path.Combine(TestTemp.Root, "nexa-textvm-zip-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
        _zip = Path.Combine(_dir, "a.zip");

        using var zip = ZipFile.Open(_zip, ZipArchiveMode.Create);
        Write(zip, "small.txt", "line one\nline two\nline three");
        Write(zip, "big.txt", BigBody(out _));
    }

    [TestCleanup]
    public void Cleanup()
    {
        try { Directory.Delete(_dir, recursive: true); } catch { }
    }

    private static string BigBody(out int lineCount)
    {
        lineCount = 10_000;                                     // ~55 chars/line ⇒ ≫ the small window
        var lines = new string[lineCount];
        for (var i = 0; i < lines.Length; i++)
            lines[i] = $"Line {i:D5}: the quick brown fox jumps over the lazy dog";
        return string.Join("\n", lines);
    }

    private static void Write(ZipArchive zip, string entry, string body)
    {
        using var w = new StreamWriter(zip.CreateEntry(entry).Open(), new UTF8Encoding(false));
        w.Write(body);
    }

    private string Entry(string name)
    {
        using var zip = ZipFile.OpenRead(_zip);
        using var reader = new StreamReader(zip.GetEntry(name)!.Open());
        return reader.ReadToEnd();
    }

    [TestMethod]
    [CoversNode("compressed-open-inner-file")]
    public void LoadAsync_SmallFileInsideZip_LoadsContent() => AsyncPump.Run(async () =>
    {
        using var vm = new TextViewModel(Path.Combine(_zip, "small.txt"), Substitute.For<IShellServices>(), VirtualFileSystem.Instance)
        { IsMonitoring = false };
        await vm.LoadAsync(CancellationToken.None);

        Assert.IsFalse(vm.IsLargeFile);
        Assert.AreEqual("line one\nline two\nline three", vm.Document.ToString());
    });

    [TestMethod]
    [CoversNode("compressed-open-inner-file")]
    [CoversNode("text-viewer-windowing")]
    public void LoadAsync_LongFileInsideZip_LoadsContent() => AsyncPump.Run(async () =>
    {
        BigBody(out var lineCount);

        using var vm = new TextViewModel(Path.Combine(_zip, "big.txt"), Substitute.For<IShellServices>(), VirtualFileSystem.Instance, SmallWindow)
        { IsMonitoring = false };
        await vm.LoadAsync(CancellationToken.None);

        Assert.IsTrue(vm.IsLargeFile);
        Assert.AreEqual(lineCount, vm.LineCount);
        StringAssert.Contains(vm.Document.ToString(), "Line 00000");
    });

    /// <summary>An edited entry is written back into the archive, not into the copy it was read from.</summary>
    [TestMethod]
    [CoversNode("compressed-open-inner-file")]
    public void Save_SmallFileInsideZip_WritesBackIntoArchive() => AsyncPump.Run(async () =>
    {
        using (var vm = new TextViewModel(Path.Combine(_zip, "small.txt"), Substitute.For<IShellServices>(), VirtualFileSystem.Instance) { IsMonitoring = false })
        {
            await vm.LoadAsync(CancellationToken.None);

            await vm.ToggleEditingCommand.ExecuteAsync(null);
            vm.Document.Write(ContentChange.Write(0, 0, "EDITED\n"));
            Assert.IsTrue(vm.IsDirty);

            await vm.SaveCommand.ExecuteAsync(null);
            Assert.IsFalse(vm.IsDirty);
        }

        Assert.AreEqual("EDITED\nline one\nline two\nline three", Entry("small.txt"));
    });

    /// <summary>A file too long for one document is edited and saved inside an archive as a short one is: the whole of it, as written, streamed back into the entry.</summary>
    [TestMethod]
    [CoversNode("compressed-open-inner-file")]
    [CoversNode("text-viewer-windowing")]
    public void Save_LongFileInsideZip_WritesTheWholeOfItBackIntoArchive() => AsyncPump.Run(async () =>
    {
        BigBody(out var lineCount);

        using (var vm = new TextViewModel(Path.Combine(_zip, "big.txt"), Substitute.For<IShellServices>(), VirtualFileSystem.Instance, SmallWindow) { IsMonitoring = false })
        {
            await vm.LoadAsync(CancellationToken.None);

            await vm.ToggleEditingCommand.ExecuteAsync(null);
            Assert.IsTrue(vm.IsEditing);
            vm.Document.Write(ContentChange.Write(0, 0, "EDITED "));

            await vm.SaveCommand.ExecuteAsync(null);
            Assert.IsFalse(vm.IsDirty);
        }

        var saved = Entry("big.txt").Split('\n');
        Assert.AreEqual(lineCount, saved.Length, "every line of the file was written, not only the window's");
        Assert.AreEqual("EDITED Line 00000: the quick brown fox jumps over the lazy dog", saved[0]);
        Assert.AreEqual("Line 09999: the quick brown fox jumps over the lazy dog", saved[^1]);
    });
}
