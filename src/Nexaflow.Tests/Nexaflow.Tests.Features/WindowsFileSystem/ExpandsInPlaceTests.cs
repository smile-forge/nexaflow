using System;
using System.IO;
using Nexaflow.Features.WindowsFileSystem.Services;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.Features.WindowsFileSystem;

/// <summary>
/// <see cref="DefaultFileOpener.ExpandsInPlace"/> is the single policy for "browse this container as a
/// folder by default" vs "open its own viewer". It is driven purely by the bundled <c>/archive</c> filemap
/// mapping: a format mapped there by <see cref="Nexaflow.Features.WindowsFileSystem.FileActions.CriteriaType.Extension"/>
/// (a real archive) expands in place; one mapped only by <c>OptionalExtension</c> (a zip-based document) or
/// not mapped there at all (an email) keeps its viewer. This is the regression bar for the routing that
/// <see cref="Nexaflow.Features.WindowsFileSystem.ViewModels.FileSystemViewModel"/> and
/// <see cref="Nexaflow.Features.WindowsFileSystem.FileSystemObjectHandler"/> share.
/// </summary>
[TestClass]
[CoversNode("winfs-open-entry")]   // the expand-in-place vs open-viewer decision behind double-click open
[DoNotParallelize]                 // mutates the process-wide FileMapManager.Instance
public class ExpandsInPlaceTests
{
    [ClassInitialize]
    public static void Init(TestContext _)
    {
        var dir = Path.Combine(Path.GetTempPath(), "nexa-expand-filemap-" + Guid.NewGuid().ToString("N"));
        FileMapManager.Instance.Initialize(baseDir: dir);
    }

    [DataTestMethod]
    [DataRow(@"C:\x\a.zip")]
    [DataRow(@"C:\x\a.tar.gz")]
    [DataRow(@"C:\x\a.7z")]
    [DataRow(@"C:\x\a.rar")]
    [DataRow(@"C:\x\a.nupkg")]
    public void RealArchives_ExpandInPlace(string path)
        => Assert.IsTrue(DefaultFileOpener.ExpandsInPlace(path), $"{path} is a real archive; it should browse in place");

    [DataTestMethod]
    [DataRow(@"C:\x\a.iso")]    // /disk/mountable → satisfies /disk at extension level
    [DataRow(@"C:\x\a.vhd")]
    [DataRow(@"C:\x\a.vhdx")]
    [DataRow(@"C:\x\a.vmdk")]   // mapped directly to /disk
    [DataRow(@"C:\x\a.vdi")]
    [DataRow(@"C:\x\a.dmg")]
    [DataRow(@"C:\x\a.img")]
    public void DiskImages_ExpandInPlace(string path)
        => Assert.IsTrue(DefaultFileOpener.ExpandsInPlace(path), $"{path} is a disk image; it should browse in place");

    [DataTestMethod]
    [DataRow(@"C:\x\a.docx")]   // zip-based document: /archive only by OptionalExtension
    [DataRow(@"C:\x\a.xlsx")]
    [DataRow(@"C:\x\a.pptx")]
    [DataRow(@"C:\x\a.odt")]
    [DataRow(@"C:\x\a.epub")]
    [DataRow(@"C:\x\a.eml")]    // email: not in /archive at all
    [DataRow(@"C:\x\a.msg")]
    [DataRow(@"C:\x\a.txt")]    // not a container at all
    public void DocumentsAndEmail_KeepTheirViewer(string path)
        => Assert.IsFalse(DefaultFileOpener.ExpandsInPlace(path), $"{path} should open its own viewer, not a folder tab");
}
