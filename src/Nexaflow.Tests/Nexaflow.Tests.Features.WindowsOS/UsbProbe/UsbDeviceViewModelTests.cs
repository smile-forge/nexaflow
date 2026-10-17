using System.IO;
using System.Text.Json.Nodes;
using Nexaflow.Features.Common;
using Nexaflow.Features.Common.ClientTools;
using Nexaflow.Features.UsbProbe.ClientTools;
using Nexaflow.Features.UsbProbe.ViewModels;
using Nexaflow.IO.Usb.Browsing;
using Nexaflow.IO.Usb.Inventory;
using Nexaflow.IO.Usb.Naming;
using Nexaflow.IO.Usb.Tools;
using Nexaflow.Plugins;
using Nexaflow.Tests.Fixtures;
using NSubstitute;

namespace Nexaflow.Tests.Features.UsbProbe;

/// <summary>
/// The device page against a device the test controls and tool plugins it writes: which tools are offered and what is
/// left uncovered, when tools run, following the device out and back in, and the agent's one tool runner — free for
/// reads, approval for writes.
/// </summary>
[TestClass]
[CoversNode("usbprobe-device")]
public class UsbDeviceViewModelTests
{
    static readonly string HidFunction = UsbProbeSamples.LgId.Replace("9A39", "9A39&MI_00");
    static readonly string SerialFunction = UsbProbeSamples.LgId.Replace("9A39", "9A39&MI_01");

    string _dir = "";
    FakeUsbInventory _inventory = null!;
    IShellServices _shell = null!;
    readonly List<FakeTool> _made = [];

    [TestInitialize]
    public void Setup()
    {
        _dir = Directory.CreateTempSubdirectory("nexaflow-usbdevice-").FullName;
        _inventory = new FakeUsbInventory(UsbProbeSamples.Machine());
        _shell = Substitute.For<IShellServices>();
        _shell.RunOnUiAsync(Arg.Any<Action>()).Returns(call =>
        {
            call.Arg<Action>()();
            return Task.CompletedTask;
        });
    }

    [TestCleanup]
    public void Cleanup() => Directory.Delete(_dir, recursive: true);

    /// <summary>A provider offering one tool on the device's HID function.</summary>
    FakeProvider HidTools() => new("hid", (device, _) =>
    {
        var tool = new FakeTool("test.hid", device.Function(HidFunction)?.InstanceId);
        _made.Add(tool);
        return [tool];
    });

    async Task<UsbDeviceViewModel> PageAsync(string target, params IUsbToolProvider[] providers)
    {
        var handles = providers.Select((p, i) => (ISubfeatureHandle<IUsbToolProvider>)new FakeHandle<IUsbToolProvider>(p, $"p{i}")).ToList();
        var vm = new UsbDeviceViewModel(_inventory, new UsbIdsStore(_dir, () => null), handles, _shell, target);
        await vm.StartAsync();
        await vm.Offered;
        return vm;
    }

    [TestMethod]
    public async Task OpensTheDevice_WithWhatItIs_AndWhatItIsMadeOf()
    {
        var vm = await PageAsync("1-2.4");

        Assert.IsTrue(vm.IsPresent);
        Assert.AreEqual("LG Monitor Controls", vm.Title);
        Assert.AreEqual("1-2.4", vm.Path);
        Assert.AreEqual("043E:9A39", vm.Ids);
        Assert.AreEqual(UsbFactTopic.Summary, vm.FactGroups[0].Topic, "what it is comes first, in plain words");
        Assert.AreEqual(1, vm.FactGroups.Count(g => g.Topic == UsbFactTopic.Summary), "and is said once");
        Assert.IsFalse(vm.FactGroups.Any(g => g.Topic is UsbFactTopic.Functions or UsbFactTopic.Diagnostics),
            "the functions and the problems have tabs of their own, so the Overview does not repeat them");
        Assert.AreEqual("1-2.4/config1", vm.Structure[0].Path);
        Assert.IsTrue(vm.Structure.Any(r => r.Path.Contains("/if")), "its interfaces are listed under the configuration");
        Assert.IsNotNull(vm.StructureDetail, "an interface is shown, with its facts and its descriptor");
        Assert.IsTrue(vm.SelectedStructure!.Path.Contains("/if"));
    }

    [TestMethod]
    public async Task ANameOrIds_FindTheDevice_AndThePageTakesItsPort()
    {
        var vm = await PageAsync("043e:9a39");
        Assert.AreEqual("1-2.4", vm.Path);
    }

