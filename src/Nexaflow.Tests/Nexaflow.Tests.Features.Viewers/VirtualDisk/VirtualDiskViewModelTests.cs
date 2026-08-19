using System;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using Nexaflow.Features.Common;
using Nexaflow.Features.Common.ClientTools;
using Nexaflow.Features.VirtualDisk.Handlers;
using Nexaflow.Features.VirtualDisk.ViewModels;
using Nexaflow.IO.Common;
using Nexaflow.Tests.Fixtures;
using NSubstitute;

namespace Nexaflow.Tests.Features.VirtualDisk;

/// <summary>The "As Disk" inspector view-model: metadata + volume rows, and lazy-expand of the contents tree
/// (a folder's children are read only when it's opened).</summary>
[TestClass]
[CoversNode("vdisk-ui")]
public class VirtualDiskViewModelTests
{
    private static VirtualFileSystem DiskVfs()
    {
        var vfs = new VirtualFileSystem();
        vfs.RegisterHandler(new DiskImageArchiveHandler());
        return vfs;
    }

    [TestMethod]
    public async Task Load_PopulatesMetadataVolumesAndRoot()
    {
        using var fix = new DiskSampleFactory.Fixtures();
        var vm = new VirtualDiskViewModel(fix.VhdPath, Substitute.For<IShellServices>(), DiskVfs());
        await vm.WhenLoaded;

        Assert.IsTrue(vm.IsRecognised);
        Assert.AreEqual("VHD", vm.Format);
        Assert.AreEqual(1, vm.Volumes.Count);
        Assert.IsTrue(vm.CanMount);   // .vhd is natively mountable
        Assert.IsTrue(vm.VisibleRows.Any(r => r.Name.Equals("readme.txt", StringComparison.OrdinalIgnoreCase)));
    }

    [TestMethod]
    public async Task ActivateRow_ExpandsFolderLazily()
    {
        using var fix = new DiskSampleFactory.Fixtures();
        var vm = new VirtualDiskViewModel(fix.VhdPath, Substitute.For<IShellServices>(), DiskVfs());
        await vm.WhenLoaded;

        var docs = vm.VisibleRows.First(r => r.Name.Equals("docs", StringComparison.OrdinalIgnoreCase));
        Assert.IsFalse(vm.VisibleRows.Any(r => r.Name.Equals("guide.txt", StringComparison.OrdinalIgnoreCase)));

        await vm.ActivateRowCommand.ExecuteAsync(docs);
        Assert.IsTrue(vm.VisibleRows.Any(r => r.Name.Equals("guide.txt", StringComparison.OrdinalIgnoreCase)));
    }

    [TestMethod]
    public async Task ActivateRow_OnFile_NeverOpensAnything()
    {
        using var fix = new DiskSampleFactory.Fixtures();
        var shell = Substitute.For<IShellServices>();
        var vm = new VirtualDiskViewModel(fix.VhdPath, shell, DiskVfs());
        await vm.WhenLoaded;

        var file = vm.VisibleRows.First(r => r.Name.Equals("readme.txt", StringComparison.OrdinalIgnoreCase));
        await vm.ActivateRowCommand.ExecuteAsync(file);

        // The As-Disk tab is display-only: activating a file must not open a tab, hand it off, or launch it.
        shell.DidNotReceive().HandleObject(Arg.Any<object>());
        shell.DidNotReceive().OpenTab(Arg.Any<string>(), Arg.Any<System.Collections.Generic.Dictionary<string, string>>(),
                                      Arg.Any<IPageView?>(), Arg.Any<bool>());
    }

    [TestMethod]
    public async Task Iso_LoadsAndIsMountable()
    {
        using var fix = new DiskSampleFactory.Fixtures();
        var vm = new VirtualDiskViewModel(fix.IsoPath, Substitute.For<IShellServices>(), DiskVfs());
        await vm.WhenLoaded;

        Assert.IsTrue(vm.IsRecognised);
        Assert.AreEqual("ISO 9660", vm.Format);
        Assert.IsTrue(vm.CanMount);
        StringAssert.Contains(vm.GetContext(), "ISO 9660");
    }

    // ── AI context readiness (regression: a valid disk mid-load must not read as "unreadable") ──

