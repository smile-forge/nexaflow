using System.Runtime.InteropServices;
using SkiaSharp;

namespace Nexaflow.Features.Images.Editing.Rendering;

/// <summary>
/// Where a frame clips, pixel by pixel, row after row: <see cref="Shadow"/> where a channel is at or below black,
/// <see cref="Highlight"/> where one is at or beyond white — each only for an end that was asked to be marked.
/// </summary>
public sealed class ClippedPixels
{
    public const byte Shadow = 1;
    public const byte Highlight = 2;

    internal ClippedPixels(int width, int height)
    {
        Width = width;
        Height = height;
        Marks = new byte[width * height];
    }

    public int Width { get; }

    public int Height { get; }

    /// <summary>The ends marked; the other's mark is never set.</summary>
    public ClipEnds Ends { get; internal set; }

    /// <summary>One byte a pixel, <see cref="Width"/> to a row.</summary>
    public byte[] Marks { get; }
}

/// <summary>What one read of a frame found: its tones and, where asked, where it clips.</summary>
public sealed record ToneReading(ToneHistogram Histogram, ClippedPixels? Clipped);

/// <summary>
/// Reads the tones of a rendered frame: the picture as the render pipeline made it, before it is narrowed to the screen's
/// 8-bit sRGB, so a 16- or 32-bit picture is read at its own depth and in its own gamut. A colour outside sRGB is not taken for
/// clipping, nor a tone a hair from black or white that 8 bits would round onto the end; a tone at or beyond white does clip,
/// as the picture now stands, however much of it exposure could bring back. The rows are counted in bands side by side.
/// </summary>
/// <remarks>
/// An 8-bit frame is counted from its own pixels. A deeper one, or one on a linear curve, is read once as half floats in its
/// own gamut on the sRGB curve, so its levels fall where a person reads tones. One reader serves one stream of frames, one at a
/// time: the <see cref="ClippedPixels"/> it hands out are reused by its next read.
/// </remarks>
public sealed class ToneReader
{
    /// <summary>A band is at least this many rows, so a small frame is not split for nothing.</summary>
    private const int BandRows = 32;

    /// <summary>How near black or white a deep value must be to count as there: half a 16-bit step.</summary>
    private const float DeepEnd = 0.5f / 65535;

    private ClippedPixels? _clipped;

    /// <summary>Reads <paramref name="picture"/>'s tones, marking where it clips at the <paramref name="marked"/> ends.</summary>
    /// <exception cref="InvalidOperationException">A deep picture could not be read back.</exception>
    public ToneReading Read(SKImage picture, ClipEnds marked)
    {
        if (ReadsDirectly(picture) && picture.PeekPixels() is { } direct)
        {
            using (direct) return Count(direct, marked);
        }
        using var deep = ReadDeep(picture);
        using var pixels = deep.PeekPixels();
        return Count(pixels, marked);
    }

    /// <summary>An 8-bit raster frame on a perceptual curve: counted from its own pixels.</summary>
    private static bool ReadsDirectly(SKImage picture)
        => picture.ColorType is SKColorType.Bgra8888 or SKColorType.Rgba8888 && picture.ColorSpace?.GammaIsLinear != true;

    /// <summary>The picture as half floats, unpremultiplied and unclamped, in its own gamut on the sRGB curve.</summary>
    private static SKBitmap ReadDeep(SKImage picture)
    {
        using var space = picture.ColorSpace is { } own && own.ToColorSpaceXyz(out var gamut)
            ? SKColorSpace.CreateRgb(SKColorSpaceTransferFn.Srgb, gamut)
            : SKColorSpace.CreateSrgb();
        var bitmap = new SKBitmap(new SKImageInfo(picture.Width, picture.Height, SKColorType.RgbaF16, SKAlphaType.Unpremul, space));
        if (picture.ReadPixels(bitmap.Info, bitmap.GetPixels(), bitmap.RowBytes, 0, 0)) return bitmap;
        bitmap.Dispose();
        throw new InvalidOperationException("The rendered picture could not be read back for its tones.");
    }

    private ToneReading Count(SKPixmap pixels, ClipEnds marked)
    {
        int width = pixels.Width, height = pixels.Height, rowBytes = pixels.RowBytes;
        var clipped = marked == ClipEnds.None ? null : ClippedFor(width, height, marked);
        bool deep = pixels.ColorType == SKColorType.RgbaF16;
        bool bgra = pixels.ColorType == SKColorType.Bgra8888;
        bool premultiplied = pixels.AlphaType == SKAlphaType.Premul;

        int bands = Math.Clamp(height / BandRows, 1, Environment.ProcessorCount);
        var tallies = new Tally[bands];
        Parallel.For(0, bands, band =>
        {
            int top = (int)((long)height * band / bands), bottom = (int)((long)height * (band + 1) / bands);
            var tally = new Tally();
            var all = pixels.GetPixelSpan();
            for (int y = top; y < bottom; y++)
            {
                var row = all.Slice(y * rowBytes, width * pixels.BytesPerPixel);
                var marks = clipped is null ? Span<byte>.Empty : clipped.Marks.AsSpan(y * width, width);
                if (deep) tally.CountDeep(MemoryMarshal.Cast<byte, Half>(row), marks, marked);
                else tally.CountBytes(row, bgra, premultiplied, marks, marked);
            }
            tallies[band] = tally;
        });
        return new ToneReading(Tally.Merge(tallies), clipped);
    }

