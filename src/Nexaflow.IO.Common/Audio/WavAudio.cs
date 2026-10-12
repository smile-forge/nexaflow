using System.Buffers.Binary;

namespace Nexaflow.IO.Common.Audio;

/// <summary>
/// WAV in and out, as speech models want it: any PCM or IEEE-float WAV, read as 16-bit mono at its own rate, and
/// written back as a 16-bit mono WAV at another. Compressed formats are not read here — they are decoded where a
/// codec is at hand.
/// </summary>
public static class WavAudio
{
    /// <summary>The rate speech models read.</summary>
    public const int SpeechRate = 16_000;

    /// <summary>The recording as a 16-bit mono WAV at <paramref name="rate"/> — itself, when it already is one.</summary>
    /// <exception cref="InvalidDataException">It is not a PCM or IEEE-float WAV.</exception>
    public static byte[] ToMono(ReadOnlySpan<byte> wav, int rate = SpeechRate)
    {
        var (pcm, from) = ReadMono(wav);
        return Write(from == rate ? pcm : new PcmResampler(from, rate).Convert(pcm), rate);
    }

    /// <summary>The samples, downmixed to 16-bit mono, and their rate.</summary>
    /// <exception cref="InvalidDataException">It is not a PCM or IEEE-float WAV.</exception>
    public static (byte[] Pcm, int Rate) ReadMono(ReadOnlySpan<byte> wav)
    {
        if (wav.Length < 12 || !wav[..4].SequenceEqual("RIFF"u8) || !wav[8..12].SequenceEqual("WAVE"u8))
            throw new InvalidDataException("Not a WAV file.");

        int format = 0, channels = 0, rate = 0, bits = 0;
        for (int at = 12; at + 8 <= wav.Length;)
        {
            var id   = wav.Slice(at, 4);
            int size = BinaryPrimitives.ReadInt32LittleEndian(wav[(at + 4)..]);
            var body = wav.Slice(at + 8, Math.Min(size, wav.Length - at - 8));
            if (id.SequenceEqual("fmt "u8))
            {
                format   = BinaryPrimitives.ReadUInt16LittleEndian(body);
                channels = BinaryPrimitives.ReadUInt16LittleEndian(body[2..]);
                rate     = BinaryPrimitives.ReadInt32LittleEndian(body[4..]);
                bits     = BinaryPrimitives.ReadUInt16LittleEndian(body[14..]);
                if (format == 0xFFFE && body.Length >= 26) format = BinaryPrimitives.ReadUInt16LittleEndian(body[24..]);   // extensible: the sub-format's tag
            }
            else if (id.SequenceEqual("data"u8))
            {
                if (channels == 0) break;
                return (Downmix(body, format, channels, bits), rate);
            }
            at += 8 + size + (size & 1);
        }
        throw new InvalidDataException("The WAV file has no format or no samples.");
    }

    public static byte[] Write(ReadOnlySpan<byte> pcm, int rate)
    {
        var wav = new byte[44 + pcm.Length];
        "RIFF"u8.CopyTo(wav);
        BinaryPrimitives.WriteInt32LittleEndian(wav.AsSpan(4), 36 + pcm.Length);
        "WAVEfmt "u8.CopyTo(wav.AsSpan(8));
        BinaryPrimitives.WriteInt32LittleEndian(wav.AsSpan(16), 16);
        BinaryPrimitives.WriteInt16LittleEndian(wav.AsSpan(20), 1);
        BinaryPrimitives.WriteInt16LittleEndian(wav.AsSpan(22), 1);
        BinaryPrimitives.WriteInt32LittleEndian(wav.AsSpan(24), rate);
        BinaryPrimitives.WriteInt32LittleEndian(wav.AsSpan(28), rate * 2);
        BinaryPrimitives.WriteInt16LittleEndian(wav.AsSpan(32), 2);
        BinaryPrimitives.WriteInt16LittleEndian(wav.AsSpan(34), 16);
        "data"u8.CopyTo(wav.AsSpan(36));
        BinaryPrimitives.WriteInt32LittleEndian(wav.AsSpan(40), pcm.Length);
        pcm.CopyTo(wav.AsSpan(44));
        return wav;
    }

    private static byte[] Downmix(ReadOnlySpan<byte> data, int format, int channels, int bits)
    {
        const int Pcm = 1, Float = 3;
        if (!(format == Pcm && bits is 8 or 16 or 24 or 32) && !(format == Float && bits == 32))
            throw new InvalidDataException($"WAV format {format} at {bits} bits is not PCM or 32-bit float.");

        int bytes  = bits / 8;
        int frames = data.Length / (bytes * channels);
        var mono   = new byte[frames * 2];
        for (int f = 0; f < frames; f++)
        {
            double sum = 0;
            for (int c = 0; c < channels; c++)
                sum += Sample(data.Slice((f * channels + c) * bytes, bytes), format, bits);
            short value = (short)Math.Clamp(Math.Round(sum / channels * short.MaxValue), short.MinValue, short.MaxValue);
            BinaryPrimitives.WriteInt16LittleEndian(mono.AsSpan(f * 2), value);
        }
        return mono;
    }

    /// <summary>One sample, from -1 to 1.</summary>
    private static double Sample(ReadOnlySpan<byte> s, int format, int bits) => (format, bits) switch
    {
        (3, 32) => BinaryPrimitives.ReadSingleLittleEndian(s),
        (_, 8)  => (s[0] - 128) / 128.0,
        (_, 16) => BinaryPrimitives.ReadInt16LittleEndian(s) / 32768.0,
        (_, 24) => ((s[0] | (s[1] << 8) | ((sbyte)s[2] << 16))) / 8388608.0,
        _       => BinaryPrimitives.ReadInt32LittleEndian(s) / 2147483648.0,
    };
}
