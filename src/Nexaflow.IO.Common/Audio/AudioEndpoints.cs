using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Nexaflow.IO.Common.Hardware;

namespace Nexaflow.IO.Common.Audio;

public enum AudioFlow { Playback, Recording }

/// <summary>
/// One active Windows audio endpoint — a speaker, a headset's microphone — with the shared-mode format the audio
/// engine mixes it at, and the devnode of the hardware behind it.
/// </summary>
/// <param name="Id">The endpoint id WASAPI opens it by.</param>
/// <param name="ChannelMask">Which speaker each channel feeds (<c>SPEAKER_*</c> bits); 0 when the format does not say.</param>
/// <param name="AdapterInstanceId">The device instance id of the driver that renders or captures it — for USB audio,
/// the USB function's <c>USB\VID_…&amp;MI_xx\…</c> — or null when Windows did not say.</param>
public sealed record AudioEndpointInfo(
    string Id, string Name, AudioFlow Flow, int Channels, int SampleRate, uint ChannelMask, string? AdapterInstanceId)
{
    public IReadOnlyList<string> ChannelNames => AudioChannels.Names(Channels, ChannelMask);
}

/// <summary>Channel names from a <c>SPEAKER_*</c> mask, in channel order.</summary>
public static class AudioChannels
{
    static readonly string[] Speakers =
    [
        "Front left", "Front right", "Front centre", "Low frequency", "Back left", "Back right",
        "Front left of centre", "Front right of centre", "Back centre", "Side left", "Side right", "Top centre",
        "Top front left", "Top front centre", "Top front right", "Top back left", "Top back centre", "Top back right",
    ];

    public static IReadOnlyList<string> Names(int channels, uint mask)
    {
        if (channels == 1 && mask is 0 or 0x4) return ["Mono"];
        if (channels == 2 && mask == 0) return [Speakers[0], Speakers[1]];

        var names = new List<string>(channels);
        for (int bit = 0; bit < 32 && names.Count < channels; bit++)
            if ((mask & (1u << bit)) != 0) names.Add(bit < Speakers.Length ? Speakers[bit] : $"Channel {names.Count + 1}");
        while (names.Count < channels) names.Add($"Channel {names.Count + 1}");
        return names;
    }
}

/// <summary>
/// The active audio endpoints, as the Windows audio engine lists them. Each endpoint's adapter comes from its device
/// topology: the endpoint's one connector leads to the kernel-streaming filter that drives it, whose device interface
/// names the devnode — which, for USB audio, is the function on the USB page.
/// </summary>
[SupportedOSPlatform("windows")]
public static class AudioEndpointCatalog
{
    const int AllFlows = 2, ActiveState = 1;

    static readonly Guid DeviceTopologyId = new("2A07407E-6497-4A18-9787-32F79BD0D98F");
    static readonly Guid AudioClientId = new("726778CD-F60A-4eda-82DE-E47610CD78AA");

    public static IReadOnlyList<AudioEndpointInfo> List()
    {
        var found = new List<AudioEndpointInfo>();
        var enumerator = (IMMDeviceEnumerator)new MMDeviceEnumeratorCom();
        IMMDeviceCollection? collection = null;
        try
        {
            WasapiStreams.Check(enumerator.EnumAudioEndpoints(AllFlows, ActiveState, out IntPtr pointer));
            collection = (IMMDeviceCollection)Marshal.GetObjectForIUnknown(pointer);
            Marshal.Release(pointer);
            WasapiStreams.Check(collection.GetCount(out uint count));
            for (uint i = 0; i < count; i++)
            {
                if (collection.Item(i, out IMMDevice device) != 0) continue;
                try { if (Describe(device) is { } endpoint) found.Add(endpoint); }
                finally { Marshal.ReleaseComObject(device); }
            }
        }
        finally
        {
            if (collection is not null) Marshal.ReleaseComObject(collection);
            Marshal.ReleaseComObject(enumerator);
        }
        return found;
    }

    static AudioEndpointInfo? Describe(IMMDevice device)
    {
        if (device.GetId(out string id) != 0) return null;
        if (((IMMEndpoint)device).GetDataFlow(out int flow) != 0) return null;
        var format = MixFormat(device);
        return new AudioEndpointInfo(id, FriendlyName(device) ?? id, flow == WasapiStreams.CaptureFlow ? AudioFlow.Recording : AudioFlow.Playback,
            format.Channels, format.Rate, format.Mask, Adapter(device));
    }

