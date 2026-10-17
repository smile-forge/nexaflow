namespace Nexaflow.IO.Usb.Descriptors.Layout;

/// <summary>The descriptors USB 2.0 chapter 9 and USB 3.2 §9.6 define for every device.</summary>
public sealed class StandardDescriptorDecoder : IUsbDescriptorDecoder
{
    static readonly Guid MsOs20PlatformId = new("d8dd60df-4589-4cc7-9cd2-659d9e648a9f");
    static readonly Guid WebUsbPlatformId = new("3408b638-09a9-47a0-8bfd-a0768815b665");

    public UsbDescriptorHeading? TryDecode(UsbFieldWriter w, UsbDescriptorScope scope) => w.Type switch
    {
        UsbDescriptorType.Device => Device(w, scope),
        UsbDescriptorType.Configuration => Configuration(w, scope),
        UsbDescriptorType.Interface => Interface(w, scope),
        UsbDescriptorType.Endpoint => Endpoint(w, scope),
        UsbDescriptorType.InterfaceAssociation => Association(w, scope),
        UsbDescriptorType.SuperSpeedEndpointCompanion => Companion(w),
        UsbDescriptorType.SuperSpeedPlusIsochronousCompanion => PlusCompanion(w),
        UsbDescriptorType.Bos => Bos(w),
        UsbDescriptorType.DeviceCapability => Capability(w, scope),
        _ => null,
    };

    static void Header(UsbFieldWriter w)
    {
        w.U8("bLength");
        w.U8("bDescriptorType", UsbDescriptorNames.Type);
    }

    /// <summary>A class triple's three fields, each named at the level it means.</summary>
    static (byte Class, byte Sub, byte Protocol) ClassTriple(UsbFieldWriter w, UsbDescriptorScope scope, string prefix)
    {
        var names = scope.Context.Names;
        var b = w.Bytes;
        int at = w.Length - w.Remaining;
        byte cls = b.Length > at ? b[at] : (byte)0, sub = b.Length > at + 1 ? b[at + 1] : (byte)0, protocol = b.Length > at + 2 ? b[at + 2] : (byte)0;
        w.U8($"b{prefix}Class", c => names?.ClassName(c) ?? UsbClassCodes.Name(c, sub, protocol));
        w.U8($"b{prefix}SubClass", s => names?.SubClassName(cls, s));
        w.U8($"b{prefix}Protocol", p => names?.ProtocolName(cls, sub, p));
        return (cls, sub, protocol);
    }

    static string ClassText(UsbDescriptorScope scope, byte cls, byte sub, byte protocol) =>
        scope.Context.Names?.DescribeClass(cls, sub, protocol) ?? UsbClassCodes.Name(cls, sub, protocol);

    static UsbDescriptorHeading Device(UsbFieldWriter w, UsbDescriptorScope scope)
    {
        var names = scope.Context.Names;
        Header(w);
        var usb = w.Bcd("bcdUSB");
        var (cls, sub, protocol) = ClassTriple(w, scope, "Device");
        w.U8("bMaxPacketSize0", v => usb.Major >= 3 ? $"{1 << Math.Min((int)v, 15)} bytes" : $"{v} bytes");
        ushort vid = w.U16("idVendor", v => names?.VendorName(v));
        ushort pid = w.U16("idProduct", p => names?.ProductName(vid, p));
        w.Bcd("bcdDevice");
        w.StringIndex("iManufacturer", scope);
        w.StringIndex("iProduct", scope);
        w.StringIndex("iSerialNumber", scope);
        byte configs = w.U8("bNumConfigurations");
        string what = cls == UsbClassCodes.PerInterface ? "class per interface" : ClassText(scope, cls, sub, protocol);
        return new("Device", $"USB {usb} · {vid:X4}:{pid:X4} · {what} · {configs} configuration(s)");
    }

    static UsbDescriptorHeading Configuration(UsbFieldWriter w, UsbDescriptorScope scope)
    {
        Header(w);
        w.U16("wTotalLength", v => $"{v} bytes");
        byte interfaces = w.U8("bNumInterfaces");
        byte value = w.U8("bConfigurationValue");
        w.StringIndex("iConfiguration", scope);
        byte attributes = w.U8("bmAttributes", a => UsbDescriptorNames.Flags(a, (0x80, "reserved (set)"), (0x40, "self-powered"), (0x20, "remote wakeup")));
        int unit = scope.Context.Speed >= UsbSpeed.Super ? 8 : 2;
        byte power = w.U8("bMaxPower", p => $"{p * unit} mA");
        string powered = (attributes & 0x40) != 0 ? "self-powered" : $"bus-powered, {power * unit} mA";
        return new("Configuration", $"Configuration {value} · {interfaces} interface(s) · {powered}");
    }

