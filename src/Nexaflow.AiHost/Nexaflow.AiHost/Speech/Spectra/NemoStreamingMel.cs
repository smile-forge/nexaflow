namespace Nexaflow.AiHost.Speech.Spectra;

/// <summary>
/// NeMo's log-mel features for a cache-aware streaming encoder, made a chunk at a time as onnxruntime-extensions'
/// NemoStreamingMelExtractor makes them: pre-emphasis carried from chunk to chunk; each chunk left-padded with the last half
/// transform of the one before (zeros before the first) and right-padded to centre its frames; symmetric-Hann frames
/// zero-padded to the transform; Slaney mel power, and log(x + guard). One per stream: it holds the stream's state.
/// </summary>
internal sealed class NemoStreamingMel
{
    private readonly PowerSpectrum _spectrum;
    private readonly MelBank _bank;
    private readonly int _fftSize, _hop;
    private readonly float _preemphasis, _logGuard;
    private readonly float[] _overlap;
    private float _lastSample;

    /// <param name="logGuard">What is added to each band's energy before its log.</param>
    public NemoStreamingMel(int mels, int fftSize, int hop, int windowLength, int sampleRate, float preemphasis, float logGuard)
    {
        _spectrum    = new PowerSpectrum(fftSize, SpectralWindows.SymmetricHann(windowLength));
        _bank        = new MelBank(mels, fftSize, sampleRate);
        _fftSize     = fftSize;
        _hop         = hop;
        _preemphasis = preemphasis;
        _logGuard    = logGuard;
        _overlap     = new float[fftSize / 2];
    }

    public int Mels => _bank.Mels;

    /// <summary>The next chunk's features.</summary>
    /// <returns>Its frames, time-major — [frames, <see cref="Mels"/>] — and how many there are.</returns>
    public (float[] Features, int Frames) Process(ReadOnlySpan<float> chunk)
    {
        int pad = _overlap.Length;
        int windowOffset = (_fftSize - _spectrum.FrameLength) / 2;

        var emphasised = new float[chunk.Length];
        float previous = _lastSample;
        for (int i = 0; i < chunk.Length; i++)
        {
            emphasised[i] = chunk[i] - _preemphasis * previous;
            previous = chunk[i];
        }
        if (chunk.Length > 0) _lastSample = chunk[^1];

        int length = Math.Max(pad + chunk.Length + windowOffset, windowOffset + _spectrum.FrameLength);
        var padded = new float[length];
        _overlap.CopyTo(padded, 0);
        emphasised.CopyTo(padded, pad);

        // The next chunk's left pad: this one's last half transform, or what of the last pad it does not replace.
        if (chunk.Length >= pad) emphasised.AsSpan(chunk.Length - pad).CopyTo(_overlap);
        else
        {
            _overlap.AsSpan(chunk.Length).CopyTo(_overlap);
            emphasised.CopyTo(_overlap.AsSpan(pad - chunk.Length));
        }

        int frames = (length - windowOffset - _spectrum.FrameLength) / _hop + 1;
        int mels = _bank.Mels;
        var features = new float[frames * mels];
        var power = new float[_spectrum.Bins];
        for (int t = 0; t < frames; t++)
        {
            _spectrum.Compute(padded.AsSpan(t * _hop + windowOffset, _spectrum.FrameLength), power);
            var row = features.AsSpan(t * mels, mels);
            _bank.Project(power, row);
            for (int m = 0; m < mels; m++) row[m] = MathF.Log(row[m] + _logGuard);
        }
        return (features, frames);
    }
}
