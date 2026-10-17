using Nexaflow.IO.Usb.Descriptors;
using Nexaflow.IO.Usb.Hid;
using Nexaflow.IO.Usb.Inventory;
using Nexaflow.IO.Usb.Naming;
using Nexaflow.IO.Usb.Tools;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.IO.Usb;

/// <summary>
/// What every tool provider leans on: which class a function is, what to say about one no tool covers, and how HID
/// usages are named.
/// </summary>
[TestClass]
[CoversNode("io-usb-tools")]
public class UsbToolSupportTests
{
    static UsbDeviceNode Lg() => UsbFixtures.LgMonitor();

    [TestMethod]
    public void AFunctionsClass_IsItsInterfaces()
    {
        var lg = Lg();
        var hid = lg.Functions[0];

        Assert.IsTrue(lg.InterfacesOf(hid).Count >= 1);
        Assert.IsTrue(lg.InterfacesOf(hid).All(s => hid.InterfaceNumbers.Contains(s.Number)), "only the interfaces the function owns");
        var cls = lg.ClassOf(hid);
        Assert.AreEqual(lg.InterfacesOf(hid)[0].Class, cls.Class);
        Assert.AreEqual(UsbClassCodes.Name(cls.Class, cls.SubClass, cls.Protocol), cls.Name);
        Assert.IsTrue(lg.Speaks(hid, cls.Class));
        Assert.IsFalse(lg.Speaks(hid, UsbClassCodes.MassStorage));
    }

    [TestMethod]
    public void Guidance_SaysWhatATool_WouldUse()
    {
        StringAssert.Contains(UsbClassGuidance.For(new(UsbClassCodes.Image, 1, 1), UsbFunctionMode.WindowsClass), "choose File transfer",
            "an MTP function no tool covers is one Windows has not made a portable device of yet");
        StringAssert.Contains(UsbClassGuidance.For(new(UsbClassCodes.Video, 1, 0), UsbFunctionMode.WindowsClass), "Media Foundation");
        StringAssert.Contains(UsbClassGuidance.For(new(UsbClassCodes.SmartCard, 0, 0), UsbFunctionMode.WindowsClass), "PC/SC");
        StringAssert.Contains(UsbClassGuidance.For(new(UsbClassCodes.CdcControl, 0x0D, 0), UsbFunctionMode.WindowsClass), "Network page");
        StringAssert.Contains(UsbClassGuidance.For(new(UsbClassCodes.VendorSpecific, 0, 0), UsbFunctionMode.VendorDriver), "Raw access");
    }

    [TestMethod]
    public void Guidance_PutsTheDriverFirst()
    {
        var video = new UsbClassTriple(UsbClassCodes.Video, 1, 0);
        StringAssert.Contains(UsbClassGuidance.For(video, UsbFunctionMode.NoDriver), "no driver", "nothing can use a function Windows has no driver for, whatever its class");
        StringAssert.Contains(UsbClassGuidance.For(video, UsbFunctionMode.GenericRaw), "raw access reaches it");
    }

    [TestMethod]
    public void HidFields_ReadAsAPersonNamesThem()
    {
        var names = UsbIdsDatabase.LoadBundled();
        Assert.AreEqual("Generic Desktop Controls › Mouse", HidNames.Collection(names, 0x01, 0x02));
        Assert.AreEqual("Buttons 1–5", HidNames.Field(names, HidNames.ButtonPage, 1, 5, isButton: true));
        Assert.AreEqual("Button 3", HidNames.Field(names, HidNames.ButtonPage, 3, 3, isButton: true));
        Assert.AreEqual(names!.UsageName(0x01, 0x30), HidNames.Field(names, 0x01, 0x30, 0x30, isButton: false));
        StringAssert.Contains(HidNames.Field(names, 0xFF00, 0x01, 0x01, isButton: false), "Vendor");
        Assert.AreEqual("Usage 0xEE", HidNames.Usage(null, 0x01, 0xEE), "without usb.ids a usage is its number");
    }
}