    [TestMethod]
    public async Task EachProvidersTools_AreOffered_AndWhatNoToolCovers_IsSaid()
    {
        var broken = new FakeProvider("broken", (_, _) => throw new InvalidOperationException("no driver for this"));
        var templates = new List<Uri>();
        var hid = HidTools();

        var handles = new IUsbToolProvider[] { hid, broken }.Select((p, i) => (ISubfeatureHandle<IUsbToolProvider>)new FakeHandle<IUsbToolProvider>(p, $"p{i}")).ToList();
        var vm = new UsbDeviceViewModel(_inventory, new UsbIdsStore(_dir, () => null), handles, _shell, "1-2.4");
        vm.TemplatesNeeded += templates.Add;
        await vm.StartAsync();
        await vm.Offered;

        var tile = vm.Tiles.OfType<UsbToolTile>().Single();
        Assert.AreEqual("test.hid", tile.Tool.ToolId);
        StringAssert.Contains(tile.FunctionName, "interface 0", "a tile says which function it works through");
        CollectionAssert.AreEqual(new[] { hid.Templates }, templates, "the templates are merged once, before the tile");

        var uncovered = vm.Tiles.OfType<UsbUncoveredTile>().Single().Functions.Single();
        StringAssert.Contains(uncovered.Name, "interface 1", "the serial function had no tool here");
        Assert.IsFalse(string.IsNullOrEmpty(uncovered.Guidance));
        Assert.AreSame(vm.Tiles[^1], vm.Tiles.OfType<UsbUncoveredTile>().Single(), "and is said last");

        StringAssert.Contains(vm.Log.Single().Message, "no driver for this", "a broken provider is logged, and the others' tools still come");
        Assert.IsNull(vm.NoTools, "a tab with tiles needs no explanation");
    }

    [TestMethod]
    public async Task AHub_SaysWhyItHasNoTools()
    {
        var vm = await PageAsync("1-2");

        Assert.AreEqual(0, vm.Tiles.Count);
        StringAssert.Contains(vm.NoTools, "hub");
        StringAssert.Contains(vm.FactGroups[0].Facts[0].Value, "gives the devices plugged into it their ports");
    }

    [TestMethod]
    public async Task ATool_RunsOnlyWhileItCanBeSeen()
    {
        var vm = await PageAsync("1-2.4", HidTools());
        var tool = _made.Single();
        var tile = vm.Tiles.OfType<UsbToolTile>().Single();

        Assert.AreEqual(0, tool.Activations, "the Overview tab is open, so nothing runs");
        vm.SetPageShown(true);
        vm.SelectedTab = UsbDeviceViewModel.LiveToolsTab;
        Assert.AreEqual(1, tool.Activations);

        tile.IsExpanded = false;
        Assert.AreEqual(1, tool.Deactivations, "shutting the tile stops it");
        tile.IsExpanded = true;
        Assert.AreEqual(2, tool.Activations);

        vm.SelectedTab = UsbDeviceViewModel.DiagnosticsTab;
        Assert.AreEqual(2, tool.Deactivations, "leaving the tab stops it");
        vm.SelectedTab = UsbDeviceViewModel.LiveToolsTab;
        vm.SetPageShown(false);
        Assert.AreEqual(3, tool.Deactivations, "so does leaving the page");
    }

    [TestMethod]
    public async Task Unplugged_TheToolsStop_AndPluggedInElsewhere_ThePageFollows()
    {
        var hid = HidTools();
        var vm = await PageAsync("1-2.4", hid);
        int moved = 0;
        vm.Moved += () => moved++;

        _inventory.Become(UsbProbeSamples.Tree(UsbProbeSamples.Hub()));
        Assert.IsFalse(vm.IsPresent);
        Assert.IsTrue(_made[0].Disposed, "its tools stop when it goes");
        Assert.AreEqual(0, vm.Tiles.Count);
        StringAssert.Contains(vm.Status, "unplugged");

        var elsewhere = UsbProbeSamples.Lg() with { Location = new UsbPortLocation(UsbProbeSamples.HubId, 1, 1, [2, 1], true, false) };
        _inventory.Become(UsbProbeSamples.Tree(UsbProbeSamples.Hub(), elsewhere));
        await vm.Offered;

        Assert.IsTrue(vm.IsPresent);
        Assert.AreEqual("1-2.1", vm.Path, "the same device on another port is still this page's device");
        Assert.AreEqual(1, moved);
        Assert.AreEqual(2, hid.Offers, "and its tools are offered again");
        Assert.AreEqual(1, vm.Tiles.OfType<UsbToolTile>().Count());
        StringAssert.Contains(vm.Status, "back");
    }

    [TestMethod]
    public async Task ADifferentDevice_OnTheSamePort_IsNotThisPagesDevice()
    {
        var vm = await PageAsync("1-2.4");
        var stranger = UsbProbeSamples.Lg() with { InstanceId = @"USB\VID_043E&PID_9A39\SOMEONEELSE" };

        _inventory.Become(UsbProbeSamples.Tree(UsbProbeSamples.Hub()));
        _inventory.Become(UsbProbeSamples.Tree(UsbProbeSamples.Hub(), stranger));

        Assert.IsFalse(vm.IsPresent, "the page follows its device, not the port");
    }

