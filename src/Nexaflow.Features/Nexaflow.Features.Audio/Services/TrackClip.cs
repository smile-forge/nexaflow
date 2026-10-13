using System.Buffers.Binary;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;
using Nexaflow.IO.Common.Audio;

namespace Nexaflow.Features.Audio.Services;

/// <summary>
/// A stretch of a track as a model hears it: decoded whatever the format, mixed to mono, at the 16 kHz speech models
/// read. Thirty seconds at most — a speech model's window; past it, what was heard would be silently cut.
/// </summary>
internal static class TrackClip
{
    public static readonly TimeSpan Longest = TimeSpan.FromSeconds(30);

    /// <summary>Up to <paramref name="length"/> of the track at <paramref name="path"/> from <paramref name="start"/>,
    /// as a 16-bit mono WAV, and how much it holds — less than asked near the end, nothing past it.</summary>
    public static (byte[] Wav, TimeSpan Length) Read(string path, TimeSpan start, TimeSpan length)
    {
        using var reader = AudioReaderFactory.CreateReader(path);
        var remaining = reader.TotalTime - start;
        if (remaining <= TimeSpan.Zero) return (WavAudio.Write([], WavAudio.SpeechRate), TimeSpan.Zero);
        reader.CurrentTime = start;

        ISampleProvider samples = reader.ToSampleProvider();
        if (samples.WaveFormat.Channels > 1) samples = new ChannelAverage(samples);
        if (samples.WaveFormat.SampleRate != WavAudio.SpeechRate) samples = new WdlResamplingSampleProvider(samples, WavAudio.SpeechRate);

        var wanted = new[] { length, Longest, remaining }.Min();
        var buffer = new float[(int)(wanted.TotalSeconds * WavAudio.SpeechRate)];
        int read = 0, n;
        while (read < buffer.Length && (n = samples.Read(buffer.AsSpan(read))) > 0) read += n;

        return (WavAudio.Write(Pcm16.FromSamples(buffer.AsSpan(0, read)), WavAudio.SpeechRate), TimeSpan.FromSeconds((double)read / WavAudio.SpeechRate));
    }

    /// <summary>Every channel's samples averaged into one.</summary>
    private sealed class ChannelAverage(ISampleProvider source) : ISampleProvider
    {
        private readonly int _channels = source.WaveFormat.Channels;
        private float[] _frames = [];

        public WaveFormat WaveFormat { get; } = WaveFormat.CreateIeeeFloatWaveFormat(source.WaveFormat.SampleRate, 1);

        public int Read(Span<float> buffer)
        {
            if (_frames.Length < buffer.Length * _channels) _frames = new float[buffer.Length * _channels];
            int got = source.Read(_frames.AsSpan(0, buffer.Length * _channels)) / _channels;
            for (int f = 0; f < got; f++)
            {
                float sum = 0;
                for (int c = 0; c < _channels; c++) sum += _frames[f * _channels + c];
                buffer[f] = sum / _channels;
            }
            return got;
        }
    }
}
