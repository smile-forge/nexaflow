using Nexaflow.IO.Usb.Browsing;
using Nexaflow.IO.Usb.Descriptors;
using Nexaflow.IO.Usb.Inventory;
using Nexaflow.Tests.Fixtures;
using static Nexaflow.Tests.IO.Usb.UsbFixtures;
using static Nexaflow.Tests.Fixtures.UsbSamples;

namespace Nexaflow.Tests.IO.Usb;

[TestClass]
[CoversNode("io-usb-browsing")]
public class UsbCapabilitySummaryTests
{
    [TestMethod]
    public void OneVerdictPerFunction()
    {
        var lines = UsbCapabilitySummary.Describe(LgMonitor());
        Assert.AreEqual(2, lines.Count);
        StringAssert.StartsWith(lines[0], "Interface 0 (HID): an input device");
        StringAssert.Contains(lines[1], "CDC ACM (serial) + CDC Data");
        StringAssert.Contains(lines[1], "COM5");
    }

    [TestMethod]
    public void RawAccessAndNoDriver()
    {
        var raw = UsbCapabilitySummary.Describe(LgMonitor(Driver("WINUSB", "winusb.inf")));
        StringAssert.Contains(raw[1], "generic USB driver (WINUSB), so a program can talk to it directly");

        var none = UsbCapabilitySummary.Describe(LgMonitor(Driver(null, null, problem: 28)));
        StringAssert.Contains(none[1], "no working driver for it (problem 28)");
    }

    [TestMethod]
    public void SleepingDeviceSaysSo()
    {
        var asleep = LgMonitor() with { Strings = UsbDeviceStrings.Unread, Power = UsbPowerState.D3 };
        Assert.IsTrue(UsbCapabilitySummary.Describe(asleep).Any(l => l.StartsWith("It was asleep")));
    }

    [TestMethod]
    public void AnEjectedDevice_SaysOnlyThat()
    {
        var ejected = LgMonitor() with { Driver = UsbDevNodeDriver.None with { Service = "usbccgp", ProblemCode = UsbProblemCodes.PreparedForRemoval } };
        Assert.IsTrue(ejected.IsEjected);
        StringAssert.StartsWith(UsbCapabilitySummary.Describe(ejected).Single(), "Ejected");
    }

    [TestMethod]
    public void AudioInputIsFoundInAnAlternateSetting()
    {
        var configuration = UsbDescriptorParser.ParseConfiguration(Bytes(Microphone), UsbSpeed.High);
        var mic = LgMonitor() with
        {
            Configurations = [configuration],
            Functions = [Function(@"USB\VID_046D&PID_0843&MI_03\B&CCF5BE1&0&0003", 3, configuration, Driver("usbaudio", "wdma_usb.inf"))],
        };
        Assert.AreEqual("Interface 3 (Audio Control + Audio Streaming): a microphone or audio input: watch its levels and record from it.", UsbCapabilitySummary.Describe(mic).Single());
    }
}
