using System;
using System.IO;
using System.Linq;
using Nexaflow.Features.Compressed.Handlers;
using Nexaflow.IO.Common;

namespace Nexaflow.Tests.Features.Compressed;

/// <summary>VFS create + extract used by the "Zip It" / "Unzip here" actions, including the zip-slip guard.</summary>
[TestClass]
public class ArchiveActionsTests
{
    [TestMethod]
    public void CreateThenExtract_RoundTrips()
    {
        using var fix = new Dir();
        var src = Path.Combine(fix.Root, "src");
        Directory.CreateDirectory(Path.Combine(src, "sub"));
        File.WriteAllText(Path.Combine(src, "a.txt"), "alpha");
        File.WriteAllText(Path.Combine(src, "sub", "b.txt"), "bravo");

        var zip = Path.Combine(fix.Root, "out.zip");
        fix.Vfs.CreateArchive(zip, src);
        Assert.IsTrue(File.Exists(zip));

        var dest = Path.Combine(fix.Root, "extracted");
        fix.Vfs.ExtractAll(zip, dest);

        Assert.AreEqual("alpha", File.ReadAllText(Path.Combine(dest, "a.txt")));
        Assert.AreEqual("bravo", File.ReadAllText(Path.Combine(dest, "sub", "b.txt")));
    }

    [TestMethod]
    public void ExtractAll_SkipsZipSlipEntries()
    {
        using var fix = new Dir();
        // Hand-craft a zip with a traversing entry name.
        var zip = Path.Combine(fix.Root, "evil.zip");
        using (var fs = new FileStream(zip, FileMode.Create))
        using (var za = new System.IO.Compression.ZipArchive(fs, System.IO.Compression.ZipArchiveMode.Create))
        {
            using (var w1 = new StreamWriter(za.CreateEntry("../escape.txt").Open())) w1.Write("nope");
            using (var w2 = new StreamWriter(za.CreateEntry("safe.txt").Open())) w2.Write("ok");
        }

        var dest = Path.Combine(fix.Root, "dest");
        fix.Vfs.ExtractAll(zip, dest);

        Assert.IsTrue(File.Exists(Path.Combine(dest, "safe.txt")));
        Assert.IsFalse(File.Exists(Path.Combine(fix.Root, "escape.txt")), "zip-slip entry must not escape the destination");
    }

    private sealed class Dir : IDisposable
    {
        public string Root { get; } = Path.Combine(Path.GetTempPath(), "nexa-act-" + Guid.NewGuid().ToString("N"));
        public VirtualFileSystem Vfs { get; }
        public Dir()
        {
            Directory.CreateDirectory(Root);
            Vfs = new VirtualFileSystem();
            Vfs.RegisterHandler(new ZipArchiveHandler());
        }
        public void Dispose() { try { Directory.Delete(Root, true); } catch { } }
    }
}
