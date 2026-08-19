using System;
using System.IO;
using System.Runtime.InteropServices;
using NAudio.Wave;
using NVorbis;

namespace Nexaflow.Features.Audio.Services;

/// <summary>
/// A seekable <see cref="WaveStream"/> over an Ogg Vorbis file, decoding on demand through NVorbis.
/// Presented as 32-bit IEEE float at the file's own rate and channel count, which is what NVorbis
/// produces — so the caller's byte buffer is decoded into directly, with no intermediate copy.
/// <para>
/// This replaces the NAudio.Vorbis package, which is built against NAudio 2.x: its reader implements
/// the array-based <c>ISampleProvider.Read</c> that NAudio 3.0 replaced with a <see cref="Span{T}"/>
/// overload, so merely naming the type throws <see cref="TypeLoadException"/>. NVorbis is the decoder
/// underneath that package anyway, and has no NAudio coupling of its own.
/// </para>
/// </summary>
public sealed class VorbisWaveStream : WaveStream
{
    private readonly VorbisReader _reader;

    public VorbisWaveStream(string path)
    {
        _reader = new VorbisReader(path);
        WaveFormat = WaveFormat.CreateIeeeFloatWaveFormat(_reader.SampleRate, _reader.Channels);
    }

    public override WaveFormat WaveFormat { get; }

    /// <summary>Length in bytes, derived from the decoded duration so it stays consistent with
    /// <see cref="Position"/> (both are the same bytes-per-second scale).</summary>
    public override long Length => (long)(_reader.TotalTime.TotalSeconds * WaveFormat.AverageBytesPerSecond);

    public override long Position
    {
        get => _reader.SamplePosition * WaveFormat.BlockAlign;
        set => _reader.SeekTo(Math.Max(0, value / WaveFormat.BlockAlign), SeekOrigin.Begin);
    }

    public override int Read(byte[] buffer, int offset, int count)
    {
        // The stream is 32-bit float, so the byte buffer IS a float buffer — decode straight into it.
        // Trim to whole samples: a caller is free to ask for a count the block size doesn't divide.
        var floats = MemoryMarshal.Cast<byte, float>(buffer.AsSpan(offset, count - count % sizeof(float)));
        return _reader.ReadSamples(floats) * sizeof(float);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) _reader.Dispose();
        base.Dispose(disposing);
    }
}
