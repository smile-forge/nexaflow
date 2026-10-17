namespace Nexaflow.IO.Usb.Descriptors.Layout;

/// <summary>
/// Audio class descriptors — USB Audio 1.0 and 2.0 control and streaming, and MIDI streaming. The version comes
/// from the interface protocol (0x20 is UAC2), because the two lay the same subtypes out differently.
/// </summary>
public sealed class AudioDescriptorDecoder : IUsbDescriptorDecoder
{
    const byte AudioControl = 0x01, AudioStreaming = 0x02, MidiStreaming = 0x03;

    public UsbDescriptorHeading? TryDecode(UsbFieldWriter w, UsbDescriptorScope scope)
    {
        if (w.Type is not (UsbDescriptorType.ClassSpecificInterface or UsbDescriptorType.ClassSpecificEndpoint)) return null;
        if (scope.Interface is not { Class: UsbClassCodes.Audio } iface) return null;
        bool v2 = iface.Protocol == 0x20;

        w.U8("bLength");
        w.U8("bDescriptorType", UsbDescriptorNames.Type);
        bool endpoint = w.Type == UsbDescriptorType.ClassSpecificEndpoint;
        byte subtype = w.U8("bDescriptorSubtype", s => SubtypeName(iface.SubClass, endpoint, v2, s));
        string name = SubtypeName(iface.SubClass, endpoint, v2, subtype) is { } known ? $"Audio {known}" : $"Audio subtype 0x{subtype:X2}";

        string summary = (iface.SubClass, endpoint, subtype) switch
        {
            (AudioControl, false, 0x01) => v2 ? Header2(w) : Header1(w),
            (AudioControl, false, 0x02) => v2 ? InputTerminal2(w, scope) : InputTerminal1(w, scope),
            (AudioControl, false, 0x03) => v2 ? OutputTerminal2(w, scope) : OutputTerminal1(w, scope),
            (AudioControl, false, 0x05) => Selector(w, scope, v2),
            (AudioControl, false, 0x06) => v2 ? Feature2(w, scope) : Feature1(w, scope),
            (AudioControl, false, 0x0A) when v2 => ClockSource(w, scope),
            (AudioStreaming, false, 0x01) => v2 ? StreamGeneral2(w, scope) : StreamGeneral1(w),
            (AudioStreaming, false, 0x02) => v2 ? Format2(w) : Format1(w),
            (AudioStreaming, true, 0x01) => v2 ? EndpointGeneral2(w) : EndpointGeneral1(w),
            (MidiStreaming, false, 0x01) => $"MIDI {w.Bcd("bcdMSC")} · {w.U16("wTotalLength", v => $"{v} bytes")} bytes of class descriptors",
            (MidiStreaming, false, 0x02) => MidiInJack(w, scope),
            (MidiStreaming, false, 0x03) => MidiOutJack(w, scope),
            (MidiStreaming, true, 0x01) => MidiEndpoint(w),
            _ => $"{w.Length} bytes",
        };
        return new(name, summary);
    }

    static string? SubtypeName(byte subClass, bool endpoint, bool v2, byte subtype) => (subClass, endpoint) switch
    {
        (_, true) when subClass != MidiStreaming => subtype == 0x01 ? "endpoint general" : null,
        (MidiStreaming, true) => subtype == 0x01 ? "MIDI endpoint general" : null,
        (AudioControl, false) => subtype switch
        {
            0x01 => "control header", 0x02 => "input terminal", 0x03 => "output terminal", 0x04 => "mixer unit",
            0x05 => "selector unit", 0x06 => "feature unit",
            0x07 => v2 ? "effect unit" : "processing unit", 0x08 => v2 ? "processing unit" : "extension unit",
            0x09 when v2 => "extension unit", 0x0A when v2 => "clock source", 0x0B when v2 => "clock selector",
            0x0C when v2 => "clock multiplier", 0x0D when v2 => "sample rate converter",
            _ => null,
        },
        (AudioStreaming, false) => subtype switch
        {
            0x01 => "streaming general", 0x02 => "format type", 0x03 => v2 ? "encoder" : "format specific", 0x04 when v2 => "decoder",
            _ => null,
        },
        (MidiStreaming, false) => subtype switch { 0x01 => "MIDI header", 0x02 => "MIDI IN jack", 0x03 => "MIDI OUT jack", 0x04 => "MIDI element", _ => null },
        _ => null,
    };

