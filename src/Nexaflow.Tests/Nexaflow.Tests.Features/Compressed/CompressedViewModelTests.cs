using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using Nexaflow.Features.Common;
using Nexaflow.Features.Compressed.Handlers;
using Nexaflow.Features.Compressed.ViewModels;
using Nexaflow.IO.Common;
using Nexaflow.Tests.Fixtures;
using NSubstitute;

namespace Nexaflow.Tests.Features.Compressed;

/// <summary>The Compressed inspector view-model: archive metadata + the flattened directory tree it
/// builds from the VFS, and folder expand/collapse of the visible rows.</summary>
[TestClass]
[CoversNode("compressed-inspector")]
[CoversNode("compressed-entry-tree")]
public class CompressedViewModelTests
{
    [TestMethod]
    [CoversNode("compressed-metadata-pane")]
    public void Load_PopulatesMetadataAndTree()
    {
        using var fix = new Fixture();
        var vm = new CompressedViewModel(fix.ZipPath, fix.Shell, fix.Vfs);

        Assert.IsTrue(vm.IsRecognised);
        Assert.AreEqual("Zip", vm.Format);
        Assert.IsTrue(vm.CanModify);
        StringAssert.Contains(vm.EntryCountText, "3 file");

        // Top level: the "docs" folder and "readme.txt".
        var topNames = vm.VisibleRows.Where(r => r.Depth == 0).Select(r => r.Name).ToHashSet();
        CollectionAssert.AreEquivalent(new[] { "docs", "readme.txt" }, topNames.ToList());
    }

    [TestMethod]
    public void ActivateRow_TogglesFolderVisibility()
    {
        using var fix = new Fixture();
        var vm = new CompressedViewModel(fix.ZipPath, fix.Shell, fix.Vfs);

        var docs = vm.VisibleRows.First(r => r.Name == "docs");
        // Collapse if auto-expanded, then expand and confirm children appear.
        if (docs.IsExpanded) vm.ActivateRowCommand.Execute(docs);
        Assert.IsFalse(vm.VisibleRows.Any(r => r.Name == "guide.md"));

        vm.ActivateRowCommand.Execute(docs);
        Assert.IsTrue(vm.VisibleRows.Any(r => r.Name == "guide.md"));
    }

    private sealed class Fixture : IDisposable
    {
        public VirtualFileSystem Vfs { get; }
        public IShellServices Shell { get; } = Substitute.For<IShellServices>();
        public string ZipPath { get; }
        private readonly string _dir;

        public Fixture()
        {
            Vfs = new VirtualFileSystem();
            Vfs.RegisterHandler(new ZipArchiveHandler());
            _dir = Path.Combine(Path.GetTempPath(), "nexa-cvm-test-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_dir);
            ZipPath = Path.Combine(_dir, "a.zip");
            using var zip = ZipFile.Open(ZipPath, ZipArchiveMode.Create);
            Add(zip, "readme.txt", "top");
            Add(zip, "docs/guide.md", "guide");
            Add(zip, "docs/img/logo.txt", "logo");
        }

        private static void Add(ZipArchive zip, string path, string body)
        {
            using var w = new StreamWriter(zip.CreateEntry(path).Open());
            w.Write(body);
        }

        public void Dispose()
        {
            try { Directory.Delete(_dir, recursive: true); } catch { }
        }
    }
}
