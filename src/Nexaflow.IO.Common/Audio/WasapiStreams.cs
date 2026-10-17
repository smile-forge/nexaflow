using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace Nexaflow.IO.Common.Audio;

/// <summary>
/// Just enough WASAPI for a spoken conversation: shared-mode streams on the default endpoints, in the communications
/// category for capture — where Windows applies its echo cancellation, noise suppression and gain control — with the
/// playback endpoint set as the echo canceller's reference. Formats are converted by the audio engine, so capture
/// arrives as mono float at the rate asked for and playback takes mono float.
/// </summary>
[SupportedOSPlatform("windows")]
internal static class WasapiStreams
{
    public const int RenderFlow  = 0;
    public const int CaptureFlow = 1;
    public const int ConsoleRole = 0;
    public const int CommunicationsRole = 2;

    private const int ClsCtxAll = 0x17;
    private const uint AutoConvertPcm = 0x80000000, SrcDefaultQuality = 0x08000000;
    private const int CategoryCommunications = 3;

    private static readonly Guid AudioClient2Id     = new("726778CD-F60A-4eda-82DE-E47610CD78AA");
    private static readonly Guid CaptureClientId    = new("C8ADBD64-E71E-48a0-A4DE-185C395CD317");
    private static readonly Guid RenderClientId     = new("F294ACFC-3146-4483-A7BF-ADDCA7C260E2");
    private static readonly Guid AudioClockId       = new("CD63314F-3FBA-4a1b-812C-EF96358728E7");
    private static readonly Guid EffectsManagerId   = new("4460B3AE-4B44-4527-8676-7548A8ACD260");
    private static readonly Guid EchoCancellationId = new("f4ae25b5-aaa3-437d-b6b3-dbbe2d0e9549");

    /// <summary>The acoustic echo cancellation effect, as <c>IAudioEffectsManager</c> names it.</summary>
    private static readonly Guid EchoCancellationEffect = new("6f64adbe-8211-11e2-8c70-2c27d7f001fa");

    public static string DefaultEndpoint(int flow, int role)
    {
        var enumerator = (IMMDeviceEnumerator)new MMDeviceEnumeratorCom();
        try
        {
            Check(enumerator.GetDefaultAudioEndpoint(flow, role, out var device));
            try
            {
                Check(device.GetId(out string id));
                return id;
            }
            finally { Marshal.ReleaseComObject(device); }
        }
        finally { Marshal.ReleaseComObject(enumerator); }
    }

    /// <summary>An initialised shared-mode client on an endpoint, mono float at <paramref name="rate"/>.</summary>
    public static IAudioClient2 Open(string endpointId, bool communications, int rate, int bufferMs)
    {
        var enumerator = (IMMDeviceEnumerator)new MMDeviceEnumeratorCom();
        IMMDevice? device = null;
        try
        {
            Check(enumerator.GetDevice(endpointId, out device));
            var iid = AudioClient2Id;
            Check(device.Activate(ref iid, ClsCtxAll, IntPtr.Zero, out object o));
            var client = (IAudioClient2)o;
            if (communications)
            {
                var properties = new AudioClientProperties { Size = Marshal.SizeOf<AudioClientProperties>(), Category = CategoryCommunications };
                Check(client.SetClientProperties(ref properties));
            }

            var format = new WaveFormatEx { FormatTag = 3, Channels = 1, SamplesPerSec = rate, BitsPerSample = 32, BlockAlign = 4, AvgBytesPerSec = rate * 4 };
            IntPtr pointer = Marshal.AllocHGlobal(Marshal.SizeOf<WaveFormatEx>());
            try
            {
                Marshal.StructureToPtr(format, pointer, false);
                Check(client.Initialize(0, AutoConvertPcm | SrcDefaultQuality, bufferMs * 10_000L, 0, pointer, IntPtr.Zero));
            }
            finally { Marshal.FreeHGlobal(pointer); }
            return client;
        }
        finally
        {
            if (device is not null) Marshal.ReleaseComObject(device);
            Marshal.ReleaseComObject(enumerator);
        }
    }

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

