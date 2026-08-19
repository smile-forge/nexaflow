using System;
using System.IO;
using System.IO.Compression;
using System.Formats.Tar;
using System.Linq;
using Nexaflow.Features.Compressed.SharpCompress;
using Nexaflow.IO.Common;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.Features.Compressed;

/// <summary>The SharpCompress backend over the VFS: tar listing/extraction, and tar.gz resolving its
/// inner tar through the VFS's nesting (gzip handler → "foo.tar" → tar handler).</summary>
[TestClass]
[CoversNode("compressed-backend-sharpcompress")]
public class TarArchiveTests
{
    [TestMethod]
    public void Tar_ListsAndReadsEntries()
    {
        using var fix = new Dir();
        var tar = Path.Combine(fix.Root, "bundle.tar");
        TarFile.CreateFromDirectory(fix.Src, tar, includeBaseDirectory: false);

        Assert.IsTrue(fix.Vfs.IsContainer(tar));
        var names = fix.Vfs.EnumerateEntries(tar).Select(e => e.Name).ToList();
        CollectionAssert.AreEquivalent(new[] { "docs", "a.txt" }, names,
            "top-level entries were: " + string.Join(" | ", names));
        Assert.AreEqual("alpha", fix.Vfs.ReadAllText(Path.Combine(tar, "a.txt")));
        Assert.AreEqual("bravo", fix.Vfs.ReadAllText(Path.Combine(tar, "docs", "b.txt")));
    }

    [TestMethod]
    public void TarGz_ResolvesInnerTarByNesting()
    {
        using var fix = new Dir();
        var tar = Path.Combine(fix.Root, "bundle.tar");
        TarFile.CreateFromDirectory(fix.Src, tar, includeBaseDirectory: false);
        var targz = Path.Combine(fix.Root, "bundle.tar.gz");
        using (var s = File.OpenRead(tar))
        using (var d = File.Create(targz))
        using (var gz = new GZipStream(d, CompressionLevel.Optimal))
            s.CopyTo(gz);

        // Top level of the .tar.gz is the single inner "bundle.tar"…
        var top = fix.Vfs.EnumerateEntries(targz).Select(e => e.Name).ToList();
        CollectionAssert.AreEqual(new[] { "bundle.tar" }, top);

        // …which the VFS descends into (double nesting) to reach the real files.
        var innerTar = Path.Combine(targz, "bundle.tar");
        Assert.AreEqual("alpha", fix.Vfs.ReadAllText(Path.Combine(innerTar, "a.txt")));
        Assert.AreEqual("bravo", fix.Vfs.ReadAllText(Path.Combine(innerTar, "docs", "b.txt")));
    }

    private sealed class Dir : IDisposable
    {
        public string Root { get; } = Path.Combine(Path.GetTempPath(), "nexa-tar-" + Guid.NewGuid().ToString("N"));
        public string Src { get; }
        public VirtualFileSystem Vfs { get; }

        public Dir()
        {
            Directory.CreateDirectory(Root);
            Src = Path.Combine(Root, "src");
            Directory.CreateDirectory(Path.Combine(Src, "docs"));
            File.WriteAllText(Path.Combine(Src, "a.txt"), "alpha");
            File.WriteAllText(Path.Combine(Src, "docs", "b.txt"), "bravo");

            Vfs = new VirtualFileSystem();
            Vfs.RegisterHandler(new SharpCompressHandler());
        }

        public void Dispose() { try { Directory.Delete(Root, true); } catch { } }
    }
}
