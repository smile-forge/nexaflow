namespace Nexaflow.IO.Common.Imaging;

/// <summary>
/// Resizing, warping and blurring pictures held as interleaved 8-bit channels or planes of floats, the way OpenCV does
/// them — so a model ported from a Python pipeline built on OpenCV sees the pixels it was made with: Lanczos resizing over
/// eight taps with the edge repeated, bilinear warps at each pixel's exact source position, and Gaussian blurs with the
/// edge mirrored about its last pixel.
/// </summary>
public static class PictureResampling
{
    /// <summary>
    /// Resizes interleaved 8-bit <paramref name="channels"/>-channel pixels as OpenCV's <c>INTER_LANCZOS4</c> does: each
    /// output pixel from the eight source pixels about where its centre falls, weighed by a Lanczos window of 4, rows then
    /// columns, past the edge the edge pixel repeated.
    /// </summary>
    public static byte[] ResizeLanczos(ReadOnlySpan<byte> pixels, int width, int height, int channels, int newWidth, int newHeight)
    {
        if (pixels.Length != width * height * channels) throw new ArgumentException("The pixels are not the size given.", nameof(pixels));
        if (newWidth <= 0 || newHeight <= 0) throw new ArgumentOutOfRangeException(nameof(newWidth), "A picture has a size.");
        if (newWidth == width && newHeight == height) return pixels.ToArray();

        var (xIndex, xWeight) = LanczosTaps(width, newWidth);
        var (yIndex, yWeight) = LanczosTaps(height, newHeight);
        var source = pixels.ToArray();

        // Across first, into floats, a source row at a time; then down.
        var across = new float[height * newWidth * channels];
        Parallel.For(0, height, y =>
        {
            int row = y * width * channels, outRow = y * newWidth * channels;
            for (int x = 0; x < newWidth; x++)
                for (int c = 0; c < channels; c++)
                {
                    float sum = 0;
                    for (int k = 0; k < 8; k++) sum += xWeight[x * 8 + k] * source[row + xIndex[x * 8 + k] * channels + c];
                    across[outRow + x * channels + c] = sum;
                }
        });
        var result = new byte[newWidth * newHeight * channels];
        int stride = newWidth * channels;
        Parallel.For(0, newHeight, y =>
        {
            for (int i = 0; i < stride; i++)
            {
                float sum = 0;
                for (int k = 0; k < 8; k++) sum += yWeight[y * 8 + k] * across[yIndex[y * 8 + k] * stride + i];
                result[y * stride + i] = Saturate(sum);
            }
        });
        return result;
    }

    /// <summary>For each output position, the eight source positions and their weights.</summary>
    private static (int[] Index, float[] Weight) LanczosTaps(int size, int newSize)
    {
        var index = new int[newSize * 8];
        var weight = new float[newSize * 8];
        double scale = (double)size / newSize;
        Span<float> taps = stackalloc float[8];
        for (int d = 0; d < newSize; d++)
        {
            double f = (d + 0.5) * scale - 0.5;
            int s = (int)Math.Floor(f);
            LanczosWeights((float)(f - s), taps);
            for (int k = 0; k < 8; k++)
            {
                index[d * 8 + k] = Math.Clamp(s - 3 + k, 0, size - 1);
                weight[d * 8 + k] = taps[k];
            }
        }
        return (index, weight);
    }

    /// <summary>OpenCV's <c>interpolateLanczos4</c>: the eight weights for a point <paramref name="x"/> past the fourth tap.</summary>
    private static void LanczosWeights(float x, Span<float> weights)
    {
        if (x < float.Epsilon)
        {
            weights.Clear();
            weights[3] = 1;
            return;
        }
        const double s45 = 0.70710678118654752440084436210485;
        ReadOnlySpan<double> cs = [1, 0, -s45, -s45, 0, 1, s45, -s45, -1, 0, s45, s45, 0, -1, -s45, s45];
        double y0 = -(x + 3) * Math.PI * 0.25, s0 = Math.Sin(y0), c0 = Math.Cos(y0);
        float sum = 0;
        for (int i = 0; i < 8; i++)
        {
            double y = -(x + 3 - i) * Math.PI * 0.25;
            weights[i] = (float)((cs[i * 2] * s0 + cs[i * 2 + 1] * c0) / (y * y));
            sum += weights[i];
        }
        for (int i = 0; i < 8; i++) weights[i] /= sum;
    }

