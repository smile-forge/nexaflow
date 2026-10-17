namespace Nexaflow.IO.Usb.Descriptors.Layout;

/// <summary>
/// USB Video Class descriptors (UVC 1.5): the control chain a camera exposes — including extension units, whose
/// GUIDs name the vendor protocols a camera answers — and the formats and frame sizes it streams.
/// </summary>
public sealed class VideoDescriptorDecoder : IUsbDescriptorDecoder
{
    const byte VideoControl = 0x01, VideoStreaming = 0x02;

    public UsbDescriptorHeading? TryDecode(UsbFieldWriter w, UsbDescriptorScope scope)
    {
        if (w.Type is not (UsbDescriptorType.ClassSpecificInterface or UsbDescriptorType.ClassSpecificEndpoint)) return null;
        if (scope.Interface is not { Class: UsbClassCodes.Video } iface) return null;

        w.U8("bLength");
        w.U8("bDescriptorType", UsbDescriptorNames.Type);
        bool endpoint = w.Type == UsbDescriptorType.ClassSpecificEndpoint;
        byte subtype = w.U8("bDescriptorSubtype", s => SubtypeName(iface.SubClass, endpoint, s));
        string name = SubtypeName(iface.SubClass, endpoint, subtype) is { } known ? $"Video {known}" : $"Video subtype 0x{subtype:X2}";

        string summary = (iface.SubClass, endpoint, subtype) switch
        {
            (VideoControl, false, 0x01) => Header(w),
            (VideoControl, false, 0x02) => InputTerminal(w, scope),
            (VideoControl, false, 0x03) => OutputTerminal(w, scope),
            (VideoControl, false, 0x05) => ProcessingUnit(w, scope),
            (VideoControl, false, 0x06) => ExtensionUnit(w, scope),
            (VideoControl, true, 0x03) => $"interrupt transfers up to {w.U16("wMaxTransferSize", v => $"{v} bytes")} bytes",
            (VideoStreaming, false, 0x01) => InputHeader(w),
            (VideoStreaming, false, 0x04) => FormatWithGuid(w, frameBased: false),
            (VideoStreaming, false, 0x10) => FormatWithGuid(w, frameBased: true),
            (VideoStreaming, false, 0x06) => FormatMjpeg(w),
            (VideoStreaming, false, 0x05 or 0x07) => Frame(w, frameBased: false),
            (VideoStreaming, false, 0x11) => Frame(w, frameBased: true),
            _ => $"{w.Length} bytes",
        };
        return new(name, summary);
    }

    static string? SubtypeName(byte subClass, bool endpoint, byte subtype) => (subClass, endpoint) switch
    {
        (VideoControl, true) => subtype switch { 0x01 => "endpoint general", 0x02 => "endpoint", 0x03 => "interrupt endpoint", _ => null },
        (VideoControl, false) => subtype switch
        {
            0x01 => "control header", 0x02 => "input terminal", 0x03 => "output terminal", 0x04 => "selector unit",
            0x05 => "processing unit", 0x06 => "extension unit", 0x07 => "encoding unit", _ => null,
        },
        (VideoStreaming, false) => subtype switch
        {
            0x01 => "input header", 0x02 => "output header", 0x03 => "still image frame", 0x04 => "uncompressed format",
            0x05 => "uncompressed frame", 0x06 => "MJPEG format", 0x07 => "MJPEG frame", 0x0A => "MPEG2-TS format",
            0x0C => "DV format", 0x0D => "colour matching", 0x10 => "frame-based format", 0x11 => "frame-based frame",
            0x12 => "stream-based format", 0x13 => "H.264 format", 0x14 => "H.264 frame", _ => null,
        },
        _ => null,
    };

    static string? TerminalType(ushort type) => type switch
    {
        0x0100 => "vendor specific", 0x0101 => "USB streaming", 0x0200 => "external", 0x0201 => "camera", 0x0202 => "media transport input",
        0x0300 => "external output", 0x0301 => "display", 0x0302 => "media transport output",
        0x0400 => "external connector", 0x0401 => "composite", 0x0402 => "S-Video", 0x0403 => "component",
        _ => null,
    };

