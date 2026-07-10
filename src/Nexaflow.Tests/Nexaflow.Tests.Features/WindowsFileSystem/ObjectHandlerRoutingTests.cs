using System;
using System.IO;
using System.IO.Compression;
using Nexaflow.Features.Compressed.Handlers;
using Nexaflow.Features.Email.Vfs;
using Nexaflow.Features.WindowsFileSystem;
using Nexaflow.Features.WindowsFileSystem.Services;
using Nexaflow.IO.Common;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.Features.WindowsFileSystem;

/// <summary>
/// <see cref="FileSystemObjectHandler.OpensAsFolder"/> decides, for a path handed to
/// <c>IShellServices.HandleObject</c> (e.g. opening an email attachment), whether to browse it as a folder
/// or fall through to the default viewer. A real archive browses in place; a container that keeps its own
/// viewer (<c>.eml</c>) opens that viewer — even nested, where the VFS reports the container's root as a
/// directory. Regression guard for "a forwarded .eml attachment opened as a FileSystem folder tab."
/// </summary>
[TestClass]
[CoversNode("winfs-open-entry")]
[DoNotParallelize]   // seeds the process-wide FileMapManager.Instance
public class ObjectHandlerRoutingTests
{
    [ClassInitialize]
    public static void Init(TestContext _)
    {
        var dir = Path.Combine(Path.GetTempPath(), "nexa-routing-filemap-" + Guid.NewGuid().ToString("N"));
        FileMapManager.Instance.Initialize(baseDir: dir);
    }

    private static IVirtualFileSystem Vfs()
    {
        var vfs = new VirtualFileSystem();
        vfs.RegisterHandler(new ZipArchiveHandler());
        vfs.RegisterHandler(new EmailArchiveHandler());
        return vfs;
    }

    [TestMethod]
    public void NestedEmailAttachment_OpensViewer_NotFolder()
    {
        // forwarded.eml embeds a message/rfc822 → attachment "Meeting notes.eml" (a nested container).
        var path = Path.Combine(TestSampleData.Path("email", "forwarded.eml"), "Meeting notes.eml");
        Assert.IsFalse(FileSystemObjectHandler.OpensAsFolder(Vfs(), path),
            "a forwarded .eml must open in the Email viewer, not a folder tab");
    }

    [TestMethod]
    public void EmailFile_OpensViewer_NotFolder()
        => Assert.IsFalse(FileSystemObjectHandler.OpensAsFolder(Vfs(), TestSampleData.Path("email", "simple.eml")));

    [TestMethod]
    public void PlainAttachment_OpensViewer_NotFolder()
    {
        var path = Path.Combine(TestSampleData.Path("email", "simple.eml"), "notes.txt");
        Assert.IsFalse(FileSystemObjectHandler.OpensAsFolder(Vfs(), path));
    }

    [TestMethod]
    public void RealArchive_AndItsFolders_BrowseInPlace()
    {
        var dir = Path.Combine(Path.GetTempPath(), "nexa-routing-zip-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var zip = Path.Combine(dir, "bundle.zip");
        try
        {
            using (var z = ZipFile.Open(zip, ZipArchiveMode.Create))
            {
                // Each writer must close before the next entry — ZipArchive allows one open entry at a time.
                static void Add(ZipArchive z, string name, string body)
                {
                    using var w = new StreamWriter(z.CreateEntry(name).Open());
                    w.Write(body);
                }
                Add(z, "readme.txt", "top");
                Add(z, "docs/guide.md", "guide");
            }
            var vfs = Vfs();
            Assert.IsTrue(FileSystemObjectHandler.OpensAsFolder(vfs, zip), "a .zip browses in place");
            Assert.IsTrue(FileSystemObjectHandler.OpensAsFolder(vfs, Path.Combine(zip, "docs")),
                "a folder inside a zip browses in place");
            Assert.IsFalse(FileSystemObjectHandler.OpensAsFolder(vfs, Path.Combine(zip, "readme.txt")),
                "a plain file inside a zip opens its viewer");
        }
        finally { try { Directory.Delete(dir, recursive: true); } catch { } }
    }
}
