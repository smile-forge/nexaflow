using Nexaflow.Features.Common;
using Nexaflow.Features.Compressed.Handlers;
using Nexaflow.Features.Markdown.ViewModels;
using Nexaflow.IO.Common;
using Nexaflow.Tests.Fixtures;
using Nexaflow.Visuals.Text.Markdown;
using NSubstitute;
using System.IO;
using System.IO.Compression;

namespace Nexaflow.Tests.Features.Markdown;

/// <summary>
/// Save as, drafts and Markdown Packages in the editor: the document written where the reader picks — a package first,
/// its attachments inside it, or a file with them beside it — the tab then that file; a draft kept in a temp folder saved
/// where the reader picks, starting where it was made for; a package edited unpacked and packed again on save.
/// </summary>
[TestClass]
[CoversNode("markdown-save-as")]
public class MarkdownSaveAsTests
{
    private string _dir = "";

    [TestInitialize]
    public void Make()
    {
        if (!VirtualFileSystem.Instance.CanCreate("a" + MarkdownPackage.Extension))
            VirtualFileSystem.Instance.RegisterHandler(new ZipArchiveHandler());
        _dir = TestTemp.CreateSubdirectory("nexaflow-md-save-as-").FullName;
    }

    [TestCleanup]
    public void Clean()
    {
        try { Directory.Delete(_dir, recursive: true); } catch { }
    }

    private static IShellServices Picking(string? target)
    {
        var shell = Substitute.For<IShellServices>();
        shell.PickSaveFileAsync(Arg.Any<string>(), Arg.Any<IReadOnlyList<SaveFileType>>(), Arg.Any<string?>(), Arg.Any<long>())
             .Returns(Task.FromResult(target));
        return shell;
    }

    /// <summary>A document in <c>draft\</c> showing a picture beside it, one from the web and one up out of its folder.</summary>
    private string Document()
    {
        string folder = Path.Combine(_dir, "draft");
        Directory.CreateDirectory(Path.Combine(folder, "notes.figures"));
        File.WriteAllBytes(Path.Combine(folder, "notes.figures", "figure 1.png"), [1, 2, 3]);
        File.WriteAllBytes(Path.Combine(_dir, "up.png"), [4]);
        string path = Path.Combine(folder, "notes.md");
        File.WriteAllText(path, "# Notes\n\n![one](notes.figures/figure%201.png)\n\n![web](https://example.com/a.png)\n\n![up](../up.png)\n");
        return path;
    }

    [TestMethod]
    public async Task SaveAs_OffersAPackageFirst_ThenAMarkdownFile_StartingBesideTheDocument()
    {
        string source = Document();
        var shell = Picking(null);

        await new MarkdownViewModel(source, shell, VirtualFileSystem.Instance).SaveAsCommand.ExecuteAsync(null);

        await shell.Received(1).PickSaveFileAsync("notes",
            Arg.Is<IReadOnlyList<SaveFileType>>(types => types.Select(t => t.Extension).SequenceEqual(new[] { ".mdz", ".md" })
                                                         && types[0].Name == "Markdown Package"),
            Path.GetDirectoryName(source), Arg.Any<long>());
    }

    [TestMethod]
    public async Task SaveAsAPackage_PutsTheDocumentAndItsAttachmentsInIt_AndIsThatFileFromThen()
    {
        string source = Document();
        string target = Path.Combine(_dir, "kept", "notes.mdz");
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        var vm = new MarkdownViewModel(source, Picking(target), VirtualFileSystem.Instance);
        await vm.Loaded;
        string? moved = null;
        vm.Moved += path => moved = path;

        await vm.SaveAsCommand.ExecuteAsync(null);

        using (var zip = ZipFile.OpenRead(target))
        {
            Assert.IsNotNull(zip.GetEntry("attachments/figure 1.png"), "the picture it shows from beside itself is inside it");
            Assert.IsNull(zip.Entries.FirstOrDefault(e => e.Name == "up.png"), "one up out of its folder is not");
        }
        CollectionAssert.AreEqual(new[] { "notes.mdz" }, Directory.GetFileSystemEntries(Path.GetDirectoryName(target)!).Select(Path.GetFileName).ToArray(),
                                  "nothing else is written beside a package");
        Assert.AreEqual(target, vm.FilePath);
        Assert.AreEqual(target, moved, "the tab is told the file it now is");
        Assert.IsFalse(vm.IsDirty);
    }

