namespace Nexaflow.IO.Pe;

/// <summary>
/// Shannon entropy across the image, in bits per byte (0 = perfectly uniform, 8 = indistinguishable
/// from random). <see cref="Buckets"/> is a fixed-width sweep suitable for driving a heatmap strip;
/// per-section values live on <see cref="PeSection.Entropy"/>.
/// </summary>
/// <param name="Overall">Entropy of the whole file.</param>
/// <param name="Buckets">One value per equal-sized slice of the file, left to right.</param>
/// <param name="BucketBytes">How many bytes each bucket covers (the last may cover fewer).</param>
public sealed record PeEntropy(double Overall, IReadOnlyList<double> Buckets, long BucketBytes)
{
    public static readonly PeEntropy Empty = new(0, [], 0);

    /// <summary>Above this, a run of bytes is compressed, encrypted or already-random data.</summary>
    public const double PackedThreshold = 7.0;

    /// <summary>Computes Shannon entropy of a byte range, 0–8. An empty range is 0.</summary>
    public static double Shannon(ReadOnlySpan<byte> data)
    {
        if (data.IsEmpty) return 0;

        Span<int> counts = stackalloc int[256];
        foreach (byte b in data) counts[b]++;

        double entropy = 0, length = data.Length;
        foreach (int c in counts)
        {
            if (c == 0) continue;
            double p = c / length;
            entropy -= p * Math.Log2(p);
        }
        return entropy;
    }

    /// <summary>Folds a running 256-bin histogram into an entropy value — lets a caller accumulate
    /// over many chunks without holding the whole file.</summary>
    public static double FromHistogram(ReadOnlySpan<long> counts, long total)
    {
        if (total <= 0) return 0;

        double entropy = 0;
        foreach (long c in counts)
        {
            if (c == 0) continue;
            double p = (double)c / total;
            entropy -= p * Math.Log2(p);
        }
        return entropy;
    }
}
