using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Nexaflow.Features.Common;
using Nexaflow.Features.VirtualDisk.Handlers;
using Nexaflow.Features.VirtualDisk.ViewModels;
using Nexaflow.IO.Common;
using Nexaflow.Tests.Fixtures;
using NSubstitute;

namespace Nexaflow.Tests.Features.VirtualDisk;

/// <summary>
/// The inspector tab's own surfaces: the metadata pane, the contents tree's lazy expansion, and the two
/// actions in the bar beside it.
/// <para>
/// The tab is read-only by design and that is the rule most easily broken by a well-meaning change:
/// activating a row expands a folder and does <i>nothing at all</i> for a file. Launching content out of a
/// disk image is the file explorer's job, reached by browsing into the image — a viewer that also launched
/// would give the same file two different open paths with different security scopes.
/// </para>
/// </summary>
[TestClass]
public class VirtualDiskSurfaceTests
{
    private static VirtualFileSystem DiskVfs()
    {
        var vfs = new VirtualFileSystem();
        vfs.RegisterHandler(new DiskImageArchiveHandler());
        return vfs;
    }

    private static async Task<VirtualDiskViewModel> LoadedAsync(string path, IShellServices? shell = null)
    {
        var vm = new VirtualDiskViewModel(path, shell ?? Substitute.For<IShellServices>(), DiskVfs());
        await vm.WhenLoaded;
        return vm;
    }

    // ── Metadata pane ─────────────────────────────────────────────────────────

    [TestMethod]
    [CoversNode("vdisk-format-summary")]
    public async Task TheSummaryNamesTheFormatAndItsCapacity()
    {
        using var fix = new DiskSampleFactory.Fixtures();

        var vm = await LoadedAsync(fix.VhdPath);

        Assert.AreEqual("VHD", vm.Format);
        Assert.IsFalse(string.IsNullOrWhiteSpace(vm.CapacityText));
        Assert.IsTrue(vm.IsRecognised);
    }

    [TestMethod]
    [CoversNode("vdisk-format-summary")]
    public async Task AnImageTheReaderCannotOpenSaysSo_RatherThanShowingBlanks()
    {
        var path = Path.Combine(Path.GetTempPath(), $"notadisk_{Guid.NewGuid():N}.vhd");
        File.WriteAllBytes(path, [0x00, 0x01, 0x02, 0x03]);
        try
        {
            var vm = await LoadedAsync(path);

            Assert.IsFalse(vm.IsRecognised);
            Assert.IsTrue(vm.IsLoaded, "and the load is over, so this is a verdict rather than a mid-read state");
            Assert.IsFalse(vm.ExtractCommand.CanExecute(null),
                           "there is nothing to extract from an image that could not be read");
        }
        finally { try { File.Delete(path); } catch { } }
    }

    [TestMethod]
    [CoversNode("vdisk-volume-table")]
    public async Task EachVolumeGetsARowWithItsFilesystemAndSize()
    {
        using var fix = new DiskSampleFactory.Fixtures();

        var vm = await LoadedAsync(fix.VhdPath);

        Assert.AreEqual(1, vm.Volumes.Count);
        var volume = vm.Volumes[0];
        StringAssert.Contains(volume.Title, "Volume", "rows are numbered, since a partitioned image has several");
        Assert.IsFalse(string.IsNullOrWhiteSpace(volume.Detail),
                       "the filesystem (and label, where there is one) - which volume holds the data is the " +
                       "first thing anyone wants to know");
        StringAssert.Contains(volume.Usage, "used of", "how full it is, not just how big");
    }

    // ── Contents tree ─────────────────────────────────────────────────────────

