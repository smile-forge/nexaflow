using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Nexaflow.Features.Hdf5.ViewModels;
using Nexaflow.Tests.Fixtures;
using Nexaflow.Visuals.Common.Localization;
using static Nexaflow.Tests.Features.Hdf5.Hdf5Harness;

namespace Nexaflow.Tests.Features.Hdf5;

/// <summary>
/// The viewer page as the user drives it: the tree lists lazily and a page at a time, the table reads one window
/// per scroll and lies N-D data on rows and columns, and the drawer shows the selected object's details and
/// attributes.
/// </summary>
[TestClass]
[CoversNode("hdf5")]
public sealed class Hdf5ViewModelTests
{
    // ── Structure panel ───────────────────────────────────────────────────

    [TestMethod]
    [CoversNode("hdf5-object-tree")]
    public async Task Opening_ListsTheRoot_AndSelectsIt()
    {
        using var vm = await OpenAsync();
        var root = vm.Roots.Single();

        Assert.AreEqual("experiment.h5", root.Display);
        CollectionAssert.IsSubsetOf(new[] { "calibration", "measurements", "metadata", "latest", "dangling", "typedef" },
            root.ObjectChildren.Select(c => c.Display).ToArray());
        Assert.AreEqual("/", vm.Current!.Path);
        Assert.IsTrue(vm.IsGroupContent);
        Assert.IsFalse(vm.IsLoading);
    }

    [TestMethod]
    [CoversNode("hdf5-object-tree")]
    public async Task ExpandingAGroup_ListsItsMembers_WithTypeAndShape()
    {
        using var vm = await OpenAsync();
        var measurements = await ExpandAsync(Child(vm.Roots[0], "measurements"));

        var frames = Child(measurements, "frames");
        Assert.AreEqual("uint16 · 4 × 16 × 16", frames.Detail);
        Assert.AreEqual(Hdf5NodeRole.Loading, Child(vm.Roots[0], "calibration").Children.Single().Role,
            "an unopened group has not been listed");
    }

    [TestMethod]
    [CoversNode("hdf5-object-tree")]
    public async Task ALargeGroup_ListsAPageAtATime()
    {
        var path = WriteLargeGroup(Hdf5ViewModel.TreePage + 20);
        try
        {
            using var vm = await OpenAsync(path: path);
            var many = await ExpandAsync(Child(vm.Roots[0], "many"));
            Assert.AreEqual(Hdf5ViewModel.TreePage, many.ObjectChildren.Count());
            var more = many.Children.Last();
            Assert.AreEqual(Hdf5NodeRole.More, more.Role);

            vm.SelectedNode = more;
            for (int i = 0; i < 500 && many.ObjectChildren.Count() < Hdf5ViewModel.TreePage + 20; i++) await Task.Delay(10);
            Assert.AreEqual(Hdf5ViewModel.TreePage + 20, many.ObjectChildren.Count());
            Assert.IsFalse(many.Children.Any(c => c.Role == Hdf5NodeRole.More));

            await SelectAsync(vm, "/many/d0519");
            Assert.AreEqual("/many/d0519", vm.Current!.Path);
        }
        finally { File.Delete(path); }
    }

    [TestMethod]
    [CoversNode("hdf5-object-tree")]
    public async Task NavigatingToAProjectedFileName_SelectsItsDataset()
    {
        using var vm = await OpenAsync(node: "/measurements/temperature.npy");
        Assert.AreEqual("/measurements/temperature", vm.Current!.Path);
        Assert.IsTrue(Child(vm.Roots[0], "measurements").IsExpanded);
    }

    [TestMethod]
    [CoversNode("hdf5-object-tree")]
    public async Task ALinkToNothing_SaysWhy()
    {
        using var vm = await OpenAsync(node: "/dangling");
        Assert.IsTrue(vm.IsMessageContent);
        StringAssert.Contains(vm.ContentMessage, "nowhere");
    }

    [TestMethod]
    [CoversNode("hdf5-object-tree")]
    public async Task AFileThatIsNotHdf5_SaysWhy_InsteadOfATree()
    {
        var path = Path.Combine(Path.GetTempPath(), $"nexa-not-hdf5-{System.Guid.NewGuid():N}.h5");
        File.WriteAllText(path, "not an hdf5 file");
        try
        {
            using var vm = await OpenAsync(path: path);
            Assert.IsNotNull(vm.LoadProblem);
            Assert.AreEqual(0, vm.Roots.Count);
            Assert.IsTrue(vm.IsContextReady);
        }
        finally { File.Delete(path); }
    }