    /// <summary>wTerminalType (USB Audio Terminal Types 1.0 / 2.0 — the two share the codes).</summary>
    public static string? TerminalType(ushort type) => type switch
    {
        0x0100 => "USB undefined", 0x0101 => "USB streaming", 0x01FF => "USB vendor specific",
        0x0200 => "input undefined", 0x0201 => "microphone", 0x0202 => "desktop microphone", 0x0203 => "personal microphone",
        0x0204 => "omni-directional microphone", 0x0205 => "microphone array", 0x0206 => "processing microphone array",
        0x0300 => "output undefined", 0x0301 => "speaker", 0x0302 => "headphones", 0x0303 => "head-mounted display audio",
        0x0304 => "desktop speaker", 0x0305 => "room speaker", 0x0306 => "communication speaker", 0x0307 => "low-frequency effects speaker",
        0x0400 => "bidirectional undefined", 0x0401 => "handset", 0x0402 => "headset", 0x0403 => "speakerphone",
        0x0404 => "echo-suppressing speakerphone", 0x0405 => "echo-cancelling speakerphone",
        0x0500 => "telephony undefined", 0x0501 => "phone line", 0x0502 => "telephone", 0x0503 => "down-line phone",
        0x0600 => "external undefined", 0x0601 => "analog connector", 0x0602 => "digital audio interface", 0x0603 => "line connector",
        0x0604 => "legacy audio connector", 0x0605 => "S/PDIF", 0x0606 => "1394 DA stream", 0x0607 => "1394 DV soundtrack",
        0x0700 => "embedded undefined", 0x0703 => "CD player", 0x0706 => "radio receiver", 0x0708 => "synthesizer",
        0x070A => "instrument", 0x070C => "multi-track recorder",
        _ => null,
    };

    static readonly (uint Bit, string Name)[] Channels =
    [
        (1u << 0, "L"), (1u << 1, "R"), (1u << 2, "C"), (1u << 3, "LFE"), (1u << 4, "Ls"), (1u << 5, "Rs"), (1u << 6, "Lc"), (1u << 7, "Rc"),
        (1u << 8, "S"), (1u << 9, "Sl"), (1u << 10, "Sr"), (1u << 11, "T"),
    ];

    static string ChannelConfig(uint v) => v == 0 ? "mono / unpositioned" : UsbDescriptorNames.Flags(v, Channels);

    static readonly string[] FeatureControls =
        ["mute", "volume", "bass", "mid", "treble", "graphic EQ", "AGC", "delay", "bass boost", "loudness", "input gain", "input gain pad", "phase inverter", "underflow", "overflow"];

    static string Terminal(ushort type) => TerminalType(type) ?? $"terminal 0x{type:X4}";

    static string Header1(UsbFieldWriter w)
    {
        var version = w.Bcd("bcdADC");
        w.U16("wTotalLength", v => $"{v} bytes");
        byte count = w.U8("bInCollection", v => $"{v} streaming interface(s)");
        for (int i = 0; i < count && w.Remaining > 0; i++) w.U8($"baInterfaceNr[{i}]", v => $"interface {v}");
        return $"Audio {version} · {count} streaming interface(s)";
    }

    static string Header2(UsbFieldWriter w)
    {
        var version = w.Bcd("bcdADC");
        w.U8("bCategory", c => c switch
        {
            0x01 => "desktop speaker", 0x02 => "home theater", 0x03 => "microphone", 0x04 => "headset", 0x05 => "telephone",
            0x06 => "converter", 0x07 => "voice/sound recorder", 0x08 => "I/O box", 0x09 => "musical instrument",
            0x0A => "pro-audio", 0x0B => "audio/video", 0x0C => "control panel", 0xFF => "other", _ => null,
        });
        w.U16("wTotalLength", v => $"{v} bytes");
        w.U8("bmControls", v => (v & 0x03) != 0 ? "latency control" : "none");
        return $"Audio {version}";
    }

