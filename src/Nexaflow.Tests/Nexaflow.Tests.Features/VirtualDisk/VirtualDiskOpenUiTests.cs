using System;
using System.IO;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Input;
using Nexaflow.Tests.Features.WindowsFileSystem.UI;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.Features.VirtualDisk;

/// <summary>
/// End-to-end UI smoke for a real disk image: generates a FAT VHD in a temp folder, navigates the file
/// browser there in the live app, and double-clicks the image — which should browse <i>into</i> it (the
/// runtime VFS registers the DiscUtils-backed handler at startup, so a disk is a container). Proves the whole
/// chain — reflection registration → container detection → lazy directory read — works in the shipping app.
/// <para>Interactive desktop only — run with <c>--filter "TestCategory=UI"</c>.</para>
/// </summary>
[TestClass]
[TestCategory("UI")]
[CoversNode("virtualdisk")]
public class VirtualDiskOpenUiTests : FileSystemUiTestBase
{
    private string? _dir;

    [TestMethod]
    public void DiskImage_DoubleClick_BrowsesIntoContents()
    {
        _dir = Path.Combine(Path.GetTempPath(), "nexa-vdisk-ui-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
        DiskSampleFactory.CreateFatVhd(_dir, "sample.vhd");

        NavigateFileBrowserTo(_dir);

        var row = WaitForName("sample.vhd", 8);
        Assert.IsNotNull(row, "sample.vhd not found in the file list.");
        row!.DoubleClick();
        Wait.UntilInputIsProcessed();

        // Browsing into the VHD should surface its contents. FAT may report 8.3 upper-case names, so accept
        // either casing for the known entries.
        bool browsedIn = WaitForFs(() =>
            HasName("readme.txt") || HasName("README.TXT") || HasName("docs") || HasName("DOCS"), 12);

        Assert.IsTrue(browsedIn, "Double-clicking the VHD did not browse into its contents.");
        Assert.IsFalse(App.HasExited, "App crashed opening the VHD.");
    }

    private bool HasName(string name) =>
        MainWindow.FindFirstDescendant(cf => cf.ByName(name)) is not null;

    [TestCleanup]
    public void CleanupDiskDir()
    {
        try { if (_dir is not null && Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true); }
        catch { /* best effort */ }
    }
}
