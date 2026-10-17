using Nexaflow.IO.Usb.Binding;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.IO.Usb;

[TestClass]
[CoversNode("io-usb-takeover-policy")]
public class UsbBindingPolicyTests
{
    const string Serial = @"USB\VID_043E&PID_9A39&MI_01\8&F264BB0&0&0001";

    static UsbBindingCandidate Candidate(string? service, string? cls = null, string id = Serial, bool present = true, params string[] children) =>
        new(id, present, service, cls, "usbccgp", children);

    [TestMethod]
    public void SerialInterfaceOfAComposite_MayBeTakenOver()
    {
        Assert.IsNull(UsbBindingPolicy.RefusalReason(Candidate("usbser", "Ports")));
        Assert.IsNull(UsbBindingPolicy.RefusalReason(Candidate(null)), "a driverless function is the main target");
        Assert.IsNull(UsbBindingPolicy.RefusalReason(Candidate("RtkUsbAD_2433", "MEDIA")), "a vendor driver may be swapped and restored");
    }

    [TestMethod]
    [DataRow("usbccgp", "composite")]
    [DataRow("USBHUB3", "hub")]
    [DataRow("USBSTOR", "mass storage")]
    [DataRow("UASPStor", "mass storage")]
    [DataRow("BTHUSB", "Bluetooth")]
    [DataRow("WINUSB", "already on WinUSB")]
    [DataRow("libusbK", "already on a generic driver")]
    public void RefusedServices(string service, string expected) =>
        StringAssert.Contains(UsbBindingPolicy.RefusalReason(Candidate(service)), expected);

    [TestMethod]
    public void InputDevicesAndDisks_AreRefusedByWhatTheyBack()
    {
        StringAssert.Contains(UsbBindingPolicy.RefusalReason(Candidate("HidUsb", "HIDClass", Serial, true, "Keyboard")), "keyboard");
        StringAssert.Contains(UsbBindingPolicy.RefusalReason(Candidate("HidUsb", "HIDClass", Serial, true, "HIDClass", "Mouse")), "mouse");
        StringAssert.Contains(UsbBindingPolicy.RefusalReason(Candidate("VendorStor", null, Serial, true, "DiskDrive")), "disk");
        Assert.IsNull(UsbBindingPolicy.RefusalReason(Candidate("HidUsb", "HIDClass", Serial, true, "HIDClass")), "a HID that is no keyboard or pointer");
    }

    [TestMethod]
    public void NotUsbNotPresentOrRootHub_AreRefused()
    {
        StringAssert.Contains(UsbBindingPolicy.RefusalReason(Candidate("usbser", id: @"PCI\VEN_8086&DEV_A36D\3&11583659&0&A0")), "not a USB device");
        StringAssert.Contains(UsbBindingPolicy.RefusalReason(Candidate("usbser", present: false)), "not plugged in");
        StringAssert.Contains(UsbBindingPolicy.RefusalReason(Candidate(null, id: @"USB\ROOT_HUB30\5&239A9E6D&0&0")), "root hub");
        StringAssert.Contains(UsbBindingPolicy.RefusalReason(Candidate("SomeBtFilter", "Bluetooth")), "Bluetooth");
    }
}
