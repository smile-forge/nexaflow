using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using NSubstitute;
using Nexaflow.Features.Common;
using Nexaflow.Features.Compressed.Handlers;
using Nexaflow.Features.WindowsFileSystem.ViewModels;
using Nexaflow.IO.Common;

namespace Nexaflow.Tests.Features.WindowsFileSystem;

/// <summary>The file explorer treating an archive like a folder: double-clicking navigates in, and the
/// VFS supplies the listing for the archive and its inner folders.</summary>
[TestClass]
public class ArchiveBrowsingTests
{
    [TestMethod]
    public void NavigateInto_Zip_ListsTopLevel()
    {
        using var fix = new Fixture();
        fix.Vm.NavigateTo(fix.ZipPath);

        Assert.AreEqual(fix.ZipPath, fix.Vm.CurrentPath);
        var names = fix.Vm.Entries.Select(e => e.Name).ToHashSet();
        CollectionAssert.AreEquivalent(new[] { "docs", "readme.txt" }, names.ToList());
        Assert.IsTrue(fix.Vm.Entries.Single(e => e.Name == "docs").IsDirectory);
        Assert.IsFalse(fix.Vm.Entries.Single(e => e.Name == "readme.txt").IsDirectory);
    }

    [TestMethod]
    public void NavigateInto_ZipSubfolder_ListsChildren()
    {
        using var fix = new Fixture();
        fix.Vm.NavigateTo(Path.Combine(fix.ZipPath, "docs"));

        var names = fix.Vm.Entries.Select(e => e.Name).ToList();
        CollectionAssert.AreEqual(new[] { "guide.md" }, names);
    }

    [TestMethod]
    public void OpenEntry_OnZipFile_NavigatesIn()
    {
        using var fix = new Fixture();
        // Simulate the browser sitting in the real folder, then double-clicking the zip.
        var zipEntry = new FileSystemEntry { Name = "a.zip", FullPath = fix.ZipPath, IsDirectory = false };
        fix.Vm.OpenEntryCommand.Execute(zipEntry);

        Assert.AreEqual(fix.ZipPath, fix.Vm.CurrentPath);
        Assert.IsTrue(fix.Vm.Entries.Any(e => e.Name == "readme.txt"));
    }

    private sealed class Fixture : IDisposable
    {
        public FileSystemViewModel Vm { get; }
        public string ZipPath { get; }
        private readonly string _dir;

        public Fixture()
        {
            _dir = Path.Combine(Path.GetTempPath(), "nexa-browse-test-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_dir);
            ZipPath = Path.Combine(_dir, "a.zip");
            using (var zip = ZipFile.Open(ZipPath, ZipArchiveMode.Create))
            {
                Add(zip, "readme.txt", "top");
                Add(zip, "docs/guide.md", "guide");
            }

            var shell = Substitute.For<IShellServices>();
            var ai = Substitute.For<IAIService>();
            Vm = new FileSystemViewModel(_dir, shell, ai, new Dictionary<Type, IFeatureConfig>());

            var vfs = new VirtualFileSystem();
            vfs.RegisterHandler(new ZipArchiveHandler());
            Vm.Vfs = vfs;
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
