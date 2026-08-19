using System.IO;
using NSubstitute;
using Nexaflow.Features.Common;
using Nexaflow.Features.Common.Viewlets;
using Nexaflow.Features.Images.FileActions;
using Nexaflow.Features.WindowsFileSystem.Services;
using Nexaflow.Features.WindowsFileSystem.ViewModels;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.Features.WindowsFileSystem;

/// <summary>
/// Covers <see cref="FileActionManager.FilterFolderActions"/> with the image folder actions, which
/// exercise the <c>ContainsFileGlobs</c> + <c>MinimumFileGlobMatchPercentage</c> (30%) content check.
/// </summary>
[TestClass]
[CoversNode("winfs-action-strip")]
public class FolderActionMatchingTests
{
    // ── Setup ─────────────────────────────────────────────────────────────

    private static FileActionManager ManagerWithImageActions()
    {
        var shell = Substitute.For<IShellServices>();
        var ai    = Substitute.For<IAIService>();

        shell.DiscoverImplementations<IFileAction>().Returns(Array.Empty<Type>());
        shell.DiscoverImplementations<IFolderAction>()
             .Returns([typeof(SlideshowFolderAction), typeof(AlbumFolderAction)]);
        shell.DiscoverImplementations<IFileCreateAction>().Returns(Array.Empty<Type>());
        shell.DiscoverImplementations<IFolderViewlet>().Returns(Array.Empty<Type>());

        var registry = FileSystemFeatureRegistry.For(shell, ai, new Dictionary<Type, IFeatureConfig>());
        return new FileActionManager(registry);
    }

    private static string MakeFolder(int images, int others)
    {
        var dir = Path.Combine(Path.GetTempPath(), "nexaflow-imgtest-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        for (int i = 0; i < images; i++) File.WriteAllText(Path.Combine(dir, $"img{i}.png"), "x");
        for (int i = 0; i < others; i++) File.WriteAllText(Path.Combine(dir, $"doc{i}.txt"), "x");
        return dir;
    }

    private static IReadOnlyList<string> MatchedActions(string dir)
    {
        var manager = ManagerWithImageActions();
        var selected = new List<FileSystemEntry>
        {
            new() { Name = Path.GetFileName(dir), FullPath = dir, IsDirectory = true },
        };
        var canPerform = manager.SnapshotCanPerform().Folder;
        return manager.FilterFolderActions(selected, canPerform)
                      .Select(a => a.DisplayName)
                      .ToList();
    }

    // Empty selection = the currently-open folder; gated by currentFolderPath.
    private static IReadOnlyList<string> MatchedActionsForOpenFolder(string? dir)
    {
        var manager = ManagerWithImageActions();
        var canPerform = manager.SnapshotCanPerform().Folder;
        return manager.FilterFolderActions([], canPerform, dir)
                      .Select(a => a.DisplayName)
                      .ToList();
    }

    // ── Tests ─────────────────────────────────────────────────────────────

    [TestMethod]
    public void AtThreshold_30Percent_ShowsSlideshowAndAlbum()
    {
        var dir = MakeFolder(images: 3, others: 7);   // 3/10 = 30%
        try
        {
            var actions = MatchedActions(dir);
            CollectionAssert.AreEquivalent(new[] { "Slideshow", "Album" }, actions.ToArray());
        }
        finally { Directory.Delete(dir, true); }
    }

    [TestMethod]
    public void BelowThreshold_20Percent_ShowsNeither()
    {
        var dir = MakeFolder(images: 2, others: 8);   // 2/10 = 20%
        try
        {
            Assert.AreEqual(0, MatchedActions(dir).Count);
        }
        finally { Directory.Delete(dir, true); }
    }

    [TestMethod]
    public void AllImages_ShowsActions()
    {
        var dir = MakeFolder(images: 4, others: 0);
        try
        {
            Assert.AreEqual(2, MatchedActions(dir).Count);
        }
        finally { Directory.Delete(dir, true); }
    }

    [TestMethod]
    public void EmptyFolder_ShowsNeither()
    {
        var dir = MakeFolder(images: 0, others: 0);
        try
        {
            Assert.AreEqual(0, MatchedActions(dir).Count);
        }
        finally { Directory.Delete(dir, true); }
    }

    // ── Current open folder (no selection) ────────────────────────────────

    [TestMethod]
    public void OpenFolder_AtThreshold_ShowsActions()
    {
        var dir = MakeFolder(images: 3, others: 7);   // 30%
        try
        {
            CollectionAssert.AreEquivalent(new[] { "Slideshow", "Album" },
                                           MatchedActionsForOpenFolder(dir).ToArray());
        }
        finally { Directory.Delete(dir, true); }
    }

    [TestMethod]
    public void OpenFolder_BelowThreshold_ShowsNeither()
    {
        var dir = MakeFolder(images: 2, others: 8);   // 20%
        try
        {
            Assert.AreEqual(0, MatchedActionsForOpenFolder(dir).Count);
        }
        finally { Directory.Delete(dir, true); }
    }

    [TestMethod]
    public void OpenFolder_NoPath_ShowsNeither()
    {
        // This-PC / no current folder: a constrained action can't be verified, so it stays hidden.
        Assert.AreEqual(0, MatchedActionsForOpenFolder(null).Count);
    }

    [TestMethod]
    public void ContainsFileGlobs_MatchesInsideAVirtualPath_NotJustRealFolders()
    {
        // A folder action's content check must go through the VFS for a virtual folder (a mounted disk image
        // or archive) — else "As DICOM" (and every ContainsFileGlobs action) never appears inside a .zip/.iso.
        Nexaflow.IO.Common.VirtualFileSystem.Instance.RegisterHandler(new Nexaflow.Features.Compressed.Handlers.ZipArchiveHandler());

        var tmp = Path.Combine(Path.GetTempPath(), "nexaflow-vfolder-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tmp);
        try
        {
            var zip = Nexaflow.Tests.Features.Dicom.DicomTestFiles.WriteZip(tmp, "study.zip", instanceCount: 2);

            var shell = Substitute.For<IShellServices>();
            var ai = Substitute.For<IAIService>();
            shell.DiscoverImplementations<IFileAction>().Returns(Array.Empty<Type>());
            shell.DiscoverImplementations<IFolderAction>().Returns([typeof(Nexaflow.Features.Dicom.FileActions.DicomFolderAction)]);
            shell.DiscoverImplementations<IFileCreateAction>().Returns(Array.Empty<Type>());
            shell.DiscoverImplementations<IFolderViewlet>().Returns(Array.Empty<Type>());
            var manager = new FileActionManager(FileSystemFeatureRegistry.For(shell, ai, new Dictionary<Type, IFeatureConfig>()));

            // Selecting the .zip (browsed as a container) — its entries include .dcm files, so "As DICOM" applies.
            var selected = new List<FileSystemEntry> { new() { Name = "study.zip", FullPath = zip, IsDirectory = true } };
            var names = manager.FilterFolderActions(selected, manager.SnapshotCanPerform().Folder)
                               .Select(a => a.DisplayName).ToList();

            CollectionAssert.Contains(names, "As DICOM", "the DICOM folder action must match a virtual folder's contents");
        }
        finally { Directory.Delete(tmp, true); }
    }
}
