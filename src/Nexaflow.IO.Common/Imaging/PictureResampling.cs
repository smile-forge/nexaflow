namespace Nexaflow.IO.Common.Imaging;

/// <summary>
/// Resizing a plane of floats as PyTorch does with its corners aligned — as a depth estimate made at the size its network
/// reads is brought back to the photo it was made from.
/// </summary>
public static class PictureResampling
{
    /// <summary>
    /// Resizes a plane of floats as PyTorch's <c>interpolate(mode="bilinear", align_corners=True)</c> does: the corners of the
    /// two planes meet, each output value from the four source values about <c>d × (size − 1) / (newSize − 1)</c>, in single
    /// precision.
    /// </summary>
    public static float[] ResizePlaneBilinearAligned(ReadOnlySpan<float> plane, int width, int height, int newWidth, int newHeight)
    {
        if (plane.Length != width * height) throw new ArgumentException("The plane is not the size given.", nameof(plane));
        if (newWidth <= 0 || newHeight <= 0) throw new ArgumentOutOfRangeException(nameof(newWidth), "A plane has a size.");
        if (newWidth == width && newHeight == height) return plane.ToArray();

        var across = AlignedTaps(width, newWidth);
        var down = AlignedTaps(height, newHeight);
        var source = plane.ToArray();
        var result = new float[newWidth * newHeight];
        Parallel.For(0, newHeight, y =>
        {
            var (y0, y1, h0, h1) = down[y];
            int row0 = y0 * width, row1 = y1 * width, outRow = y * newWidth;
            for (int x = 0; x < newWidth; x++)
            {
                var (x0, x1, w0, w1) = across[x];
                result[outRow + x] = h0 * (w0 * source[row0 + x0] + w1 * source[row0 + x1]) + h1 * (w0 * source[row1 + x0] + w1 * source[row1 + x1]);
            }
        });
        return result;
    }

    /// <summary>PyTorch's source index for each output position with the corners aligned — <c>(size − 1) / (newSize − 1) × d</c>
    /// in single precision, 0 for a single output — as the two neighbours it falls between and their weights.</summary>
    private static (int Low, int High, float LowWeight, float HighWeight)[] AlignedTaps(int size, int newSize)
    {
        var taps = new (int, int, float, float)[newSize];
        float scale = newSize > 1 ? (float)(size - 1) / (newSize - 1) : 0f;
        for (int d = 0; d < newSize; d++)
        {
            float s = scale * d;
            int low = (int)s;
            int high = low < size - 1 ? low + 1 : low;
            float lambda = s - low;
            taps[d] = (low, high, 1f - lambda, lambda);
        }
        return taps;
    }
}