    private ClippedPixels ClippedFor(int width, int height, ClipEnds marked)
    {
        if (_clipped is not { } kept || kept.Width != width || kept.Height != height) _clipped = kept = new ClippedPixels(width, height);
        kept.Ends = marked;
        return kept;
    }

    /// <summary>One band's counts.</summary>
    private sealed class Tally
    {
        private readonly int[] _levels = new int[4 * ToneHistogram.Levels];
        private readonly long[] _atBlack = new long[3];
        private readonly long[] _atWhite = new long[3];
        private long _pixels, _shadowPixels, _highlightPixels;

        public void CountBytes(ReadOnlySpan<byte> row, bool bgra, bool premultiplied, Span<byte> marks, ClipEnds marked)
        {
            for (int x = 0, at = 0; at < row.Length; x++, at += 4)
            {
                int a = row[at + 3];
                if (a == 0)
                {
                    if (!marks.IsEmpty) marks[x] = 0;
                    continue;
                }
                int r = row[bgra ? at + 2 : at], g = row[at + 1], b = row[bgra ? at : at + 2];
                if (premultiplied && a < 255)
                {
                    r = Math.Min(255, (r * 255 + a / 2) / a);
                    g = Math.Min(255, (g * 255 + a / 2) / a);
                    b = Math.Min(255, (b * 255 + a / 2) / a);
                }
                int luminance = (54 * r + 183 * g + 19 * b + 128) >> 8;
                Take(r, g, b, luminance, r == 0, g == 0, b == 0, r == 255, g == 255, b == 255, marks, x, marked);
            }
        }

        public void CountDeep(ReadOnlySpan<Half> row, Span<byte> marks, ClipEnds marked)
        {
            for (int x = 0, at = 0; at < row.Length; x++, at += 4)
            {
                float a = (float)row[at + 3];
                if (a <= 0)
                {
                    if (!marks.IsEmpty) marks[x] = 0;
                    continue;
                }
                float r = (float)row[at], g = (float)row[at + 1], b = (float)row[at + 2];
                float luminance = 0.2126f * r + 0.7152f * g + 0.0722f * b;
                Take(Level(r), Level(g), Level(b), Level(luminance),
                     r <= DeepEnd, g <= DeepEnd, b <= DeepEnd, r >= 1 - DeepEnd, g >= 1 - DeepEnd, b >= 1 - DeepEnd, marks, x, marked);
            }
        }

        private static int Level(float value) => float.IsNaN(value) ? 0 : (int)Math.Clamp(value * 255 + 0.5f, 0, 255);

        private void Take(int r, int g, int b, int luminance, bool redBlack, bool greenBlack, bool blueBlack,
                          bool redWhite, bool greenWhite, bool blueWhite, Span<byte> marks, int x, ClipEnds marked)
        {
            _pixels++;
            _levels[r]++;
            _levels[ToneHistogram.Levels + g]++;
            _levels[2 * ToneHistogram.Levels + b]++;
            _levels[3 * ToneHistogram.Levels + luminance]++;
            if (redBlack) _atBlack[0]++;
            if (greenBlack) _atBlack[1]++;
            if (blueBlack) _atBlack[2]++;
            if (redWhite) _atWhite[0]++;
            if (greenWhite) _atWhite[1]++;
            if (blueWhite) _atWhite[2]++;
            bool shadow = redBlack || greenBlack || blueBlack, highlight = redWhite || greenWhite || blueWhite;
            if (shadow) _shadowPixels++;
            if (highlight) _highlightPixels++;
            if (marks.IsEmpty) return;
            byte mark = 0;
            if (shadow && marked.HasFlag(ClipEnds.Shadows)) mark |= ClippedPixels.Shadow;
            if (highlight && marked.HasFlag(ClipEnds.Highlights)) mark |= ClippedPixels.Highlight;
            marks[x] = mark;
        }

        public static ToneHistogram Merge(Tally[] tallies)
        {
            var levels = new int[4 * ToneHistogram.Levels];
            var atBlack = new long[3];
            var atWhite = new long[3];
            long pixels = 0, shadowPixels = 0, highlightPixels = 0;
            foreach (var tally in tallies)
            {
                for (int i = 0; i < levels.Length; i++) levels[i] += tally._levels[i];
                for (int c = 0; c < 3; c++)
                {
                    atBlack[c] += tally._atBlack[c];
                    atWhite[c] += tally._atWhite[c];
                }
                pixels += tally._pixels;
                shadowPixels += tally._shadowPixels;
                highlightPixels += tally._highlightPixels;
            }
            return new ToneHistogram(levels, pixels, atBlack, atWhite, shadowPixels, highlightPixels);
        }
    }
}
