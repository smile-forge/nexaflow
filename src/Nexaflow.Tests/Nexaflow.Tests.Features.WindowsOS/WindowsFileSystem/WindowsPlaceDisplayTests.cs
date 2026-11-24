using Nexaflow.Features.Common;
using Nexaflow.Features.WindowsFileSystem;
using Nexaflow.IO.Common;
using Nexaflow.IO.Common.Locations;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.Features.WindowsFileSystem;

/// <summary>
/// A crumb standing for a place on this computer reads as a person names it: a whole path as its parts, so the shell can
/// shorten it from the middle, or the place's own name — and a location's place is left to its own feature.
/// </summary>
[TestClass]
[CoversNode("winfs-place-crumbs")]
public sealed class WindowsPlaceDisplayTests
{
    private static BreadcrumbDisplay Shown(IVirtualFileSystem files, string path, PlaceNaming naming)
    {
        var crumb   = new BreadcrumbSegment { Label = path, Path = path, Content = new PlaceContent(path, naming) };
        var display = BreadcrumbDisplay.Of(crumb);
        new WindowsPlaceDisplay(files).Apply(crumb, display);
        return display;
    }

    [TestMethod]
    public void AWholePath_IsItsParts_JoinedAsWindowsJoinsThem()
    {
        var display = Shown(VirtualFileSystem.Instance, @"D:\work\src\temp", PlaceNaming.WholePath);

        CollectionAssert.AreEqual(new[] { "D:", "work", "src", "temp" }, (System.Collections.ICollection)display.Parts);
        Assert.AreEqual(@"D:\work\src\temp", display.Text);
        Assert.AreEqual(@"D:\work\src\temp", display.CopyText);
        Assert.AreEqual(@"D:\work\src\temp", display.ToolTip);
    }

    [TestMethod]
    public void ARoot_IsOnePart_Whole()
    {
        var display = Shown(VirtualFileSystem.Instance, @"D:\", PlaceNaming.WholePath);

        CollectionAssert.AreEqual(new[] { @"D:\" }, (System.Collections.ICollection)display.Parts);
    }

    [TestMethod]
    public void AnOwnName_IsTheName_AndCopiesTheWholePath()
    {
        var display = Shown(VirtualFileSystem.Instance, @"D:\work\notes.txt", PlaceNaming.OwnName);

        Assert.AreEqual("notes.txt", display.Text);
        Assert.AreEqual(@"D:\work\notes.txt", display.CopyText);
    }

    [TestMethod]
    public void APageLabel_IsKept()
    {
        var display = Shown(VirtualFileSystem.Instance, @"D:\work\notes.txt", PlaceNaming.PageLabel);

        Assert.AreEqual(@"D:\work\notes.txt", display.Text, "the page chose the label; only what is copied and shown on hover is the filter's");
    }

    [TestMethod]
    public void ALocationsPlace_IsLeftToItsOwnFeature()
    {
        var files = new LocationFileSystem(VirtualFileSystem.Instance);
        files.RegisterLocation(new LocationMount(new ConnectionProfile("wsl-crumbprobe", "wsl", "Crumbprobe", "Crumbprobe"),
                                                 LocationCapabilities.SameMachine, null!));

        var display = Shown(files, @"::wsl-crumbprobe\etc", PlaceNaming.WholePath);

        Assert.AreEqual(@"::wsl-crumbprobe\etc", display.Text, "a distro's folder is the WSL feature's to restate");
    }
}
