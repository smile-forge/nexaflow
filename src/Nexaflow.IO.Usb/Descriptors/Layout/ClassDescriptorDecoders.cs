namespace Nexaflow.IO.Usb.Descriptors.Layout;

/// <summary>The HID class descriptor (HID 1.11 §6.2.1), which says how long the report descriptor is.</summary>
public sealed class HidDescriptorDecoder : IUsbDescriptorDecoder
{
    public UsbDescriptorHeading? TryDecode(UsbFieldWriter w, UsbDescriptorScope scope)
    {
        if (w.Type != UsbDescriptorType.ClassFunctional || scope.Interface is not { Class: UsbClassCodes.Hid }) return null;

        w.U8("bLength");
        w.U8("bDescriptorType", UsbDescriptorNames.Type);
        var version = w.Bcd("bcdHID");
        w.U8("bCountryCode", c => c == 0 ? "not localized" : null);
        byte count = w.U8("bNumDescriptors");
        ushort report = 0;
        for (int i = 0; i < count && w.Remaining >= 3; i++)
        {
            byte type = w.U8($"bDescriptorType[{i}]", t => t switch { UsbDescriptorType.HidReport => "Report", 0x23 => "Physical", _ => null });
            ushort length = w.U16($"wDescriptorLength[{i}]", v => $"{v} bytes");
            if (type == UsbDescriptorType.HidReport && report == 0) report = length;
        }
        return new("HID", report > 0 ? $"HID {version} · report descriptor {report} bytes" : $"HID {version}");
    }
}

/// <summary>
/// CDC functional descriptors (CDC 1.2 §5.2.3) on a communications interface: what kind of serial port or
/// network adapter the function is, and which interface carries its data.
/// </summary>
public sealed class CdcDescriptorDecoder : IUsbDescriptorDecoder
{
    public UsbDescriptorHeading? TryDecode(UsbFieldWriter w, UsbDescriptorScope scope)
    {
        if (w.Type != UsbDescriptorType.ClassSpecificInterface || scope.Interface is not { Class: UsbClassCodes.CdcControl }) return null;

        w.U8("bLength");
        w.U8("bDescriptorType", UsbDescriptorNames.Type);
        byte subtype = w.U8("bDescriptorSubtype", SubtypeName);
        string name = $"CDC {SubtypeName(subtype) ?? $"functional 0x{subtype:X2}"}";
        string summary = subtype switch
        {
            0x00 => $"CDC {w.Bcd("bcdCDC")}",
            0x01 => CallManagement(w),
            0x02 => Acm(w),
            0x06 => Union(w),
            0x07 => Country(w, scope),
            0x0F => Ethernet(w, scope),
            0x1A => $"NCM {w.Bcd("bcdNcmVersion")} · {Flags(w.U8("bmNetworkCapabilities", NcmCapabilities), NcmCapabilities)}",
            0x1B => Mbim(w),
            _ => $"{w.Length} bytes",
        };
        return new(name, summary);
    }

    public static string? SubtypeName(byte subtype) => subtype switch
    {
        0x00 => "Header", 0x01 => "Call Management", 0x02 => "Abstract Control Management", 0x03 => "Direct Line Management",
        0x04 => "Telephone Ringer", 0x05 => "Telephone Call/Line State Reporting", 0x06 => "Union", 0x07 => "Country Selection",
        0x08 => "Telephone Operational Modes", 0x09 => "USB Terminal", 0x0A => "Network Channel Terminal", 0x0B => "Protocol Unit",
        0x0C => "Extension Unit", 0x0D => "Multi-Channel Management", 0x0E => "CAPI Control Management", 0x0F => "Ethernet Networking",
        0x10 => "ATM Networking", 0x11 => "Wireless Handset Control Model", 0x12 => "Mobile Direct Line Model", 0x13 => "MDLM Detail",
        0x14 => "Device Management Model", 0x15 => "OBEX", 0x16 => "Command Set", 0x17 => "Command Set Detail",
        0x18 => "Telephone Control Model", 0x19 => "OBEX Service Identifier", 0x1A => "NCM", 0x1B => "MBIM", 0x1C => "MBIM Extended",
        _ => null,
    };

    static string Flags(byte v, Func<byte, string?> name) => name(v) ?? "none";

