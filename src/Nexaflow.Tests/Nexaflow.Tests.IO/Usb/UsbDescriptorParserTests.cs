using Nexaflow.IO.Usb.Descriptors;
using Nexaflow.Tests.Fixtures;
using static Nexaflow.Tests.IO.Usb.UsbFixtures;
using static Nexaflow.Tests.Fixtures.UsbSamples;

namespace Nexaflow.Tests.IO.Usb;

[TestClass]
[CoversNode("io-usb-descriptors")]
public class UsbDescriptorParserTests
{
    [TestMethod]
    public void DeviceDescriptor_ReadsEveryField()
    {
        Assert.IsTrue(UsbDeviceDescriptor.TryParse(Bytes(LgDevice), out var d));
        Assert.AreEqual(0x043E, d.VendorId);
        Assert.AreEqual(0x9A39, d.ProductId);
        Assert.AreEqual("2.00", d.UsbVersion.ToString());
        Assert.AreEqual("4.17", d.DeviceVersion.ToString());
        Assert.AreEqual(UsbClassCodes.Miscellaneous, d.Class);
        Assert.AreEqual(64, d.MaxPacketSize0);
        Assert.AreEqual((1, 3, 4, 1), (d.ManufacturerIndex, d.ProductIndex, d.SerialNumberIndex, d.ConfigurationCount));
        Assert.IsFalse(d.HasBos, "bcdUSB 2.00 predates BOS");
    }

    [TestMethod]
    public void DeviceDescriptor_RefusesShortOrMistypedBytes()
    {
        Assert.IsFalse(UsbDeviceDescriptor.TryParse(Bytes(LgDevice)[..17], out _));
        var mistyped = Bytes(LgDevice);
        mistyped[1] = UsbDescriptorType.Configuration;
        Assert.IsFalse(UsbDeviceDescriptor.TryParse(mistyped, out _));
    }

    [TestMethod]
    public void Configuration_CompositeWithAssociation_KeepsTheWholeTree()
    {
        var c = UsbDescriptorParser.ParseConfiguration(Bytes(LgConfiguration), UsbSpeed.Full);

        Assert.AreEqual(1, c.Value);
        Assert.IsTrue(c.SelfPowered);
        Assert.IsFalse(c.RemoteWakeup);
        Assert.AreEqual(0, c.Problems.Count, string.Join("; ", c.Problems));
        CollectionAssert.AreEqual(new byte[] { 0, 1, 2 }, c.Interfaces.Select(i => i.Number).ToArray());

        var hid = c.Interfaces[0];
        Assert.AreEqual(UsbClassCodes.Hid, hid.Class);
        Assert.AreEqual((ushort)0x21, hid.HidReportDescriptorLength);
        Assert.AreEqual(5, hid.NameIndex);
        Assert.AreEqual(2, hid.Endpoints.Count);
        Assert.AreEqual((0x81, UsbEndpointType.Interrupt, UsbDirection.In, 64), (hid.Endpoints[0].Address, hid.Endpoints[0].Type, hid.Endpoints[0].Direction, hid.Endpoints[0].MaxPacketSize));

        var association = c.Associations.Single();
        Assert.AreEqual((1, 2, UsbClassCodes.CdcControl), (association.FirstInterface, association.InterfaceCount, association.Class));
        Assert.AreSame(association, c.AssociationOf(2));
        Assert.IsNull(c.AssociationOf(0));

        var acm = c.Interfaces[1];
        Assert.AreEqual(4, acm.ClassSpecific.Count, "header, call management, ACM and union functional descriptors");
        Assert.IsTrue(acm.ClassSpecific.All(x => x.Kind == "CDC functional"));
        Assert.AreEqual(UsbEndpointType.Interrupt, acm.Endpoints.Single().Type);

        var data = c.Interfaces[2];
        Assert.AreEqual(UsbClassCodes.CdcData, data.Class);
        Assert.IsTrue(data.Endpoints.All(e => e.Type == UsbEndpointType.Bulk));
        CollectionAssert.AreEqual(new byte[] { 0x02, 0x82 }, data.Endpoints.Select(e => e.Address).ToArray());
    }

    [TestMethod]
    public void Configuration_MaxPower_CountsEightMilliampUnitsAtSuperSpeed()
    {
        var bytes = Bytes(AsusLcdConfiguration); // bMaxPower 0xFA
        Assert.AreEqual(500, UsbDescriptorParser.ParseConfiguration(bytes, UsbSpeed.High).MaxPowerMilliamps);
        Assert.AreEqual(2000, UsbDescriptorParser.ParseConfiguration(bytes, UsbSpeed.Super).MaxPowerMilliamps);
    }

