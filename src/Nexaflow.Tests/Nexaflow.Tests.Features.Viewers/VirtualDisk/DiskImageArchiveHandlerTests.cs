using System;
using System.IO;
using System.Linq;
using Nexaflow.Features.VirtualDisk.Handlers;
using Nexaflow.IO.Common;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.Features.VirtualDisk;

/// <summary>The disk-image <see cref="IArchiveHandler"/> and its lazy session, and that a disk registered in
/// the VFS browses as a folder and reads inner files through the materialisation path.</summary>
[TestClass]
[CoversNode("vdisk-vfs")]
public class DiskImageArchiveHandlerTests
{
    [TestMethod]
    public void CanHandle_DiskExtensions_ReadOnlyCaps()
    {
        var h = new DiskImageArchiveHandler();
        Assert.IsTrue(h.CanHandle("disk.vhd"));
        Assert.IsTrue(h.CanHandle("disk.iso"));
        Assert.IsFalse(h.CanHandle("archive.zip"));
        Assert.AreEqual(ArchiveCapabilities.List | ArchiveCapabilities.Extract, h.Capabilities);
    }

    [TestMethod]
    public void Session_IsLazy_AndListsRoot()
    {
        using var fix = new DiskSampleFactory.Fixtures();
        var h = new DiskImageArchiveHandler();
        using var session = h.Open(
            new FileStream(fix.IsoPath, FileMode.Open, FileAccess.Read, FileShare.Read), "sample.iso");

        Assert.IsTrue(session.SupportsLazyBrowse);
        var names = session.ListChildren("").Select(e => e.Name).ToList();
        Assert.IsTrue(names.Any(n => n.Equals("readme.txt", StringComparison.OrdinalIgnoreCase)));
    }

    [TestMethod]
    public void Vfs_TreatsDiskAsBrowsableContainer()
    {
        using var fix = new DiskSampleFactory.Fixtures();
        var vfs = new VirtualFileSystem();
        vfs.RegisterHandler(new DiskImageArchiveHandler());

        Assert.IsTrue(vfs.IsContainer(fix.VhdPath));
        Assert.IsTrue(vfs.IsDirectory(fix.VhdPath));

        var root = vfs.EnumerateEntries(fix.VhdPath).Select(e => e.Name).ToList();
        Assert.IsTrue(root.Any(n => n.Equals("readme.txt", StringComparison.OrdinalIgnoreCase)));
        Assert.IsTrue(root.Any(n => n.Equals("docs", StringComparison.OrdinalIgnoreCase)));
    }

    [TestMethod]
    public void Vfs_ReadsInnerFileThroughMaterialisation()
    {
        using var fix = new DiskSampleFactory.Fixtures();
        var vfs = new VirtualFileSystem();
        vfs.RegisterHandler(new DiskImageArchiveHandler());

        var readme = vfs.EnumerateEntries(fix.VhdPath)
            .First(e => !e.IsDirectory && e.Name.Contains("readme", StringComparison.OrdinalIgnoreCase));
        var innerPath = Path.Combine(fix.VhdPath, readme.Name);

        Assert.IsFalse(vfs.IsDirectory(innerPath));
        StringAssert.Contains(vfs.ReadAllText(innerPath), "hello from the vhd");
    }
}
