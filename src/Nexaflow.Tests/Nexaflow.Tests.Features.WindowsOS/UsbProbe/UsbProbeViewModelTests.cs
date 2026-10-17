using System.IO;
using System.Text.Json.Nodes;
using Nexaflow.Features.Common;
using Nexaflow.Features.Common.ClientTools;
using Nexaflow.Features.UsbProbe;
using Nexaflow.Features.UsbProbe.ViewModels;
using Nexaflow.Icons;
using Nexaflow.IO.Usb.Browsing;
using Nexaflow.IO.Usb.Inventory;
using Nexaflow.IO.Usb.Naming;
using Nexaflow.IO.Usb.Tools;
using Nexaflow.Plugins;
using Nexaflow.Tests.Fixtures;
using NSubstitute;

namespace Nexaflow.Tests.Features.UsbProbe;

/// <summary>
/// The page against a machine the test controls: moving around, following plug and unplug without losing the
/// user's place, the byte-to-field link, and the same tree through the agent's tools and <c>&gt;</c>.
/// </summary>
[TestClass]
[CoversNode("usbprobe")]
public class UsbProbeViewModelTests
{
    string _dir = "";
    FakeUsbInventory _inventory = null!;
    IShellServices _shell = null!;
    readonly List<IReadOnlyList<(string Label, string Path)>> _crumbs = [];

