using System;
using System.IO;
using System.Linq;
using Nexaflow.Features.Common;
using Nexaflow.Features.Compressed;
using Nexaflow.Features.Compressed.FileActions;
using Nexaflow.Features.Compressed.Handlers;
using Nexaflow.IO.Common;
using Nexaflow.Tests.Fixtures;
using NSubstitute;

namespace Nexaflow.Tests.Features.Compressed;

/// <summary>VFS create + extract used by the "Zip It" / "Unzip here" actions, including the zip-slip guard.</summary>
[TestClass]
public class ArchiveActionsTests
{
    [TestMethod]
    [CoversNode("compressed-unzip-here")]
    [CoversNode("compressed-zip-it")]
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
    [CoversNode("compressed-unzip-here")]
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

    [TestMethod]
    [CoversNode("compressed-zip-it")]
    public void CreateArchive_MultiSelection_KeepsEachItemUnderItsOwnName()
    {
        using var fix = new Dir();
        File.WriteAllText(Path.Combine(fix.Root, "a.txt"), "alpha");
        var folder = Path.Combine(fix.Root, "notes");
        Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder, "a.txt"), "bravo");   // same leaf name as the loose file

        var zip = Path.Combine(fix.Root, "sel.zip");
        fix.Vfs.CreateArchive(zip, new[] { Path.Combine(fix.Root, "a.txt"), folder });

        var dest = Path.Combine(fix.Root, "extracted");
        fix.Vfs.ExtractAll(zip, dest);

        Assert.AreEqual("alpha", File.ReadAllText(Path.Combine(dest, "a.txt")));
        Assert.AreEqual("bravo", File.ReadAllText(Path.Combine(dest, "notes", "a.txt")),
            "a selected folder keeps its own name, so its entries cannot collide with a loose file's");
    }

    [TestMethod]
    [CoversNode("compressed-zip-it")]
    public void ZipIt_MultipleSelectedFiles_ProducesOneArchiveNamedForTheirFolder()
    {
        using var fix = new Dir();
        EnsureProcessVfsCanZip();

        var folder = Path.Combine(fix.Root, "reports");
        Directory.CreateDirectory(folder);
        foreach (var n in new[] { "one.txt", "two.txt", "three.txt" })
            File.WriteAllText(Path.Combine(folder, n), n);

        var action = new ZipItAction(Substitute.For<IShellServices>(), new CompressedConfig());
        Assert.IsTrue(action.PerformAction(new[] { Path.Combine(folder, "one.txt"), Path.Combine(folder, "two.txt") }));

        var zip = Path.Combine(folder, "reports.zip");
        Assert.IsTrue(File.Exists(zip), "the selection is zipped once, under the name of the folder it sits in");

        var dest = Path.Combine(fix.Root, "extracted");
        fix.Vfs.ExtractAll(zip, dest);
        CollectionAssert.AreEquivalent(
            new[] { "one.txt", "two.txt" },
            Directory.GetFiles(dest).Select(Path.GetFileName).ToArray(),
            "only the selected files go in - not the rest of the folder");
    }

    [TestMethod]
    [CoversNode("compressed-zip-it")]
    public void ZipIt_SingleFile_TakesItsNameWithTheExtensionReplaced()
    {
        using var fix = new Dir();
        EnsureProcessVfsCanZip();

        var doc = Path.Combine(fix.Root, "report.docx");
        File.WriteAllText(doc, "body");

        var action = new ZipItAction(Substitute.For<IShellServices>(), new CompressedConfig());
        Assert.IsTrue(action.PerformAction(doc));

        var zip = Path.Combine(fix.Root, "report.zip");
        Assert.IsTrue(File.Exists(zip), "'report.docx' zips to 'report.zip', not 'report.docx.zip'");

        var dest = Path.Combine(fix.Root, "extracted");
        fix.Vfs.ExtractAll(zip, dest);
        Assert.AreEqual("body", File.ReadAllText(Path.Combine(dest, "report.docx")));
    }

    /// <summary>The action reaches for the process-wide VFS by design, so the test process needs a zip
    /// backend registered on it. Idempotent: a second handler instance would only shadow the first.</summary>
    private static void EnsureProcessVfsCanZip()
    {
        if (!VirtualFileSystem.Instance.CanCreate("a.zip"))
            VirtualFileSystem.Instance.RegisterHandler(new ZipArchiveHandler());
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
