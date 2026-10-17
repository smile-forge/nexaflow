namespace Nexaflow.IO.Usb.Descriptors;

/// <summary>
/// usb.org's defined class codes, with readable names for the class/subclass/protocol triples a probe meets.
/// These are fixed by the standard, so they live in code; <see cref="Naming.UsbIdsDatabase"/> names the rest.
/// </summary>
public static class UsbClassCodes
{
    public const byte PerInterface = 0x00;
    public const byte Audio = 0x01;
    public const byte CdcControl = 0x02;
    public const byte Hid = 0x03;
    public const byte Physical = 0x05;
    public const byte Image = 0x06;
    public const byte Printer = 0x07;
    public const byte MassStorage = 0x08;
    public const byte Hub = 0x09;
    public const byte CdcData = 0x0A;
    public const byte SmartCard = 0x0B;
    public const byte ContentSecurity = 0x0D;
    public const byte Video = 0x0E;
    public const byte PersonalHealthcare = 0x0F;
    public const byte AudioVideo = 0x10;
    public const byte Billboard = 0x11;
    public const byte TypeCBridge = 0x12;
    public const byte Diagnostic = 0xDC;
    public const byte WirelessController = 0xE0;
    public const byte Miscellaneous = 0xEF;
    public const byte ApplicationSpecific = 0xFE;
    public const byte VendorSpecific = 0xFF;

    public static string Name(byte cls, byte sub, byte protocol) => cls switch
    {
        PerInterface => "Defined per interface",
        Audio => sub switch { 1 => "Audio Control", 2 => "Audio Streaming", 3 => "MIDI Streaming", _ => "Audio" },
        CdcControl => sub switch { 2 => "CDC ACM (serial)", 6 => "CDC ECM (Ethernet)", 0x0D => "CDC NCM (Ethernet)", 0x0E => "CDC MBIM", _ => "CDC Control" },
        Hid => (sub, protocol) switch { (1, 1) => "HID Keyboard", (1, 2) => "HID Mouse", _ => "HID" },
        Physical => "Physical",
        Image => "Still Image (PTP/MTP)",
        Printer => "Printer",
        MassStorage => (sub, protocol) switch { (6, 0x62) => "Mass Storage (UAS)", (6, _) => "Mass Storage (SCSI)", _ => "Mass Storage" },
        Hub => "Hub",
        CdcData => "CDC Data",
        SmartCard => "Smart Card (CCID)",
        ContentSecurity => "Content Security",
        Video => sub switch { 1 => "Video Control", 2 => "Video Streaming", 3 => "Video Interface Collection", _ => "Video" },
        PersonalHealthcare => "Personal Healthcare",
        AudioVideo => "Audio/Video",
        Billboard => "Billboard",
        TypeCBridge => "USB Type-C Bridge",
        Diagnostic => "Diagnostic",
        WirelessController => (sub, protocol) switch { (1, 1) => "Bluetooth", (1, 3) => "RNDIS", _ => "Wireless Controller" },
        Miscellaneous => (sub, protocol) switch { (2, 1) => "Interface Association", (4, _) => "RNDIS", _ => "Miscellaneous" },
        ApplicationSpecific => sub switch { 1 => "DFU (firmware update)", 2 => "IrDA Bridge", 3 => "Test & Measurement (USBTMC)", _ => "Application Specific" },
        VendorSpecific => "Vendor Specific",
        _ => $"Class 0x{cls:X2}",
    };
}
