namespace Nexaflow.IO.Usb.Descriptors.Layout;

/// <summary>Names the standard gives to descriptor-level values, shared by every decoder.</summary>
public static class UsbDescriptorNames
{
    public static string? Type(byte type) => type switch
    {
        UsbDescriptorType.Device => "Device",
        UsbDescriptorType.Configuration => "Configuration",
        UsbDescriptorType.String => "String",
        UsbDescriptorType.Interface => "Interface",
        UsbDescriptorType.Endpoint => "Endpoint",
        0x06 => "Device qualifier",
        0x07 => "Other speed configuration",
        0x08 => "Interface power",
        0x09 => "OTG",
        0x0A => "Debug",
        UsbDescriptorType.InterfaceAssociation => "Interface association",
        UsbDescriptorType.Bos => "BOS",
        UsbDescriptorType.DeviceCapability => "Device capability",
        UsbDescriptorType.ClassFunctional => "Class functional",
        UsbDescriptorType.HidReport => "HID report",
        0x23 => "HID physical",
        UsbDescriptorType.ClassSpecificInterface => "Class-specific interface",
        UsbDescriptorType.ClassSpecificEndpoint => "Class-specific endpoint",
        0x29 => "Hub",
        0x2A => "SuperSpeed hub",
        UsbDescriptorType.SuperSpeedEndpointCompanion => "SuperSpeed endpoint companion",
        UsbDescriptorType.SuperSpeedPlusIsochronousCompanion => "SuperSpeedPlus isochronous companion",
        _ => null,
    };

    public static string? Capability(byte type) => type switch
    {
        0x01 => "Wireless USB",
        0x02 => "USB 2.0 Extension",
        0x03 => "SuperSpeed USB",
        0x04 => "Container ID",
        0x05 => "Platform",
        0x06 => "Power Delivery",
        0x07 => "Battery Info",
        0x08 => "PD Consumer Port",
        0x09 => "PD Provider Port",
        0x0A => "SuperSpeedPlus USB",
        0x0B => "Precision Time Measurement",
        0x0C => "Wireless USB Ext",
        0x0D => "Billboard",
        0x0E => "Authentication",
        0x0F => "Billboard Ex",
        0x10 => "Configuration Summary",
        _ => null,
    };

    public static string Direction(byte endpointAddress) => (endpointAddress & 0x80) != 0 ? "IN" : "OUT";

    public static string Endpoint(byte address) => $"EP {address & 0x0F} {Direction(address)}";

    /// <summary>bmAttributes of an endpoint: the transfer type, and for isochronous its sync and usage.</summary>
    public static string EndpointAttributes(byte attributes)
    {
        var type = (UsbEndpointType)(attributes & 0x03);
        if (type != UsbEndpointType.Isochronous) return type.ToString();
        string sync = ((attributes >> 2) & 0x03) switch { 0 => "no sync", 1 => "asynchronous", 2 => "adaptive", _ => "synchronous" };
        string usage = ((attributes >> 4) & 0x03) switch { 0 => "data", 1 => "feedback", 2 => "implicit feedback", _ => "reserved usage" };
        return $"Isochronous, {sync}, {usage}";
    }

    /// <summary>wMaxPacketSize: the size, and at high speed the extra transactions per microframe in bits 12:11.</summary>
    public static string MaxPacket(ushort raw)
    {
        int size = raw & 0x07FF, extra = (raw >> 11) & 0x03;
        return extra == 0 ? $"{size} bytes" : $"{size} bytes × {extra + 1} per microframe";
    }

    /// <summary>
    /// bInterval as time. Full/low-speed interrupt endpoints count frames directly; everything at high speed or
    /// faster, and isochronous at any speed, is 2^(n-1) (micro)frames.
    /// </summary>
    public static string? Interval(byte interval, byte attributes, UsbSpeed speed)
    {
        var type = (UsbEndpointType)(attributes & 0x03);
        if (type is UsbEndpointType.Control or UsbEndpointType.Bulk)
            return interval == 0 ? "no NAK limit" : speed == UsbSpeed.High ? $"at most 1 NAK per {interval} microframe(s)" : null;
        if (interval == 0) return "invalid (0)";
        bool microframes = speed >= UsbSpeed.High;
        if (type == UsbEndpointType.Interrupt && !microframes && speed != UsbSpeed.Unknown) return $"{interval} ms";
        if (speed == UsbSpeed.Unknown) return null;
        double units = Math.Pow(2, Math.Min(interval, (byte)16) - 1);
        double micros = units * (microframes ? 125 : 1000);
        return micros >= 1000 ? $"{micros / 1000:0.###} ms" : $"{micros:0} µs";
    }

    /// <summary>A sampling rate in hertz as people say it.</summary>
    public static string Hertz(uint hz) => hz % 1000 == 0 ? $"{hz / 1000} kHz" : $"{hz / 1000.0:0.###} kHz";

    /// <summary>The flags that are set, by name; unknown set bits are listed by position.</summary>
    public static string Flags(uint value, params (uint Bit, string Name)[] known)
    {
        var set = new List<string>();
        uint unnamed = value;
        foreach (var (bit, name) in known)
        {
            if ((value & bit) != 0) set.Add(name);
            unnamed &= ~bit;
        }
        for (int i = 0; i < 32; i++)
            if ((unnamed & (1u << i)) != 0) set.Add($"bit {i}");
        return set.Count == 0 ? "none" : string.Join(", ", set);
    }
}