    [TestMethod]
    public void Configuration_MalformedTail_KeepsEverythingBeforeIt()
    {
        var bytes = Bytes(AsusLcdConfiguration);
        bytes[32] = 0x40; // interface 1's bLength now runs past the end
        var c = UsbDescriptorParser.ParseConfiguration(bytes, UsbSpeed.High);

        Assert.AreEqual(1, c.Interfaces.Count, "interface 0 and its endpoints survive");
        Assert.AreEqual(2, c.Interfaces[0].Endpoints.Count);
        Assert.AreEqual(1, c.Problems.Count);
        StringAssert.Contains(c.Problems[0], "offset 32");
    }

    [TestMethod]
    public void Configuration_TruncatedReply_IsReportedNotThrown()
    {
        var c = UsbDescriptorParser.ParseConfiguration(Bytes(LgConfiguration)[..50], UsbSpeed.Full);
        Assert.IsTrue(c.Problems.Any(p => p.Contains("wTotalLength 107")));
        Assert.IsTrue(c.Interfaces.Count >= 1);
    }

    [TestMethod]
    public void Configuration_NotAConfiguration_YieldsAProblem()
    {
        var c = UsbDescriptorParser.ParseConfiguration(Bytes(LgDevice), UsbSpeed.Full);
        Assert.AreEqual(0, c.Interfaces.Count);
        Assert.AreEqual(1, c.Problems.Count);
    }

    [TestMethod]
    public void Bos_StandardCapabilities()
    {
        var bos = UsbDescriptorParser.ParseBos(Bytes(TiHubBos));
        CollectionAssert.AreEqual(new[] { "USB 2.0 Extension", "SuperSpeed USB", "Container ID" }, bos.Capabilities.Select(c => c.Name).ToArray());
        Assert.AreEqual("Link Power Management", bos.Capabilities[0].Detail);
        Assert.AreEqual("Full, High, 5 Gbps", bos.Capabilities[1].Detail);
        Assert.IsNull(bos.MsOs20);
        Assert.IsNull(bos.WebUsb);
    }

    [TestMethod]
    public void Bos_PlatformCapabilities_MsOs20AndWebUsb()
    {
        var bos = UsbDescriptorParser.ParseBos(Bytes(PlatformBos));
        Assert.IsNotNull(bos.MsOs20);
        Assert.AreEqual(0x06030000u, bos.MsOs20.WindowsVersion);
        Assert.AreEqual((ushort)0xB2, bos.MsOs20.DescriptorSetLength);
        Assert.AreEqual(0x01, bos.MsOs20.VendorCode);
        Assert.IsNotNull(bos.WebUsb);
        Assert.AreEqual("1.00", bos.WebUsb.Version.ToString());
        Assert.AreEqual((0x02, 0x01), (bos.WebUsb.VendorCode, bos.WebUsb.LandingPageIndex));
    }

    [TestMethod]
    public void Bos_OverrunningCapability_EndsTheWalk()
    {
        var bytes = Bytes(TiHubBos);
        bytes[12] = 0x40; // the SuperSpeed capability now claims more than the set holds
        var bos = UsbDescriptorParser.ParseBos(bytes);
        Assert.AreEqual(1, bos.Capabilities.Count);
    }

    [TestMethod]
    public void Strings_AreBoundedByTheirOwnLength()
    {
        // "LG" then two bytes past bLength that must not be read
        Assert.AreEqual("LG", UsbDescriptorParser.ParseString(Bytes("06 03 4C 00 47 00 41 00")));
        Assert.AreEqual("", UsbDescriptorParser.ParseString(Bytes("02 03")));
        Assert.IsNull(UsbDescriptorParser.ParseString(Bytes("06 02 4C 00 47 00")));
        CollectionAssert.AreEqual(new ushort[] { 0x0409, 0x0407 }, UsbDescriptorParser.ParseLanguageIds(Bytes("06 03 09 04 07 04")));
    }

    [TestMethod]
    public void ClassNames_NameTheTriplesAProbeMeets()
    {
        Assert.AreEqual("CDC ACM (serial)", UsbClassCodes.Name(0x02, 0x02, 0x01));
        Assert.AreEqual("HID Keyboard", UsbClassCodes.Name(0x03, 0x01, 0x01));
        Assert.AreEqual("DFU (firmware update)", UsbClassCodes.Name(0xFE, 0x01, 0x02));
        Assert.AreEqual("Mass Storage (UAS)", UsbClassCodes.Name(0x08, 0x06, 0x62));
        Assert.AreEqual("Class 0x42", UsbClassCodes.Name(0x42, 0, 0));
    }
}
