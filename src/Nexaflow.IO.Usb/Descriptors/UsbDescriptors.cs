using System.Buffers.Binary;

namespace Nexaflow.IO.Usb.Descriptors;

/// <summary>The 18-byte device descriptor (USB 2.0 §9.6.1).</summary>
public sealed record UsbDeviceDescriptor(
    UsbBcd UsbVersion, byte Class, byte SubClass, byte Protocol, byte MaxPacketSize0,
    ushort VendorId, ushort ProductId, UsbBcd DeviceVersion,
    byte ManufacturerIndex, byte ProductIndex, byte SerialNumberIndex, byte ConfigurationCount)
{
    public const int Length = 18;

    /// <summary>True when the device may carry a BOS descriptor (bcdUSB 2.01 or later).</summary>
    public bool HasBos => UsbVersion.Raw >= 0x0201;

    public static bool TryParse(ReadOnlySpan<byte> d, out UsbDeviceDescriptor descriptor)
    {
        descriptor = null!;
        if (d.Length < Length || d[0] < Length || d[1] != UsbDescriptorType.Device) return false;
        descriptor = new UsbDeviceDescriptor(
            new UsbBcd(BinaryPrimitives.ReadUInt16LittleEndian(d[2..])), d[4], d[5], d[6], d[7],
            BinaryPrimitives.ReadUInt16LittleEndian(d[8..]), BinaryPrimitives.ReadUInt16LittleEndian(d[10..]),
            new UsbBcd(BinaryPrimitives.ReadUInt16LittleEndian(d[12..])), d[14], d[15], d[16], d[17]);
        return true;
    }
}

/// <summary>A descriptor the parser keeps as bytes, labelled by what it is where it was found.</summary>
public sealed record UsbClassSpecificDescriptor(byte Type, string Kind, byte[] Raw);

/// <summary>The SuperSpeed endpoint companion (USB 3.2 §9.6.7).</summary>
public sealed record UsbSuperSpeedCompanion(byte MaxBurst, byte Attributes, ushort BytesPerInterval);

public sealed record UsbEndpoint(
    byte Address, UsbEndpointType Type, ushort MaxPacketSize, byte AdditionalTransactions, byte Interval,
    UsbSuperSpeedCompanion? Companion, IReadOnlyList<UsbClassSpecificDescriptor> ClassSpecific)
{
    public UsbDirection Direction => (Address & 0x80) != 0 ? UsbDirection.In : UsbDirection.Out;
    public byte Number => (byte)(Address & 0x0F);
}

/// <summary>One alternate setting of one interface, with its endpoints and class-specific descriptors.</summary>
public sealed record UsbInterfaceSetting(
    byte Number, byte AlternateSetting, byte Class, byte SubClass, byte Protocol, byte NameIndex,
    IReadOnlyList<UsbEndpoint> Endpoints, IReadOnlyList<UsbClassSpecificDescriptor> ClassSpecific)
{
    /// <summary>wDescriptorLength of the report descriptor, read from a HID interface's class descriptor.</summary>
    public ushort? HidReportDescriptorLength { get; init; }
}

/// <summary>An interface association (IAD): one function spanning several consecutive interfaces.</summary>
public sealed record UsbInterfaceAssociation(byte FirstInterface, byte InterfaceCount, byte Class, byte SubClass, byte Protocol, byte NameIndex)
{
    public bool Covers(byte interfaceNumber) => interfaceNumber >= FirstInterface && interfaceNumber < FirstInterface + InterfaceCount;
}

/// <summary>
/// A parsed configuration descriptor. Arbitrary devices ship broken descriptors, so a malformed tail is
/// reported in <see cref="Problems"/> and everything before it is kept rather than the whole parse failing.
/// </summary>
public sealed record UsbConfiguration(
    byte Value, byte NameIndex, bool SelfPowered, bool RemoteWakeup, int MaxPowerMilliamps,
    IReadOnlyList<UsbInterfaceSetting> Interfaces, IReadOnlyList<UsbInterfaceAssociation> Associations,
    IReadOnlyList<UsbClassSpecificDescriptor> Unattached, IReadOnlyList<string> Problems, byte[] Raw)
{
    public IEnumerable<UsbInterfaceSetting> DefaultSettings => Interfaces.Where(i => i.AlternateSetting == 0);

    public UsbInterfaceAssociation? AssociationOf(byte interfaceNumber) => Associations.FirstOrDefault(a => a.Covers(interfaceNumber));
}

public sealed record UsbMsOs20Platform(uint WindowsVersion, ushort DescriptorSetLength, byte VendorCode, byte AlternateEnumerationCode);

public sealed record UsbWebUsbPlatform(UsbBcd Version, byte VendorCode, byte LandingPageIndex);

public sealed record UsbBosCapability(byte Type, string Name, string Detail, byte[] Raw);

/// <summary>The BOS descriptor set (USB 3.2 §9.6.2) with the two platform capabilities Nexaflow acts on lifted out.</summary>
public sealed record UsbBos(IReadOnlyList<UsbBosCapability> Capabilities, UsbMsOs20Platform? MsOs20, UsbWebUsbPlatform? WebUsb, byte[] Raw);