    static string Header(UsbFieldWriter w)
    {
        var version = w.Bcd("bcdUVC");
        w.U16("wTotalLength", v => $"{v} bytes");
        w.U32("dwClockFrequency", v => $"{v / 1_000_000.0:0.###} MHz");
        byte count = w.U8("bInCollection", v => $"{v} streaming interface(s)");
        for (int i = 0; i < count && w.Remaining > 0; i++) w.U8($"baInterfaceNr[{i}]", v => $"interface {v}");
        return $"UVC {version} · {count} streaming interface(s)";
    }

    static string InputTerminal(UsbFieldWriter w, UsbDescriptorScope scope)
    {
        byte id = w.U8("bTerminalID");
        ushort type = w.U16("wTerminalType", TerminalType);
        w.U8("bAssocTerminal", v => v == 0 ? "none" : $"terminal {v}");
        w.StringIndex("iTerminal", scope);
        if (type == 0x0201 && w.Remaining >= 7)
        {
            w.U16("wObjectiveFocalLengthMin");
            w.U16("wObjectiveFocalLengthMax");
            w.U16("wOcularFocalLength");
            byte size = w.U8("bControlSize", v => $"{v} byte(s)");
            w.Raw("bmControls", Math.Min(size, w.Remaining), "camera controls supported, one bit each (UVC 1.5 §3.7.2.3)");
        }
        return $"terminal {id} · {TerminalType(type) ?? $"type 0x{type:X4}"}";
    }

    static string OutputTerminal(UsbFieldWriter w, UsbDescriptorScope scope)
    {
        byte id = w.U8("bTerminalID");
        ushort type = w.U16("wTerminalType", TerminalType);
        w.U8("bAssocTerminal", v => v == 0 ? "none" : $"terminal {v}");
        byte source = w.U8("bSourceID", v => $"unit/terminal {v}");
        w.StringIndex("iTerminal", scope);
        return $"terminal {id} · {TerminalType(type) ?? $"type 0x{type:X4}"} · from {source}";
    }

    static string ProcessingUnit(UsbFieldWriter w, UsbDescriptorScope scope)
    {
        byte id = w.U8("bUnitID");
        byte source = w.U8("bSourceID", v => $"unit/terminal {v}");
        w.U16("wMaxMultiplier");
        byte size = w.U8("bControlSize", v => $"{v} byte(s)");
        w.Raw("bmControls", Math.Min(size, w.Remaining), "processing controls supported, one bit each (UVC 1.5 §3.7.2.5)");
        w.StringIndex("iProcessing", scope);
        if (w.Remaining > 0) w.U8("bmVideoStandards");
        return $"unit {id} · from {source}";
    }

    /// <summary>An extension unit: the GUID names a vendor's protocol, reached through class requests on this unit.</summary>
    static string ExtensionUnit(UsbFieldWriter w, UsbDescriptorScope scope)
    {
        byte id = w.U8("bUnitID");
        var guid = w.Uuid("guidExtensionCode", _ => "vendor-defined control set");
        byte controls = w.U8("bNumControls");
        byte pins = w.U8("bNrInPins");
        for (int i = 0; i < pins && w.Remaining > 0; i++) w.U8($"baSourceID[{i}]", v => $"unit/terminal {v}");
        byte size = w.U8("bControlSize", v => $"{v} byte(s)");
        w.Raw("bmControls", Math.Min(size, w.Remaining));
        w.StringIndex("iExtension", scope);
        return $"unit {id} · {guid:B} · {controls} control(s)";
    }

    static string InputHeader(UsbFieldWriter w)
    {
        byte formats = w.U8("bNumFormats");
        w.U16("wTotalLength", v => $"{v} bytes");
        byte endpoint = w.U8("bEndpointAddress", UsbDescriptorNames.Endpoint);
        w.U8("bmInfo", v => (v & 0x01) != 0 ? "dynamic format change" : "none");
        w.U8("bTerminalLink", v => $"terminal {v}");
        w.U8("bStillCaptureMethod", v => v == 0 ? "none" : $"method {v}");
        w.U8("bTriggerSupport", v => v == 0 ? "no hardware trigger" : "hardware trigger");
        w.U8("bTriggerUsage");
        byte size = w.U8("bControlSize", v => $"{v} byte(s) per format");
        for (int i = 0; i < formats && size > 0 && w.Remaining >= size; i++) w.Raw($"bmaControls[{i}]", size);
        return $"{formats} format(s) · {UsbDescriptorNames.Endpoint(endpoint)}";
    }