    [TestMethod]
    public async Task ATileShut_StaysShut_WhenTheToolsAreOfferedAgain()
    {
        var vm = await PageAsync("1-2.4", HidTools());
        vm.Tiles.OfType<UsbToolTile>().Single().IsExpanded = false;

        // The serial function's driver changes: the providers are asked again.
        var redriven = UsbProbeSamples.Lg() with
        {
            Functions = [.. UsbProbeSamples.Lg().Functions.Select(f => f.InstanceId == SerialFunction ? f with { ComPort = "COM9" } : f)],
        };
        _inventory.Become(UsbProbeSamples.Tree(UsbProbeSamples.Hub(), redriven));
        await vm.Offered;

        Assert.AreEqual(2, _made.Count, "offered again");
        Assert.IsTrue(_made[0].Disposed, "the old tool is let go");
        Assert.IsFalse(vm.Tiles.OfType<UsbToolTile>().Single().IsExpanded, "and the new one keeps the tile shut");
    }

    [TestMethod]
    public async Task Diagnostics_NameWhatIsWrong()
    {
        var broken = UsbProbeSamples.Lg() with
        {
            Functions = [.. UsbProbeSamples.Lg().Functions.Select(f => f.InstanceId == SerialFunction ? f with { Driver = f.Driver with { ProblemCode = 28 } } : f)],
            Power = UsbPowerState.D3,
        };
        _inventory = new FakeUsbInventory(UsbProbeSamples.Tree(UsbProbeSamples.Hub(), broken));
        var vm = await PageAsync("1-2.4");

        Assert.IsTrue(vm.HasProblems);
        Assert.IsTrue(vm.Problems.Any(p => p.Contains("no driver installed") && p.Contains("interface 1")), string.Join(" | ", vm.Problems));
        Assert.IsTrue(vm.Problems.Any(p => p.Contains("asleep")));
    }

    [TestMethod]
    public async Task TheAgent_RunsReadsFreely_AndWritesOnlyWithApproval()
    {
        var vm = await PageAsync("1-2.4", HidTools());
        var tools = vm.GetClientTools();
        var run = tools.Single(t => t.Name == "usb_tool_run");

        var peek = new JsonObject { ["tool"] = "test.hid", ["operation"] = "peek" };
        var poke = new JsonObject { ["tool"] = "test.hid", ["operation"] = "poke", ["arguments"] = new JsonObject { ["value"] = "42" } };
        Assert.AreEqual(ToolSafety.SafeOperation, run.SafetyOf(peek), "reading runs at once");
        Assert.AreEqual(ToolSafety.RequiresApproval, run.SafetyOf(poke), "writing is put to the user");
        Assert.AreEqual(ToolSafety.RequiresApproval, run.SafetyOf(new JsonObject { ["tool"] = "nope", ["operation"] = "peek" }), "and so is anything it cannot place");

        Assert.AreEqual("test.hid peeked", Run(run, peek).ModelText);
        Assert.AreEqual("test.hid wrote 42", Run(run, poke).ModelText);
        CollectionAssert.AreEqual(new[] { "42" }, _made[0].Written);

        var missing = Run(run, new JsonObject { ["tool"] = "test.hid", ["operation"] = "poke" });
        Assert.IsTrue(missing.IsError);
        StringAssert.Contains(missing.Summary, "needs 'value'");
        StringAssert.Contains(Run(run, new JsonObject { ["tool"] = "test.hid", ["operation"] = "jump" }).Summary, "peek, poke");

        var listed = Run(tools.Single(t => t.Name == "usb_device_tools"), new JsonObject()).ModelText;
        StringAssert.Contains(listed, "op poke(value) — asks the user first");
        StringAssert.Contains(listed, "Not covered:");
        StringAssert.Contains(vm.GetContext(), "usb_tool_run");
    }

    [TestMethod]
    public async Task TheAgent_IsTold_WhenTheDeviceHasGone()
    {
        var vm = await PageAsync("1-2.4", HidTools());
        _inventory.Become(UsbProbeSamples.Tree(UsbProbeSamples.Hub()));

        var result = Run(vm.GetClientTools().Single(t => t.Name == "usb_tool_run"), new JsonObject { ["tool"] = "test.hid", ["operation"] = "peek" });
        Assert.IsTrue(result.IsError);
        StringAssert.Contains(result.Summary, "unplugged");
    }

    static ToolResult Run(IClientTool tool, JsonObject args) => tool.InvokeAsync(args, CancellationToken.None).GetAwaiter().GetResult();
}