    static string InputTerminal1(UsbFieldWriter w, UsbDescriptorScope scope)
    {
        byte id = w.U8("bTerminalID");
        ushort type = w.U16("wTerminalType", TerminalType);
        w.U8("bAssocTerminal", v => v == 0 ? "none" : $"terminal {v}");
        byte channels = w.U8("bNrChannels");
        w.U16("wChannelConfig", v => ChannelConfig(v));
        w.StringIndex("iChannelNames", scope);
        w.StringIndex("iTerminal", scope);
        return $"terminal {id} · {Terminal(type)} · {channels} channel(s)";
    }

    static string InputTerminal2(UsbFieldWriter w, UsbDescriptorScope scope)
    {
        byte id = w.U8("bTerminalID");
        ushort type = w.U16("wTerminalType", TerminalType);
        w.U8("bAssocTerminal", v => v == 0 ? "none" : $"terminal {v}");
        w.U8("bCSourceID", v => $"clock {v}");
        byte channels = w.U8("bNrChannels");
        w.U32("bmChannelConfig", ChannelConfig);
        w.StringIndex("iChannelNames", scope);
        w.U16("bmControls");
        w.StringIndex("iTerminal", scope);
        return $"terminal {id} · {Terminal(type)} · {channels} channel(s)";
    }

    static string OutputTerminal1(UsbFieldWriter w, UsbDescriptorScope scope)
    {
        byte id = w.U8("bTerminalID");
        ushort type = w.U16("wTerminalType", TerminalType);
        w.U8("bAssocTerminal", v => v == 0 ? "none" : $"terminal {v}");
        byte source = w.U8("bSourceID", v => $"unit/terminal {v}");
        w.StringIndex("iTerminal", scope);
        return $"terminal {id} · {Terminal(type)} · from {source}";
    }

    static string OutputTerminal2(UsbFieldWriter w, UsbDescriptorScope scope)
    {
        byte id = w.U8("bTerminalID");
        ushort type = w.U16("wTerminalType", TerminalType);
        w.U8("bAssocTerminal", v => v == 0 ? "none" : $"terminal {v}");
        byte source = w.U8("bSourceID", v => $"unit/terminal {v}");
        w.U8("bCSourceID", v => $"clock {v}");
        w.U16("bmControls");
        w.StringIndex("iTerminal", scope);
        return $"terminal {id} · {Terminal(type)} · from {source}";
    }

    static string Selector(UsbFieldWriter w, UsbDescriptorScope scope, bool v2)
    {
        byte id = w.U8("bUnitID");
        byte pins = w.U8("bNrInPins");
        for (int i = 0; i < pins && w.Remaining > 0; i++) w.U8($"baSourceID[{i}]", v => $"unit/terminal {v}");
        if (v2) w.U8("bmControls");
        w.StringIndex("iSelector", scope);
        return $"unit {id} · {pins} input(s)";
    }

    static string Feature1(UsbFieldWriter w, UsbDescriptorScope scope)
    {
        byte id = w.U8("bUnitID");
        byte source = w.U8("bSourceID", v => $"unit/terminal {v}");
        byte size = w.U8("bControlSize", v => $"{v} byte(s) per channel");
        var controls = new SortedSet<string>(StringComparer.Ordinal);
        if (size > 0)
            for (int channel = 0; w.Remaining >= size + 1; channel++)
            {
                uint bits = size switch { 1 => w.U8(Channel(channel), b => Controls1(b)), 2 => w.U16(Channel(channel), b => Controls1(b)), _ => 0 };
                if (size > 2) w.Raw(Channel(channel), size);
                foreach (var c in Controls1Names(bits)) controls.Add(c);
            }
        w.StringIndex("iFeature", scope);
        return $"unit {id} · from {source} · {(controls.Count == 0 ? "no controls" : string.Join(", ", controls))}";
    }

