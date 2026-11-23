using System.Buffers.Binary;

namespace Nexaflow.AiHost.Speech;

/// <summary>
/// The host's audio as it crosses the channel — 16-bit signed little-endian mono PCM — and as its models work it: samples of
/// -1 to 1. And the WAV a language model's projector reads a recording from: the same samples behind the header llama.cpp's reader
/// expects. The host takes and gives raw samples only; files are its callers'.
/// </summary>
internal static class PcmSamples
{
    /// <summary>The rate the host's speech models and a language model's projector hear at.</summary>
    public const int Rate = 16_000;

    public static byte[] FromSamples(ReadOnlySpan<float> samples)
    {
        var pcm = new byte[samples.Length * 2];
        for (int i = 0; i < samples.Length; i++)
            BinaryPrimitives.WriteInt16LittleEndian(pcm.AsSpan(i * 2), (short)Math.Clamp(samples[i] * 32767f, short.MinValue, short.MaxValue));
        return pcm;
    }

    public static float[] ToSamples(ReadOnlySpan<byte> pcm)
    {
        var samples = new float[pcm.Length / 2];
        for (int i = 0; i < samples.Length; i++) samples[i] = BinaryPrimitives.ReadInt16LittleEndian(pcm[(i * 2)..]) / 32768f;
        return samples;
    }

    /// <summary><paramref name="pcm"/>, mono at <see cref="Rate"/>, as the WAV a projector reads.</summary>
    public static byte[] Wav(ReadOnlySpan<byte> pcm)
    {
        var wav = new byte[44 + pcm.Length];
        "RIFF"u8.CopyTo(wav);
        BinaryPrimitives.WriteInt32LittleEndian(wav.AsSpan(4), 36 + pcm.Length);
        "WAVEfmt "u8.CopyTo(wav.AsSpan(8));
        BinaryPrimitives.WriteInt32LittleEndian(wav.AsSpan(16), 16);
        BinaryPrimitives.WriteInt16LittleEndian(wav.AsSpan(20), 1);   // PCM
        BinaryPrimitives.WriteInt16LittleEndian(wav.AsSpan(22), 1);   // mono
        BinaryPrimitives.WriteInt32LittleEndian(wav.AsSpan(24), Rate);
        BinaryPrimitives.WriteInt32LittleEndian(wav.AsSpan(28), Rate * 2);
        BinaryPrimitives.WriteInt16LittleEndian(wav.AsSpan(32), 2);
        BinaryPrimitives.WriteInt16LittleEndian(wav.AsSpan(34), 16);
        "data"u8.CopyTo(wav.AsSpan(36));
        BinaryPrimitives.WriteInt32LittleEndian(wav.AsSpan(40), pcm.Length);
        pcm.CopyTo(wav.AsSpan(44));
        return wav;
    }
}
