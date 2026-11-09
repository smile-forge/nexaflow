namespace Nexaflow.AiHost.Speech.Spectra;

/// <summary>
/// The log-mel spectrogram a Whisper encoder reads, as the feature extractor its export is published with makes it
/// (onnxruntime-extensions' STFTNorm and LogMelSpectrum): 25 ms periodic-Hann frames every 10 ms over the audio zero-padded
/// by half a frame either side, its last frame dropped; Slaney mel power, log10 floored at 1e-10, clamped to 8 below its
/// peak and scaled to (x + 4) / 4. Frames past the audio, up to the encoder's 30 s window, hold the clamp's floor.
/// </summary>
internal sealed class WhisperLogMel
{
    public const int SampleRate = 16_000;
    public const int FftSize    = 400;
    public const int Hop        = 160;

    /// <summary>The frames the encoder reads: its 30 s window.</summary>
    public const int Frames = 30 * SampleRate / Hop;

    private static readonly PowerSpectrum Spectrum = new(FftSize, SpectralWindows.PeriodicHann(FftSize));

    private readonly MelBank _bank;

    public WhisperLogMel(int mels) => _bank = new MelBank(mels, FftSize, SampleRate);

    public int Mels => _bank.Mels;

    /// <summary>The features of <paramref name="audio"/>, 16 kHz and at most 30 s.</summary>
    /// <returns>[<see cref="Mels"/>, <see cref="Frames"/>], row-major.</returns>
    public float[] Compute(ReadOnlySpan<float> audio)
    {
        const int pad = FftSize / 2;
        int length = Math.Max(audio.Length, FftSize);   // at least one whole frame, as the extractor requires
        var padded = new float[length + 2 * pad];
        audio.CopyTo(padded.AsSpan(pad));

        // The transform's frames, less the last, which the extractor drops.
        int frames = Math.Min((length + Hop) / Hop - 1, Frames);
        int mels = _bank.Mels;
        var features = new float[mels * Frames];
        var power = new float[Spectrum.Bins];
        var energies = new float[mels];
        float peak = float.NegativeInfinity;
        for (int t = 0; t < frames; t++)
        {
            Spectrum.Compute(padded.AsSpan(t * Hop, FftSize), power);
            _bank.Project(power, energies);
            for (int m = 0; m < mels; m++)
            {
                float v = MathF.Log10(MathF.Max(energies[m], 1e-10f));
                features[m * Frames + t] = v;
                if (v > peak) peak = v;
            }
        }

        float floor = peak - 8f;
        float padding = (floor + 4f) / 4f;
        for (int m = 0; m < mels; m++)
        {
            var row = features.AsSpan(m * Frames, Frames);
            for (int t = 0; t < frames; t++) row[t] = (MathF.Max(row[t], floor) + 4f) / 4f;
            row[frames..].Fill(padding);
        }
        return features;
    }
}
