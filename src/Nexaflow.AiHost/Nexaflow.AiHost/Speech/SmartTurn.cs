using System.Numerics;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using Nexaflow.AiHost.Contracts;
using Nexaflow.AiHost.Models;
using Nexaflow.AiHost.Speech.Spectra;

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

    public IHostModel Load(ModelSpec spec) => new SmartTurn(spec, CpuSessions.Open(spec, threads: 4));
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

    private static readonly PowerSpectrum Spectrum = new(FftSize, SpectralWindows.PeriodicHann(FftSize));
    private static readonly MelBank Bank = new(Mels, FftSize, Rate);

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
        var power = new float[Spectrum.Bins];
        var energies = new float[Mels];
        float max = float.NegativeInfinity;
        for (int t = 0; t < Frames; t++)   // the extractor computes one more frame and drops it
        {
            Spectrum.Compute(padded.AsSpan(t * Hop, FftSize), power);
            Bank.Project(power, energies);
            for (int m = 0; m < Mels; m++)
            {
                float v = MathF.Log10(MathF.Max(energies[m], 1e-10f));
                logSpec[m * Frames + t] = v;
                if (v > max) max = v;
            }
        }
        for (int i = 0; i < logSpec.Length; i++) logSpec[i] = (MathF.Max(logSpec[i], max - 8f) + 4f) / 4f;
        return logSpec;
    }
}