    static string Feature2(UsbFieldWriter w, UsbDescriptorScope scope)
    {
        byte id = w.U8("bUnitID");
        byte source = w.U8("bSourceID", v => $"unit/terminal {v}");
        var controls = new SortedSet<string>(StringComparer.Ordinal);
        for (int channel = 0; w.Remaining >= 5; channel++)
        {
            uint bits = w.U32(Channel(channel), Controls2);
            foreach (var c in Controls2Names(bits)) controls.Add(c);
        }
        w.StringIndex("iFeature", scope);
        return $"unit {id} · from {source} · {(controls.Count == 0 ? "no controls" : string.Join(", ", controls))}";
    }

    static string Channel(int channel) => channel == 0 ? "bmaControls[master]" : $"bmaControls[{channel}]";

    static IEnumerable<string> Controls1Names(uint bits)
    {
        for (int i = 0; i < 10; i++)
            if ((bits & (1u << i)) != 0) yield return FeatureControls[i];
    }

    static string Controls1(uint bits) => Controls1Names(bits).ToList() is { Count: > 0 } names ? string.Join(", ", names) : "none";

    static IEnumerable<string> Controls2Names(uint bits)
    {
        for (int i = 0; i < FeatureControls.Length; i++)
            if (((bits >> (i * 2)) & 0x03) != 0) yield return FeatureControls[i];
    }

    static string Controls2(uint bits)
    {
        var named = new List<string>();
        for (int i = 0; i < FeatureControls.Length; i++)
            switch ((bits >> (i * 2)) & 0x03)
            {
                case 1: named.Add($"{FeatureControls[i]} (read-only)"); break;
                case 3: named.Add(FeatureControls[i]); break;
            }
        return named.Count == 0 ? "none" : string.Join(", ", named);
    }

    static string ClockSource(UsbFieldWriter w, UsbDescriptorScope scope)
    {
        byte id = w.U8("bClockID");
        byte attributes = w.U8("bmAttributes", a => ((a & 0x03) switch { 0 => "external", 1 => "internal fixed", 2 => "internal variable", _ => "internal programmable" })
            + ((a & 0x04) != 0 ? ", synchronised to SOF" : ""));
        w.U8("bmControls", v => UsbDescriptorNames.Flags(v, (0x03, "frequency"), (0x0C, "validity")));
        w.U8("bAssocTerminal", v => v == 0 ? "none" : $"terminal {v}");
        w.StringIndex("iClockSource", scope);
        return $"clock {id} · {((attributes & 0x03) == 0 ? "external" : "internal")}";
    }

    static string? FormatTag(ushort tag) => tag switch
    {
        0x0001 => "PCM", 0x0002 => "PCM8", 0x0003 => "IEEE float", 0x0004 => "A-law", 0x0005 => "µ-law",
        0x1001 => "MPEG", 0x1002 => "AC-3", _ => null,
    };

    static string StreamGeneral1(UsbFieldWriter w)
    {
        byte link = w.U8("bTerminalLink", v => $"terminal {v}");
        w.U8("bDelay", v => $"{v} frame(s)");
        ushort format = w.U16("wFormatTag", FormatTag);
        return $"{FormatTag(format) ?? $"format 0x{format:X4}"} · terminal {link}";
    }

    static string StreamGeneral2(UsbFieldWriter w, UsbDescriptorScope scope)
    {
        byte link = w.U8("bTerminalLink", v => $"terminal {v}");
        w.U8("bmControls");
        w.U8("bFormatType", v => $"type {v}");
        uint formats = w.U32("bmFormats", Formats2);
        byte channels = w.U8("bNrChannels");
        w.U32("bmChannelConfig", ChannelConfig);
        w.StringIndex("iChannelNames", scope);
        return $"{Formats2(formats)} · {channels} channel(s) · terminal {link}";
    }

    static string Formats2(uint v) => UsbDescriptorNames.Flags(v,
        (1u << 0, "PCM"), (1u << 1, "PCM8"), (1u << 2, "IEEE float"), (1u << 3, "A-law"), (1u << 4, "µ-law"), (1u << 31, "raw data"));

