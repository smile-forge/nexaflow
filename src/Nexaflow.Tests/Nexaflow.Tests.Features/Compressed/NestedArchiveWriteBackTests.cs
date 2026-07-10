using System;
using System.IO;
using System.IO.Compression;
using System.Text;
using Nexaflow.Features.Compressed.Handlers;
using Nexaflow.IO.Common;

namespace Nexaflow.Tests.Features.Compressed;

/// <summary>
/// Editing an entry that lives inside a <i>nested</i> archive. Resolution materialises each intermediate
/// container to a temp file, so a write-back that only rebuilds the innermost one lands in <c>%TEMP%</c> and
/// never reaches the archive on disk. Each level must be rebuilt back into its parent.
/// </summary>
[TestClass]
public class NestedArchiveWriteBackTests
{
    private VirtualFileSystem _vfs = null!;
    private string _dir = string.Empty;

    [TestInitialize]
    public void Setup()
    {
        _vfs = new VirtualFileSystem();               // isolated — never the process-wide Instance
        _vfs.RegisterHandler(new ZipArchiveHandler());
        _dir = Path.Combine(Path.GetTempPath(), "nexa-nested-wb-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
    }

    [TestCleanup]
    public void Cleanup() { try { Directory.Delete(_dir, recursive: true); } catch { } }

    [TestMethod]
    public void WriteAllText_EntryInTopLevelZip_WritesBackIntoArchive()
    {
        var outer = Path.Combine(_dir, "outer.zip");
        WriteZipFile(outer, ("x.txt", Utf8("original")));

        _vfs.WriteAllText(Path.Combine(outer, "x.txt"), "EDITED", new UTF8Encoding(false));

        Assert.AreEqual("EDITED", EntryText(File.ReadAllBytes(outer), "x.txt"));
    }

    [TestMethod]
    public void WriteAllText_EntryInNestedZip_WritesBackIntoOuterArchive()
    {
        var inner = BuildZip(("x.txt", Utf8("original")));
        var outer = Path.Combine(_dir, "outer.zip");
        WriteZipFile(outer, ("inner.zip", inner), ("sibling.txt", Utf8("untouched")));

        var virtualPath = Path.Combine(outer, "inner.zip", "x.txt");
        Assert.AreEqual("original", _vfs.ReadAllText(virtualPath), "sanity: the nested read already worked");

        _vfs.WriteAllText(virtualPath, "EDITED", new UTF8Encoding(false));

        // Read outer.zip straight off disk — the edit must be inside the inner.zip entry it holds.
        var outerBytes = File.ReadAllBytes(outer);
        Assert.AreEqual("EDITED", EntryText(EntryBytes(outerBytes, "inner.zip"), "x.txt"));
        Assert.AreEqual("untouched", Encoding.UTF8.GetString(EntryBytes(outerBytes, "sibling.txt")),
            "rebuilding must carry every other entry through unchanged");
    }

    [TestMethod]
    public void WriteAllText_EntryThreeLevelsDeep_WritesBackIntoOuterArchive()
    {
        var inner  = BuildZip(("x.txt", Utf8("original")));
        var middle = BuildZip(("inner.zip", inner));
        var outer  = Path.Combine(_dir, "outer.zip");
        WriteZipFile(outer, ("middle.zip", middle));

        _vfs.WriteAllText(Path.Combine(outer, "middle.zip", "inner.zip", "x.txt"), "EDITED", new UTF8Encoding(false));

        var deep = EntryBytes(EntryBytes(File.ReadAllBytes(outer), "middle.zip"), "inner.zip");
        Assert.AreEqual("EDITED", EntryText(deep, "x.txt"));
    }

    [TestMethod]
    public void ReadAfterWrite_EntryInNestedZip_SeesTheNewContent()
    {
        var inner = BuildZip(("x.txt", Utf8("original")));
        var outer = Path.Combine(_dir, "outer.zip");
        WriteZipFile(outer, ("inner.zip", inner));

        var virtualPath = Path.Combine(outer, "inner.zip", "x.txt");
        _vfs.ReadAllText(virtualPath);                       // prime the materialised temps
        _vfs.WriteAllText(virtualPath, "EDITED", new UTF8Encoding(false));

        // The stale temps for both containers must have been invalidated, not served again.
        Assert.AreEqual("EDITED", _vfs.ReadAllText(virtualPath));
    }

    // ── Zip helpers (System.IO.Compression, independent of the handler under test) ──

    private static byte[] Utf8(string s) => new UTF8Encoding(false).GetBytes(s);

    private static byte[] BuildZip(params (string Name, byte[] Body)[] entries)
    {
        using var ms = new MemoryStream();
        using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
            foreach (var (name, body) in entries)
            {
                using var s = zip.CreateEntry(name).Open();
                s.Write(body, 0, body.Length);
            }
        return ms.ToArray();
    }

    private static void WriteZipFile(string path, params (string Name, byte[] Body)[] entries)
        => File.WriteAllBytes(path, BuildZip(entries));

    private static byte[] EntryBytes(byte[] zipBytes, string entryName)
    {
        using var zip = new ZipArchive(new MemoryStream(zipBytes), ZipArchiveMode.Read);
        using var src = zip.GetEntry(entryName)!.Open();
        using var ms  = new MemoryStream();
        src.CopyTo(ms);
        return ms.ToArray();
    }

    private static string EntryText(byte[] zipBytes, string entryName)
        => Encoding.UTF8.GetString(EntryBytes(zipBytes, entryName));
}
