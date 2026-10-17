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
        "HID 00  Unknown",
        "HUT 01  Generic Desktop Controls",
        "\t002  Mouse",
        "\t030  Direction-X",
        "HUT 09  Buttons",
        "\t001  Button 1 (Primary)",
        "HUT ff  Vendor Specific",
        "R 04  Usage Page");

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
    public void HidUsageTables_NamePagesAndUsages()
    {
        var db = Parse();
        Assert.AreEqual("Generic Desktop Controls", db.UsagePageName(0x01));
        Assert.AreEqual("Mouse", db.UsageName(0x01, 0x02));
        Assert.AreEqual("Direction-X", db.UsageName(0x01, 0x30));
        Assert.AreEqual("Button 1 (Primary)", db.UsageName(0x09, 0x01));
        Assert.IsNull(db.UsageName(0x09, 0x02), "a usage the file does not list has no name");
        Assert.AreEqual("Vendor Specific", db.UsagePageName(0xFF00), "the file names the whole vendor range under ff");
        Assert.AreEqual("Vendor Specific", db.UsagePageName(0xFFA0));
        Assert.IsNull(db.UsagePageName(0x0400), "between the defined pages and the vendor range nothing is named");
        Assert.IsNull(db.UsageName(0x04, 0x01), "the R section after the table is not usages");
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
