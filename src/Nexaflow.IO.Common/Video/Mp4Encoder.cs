using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace Nexaflow.IO.Common.Video;

/// <summary>
/// Writes 8-bit RGB frames as an H.264 video in an MP4, through the encoder Windows has built in (Media Foundation), on
/// the GPU's own video encoder where it has one: for code that makes clips but has no media stack, such as the AI host.
/// Frames are turned into BT.709 limited-range YUV here and the file says so, so every player shows the colours as made.
/// Frames go in one at a time, so a clip is never held whole; the MP4 is finished by <see cref="Finish"/>.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class Mp4Encoder : IDisposable
{
    /// <summary>The bitrate a clip is written at, per pixel of each frame: generous, since a made clip is kept.</summary>
    private const double BitsPerPixel = 0.3;

    private const long TicksPerSecond = 10_000_000;

    private readonly int _width, _height, _framesPerSecond;
    private readonly MediaFoundation.ManagedStream _stream;
    private readonly MediaFoundation.IMFSinkWriter _writer;
    private readonly uint _streamIndex;
    private readonly byte[] _yuv;
    private long _frames;
    private bool _finished, _disposed;

    /// <param name="destination">Where the MP4 is written: a stream that can seek, since the file's index is written last.</param>
    /// <exception cref="COMException">Windows has no H.264 encoder — an N edition without its Media Feature Pack.</exception>
    public Mp4Encoder(Stream destination, int width, int height, int framesPerSecond)
    {
        ArgumentNullException.ThrowIfNull(destination);
        if (!destination.CanSeek || !destination.CanWrite) throw new ArgumentException("An MP4 is written to a stream that can seek.", nameof(destination));
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(framesPerSecond);
        if (width % 2 != 0 || height % 2 != 0) throw new ArgumentException($"H.264 takes frames of even sides, not {width}x{height}.");

        _width = width;
        _height = height;
        _framesPerSecond = framesPerSecond;
        _yuv = new byte[width * height * 3 / 2];
        _stream = new MediaFoundation.ManagedStream(destination);

        MediaFoundation.Check(MediaFoundation.MFStartup(MediaFoundation.Version, 0));
        try
        {
            _writer = CreateWriter(_stream);
            _streamIndex = AddVideoStream(_writer, width, height, framesPerSecond);
            MediaFoundation.Check(_writer.BeginWriting());
        }
        catch
        {
            MediaFoundation.MFShutdown();
            throw;
        }
    }

    /// <summary>How many frames have gone in.</summary>
    public long Frames => _frames;

    /// <summary>The next frame: <paramref name="rgb"/> holds it row by row, three bytes a pixel.</summary>
    public void AddFrame(ReadOnlySpan<byte> rgb)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_finished) throw new InvalidOperationException("The clip is finished.");
        if (rgb.Length != _width * _height * 3)
            throw new ArgumentException($"A {_width}x{_height} RGB frame takes {_width * _height * 3} bytes, not {rgb.Length}.", nameof(rgb));

        Yuv.FromRgb(rgb, _width, _height, _yuv);
        MediaFoundation.Check(MediaFoundation.MFCreateMemoryBuffer((uint)_yuv.Length, out var buffer));
        try
        {
            MediaFoundation.Check(buffer.Lock(out IntPtr data, out _, out _));
            try { Marshal.Copy(_yuv, 0, data, _yuv.Length); }
            finally { buffer.Unlock(); }
            MediaFoundation.Check(buffer.SetCurrentLength((uint)_yuv.Length));

            MediaFoundation.Check(MediaFoundation.MFCreateSample(out var sample));
            try
            {
                long start = Time(_frames), end = Time(_frames + 1);
                MediaFoundation.Check(sample.AddBuffer(buffer));
                MediaFoundation.Check(sample.SetSampleTime(start));
                MediaFoundation.Check(sample.SetSampleDuration(end - start));
                MediaFoundation.Check(_writer.WriteSample(_streamIndex, sample));
            }
            finally { Marshal.ReleaseComObject(sample); }
        }
        finally { Marshal.ReleaseComObject(buffer); }
        _frames++;
    }

    /// <summary>Encodes what is still in the encoder and writes the MP4's index; the stream then holds the whole file.</summary>
    public void Finish()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_finished) return;
        if (_frames == 0) throw new InvalidOperationException("A clip has at least one frame.");
        MediaFoundation.Check(_writer.FinalizeWriting());
        _finished = true;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Marshal.ReleaseComObject(_writer);
        MediaFoundation.MFShutdown();
    }

    /// <summary>When frame <paramref name="index"/> starts, in 100 ns units: whole frames from 0, so the timestamps never drift.</summary>
    private long Time(long index) => index * TicksPerSecond / _framesPerSecond;

    private static MediaFoundation.IMFSinkWriter CreateWriter(MediaFoundation.ManagedStream stream)
    {
        MediaFoundation.Check(MediaFoundation.MFCreateMFByteStreamOnStream(stream, out IntPtr byteStream));
        try
        {
            MediaFoundation.Check(MediaFoundation.MFCreateAttributes(out var attributes, 3));
            try
            {
                MediaFoundation.Check(attributes.SetGUID(MediaFoundation.TranscodeContainerType, MediaFoundation.ContainerMpeg4));
                MediaFoundation.Check(attributes.SetUINT32(MediaFoundation.EnableHardwareTransforms, 1));
                MediaFoundation.Check(attributes.SetUINT32(MediaFoundation.DisableThrottling, 1));
                MediaFoundation.Check(MediaFoundation.MFCreateSinkWriterFromURL(null, byteStream, attributes, out var writer));
                return writer;
            }
            finally { Marshal.ReleaseComObject(attributes); }
        }
        finally { Marshal.Release(byteStream); }
    }

    /// <summary>An H.264 High-profile stream, fed NV12 frames of the same size and rate.</summary>
    private static uint AddVideoStream(MediaFoundation.IMFSinkWriter writer, int width, int height, int framesPerSecond)
    {
        uint index;
        MediaFoundation.Check(MediaFoundation.MFCreateMediaType(out var output));
        try
        {
            Describe(output, MediaFoundation.FormatH264, width, height, framesPerSecond);
            MediaFoundation.Check(output.SetUINT32(MediaFoundation.AverageBitrate, (uint)(width * height * framesPerSecond * BitsPerPixel)));
            MediaFoundation.Check(output.SetUINT32(MediaFoundation.Mpeg2Profile, MediaFoundation.H264ProfileHigh));
            MediaFoundation.Check(writer.AddStream(output, out index));
        }
        finally { Marshal.ReleaseComObject(output); }

        MediaFoundation.Check(MediaFoundation.MFCreateMediaType(out var input));
        try
        {
            Describe(input, MediaFoundation.FormatNv12, width, height, framesPerSecond);
            MediaFoundation.Check(input.SetUINT32(MediaFoundation.DefaultStride, (uint)width));
            MediaFoundation.Check(writer.SetInputMediaType(index, input, null));
        }
        finally { Marshal.ReleaseComObject(input); }
        return index;
    }

    /// <summary>What both ends of the stream share: progressive frames of this size and rate, square pixels, BT.709 colour.</summary>
    private static void Describe(MediaFoundation.IMFMediaType type, Guid subtype, int width, int height, int framesPerSecond)
    {
        MediaFoundation.Check(type.SetGUID(MediaFoundation.MajorType, MediaFoundation.MediaTypeVideo));
        MediaFoundation.Check(type.SetGUID(MediaFoundation.Subtype, subtype));
        MediaFoundation.Check(type.SetUINT32(MediaFoundation.InterlaceMode, MediaFoundation.Progressive));
        MediaFoundation.Check(type.SetUINT64(MediaFoundation.FrameSize, Pair(width, height)));
        MediaFoundation.Check(type.SetUINT64(MediaFoundation.FrameRate, Pair(framesPerSecond, 1)));
        MediaFoundation.Check(type.SetUINT64(MediaFoundation.PixelAspectRatio, Pair(1, 1)));
        MediaFoundation.Check(type.SetUINT32(MediaFoundation.YuvMatrix, MediaFoundation.MatrixBt709));
        MediaFoundation.Check(type.SetUINT32(MediaFoundation.NominalRange, MediaFoundation.Range16To235));
        MediaFoundation.Check(type.SetUINT32(MediaFoundation.Primaries, MediaFoundation.PrimariesBt709));
        MediaFoundation.Check(type.SetUINT32(MediaFoundation.TransferFunction, MediaFoundation.Transfer709));
    }

    private static ulong Pair(int high, int low) => ((ulong)(uint)high << 32) | (uint)low;
}
