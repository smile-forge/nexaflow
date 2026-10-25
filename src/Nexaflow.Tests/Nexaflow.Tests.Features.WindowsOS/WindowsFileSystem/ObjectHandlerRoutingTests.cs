using System;
using System.IO;
using System.IO.Compression;
using Nexaflow.Features.Compressed.Handlers;
using Nexaflow.Features.Email.Vfs;
using Nexaflow.Features.WindowsFileSystem;
using Nexaflow.Features.WindowsFileSystem.Services;
using Nexaflow.IO.Common;
using Nexaflow.Tests.Fixtures;
using NSubstitute;
using Nexaflow.Features.Common;
using System.Collections.Generic;

namespace Nexaflow.Tests.Features.WindowsFileSystem;

/// <summary>
/// <see cref="FileSystemObjectHandler.OpensAsFolderAsync"/> decides, for a path handed to
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
        var dir = Path.Combine(TestTemp.Root, "nexa-routing-filemap-" + Guid.NewGuid().ToString("N"));
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
    public async Task NestedEmailAttachment_OpensViewer_NotFolder()
    {
        // forwarded.eml embeds a message/rfc822 → attachment "Meeting notes.eml" (a nested container).
        var path = Path.Combine(TestSampleData.Path("email", "forwarded.eml"), "Meeting notes.eml");
        Assert.IsFalse(await FileSystemObjectHandler.OpensAsFolderAsync(Vfs(), path),
            "a forwarded .eml must open in the Email viewer, not a folder tab");
    }

    [TestMethod]
    public async Task EmailFile_OpensViewer_NotFolder()
        => Assert.IsFalse(await FileSystemObjectHandler.OpensAsFolderAsync(Vfs(), TestSampleData.Path("email", "simple.eml")));

    [TestMethod]
    public async Task PlainAttachment_OpensViewer_NotFolder()
    {
        var path = Path.Combine(TestSampleData.Path("email", "simple.eml"), "notes.txt");
        Assert.IsFalse(await FileSystemObjectHandler.OpensAsFolderAsync(Vfs(), path));
    }

    [TestMethod]
    public async Task RealArchive_AndItsFolders_BrowseInPlace()
    {
        var dir = Path.Combine(TestTemp.Root, "nexa-routing-zip-" + Guid.NewGuid().ToString("N"));
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
            Assert.IsTrue(await FileSystemObjectHandler.OpensAsFolderAsync(vfs, zip), "a .zip browses in place");
            Assert.IsTrue(await FileSystemObjectHandler.OpensAsFolderAsync(vfs, Path.Combine(zip, "docs")),
                "a folder inside a zip browses in place");
            Assert.IsFalse(await FileSystemObjectHandler.OpensAsFolderAsync(vfs, Path.Combine(zip, "readme.txt")),
                "a plain file inside a zip opens its viewer");
        }
        finally { try { Directory.Delete(dir, recursive: true); } catch { } }
    }

    [TestMethod]
    public void APathIsClaimedByItsShape_WithoutAskingWhetherAnythingIsThere()
    {
        var vfs = Substitute.For<IVirtualFileSystem>();
        vfs.TryParseDisplayPath("sftp://adam@nas/home").Returns(@"::sftp-nas\home");
        var handler = new FileSystemObjectHandler(Substitute.For<IShellServices>(), Substitute.For<IAIService>(),
                                                  new Dictionary<Type, IFeatureConfig>(), vfs);

        Assert.IsTrue(handler.CanHandleObject(@"C:\nowhere\at-all.txt"), "existence is the open's question, not the claim's");
        Assert.IsTrue(handler.CanHandleObject(@"::docs\notes.md"));
        Assert.IsTrue(handler.CanHandleObject("file:///C:/nowhere/at-all.txt"));
        Assert.IsTrue(handler.CanHandleObject("sftp://adam@nas/home"), "a location's address names a path too");
        Assert.IsFalse(handler.CanHandleObject("https://example.com/a.txt"));
        Assert.IsFalse(handler.CanHandleObject("notes.txt"));
        vfs.DidNotReceiveWithAnyArgs().ExistsAsync(default!, default);
    }
}
