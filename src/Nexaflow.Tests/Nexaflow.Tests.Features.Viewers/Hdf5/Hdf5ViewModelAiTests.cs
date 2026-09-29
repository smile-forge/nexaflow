using System.Linq;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Nexaflow.Features.Common.ClientTools;
using Nexaflow.Features.Hdf5.ViewModels;
using Nexaflow.Tests.Fixtures;
using static Nexaflow.Tests.Features.Hdf5.Hdf5Harness;

namespace Nexaflow.Tests.Features.Hdf5;

/// <summary>
/// The AI sees everything the user sees — the tree as expanded, the whole drawer, the slice and the rows on
/// screen — and its tools reach anything the user could navigate to.
/// </summary>
[TestClass]
[CoversNode("hdf5")]
public sealed class Hdf5ViewModelAiTests
{
    private static Task<ToolResult> Invoke(Hdf5ViewModel vm, string tool, JsonObject args) =>
        vm.GetClientTools().Single(t => t.Name == tool).InvokeAsync(args, CancellationToken.None);

    [TestMethod]
    [CoversNode("hdf5-ai-context")]
    public async Task Context_CarriesTheTreeTheDrawerAndTheRowsOnScreen()
    {
        using var vm = await OpenAsync(node: "/measurements/temperature");
        var context = vm.GetContext();

        StringAssert.Contains(context, "measurements");
        StringAssert.Contains(context, "temperature — dataset float64, shape 500");
        StringAssert.Contains(context, "Details drawer (open)");
        StringAssert.Contains(context, "units: string = K");
        StringAssert.Contains(context, "Chunk shape: 100");
        StringAssert.Contains(context, "Rows 0–59 as loaded on screen");
        StringAssert.Contains(context, "call hdf5_read for any part of it", "a window longer than the context carries says where the rest is");
        StringAssert.Contains(context, "  0: 293.15");
        Assert.IsTrue(vm.IsContextReady);
    }

    [TestMethod]
    [CoversNode("hdf5-ai-context")]
    public async Task Context_FollowsTheSliceTheUserChose()
    {
        using var vm = await OpenAsync(node: "/measurements/frames");
        vm.Table!.Slice.FixedIndices[0].Index = 3;
        await vm.Table.ScrollToAsync(10);

        var context = vm.GetContext();
        StringAssert.Contains(context, "dimension 0 held at 3");
        StringAssert.Contains(context, "Rows 10–15");
        StringAssert.Contains(context, "along dimension 1");
    }

    [TestMethod]
    [CoversNode("hdf5-ai-act-list")]
    public async Task List_NamesEachMemberWithKindTypeAndShape()
    {
        using var vm = await OpenAsync();
        var result = await Invoke(vm, "hdf5_list", new JsonObject { ["path"] = "/measurements" });

        Assert.IsTrue(result.Success);
        StringAssert.Contains(result.ModelText, "frames — dataset uint16, shape 4 × 16 × 16");
        StringAssert.Contains(result.ModelText, "events — dataset {t: float64, ch: int16, energy: float32}, shape 50");
    }

    [TestMethod]
    [CoversNode("hdf5-ai-act-list")]
    public async Task List_PagesThroughAGroup()
    {
        using var vm = await OpenAsync();
        var first = await Invoke(vm, "hdf5_list", new JsonObject { ["path"] = "/metadata", ["take"] = 3 });
        StringAssert.Contains(first.ModelText, "call again with skip 3");
    }

    [TestMethod]
    [CoversNode("hdf5-ai-act-describe")]
    public async Task Describe_GivesTheDrawerForAnyObject_EvenOneNotSelected()
    {
        using var vm = await OpenAsync();
        var result = await Invoke(vm, "hdf5_describe", new JsonObject { ["path"] = "/measurements/temperature" });

        Assert.IsTrue(result.Success);
        StringAssert.Contains(result.ModelText, "Max shape: ∞");
        StringAssert.Contains(result.ModelText, "sample_rate: float64 = 1000");
        Assert.AreEqual("/", vm.Current!.Path, "describing does not move the user's selection");
    }

    [TestMethod]
    [CoversNode("hdf5-ai-act-read")]
    public async Task Read_ReturnsTheSelectionAsked_LineByLastDimension()
    {
        using var vm = await OpenAsync();
        var result = await Invoke(vm, "hdf5_read", new JsonObject
        {
            ["path"] = "/measurements/matrix", ["start"] = "10,5", ["count"] = "2,3",
        });

        Assert.IsTrue(result.Success);
        StringAssert.Contains(result.ModelText, "[10, 5]: 3005, 3006, 3007");
        StringAssert.Contains(result.ModelText, "[11, 5]: 3305, 3306, 3307");
    }

    [TestMethod]
    [CoversNode("hdf5-ai-act-read")]
    public async Task Read_WithoutASelection_ReadsTheFirstElements_AndRefusesTooMany()
    {
        using var vm = await OpenAsync();
        var first = await Invoke(vm, "hdf5_read", new JsonObject { ["path"] = "/measurements/counter" });
        StringAssert.StartsWith(first.ModelText, "[0]: 0, 1, 2");

        var tooMany = await Invoke(vm, "hdf5_read", new JsonObject { ["path"] = "/measurements/matrix", ["start"] = "0,0", ["count"] = "200,300" });
        Assert.IsFalse(tooMany.Success);
    }

    [TestMethod]
    [CoversNode("hdf5-ai-act-select")]
    public async Task Select_ShowsTheUserTheObject_AtTheRowAndSliceAsked()
    {
        using var vm = await OpenAsync();
        var result = await Invoke(vm, "hdf5_select", new JsonObject
        {
            ["path"] = "/measurements/frames", ["row"] = 4, ["indices"] = "2,0,0",
        });

        Assert.IsTrue(result.Success, result.ModelText);
        Assert.AreEqual("/measurements/frames", vm.Current!.Path);
        Assert.AreEqual(4, vm.Table!.FocalRow);
        Assert.AreEqual(2, (int)vm.Table.Slice.FixedIndices.Single().Index);
        Assert.AreEqual(4, vm.Table.Window[0].AbsoluteIndex);
    }

    [TestMethod]
    [CoversNode("hdf5-ai-act-select")]
    public async Task Select_OfNothing_SaysSo()
    {
        using var vm = await OpenAsync();
        var result = await Invoke(vm, "hdf5_select", new JsonObject { ["path"] = "/measurements/nothing" });
        Assert.IsFalse(result.Success);
    }

    [TestMethod]
    [CoversNode("hdf5-ai")]
    public async Task EveryTool_IsReadOnly_AndRunsUnasked()
    {
        using var vm = await OpenAsync();
        Assert.IsTrue(vm.GetClientTools().All(t => t.Safety == ToolSafety.SafeOperation));
        CollectionAssert.AreEquivalent(new[] { "hdf5_list", "hdf5_describe", "hdf5_read", "hdf5_select" },
            vm.GetClientTools().Select(t => t.Name).ToArray());
    }
}