    static string CallManagement(UsbFieldWriter w)
    {
        byte caps = w.U8("bmCapabilities", v => UsbDescriptorNames.Flags(v, (0x01, "handles call management"), (0x02, "over the data interface")));
        byte data = w.U8("bDataInterface", v => $"interface {v}");
        return (caps & 0x01) != 0 ? $"device handles calls · data on interface {data}" : $"no call management · data on interface {data}";
    }

    static string? AcmCapabilities(byte v) => UsbDescriptorNames.Flags(v,
        (0x01, "Comm feature"), (0x02, "line coding + serial state"), (0x04, "Send Break"), (0x08, "Network Connection"));

    static string Acm(UsbFieldWriter w) => $"ACM · {AcmCapabilities(w.U8("bmCapabilities", AcmCapabilities))}";

    static string Union(UsbFieldWriter w)
    {
        byte control = w.U8("bControlInterface", v => $"interface {v}");
        var subordinates = new List<byte>();
        for (int i = 0; w.Remaining > 0; i++)
            subordinates.Add(w.U8($"bSubordinateInterface{i}", v => $"interface {v}"));
        return $"control {control} → {(subordinates.Count == 0 ? "nothing" : string.Join(", ", subordinates))}";
    }

    static string Country(UsbFieldWriter w, UsbDescriptorScope scope)
    {
        w.StringIndex("iCountryCodeRelDate", scope);
        int count = 0;
        for (; w.Remaining >= 2; count++)
            w.U16($"wCountryCode{count}", v => v is >= 0x4141 and <= 0x5A5A ? $"{(char)(v >> 8)}{(char)(v & 0xFF)}" : null);
        return $"{count} countr{(count == 1 ? "y" : "ies")}";
    }

    static string Ethernet(UsbFieldWriter w, UsbDescriptorScope scope)
    {
        w.StringIndex("iMACAddress", scope);
        w.U32("bmEthernetStatistics");
        ushort segment = w.U16("wMaxSegmentSize", v => $"{v} bytes");
        w.U16("wNumberMCFilters", v => $"{v & 0x7FFF} filter(s){((v & 0x8000) != 0 ? ", imperfect" : "")}");
        w.U8("bNumberPowerFilters");
        return $"Ethernet · max segment {segment} bytes";
    }

    static string? NcmCapabilities(byte v) => UsbDescriptorNames.Flags(v,
        (0x01, "packet filter"), (0x02, "net address"), (0x04, "encapsulated commands"), (0x08, "max datagram size"),
        (0x10, "CRC mode"), (0x20, "8-byte NTB input size"));

    static string Mbim(UsbFieldWriter w)
    {
        var version = w.Bcd("bcdMBIMVersion");
        ushort control = w.U16("wMaxControlMessage", v => $"{v} bytes");
        w.U8("bNumberFilters");
        w.U8("bMaxFilterSize", v => $"{v} bytes");
        w.U16("wMaxSegmentSize", v => $"{v} bytes");
        w.U8("bmNetworkCapabilities", NcmCapabilities);
        return $"MBIM {version} · control messages up to {control} bytes";
    }
}

/// <summary>The DFU functional descriptor (DFU 1.1 §4.1.3): what a firmware-update interface lets a host do.</summary>
public sealed class DfuDescriptorDecoder : IUsbDescriptorDecoder
{
    public UsbDescriptorHeading? TryDecode(UsbFieldWriter w, UsbDescriptorScope scope)
    {
        if (w.Type != UsbDescriptorType.ClassFunctional || scope.Interface is not { Class: UsbClassCodes.ApplicationSpecific, SubClass: 0x01 }) return null;

        w.U8("bLength");
        w.U8("bDescriptorType", UsbDescriptorNames.Type);
        byte attributes = w.U8("bmAttributes", Attributes);
        w.U16("wDetachTimeOut", v => $"{v} ms");
        ushort transfer = w.U16("wTransferSize", v => $"{v} bytes per transfer");
        var version = w.Remaining >= 2 ? w.Bcd("bcdDFUVersion") : default;
        string what = Attributes(attributes);
        return new("DFU functional", version.Raw == 0 ? $"DFU · {what} · {transfer} bytes" : $"DFU {version} · {what} · {transfer} bytes");
    }

    static string Attributes(byte v) => UsbDescriptorNames.Flags(v,
        (0x01, "download"), (0x02, "upload"), (0x04, "manifestation tolerant"), (0x08, "detaches itself"));
}
