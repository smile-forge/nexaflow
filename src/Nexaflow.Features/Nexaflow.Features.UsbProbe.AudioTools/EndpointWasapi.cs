using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace Nexaflow.Features.UsbProbe.AudioTools;

/// <summary>
/// The WASAPI and configuration-manager calls the audio tools make, and only those: every active endpoint of a flow, the
/// devnode behind each, and shared-mode streams at the engine's own mix format — every channel the endpoint has, at the
/// mixer's rate — so nothing is downmixed or resampled between a test signal and the device. The app's own speech has its
/// own WASAPI in IO.Common, for the default endpoints, communications mode and echo cancellation; the two declare the COM
/// interfaces they share each for themselves, so each holds what it uses.
/// </summary>
[SupportedOSPlatform("windows")]
internal static class EndpointWasapi
{
    public const int RenderFlow  = 0;
    public const int CaptureFlow = 1;

    private const int ClsCtxAll = 0x17;
    private const uint AutoConvertPcm = 0x80000000, SrcDefaultQuality = 0x08000000;

    private static readonly Guid AudioClient2Id  = new("726778CD-F60A-4eda-82DE-E47610CD78AA");
    private static readonly Guid CaptureClientId = new("C8ADBD64-E71E-48a0-A4DE-185C395CD317");
    private static readonly Guid RenderClientId  = new("F294ACFC-3146-4483-A7BF-ADDCA7C260E2");

    /// <summary>A shared-mode format as WASAPI reports one: channels, rate, speaker mask, and whether samples are 32-bit float.</summary>
    public readonly record struct StreamFormat(int Channels, int Rate, uint Mask, bool IsFloat32);

    private static readonly Guid FloatSubFormat = new("00000003-0000-0010-8000-00aa00389b71");

    /// <summary>Reads a <c>WAVEFORMATEX</c>, or the <c>WAVEFORMATEXTENSIBLE</c> it opens, at <paramref name="format"/>.</summary>
    public static StreamFormat Describe(IntPtr format)
    {
        ushort tag = (ushort)Marshal.ReadInt16(format, 0);
        int channels = (ushort)Marshal.ReadInt16(format, 2);
        int rate = Marshal.ReadInt32(format, 4);
        int bits = (ushort)Marshal.ReadInt16(format, 14);
        uint mask = 0;
        bool isFloat = tag == 3;
        if (tag == 0xFFFE && (ushort)Marshal.ReadInt16(format, 16) >= 22)
        {
            mask = (uint)Marshal.ReadInt32(format, 20);
            var sub = new byte[16];
            Marshal.Copy(format + 24, sub, 0, 16);
            isFloat = new Guid(sub) == FloatSubFormat;
        }
        return new(channels, rate, mask, isFloat && bits == 32);
    }

    /// <summary>
    /// An initialised shared-mode client at the audio engine's own mix format — every channel the endpoint has, at the
    /// mixer's rate, as 32-bit float — so nothing is downmixed or resampled between a test signal and the device. An
    /// engine mixing in anything but float is asked for float at the same layout, and converts.
    /// </summary>
    public static IAudioClient2 OpenAtMixFormat(string endpointId, int bufferMs, out StreamFormat format)
    {
        var enumerator = (IMMDeviceEnumerator)new MMDeviceEnumeratorCom();
        IMMDevice? device = null;
        try
        {
            Check(enumerator.GetDevice(endpointId, out device));
            var iid = AudioClient2Id;
            Check(device.Activate(ref iid, ClsCtxAll, IntPtr.Zero, out object o));
            var client = (IAudioClient2)o;
            Check(client.GetMixFormat(out IntPtr mix));
            try
            {
                format = Describe(mix);
                if (format.IsFloat32)
                {
                    Check(client.Initialize(0, 0, bufferMs * 10_000L, 0, mix, IntPtr.Zero));
                    return client;
                }

                IntPtr wanted = Marshal.AllocHGlobal(40);
                try
                {
                    int block = format.Channels * 4;
                    Marshal.WriteInt16(wanted, 0, unchecked((short)0xFFFE));
                    Marshal.WriteInt16(wanted, 2, (short)format.Channels);
                    Marshal.WriteInt32(wanted, 4, format.Rate);
                    Marshal.WriteInt32(wanted, 8, format.Rate * block);
                    Marshal.WriteInt16(wanted, 12, (short)block);
                    Marshal.WriteInt16(wanted, 14, 32);
                    Marshal.WriteInt16(wanted, 16, 22);
                    Marshal.WriteInt16(wanted, 18, 32);
                    Marshal.WriteInt32(wanted, 20, unchecked((int)format.Mask));
                    Marshal.Copy(FloatSubFormat.ToByteArray(), 0, wanted + 24, 16);
                    Check(client.Initialize(0, AutoConvertPcm | SrcDefaultQuality, bufferMs * 10_000L, 0, wanted, IntPtr.Zero));
                }
                finally { Marshal.FreeHGlobal(wanted); }
                format = format with { IsFloat32 = true };
                return client;
            }
            finally { Marshal.FreeCoTaskMem(mix); }
        }
        finally
        {
            if (device is not null) Marshal.ReleaseComObject(device);
            Marshal.ReleaseComObject(enumerator);
        }
    }