    static UsbDescriptorHeading Interface(UsbFieldWriter w, UsbDescriptorScope scope)
    {
        Header(w);
        byte number = w.U8("bInterfaceNumber");
        byte alternate = w.U8("bAlternateSetting");
        byte endpoints = w.U8("bNumEndpoints");
        var (cls, sub, protocol) = ClassTriple(w, scope, "Interface");
        byte name = w.StringIndex("iInterface", scope);
        string label = scope.Context.StringOf?.Invoke(name) is { } text && name != 0 ? $" · \"{text}\"" : "";
        return new("Interface", $"Interface {number} alt {alternate} · {ClassText(scope, cls, sub, protocol)} · {endpoints} endpoint(s){label}");
    }

    static UsbDescriptorHeading Endpoint(UsbFieldWriter w, UsbDescriptorScope scope)
    {
        var speed = scope.Context.Speed;
        Header(w);
        byte address = w.U8("bEndpointAddress", UsbDescriptorNames.Endpoint);
        byte attributes = w.U8("bmAttributes", UsbDescriptorNames.EndpointAttributes);
        ushort packet = w.U16("wMaxPacketSize", UsbDescriptorNames.MaxPacket);
        w.U8("bInterval", i => UsbDescriptorNames.Interval(i, attributes, speed));
        if (w.Remaining >= 2)
        {
            // Audio 1.0 endpoints are 9 bytes long (UAC1 §4.6.1.1).
            w.U8("bRefresh");
            w.U8("bSynchAddress", a => a == 0 ? "none" : UsbDescriptorNames.Endpoint(a));
        }
        return new("Endpoint", $"{UsbDescriptorNames.Endpoint(address)} (0x{address:X2}) · {UsbDescriptorNames.EndpointAttributes(attributes)} · {UsbDescriptorNames.MaxPacket(packet)}");
    }

    static UsbDescriptorHeading Association(UsbFieldWriter w, UsbDescriptorScope scope)
    {
        Header(w);
        byte first = w.U8("bFirstInterface");
        byte count = w.U8("bInterfaceCount");
        var (cls, sub, protocol) = ClassTriple(w, scope, "Function");
        w.StringIndex("iFunction", scope);
        string span = count <= 1 ? $"Interface {first}" : $"Interfaces {first}–{first + count - 1}";
        return new("Interface association", $"{span} · {ClassText(scope, cls, sub, protocol)}");
    }

    static UsbDescriptorHeading Companion(UsbFieldWriter w)
    {
        Header(w);
        byte burst = w.U8("bMaxBurst", b => $"up to {b + 1} packet(s) per burst");
        w.U8("bmAttributes", a => $"bulk: {((a & 0x1F) == 0 ? "no" : (1 << (a & 0x1F)).ToString())} streams; isochronous: Mult {a & 0x03}");
        ushort bytes = w.U16("wBytesPerInterval", v => $"{v} bytes");
        return new("SuperSpeed endpoint companion", $"burst {burst + 1} · {bytes} bytes per interval");
    }

    static UsbDescriptorHeading PlusCompanion(UsbFieldWriter w)
    {
        Header(w);
        w.U16("wReserved");
        uint bytes = w.U32("dwBytesPerInterval", v => $"{v} bytes");
        return new("SuperSpeedPlus isochronous companion", $"{bytes} bytes per interval");
    }

    static UsbDescriptorHeading Bos(UsbFieldWriter w)
    {
        Header(w);
        ushort total = w.U16("wTotalLength", v => $"{v} bytes");
        byte caps = w.U8("bNumDeviceCaps");
        return new("BOS", $"{caps} capabilit{(caps == 1 ? "y" : "ies")} · {total} bytes");
    }

