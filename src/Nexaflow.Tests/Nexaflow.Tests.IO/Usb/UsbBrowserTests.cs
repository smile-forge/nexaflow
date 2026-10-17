using Nexaflow.IO.Usb.Browsing;
using Nexaflow.IO.Usb.Descriptors;
using Nexaflow.IO.Usb.Inventory;
using Nexaflow.Tests.Fixtures;
using static Nexaflow.Tests.IO.Usb.UsbFixtures;

namespace Nexaflow.Tests.IO.Usb;

[TestClass]
[CoversNode("io-usb-browsing")]
public class UsbBrowserTests
{
    static UsbBrowser Browser(UsbInventorySnapshot? snapshot = null) => new(snapshot ?? Tree(), new UsbDescriptorMapCache(null));

    static string Paths(IEnumerable<UsbLocation> locations) => string.Join(" ", locations.Select(l => l.Path));

    [TestMethod]
    public void Topology_IsBusThenPortsThenDevices()
    {
        var browser = Browser();
        var root = browser.Root;
        Assert.AreEqual("1", Paths(browser.Children(root)));

        var bus = browser.Resolve("1")!;
        Assert.AreEqual(UsbLocationKind.Bus, bus.Kind);
        Assert.AreEqual("Intel(R) USB 3.20 eXtensible Host Controller", bus.Name);
        Assert.AreEqual("Bus 1 · 4 port(s), 1 in use", bus.Summary);

        var ports = browser.Children(bus);
        Assert.AreEqual("1-1 1-2 1-3 1-4", Paths(ports));
        Assert.AreEqual(UsbLocationKind.Device, ports[1].Kind);
        Assert.AreEqual("empty · USB-C", ports[2].Summary);
        Assert.AreEqual("empty · internal", ports[3].Summary);

        var hub = browser.Resolve("1-2")!;
        Assert.AreEqual("1-2.1 1-2.2 1-2.3 1-2.4 1-2/config1", Paths(browser.Children(hub)), "a hub holds its ports, then its own configuration");
        Assert.AreEqual("1-2.4/config1", Paths(browser.Children(browser.Resolve("1-2.4")!)));
    }

    [TestMethod]
    public void InsideADevice_PathsFollowTheDescriptors()
    {
        var browser = Browser();
        var config = browser.Resolve("1-2.4/config1")!;
        Assert.AreEqual("Configuration 1 (active)", config.Name);
        Assert.AreEqual("1-2.4/config1/if0.0 1-2.4/config1/iad1", Paths(browser.Children(config)));

        var endpoint = browser.Resolve("1-2.4/config1/iad1/if1.0/ep0x83")!;
        Assert.AreEqual(UsbLocationKind.Descriptor, endpoint.Kind);
        Assert.AreEqual("Endpoint 0x83", endpoint.Name);
        Assert.AreEqual(@"USB\VID_043E&PID_9A39&MI_01\8&F264BB0&0&0001", endpoint.Function?.InstanceId, "an endpoint belongs to its interface's function");

        var shortcut = browser.Resolve("USB:1-2.4/CONFIG1/if1.0")!;
        Assert.AreEqual("1-2.4/config1/iad1/if1.0", shortcut.Path, "an interface an association holds is found without naming the association");
        Assert.AreEqual("Interface 1, alt 0", shortcut.Name);

        var data = browser.Resolve("1-2.4/config1/iad1/if2.0")!;
        Assert.AreEqual(@"USB\VID_043E&PID_9A39&MI_01\8&F264BB0&0&0001", data.Function?.InstanceId, "CDC data is in the serial function its association opened");
    }

    [TestMethod]
    public void Paths_ClimbAndFallBack()
    {
        Assert.AreEqual("1-2", UsbLocationPath.Parent("1-2.4"));
        Assert.AreEqual("1", UsbLocationPath.Parent("1-2"));
        Assert.AreEqual("", UsbLocationPath.Parent("1"));
        Assert.IsNull(UsbLocationPath.Parent(""));
        Assert.AreEqual("1-2.4/config1", UsbLocationPath.Parent("1-2.4/config1/if0.0"));
        CollectionAssert.AreEqual(new[] { "", "1", "1-2", "1-2.4", "1-2.4/config1" }, UsbLocationPath.Lineage("1-2.4/config1").ToArray());

        var browser = Browser();
        Assert.IsNull(browser.Resolve("1-9"));
        Assert.IsNull(browser.Resolve("2"));
        Assert.AreEqual("1-2", browser.NearestExisting("1-2.7/config1").Path);
        Assert.AreEqual(UsbLocationKind.Port, browser.Resolve("1-1")!.Kind);
    }

    [TestMethod]
    public void Find_MatchesEveryWordAnywhereADeviceNamesItself()
    {
        var browser = Browser();
        Assert.AreEqual("1-2.4", Paths(browser.Find("043e:9a39")));
        Assert.AreEqual("1-2.4", Paths(browser.Find("COM5")));
        Assert.AreEqual("1-2.4", Paths(browser.Find("lg monitor")));
        Assert.AreEqual("1-2.4", Paths(browser.Find("usbser")));
        Assert.AreEqual("1-2", Paths(browser.Find("1-2")), "a path is itself");
        Assert.AreEqual("", Paths(browser.Find("logitech")));
    }

    [TestMethod]
    public void Facts_SayWhatTheListingHasNoRoomFor()
    {
        var browser = Browser();
        var device = UsbLocationFacts.Of(browser.Resolve("1-2.4")!);
        CollectionAssert.IsSubsetOf(new[] { "Identity", "Connection", "What it is", "Microsoft OS descriptors", "Driver", "Functions" }, device.Select(g => g.Title).ToArray());
        Assert.AreEqual("043E:9A39", Fact(device, "Identity", "VID:PID"));
        Assert.AreEqual("1-2.4", Fact(device, "Connection", "Port path"));
        StringAssert.Contains(Fact(device, "Functions", "Interface 1"), "COM5");

        var serial = UsbLocationFacts.Of(browser.Resolve("1-2.4/config1/if1.0")!).Single();
        Assert.AreEqual("Windows function", serial.Title);
        Assert.AreEqual("usbser", Fact([serial], "Windows function", "Service"));
        Assert.AreEqual("COM5", Fact([serial], "Windows function", "COM port"));

        var bus = UsbLocationFacts.Of(browser.Resolve("1")!);
        Assert.AreEqual("USBXHCI", Fact(bus, "Host controller", "Service"));
        Assert.AreEqual(0, UsbLocationFacts.Of(browser.Resolve("1-2.4/config1/iad1/if1.0/ep0x83")!).Count, "an endpoint is all descriptor");
    }

    [TestMethod]
    public void Maps_AreBuiltOncePerBlob()
    {
        var maps = new UsbDescriptorMapCache(null);
        var snapshot = Tree();
        var first = new UsbBrowser(snapshot, maps).Resolve("1-2.4/config1")!.Map;
        var again = new UsbBrowser(snapshot, maps).Resolve("1-2.4/config1")!.Map;
        Assert.AreSame(first, again);
    }

    static string Fact(IReadOnlyList<UsbFactGroup> groups, string group, string label) =>
        groups.Single(g => g.Title == group).Facts.Single(f => f.Label == label).Value;
}
