using System.Numerics;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using Nexaflow.AiHost.Contracts;
using Nexaflow.AiHost.Models;

namespace Nexaflow.AiHost.Speech;

/// <summary>
/// Smart Turn v3 (pipecat-ai, BSD-2): a Whisper-tiny encoder that judges from the last 8 s of a turn's audio whether
/// the speaker has finished — the pause after "I was thinking that…" sounds different from the one after a question.
/// On four CPU threads; a decision costs about 10 ms.
/// </summary>
internal sealed class SmartTurn(ModelSpec spec, InferenceSession session) : IHostModel, ITurnDetector
{
    private readonly SmartTurnFeatures _features = new();

    public ModelSpec Spec { get; } = spec;

    public float CompleteProbability(ReadOnlySpan<float> turn)
    {
        var features = _features.Compute(turn);
        using var results = session.Run(
        [
            NamedOnnxValue.CreateFromTensor("input_features",
                new DenseTensor<float>(features, [1, SmartTurnFeatures.Mels, SmartTurnFeatures.Frames])),
        ]);
        return results.First().AsTensor<float>().GetValue(0);
    }

    public void Dispose() => session.Dispose();
}

internal sealed class SmartTurnRunner : IModelRunner
{
    public string Id => "smart-turn";

    public IHostModel Load(ModelSpec spec, string executionProvider) => new SmartTurn(spec, CpuSessions.Open(spec, threads: 4));
}

/// <summary>
/// The input Smart Turn was trained on — what Whisper's feature extractor makes with an 8 s chunk and normalisation:
/// the last 8 s of 16 kHz audio, front-padded with silence and normalised to zero mean and unit variance, as an
/// 80-band log-mel spectrogram (25 ms window, 10 ms hop, Slaney mel scale).
/// </summary>
internal sealed class SmartTurnFeatures
{
    public const int Mels = 80;
    private const int Rate = 16_000;
    private const int Samples = Rate * 8;
    private const int FftSize = 400;
    private const int Hop = 160;
    public const int Frames = Samples / Hop;
    private const int Bins = FftSize / 2 + 1;

    // Whisper's window is 400 samples, not a power of two, so the transform is a direct one: a dot product per bin
    // against precomputed rows, vectorised.
    private static readonly float[] Window = HannWindow();
    private static readonly float[][] Cos = Basis(Math.Cos);
    private static readonly float[][] Sin = Basis(Math.Sin);
    private static readonly float[][] MelBands = MelFilters();

    /// <returns>[<see cref="Mels"/>, <see cref="Frames"/>], row-major.</returns>
    public float[] Compute(ReadOnlySpan<float> audio)
    {
        var x = new float[Samples];
        int take = Math.Min(audio.Length, Samples);
        audio[^take..].CopyTo(x.AsSpan(Samples - take));

        double mean = 0;
        foreach (float v in x) mean += v;
        mean /= Samples;
        double variance = 0;
        foreach (float v in x) variance += (v - mean) * (v - mean);
        float scale = (float)(1.0 / Math.Sqrt(variance / Samples + 1e-7));

        // Reflect-padded by half a window either side (the extractor's center=True).
        const int pad = FftSize / 2;
        var padded = new float[Samples + 2 * pad];
        for (int i = 0; i < padded.Length; i++)
        {
            int j = i - pad;
            if (j < 0) j = -j;
            else if (j >= Samples) j = 2 * (Samples - 1) - j;
            padded[i] = (float)((x[j] - mean) * scale);
        }

        var logSpec = new float[Mels * Frames];
        var frame = new float[FftSize];
        var power = new float[Bins];
        float max = float.NegativeInfinity;
        for (int t = 0; t < Frames; t++)   // the extractor computes one more frame and drops it
        {
            for (int n = 0; n < FftSize; n++) frame[n] = padded[t * Hop + n] * Window[n];
            for (int k = 0; k < Bins; k++)
            {
                float re = Dot(frame, Cos[k]), im = Dot(frame, Sin[k]);
                power[k] = re * re + im * im;
            }
            for (int m = 0; m < Mels; m++)
            {
                float v = MathF.Log10(MathF.Max(Dot(power, MelBands[m]), 1e-10f));
                logSpec[m * Frames + t] = v;
                if (v > max) max = v;
            }
        }
        for (int i = 0; i < logSpec.Length; i++) logSpec[i] = (MathF.Max(logSpec[i], max - 8f) + 4f) / 4f;
        return logSpec;
    }

    private static float Dot(ReadOnlySpan<float> a, ReadOnlySpan<float> b)
    {
        int width = Vector<float>.Count, i = 0;
        var sum = Vector<float>.Zero;
        for (; i <= a.Length - width; i += width) sum += new Vector<float>(a[i..]) * new Vector<float>(b[i..]);
        float total = Vector.Sum(sum);
        for (; i < a.Length; i++) total += a[i] * b[i];
        return total;
    }

    /// <summary>A periodic Hann window.</summary>
    private static float[] HannWindow()
    {
        var w = new float[FftSize];
        for (int n = 0; n < FftSize; n++) w[n] = (float)(0.5 - 0.5 * Math.Cos(2 * Math.PI * n / FftSize));
        return w;
    }

    private static float[][] Basis(Func<double, double> f)
    {
        var rows = new float[Bins][];
        for (int k = 0; k < Bins; k++)
        {
            rows[k] = new float[FftSize];
            for (int n = 0; n < FftSize; n++) rows[k][n] = (float)f(2 * Math.PI * k * n / FftSize);
        }
        return rows;
    }

    private static double HzToMel(double f) => f < 1000 ? 3.0 * f / 200.0 : 15.0 + Math.Log(f / 1000.0) * 27.0 / Math.Log(6.4);
    private static double MelToHz(double m) => m < 15 ? 200.0 * m / 3.0 : 1000.0 * Math.Exp(Math.Log(6.4) / 27.0 * (m - 15.0));

    /// <summary>Slaney-scale triangles from 0 to 8 kHz, each normalised by its width; one row of bin weights per band.</summary>
    private static float[][] MelFilters()
    {
        double low = HzToMel(0), high = HzToMel(Rate / 2.0);
        var edges = Enumerable.Range(0, Mels + 2).Select(i => MelToHz(low + (high - low) * i / (Mels + 1))).ToArray();
        var bands = new float[Mels][];
        for (int m = 0; m < Mels; m++)
        {
            bands[m] = new float[Bins];
            double norm = 2.0 / (edges[m + 2] - edges[m]);
            for (int b = 0; b < Bins; b++)
            {
                double hz = b * (Rate / 2.0) / (Bins - 1);
                double rising = (hz - edges[m]) / (edges[m + 1] - edges[m]);
                double falling = (edges[m + 2] - hz) / (edges[m + 2] - edges[m + 1]);
                bands[m][b] = (float)(Math.Max(0, Math.Min(rising, falling)) * norm);
            }
        }
        return bands;
    }
}
