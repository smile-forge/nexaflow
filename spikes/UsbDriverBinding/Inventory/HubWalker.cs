using System.Buffers.Binary;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;
using Nexaflow.Spikes.UsbDriverBinding.Native;

namespace Nexaflow.Spikes.UsbDriverBinding.Inventory;

public sealed record HidReportProbe(byte Interface, ushort DeclaredLength, string Outcome, string? Hex);

public sealed record PortDescriptors(
    List<byte[]> Configurations, byte[]? Bos, ushort[] Languages, Dictionary<byte, string> Strings,
    List<HidReportProbe> HidReports, List<string> Errors, double ElapsedMs);

public sealed record HubPort(
    int Port, string? DriverKey, bool IsHub, string Speed, bool? SuperSpeedCapable, byte CurrentConfiguration,
    ushort Address, uint OpenPipes, bool? UserConnectable, bool? TypeC, byte[] DeviceDescriptor, PortDescriptors? Descriptors);

public sealed record HubScan(string HubInstanceId, string HubType, int PortCount, string? OpenError, List<HubPort> Ports);

/// <summary>
/// The USBView technique: every hub exposes GUID_DEVINTERFACE_USB_HUB, and its IOCTLs return each
/// downstream device's descriptors whatever driver the device has — no privilege, no driver change.
/// </summary>
public static unsafe class HubWalker
{
    public static List<HubScan> Scan(bool readDescriptors)
    {
        var scans = new List<HubScan>();
        foreach (var path in DevNodes.Interfaces(UsbIoctl.HubInterface, null))
        {
            var instanceId = DevNodes.InterfaceInstanceId(path) ?? path;
            using var hub = Kernel32.CreateFile(path, Kernel32.GenericWrite, Kernel32.FileShareWrite, 0, Kernel32.OpenExisting, 0, 0);
            if (hub.IsInvalid)
            {
                scans.Add(new HubScan(instanceId, "?", 0, Win32Error("CreateFile(hub)"), []));
                continue;
            }
            var (hubType, ports) = HubInformation(hub);
            var scan = new HubScan(instanceId, hubType, ports, null, []);
            for (int port = 1; port <= ports; port++)
            {
                var connection = Connection(hub, port, readDescriptors);
                if (connection is not null) scan.Ports.Add(connection);
            }
            scans.Add(scan);
        }
        return scans;
    }

    static (string HubType, int Ports) HubInformation(SafeFileHandle hub)
    {
        var buffer = new byte[128];
        if (!Ioctl(hub, UsbIoctl.GetHubInformationEx, buffer, buffer.Length, out _)) return ("?", 0);
        string type = BinaryPrimitives.ReadInt32LittleEndian(buffer) switch { 1 => "Root hub", 2 => "USB 2.0 hub", 3 => "USB 3.x hub", var t => $"type {t}" };
        return (type, BinaryPrimitives.ReadUInt16LittleEndian(buffer.AsSpan(4)));
    }

