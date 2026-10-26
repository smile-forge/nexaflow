using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Nexaflow.Core.Controls;
using Nexaflow.Features.Common.ThisPc;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.Core.Unit;

/// <summary>
/// The themed file/folder pickers render the same rows the file browser does — one <see cref="ThisPcItemSet.Collect"/>
/// feeds both, so the two can't disagree on what This PC contains or what each row is called.
/// <para>
/// A picker renders every row by its REAL path, because its result goes straight to ordinary
/// <c>System.IO</c> callers; the user only reads the label. A root its provider finds is not there leaves the list.
/// </para>
/// </summary>
[TestClass]
[CoversNode("win-cux-folder-picker")]
public class PickerProvidedRootsTests
{
    /// <summary>Describes each of its rows as given: available unless named in <paramref name="gone"/>, with a fuller
    /// label where <paramref name="labels"/> has one.</summary>
    private sealed class TestProvider(string id, IReadOnlyList<ThisPcItem> items, IReadOnlyCollection<string>? gone = null,
                                      IReadOnlyDictionary<string, string>? labels = null) : IThisPcItemProvider
    {
        public string ProviderId => id;
        public event System.Action? Changed { add { } remove { } }
        public IReadOnlyList<ThisPcItem> GetItems() => items;

        public Task<ThisPcItemDetail> GetDetailAsync(ThisPcItem item, CancellationToken ct = default)
            => Task.FromResult(new ThisPcItemDetail
            {
                Available = gone?.Contains(item.Id) != true,
                Label     = labels?.GetValueOrDefault(item.Id),
            });
    }

    private static ThisPcItem Volume(string path, string label, ThisPcItemIcon icon = ThisPcItemIcon.Disk) =>
        new() { Id = "drive." + path[0], Label = label, TargetPath = path, TypeLabel = "Local Disk", Icon = icon,
                Backing = ThisPcItemBacking.OwnPath };

    private static ThisPcItem Provided(string id, string path) =>
        new() { Id = id, Label = "Cloud " + id, TargetPath = path, TypeLabel = "Cloud", Icon = ThisPcItemIcon.Cloud };

    private static List<ThisPcRow> Rows(params ThisPcItem[] items)
    {
        var provider = new TestProvider("test", items);
        return [.. items.Select(item => new ThisPcRow(provider, item))];
    }

    [TestMethod]
    public void TheFolderPickerShowsVolumesAndContributedLocationsInOneList()
    {
        var vm = new FolderBrowserViewModel(rows: Rows(Volume(@"C:\", "System (C:)"), Provided("a", @"C:\Cloud\A")));

        CollectionAssert.AreEqual(new[] { "System (C:)", "Cloud a" },
                                  vm.Roots.Select(r => r.DisplayName).ToArray());
    }

    [TestMethod]
    public void EveryRootIsListedByItsRealPathSoTheResultCanBeOpened()
    {
        var vm = new FolderBrowserViewModel(rows: Rows(Provided("a", @"C:\Cloud\A")));

        Assert.AreEqual(@"C:\Cloud\A", vm.Roots[0].FullPath);
        Assert.IsFalse(vm.Roots[0].FullPath.StartsWith("::"), "a picker result must be a usable path");
    }

    [TestMethod]
    public void EveryKindOfPlaceGetsItsOwnGlyphIncludingTheVolumes()
    {
        // The gap this closes: a USB stick reading as a plain folder while a cloud location got its own
        // mark, because only half the vocabulary was mapped.
        var vm = new FolderBrowserViewModel(rows: Rows(
            Volume(@"C:\", "Disk",    ThisPcItemIcon.Disk),
            Volume(@"E:\", "Stick",   ThisPcItemIcon.Removable),
            Volume(@"D:\", "Optical", ThisPcItemIcon.Optical),
            Volume(@"Z:\", "Share",   ThisPcItemIcon.Network),
            Provided("c", @"C:\Cloud")));

        var glyphs = vm.Roots.Select(r => r.Glyph).ToArray();
        Assert.AreEqual(glyphs.Length, glyphs.Distinct().Count(),
                        "each kind of place must be visually distinguishable from the others");
    }

    [TestMethod]
    public void TheFilePickerRendersTheSameListAsTheFolderPicker()
    {
        var rows = Rows(Volume(@"C:\", "System (C:)"), Provided("a", @"C:\Cloud\A"));

        var folders = new FolderBrowserViewModel(rows: rows);
        var files   = new FileBrowserViewModel(null, rows: rows);

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
        var files = new FileBrowserViewModel(null, rows: Rows(Volume(@"C:\", "System (C:)"), Provided("a", @"C:\Cloud")));

        Assert.IsTrue(files.Roots.All(r => r.IsDirectory));
    }

    [TestMethod]
    public void WithNoRowsAPickerIsSimplyEmptyRatherThanGuessing()
    {
        Assert.AreEqual(0, new FolderBrowserViewModel(rows: []).Roots.Count);
        Assert.AreEqual(0, new FileBrowserViewModel(null, rows: []).Roots.Count);
    }

    [TestMethod]
    public void ARootThatIsNotThereLeavesThePicker_AndOneThatAnsweredTakesItsFullerLabel()
    {
        // An empty optical bay cannot be opened, so a picker does not offer it; a volume that answered is named in full.
        ThisPcItem[] items = [Volume(@"C:\", @"C:\"), Volume(@"D:\", @"D:\", ThisPcItemIcon.Optical)];
        var provider = new TestProvider("drive", items, gone: ["drive.D"],
                                        labels: new Dictionary<string, string> { ["drive.C"] = "System (C:)" });
        List<ThisPcRow> rows = [.. items.Select(item => new ThisPcRow(provider, item))];

        var folders = new FolderBrowserViewModel(rows: rows);
        var files   = new FileBrowserViewModel(null, rows: rows);

        CollectionAssert.AreEqual(new[] { "System (C:)" }, folders.Roots.Select(r => r.DisplayName).ToArray());
        CollectionAssert.AreEqual(new[] { "System (C:)" }, files.Roots.Select(r => r.DisplayName).ToArray());
    }

    // ── What the shared merge hands a picker ─────────────────────────────────

    [TestMethod]
    public void TheMergeSkipsALocationThatIsAlreadyAVolume()
    {
        var volumes  = new TestProvider("drive", [Volume(@"C:\", "System (C:)")]);
        var provided = new TestProvider("test", [Provided("dup", @"C:\")]);

        var rows = ThisPcItemSet.Collect([provided, volumes], allowVirtual: false);

        CollectionAssert.AreEqual(new[] { "drive.C" }, rows.Select(r => r.Item.Id).ToArray());
    }

    [TestMethod]
    public void TheMergeSkipsALocationAPickerCouldNotHandBackAPathFor()
    {
        var provider = new TestProvider("test",
            [new ThisPcItem { Id = "cloudonly", Label = "Cloud", TargetPath = "cloud://acct", TypeLabel = "Cloud",
                              Backing = ThisPcItemBacking.Virtual }]);

        Assert.AreEqual(0, ThisPcItemSet.Collect([provider], allowVirtual: false).Count);
    }
}
