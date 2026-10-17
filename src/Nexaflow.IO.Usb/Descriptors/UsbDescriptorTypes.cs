namespace Nexaflow.IO.Usb.Descriptors;

/// <summary>The bDescriptorType values the parser acts on — USB 2.0 §9.4, USB 3.2 §9.6, HID 1.11 §7.1.</summary>
public static class UsbDescriptorType
{
    public const byte Device = 0x01;
    public const byte Configuration = 0x02;
    public const byte String = 0x03;
    public const byte Interface = 0x04;
    public const byte Endpoint = 0x05;
    public const byte InterfaceAssociation = 0x0B;
    public const byte Bos = 0x0F;
    public const byte DeviceCapability = 0x10;
    public const byte ClassFunctional = 0x21;
    public const byte HidReport = 0x22;
    public const byte ClassSpecificInterface = 0x24;
    public const byte ClassSpecificEndpoint = 0x25;
    public const byte SuperSpeedEndpointCompanion = 0x30;
    public const byte SuperSpeedPlusIsochronousCompanion = 0x31;
}

public enum UsbSpeed { Unknown, Low, Full, High, Super, SuperPlus }

public enum UsbEndpointType { Control, Isochronous, Bulk, Interrupt }

public enum UsbDirection { Out, In }

/// <summary>A binary-coded-decimal version field (bcdUSB, bcdDevice): 0x0210 reads "2.10".</summary>
public readonly record struct UsbBcd(ushort Raw)
{
    public byte Major => (byte)(Raw >> 8);
    public byte Minor => (byte)Raw;
    public override string ToString() => $"{Raw >> 8:X}.{Raw & 0xFF:X2}";
}
