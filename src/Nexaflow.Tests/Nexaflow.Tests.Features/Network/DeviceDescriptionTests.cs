using Nexaflow.Features.Network.Ssdp;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.Features.Network;

/// <summary>
/// The document that turns an address into a device.
/// </summary>
/// <remarks>
/// SSDP's reply says only where to look. UPnP Device Architecture 1.1 §2.3 says what is there — a friendly
/// name, a make, a model with a number and a description, a serial, the address of the actual web
/// interface, and icons. Everything the list shows about a television rather than about a socket comes
/// from here.
/// </remarks>
[TestClass]
[CoversNode("network-discovery")]
public class DeviceDescriptionTests
{
    private static readonly Uri Location = new("http://192.168.1.4:9197/dmr/description.xml");

    /// <summary>A description in the shape a real television serves one, prefixed namespace and all.</summary>
    private const string Samsung = """
        <?xml version="1.0"?>
        <root xmlns="urn:schemas-upnp-org:device-1-0">
          <specVersion><major>1</major><minor>0</minor></specVersion>
          <device>
            <deviceType>urn:schemas-upnp-org:device:MediaRenderer:1</deviceType>
            <friendlyName>[TV] Living Room</friendlyName>
            <manufacturer>Samsung Electronics</manufacturer>
            <manufacturerURL>http://www.samsung.com/sec</manufacturerURL>
            <modelDescription>Samsung TV DMR</modelDescription>
            <modelName>UE55NU8000</modelName>
            <modelNumber>AllShare1.0</modelNumber>
            <modelURL>http://www.samsung.com/sec</modelURL>
            <serialNumber>0BCD3FGH400012Z</serialNumber>
            <UDN>uuid:0d3d0100-1dab-11e1-9b23-984827b3e1c9</UDN>
            <presentationURL>http://192.168.1.4:8080/</presentationURL>
            <iconList>
              <icon>
                <mimetype>image/jpeg</mimetype><width>48</width><height>48</height><depth>24</depth>
                <url>/icon/48.jpg</url>
              </icon>
              <icon>
                <mimetype>image/png</mimetype><width>120</width><height>120</height><depth>24</depth>
                <url>/icon/120.png</url>
              </icon>
            </iconList>
          </device>
        </root>
        """;

    [TestMethod]
    public void A_description_says_what_the_device_is()
    {
        var told = DeviceDescription.Read(Samsung, Location);

        Assert.IsNotNull(told);
        Assert.AreEqual("[TV] Living Room", told.FriendlyName,
            "the name somebody typed into an app once, and the only one worth showing in a list");
        Assert.AreEqual("Samsung Electronics", told.Manufacturer);
        Assert.AreEqual("UE55NU8000", told.ModelName);
        Assert.AreEqual("AllShare1.0", told.ModelNumber);
        Assert.AreEqual("Samsung TV DMR", told.ModelDescription);
        Assert.AreEqual("0BCD3FGH400012Z", told.SerialNumber);
        Assert.AreEqual("urn:schemas-upnp-org:device:MediaRenderer:1", told.DeviceType);
    }

    [TestMethod]
    public void And_a_relative_address_is_resolved_against_where_it_came_from()
    {
        // §2.3 says every URL in the document may be relative to the address it was fetched from, and
        // devices differ on which they use. A description that gave an absolute icon and one that gave
        // "/icon/120.png" have to come out the same, or half the icons point at nothing.
        var told = DeviceDescription.Read(Samsung, Location)!;

        Assert.AreEqual("http://192.168.1.4:9197/icon/120.png", told.Icons[0].Url.ToString());
        Assert.AreEqual("http://192.168.1.4:8080/", told.PresentationUrl);
        Assert.AreEqual("http://www.samsung.com/sec", told.ModelUrl, "an absolute one is left alone");
    }

    [TestMethod]
    public void And_the_best_icon_comes_first()
    {
        // A device offers the same picture at several sizes, and what a caller wants is nearly always the
        // best available — so the choosing is done here rather than at every point of display.
        var told = DeviceDescription.Read(Samsung, Location)!;

        Assert.AreEqual(2, told.Icons.Count);
        Assert.AreEqual(120, told.Icons[0].Width);
        Assert.AreEqual("image/png", told.Icons[0].MimeType);
        Assert.AreEqual(48, told.Icons[1].Width);
    }

    [TestMethod]
    public void And_a_device_that_names_no_namespace_reads_the_same()
    {
        // Devices in the field are inconsistent about prefixes and occasionally about the namespace
        // itself. Matching on the local name reads all of them, and cannot be wrong about which element is
        // which because these names do not collide.
        var told = DeviceDescription.Read(
            Samsung.Replace(" xmlns=\"urn:schemas-upnp-org:device-1-0\"", ""), Location);

        Assert.IsNotNull(told);
        Assert.AreEqual("[TV] Living Room", told.FriendlyName);
    }

    [TestMethod]
    public void And_a_URLBase_wins_over_the_address_it_was_fetched_from()
    {
        // Deprecated in 1.1 and still emitted. Where a device gives one, every relative address in the
        // document is against it rather than against where the document happened to be served.
        var told = DeviceDescription.Read(
            Samsung.Replace("<device>", "</specVersion><URLBase>http://192.168.1.4:7676/</URLBase><device>")
                   .Replace("<specVersion><major>1</major><minor>0</minor></specVersion>",
                            "<specVersion><major>1</major><minor>0</minor>"),
            Location)!;

        Assert.AreEqual("http://192.168.1.4:7676/icon/120.png", told.Icons[0].Url.ToString());
    }

    [TestMethod]
    public void A_device_that_publishes_nothing_extra_still_reads()
    {
        // The minimum a description can be. Everything absent comes back empty rather than null, so a
        // caller writes no null checks and a fact with no value is simply never asserted.
        var told = DeviceDescription.Read("""
            <root xmlns="urn:schemas-upnp-org:device-1-0">
              <device><friendlyName>Thing</friendlyName></device>
            </root>
            """, Location);

        Assert.IsNotNull(told);
        Assert.AreEqual("Thing", told.FriendlyName);
        Assert.AreEqual("", told.PresentationUrl);
        Assert.AreEqual("", told.SerialNumber);
        Assert.AreEqual(0, told.Icons.Count);
    }

    [TestMethod]
    public void And_something_that_is_not_a_description_is_refused()
    {
        // A device may serve anything at the address it advertised, including an error page. Reading one
        // as a description would invent a device with no name and no model, which is worse than none.
        Assert.IsNull(DeviceDescription.Read("<html><body>404</body></html>", Location),
            "HTML has no device element, so there is nothing here to believe");
        Assert.IsNull(DeviceDescription.Read("not xml at all", Location));
    }
}
