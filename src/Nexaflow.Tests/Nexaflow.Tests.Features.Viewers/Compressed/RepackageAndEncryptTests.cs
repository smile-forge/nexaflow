using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using Nexaflow.Features.Compressed.Handlers;
using Nexaflow.Features.Compressed.SecureZip;
using Nexaflow.IO.Common;
using Nexaflow.Tests.Fixtures;
using SharpCompressHandlerNs = Nexaflow.Features.Compressed.SharpCompress;

namespace Nexaflow.Tests.Features.Compressed;

/// <summary>Convert (zip → tar) and Recompress (rebuild with a level) via the VFS, plus AES encrypt →
/// decrypt round-trip.</summary>
[TestClass]
public class RepackageAndEncryptTests
{
    [TestMethod]
    [CoversNode("compressed-convert")]
    public void Convert_ZipToTar_PreservesEntries()
    {
        var dir = NewDir();
        try
        {
            var zip = Path.Combine(dir, "a.zip");
            using (var z = ZipFile.Open(zip, ZipArchiveMode.Create))
            {
                Add(z, "readme.txt", "alpha");
                Add(z, "docs/notes.txt", "bravo");
            }

            var vfs = new VirtualFileSystem();
            vfs.RegisterHandler(new ZipArchiveHandler());
            vfs.RegisterHandler(new SharpCompressHandlerNs.SharpCompressHandler());

            var tar = Path.Combine(dir, "a.tar");
            vfs.Repackage(zip, tar, null);

            Assert.IsTrue(File.Exists(tar));
            Assert.AreEqual("alpha", vfs.ReadAllText(Path.Combine(tar, "readme.txt")));
            Assert.AreEqual("bravo", vfs.ReadAllText(Path.Combine(tar, "docs", "notes.txt")));
        }
        finally { Cleanup(dir); }
    }

    [TestMethod]
    [CoversNode("compressed-recompress")]
    public void Recompress_RebuildsInPlace_KeepingContents()
    {
        var dir = NewDir();
        try
        {
            var zip = Path.Combine(dir, "a.zip");
            using (var z = ZipFile.Open(zip, ZipArchiveMode.Create)) Add(z, "f.txt", "data");

            var vfs = new VirtualFileSystem();
            vfs.RegisterHandler(new ZipArchiveHandler());
            vfs.Repackage(zip, zip, new ArchiveWriteOptions { CompressionLevel = 0 });

            Assert.AreEqual("data", vfs.ReadAllText(Path.Combine(zip, "f.txt")));
        }
        finally { Cleanup(dir); }
    }

    [TestMethod]
    [CoversNode("compressed-encrypt")]
    [CoversNode("compressed-decrypt")]
    public void Encrypt_ThenDecrypt_RoundTrips()
    {
        var dir = NewDir();
        try
        {
            var zip = Path.Combine(dir, "a.zip");
            using (var z = ZipFile.Open(zip, ZipArchiveMode.Create))
            {
                Add(z, "secret.txt", "classified");
                Add(z, "docs/info.txt", "details");
            }

            var enc = new SecureZipEncryptor();
            var encrypted = Path.Combine(dir, "a.enc.zip");
            enc.Encrypt(zip, encrypted, "hunter2");

            // The encrypted zip is not readable by the in-box (no-encryption) handler.
            var vfs = new VirtualFileSystem();
            vfs.RegisterHandler(new ZipArchiveHandler());
            bool threw = false;
            try { vfs.ReadAllText(Path.Combine(encrypted, "secret.txt")); }
            catch { threw = true; }
            Assert.IsTrue(threw, "the in-box handler should not be able to read an AES-encrypted entry");

            // Decrypt with the right password and verify contents.
            var decrypted = Path.Combine(dir, "a.dec.zip");
            enc.Decrypt(encrypted, decrypted, "hunter2");
            Assert.AreEqual("classified", vfs.ReadAllText(Path.Combine(decrypted, "secret.txt")));
            Assert.AreEqual("details", vfs.ReadAllText(Path.Combine(decrypted, "docs", "info.txt")));
        }
        finally { Cleanup(dir); }
    }

    private static void Add(ZipArchive zip, string path, string body)
    {
        using var w = new StreamWriter(zip.CreateEntry(path).Open());
        w.Write(body);
    }

    private static string NewDir()
    {
        var d = Path.Combine(Path.GetTempPath(), "nexa-repack-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(d);
        return d;
    }

    private static void Cleanup(string dir) { try { Directory.Delete(dir, true); } catch { } }
}
