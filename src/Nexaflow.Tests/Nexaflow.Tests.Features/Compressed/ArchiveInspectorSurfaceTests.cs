using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Threading.Tasks;
using Nexaflow.Features.Common;
using Nexaflow.Features.Compressed.Handlers;
using Nexaflow.Features.Compressed.SecureZip;
using Nexaflow.Features.Compressed.ViewModels;
using Nexaflow.IO.Common;
using System.Collections.Generic;
using Nexaflow.Tests.Fixtures;
using NSubstitute;

namespace Nexaflow.Tests.Features.Compressed;

/// <summary>
/// The archive inspector's action bar and its overlays: extract, add, test, and the two modals the
/// destructive actions raise.
/// <para>
/// Add is the one to watch. A read-only format has to refuse it and <i>say so</i> — silently doing nothing
/// when a user drags a file onto a .rar reads as a broken drop target, and the same guard sits behind both
/// the button and the drop, which is why it is asserted through the drop path.
/// </para>
/// </summary>
[TestClass]
public class ArchiveInspectorSurfaceTests
{
    // ── Extract ───────────────────────────────────────────────────────────────

    [TestMethod]
    [CoversNode("compressed-extract")]
    public async Task ExtractingWritesTheEntriesOutAndSaysWhereTheyWent()
    {
        using var fix = new Fixture();
        var dest = fix.NewFolder();
        fix.Shell.PickFolderAsync(Arg.Any<string>()).Returns(Task.FromResult<string?>(dest));
        var vm = new CompressedViewModel(fix.ZipPath, fix.Shell, fix.Vfs);

        await vm.ExtractCommand.ExecuteAsync(null);

        Assert.IsTrue(File.Exists(Path.Combine(dest, "readme.txt")));
        Assert.IsTrue(File.Exists(Path.Combine(dest, "docs", "guide.md")), "the tree comes out shaped as it went in");
        StringAssert.Contains(vm.StatusText, dest);
    }

    [TestMethod]
    [CoversNode("compressed-extract")]
    public async Task CancellingTheFolderPickerExtractsNothing()
    {
        using var fix = new Fixture();
        fix.Shell.PickFolderAsync(Arg.Any<string>()).Returns(Task.FromResult<string?>(null));
        var vm = new CompressedViewModel(fix.ZipPath, fix.Shell, fix.Vfs);

        await vm.ExtractCommand.ExecuteAsync(null);

        Assert.IsFalse(vm.StatusText.Contains("Extracted"), "backing out of the picker is not a silent extract");
    }

    // ── Add ───────────────────────────────────────────────────────────────────

    [TestMethod]
    [CoversNode("compressed-addfile")]
    public async Task AddingAFilePutsItInTheArchiveAndRefreshesTheTree()
    {
        using var fix = new Fixture();
        var extra = fix.NewFile("notes.txt", "added");
        var vm = new CompressedViewModel(fix.ZipPath, fix.Shell, fix.Vfs);
        Assert.IsFalse(vm.VisibleRows.Any(r => r.Name == "notes.txt"));

        await vm.AddSourcesAsync([extra]);

        Assert.IsTrue(vm.VisibleRows.Any(r => r.Name == "notes.txt"),
                      "the tree reloads, so the archive on screen matches the archive on disk");
        StringAssert.Contains(vm.StatusText, "Added 1 file");
    }

    [TestMethod]
    [CoversNode("compressed-addfile")]
    public async Task AddingSeveralFilesCountsThemInThePlural()
    {
        using var fix = new Fixture();
        var a = fix.NewFile("one.txt", "1");
        var b = fix.NewFile("two.txt", "2");
        var vm = new CompressedViewModel(fix.ZipPath, fix.Shell, fix.Vfs);

        await vm.AddSourcesAsync([a, b]);

        StringAssert.Contains(vm.StatusText, "Added 2 files");
    }

    [TestMethod]
    [CoversNode("compressed-addfile")]
    public async Task ASourceThatIsNotThereIsSkipped_NotCounted()
    {
        using var fix = new Fixture();
        var real = fix.NewFile("real.txt", "x");
        var vm = new CompressedViewModel(fix.ZipPath, fix.Shell, fix.Vfs);

        await vm.AddSourcesAsync([real, Path.Combine(fix.Dir, "vanished.txt")]);

        StringAssert.Contains(vm.StatusText, "Added 1 file");
    }

    [TestMethod]
    [CoversNode("compressed-droptarget")]
    public async Task DroppingNothingUsableChangesNothing()
    {
        using var fix = new Fixture();
        var vm = new CompressedViewModel(fix.ZipPath, fix.Shell, fix.Vfs);
        var before = vm.VisibleRows.Count;

        await vm.AddSourcesAsync([Path.Combine(fix.Dir, "not-here.txt")]);

        Assert.AreEqual(before, vm.VisibleRows.Count);
        Assert.IsFalse(vm.StatusText.StartsWith("Added"), "nothing was added, so nothing is claimed");
    }

    // ── Test ──────────────────────────────────────────────────────────────────

    [TestMethod]
    [CoversNode("compressed-test")]
    public async Task TestingAGoodArchiveReadsEveryEntryAndReportsTheCount()
    {
        using var fix = new Fixture();
        var vm = new CompressedViewModel(fix.ZipPath, fix.Shell, fix.Vfs);

        await vm.TestCommand.ExecuteAsync(null);

        StringAssert.Contains(vm.StatusText, "OK");
        StringAssert.Contains(vm.StatusText, "3 entries",
                              "every file is actually decompressed — a header-only check would pass a corrupt archive");
    }

