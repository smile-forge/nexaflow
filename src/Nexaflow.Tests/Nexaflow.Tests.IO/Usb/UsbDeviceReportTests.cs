using System.Text.Json;
using Nexaflow.IO.Usb.Inventory;
using Nexaflow.IO.Usb.Reports;
using Nexaflow.Tests.Fixtures;
using static Nexaflow.Tests.IO.Usb.UsbFixtures;
using Nexaflow.IO.Usb.Descriptors;

namespace Nexaflow.Tests.IO.Usb;

[TestClass]
[CoversNode("io-usb-report")]
public class UsbDeviceReportTests
{
    [TestMethod]
    public void Json_FollowsTheSpecShape()
    {
        var json = UsbDeviceReports.Build(LgMonitor(), new DateTimeOffset(2026, 10, 16, 3, 0, 0, TimeSpan.FromHours(2))).ToJson();
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        Assert.AreEqual("nexaflow.usb.report/1", root.GetProperty("schema").GetString());
        var device = root.GetProperty("device");
        Assert.AreEqual("0x043E", device.GetProperty("vid").GetString());
        Assert.AreEqual("0x9A39", device.GetProperty("pid").GetString());
        Assert.AreEqual("2.00", device.GetProperty("usb").GetString());
        Assert.AreEqual("Full", device.GetProperty("speed").GetString());
        Assert.AreEqual("1-2.4", device.GetProperty("portPath").GetString());
        Assert.AreEqual("LG Monitor Controls", device.GetProperty("strings").GetProperty("product").GetString());

        var interfaces = root.GetProperty("configurations")[0].GetProperty("interfaces");
        Assert.AreEqual(3, interfaces.GetArrayLength());
        var acm = interfaces[1];
        Assert.AreEqual("0x02", acm.GetProperty("class").GetString());
        Assert.AreEqual("WindowsClass", acm.GetProperty("mode").GetString());
        Assert.AreEqual("usbser", acm.GetProperty("driver").GetProperty("service").GetString());
        Assert.AreEqual("COM5", acm.GetProperty("driver").GetProperty("comPort").GetString());
        Assert.AreEqual(interfaces[2].GetProperty("functionInstanceId").GetString(), acm.GetProperty("functionInstanceId").GetString(),
            "CDC data belongs to the function its association opened");
        Assert.AreEqual("0x83", acm.GetProperty("endpoints")[0].GetProperty("address").GetString());

        Assert.AreEqual(LgDevice, root.GetProperty("rawDescriptorsHex").GetProperty("device").GetString());
        Assert.IsFalse(root.GetProperty("msOsDescriptors").GetProperty("present").GetBoolean());
        Assert.IsFalse(json.Contains("\"serial\""), "null fields are left out");
    }

    [TestMethod]
    public void Json_RoundTrips()
    {
        var report = UsbDeviceReports.Build(LgMonitor(), DateTimeOffset.Now);
        var back = UsbDeviceReport.FromJson(report.ToJson());
        Assert.IsNotNull(back);
        Assert.AreEqual(report.Device.InstanceId, back.Device.InstanceId);
        Assert.AreEqual(report.Configurations[0].Interfaces.Count, back.Configurations[0].Interfaces.Count);
    }

    [TestMethod]
    public void Summary_OneVerdictPerFunction()
    {
        var lines = UsbCapabilitySummary.Describe(LgMonitor());
        Assert.AreEqual(2, lines.Count);
        StringAssert.StartsWith(lines[0], "Interface 0 — HID:");
        StringAssert.Contains(lines[1], "CDC ACM (serial) + CDC Data");
        StringAssert.Contains(lines[1], "COM5");
    }

    [TestMethod]
    public void Summary_RawAccessAndNoDriver()
    {
        var raw = UsbCapabilitySummary.Describe(LgMonitor(Driver("WINUSB", "winusb.inf")));
        StringAssert.Contains(raw[1], "raw access now through WINUSB");

        var none = UsbCapabilitySummary.Describe(LgMonitor(Driver(null, null, problem: 28)));
        StringAssert.Contains(none[1], "no driver (problem 28)");
    }

    [TestMethod]
    public void Summary_SleepingDeviceSaysSo()
    {
        var asleep = LgMonitor() with { Strings = UsbDeviceStrings.Unread, Power = UsbPowerState.D3 };
        Assert.IsTrue(UsbCapabilitySummary.Describe(asleep).Any(l => l.StartsWith("Asleep")));
    }

    [TestMethod]
    public void Hex_IsSpacedUppercase()
    {
        Assert.AreEqual("", UsbDeviceReports.Hex([]));
        Assert.AreEqual("0A FF 00", UsbDeviceReports.Hex([0x0A, 0xFF, 0x00]));
    }

    [TestMethod]
    public void Summary_AudioInputIsFoundInAnAlternateSetting()
    {
        var configuration = UsbDescriptorParser.ParseConfiguration(Bytes(Microphone), UsbSpeed.High);
        var mic = LgMonitor() with
        {
            Configurations = [configuration],
            Functions = [Function(@"USB\VID_046D&PID_0843&MI_03\B&CCF5BE1&0&0003", 3, configuration, Driver("usbaudio", "wdma_usb.inf"))],
        };
        Assert.AreEqual("Interface 3 — Audio Control + Audio Streaming: audio input — level meter and recording.", UsbCapabilitySummary.Describe(mic).Single());
    }
}
