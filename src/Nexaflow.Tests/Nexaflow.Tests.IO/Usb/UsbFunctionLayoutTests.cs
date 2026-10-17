using Nexaflow.IO.Usb.Descriptors;
using Nexaflow.IO.Usb.Inventory;
using Nexaflow.Tests.Fixtures;
using static Nexaflow.Tests.IO.Usb.UsbFixtures;
using static Nexaflow.Tests.Fixtures.UsbSamples;

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
    public void AVendorsCompositeDriver_SplitsTheDeviceToo()
    {
        // Samsung's dg_ssudbus names its functions by what they are, not MI_xx, and numbers them as usbccgp does.
        const string phone = @"USB\VID_04E8&PID_6860\RFCWC16VHEV";
        string[] functions =
        [
            @"USB\VID_04E8&PID_6860&MS_COMP_MTP&SAMSUNG_Android\8&130557e0&2&0000",
            @"USB\VID_04E8&PID_6860&Modem\8&130557e0&2&0001",
        ];
        Assert.IsTrue(UsbFunctionLayout.SplitsIntoFunctions(phone, functions));
        Assert.IsFalse(UsbFunctionLayout.SplitsIntoFunctions(phone, [@"USBSTOR\DISK&VEN_GENERIC\18052507001672&0"]),
            "a disk beneath a stick is what Windows made of it, not a function of the device");

        var configuration = Lg;   // interfaces 0, 1 and 2
        Assert.AreEqual((byte)1, UsbFunctionLayout.TrailingInterfaceOf(functions[1], configuration));
        Assert.IsNull(UsbFunctionLayout.TrailingInterfaceOf(@"USB\VID_04E8&PID_6860&ADB\8&130557e0&2&0003", configuration),
            "an interface the configuration does not have is no interface");
        Assert.IsNull(UsbFunctionLayout.TrailingInterfaceOf(phone, configuration));
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