    [TestMethod]
    [CoversNode("hdf5-tree-filter")]
    public async Task TreeFilter_HidesWhatDoesNotMatch_KeepingTheWayThere()
    {
        using var vm = await OpenAsync();
        var root = vm.Roots[0];
        var measurements = await ExpandAsync(Child(root, "measurements"));

        vm.TreeFilter = "frame";
        Assert.IsTrue(Child(measurements, "frames").IsVisible);
        Assert.IsFalse(Child(measurements, "counter").IsVisible);
        Assert.IsTrue(measurements.IsVisible, "the group leading to a match stays");
        Assert.IsFalse(Child(root, "calibration").IsVisible);

        vm.TreeFilter = string.Empty;
        Assert.IsTrue(Child(measurements, "counter").IsVisible);
    }

    // ── Content pane ──────────────────────────────────────────────────────

    [TestMethod]
    [CoversNode("hdf5-data-table")]
    public async Task ADataset_ShowsItsFirstWindow_OneColumnForOneDimension()
    {
        using var vm = await OpenAsync(node: "/measurements/temperature");
        var table = vm.Table!;

        Assert.AreEqual(500, table.TotalRowCount);
        Assert.AreEqual(Hdf5TableViewModel.WindowSize, table.Window.Count);
        Assert.AreEqual("temperature", table.Columns.Single().Header);
        Assert.AreEqual(0, table.Window[0].AbsoluteIndex);
        Assert.AreEqual(293.15, double.Parse(table.Window[0].Cells[0], CultureInfo.InvariantCulture), 1e-9);
    }

    [TestMethod]
    [CoversNode("hdf5-data-table")]
    [CoversNode("hdf5-windowing")]
    public async Task Scrolling_ReadsTheWindowAtTheNewRow()
    {
        using var vm = await OpenAsync(node: "/measurements/counter");
        var table = vm.Table!;
        table.FocalRow = 1900;
        await table.RefreshWindowAsync();

        Assert.AreEqual(100, table.Window.Count, "the window stops at the last row");
        Assert.AreEqual(1900, table.Window[0].AbsoluteIndex);
        Assert.AreEqual("1999", table.Window[^1].Cells[0]);
    }

    [TestMethod]
    [CoversNode("hdf5-data-table")]
    public async Task ACompound_HasAColumnPerMember()
    {
        using var vm = await OpenAsync(node: "/measurements/events");
        var table = vm.Table!;

        CollectionAssert.AreEqual(new[] { "t", "ch", "energy" }, table.Columns.Select(c => c.Header).ToArray());
        CollectionAssert.AreEqual(new[] { "0.002", "2", "513" }, table.Window[2].Cells.ToArray());
        Assert.IsFalse(table.Slice.HasSlice, "one dimension of records has nothing to slice");
    }

    [TestMethod]
    [CoversNode("hdf5-slice-bar")]
    public async Task ThreeDimensions_ShowTheLastTwo_HoldingTheFirstAtAnIndex()
    {
        using var vm = await OpenAsync(node: "/measurements/frames");
        var table = vm.Table!;
        var slice = table.Slice;

        Assert.AreEqual(1, slice.RowAxis!.Dimension);
        Assert.AreEqual(2, slice.ColumnAxis!.Dimension);
        Assert.AreEqual(0, slice.FixedIndices.Single().Dimension);
        Assert.AreEqual(16, table.Columns.Count);
        Assert.AreEqual("35", table.Window[2].Cells[3], "frame 0, row 2, column 3");

        slice.FixedIndices[0].Index = 2;
        await table.RefreshWindowAsync();
        Assert.AreEqual(((2 * 256 + 2 * 16 + 3) % 1000).ToString(CultureInfo.InvariantCulture), table.Window[2].Cells[3]);
    }

    [TestMethod]
    [CoversNode("hdf5-slice-bar")]
    public async Task ChoosingTheColumnsDimensionForRows_SwapsTheTwo()
    {
        using var vm = await OpenAsync(node: "/measurements/matrix");
        var table = vm.Table!;
        var slice = table.Slice;

        slice.RowAxis = slice.Axes[1];
        await table.RefreshWindowAsync();

        Assert.AreEqual(0, slice.ColumnAxis!.Dimension);
        Assert.AreEqual(300, table.TotalRowCount);
        Assert.AreEqual((10 * 300 + 5).ToString(CultureInfo.InvariantCulture), table.Window[5].Cells[10], "row 5 is column 5 of the file");
    }

