using System.Numerics;

namespace Nexaflow.AiHost.Speech.Spectra;

/// <summary>
/// The power of each frequency bin of one windowed frame, |X[k]|² for k from 0 to half the transform's size, the frame
/// zero-padded to that size. Speech frames are a few hundred samples and seldom a power of two (Whisper's are 400), so the
/// transform is a direct one: a dot product per bin against rows holding the window and the basis together, vectorised.
/// </summary>
internal sealed class PowerSpectrum
{
    private readonly float[][] _cos;
    private readonly float[][] _sin;

    /// <param name="fftSize">The transform's size; a frame shorter than it is zero-padded to it.</param>
    /// <param name="window">The weights of a frame's samples: its length is the frame's.</param>
    public PowerSpectrum(int fftSize, float[] window)
    {
        if (window.Length > fftSize) throw new ArgumentException("A frame is no longer than its transform.", nameof(window));
        Bins        = fftSize / 2 + 1;
        FrameLength = window.Length;
        _cos = new float[Bins][];
        _sin = new float[Bins][];
        for (int k = 0; k < Bins; k++)
        {
            _cos[k] = new float[FrameLength];
            _sin[k] = new float[FrameLength];
            for (int n = 0; n < FrameLength; n++)
            {
                // k·n taken modulo the size first, so the angle stays small and exact.
                double angle = 2 * Math.PI * ((long)k * n % fftSize) / fftSize;
                _cos[k][n] = (float)(window[n] * Math.Cos(angle));
                _sin[k][n] = (float)(window[n] * Math.Sin(angle));
            }
        }
    }

    /// <summary>How many bins a frame has: half the transform's size, and one.</summary>
    public int Bins { get; }

    /// <summary>How many samples a frame is.</summary>
    public int FrameLength { get; }

    /// <summary>Writes the power of each of <paramref name="frame"/>'s bins to <paramref name="power"/>.</summary>
    public void Compute(ReadOnlySpan<float> frame, Span<float> power)
    {
        frame = frame[..FrameLength];
        for (int k = 0; k < Bins; k++)
        {
            float re = Dot(frame, _cos[k]), im = Dot(frame, _sin[k]);
            power[k] = re * re + im * im;
        }
    }

    internal static float Dot(ReadOnlySpan<float> a, ReadOnlySpan<float> b)
    {
        int width = Vector<float>.Count, i = 0;
        var sum = Vector<float>.Zero;
        for (; i <= a.Length - width; i += width) sum += new Vector<float>(a[i..]) * new Vector<float>(b[i..]);
        float total = Vector.Sum(sum);
        for (; i < a.Length; i++) total += a[i] * b[i];
        return total;
    }
}