    [TestMethod]
    public void Context_WhileNotLoaded_ReadsAsReading_NotUnreadable()
    {
        // A non-existent path makes LoadAsync complete synchronously (no background continuation to race),
        // so forcing the not-yet-loaded state is deterministic. IsRecognised is false while loading AND on a
        // genuine failure — the fix keys GetContext off IsLoaded so the two are no longer conflated.
        var vm = new VirtualDiskViewModel(@"Z:\does-not-exist.vhd", Substitute.For<IShellServices>(), DiskVfs());
        Assert.IsTrue(vm.IsLoaded, "precondition: the synchronous no-file load has finished");

        vm.IsLoaded = false;   // simulate the mid-load window

        Assert.IsFalse(vm.IsContextReady, "the send must be held while loading");
        StringAssert.Contains(vm.GetContext(), "reading");
        Assert.IsFalse(vm.GetContext().Contains("unrecognised"),
            "a still-loading disk must not be reported as unrecognised/unreadable");
    }

    [TestMethod]
    public async Task Context_AfterLoad_IsReady_AndReflectsTheDisk()
    {
        using var fix = new DiskSampleFactory.Fixtures();
        var vm = new VirtualDiskViewModel(fix.VhdPath, Substitute.For<IShellServices>(), DiskVfs());
        await vm.WhenLoaded;

        Assert.IsTrue(vm.IsContextReady);
        Assert.IsTrue(vm.IsRecognised);
        StringAssert.Contains(vm.GetContext(), "VHD");
        Assert.IsFalse(vm.GetContext().Contains("reading"));
    }

    // ── AI Act: read-only client tools (list volumes / list files / read a file, without extracting) ──

    [TestMethod]
    [CoversNode("virtual-disk-viewer-ai-act")]
    [CoversNode("virtual-disk-viewer-ai-context")]
    public async Task ClientTools_ReadVolumesFilesAndContent()
    {
        using var fix = new DiskSampleFactory.Fixtures();
        var vm = new VirtualDiskViewModel(fix.VhdPath, Substitute.For<IShellServices>(), DiskVfs());
        await vm.WhenLoaded;

        // Scope boundary = the disk-image file path; context stays honest.
        Assert.AreEqual(fix.VhdPath, vm.GetSecurityContext());
        StringAssert.Contains(vm.GetContext(), "VHD");

        var tools = vm.GetClientTools();
        var names = tools.Select(t => t.Name).ToArray();
        CollectionAssert.Contains(names, "list_volumes");
        CollectionAssert.Contains(names, "list_files");
        CollectionAssert.Contains(names, "read_file");
        Assert.IsTrue(tools.All(t => t.Safety == ToolSafety.SafeOperation), "all disk read tools auto-run");

        IClientTool Tool(string n) => tools.First(t => t.Name == n);

        // list_volumes → the volume table (metadata pane always has "Volume 1").
        var vols = await Tool("list_volumes").InvokeAsync(new JsonObject(), default);
        Assert.IsTrue(vols.Success);
        StringAssert.Contains(vols.ModelText, "Volume 1");

        // list_files at the root → sees the root file and folder, one directory at a time.
        var root = await Tool("list_files").InvokeAsync(new JsonObject(), default);
        Assert.IsTrue(root.Success);
        StringAssert.Contains(root.ModelText, "readme.txt");
        StringAssert.Contains(root.ModelText, "docs");

        // list_files into a subfolder → lazily reads its children.
        var docs = await Tool("list_files").InvokeAsync(new JsonObject { ["path"] = "docs" }, default);
        Assert.IsTrue(docs.Success);
        StringAssert.Contains(docs.ModelText, "guide.txt");

        // read_file → the file's text, read through the image (never extracted to disk).
        var read = await Tool("read_file").InvokeAsync(new JsonObject { ["path"] = "readme.txt" }, default);
        Assert.IsTrue(read.Success);
        StringAssert.Contains(read.ModelText, "hello from the vhd");

        // read_file into docs/guide.txt (nested path) works too.
        var nested = await Tool("read_file").InvokeAsync(new JsonObject { ["path"] = "docs/guide.txt" }, default);
        Assert.IsTrue(nested.Success);
        StringAssert.Contains(nested.ModelText, "nested file body");

        // Guards: reading a folder, and a missing entry, are both refused (not thrown).
        var asFolder = await Tool("read_file").InvokeAsync(new JsonObject { ["path"] = "docs" }, default);
        Assert.IsTrue(asFolder.IsError);
        var missing = await Tool("read_file").InvokeAsync(new JsonObject { ["path"] = "nope.txt" }, default);
        Assert.IsTrue(missing.IsError);
    }
}