    public static IAudioCaptureClient Capture(IAudioClient2 client) => Service<IAudioCaptureClient>(client, CaptureClientId);
    public static IAudioRenderClient Render(IAudioClient2 client) => Service<IAudioRenderClient>(client, RenderClientId);

    private static T Service<T>(IAudioClient2 client, Guid id) where T : class
    {
        Check(client.GetService(ref id, out object o));
        return (T)o;
    }

    public static void Check(int hr)
    {
        if (hr < 0) Marshal.ThrowExceptionForHR(hr);
    }

    private static readonly Guid InstanceIdSet = new("78c34fc8-104a-4aca-9ea4-524d52996e57");
    private const uint CrSuccess = 0, CrBufferSmall = 0x1A;

    /// <summary>The device instance id of the devnode that owns <paramref name="interfacePath"/>, or null.</summary>
    public static string? InstanceIdOf(string interfacePath)
    {
        var key = new DevPropKey { FormatId = InstanceIdSet, PropertyId = 256 };
        uint size = 0;
        if (CM_Get_Device_Interface_PropertyW(interfacePath, ref key, out _, null, ref size, 0) != CrBufferSmall || size == 0) return null;
        var buffer = new byte[size];
        if (CM_Get_Device_Interface_PropertyW(interfacePath, ref key, out _, buffer, ref size, 0) != CrSuccess) return null;
        return System.Text.Encoding.Unicode.GetString(buffer, 0, (int)size).TrimEnd('\0');
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DevPropKey
    {
        public Guid FormatId;
        public uint PropertyId;
    }

    [DllImport("cfgmgr32.dll", CharSet = CharSet.Unicode)]
    private static extern uint CM_Get_Device_Interface_PropertyW(string deviceInterface, ref DevPropKey key, out uint type,
        [Out] byte[]? buffer, ref uint size, uint flags);
}

[ComImport, Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")]
internal class MMDeviceEnumeratorCom;

[ComImport, Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IMMDeviceEnumerator
{
    [PreserveSig] int EnumAudioEndpoints(int dataFlow, int stateMask, out IntPtr devices);
    [PreserveSig] int GetDefaultAudioEndpoint(int dataFlow, int role, out IMMDevice device);
    [PreserveSig] int GetDevice([MarshalAs(UnmanagedType.LPWStr)] string id, out IMMDevice device);
}

[ComImport, Guid("D666063F-1587-4E43-81F1-B948E807363F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IMMDevice
{
    [PreserveSig] int Activate(ref Guid iid, int clsCtx, IntPtr activationParams, [MarshalAs(UnmanagedType.IUnknown)] out object o);
    [PreserveSig] int OpenPropertyStore(int access, out IntPtr store);
    [PreserveSig] int GetId([MarshalAs(UnmanagedType.LPWStr)] out string id);
    [PreserveSig] int GetState(out int state);
}

[ComImport, Guid("726778CD-F60A-4eda-82DE-E47610CD78AA"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IAudioClient2
{
    [PreserveSig] int Initialize(int shareMode, uint streamFlags, long bufferDuration, long periodicity, IntPtr format, IntPtr sessionGuid);
    [PreserveSig] int GetBufferSize(out uint frames);
    [PreserveSig] int GetStreamLatency(out long latency);
    [PreserveSig] int GetCurrentPadding(out uint padding);
    [PreserveSig] int IsFormatSupported(int shareMode, IntPtr format, out IntPtr closest);
    [PreserveSig] int GetMixFormat(out IntPtr format);
    [PreserveSig] int GetDevicePeriod(out long defaultPeriod, out long minimumPeriod);
    [PreserveSig] int Start();
    [PreserveSig] int Stop();
    [PreserveSig] int Reset();
    [PreserveSig] int SetEventHandle(IntPtr handle);
    [PreserveSig] int GetService(ref Guid iid, [MarshalAs(UnmanagedType.IUnknown)] out object service);
    [PreserveSig] int IsOffloadCapable(int category, out int capable);
    [PreserveSig] int SetClientProperties(ref AudioClientProperties properties);
    [PreserveSig] int GetBufferSizeLimits(IntPtr format, int eventDriven, out long min, out long max);
}

[ComImport, Guid("C8ADBD64-E71E-48a0-A4DE-185C395CD317"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IAudioCaptureClient
{
    [PreserveSig] int GetBuffer(out IntPtr data, out uint frames, out uint flags, out ulong devicePosition, out ulong qpcPosition);
    [PreserveSig] int ReleaseBuffer(uint frames);
    [PreserveSig] int GetNextPacketSize(out uint frames);
}

[ComImport, Guid("F294ACFC-3146-4483-A7BF-ADDCA7C260E2"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IAudioRenderClient
{
    [PreserveSig] int GetBuffer(uint frames, out IntPtr data);
    [PreserveSig] int ReleaseBuffer(uint frames, uint flags);
}

[StructLayout(LayoutKind.Sequential)]
internal struct AudioClientProperties
{
    public int Size;
    public int IsOffload;
    public int Category;
    public int Options;
}
