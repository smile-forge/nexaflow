using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using Nexaflow.Features.Compressed.Handlers;
using Nexaflow.IO.Common;

namespace Nexaflow.Tests.Features.Compressed;

/// <summary>
/// The in-box ZIP backend, driven through the VFS: listing (with synthesised directories), extraction
/// of nested entries, and write-back (rebuild) of a single entry.
/// </summary>
[TestClass]
public class ZipArchiveHandlerTests
{
    [TestMethod]
    public void Enumerate_TopLevel_SynthesisesDirsAndFiles()
    {
        using var fix = new ZipFixture();
        var top = fix.Vfs.EnumerateEntries(fix.ZipPath).Select(e => e.Name).ToHashSet();
        CollectionAssert.AreEquivalent(new[] { "docs", "readme.txt" }, top.ToList());

        var docs = fix.Vfs.EnumerateEntries(Path.Combine(fix.ZipPath, "docs")).Select(e => e.Name).ToHashSet();
        CollectionAssert.AreEquivalent(new[] { "img", "guide.md" }, docs.ToList());
    }

    [TestMethod]
    public void IsContainer_And_IsDirectory_TrueForZip()
    {
        using var fix = new ZipFixture();
        Assert.IsTrue(fix.Vfs.IsContainer(fix.ZipPath));
        Assert.IsTrue(fix.Vfs.IsDirectory(fix.ZipPath));
        Assert.IsTrue(fix.Vfs.IsDirectory(Path.Combine(fix.ZipPath, "docs")));
    }

    [TestMethod]
    public void Read_NestedEntry_ReturnsSeekableContent()
    {
        using var fix = new ZipFixture();
        var logo = Path.Combine(fix.ZipPath, "docs", "img", "logo.txt");

        Assert.AreEqual("logo", fix.Vfs.ReadAllText(logo));
        Assert.AreEqual(4, fix.Vfs.GetLength(logo));

        using var s = fix.Vfs.OpenRead(logo);
        Assert.IsTrue(s.CanSeek);
    }

    [TestMethod]
    public void Read_TopLevelEntry_ReturnsContent()
    {
        using var fix = new ZipFixture();
        Assert.AreEqual("top body", fix.Vfs.ReadAllText(Path.Combine(fix.ZipPath, "readme.txt")));
    }

    [TestMethod]
    public void Replace_RewritesEntry_OthersIntact()
    {
        using var fix = new ZipFixture();
        var readme = Path.Combine(fix.ZipPath, "readme.txt");

        fix.Vfs.Replace(readme, Encoding.UTF8.GetBytes("changed!"));

        // Re-read through a fresh VFS so nothing is served from the first instance's cache.
        var fresh = NewVfs();
        Assert.AreEqual("changed!", fresh.ReadAllText(readme));
        Assert.AreEqual("guide", fresh.ReadAllText(Path.Combine(fix.ZipPath, "docs", "guide.md")));
        Assert.AreEqual("logo", fresh.ReadAllText(Path.Combine(fix.ZipPath, "docs", "img", "logo.txt")));
    }

    private static VirtualFileSystem NewVfs()
    {
        var vfs = new VirtualFileSystem();
        vfs.RegisterHandler(new ZipArchiveHandler());
        return vfs;
    }

    private sealed class ZipFixture : IDisposable
    {
        public VirtualFileSystem Vfs { get; }
        public string ZipPath { get; }
        private readonly string _dir;

        public ZipFixture()
        {
            Vfs = NewVfs();
            _dir = Path.Combine(Path.GetTempPath(), "nexa-zip-test-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_dir);
            ZipPath = Path.Combine(_dir, "a.zip");

            using var zip = ZipFile.Open(ZipPath, ZipArchiveMode.Create);
            Add(zip, "readme.txt", "top body");
            Add(zip, "docs/guide.md", "guide");
            Add(zip, "docs/img/logo.txt", "logo");
        }

        private static void Add(ZipArchive zip, string path, string body)
        {
            var entry = zip.CreateEntry(path);
            using var w = new StreamWriter(entry.Open());
            w.Write(body);
        }

        public void Dispose()
        {
            try { Directory.Delete(_dir, recursive: true); } catch { }
        }
    }
}