    [TestMethod]
    public async Task SaveAsAMarkdownFile_PutsItsAttachmentsBesideIt()
    {
        string source = Document();
        string target = Path.Combine(_dir, "kept", "notes.md");
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        var vm = new MarkdownViewModel(source, Picking(target), VirtualFileSystem.Instance);
        await vm.Loaded;

        await vm.SaveAsCommand.ExecuteAsync(null);

        Assert.AreEqual(File.ReadAllText(source), File.ReadAllText(target));
        CollectionAssert.AreEquivalent(new[] { "notes.md", "notes.figures" },
                                       Directory.GetFileSystemEntries(Path.Combine(_dir, "kept")).Select(Path.GetFileName).ToArray(),
                                       "only what it shows from beside itself is carried — not the web picture, not one up out of its folder");
        CollectionAssert.AreEqual(new byte[] { 1, 2, 3 }, File.ReadAllBytes(Path.Combine(_dir, "kept", "notes.figures", "figure 1.png")));
        Assert.AreEqual(Path.Combine(_dir, "kept"), vm.DocumentFolder, "its pictures are read from beside the new file");
    }

    [TestMethod]
    public async Task SaveAs_Cancelled_LeavesTheDocumentWhereItWas()
    {
        string source = Document();
        var vm = new MarkdownViewModel(source, Picking(null), VirtualFileSystem.Instance);
        await vm.Loaded;
        vm.Markdown += "More.\n";

        await vm.SaveAsCommand.ExecuteAsync(null);

        Assert.AreEqual(source, vm.FilePath);
        Assert.IsTrue(vm.IsDirty, "nothing was saved");
    }

    [TestMethod]
    public async Task ADraft_IsSavedWhereTheReaderPicks_StartingInTheFolderItWasMadeFor()
    {
        string draft = Document();
        string home = Path.Combine(_dir, "scans");
        Directory.CreateDirectory(home);
        string target = Path.Combine(home, "notes.mdz");
        var shell = Picking(target);
        var vm = new MarkdownViewModel(draft, shell, VirtualFileSystem.Instance, draftFolder: home);
        await vm.Loaded;

        Assert.IsFalse(vm.IsDirty);
        Assert.IsTrue(vm.SaveCommand.CanExecute(null), "a draft is there to be saved before anything is changed in it");

        await vm.SaveCommand.ExecuteAsync(null);

        await shell.Received(1).PickSaveFileAsync("notes", Arg.Any<IReadOnlyList<SaveFileType>>(), home, Arg.Any<long>());
        Assert.IsTrue(File.Exists(target));
        Assert.IsNull(vm.DraftFolder, "saved, it is no longer a draft");
        Assert.IsFalse(vm.SaveCommand.CanExecute(null), "and Save writes it in place again only once it is changed");
    }

    [TestMethod]
    public async Task APackage_IsEditedUnpacked_AndPackedAgainOnSave()
    {
        string source = Document();
        string package = Path.Combine(_dir, "notes.mdz");
        await MarkdownPackage.PackAsync(VirtualFileSystem.Instance, File.ReadAllText(source), Path.GetDirectoryName(source)!, package);
        var vm = new MarkdownViewModel(package, Substitute.For<IShellServices>(), VirtualFileSystem.Instance);
        await vm.Loaded;

        StringAssert.Contains(vm.Markdown, "](attachments/figure%201.png)", "opened, it reads as it is written in the package");
        Assert.IsTrue(File.Exists(Path.Combine(vm.DocumentFolder!, "attachments", "figure 1.png")), "its attachments beside it while it is open");

        vm.Markdown += "\nMore.\n";
        await vm.SaveCommand.ExecuteAsync(null);
        string folder = vm.DocumentFolder!;
        vm.Release();

        using (var zip = ZipFile.OpenRead(package))
        {
            using var reader = new StreamReader(zip.GetEntry("document.md")!.Open());
            StringAssert.EndsWith(reader.ReadToEnd(), "More.\n");
            Assert.IsNotNull(zip.GetEntry("attachments/figure 1.png"), "its attachments are packed with it again");
        }
        Assert.IsFalse(Directory.Exists(folder), "closed, the folder it was unpacked in goes");
    }
}
