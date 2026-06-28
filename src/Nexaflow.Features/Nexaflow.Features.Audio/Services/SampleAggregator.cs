using System;
using NAudio.Dsp;
using NAudio.Wave;

namespace Nexaflow.Features.Audio.Services;

/// <summary>
/// A pass-through <see cref="ISampleProvider"/> that taps the audio it forwards and computes a short
/// FFT for the spectrum analyser. Samples are downmixed to mono, Hann-windowed, transformed, then
/// folded into <see cref="BandCount"/> logarithmically-spaced bands (0..1, dB-scaled). The latest
/// bands are published by atomic reference swap so the UI thread can read them lock-free while NAudio's
/// output thread keeps filling them.
/// </summary>
public sealed class SampleAggregator : ISampleProvider
{
    private const int FftLength = 1024;                 // power of two
    private static readonly int FftM = (int)Math.Log2(FftLength);

    private readonly ISampleProvider _source;
    private readonly int _channels;
    private readonly Complex[] _fft = new Complex[FftLength];
    private int _pos;

    private volatile float[] _latestBands = [];
    private int _bandCount = 64;

    public SampleAggregator(ISampleProvider source)
    {
        _source = source;
        _channels = Math.Max(1, source.WaveFormat.Channels);
    }

    public WaveFormat WaveFormat => _source.WaveFormat;

    /// <summary>Number of spectrum bars to fold the FFT bins into.</summary>
    public int BandCount
    {
        get => _bandCount;
        set => _bandCount = Math.Max(1, value);
    }

    /// <summary>The most recently computed band magnitudes (0..1), or an empty array before the first frame.</summary>
    public float[] LatestBands => _latestBands;

    /// <summary>Clears the published bands (e.g. when playback pauses), so the bars fall to zero.</summary>
    public void Reset() => _latestBands = [];

    public int Read(float[] buffer, int offset, int count)
    {
        int read = _source.Read(buffer, offset, count);

        for (int i = 0; i + _channels <= read; i += _channels)
        {
            float mono = 0;
            for (int c = 0; c < _channels; c++) mono += buffer[offset + i + c];
            mono /= _channels;

            _fft[_pos].X = (float)(mono * FastFourierTransform.HannWindow(_pos, FftLength));
            _fft[_pos].Y = 0;
            if (++_pos >= FftLength)
            {
                _pos = 0;
                ComputeBands();
            }
        }

        return read;
    }

    private void ComputeBands()
    {
        FastFourierTransform.FFT(true, FftM, _fft);

        int bins = FftLength / 2;
        int n = _bandCount;
        var bands = new float[n];

        for (int b = 0; b < n; b++)
        {
            int lo = LogBin(b, n, bins);
            int hi = Math.Max(LogBin(b + 1, n, bins), lo + 1);

            float max = 0;
            for (int i = lo; i < hi && i < bins; i++)
            {
                float mag = MathF.Sqrt(_fft[i].X * _fft[i].X + _fft[i].Y * _fft[i].Y);
                if (mag > max) max = mag;
            }

            // dB scale into a ~60 dB window → 0..1.
            float db = 20f * MathF.Log10(max + 1e-6f);
            bands[b] = Math.Clamp((db + 60f) / 60f, 0f, 1f);
        }

        _latestBands = bands;
    }

    /// <summary>Maps band index → FFT bin index on a log scale spanning bin 1..<paramref name="bins"/>.</summary>
    private static int LogBin(int band, int bandCount, int bins)
    {
        double t = (double)band / bandCount;
        return (int)Math.Round(Math.Pow(bins, t));
    }
}