    [TestMethod]
    [CoversNode("hdf5-slice-bar")]
    public async Task WideData_ShowsAWindowOfColumns_ThatMoves()
    {
        using var vm = await OpenAsync(node: "/measurements/matrix");
        var table = vm.Table!;
        var slice = table.Slice;

        Assert.IsTrue(slice.HasColumnWindow);
        Assert.AreEqual((int)Hdf5SliceViewModel.ColumnWindow, table.Columns.Count);

        slice.ColumnOffset = 44;
        await table.RefreshWindowAsync();
        Assert.AreEqual("44", table.Columns[0].Header);
        Assert.AreEqual((int)Hdf5SliceViewModel.ColumnWindow, table.Columns.Count);
        Assert.AreEqual((1 * 300 + 44).ToString(CultureInfo.InvariantCulture), table.Window[1].Cells[0]);
    }

    [TestMethod]
    [CoversNode("hdf5-group-summary")]
    public async Task AGroup_CountsItsMembersByKind()
    {
        using var vm = await OpenAsync();
        var counts = vm.GroupMembers.ToDictionary(m => m.Kind, m => m.Count);

        Assert.AreEqual(3, counts[Str.Get("Hdf5.Group.Groups")]);
        Assert.AreEqual(1, counts[Str.Get("Hdf5.Group.Datasets")]);
        Assert.AreEqual(2, counts[Str.Get("Hdf5.Group.UnresolvedLinks")]);
        Assert.AreEqual(1, counts[Str.Get("Hdf5.Group.NamedDatatypes")]);
    }

    [TestMethod]
    [CoversNode("hdf5-data-table")]
    public async Task AnEmptyDataset_SaysSo_RatherThanAnEmptyTable()
    {
        using var vm = await OpenAsync(node: "/metadata/empty");
        Assert.IsTrue(vm.IsMessageContent);
        Assert.IsNull(vm.Table);
    }

    // ── Details drawer ────────────────────────────────────────────────────

    [TestMethod]
    [CoversNode("hdf5-details-toggle")]
    public async Task TheDrawer_StartsOpen_AndCloses()
    {
        using var vm = await OpenAsync();
        Assert.IsTrue(vm.DetailsOpen);
        vm.DetailsOpen = false;
        StringAssert.Contains(vm.GetContext(), "Details drawer (closed");
    }

    [TestMethod]
    [CoversNode("hdf5-object-details")]
    public async Task ObjectDetails_GiveTypeShapeAndStorage()
    {
        using var vm = await OpenAsync(node: "/measurements/temperature");
        var obj = vm.ObjectDetails.ToDictionary(r => r.Label, r => r.Value);
        var storage = vm.StorageDetails.ToDictionary(r => r.Label, r => r.Value);

        Assert.AreEqual("/measurements/temperature", obj[Str.Get("Hdf5.Details.Path")]);
        Assert.AreEqual("float64", obj[Str.Get("Hdf5.Details.Type")]);
        Assert.AreEqual("500", obj[Str.Get("Hdf5.Details.Shape")]);
        Assert.AreEqual("∞", obj[Str.Get("Hdf5.Details.MaxShape")]);
        Assert.AreEqual(Str.Get("Hdf5.Layout.Chunked"), storage[Str.Get("Hdf5.Details.Layout")]);
        Assert.AreEqual("100", storage[Str.Get("Hdf5.Details.ChunkShape")]);
        Assert.IsTrue(vm.HasStorage);
    }

    [TestMethod]
    [CoversNode("hdf5-attribute-list")]
    public async Task Attributes_ListWithTypeAndValue()
    {
        using var vm = await OpenAsync(node: "/measurements/temperature");
        var attrs = vm.Attributes.ToDictionary(a => a.Name);

        Assert.AreEqual("K", attrs["units"].Value);
        Assert.AreEqual("[1, 0.5, 0.25]", attrs["calibration"].Value);
        Assert.AreEqual("float64 [3]", attrs["calibration"].TypeText);
        Assert.AreEqual(Str.Format("Hdf5.Details.AttributesHeaderFormat", 4), vm.AttributesHeader);
        Assert.IsFalse(vm.HasNoAttributes);
    }

    [TestMethod]
    [CoversNode("hdf5-attribute-filter")]
    public async Task AttributeFilter_NarrowsByNameOrValue()
    {
        using var vm = await OpenAsync(node: "/measurements/temperature");

        vm.AttributeFilter = "sens";
        CollectionAssert.AreEqual(new[] { "sensor" }, vm.Attributes.Select(a => a.Name).ToArray());
        vm.AttributeFilter = "pt100";
        CollectionAssert.AreEqual(new[] { "sensor" }, vm.Attributes.Select(a => a.Name).ToArray(), "values match too");
        vm.AttributeFilter = string.Empty;
        Assert.AreEqual(4, vm.Attributes.Count);
    }
}
