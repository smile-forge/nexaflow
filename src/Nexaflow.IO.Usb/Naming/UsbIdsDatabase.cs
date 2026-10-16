using System.Globalization;
using System.Text;

namespace Nexaflow.IO.Usb.Naming;

/// <summary>
/// Vendor, product and class names from a linux-usb.org <c>usb.ids</c> file. The format is line-oriented:
/// vendors flush left (<c>vvvv  name</c>) with products one tab in, then a <c>C cc  name</c> class section with
/// subclasses and protocols tabbed beneath; the remaining sections (AT, HID, R, L, …) are not used and skipped.
/// Immutable once parsed, so one instance is shared freely across threads.
/// </summary>
public sealed class UsbIdsDatabase
{
    /// <summary>The copy built into this assembly from the <c>external/usbids</c> submodule.</summary>
    public const string BundledResourceName = "Nexaflow.IO.Usb.usb.ids";

    static readonly Lazy<UsbIdsDatabase?> Bundled = new(LoadBundledCore, LazyThreadSafetyMode.ExecutionAndPublication);

    readonly Dictionary<ushort, string> _vendors = [];
    readonly Dictionary<uint, string> _products = [];
    readonly Dictionary<uint, string> _classes = [];

    UsbIdsDatabase() { }

    /// <summary>The <c># Version:</c> header, e.g. "2026.10.10".</summary>
    public string? Version { get; private set; }

    public int VendorCount => _vendors.Count;
    public int ProductCount => _products.Count;

    /// <summary>The bundled database, parsed once on first use; null only if the assembly was built without it.</summary>
    public static UsbIdsDatabase? LoadBundled() => Bundled.Value;

    public static UsbIdsDatabase Load(Stream stream)
    {
        using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true, leaveOpen: true);
        return Parse(reader);
    }

    public static UsbIdsDatabase Parse(TextReader reader)
    {
        var db = new UsbIdsDatabase();
        var section = Section.Vendors;
        ushort vendor = 0;
        byte cls = 0, sub = 0;
        bool haveVendor = false, haveClass = false, haveSub = false;

        for (string? line = reader.ReadLine(); line is not null; line = reader.ReadLine())
        {
            if (line.Length == 0) continue;
            if (line[0] == '#')
            {
                if (db.Version is null && line.StartsWith("# Version:", StringComparison.Ordinal)) db.Version = line[10..].Trim();
                continue;
            }

            int depth = line[0] == '\t' ? (line.Length > 1 && line[1] == '\t' ? 2 : 1) : 0;
            var body = line.AsSpan(depth);
            if (depth == 0)
            {
                haveVendor = haveClass = haveSub = false;
                if (TryHexField(body, 4, out uint v))
                {
                    section = Section.Vendors;
                    vendor = (ushort)v;
                    haveVendor = true;
                    db._vendors[vendor] = Name(body, 4);
                }
                else if (body.StartsWith("C ") && TryHexField(body[2..], 2, out uint c))
                {
                    section = Section.Classes;
                    cls = (byte)c;
                    haveClass = true;
                    db._classes[ClassKey(cls)] = Name(body, 4);
                }
                else section = Section.Other;
                continue;
            }

            switch (section)
            {
                case Section.Vendors when depth == 1 && haveVendor && TryHexField(body, 4, out uint p):
                    db._products[(uint)vendor << 16 | p] = Name(body, 4);
                    break;
                case Section.Classes when depth == 1 && haveClass && TryHexField(body, 2, out uint s):
                    sub = (byte)s;
                    haveSub = true;
                    db._classes[ClassKey(cls, sub)] = Name(body, 2);
                    break;
                case Section.Classes when depth == 2 && haveSub && TryHexField(body, 2, out uint pr):
                    db._classes[ClassKey(cls, sub, (byte)pr)] = Name(body, 2);
                    break;
            }
        }
        return db;
    }

    public string? VendorName(ushort vendorId) => _vendors.GetValueOrDefault(vendorId);

    public string? ProductName(ushort vendorId, ushort productId) => _products.GetValueOrDefault((uint)vendorId << 16 | productId);

    /// <summary>The class's own name, e.g. "Human Interface Device".</summary>
    public string? ClassName(byte cls) => _classes.GetValueOrDefault(ClassKey(cls));

    /// <summary>The subclass's name within its class, e.g. "Boot Interface Subclass".</summary>
    public string? SubClassName(byte cls, byte sub) => _classes.GetValueOrDefault(ClassKey(cls, sub));

    /// <summary>The protocol's name within its subclass, e.g. "Mouse".</summary>
    public string? ProtocolName(byte cls, byte sub, byte protocol) => _classes.GetValueOrDefault(ClassKey(cls, sub, protocol));

    /// <summary>
    /// Every level the file names, joined: "Human Interface Device / Boot Interface Subclass / Mouse". All of them,
    /// because the file's deeper levels are often placeholders ("No Subclass", "None") and no one level stands alone.
    /// </summary>
    public string? DescribeClass(byte cls, byte sub, byte protocol)
    {
        var parts = new[] { ClassName(cls), SubClassName(cls, sub), ProtocolName(cls, sub, protocol) }.OfType<string>().ToArray();
        return parts.Length == 0 ? null : string.Join(" / ", parts);
    }

    enum Section { Vendors, Classes, Other }

    // Level in the top byte keeps "class 03" distinct from "class 03, subclass 00".
    static uint ClassKey(byte cls) => 1u << 24 | (uint)cls << 16;
    static uint ClassKey(byte cls, byte sub) => 2u << 24 | (uint)cls << 16 | (uint)sub << 8;
    static uint ClassKey(byte cls, byte sub, byte protocol) => 3u << 24 | (uint)cls << 16 | (uint)sub << 8 | protocol;

    static bool TryHexField(ReadOnlySpan<char> s, int digits, out uint value)
    {
        value = 0;
        return s.Length > digits && s[digits] == ' '
            && uint.TryParse(s[..digits], NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out value);
    }

    static string Name(ReadOnlySpan<char> body, int idLength) => body[idLength..].Trim().ToString();

    static UsbIdsDatabase? LoadBundledCore()
    {
        using var stream = typeof(UsbIdsDatabase).Assembly.GetManifestResourceStream(BundledResourceName);
        return stream is null ? null : Load(stream);
    }
}