    /// <summary>
    /// Warps interleaved 8-bit pixels as OpenCV's bilinear <c>warpAffine</c>, giving the destination's pixels within
    /// <paramref name="region"/> alone, row by row: each read from where <paramref name="map"/>'s inverse takes it in the
    /// source, a source pixel past the edge being <paramref name="border"/>. Coordinates are worked in the whole
    /// destination's frame, so a region comes out exactly as that part of a whole warp would.
    /// </summary>
    /// <param name="map">Source to destination, as OpenCV's matrix.</param>
    public static byte[] WarpAffine(ReadOnlySpan<byte> pixels, int width, int height, int channels, Affine2D map,
                                    (int X, int Y, int Width, int Height) region, ReadOnlySpan<byte> border)
    {
        if (pixels.Length != width * height * channels) throw new ArgumentException("The pixels are not the size given.", nameof(pixels));
        var inverse = map.Inverse();
        var source = pixels.ToArray();
        var cval = border.ToArray();
        var target = new byte[region.Width * region.Height * channels];
        Parallel.For(0, region.Height, ry =>
        {
            Span<float> px = stackalloc float[channels];
            for (int rx = 0; rx < region.Width; rx++)
            {
                var (sx, sy, fx, fy) = Source(inverse, region.X + rx, region.Y + ry);
                Bilinear(source, width, height, channels, sx, sy, fx, fy, cval, px);
                for (int c = 0; c < channels; c++) target[(ry * region.Width + rx) * channels + c] = Saturate(px[c]);
            }
        });
        return target;
    }

    /// <summary>As the 8-bit warp, for a plane of floats, past the edge 0.</summary>
    public static float[] WarpAffine(ReadOnlySpan<float> plane, int width, int height, Affine2D map, (int X, int Y, int Width, int Height) region)
    {
        if (plane.Length != width * height) throw new ArgumentException("The plane is not the size given.", nameof(plane));
        var inverse = map.Inverse();
        var source = plane.ToArray();
        var target = new float[region.Width * region.Height];
        Parallel.For(0, region.Height, ry =>
        {
            for (int rx = 0; rx < region.Width; rx++)
            {
                var (sx, sy, fx, fy) = Source(inverse, region.X + rx, region.Y + ry);
                target[ry * region.Width + rx] = Bilinear(source, width, height, sx, sy, fx, fy);
            }
        });
        return target;
    }

    /// <summary>
    /// Where in the source a destination pixel reads: the source pixel at or before it each way, and how far past it — as
    /// OpenCV 5 computes it, exactly, where OpenCV 4 kept it to 32nds of a pixel.
    /// </summary>
    private static (int X, int Y, float FracX, float FracY) Source(Affine2D inverse, int x, int y)
    {
        var (sx, sy) = inverse.Map(x, y);
        double fx = Math.Floor(sx), fy = Math.Floor(sy);
        return ((int)fx, (int)fy, (float)(sx - fx), (float)(sy - fy));
    }

    /// <summary>The bilinear mix of the four source pixels about a position, each past the edge being the
    /// border; a pixel all four of whose taps are past the edge is the border outright, as OpenCV's constant border makes it.</summary>
    private static void Bilinear(byte[] source, int width, int height, int channels, int sx, int sy, float fracX, float fracY,
                                 ReadOnlySpan<byte> border, Span<float> into)
    {
        if (sx >= width || sx + 1 < 0 || sy >= height || sy + 1 < 0)
        {
            for (int c = 0; c < channels; c++) into[c] = border[c];
            return;
        }
        var (w00, w01, w10, w11) = Weights(fracX, fracY);
        bool x0 = (uint)sx < (uint)width, x1 = (uint)(sx + 1) < (uint)width, y0 = (uint)sy < (uint)height, y1 = (uint)(sy + 1) < (uint)height;
        int r0 = (sy * width + sx) * channels, r1 = r0 + width * channels;
        for (int c = 0; c < channels; c++)
        {
            float v00 = x0 && y0 ? source[r0 + c] : border[c];
            float v01 = x1 && y0 ? source[r0 + channels + c] : border[c];
            float v10 = x0 && y1 ? source[r1 + c] : border[c];
            float v11 = x1 && y1 ? source[r1 + channels + c] : border[c];
            into[c] = v00 * w00 + v01 * w01 + v10 * w10 + v11 * w11;
        }
    }

