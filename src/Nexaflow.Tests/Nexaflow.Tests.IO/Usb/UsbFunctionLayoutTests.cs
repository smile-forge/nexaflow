using Nexaflow.IO.Usb.Descriptors;
using Nexaflow.IO.Usb.Inventory;
using Nexaflow.Tests.Fixtures;
using static Nexaflow.Tests.IO.Usb.UsbFixtures;

namespace Nexaflow.Tests.IO.Usb;

[TestClass]
[CoversNode("io-usb-inventory")]
public class UsbFunctionLayoutTests
{
    static UsbConfiguration Lg => UsbDescriptorParser.ParseConfiguration(Bytes(LgConfiguration), UsbSpeed.Full);

    [TestMethod]
    public void Interfaces_AnAssociationIsOneFunction()
    {
        CollectionAssert.AreEqual(new byte[] { 1, 2 }, UsbFunctionLayout.InterfacesOf(1, Lg).ToArray(), "MI_01 opens the CDC association");
        CollectionAssert.AreEqual(new byte[] { 0 }, UsbFunctionLayout.InterfacesOf(0, Lg).ToArray());
        CollectionAssert.AreEqual(new byte[] { 0, 1, 2 }, UsbFunctionLayout.InterfacesOf(null, Lg).ToArray(), "a non-composite device owns them all");
        CollectionAssert.AreEqual(new byte[] { 3 }, UsbFunctionLayout.InterfacesOf(3, null).ToArray(), "no configuration read: the MI number alone");
    }

    [TestMethod]
    public void Mode_ByServiceThenByWhereTheInfCameFrom()
    {
        Assert.AreEqual(UsbFunctionMode.NoDriver, UsbFunctionLayout.Classify(null, null));
        Assert.AreEqual(UsbFunctionMode.CompositeParent, UsbFunctionLayout.Classify("usbccgp", "usb.inf"));
        Assert.AreEqual(UsbFunctionMode.GenericRaw, UsbFunctionLayout.Classify("WINUSB", "winusb.inf"));
        Assert.AreEqual(UsbFunctionMode.GenericRaw, UsbFunctionLayout.Classify("WinUSB", "oem44.inf"), "a Zadig-style INF is still WinUSB");
        Assert.AreEqual(UsbFunctionMode.WindowsClass, UsbFunctionLayout.Classify("usbvideo", "oem62.inf"), "a vendor INF naming the inbox class driver");
        Assert.AreEqual(UsbFunctionMode.WindowsClass, UsbFunctionLayout.Classify("mshidkmdf", "machine.inf"), "an inbox INF");
        Assert.AreEqual(UsbFunctionMode.VendorDriver, UsbFunctionLayout.Classify("RtkUsbAD_2433", @"C:\Windows\INF\oem114.inf"));
    }

    [TestMethod]
    public void FunctionInterface_FromTheInstanceId()
    {
        Assert.AreEqual((byte)0x01, UsbFunctionLayout.FunctionInterfaceOf(@"USB\VID_043E&PID_9A39&MI_01\8&F264BB0&0&0001"));
        Assert.AreEqual((byte)0x0A, UsbFunctionLayout.FunctionInterfaceOf(@"USB\VID_1234&PID_0001&MI_0A\X"));
        Assert.IsNull(UsbFunctionLayout.FunctionInterfaceOf(@"USB\VID_046D&PID_C332&LAMPARRAY\B&38EB521D&0&SLOT00"));
        Assert.IsNull(UsbFunctionLayout.FunctionInterfaceOf(@"USB\VID_058F&PID_6387\18052507001672"));
    }

    [TestMethod]
    public void Interfaces_AFunctionNamedAfterALaterInterfaceOwnsItsAssociation() =>
        CollectionAssert.AreEqual(new byte[] { 2, 3 }, UsbFunctionLayout.InterfacesOf(3, UsbDescriptorParser.ParseConfiguration(Bytes(Microphone), UsbSpeed.High)).ToArray());
}
