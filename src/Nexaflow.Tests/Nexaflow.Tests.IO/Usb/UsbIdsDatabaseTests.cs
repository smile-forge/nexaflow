using Nexaflow.IO.Usb.Naming;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.IO.Usb;

[TestClass]
[CoversNode("io-usb-names")]
public class UsbIdsDatabaseTests
{
    static readonly string Sample = string.Join('\n',
        "#\tList of USB ID's",
        "# Version: 2026.06.26",
        "# Date:    2026-06-26 20:34:02",
        "046d  Logitech, Inc.",
        "\tc52b  Unifying Receiver",
        "\t\t0000  an interface line, ignored",
        "0b05  ASUSTek Computer, Inc.",
        "\t1c83  Motherboard LCD Panel",
        "# List of known device classes, subclasses and protocols",
        "C 02  Communications",
        "\t02  Abstract (modem)",
        "\t\t01  AT-commands (v.25ter)",
        "C 03  Human Interface Device",
        "\t01  Boot Interface Subclass",
        "\t\t02  Mouse",
        "AT 0100  USB Audio Terminal section, not used",
        "\t0101  must not become a product of the last vendor",
        "HID 00  Unknown");

    static UsbIdsDatabase Parse() => UsbIdsDatabase.Parse(new StringReader(Sample));

    [TestMethod]
    public void VendorsAndProducts()
    {
        var db = Parse();
        Assert.AreEqual("2026.06.26", db.Version);
        Assert.AreEqual("Logitech, Inc.", db.VendorName(0x046D));
        Assert.AreEqual("Unifying Receiver", db.ProductName(0x046D, 0xC52B));
        Assert.AreEqual("Motherboard LCD Panel", db.ProductName(0x0B05, 0x1C83));
        Assert.IsNull(db.ProductName(0x046D, 0x1C83), "products belong to their own vendor");
        Assert.AreEqual(2, db.VendorCount);
        Assert.AreEqual(2, db.ProductCount);
    }

    [TestMethod]
    public void Classes_EachLevelAndTheirDescription()
    {
        var db = Parse();
        Assert.AreEqual("Communications", db.ClassName(0x02));
        Assert.AreEqual("Abstract (modem)", db.SubClassName(0x02, 0x02));
        Assert.AreEqual("AT-commands (v.25ter)", db.ProtocolName(0x02, 0x02, 0x01));
        Assert.AreEqual("Communications / Abstract (modem) / AT-commands (v.25ter)", db.DescribeClass(0x02, 0x02, 0x01));
        Assert.AreEqual("Communications / Abstract (modem)", db.DescribeClass(0x02, 0x02, 0x7F), "an unnamed protocol is left out");
        Assert.AreEqual("Human Interface Device / Boot Interface Subclass / Mouse", db.DescribeClass(0x03, 0x01, 0x02));
        Assert.IsNull(db.SubClassName(0x03, 0x02), "a subclass belongs to its own class");
        Assert.IsNull(db.DescribeClass(0x42, 0, 0));
    }

    [TestMethod]
    public void LaterSections_AreSkipped()
    {
        var db = Parse();
        Assert.IsNull(db.ProductName(0x0B05, 0x0101));
        Assert.IsNull(db.VendorName(0x0100));
    }

    [TestMethod]
    public void Bundled_IsTheSubmoduleCopy()
    {
        var db = UsbIdsDatabase.LoadBundled();
        Assert.IsNotNull(db, "usb.ids is embedded from external/usbids");
        Assert.IsNotNull(db.Version);
        Assert.IsTrue(db.VendorCount > 3000, $"{db.VendorCount} vendors");
        Assert.AreEqual("Logitech, Inc.", db.VendorName(0x046D));
        Assert.AreEqual("Human Interface Device", db.ClassName(0x03));
        Assert.AreEqual("Mouse", db.ProtocolName(0x03, 0x01, 0x02));
        Assert.AreSame(db, UsbIdsDatabase.LoadBundled(), "parsed once");
    }
}