    /// <summary>The fourcc a format GUID spells in its first four bytes (YUY2, NV12, H264…).</summary>
    static string? FourCc(Guid guid)
    {
        Span<byte> bytes = stackalloc byte[16];
        guid.TryWriteBytes(bytes);
        foreach (var b in bytes[..4])
            if (b is < 0x20 or > 0x7E) return null;
        return System.Text.Encoding.ASCII.GetString(bytes[..4]).TrimEnd();
    }

    static string FormatWithGuid(UsbFieldWriter w, bool frameBased)
    {
        byte index = w.U8("bFormatIndex");
        byte frames = w.U8("bNumFrameDescriptors");
        var guid = w.Uuid("guidFormat", FourCc);
        w.U8("bBitsPerPixel");
        w.U8("bDefaultFrameIndex");
        w.U8("bAspectRatioX");
        w.U8("bAspectRatioY");
        w.U8("bmInterlaceFlags");
        w.U8("bCopyProtect");
        if (frameBased) w.U8("bVariableSize", v => v != 0 ? "variable-size frames" : "fixed-size frames");
        return $"format {index} · {FourCc(guid) ?? guid.ToString("B")} · {frames} frame size(s)";
    }

    static string FormatMjpeg(UsbFieldWriter w)
    {
        byte index = w.U8("bFormatIndex");
        byte frames = w.U8("bNumFrameDescriptors");
        w.U8("bmFlags", v => (v & 0x01) != 0 ? "fixed-size samples" : "variable-size samples");
        w.U8("bDefaultFrameIndex");
        w.U8("bAspectRatioX");
        w.U8("bAspectRatioY");
        w.U8("bmInterlaceFlags");
        w.U8("bCopyProtect");
        return $"format {index} · MJPEG · {frames} frame size(s)";
    }

    /// <summary>Frame intervals count 100 ns units.</summary>
    static string Fps(uint interval) => interval == 0 ? "?" : $"{10_000_000.0 / interval:0.##} fps";

    static string Frame(UsbFieldWriter w, bool frameBased)
    {
        byte index = w.U8("bFrameIndex");
        w.U8("bmCapabilities", v => UsbDescriptorNames.Flags(v, (0x01, "still image"), (0x02, "fixed frame rate")));
        ushort width = w.U16("wWidth", v => $"{v} px");
        ushort height = w.U16("wHeight", v => $"{v} px");
        w.U32("dwMinBitRate", v => $"{v / 1_000_000.0:0.##} Mb/s");
        w.U32("dwMaxBitRate", v => $"{v / 1_000_000.0:0.##} Mb/s");
        if (!frameBased) w.U32("dwMaxVideoFrameBufferSize", v => $"{v} bytes");
        uint fallback = w.U32("dwDefaultFrameInterval", Fps);
        byte kind = w.U8("bFrameIntervalType", v => v == 0 ? "continuous" : $"{v} discrete interval(s)");
        if (frameBased) w.U32("dwBytesPerLine");
        var rates = new List<string>();
        if (kind == 0)
        {
            uint min = w.U32("dwMinFrameInterval", Fps), max = w.U32("dwMaxFrameInterval", Fps);
            w.U32("dwFrameIntervalStep");
            rates.Add($"{Fps(max)}–{Fps(min)}");
        }
        else
            for (int i = 0; i < kind && w.Remaining >= 4; i++) rates.Add(Fps(w.U32($"dwFrameInterval[{i}]", Fps)));
        return $"frame {index} · {width}×{height} · {(rates.Count > 0 ? string.Join(", ", rates) : Fps(fallback))}";
    }
}
