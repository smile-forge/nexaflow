using System.Buffers.Binary;
using Nexaflow.IO.Usb.Descriptors;
using Nexaflow.IO.Usb.Inventory;
using Nexaflow.IO.Usb.Windows.Native;

namespace Nexaflow.IO.Usb.Windows;

/// <summary>One HID interface's report descriptor as the device sends it, or why it could not be had.</summary>
public sealed record UsbHidReportDescriptor(byte Interface, byte[]? Bytes, string? Problem);

/// <summary>
/// Reads a device's HID report descriptors from the device itself, through its hub — the bytes the HID class driver
/// decodes, which Windows otherwise keeps to itself (it hands out only its own parse of them). For the debug details: a
/// device no tool here understands yet is understood from these.
/// </summary>
/// <remarks>
/// A standard GET_DESCRIPTOR addressed to the interface (bmRequestType 0x81, wValue 0x22xx), with the length the
/// interface's HID descriptor declares. It changes nothing on the device, and is sent only when debug details are asked
/// for.
/// </remarks>
public static class UsbHidReportReader
{
    const byte ReportDescriptorType = 0x22;

    public static IReadOnlyList<UsbHidReportDescriptor> Read(UsbDeviceNode device)
    {
        var interfaces = (device.ActiveConfiguration?.DefaultSettings ?? [])
            .Where(i => i.Class == UsbClassCodes.Hid && i.HidReportDescriptorLength is > 0)
            .ToList();
        if (interfaces.Count == 0) return [];

        string? hubPath = DevNodes.Interfaces(UsbIoctl.HubInterface, null)
            .FirstOrDefault(p => string.Equals(DevNodes.InterfaceInstanceId(p), device.Location.HubInstanceId, StringComparison.OrdinalIgnoreCase));
        if (hubPath is null) return [.. interfaces.Select(i => new UsbHidReportDescriptor(i.Number, null, "its hub could not be found"))];

        using var hub = UsbHubTopology.OpenHub(hubPath);
        if (hub.IsInvalid) return [.. interfaces.Select(i => new UsbHidReportDescriptor(i.Number, null, Kernel32.LastError("open hub")))];

        var read = new List<UsbHidReportDescriptor>();
        foreach (var setting in interfaces)
        {
            ushort length = setting.HidReportDescriptorLength!.Value;
            var buffer = new byte[12 + length];
            BinaryPrimitives.WriteUInt32LittleEndian(buffer, (uint)device.Location.Port);
            buffer[4] = 0x81; // device-to-host, standard, interface
            buffer[5] = 0x06; // GET_DESCRIPTOR
            BinaryPrimitives.WriteUInt16LittleEndian(buffer.AsSpan(6), ReportDescriptorType << 8);
            BinaryPrimitives.WriteUInt16LittleEndian(buffer.AsSpan(8), setting.Number);
            BinaryPrimitives.WriteUInt16LittleEndian(buffer.AsSpan(10), length);
            read.Add(UsbHubTopology.Ioctl(hub, UsbIoctl.GetDescriptorFromNodeConnection, buffer, out int returned)
                ? new UsbHidReportDescriptor(setting.Number, buffer.AsSpan(12, Math.Max(0, returned - 12)).ToArray(), null)
                : new UsbHidReportDescriptor(setting.Number, null, Kernel32.LastError("GET_DESCRIPTOR (HID report)")));
        }
        return read;
    }
}
