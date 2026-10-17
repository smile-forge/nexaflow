using Nexaflow.IO.Usb.Descriptors;
using Nexaflow.IO.Usb.Inventory;
using static Nexaflow.Tests.Fixtures.UsbSamples;

namespace Nexaflow.Tests.IO.Usb;

/// <summary>
/// Devices built from the captured descriptors in <see cref="Nexaflow.Tests.Fixtures.UsbSamples"/>, as the inventory would report them.
/// </summary>
internal static class UsbFixtures
{
    public static UsbDevNodeDriver Driver(string? service, string? inf = null, uint problem = 0) =>
        UsbDevNodeDriver.None with { Service = service, InfPath = inf, ProblemCode = problem };

    public static UsbFunctionNode Function(string instanceId, byte? mi, UsbConfiguration configuration, UsbDevNodeDriver driver,
        string? comPort = null, params UsbChildNode[] children) =>
        new(instanceId, UsbFunctionLayout.InterfacesOf(mi, configuration), null, driver, UsbFunctionLayout.Classify(driver.Service, driver.InfPath),
            UsbPowerState.D0, [], comPort, children) { FunctionInterface = mi };

    /// <summary>The LG monitor as the inventory sees it with its inbox drivers: HidUsb on MI_00, usbser (COM5) on MI_01.</summary>
    public static UsbDeviceNode LgMonitor(UsbDevNodeDriver? serialDriver = null)
    {
        Assert.IsTrue(UsbDeviceDescriptor.TryParse(Bytes(LgDevice), out var descriptor));
        var configuration = UsbDescriptorParser.ParseConfiguration(Bytes(LgConfiguration), UsbSpeed.Full);
        const string root = @"USB\VID_043E&PID_9A39\902NTPC6R928";
        var functions = new[]
        {
            Function(@"USB\VID_043E&PID_9A39&MI_00\8&F264BB0&0&0000", 0, configuration, Driver("HidUsb", "input.inf")),
            Function(@"USB\VID_043E&PID_9A39&MI_01\8&F264BB0&0&0001", 1, configuration, serialDriver ?? Driver("usbser", "usbser.inf"), "COM5"),
        };
        var strings = new UsbDeviceStrings("LG Electronics Inc.", "LG Monitor Controls", null,
            new Dictionary<byte, string> { [1] = "LG Electronics Inc.", [3] = "LG Monitor Controls" }, [0x0409], true);
        return new UsbDeviceNode(root, new UsbPortLocation(@"USB\VID_0451&PID_8142\MSFT20C303007916F0", 4, 1, [2, 4], true, false),
            descriptor, Bytes(LgDevice), [configuration], 1, null, strings, UsbSpeed.Full, false, false, UsbPowerState.D0,
            Driver("usbccgp", "usb.inf"), functions, [], new UsbMsOsSupport(false, null, null, null), "LG Monitor Controls", []);
    }

    public const string RootHubId = @"USB\ROOT_HUB30\4&2C1A5B0&0&0";
    public const string HubId = @"USB\VID_0451&PID_8142\MSFT20C303007916F0";
    /// <summary>
    /// Bus 1 as a tree: a root hub with four ports — one internal, one USB-C — the TI hub on port 2, and the LG
    /// monitor on the hub's port 4 (port path 1-2.4). <paramref name="more"/> adds devices beside them.
    /// </summary>
    public static UsbInventorySnapshot Tree(params UsbDeviceNode[] more)
    {
        Assert.IsTrue(UsbDeviceDescriptor.TryParse(Bytes(HubDevice), out var hubDescriptor));
        var hubConfiguration = UsbDescriptorParser.ParseConfiguration(Bytes(HubConfiguration), UsbSpeed.High);
        var hub = LgMonitor() with
        {
            InstanceId = HubId,
            Location = new UsbPortLocation(RootHubId, 2, 1, [2], true, false),
            Descriptor = hubDescriptor,
            RawDeviceDescriptor = Bytes(HubDevice),
            Configurations = [hubConfiguration],
            IsHub = true,
            Speed = UsbSpeed.High,
            Strings = UsbDeviceStrings.Unread,
            Driver = Driver("USBHUB3", "usbhub3.inf"),
            Functions = [Function(HubId, null, hubConfiguration, Driver("USBHUB3", "usbhub3.inf"))],
            BusReportedName = "USB2.0 Hub",
        };
        var root = new UsbHubInfo(RootHubId, "Root hub", "USB Root Hub (USB 3.0)", 1, 4,
            [new(1, false, true, false), new(2, true, true, false), new(3, false, true, true), new(4, false, false, false)],
            new UsbHostController(@"PCI\VEN_8086&DEV_7AE0\3&11583659&0&A0", "Intel(R) USB 3.20 eXtensible Host Controller", Driver("USBXHCI", "usbxhci.inf")),
            null);
        var external = new UsbHubInfo(HubId, "USB 2.0 hub", "Generic USB Hub", 1, 4,
            [new(1, false, true, false), new(2, false, true, false), new(3, false, true, false), new(4, true, true, false)], null, null);
        return new UsbInventorySnapshot(DateTimeOffset.Now, [hub, LgMonitor(), .. more], [root, external], []);
    }
}