    static (int Channels, int Rate, uint Mask) MixFormat(IMMDevice device)
    {
        var iid = AudioClientId;
        if (device.Activate(ref iid, 0x17, IntPtr.Zero, out object o) != 0) return (0, 0, 0);
        var client = (IAudioClient2)o;
        try
        {
            if (client.GetMixFormat(out IntPtr format) != 0) return (0, 0, 0);
            try { var mix = WasapiStreams.Describe(format); return (mix.Channels, mix.Rate, mix.Mask); }
            finally { Marshal.FreeCoTaskMem(format); }
        }
        finally { Marshal.ReleaseComObject(client); }
    }

    static string? FriendlyName(IMMDevice device)
    {
        if (device.OpenPropertyStore(0, out IntPtr pointer) != 0) return null;
        var store = (IPropertyStore)Marshal.GetObjectForIUnknown(pointer);
        Marshal.Release(pointer);
        try
        {
            var key = new PropertyKey(new Guid("a45c254e-df1c-4efd-8020-67d146a850e0"), 14);
            if (store.GetValue(ref key, out PropVariant value) != 0) return null;
            try { return value.Type == 31 ? Marshal.PtrToStringUni(value.Pointer) : null; }
            finally { PropVariant.Clear(ref value); }
        }
        finally { Marshal.ReleaseComObject(store); }
    }

    /// <summary>The devnode behind the endpoint: its connector's far side is the adapter's filter, named by a device id
    /// such as <c>{2}.\\?\usb#vid_046d&amp;pid_0a87&amp;mi_00#…#{6994ad04-…}\global</c>.</summary>
    static string? Adapter(IMMDevice device)
    {
        var iid = DeviceTopologyId;
        if (device.Activate(ref iid, 0x17, IntPtr.Zero, out object o) != 0) return null;
        var topology = (IDeviceTopology)o;
        IConnector? connector = null;
        try
        {
            if (topology.GetConnectorCount(out uint count) != 0 || count == 0) return null;
            if (topology.GetConnector(0, out connector) != 0) return null;
            if (connector.GetDeviceIdConnectedTo(out string adapter) != 0) return null;
            int at = adapter.IndexOf(@"\\?\", StringComparison.Ordinal);
            return at < 0 ? null : DeviceInterfaces.InstanceIdOf(adapter[at..]);
        }
        catch (COMException) { return null; }
        finally
        {
            if (connector is not null) Marshal.ReleaseComObject(connector);
            Marshal.ReleaseComObject(topology);
        }
    }
}

[ComImport, Guid("0BD7A1BE-7A1A-44DB-8397-CC5392387B5E"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IMMDeviceCollection
{
    [PreserveSig] int GetCount(out uint count);
    [PreserveSig] int Item(uint index, out IMMDevice device);
}

[ComImport, Guid("1BE09788-6894-4089-8586-9A2A6C265AC5"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IMMEndpoint
{
    [PreserveSig] int GetDataFlow(out int flow);
}

[ComImport, Guid("886d8eeb-8cf2-4446-8d02-cdba1dbdcf99"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IPropertyStore
{
    [PreserveSig] int GetCount(out uint count);
    [PreserveSig] int GetAt(uint index, out PropertyKey key);
    [PreserveSig] int GetValue(ref PropertyKey key, out PropVariant value);
}

[ComImport, Guid("2A07407E-6497-4A18-9787-32F79BD0D98F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IDeviceTopology
{
    [PreserveSig] int GetConnectorCount(out uint count);
    [PreserveSig] int GetConnector(uint index, out IConnector connector);
}

[ComImport, Guid("9c2c4058-23f5-41de-877a-df3af236a09e"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IConnector
{
    [PreserveSig] int GetType(out int type);
    [PreserveSig] int GetDataFlow(out int flow);
    [PreserveSig] int ConnectTo(IConnector other);
    [PreserveSig] int Disconnect();
    [PreserveSig] int IsConnected(out int connected);
    [PreserveSig] int GetConnectedTo(out IConnector other);
    [PreserveSig] int GetConnectorIdConnectedTo([MarshalAs(UnmanagedType.LPWStr)] out string id);
    [PreserveSig] int GetDeviceIdConnectedTo([MarshalAs(UnmanagedType.LPWStr)] out string id);
}

[StructLayout(LayoutKind.Sequential)]
internal struct PropertyKey(Guid formatId, int propertyId)
{
    public Guid FormatId = formatId;
    public int PropertyId = propertyId;
}

[StructLayout(LayoutKind.Explicit, Size = 24)]
internal struct PropVariant
{
    [FieldOffset(0)] public ushort Type;
    [FieldOffset(8)] public IntPtr Pointer;

    [DllImport("ole32.dll")]
    private static extern int PropVariantClear(ref PropVariant value);

    public static void Clear(ref PropVariant value) => PropVariantClear(ref value);
}
