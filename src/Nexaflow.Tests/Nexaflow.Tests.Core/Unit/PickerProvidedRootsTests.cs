using System.Collections.Generic;
using System.Linq;
using Nexaflow.Core.Controls;
using Nexaflow.Features.Common.ThisPc;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.Core.Unit;

/// <summary>
/// The themed file/folder pickers render the same list of top-level places the file browser does — one
/// <see cref="ThisPcItemSet.Enumerate"/> feeds both, so the two can't disagree on what This PC contains
/// or what each row is called.
/// <para>
/// A picker renders every place by its REAL path, because its result goes straight to ordinary
/// <c>System.IO</c> callers; the user only reads the label.
/// </para>
/// </summary>
[TestClass]
[CoversNode("win-cux-folder-picker")]
public class PickerProvidedRootsTests
{
    private static ThisPcPlace Drive(string path, string label, ThisPcItemIcon icon = ThisPcItemIcon.Disk) =>
        new() { Kind = ThisPcPlaceKind.Drive, RealPath = path, Label = label, TypeLabel = "Local Disk", Icon = icon };

    private static ThisPcPlace Provided(string id, string path) =>
        new()
        {
            Kind = ThisPcPlaceKind.Provided, RealPath = path, Label = "Cloud " + id,
            TypeLabel = "Cloud", Icon = ThisPcItemIcon.Cloud,
            Item = new ThisPcItem { Id = id, Label = "Cloud " + id, TargetPath = path, TypeLabel = "Cloud" },
        };

    [TestMethod]
    public void TheFolderPickerShowsDrivesAndContributedLocationsInOneList()
    {
        var vm = new FolderBrowserViewModel(places:
            [Drive(@"C:\", "System (C:)"), Provided("a", @"C:\Cloud\A")]);

        CollectionAssert.AreEqual(new[] { "System (C:)", "Cloud a" },
                                  vm.Roots.Select(r => r.DisplayName).ToArray());
    }

    [TestMethod]
    public void EveryRootIsListedByItsRealPathSoTheResultCanBeOpened()
    {
        var vm = new FolderBrowserViewModel(places: [Provided("a", @"C:\Cloud\A")]);

        Assert.AreEqual(@"C:\Cloud\A", vm.Roots[0].FullPath);
        Assert.IsFalse(vm.Roots[0].FullPath.StartsWith("::"), "a picker result must be a usable path");
    }

    [TestMethod]
    public void EveryKindOfPlaceGetsItsOwnGlyphIncludingTheDrives()
    {
        // The gap this closes: a USB stick reading as a plain folder while a cloud location got its own
        // mark, because only half the vocabulary was mapped.
        var vm = new FolderBrowserViewModel(places:
        [
            Drive(@"C:\", "Disk",       ThisPcItemIcon.Disk),
            Drive(@"E:\", "Stick",      ThisPcItemIcon.Removable),
            Drive(@"D:\", "Optical",    ThisPcItemIcon.Optical),
            Drive(@"Z:\", "Share",      ThisPcItemIcon.Network),
            Provided("c", @"C:\Cloud"),
        ]);

        var glyphs = vm.Roots.Select(r => r.Glyph).ToArray();
        Assert.AreEqual(glyphs.Length, glyphs.Distinct().Count(),
                        "each kind of place must be visually distinguishable from the others");
    }

    [TestMethod]
    public void TheFilePickerRendersTheSameListAsTheFolderPicker()
    {
        List<ThisPcPlace> places = [Drive(@"C:\", "System (C:)"), Provided("a", @"C:\Cloud\A")];

        var folders = new FolderBrowserViewModel(places: places);
        var files   = new FileBrowserViewModel(null, places: places);

        CollectionAssert.AreEqual(folders.Roots.Select(r => r.DisplayName).ToArray(),
                                  files.Roots.Select(r => r.DisplayName).ToArray());
        CollectionAssert.AreEqual(folders.Roots.Select(r => r.FullPath).ToArray(),
                                  files.Roots.Select(r => r.FullPath).ToArray());
        CollectionAssert.AreEqual(folders.Roots.Select(r => r.Glyph).ToArray(),
                                  files.Roots.Select(r => r.Glyph).ToArray());
    }

    [TestMethod]
    public void EveryFilePickerRootIsBrowsable()
    {
        var files = new FileBrowserViewModel(null, places: [Drive(@"C:\", "System (C:)"), Provided("a", @"C:\Cloud")]);

        Assert.IsTrue(files.Roots.All(r => r.IsDirectory));
    }

    [TestMethod]
    public void WithNoPlacesAPickerIsSimplyEmptyRatherThanGuessing()
    {
        Assert.AreEqual(0, new FolderBrowserViewModel(places: []).Roots.Count);
        Assert.AreEqual(0, new FileBrowserViewModel(null, places: []).Roots.Count);
    }

    // ── What the shared enumeration hands a picker ───────────────────────────

    [TestMethod]
    public void TheEnumerationSkipsALocationThatIsAlreadyADrive()
    {
        var drive = System.IO.DriveInfo.GetDrives().First(d => d.IsReady).RootDirectory.FullName;

        var places = ThisPcItemSet.Enumerate(
            [new SingleItemProvider(new ThisPcItem
             { Id = "dup", Label = "Dup", TargetPath = drive, TypeLabel = "Cloud" })],
            readyDrivesOnly: true, allowVirtual: false);

        Assert.AreEqual(0, places.Count(p => p.Kind == ThisPcPlaceKind.Provided));
    }

    [TestMethod]
    public void TheEnumerationSkipsALocationAPickerCouldNotHandBackAPathFor()
    {
        var places = ThisPcItemSet.Enumerate(
            [new SingleItemProvider(new ThisPcItem
             { Id = "cloudonly", Label = "Cloud", TargetPath = "cloud://acct", TypeLabel = "Cloud",
               Backing = ThisPcItemBacking.Virtual })],
            readyDrivesOnly: true, allowVirtual: false);

        Assert.AreEqual(0, places.Count(p => p.Kind == ThisPcPlaceKind.Provided));
    }

    [TestMethod]
    public void TheEnumerationListsTheDrivesEvenWithNoProvidersAtAll()
    {
        var places = ThisPcItemSet.Enumerate([], readyDrivesOnly: true, allowVirtual: false);

        Assert.AreEqual(System.IO.DriveInfo.GetDrives().Count(d => d.IsReady), places.Count);
        Assert.IsTrue(places.All(p => p.Kind == ThisPcPlaceKind.Drive));
    }

    private sealed class SingleItemProvider(ThisPcItem item) : IThisPcItemProvider
    {
        public string ProviderId => "test";
        public event System.Action? Changed { add { } remove { } }
        public IReadOnlyList<ThisPcItem> GetItems() => [item];
    }
}
