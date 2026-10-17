using System.Text.Json.Nodes;
using Nexaflow.IO.Usb.Diagnostics;
using Nexaflow.IO.Usb.Inventory;
using Nexaflow.IO.Usb.Windows;
using Nexaflow.Tests.Fixtures;
using static Nexaflow.Tests.IO.Usb.UsbFixtures;
using static Nexaflow.Tests.Fixtures.UsbSamples;

namespace Nexaflow.Tests.IO.Usb;

/// <summary>
/// A device's debug details, built from the LG monitor's captured descriptors: the bytes written back as the fixtures
/// hold them, serials hidden wherever they turn up, and the summary and each tool's answer in their places.
/// </summary>
[TestClass]
[CoversNode("io-usb-diagnostics")]
public class UsbDebugDetailsTests
{
    const string Serial = "902NTPC6R928";

    static JsonObject Details(UsbDeviceNode device, IReadOnlyList<UsbDebugTool>? tools = null) =>
        UsbDebugDetails.Build(device, UsbDebugMask.For(device), "1.2.3", "2026.10.01", tools ?? [],
            ["Interface 1 (CDC Data): nothing here works through it"], ["10:00:00 HID: read the layout"],
            [new UsbHidReportDescriptor(0, [0x06, 0x00, 0xFF], null), new UsbHidReportDescriptor(1, null, "GET_DESCRIPTOR (HID report): Win32 31")]);

    [TestMethod]
    public void TheDescriptors_AreWrittenAsTheFixturesHoldThem_SoADumpBecomesATest()
    {
        var descriptors = Details(LgMonitor())["device"]!["descriptors"]!;

        Assert.AreEqual(LgDevice, (string?)descriptors["device"]);
        CollectionAssert.AreEqual(Bytes(LgConfiguration), Bytes((string)descriptors["configurations"]![0]!));
        Assert.AreEqual((byte)1, (byte?)descriptors["activeConfiguration"]);
    }

    [TestMethod]
    public void TheSerial_IsHiddenEverywhere_AndHidesTheSameWayEachTime()
    {
        var device = LgMonitor() with
        {
            Functions = [.. LgMonitor().Functions.Select((f, i) => i == 0
                ? f with { Children = [new UsbChildNode($@"HID\VID_043E&PID_9A39&MI_00\{Serial}&0", "HIDClass", "HidUsb", "HID device")] }
                : f)],
        };
        string text = UsbDebugDetails.ToText(Details(device));

        Assert.IsFalse(text.Contains(Serial, StringComparison.OrdinalIgnoreCase), "the instance id's serial, and a child's copy of it, are hidden");
        string id = (string)Details(device)["device"]!["instanceId"]!;
        StringAssert.StartsWith(id, @"USB\VID_043E&PID_9A39\SERIAL-");
        Assert.AreEqual(id, (string?)Details(LgMonitor())["device"]!["instanceId"], "the same device reads as the same device");
    }

    [TestMethod]
    public void ASerialString_IsHidden_AndAGeneratedInstanceIdIsKept()
    {
        var device = LgMonitor() with
        {
            InstanceId = @"USB\VID_043E&PID_9A39\5&1A2B3C&0&4",
            Strings = LgMonitor().Strings with
            {
                SerialNumber = "AB12CD34",
                ByIndex = new Dictionary<byte, string> { [1] = "LG Electronics Inc.", [2] = "AB12CD34" },
            },
        };
        var details = Details(device);

        Assert.AreEqual(@"USB\VID_043E&PID_9A39\5&1A2B3C&0&4", (string?)details["device"]!["instanceId"], "Windows made it up: it names no device");
        StringAssert.StartsWith((string)details["device"]!["strings"]!["byIndex"]!["2"]!, "SERIAL-");
        Assert.AreEqual("LG Electronics Inc.", (string?)details["device"]!["strings"]!["byIndex"]!["1"]);
    }

    [TestMethod]
    public void APersonsName_IsAToken()
    {
        StringAssert.StartsWith(UsbDebugMask.Name("Holiday photos"), "NAME-");
        Assert.AreEqual(UsbDebugMask.Name("Holiday photos"), UsbDebugMask.Name("Holiday photos"));
        Assert.IsNull(UsbDebugMask.Name(null));
    }

    [TestMethod]
    public void ItSaysFirst_WhatTheDeviceIs_AndWhatEachToolSawOrWhyNot()
    {
        var details = Details(LgMonitor(),
        [
            new UsbDebugTool("usb.hid", "HID", @"USB\VID_043E&PID_9A39&MI_00\8&F264BB0&0&0000", "1 collection", new JsonObject { ["collections"] = 1 }, null),
            new UsbDebugTool("usb.serial", "Serial link", @"USB\VID_043E&PID_9A39&MI_01\8&F264BB0&0&0001", "COM5", null, "0x80004005 IOException: gone"),
        ]);
        var summary = details["summary"]!.AsArray().Select(l => (string)l!).ToList();

        Assert.AreEqual(UsbDebugDetails.Format, (int?)details["nexaflowUsbDebug"]);
        StringAssert.Contains(summary[0], "043E:9A39");
        StringAssert.Contains(summary[1], "interface 0: HID");
        Assert.IsTrue(summary.Contains("live tools: usb.hid, usb.serial (failed)"), string.Join("\n", summary));
        Assert.IsTrue(summary.Any(l => l.StartsWith("not covered: Interface 1")));

        Assert.AreEqual(1, (int?)details["tools"]![0]!["details"]!["collections"]);
        Assert.AreEqual("0x80004005 IOException: gone", (string?)details["tools"]![1]!["error"]);
        Assert.AreEqual("06 00 FF", (string?)details["hidReportDescriptors"]![0]!["bytes"]);
        StringAssert.Contains((string)details["hidReportDescriptors"]![1]!["problem"]!, "Win32 31");
        Assert.AreEqual("10:00:00 HID: read the layout", (string?)details["log"]![0]);
        Assert.AreEqual("usbser", (string?)details["device"]!["functions"]![1]!["driver"]!["service"]);
        Assert.AreEqual("COM5", (string?)details["device"]!["functions"]![1]!["comPort"]);
    }
}