    static HubPort? Connection(SafeFileHandle hub, int port, bool readDescriptors)
    {
        // USB_NODE_CONNECTION_INFORMATION_EX, byte-packed: 35-byte head then USB_PIPE_INFO[11 bytes].
        var info = new byte[35 + 32 * 11];
        BinaryPrimitives.WriteUInt32LittleEndian(info, (uint)port);
        if (!Ioctl(hub, UsbIoctl.GetNodeConnectionInformationEx, info, info.Length, out _)) return null;
        if (BinaryPrimitives.ReadInt32LittleEndian(info.AsSpan(31)) != 1) return null; // DeviceConnected

        var deviceDescriptor = info.AsSpan(4, 18).ToArray();
        byte speed = info[23];
        bool isHub = info[24] != 0;

        var v2 = new byte[16];
        BinaryPrimitives.WriteUInt32LittleEndian(v2, (uint)port);
        BinaryPrimitives.WriteUInt32LittleEndian(v2.AsSpan(4), 16);
        BinaryPrimitives.WriteUInt32LittleEndian(v2.AsSpan(8), 0x7); // USB 1.1 | 2.0 | 3.0
        uint? flags = Ioctl(hub, UsbIoctl.GetNodeConnectionInformationExV2, v2, v2.Length, out _) ? BinaryPrimitives.ReadUInt32LittleEndian(v2.AsSpan(12)) : null;

        string speedName = flags is { } f && (f & 0x4) != 0 ? "SuperSpeedPlus"
            : speed switch { 0 => "Low", 1 => "Full", 2 => "High", 3 => "Super", _ => $"speed {speed}" };

        var (userConnectable, typeC) = ConnectorProperties(hub, port);
        var descriptors = readDescriptors ? ReadDescriptors(hub, port, DeviceDescriptorInfo.Parse(deviceDescriptor), speed >= 3) : null;

        return new HubPort(port, DriverKeyName(hub, port), isHub, speedName, flags is { } g ? (g & 0x2) != 0 : null,
            info[22], BinaryPrimitives.ReadUInt16LittleEndian(info.AsSpan(25)), BinaryPrimitives.ReadUInt32LittleEndian(info.AsSpan(27)),
            userConnectable, typeC, deviceDescriptor, descriptors);
    }

    static (bool?, bool?) ConnectorProperties(SafeFileHandle hub, int port)
    {
        var head = new byte[20];
        BinaryPrimitives.WriteUInt32LittleEndian(head, (uint)port);
        if (!Ioctl(hub, UsbIoctl.GetPortConnectorProperties, head, head.Length, out _)) return (null, null);
        uint properties = BinaryPrimitives.ReadUInt32LittleEndian(head.AsSpan(8));
        return ((properties & 0x1) != 0, (properties & 0x8) != 0);
    }

    static string? DriverKeyName(SafeFileHandle hub, int port)
    {
        var head = new byte[10];
        BinaryPrimitives.WriteUInt32LittleEndian(head, (uint)port);
        if (!Ioctl(hub, UsbIoctl.GetNodeConnectionDriverKeyName, head, head.Length, out _)) return null;
        int actual = (int)BinaryPrimitives.ReadUInt32LittleEndian(head.AsSpan(4));
        if (actual <= 8) return null;
        var full = new byte[actual];
        BinaryPrimitives.WriteUInt32LittleEndian(full, (uint)port);
        if (!Ioctl(hub, UsbIoctl.GetNodeConnectionDriverKeyName, full, full.Length, out _)) return null;
        return MemoryMarshal.Cast<byte, char>(full.AsSpan(8)).ToString().TrimEnd('\0');
    }

