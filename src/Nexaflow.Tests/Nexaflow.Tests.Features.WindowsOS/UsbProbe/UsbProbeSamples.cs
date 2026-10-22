using Nexaflow.IO.Usb.Descriptors;
using Nexaflow.IO.Usb.Inventory;
using Nexaflow.IO.Usb.Naming;
using static Nexaflow.Tests.Fixtures.UsbSamples;

namespace Nexaflow.Tests.Features.UsbProbe;

/// <summary>
/// A machine for the page to browse — bus 1, the TI hub on its port 2, the LG monitor on the hub's port 4 — and
/// an inventory that changes only when a test says so.
/// </summary>
internal static class UsbProbeSamples
{
    public const string RootHubId = @"USB\ROOT_HUB30\4&2C1A5B0&0&0";
    public const string HubId = @"USB\VID_0451&PID_8142\MSFT20C303007916F0";
    public const string LgId = @"USB\VID_043E&PID_9A39\902NTPC6R928";

    static UsbDevNodeDriver Driver(string? service, string? inf = null) => UsbDevNodeDriver.None with { Service = service, InfPath = inf };

    static UsbFunctionNode Function(string id, byte? mi, UsbConfiguration configuration, UsbDevNodeDriver driver, string? comPort = null) =>
        new(id, UsbFunctionLayout.InterfacesOf(mi, configuration), null, driver, UsbFunctionLayout.Classify(driver.Service, driver.InfPath),
            UsbPowerState.D0, [], comPort, []) { FunctionInterface = mi };

    static UsbDeviceNode Device(string id, string hub, int port, int[] chain, string device, string configuration, UsbSpeed speed,
        bool isHub, UsbDeviceStrings strings, Func<UsbConfiguration, UsbFunctionNode[]> functions)
    {
        if (!UsbDeviceDescriptor.TryParse(Bytes(device), out var descriptor)) throw new InvalidOperationException("bad sample");
        var parsed = UsbDescriptorParser.ParseConfiguration(Bytes(configuration), speed);
        return new UsbDeviceNode(id, new UsbPortLocation(hub, port, 1, chain, true, false), descriptor, Bytes(device), [parsed], 1, null,
            strings, speed, false, isHub, UsbPowerState.D0, Driver(isHub ? "USBHUB3" : "usbccgp"), functions(parsed), [],
            new UsbMsOsSupport(false, null, null, null), null, []);
    }

    public static UsbDeviceNode Hub() => Device(HubId, RootHubId, 2, [2], HubDevice, HubConfiguration, UsbSpeed.High, true,
        UsbDeviceStrings.Unread, c => [Function(HubId, null, c, Driver("USBHUB3"))]);

    public static UsbDeviceNode Lg() => Device(LgId, HubId, 4, [2, 4], LgDevice, LgConfiguration, UsbSpeed.Full, false,
        new UsbDeviceStrings("LG Electronics Inc.", "LG Monitor Controls", null, new Dictionary<byte, string> { [1] = "LG Electronics Inc.", [3] = "LG Monitor Controls" }, [0x0409], true),
        c => [Function(LgId.Replace("9A39", "9A39&MI_00"), 0, c, Driver("HidUsb", "input.inf")), Function(LgId.Replace("9A39", "9A39&MI_01"), 1, c, Driver("usbser", "usbser.inf"), "COM5")]);

    /// <summary>A second LG-shaped device on the hub's port 1, for arrivals.</summary>
    public static UsbDeviceNode Newcomer() => Lg() with
    {
        InstanceId = @"USB\VID_043E&PID_9A39\NEWCOMER",
        Location = new UsbPortLocation(HubId, 1, 1, [2, 1], true, false),
        Strings = UsbDeviceStrings.Unread,
    };

    public const string MicrophoneId = @"USB\VID_0D8C&PID_0014\MIC";
    public const string MicrophoneFunction = @"USB\VID_0D8C&PID_0014&MI_02\8&1&0&0002";

    /// <summary>A USB microphone on the hub's port 3: one audio function over interfaces 2 and 3, joined by an association.</summary>
    public static UsbDeviceNode UsbMicrophone() => Device(MicrophoneId, HubId, 3, [2, 3], LgDevice, Nexaflow.Tests.Fixtures.UsbSamples.Microphone, UsbSpeed.Full, false,
        UsbDeviceStrings.Unread, c => [Function(MicrophoneFunction, 2, c, Driver("usbaudio2", "wdma_usb.inf"))]);

    public const string StickId = @"USB\VID_0781&PID_5581\4C530001";

