using System;
using NAudio.Wave;

namespace Nexaflow.Features.Audio.Services;

/// <summary>
/// One background pass over a whole file producing a fixed-width peak envelope (0..1 per bucket) for
/// the static waveform overview. Reads the decoded sample stream, downmixes to mono, and keeps the
/// peak amplitude per bucket, normalised by the track's overall peak. Best-effort: returns an empty
/// array on any failure so the waveform simply doesn't draw.
/// </summary>
public static class WaveformAnalyzer
{
    public static float[] Analyze(string path, int buckets)
    {
        if (buckets <= 0) return [];

        try
        {
            using var reader = AudioReaderFactory.CreateReader(path);
            var samples = reader.ToSampleProvider();
            int channels = Math.Max(1, samples.WaveFormat.Channels);

            long totalFrames = (long)(reader.TotalTime.TotalSeconds * samples.WaveFormat.SampleRate);
            if (totalFrames <= 0) totalFrames = 1;

            var peaks = new float[buckets];
            var block = new float[8192];
            long frame = 0;
            float overall = 0;

            int read;
            while ((read = samples.Read(block)) > 0)
            {
                for (int i = 0; i + channels <= read; i += channels)
                {
                    float mono = 0;
                    for (int c = 0; c < channels; c++) mono += block[i + c];
                    mono = Math.Abs(mono / channels);

                    int bucket = (int)(frame * buckets / totalFrames);
                    if (bucket < 0) bucket = 0;
                    else if (bucket >= buckets) bucket = buckets - 1;

                    if (mono > peaks[bucket]) peaks[bucket] = mono;
                    if (mono > overall) overall = mono;
                    frame++;
                }
            }

            if (overall > 0)
                for (int b = 0; b < buckets; b++) peaks[b] /= overall;

            return peaks;
        }
        catch
        {
            return [];
        }
    }
}