    static PortDescriptors ReadDescriptors(SafeFileHandle hub, int port, DeviceDescriptorInfo device, bool superSpeed)
    {
        var clock = Stopwatch.StartNew();
        var errors = new List<string>();
        var configurations = new List<byte[]>();
        var stringIndexes = new SortedSet<byte> { device.ManufacturerIndex, device.ProductIndex, device.SerialIndex };
        var hidReports = new List<HidReportProbe>();

        for (byte index = 0; index < device.NumConfigurations; index++)
        {
            var head = Descriptor(hub, port, 0x80, 0x02, index, 0, 9, errors, $"config[{index}] header");
            if (head is not { Length: >= 9 }) continue;
            ushort total = BinaryPrimitives.ReadUInt16LittleEndian(head.AsSpan(2));
            var full = Descriptor(hub, port, 0x80, 0x02, index, 0, total, errors, $"config[{index}]");
            if (full is null) continue;
            configurations.Add(full);

            var parsed = DescriptorParser.ParseConfiguration(full, superSpeed);
            stringIndexes.Add(parsed.NameIndex);
            foreach (var iface in parsed.Interfaces)
            {
                stringIndexes.Add(iface.NameIndex);
                if (iface is { HidReportLength: { } reportLength, Alternate: 0 })
                    hidReports.Add(HidReport(hub, port, iface.Number, reportLength));
            }
        }

        byte[]? bos = null;
        if (device.BcdUsbRaw >= 0x0201)
        {
            var head = Descriptor(hub, port, 0x80, 0x0F, 0, 0, 5, errors, "BOS header");
            if (head is { Length: >= 5 })
                bos = Descriptor(hub, port, 0x80, 0x0F, 0, 0, BinaryPrimitives.ReadUInt16LittleEndian(head.AsSpan(2)), errors, "BOS");
        }

        stringIndexes.Remove(0);
        ushort[] languages = [];
        var strings = new Dictionary<byte, string>();
        if (stringIndexes.Count > 0)
        {
            var langs = Descriptor(hub, port, 0x80, 0x03, 0, 0, 255, errors, "string[0] languages");
            if (langs is { Length: >= 4 })
                languages = MemoryMarshal.Cast<byte, ushort>(langs.AsSpan(2, (langs[0] - 2) & ~1)).ToArray();
            ushort lang = languages.Length > 0 ? languages[0] : (ushort)0x0409;
            foreach (var index in stringIndexes)
            {
                var s = Descriptor(hub, port, 0x80, 0x03, index, lang, 255, errors, $"string[{index}]");
                if (s is { Length: >= 2 }) strings[index] = MemoryMarshal.Cast<byte, char>(s.AsSpan(2, (Math.Min(s[0], s.Length) - 2) & ~1)).ToString();
            }
        }

        return new PortDescriptors(configurations, bos, languages, strings, hidReports, errors, clock.Elapsed.TotalMilliseconds);
    }

    /// <summary>
    /// Experiment: the hub IOCTL is documented for device-recipient GET_DESCRIPTOR. A HID report descriptor is
    /// interface-recipient (bmRequestType 0x81). Whether the hub forwards it decides how the HID adapter gets one.
    /// </summary>
    static HidReportProbe HidReport(SafeFileHandle hub, int port, byte iface, ushort length)
    {
        var errors = new List<string>();
        var data = Descriptor(hub, port, 0x81, 0x22, 0, iface, length, errors, "HID report");
        if (data is null) return new(iface, length, errors[0], null);
        bool looksLikeReport = data.Length > 0 && data[0] is 0x05 or 0x06; // a report descriptor opens with Usage Page
        return new(iface, length, looksLikeReport ? $"ok ({data.Length} bytes)" : $"returned {data.Length} bytes that are not a report descriptor", DescriptorParser.Hex(data));
    }

    static byte[]? Descriptor(SafeFileHandle hub, int port, byte requestType, byte type, byte index, ushort wIndex, ushort length, List<string> errors, string what)
    {
        // USB_DESCRIPTOR_REQUEST, byte-packed: ConnectionIndex, 8-byte setup packet, data.
        var buffer = new byte[12 + length];
        BinaryPrimitives.WriteUInt32LittleEndian(buffer, (uint)port);
        buffer[4] = requestType;
        buffer[5] = 0x06; // GET_DESCRIPTOR
        BinaryPrimitives.WriteUInt16LittleEndian(buffer.AsSpan(6), (ushort)(type << 8 | index));
        BinaryPrimitives.WriteUInt16LittleEndian(buffer.AsSpan(8), wIndex);
        BinaryPrimitives.WriteUInt16LittleEndian(buffer.AsSpan(10), length);
        if (!Ioctl(hub, UsbIoctl.GetDescriptorFromNodeConnection, buffer, buffer.Length, out int returned))
        {
            errors.Add(Win32Error(what));
            return null;
        }
        return buffer.AsSpan(12, Math.Max(0, returned - 12)).ToArray();
    }

    static bool Ioctl(SafeFileHandle hub, uint code, byte[] buffer, int length, out int returned)
    {
        fixed (byte* p = buffer)
            return Kernel32.DeviceIoControl(hub, code, p, length, p, length, out returned, 0);
    }

    internal static string Win32Error(string what)
    {
        int error = Marshal.GetLastPInvokeError();
        return $"{what}: Win32 {error} ({new Win32Exception(error).Message})";
    }
}
