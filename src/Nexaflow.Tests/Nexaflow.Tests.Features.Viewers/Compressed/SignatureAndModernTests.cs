using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using Nexaflow.Features.Compressed.Modern;
using Nexaflow.Features.Compressed.Services;
using Nexaflow.IO.Common;
using Nexaflow.Tests.Fixtures;
using ZstdSharp;

namespace Nexaflow.Tests.Features.Compressed;

/// <summary>Detached sign/verify round-trip + tamper detection, and the Modern (zstd) backend.</summary>
[TestClass]
public class SignatureAndModernTests
{
    [TestMethod]
    [CoversNode("compressed-sign")]
    [CoversNode("compressed-verify")]
    public void Sign_ThenVerify_IsValid_AndDetectsTampering()
    {
        var dir = NewDir();
        try
        {
            var archive = Path.Combine(dir, "a.zip");
            File.WriteAllBytes(archive, [1, 2, 3, 4, 5]);
            var signer = new ArchiveSignatureService(Path.Combine(dir, "keys"));

            var fingerprint = signer.Sign(archive);
            Assert.IsTrue(File.Exists(archive + ".sig"));
            Assert.IsFalse(string.IsNullOrEmpty(fingerprint));

            var (valid, fp) = signer.Verify(archive);
            Assert.IsTrue(valid);
            Assert.AreEqual(fingerprint, fp);

            // Tamper with the archive — verification must now fail.
            File.WriteAllBytes(archive, [1, 2, 3, 4, 6]);
            Assert.IsFalse(signer.Verify(archive).Valid);
        }
        finally { Cleanup(dir); }
    }

    [TestMethod]
    [CoversNode("compressed-verify")]
    public void Verify_NoSidecar_ReturnsNotValidNullFingerprint()
    {
        var dir = NewDir();
        try
        {
            var archive = Path.Combine(dir, "unsigned.zip");
            File.WriteAllBytes(archive, [9, 9, 9]);
            var (valid, fp) = new ArchiveSignatureService(Path.Combine(dir, "keys")).Verify(archive);
            Assert.IsFalse(valid);
            Assert.IsNull(fp);
        }
        finally { Cleanup(dir); }
    }

    [TestMethod]
    [CoversNode("compressed-backend-modern")]
    public void Zstd_SingleStream_ReadsThroughVfs()
    {
        var dir = NewDir();
        try
        {
            // Compress a known payload to data.txt.zst.
            var payload = "the quick brown fox"u8.ToArray();
            var zst = Path.Combine(dir, "data.txt.zst");
            using (var outFs = File.Create(zst))
            using (var cs = new CompressionStream(outFs))
                cs.Write(payload, 0, payload.Length);

            var vfs = new VirtualFileSystem();
            vfs.RegisterHandler(new ModernCompressionHandler());

            Assert.IsTrue(vfs.IsContainer(zst));
            var names = vfs.EnumerateEntries(zst).Select(e => e.Name).ToList();
            CollectionAssert.AreEqual(new[] { "data.txt" }, names);
            Assert.AreEqual("the quick brown fox", vfs.ReadAllText(Path.Combine(zst, "data.txt")));
        }
        finally { Cleanup(dir); }
    }

    private static string NewDir()
    {
        var d = Path.Combine(Path.GetTempPath(), "nexa-sig-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(d);
        return d;
    }

    private static void Cleanup(string dir) { try { Directory.Delete(dir, true); } catch { } }
}