    private static float Bilinear(float[] source, int width, int height, int sx, int sy, float fracX, float fracY)
    {
        if (sx >= width || sx + 1 < 0 || sy >= height || sy + 1 < 0) return 0;
        var (w00, w01, w10, w11) = Weights(fracX, fracY);
        bool x0 = (uint)sx < (uint)width, x1 = (uint)(sx + 1) < (uint)width, y0 = (uint)sy < (uint)height, y1 = (uint)(sy + 1) < (uint)height;
        int r0 = sy * width + sx, r1 = r0 + width;
        return (x0 && y0 ? source[r0] * w00 : 0) + (x1 && y0 ? source[r0 + 1] * w01 : 0)
             + (x0 && y1 ? source[r1] * w10 : 0) + (x1 && y1 ? source[r1 + 1] * w11 : 0);
    }

    private static (float W00, float W01, float W10, float W11) Weights(float ax, float ay)
        => ((1 - ax) * (1 - ay), ax * (1 - ay), (1 - ax) * ay, ax * ay);

    /// <summary>
    /// Blurs a plane in place as OpenCV's <c>GaussianBlur</c> with a square kernel of <paramref name="size"/> and
    /// <paramref name="sigma"/>: across, then down, past the edge the picture mirrored about its last pixel.
    /// </summary>
    public static void GaussianBlur(Span<float> plane, int width, int height, int size, double sigma)
    {
        if (size % 2 == 0 || size < 1) throw new ArgumentOutOfRangeException(nameof(size), "A Gaussian kernel has an odd size.");
        int half = size / 2;
        var kernel = new double[size];
        double total = 0;
        for (int i = 0; i < size; i++) total += kernel[i] = Math.Exp(-(i - half) * (i - half) / (2 * sigma * sigma));
        for (int i = 0; i < size; i++) kernel[i] /= total;

        var data = plane.ToArray();
        var across = new float[data.Length];
        Parallel.For(0, height, y =>
        {
            for (int x = 0; x < width; x++)
            {
                double sum = 0;
                for (int k = 0; k < size; k++) sum += kernel[k] * data[y * width + Reflect101(x + k - half, width)];
                across[y * width + x] = (float)sum;
            }
        });
        Parallel.For(0, height, y =>
        {
            for (int x = 0; x < width; x++)
            {
                double sum = 0;
                for (int k = 0; k < size; k++) sum += kernel[k] * across[Reflect101(y + k - half, height) * width + x];
                data[y * width + x] = (float)sum;
            }
        });
        data.CopyTo(plane);
    }

    /// <summary>OpenCV's <c>BORDER_REFLECT_101</c>: past an edge, the picture mirrored about its last pixel.</summary>
    private static int Reflect101(int i, int size)
    {
        if (size == 1) return 0;
        while ((uint)i >= (uint)size) i = i < 0 ? -i : 2 * size - 2 - i;
        return i;
    }

    private static byte Saturate(float v) => v <= 0 ? (byte)0 : v >= 255 ? (byte)255 : (byte)MathF.Round(v, MidpointRounding.ToEven);

    /// <summary>
    /// Resizes interleaved 8-bit <paramref name="channels"/>-channel pixels as PIL's <c>Image.resize(…, BILINEAR)</c> does:
    /// each output pixel a triangle-weighted average of the source pixels about where its centre falls, the triangle widened
    /// by the reduction so a large picture is averaged rather than sampled; the weights in PIL's 22-bit fixed point, across
    /// first then down, each pass rounded to 8 bits.
    /// </summary>
    public static byte[] ResizeBilinearAsPil(ReadOnlySpan<byte> pixels, int width, int height, int channels, int newWidth, int newHeight)
    {
        if (pixels.Length != width * height * channels) throw new ArgumentException("The pixels are not the size given.", nameof(pixels));
        if (newWidth <= 0 || newHeight <= 0) throw new ArgumentOutOfRangeException(nameof(newWidth), "A picture has a size.");
        if (newWidth == width && newHeight == height) return pixels.ToArray();

        var across = PilTaps(width, newWidth);
        var down = PilTaps(height, newHeight);
        var source = pixels.ToArray();

        // Across, over only the source rows the downward pass reads.
        int firstRow = down.Start[0], lastRow = down.Start[newHeight - 1] + down.Count[newHeight - 1];
        byte[] rows = source;
        if (newWidth != width)
        {
            int rowCount = lastRow - firstRow;
            rows = new byte[rowCount * newWidth * channels];
            Parallel.For(0, rowCount, y =>
            {
                int row = (y + firstRow) * width * channels, outRow = y * newWidth * channels;
                for (int x = 0; x < newWidth; x++)
                {
                    int start = across.Start[x], count = across.Count[x], taps = x * across.Size;
                    for (int c = 0; c < channels; c++)
                    {
                        int sum = PilHalf;
                        for (int k = 0; k < count; k++) sum += source[row + (start + k) * channels + c] * across.Weight[taps + k];
                        rows[outRow + x * channels + c] = PilClip(sum);
                    }
                }
            });
        }
        else firstRow = 0;
        if (newHeight == height) return rows;

        int stride = newWidth * channels;
        var result = new byte[newHeight * stride];
        Parallel.For(0, newHeight, y =>
        {
            int start = down.Start[y] - firstRow, count = down.Count[y], taps = y * down.Size;
            for (int i = 0; i < stride; i++)
            {
                int sum = PilHalf;
                for (int k = 0; k < count; k++) sum += rows[(start + k) * stride + i] * down.Weight[taps + k];
                result[y * stride + i] = PilClip(sum);
            }
        });
        return result;
    }

