using System;

namespace Nexaflow.Tests.Fixtures;

/// <summary>
/// Audio fixtures for the player. Small, byte-exact PCM WAV tones (no <c>Random</c>, no codecs) so the
/// dataset stays reproducible — enough to exercise the player tab opening, the waveform analyser, and
/// mono/stereo handling. Only audio files live here (every file in a set must open in the same viewer,
/// per <c>SampleFileViewerTests</c>), so lyrics parsing is covered by unit tests instead.
/// </summary>
internal sealed class AudioSamples : ISampleSet
{
    public string SubDirectory => "audio";

    public IReadOnlyList<SampleFile> Files { get; } =
    [
        SampleFile.Raw("tone_mono.wav",   Wav(sampleRate: 8000,  channels: 1, seconds: 0.3, freq: 440)),
        SampleFile.Raw("tone_stereo.wav", Wav(sampleRate: 44100, channels: 2, seconds: 0.3, freq: 220)),
    ];

    /// <summary>A minimal 16-bit PCM WAV of a sine tone — the simplest losslessly-encodable audio.</summary>
    private static byte[] Wav(int sampleRate, int channels, double seconds, double freq)
    {
        int frames = (int)(sampleRate * seconds);
        const int bytesPerSample = 2;
        int dataSize = frames * channels * bytesPerSample;
        var buf = new byte[44 + dataSize];

        WriteAscii(buf, 0, "RIFF");
        WriteI32(buf, 4, 36 + dataSize);
        WriteAscii(buf, 8, "WAVE");

        WriteAscii(buf, 12, "fmt ");
        WriteI32(buf, 16, 16);                                   // PCM fmt chunk size
        WriteI16(buf, 20, 1);                                    // audio format = PCM
        WriteI16(buf, 22, (short)channels);
        WriteI32(buf, 24, sampleRate);
        WriteI32(buf, 28, sampleRate * channels * bytesPerSample); // byte rate
        WriteI16(buf, 32, (short)(channels * bytesPerSample));   // block align
        WriteI16(buf, 34, 16);                                   // bits per sample

        WriteAscii(buf, 36, "data");
        WriteI32(buf, 40, dataSize);

        int p = 44;
        for (int n = 0; n < frames; n++)
        {
            double t = (double)n / sampleRate;
            short s = (short)(Math.Sin(2 * Math.PI * freq * t) * 8000);
            for (int c = 0; c < channels; c++)
            {
                buf[p++] = (byte)(s & 0xFF);
                buf[p++] = (byte)((s >> 8) & 0xFF);
            }
        }
        return buf;
    }

    private static void WriteAscii(byte[] buf, int off, string s)
    {
        for (int i = 0; i < s.Length; i++) buf[off + i] = (byte)s[i];
    }

    private static void WriteI32(byte[] buf, int off, int v)
    {
        buf[off]     = (byte)v;
        buf[off + 1] = (byte)(v >> 8);
        buf[off + 2] = (byte)(v >> 16);
        buf[off + 3] = (byte)(v >> 24);
    }

    private static void WriteI16(byte[] buf, int off, short v)
    {
        buf[off]     = (byte)v;
        buf[off + 1] = (byte)(v >> 8);
    }
}
