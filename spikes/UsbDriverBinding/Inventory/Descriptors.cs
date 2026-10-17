using System.Buffers.Binary;

namespace Nexaflow.Spikes.UsbDriverBinding.Inventory;

public sealed record DeviceDescriptorInfo(
    string BcdUsb, byte Class, byte SubClass, byte Protocol, byte MaxPacketSize0,
    ushort VendorId, ushort ProductId, string BcdDevice,
    byte ManufacturerIndex, byte ProductIndex, byte SerialIndex, byte NumConfigurations)
{
    public ushort BcdUsbRaw { get; init; }
    public ushort BcdDeviceRaw { get; init; }

    public static DeviceDescriptorInfo Parse(ReadOnlySpan<byte> d)
    {
        ushort bcdUsb = BinaryPrimitives.ReadUInt16LittleEndian(d[2..]);
        ushort bcdDevice = BinaryPrimitives.ReadUInt16LittleEndian(d[12..]);
        return new(Bcd(bcdUsb), d[4], d[5], d[6], d[7],
            BinaryPrimitives.ReadUInt16LittleEndian(d[8..]), BinaryPrimitives.ReadUInt16LittleEndian(d[10..]),
            Bcd(bcdDevice), d[14], d[15], d[16], d[17])
        { BcdUsbRaw = bcdUsb, BcdDeviceRaw = bcdDevice };
    }

    public static string Bcd(ushort v) => $"{v >> 8:X}.{v & 0xFF:X2}";
}

public sealed record EndpointInfo(string Address, string Direction, string Type, ushort MaxPacketSize, byte Interval);

public sealed record ClassSpecificDescriptor(string Kind, string Hex);

public sealed record InterfaceInfo(
    byte Number, byte Alternate, byte Class, byte SubClass, byte Protocol, string ClassName, byte NameIndex,
    List<EndpointInfo> Endpoints, List<ClassSpecificDescriptor> ClassSpecific)
{
    public string? Name { get; set; }
    /// <summary>wDescriptorLength of the report descriptor, from the HID class descriptor.</summary>
    public ushort? HidReportLength { get; set; }
}

public sealed record InterfaceAssociation(byte FirstInterface, byte Count, byte Class, byte SubClass, byte Protocol, string ClassName);

public sealed record ConfigurationInfo(
    byte Value, byte NameIndex, bool SelfPowered, bool RemoteWakeup, int MaxPowerMa,
    List<InterfaceInfo> Interfaces, List<InterfaceAssociation> Associations, List<string> Unparsed);

public sealed record BosCapability(byte Type, string Name, string Detail, string Hex);

public static class DescriptorParser
{
    /// <summary>Walks a full configuration descriptor. bMaxPower units depend on speed (2 mA, or 8 mA at SuperSpeed).</summary>
    public static ConfigurationInfo ParseConfiguration(ReadOnlySpan<byte> d, bool superSpeed)
    {
        var interfaces = new List<InterfaceInfo>();
        var associations = new List<InterfaceAssociation>();
        var unparsed = new List<string>();
        var config = new ConfigurationInfo(d[5], d[6], (d[7] & 0x40) != 0, (d[7] & 0x20) != 0, d[8] * (superSpeed ? 8 : 2), interfaces, associations, unparsed);

        InterfaceInfo? current = null;
        for (int offset = d[0]; offset + 2 <= d.Length;)
        {
            int length = d[offset];
            if (length < 2 || offset + length > d.Length)
            {
                unparsed.Add($"malformed descriptor at offset {offset} (bLength {length})");
                break;
            }
            var desc = d.Slice(offset, length);
            switch (desc[1])
            {
                case 0x04 when length >= 9:
                    current = new InterfaceInfo(desc[2], desc[3], desc[5], desc[6], desc[7], UsbClassNames.Interface(desc[5], desc[6], desc[7]), desc[8], [], []);
                    interfaces.Add(current);
                    break;
                case 0x05 when length >= 7:
                    byte address = desc[2];
                    current?.Endpoints.Add(new EndpointInfo($"0x{address:X2}", (address & 0x80) != 0 ? "In" : "Out",
                        (desc[3] & 0x03) switch { 0 => "Control", 1 => "Isochronous", 2 => "Bulk", _ => "Interrupt" },
                        (ushort)(BinaryPrimitives.ReadUInt16LittleEndian(desc[4..]) & 0x07FF), desc[6]));
                    break;
                case 0x0B when length >= 8:
                    associations.Add(new InterfaceAssociation(desc[2], desc[3], desc[4], desc[5], desc[6], UsbClassNames.Interface(desc[4], desc[5], desc[6])));
                    break;
                case 0x21 when current is { Class: 0x03 } && length >= 9:
                    current.HidReportLength = BinaryPrimitives.ReadUInt16LittleEndian(desc[7..]);
                    current.ClassSpecific.Add(new("HID", Hex(desc)));
                    break;
                default:
                    var kind = ClassSpecificKind(desc[1], current);
                    if (current is not null) current.ClassSpecific.Add(new(kind, Hex(desc)));
                    else unparsed.Add($"{kind}: {Hex(desc)}");
                    break;
            }
            offset += length;
        }
        return config;
    }