    /// <summary>A USB stick on the hub's port 1: one function, the whole device, that Windows made a disk drive of.</summary>
    public static UsbDeviceNode Stick() => Device(StickId, HubId, 1, [2, 1], LgDevice, LgConfiguration, UsbSpeed.High, false,
        UsbDeviceStrings.Unread, c => [Function(StickId, null, c, Driver("USBSTOR", "usbstor.inf")) with
        {
            Children = [new UsbChildNode(@"USBSTOR\DISK&VEN_SANDISK&PROD_ULTRA\4C530001&0", "DiskDrive", "disk", "SanDisk Ultra USB Device")],
        }]);

    public const string GadgetId = @"USB\VID_CAFE&PID_0001\GADGET1";

    /// <summary>A vendor-defined gadget's device descriptor: VID CAFE, PID 0001, class FF.</summary>
    public const string GadgetDevice = "12 01 00 02 FF 00 00 40 FE CA 01 00 00 01 01 02 03 01";

    /// <summary>One vendor-defined interface with a bulk endpoint each way, 0x81 IN and 0x01 OUT.</summary>
    const string GadgetConfiguration = "09 02 20 00 01 01 00 80 32 09 04 00 00 02 FF 00 00 00 07 05 81 02 40 00 00 07 05 01 02 40 00 00";

    /// <summary>A vendor-defined gadget on the hub's port 3, one function that is the whole device: with no driver, or on
    /// the generic driver given — WinUSB with the device interface it registers.</summary>
    public static UsbDeviceNode Gadget(string? service = null)
    {
        var driver = Driver(service, service is null ? null : "winusb.inf");
        var gadget = Device(GadgetId, HubId, 3, [2, 3], GadgetDevice, GadgetConfiguration, UsbSpeed.High, false, UsbDeviceStrings.Unread,
            c => [Function(GadgetId, null, c, driver) with { DeviceInterfaceGuids = service is null ? [] : [new Guid("dee824ef-729b-4a0e-9c14-b7117d33a817")] }]);
        return gadget with { Driver = driver };
    }

    public static UsbInventorySnapshot Tree(params UsbDeviceNode[] devices)
    {
        var connected = devices.Select(d => d.Location.PortPath).ToHashSet();
        var root = new UsbHubInfo(RootHubId, "Root hub", "USB Root Hub (USB 3.0)", 1, 4,
            [.. Enumerable.Range(1, 4).Select(p => new UsbHubPort(p, connected.Contains($"1-{p}"), true, false))],
            new UsbHostController(@"PCI\VEN_8086&DEV_7AE0\3&11583659&0&A0", "Intel(R) USB 3.20 eXtensible Host Controller", Driver("USBXHCI")), null);
        var hub = new UsbHubInfo(HubId, "USB 2.0 hub", "Generic USB Hub", 1, 4,
            [.. Enumerable.Range(1, 4).Select(p => new UsbHubPort(p, connected.Contains($"1-2.{p}"), true, false))], null, null);
        return new UsbInventorySnapshot(DateTimeOffset.Now, devices, [root, hub], []);
    }

    public static UsbInventorySnapshot Machine() => Tree(Hub(), Lg());
}

/// <summary>An inventory a test drives: it holds a snapshot and raises a change when told to.</summary>
internal sealed class FakeUsbInventory(UsbInventorySnapshot snapshot) : IUsbDeviceInventory
{
    public UsbInventorySnapshot Snapshot { get; private set; } = snapshot;
    public event EventHandler<UsbInventoryChangedEventArgs>? Changed;
    public bool Disposed { get; private set; }
    public List<string> ReRead { get; } = [];

    public Task StartAsync(CancellationToken ct) => Task.CompletedTask;
    public Task<UsbInventorySnapshot> RefreshAsync(CancellationToken ct) => Task.FromResult(Snapshot);

    public Task<UsbDeviceNode?> RefreshDeviceAsync(string instanceId, CancellationToken ct)
    {
        ReRead.Add(instanceId);
        return Task.FromResult(Snapshot.Find(instanceId));
    }

    public Task<UsbInventorySnapshot> UseNamesAsync(UsbIdsDatabase? names, CancellationToken ct) => Task.FromResult(Snapshot);

    public void Become(UsbInventorySnapshot next)
    {
        var changes = UsbInventoryDiff.Between(Snapshot, next);
        Snapshot = next;
        Changed?.Invoke(this, new UsbInventoryChangedEventArgs(next, changes));
    }

    public void Dispose() => Disposed = true;
}
