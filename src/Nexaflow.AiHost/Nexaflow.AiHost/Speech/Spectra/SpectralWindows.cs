namespace Nexaflow.AiHost.Speech.Spectra;

/// <summary>The windows a spectrogram's frames are weighted by before their transform.</summary>
internal static class SpectralWindows
{
    /// <summary>A periodic Hann window, as Whisper's feature extractor makes it (torch.hann_window(n)).</summary>
    public static float[] PeriodicHann(int length)
    {
        var window = new float[length];
        for (int n = 0; n < length; n++)
        {
            double s = Math.Sin(Math.PI * n / length);
            window[n] = (float)(s * s);
        }
        return window;
    }

    /// <summary>A symmetric Hann window, as NeMo's feature extractor makes it (torch.hann_window(n, periodic=False)).</summary>
    public static float[] SymmetricHann(int length)
    {
        var window = new float[length];
        for (int n = 0; n < length; n++) window[n] = (float)(0.5 - 0.5 * Math.Cos(2 * Math.PI * n / (length - 1)));
        return window;
    }
}