    /// <summary>PIL's fixed point for 8-bit pictures: weights carry 22 fractional bits, a sum starts at a half.</summary>
    private const int PilPrecisionBits = 32 - 8 - 2;

    private const int PilHalf = 1 << (PilPrecisionBits - 1);

    private static byte PilClip(int sum) => sum <= 0 ? (byte)0 : sum >= 255 << PilPrecisionBits ? (byte)255 : (byte)(sum >> PilPrecisionBits);

    /// <summary>PIL's <c>precompute_coeffs</c> for its bilinear filter: for each output position, the first source position
    /// it reads, how many it reads, and their weights, normalised to 1 and then put in fixed point.</summary>
    private static (int[] Start, int[] Count, int[] Weight, int Size) PilTaps(int size, int newSize)
    {
        double scale = (double)size / newSize;
        double filterScale = Math.Max(scale, 1.0);
        double support = filterScale;   // the bilinear filter's support is 1, widened by the reduction
        int taps = (int)Math.Ceiling(support) * 2 + 1;
        var start = new int[newSize];
        var count = new int[newSize];
        var weight = new int[newSize * taps];
        var w = new double[taps];
        for (int d = 0; d < newSize; d++)
        {
            double centre = (d + 0.5) * scale;
            int min = Math.Max(0, (int)(centre - support + 0.5));
            int max = Math.Min(size, (int)(centre + support + 0.5)) - min;
            double total = 0;
            for (int k = 0; k < max; k++)
            {
                double x = Math.Abs((k + min - centre + 0.5) / filterScale);
                w[k] = x < 1 ? 1 - x : 0;
                total += w[k];
            }
            for (int k = 0; k < max; k++)
            {
                double normalised = total != 0 ? w[k] / total : w[k];
                weight[d * taps + k] = normalised < 0 ? (int)(-0.5 + normalised * (1 << PilPrecisionBits))
                                                      : (int)(0.5 + normalised * (1 << PilPrecisionBits));
            }
            start[d] = min;
            count[d] = max;
        }
        return (start, count, weight, taps);
    }

    /// <summary>
    /// Resizes a plane of floats as PyTorch's <c>interpolate(mode="bilinear", align_corners=False)</c> does, up or down
    /// alike: each output value from the four source values about where its centre falls, nothing averaged when it shrinks.
    /// </summary>
    public static float[] ResizePlaneBilinear(ReadOnlySpan<float> plane, int width, int height, int newWidth, int newHeight)
    {
        if (plane.Length != width * height) throw new ArgumentException("The plane is not the size given.", nameof(plane));
        if (newWidth <= 0 || newHeight <= 0) throw new ArgumentOutOfRangeException(nameof(newWidth), "A plane has a size.");
        if (newWidth == width && newHeight == height) return plane.ToArray();

        var across = TorchTaps(width, newWidth);
        var down = TorchTaps(height, newHeight);
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

    /// <summary>PyTorch's source index for each output position — <c>scale × (d + 0.5) − 0.5</c>, not below 0, in single
    /// precision — as the two neighbours it falls between and their weights.</summary>
    private static (int Low, int High, float LowWeight, float HighWeight)[] TorchTaps(int size, int newSize)
    {
        var taps = new (int, int, float, float)[newSize];
        float scale = (float)size / newSize;
        for (int d = 0; d < newSize; d++)
        {
            float s = Math.Max(scale * (d + 0.5f) - 0.5f, 0f);
            int low = (int)s;
            int high = low < size - 1 ? low + 1 : low;
            float lambda = Math.Clamp(s - low, 0f, 1f);
            taps[d] = (low, high, 1f - lambda, lambda);
        }
        return taps;
    }
}
