using System.Buffers.Binary;
using System.Runtime.InteropServices;

namespace Nexaflow.IO.Usb.Descriptors;

/// <summary>Pure parsers over descriptor bytes as a device returns them — no I/O, no platform types.</summary>
public static class UsbDescriptorParser
{
    static readonly Guid MsOs20PlatformId = new("d8dd60df-4589-4cc7-9cd2-659d9e648a9f");
    static readonly Guid WebUsbPlatformId = new("3408b638-09a9-47a0-8bfd-a0768815b665");

    /// <summary>
    /// Walks a full configuration descriptor (wTotalLength bytes). bMaxPower counts 2 mA units, or 8 mA when the
    /// device is operating at SuperSpeed or faster (USB 3.2 §9.6.3).
    /// </summary>
    public static UsbConfiguration ParseConfiguration(ReadOnlySpan<byte> d, UsbSpeed speed)
    {
        var problems = new List<string>();
        if (d.Length < 9 || d[1] != UsbDescriptorType.Configuration)
            return new UsbConfiguration(0, 0, false, false, 0, [], [], [], ["not a configuration descriptor"], d.ToArray());

        int total = Math.Min(BinaryPrimitives.ReadUInt16LittleEndian(d[2..]), d.Length);
        if (total < BinaryPrimitives.ReadUInt16LittleEndian(d[2..]))
            problems.Add($"wTotalLength {BinaryPrimitives.ReadUInt16LittleEndian(d[2..])} but only {d.Length} bytes returned");

        var interfaces = new List<InterfaceBuilder>();
        var associations = new List<UsbInterfaceAssociation>();
        var unattached = new List<UsbClassSpecificDescriptor>();
        InterfaceBuilder? currentInterface = null;
        EndpointBuilder? currentEndpoint = null;

        for (int offset = Math.Min((int)d[0], total); offset + 2 <= total;)
        {
            int length = d[offset];
            if (length < 2 || offset + length > total)
            {
                problems.Add($"descriptor at offset {offset} claims bLength {length}; parsing stopped there");
                break;
            }
            var desc = d.Slice(offset, length);
            switch (desc[1])
            {
                case UsbDescriptorType.Interface when length >= 9:
                    currentInterface = new InterfaceBuilder(desc[2], desc[3], desc[5], desc[6], desc[7], desc[8]);
                    currentEndpoint = null;
                    interfaces.Add(currentInterface);
                    break;

                case UsbDescriptorType.Endpoint when length >= 7:
                    if (currentInterface is null)
                    {
                        problems.Add($"endpoint 0x{desc[2]:X2} at offset {offset} precedes any interface");
                        break;
                    }
                    ushort packet = BinaryPrimitives.ReadUInt16LittleEndian(desc[4..]);
                    currentEndpoint = new EndpointBuilder(desc[2], (UsbEndpointType)(desc[3] & 0x03), (ushort)(packet & 0x07FF), (byte)((packet >> 11) & 0x03), desc[6]);
                    currentInterface.Endpoints.Add(currentEndpoint);
                    break;

                case UsbDescriptorType.InterfaceAssociation when length >= 8:
                    associations.Add(new UsbInterfaceAssociation(desc[2], desc[3], desc[4], desc[5], desc[6], desc[7]));
                    break;

                case UsbDescriptorType.SuperSpeedEndpointCompanion when length >= 6 && currentEndpoint is not null:
                    currentEndpoint.Companion = new UsbSuperSpeedCompanion(desc[2], desc[3], BinaryPrimitives.ReadUInt16LittleEndian(desc[4..]));
                    break;

                case UsbDescriptorType.ClassFunctional when currentInterface is { Class: UsbClassCodes.Hid } && length >= 9:
                    // HID 1.11 §6.2.1: the first class descriptor listed is the report descriptor.
                    if (desc[6] == UsbDescriptorType.HidReport)
                        currentInterface.HidReportLength = BinaryPrimitives.ReadUInt16LittleEndian(desc[7..]);
                    currentInterface.ClassSpecific.Add(new(desc[1], "HID", desc.ToArray()));
                    break;

                default:
                    var extra = new UsbClassSpecificDescriptor(desc[1], KindOf(desc[1], currentInterface?.Class, currentEndpoint is not null), desc.ToArray());
                    if (currentEndpoint is not null && desc[1] == UsbDescriptorType.ClassSpecificEndpoint) currentEndpoint.ClassSpecific.Add(extra);
                    else if (currentInterface is not null) currentInterface.ClassSpecific.Add(extra);
                    else unattached.Add(extra);
                    break;
            }
            offset += length;
        }

        return new UsbConfiguration(d[5], d[6], (d[7] & 0x40) != 0, (d[7] & 0x20) != 0,
            d[8] * (speed >= UsbSpeed.Super ? 8 : 2),
            interfaces.Select(i => i.Build()).ToList(), associations, unattached, problems, d[..total].ToArray());
    }

