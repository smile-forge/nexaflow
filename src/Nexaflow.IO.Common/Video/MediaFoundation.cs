using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using System.Runtime.Versioning;

namespace Nexaflow.IO.Common.Video;

/// <summary>
/// Just enough Media Foundation to encode a clip: a sink writer onto a .NET stream, the media types that describe its
/// one video stream, and the samples and buffers that carry its frames. Slots of an interface this code never calls are
/// left as gaps (<c>_VtblGap</c>), so each declares only what is used.
/// </summary>
[SupportedOSPlatform("windows")]
internal static class MediaFoundation
{
    /// <summary>MF_VERSION: the SDK and API versions <see cref="MFStartup"/> checks.</summary>
    public const uint Version = 0x0002_0070;

    public static readonly Guid MajorType        = new("48eba18e-f8c9-4687-bf11-0a74c9f96a8f");
    public static readonly Guid Subtype          = new("f7e34c9a-42e8-4714-b74b-cb29d72c35e5");
    public static readonly Guid AverageBitrate   = new("20332624-fb0d-4d9e-bd0d-cbf6786c102e");
    public static readonly Guid InterlaceMode    = new("e2724bb8-e676-4806-b4b2-a8d6efb44ccd");
    public static readonly Guid FrameSize        = new("1652c33d-d6b2-4012-b834-72030849a37d");
    public static readonly Guid FrameRate        = new("c459a2e8-3d2c-4e44-b132-fee5156c7bb0");
    public static readonly Guid PixelAspectRatio = new("c6376a1e-8d0a-4027-be45-6d9a0ad39bb6");
    public static readonly Guid Mpeg2Profile     = new("ad76a80b-2d5c-4e0b-b375-64e520137036");
    public static readonly Guid DefaultStride    = new("644b4e48-1e02-4516-b0eb-c01ca9d49ac6");
    public static readonly Guid YuvMatrix        = new("3e23d450-2c75-4d25-a00e-b91670d12327");
    public static readonly Guid NominalRange     = new("c21b8ee5-b956-4071-8daf-325edf5cab11");
    public static readonly Guid Primaries        = new("dbfbe4d7-0740-4ee0-8192-850ab0e21935");
    public static readonly Guid TransferFunction = new("5fb0fce9-be5c-4935-a811-ec838f8eed93");

    public static readonly Guid TranscodeContainerType   = new("150ff23f-4abc-478b-ac4f-e1916fba1cca");
    public static readonly Guid ContainerMpeg4           = new("dc6cd05d-b9d0-40ef-bd35-fa622c1ab28a");
    public static readonly Guid EnableHardwareTransforms = new("a634a91c-822b-41b9-a494-4de4643612b0");
    public static readonly Guid DisableThrottling        = new("08b845d8-2b74-4afe-9d53-be16d2d5ae4f");

    public static readonly Guid MediaTypeVideo = new("73646976-0000-0010-8000-00aa00389b71");
    public static readonly Guid FormatH264     = new("34363248-0000-0010-8000-00aa00389b71");
    public static readonly Guid FormatNv12     = new("3231564e-0000-0010-8000-00aa00389b71");

    public const uint Progressive     = 2;    // MFVideoInterlace_Progressive
    public const uint H264ProfileHigh = 100;  // eAVEncH264VProfile_High
    public const uint MatrixBt709     = 1;    // MFVideoTransferMatrix_BT709
    public const uint Range16To235    = 2;    // MFNominalRange_16_235
    public const uint PrimariesBt709  = 2;    // MFVideoPrimaries_BT709
    public const uint Transfer709     = 5;    // MFVideoTransFunc_709

    [DllImport("mfplat.dll", ExactSpelling = true)]
    public static extern int MFStartup(uint version, uint flags);

    [DllImport("mfplat.dll", ExactSpelling = true)]
    public static extern int MFShutdown();

    [DllImport("mfplat.dll", ExactSpelling = true)]
    public static extern int MFCreateAttributes(out IMFAttributes attributes, uint initialSize);

    [DllImport("mfplat.dll", ExactSpelling = true)]
    public static extern int MFCreateMediaType(out IMFMediaType mediaType);

    [DllImport("mfplat.dll", ExactSpelling = true)]
    public static extern int MFCreateSample(out IMFSample sample);

    [DllImport("mfplat.dll", ExactSpelling = true)]
    public static extern int MFCreateMemoryBuffer(uint maxLength, out IMFMediaBuffer buffer);

    [DllImport("mfplat.dll", ExactSpelling = true)]
    public static extern int MFCreateMFByteStreamOnStream(IStream stream, out IntPtr byteStream);

    [DllImport("mfreadwrite.dll", ExactSpelling = true, CharSet = CharSet.Unicode)]
    public static extern int MFCreateSinkWriterFromURL(string? outputUrl, IntPtr byteStream, IMFAttributes attributes, out IMFSinkWriter writer);