    static string ClassSpecificKind(byte type, InterfaceInfo? iface) => (type, iface?.Class) switch
    {
        (0x21, 0xFE) => "DFU functional",
        (0x21, 0x0B) => "CCID (smart card)",
        (0x24, 0x01) => "Audio CS_INTERFACE",
        (0x25, 0x01) => "Audio CS_ENDPOINT",
        (0x24, 0x02) or (0x24, 0x0A) => "CDC functional",
        (0x24, 0x0E) => "Video CS_INTERFACE",
        (0x25, 0x0E) => "Video CS_ENDPOINT",
        (0x30, _) => "SuperSpeed endpoint companion",
        (0x31, _) => "SuperSpeedPlus isochronous companion",
        (0x24, _) => "CS_INTERFACE",
        (0x25, _) => "CS_ENDPOINT",
        _ => $"type 0x{type:X2}",
    };

    static readonly Guid MsOs20Platform = new("d8dd60df-4589-4cc7-9cd2-659d9e648a9f");
    static readonly Guid WebUsbPlatform = new("3408b638-09a9-47a0-8bfd-a0768815b665");

    public static List<BosCapability> ParseBos(ReadOnlySpan<byte> d)
    {
        var caps = new List<BosCapability>();
        for (int offset = d[0]; offset + 3 <= d.Length;)
        {
            int length = d[offset];
            if (length < 3 || offset + length > d.Length) break;
            var cap = d.Slice(offset, length);
            caps.Add(DescribeCapability(cap));
            offset += length;
        }
        return caps;
    }

    static BosCapability DescribeCapability(ReadOnlySpan<byte> c)
    {
        byte type = c[2];
        (string name, string detail) = type switch
        {
            0x02 => ("USB 2.0 Extension", (BinaryPrimitives.ReadUInt32LittleEndian(c[3..]) & 0x2) != 0 ? "LPM supported" : "no LPM"),
            0x03 => ("SuperSpeed USB", $"speeds 0x{BinaryPrimitives.ReadUInt16LittleEndian(c[4..]):X4}"),
            0x04 => ("Container ID", new Guid(c.Slice(4, 16)).ToString()),
            0x05 => DescribePlatform(c),
            0x0A => ("SuperSpeedPlus USB", ""),
            0x0D => ("Billboard", "USB-C alternate mode status"),
            _ => ($"capability 0x{type:X2}", ""),
        };
        return new BosCapability(type, name, detail, Hex(c));
    }

    static (string, string) DescribePlatform(ReadOnlySpan<byte> c)
    {
        if (c.Length < 20) return ("Platform", "truncated");
        var uuid = new Guid(c.Slice(4, 16));
        var data = c[20..];
        if (uuid == MsOs20Platform && data.Length >= 8)
            return ("MS OS 2.0", $"windows 0x{BinaryPrimitives.ReadUInt32LittleEndian(data):X8}, set length {BinaryPrimitives.ReadUInt16LittleEndian(data[4..])}, vendor code 0x{data[6]:X2}, alt enum 0x{data[7]:X2}");
        if (uuid == WebUsbPlatform && data.Length >= 4)
            return ("WebUSB", $"version {DeviceDescriptorInfo.Bcd(BinaryPrimitives.ReadUInt16LittleEndian(data))}, vendor code 0x{data[2]:X2}, landing page index {data[3]}");
        return ("Platform", uuid.ToString());
    }

    public static string Hex(ReadOnlySpan<byte> data) => string.Join(' ', Convert.ToHexString(data).Chunk(2).Select(c => new string(c)));
}

/// <summary>usb.org base class codes. The probe proper reads these and vendor names from usb.ids.</summary>
public static class UsbClassNames
{
    public static string Interface(byte cls, byte sub, byte proto) => cls switch
    {
        0x00 => "Per-interface",
        0x01 => sub switch { 1 => "Audio Control", 2 => "Audio Streaming", 3 => "MIDI Streaming", _ => "Audio" },
        0x02 => sub switch { 2 => "CDC ACM", 6 => "CDC ECM", 0x0D => "CDC NCM", _ => "CDC Control" },
        0x03 => (sub, proto) switch { (1, 1) => "HID Keyboard (boot)", (1, 2) => "HID Mouse (boot)", _ => "HID" },
        0x05 => "Physical",
        0x06 => "Still Image (PTP)",
        0x07 => "Printer",
        0x08 => sub switch { 6 => proto == 0x62 ? "Mass Storage (UAS)" : "Mass Storage (SCSI/BOT)", _ => "Mass Storage" },
        0x09 => "Hub",
        0x0A => "CDC Data",
        0x0B => "Smart Card (CCID)",
        0x0D => "Content Security",
        0x0E => sub switch { 1 => "Video Control", 2 => "Video Streaming", 3 => "Video Interface Collection", _ => "Video" },
        0x0F => "Personal Healthcare",
        0x10 => "Audio/Video",
        0x11 => "Billboard",
        0x12 => "USB Type-C Bridge",
        0xDC => "Diagnostic",
        0xE0 => (sub, proto) switch { (1, 1) => "Bluetooth", (1, 3) => "RNDIS", _ => "Wireless Controller" },
        0xEF => (sub, proto) switch { (2, 1) => "Interface Association", (4, _) => "RNDIS", _ => "Miscellaneous" },
        0xFE => sub switch { 1 => "DFU", 2 => "IrDA Bridge", 3 => "USB Test & Measurement", _ => "Application Specific" },
        0xFF => "Vendor Specific",
        _ => $"Class 0x{cls:X2}",
    };
}
