using System;
using System.IO;
using System.IO.Compression;
using System.Text;
using System.Threading;
using Nexaflow.Features.Common;
using Nexaflow.Features.Compressed.Handlers;
using Nexaflow.Features.Text.ViewModels;
using Nexaflow.IO.Common;
using Nexaflow.Tests.Features.Infrastructure;
using NSubstitute;

namespace Nexaflow.Tests.Features.Text;

/// <summary>
/// The text viewer opening a file that lives <i>inside</i> an archive. Such a path is not a real OS path,
/// so every read must go through <see cref="VirtualFileSystem"/> — including the large-file
/// (<see cref="OverlayTextFile"/>) path, which is what the 100 KB threshold selects.
/// </summary>
[TestClass]
public class TextViewModelArchiveTests
{
    private string _dir = string.Empty;
    private string _zip = string.Empty;

    [TestInitialize]
    public void Setup()
    {
        // TextViewModel reads through the process-wide VFS, so the zip handler must be on the singleton.
        VirtualFileSystem.Instance.RegisterHandler(new ZipArchiveHandler());

        _dir = Path.Combine(Path.GetTempPath(), "nexa-textvm-zip-" + Guid.NewGuid().ToString("N"));
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
        lineCount = 10_000;                                     // ~55 chars/line ⇒ ≫ the 100 KB large-file limit
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

    [TestMethod]
    public void LoadAsync_SmallFileInsideZip_LoadsContent() => AsyncPump.Run(async () =>
    {
        using var vm = new TextViewModel(Path.Combine(_zip, "small.txt"), Substitute.For<IShellServices>())
        { IsMonitoring = false };
        await vm.LoadAsync(CancellationToken.None);

        Assert.IsFalse(vm.IsLargeFile);
        Assert.AreEqual("line one\nline two\nline three", vm.Document.Text);
    });

    [TestMethod]
    public void LoadAsync_LargeFileInsideZip_LoadsContent() => AsyncPump.Run(async () =>
    {
        BigBody(out var lineCount);

        using var vm = new TextViewModel(Path.Combine(_zip, "big.txt"), Substitute.For<IShellServices>())
        { IsMonitoring = false };
        await vm.LoadAsync(CancellationToken.None);

        Assert.IsTrue(vm.IsLargeFile);
        Assert.AreEqual(lineCount, vm.LineCount);
        StringAssert.Contains(vm.Document.Text, "Line 00000");
    });

    /// <summary>An edited entry is written back into the archive, not into the materialised temp copy.</summary>
    [TestMethod]
    public void Save_SmallFileInsideZip_WritesBackIntoArchive() => AsyncPump.Run(async () =>
    {
        var entryPath = Path.Combine(_zip, "small.txt");
        using (var vm = new TextViewModel(entryPath, Substitute.For<IShellServices>()) { IsMonitoring = false })
        {
            await vm.LoadAsync(CancellationToken.None);

            vm.IsEditing = true;
            vm.Document.Insert(0, "EDITED\n");
            vm.OnUserEdit(0, 0, "EDITED\n");   // the view forwards the document change
            Assert.IsTrue(vm.IsDirty);

            await vm.SaveCommand.ExecuteAsync(null);
            Assert.IsFalse(vm.IsDirty);
        }

        // Re-read straight from the zip on disk — the bytes must have landed in the archive itself.
        using var zip = ZipFile.OpenRead(_zip);
        using var reader = new StreamReader(zip.GetEntry("small.txt")!.Open());
        StringAssert.StartsWith(reader.ReadToEnd(), "EDITED\n");
    });

    /// <summary>A large entry inside an archive is deliberately read-only: the streaming save replaces a real
    /// file in place, which an archive path has no equivalent for. The viewer must refuse rather than silently
    /// write the materialised temp copy.</summary>
    [TestMethod]
    public void LargeFileInsideZip_IsNotEditable() => AsyncPump.Run(async () =>
    {
        using var vm = new TextViewModel(Path.Combine(_zip, "big.txt"), Substitute.For<IShellServices>())
        { IsMonitoring = false };
        await vm.LoadAsync(CancellationToken.None);

        Assert.IsTrue(vm.IsLargeFile);
        Assert.IsFalse(vm.CanEdit);
    });
}
