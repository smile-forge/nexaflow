namespace Nexaflow.AiHost.Speech.Spectra;

/// <summary>
/// Triangular filters on the Slaney mel scale from 0 Hz to half the sample rate, each normalised by its width — librosa's
/// <c>htk=False, norm="slaney"</c>, the bank Whisper's and NeMo's feature extractors share.
/// </summary>
internal sealed class MelBank
{
    private readonly float[][] _bands;

    /// <param name="mels">How many bands.</param>
    /// <param name="fftSize">The transform the power spectra it reads come from.</param>
    /// <param name="sampleRate">The audio's rate.</param>
    public MelBank(int mels, int fftSize, int sampleRate)
    {
        int bins = fftSize / 2 + 1;
        double low = HzToMel(0), high = HzToMel(sampleRate / 2.0);
        var edges = Enumerable.Range(0, mels + 2).Select(i => MelToHz(low + (high - low) * i / (mels + 1))).ToArray();
        _bands = new float[mels][];
        for (int m = 0; m < mels; m++)
        {
            _bands[m] = new float[bins];
            double norm = 2.0 / (edges[m + 2] - edges[m]);
            for (int b = 0; b < bins; b++)
            {
                double hz = (double)b * sampleRate / fftSize;
                double rising  = (hz - edges[m]) / (edges[m + 1] - edges[m]);
                double falling = (edges[m + 2] - hz) / (edges[m + 2] - edges[m + 1]);
                _bands[m][b] = (float)(Math.Max(0, Math.Min(rising, falling)) * norm);
            }
        }
    }

    public int Mels => _bands.Length;

    /// <summary>Writes each band's energy in <paramref name="power"/>, one frame's power spectrum, to <paramref name="energies"/>.</summary>
    public void Project(ReadOnlySpan<float> power, Span<float> energies)
    {
        for (int m = 0; m < _bands.Length; m++) energies[m] = PowerSpectrum.Dot(power, _bands[m]);
    }

    private static double HzToMel(double f) => f < 1000 ? 3.0 * f / 200.0 : 15.0 + Math.Log(f / 1000.0) * 27.0 / Math.Log(6.4);
    private static double MelToHz(double m) => m < 15 ? 200.0 * m / 3.0 : 1000.0 * Math.Exp(Math.Log(6.4) / 27.0 * (m - 15.0));
}
