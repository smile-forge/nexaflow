namespace Nexaflow.IO.Pe.Internal;

/// <summary>
/// One streaming pass that produces both the whole-file entropy and the fixed-width sweep the
/// heatmap draws. Reading in 1 MB chunks keeps a 300 MB image off the managed heap entirely.
/// </summary>
internal static class EntropyCalculator
{
    private const int ChunkSize = 1 << 20;

    public static PeEntropy Compute(PeBuffer buffer, int bucketCount)
    {
        long length = buffer.Length;
        if (length <= 0 || bucketCount <= 0) return PeEntropy.Empty;

        // Never slice finer than a byte; a tiny file gets fewer, larger buckets.
        int  buckets     = (int)Math.Min(bucketCount, length);
        long bucketBytes = (length + buckets - 1) / buckets;

        var global  = new long[256];
        var values  = new double[buckets];
        var local   = new long[256];

        for (int b = 0; b < buckets; b++)
        {
            long start = b * bucketBytes;
            long size  = Math.Min(bucketBytes, length - start);
            if (size <= 0) break;

            Array.Clear(local);
            for (long done = 0; done < size; )
            {
                int chunk = (int)Math.Min(ChunkSize, size - done);
                var span  = buffer.Slice(start + done, chunk);
                if (span.IsEmpty) break;

                foreach (byte v in span)
                {
                    local[v]++;
                    global[v]++;
                }
                done += chunk;
            }
            values[b] = PeEntropy.FromHistogram(local, size);
        }

        return new PeEntropy(PeEntropy.FromHistogram(global, length), values, bucketBytes);
    }
}