    /// <summary>Parses a BOS descriptor set; a capability that overruns the set ends the walk.</summary>
    public static UsbBos ParseBos(ReadOnlySpan<byte> d)
    {
        var capabilities = new List<UsbBosCapability>();
        UsbMsOs20Platform? msOs20 = null;
        UsbWebUsbPlatform? webUsb = null;
        if (d.Length < 5 || d[1] != UsbDescriptorType.Bos) return new UsbBos([], null, null, d.ToArray());

        int total = Math.Min(BinaryPrimitives.ReadUInt16LittleEndian(d[2..]), d.Length);
        for (int offset = d[0]; offset + 3 <= total;)
        {
            int length = d[offset];
            if (length < 3 || offset + length > total) break;
            var cap = d.Slice(offset, length);
            if (cap[1] == UsbDescriptorType.DeviceCapability)
            {
                var (name, detail) = Describe(cap, ref msOs20, ref webUsb);
                capabilities.Add(new UsbBosCapability(cap[2], name, detail, cap.ToArray()));
            }
            offset += length;
        }
        return new UsbBos(capabilities, msOs20, webUsb, d[..total].ToArray());
    }

    /// <summary>UTF-16LE text of a string descriptor, bounded by its own bLength.</summary>
    public static string? ParseString(ReadOnlySpan<byte> d)
    {
        if (d.Length < 2 || d[1] != UsbDescriptorType.String) return null;
        int length = Math.Min(d[0], d.Length);
        return length <= 2 ? "" : MemoryMarshal.Cast<byte, char>(d[2..(2 + ((length - 2) & ~1))]).ToString().TrimEnd('\0');
    }

    /// <summary>The LANGID array from string descriptor zero.</summary>
    public static ushort[] ParseLanguageIds(ReadOnlySpan<byte> d)
    {
        if (d.Length < 4 || d[1] != UsbDescriptorType.String) return [];
        int length = Math.Min(d[0], d.Length);
        var ids = new ushort[(length - 2) / 2];
        for (int i = 0; i < ids.Length; i++) ids[i] = BinaryPrimitives.ReadUInt16LittleEndian(d[(2 + i * 2)..]);
        return ids;
    }

