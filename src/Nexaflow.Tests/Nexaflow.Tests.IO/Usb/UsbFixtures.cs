using Nexaflow.IO.Usb.Descriptors;
using Nexaflow.IO.Usb.Inventory;

namespace Nexaflow.Tests.IO.Usb;

/// <summary>
/// Descriptors captured from real devices by the Phase 0 spike (spikes/UsbDriverBinding), and devices built from them.
/// </summary>
internal static class UsbFixtures
{
    /// <summary>LG 27UP850 monitor controls (043E:9A39), full speed: HID on interface 0, CDC ACM on 1–2 grouped by an IAD.</summary>
    public const string LgDevice = "12 01 00 02 EF 02 01 40 3E 04 39 9A 17 04 01 03 04 01";
    public const string LgConfiguration =
        "09 02 6B 00 03 01 00 C0 00 09 04 00 00 02 03 00 00 05 09 21 11 01 00 01 22 21 00 07 05 81 03 40 00 01 07 05 01 03 40 00 01 " +
        "08 0B 01 02 02 02 00 05 09 04 01 00 01 02 02 00 08 05 24 00 10 01 05 24 01 01 02 04 24 02 02 05 24 06 01 02 07 05 83 03 0A 00 02 " +
        "09 04 02 00 02 0A 00 00 08 07 05 02 02 40 00 00 07 05 82 02 40 00 00";

    /// <summary>ASUS motherboard LCD panel (0B05:1C83), high speed, a WCID device: vendor interface 0 on two bulk pipes, HID on 1.</summary>
    public const string AsusLcdConfiguration =
        "09 02 40 00 02 01 04 E0 FA 09 04 00 00 02 FF 00 00 00 07 05 02 02 00 02 00 07 05 82 02 00 02 00 " +
        "09 04 01 00 02 03 00 00 00 09 21 11 01 00 01 22 33 00 07 05 81 03 40 00 01 07 05 01 03 40 00 01";

    /// <summary>TI TUSB8041 hub (0451:8142) BOS: USB 2.0 extension with LPM, SuperSpeed (Full/High/5 Gbps), container id.</summary>
    /// <summary>
    /// A webcam microphone as a Logitech C930e lays it out: an IAD over Audio Control 2 and Audio Streaming 3, whose
    /// isochronous IN endpoint exists only in alternate setting 1 — and Windows names the function MI_03.
    /// </summary>
    public const string Microphone =
        "09 02 33 00 02 01 00 80 32 08 0B 02 02 01 01 00 00 09 04 02 00 00 01 01 00 00 " +
        "09 04 03 00 00 01 02 00 00 09 04 03 01 01 01 02 00 00 07 05 83 05 44 00 04";

    public const string TiHubBos =
        "05 0F 2A 00 03 07 10 02 06 00 00 00 0A 10 03 00 0E 00 01 0A 0A 00 14 10 04 00 02 40 CE D0 04 9A D0 5D 0E 17 27 C2 B6 C9 D0 00";

    /// <summary>Synthetic BOS carrying the MS OS 2.0 and WebUSB platform capabilities, as a TinyUSB device writes them.</summary>
    public const string PlatformBos =
        "05 0F 39 00 02 " +
        "1C 10 05 00 DF 60 DD D8 89 45 C7 4C 9C D2 65 9D 9E 64 8A 9F 00 00 03 06 B2 00 01 00 " +
        "18 10 05 00 38 B6 08 34 A9 09 A0 47 8B FD A0 76 88 15 B6 65 00 01 02 01";

    public static byte[] Bytes(string hex) => Convert.FromHexString(hex.Replace(" ", ""));

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
}
