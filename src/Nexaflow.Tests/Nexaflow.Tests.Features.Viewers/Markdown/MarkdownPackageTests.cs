using System.IO;
using System.IO.Compression;
using Nexaflow.Features.Compressed.Handlers;
using Nexaflow.IO.Common;
using Nexaflow.Tests.Fixtures;
using Nexaflow.Visuals.Text.Markdown;

namespace Nexaflow.Tests.Features.Markdown;

/// <summary>
/// A Markdown Package: the document and every file it names beside itself zipped together, its addresses written to
/// <c>attachments/</c>; unpacked into a folder it reads as it was written — reached through the file system wherever it is,
/// a zip inside a zip among them — and nothing in it lands outside that folder.
/// </summary>
[TestClass]
[CoversNode("markdown-package")]
public class MarkdownPackageTests
{
    private string _dir = "";

    [TestInitialize]
    public void Make()
    {
        // The app's file system has its archive handlers from the Compressed feature; a test process registers the zip one.
        if (!VirtualFileSystem.Instance.CanCreate("a" + MarkdownPackage.Extension))
            VirtualFileSystem.Instance.RegisterHandler(new ZipArchiveHandler());
        _dir = TestTemp.CreateSubdirectory("nexaflow-mdz-").FullName;
    }

    [TestCleanup]
    public void Clean()
    {
        try { Directory.Delete(_dir, recursive: true); } catch { }
    }

    private void Beside(string relative, byte[] bytes)
    {
        string path = Path.Combine(_dir, "doc", relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, bytes);
    }

    private static string DocumentIn(string package)
    {
        using var zip = ZipFile.OpenRead(package);
        using var reader = new StreamReader(zip.GetEntry(MarkdownPackage.Document)!.Open());
        return reader.ReadToEnd();
    }

    [TestMethod]
    public async Task APackage_HoldsTheDocumentAndItsAttachments_ItsAddressesWrittenToThem()
    {
        Beside(Path.Combine("scan.figures", "figure 1.png"), [1]);
        Beside(Path.Combine("more", "figure 1.png"), [2]);
        Beside("spec.pdf", [3]);
        string package = Path.Combine(_dir, "notes.mdz");

        await MarkdownPackage.PackAsync(VirtualFileSystem.Instance, "![a](scan.figures/figure%201.png) ![b](scan.figures/figure%201.png) ![c](<more/figure 1.png>)\n\n" +
                             "[spec][spec] [web](https://example.com/a.png)\n\n[spec]: spec.pdf\n",
                             Path.Combine(_dir, "doc"), package);

        using (var zip = ZipFile.OpenRead(package))
            CollectionAssert.IsSubsetOf(
                new[] { "document.md", "attachments/figure 1.png", "attachments/figure 1 (2).png", "attachments/spec.pdf" },
                zip.Entries.Select(entry => entry.FullName.TrimEnd('/')).ToArray(),
                "one name each, the second of two the same numbered");
        string document = DocumentIn(package);
        Assert.AreEqual(2, document.Split("](attachments/figure%201.png)").Length - 1, "a picture shown twice is one attachment, named twice");
        StringAssert.Contains(document, "[spec]: attachments/spec.pdf", "a link's definition is written to its attachment too");
        StringAssert.Contains(document, "(https://example.com/a.png)", "what is not beside it is left as written");
    }

    [TestMethod]
    public async Task UnpackedAgain_ItReadsAsItWasPacked_ItsAttachmentsBesideIt()
    {
        Beside(Path.Combine("scan.figures", "figure 1.png"), [1, 2, 3]);
        string package = Path.Combine(_dir, "notes.mdz");
        await MarkdownPackage.PackAsync(VirtualFileSystem.Instance, "![a](scan.figures/figure%201.png)\n", Path.Combine(_dir, "doc"), package);

        string folder = Path.Combine(_dir, "open");
        string text = await MarkdownPackage.UnpackAsync(VirtualFileSystem.Instance, package, folder);

        Assert.AreEqual("![a](attachments/figure%201.png)\n", text);
        CollectionAssert.AreEqual(new byte[] { 1, 2, 3 }, File.ReadAllBytes(Path.Combine(folder, "attachments", "figure 1.png")));
    }