    static UsbDescriptorHeading Capability(UsbFieldWriter w, UsbDescriptorScope scope)
    {
        Header(w);
        byte type = w.U8("bDevCapabilityType", UsbDescriptorNames.Capability);
        string name = UsbDescriptorNames.Capability(type) ?? $"Capability 0x{type:X2}";
        var (kind, detail) = type switch
        {
            0x02 => (name, Usb2Extension(w)),
            0x03 => (name, SuperSpeed(w)),
            0x04 => (name, ContainerId(w)),
            0x05 => Platform(w, scope),
            0x0A => (name, SuperSpeedPlus(w)),
            _ => (name, ""),
        };
        w.Rest("Capability data");
        return new(kind, detail.Length == 0 ? $"{w.Length} bytes" : detail);
    }

    static string Usb2Extension(UsbFieldWriter w)
    {
        uint a = w.U32("bmAttributes", v => UsbDescriptorNames.Flags(v, (0x02, "LPM"), (0x04, "BESL/alt HIRD"), (0x08, "baseline BESL valid"), (0x10, "deep BESL valid")));
        return (a & 0x02) != 0 ? "Link Power Management" : "no LPM";
    }

    static string SuperSpeed(UsbFieldWriter w)
    {
        w.U8("bmAttributes", v => (v & 0x02) != 0 ? "Latency Tolerance Messages" : "no LTM");
        ushort speeds = w.U16("wSpeedsSupported", v => UsbDescriptorNames.Flags(v, (1, "low"), (2, "full"), (4, "high"), (8, "5 Gbps")));
        w.U8("bFunctionalitySupport", v => v switch { 0 => "low speed", 1 => "full speed", 2 => "high speed", 3 => "5 Gbps", _ => null });
        w.U8("bU1DevExitLat", v => $"{v} µs");
        w.U16("wU2DevExitLat", v => $"{v} µs");
        return UsbDescriptorNames.Flags(speeds, (1, "low"), (2, "full"), (4, "high"), (8, "5 Gbps"));
    }

    static string ContainerId(UsbFieldWriter w)
    {
        w.U8("bReserved");
        return w.Uuid("ContainerID").ToString();
    }

    /// <summary>A platform capability, named after the platform its UUID identifies.</summary>
    static (string Kind, string Detail) Platform(UsbFieldWriter w, UsbDescriptorScope scope)
    {
        w.U8("bReserved");
        var platform = w.Uuid("PlatformCapabilityUUID", g => g == MsOs20PlatformId ? "Microsoft OS 2.0" : g == WebUsbPlatformId ? "WebUSB" : null);
        if (platform == MsOs20PlatformId)
        {
            uint windows = w.U32("dwWindowsVersion", v => v >= 0x06030000 ? "Windows 8.1 or later" : null);
            ushort set = w.U16("wMSOSDescriptorSetTotalLength", v => $"{v} bytes");
            byte vendor = w.U8("bMS_VendorCode", v => "the bRequest that returns the descriptor set");
            w.U8("bAltEnumCode", v => v == 0 ? "no alternate enumeration" : null);
            return ("Microsoft OS 2.0", $"Windows 0x{windows:X8} · set {set} bytes · vendor code 0x{vendor:X2}");
        }
        if (platform == WebUsbPlatformId)
        {
            var version = w.Bcd("bcdVersion");
            byte vendor = w.U8("bVendorCode", v => "the bRequest for WebUSB requests");
            w.StringIndex("iLandingPage", scope);
            return ("WebUSB", $"version {version} · vendor code 0x{vendor:X2}");
        }
        return ("Platform", platform.ToString());
    }

    static string SuperSpeedPlus(UsbFieldWriter w)
    {
        w.U8("bReserved");
        uint a = w.U32("bmAttributes", v => $"{(v & 0x1F) + 1} sublink speed attribute(s), {((v >> 5) & 0x0F) + 1} speed ID(s)");
        w.U16("wFunctionalitySupport");
        w.U16("wReserved");
        int count = (int)(a & 0x1F) + 1;
        for (int i = 0; i < count && w.Remaining >= 4; i++)
            w.U32($"bmSublinkSpeedAttr[{i}]", SublinkSpeed);
        return $"{count} sublink speed attribute(s)";
    }

    static string SublinkSpeed(uint v)
    {
        string unit = ((v >> 4) & 0x03) switch { 0 => "b/s", 1 => "Kb/s", 2 => "Mb/s", _ => "Gb/s" };
        string direction = ((v >> 7) & 1) == 0 ? "symmetric" : ((v >> 6) & 1) == 0 ? "receive" : "transmit";
        return $"ID {v & 0x0F}: {v >> 16} {unit}, {direction}";
    }
}
