using System.Buffers.Binary;

namespace Nexaflow.IO.Common.Audio;

/// <summary>16-bit signed little-endian PCM — the form speech travels in — to and from samples in [-1, 1].</summary>
public static class Pcm16
{
    /// <summary>The samples as PCM, clipped at full scale.</summary>
    public static byte[] FromSamples(ReadOnlySpan<float> samples)
    {
        var pcm = new byte[samples.Length * 2];
        for (int i = 0; i < samples.Length; i++)
            BinaryPrimitives.WriteInt16LittleEndian(pcm.AsSpan(i * 2), (short)Math.Clamp(samples[i] * 32767f, short.MinValue, short.MaxValue));
        return pcm;
    }

    /// <summary>The PCM as samples; a trailing odd byte is not a sample.</summary>
    public static float[] ToSamples(ReadOnlySpan<byte> pcm)
    {
        var samples = new float[pcm.Length / 2];
        for (int i = 0; i < samples.Length; i++) samples[i] = BinaryPrimitives.ReadInt16LittleEndian(pcm[(i * 2)..]) / 32768f;
        return samples;
    }
}
