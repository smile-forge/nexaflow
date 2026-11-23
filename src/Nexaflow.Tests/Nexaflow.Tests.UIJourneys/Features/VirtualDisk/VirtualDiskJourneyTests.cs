using System.IO;
using FlaUI.Core.Input;
using Nexaflow.Tests.Fixtures;
using Nexaflow.Tests.UIJourneys.Infrastructure;

namespace Nexaflow.Tests.Features.VirtualDisk.UI;

/// <summary>
/// One-pass UI journey for the "OpenAsDiskAction" inspector: opens the corpus VHD through the explicit <b>As Disk</b>
/// action (a double-click browses <i>into</i> the image instead — the journey's other step)
/// and walks the contents tree's expand chevron and the action bar.
/// <para>
/// <b>Both action-bar buttons are present-checked and never pressed.</b> Extract raises a folder picker and
/// writes every entry out; Mount attaches the image to the OS and, for a VHD, asks for elevation. Mount is
/// only shown for a natively mountable format, which is why the fixture is a <c>.vhd</c> rather than any
/// DiscUtils-readable image.
/// </para>
/// Interactive desktop only — run with <c>--filter "TestCategory=UI"</c>.
/// </summary>
[TestClass]
[TestCategory("UI")]
[CoversNode("vdisk-action-bar")]
public class VirtualDiskJourneyTests : UiJourneyTestBase
{
    /// <summary>The image's inspector through the As Disk action, then a double-click that browses into the image instead.</summary>
    [TestMethod]
    [CoversNode("virtual-disk-viewer")]
    [CoversNode("vdisk-ui")]
    public void VirtualDisk_Journey()
    {
        RunStep("the inspector's contents tree and action bar", ActionBar);
        RunStep("a double-click browses into the image's contents", DoubleClickBrowsesIntoImage);
        AssertJourney();
    }

    /// <summary>
    /// The corpus VHD's folder. Writing a real disk image needs DiscUtils — the library the feature reads it with — so the
    /// image is built into the corpus and only opened here.
    /// </summary>
    private static string DiskFolder() => Path.GetDirectoryName(
        RequiredFixture.File(Path.Combine("disk", "sample.vhd"),
                             "DiskUiFixtureTests in Nexaflow.Tests.Features.Viewers"))!;

    /// <summary>
    /// A double-click browses <i>into</i> the image (the runtime VFS registers the DiscUtils-backed handler at startup, so a
    /// disk is a container): reflection registration → container detection → lazy directory read, in the shipping app.
    /// </summary>
    private void DoubleClickBrowsesIntoImage()
    {
        NavigateFileBrowserTo(DiskFolder());

        var row = WaitForName("sample.vhd", 8);
        Assert.IsNotNull(row, "sample.vhd not found in the file list.");
        row!.DoubleClick();
        Wait.UntilInputIsProcessed();

        // FAT may report 8.3 upper-case names, so accept either casing for the known entries.
        bool browsedIn = WaitForFs(() =>
            HasName("readme.txt") || HasName("README.TXT") || HasName("docs") || HasName("DOCS"), 12);

        Assert.IsTrue(browsedIn, "Double-clicking the VHD did not browse into its contents.");
        Assert.IsFalse(App.HasExited, "App crashed opening the VHD.");
    }

    private bool HasName(string name) => MainWindow.FindFirstDescendant(cf => cf.ByName(name)) is not null;

    private void ActionBar()
    {
        var view = OpenFileVia(DiskFolder(), "sample.vhd", "OpenAsDiskAction", "VirtualDiskView");
        Assert.IsNotNull(view, "VirtualDiskView did not open via the 'As Disk' action.");

        // One id on N controls: the chevron is in the row template and only renders for a row with children —
        // the fixture's docs/ folder — so this presses whichever folder row comes first. Expanding is a lazy
        // read of the image, never a write.
        CheckInvoke("Row expand chevron", "VirtualDisk_RowExpand");

        CheckPresent("Extract", "VirtualDisk_Extract");
        CheckPresent("Mount",   "VirtualDisk_Mount");
    }
}
