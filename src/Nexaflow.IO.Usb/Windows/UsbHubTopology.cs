using System.Buffers.Binary;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;
using Nexaflow.IO.Usb.Descriptors;
using Nexaflow.IO.Usb.Windows.Native;

namespace Nexaflow.IO.Usb.Windows;

internal sealed record UsbPortConnection(
    int Port, string? DriverKey, bool IsHub, UsbSpeed Speed, bool? SuperSpeedCapable, byte CurrentConfiguration,
    byte[] DeviceDescriptor, bool? UserConnectable, bool? TypeC);

internal sealed record UsbHubScan(string InstanceId, string InterfacePath, string Kind, int PortCount, string? OpenError, IReadOnlyList<UsbPortConnection> Ports);

/// <summary>
/// Every hub — root or external — exposes GUID_DEVINTERFACE_USB_HUB, and its connection IOCTLs describe each
/// downstream port from the hub driver's own state: nothing is sent to the devices, and no privilege is needed.
/// </summary>
internal static unsafe class UsbHubTopology
{
    public static List<UsbHubScan> Scan()
    {
        var scans = new List<UsbHubScan>();
        foreach (var path in DevNodes.Interfaces(UsbIoctl.HubInterface, null))
        {
            var instanceId = DevNodes.InterfaceInstanceId(path) ?? path;
            using var hub = OpenHub(path);
            if (hub.IsInvalid)
            {
                scans.Add(new UsbHubScan(instanceId, path, "?", 0, Kernel32.LastError("open hub"), []));
                continue;
            }
            var (kind, portCount) = HubInformation(hub);
            var ports = new List<UsbPortConnection>();
            for (int port = 1; port <= portCount; port++)
                if (Connection(hub, port) is { } connection) ports.Add(connection);
            scans.Add(new UsbHubScan(instanceId, path, kind, portCount, null, ports));
        }
        return scans;
    }

    public static SafeFileHandle OpenHub(string path) =>
        Kernel32.CreateFile(path, Kernel32.GenericWrite, Kernel32.FileShareWrite, 0, Kernel32.OpenExisting, 0, 0);

    public static bool Ioctl(SafeFileHandle hub, uint code, Span<byte> buffer, out int returned)
    {
        fixed (byte* p = buffer)
            return Kernel32.DeviceIoControl(hub, code, p, buffer.Length, p, buffer.Length, out returned, 0);
    }

    static (string Kind, int Ports) HubInformation(SafeFileHandle hub)
    {
        // USB_HUB_INFORMATION_EX: HubType, HighestPortNumber, then the (larger) hub descriptor union.
        Span<byte> buffer = stackalloc byte[128];
        if (!Ioctl(hub, UsbIoctl.GetHubInformationEx, buffer, out _)) return ("?", 0);
        string kind = BinaryPrimitives.ReadInt32LittleEndian(buffer) switch { 1 => "Root hub", 2 => "USB 2.0 hub", 3 => "USB 3.x hub", var t => $"hub type {t}" };
        return (kind, BinaryPrimitives.ReadUInt16LittleEndian(buffer[4..]));
    }

    static UsbPortConnection? Connection(SafeFileHandle hub, int port)
    {
        // USB_NODE_CONNECTION_INFORMATION_EX is byte-packed: a 35-byte head, then 11-byte USB_PIPE_INFO entries.
        Span<byte> info = stackalloc byte[35 + 32 * 11];
        BinaryPrimitives.WriteUInt32LittleEndian(info, (uint)port);
        if (!Ioctl(hub, UsbIoctl.GetNodeConnectionInformationEx, info, out _)) return null;
        if (BinaryPrimitives.ReadInt32LittleEndian(info[31..]) != 1) return null; // DeviceConnected

        byte speedCode = info[23];
        Span<byte> v2 = stackalloc byte[16];
        BinaryPrimitives.WriteUInt32LittleEndian(v2, (uint)port);
        BinaryPrimitives.WriteUInt32LittleEndian(v2[4..], 16);
        BinaryPrimitives.WriteUInt32LittleEndian(v2[8..], 0x7); // ask about USB 1.1, 2.0 and 3.x
        uint? flags = Ioctl(hub, UsbIoctl.GetNodeConnectionInformationExV2, v2, out _) ? BinaryPrimitives.ReadUInt32LittleEndian(v2[12..]) : null;

        var speed = flags is { } f && (f & 0x4) != 0 ? UsbSpeed.SuperPlus
            : speedCode switch { 0 => UsbSpeed.Low, 1 => UsbSpeed.Full, 2 => UsbSpeed.High, 3 => UsbSpeed.Super, _ => UsbSpeed.Unknown };
        var (userConnectable, typeC) = ConnectorProperties(hub, port);

        return new UsbPortConnection(port, DriverKeyName(hub, port), info[24] != 0, speed, flags is { } g ? (g & 0x2) != 0 : null,
            info[22], info.Slice(4, UsbDeviceDescriptor.Length).ToArray(), userConnectable, typeC);
    }

    static (bool?, bool?) ConnectorProperties(SafeFileHandle hub, int port)
    {
        Span<byte> head = stackalloc byte[20];
        BinaryPrimitives.WriteUInt32LittleEndian(head, (uint)port);
        if (!Ioctl(hub, UsbIoctl.GetPortConnectorProperties, head, out _)) return (null, null);
        uint properties = BinaryPrimitives.ReadUInt32LittleEndian(head[8..]);
        return ((properties & 0x1) != 0, (properties & 0x8) != 0);
    }

    static string? DriverKeyName(SafeFileHandle hub, int port)
    {
        // USB_NODE_CONNECTION_DRIVERKEY_NAME: ask once for ActualLength, then again with room for the name.
        Span<byte> head = stackalloc byte[10];
        BinaryPrimitives.WriteUInt32LittleEndian(head, (uint)port);
        if (!Ioctl(hub, UsbIoctl.GetNodeConnectionDriverKeyName, head, out _)) return null;
        int actual = (int)BinaryPrimitives.ReadUInt32LittleEndian(head[4..]);
        if (actual <= 8 || actual > 4096) return null;
        Span<byte> full = stackalloc byte[actual];
        BinaryPrimitives.WriteUInt32LittleEndian(full, (uint)port);
        if (!Ioctl(hub, UsbIoctl.GetNodeConnectionDriverKeyName, full, out _)) return null;
        return MemoryMarshal.Cast<byte, char>(full[8..]).ToString().TrimEnd('\0');
    }
}
