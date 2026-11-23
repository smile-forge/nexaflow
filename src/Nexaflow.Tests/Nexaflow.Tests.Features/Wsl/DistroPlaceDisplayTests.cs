using Nexaflow.Features.Common;
using Nexaflow.Features.Wsl;
using Nexaflow.IO.Common;
using Nexaflow.IO.Common.Locations;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.Features.Wsl;

/// <summary>
/// A crumb standing for a place in a WSL distro reads as Linux names it — the distro's address, then each folder by its
/// Linux name, joined by <c>/</c> — and copies as its <c>wsl://</c> address; anything else is left as it was.
/// </summary>
[TestClass]
[CoversNode("wslshell-place-crumbs")]
public sealed class DistroPlaceDisplayTests
{
    private const string Root = "::wsl-placeprobe";

    private static LocationFileSystem Files(string kind = "wsl")
    {
        var files = new LocationFileSystem(VirtualFileSystem.Instance);
        files.RegisterLocation(new LocationMount(new ConnectionProfile(Root[2..], kind, "Placeprobe", "Placeprobe"),
                                                 LocationCapabilities.SameMachine | LocationCapabilities.Shell, null!));
        return files;
    }

    private static BreadcrumbDisplay Shown(LocationFileSystem files, string path, PlaceNaming naming)
    {
        var crumb   = new BreadcrumbSegment { Label = path, Path = path, Content = new PlaceContent(path, naming) };
        var display = BreadcrumbDisplay.Of(crumb);
        new DistroPlaceDisplay(files, files).Apply(crumb, display);
        return display;
    }

    [TestMethod]
    public void AWholePath_IsTheDistrosAddress_ThenEachFolderByItsLinuxName()
    {
        var display = Shown(Files(), $@"{Root}\etc\at 12%3A30", PlaceNaming.WholePath);

        CollectionAssert.AreEqual(new[] { "wsl://Placeprobe", "etc", "at 12:30" }, (System.Collections.ICollection)display.Parts);
        Assert.AreEqual("wsl://Placeprobe/etc/at 12:30", display.Text);
        Assert.AreEqual("wsl://Placeprobe/etc/at 12:30", display.CopyText, "copied as the address Nexaflow reopens");
        Assert.AreEqual("wsl://Placeprobe/etc/at 12:30", display.ToolTip);
    }

    [TestMethod]
    public void TheDistrosRoot_IsItsAddress()
    {
        var display = Shown(Files(), Root, PlaceNaming.WholePath);

        Assert.AreEqual("wsl://Placeprobe/", display.Text);
    }

    [TestMethod]
    public void AnOwnName_IsTheLinuxName_NotTheEncodedSegment()
    {
        var display = Shown(Files(), $@"{Root}\etc\at 12%3A30", PlaceNaming.OwnName);

        Assert.AreEqual("at 12:30", display.Text);
        Assert.AreEqual("wsl://Placeprobe/etc/at 12:30", display.CopyText);
    }

    [TestMethod]
    public void APlaceThatIsNoDistros_IsLeftAsItWas()
    {
        Assert.AreEqual(@"C:\work", Shown(Files(), @"C:\work", PlaceNaming.WholePath).Text, "this computer's place");
        Assert.AreEqual($@"{Root}\etc", Shown(Files(kind: "sftp"), $@"{Root}\etc", PlaceNaming.WholePath).Text,
                        "a server's place is another feature's");
    }
}
