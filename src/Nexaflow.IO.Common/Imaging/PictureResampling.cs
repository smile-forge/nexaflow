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
        => ResizeAsPil(pixels, width, height, channels, newWidth, newHeight, PilFilter.Bilinear);

    /// <summary>
    /// Resizes interleaved 8-bit <paramref name="channels"/>-channel pixels as PIL's <c>Image.resize</c> does with
    /// <paramref name="filter"/>: each output pixel a weighted average of the source pixels about where its centre falls, the
    /// filter widened by the reduction so a large picture is averaged rather than sampled; the weights in PIL's 22-bit fixed
    /// point, across first then down, each pass rounded and clipped to 8 bits.
    /// </summary>
    public static byte[] ResizeAsPil(ReadOnlySpan<byte> pixels, int width, int height, int channels, int newWidth, int newHeight, PilFilter filter)
        => ResizeSeparable(pixels, width, height, channels, newWidth, newHeight, filter, sixteenBit: false);

    /// <summary>
    /// Resizes interleaved 8-bit <paramref name="channels"/>-channel pixels as PyTorch's <c>interpolate(…, antialias=True)</c>
    /// does an 8-bit picture with a bilinear or bicubic <paramref name="filter"/> — as torchvision's resize, and the image
    /// processors built on it, do: PIL's filters and arithmetic, but each pass's weights in 16 bits, as precise as its
    /// largest weight lets them be.
    /// </summary>
    public static byte[] ResizeAsTorch(ReadOnlySpan<byte> pixels, int width, int height, int channels, int newWidth, int newHeight, PilFilter filter)
    {
        if (filter is not (PilFilter.Bilinear or PilFilter.Bicubic))
            throw new ArgumentOutOfRangeException(nameof(filter), filter, "PyTorch resizes 8-bit pictures bilinearly or bicubically");
        return ResizeSeparable(pixels, width, height, channels, newWidth, newHeight, filter, sixteenBit: true);
    }

    /// <summary>A separable fixed-point resize: across first, over only the source rows the downward pass reads, then
    /// down, each pass rounded and clipped to 8 bits at its weights' precision.</summary>
    private static byte[] ResizeSeparable(ReadOnlySpan<byte> pixels, int width, int height, int channels, int newWidth, int newHeight,
                                          PilFilter filter, bool sixteenBit)
    {
        if (pixels.Length != width * height * channels) throw new ArgumentException("The pixels are not the size given.", nameof(pixels));
        if (newWidth <= 0 || newHeight <= 0) throw new ArgumentOutOfRangeException(nameof(newWidth), "A picture has a size.");
        if (newWidth == width && newHeight == height) return pixels.ToArray();

        var across = PilTaps(width, newWidth, filter, sixteenBit);
        var down = PilTaps(height, newHeight, filter, sixteenBit);
        var source = pixels.ToArray();

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
                        int sum = 1 << (across.Precision - 1);
                        for (int k = 0; k < count; k++) sum += source[row + (start + k) * channels + c] * across.Weight[taps + k];
                        rows[outRow + x * channels + c] = FixedClip(sum, across.Precision);
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
                int sum = 1 << (down.Precision - 1);
                for (int k = 0; k < count; k++) sum += rows[(start + k) * stride + i] * down.Weight[taps + k];
                result[y * stride + i] = FixedClip(sum, down.Precision);
            }
        });
        return result;
    }

    /// <summary>PIL's fixed point for 8-bit pictures: weights carry 22 fractional bits, a sum starts at a half.</summary>
    private const int PilPrecisionBits = 32 - 8 - 2;

    private static byte FixedClip(int sum, int precision) => sum <= 0 ? (byte)0 : sum >= 255 << precision ? (byte)255 : (byte)(sum >> precision);

    /// <summary>PIL's <c>precompute_coeffs</c>: for each output position, the first source position it reads, how many it
    /// reads, and their weights under <paramref name="filter"/>, normalised to 1 and then put in fixed point — PIL's 22 bits,
    /// or, <paramref name="sixteenBit"/>, as many as keep the largest weight within 16 bits, as PyTorch's 8-bit resize puts
    /// them.</summary>
    private static (int[] Start, int[] Count, int[] Weight, int Size, int Precision) PilTaps(int size, int newSize, PilFilter filter, bool sixteenBit)
    {
        double scale = (double)size / newSize;
        double filterScale = Math.Max(scale, 1.0);
        double support = PilSupport(filter) * filterScale;   // the filter's reach, widened by the reduction
        double inverse = 1.0 / filterScale;
        int taps = (int)Math.Ceiling(support) * 2 + 1;
        var start = new int[newSize];
        var count = new int[newSize];
        var normalised = new double[newSize * taps];
        double largest = 0;
        for (int d = 0; d < newSize; d++)
        {
            double centre = (d + 0.5) * scale;
            int min = Math.Max(0, (int)(centre - support + 0.5));
            int max = Math.Min(size, (int)(centre + support + 0.5)) - min;
            double total = 0;
            for (int k = 0; k < max; k++)
            {
                double w = PilKernel(filter, (k + min - centre + 0.5) * inverse);
                normalised[d * taps + k] = w;
                total += w;
            }
            for (int k = 0; k < max; k++)
            {
                if (total != 0) normalised[d * taps + k] /= total;
                largest = Math.Max(largest, normalised[d * taps + k]);
            }
            start[d] = min;
            count[d] = max;
        }

        int precision = PilPrecisionBits;
        if (sixteenBit)
            for (precision = 0; precision < PilPrecisionBits; precision++)
                if ((int)(0.5 + largest * (1 << (precision + 1))) >= 1 << 15) break;
        var weight = new int[newSize * taps];
        for (int i = 0; i < weight.Length; i++)
        {
            double v = normalised[i] * (1 << precision);
            weight[i] = v < 0 ? (int)(-0.5 + v) : (int)(0.5 + v);
        }
        return (start, count, weight, taps, precision);
    }

    /// <summary>How far each PIL filter reaches either side of a pixel, before any widening.</summary>
    private static double PilSupport(PilFilter filter) => filter switch
    {
        PilFilter.Box      => 0.5,
        PilFilter.Bilinear => 1.0,
        PilFilter.Bicubic  => 2.0,
        PilFilter.Lanczos  => 3.0,
        _ => throw new ArgumentOutOfRangeException(nameof(filter), filter, "PIL has no such filter"),
    };

    /// <summary>PIL's filters at <paramref name="x"/> source pixels from where an output pixel's centre falls.</summary>
    private static double PilKernel(PilFilter filter, double x)
    {
        switch (filter)
        {
            case PilFilter.Box:
                return x > -0.5 && x <= 0.5 ? 1.0 : 0.0;
            case PilFilter.Bilinear:
                x = Math.Abs(x);
                return x < 1.0 ? 1.0 - x : 0.0;
            case PilFilter.Bicubic:
                const double a = -0.5;
                x = Math.Abs(x);
                if (x < 1.0) return ((a + 2.0) * x - (a + 3.0)) * x * x + 1;
                if (x < 2.0) return (((x - 5) * x + 8) * x - 4) * a;
                return 0.0;
            case PilFilter.Lanczos:
                return x >= -3.0 && x < 3.0 ? Sinc(x) * Sinc(x / 3) : 0.0;
            default:
                throw new ArgumentOutOfRangeException(nameof(filter), filter, "PIL has no such filter");
        }

        static double Sinc(double x)
        {
            if (x == 0.0) return 1.0;
            x *= Math.PI;
            return Math.Sin(x) / x;
        }
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

    /// <summary>
    /// Resizes interleaved <paramref name="channels"/>-channel values in double precision as OpenCV's <c>INTER_CUBIC</c> does a
    /// <c>CV_64F</c> picture: each output value from the four source values about where its centre falls — the source position
    /// <c>(d + 0.5) × scale − 0.5</c> in single precision — weighed by the cubic convolution with A = −0.75 in single
    /// precision, across then down, past the edge the edge value repeated. Nothing is averaged when it shrinks.
    /// </summary>
    public static double[] ResizeCubicAsOpenCv(ReadOnlySpan<double> pixels, int width, int height, int channels, int newWidth, int newHeight)
    {
        if (pixels.Length != width * height * channels) throw new ArgumentException("The pixels are not the size given.", nameof(pixels));
        if (newWidth <= 0 || newHeight <= 0) throw new ArgumentOutOfRangeException(nameof(newWidth), "A picture has a size.");
        if (newWidth == width && newHeight == height) return pixels.ToArray();

        var (xIndex, xWeight) = CubicTaps(width, newWidth);
        var (yIndex, yWeight) = CubicTaps(height, newHeight);
        var source = pixels.ToArray();

        // Across first, a source row at a time; then down.
        var across = new double[height * newWidth * channels];
        Parallel.For(0, height, y =>
        {
            int row = y * width * channels, outRow = y * newWidth * channels;
            for (int x = 0; x < newWidth; x++)
                for (int c = 0; c < channels; c++)
                {
                    double sum = 0;
                    for (int k = 0; k < 4; k++) sum += source[row + xIndex[x * 4 + k] * channels + c] * xWeight[x * 4 + k];
                    across[outRow + x * channels + c] = sum;
                }
        });
        var result = new double[newWidth * newHeight * channels];
        int stride = newWidth * channels;
        Parallel.For(0, newHeight, y =>
        {
            int r0 = yIndex[y * 4] * stride, r1 = yIndex[y * 4 + 1] * stride, r2 = yIndex[y * 4 + 2] * stride, r3 = yIndex[y * 4 + 3] * stride;
            double b0 = yWeight[y * 4], b1 = yWeight[y * 4 + 1], b2 = yWeight[y * 4 + 2], b3 = yWeight[y * 4 + 3];
            for (int i = 0; i < stride; i++)
                result[y * stride + i] = b0 * across[r0 + i] + b1 * across[r1 + i] + b2 * across[r2 + i] + b3 * across[r3 + i];
        });
        return result;
    }

    /// <summary>OpenCV's cubic taps for each output position: the four source positions, the edge repeated, and their single
    /// precision weights — <c>interpolateCubic</c>, its last weight what the first three leave of 1.</summary>
    private static (int[] Index, float[] Weight) CubicTaps(int size, int newSize)
    {
        var index = new int[newSize * 4];
        var weight = new float[newSize * 4];
        double scale = 1.0 / ((double)newSize / size);
        const float A = -0.75f;
        for (int d = 0; d < newSize; d++)
        {
            float f = (float)((d + 0.5) * scale - 0.5);
            int s = (int)MathF.Floor(f);
            f -= s;
            float w0 = ((A * (f + 1) - 5 * A) * (f + 1) + 8 * A) * (f + 1) - 4 * A;
            float w1 = ((A + 2) * f - (A + 3)) * f * f + 1;
            float w2 = ((A + 2) * (1 - f) - (A + 3)) * (1 - f) * (1 - f) + 1;
            weight[d * 4] = w0;
            weight[d * 4 + 1] = w1;
            weight[d * 4 + 2] = w2;
            weight[d * 4 + 3] = 1f - w0 - w1 - w2;
            for (int k = 0; k < 4; k++) index[d * 4 + k] = Math.Clamp(s - 1 + k, 0, size - 1);
        }
        return (index, weight);
    }

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