    /// <summary>Whether Windows reports echo cancellation on for the stream.</summary>
    public static bool CancelsEcho(IAudioClient2 client)
    {
        var iid = EffectsManagerId;
        if (client.GetService(ref iid, out object o) != 0) return false;
        var manager = (IAudioEffectsManager)o;
        try
        {
            if (manager.GetAudioEffects(out IntPtr effects, out uint count) != 0) return false;
            try
            {
                int size = Marshal.SizeOf<AudioEffect>();
                for (int i = 0; i < count; i++)
                {
                    var effect = Marshal.PtrToStructure<AudioEffect>(effects + i * size);
                    if (effect.Id == EchoCancellationEffect && effect.State == 1) return true;
                }
                return false;
            }
            finally { if (effects != IntPtr.Zero) Marshal.FreeCoTaskMem(effects); }
        }
        finally { Marshal.ReleaseComObject(manager); }
    }

    /// <summary>Points the stream's echo canceller at a playback endpoint; false where the stream offers no control,
    /// and the canceller follows the default playback device.</summary>
    public static bool SetEchoReference(IAudioClient2 client, string renderEndpointId)
    {
        var iid = EchoCancellationId;
        if (client.GetService(ref iid, out object o) != 0) return false;
        try { return ((IAcousticEchoCancellationControl)o).SetEchoCancellationRenderEndpoint(renderEndpointId) == 0; }
        finally { Marshal.ReleaseComObject(o); }
    }

    public static IAudioCaptureClient Capture(IAudioClient2 client) => Service<IAudioCaptureClient>(client, CaptureClientId);
    public static IAudioRenderClient Render(IAudioClient2 client) => Service<IAudioRenderClient>(client, RenderClientId);
    public static IAudioClock Clock(IAudioClient2 client) => Service<IAudioClock>(client, AudioClockId);

    private static T Service<T>(IAudioClient2 client, Guid id) where T : class
    {
        Check(client.GetService(ref id, out object o));
        return (T)o;
    }

    public static void Check(int hr)
    {
        if (hr < 0) Marshal.ThrowExceptionForHR(hr);
    }
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

[ComImport, Guid("CD63314F-3FBA-4a1b-812C-EF96358728E7"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IAudioClock
{
    [PreserveSig] int GetFrequency(out ulong frequency);
    [PreserveSig] int GetPosition(out ulong position, out ulong qpcPosition);
    [PreserveSig] int GetCharacteristics(out uint characteristics);
}

[ComImport, Guid("4460B3AE-4B44-4527-8676-7548A8ACD260"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IAudioEffectsManager
{
    [PreserveSig] int RegisterAudioEffectsChangedNotificationCallback(IntPtr client);
    [PreserveSig] int UnregisterAudioEffectsChangedNotificationCallback(IntPtr client);
    [PreserveSig] int GetAudioEffects(out IntPtr effects, out uint count);
    [PreserveSig] int SetAudioEffectState(Guid effectId, int state);
}

[ComImport, Guid("f4ae25b5-aaa3-437d-b6b3-dbbe2d0e9549"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IAcousticEchoCancellationControl
{
    [PreserveSig] int SetEchoCancellationRenderEndpoint([MarshalAs(UnmanagedType.LPWStr)] string endpointId);
}

[StructLayout(LayoutKind.Sequential)]
internal struct AudioClientProperties
{
    public int Size;
    public int IsOffload;
    public int Category;
    public int Options;
}

[StructLayout(LayoutKind.Sequential)]
internal struct AudioEffect
{
    public Guid Id;
    public int CanSetState;
    public int State;
}

[StructLayout(LayoutKind.Sequential, Pack = 2)]
internal struct WaveFormatEx
{
    public ushort FormatTag;
    public ushort Channels;
    public int SamplesPerSec;
    public int AvgBytesPerSec;
    public ushort BlockAlign;
    public ushort BitsPerSample;
    public ushort Size;
}
