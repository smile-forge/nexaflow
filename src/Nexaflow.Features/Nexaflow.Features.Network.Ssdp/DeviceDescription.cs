using System.Xml.Linq;

namespace Nexaflow.Features.Network.Ssdp;

/// <summary>One icon a device offers, with enough to pick between them.</summary>
public readonly record struct DeviceIcon(string MimeType, int Width, int Height, int Depth, Uri Url);

/// <summary>
/// A UPnP device description, read from the document at LOCATION.
/// </summary>
/// <remarks>
/// <para>
/// This is where a device stops being an address and becomes a thing with a name. SSDP's reply says only
/// where to look; UPnP Device Architecture 1.1 §2.3 says what is there — a friendly name, a manufacturer,
/// a model with a number and a description, a serial, a URL for the manufacturer and one for the model,
/// the address of the actual web interface, and a list of icons.
/// </para>
/// <para>
/// Parsed by name rather than by position, and namespace-blind. The schema declares
/// <c>urn:schemas-upnp-org:device-1-0</c>, and devices in the field are inconsistent about prefixes and
/// occasionally about the namespace itself; matching on the local name reads all of them and cannot be
/// wrong about which element is which, because these names do not collide.
/// </para>
/// </remarks>
public sealed class DeviceDescription
{
    public string FriendlyName { get; private init; } = "";
    public string Manufacturer { get; private init; } = "";
    public string ManufacturerUrl { get; private init; } = "";
    public string ModelName { get; private init; } = "";
    public string ModelNumber { get; private init; } = "";
    public string ModelDescription { get; private init; } = "";
    public string ModelUrl { get; private init; } = "";
    public string SerialNumber { get; private init; } = "";
    public string Udn { get; private init; } = "";
    public string DeviceType { get; private init; } = "";
    public string PresentationUrl { get; private init; } = "";

    public IReadOnlyList<DeviceIcon> Icons { get; private init; } = [];

    /// <summary>
    /// Reads a description, or null if the document is not one.
    /// </summary>
    /// <param name="xml">The document.</param>
    /// <param name="location">Where it was fetched from — every URL in it may be relative to this, and
    /// §2.3 says so explicitly. A description that gave an absolute icon path and one that gave
    /// <c>/icon.png</c> must come out the same.</param>
    public static DeviceDescription? Read(string xml, Uri location)
    {
        XDocument doc;
        try { doc = XDocument.Parse(xml); }
        catch (System.Xml.XmlException) { return null; }

        // The FIRST device element, which for a root description is the root device. A device may embed
        // others (a printer inside a multi-function unit); those are their own things and this does not
        // pretend to speak for them.
        var device = Named(doc.Root, "device");
        if (device is null) return null;

        // urlBase is deprecated in 1.1 but still emitted, and where present it wins over the fetch address
        // for resolving everything else.
        var root = Text(Named(doc.Root, "URLBase")) is { Length: > 0 } declared
                && Uri.TryCreate(declared, UriKind.Absolute, out var based) ? based : location;

        return new DeviceDescription
        {
            FriendlyName = Text(Named(device, "friendlyName")),
            Manufacturer = Text(Named(device, "manufacturer")),
            ManufacturerUrl = Absolute(Text(Named(device, "manufacturerURL")), root),
            ModelName = Text(Named(device, "modelName")),
            ModelNumber = Text(Named(device, "modelNumber")),
            ModelDescription = Text(Named(device, "modelDescription")),
            ModelUrl = Absolute(Text(Named(device, "modelURL")), root),
            SerialNumber = Text(Named(device, "serialNumber")),
            Udn = Text(Named(device, "UDN")),
            DeviceType = Text(Named(device, "deviceType")),
            PresentationUrl = Absolute(Text(Named(device, "presentationURL")), root),
            Icons = [.. ReadIcons(device, root)],
        };
    }

    /// <summary>
    /// The icons, largest and deepest first.
    /// </summary>
    /// <remarks>
    /// Ordered here rather than at the point of display: §2.3.5 lets a device offer the same picture at
    /// several sizes and colour depths, and which one is wanted is nearly always "the best available".
    /// </remarks>
    private static IEnumerable<DeviceIcon> ReadIcons(XElement device, Uri root)
        => (Named(device, "iconList")?.Elements() ?? [])
            .Where(e => e.Name.LocalName == "icon")
            .Select(e => new
            {
                Mime = Text(Named(e, "mimetype")),
                W = Number(Named(e, "width")),
                H = Number(Named(e, "height")),
                D = Number(Named(e, "depth")),
                Url = Absolute(Text(Named(e, "url")), root),
            })
            .Where(x => x.Url.Length > 0 && Uri.TryCreate(x.Url, UriKind.Absolute, out _))
            .OrderByDescending(x => x.W * x.H)
            .ThenByDescending(x => x.D)
            .Select(x => new DeviceIcon(x.Mime, x.W, x.H, x.D, new Uri(x.Url)));

    /// <summary>A child by local name, whatever namespace it happens to carry.</summary>
    private static XElement? Named(XElement? parent, string name)
        => parent?.Descendants().FirstOrDefault(e => e.Name.LocalName == name);

    private static string Text(XElement? element) => element?.Value.Trim() ?? "";

    private static int Number(XElement? element)
        => int.TryParse(Text(element), out var n) ? n : 0;

    /// <summary>A URL as written, made absolute against where the document came from.</summary>
    private static string Absolute(string url, Uri root)
        => url.Length == 0 ? ""
         : Uri.TryCreate(root, url, out var joined) ? joined.ToString()
         : "";
}