    [TestMethod]
    [CoversNode("vdisk-contents-tree")]
    public async Task TheRootListsFoldersBeforeFiles()
    {
        using var fix = new DiskSampleFactory.Fixtures();

        var vm = await LoadedAsync(fix.VhdPath);

        var kinds = vm.VisibleRows.Select(r => r.IsFolder).ToList();
        CollectionAssert.AreEqual(kinds.OrderByDescending(k => k).ToList(), kinds,
                                  "folders group at the top, as they do everywhere else in the app");
    }

    [TestMethod]
    [CoversNode("vdisk-contents-tree")]
    public async Task AFolderLoadsItsChildrenOnlyWhenItIsFirstOpened()
    {
        using var fix = new DiskSampleFactory.Fixtures();
        var vm = await LoadedAsync(fix.VhdPath);
        var folder = vm.VisibleRows.First(r => r.IsFolder);
        Assert.AreEqual(0, folder.Children.Count, "a whole filesystem is not walked up front");

        await vm.ActivateRowCommand.ExecuteAsync(folder);

        Assert.IsTrue(folder.IsExpanded);
        Assert.IsTrue(folder.Children.Count > 0);
        Assert.IsTrue(vm.VisibleRows.Count > 1, "and the children join the flat row list");
    }

    [TestMethod]
    [CoversNode("vdisk-contents-tree")]
    public async Task CollapsingAFolderTakesItsChildrenOffTheList_ButKeepsThemLoaded()
    {
        using var fix = new DiskSampleFactory.Fixtures();
        var vm = await LoadedAsync(fix.VhdPath);
        var folder = vm.VisibleRows.First(r => r.IsFolder);
        await vm.ActivateRowCommand.ExecuteAsync(folder);
        var expandedRows = vm.VisibleRows.Count;

        await vm.ActivateRowCommand.ExecuteAsync(folder);

        Assert.IsFalse(folder.IsExpanded);
        Assert.IsTrue(vm.VisibleRows.Count < expandedRows);
        Assert.IsTrue(folder.Children.Count > 0, "re-opening it must not re-read the directory");
    }

    // ── Actions ───────────────────────────────────────────────────────────────

    [TestMethod]
    [CoversNode("vdisk-extract")]
    public async Task CancellingTheFolderPickerExtractsNothing()
    {
        using var fix = new DiskSampleFactory.Fixtures();
        var shell = Substitute.For<IShellServices>();
        shell.PickFolderAsync(Arg.Any<string>()).Returns(Task.FromResult<string?>(null));
        var vm = await LoadedAsync(fix.VhdPath, shell);

        await vm.ExtractCommand.ExecuteAsync(null);

        Assert.IsFalse(vm.StatusText.Contains("Extracted"), "backing out of the picker is not a silent extract");
    }

    [TestMethod]
    [CoversNode("vdisk-extract")]
    public async Task ExtractingWritesTheImageOutAndSaysWhereItWent()
    {
        using var fix = new DiskSampleFactory.Fixtures();
        var dest = Path.Combine(Path.GetTempPath(), $"vdiskout_{Guid.NewGuid():N}");
        Directory.CreateDirectory(dest);
        var shell = Substitute.For<IShellServices>();
        shell.PickFolderAsync(Arg.Any<string>()).Returns(Task.FromResult<string?>(dest));
        try
        {
            var vm = await LoadedAsync(fix.VhdPath, shell);

            await vm.ExtractCommand.ExecuteAsync(null);

            Assert.IsTrue(Directory.EnumerateFileSystemEntries(dest).Any(), "the contents reached the folder");
            StringAssert.Contains(vm.StatusText, dest, "and the tab says where, since nothing else will");
        }
        finally { try { Directory.Delete(dest, recursive: true); } catch { } }
    }

    [TestMethod]
    [CoversNode("vdisk-status")]
    public async Task TheStatusLineIsQuietUntilSomethingHappens()
    {
        using var fix = new DiskSampleFactory.Fixtures();

        var vm = await LoadedAsync(fix.VhdPath);

        Assert.IsFalse(vm.IsBusy, "the load is done, so the tab is not still claiming to be working");
    }
}