    public static void Check(int hr)
    {
        if (hr < 0) Marshal.ThrowExceptionForHR(hr);
    }

    [ComImport, Guid("2cd2d921-c447-44a7-a13c-4adabfc247e3"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IMFAttributes
    {
        void _VtblGap1_18();
        [PreserveSig] int SetUINT32(in Guid key, uint value);
        [PreserveSig] int SetUINT64(in Guid key, ulong value);
        void _VtblGap2_1();
        [PreserveSig] int SetGUID(in Guid key, in Guid value);
    }

    [ComImport, Guid("44ae0fa8-ea31-4109-8d2e-4cae4997c555"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IMFMediaType
    {
        void _VtblGap1_18();
        [PreserveSig] int SetUINT32(in Guid key, uint value);
        [PreserveSig] int SetUINT64(in Guid key, ulong value);
        void _VtblGap2_1();
        [PreserveSig] int SetGUID(in Guid key, in Guid value);
    }

    [ComImport, Guid("c40a00f2-b93a-4d80-ae8c-5a1c634f58e4"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IMFSample
    {
        void _VtblGap1_33();   // IMFAttributes' thirty, GetSampleFlags, SetSampleFlags, GetSampleTime
        [PreserveSig] int SetSampleTime(long time);
        void _VtblGap2_1();    // GetSampleDuration
        [PreserveSig] int SetSampleDuration(long duration);
        void _VtblGap3_3();    // GetBufferCount, GetBufferByIndex, ConvertToContiguousBuffer
        [PreserveSig] int AddBuffer(IMFMediaBuffer buffer);
    }

    [ComImport, Guid("045fa593-8799-42b8-bc8d-8968c6453507"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IMFMediaBuffer
    {
        [PreserveSig] int Lock(out IntPtr buffer, out uint maxLength, out uint currentLength);
        [PreserveSig] int Unlock();
        [PreserveSig] int GetCurrentLength(out uint length);
        [PreserveSig] int SetCurrentLength(uint length);
    }

    [ComImport, Guid("3137f1cd-fe5e-4805-a5d8-fb477448cb3d"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IMFSinkWriter
    {
        [PreserveSig] int AddStream(IMFMediaType targetType, out uint streamIndex);
        [PreserveSig] int SetInputMediaType(uint streamIndex, IMFMediaType inputType, IMFAttributes? encodingParameters);
        [PreserveSig] int BeginWriting();
        [PreserveSig] int WriteSample(uint streamIndex, IMFSample sample);
        void _VtblGap1_4();    // SendStreamTick, PlaceMarker, NotifyEndOfSegment, Flush
        [PreserveSig] int FinalizeWriting();
    }

    /// <summary>A .NET stream as the COM stream Media Foundation writes the file through.</summary>
    internal sealed class ManagedStream(Stream stream) : IStream
    {
        private const int NotImplemented = unchecked((int)0x80004001);

        public void Read(byte[] pv, int cb, IntPtr pcbRead)
        {
            int read = stream.Read(pv, 0, cb);
            if (pcbRead != IntPtr.Zero) Marshal.WriteInt32(pcbRead, read);
        }

        public void Write(byte[] pv, int cb, IntPtr pcbWritten)
        {
            stream.Write(pv, 0, cb);
            if (pcbWritten != IntPtr.Zero) Marshal.WriteInt32(pcbWritten, cb);
        }

        public void Seek(long dlibMove, int dwOrigin, IntPtr plibNewPosition)
        {
            long position = stream.Seek(dlibMove, (SeekOrigin)dwOrigin);
            if (plibNewPosition != IntPtr.Zero) Marshal.WriteInt64(plibNewPosition, position);
        }

        public void SetSize(long libNewSize) => stream.SetLength(libNewSize);

        /// <summary>The stream's size, and that it is written to: Media Foundation refuses to write a file onto a stream whose
        /// access mode does not say so.</summary>
        public void Stat(out STATSTG pstatstg, int grfStatFlag)
            => pstatstg = new STATSTG { type = 2 /* STGTY_STREAM */, cbSize = stream.Length, grfMode = stream.CanRead ? 2 /* STGM_READWRITE */ : 1 /* STGM_WRITE */ };

        public void Commit(int grfCommitFlags) => stream.Flush();

        public void CopyTo(IStream pstm, long cb, IntPtr pcbRead, IntPtr pcbWritten) => throw new COMException(null, NotImplemented);
        public void Revert() => throw new COMException(null, NotImplemented);
        public void LockRegion(long libOffset, long cb, int dwLockType) => throw new COMException(null, NotImplemented);
        public void UnlockRegion(long libOffset, long cb, int dwLockType) => throw new COMException(null, NotImplemented);
        public void Clone(out IStream ppstm) => throw new COMException(null, NotImplemented);
    }
}
