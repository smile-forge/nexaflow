using System.IO;
using Nexaflow.Tests.Fixtures;
using Nexaflow.Tests.UIJourneys.Infrastructure;

namespace Nexaflow.Tests.Features.VirtualDisk.UI;

/// <summary>
/// One-pass UI journey for the "As Disk" inspector: opens the corpus VHD through the explicit <b>As Disk</b>
/// action (a double-click browses <i>into</i> the image instead — that path is <see cref="VirtualDiskOpenUiTests"/>)
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
    [TestMethod]
    public void VirtualDisk_ActionBar_RespondsInOnePass()
    {
        var dir = Path.GetDirectoryName(
            RequiredFixture.File(Path.Combine("disk", "sample.vhd"),
                                 "DiskUiFixtureTests in Nexaflow.Tests.Features.Viewers"))!;

        var view = OpenFileVia(dir, "sample.vhd", "As Disk", "VirtualDiskView");
        Assert.IsNotNull(view, "VirtualDiskView did not open via the 'As Disk' action.");

        // One id on N controls: the chevron is in the row template and only renders for a row with children —
        // the fixture's docs/ folder — so this presses whichever folder row comes first. Expanding is a lazy
        // read of the image, never a write.
        CheckInvoke("Row expand chevron", "VirtualDisk_RowExpand");

        CheckPresent("Extract", "VirtualDisk_Extract");
        CheckPresent("Mount",   "VirtualDisk_Mount");

        AssertJourney();
    }
}