    static (string, string) Describe(ReadOnlySpan<byte> c, ref UsbMsOs20Platform? msOs20, ref UsbWebUsbPlatform? webUsb)
    {
        switch (c[2])
        {
            case 0x02 when c.Length >= 7:
                return ("USB 2.0 Extension", (BinaryPrimitives.ReadUInt32LittleEndian(c[3..]) & 0x2) != 0 ? "Link Power Management" : "no LPM");
            case 0x03 when c.Length >= 10:
                return ("SuperSpeed USB", SpeedsSupported(BinaryPrimitives.ReadUInt16LittleEndian(c[4..])));
            case 0x04 when c.Length >= 20:
                return ("Container ID", new Guid(c.Slice(4, 16)).ToString());
            case 0x05 when c.Length >= 20:
                var platform = new Guid(c.Slice(4, 16));
                var data = c[20..];
                if (platform == MsOs20PlatformId && data.Length >= 8)
                {
                    msOs20 = new UsbMsOs20Platform(BinaryPrimitives.ReadUInt32LittleEndian(data), BinaryPrimitives.ReadUInt16LittleEndian(data[4..]), data[6], data[7]);
                    return ("Microsoft OS 2.0", $"Windows 0x{msOs20.WindowsVersion:X8}, set {msOs20.DescriptorSetLength} bytes, vendor code 0x{msOs20.VendorCode:X2}");
                }
                if (platform == WebUsbPlatformId && data.Length >= 4)
                {
                    webUsb = new UsbWebUsbPlatform(new UsbBcd(BinaryPrimitives.ReadUInt16LittleEndian(data)), data[2], data[3]);
                    return ("WebUSB", $"version {webUsb.Version}, vendor code 0x{webUsb.VendorCode:X2}, landing page string {webUsb.LandingPageIndex}");
                }
                return ("Platform", platform.ToString());
            case 0x0A:
                return ("SuperSpeedPlus USB", "");
            case 0x0D:
                return ("Billboard", "USB Type-C alternate modes");
            default:
                return ($"Capability 0x{c[2]:X2}", "");
        }
    }

    static string SpeedsSupported(ushort bits) =>
        string.Join(", ", new[] { (1, "Low"), (2, "Full"), (4, "High"), (8, "5 Gbps") }.Where(s => (bits & s.Item1) != 0).Select(s => s.Item2));

    static string KindOf(byte type, byte? interfaceClass, bool afterEndpoint) => (type, interfaceClass) switch
    {
        (UsbDescriptorType.ClassFunctional, UsbClassCodes.ApplicationSpecific) => "DFU functional",
        (UsbDescriptorType.ClassFunctional, UsbClassCodes.SmartCard) => "CCID",
        (UsbDescriptorType.ClassSpecificInterface, UsbClassCodes.Audio) => "Audio class-specific interface",
        (UsbDescriptorType.ClassSpecificEndpoint, UsbClassCodes.Audio) => "Audio class-specific endpoint",
        (UsbDescriptorType.ClassSpecificInterface, UsbClassCodes.CdcControl or UsbClassCodes.CdcData) => "CDC functional",
        (UsbDescriptorType.ClassSpecificInterface, UsbClassCodes.Video) => "Video class-specific interface",
        (UsbDescriptorType.ClassSpecificEndpoint, UsbClassCodes.Video) => "Video class-specific endpoint",
        (UsbDescriptorType.SuperSpeedPlusIsochronousCompanion, _) => "SuperSpeedPlus isochronous companion",
        (UsbDescriptorType.ClassSpecificInterface, _) => "Class-specific interface",
        (UsbDescriptorType.ClassSpecificEndpoint, _) => "Class-specific endpoint",
        _ => afterEndpoint ? $"Descriptor 0x{type:X2} after endpoint" : $"Descriptor 0x{type:X2}",
    };

    sealed class InterfaceBuilder(byte number, byte alternate, byte cls, byte sub, byte protocol, byte nameIndex)
    {
        public byte Class => cls;
        public List<EndpointBuilder> Endpoints { get; } = [];
        public List<UsbClassSpecificDescriptor> ClassSpecific { get; } = [];
        public ushort? HidReportLength { get; set; }

        public UsbInterfaceSetting Build() =>
            new(number, alternate, cls, sub, protocol, nameIndex, Endpoints.Select(e => e.Build()).ToList(), ClassSpecific) { HidReportDescriptorLength = HidReportLength };
    }

    sealed class EndpointBuilder(byte address, UsbEndpointType type, ushort maxPacket, byte additional, byte interval)
    {
        public UsbSuperSpeedCompanion? Companion { get; set; }
        public List<UsbClassSpecificDescriptor> ClassSpecific { get; } = [];
        public UsbEndpoint Build() => new(address, type, maxPacket, additional, interval, Companion, ClassSpecific);
    }
}