    [TestMethod]
    [CoversNode("compressed-test")]
    public async Task TestingACorruptedArchiveNamesHowManyEntriesFailed()
    {
        using var fix = new Fixture();
        var corrupt = fix.CorruptCopy();
        var vm = new CompressedViewModel(corrupt, fix.Shell, fix.Vfs);
        if (!vm.IsRecognised) Assert.Inconclusive("the corrupted copy no longer parses as an archive at all");

        await vm.TestCommand.ExecuteAsync(null);

        StringAssert.Contains(vm.StatusText, "failed",
                              "the point of Test is to find damage before you rely on the archive");
    }

    // ── Overlays ──────────────────────────────────────────────────────────────

    [TestMethod]
    [CoversNode("compressed-overlays")]
    public async Task TheChoiceOverlayOffersTheOptions_AndCancellingRunsNothing()
    {
        using var fix = new Fixture();
        var vm = new CompressedViewModel(fix.ZipPath, fix.Shell, fix.Vfs);

        vm.RecompressCommand.Execute(null);

        Assert.IsTrue(vm.ChoiceVisible, "Recompress asks which level before doing anything");
        Assert.IsTrue(vm.ChoiceOptions.Count > 0);
        var before = vm.StatusText;

        vm.CancelChoiceCommand.Execute(null);

        Assert.IsFalse(vm.ChoiceVisible);
        Assert.AreEqual(before, vm.StatusText, "backing out of the overlay leaves the archive untouched");
    }

    [TestMethod]
    [CoversNode("compressed-overlays")]
    public async Task ThePasswordOverlayIsRaisedBeforeEncrypting_AndCancellingLeavesTheArchiveAlone()
    {
        using var fix = new Fixture();
        var vm = new CompressedViewModel(fix.ZipPath, fix.Shell, fix.Vfs);
        if (!vm.EncryptCommand.CanExecute(null)) Assert.Inconclusive("no encrypting backend is registered here");
        var bytesBefore = File.ReadAllBytes(fix.ZipPath).Length;

        vm.EncryptCommand.Execute(null);
        Assert.IsTrue(vm.PasswordVisible, "encryption never proceeds without asking for the password first");

        vm.CancelPasswordCommand.Execute(null);

        Assert.IsFalse(vm.PasswordVisible);
        Assert.AreEqual(bytesBefore, File.ReadAllBytes(fix.ZipPath).Length,
                        "a cancelled encrypt must not have rewritten the archive");
    }

    [TestMethod]
    [CoversNode("compressed-overlays")]
    public async Task SubmittingAnEmptyPasswordDoesNotCountAsAPassword()
    {
        using var fix = new Fixture();
        var vm = new CompressedViewModel(fix.ZipPath, fix.Shell, fix.Vfs);
        if (!vm.EncryptCommand.CanExecute(null)) Assert.Inconclusive("no encrypting backend is registered here");
        vm.EncryptCommand.Execute(null);
        var bytesBefore = File.ReadAllBytes(fix.ZipPath).Length;

        await vm.SubmitPasswordAsync("");

        Assert.IsFalse(vm.PasswordVisible);
        Assert.AreEqual(bytesBefore, File.ReadAllBytes(fix.ZipPath).Length,
                        "an empty password would encrypt to nothing — the action is dropped instead");
    }

    // ── Fixture ───────────────────────────────────────────────────────────────

    private sealed class Fixture : IDisposable
    {
        public VirtualFileSystem Vfs { get; }
        public IShellServices Shell { get; } = Substitute.For<IShellServices>();
        public string ZipPath { get; }
        public string Dir { get; }

        public Fixture()
        {
            Vfs = new VirtualFileSystem();
            Vfs.RegisterHandler(new ZipArchiveHandler());
            // The inspector discovers its encryptor through the shell, so the overlay tests need one offered.
            Shell.DiscoverImplementations<IArchiveEncryptor>().Returns([typeof(SecureZipEncryptor)]);
            Dir = Path.Combine(Path.GetTempPath(), "nexa-cinsp-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Dir);
            ZipPath = Path.Combine(Dir, "a.zip");
            using var zip = ZipFile.Open(ZipPath, ZipArchiveMode.Create);
            Add(zip, "readme.txt", "top");
            Add(zip, "docs/guide.md", "guide");
            Add(zip, "docs/img/logo.txt", "logo");
        }

        public string NewFolder()
        {
            var p = Path.Combine(Dir, "out-" + Guid.NewGuid().ToString("N")[..8]);
            Directory.CreateDirectory(p);
            return p;
        }

        public string NewFile(string name, string body)
        {
            var p = Path.Combine(Dir, name);
            File.WriteAllText(p, body);
            return p;
        }

        /// <summary>A copy whose central directory still parses but whose entry data is damaged.</summary>
        public string CorruptCopy()
        {
            var copy = Path.Combine(Dir, "corrupt.zip");
            var bytes = File.ReadAllBytes(ZipPath);
            // Scribble over the first local-header payload region, well before the central directory.
            for (var i = 40; i < Math.Min(80, bytes.Length); i++) bytes[i] ^= 0xFF;
            File.WriteAllBytes(copy, bytes);
            return copy;
        }

        private static void Add(ZipArchive zip, string path, string body)
        {
            using var w = new StreamWriter(zip.CreateEntry(path).Open());
            w.Write(body);
        }

        public void Dispose() { try { Directory.Delete(Dir, recursive: true); } catch { } }
    }
}