    [TestMethod]
    public async Task APackageInsideAZip_IsReadAndWrittenThroughTheFileSystem()
    {
        Beside("photo.png", [7, 7]);
        string outer = Path.Combine(_dir, "outer.zip");
        using (var zip = ZipFile.Open(outer, ZipArchiveMode.Create)) zip.CreateEntry("readme.txt");
        string package = Path.Combine(outer, "notes.mdz");

        await MarkdownPackage.PackAsync(VirtualFileSystem.Instance, "# Inside\n\n![p](photo.png)\n", Path.Combine(_dir, "doc"), package);
        string text = await MarkdownPackage.UnpackAsync(VirtualFileSystem.Instance, package, Path.Combine(_dir, "open"));

        Assert.AreEqual("# Inside\n\n![p](attachments/photo.png)\n", text);
        CollectionAssert.AreEqual(new byte[] { 7, 7 }, File.ReadAllBytes(Path.Combine(_dir, "open", "attachments", "photo.png")));
    }

    [TestMethod]
    public async Task PackedAgain_ItIsReplacedWhole()
    {
        string package = Path.Combine(_dir, "notes.mdz");
        await MarkdownPackage.PackAsync(VirtualFileSystem.Instance, "first\n", _dir, package);
        await MarkdownPackage.PackAsync(VirtualFileSystem.Instance, "second\n", _dir, package);

        Assert.AreEqual("second\n", await MarkdownPackage.UnpackAsync(VirtualFileSystem.Instance, package, Path.Combine(_dir, "open")));
    }

    [TestMethod]
    public async Task AnEntryReachingOutOfTheFolder_IsNotWritten()
    {
        string package = Path.Combine(_dir, "sly.mdz");
        using (var zip = ZipFile.Open(package, ZipArchiveMode.Create))
        {
            using (var writer = new StreamWriter(zip.CreateEntry("document.md").Open())) writer.Write("hi");
            using (var writer = new StreamWriter(zip.CreateEntry("../outside.txt").Open())) writer.Write("no");
        }

        await MarkdownPackage.UnpackAsync(VirtualFileSystem.Instance, package, Path.Combine(_dir, "open"));

        Assert.IsFalse(File.Exists(Path.Combine(_dir, "outside.txt")));
    }

    [TestMethod]
    public async Task AZipWithNoDocument_IsNoPackage()
    {
        string package = Path.Combine(_dir, "empty.mdz");
        using (var zip = ZipFile.Open(package, ZipArchiveMode.Create)) zip.CreateEntry("attachments/a.png");

        await Assert.ThrowsExactlyAsync<InvalidDataException>(() => MarkdownPackage.UnpackAsync(VirtualFileSystem.Instance, package, Path.Combine(_dir, "open")));
    }

    [TestMethod]
    public async Task APackage_IsReadWhereItStands_AsAMarkdownFileIs()
    {
        Beside("photo.png", [7, 7]);
        string package = Path.Combine(_dir, "notes.mdz");
        await MarkdownPackage.PackAsync(VirtualFileSystem.Instance, "# Notes\r\n\r\n![Photo](photo.png)\r\n", Path.Combine(_dir, "doc"), package);
        string file = Path.Combine(_dir, "notes.md");
        File.WriteAllText(file, "# Notes\n");

        Assert.IsTrue(MarkdownDocuments.Names(package) && MarkdownDocuments.Names(file) && !MarkdownDocuments.Names(Path.Combine(_dir, "notes.txt")));
        Assert.AreEqual("# Notes\n\n![Photo](attachments/photo.png)\n", await MarkdownDocuments.ReadTextAsync(VirtualFileSystem.Instance, package), "its own document, read inside it, its lines as the editor has them");
        Assert.AreEqual(package, MarkdownDocuments.FolderOf(package), "the files it names are read inside it");
        CollectionAssert.AreEqual(new byte[] { 7, 7 }, VirtualFileSystem.Instance.ReadAllBytes(Path.Combine(MarkdownDocuments.FolderOf(package)!, "attachments", "photo.png")));
        Assert.AreEqual(_dir, MarkdownDocuments.FolderOf(file), "and a file's, beside it");
        Assert.IsNull(await MarkdownDocuments.ReadTextAsync(VirtualFileSystem.Instance, Path.Combine(_dir, "gone.mdz")), "nothing there is nothing to read");
    }
}
