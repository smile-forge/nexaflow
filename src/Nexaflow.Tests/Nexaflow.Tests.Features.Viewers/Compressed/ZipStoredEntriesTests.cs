using System.IO;
using System.IO.Compression;
using System.Text;
using Nexaflow.Features.Compressed.Handlers;
using Nexaflow.IO.Common;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.Features.Compressed;

/// <summary>
/// Entries written stored stay stored: an archive created from entries keeps their order and each one's compression, and a
/// rewrite through the file system — <c>Replace</c>'s rebuild — carries each entry's method across, so a package whose
/// <c>mimetype</c> must stay first and stored (OpenRaster, EPUB, OpenDocument) is still a valid package after an edit.
/// </summary>
[TestClass]
[CoversNode("compressed-backend-zip")]
public class ZipStoredEntriesTests
{
    private string _dir = "";

    [TestInitialize]
    public void Make() => _dir = TestTemp.CreateSubdirectory("nexaflow-stored-").FullName;

    [TestCleanup]
    public void Clean()
    {
        try { Directory.Delete(_dir, recursive: true); } catch { }
    }

    private static VirtualFileSystem NewVfs()
    {
        var vfs = new VirtualFileSystem();
        vfs.RegisterHandler(new ZipArchiveHandler());
        return vfs;
    }

    private static ArchiveWriteEntry Entry(string path, string text, ArchiveEntryCompression compression = ArchiveEntryCompression.Default)
    {
        var bytes = Encoding.UTF8.GetBytes(text);
        return new ArchiveWriteEntry { Path = path, OpenContent = () => new MemoryStream(bytes), Compression = compression };
    }

    /// <summary>The first entry's name and method, read from the file's first local header.</summary>
    private static (string Name, ushort Method) FirstEntry(string zip)
    {
        var bytes = File.ReadAllBytes(zip);
        ushort method = BitConverter.ToUInt16(bytes, 8);
        int nameLength = BitConverter.ToUInt16(bytes, 26);
        return (Encoding.ASCII.GetString(bytes, 30, nameLength), method);
    }

    [TestMethod]
    public void AnArchiveFromEntries_KeepsTheirOrder_AndStoresWhatAsksToBeStored()
    {
        var vfs = NewVfs();
        var zip = Path.Combine(_dir, "package.zip");
        var repeated = new string('a', 4000);
        vfs.CreateArchive(zip, [Entry("mimetype", "image/openraster", ArchiveEntryCompression.Stored), Entry("stack.xml", repeated), Entry("z/last.txt", repeated)]);

        Assert.AreEqual(("mimetype", (ushort)0), FirstEntry(zip), "first, and stored");
        using var archive = ZipFile.OpenRead(zip);
        CollectionAssert.AreEqual(new[] { "mimetype", "stack.xml", "z/last.txt" }, archive.Entries.Select(e => e.FullName).ToArray(), "in the order given");
        Assert.IsTrue(archive.GetEntry("stack.xml")!.CompressedLength < 4000, "the rest compressed as the archive is");
        Assert.AreEqual("Stored", vfs.GetEntryInfo(zip + "\\mimetype")!.Method, "the file system reports the method written");
        Assert.AreEqual("Deflate", vfs.GetEntryInfo(zip + "\\stack.xml")!.Method);
    }

    [TestMethod]
    public void ARewriteThroughTheFileSystem_KeepsAStoredEntryStoredAndFirst()
    {
        var vfs = NewVfs();
        var zip = Path.Combine(_dir, "book.epub");
        using (var archive = ZipFile.Open(zip, ZipArchiveMode.Create))
        {
            using (var mime = new StreamWriter(archive.CreateEntry("mimetype", CompressionLevel.NoCompression).Open())) mime.Write("application/epub+zip");
            using (var chapter = new StreamWriter(archive.CreateEntry("OEBPS/chapter.xhtml", CompressionLevel.Optimal).Open())) chapter.Write(new string('b', 4000));
        }

        vfs.Replace(zip + "\\OEBPS\\chapter.xhtml", Encoding.UTF8.GetBytes("<p>edited</p>" + new string('c', 4000)));

        Assert.AreEqual(("mimetype", (ushort)0), FirstEntry(zip), "an EPUB edited through the file system keeps its mimetype first and stored");
        Assert.AreEqual("Deflate", vfs.GetEntryInfo(zip + "\\OEBPS\\chapter.xhtml")!.Method, "and its chapters compressed");
        StringAssert.StartsWith(Encoding.UTF8.GetString(vfs.ReadAllBytes(zip + "\\OEBPS\\chapter.xhtml")), "<p>edited</p>");
    }
}