    static string Format1(UsbFieldWriter w)
    {
        byte type = w.U8("bFormatType", v => $"type {v}");
        if (type != 1) return $"format type {type}";
        byte channels = w.U8("bNrChannels");
        w.U8("bSubframeSize", v => $"{v} byte(s)");
        byte bits = w.U8("bBitResolution", v => $"{v} bits");
        byte rates = w.U8("bSamFreqType", v => v == 0 ? "continuous" : $"{v} discrete rate(s)");
        var named = new List<string>();
        if (rates == 0)
        {
            uint low = w.U24("tLowerSamFreq", UsbDescriptorNames.Hertz), high = w.U24("tUpperSamFreq", UsbDescriptorNames.Hertz);
            named.Add($"{UsbDescriptorNames.Hertz(low)}–{UsbDescriptorNames.Hertz(high)}");
        }
        else
            for (int i = 0; i < rates && w.Remaining >= 3; i++)
                named.Add(UsbDescriptorNames.Hertz(w.U24($"tSamFreq[{i}]", UsbDescriptorNames.Hertz)));
        return $"{channels} ch × {bits}-bit @ {string.Join(", ", named)}";
    }

    static string Format2(UsbFieldWriter w)
    {
        byte type = w.U8("bFormatType", v => $"type {v}");
        if (type != 1) return $"format type {type}";
        byte slot = w.U8("bSubslotSize", v => $"{v} byte(s)");
        byte bits = w.U8("bBitResolution", v => $"{v} bits");
        return $"{bits}-bit in {slot}-byte slots";
    }

    static string EndpointGeneral1(UsbFieldWriter w)
    {
        byte attributes = w.U8("bmAttributes", v => UsbDescriptorNames.Flags(v, (0x01, "sampling frequency control"), (0x02, "pitch control"), (0x80, "max packets only")));
        w.U8("bLockDelayUnits", v => v switch { 1 => "milliseconds", 2 => "decoded PCM samples", _ => "undefined" });
        w.U16("wLockDelay");
        return UsbDescriptorNames.Flags(attributes, (0x01, "sampling frequency control"), (0x02, "pitch control"), (0x80, "max packets only"));
    }

    static string EndpointGeneral2(UsbFieldWriter w)
    {
        byte attributes = w.U8("bmAttributes", v => (v & 0x80) != 0 ? "max packets only" : "none");
        w.U8("bmControls", v => UsbDescriptorNames.Flags(v, (0x03, "pitch"), (0x0C, "data overrun"), (0x30, "data underrun")));
        w.U8("bLockDelayUnits", v => v switch { 1 => "milliseconds", 2 => "decoded PCM samples", _ => "undefined" });
        w.U16("wLockDelay");
        return (attributes & 0x80) != 0 ? "max packets only" : "general";
    }

    static string MidiInJack(UsbFieldWriter w, UsbDescriptorScope scope)
    {
        byte type = w.U8("bJackType", JackType);
        byte id = w.U8("bJackID");
        w.StringIndex("iJack", scope);
        return $"jack {id} · {JackType(type)}";
    }

    static string MidiOutJack(UsbFieldWriter w, UsbDescriptorScope scope)
    {
        byte type = w.U8("bJackType", JackType);
        byte id = w.U8("bJackID");
        byte pins = w.U8("bNrInputPins");
        for (int i = 0; i < pins && w.Remaining >= 2; i++)
        {
            w.U8($"baSourceID[{i}]", v => $"jack/element {v}");
            w.U8($"BaSourcePin[{i}]");
        }
        w.StringIndex("iJack", scope);
        return $"jack {id} · {JackType(type)} · {pins} input pin(s)";
    }

    static string MidiEndpoint(UsbFieldWriter w)
    {
        byte count = w.U8("bNumEmbMIDIJack");
        for (int i = 0; i < count && w.Remaining > 0; i++) w.U8($"baAssocJackID[{i}]", v => $"jack {v}");
        return $"{count} embedded jack(s)";
    }

    static string JackType(byte v) => v switch { 1 => "embedded", 2 => "external", _ => $"type {v}" };
}
