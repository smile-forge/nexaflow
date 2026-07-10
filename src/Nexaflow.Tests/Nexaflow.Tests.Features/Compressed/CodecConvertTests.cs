using System;
using System.IO;
using System.IO.Compression;
using Nexaflow.Features.Compressed.Codecs;
using Nexaflow.Features.Compressed.Handlers;
using Nexaflow.IO.Common;
using Nexaflow.Tests.Fixtures;
using ModernNs = Nexaflow.Features.Compressed.Modern;
using SharpCompressNs = Nexaflow.Features.Compressed.SharpCompress;

namespace Nexaflow.Tests.Features.Compressed;

/// <summary>Convert via codec composition: a zip → tar.zst / tar.lz4 / tar.br / tar.bz2 and single-stream
/// .zst, each read back through the VFS. Exercises <see cref="IStreamCodec"/> + the tar+codec write path.</summary>
[TestClass]
[CoversNode("iocommon-codecs")]
public class CodecConvertTests
{
    private static VirtualFileSystem MakeVfs()
    {
        var vfs = new VirtualFileSystem();
        vfs.RegisterHandler(new ZipArchiveHandler());
        vfs.RegisterHandler(new SharpCompressNs.SharpCompressHandler());
        vfs.RegisterHandler(new ModernNs.ModernCompressionHandler());
        vfs.RegisterHandler(new BrotliArchiveHandler());
        vfs.RegisterCodec(new GzipCodec());
        vfs.RegisterCodec(new BrotliCodec());
        vfs.RegisterCodec(new ModernNs.ZstdCodec());
        vfs.RegisterCodec(new ModernNs.Lz4Codec());
        vfs.RegisterCodec(new SharpCompressNs.Bzip2Codec());
        return vfs;
    }

    [TestMethod]
    [DataRow("out.tar.zst")]
    [DataRow("out.tar.lz4")]
    [DataRow("out.tar.br")]
    [DataRow("out.tar.bz2")]
    public void Convert_ZipToTarCodec_RoundTrips(string targetName)
    {
        var dir = NewDir();
        try
        {
            var vfs = MakeVfs();
            var dest = Path.Combine(dir, targetName);
            vfs.Repackage(MakeZip(dir), dest, null);

            Assert.IsTrue(File.Exists(dest));
            Assert.IsTrue(vfs.IsContainer(dest));
            // A compressed tar surfaces a single inner ".tar" (named from the outer file) which the VFS nests into.
            var tar = Path.Combine(dest, "out.tar");
            Assert.AreEqual("alpha", vfs.ReadAllText(Path.Combine(tar, "readme.txt")));
            Assert.AreEqual("bravo", vfs.ReadAllText(Path.Combine(tar, "docs", "notes.txt")));
        }
        finally { Cleanup(dir); }
    }

    [TestMethod]
    public void Convert_SingleFileZip_ToZst_RoundTrips()
    {
        var dir = NewDir();
        try
        {
            var zip = Path.Combine(dir, "one.zip");
            using (var z = ZipFile.Open(zip, ZipArchiveMode.Create)) Add(z, "only.txt", "solo");

            var vfs = MakeVfs();
            var dest = Path.Combine(dir, "only.zst");
            vfs.Repackage(zip, dest, null);

            Assert.IsTrue(File.Exists(dest));
            // A .zst surfaces one inner file named by stripping the codec extension → "only".
            Assert.AreEqual("solo", vfs.ReadAllText(Path.Combine(dest, "only")));
        }
        finally { Cleanup(dir); }
    }

    [TestMethod]
    public void Convert_MultiFileZip_ToSingleStream_Throws()
    {
        var dir = NewDir();
        try
        {
            var vfs = MakeVfs();
            bool threw = false;
            try { vfs.Repackage(MakeZip(dir), Path.Combine(dir, "x.zst"), null); }
            catch (NotSupportedException) { threw = true; }
            Assert.IsTrue(threw, "a multi-file archive cannot convert to a single-stream .zst");
        }
        finally { Cleanup(dir); }
    }

    [TestMethod]
    public void CanCreate_ReflectsCodecTargets()
    {
        var vfs = MakeVfs();
        foreach (var t in new[] { "x.zip", "x.tar", "x.tar.gz", "x.tar.bz2", "x.tar.zst", "x.tar.lz4", "x.tar.br", "x.zst", "x.br" })
            Assert.IsTrue(vfs.CanCreate(t), $"expected CanCreate('{t}')");
        // xz / 7z have no in-process writer.
        Assert.IsFalse(vfs.CanCreate("x.7z"));
    }

    private static string MakeZip(string dir)
    {
        var zip = Path.Combine(dir, "a.zip");
        using var z = ZipFile.Open(zip, ZipArchiveMode.Create);
        Add(z, "readme.txt", "alpha");
        Add(z, "docs/notes.txt", "bravo");
        return zip;
    }

    private static void Add(ZipArchive zip, string path, string body)
    {
        using var w = new StreamWriter(zip.CreateEntry(path).Open());
        w.Write(body);
    }

    private static string NewDir()
    {
        var d = Path.Combine(Path.GetTempPath(), "nexa-codec-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(d);
        return d;
    }

    private static void Cleanup(string dir) { try { Directory.Delete(dir, true); } catch { } }
}
