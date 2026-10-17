using System.Buffers.Binary;
using Microsoft.Win32.SafeHandles;
using Nexaflow.IO.Usb.Descriptors;
using Nexaflow.IO.Usb.Windows.Native;

namespace Nexaflow.IO.Usb.Windows;

/// <summary>What a device's descriptors came to: configurations always, BOS and strings when the wire was used.</summary>
internal sealed record UsbWireDescriptors(
    IReadOnlyList<byte[]> Configurations, byte[]? Bos, IReadOnlyDictionary<byte, string> Strings, IReadOnlyList<ushort> Languages,
    bool WireRead, IReadOnlyList<string> Problems);

/// <summary>
/// Standard GET_DESCRIPTOR requests through the parent hub (IOCTL_USB_GET_DESCRIPTOR_FROM_NODE_CONNECTION) — the
/// USBView technique, which works whatever driver the device has. The hub answers device and configuration
/// descriptors from its own cache; string and BOS requests reach the device, and fail while it is suspended.
/// </summary>
internal static class UsbHubDescriptorReader
{
    const byte GetDescriptor = 0x06;
    const ushort FallbackLanguage = 0x0409;

    public static UsbWireDescriptors Read(string hubPath, int port, UsbDeviceDescriptor device, bool readWire)
    {
        var problems = new List<string>();
        using var hub = UsbHubTopology.OpenHub(hubPath);
        if (hub.IsInvalid) return new([], null, new Dictionary<byte, string>(), [], false, [Kernel32.LastError("open hub")]);

        var configurations = new List<byte[]>();
        var stringIndexes = new SortedSet<byte> { device.ManufacturerIndex, device.ProductIndex, device.SerialNumberIndex };
        for (byte index = 0; index < device.ConfigurationCount; index++)
        {
            if (Descriptor(hub, port, UsbDescriptorType.Configuration, index, 0, 9, problems, $"configuration {index}") is not { Length: >= 9 } head) continue;
            ushort total = BinaryPrimitives.ReadUInt16LittleEndian(head.AsSpan(2));
            if (Descriptor(hub, port, UsbDescriptorType.Configuration, index, 0, total, problems, $"configuration {index}") is not { } full) continue;
            configurations.Add(full);
            CollectStringIndexes(full, stringIndexes);
        }

        if (!readWire) return new(configurations, null, new Dictionary<byte, string>(), [], false, problems);

        byte[]? bos = null;
        if (device.HasBos && Descriptor(hub, port, UsbDescriptorType.Bos, 0, 0, 5, problems, "BOS") is { Length: >= 5 } bosHead)
            bos = Descriptor(hub, port, UsbDescriptorType.Bos, 0, 0, BinaryPrimitives.ReadUInt16LittleEndian(bosHead.AsSpan(2)), problems, "BOS");

        stringIndexes.Remove(0);
        var strings = new Dictionary<byte, string>();
        ushort[] languages = [];
        if (stringIndexes.Count > 0)
        {
            if (Descriptor(hub, port, UsbDescriptorType.String, 0, 0, 255, problems, "string languages") is { } langs)
                languages = UsbDescriptorParser.ParseLanguageIds(langs);
            ushort language = languages.Length > 0 ? languages[0] : FallbackLanguage;
            foreach (var index in stringIndexes)
                if (Descriptor(hub, port, UsbDescriptorType.String, index, language, 255, problems, $"string {index}") is { } s
                    && UsbDescriptorParser.ParseString(s) is { } text)
                    strings[index] = text;
        }
        return new(configurations, bos, strings, languages, true, problems);
    }

    /// <summary>iConfiguration and every iInterface, read without a full parse.</summary>
    static void CollectStringIndexes(ReadOnlySpan<byte> config, SortedSet<byte> into)
    {
        if (config.Length >= 7) into.Add(config[6]);
        for (int offset = 0; offset + 2 <= config.Length && config[offset] >= 2;)
        {
            if (config[offset + 1] == UsbDescriptorType.Interface && offset + 9 <= config.Length) into.Add(config[offset + 8]);
            if (config[offset + 1] == UsbDescriptorType.InterfaceAssociation && offset + 8 <= config.Length) into.Add(config[offset + 7]);
            offset += config[offset];
        }
    }

    static byte[]? Descriptor(SafeFileHandle hub, int port, byte type, byte index, ushort languageId, ushort length, List<string> problems, string what)
    {
        // USB_DESCRIPTOR_REQUEST, byte-packed: ConnectionIndex, the 8-byte setup packet, then the data.
        var buffer = new byte[12 + length];
        BinaryPrimitives.WriteUInt32LittleEndian(buffer, (uint)port);
        buffer[4] = 0x80; // device-to-host, standard, device
        buffer[5] = GetDescriptor;
        BinaryPrimitives.WriteUInt16LittleEndian(buffer.AsSpan(6), (ushort)(type << 8 | index));
        BinaryPrimitives.WriteUInt16LittleEndian(buffer.AsSpan(8), languageId);
        BinaryPrimitives.WriteUInt16LittleEndian(buffer.AsSpan(10), length);
        if (!UsbHubTopology.Ioctl(hub, UsbIoctl.GetDescriptorFromNodeConnection, buffer, out int returned))
        {
            problems.Add(Kernel32.LastError(what));
            return null;
        }
        return buffer.AsSpan(12, Math.Max(0, returned - 12)).ToArray();
    }
}