    [TestInitialize]
    public void Setup()
    {
        _dir = TestTemp.CreateSubdirectory("nexaflow-usbprobe-").FullName;
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

    UsbProbeViewModel Page(string? location = null, params IUsbToolProvider[] providers)
    {
        // No monthly check: a test never asks linux-usb.org anything.
        var handles = providers.Select((p, i) => (ISubfeatureHandle<IUsbToolProvider>)new FakeHandle<IUsbToolProvider>(p, $"p{i}")).ToList();
        var vm = new UsbProbeViewModel(_inventory, new UsbIdsStore(_dir, () => null), new UsbProbeConfig { CheckDeviceNamesMonthly = false }, handles, _shell, location);
        vm.NavigationChanged += _crumbs.Add;
        vm.StartAsync().GetAwaiter().GetResult();
        return vm;
    }

    static string Paths(UsbProbeViewModel vm) => string.Join(" ", vm.Rows.Select(r => r.Path));

    [TestMethod]
    public void OpensWhereTheTabSays_AndListsWhatIsThere()
    {
        var vm = Page("1-2.4");

        Assert.AreEqual("1-2.4", vm.CurrentPath);
        Assert.AreEqual("1-2.4/config1", Paths(vm));
        Assert.IsFalse(vm.IsTopologyListing, "inside a device the listing shows structure");
        Assert.AreEqual("LG Monitor Controls", vm.Detail?.Title);
        CollectionAssert.AreEqual(new[] { "", "1", "1-2", "1-2.4" }, _crumbs[^1].Select(c => c.Path).ToArray());
        Assert.AreEqual("LG Monitor Controls", _crumbs[^1][^1].Label);
    }

    [TestMethod]
    public void MovesLikeAFileBrowser()
    {
        var vm = Page();
        Assert.AreEqual("", vm.CurrentPath);
        Assert.AreEqual("1", Paths(vm));

        Assert.IsTrue(vm.Navigate("com5"), "a device is found by its COM port");
        Assert.AreEqual("1-2.4", vm.CurrentPath);

        vm.UpCommand.Execute(null);
        Assert.AreEqual("1-2", vm.CurrentPath);
        Assert.AreEqual("1-2.4", vm.SelectedRow?.Path, "going up selects where you came from");
        Assert.IsTrue(vm.IsTopologyListing);

        vm.OpenCommand.Execute(vm.SelectedRow);
        Assert.AreEqual("1-2.4", vm.CurrentPath);
        Assert.IsFalse(vm.Navigate("logitech"));
    }

    [TestMethod]
    public void ARightClickedPlace_IsWhatItsMenuActsOn()
    {
        var vm = Page("1-2");
        var lg = vm.Rows.Single(r => r.Path == "1-2.4");
        Assert.AreEqual("1-2.4", vm.CopyTextOf(lg, UsbCopyKind.Path));
        Assert.AreEqual(UsbProbeSamples.LgId, vm.CopyTextOf(lg, UsbCopyKind.InstanceId));
        Assert.AreEqual("043E:9A39", vm.CopyTextOf(lg, UsbCopyKind.Ids));
        Assert.IsTrue(vm.OpenDeviceCommand.CanExecute(lg));
        Assert.IsTrue(vm.CopyDebugDetailsCommand.CanExecute(lg) && vm.SaveDebugDetailsCommand.CanExecute(lg));

        var port = vm.Rows.First(r => r.Location.Kind == UsbLocationKind.Port);
        Assert.IsNull(vm.CopyTextOf(port, UsbCopyKind.InstanceId), "an empty port is no device");
        Assert.IsFalse(vm.OpenDeviceCommand.CanExecute(port));
        Assert.IsFalse(vm.ReReadCommand.CanExecute(port));
        Assert.IsFalse(vm.CopyDebugDetailsCommand.CanExecute(port) || vm.SaveDebugDetailsCommand.CanExecute(port), "debug details are a device's");

        var bus = vm.Root.Children.Single();
        Assert.AreEqual("1", vm.CopyTextOf(bus, UsbCopyKind.Path));
        Assert.IsNull(vm.CopyTextOf(bus, UsbCopyKind.Ids), "a bus has no IDs of its own");
        vm.OpenCommand.Execute(bus);
        Assert.AreEqual("1", vm.CurrentPath, "Open on a tree node goes there");

        Assert.AreEqual("1", vm.CopyTextOf(null, UsbCopyKind.Path), "with nothing picked, the place open");
        Assert.IsNull(Page().CopyTextOf(null, UsbCopyKind.Path), "the top has no path to copy");
    }

    [TestMethod]
    public void EachPlace_IsDrawnAsWhatItIs()
    {
        var vm = Page("1");
        Assert.AreEqual(IconRef.Fluent("developer_board"), vm.Root.Children.Single().Icon);
        Assert.AreEqual(IconRef.Fluent("branch_fork"), vm.Rows.Single(r => r.Path == "1-2").Icon, "a hub");
        Assert.AreEqual(IconRef.Fluent("circle_small"), vm.Rows.First(r => r.Location.Kind == UsbLocationKind.Port).Icon, "an empty port");
        Assert.IsTrue(vm.Navigate("1-2"));
        Assert.AreEqual(IconRef.Fluent("serial_port"), vm.Rows.Single(r => r.Path == "1-2.4").Icon,
            "the monitor's controls are HID and a serial port; the serial port is what it is drawn as");
    }

    [TestMethod]
    public void TreeStopsAtDevices()
    {
        var vm = Page("1-2.4/config1/iad1/if1.0");
        var bus = vm.Root.Children.Single();
        var hub = bus.Children.Single();
        Assert.AreEqual("1-2", hub.Path, "the empty root ports are leaves, so listing rows rather than tree nodes");
        var lg = hub.Children.Single(c => c.Path == "1-2.4");
        Assert.AreEqual(0, lg.Children.Count, "what a device is made of is the listing's and its own page's, not the tree's");
        var selected = Flatten(vm.Root).Single(n => n.IsSelected);
        Assert.AreEqual("1-2.4", selected.Path, "inside a device, the tree holds the device selected");
    }

    [TestMethod]
    public void APluggedInDevice_IsNewUntilLookedAt()
    {
        var vm = Page("1-2");
        _inventory.Become(UsbProbeSamples.Tree(UsbProbeSamples.Hub(), UsbProbeSamples.Lg(), UsbProbeSamples.Newcomer()));

        var row = vm.Rows.Single(r => r.Path == "1-2.1");
        Assert.IsTrue(row.IsNew);
        Assert.IsTrue(Flatten(vm.Root).Single(n => n.Path == "1-2.1").IsNew);
        StringAssert.Contains(vm.Status, "plugged in");

        vm.SelectedRow = row;
        Assert.IsFalse(row.IsNew);
        Assert.IsFalse(Flatten(vm.Root).Single(n => n.Path == "1-2.1").IsNew);
    }

    [TestMethod]
    public void APluggedInDevice_ShowsInTheTree_WithoutTakingTheSelection()
    {
        var vm = Page("1");
        var hub = vm.Root.Children.Single().Children.Single(c => c.Path == "1-2");
        hub.IsExpanded = false;

        _inventory.Become(UsbProbeSamples.Tree(UsbProbeSamples.Hub(), UsbProbeSamples.Lg(), UsbProbeSamples.Newcomer()));

        Assert.IsTrue(hub.IsExpanded, "the hub it was plugged into opens, so it shows");
        var arrived = hub.Children.Single(c => c.Path == "1-2.1");
        Assert.IsTrue(arrived.IsNew);
        Assert.IsFalse(arrived.IsSelected);
        Assert.AreEqual("1", vm.CurrentPath, "what was open stays open");
    }

    [TestMethod]
    public void AnEjectedDevice_IsShownEjected()
    {
        var vm = Page("1-2");
        var ejected = UsbProbeSamples.Lg() with { Driver = UsbProbeSamples.Lg().Driver with { ProblemCode = UsbProblemCodes.PreparedForRemoval } };
        _inventory.Become(UsbProbeSamples.Tree(UsbProbeSamples.Hub(), ejected));

        var row = vm.Rows.Single(r => r.Path == "1-2.4");
        StringAssert.Contains(row.Summary, "ejected — safe to unplug");
        Assert.AreEqual(Nexaflow.Icons.IconRef.Fluent("plug_disconnected"), row.Icon);
    }

    [TestMethod]
    public void UnpluggingTheDeviceInView_MovesToTheNearestPlaceLeft_AndPluggingBackShowsItThere()
    {
        var vm = Page("1-2.4/config1/iad1/if1.0");
        _inventory.Become(UsbProbeSamples.Tree(UsbProbeSamples.Hub()));

        Assert.AreEqual("1-2.4", vm.CurrentPath, "the port the device was in is still a place");
        Assert.AreEqual(UsbLocationKind.Port, vm.Current?.Kind);
        StringAssert.Contains(vm.Status, "LG Monitor Controls");

        _inventory.Become(UsbProbeSamples.Machine());
        Assert.AreEqual(UsbLocationKind.Device, vm.Current?.Kind, "plugged back in, it is where the user is looking");
        Assert.AreEqual("1-2.4/config1", Paths(vm));
    }

    [TestMethod]
    public void AnUpdate_KeepsTheRowAndItsSelection()
    {
        _inventory = new FakeUsbInventory(UsbProbeSamples.Tree(UsbProbeSamples.Hub(), UsbProbeSamples.Lg() with { Strings = UsbDeviceStrings.Unread }));
        var vm = Page("1-2");
        var row = vm.Rows.Single(r => r.Path == "1-2.4");
        vm.SelectedRow = row;

        _inventory.Become(UsbProbeSamples.Machine());

        Assert.AreSame(row, vm.SelectedRow);
        Assert.AreEqual("LG Monitor Controls", row.Name, "the row now carries the strings the device gave on waking");
        Assert.AreEqual("LG Monitor Controls", vm.Detail?.Title);
    }

    [TestMethod]
    public void HoveringBytesNamesTheirField_AndHoveringAFieldLightsItsBytes()
    {
        var vm = Page("1-2.4/config1/iad1/if1.0");
        vm.SelectedRow = vm.Rows.Single(r => r.Path.EndsWith("ep0x83"));
        var detail = vm.Detail!;
        Assert.AreEqual(1, vm.DetailTab, "an endpoint has nothing but its descriptor");
        Assert.AreEqual((77, 7), (detail.Selection!.Value.Offset, detail.Selection.Value.Length));

        detail.HoveredOffset = 83;
        Assert.AreEqual((83, 1), (detail.Highlight!.Value.Offset, detail.Highlight.Value.Length));
        StringAssert.StartsWith(detail.HoverText, "Endpoint 0x83 › bInterval = 0x02");

        detail.HoveredOffset = 50;
        StringAssert.StartsWith(detail.HoverText, "Interface 1, alt 0 › bDescriptorType", "a byte of another descriptor in the same blob is named too");

        detail.HoveredOffset = -1;
        detail.HoveredField = detail.Fields.Single(f => f.Name == "wMaxPacketSize");
        Assert.AreEqual((81, 2), (detail.Highlight!.Value.Offset, detail.Highlight.Value.Length));
        Assert.AreEqual(4, detail.HoveredField.Offset, "the table counts from the start of its descriptor");
    }

    [TestMethod]
    public void TheAgent_SeesAndWalksTheSameTree()
    {
        var vm = Page("1-2");
        var context = vm.GetContext();
        StringAssert.Contains(context, "USB page at '1-2'");
        StringAssert.Contains(context, "- 1-2.4 | LG Monitor Controls |");

        var tools = vm.GetClientTools().ToDictionary(t => t.Name);
        CollectionAssert.AreEquivalent(new[] { "usb_list", "usb_describe", "usb_find", "usb_open", "usb_reread" }, tools.Keys.ToArray());
        Assert.IsTrue(tools.Values.All(t => t.Safety == ToolSafety.SafeOperation));

        var list = Run(tools["usb_list"], new JsonObject { ["location"] = "1-2.4/config1" });
        StringAssert.Contains(list.ModelText, "- 1-2.4/config1/iad1 |");

        var describe = Run(tools["usb_describe"], new JsonObject { ["location"] = "1-2.4/config1/iad1/if1.0/ep0x83" });
        StringAssert.Contains(describe.ModelText, "+2 bEndpointAddress (1) = 0x83 (131) — EP 3 IN");
        StringAssert.Contains(describe.ModelText, "bytes: 07 05 83 03 0A 00 02");

        StringAssert.Contains(Run(tools["usb_find"], new JsonObject { ["query"] = "043e:9a39" }).ModelText, "- 1-2.4 |");
        Assert.IsTrue(Run(tools["usb_describe"], new JsonObject { ["location"] = "9-9" }).IsError);

        Run(tools["usb_open"], new JsonObject { ["location"] = "1-2.4/config1" });
        Assert.AreEqual("1-2.4/config1", vm.CurrentPath);

        Run(tools["usb_reread"], new JsonObject { ["location"] = "1-2.4/config1/if0.0" });
        CollectionAssert.AreEqual(new[] { UsbProbeSamples.LgId }, _inventory.ReRead, "anything inside a device re-reads that device");
    }

    [TestMethod]
    public void GreaterThan_GoesThere()
    {
        var vm = Page();
        var handler = new UsbProbeQueryHandler();
        Assert.AreEqual(1.0f, handler.CanProcess("lg", prefixed: true, vm));
        Assert.AreEqual(0.6f, handler.CanProcess("1-2.4", prefixed: false, vm), "a bare path is a place");
        Assert.AreEqual(0f, handler.CanProcess("what is this device", prefixed: false, vm), "bare words are for the agent");
        Assert.AreEqual(0f, handler.CanProcess("1-2.4", prefixed: true, pageVm: null), "only on the USB page");

        Assert.IsNull(handler.ProcessAsync("monitor", prefixed: true, vm).GetAwaiter().GetResult());
        Assert.AreEqual("1-2.4", vm.CurrentPath);
        Assert.IsNotNull(handler.ProcessAsync("nothing here", prefixed: true, vm).GetAwaiter().GetResult());
    }

    [TestMethod]
    public void Registration_IsCheap_AndLocationIsNotIdentity()
    {
        var registration = new UsbProbePageRegistration(new UsbProbeConfig(), [], _shell);
        Assert.IsFalse(registration.Parameters.Single(p => p.Name == UsbProbePageRegistration.LocationParam).Identity);

        var page = registration.CreatePageDefinition(new() { [UsbProbePageRegistration.LocationParam] = " USB:1-2.4/ " });
        Assert.IsNull(page.Content, "nothing is read until the tab is shown");
        Assert.AreEqual("1-2.4", page.PageParams?[UsbProbePageRegistration.LocationParam]);
        Assert.AreEqual(0, registration.CreatePageDefinition().PageParams?.Count);
    }

    [TestMethod]
    public void Closing_StopsFollowingTheMachine()
    {
        var vm = Page();
        vm.Dispose();
        Assert.IsTrue(_inventory.Disposed);
        _inventory.Become(UsbProbeSamples.Tree(UsbProbeSamples.Hub()));
        Assert.AreEqual("", vm.CurrentPath);
    }

    static ToolResult Run(IClientTool tool, JsonObject args) => tool.InvokeAsync(args, CancellationToken.None).GetAwaiter().GetResult();

    static IEnumerable<UsbTreeNode> Flatten(UsbTreeNode node) => node.Children.SelectMany(Flatten).Prepend(node);
}
