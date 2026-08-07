using System.Collections.Generic;
using System.IO;
using System.Linq;
using Nexaflow.Core.Controls;
using Nexaflow.Features.Common.ThisPc;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.Core.Unit;

/// <summary>
/// The themed file/folder pickers list provider-supplied locations beside the drives.
/// <para>
/// They list them by their REAL path under the friendly label, not by the virtual root the browser uses:
/// a picker's result goes straight to ordinary <c>System.IO</c> callers, so it has to hand back something
/// they can open. The user still only reads the label.
/// </para>
/// </summary>
[TestClass]
[CoversNode("win-cux-folder-picker")]
public class PickerProvidedRootsTests
{
    private static ThisPcItem Item(string id, string path,
                                   ThisPcItemBacking backing = ThisPcItemBacking.LocalPath,
                                   ThisPcItemIcon icon = ThisPcItemIcon.Cloud) =>
        new() { Id = id, Label = "Cloud " + id, TargetPath = path, TypeLabel = "Cloud", Backing = backing, Icon = icon };

    private static string AnyDriveRoot() => DriveInfo.GetDrives()[0].RootDirectory.FullName;

    [TestMethod]
    public void TheFolderPickerShowsAProvidedLocationBesideTheDrives()
    {
        var vm = new FolderBrowserViewModel(provided: [Item("a", @"C:\Cloud\A")]);

        Assert.AreEqual(DriveInfo.GetDrives().Count(d => d.IsReady) + 1, vm.Roots.Count);
        Assert.AreEqual("Cloud a", vm.Roots[^1].DisplayName);
    }

    [TestMethod]
    public void TheFolderPickerListsItByItsRealPathSoTheResultCanBeOpened()
    {
        var vm = new FolderBrowserViewModel(provided: [Item("a", @"C:\Cloud\A")]);

        Assert.AreEqual(@"C:\Cloud\A", vm.Roots[^1].FullPath);
        Assert.IsFalse(vm.Roots[^1].FullPath.StartsWith("::"), "a picker result must be a usable path");
    }

    [TestMethod]
    public void TheFolderPickerGivesAProvidedLocationItsOwnIcon()
    {
        var vm = new FolderBrowserViewModel(provided:
            [Item("a", @"C:\Cloud\A"), Item("n", @"C:\Net\N", icon: ThisPcItemIcon.Network)]);

        Assert.AreNotEqual(vm.Roots[0].Glyph, vm.Roots[^2].Glyph, "a cloud location doesn't read as a plain folder");
        Assert.AreNotEqual(vm.Roots[^2].Glyph, vm.Roots[^1].Glyph, "nor does a network one read as a cloud");
    }

    [TestMethod]
    public void TheFilePickerShowsProvidedLocationsToo()
    {
        var vm = new FileBrowserViewModel(null, provided: [Item("a", @"C:\Cloud\A")]);

        Assert.AreEqual(DriveInfo.GetDrives().Count(d => d.IsReady) + 1, vm.Roots.Count);
        Assert.AreEqual(@"C:\Cloud\A", vm.Roots[^1].FullPath);
        Assert.IsTrue(vm.Roots[^1].IsDirectory);
    }

    [TestMethod]
    public void WithNothingProvidedAPickerIsExactlyTheDriveListItAlwaysWas()
    {
        var folders = new FolderBrowserViewModel(provided: []);
        var files   = new FileBrowserViewModel(null, provided: []);

        var ready = DriveInfo.GetDrives().Count(d => d.IsReady);
        Assert.AreEqual(ready, folders.Roots.Count);
        Assert.AreEqual(ready, files.Roots.Count);
    }

    [TestMethod]
    public void ALocationThatIsAlreadyADriveIsNotOfferedTwice()
    {
        // Collect() does the dedupe; this pins that the picker actually routes through it.
        var drive = AnyDriveRoot();
        var vm = new FolderBrowserViewModel(
            provided: ThisPcItemSet.Collect(
                [new SingleItemProvider(Item("dup", drive))],
                DriveInfo.GetDrives().Where(d => d.IsReady).Select(d => d.RootDirectory.FullName),
                allowVirtual: false));

        Assert.AreEqual(DriveInfo.GetDrives().Count(d => d.IsReady), vm.Roots.Count);
    }

    [TestMethod]
    public void AVirtualLocationIsSkippedBecauseAPickerCannotHandBackAPathForIt()
    {
        var vm = new FolderBrowserViewModel(
            provided: ThisPcItemSet.Collect(
                [new SingleItemProvider(Item("cloudonly", "cloud://acct", ThisPcItemBacking.Virtual))],
                [], allowVirtual: false));

        Assert.AreEqual(DriveInfo.GetDrives().Count(d => d.IsReady), vm.Roots.Count);
    }

    private sealed class SingleItemProvider(ThisPcItem item) : IThisPcItemProvider
    {
        public string ProviderId => "test";
        public event System.Action? Changed { add { } remove { } }
        public IReadOnlyList<ThisPcItem> GetItems() => [item];
    }
}
